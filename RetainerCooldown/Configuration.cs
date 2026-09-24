using Dalamud.Configuration;

namespace RetainerCooldown;

// Which venture the single HUD icon should track.
public enum VentureDisplayMode
{
    Fastest, // Soonest to complete
    Latest,  // Last to complete
}

public sealed class Configuration : IPluginConfiguration
{
    // Preset icon sizes matching the game's hotbar slot (48px base) and its
    // common scale steps. Exposed instead of a free slider so the icon always
    // lines up with real FFXIV hotbar sizes.
    public static readonly float[] IconSizePresets =
        { 32f, 40f, 48f, 56f, 64f, 72f, 80f, 88f, 96f, 112f, 128f };

    // Assumed venture durations (minutes) for the radial fill; the game doesn't
    // expose the real start time. 60 = Quick Exploration.
    public static readonly (string Label, int Minutes)[] VentureLengthPresets =
    {
        ("30m", 30),
        ("45m", 45),
        ("1h", 60),
        ("90m", 90),
        ("2h", 120),
    };

    public int Version { get; set; } = 1;
    public float IconSize { get; set; } = 64f;
    // Game icon rendered as the slot face (65049 = Ventures). Configurable.
    public uint IconId { get; set; } = 65049;
    public int VentureDurationMinutes { get; set; } = 60;
    public bool Locked { get; set; } = false;
    public bool ShowTimerText { get; set; } = true;
    public bool HideWhenNoVentures { get; set; } = false;
    public VentureDisplayMode DisplayMode { get; set; } = VentureDisplayMode.Fastest;

    // Persisted HUD icon position (screen coords). Negative = not yet placed.
    public float IconPosX { get; set; } = -1f;
    public float IconPosY { get; set; } = -1f;
}
