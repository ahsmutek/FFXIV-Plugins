using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;

namespace FlickWheel;

// What switches to the targeted job.
public enum ConfirmMode
{
    ShortcutRelease, // Letting go of the hotkey; the target stays while the stick springs back.
    StickRelease,    // The stick returning to center.
}

public enum HotkeyModifier
{
    None,
    Ctrl,
    Shift,
    Alt,
}

public sealed class Hotkey
{
    public HotkeyModifier Modifier { get; set; }
    public VirtualKey Key { get; set; }
}

public enum ShortcutKind
{
    MainCommand,   // opens a game window, like the main menu does (MainCommand row)
    GeneralAction, // Teleport, Return, Sprint… (GeneralAction row)
    Action,        // an action used from the hotbar, e.g. the Aether Compass (Action row)
    Item,          // uses an item from your bags (Item row)
    Command,       // runs a plugin's chat command, e.g. /cbarding job
}

public sealed class ShortcutEntry
{
    // Stable id so the settings can keep hold of an entry while it's dragged around.
    public string Uid { get; set; } = Guid.NewGuid().ToString("N");
    public ShortcutKind Kind { get; set; }
    // The sheet row for Kind (unused for Command).
    public uint RowId { get; set; }
    public string? Command { get; set; }
    // Shown instead of the game's name when set.
    public string? Label { get; set; }
    // Game icon to show instead of the default one; 0 = default.
    public uint IconId { get; set; }
}

public sealed class ShortcutGroup
{
    public string Uid { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    // Palette hue (see Palette.Hues), shaded by the wheel's style. -1 = the custom Color below.
    public int Hue { get; set; } = -1;
    // Custom band color as ABGR, used when Hue is -1.
    public uint Color { get; set; }
    public List<ShortcutEntry> Entries { get; set; } = new();
}

public sealed class ShortcutWheel
{
    // Clockwise from the top.
    public List<ShortcutGroup> Groups { get; set; } = new();
    // Index of the group centered at the top.
    public int PinnedGroup { get; set; }
    // Palette shade style (see Palette.Styles) for groups that use a palette hue.
    public int Style { get; set; }
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

    public ConfirmMode Confirm { get; set; } = ConfirmMode.ShortcutRelease;

    // Tapped on the pad while the wheel is open to move on to the next wheel.
    public GamepadButtons SwapButton { get; set; } = GamepadButtons.R1;

    // Id of the wheel that was up last time the shared hotkey was used; the next
    // open starts on it. Settings from before modules stored 0 / 1 (see Migrate).
    public string LastWheel { get; set; } = "combat";

    // The shared hotkey that opens the wheel.
    public HotkeyModifier Modifier { get; set; } = HotkeyModifier.Ctrl;
    public VirtualKey Key { get; set; } = VirtualKey.OEM_COMMA;

    // Wheel id -> a hotkey that opens straight to that wheel. Missing = none.
    public Dictionary<string, Hotkey> DirectHotkeys { get; set; } = new();

    // Module id -> turned on. Missing = on.
    public Dictionary<string, bool> Modules { get; set; } = new();

    // Wheel id (e.g. "crafting") -> turned on, within a module that's on. Missing = on.
    public Dictionary<string, bool> WheelsOn { get; set; } = new();

    // Job abbreviation -> 1-based gear set number. Missing or 0 = first matching set.
    public Dictionary<string, int> GearsetOverrides { get; set; } = new();

    // The group centered at the top of each wheel. Missing = tanks / crafters.
    public Dictionary<WheelKind, JobGroup> NorthGroup { get; set; } = new();

    // Clockwise group order on each wheel. Missing = default order.
    public Dictionary<WheelKind, List<JobGroup>> GroupOrder { get; set; } = new();

    // Log entry (e.g. "FISHGUIDE") -> on the crafting wheel. Missing = default (all but the Fish Guide).
    public Dictionary<string, bool> ShownLogs { get; set; } = new();

    // Clockwise job order within each group. Missing = default order.
    public Dictionary<JobGroup, List<string>> JobOrder { get; set; } = new();

    // Custom band color per group as ABGR, picked with the swatch's Custom picker.
    public Dictionary<JobGroup, uint> RoleColors { get; set; } = new();

    // Palette hue per group (see Palette.Hues). Missing = the group's default hue.
    public Dictionary<JobGroup, int> RoleHues { get; set; } = new();
    // Palette shade style (see Palette.Styles) for the job wheels.
    public int RoleStyle { get; set; }
    // Color every group by its role as the game does (tanks blue, healers green, DPS red…),
    // ignoring the per-group picks.
    public bool UseJobColors { get; set; }

    // The Shortcuts wheel. Null until first used, then filled with starter groups (in
    // code rather than here, so loading saved groups doesn't add to the starter ones).
    public ShortcutWheel? Shortcuts { get; set; }

    /// <summary>Brings settings saved by older versions up to date. True when anything changed.</summary>
    public bool Migrate()
    {
        // LastWheel used to be the WheelKind number.
        var lastWheel = LastWheel switch
        {
            "0" => "combat",
            "1" => "crafting",
            _ => LastWheel,
        };
        if (lastWheel == LastWheel)
            return false;

        LastWheel = lastWheel;
        return true;
    }
}
