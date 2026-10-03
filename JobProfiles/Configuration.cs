using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;

namespace JobProfiles;

// What to do when switching to a job. On a group, null = do nothing; on a job's
// override, null = use the group's choice, and NoHudLayout / an empty macro list = do nothing.
public sealed class Profile
{
    public const int NoHudLayout = -1;

    // 0-based HUD layout (the game's Layout 1 is 0).
    public int? HudLayout { get; set; }

    // Macro slots run one after another, each once the one before has finished:
    // 0–99 individual slot N, 100–199 shared slot N − 100.
    public List<uint>? Macros { get; set; }

    // Version 1 had a single macro; uint.MaxValue meant none on a job override. Read
    // for Migrate, never written back.
    public uint? MacroId { get; set; }
    public bool ShouldSerializeMacroId() => false;
}

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public bool Enabled { get; set; } = true;

    // Apply the current job's profile once after logging in, not only on changes.
    public bool ApplyOnLogin { get; set; } = true;

    // Each role group's profile. Missing = do nothing.
    public Dictionary<JobGroup, Profile> Groups { get; set; } = new();

    // Job abbreviation (English, e.g. "WHM") -> override. Missing = use the group.
    public Dictionary<string, Profile> Jobs { get; set; } = new();

    /// <summary>Brings settings saved by older versions up to date. True when anything changed.</summary>
    public bool Migrate()
    {
        var changed = false;
        foreach (var profile in Groups.Values.Concat(Jobs.Values))
        {
            if (profile.MacroId is not { } id)
                continue;
            profile.Macros = id == uint.MaxValue ? [] : [id];
            profile.MacroId = null;
            changed = true;
        }

        if (Version < 2)
        {
            Version = 2;
            changed = true;
        }

        return changed;
    }
}
