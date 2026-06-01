using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using HarmonyLib;
using Vintagestory.Client.NoObf;

namespace DiscordProximityVoice;

public sealed class DiscordProximityVoiceModSystem : ModSystem
{
    internal const string ChannelName = "dpvoice";
    internal const int ProtocolVersion = 2;

    private VoiceClient client = null;
    private VoiceServer server = null;
    private Harmony harmony = null;

    public override bool ShouldLoad(EnumAppSide forSide) => true;

    public override void Start(ICoreAPI api)
    {
        api.Network
            .RegisterChannel(ChannelName)
            .RegisterMessageType<VoiceSessionPacket>()
            .RegisterMessageType<VoiceHelloPacket>()
            .RegisterMessageType<VoiceStatePacket>()
            .RegisterMessageType<VoiceProximityPacket>();
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        client = new VoiceClient(api);
        VoiceReadinessGate.Set(client);
        harmony = new Harmony("discordproximityvoice.readygate");
        harmony.Patch(
            AccessTools.Method(typeof(NetworkAPI), nameof(NetworkAPI.SendPlayerNowReady)),
            prefix: new HarmonyMethod(typeof(PlayerReadyPatch), nameof(PlayerReadyPatch.Prefix))
        );
        client.Start();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        server = new VoiceServer(api);
        server.Start();
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll("discordproximityvoice.readygate");
        VoiceReadinessGate.Clear(client);
        client?.Dispose();
        server?.Dispose();
    }
}
