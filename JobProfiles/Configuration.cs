using System.Collections.Generic;
using Dalamud.Configuration;

namespace JobProfiles;

// What to do when switching to a job. On a group, null = do nothing; on a job's
// override, null = use the group's choice and the None values below = do nothing.
public sealed class Profile
{
    public const int NoHudLayout = -1;
    public const uint NoMacro = uint.MaxValue;

    // 0-based HUD layout (the game's Layout 1 is 0).
    public int? HudLayout { get; set; }
    // Macro slot: 0–99 individual slot N, 100–199 shared slot N − 100.
    public uint? MacroId { get; set; }
}

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    // Apply the current job's profile once after logging in, not only on changes.
    public bool ApplyOnLogin { get; set; } = true;

    // Each role group's profile. Missing = do nothing.
    public Dictionary<JobGroup, Profile> Groups { get; set; } = new();

    // Job abbreviation (English, e.g. "WHM") -> override. Missing = use the group.
    public Dictionary<string, Profile> Jobs { get; set; } = new();
}
