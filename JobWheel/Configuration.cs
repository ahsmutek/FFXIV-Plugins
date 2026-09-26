using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;

namespace JobWheel;

public enum HotkeyModifier
{
    None,
    Ctrl,
    Shift,
    Alt,
}

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Multiplies the whole wheel: ring radius and icons.
    public float Scale { get; set; } = 1f;
    public float IconSize { get; set; } = 40f;

    // Wheel center, relative to the middle of the screen.
    public float OffsetX { get; set; } = 0f;
    public float OffsetY { get; set; } = 0f;

    // Stick tilt (0-1) below which the stick counts as back at center, ending one flick.
    public float Deadzone { get; set; } = 0.25f;
    // Stick tilt (0-1) needed before a job gets targeted.
    public float SelectThreshold { get; set; } = 0.55f;

    // Tapped on the pad while the wheel is open to swap between the combat and crafting wheels.
    public GamepadButtons SwapButton { get; set; } = GamepadButtons.R1;

    // The wheel that was up last time; the next open starts on it.
    public WheelKind LastWheel { get; set; } = WheelKind.Combat;

    public HotkeyModifier Modifier { get; set; } = HotkeyModifier.Ctrl;
    public VirtualKey Key { get; set; } = VirtualKey.OEM_COMMA;

    // Job abbreviation -> 1-based gear set number. Missing or 0 = first matching set.
    public Dictionary<string, int> GearsetOverrides { get; set; } = new();

    // Clockwise job order within each group. Missing = default order.
    public Dictionary<JobGroup, List<string>> JobOrder { get; set; } = new();

    // Band color per group as ABGR. Missing = default color.
    public Dictionary<JobGroup, uint> RoleColors { get; set; } = new();
}
