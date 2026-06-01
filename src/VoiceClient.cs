using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace DiscordProximityVoice;

internal sealed class VoiceClient : IDisposable
{
    private const string ClientConfigName = "DiscordProximityVoice.Client.json";
    internal const string PushToTalkHotkeyCode = "dpvoiceptt";

    private readonly ICoreClientAPI api = null;
    private readonly DiscordVoiceClientConfig config = null;
    private readonly IDiscordVoiceBackend backend = null;

    private IClientNetworkChannel channel = null;
    private VoiceSessionPacket session = null;
    private GuiDialogVoiceSetup setupDialog = null;
    private long tickListenerId = 0;
    private int lastPeerCount = 0;
    private float stateSendSeconds = 0f;
    private bool pendingSetupDialog = false;
    private string lastHelloKey = "";
    private string lastStateKey = "";
    private readonly HashSet<string> activePeerTokens = new HashSet<string>();

    public VoiceClient(ICoreClientAPI api)
    {
        this.api = api;
        config = LoadConfig(api);
        backend = DiscordVoiceBackendFactory.Create(api, config);
    }

    private static DiscordVoiceClientConfig LoadConfig(ICoreClientAPI api)
    {
        DiscordVoiceClientConfig config = api.LoadModConfig<DiscordVoiceClientConfig>(ClientConfigName);
        if (config == null)
        {
            config = new DiscordVoiceClientConfig();
            api.StoreModConfig(config, ClientConfigName);
        }

        config.Servers ??= new Dictionary<string, DiscordVoiceClientServerSetup>();
        return config;
    }

    public void Start()
    {
        api.Input.RegisterHotKey(
            PushToTalkHotkeyCode,
            "Voice push to talk",
            (GlKeys)DiscordVoiceClientServerSetup.DefaultPushToTalkKeyCode,
            HotkeyType.CharacterControls
        );

        channel = api.Network.GetChannel(DiscordProximityVoiceModSystem.ChannelName)
            .SetMessageHandler<VoiceSessionPacket>(OnSession)
            .SetMessageHandler<VoiceProximityPacket>(OnProximity);

        api.Event.LevelFinalize += OnLevelFinalize;
        api.Event.IsPlayerReady += OnIsPlayerReady;
        api.Event.LeaveWorld += OnLeaveWorld;
        tickListenerId = api.Event.RegisterGameTickListener(OnTick, 100);

        api.ChatCommands.Create("dpvoice")
            .WithDescription("Discord proximity voice status")
            .HandleWith(OnCommand);

        api.ChatCommands.Create("voiceconfig")
            .WithAlias("vc")
            .WithDescription("Open Discord proximity voice setup")
            .HandleWith(OnVoiceConfigCommand);

        api.Logger.Notification("[DiscordProximityVoice] client side started: {0}", backend.Status);
    }

    private TextCommandResult OnCommand(TextCommandCallingArgs args)
    {
        string sessionState = session == null ? "no session" : "session " + session.ServerVoiceId + ", peers=" + lastPeerCount;
        return TextCommandResult.Success("DiscordProximityVoice: " + sessionState + ", backend=" + backend.Status);
    }

    private TextCommandResult OnVoiceConfigCommand(TextCommandCallingArgs args)
    {
        if (session == null)
        {
            return TextCommandResult.Error("No Discord voice session is active on this server yet.");
        }

        if (!session.Enabled)
        {
            return TextCommandResult.Error("Discord proximity voice is disabled on this server.");
        }

        OpenSetupDialog(session);
        SendHelloIfChanged();
        SendStateIfChanged(true);
        return TextCommandResult.Success("Opening Discord voice setup.");
    }

    private void OnLevelFinalize()
    {
        SendHello();
    }

    private bool OnIsPlayerReady(ref EnumHandling handling)
    {
        if (!TryBlockPlayerReady()) return true;

        handling = EnumHandling.PreventDefault;
        return false;
    }

    public bool TryBlockPlayerReady()
    {
        if (session == null || !session.Enabled) return false;
        if (!ShouldShowSetup(session)) return false;
        if (IsCharacterDialogOpen())
        {
            pendingSetupDialog = true;
            return false;
        }

        pendingSetupDialog = false;
        OpenSetupDialog(session);
        SendHelloIfChanged();
        SendStateIfChanged(true);
        return true;
    }

    private void OnSession(VoiceSessionPacket packet)
    {
        if (packet.ProtocolVersion != DiscordProximityVoiceModSystem.ProtocolVersion)
        {
            api.Logger.Warning("[DiscordProximityVoice] server protocol mismatch: {0}", packet.ProtocolVersion);
            return;
        }

        session = packet;

        if (!packet.Enabled)
        {
            backend.Disconnect();
            SendStateIfChanged(true);
            return;
        }

        if (ShouldShowSetup(packet))
        {
            pendingSetupDialog = true;
            SendHelloIfChanged();
            SendStateIfChanged(true);
            return;
        }

        if (VoiceDisabledFor(packet))
        {
            backend.Disconnect();
            SendHelloIfChanged();
            SendStateIfChanged(true);
            return;
        }

        ConnectVoice();
        SendHelloIfChanged();
        SendStateIfChanged(true);
    }

    private bool ShouldShowSetup(VoiceSessionPacket packet)
    {
        if (!packet.RequireSetup) return false;

        string key = SetupKey(packet);
        if (!config.Servers.TryGetValue(key, out DiscordVoiceClientServerSetup setup)) return true;

        return !setup.Completed
            || setup.ApplicationId != packet.DiscordApplicationId
            || setup.SetupVersion < DiscordVoiceClientServerSetup.CurrentSetupVersion;
    }

    private bool VoiceDisabledFor(VoiceSessionPacket packet)
    {
        return config.Servers.TryGetValue(SetupKey(packet), out DiscordVoiceClientServerSetup setup) && setup.Completed && setup.VoiceDisabled;
    }

    private void OpenSetupDialog(VoiceSessionPacket packet)
    {
        if (setupDialog?.IsOpened() == true) return;

        DiscordVoiceClientServerSetup setup = GetSetup(packet);
        SyncPushToTalkSetupFromHotkey(setup);
        backend.ApplySettings(setup);
        ConnectVoice();
        backend.RefreshInputDevices();

        setupDialog?.Dispose();
        setupDialog = new GuiDialogVoiceSetup(api, this, packet, backend, setup);
        setupDialog.TryOpen();
    }

    public void ApplySetupPreview(DiscordVoiceClientServerSetup setup)
    {
        backend.ApplySettings(setup);
    }

    public void ApplyPushToTalkPreview(DiscordVoiceClientServerSetup setup)
    {
        ApplyPushToTalkHotkey(setup);
    }

    public void CompleteSetup(bool enableVoice, DiscordVoiceClientServerSetup selectedSetup)
    {
        if (session == null) return;
        SyncPushToTalkSetupFromHotkey(selectedSetup);

        string key = SetupKey(session);
        DiscordVoiceClientServerSetup setup = selectedSetup?.Clone() ?? new DiscordVoiceClientServerSetup();
        setup.FillDefaults();
        config.Servers[key] = setup;

        setup.ApplicationId = session.DiscordApplicationId;
        setup.SetupVersion = DiscordVoiceClientServerSetup.CurrentSetupVersion;
        setup.Completed = true;
        setup.VoiceDisabled = !enableVoice;
        setup.LastBackendStatus = backend.Status;
        SaveConfig();
        ApplyPushToTalkHotkey(setup);

        setupDialog?.TryClose();
        setupDialog?.Dispose();
        setupDialog = null;

        if (enableVoice)
        {
            backend.ApplySettings(setup);
            ConnectVoice();
        }
        else
        {
            backend.Disconnect();
        }

        SendHelloIfChanged();
        SendStateIfChanged(true);

        if (!api.PlayerReadyFired)
        {
            api.Network.SendPlayerNowReady();
        }
    }

    private void ConnectVoice()
    {
        if (session == null) return;
        backend.Configure(session);
    }

    private void SaveConfig()
    {
        api.StoreModConfig(config, ClientConfigName);
    }

    private static string SetupKey(VoiceSessionPacket packet)
    {
        return string.IsNullOrWhiteSpace(packet.ServerVoiceId) ? "unknown" : packet.ServerVoiceId;
    }

    private void OnProximity(VoiceProximityPacket packet)
    {
        VoicePeerPacket[] peers = packet.Peers ?? Array.Empty<VoicePeerPacket>();
        lastPeerCount = peers.Length;
        HashSet<string> seenPeerTokens = new HashSet<string>();

        foreach (VoicePeerPacket peer in peers)
        {
            if (string.IsNullOrEmpty(peer.VoiceToken)) continue;

            seenPeerTokens.Add(peer.VoiceToken);
            backend.SetPeerVolume(peer);
        }

        foreach (string token in activePeerTokens)
        {
            if (seenPeerTokens.Contains(token)) continue;

            backend.SetPeerVolume(new VoicePeerPacket
            {
                VoiceToken = token,
                Volume = 0f
            });
        }

        activePeerTokens.Clear();
        foreach (string token in seenPeerTokens)
        {
            activePeerTokens.Add(token);
        }
    }

    private void OnTick(float dt)
    {
        backend.Tick(dt);
        TryOpenPendingSetupDialog();
        ApplyPushToTalkState();
        setupDialog?.UpdateInputLevel(backend.InputLevel, backend.Status);
        stateSendSeconds += dt;
        if (session != null && channel?.Connected == true && stateSendSeconds >= 10f)
        {
            stateSendSeconds = 0f;
            SendHelloIfChanged();
            SendStateIfChanged(false);
        }
    }

    private void SendHelloIfChanged()
    {
        string key = backend.DiscordUserId + "|" + backend.Available + "|" + backend.Status;
        if (key == lastHelloKey) return;

        lastHelloKey = key;
        SendHello();
    }

    private void SendStateIfChanged(bool force)
    {
        string key = backend.Linked + "|" + backend.JoinedLobby + "|" + backend.InCall + "|" + backend.Muted + "|" + backend.Deafened + "|" + backend.Status;
        if (!force && key == lastStateKey) return;

        lastStateKey = key;
        SendState();
    }

    private void SendHello()
    {
        if (channel?.Connected != true) return;

        channel.SendPacket(new VoiceHelloPacket
        {
            ProtocolVersion = DiscordProximityVoiceModSystem.ProtocolVersion,
            DiscordUserId = backend.DiscordUserId,
            BackendAvailable = backend.Available,
            BackendStatus = backend.Status
        });
    }

    private DiscordVoiceClientServerSetup GetSetup(VoiceSessionPacket packet)
    {
        DiscordVoiceClientServerSetup setup;
        if (config.Servers.TryGetValue(SetupKey(packet), out DiscordVoiceClientServerSetup existing))
        {
            setup = existing.Clone();
        }
        else
        {
            setup = new DiscordVoiceClientServerSetup();
        }

        setup.ApplicationId = packet.DiscordApplicationId;
        setup.FillDefaults();
        return setup;
    }

    private void ApplyPushToTalkState()
    {
        if (session == null || !session.Enabled) return;

        DiscordVoiceClientServerSetup setup = setupDialog?.CurrentSetup ?? GetSavedSetup(session);
        if (setup == null) return;

        if (setup.TalkMode == VoiceTalkModes.OpenMic)
        {
            backend.SetPushToTalkActive(true);
            return;
        }

        backend.SetPushToTalkActive(IsPushToTalkPressed(setup));
    }

    private DiscordVoiceClientServerSetup GetSavedSetup(VoiceSessionPacket packet)
    {
        if (!config.Servers.TryGetValue(SetupKey(packet), out DiscordVoiceClientServerSetup setup) || setup.VoiceDisabled) return null;

        setup.FillDefaults();
        return setup;
    }

    private bool IsPushToTalkPressed(DiscordVoiceClientServerSetup setup)
    {
        if (api.Input.IsHotKeyPressed(PushToTalkHotkeyCode)) return true;

        return IsKeyDown(setup.PushToTalkKeyCode)
            && (!setup.PushToTalkCtrl || IsKeyDown((int)GlKeys.ControlLeft) || IsKeyDown((int)GlKeys.ControlRight))
            && (!setup.PushToTalkAlt || IsKeyDown((int)GlKeys.AltLeft) || IsKeyDown((int)GlKeys.AltRight))
            && (!setup.PushToTalkShift || IsKeyDown((int)GlKeys.ShiftLeft) || IsKeyDown((int)GlKeys.ShiftRight));
    }

    private bool IsKeyDown(int keyCode)
    {
        return keyCode > 0
            && api.Input.KeyboardKeyStateRaw != null
            && keyCode < api.Input.KeyboardKeyStateRaw.Length
            && api.Input.KeyboardKeyStateRaw[keyCode];
    }

    private void SendState()
    {
        if (channel?.Connected != true) return;

        channel.SendPacket(new VoiceStatePacket
        {
            Linked = backend.Linked,
            JoinedLobby = backend.JoinedLobby,
            InCall = backend.InCall,
            Muted = backend.Muted,
            Deafened = backend.Deafened,
            BackendStatus = backend.Status
        });
    }

    private void OnLeaveWorld()
    {
        setupDialog?.ForceClose();
        setupDialog?.Dispose();
        setupDialog = null;
        session = null;
        lastPeerCount = 0;
        stateSendSeconds = 0f;
        pendingSetupDialog = false;
        lastHelloKey = "";
        lastStateKey = "";
        activePeerTokens.Clear();
        backend.Disconnect();
    }

    private void TryOpenPendingSetupDialog()
    {
        if (!pendingSetupDialog) return;
        if (session == null || !session.Enabled) return;
        if (!ShouldShowSetup(session))
        {
            pendingSetupDialog = false;
            return;
        }

        if (IsCharacterDialogOpen()) return;

        pendingSetupDialog = false;
        OpenSetupDialog(session);
        SendHelloIfChanged();
        SendStateIfChanged(true);
    }

    private bool IsCharacterDialogOpen()
    {
        if (api.Gui?.OpenedGuis == null) return false;

        foreach (GuiDialog dialog in api.Gui.OpenedGuis)
        {
            if (dialog == null || !dialog.IsOpened()) continue;
            if (dialog is GuiDialogCharacterBase) return true;
            if (dialog.GetType().Name.IndexOf("Character", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }

        return false;
    }

    private void ApplyPushToTalkHotkey(DiscordVoiceClientServerSetup setup)
    {
        if (setup == null) return;

        HotKey hotKey = api.Input.GetHotKeyByCode(PushToTalkHotkeyCode);
        if (hotKey == null) return;

        hotKey.CurrentMapping = new KeyCombination
        {
            KeyCode = setup.PushToTalkKeyCode,
            Ctrl = setup.PushToTalkCtrl,
            Alt = setup.PushToTalkAlt,
            Shift = setup.PushToTalkShift
        };
    }

    private void SyncPushToTalkSetupFromHotkey(DiscordVoiceClientServerSetup setup)
    {
        if (setup == null) return;

        HotKey hotKey = api.Input.GetHotKeyByCode(PushToTalkHotkeyCode);
        KeyCombination mapping = hotKey?.CurrentMapping;
        if (mapping == null) return;

        setup.PushToTalkKeyCode = mapping.KeyCode;
        setup.PushToTalkCtrl = mapping.Ctrl;
        setup.PushToTalkAlt = mapping.Alt;
        setup.PushToTalkShift = mapping.Shift;
    }

    public void Dispose()
    {
        if (tickListenerId != 0)
        {
            api.Event.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
        }

        api.Event.LevelFinalize -= OnLevelFinalize;
        api.Event.IsPlayerReady -= OnIsPlayerReady;
        api.Event.LeaveWorld -= OnLeaveWorld;
        setupDialog?.ForceClose();
        setupDialog?.Dispose();
        backend.Dispose();
    }
}
