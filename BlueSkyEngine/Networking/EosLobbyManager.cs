using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSky.Networking;

/// <summary>
/// Represents a player in the lobby.
/// </summary>
public sealed class LobbyPlayer
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsReady { get; set; } = true;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Manages EOS multiplayer lobby: player list, countdown, and server travel.
/// Uses EOS P2P for transport and the SessionManager for session registry.
/// </summary>
public sealed class EosLobbyManager : IDisposable
{
    private readonly SessionManager _sessionManager;
    private readonly Action<string>? _log;

    // ── Lobby State ──────────────────────────────────────────────
    private bool _isInLobby;
    private bool _isHost;
    private string _lobbyName = "";
    private string _scenePath = "";
    private int _maxPlayers = 8;

    // ── Player List ──────────────────────────────────────────────
    private readonly List<LobbyPlayer> _players = new();
    private readonly object _playersLock = new();

    // ── Countdown ────────────────────────────────────────────────
    private bool _countdownActive;
    private float _countdownSeconds;
    private float _countdownTotal = 10f;
    private bool _travelTriggered;

    // ── Events ───────────────────────────────────────────────────
    public event Action<IReadOnlyList<LobbyPlayer>>? OnPlayersUpdated;
    public event Action<float>? OnCountdownTick;
    public event Action? OnServerTravel;
    public event Action<string>? OnLobbyError;

    // ── Public State ─────────────────────────────────────────────
    public bool IsInLobby => _isInLobby;
    public bool IsHost => _isHost;
    public string LobbyName => _lobbyName;
    public string ScenePath => _scenePath;
    public float CountdownSeconds => _countdownSeconds;
    public bool IsCountdownActive => _countdownActive;
    public bool TravelTriggered => _travelTriggered;

    public IReadOnlyList<LobbyPlayer> Players
    {
        get
        {
            lock (_playersLock)
                return _players.ToList();
        }
    }

    public int PlayerCount
    {
        get
        {
            lock (_playersLock)
                return _players.Count;
        }
    }

    private float _lastBroadcastTime = 0f;

    // ── Client Join Retry ────────────────────────────────────────
    private float _joinRetryTimer = 0f;
    private const float JoinRetryInterval = 2f; // retry every 2 seconds
    private string _pendingJoinHostP2PId = "";
    private string _pendingJoinPlayerName = "";
    private string _pendingJoinUserId = "";

    // ── UDP Lobby State Sync ─────────────────────────────────────
    private System.Net.Sockets.UdpClient? _udpLobbySender;
    private System.Net.Sockets.UdpClient? _udpLobbyListener;
    private System.Threading.Tasks.Task? _udpLobbyListenTask;
    private System.Threading.CancellationTokenSource? _udpLobbyCts;
    private const int LobbyStatePort = 48801; // different from session discovery port

    public EosLobbyManager(SessionManager sessionManager, Action<string>? log = null)
    {
        _sessionManager = sessionManager;
        _log = log;

        var service = _sessionManager.Service;
        if (service != null)
        {
            service.OnPacketReceived += HandlePacketReceived;
            _log?.Invoke("[Lobby] Subscribed to IMultiplayerService OnPacketReceived");
        }

        // Start UDP lobby state listener
        StartUdpLobbyListener();
    }

    // ═══════════════════════════════════════════════════════════════
    //  UDP LOBBY STATE SYNC
    // ═══════════════════════════════════════════════════════════════

    private void StartUdpLobbyListener()
    {
        try
        {
            _udpLobbyCts = new System.Threading.CancellationTokenSource();
            _udpLobbyListener = new System.Net.Sockets.UdpClient();
            _udpLobbyListener.Client.SetSocketOption(
                System.Net.Sockets.SocketOptionLevel.Socket,
                System.Net.Sockets.SocketOptionName.ReuseAddress, true);
            _udpLobbyListener.Client.Bind(new System.Net.IPEndPoint(
                System.Net.IPAddress.Any, LobbyStatePort));

            _udpLobbyListenTask = ListenForLobbyStateAsync(_udpLobbyCts.Token);
            _log?.Invoke($"[Lobby] UDP lobby state listener started on port {LobbyStatePort}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Lobby] Failed to start UDP lobby listener: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task ListenForLobbyStateAsync(
        System.Threading.CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await _udpLobbyListener!.ReceiveAsync(token);
                string json = System.Text.Encoding.UTF8.GetString(result.Buffer);

                // Try parsing as LobbyStateUdp
                var state = JsonSerializer.Deserialize<LobbyStateUdp>(json);
                if (state != null)
                {
                    if (state.Type == "lobby_state" && !_isHost)
                    {
                        // Host broadcast received — update our player list
                        ApplyLobbyStateFromHost(state);
                    }
                    else if (state.Type == "join_request" && _isHost)
                    {
                        // Client wants to join
                        HandleUdpJoinRequest(state);
                    }
                    else if (state.Type == "server_travel" && !_isHost)
                    {
                        // Host says it's time to load the game scene
                        Console.WriteLine($"[Lobby] UDP ServerTravel received — scene: {state.ScenePath}");
                        _log?.Invoke($"[Lobby] UDP ServerTravel received — scene: {state.ScenePath}");
                        _scenePath = state.ScenePath;
                        TriggerTravel();
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) break;
                _log?.Invoke($"[Lobby] UDP lobby listen error: {ex.Message}");
                await System.Threading.Tasks.Task.Delay(500, token);
            }
        }
    }

    private void ApplyLobbyStateFromHost(LobbyStateUdp state)
    {
        lock (_playersLock)
        {
            _players.Clear();
            foreach (var p in state.Players)
            {
                _players.Add(new LobbyPlayer
                {
                    UserId = p.UserId,
                    DisplayName = p.DisplayName,
                    IsReady = true
                });
            }
        }

        _countdownActive = state.CountdownActive;
        _countdownSeconds = state.CountdownSeconds;
        _countdownTotal = state.CountdownTotal;
        _scenePath = state.ScenePath;

        // Clear retry state — we got a response from host
        _joinRetryTimer = 0f;
        _pendingJoinHostP2PId = "";

        // Bridge: register host as a discoverable session so search() finds it
        // (EosLobbyManager already broadcasts to Broadcast+Loopback on port 48801,
        //  but SessionManager only sends to Broadcast on port 48800 — this ensures
        //  same-machine clients see the lobby in the available list.)
        var hostSession = new SessionInfo
        {
            SessionId = "lobby_" + state.LobbyName.GetHashCode().ToString("X8"),
            SessionName = state.LobbyName,
            HostName = state.SenderName,
            HostUserId = state.SenderId,
            MaxPlayers = state.MaxPlayers,
            CurrentPlayers = state.Players.Count,
            ScenePath = state.ScenePath,
            IsLAN = true,
        };
        _sessionManager.RegisterDiscoveredSession(hostSession);

        // Update multiplayer service with current peer list
        UpdateMultiplayerLobbyPeers();

        OnPlayersUpdated?.Invoke(Players);
    }

    private void HandleUdpJoinRequest(LobbyStateUdp state)
    {
        _log?.Invoke($"[Lobby] UDP JoinRequest from '{state.SenderName}'");
        AddPlayer(state.SenderId, state.SenderName);
    }

    private async void BroadcastLobbyStateUdp()
    {
        if (!_isHost) return;

        try
        {
            if (_udpLobbySender == null)
            {
                _udpLobbySender = new System.Net.Sockets.UdpClient();
                _udpLobbySender.Client.SetSocketOption(
                    System.Net.Sockets.SocketOptionLevel.Socket,
                    System.Net.Sockets.SocketOptionName.ReuseAddress, true);
                _udpLobbySender.Client.SetSocketOption(
                    System.Net.Sockets.SocketOptionLevel.Socket,
                    System.Net.Sockets.SocketOptionName.Broadcast, true);
            }

            var state = new LobbyStateUdp
            {
                Type = "lobby_state",
                SenderId = GetLocalUserId(),
                SenderName = GetLocalDisplayName(),
                LobbyName = _lobbyName,
                ScenePath = _scenePath,
                MaxPlayers = _maxPlayers,
                CountdownActive = _countdownActive,
                CountdownSeconds = _countdownSeconds,
                CountdownTotal = _countdownTotal,
                Players = Players.Select(p => new LobbyPlayerPayload
                {
                    UserId = p.UserId,
                    DisplayName = p.DisplayName,
                    IsReady = p.IsReady
                }).ToList()
            };

            string json = JsonSerializer.Serialize(state);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            // Send to broadcast (other machines) AND localhost (same machine)
            await _udpLobbySender.SendAsync(bytes, bytes.Length,
                new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, LobbyStatePort));
            await _udpLobbySender.SendAsync(bytes, bytes.Length,
                new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, LobbyStatePort));
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Lobby] UDP lobby broadcast error: {ex.Message}");
        }
    }

    private async void SendJoinRequestUdp(string playerName, string userId)
    {
        try
        {
            if (_udpLobbySender == null)
            {
                _udpLobbySender = new System.Net.Sockets.UdpClient();
                _udpLobbySender.Client.SetSocketOption(
                    System.Net.Sockets.SocketOptionLevel.Socket,
                    System.Net.Sockets.SocketOptionName.ReuseAddress, true);
                _udpLobbySender.Client.SetSocketOption(
                    System.Net.Sockets.SocketOptionLevel.Socket,
                    System.Net.Sockets.SocketOptionName.Broadcast, true);
            }

            var state = new LobbyStateUdp
            {
                Type = "join_request",
                SenderId = userId,
                SenderName = playerName,
                LobbyName = _lobbyName,
                ScenePath = _scenePath,
            };

            string json = JsonSerializer.Serialize(state);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            // Send to broadcast (other machines) AND localhost (same machine)
            await _udpLobbySender.SendAsync(bytes, bytes.Length,
                new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, LobbyStatePort));
            await _udpLobbySender.SendAsync(bytes, bytes.Length,
                new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, LobbyStatePort));
            _log?.Invoke($"[Lobby] UDP JoinRequest broadcast sent");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Lobby] UDP join request error: {ex.Message}");
        }
    }

    private void HandlePacketReceived(string peerId, byte[] data, byte channel)
    {
        // Skip handshake bytes used for P2P connection establishment
        if (data.Length == 1 && data[0] == 0x01)
            return;

        // Only handle lobby channel (0); replication uses channel 1
        if (channel != 0) return;

        try
        {
            string json = System.Text.Encoding.UTF8.GetString(data);
            var message = JsonSerializer.Deserialize<LobbyMessage>(json);
            if (message == null) return;

            Console.WriteLine($"[Lobby] P2P packet received: Type={message.Type}, from={peerId}, SenderId={message.SenderId}");
            switch (message.Type)
            {
                case LobbyMessageType.JoinRequest:
                    HandleJoinRequest(peerId, message.SenderName);
                    break;
                case LobbyMessageType.LobbyUpdate:
                    HandleLobbyUpdate(message.Data);
                    break;
                case LobbyMessageType.ServerTravel:
                    HandleServerTravel();
                    break;
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Lobby] Error handling P2P packet: {ex.Message}");
        }
    }

    private void HandleJoinRequest(string clientId, string clientName)
    {
        if (!_isHost) return;

        _log?.Invoke($"[Lobby] Received JoinRequest from '{clientName}' ({clientId})");
        AddPlayer(clientId, clientName);
    }

    private void HandleLobbyUpdate(string payloadJson)
    {
        if (_isHost) return;

        var payload = JsonSerializer.Deserialize<LobbyUpdatePayload>(payloadJson);
        if (payload == null) return;

        lock (_playersLock)
        {
            _players.Clear();
            foreach (var p in payload.Players)
            {
                _players.Add(new LobbyPlayer
                {
                    UserId = p.UserId,
                    DisplayName = p.DisplayName,
                    IsReady = p.IsReady
                });
            }
        }

        _countdownActive = payload.CountdownActive;
        _countdownSeconds = payload.CountdownSeconds;
        _countdownTotal = payload.CountdownTotal;
        _scenePath = payload.ScenePath;

        // Update multiplayer service with current peer list
        UpdateMultiplayerLobbyPeers();

        // We got a lobby update — clear the retry state (we're connected)
        _joinRetryTimer = 0f;
        _pendingJoinHostP2PId = "";

        OnPlayersUpdated?.Invoke(Players);
        if (_countdownActive)
        {
            OnCountdownTick?.Invoke(_countdownSeconds);
        }
    }

    private void HandleServerTravel()
    {
        Console.WriteLine($"[Lobby] HandleServerTravel called (isHost={_isHost}, travelTriggered={_travelTriggered}, scenePath='{_scenePath}')");
        if (_isHost) return;

        _log?.Invoke("[Lobby] Received ServerTravel from host. Triggering travel...");
        TriggerTravel();
    }

    private void SendJoinRequest(string hostP2PId, string playerName, string userId)
    {
        var msg = new LobbyMessage
        {
            Type = LobbyMessageType.JoinRequest,
            SenderId = userId,
            SenderName = playerName,
            Data = ""
        };

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg));
        bool success = _sessionManager.Service?.SendPacket(hostP2PId, bytes) ?? false;
        _log?.Invoke(success
            ? $"[Lobby] JoinRequest sent to host '{hostP2PId}'"
            : $"[Lobby] Failed to send JoinRequest to host '{hostP2PId}' (P2P may not be connected yet)");
    }

    private void BroadcastLobbyUpdate()
    {
        if (!_isHost) return;

        var payload = new LobbyUpdatePayload
        {
            Players = Players.Select(p => new LobbyPlayerPayload { UserId = p.UserId, DisplayName = p.DisplayName, IsReady = p.IsReady }).ToList(),
            CountdownActive = _countdownActive,
            CountdownSeconds = _countdownSeconds,
            CountdownTotal = _countdownTotal,
            ScenePath = _scenePath
        };

        var msg = new LobbyMessage
        {
            Type = LobbyMessageType.LobbyUpdate,
            SenderId = GetLocalUserId(),
            SenderName = GetLocalDisplayName(),
            Data = JsonSerializer.Serialize(payload)
        };

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg));

        lock (_playersLock)
        {
            foreach (var player in _players)
            {
                if (player.UserId != GetLocalUserId())
                {
                    _sessionManager.Service?.SendPacket(player.UserId, bytes);
                }
            }
        }
    }

    private void BroadcastServerTravel()
    {
        var msg = new LobbyMessage
        {
            Type = LobbyMessageType.ServerTravel,
            SenderId = GetLocalUserId(),
            SenderName = GetLocalDisplayName(),
            Data = ""
        };

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg));

        int sentCount = 0;
        lock (_playersLock)
        {
            foreach (var player in _players)
            {
                if (player.UserId != GetLocalUserId())
                {
                    bool sent = _sessionManager.Service?.SendPacket(player.UserId, bytes) ?? false;
                    Console.WriteLine($"[Lobby] BroadcastServerTravel → '{player.UserId}' (sent={sent})");
                    if (sent) sentCount++;
                }
            }
        }
        Console.WriteLine($"[Lobby] BroadcastServerTravel done: sent to {sentCount} players (total players: {_players.Count})");
    }

    // ═══════════════════════════════════════════════════════════════
    //  UPDATE (called every frame)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Must be called every frame to tick countdown and broadcast lobby state.
    /// </summary>
    public void Update(float deltaTime)
    {
        // Host: periodically broadcast lobby state to all connected players
        if (_isInLobby && _isHost)
        {
            _lastBroadcastTime += deltaTime;
            if (_lastBroadcastTime >= 1.0f)
            {
                _lastBroadcastTime = 0f;
                BroadcastLobbyUpdate();      // P2P broadcast
                BroadcastLobbyStateUdp();    // UDP broadcast (fallback)
            }
        }

        // Client: periodically retry JoinRequest if we haven't received a LobbyUpdate yet
        if (_isInLobby && !_isHost && !string.IsNullOrEmpty(_pendingJoinHostP2PId))
        {
            _joinRetryTimer += deltaTime;
            if (_joinRetryTimer >= JoinRetryInterval)
            {
                _joinRetryTimer = 0f;
                _log?.Invoke("[Lobby] Retrying JoinRequest to host...");
                SendJoinRequest(_pendingJoinHostP2PId, _pendingJoinPlayerName, _pendingJoinUserId);
                SendJoinRequestUdp(_pendingJoinPlayerName, _pendingJoinUserId);  // UDP fallback
            }
        }

        // Countdown (host only)
        if (_isInLobby && _isHost && _countdownActive)
        {
            _countdownSeconds -= deltaTime;
            if (_countdownSeconds <= 0)
            {
                _countdownSeconds = 0;
                _countdownActive = false;
                BroadcastLobbyUpdate();
                TriggerTravel();
            }
            else
            {
                OnCountdownTick?.Invoke(_countdownSeconds);
                // Broadcast every tick during countdown
                BroadcastLobbyUpdate();
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  HOST LOBBY
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Create and host a new lobby. Registers the session and adds the host as the first player.
    /// </summary>
    public bool HostLobby(string lobbyName, string scenePath, int maxPlayers, string hostName, string hostUserId)
    {
        if (_isInLobby)
        {
            _log?.Invoke("[Lobby] Already in a lobby. Leave first.");
            return false;
        }

        _lobbyName = lobbyName;
        _scenePath = scenePath;
        _maxPlayers = maxPlayers;
        _isHost = true;
        _isInLobby = true;
        _countdownActive = false;
        _countdownSeconds = 0;
        _travelTriggered = false;

        // Register the session
        _sessionManager.Host(lobbyName, hostName, scenePath, maxPlayers);

        // Add host as first player
        lock (_playersLock)
        {
            _players.Clear();
            _players.Add(new LobbyPlayer
            {
                UserId = GetLocalUserId(),
                DisplayName = hostName,
                IsReady = true,
                JoinedAt = DateTime.UtcNow
            });
        }

        _log?.Invoke($"[Lobby] Hosted lobby '{lobbyName}' with scene '{scenePath}' (max {maxPlayers} players)");
        _log?.Invoke($"[Lobby] Host P2P ID: {GetLocalUserId()}");
        OnPlayersUpdated?.Invoke(Players);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  JOIN LOBBY
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Join an existing lobby. Called when a client connects to a session.
    /// </summary>
    public bool JoinLobby(string lobbyName, string scenePath, string playerName, string userId)
    {
        if (_isInLobby)
        {
            _log?.Invoke("[Lobby] Already in a lobby. Leave first.");
            return false;
        }

        // Locate host session to get host PUID
        var session = _sessionManager.DiscoveredSessions
            .FirstOrDefault(s => s.SessionName == lobbyName);

        if (session == null)
        {
            session = _sessionManager.DiscoveredSessions.FirstOrDefault();
        }

        if (session == null)
        {
            _log?.Invoke($"[Lobby] Join failed: Session '{lobbyName}' not discovered yet.");
            OnLobbyError?.Invoke($"Session '{lobbyName}' not found");
            return false;
        }

        string hostP2PId = session.HostUserId;
        _log?.Invoke($"[Lobby] Found host session: '{session.SessionName}' (HostUserId: {hostP2PId})");

        if (string.IsNullOrEmpty(hostP2PId))
        {
            _log?.Invoke($"[Lobby] WARNING: Host P2P ID is invalid: '{hostP2PId}'. Host may not be logged in via EOS.");
            _log?.Invoke($"[Lobby] Waiting for host to broadcast with valid P2P ID...");
        }

        _lobbyName = lobbyName;
        _scenePath = scenePath;
        _isHost = false;
        _isInLobby = true;
        _countdownActive = false;
        _countdownSeconds = 0;
        _travelTriggered = false;

        // Add self as a player
        lock (_playersLock)
        {
            _players.Clear();
            _players.Add(new LobbyPlayer
            {
                UserId = GetLocalUserId(),
                DisplayName = playerName,
                IsReady = true,
                JoinedAt = DateTime.UtcNow
            });
        }

        _log?.Invoke($"[Lobby] Joined lobby '{lobbyName}' locally. (scene: {scenePath})");

        // Update multiplayer service with current peer list
        UpdateMultiplayerLobbyPeers();

        OnPlayersUpdated?.Invoke(Players);

        // Send JoinRequest P2P message to host
        if (!string.IsNullOrEmpty(hostP2PId))
        {
            SendJoinRequest(hostP2PId, playerName, GetLocalUserId());
        }

        // Also send via UDP (works even without P2P connection)
        SendJoinRequestUdp(playerName, GetLocalUserId());

        // Set up retry state so Update() keeps retrying until we get a response
        _pendingJoinHostP2PId = hostP2PId;
        _pendingJoinPlayerName = playerName;
        _pendingJoinUserId = GetLocalUserId();
        _joinRetryTimer = 0f;

        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  PLAYER MANAGEMENT
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Add a player to the lobby (called when a peer connects).
    /// </summary>
    public void AddPlayer(string userId, string displayName)
    {
        lock (_playersLock)
        {
            if (_players.Any(p => p.UserId == userId))
            {
                _log?.Invoke($"[Lobby] Player '{displayName}' already in lobby");
                return;
            }

            if (_players.Count >= _maxPlayers)
            {
                _log?.Invoke($"[Lobby] Lobby full! Cannot add '{displayName}'");
                return;
            }

            _players.Add(new LobbyPlayer
            {
                UserId = userId,
                DisplayName = displayName,
                IsReady = true,
                JoinedAt = DateTime.UtcNow
            });

            _log?.Invoke($"[Lobby] Player '{displayName}' joined ({_players.Count}/{_maxPlayers})");
        }

        // Update multiplayer service with current peer list (proactively opens P2P connections)
        UpdateMultiplayerLobbyPeers();

        OnPlayersUpdated?.Invoke(Players);

        if (_isHost)
        {
            BroadcastLobbyUpdate();      // P2P broadcast to connected peers
            BroadcastLobbyStateUdp();    // UDP broadcast (works even without P2P)
        }
    }

    /// <summary>
    /// Remove a player from the lobby.
    /// </summary>
    public void RemovePlayer(string userId)
    {
        lock (_playersLock)
        {
            var player = _players.FirstOrDefault(p => p.UserId == userId);
            if (player != null)
            {
                _players.Remove(player);
                _log?.Invoke($"[Lobby] Player '{player.DisplayName}' left ({_players.Count}/{_maxPlayers})");
            }
        }

        // Update multiplayer service peer list
        UpdateMultiplayerLobbyPeers();

        OnPlayersUpdated?.Invoke(Players);

        if (_isHost)
        {
            BroadcastLobbyUpdate();
        }
    }

    /// <summary>
    /// Push the current lobby peer list to the multiplayer service
    /// so BroadcastPacket can reach all players (even those not yet in _connectedPeers).
    /// </summary>
    private void UpdateMultiplayerLobbyPeers()
    {
        lock (_playersLock)
        {
            var peerIds = _players.Select(p => p.UserId).ToList();
            _sessionManager.Service?.SetLobbyPeers(peerIds);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  COUNTDOWN & TRAVEL
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Start the countdown to server travel (host only).
    /// </summary>
    public void StartCountdown(float seconds)
    {
        if (!_isHost)
        {
            _log?.Invoke("[Lobby] Only host can start countdown");
            return;
        }

        _countdownActive = true;
        _countdownSeconds = seconds;
        _countdownTotal = seconds;
        _log?.Invoke($"[Lobby] Countdown started: {seconds}s");
        BroadcastLobbyUpdate();
    }

    /// <summary>
    /// Trigger server travel immediately.
    /// </summary>
    public void TriggerTravel()
    {
        if (_travelTriggered) return;
        _travelTriggered = true;

        _log?.Invoke("[Lobby] Triggering server travel...");
        BroadcastServerTravel();
        BroadcastServerTravelUdp();
        OnServerTravel?.Invoke();
    }

    private async void BroadcastServerTravelUdp()
    {
        try
        {
            if (_udpLobbySender == null)
            {
                _udpLobbySender = new System.Net.Sockets.UdpClient();
                _udpLobbySender.Client.SetSocketOption(
                    System.Net.Sockets.SocketOptionLevel.Socket,
                    System.Net.Sockets.SocketOptionName.ReuseAddress, true);
                _udpLobbySender.Client.SetSocketOption(
                    System.Net.Sockets.SocketOptionLevel.Socket,
                    System.Net.Sockets.SocketOptionName.Broadcast, true);
            }

            var state = new LobbyStateUdp
            {
                Type = "server_travel",
                SenderId = GetLocalUserId(),
                SenderName = GetLocalDisplayName(),
                ScenePath = _scenePath,
            };

            string json = JsonSerializer.Serialize(state);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await _udpLobbySender.SendAsync(bytes, bytes.Length,
                new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, LobbyStatePort));
            await _udpLobbySender.SendAsync(bytes, bytes.Length,
                new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, LobbyStatePort));
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Lobby] UDP server travel broadcast error: {ex.Message}");
        }
    }

    /// <summary>
    /// Leave the current lobby.
    /// </summary>
    public void LeaveLobby()
    {
        if (!_isInLobby) return;

        _log?.Invoke("[Lobby] Leaving lobby");
        _isInLobby = false;
        _isHost = false;
        _countdownActive = false;
        _countdownSeconds = 0;
        _travelTriggered = false;
        _pendingJoinHostP2PId = "";
        _joinRetryTimer = 0f;

        lock (_playersLock)
        {
            _players.Clear();
        }

        _sessionManager.Leave();
    }

    // ═══════════════════════════════════════════════════════════════
    //  PUBLIC QUERIES
    // ═══════════════════════════════════════════════════════════════

    public string GetPlayerName(int index)
    {
        lock (_playersLock)
        {
            if (index >= 0 && index < _players.Count)
                return _players[index].DisplayName;
        }
        return "";
    }

    public int GetPlayerCount() => PlayerCount;

    public float GetCountdownProgress()
    {
        if (_countdownTotal <= 0) return 0;
        return 1f - (_countdownSeconds / _countdownTotal);
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private string GetLocalUserId()
    {
        if (_sessionManager.Service != null && !string.IsNullOrEmpty(_sessionManager.Service.LocalP2PId))
            return _sessionManager.Service.LocalP2PId;
        return Environment.UserName;
    }

    private string GetLocalDisplayName()
    {
        if (_sessionManager.Service is EosMultiplayerService eos && !string.IsNullOrEmpty(eos.EosDisplayName))
            return eos.EosDisplayName;
        return Environment.UserName;
    }

    public void Dispose()
    {
        LeaveLobby();
    }
}

// ═══════════════════════════════════════════════════════════════
//  P2P MESSAGE TYPES
// ═══════════════════════════════════════════════════════════════

public enum LobbyMessageType
{
    JoinRequest,
    LobbyUpdate,
    ServerTravel,
}

public class LobbyMessage
{
    public LobbyMessageType Type { get; set; }
    public string SenderId { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string Data { get; set; } = "";
}

public class LobbyUpdatePayload
{
    public List<LobbyPlayerPayload> Players { get; set; } = new();
    public bool CountdownActive { get; set; }
    public float CountdownSeconds { get; set; }
    public float CountdownTotal { get; set; }
    public string ScenePath { get; set; } = "";
}

public class LobbyPlayerPayload
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsReady { get; set; } = true;
}

/// <summary>
/// UDP message for lobby state synchronization.
/// </summary>
public class LobbyStateUdp
{
    public string Type { get; set; } = "";  // "lobby_state" or "join_request"
    public string SenderId { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string LobbyName { get; set; } = "";
    public string ScenePath { get; set; } = "";
    public int MaxPlayers { get; set; } = 8;
    public bool CountdownActive { get; set; }
    public float CountdownSeconds { get; set; }
    public float CountdownTotal { get; set; }
    public List<LobbyPlayerPayload> Players { get; set; } = new();
}
