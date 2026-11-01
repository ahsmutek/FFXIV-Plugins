using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace JobProfiles;

internal sealed class BindingWatcher : IDisposable
{
    public static readonly GamepadButtons[] PadButtons =
        Enum.GetValues<GamepadButtons>().Where(b => b != GamepadButtons.None).ToArray();

    private readonly IFramework framework;
    private readonly IGamepadState gamepad;
    private readonly IKeyState keys;
    private readonly IPlayerState player;
    private readonly ICondition condition;
    private readonly IPartyList party;
    private readonly Configuration config;
    private readonly JobGroups groups;
    private readonly Duties duties;
    private readonly MacroRunner runner;
    private readonly FlickWheelLink flickWheel;

    private (Binding Binding, Rule Rule)? holding;
    // Kept apart from the rule, which can be edited while it's held.
    private string? heldWheel;

    private GamepadButtons lastHeld;

    private HashSet<VirtualKey> lastKeys = [];

    private static readonly VirtualKey[] Modifiers = [VirtualKey.CONTROL, VirtualKey.SHIFT, VirtualKey.MENU];

    public BindingWatcher(IFramework framework, IGamepadState gamepad, IKeyState keys, IPlayerState player,
                          ICondition condition, IPartyList party, Configuration config, JobGroups groups,
                          Duties duties, MacroRunner runner, FlickWheelLink flickWheel)
    {
        this.condition = condition;
        this.party = party;
        this.flickWheel = flickWheel;
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

    public bool Paused { get; set; }

    // The settings window has the pad, so its buttons move through the window instead.
    public bool Navigating { get; set; }

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

    public Rule? Match(Binding binding)
    {
        var job = groups.Of(player.ClassJob.RowId);
        var (inDuty, contentType) = duties.Current();
        var inCombat = condition[ConditionFlag.InCombat];
        var inParty = InParty();
        var dutyType = inDuty ? $"t:{contentType}" : null;
        return binding.Rules
                      .Where(rule =>
                          (rule.Job != null ? job?.Abbreviation == rule.Job :
                           rule.Group == null || job is { } j && (j.Group == rule.Group || JobGroups.Parent(j.Group) == rule.Group)) &&
                          (rule.Places.Count == 0 || rule.Places.Any(place => place switch
                          {
                              "o" => !inDuty,
                              "d" => inDuty,
                              "c" => inCombat,
                              "p" => inParty,
                              _ => place == dutyType,
                          })))
                      .OrderByDescending(Specificity)
                      .FirstOrDefault();
    }

    // The party list stays empty for a cross-world party outside instances.
    private unsafe bool InParty()
    {
        if (party.Length > 0)
            return true;
        var crossRealm = InfoProxyCrossRealm.Instance();
        return crossRealm != null && crossRealm->IsInCrossRealmParty;
    }

    private static int Specificity(Rule rule) =>
        (rule.Job != null ? 20 : rule.Group == JobGroup.Combat ? 5 : rule.Group != null ? 10 : 0) +
        (rule.Places.Count == 0 ? 0 : rule.Places.All(p => p.StartsWith("t:")) ? 2 : 1);

    private void OnUpdate(IFramework _)
    {
        if (Paused || Navigating || !config.BindingsEnabled || config.Bindings.Count == 0 || !player.IsLoaded)
        {
            Release();
            lastHeld = HeldButtons();
            lastKeys = KeysDown();
            return;
        }

        // Pad bindings still work while typing, so they can reach /catproof when a cat has opened chat.
        var typing = Typing();
        if (holding is var (held, _) && (!StillHeld(held) || typing && held.HasKeys))
            Release();

        var pressed = Press(typing);
        if (!typing)
            HideHeldKeys();
        if (pressed != null)
            Fire(pressed);
    }

    // Not the game's "just pressed": PadBlocker makes held buttons look newly pressed every frame.
    private Binding? Press(bool typing)
    {
        var held = HeldButtons();
        var pressedButtons = held & ~lastHeld;
        lastHeld = held;

        var keysDown = typing ? new HashSet<VirtualKey>() : KeysDown();
        var pressedKeys = keysDown.Where(k => !lastKeys.Contains(k)).ToHashSet();
        lastKeys = keysDown;

        if (pressedButtons == GamepadButtons.None && pressedKeys.Count == 0)
            return null;

        return config.Bindings
                     .Where(b => b.IsSet && Held(b, held, keysDown) &&
                                 ((b.Buttons & pressedButtons) != 0 || b.Keys.Any(pressedKeys.Contains)))
                     .MaxBy(b => b.Size);
    }

    private static bool Held(Binding binding, GamepadButtons held, HashSet<VirtualKey> keysDown) =>
        (binding.Buttons & ~held) == 0 && binding.Keys.All(keysDown.Contains) &&
        (binding.Buttons == GamepadButtons.None || (held & ~binding.Buttons) == 0) &&
        (!binding.HasKeys || Modifiers.All(m => binding.Keys.Contains(m) || !keysDown.Contains(m)));

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
            case { OpensWheel: true } rule:
                Release();
                flickWheel.Hold(rule.WheelHotkey!, true);
                holding = (binding, rule);
                heldWheel = rule.WheelHotkey;
                break;
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
        if (heldWheel != null)
            flickWheel.Hold(heldWheel, false);
        else
            KeyPresser.Up(rule);
        heldWheel = null;
    }

    private static unsafe bool Typing()
    {
        var atk = RaptureAtkModule.Instance();
        return atk != null && atk->IsTextInputActive();
    }

    public static bool IsDown(VirtualKey key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

    // The key checks read the physical keyboard.
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
