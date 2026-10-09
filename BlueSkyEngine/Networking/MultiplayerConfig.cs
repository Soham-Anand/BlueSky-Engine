using System;

namespace BlueSky.Networking;

public sealed class MultiplayerConfig
{
    public MultiplayerMode Mode { get; set; } = MultiplayerMode.Host;
    public string SessionName { get; set; } = "BlueSkySession";
    public string LocalPlayerName { get; set; } = Environment.UserName;
    public string? JoinSessionId { get; set; }
    public string ScenePath { get; set; } = "";
    public EosCredentials Eos { get; set; } = EosCredentials.FromEnvironment();

    public bool Enabled => Mode != MultiplayerMode.Offline;

    public static MultiplayerConfig Offline() => new();
}

