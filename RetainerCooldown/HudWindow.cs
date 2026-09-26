using System;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace RetainerCooldown;

internal sealed class HudWindow : Window
{
    private readonly Plugin plugin;
    // Read in DrawConditions and reused by Draw the same frame.
    private RetainerSnapshot[] retainers = [];
    // Moved since the last save; written once the drag ends.
    private bool positionDirty;

    public HudWindow(Plugin plugin) : base("Retainer Cooldown HUD###RetainerCooldownHud",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
        ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground)
    {
        this.plugin = plugin;
        IsOpen = true;
        RespectCloseHotkey = false;

        if (plugin.Config.IconPosX >= 0 && plugin.Config.IconPosY >= 0)
        {
            Position = new Vector2(plugin.Config.IconPosX, plugin.Config.IconPosY);
            PositionCondition = ImGuiCond.Once;
        }
    }

    public override void PreDraw()
    {
        // Keep the icon clickable even when locked (only movement is disabled),
        // so clicking it always opens settings.
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground;
        if (plugin.Config.Locked)
            Flags |= ImGuiWindowFlags.NoMove;
    }

    // When hidden, skip the window entirely so no empty window sits over the game.
    public override bool DrawConditions()
    {
        retainers = plugin.ReadRetainers();
        return !plugin.Config.HideWhenNoVentures || retainers.Any(x => x.VentureId != 0);
    }

    public override void Draw()
    {
        var ventures = retainers.Where(x => x.VentureId != 0).ToArray();

        var size = plugin.Config.IconSize;
        var p = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var center = p + new Vector2(size / 2);

        var allComplete = ventures.Length > 0 && ventures.All(x => x.Complete);
        DrawFace(dl, p, size, allComplete ? plugin.Config.CompleteIconId : plugin.Config.IconId);

        if (!allComplete)
        {
            var running = ventures.Where(x => !x.Complete);
            RetainerSnapshot? next = (plugin.Config.DisplayMode == VentureDisplayMode.Latest
                    ? running.OrderByDescending(x => x.Remaining?.TotalSeconds ?? 0)
                    : running.OrderBy(x => x.Remaining?.TotalSeconds ?? double.MaxValue))
                .Cast<RetainerSnapshot?>()
                .FirstOrDefault();

            if (next is { Remaining: { } rem })
            {
                var duration = Math.Max(1, plugin.Config.VentureDurationMinutes * 60.0);
                var fractionRemaining = (float)Math.Clamp(rem.TotalSeconds / duration, 0, 1);
                // Mask the pie to the rounded icon so it neither spills past the
                // face nor gets cut into a square.
                DrawCooldownSweep(dl, p, size, fractionRemaining);

                if (plugin.Config.ShowTimerText)
                    DrawCenteredText(dl, FormatTime(rem), center, size * 0.30f,
                        0xFFFFFFFF, ImGui.GetColorU32(new Vector4(0, 0, 0, .95f)));
            }
        }

        // Dummy (not InvisibleButton) reserves layout space without capturing
        // clicks, so the window stays draggable when unlocked.
        ImGui.Dummy(new Vector2(size));
        if (ImGui.IsItemHovered())
        {
            DrawTooltip(retainers);
            // A release with negligible drag = a click, not a move: open settings.
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) &&
                ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).LengthSquared() < 25f)
                plugin.OpenConfig();
        }

        PersistPosition();
    }

    private void PersistPosition()
    {
        if (plugin.Config.Locked) return;

        var pos = ImGui.GetWindowPos();
        if (Math.Abs(pos.X - plugin.Config.IconPosX) >= 0.5f || Math.Abs(pos.Y - plugin.Config.IconPosY) >= 0.5f)
        {
            plugin.Config.IconPosX = pos.X;
            plugin.Config.IconPosY = pos.Y;
            positionDirty = true;
        }

        // Only write to disk once the drag ends to avoid saving every frame.
        if (positionDirty && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            plugin.Save();
            positionDirty = false;
        }
    }

    // Slot face: the real game icon texture, framed like a hotbar slot.
    // Falls back to a drawn silver plate until the texture finishes loading.
    private void DrawFace(ImDrawListPtr dl, Vector2 p, float size, uint iconId)
    {
        var max = p + new Vector2(size);
        var rounding = size * 0.18f;

        if (plugin.TryGetIcon(iconId, out IDalamudTextureWrap? icon) && icon != null)
            dl.AddImageRounded(icon.Handle, p, max, Vector2.Zero, Vector2.One, 0xFFFFFFFF, rounding);
        else
            DrawSlot(dl, p, size);
    }

    private static void DrawSlot(ImDrawListPtr dl, Vector2 p, float size)
    {
        var max = p + new Vector2(size);
        var mid = p.Y + size * 0.55f;
        var rounding = size * 0.18f;

        dl.AddRectFilled(p, max, ImGui.GetColorU32(new Vector4(.70f, .71f, .75f, .98f)), rounding);
        dl.AddRectFilled(p, new Vector2(max.X, mid),
            ImGui.GetColorU32(new Vector4(.86f, .87f, .91f, .65f)), rounding, ImDrawFlags.RoundCornersTop);
        dl.AddRectFilled(new Vector2(p.X, mid), max,
            ImGui.GetColorU32(new Vector4(.50f, .51f, .56f, .40f)), rounding, ImDrawFlags.RoundCornersBottom);
    }

    // Radial darkening sweep clipped to the rounded icon. The pie is larger than
    // the face; clipping keeps the shadow off the corners and off the frame.
    private static void DrawCooldownSweep(ImDrawListPtr dl, Vector2 origin, float size, float fraction)
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

    // Sutherland–Hodgman. `count` is the live vertex count in `subject`.
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

    // Centered text with a dark outline, like the game's cooldown numbers.
    private static void DrawCenteredText(ImDrawListPtr dl, string text, Vector2 center, float fontSize, uint color, uint outline)
    {
        var font = ImGui.GetFont();
        var scale = fontSize / ImGui.GetFontSize();
        var ts = ImGui.CalcTextSize(text) * scale;
        var pos = center - ts / 2;

        ReadOnlySpan<Vector2> offsets = stackalloc Vector2[]
        {
            new(-1, -1), new(1, -1), new(-1, 1), new(1, 1),
            new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
        };
        foreach (var o in offsets)
            dl.AddText(font, fontSize, pos + o, outline, text);
        dl.AddText(font, fontSize, pos, color, text);
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}" :
        t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m" : $"{Math.Max(0, t.Seconds)}s";

    private static void DrawTooltip(RetainerSnapshot[] retainers)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(.10f, .11f, .14f, .96f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(.85f, .77f, .54f, .85f));

        ImGui.BeginTooltip();
        ImGui.TextColored(new Vector4(1f, .86f, .52f, 1f), "Retainer Ventures");
        ImGui.Separator();

        if (retainers.Length == 0)
        {
            ImGui.TextColored(new Vector4(.7f, .7f, .74f, 1f), "No retainer data yet.");
            ImGui.TextWrapped("Open a summoning bell once this session. The game only sends the retainer list after that.");
        }
        else
        {
            var nameWidth = retainers.Max(r => ImGui.CalcTextSize(r.Name).X);
            foreach (var r in retainers)
            {
                ImGui.TextUnformatted(r.Name);
                ImGui.SameLine(nameWidth + 24f);
                if (r.VentureId == 0)
                    ImGui.TextColored(new Vector4(.6f, .6f, .64f, 1f), "No venture");
                else if (r.Complete)
                    ImGui.TextColored(new Vector4(.45f, .85f, .45f, 1f), "Ready");
                else
                    ImGui.TextColored(new Vector4(1f, .85f, .5f, 1f), FormatTime(r.Remaining ?? TimeSpan.Zero));
            }
        }

        ImGui.EndTooltip();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
    }
}
