using System;
using System.Collections.Generic;

namespace BlueSky.Networking;

public sealed class NullMultiplayerService : IMultiplayerService
{
    public bool IsEnabled => false;
    public bool IsInitialized => true;
    public bool IsLoggedIn => false;
    public bool IsLoginPending => false;
    public MultiplayerConfig Config { get; }
    public string LocalP2PId => "";

    // Required by IMultiplayerService; never raised by the null (offline) implementation.
#pragma warning disable CS0067
    public event Action<string, byte[], byte>? OnPacketReceived;
    public event Action<string>? OnPeerConnected;
#pragma warning restore CS0067

    public NullMultiplayerService(MultiplayerConfig config)
    {
        Config = config;
    }

    public void Initialize(Action<string> log)
    {
        log("[Net] Multiplayer disabled - running offline");
    }

    public void Login(string localPlayerName, Action<bool, string> onComplete)
    {
        onComplete?.Invoke(false, "Multiplayer disabled (running offline)");
    }

    public void Tick(double deltaTime)
    {
    }

    public void Shutdown()
    {
    }

    public void BroadcastPacket(byte[] data, byte channel = 0) { }

    public bool SendPacket(string peerId, byte[] data, byte channel = 0) => false;

    public void SetLobbyPeers(IEnumerable<string> peerIds) { }

    public void ForceReconnectToAllPeers(IEnumerable<string> peerIds) { }

    public void TryOpenConnection(string peerId) { }

    public List<string> GetConnectedPeerIds() => new();

    public void Dispose()
    {
        Shutdown();
    }
}
