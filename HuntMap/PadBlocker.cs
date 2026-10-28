using System;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace HuntMap;

// Dalamud's own block (EnableGamepadNav) always shows its screen-dimming gamepad mode notifier.
internal sealed unsafe class PadBlocker : IDisposable
{
    private const GamepadButtonsFlags Hidden =
        GamepadButtonsFlags.DPadUp | GamepadButtonsFlags.DPadDown | GamepadButtonsFlags.DPadLeft |
        GamepadButtonsFlags.DPadRight | GamepadButtonsFlags.Cross | GamepadButtonsFlags.Circle |
        GamepadButtonsFlags.Triangle | GamepadButtonsFlags.Square | GamepadButtonsFlags.L1 | GamepadButtonsFlags.R1;

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

        // Dalamud's hook runs inside this call, so IGamepadState still sees the real buttons.
        hook.Original(pad);

        try
        {
            hidLast = false;
            if (!Active)
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
}
