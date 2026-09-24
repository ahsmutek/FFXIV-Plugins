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

    public HudWindow(Plugin plugin) : base("Retainer Cooldown HUD###RetainerCooldownHud",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
        ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground)
    {
        this.plugin = plugin;
        IsOpen = true;
        RespectCloseHotkey = false;

        // Restore the saved position (once) so the icon stays where the user left it.
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

    public override void Draw()
    {
        var retainers = plugin.ReadRetainers();
        var ventures = retainers.Where(x => x.VentureId != 0).ToArray();
        if (ventures.Length == 0 && plugin.Config.HideWhenNoVentures) return;

        var size = plugin.Config.IconSize;
        var p = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var center = p + new Vector2(size / 2);

        DrawFace(dl, p, size);

        var allComplete = ventures.Length > 0 && ventures.All(x => x.Complete);
        if (allComplete)
        {
            // Every venture is done: green outline instead of a timer.
            DrawReadyOutline(dl, p, size);
        }
        else
        {
            // Track the next still-running venture (fastest or latest).
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
                // Authentic FFXIV cooldown: darken the remaining sweep; the icon
                // brightens as it completes.
                DrawCooldownSweep(dl, center, size * 0.72f, fractionRemaining);

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

    // Save the icon's position after the user finishes dragging it.
    private void PersistPosition()
    {
        if (plugin.Config.Locked) return;

        var pos = ImGui.GetWindowPos();
        if (Math.Abs(pos.X - plugin.Config.IconPosX) < 0.5f && Math.Abs(pos.Y - plugin.Config.IconPosY) < 0.5f)
            return;

        plugin.Config.IconPosX = pos.X;
        plugin.Config.IconPosY = pos.Y;
        // Only write to disk once the drag ends to avoid saving every frame.
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            plugin.Save();
    }

    // Slot face: the real game icon texture, framed like a hotbar slot.
    // Falls back to a drawn silver plate until the texture finishes loading.
    private void DrawFace(ImDrawListPtr dl, Vector2 p, float size)
    {
        var max = p + new Vector2(size);
        var rounding = size * 0.18f;

        if (plugin.TryGetIcon(plugin.Config.IconId, out IDalamudTextureWrap? icon) && icon != null)
            dl.AddImageRounded(icon.Handle, p, max, Vector2.Zero, Vector2.One, 0xFFFFFFFF, rounding);
        else
            DrawSlot(dl, p, size);

        // Dark outer edge + light inner bevel to match FFXIV slots.
        dl.AddRect(p, max, ImGui.GetColorU32(new Vector4(.10f, .10f, .12f, .90f)), rounding, ImDrawFlags.None, 2f);
        dl.AddRect(p + Vector2.One, max - Vector2.One,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, .25f)), rounding - 1, ImDrawFlags.None, 1f);
    }

    // Light silver metallic slot (fallback when the game icon isn't ready yet).
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

    // Pulsing green outline shown when every venture is complete.
    private static void DrawReadyOutline(ImDrawListPtr dl, Vector2 p, float size)
    {
        var rounding = size * 0.18f;
        var pulse = .70f + .30f * (float)((Math.Sin(ImGui.GetTime() * 3) + 1) / 2);
        var col = ImGui.GetColorU32(new Vector4(.25f, 1f, .40f, pulse));
        dl.AddRect(p - Vector2.One, p + new Vector2(size) + Vector2.One, col, rounding,
            ImDrawFlags.None, MathF.Max(2.5f, size * 0.06f));
    }

    // Radial darkening sweep (FFXIV cooldown look): shades the remaining fraction
    // clockwise from the top; shrinks as the venture nears completion.
    private static void DrawCooldownSweep(ImDrawListPtr dl, Vector2 center, float radius, float fraction)
    {
        if (fraction <= 0) return;
        var col = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, .70f));
        var start = -MathF.PI / 2;
        var span = MathF.PI * 2 * fraction;
        var segments = Math.Max(3, (int)MathF.Ceiling(64 * fraction));
        for (var i = 0; i < segments; i++)
        {
            var a1 = start + span * i / segments;
            var a2 = start + span * (i + 1) / segments;
            dl.AddTriangleFilled(center,
                center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius,
                center + new Vector2(MathF.Cos(a2), MathF.Sin(a2)) * radius, col);
        }
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
            ImGui.TextColored(new Vector4(.7f, .7f, .74f, 1f), "No retainer data.");
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
