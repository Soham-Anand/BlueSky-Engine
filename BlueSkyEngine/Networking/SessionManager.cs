using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSky.Networking;

/// <summary>
/// Represents a multiplayer session that can be hosted, searched, and joined.
/// </summary>
public sealed class SessionInfo
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string SessionName { get; set; } = "BlueSky Session";
    public string HostName { get; set; } = "";
    public string HostUserId { get; set; } = "";
    public int MaxPlayers { get; set; } = 8;
    public int CurrentPlayers { get; set; } = 1;
    public string ScenePath { get; set; } = "";
    public bool IsLAN { get; set; } = true;
    public string[] Tags { get; set; } = Array.Empty<string>();

    [JsonIgnore] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public bool IsAlive => (DateTime.UtcNow - CreatedAt).TotalSeconds < 30; // expires after 30s without refresh

    public string ToJson() => JsonSerializer.Serialize(this);
    public static SessionInfo? FromJson(string json) => JsonSerializer.Deserialize<SessionInfo>(json);
}

/// <summary>
/// Manages multiplayer session lifecycle: host, search, join.
/// Uses EOS P2P for transport and a local registry for discovery.
/// </summary>
public sealed class SessionManager : IDisposable
{
    private readonly IMultiplayerService? _service;
    private readonly Action<string>? _log;

    // ── Session State ──────────────────────────────────────────────
    private SessionInfo? _hostedSession;
    private bool _isHosting;
    private bool _isJoining;
    private string _lastJoinedSessionId = "";

    // ── Search Results ─────────────────────────────────────────────
    private readonly List<SessionInfo> _discoveredSessions = new();
    private readonly object _sessionLock = new();
    private CancellationTokenSource? _searchCts = null;

    // ── UDP Session Discovery Variables ────────────────────────────
    private System.Net.Sockets.UdpClient? _udpAdvertiser;
    private System.Net.Sockets.UdpClient? _udpListener;
    private System.Threading.Tasks.Task? _advertiseTask;
    private System.Threading.Tasks.Task? _listenTask;
    private System.Threading.CancellationTokenSource? _udpCts;
    private readonly Dictionary<string, (SessionInfo Session, DateTime LastSeen)> _discoveredSessionsMap = new();
    private const int DiscoveryPort = 48800;

    // ── Events ─────────────────────────────────────────────────────
    public event Action<SessionInfo>? OnSessionHosted;
    public event Action<SessionInfo>? OnSessionJoined;
    public event Action<string>? OnSessionJoinFailed;
    public event Action<IReadOnlyList<SessionInfo>>? OnSearchCompleted;
    public event Action<SessionInfo>? OnPeerSessionDiscovered;

    // ── Public State ───────────────────────────────────────────────
    public bool IsHosting => _isHosting;
    public bool IsJoining => _isJoining;
    public SessionInfo? HostedSession => _hostedSession;
    public IMultiplayerService? Service => _service;
    public IReadOnlyList<SessionInfo> DiscoveredSessions
    {
        get
        {
            lock (_sessionLock)
                return _discoveredSessions.Where(s => s.IsAlive).ToList();
        }
    }

    public SessionManager(IMultiplayerService? service, Action<string>? log = null)
    {
        _service = service;
        _log = log;

        // Start UDP session listener
        try
        {
            _udpCts = new System.Threading.CancellationTokenSource();
            _udpListener = new System.Net.Sockets.UdpClient();
            _udpListener.Client.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.ReuseAddress, true);
            _udpListener.Client.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, DiscoveryPort));
            
            _listenTask = ListenForSessionsAsync(_udpCts.Token);
            _log?.Invoke($"[Session] UDP Discovery listener started on port {DiscoveryPort}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Session] Failed to start UDP discovery listener: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task ListenForSessionsAsync(System.Threading.CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await _udpListener!.ReceiveAsync(token);
                string json = System.Text.Encoding.UTF8.GetString(result.Buffer);
                var session = SessionInfo.FromJson(json);
                if (session != null)
                {
                    lock (_sessionLock)
                    {
                        _discoveredSessionsMap[session.SessionId] = (session, DateTime.UtcNow);
                    }
                    RegisterDiscoveredSession(session);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) break;
                _log?.Invoke($"[Session] UDP Listen error: {ex.Message}");
                await System.Threading.Tasks.Task.Delay(1000, token);
            }
        }
    }

    private async System.Threading.Tasks.Task AdvertiseSessionAsync(System.Threading.CancellationToken token)
    {
        var broadcastEndpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, DiscoveryPort);
        while (!token.IsCancellationRequested)
        {
            try
            {
                SessionInfo? session = null;
                lock (_sessionLock)
                {
                    session = _hostedSession;
                }

                if (session != null && _isHosting)
                {
                    string json = session.ToJson();
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    await _udpAdvertiser!.SendAsync(bytes, bytes.Length, broadcastEndpoint);
                    await _udpAdvertiser.SendAsync(bytes, bytes.Length,
                        new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, DiscoveryPort));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) break;
                _log?.Invoke($"[Session] UDP Advertise error: {ex.Message}");
            }

            await System.Threading.Tasks.Task.Delay(1000, token);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  HOST
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Create and host a new session.
    /// </summary>
    public SessionInfo? Host(string sessionName, string hostName, string scenePath, int maxPlayers = 8)
    {
        if (_isHosting)
        {
            _log?.Invoke("[Session] Already hosting a session!");
            return _hostedSession;
        }

        _hostedSession = new SessionInfo
        {
            SessionName = sessionName,
            HostName = hostName,
            HostUserId = GetLocalUserId(),
            MaxPlayers = maxPlayers,
            CurrentPlayers = 1,
            ScenePath = scenePath,
            IsLAN = true,
        };

        _isHosting = true;
        _isJoining = false;

        // Start advertising
        try
        {
            _udpAdvertiser = new System.Net.Sockets.UdpClient();
            _udpAdvertiser.Client.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.ReuseAddress, true);
            _udpAdvertiser.Client.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.Broadcast, true);
            
            _advertiseTask = AdvertiseSessionAsync(_udpCts!.Token);
            _log?.Invoke("[Session] UDP session advertiser started");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Session] Failed to start UDP session advertiser: {ex.Message}");
        }

        _log?.Invoke($"[Session] Hosted session '{sessionName}' (ID: {_hostedSession.SessionId})");
        OnSessionHosted?.Invoke(_hostedSession);

        return _hostedSession;
    }

    // ═══════════════════════════════════════════════════════════════
    //  SEARCH
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Search for available sessions. Polls the local registry periodically.
    /// </summary>
    public void Search(string query = "", int maxResults = 20)
    {
        _log?.Invoke($"[Session] Searching for sessions... (query: '{query}')");

        lock (_sessionLock)
        {
            _discoveredSessions.Clear();

            // Include local sessions received via UDP broadcast in the last 5 seconds
            var now = DateTime.UtcNow;
            foreach (var kvp in _discoveredSessionsMap.ToList())
            {
                if ((now - kvp.Value.LastSeen).TotalSeconds < 5.0)
                {
                    _discoveredSessions.Add(kvp.Value.Session);
                }
            }

            // Also include own hosted session
            if (_hostedSession != null && _isHosting)
            {
                if (!_discoveredSessions.Any(s => s.SessionId == _hostedSession.SessionId))
                {
                    _discoveredSessions.Add(_hostedSession);
                }
            }
        }

        var results = DiscoveredSessions;
        _log?.Invoke($"[Session] Search complete: {results.Count} session(s) found");
        OnSearchCompleted?.Invoke(results);
    }

    /// <summary>
    /// Register a discovered session (called by P2P broadcast or lobby discovery).
    /// </summary>
    public void RegisterDiscoveredSession(SessionInfo session)
    {
        lock (_sessionLock)
        {
            var existing = _discoveredSessions.FirstOrDefault(s => s.SessionId == session.SessionId);
            if (existing != null)
            {
                // Update existing session
                existing.CurrentPlayers = session.CurrentPlayers;
                existing.CreatedAt = DateTime.UtcNow; // refresh TTL
            }
            else
            {
                _discoveredSessions.Add(session);
            }

            // Also store in map so Search() picks it up (Search clears _discoveredSessions
            // and rebuilds from _discoveredSessionsMap within 5s TTL)
            _discoveredSessionsMap[session.SessionId] = (session, DateTime.UtcNow);
        }

        OnPeerSessionDiscovered?.Invoke(session);
    }

    /// <summary>
    /// Get the count of discovered sessions.
    /// </summary>
    public int GetSessionCount()
    {
        lock (_sessionLock)
            return _discoveredSessions.Count(s => s.IsAlive);
    }

    /// <summary>
    /// Get session name at a specific index.
    /// </summary>
    public string GetSessionName(int index)
    {
        lock (_sessionLock)
        {
            var alive = _discoveredSessions.Where(s => s.IsAlive).ToList();
            if (index >= 0 && index < alive.Count)
                return alive[index].SessionName;
        }
        return "";
    }

    /// <summary>
    /// Get session ID at a specific index.
    /// </summary>
    public string GetSessionId(int index)
    {
        lock (_sessionLock)
        {
            var alive = _discoveredSessions.Where(s => s.IsAlive).ToList();
            if (index >= 0 && index < alive.Count)
                return alive[index].SessionId;
        }
        return "";
    }

    /// <summary>
    /// Get the host name of a session at a specific index.
    /// </summary>
    public string GetSessionHost(int index)
    {
        lock (_sessionLock)
        {
            var alive = _discoveredSessions.Where(s => s.IsAlive).ToList();
            if (index >= 0 && index < alive.Count)
                return alive[index].HostName;
        }
        return "";
    }

    /// <summary>
    /// Get player count of a session at a specific index.
    /// </summary>
    public int GetSessionPlayerCount(int index)
    {
        lock (_sessionLock)
        {
            var alive = _discoveredSessions.Where(s => s.IsAlive).ToList();
            if (index >= 0 && index < alive.Count)
                return alive[index].CurrentPlayers;
        }
        return 0;
    }

    /// <summary>
    /// Get max players of a session at a specific index.
    /// </summary>
    public int GetSessionMaxPlayers(int index)
    {
        lock (_sessionLock)
        {
            var alive = _discoveredSessions.Where(s => s.IsAlive).ToList();
            if (index >= 0 && index < alive.Count)
                return alive[index].MaxPlayers;
        }
        return 0;
    }

    // ═══════════════════════════════════════════════════════════════
    //  JOIN
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Join a session by its ID.
    /// </summary>
    public bool Join(string sessionId)
    {
        if (_isHosting)
        {
            _log?.Invoke("[Session] Cannot join while hosting. Stop hosting first.");
            OnSessionJoinFailed?.Invoke("Cannot join while hosting");
            return false;
        }

        SessionInfo? target = null;
        lock (_sessionLock)
        {
            target = _discoveredSessions.FirstOrDefault(s => s.SessionId == sessionId && s.IsAlive);
        }

        if (target == null)
        {
            _log?.Invoke($"[Session] Session '{sessionId}' not found or expired");
            OnSessionJoinFailed?.Invoke($"Session '{sessionId}' not found");
            return false;
        }

        _isJoining = true;
        _lastJoinedSessionId = sessionId;
        target.CurrentPlayers++;

        _log?.Invoke($"[Session] Joining session '{target.SessionName}' (Host: {target.HostName})");
        OnSessionJoined?.Invoke(target);

        return true;
    }

    /// <summary>
    /// Join a session by index in the search results.
    /// </summary>
    public bool JoinByIndex(int index)
    {
        lock (_sessionLock)
        {
            var alive = _discoveredSessions.Where(s => s.IsAlive).ToList();
            if (index >= 0 && index < alive.Count)
                return Join(alive[index].SessionId);
        }

        _log?.Invoke($"[Session] Invalid session index: {index}");
        OnSessionJoinFailed?.Invoke($"Invalid index: {index}");
        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    //  LEAVE
    // ═══════════════════════════════════════════════════════════════

    public void Leave()
    {
        if (_isHosting)
        {
            _log?.Invoke("[Session] Stopped hosting");
            _isHosting = false;
            _hostedSession = null;

            if (_advertiseTask != null)
            {
                try
                {
                    _udpAdvertiser?.Close();
                    _udpAdvertiser = null;
                    _advertiseTask = null;
                }
                catch {}
            }
        }

        if (_isJoining)
        {
            _log?.Invoke($"[Session] Left session '{_lastJoinedSessionId}'");
            _isJoining = false;
            _lastJoinedSessionId = "";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private string GetLocalUserId()
    {
        if (_service != null && !string.IsNullOrEmpty(_service.LocalP2PId))
            return _service.LocalP2PId;
        return Environment.UserName;
    }

    public void Dispose()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();

        _udpCts?.Cancel();
        try
        {
            _udpListener?.Close();
            _udpAdvertiser?.Close();
        }
        catch {}
        _udpCts?.Dispose();

        Leave();
    }
}