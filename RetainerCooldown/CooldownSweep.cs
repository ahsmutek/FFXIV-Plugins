using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace RetainerCooldown;

internal static class CooldownSweep
{
    public static void Draw(ImDrawListPtr dl, Vector2 origin, float size, float fraction)
    {
        if (fraction <= 0) return;
        var center = origin + new Vector2(size / 2);
        var col = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, .55f));
        var rounding = size * 0.18f;

        Span<Vector2> clip = stackalloc Vector2[32];
        var clipCount = BuildRoundedRect(origin, size, rounding, 8, clip);

        var start = -MathF.PI / 2f;
        var span = MathF.PI * 2f * fraction;
        var segments = Math.Max(8, (int)MathF.Ceiling(48 * fraction));
        var radius = size;
        Span<Vector2> poly = stackalloc Vector2[40];
        Span<Vector2> next = stackalloc Vector2[40];

        var flags = dl.Flags;
        dl.Flags = flags & ~ImDrawListFlags.AntiAliasedFill;
        for (var i = 0; i < segments; i++)
        {
            var a1 = start + span * i / segments;
            var a2 = start + span * (i + 1) / segments;
            poly[0] = center;
            poly[1] = center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius;
            poly[2] = center + new Vector2(MathF.Cos(a2), MathF.Sin(a2)) * radius;
            var count = ClipPolygon(poly, 3, clip[..clipCount], next);
            if (count >= 3)
                dl.AddConvexPolyFilled(ref poly[0], count, col);
        }
        dl.Flags = flags;
    }

    // Clockwise-on-screen rounded rect. Cross >= 0 is inside with Y growing down.
    private static int BuildRoundedRect(Vector2 origin, float size, float radius, int cornerSegs, Span<Vector2> dst)
    {
        var max = origin + new Vector2(size);
        var n = 0;
        n = AddArc(dst, n, new Vector2(max.X - radius, origin.Y + radius), radius, -MathF.PI / 2f, 0f, cornerSegs);
        n = AddArc(dst, n, new Vector2(max.X - radius, max.Y - radius), radius, 0f, MathF.PI / 2f, cornerSegs);
        n = AddArc(dst, n, new Vector2(origin.X + radius, max.Y - radius), radius, MathF.PI / 2f, MathF.PI, cornerSegs);
        n = AddArc(dst, n, new Vector2(origin.X + radius, origin.Y + radius), radius, MathF.PI, MathF.PI * 1.5f, cornerSegs);
        return n;
    }

    private static int AddArc(Span<Vector2> dst, int n, Vector2 c, float radius, float a0, float a1, int segs)
    {
        for (var i = 0; i < segs; i++)
        {
            var t = a0 + (a1 - a0) * i / segs;
            dst[n++] = c + new Vector2(MathF.Cos(t), MathF.Sin(t)) * radius;
        }
        return n;
    }

    // Sutherland–Hodgman.
    private static int ClipPolygon(Span<Vector2> subject, int count, Span<Vector2> clip, Span<Vector2> scratch)
    {
        for (var e = 0; e < clip.Length; e++)
        {
            if (count == 0) return 0;
            var a = clip[e];
            var b = clip[(e + 1) % clip.Length];
            var outCount = 0;
            var prev = subject[count - 1];
            var prevIn = Inside(prev, a, b);
            for (var i = 0; i < count; i++)
            {
                var cur = subject[i];
                var curIn = Inside(cur, a, b);
                if (curIn)
                {
                    if (!prevIn) scratch[outCount++] = Intersect(prev, cur, a, b);
                    scratch[outCount++] = cur;
                }
                else if (prevIn)
                {
                    scratch[outCount++] = Intersect(prev, cur, a, b);
                }
                prev = cur;
                prevIn = curIn;
            }
            for (var i = 0; i < outCount; i++) subject[i] = scratch[i];
            count = outCount;
        }
        return count;
    }

    private static bool Inside(Vector2 p, Vector2 a, Vector2 b)
    {
        var cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
        return cross >= 0f;
    }

    private static Vector2 Intersect(Vector2 p, Vector2 q, Vector2 a, Vector2 b)
    {
        var r = q - p;
        var s = b - a;
        var denom = r.X * s.Y - r.Y * s.X;
        if (MathF.Abs(denom) < 0.0001f) return q;
        var t = ((a.X - p.X) * s.Y - (a.Y - p.Y) * s.X) / denom;
        return p + r * t;
    }
}
