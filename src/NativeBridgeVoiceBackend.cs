using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Vintagestory.API.Client;

namespace DiscordProximityVoice;

internal sealed class NativeBridgeVoiceBackend : IDiscordVoiceBackend
{
    private readonly ICoreClientAPI api = null;
    private readonly DiscordVoiceClientConfig config = null;
    private readonly string bridgePath = "";
    private readonly string discordPath = "";
    private readonly List<VoiceInputDevice> inputDevices = new List<VoiceInputDevice> { new VoiceInputDevice() };
    private string status = "";
    private bool initialized = false;
    private string configuredInputDeviceId = "";
    private string configuredTalkMode = VoiceTalkModes.PushToTalk;
    private bool lastPushToTalkActive = false;
    private readonly DpvLogCallback logCallback = null;

    public NativeBridgeVoiceBackend(ICoreClientAPI api, DiscordVoiceClientConfig config, string bridgePath, string discordPath)
    {
        this.api = api;
        this.config = config;
        this.bridgePath = bridgePath;
        this.discordPath = discordPath;
        logCallback = OnBridgeLog;
        status = "native bridge present, not initialized";
    }

    public bool Available => true;
    public bool Linked { get; private set; } = false;
    public bool JoinedLobby { get; private set; } = false;
    public bool InCall { get; private set; } = false;
    public bool Muted { get; private set; } = false;
    public bool Deafened { get; private set; } = false;
    public string DiscordUserId { get; private set; } = "";
    public string Status => status;
    public float InputLevel => initialized ? Math.Clamp(dpv_get_input_level(), 0f, 1f) : 0f;
    public IReadOnlyList<VoiceInputDevice> InputDevices => inputDevices;

    public void Configure(VoiceSessionPacket session)
    {
        if (!config.AutoConnect)
        {
            status = "auto connect disabled";
            return;
        }

        ulong applicationId = session.DiscordApplicationId != 0 ? session.DiscordApplicationId : config.ApplicationId;
        if (applicationId == 0)
        {
            status = "server has no Discord application id configured";
            return;
        }

        if (string.IsNullOrWhiteSpace(session.DiscordAccessToken))
        {
            status = string.IsNullOrWhiteSpace(session.AuthStatus) ? "waiting for Discord provisional token" : session.AuthStatus;
            return;
        }

        if (!EnsureInitialized(session, applicationId)) return;

        int lobbyResult = dpv_connect_lobby(session.LobbySecret ?? "");
        JoinedLobby = lobbyResult == 0;
        if (!JoinedLobby)
        {
            status = "bridge lobby join failed: " + lobbyResult;
            return;
        }

        int callResult = dpv_start_call();
        InCall = callResult == 0;
        ApplyCurrentSettings();
        status = InCall ? "in Discord lobby call " + session.ServerVoiceId : "bridge call start failed: " + callResult;
    }

    public void ApplySettings(DiscordVoiceClientServerSetup setup)
    {
        if (setup == null) return;

        setup.FillDefaults();
        configuredInputDeviceId = setup.MicrophoneDeviceId ?? "";
        configuredTalkMode = setup.TalkMode;
        if (initialized)
        {
            ApplyCurrentSettings();
        }
    }

    public void SetPushToTalkActive(bool active)
    {
        if (!initialized || !InCall || configuredTalkMode != VoiceTalkModes.PushToTalk) return;
        if (active == lastPushToTalkActive) return;

        dpv_set_ptt_active(active);
        lastPushToTalkActive = active;
    }

    public void RefreshInputDevices()
    {
        if (!initialized) return;

        dpv_refresh_input_devices();
        ReadInputDevices();
    }

    public void SetPeerVolume(VoicePeerPacket peer)
    {
        if (!initialized || !InCall || string.IsNullOrEmpty(peer.VoiceToken)) return;

        int result = dpv_set_peer_volume(peer.VoiceToken, peer.Volume);
        if (result != 0)
        {
            api.Logger.VerboseDebug("[DiscordProximityVoice] volume update failed for {0}: {1}", peer.PlayerName, result);
        }
    }

    public void Tick(float dt)
    {
        if (initialized)
        {
            dpv_tick();
            ReadInputDevices();
        }
    }

    public void Disconnect()
    {
        if (initialized)
        {
            dpv_disconnect();
        }

        JoinedLobby = false;
        InCall = false;
    }

    public void Dispose()
    {
        Disconnect();
        if (initialized)
        {
            dpv_shutdown();
            initialized = false;
        }
    }

    private bool EnsureInitialized(VoiceSessionPacket session, ulong applicationId)
    {
        if (initialized) return true;

        try
        {
            NativeLibrary.Load(discordPath);
            NativeLibrary.Load(bridgePath);
            int result = dpv_init(applicationId, session.PlayerVoiceToken ?? "", session.DiscordAccessToken ?? "", logCallback);
            if (result != 0)
            {
                status = "bridge init failed: " + result;
                return false;
            }

            initialized = true;
            Linked = true;
            DiscordUserId = ReadDiscordUserId();
            dpv_refresh_input_devices();
            ApplyCurrentSettings();
            status = "bridge initialized";
            return true;
        }
        catch (Exception ex)
        {
            status = "bridge load failed: " + ex.GetType().Name + ": " + ex.Message;
            api.Logger.Error("[DiscordProximityVoice] {0}", status);
            return false;
        }
    }

    private string ReadDiscordUserId()
    {
        IntPtr ptr = dpv_get_discord_user_id();
        return ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
    }

    private void ApplyCurrentSettings()
    {
        int audioMode = configuredTalkMode == VoiceTalkModes.OpenMic ? 1 : 2;
        dpv_set_audio_mode(audioMode);
        if (!string.IsNullOrEmpty(configuredInputDeviceId))
        {
            dpv_set_input_device(configuredInputDeviceId);
        }

        if (configuredTalkMode == VoiceTalkModes.PushToTalk)
        {
            lastPushToTalkActive = false;
            dpv_set_ptt_active(false);
        }
    }

    private void ReadInputDevices()
    {
        int count = dpv_get_input_device_count();
        if (count <= 0) return;

        inputDevices.Clear();
        inputDevices.Add(new VoiceInputDevice());
        for (int i = 0; i < count; i++)
        {
            string id = PtrToString(dpv_get_input_device_id(i));
            string name = PtrToString(dpv_get_input_device_name(i));
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;

            inputDevices.Add(new VoiceInputDevice { Id = id, Name = name });
        }
    }

    private static string PtrToString(IntPtr ptr)
    {
        return ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
    }

    private void OnBridgeLog(string message)
    {
        api.Logger.Notification("[DiscordProximityVoice.Native] {0}", message);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DpvLogCallback([MarshalAs(UnmanagedType.LPUTF8Str)] string message);

    [DllImport("DiscordProximityVoice.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern int dpv_init(
        ulong applicationId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string voiceToken,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string DiscordAccessToken,
        DpvLogCallback logCallback);

    [DllImport("DiscordProximityVoice.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern void dpv_tick();

    [DllImport("DiscordProximityVoice.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern int dpv_connect_lobby([MarshalAs(UnmanagedType.LPUTF8Str)] string lobbySecret);

    [DllImport("DiscordProximityVoice.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern int dpv_start_call();

    [DllImport("DiscordProximityVoice.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern void dpv_set_audio_mode(int mode);

    [DllImport("DiscordProximityVoice.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern void dpv_set_ptt_active([MarshalAs(UnmanagedType.I1)] bool active);

    [DllImport("DiscordProximityVoice.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern void dpv_set_input_device([MarshalAs(UnmanagedType.LPUTF8Str)] string deviceId);

    [DllImport("DiscordProximityVoice.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern void dpv_refresh_input_devices();

    [DllImport("DiscordProximityVoice.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern int dpv_get_input_device_count();

    [DllImport("DiscordProximityVoice.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr dpv_get_input_device_id(int index);

    [DllImport("DiscordProximityVoice.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr dpv_get_input_device_name(int index);

    [DllImport("DiscordProximityVoice.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern float dpv_get_input_level();

    [DllImport("DiscordProximityVoice.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern int dpv_set_peer_volume([MarshalAs(UnmanagedType.LPUTF8Str)] string voiceToken, float volume);

    [DllImport("DiscordProximityVoice.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern void dpv_disconnect();

    [DllImport("DiscordProximityVoice.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern void dpv_shutdown();

    [DllImport("DiscordProximityVoice.Native", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr dpv_get_discord_user_id();
}
