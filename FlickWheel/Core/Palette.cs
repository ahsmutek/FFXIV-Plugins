using System;
using System.Numerics;

namespace FlickWheel;

// Band colors to pick from: a set of hues, each in a few shade styles. A group stores
// its hue, and the wheel's style decides the actual shade, so changing the style
// recolors every group while keeping them apart.
internal static class Palette
{
    // Name and hue in degrees; a negative hue is grey.
    public static readonly (string Name, float Hue)[] Hues =
    [
        ("Red", 0f),
        ("Vermilion", 14f),
        ("Orange", 28f),
        ("Amber", 42f),
        ("Olive", 58f),
        ("Lime", 88f),
        ("Green", 125f),
        ("Emerald", 150f),
        ("Teal", 172f),
        ("Cyan", 190f),
        ("Sky", 206f),
        ("Blue", 222f),
        ("Indigo", 242f),
        ("Violet", 262f),
        ("Purple", 284f),
        ("Magenta", 308f),
        ("Rose", 336f),
        ("Grey", -1f),
    ];

    // Saturation, value and alpha per style. All stay dark enough for the gold glyphs to read on.
    public static readonly (string Name, float Saturation, float Value, float Alpha)[] Styles =
    [
        ("Muted", .68f, .50f, .88f),
        ("Vivid", .78f, .76f, .92f),
        ("Soft", .42f, .64f, .82f),
        ("Dark", .55f, .22f, .88f),
    ];

    public static Vector4 Color(int hue, int style)
    {
        var (_, degrees) = Hues[Math.Clamp(hue, 0, Hues.Length - 1)];
        var (_, saturation, value, alpha) = Styles[Math.Clamp(style, 0, Styles.Length - 1)];
        // Grey keeps the style's brightness but drops the color.
        return degrees < 0
            ? new Vector4(value * .78f, value * .8f, value * .88f, alpha)
            : Hsv(degrees / 360f, saturation, value, alpha);
    }

    public static uint Abgr(Vector4 color) =>
        ((uint)(color.W * 255) << 24) | ((uint)(color.Z * 255) << 16) | ((uint)(color.Y * 255) << 8) | (uint)(color.X * 255);

    public static Vector4 FromAbgr(uint abgr) =>
        new((abgr & 0xFF) / 255f, ((abgr >> 8) & 0xFF) / 255f, ((abgr >> 16) & 0xFF) / 255f, (abgr >> 24) / 255f);

    private static Vector4 Hsv(float h, float s, float v, float a)
    {
        var sector = h * 6f;
        var i = (int)MathF.Floor(sector) % 6;
        var f = sector - MathF.Floor(sector);
        var p = v * (1 - s);
        var q = v * (1 - s * f);
        var t = v * (1 - s * (1 - f));
        var (r, g, b) = i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return new Vector4(r, g, b, a);
    }
}
