using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

namespace DiscordProximityVoice;

internal sealed class DiscordVoiceServerConfig
{
    public bool Enabled = true;
    public ulong DiscordApplicationId = 0;
    public string DiscordBotToken = "";
    public string ServerVoiceId = "";
    public string LobbySecret = "";
    public bool RequireClientSetup = true;
    public float NearRadius = VoiceSettings.DefaultNearRadius;
    public float FarRadius = VoiceSettings.DefaultFarRadius;
    public int UpdateIntervalMs = VoiceSettings.DefaultUpdateIntervalMs;

    public void FillDefaults(int worldSeed)
    {
        if (string.IsNullOrWhiteSpace(ServerVoiceId))
        {
            ServerVoiceId = "vs-" + worldSeed;
        }

        if (string.IsNullOrWhiteSpace(LobbySecret))
        {
            LobbySecret = "vs-dpvoice-" + Guid.NewGuid().ToString("N");
        }

        if (NearRadius <= 0f) NearRadius = VoiceSettings.DefaultNearRadius;
        if (FarRadius <= NearRadius) FarRadius = VoiceSettings.DefaultFarRadius;
        if (UpdateIntervalMs < 1000) UpdateIntervalMs = 1000;
    }
}

internal sealed class DiscordVoiceClientConfig
{
    public ulong ApplicationId = 0;
    public bool AutoConnect = true;
    public Dictionary<string, DiscordVoiceClientServerSetup> Servers = new Dictionary<string, DiscordVoiceClientServerSetup>();
}

internal static class VoiceTalkModes
{
    public const string PushToTalk = "PushToTalk";
    public const string OpenMic = "OpenMic";
}

internal sealed class VoiceInputDevice
{
    public string Id = "";
    public string Name = "Default microphone";
}

internal sealed class DiscordVoiceClientServerSetup
{
    public const int DefaultPushToTalkKeyCode = (int)GlKeys.V;
    public const int CurrentSetupVersion = 2;

    public int SetupVersion = 0;
    public ulong ApplicationId = 0;
    public bool Completed = false;
    public bool VoiceDisabled = false;
    public string MicrophoneDeviceId = "";
    public string MicrophoneDeviceName = "Default microphone";
    public string TalkMode = VoiceTalkModes.PushToTalk;
    public int PushToTalkKeyCode = DefaultPushToTalkKeyCode;
    public bool PushToTalkCtrl = false;
    public bool PushToTalkAlt = false;
    public bool PushToTalkShift = false;
    public string LastBackendStatus = "";

    public void FillDefaults()
    {
        if (string.IsNullOrWhiteSpace(MicrophoneDeviceName)) MicrophoneDeviceName = "Default microphone";
        if (TalkMode != VoiceTalkModes.OpenMic) TalkMode = VoiceTalkModes.PushToTalk;
        if (PushToTalkKeyCode <= 0) PushToTalkKeyCode = DefaultPushToTalkKeyCode;
    }

    public DiscordVoiceClientServerSetup Clone()
    {
        return new DiscordVoiceClientServerSetup
        {
            SetupVersion = SetupVersion,
            ApplicationId = ApplicationId,
            Completed = Completed,
            VoiceDisabled = VoiceDisabled,
            MicrophoneDeviceId = MicrophoneDeviceId,
            MicrophoneDeviceName = MicrophoneDeviceName,
            TalkMode = TalkMode,
            PushToTalkKeyCode = PushToTalkKeyCode,
            PushToTalkCtrl = PushToTalkCtrl,
            PushToTalkAlt = PushToTalkAlt,
            PushToTalkShift = PushToTalkShift,
            LastBackendStatus = LastBackendStatus
        };
    }
}
