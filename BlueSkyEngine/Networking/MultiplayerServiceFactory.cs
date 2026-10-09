namespace BlueSky.Networking;

public static class MultiplayerServiceFactory
{
    public static IMultiplayerService Create(MultiplayerConfig config)
    {
        if (!config.Enabled)
            return new NullMultiplayerService(config);

        return new EosMultiplayerService(config);
    }
}

