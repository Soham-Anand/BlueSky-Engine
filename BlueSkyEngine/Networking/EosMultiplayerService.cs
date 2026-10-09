using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BlueSky.Networking.EOS;

namespace BlueSky.Networking;

/// <summary>
/// EOS-backed multiplayer coordinator.
/// Uses the real Epic Online Services SDK for Auth, Connect, and P2P transport.
/// </summary>
public sealed class EosMultiplayerService : IMultiplayerService
{
    public bool IsEnabled => Config.Enabled;
    public bool IsInitialized { get; private set; }
    public bool IsLoggedIn { get; private set; }
    public bool IsLoginPending { get; private set; }
    public MultiplayerConfig Config { get; }
    public string LocalP2PId => _eosManager != null && _eosManager.IsConnectLoggedIn
        ? EOSManager.ProductUserIdToString(_eosManager.LocalProductUserId)
        : "";

    private Action<string>? _log;
    private HttpListener? _activeListener;
    private EOSManager? _eosManager;

    // ── P2P state exposed to engine ──────────────────────────────────
    public bool IsP2PReady => _eosManager?.IsConnectLoggedIn == true;
    public event Action<string>? OnPeerConnected;
    public event Action<string, byte[], byte>? OnPacketReceived;

    // ── Lobby peer tracking (for reliable broadcasting) ──────────────
    private readonly HashSet<string> _lobbyPeers = new();

    public EosMultiplayerService(MultiplayerConfig config)
    {
        Config = config;
    }

    /// <summary>The display name from the EOS UserInfo API (set after successful Auth login).</summary>
    public string EosDisplayName => _eosManager?.DisplayName ?? "";

    public void Login(string localPlayerName, Action<bool, string> onComplete)
    {
        _log?.Invoke($"[Net] EOS Login requested for player: '{localPlayerName}'");
        if (IsLoginPending)
        {
            _log?.Invoke("[Net] EOS Login is already pending — ignoring duplicate login request.");
            onComplete?.Invoke(false, "Login already pending");
            return;
        }

        if (!Config.Eos.IsConfigured)
        {
            _log?.Invoke("[Net] EOS credentials are not configured. Set environment variables or EOS.ini.");
            onComplete?.Invoke(false, "EOS Credentials not configured");
            return;
        }

        // Use the real EOS SDK for login
        if (_eosManager == null || !_eosManager.IsInitialized)
        {
            _log?.Invoke("[Net] EOS SDK not initialized — call Initialize() first");
            onComplete?.Invoke(false, "EOS SDK not initialized");
            return;
        }

        _log?.Invoke($"[Net] Starting EOS Auth login (Account Portal) for client ID: {Config.Eos.ClientId}");

        IsLoginPending = true;
        _eosManager.OnPeerConnected = (peerInfo) =>
        {
            _log?.Invoke($"[Net] Peer connected: {peerInfo}");
            OnPeerConnected?.Invoke(peerInfo);
        };

        _eosManager.OnPacketReceived = (peerId, data, channel) =>
        {
            OnPacketReceived?.Invoke(peerId, data, channel);
        };

        _eosManager.LoginWithAccountPortal((success, message) =>
        {
            IsLoginPending = false;

            if (success)
            {
                IsLoggedIn = true;
                _log?.Invoke($"[Net] Login successful — ProductUserId is valid");
            }
            else
            {
                _log?.Invoke($"[Net] Login failed: {message}");
            }
            onComplete?.Invoke(success, message);
        });
    }

    public void Initialize(Action<string> log)
    {
        _log = log;

        // Always initialize EOS SDK if credentials are configured,
        // even when multiplayer mode is Offline. This allows the lobby
        // script to authenticate first, then transition to Host/Join.
        if (!Config.Eos.IsConfigured)
        {
            log("[Net] EOS credentials are not configured. Set EOS_PRODUCT_ID, EOS_SANDBOX_ID, EOS_DEPLOYMENT_ID, EOS_CLIENT_ID, and EOS_CLIENT_SECRET.");
            log("[Net] Multiplayer session state will stay local until EOS is configured.");
            IsInitialized = false;
            return;
        }

        if (!Config.Enabled)
        {
            log("[Net] Multiplayer config is offline, but EOS credentials detected — initializing SDK for lobby-driven auth");
        }

        log($"[Net] Initializing EOS SDK ({Config.Mode}, session='{Config.SessionName}', player='{Config.LocalPlayerName}')");

        // Initialize the real EOS SDK
        _eosManager = new EOSManager();
        bool success = _eosManager.Initialize(Config.Eos, log);

        if (success)
        {
            log("[Net] EOS SDK initialized successfully — ready for Auth/Connect/P2P");
            IsInitialized = true;
            IsLoginPending = false;
        }
        else
        {
            log("[Net] EOS SDK initialization failed — multiplayer will not work");
            IsInitialized = false;
            IsLoginPending = false;
        }
    }

    public void Tick(double deltaTime)
    {
        if (!IsInitialized || _eosManager == null)
            return;

        // Always tick the EOS SDK to pump callbacks (even in offline mode,
        // so auth/login callbacks get processed)
        _eosManager.Tick();

        // Only receive P2P packets if fully connected
        if (_eosManager.IsConnectLoggedIn)
        {
            _eosManager.ReceivePackets();
        }
    }

    /// <summary>
    /// Send data to all connected peers via P2P.
    /// Uses both the EOS connected peers list AND the lobby peer list
    /// to ensure all players receive the data even if P2P connection
    /// establishment is delayed.
    /// </summary>
    public void BroadcastPacket(byte[] data, byte channel = 0)
    {
        if (_eosManager == null || !_eosManager.IsConnectLoggedIn)
            return;

        // Collect all peers to send to: connected peers + lobby peers
        var allPeers = new HashSet<string>();

        // Add connected peers
        foreach (var peer in _eosManager.GetConnectedPeers())
            allPeers.Add(EOSManager.ProductUserIdToString(peer));

        // Add lobby peers (may include peers not yet fully connected)
        lock (_lobbyPeers)
        {
            foreach (var peerId in _lobbyPeers)
                allPeers.Add(peerId);
        }

        _log?.Invoke($"[Net] BroadcastPacket channel={channel} to {allPeers.Count} peers: {string.Join(", ", allPeers)}");

        foreach (var peerId in allPeers)
        {
            bool sent = SendPacket(peerId, data, channel);
            _log?.Invoke($"[Net] SendPacket to {peerId} channel={channel}: {(sent ? "OK" : "FAILED")}");
        }
    }

    /// <summary>
    /// Update the set of known lobby peer IDs.
    /// Called by the lobby manager when the player list changes.
    /// Only opens P2P connections for NEW peers to avoid handshake spam.
    /// Use ForceReconnectToAllPeers() when a forced reconnect is needed
    /// (e.g. after server travel).
    /// </summary>
    public void SetLobbyPeers(IEnumerable<string> peerIds)
    {
        var newPeers = new HashSet<string>(peerIds);

        lock (_lobbyPeers)
        {
            foreach (var peerId in newPeers)
            {
                if (!_lobbyPeers.Contains(peerId))
                {
                    _log?.Invoke($"[Net] New lobby peer: {peerId} — opening P2P connection");
                    TryOpenConnection(peerId);
                }
            }
            _lobbyPeers.Clear();
            foreach (var peerId in newPeers)
                _lobbyPeers.Add(peerId);
        }
    }

    /// <summary>
    /// Forcefully re-open P2P connections to all specified peer IDs.
    /// Used after server travel when connections may have degraded.
    /// Closes stale connections first to clear EOS session ID conflicts.
    /// This bypasses the normal SetLobbyPeers optimization that skips existing peers.
    /// </summary>
    public void ForceReconnectToAllPeers(IEnumerable<string> peerIds)
    {
        // First close all existing connections to clear stale session IDs
        _log?.Invoke("[Net] Closing all P2P connections before reconnect...");
        _eosManager?.CloseAllP2PConnections();

        // Re-open connections to all peers
        foreach (var peerId in peerIds)
        {
            if (!string.IsNullOrEmpty(peerId))
            {
                _log?.Invoke($"[Net] Force reconnecting to peer: {peerId}");
                TryOpenConnection(peerId);
            }
        }
    }

    /// <summary>
    /// Proactively open a P2P connection to a peer.
    /// </summary>
    public void TryOpenConnection(string peerId)
    {
        if (_eosManager == null || !_eosManager.IsConnectLoggedIn) return;
        if (string.IsNullOrEmpty(peerId)) return;

        var target = EOSManager.ProductUserIdFromString(peerId);
        if (target.Handle == IntPtr.Zero)
        {
            _log?.Invoke($"[EOS] TryOpenConnection: invalid peer ID '{peerId}'");
            return;
        }
        _eosManager.OpenConnection(target);
    }

    /// <summary>
    /// Send data to a specific peer via P2P.
    /// </summary>
    public bool SendPacket(string peerId, byte[] data, byte channel = 0)
    {
        if (_eosManager == null || !_eosManager.IsConnectLoggedIn)
            return false;

        if (string.IsNullOrEmpty(peerId))
            return false;

        var targetPeer = EOSManager.ProductUserIdFromString(peerId);
        if (targetPeer.Handle == IntPtr.Zero)
            return false;

        return _eosManager.SendPacket(targetPeer, data, channel);
    }

    /// <summary>
    /// Get the list of connected peer IDs.
    /// </summary>
    public List<string> GetConnectedPeerIds()
    {
        var result = new List<string>();
        if (_eosManager == null || !_eosManager.IsConnectLoggedIn)
            return result;

        foreach (var peer in _eosManager.GetConnectedPeers())
        {
            result.Add(EOSManager.ProductUserIdToString(peer));
        }
        return result;
    }

    public void Shutdown()
    {
        if (IsInitialized && Config.Enabled)
        {
            _log?.Invoke("[Net] EOS multiplayer shutdown");
        }

        try
        {
            if (_activeListener != null)
            {
                _activeListener.Stop();
                _activeListener.Close();
                _activeListener = null;
            }
        }
        catch {}

        // Dispose the EOS SDK manager
        _eosManager?.Dispose();
        _eosManager = null;

        IsInitialized = false;
        IsLoggedIn = false;
        IsLoginPending = false;
    }

    public void Dispose()
    {
        Shutdown();
    }
}