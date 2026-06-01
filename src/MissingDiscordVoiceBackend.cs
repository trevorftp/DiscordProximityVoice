using System.Collections.Generic;
using System.IO;

namespace DiscordProximityVoice;

internal sealed class MissingDiscordVoiceBackend : IDiscordVoiceBackend
{
    private readonly string bridgePath = "";
    private readonly string discordPath = "";

    public MissingDiscordVoiceBackend(string bridgePath, string discordPath)
    {
        this.bridgePath = bridgePath;
        this.discordPath = discordPath;
    }

    public bool Available => false;
    public bool Linked => false;
    public bool JoinedLobby => false;
    public bool InCall => false;
    public bool Muted => false;
    public bool Deafened => false;
    public string DiscordUserId => "";
    public string Status => "native bridge missing: " + Path.GetFileName(bridgePath) + ", " + Path.GetFileName(discordPath);
    public float InputLevel => 0f;
    public IReadOnlyList<VoiceInputDevice> InputDevices { get; } = new[] { new VoiceInputDevice() };

    public void Configure(VoiceSessionPacket session) { }
    public void ApplySettings(DiscordVoiceClientServerSetup setup) { }
    public void SetPushToTalkActive(bool active) { }
    public void RefreshInputDevices() { }
    public void SetPeerVolume(VoicePeerPacket peer) { }
    public void Tick(float dt) { }
    public void Disconnect() { }
    public void Dispose() { }
}
