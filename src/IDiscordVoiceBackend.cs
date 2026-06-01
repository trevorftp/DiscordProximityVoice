using System;
using System.Collections.Generic;

namespace DiscordProximityVoice;

internal interface IDiscordVoiceBackend : IDisposable
{
    bool Available { get; }
    bool Linked { get; }
    bool JoinedLobby { get; }
    bool InCall { get; }
    bool Muted { get; }
    bool Deafened { get; }
    string DiscordUserId { get; }
    string Status { get; }
    float InputLevel { get; }
    IReadOnlyList<VoiceInputDevice> InputDevices { get; }

    void Configure(VoiceSessionPacket session);
    void ApplySettings(DiscordVoiceClientServerSetup setup);
    void SetPushToTalkActive(bool active);
    void RefreshInputDevices();
    void SetPeerVolume(VoicePeerPacket peer);
    void Tick(float dt);
    void Disconnect();
}
