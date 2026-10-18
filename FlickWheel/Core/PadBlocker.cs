using System;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace FlickWheel;

// Dalamud's own block (EnableGamepadNav) now always shows its screen-dimming gamepad mode notifier.
internal sealed unsafe class PadBlocker : IDisposable
{
    private const GamepadButtonsFlags Hidden =
        GamepadButtonsFlags.DPadUp | GamepadButtonsFlags.DPadDown | GamepadButtonsFlags.DPadLeft |
        GamepadButtonsFlags.DPadRight | GamepadButtonsFlags.L2 | GamepadButtonsFlags.R2;

    private readonly IPluginLog log;
    private readonly Hook<PadDevice.Delegates.Update> hook;
    private GamepadInputData real;
    private bool hidLast;

    public bool Active { get; set; }

    public PadBlocker(IGameInteropProvider interop, IPluginLog log)
    {
        this.log = log;
        hook = interop.HookFromAddress<PadDevice.Delegates.Update>(
            (nint)PadDevice.StaticVirtualTablePointer->Update, OnUpdate);
        hook.Enable();
    }

    public void Dispose() => hook.Dispose();

    private void OnUpdate(PadDevice* pad)
    {
        // Presses are found by diffing against last frame, so the update starts from the real data.
        if (hidLast)
            pad->GamepadInputData = real;

        // Dalamud's hook runs inside this call, so IGamepadState still sees the real stick.
        hook.Original(pad);

        try
        {
            hidLast = false;
            if (!Active)
                return;

            ref var input = ref pad->GamepadInputData;
            real = input;
            hidLast = true;
            input.LeftStickX = 0;
            input.LeftStickY = 0;
            input.RightStickX = 0;
            input.RightStickY = 0;
            input.Buttons &= ~Hidden;
            input.ButtonsPressed = GamepadButtonsFlags.None;
            input.ButtonsReleased = GamepadButtonsFlags.None;
            input.ButtonsRepeat = GamepadButtonsFlags.None;
        }
        catch (Exception e)
        {
            log.Error(e, "Couldn't hide the controller from the game.");
        }
    }
}
