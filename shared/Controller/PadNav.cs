using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace Controller;

// Drives a plugin's windows with the controller while one has focus, through ImGui's keyboard navigation:
// Dalamud's gamepad mode does the same with the gamepad flag, which always brings up its screen-dimming notifier.
// D-pad moves, A presses, X types, B backs out or closes; Y, X, L1 and R1 also reach ImGui as gamepad keys,
// for item menus (ContextMenu) and tab switching (PadTabs).
internal sealed unsafe class PadNav : IDisposable
{
    private const GamepadButtonsFlags Hidden =
        GamepadButtonsFlags.DPadUp | GamepadButtonsFlags.DPadDown | GamepadButtonsFlags.DPadLeft |
        GamepadButtonsFlags.DPadRight | GamepadButtonsFlags.Cross | GamepadButtonsFlags.Circle |
        GamepadButtonsFlags.Triangle | GamepadButtonsFlags.Square | GamepadButtonsFlags.L1 | GamepadButtonsFlags.R1;
    private const GamepadButtons Watched =
        GamepadButtons.DpadUp | GamepadButtons.DpadDown | GamepadButtons.DpadLeft | GamepadButtons.DpadRight |
        GamepadButtons.South | GamepadButtons.East | GamepadButtons.North | GamepadButtons.West | GamepadButtons.L1 |
        GamepadButtons.R1;
    // Draw stops in cutscenes and with the UI hidden; the pad goes back to the game then.
    private const long DrawnWithinMs = 200;

    private static readonly (GamepadButtons Button, ImGuiKey Key)[] Keys =
    [
        (GamepadButtons.DpadUp, ImGuiKey.UpArrow),
        (GamepadButtons.DpadDown, ImGuiKey.DownArrow),
        (GamepadButtons.DpadLeft, ImGuiKey.LeftArrow),
        (GamepadButtons.DpadRight, ImGuiKey.RightArrow),
        (GamepadButtons.South, ImGuiKey.Space),
        (GamepadButtons.West, ImGuiKey.Enter),
        (GamepadButtons.West, ImGuiKey.GamepadFaceLeft),
        (GamepadButtons.North, ImGuiKey.GamepadFaceUp),
        (GamepadButtons.L1, ImGuiKey.GamepadL1),
        (GamepadButtons.R1, ImGuiKey.GamepadR1),
    ];

    private readonly IGamepadState gamepad;
    private readonly IPluginLog log;
    private readonly WindowSystem windows;
    private readonly Hook<PadDevice.Delegates.Update> hook;
    private readonly bool[] sent = new bool[Keys.Length];
    private GamepadInputData real;
    private bool hidLast;
    private long updatedAt;
    private GamepadButtons held;
    // Navigation only starts with a pad press, so the mouse and keyboard work as before until then.
    private bool navigating;
    private bool ownsFlag;

    // One of the windows has focus, so the pad is hidden from the game.
    public bool Active { get; private set; }

    // The pad stays hidden from the game but stops driving the window, e.g. while a binding is recorded.
    public bool Paused { get; set; }

    public PadNav(IGameInteropProvider interop, IGamepadState gamepad, IPluginLog log, WindowSystem windows)
    {
        this.gamepad = gamepad;
        this.log = log;
        this.windows = windows;
        hook = interop.HookFromAddress<PadDevice.Delegates.Update>(
            (nint)PadDevice.StaticVirtualTablePointer->Update, OnUpdate);
        hook.Enable();
    }

    // Call before the windows draw, each frame.
    public void Update()
    {
        updatedAt = Environment.TickCount64;
        var io = ImGui.GetIO();
        var gamepadMode = (io.ConfigFlags & ImGuiConfigFlags.NavEnableGamepad) != 0;
        var focused = gamepadMode
                          ? null
                          : windows.Windows.FirstOrDefault(w => w.IsOpen && w.IsFocused &&
                                                                (w.Flags & ImGuiWindowFlags.NoNav) == 0);
        Active = focused != null;

        var now = HeldButtons();
        var pressed = now & ~held;
        held = now;

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            navigating = false;
        else if (Active && !Paused && pressed != GamepadButtons.None)
            navigating = true;
        if (!Active || Paused)
            navigating = false;

        if (!navigating)
        {
            Stop(io);
            return;
        }

        if (!ownsFlag && (io.ConfigFlags & ImGuiConfigFlags.NavEnableKeyboard) == 0)
        {
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            ownsFlag = true;
        }
        for (var i = 0; i < Keys.Length; i++)
            Send(io, i, (now & Keys[i].Button) != 0);

        if (pressed.HasFlag(GamepadButtons.East))
        {
            if (ImGui.IsAnyItemActive() ||
                ImGui.IsPopupOpen(string.Empty, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel))
            {
                io.AddKeyEvent(ImGuiKey.Escape, true);
                io.AddKeyEvent(ImGuiKey.Escape, false);
            }
            else if (focused!.RespectCloseHotkey && !focused.IsPinned)
            {
                focused.IsOpen = false;
            }
            else
            {
                ImGuiP.FocusWindow(default);
            }
        }
    }

    // Opens the item's popup with Y as well as a right-click; use in place of ImGui.BeginPopupContextItem.
    public static bool ContextMenu(string id)
    {
        if (ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.GamepadFaceUp, false))
            ImGui.OpenPopup(id);
        return ImGui.BeginPopupContextItem(id);
    }

    // Whether the pad (here or in Dalamud's gamepad mode) or the keyboard is moving through the window.
    public static bool Navigating =>
        (ImGui.GetIO().ConfigFlags & (ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.NavEnableGamepad)) != 0;

    private void Send(ImGuiIOPtr io, int index, bool down)
    {
        if (sent[index] == down)
            return;
        sent[index] = down;
        io.AddKeyEvent(Keys[index].Key, down);
    }

    private void Stop(ImGuiIOPtr io)
    {
        for (var i = 0; i < Keys.Length; i++)
            Send(io, i, false);
        if (ownsFlag)
            io.ConfigFlags &= ~ImGuiConfigFlags.NavEnableKeyboard;
        ownsFlag = false;
    }

    private GamepadButtons HeldButtons()
    {
        var buttons = GamepadButtons.None;
        foreach (var button in Enum.GetValues<GamepadButtons>())
            if ((Watched & button) != 0 && gamepad.Raw(button) > 0)
                buttons |= button;
        return buttons;
    }

    private void OnUpdate(PadDevice* pad)
    {
        // Presses are found by diffing against last frame, so the update starts from the real data.
        if (hidLast)
            pad->GamepadInputData = real;

        // Dalamud's hook runs inside this call, so IGamepadState still sees the real buttons.
        hook.Original(pad);

        try
        {
            hidLast = false;
            if (!Active || Environment.TickCount64 - updatedAt > DrawnWithinMs)
                return;

            ref var input = ref pad->GamepadInputData;
            real = input;
            hidLast = true;
            input.Buttons &= ~Hidden;
            input.ButtonsPressed &= ~Hidden;
            input.ButtonsReleased &= ~Hidden;
            input.ButtonsRepeat &= ~Hidden;
            input.DPadUp = input.DPadDown = input.DPadLeft = input.DPadRight = 0;
            input.Cross = input.Circle = input.Triangle = input.Square = input.L1 = input.R1 = 0;
        }
        catch (Exception e)
        {
            log.Error(e, "Couldn't hide the controller from the game.");
        }
    }

    public void Dispose()
    {
        hook.Dispose();
        Stop(ImGui.GetIO());
    }
}
