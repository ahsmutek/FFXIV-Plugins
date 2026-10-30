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
    private RetainerSnapshot[] retainers = [];
    private bool positionDirty;

    public HudWindow(Plugin plugin) : base("Retainer Cooldown HUD###RetainerCooldownHud",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
        ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoNav)
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
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoNav;
        if (plugin.Config.Locked)
            Flags |= ImGuiWindowFlags.NoMove;
    }

    // Dalamud closes focused windows on a controller's B regardless of RespectCloseHotkey; reopen.
    public override void OnClose() => IsOpen = true;

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
                CooldownSweep.Draw(dl, p, size, fractionRemaining);

                if (plugin.Config.ShowTimerText)
                    DrawCenteredText(dl, FormatTime(rem), center, size * 0.30f,
                        0xFFFFFFFF, ImGui.GetColorU32(new Vector4(0, 0, 0, .95f)));
            }
        }

        // Dummy, not InvisibleButton, so clicks don't block dragging.
        ImGui.Dummy(new Vector2(size));
        if (ImGui.IsItemHovered())
        {
            DrawTooltip(retainers);
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

        if (positionDirty && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            plugin.Save();
            positionDirty = false;
        }
    }

    private void DrawFace(ImDrawListPtr dl, Vector2 p, float size, uint iconId)
    {
        var max = p + new Vector2(size);
        var rounding = size * 0.18f;

        if (plugin.TryGetIcon(iconId, out IDalamudTextureWrap? icon) && icon != null)
        {
            var (min, fitMax) = Fit(icon, p, max);
            dl.AddImageRounded(icon.Handle, min, fitMax, Vector2.Zero, Vector2.One, 0xFFFFFFFF, rounding);
        }
        else
            DrawSlot(dl, p, size);
    }

    // Not every game icon is square, so it's centred in the box at its own shape.
    internal static (Vector2 Min, Vector2 Max) Fit(IDalamudTextureWrap icon, Vector2 min, Vector2 max)
    {
        var box = max - min;
        var shape = new Vector2(icon.Width, icon.Height);
        var size = shape * Math.Min(box.X / shape.X, box.Y / shape.Y);
        var at = min + (box - size) / 2;
        return (at, at + size);
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
