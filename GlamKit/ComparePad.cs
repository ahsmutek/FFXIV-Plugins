using System;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace GlamKit;

// While the Compare window's job arrows are shown, L1 and R1 flip jobs and are hidden from the game.
internal sealed unsafe class ComparePad : IDisposable
{
    private const GamepadButtonsFlags Shoulders = GamepadButtonsFlags.L1 | GamepadButtonsFlags.R1;
    // Draw stops in cutscenes and with the UI hidden; the buttons go back to the game then.
    private const long ShownWithinMs = 200;

    private readonly IGamepadState gamepad;
    private readonly IPluginLog log;
    private readonly Hook<PadDevice.Delegates.Update> hook;
    private GamepadInputData real;
    private bool hidLast;
    private long shownAt;

    public ComparePad(IGameInteropProvider interop, IGamepadState gamepad, IPluginLog log)
    {
        this.gamepad = gamepad;
        this.log = log;
        hook = interop.HookFromAddress<PadDevice.Delegates.Update>(
            (nint)PadDevice.StaticVirtualTablePointer->Update, OnUpdate);
        hook.Enable();
    }

    public void Dispose() => hook.Dispose();

    // Call each frame the arrows are shown.
    public int Step()
    {
        shownAt = Environment.TickCount64;
        return gamepad.Pressed(GamepadButtons.R1) > 0 ? 1 : gamepad.Pressed(GamepadButtons.L1) > 0 ? -1 : 0;
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
            if (Environment.TickCount64 - shownAt > ShownWithinMs)
                return;

            ref var input = ref pad->GamepadInputData;
            real = input;
            hidLast = true;
            input.Buttons &= ~Shoulders;
            input.ButtonsPressed &= ~Shoulders;
            input.ButtonsReleased &= ~Shoulders;
            input.ButtonsRepeat &= ~Shoulders;
            input.L1 = input.R1 = 0;
        }
        catch (Exception e)
        {
            log.Error(e, "Couldn't hide L1 and R1 from the game.");
        }
    }
}
