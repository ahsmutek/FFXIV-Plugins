using Dalamud.Configuration;

namespace RetainerCooldown;

public enum VentureDisplayMode
{
    Fastest,
    Latest,
}

public sealed class Configuration : IPluginConfiguration
{
    public static readonly float[] IconSizePresets =
        { 32f, 40f, 48f, 56f, 64f, 72f, 80f, 88f, 96f, 112f, 128f };

    // The game doesn't expose the real start time.
    public static readonly (string Label, int Minutes)[] VentureLengthPresets =
    {
        ("30m", 30),
        ("40m", 40),
        ("50m", 50),
        ("1h", 60),
        ("90m", 90),
        ("2h", 120),
    };

    public const uint DefaultIconId = 70131;
    public const uint DefaultCompleteIconId = 70132;

    public int Version { get; set; } = 1;
    public float IconSize { get; set; } = 64f;
    public uint IconId { get; set; } = DefaultIconId;
    public uint CompleteIconId { get; set; } = DefaultCompleteIconId;
    public int VentureDurationMinutes { get; set; } = 60;
    public bool Locked { get; set; } = false;
    public bool ShowTimerText { get; set; } = true;
    public bool HideWhenNoVentures { get; set; } = false;
    public VentureDisplayMode DisplayMode { get; set; } = VentureDisplayMode.Fastest;

    // Negative = not yet placed.
    public float IconPosX { get; set; } = -1f;
    public float IconPosY { get; set; } = -1f;
}
