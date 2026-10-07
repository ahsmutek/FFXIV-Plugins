using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using Shortcuts;

namespace FlickWheel;

public enum ConfirmMode
{
    ShortcutRelease,
    StickRelease,
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

public sealed class ShortcutEntry : IShortcut
{
    public string Uid { get; set; } = Guid.NewGuid().ToString("N");
    public ShortcutKind Kind { get; set; }
    public uint RowId { get; set; }
    public string? Command { get; set; }
    public string? Label { get; set; }
    public uint IconId { get; set; }
}

public sealed class ShortcutGroup
{
    public string Uid { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    // -1 = use Color.
    public int Hue { get; set; } = -1;
    // ABGR.
    public uint Color { get; set; }
    public List<ShortcutEntry> Entries { get; set; } = new();
}

public sealed class ShortcutWheel
{
    public string Uid { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool InCycle { get; set; } = true;
    public List<ShortcutGroup> Groups { get; set; } = new();
    public int PinnedGroup { get; set; }
    public int Style { get; set; }
}

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public float Scale { get; set; } = 1f;
    public float IconSize { get; set; } = 40f;

    // Relative to the screen center.
    public float OffsetX { get; set; } = 0f;
    public float OffsetY { get; set; } = 0f;

    public float Deadzone { get; set; } = 0.25f;
    public float SelectThreshold { get; set; } = 0.55f;

    public ConfirmMode Confirm { get; set; } = ConfirmMode.ShortcutRelease;

    public GamepadButtons SwapButton { get; set; } = GamepadButtons.R1;

    public string LastWheel { get; set; } = "combat";

    public HotkeyModifier Modifier { get; set; } = HotkeyModifier.Ctrl;
    public VirtualKey Key { get; set; } = VirtualKey.OEM_COMMA;

    public Dictionary<string, Hotkey> DirectHotkeys { get; set; } = new();

    public Dictionary<string, bool> Modules { get; set; } = new();

    public Dictionary<string, bool> WheelsOn { get; set; } = new();

    // 1-based; 0 or missing = first matching set.
    public Dictionary<string, int> GearsetOverrides { get; set; } = new();

    public Dictionary<WheelKind, JobGroup> NorthGroup { get; set; } = new();

    public Dictionary<WheelKind, List<JobGroup>> GroupOrder { get; set; } = new();

    public Dictionary<string, bool> ShownLogs { get; set; } = new();

    public Dictionary<JobGroup, List<string>> JobOrder { get; set; } = new();

    // ABGR.
    public Dictionary<JobGroup, uint> RoleColors { get; set; } = new();

    public Dictionary<JobGroup, int> RoleHues { get; set; } = new();
    public int RoleStyle { get; set; }
    public bool UseJobColors { get; set; }

    // Seeded in code, not here, or deserializing would append to the starter groups.
    public ShortcutWheel? Shortcuts { get; set; }

    public List<ShortcutWheel> MoreShortcutWheels { get; set; } = new();

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
