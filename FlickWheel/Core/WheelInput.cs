using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;

namespace FlickWheel;
internal sealed class WheelInput : IDisposable
{
    // Game stick axes run -99..99.
    private const float StickRange = 99f;
    private static readonly float Hysteresis = 3f * MathF.PI / 180f;
    // Retarget only while the stick is near its furthest tilt this gesture, so the
    // spring back to center can't drift onto a neighbour.
    private const float PeakFraction = 0.85f;
    private const long NotDrawnTimeout = 1000;

    private static readonly ConditionFlag[] ForceClose =
    [
        ConditionFlag.BetweenAreas,
        ConditionFlag.BetweenAreas51,
        ConditionFlag.WatchingCutscene,
        ConditionFlag.WatchingCutscene78,
        ConditionFlag.OccupiedInCutSceneEvent,
        ConditionFlag.LoggingOut,
    ];

    private readonly IFramework framework;
    private readonly IKeyState keys;
    private readonly IGamepadState gamepad;
    private readonly PadBlocker blocker;
    private readonly ICondition condition;
    private readonly IPlayerState player;
    private readonly WheelSet wheels;
    private readonly Configuration config;
    private readonly Action save;

    private bool waitForRelease;
    private float gesturePeak;
    private bool swapWasDown;
    private long lastDrawn;

    private HotkeyModifier heldModifier;
    private VirtualKey heldKey;
    private WheelCycle? openedSet;
    private bool openedByIpc;
    private string? ipcHeld;

    // Set while settings record a hotkey.
    public bool Paused { get; set; }
    public bool IsOpen { get; private set; }
    public IWheel? Wheel { get; private set; }
    public WheelRing Ring { get; private set; } = null!;
    public IReadOnlyList<WheelEntry> Entries { get; private set; } = [];
    public WheelEntry? Target { get; private set; }
    public Vector2 StickDirection { get; private set; }
    public float StickTilt { get; private set; }

    public WheelInput(IFramework framework, IKeyState keys, IGamepadState gamepad, PadBlocker blocker,
                      ICondition condition, IPlayerState player, WheelSet wheels, Configuration config, Action save)
    {
        this.framework = framework;
        this.keys = keys;
        this.gamepad = gamepad;
        this.blocker = blocker;
        this.condition = condition;
        this.player = player;
        this.wheels = wheels;
        this.config = config;
        this.save = save;
        framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        if (IsOpen)
            Close();
    }

    private void OnUpdate(IFramework _)
    {
        if (!IsOpen)
        {
            // The just-recorded key is still down when recording ends; wait for its release.
            if (Paused)
            {
                waitForRelease = true;
                return;
            }

            var pressed = PressedShortcut();
            if (pressed == null)
            {
                waitForRelease = false;
                return;
            }

            if (waitForRelease)
                return;

            waitForRelease = true;
            if ((pressed.Value.ByIpc || GameFocused()) && CanOpen())
                Open(pressed.Value.Set, pressed.Value.ByIpc);
            return;
        }

        // Never keep blocking the pad with nothing on screen.
        if (!CanOpen() || Environment.TickCount64 - lastDrawn > NotDrawnTimeout)
        {
            Close();
            return;
        }

        if (!openedByIpc && keys.IsVirtualKeyValid(heldKey))
            keys[heldKey] = false;

        if (gamepad.Raw(GamepadButtons.East) > 0)
        {
            Close();
            return;
        }

        ReadStick();

        var swapDown = gamepad.Raw(config.SwapButton) > 0;
        if (swapDown && !swapWasDown)
            ShowNext();
        swapWasDown = swapDown;

        if (!StillHeld())
        {
            Commit();
            return;
        }

        if (StickTilt < config.Deadzone)
        {
            if (config.Confirm == ConfirmMode.StickRelease && Target != null)
            {
                Commit();
                return;
            }
            gesturePeak = 0;
            return;
        }

        gesturePeak = Math.Max(gesturePeak, StickTilt);
        if (StickTilt >= config.SelectThreshold && StickTilt >= gesturePeak * PeakFraction)
            Target = Pick(Target);
    }

    // Held through IPC, e.g. by a Job Profiles button.
    public void Hold(string uid, bool held)
    {
        if (held)
            ipcHeld = uid;
        else if (ipcHeld == uid)
            ipcHeld = null;
    }

    private (WheelCycle Set, bool ByIpc)? PressedShortcut()
    {
        if (ipcHeld != null && config.WheelCycles.FirstOrDefault(h => h.Uid == ipcHeld) is { } held &&
            wheels.CycleOf(held).Count > 0)
            return (held, true);

        var set = config.WheelCycles.FirstOrDefault(h => h.Key != 0 && IsHeld(h.Modifier, h.Key) &&
                                                         wheels.CycleOf(h).Count > 0);
        return set != null ? (set, false) : null;
    }

    private bool StillHeld() => openedByIpc ? ipcHeld == openedSet?.Uid : IsHeld(heldModifier, heldKey);

    private void Open(WheelCycle set, bool byIpc)
    {
        openedSet = set;
        openedByIpc = byIpc;
        var all = wheels.CycleOf(set);
        var start = Math.Max(0, all.FindIndex(w => w.Id == set.LastWheel));
        for (var i = 0; i < all.Count; i++)
        {
            Show(all[(start + i) % all.Count]);
            if (Entries.Count > 0)
                break;
        }

        if (Entries.Count == 0)
            return;

        heldModifier = set.Modifier;
        heldKey = set.Key;

        // A swap button already held while opening isn't a tap.
        swapWasDown = gamepad.Raw(config.SwapButton) > 0;

        Target = null;
        gesturePeak = 0;
        StickTilt = 0;
        IsOpen = true;
        lastDrawn = Environment.TickCount64;

        // Stops the right stick turning the camera.
        blocker.Active = true;
    }

    private void ShowNext()
    {
        var all = wheels.CycleOf(openedSet!);
        var index = all.FindIndex(w => w.Id == Wheel?.Id);
        var (wheel, ring, entries) = (Wheel, Ring, Entries);
        var steps = index < 0 ? all.Count : all.Count - 1;
        for (var i = 1; i <= steps; i++)
        {
            Show(all[(index + i) % all.Count]);
            if (Entries.Count > 0)
            {
                Target = null;
                gesturePeak = 0;
                return;
            }
        }

        (Wheel, Ring, Entries) = (wheel, ring, entries);
    }

    private void Commit()
    {
        var target = Target;
        Close();
        target?.Run();
    }

    public void MarkDrawn() => lastDrawn = Environment.TickCount64;

    private void Close()
    {
        IsOpen = false;
        Target = null;
        Entries = [];
        StickTilt = 0;
        blocker.Active = false;

        if (openedSet != null && Wheel != null && openedSet.LastWheel != Wheel.Id)
        {
            openedSet.LastWheel = Wheel.Id;
            save();
        }
    }

    private void ReadStick()
    {
        var raw = gamepad.RightStick;
        // Screen Y grows down; the stick's Y is positive when pushed up.
        var v = new Vector2(raw.X, -raw.Y) / StickRange;
        var length = v.Length();
        StickTilt = Math.Clamp(length, 0f, 1f);
        StickDirection = length > 0.0001f ? v / length : Vector2.Zero;
    }

    private WheelEntry? Pick(WheelEntry? current)
    {
        var angle = MathF.Atan2(StickDirection.X, -StickDirection.Y);

        if (current != null && InSlice(angle, current.Slot, Hysteresis))
            return current;
        foreach (var entry in Entries)
            if (InSlice(angle, entry.Slot, 0))
                return entry;

        WheelEntry? best = null;
        var bestDistance = float.MaxValue;
        foreach (var entry in Entries)
        {
            var distance = WheelRing.AngleDistance(angle, Ring.AngleOf(entry.Slot));
            if (distance < bestDistance)
            {
                best = entry;
                bestDistance = distance;
            }
        }

        if (current != null && best != null && best != current)
        {
            var currentDistance = WheelRing.AngleDistance(angle, Ring.AngleOf(current.Slot));
            if (currentDistance - bestDistance < Hysteresis)
                return current;
        }

        return best;
    }

    private bool InSlice(float angle, int slot, float margin)
    {
        var halfWidth = (Ring.EndOf(slot) - Ring.StartOf(slot)) / 2 + margin;
        return WheelRing.AngleDistance(angle, Ring.AngleOf(slot)) <= halfWidth;
    }

    private void Show(IWheel wheel)
    {
        Wheel = wheel;
        Ring = wheel.Layout();
        Entries = wheel.Entries(Ring);
    }

    private bool CanOpen() => player.IsLoaded && !condition.Any(ForceClose);

    private static bool IsHeld(HotkeyModifier modifier, VirtualKey key) => IsDown(key) && modifier switch
    {
        HotkeyModifier.Ctrl => IsDown(VirtualKey.CONTROL),
        HotkeyModifier.Shift => IsDown(VirtualKey.SHIFT),
        HotkeyModifier.Alt => IsDown(VirtualKey.MENU),
        _ => true,
    };

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
