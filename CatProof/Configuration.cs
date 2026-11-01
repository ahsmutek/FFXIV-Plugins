using Dalamud.Configuration;
using Dalamud.Interface;

namespace CatProof;

// The lock state is never saved, so every session starts unlocked.
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // -1 = not placed yet.
    public float IndicatorX { get; set; } = -1;
    public float IndicatorY { get; set; } = -1;

    public bool IconOnly { get; set; }
    public FontAwesomeIcon Icon { get; set; } = FontAwesomeIcon.Paw;
}
