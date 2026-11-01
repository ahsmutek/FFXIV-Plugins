using System;
using System.Collections.Generic;
using System.Linq;
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

    public ShortcutEntry Copy() => new() { Kind = Kind, RowId = RowId, Command = Command, Label = Label, IconId = IconId };
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

    public ShortcutGroup Copy() => new()
    {
        Name = Name, Hue = Hue, Color = Color, Entries = Entries.Select(e => e.Copy()).ToList(),
    };
}

public sealed class ShortcutWheel
{
    public string Uid { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    // Before version 2.
    public bool InCycle { get; set; } = true;
    public List<ShortcutGroup> Groups { get; set; } = new();
    public int PinnedGroup { get; set; }
    public int Style { get; set; }
}

public sealed class WheelCycle
{
    public string Uid { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public HotkeyModifier Modifier { get; set; }
    public VirtualKey Key { get; set; }
    public List<string> Wheels { get; set; } = new();
    public string? LastWheel { get; set; }
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

    public List<WheelCycle> WheelCycles { get; set; } = new();

    // Before version 2; read once to seed WheelCycles.
    public string? LastWheel { get; set; }
    public HotkeyModifier? Modifier { get; set; }
    public VirtualKey? Key { get; set; }
    public Dictionary<string, Hotkey>? DirectHotkeys { get; set; }
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
        if (Version >= 2)
            return false;

        // The shared hotkey opened every turned-on wheel left in the RB rotation.
        bool On(string module, string id) =>
            (!Modules.TryGetValue(module, out var m) || m) && (!WheelsOn.TryGetValue(id, out var w) || w);
        List<string> main = ["combat", "crafting"];
        if (Shortcuts?.InCycle ?? true)
            main.Add("shortcuts");
        main.AddRange(MoreShortcutWheels.Where(w => w.InCycle).Select(w => $"shortcuts-{w.Uid}"));
        main = main.Where(id => On(id is "combat" or "crafting" ? "jobs" : "shortcuts", id)).ToList();
        WheelCycles.Insert(0, new WheelCycle
        {
            Name = "Main",
            Modifier = Modifier ?? HotkeyModifier.Ctrl,
            Key = Key ?? VirtualKey.OEM_COMMA,
            Wheels = main,
            // LastWheel used to be the WheelKind number.
            LastWheel = LastWheel switch { "0" => "combat", "1" => "crafting", _ => LastWheel },
        });

        foreach (var (id, hotkey) in DirectHotkeys ?? [])
        {
            if (hotkey.Key == 0)
                continue;
            var name = id switch
            {
                "combat" => "Combat",
                "crafting" => "Crafting & gathering",
                "shortcuts" => "Shortcuts",
                _ => MoreShortcutWheels.FirstOrDefault(w => $"shortcuts-{w.Uid}" == id)?.Name ?? "Wheel",
            };
            WheelCycles.Add(new WheelCycle { Name = name, Modifier = hotkey.Modifier, Key = hotkey.Key, Wheels = [id] });
        }

        (LastWheel, Modifier, Key, DirectHotkeys) = (null, null, null, null);
        Version = 2;
        return true;
    }
}
