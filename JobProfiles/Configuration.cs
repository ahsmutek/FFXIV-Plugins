using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using Shortcuts;

namespace JobProfiles;

// One thing to run: anything Flick Wheel's shortcuts can do (a macro, a menu, an action, an
// item…) or a command typed as if in chat (/gs change 3, /wait 2…).
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

// What to do when switching to a job. On a group, null = do nothing; on a job's
// override, null = use the group's choice, and NoHudLayout / an empty step list = do nothing.
public sealed class Profile
{
    public const int NoHudLayout = -1;

    // 0-based HUD layout (the game's Layout 1 is 0).
    public int? HudLayout { get; set; }

    // Run one after another, each once the one before has finished.
    public List<Step>? Steps { get; set; }

    // Older versions, read for Migrate and never written back. Version 1 had a single
    // macro (uint.MaxValue = none on a job override); version 2 a list of macros.
    public uint? MacroId { get; set; }
    public bool ShouldSerializeMacroId() => false;
    public List<uint>? Macros { get; set; }
    public bool ShouldSerializeMacros() => false;
}

// Where a rule applies.
public enum DutyScope
{
    Anywhere,
    OutsideDuty,
    AnyDuty,
    ContentType, // a duty of one ContentType row (Dungeons, Trials, Raids…), see Rule.ContentType
}

// One line of a binding: who and where it applies, and the macros it runs.
public sealed class Rule
{
    public string Uid { get; set; } = Guid.NewGuid().ToString("N");
    // Job abbreviation (English, e.g. "WHM"); when set, Group is ignored.
    public string? Job { get; set; }
    // Role group; both null = anyone.
    public JobGroup? Group { get; set; }
    public DutyScope Where { get; set; }
    // ContentType row when Where is ContentType.
    public uint ContentType { get; set; }
    public List<Step> Steps { get; set; } = new();
}

// A pad button combo or a keyboard key (with modifiers) and the rules it runs. The first rule
// that matches your job and where you are wins.
public sealed class Binding
{
    public string Uid { get; set; } = Guid.NewGuid().ToString("N");
    // Pad buttons that must all be down; fires when the last of them is pressed.
    public GamepadButtons Buttons { get; set; }
    // A keyboard key instead (paddles mapped to keys in reWASD); 0 = none.
    public VirtualKey Key { get; set; }
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
    public List<Rule> Rules { get; set; } = new();

    public bool IsKeyboard => Key != 0;
    public bool IsSet => Key != 0 || Buttons != 0;
}

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    public bool Enabled { get; set; } = true;

    // Apply the current job's profile once after logging in, not only on changes.
    public bool ApplyOnLogin { get; set; } = true;

    // Each role group's profile. Missing = do nothing.
    public Dictionary<JobGroup, Profile> Groups { get; set; } = new();

    // Job abbreviation (English, e.g. "WHM") -> override. Missing = use the group.
    public Dictionary<string, Profile> Jobs { get; set; } = new();

    // Run macros from pad buttons and keys.
    public bool BindingsEnabled { get; set; } = true;
    public List<Binding> Bindings { get; set; } = new();

    /// <summary>Brings settings saved by older versions up to date. True when anything changed.</summary>
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

        if (Version < 3)
        {
            Version = 3;
            changed = true;
        }

        return changed;
    }
}
