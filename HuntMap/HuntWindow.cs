using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace HuntMap;

internal sealed class HuntWindow : Window
{
    private const GamepadButtons Watched =
        GamepadButtons.DpadUp | GamepadButtons.DpadDown | GamepadButtons.DpadLeft | GamepadButtons.DpadRight |
        GamepadButtons.South | GamepadButtons.East | GamepadButtons.North | GamepadButtons.West | GamepadButtons.L1 |
        GamepadButtons.R1;
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan RepeatEvery = TimeSpan.FromMilliseconds(60);
    private static readonly Vector4 Dim = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.15f, 0.1f, 1f);
    private static readonly Vector4 Grey = new(0.55f, 0.55f, 0.55f, 0.9f);
    // Below twice this, the map preview is left out.
    private const float MinListWidth = 260f;

    private readonly Bills bills;
    private readonly Configuration config;
    private readonly Action save;
    private readonly MapMarkers markers;
    private readonly IClientState client;
    private readonly IObjectTable objects;
    private readonly IGameGui gameGui;
    private readonly IGamepadState gamepad;
    private readonly ITextureProvider textures;

    private List<Target> targets = new();
    private uint targetOrder;
    // Spot indices, nearest first when the target was picked.
    private List<int> order = new();
    private int selected;

    private GamepadButtons held;
    private GamepadButtons repeating;
    private DateTime repeatAt;
    private bool scrollToSelection;
    private int mouseRow = -1;

    public DateTime LastDrawn { get; private set; }

    public HuntWindow(Bills bills, Configuration config, Action save, MapMarkers markers, IClientState client,
                      IObjectTable objects, IGameGui gameGui, IGamepadState gamepad, ITextureProvider textures)
        : base("Hunt Map###HuntMap")
    {
        this.bills = bills;
        this.config = config;
        this.save = save;
        this.markers = markers;
        this.client = client;
        this.objects = objects;
        this.gameGui = gameGui;
        this.gamepad = gamepad;
        this.textures = textures;
        Size = new Vector2(760, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnOpen()
    {
        // The press that opened the window shouldn't count.
        held = HeldButtons();
        repeating = GamepadButtons.None;
        targets = bills.Held();
        var here = targets.FirstOrDefault(t => t.TerritoryId == client.TerritoryType && !Done(t))
                   ?? targets.FirstOrDefault(t => !Done(t)) ?? targets.FirstOrDefault();
        Pick(here);
    }

    public override void Draw()
    {
        LastDrawn = DateTime.UtcNow;
        targets = bills.Held();
        if (targets.Count == 0)
        {
            ImGui.TextWrapped("You don't hold an Elite Mark Bill. Take one from a Hunt Board, then open this again.");
            return;
        }
        var target = targets.FirstOrDefault(t => t.Order == targetOrder);
        if (target == null)
        {
            Pick(targets[0]);
            target = targets[0];
        }

        HandlePad(target);

        if (ImGui.BeginTabBar("##targets"))
        {
            foreach (var t in targets)
            {
                var flags = t.Order == targetOrder && scrollToSelection ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                if (!ImGui.BeginTabItem($"{t.Name}###{t.Order}", flags))
                    continue;
                if (t.Order != targetOrder && !scrollToSelection)
                    Pick(t);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        var kills = Bills.Kills(target);
        var checkedCount = Checks(target)?.Count ?? 0;
        ImGui.TextUnformatted(Fit($"{target.Zone} · {(Done(target) ? "done" : $"{kills}/{target.Needed} killed")} · " +
                                  $"{checkedCount}/{target.Spots.Count} spots checked", ImGui.GetContentRegionAvail().X));
        DrawSpots(target);

        ImGui.Separator();
        ImGui.TextColored(Dim, Fit("D-pad move · A show on map · X checked · Y nearest unchecked · L1/R1 target · B close",
                                   ImGui.GetContentRegionAvail().X));
    }

    private void DrawSpots(Target target)
    {
        var footer = ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y * 2;
        var avail = ImGui.GetContentRegionAvail() - new Vector2(0, footer);
        var previewSize = avail.X >= MinListWidth * 2 ? MathF.Min(avail.X / 2, avail.Y) : 0;
        var listWidth = previewSize > 0 ? avail.X - previewSize - ImGui.GetStyle().ItemSpacing.X : 0;
        var checks = Checks(target);
        var player = PlayerCoord(target);

        selected = Math.Clamp(selected, 0, Math.Max(0, order.Count - 1));
        var shown = selected;
        var clicked = -1;
        if (ImGui.BeginChild("##spots", new Vector2(listWidth, avail.Y)))
        {
            var top = ImGui.GetWindowPos().Y;
            var bottom = top + ImGui.GetWindowSize().Y;
            var right = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
            var mouseMoved = ImGui.GetIO().MouseDelta != Vector2.Zero;
            var anyHovered = false;
            for (var row = 0; row < order.Count; row++)
            {
                var spot = target.Spots[order[row]];
                var isChecked = checks?.Contains(spot.Index) == true;
                var start = ImGui.GetCursorPos();
                if (ImGui.Selectable($"##spot{row}", row == selected))
                {
                    selected = row;
                    clicked = row;
                }
                if (ImGui.IsItemHovered())
                {
                    anyHovered = true;
                    if (mouseMoved)
                        mouseRow = row;
                }
                if (row == selected && scrollToSelection)
                {
                    var min = ImGui.GetItemRectMin().Y;
                    var max = ImGui.GetItemRectMax().Y;
                    if (min < top || max > bottom)
                        ImGui.SetScrollHereY(min < top ? 0f : 1f);
                }

                ImGui.SetCursorPos(start);
                var label = $"{(isChecked ? "✓" : "·")}  X {spot.Coord.X:0.0}  Y {spot.Coord.Y:0.0}";
                if (isChecked)
                    ImGui.TextColored(Dim, label);
                else
                    ImGui.TextUnformatted(label);
                if (player is { } at)
                {
                    var distance = $"{Vector2.Distance(at, spot.Coord):0.0} away";
                    ImGui.SameLine(right - ImGui.CalcTextSize(distance).X);
                    ImGui.TextColored(Dim, distance);
                }
            }
            if (!anyHovered)
                mouseRow = -1;
            if (mouseRow >= 0 && mouseRow < order.Count)
                shown = mouseRow;
        }
        ImGui.EndChild();
        scrollToSelection = false;

        if (previewSize > 0 && order.Count > 0)
        {
            ImGui.SameLine();
            DrawMap(target, checks, order[shown], player, previewSize);
        }
        if (clicked >= 0)
            Flag(target, target.Spots[order[clicked]]);
    }

    private void DrawMap(Target target, HashSet<int>? checks, int highlighted, Vector2? player, float size)
    {
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(size));
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddImage(textures.GetFromGame(target.MapTexture).GetWrapOrEmpty().Handle, min, min + new Vector2(size));

        var radius = MathF.Max(3f, size / 90f);
        var white = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));
        foreach (var spot in target.Spots)
        {
            var center = min + spot.Pixel / 2048f * size;
            var isChecked = checks?.Contains(spot.Index) == true;
            drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(isChecked ? Grey : Red));
            if (spot.Index == highlighted)
            {
                drawList.AddCircle(center, radius, white, 0, 2f);
                drawList.AddCircle(center, radius * 2.8f, ImGui.GetColorU32(Red), 0, 2f);
            }
        }

        if (player is { } at)
        {
            var center = min + (at - Vector2.One) * target.Scale / 41f * size;
            drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 1f, 1f)));
            drawList.AddCircle(center, radius, white, 0, 1.5f);
        }
    }

    private void HandlePad(Target target)
    {
        var now = HeldButtons();
        var pressed = now & ~held;
        held = now;

        var vertical = now & (GamepadButtons.DpadUp | GamepadButtons.DpadDown);
        var step = 0;
        if ((pressed & vertical) != 0)
        {
            repeating = pressed & vertical;
            repeatAt = DateTime.UtcNow + RepeatDelay;
            step = repeating.HasFlag(GamepadButtons.DpadUp) ? -1 : 1;
        }
        else if (repeating != GamepadButtons.None && (now & repeating) != 0 && DateTime.UtcNow >= repeatAt)
        {
            repeatAt = DateTime.UtcNow + RepeatEvery;
            step = repeating.HasFlag(GamepadButtons.DpadUp) ? -1 : 1;
        }
        else if ((now & repeating) == 0)
        {
            repeating = GamepadButtons.None;
        }

        if (step != 0 && order.Count > 0)
        {
            selected = Wrap(selected + step, order.Count);
            scrollToSelection = true;
            mouseRow = -1;
        }
        if ((pressed & (GamepadButtons.DpadLeft | GamepadButtons.L1)) != 0)
            SwitchTarget(-1);
        if ((pressed & (GamepadButtons.DpadRight | GamepadButtons.R1)) != 0)
            SwitchTarget(1);
        if (pressed.HasFlag(GamepadButtons.South) && order.Count > 0)
            Flag(target, target.Spots[order[selected]]);
        if (pressed.HasFlag(GamepadButtons.West) && order.Count > 0)
            ToggleChecked(target, order[selected]);
        if (pressed.HasFlag(GamepadButtons.North))
            FlagNearestUnchecked(target);
        if (pressed.HasFlag(GamepadButtons.East))
            IsOpen = false;
    }

    private GamepadButtons HeldButtons()
    {
        var buttons = GamepadButtons.None;
        foreach (var button in Enum.GetValues<GamepadButtons>())
            if ((Watched & button) != 0 && gamepad.Raw(button) > 0)
                buttons |= button;
        return buttons;
    }

    private static int Wrap(int index, int count) => count == 0 ? 0 : (index % count + count) % count;

    private void SwitchTarget(int step)
    {
        if (targets.Count < 2)
            return;
        var index = targets.FindIndex(t => t.Order == targetOrder);
        Pick(targets[Wrap(index + step, targets.Count)]);
    }

    private void Pick(Target? target)
    {
        targetOrder = target?.Order ?? 0;
        selected = 0;
        mouseRow = -1;
        scrollToSelection = true;
        order = target == null ? new() : NearestFirst(target).ToList();
    }

    private IEnumerable<int> NearestFirst(Target target) =>
        PlayerCoord(target) is { } at
            ? target.Spots.OrderBy(s => Vector2.DistanceSquared(at, s.Coord)).Select(s => s.Index)
            : target.Spots.Select(s => s.Index);

    private void FlagNearestUnchecked(Target target)
    {
        var checks = Checks(target);
        var at = PlayerCoord(target);
        var nearest = target.Spots.Where(s => checks?.Contains(s.Index) != true)
                            .OrderBy(s => at is { } p ? Vector2.DistanceSquared(p, s.Coord) : s.Index)
                            .FirstOrDefault();
        if (nearest == null)
            return;
        selected = Math.Max(0, order.IndexOf(nearest.Index));
        scrollToSelection = true;
        mouseRow = -1;
        Flag(target, nearest);
    }

    private void ToggleChecked(Target target, int spot)
    {
        if (!config.Checked.TryGetValue(target.Bill, out var checks) || checks.Order != target.Order)
            config.Checked[target.Bill] = checks = new BillChecks { Order = target.Order };
        if (!checks.Spots.Remove(spot))
            checks.Spots.Add(spot);
        save();
        markers.Redraw();
    }

    private HashSet<int>? Checks(Target target) =>
        config.Checked.TryGetValue(target.Bill, out var checks) && checks.Order == target.Order ? checks.Spots : null;

    private Vector2? PlayerCoord(Target target) =>
        client.TerritoryType == target.TerritoryId && objects.LocalPlayer is { } player
            ? target.CoordOf(new Vector2(player.Position.X, player.Position.Z))
            : null;

    private void Flag(Target target, Spot spot) =>
        gameGui.OpenMapWithMapLink(target.TerritoryId, target.MapId, new Vector3(spot.World.X, 0, spot.World.Y));

    private static bool Done(Target target) => Bills.Kills(target) >= target.Needed;

    private static string Fit(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width)
            return text;
        for (var length = text.Length - 1; length > 0; length--)
        {
            var trimmed = text[..length].TrimEnd() + "…";
            if (ImGui.CalcTextSize(trimmed).X <= width)
                return trimmed;
        }
        return "…";
    }
}
