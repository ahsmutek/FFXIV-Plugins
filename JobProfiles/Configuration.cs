using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using Shortcuts;

namespace JobProfiles;

public sealed class Step : IShortcut
{
    public ShortcutKind Kind { get; set; }
    public uint RowId { get; set; }
    public string? Command { get; set; }
    public string? Label { get; set; }
    public uint IconId { get; set; }

    public static Step OfMacro(uint slot) => new() { Kind = ShortcutKind.Macro, RowId = slot };

    public static Step From(PickedShortcut picked) => new()
    {
        Kind = picked.Kind, RowId = picked.RowId, Command = picked.Command, Label = picked.Label, IconId = picked.IconId,
    };

    public Step Copy() => new() { Kind = Kind, RowId = RowId, Command = Command, Label = Label, IconId = IconId };
}

// On a group, null = do nothing; on a job, null = use the group's.
public sealed class Profile
{
    public const int NoHudLayout = -1;

    // 0-based HUD layout (the game's Layout 1 is 0).
    public int? HudLayout { get; set; }

    public List<Step>? Steps { get; set; }

    // Legacy, read by Migrate only.
    public uint? MacroId { get; set; }
    public bool ShouldSerializeMacroId() => false;
    public List<uint>? Macros { get; set; }
    public bool ShouldSerializeMacros() => false;
}

public sealed class Rule
{
    public string Uid { get; set; } = Guid.NewGuid().ToString("N");
    // Creation time, so a page lists its lines in a fixed order whichever button they're on.
    public long Created { get; set; }
    // When set, Group is ignored.
    public string? Job { get; set; }
    // Both null = anyone.
    public JobGroup? Group { get; set; }
    // Any one matching is enough; empty = anywhere. "o" outside duties, "d" any duty, "c" in combat,
    // "p" in a party, "t:5" one duty type.
    public List<string> Places { get; set; } = new();

    // Legacy, read by Migrate only: 1 outside duties, 2 any duty, 3 the duty type in ContentType,
    // 4 combat, duty or party.
    public int Where { get; set; }
    public uint ContentType { get; set; }
    public bool ShouldSerializeWhere() => false;
    public bool ShouldSerializeContentType() => false;

    public VirtualKey HoldKey { get; set; }
    public bool HoldCtrl { get; set; }
    public bool HoldShift { get; set; }
    public bool HoldAlt { get; set; }

    public bool ActsAsKey => HoldKey != 0;

    // A Flick Wheel hotkey's Uid; empty while one is still to be chosen.
    public string? WheelHotkey { get; set; }
    public bool OpensWheel => !string.IsNullOrEmpty(WheelHotkey);
    public List<Step> Steps { get; set; } = new();
}

public sealed class Binding
{
    public string Uid { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool HideFromGame { get; set; } = true;
    // Modifiers are stored as CONTROL, SHIFT, MENU.
    public GamepadButtons Buttons { get; set; }
    public List<VirtualKey> Keys { get; set; } = new();
    public List<Rule> Rules { get; set; } = new();

    // Legacy, read by Migrate only.
    public VirtualKey Key { get; set; }
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
    public bool ShouldSerializeKey() => false;
    public bool ShouldSerializeCtrl() => false;
    public bool ShouldSerializeShift() => false;
    public bool ShouldSerializeAlt() => false;

    public bool HasKeys => Keys.Count > 0;
    public bool IsSet => Keys.Count > 0 || Buttons != 0;

    public int Size => Keys.Count + BitOperations.PopCount((uint)Buttons);
}

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    public bool Enabled { get; set; } = true;

    public bool ApplyOnLogin { get; set; } = true;

    public string[] HudLayoutNames { get; set; } = ["", "", "", ""];

    public Dictionary<JobGroup, Profile> Groups { get; set; } = new();

    public Dictionary<string, Profile> Jobs { get; set; } = new();

    public bool BindingsEnabled { get; set; } = true;
    public List<Binding> Bindings { get; set; } = new();

    public bool Migrate()
    {
        var changed = false;
        foreach (var profile in Groups.Values.Concat(Jobs.Values))
        {
            if (profile.MacroId is { } id)
            {
                profile.Steps = id == uint.MaxValue ? [] : [Step.OfMacro(id)];
                profile.MacroId = null;
                changed = true;
            }

            if (profile.Macros is { } macros)
            {
                profile.Steps = macros.Select(Step.OfMacro).ToList();
                profile.Macros = null;
                changed = true;
            }
        }

        foreach (var binding in Bindings)
        {
            if (binding.Key == 0)
                continue;
            var keys = new List<VirtualKey>();
            if (binding.Ctrl)
                keys.Add(VirtualKey.CONTROL);
            if (binding.Shift)
                keys.Add(VirtualKey.SHIFT);
            if (binding.Alt)
                keys.Add(VirtualKey.MENU);
            keys.Add(binding.Key);
            binding.Keys = keys;
            binding.Key = 0;
            binding.Ctrl = binding.Shift = binding.Alt = false;
            changed = true;
        }

        foreach (var rule in Bindings.SelectMany(b => b.Rules).Where(r => r.Where != 0))
        {
            rule.Places = rule.Where switch
            {
                1 => ["o"],
                2 => ["d"],
                3 => [$"t:{rule.ContentType}"],
                4 => ["d", "c", "p"],
                _ => [],
            };
            rule.Where = 0;
            rule.ContentType = 0;
            changed = true;
        }

        if (Version < 3)
        {
            Version = 3;
            changed = true;
        }

        return changed;
    }
}
