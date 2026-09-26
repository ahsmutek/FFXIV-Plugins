using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;

namespace JobWheel;

// Hold the hotkey to show the wheel and tilt the right stick toward a job; the
// target stays when the stick springs back. Letting go of the hotkey switches to
// the target if there is one and cancels otherwise, so the wheel can never get
// stuck open. B cancels.
internal sealed class WheelInput : IDisposable
{
    // Game stick axes run -99..99.
    private const float StickRange = 99f;
    // Another icon must be this much closer before the target moves off the current one.
    private static readonly float Hysteresis = 3f * MathF.PI / 180f;
    // Retarget only while the stick is near its furthest tilt this gesture, so the
    // spring back to center can't drift onto a neighbour.
    private const float PeakFraction = 0.85f;
    // Close the wheel if the overlay hasn't drawn it for this long (ms).
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
    private readonly ICondition condition;
    private readonly IPlayerState player;
    private readonly JobCatalog catalog;
    private readonly JobSwitcher switcher;
    private readonly Configuration config;
    private readonly Action save;

    // Hotkey stayed down after the wheel closed; wait for a real release before reopening.
    private bool waitForRelease;
    private bool savedGamepadNav;
    private float gesturePeak;
    private bool swapWasDown;
    private long lastDrawn;

    public bool IsOpen { get; private set; }
    public WheelKind Kind { get; private set; }
    public WheelRing Ring { get; private set; } = null!;
    public IReadOnlyList<WheelJob> Jobs { get; private set; } = [];
    public WheelJob? Target { get; private set; }
    // Stick direction (screen space, unit length) and tilt 0-1.
    public Vector2 StickDirection { get; private set; }
    public float StickTilt { get; private set; }

    public WheelInput(IFramework framework, IKeyState keys, IGamepadState gamepad, ICondition condition,
                      IPlayerState player, JobCatalog catalog, JobSwitcher switcher, Configuration config, Action save)
    {
        this.framework = framework;
        this.keys = keys;
        this.gamepad = gamepad;
        this.condition = condition;
        this.player = player;
        this.catalog = catalog;
        this.switcher = switcher;
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
        var held = HotkeyHeld();

        if (!IsOpen)
        {
            if (!held)
            {
                waitForRelease = false;
                return;
            }

            if (waitForRelease)
                return;

            // One attempt per press, whether or not it opens.
            waitForRelease = true;
            if (GameFocused() && CanOpen())
                Open();
            return;
        }

        // Zoning, cutscenes, or the wheel not being drawn (game UI hidden, gpose):
        // close rather than keep blocking the pad with nothing on screen.
        if (!CanOpen() || Environment.TickCount64 - lastDrawn > NotDrawnTimeout)
        {
            Close();
            return;
        }

        // Keep the key away from the game while the wheel owns it.
        if (keys.IsVirtualKeyValid(config.Key))
            keys[config.Key] = false;

        // B cancels. The wheel stays shut until the shortcut is pressed again.
        if (gamepad.Raw(GamepadButtons.East) > 0)
        {
            Close();
            return;
        }

        ReadStick();

        // Tapping the swap button flips between the combat and crafting wheels.
        var swapDown = gamepad.Raw(config.SwapButton) > 0;
        if (swapDown && !swapWasDown)
        {
            Show(Kind == WheelKind.Combat ? WheelKind.Crafting : WheelKind.Combat);
            Target = null;
            gesturePeak = 0;
        }
        swapWasDown = swapDown;

        if (!held)
        {
            Commit();
            return;
        }

        if (StickTilt < config.Deadzone)
        {
            // Back at center: keep the target so letting go of the shortcut switches to it.
            gesturePeak = 0;
            return;
        }

        gesturePeak = Math.Max(gesturePeak, StickTilt);
        if (StickTilt >= config.SelectThreshold && StickTilt >= gesturePeak * PeakFraction)
            Target = Pick(Target);
    }

    private void Open()
    {
        // Reopen the wheel used last time; fall back to the other if it has no jobs.
        Show(config.LastWheel);
        if (Jobs.Count == 0)
            Show(config.LastWheel == WheelKind.Combat ? WheelKind.Crafting : WheelKind.Combat);
        if (Jobs.Count == 0)
            return;

        // A swap button already held while opening isn't a tap.
        swapWasDown = gamepad.Raw(config.SwapButton) > 0;

        Target = null;
        gesturePeak = 0;
        StickTilt = 0;
        IsOpen = true;
        lastDrawn = Environment.TickCount64;

        // Stops the right stick from also turning the camera. Dalamud still
        // reports the stick values to us before it blanks them for the game.
        // If Dalamud's own gamepad mode is already on, input is already blocked; leave it be.
        savedGamepadNav = gamepad.EnableGamepadNav;
        if (!savedGamepadNav)
            SetPadBlockQuietly(true);
    }

    private void Commit()
    {
        var target = Target;
        Close();
        if (target != null)
            switcher.Switch(target);
    }

    // Dalamud's setter also opens its "gamepad mode" notifier, which dims the screen,
    // whenever ImGui has a gamepad. Hide that flag just for the set so only the input
    // blocking happens.
    private void SetPadBlockQuietly(bool on)
    {
        var io = ImGui.GetIO();
        var flags = io.BackendFlags;
        io.BackendFlags = flags & ~ImGuiBackendFlags.HasGamepad;
        gamepad.EnableGamepadNav = on;
        io.BackendFlags = flags;
    }

    // Called by the overlay each frame it draws the wheel.
    public void MarkDrawn() => lastDrawn = Environment.TickCount64;

    private void Close()
    {
        IsOpen = false;
        Target = null;
        Jobs = [];
        StickTilt = 0;
        if (!savedGamepadNav)
            SetPadBlockQuietly(false);

        // Remember which wheel was up so it opens again next time.
        if (config.LastWheel != Kind)
        {
            config.LastWheel = Kind;
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

    // Nearest shown icon to the stick direction, sticky toward the current target.
    // The job whose slice the stick points into, sticky toward the current target.
    // If that slice is empty (a locked job), the nearest shown icon takes it.
    private WheelJob? Pick(WheelJob? current)
    {
        var angle = MathF.Atan2(StickDirection.X, -StickDirection.Y);

        if (current != null && InSlice(angle, current.Slot, Hysteresis))
            return current;
        foreach (var job in Jobs)
            if (InSlice(angle, job.Slot, 0))
                return job;

        WheelJob? best = null;
        var bestDistance = float.MaxValue;
        foreach (var job in Jobs)
        {
            var distance = JobLayout.AngleDistance(angle, Ring.AngleOf(job.Slot));
            if (distance < bestDistance)
            {
                best = job;
                bestDistance = distance;
            }
        }

        if (current != null && best != null && best != current)
        {
            var currentDistance = JobLayout.AngleDistance(angle, Ring.AngleOf(current.Slot));
            if (currentDistance - bestDistance < Hysteresis)
                return current;
        }

        return best;
    }

    private bool InSlice(float angle, int slot, float margin)
    {
        var halfWidth = (Ring.EndOf(slot) - Ring.StartOf(slot)) / 2 + margin;
        return JobLayout.AngleDistance(angle, Ring.AngleOf(slot)) <= halfWidth;
    }

    // Switch the open wheel to the given kind and load its jobs.
    private void Show(WheelKind kind)
    {
        Kind = kind;
        Ring = JobLayout.Build(kind, config);
        Jobs = catalog.Build(Ring);
    }

    private bool CanOpen() => player.IsLoaded && !condition.Any(ForceClose);

    private bool HotkeyHeld() => IsDown(config.Key) && config.Modifier switch
    {
        HotkeyModifier.Ctrl => IsDown(VirtualKey.CONTROL),
        HotkeyModifier.Shift => IsDown(VirtualKey.SHIFT),
        HotkeyModifier.Alt => IsDown(VirtualKey.MENU),
        _ => true,
    };

    private static bool IsDown(VirtualKey key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

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
