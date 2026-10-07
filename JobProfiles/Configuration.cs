using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

// One line of a binding: who and where it applies, and what it does there.
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

    // Act as this key instead of running Steps: held down while the binding's button is,
    // e.g. Flick Wheel's hotkey for a wheel. 0 = run Steps.
    public VirtualKey HoldKey { get; set; }
    public bool HoldCtrl { get; set; }
    public bool HoldShift { get; set; }
    public bool HoldAlt { get; set; }

    public bool ActsAsKey => HoldKey != 0;
    public List<Step> Steps { get; set; } = new();
}

// A button, key or combo and its rules; the most specific rule for your job and where you
// are wins.
public sealed class Binding
{
    public string Uid { get; set; } = Guid.NewGuid().ToString("N");
    // Your name for it, e.g. "Emotes wheel"; empty = shown by its button or key.
    public string Name { get; set; } = string.Empty;
    // Keep the game from seeing these controller buttons while they do something here (for
    // buttons you can't unbind in the game's settings).
    public bool HideFromGame { get; set; } = true;
    // What has to be held: controller buttons and keyboard keys (modifiers included, as
    // CONTROL, SHIFT, MENU), in any mix, e.g. Ctrl + ' + A. Fires when the last one goes down.
    public GamepadButtons Buttons { get; set; }
    public List<VirtualKey> Keys { get; set; } = new();
    public List<Rule> Rules { get; set; } = new();

    // Version 3 had one key with modifier flags; read for Migrate, never written back.
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

    // How many keys and buttons it takes: the bigger combo wins when several are held.
    public int Size => Keys.Count + BitOperations.PopCount((uint)Buttons);
}

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    public bool Enabled { get; set; } = true;

    // Apply the current job's profile once after logging in, not only on changes.
    public bool ApplyOnLogin { get; set; } = true;

    // Your names for the game's HUD layouts 1–4 (the game has none); empty = just the number.
    public string[] HudLayoutNames { get; set; } = ["", "", "", ""];

    // Each role group's profile. Missing = do nothing.
    public Dictionary<JobGroup, Profile> Groups { get; set; } = new();

    // Job abbreviation (English, e.g. "WHM") -> override. Missing = use the group.
    public Dictionary<string, Profile> Jobs { get; set; } = new();

    // Use the buttons and keys set up here.
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

        if (Version < 3)
        {
            Version = 3;
            changed = true;
        }

        return changed;
    }
}
