using System;

namespace BlueSky.Networking;

public interface IMultiplayerService : IDisposable
{
    bool IsEnabled { get; }
    bool IsInitialized { get; }
    bool IsLoggedIn { get; }
    bool IsLoginPending { get; }
    MultiplayerConfig Config { get; }
    string LocalP2PId { get; }

    void Initialize(Action<string> log);
    void Tick(double deltaTime);
    void Shutdown();
    void Login(string localPlayerName, Action<bool, string> onComplete);

    event Action<string, byte[], byte>? OnPacketReceived;
    event Action<string>? OnPeerConnected;
    void BroadcastPacket(byte[] data, byte channel = 0);
    bool SendPacket(string peerId, byte[] data, byte channel = 0);
    void SetLobbyPeers(System.Collections.Generic.IEnumerable<string> peerIds);
    void ForceReconnectToAllPeers(System.Collections.Generic.IEnumerable<string> peerIds);
    void TryOpenConnection(string peerId);
    System.Collections.Generic.List<string> GetConnectedPeerIds();
}

