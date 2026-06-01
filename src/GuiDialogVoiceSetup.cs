using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;

namespace DiscordProximityVoice;

internal sealed class GuiDialogVoiceSetup : GuiDialog
{
    private readonly VoiceClient client = null;
    private readonly VoiceSessionPacket session = null;
    private readonly IDiscordVoiceBackend backend = null;
    private readonly DiscordVoiceClientServerSetup setup = null;
    private readonly List<VoiceInputDevice> inputDevices = null;
    private bool allowClose = false;
    private bool capturingPushToTalk = false;
    private float inputLevel = 0f;
    private string backendStatus = "";

    public GuiDialogVoiceSetup(ICoreClientAPI api, VoiceClient client, VoiceSessionPacket session, IDiscordVoiceBackend backend, DiscordVoiceClientServerSetup setup) : base(api)
    {
        this.client = client;
        this.session = session;
        this.backend = backend;
        this.setup = setup.Clone();
        this.setup.FillDefaults();
        backendStatus = backend.Status;
        inputDevices = BuildInputDeviceList(backend.InputDevices, this.setup);
        Compose();
    }

    public DiscordVoiceClientServerSetup CurrentSetup => setup;

    public override string ToggleKeyCombinationCode => null;
    public override double DrawOrder => 2.5;
    public override double InputOrder => 0;
    public override bool DisableMouseGrab => true;
    public override bool PrefersUngrabbedMouse => true;
    public override bool CaptureAllInputs() => true;
    public override bool CaptureRawMouse() => true;

    public override bool TryClose()
    {
        return allowClose && base.TryClose();
    }

    public void ForceClose()
    {
        allowClose = true;
        TryClose();
    }

    private void Compose()
    {
        ElementBounds statusBounds = ElementBounds.Fixed(0, 34, 620, 94);
        ElementBounds labelBounds = ElementBounds.Fixed(0, 144, 160, 24);
        ElementBounds fieldBounds = ElementBounds.Fixed(184, 140, 350, 30);
        ElementBounds meterBounds = ElementBounds.Fixed(184, 186, 350, 18);
        ElementBounds buttonRow = ElementBounds.Fixed(0, 292, 620, 34);
        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(statusBounds, labelBounds, fieldBounds, meterBounds, buttonRow);

        string appText = session.DiscordApplicationId == 0 ? "not configured on server" : session.DiscordApplicationId.ToString();
        string[] deviceIds = inputDevices.Select(device => device.Id).ToArray();
        string[] deviceNames = inputDevices.Select(device => device.Name).ToArray();
        int deviceIndex = Math.Max(0, inputDevices.FindIndex(device => device.Id == setup.MicrophoneDeviceId));
        int talkModeIndex = setup.TalkMode == VoiceTalkModes.OpenMic ? 1 : 0;

        SingleComposer = capi.Gui
            .CreateCompo("dpvoice-setup", ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle))
            .AddShadedDialogBG(bgBounds, true)
            .AddDialogTitleBar("Discord Voice Setup", () => { })
            .BeginChildElements(bgBounds)
                .AddDynamicText(StatusText(appText), CairoFont.WhiteSmallText(), statusBounds, "status")
                .AddStaticText("microphone", CairoFont.WhiteSmallText(), labelBounds)
                .AddDropDown(deviceIds, deviceNames, deviceIndex, OnMicrophoneChanged, fieldBounds, "microphone")
                .AddStaticText("test", CairoFont.WhiteSmallText(), labelBounds.BelowCopy(0, 46))
                .AddInset(meterBounds.ForkBoundingParent(2, 2, 2, 2), 2)
                .AddDynamicCustomDraw(meterBounds, DrawInputLevel, "inputlevel")
                .AddDynamicText("", CairoFont.WhiteSmallText(), fieldBounds.BelowCopy(0, 44), "leveltext")
                .AddStaticText("talk type", CairoFont.WhiteSmallText(), labelBounds.BelowCopy(0, 92))
                .AddDropDown(new[] { VoiceTalkModes.PushToTalk, VoiceTalkModes.OpenMic }, new[] { "Push to talk", "Open microphone" }, talkModeIndex, OnTalkModeChanged, fieldBounds.BelowCopy(0, 92), "TalkMode")
                .AddStaticText("ptt key", CairoFont.WhiteSmallText(), labelBounds.BelowCopy(0, 138))
                .AddSmallButton(KeyDisplay(), OnCapturePushToTalk, fieldBounds.BelowCopy(0, 138).WithFixedWidth(180), EnumButtonStyle.Normal, "pttkey")
                .AddDynamicText("", CairoFont.WhiteSmallText(), fieldBounds.BelowCopy(190, 142).WithFixedWidth(150), "capture")
                .AddSmallButton("Continue with voice", OnContinueWithVoice, buttonRow.FlatCopy().WithFixedWidth(210).WithAlignment(EnumDialogArea.LeftFixed), EnumButtonStyle.Normal)
                .AddSmallButton("Disable voice here", OnDisableVoice, buttonRow.FlatCopy().WithFixedWidth(210).WithAlignment(EnumDialogArea.RightFixed), EnumButtonStyle.Normal)
            .EndChildElements()
            .Compose();

        UpdateCaptureText();
        UpdateLevelText();
    }

    public override void OnKeyDown(KeyEvent args)
    {
        if (capturingPushToTalk)
        {
            args.Handled = true;
            capturingPushToTalk = false;
            if (args.KeyCode != (int)GlKeys.Escape)
            {
                setup.PushToTalkKeyCode = args.KeyCode;
                setup.PushToTalkCtrl = args.CtrlPressed;
                setup.PushToTalkAlt = args.AltPressed;
                setup.PushToTalkShift = args.ShiftPressed;
                setup.FillDefaults();
                client.ApplyPushToTalkPreview(setup);
                client.ApplySetupPreview(setup);
            }

            GuiElementTextButton button = SingleComposer.GetButton("pttkey");
            if (button != null) button.Text = KeyDisplay();
            UpdateCaptureText();
            return;
        }

        base.OnKeyDown(args);
    }

    public void UpdateInputLevel(float level, string status)
    {
        inputLevel = Math.Clamp(level, 0f, 1f);
        backendStatus = status;
        SingleComposer?.GetCustomDraw("inputlevel")?.Redraw();
        SingleComposer?.GetDynamicText("status")?.SetNewText(StatusText(session.DiscordApplicationId == 0 ? "not configured on server" : session.DiscordApplicationId.ToString()));
        UpdateLevelText();
    }

    private bool OnContinueWithVoice()
    {
        allowClose = true;
        client.CompleteSetup(enableVoice: true, setup);
        return true;
    }

    private bool OnDisableVoice()
    {
        allowClose = true;
        client.CompleteSetup(enableVoice: false, setup);
        return true;
    }

    private void OnMicrophoneChanged(string code, bool selected)
    {
        if (!selected) return;

        VoiceInputDevice device = inputDevices.FirstOrDefault(device => device.Id == code) ?? inputDevices[0];
        setup.MicrophoneDeviceId = device.Id;
        setup.MicrophoneDeviceName = device.Name;
        client.ApplySetupPreview(setup);
    }

    private void OnTalkModeChanged(string code, bool selected)
    {
        if (!selected) return;

        setup.TalkMode = code == VoiceTalkModes.OpenMic ? VoiceTalkModes.OpenMic : VoiceTalkModes.PushToTalk;
        client.ApplySetupPreview(setup);
    }

    private bool OnCapturePushToTalk()
    {
        capturingPushToTalk = !capturingPushToTalk;
        UpdateCaptureText();
        return true;
    }

    private string StatusText(string appText)
    {
        return "This server uses Discord proximity voice.\n" +
            "Discord app id: " + appText + "\n" +
            "Voice session: " + session.ServerVoiceId + "\n" +
            "Backend: " + backendStatus;
    }

    private string KeyDisplay()
    {
        HotKey hotKey = capi.Input.GetHotKeyByCode(VoiceClient.PushToTalkHotkeyCode);
        KeyCombination keyCombination = hotKey?.CurrentMapping ?? new KeyCombination
        {
            KeyCode = setup.PushToTalkKeyCode,
            Ctrl = setup.PushToTalkCtrl,
            Alt = setup.PushToTalkAlt,
            Shift = setup.PushToTalkShift
        };

        return keyCombination.ToString();
    }

    private void UpdateCaptureText()
    {
        SingleComposer?.GetDynamicText("capture")?.SetNewText(capturingPushToTalk ? "press a key" : "");
    }

    private void UpdateLevelText()
    {
        SingleComposer?.GetDynamicText("leveltext")?.SetNewText(inputLevel > 0.03f ? "input detected" : "listening");
    }

    private void DrawInputLevel(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        double width = currentBounds.InnerWidth * inputLevel;
        ctx.SetSourceRGBA(0.18, 0.20, 0.22, 0.85);
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Fill();

        ctx.SetSourceRGBA(0.22, 0.74, 0.54, 0.95);
        ctx.Rectangle(0, 0, width, currentBounds.InnerHeight);
        ctx.Fill();
    }

    private static List<VoiceInputDevice> BuildInputDeviceList(IReadOnlyList<VoiceInputDevice> devices, DiscordVoiceClientServerSetup setup)
    {
        List<VoiceInputDevice> list = new List<VoiceInputDevice> { new VoiceInputDevice() };
        if (devices != null)
        {
            foreach (VoiceInputDevice device in devices)
            {
                if (device == null || string.IsNullOrWhiteSpace(device.Name)) continue;
                if (list.Any(existing => existing.Id == device.Id)) continue;

                list.Add(new VoiceInputDevice { Id = device.Id ?? "", Name = device.Name });
            }
        }

        if (!string.IsNullOrWhiteSpace(setup.MicrophoneDeviceId) && list.All(device => device.Id != setup.MicrophoneDeviceId))
        {
            list.Add(new VoiceInputDevice
            {
                Id = setup.MicrophoneDeviceId,
                Name = string.IsNullOrWhiteSpace(setup.MicrophoneDeviceName) ? setup.MicrophoneDeviceId : setup.MicrophoneDeviceName
            });
        }

        return list;
    }
}
