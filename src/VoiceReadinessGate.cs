namespace DiscordProximityVoice;

internal static class VoiceReadinessGate
{
    private static VoiceClient client = null;

    public static void Set(VoiceClient voiceClient)
    {
        client = voiceClient;
    }

    public static void Clear(VoiceClient voiceClient)
    {
        if (client == voiceClient) client = null;
    }

    public static bool ShouldBlockPlayerReady()
    {
        return client?.TryBlockPlayerReady() == true;
    }
}

internal static class PlayerReadyPatch
{
    public static bool Prefix()
    {
        return !VoiceReadinessGate.ShouldBlockPlayerReady();
    }
}
