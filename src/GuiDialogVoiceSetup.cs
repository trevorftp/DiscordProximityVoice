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
        ElementBounds statusBounds = ElementBounds.Fixed(0, 34, 640, 92);
        ElementBounds labelBounds = ElementBounds.Fixed(0, 146, 170, 24);
        ElementBounds fieldBounds = ElementBounds.Fixed(188, 140, 376, 30);
        ElementBounds meterBounds = ElementBounds.Fixed(188, 184, 376, 18);
        ElementBounds buttonRow = ElementBounds.Fixed(0, 344, 640, 34);
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
                .AddStaticText("Microphone", CairoFont.WhiteSmallText(), labelBounds)
                .AddDropDown(deviceIds, deviceNames, deviceIndex, OnMicrophoneChanged, fieldBounds, "microphone")
                .AddStaticText("Input Test", CairoFont.WhiteSmallText(), labelBounds.BelowCopy(0, 44))
                .AddInset(meterBounds.ForkBoundingParent(2, 2, 2, 2), 2)
                .AddDynamicCustomDraw(meterBounds, DrawInputLevel, "inputlevel")
                .AddDynamicText("", CairoFont.WhiteSmallText(), fieldBounds.BelowCopy(0, 42), "leveltext")
                .AddStaticText("Talk Mode", CairoFont.WhiteSmallText(), labelBounds.BelowCopy(0, 92))
                .AddDropDown(new[] { VoiceTalkModes.PushToTalk, VoiceTalkModes.OpenMic }, new[] { "Push To Talk", "Open Microphone" }, talkModeIndex, OnTalkModeChanged, fieldBounds.BelowCopy(0, 92), "TalkMode")
                .AddStaticText("Push To Talk Key", CairoFont.WhiteSmallText(), labelBounds.BelowCopy(0, 138))
                .AddSmallButton(KeyDisplay(), OnCapturePushToTalk, fieldBounds.BelowCopy(0, 138).WithFixedWidth(180), EnumButtonStyle.Normal, "pttkey")
                .AddDynamicText("", CairoFont.WhiteSmallText(), fieldBounds.BelowCopy(198, 142).WithFixedWidth(178), "capture")
                .AddSmallButton("Continue With Voice", OnContinueWithVoice, buttonRow.FlatCopy().WithFixedWidth(220).WithAlignment(EnumDialogArea.LeftFixed), EnumButtonStyle.Normal)
                .AddSmallButton("Disable Voice Here", OnDisableVoice, buttonRow.FlatCopy().WithFixedWidth(220).WithAlignment(EnumDialogArea.RightFixed), EnumButtonStyle.Normal)
            .EndChildElements()
            .Compose();

        UpdateCaptureText();
        UpdateLevelText();
    }

    public override void OnKeyDown(KeyEvent args)
    {
        if (TryCapturePushToTalkKey(args)) return;

        base.OnKeyDown(args);
    }

    public override void OnKeyPress(KeyEvent args)
    {
        if (TryCapturePushToTalkKey(args)) return;

        base.OnKeyPress(args);
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

    private bool TryCapturePushToTalkKey(KeyEvent args)
    {
        if (!capturingPushToTalk) return false;

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

        UpdatePushToTalkButton();
        UpdateCaptureText();
        return true;
    }

    private void UpdatePushToTalkButton()
    {
        GuiElementTextButton button = SingleComposer.GetButton("pttkey");
        if (button == null) return;

        button.Text = KeyDisplay();
    }

    private string StatusText(string appText)
    {
        return "This server uses Discord proximity voice.\n" +
            "Discord App ID: " + appText + "\n" +
            "Voice Session: " + session.ServerVoiceId + "\n" +
            "Backend: " + backendStatus;
    }

    private string KeyDisplay()
    {
        KeyCombination keyCombination = new KeyCombination
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
        SingleComposer?.GetDynamicText("capture")?.SetNewText(capturingPushToTalk ? "Press A Key" : "");
    }

    private void UpdateLevelText()
    {
        SingleComposer?.GetDynamicText("leveltext")?.SetNewText(inputLevel > 0.03f ? "Input Detected" : "Listening");
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
