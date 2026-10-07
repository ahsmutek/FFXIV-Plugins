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
/// Runs a button's (or combo's) behaviour when it's pressed: the most specific of its rules
/// for your job and where you are. Nothing fires while you're typing.
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

    // Pad buttons that were down last frame, to fire once per press.
    private GamepadButtons lastHeld;

    // Bound keys that were down last frame, to fire once per press.
    private HashSet<VirtualKey> lastKeys = [];

    private static readonly VirtualKey[] Modifiers = [VirtualKey.CONTROL, VirtualKey.SHIFT, VirtualKey.MENU];

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

    /// <summary>
    /// The rule of the binding that fits your job and where you are, the most specific one
    /// winning: a job's beats its role's, which beats everyone's; within that, one kind of
    /// duty beats any duty or outside duties, which beats anywhere.
    /// </summary>
    public Rule? Match(Binding binding)
    {
        var job = groups.Of(player.ClassJob.RowId);
        var (inDuty, contentType) = duties.Current();
        return binding.Rules
                      .Where(rule =>
                          (rule.Job != null ? job?.Abbreviation == rule.Job : rule.Group == null || job?.Group == rule.Group) &&
                          rule.Where switch
                          {
                              DutyScope.OutsideDuty => !inDuty,
                              DutyScope.AnyDuty => inDuty,
                              DutyScope.ContentType => inDuty && contentType == rule.ContentType,
                              _ => true,
                          })
                      .OrderByDescending(Specificity)
                      .FirstOrDefault();
    }

    private static int Specificity(Rule rule) =>
        (rule.Job != null ? 20 : rule.Group != null ? 10 : 0) +
        rule.Where switch
        {
            DutyScope.ContentType => 2,
            DutyScope.AnyDuty or DutyScope.OutsideDuty => 1,
            _ => 0,
        };

    private void OnUpdate(IFramework _)
    {
        if (Paused || !config.BindingsEnabled || config.Bindings.Count == 0 || !player.IsLoaded || Typing())
        {
            Release();
            lastHeld = HeldButtons();
            lastKeys = KeysDown();
            return;
        }

        // A held key goes up when its button does.
        if (holding is var (held, _) && !StillHeld(held))
            Release();

        var pressed = Press();
        HideHeldKeys();
        if (pressed != null)
            Fire(pressed);
    }

    // The binding completed this frame: all its keys and buttons held, one of them just
    // pressed, and no modifier held that isn't part of it. The biggest combo wins, so
    // Ctrl + ' + A doesn't also run Ctrl + '. Presses are worked out here frame to frame,
    // not from the game's "just pressed": hiding a button from the game (PadBlocker) makes
    // the game see it pressed again every frame it's held.
    private Binding? Press()
    {
        var held = HeldButtons();
        var pressedButtons = held & ~lastHeld;
        lastHeld = held;

        var keysDown = KeysDown();
        var pressedKeys = keysDown.Where(k => !lastKeys.Contains(k)).ToHashSet();
        lastKeys = keysDown;

        if (pressedButtons == GamepadButtons.None && pressedKeys.Count == 0)
            return null;

        return config.Bindings
                     .Where(b => b.IsSet && Held(b, held, keysDown) &&
                                 ((b.Buttons & pressedButtons) != 0 || b.Keys.Any(pressedKeys.Contains)))
                     .MaxBy(b => b.Size);
    }

    // All of the binding's keys and buttons are down, and no modifier it doesn't use.
    private static bool Held(Binding binding, GamepadButtons held, HashSet<VirtualKey> keysDown) =>
        (binding.Buttons & ~held) == 0 && binding.Keys.All(keysDown.Contains) &&
        Modifiers.All(m => binding.Keys.Contains(m) || !keysDown.Contains(m));

    // The bound keys (and modifiers) down right now; none while the game isn't the window
    // being typed into, since the keyboard is read directly.
    private HashSet<VirtualKey> KeysDown()
    {
        var down = new HashSet<VirtualKey>();
        if (!GameFocused())
            return down;
        foreach (var key in config.Bindings.SelectMany(b => b.Keys).Concat(Modifiers).Distinct())
        {
            if (IsDown(key))
                down.Add(key);
        }
        return down;
    }

    // A held combo's keys (not its modifiers) are hidden from the game, so they don't also do
    // whatever the game has on them.
    private void HideHeldKeys()
    {
        var held = HeldButtons();
        foreach (var binding in config.Bindings.Where(b => b.HasKeys && Held(b, held, lastKeys)))
        {
            foreach (var key in binding.Keys.Where(k => !Modifiers.Contains(k)))
            {
                if (keys.IsVirtualKeyValid(key))
                    keys[key] = false;
            }
        }
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
        (HeldButtons() & binding.Buttons) == binding.Buttons && binding.Keys.All(IsDown);

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
