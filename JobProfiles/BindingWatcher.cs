using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace JobProfiles;

/// <summary>
/// Runs a binding's macros when its pad combo or key is pressed: the first of its rules
/// that matches your job and where you are. When several pad bindings are held at once
/// (R3 and LT+R3), the one with the most buttons wins. Nothing fires while you're typing.
/// </summary>
internal sealed class BindingWatcher : IDisposable
{
    public static readonly GamepadButtons[] PadButtons =
        Enum.GetValues<GamepadButtons>().Where(b => b != GamepadButtons.None).ToArray();

    private readonly IFramework framework;
    private readonly IGamepadState gamepad;
    private readonly IKeyState keys;
    private readonly IPlayerState player;
    private readonly Configuration config;
    private readonly JobGroups groups;
    private readonly Duties duties;
    private readonly MacroRunner runner;

    // A rule acting as a key: the binding whose button holds it down, and the rule.
    private (Binding Binding, Rule Rule)? holding;

    // Keys that were down last frame, to fire once per press.
    private readonly HashSet<VirtualKey> keysDown = [];

    public BindingWatcher(IFramework framework, IGamepadState gamepad, IKeyState keys, IPlayerState player,
                          Configuration config, JobGroups groups, Duties duties, MacroRunner runner)
    {
        this.framework = framework;
        this.gamepad = gamepad;
        this.keys = keys;
        this.player = player;
        this.config = config;
        this.groups = groups;
        this.duties = duties;
        this.runner = runner;
        framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        Release();
    }

    /// <summary>Set while settings are recording a new combo, so pressing it doesn't run it.</summary>
    public bool Paused { get; set; }

    /// <summary>Every pad button held right now.</summary>
    public GamepadButtons HeldButtons()
    {
        var held = GamepadButtons.None;
        foreach (var button in PadButtons)
        {
            if (gamepad.Raw(button) > 0)
                held |= button;
        }

        return held;
    }

    /// <summary>The first rule of the binding that fits your current job and where you are.</summary>
    public Rule? Match(Binding binding)
    {
        var job = groups.Of(player.ClassJob.RowId);
        var (inDuty, contentType) = duties.Current();
        return binding.Rules.FirstOrDefault(rule =>
            (rule.Job != null ? job?.Abbreviation == rule.Job : rule.Group == null || job?.Group == rule.Group) &&
            rule.Where switch
            {
                DutyScope.OutsideDuty => !inDuty,
                DutyScope.AnyDuty => inDuty,
                DutyScope.ContentType => inDuty && contentType == rule.ContentType,
                _ => true,
            });
    }

    private void OnUpdate(IFramework _)
    {
        if (Paused || !config.BindingsEnabled || config.Bindings.Count == 0 || !player.IsLoaded || Typing())
        {
            keysDown.Clear();
            Release();
            return;
        }

        // A held key goes up when its button does.
        if (holding is var (held, _) && !StillHeld(held))
            Release();

        if (PadPress() is { } pad)
            Fire(pad);
        if (GameFocused() && KeyPress() is { } key)
            Fire(key);
    }

    // The pad binding completed this frame: all its buttons held and one just pressed. The
    // one with the most buttons wins, so LT+R3 doesn't also run R3.
    private Binding? PadPress()
    {
        var pressed = PadButtons.Where(b => gamepad.Pressed(b) > 0).Aggregate(GamepadButtons.None, (all, b) => all | b);
        if (pressed == GamepadButtons.None)
            return null;

        var held = HeldButtons() | pressed;
        return config.Bindings
                     .Where(b => !b.IsKeyboard && b.Buttons != 0 && (b.Buttons & pressed) != 0 && (b.Buttons & ~held) == 0)
                     .MaxBy(b => BitOperations.PopCount((uint)b.Buttons));
    }

    // A bound key pressed this frame with exactly its modifiers. The key is hidden from the
    // game while held, so it doesn't also do whatever the game has on it.
    private Binding? KeyPress()
    {
        var ctrl = IsDown(VirtualKey.CONTROL);
        var shift = IsDown(VirtualKey.SHIFT);
        var alt = IsDown(VirtualKey.MENU);
        Binding? fired = null;
        foreach (var binding in config.Bindings.Where(b => b.IsKeyboard))
        {
            var key = binding.Key;
            if (!IsDown(key))
            {
                keysDown.Remove(key);
                continue;
            }

            if (binding.Ctrl != ctrl || binding.Shift != shift || binding.Alt != alt)
                continue;

            if (keys.IsVirtualKeyValid(key))
                keys[key] = false;
            if (keysDown.Add(key))
                fired ??= binding;
        }

        return fired;
    }

    private void Fire(Binding binding)
    {
        switch (Match(binding))
        {
            case { ActsAsKey: true } rule:
                Release();
                KeyPresser.Down(rule);
                holding = (binding, rule);
                break;
            case { Steps.Count: > 0 } rule:
                runner.Enqueue(rule.Steps);
                break;
        }
    }

    private bool StillHeld(Binding binding) =>
        binding.IsKeyboard ? IsDown(binding.Key) : (HeldButtons() & binding.Buttons) == binding.Buttons;

    private void Release()
    {
        if (holding is not var (_, rule))
            return;
        holding = null;
        KeyPresser.Up(rule);
    }

    private static unsafe bool Typing()
    {
        var atk = RaptureAtkModule.Instance();
        return atk != null && atk->IsTextInputActive();
    }

    public static bool IsDown(VirtualKey key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

    // The key checks read the physical keyboard, so make sure the game is the window being typed into.
    private static bool GameFocused()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
