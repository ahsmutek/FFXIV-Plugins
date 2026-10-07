using System;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace JobProfiles;

internal sealed unsafe class PadBlocker : IDisposable
{
    private const GamepadButtons Shoulders =
        GamepadButtons.L1 | GamepadButtons.L2 | GamepadButtons.R1 | GamepadButtons.R2;

    private readonly Configuration config;
    private readonly BindingWatcher watcher;
    private readonly IPluginLog log;
    private readonly Hook<PadDevice.Delegates.Update> hook;

    public PadBlocker(IGameInteropProvider interop, Configuration config, BindingWatcher watcher, IPluginLog log)
    {
        this.config = config;
        this.watcher = watcher;
        this.log = log;
        hook = interop.HookFromAddress<PadDevice.Delegates.Update>(
            (nint)PadDevice.StaticVirtualTablePointer->Update, OnUpdate);
        hook.Enable();
    }

    public void Dispose() => hook.Dispose();

    private GamepadInputData real;
    private bool hidLast;

    private void OnUpdate(PadDevice* pad)
    {
        // Presses are found by diffing against last frame. Hidden there, a held button would
        // look newly pressed every frame, to Dalamud too (L1 + L3 would flip its gamepad mode
        // each frame), so the update starts from the real data.
        if (hidLast)
            pad->GamepadInputData = real;

        hook.Original(pad);

        try
        {
            hidLast = false;
            if (Hidden() is not (var hidden and not GamepadButtons.None))
                return;

            ref var input = ref pad->GamepadInputData;
            real = input;
            hidLast = true;
            var mask = (GamepadButtonsFlags)(ushort)~(ushort)hidden;
            input.Buttons &= mask;
            input.ButtonsPressed &= mask;
            input.ButtonsReleased &= mask;
            input.ButtonsRepeat &= mask;
            ClearAnalog(ref input, hidden);
        }
        catch (Exception e)
        {
            log.Error(e, "Couldn't hide controller buttons from the game.");
        }
    }

    private GamepadButtons Hidden()
    {
        if (!config.BindingsEnabled || watcher.Paused || config.Bindings.Count == 0)
            return GamepadButtons.None;

        var held = watcher.HeldButtons();
        var hidden = GamepadButtons.None;
        foreach (var binding in config.Bindings)
        {
            if (!binding.HideFromGame || binding.Buttons == GamepadButtons.None)
                continue;

            var single = binding.Size == 1;
            if (single)
            {
                // Hidden even when not pressed, so the game never sees it go down.
                if (watcher.Match(binding) is { } rule && (rule.ActsAsKey || rule.Steps.Count > 0))
                    hidden |= binding.Buttons;
            }
            else if ((held & binding.Buttons) == binding.Buttons && watcher.Match(binding) is not null)
            {
                // Shoulder buttons stay visible; the cross hotbar needs them.
                hidden |= binding.Buttons & ~Shoulders;
            }
        }

        return hidden;
    }

    private static void ClearAnalog(ref GamepadInputData input, GamepadButtons hidden)
    {
        if (hidden.HasFlag(GamepadButtons.West)) input.Square = 0;
        if (hidden.HasFlag(GamepadButtons.South)) input.Cross = 0;
        if (hidden.HasFlag(GamepadButtons.East)) input.Circle = 0;
        if (hidden.HasFlag(GamepadButtons.North)) input.Triangle = 0;
        if (hidden.HasFlag(GamepadButtons.L1)) input.L1 = 0;
        if (hidden.HasFlag(GamepadButtons.R1)) input.R1 = 0;
        if (hidden.HasFlag(GamepadButtons.L2)) input.L2 = 0;
        if (hidden.HasFlag(GamepadButtons.R2)) input.R2 = 0;
        if (hidden.HasFlag(GamepadButtons.Start)) input.Start = 0;
        if (hidden.HasFlag(GamepadButtons.Select)) input.Select = 0;
        if (hidden.HasFlag(GamepadButtons.L3)) input.L3 = 0;
        if (hidden.HasFlag(GamepadButtons.R3)) input.R3 = 0;
        if (hidden.HasFlag(GamepadButtons.DpadUp)) input.DPadUp = 0;
        if (hidden.HasFlag(GamepadButtons.DpadDown)) input.DPadDown = 0;
        if (hidden.HasFlag(GamepadButtons.DpadLeft)) input.DPadLeft = 0;
        if (hidden.HasFlag(GamepadButtons.DpadRight)) input.DPadRight = 0;
    }
}
