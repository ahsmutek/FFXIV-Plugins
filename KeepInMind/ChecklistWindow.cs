using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Item = Lumina.Excel.Sheets.Item;

namespace KeepInMind;

internal sealed class ChecklistWindow : Window
{
    private const string ItemMenu = "##item";
    private const string ClearAllPopup = "Clear all?##clearAll";
    private static readonly TimeSpan HighlightFor = TimeSpan.FromSeconds(2);

    private readonly Configuration config;
    private readonly Action save;
    private readonly ItemActions actions;
    private readonly IDataManager data;
    private readonly ITextureProvider textures;
    private readonly NativeTooltip tooltip;
    private readonly IGamepadState gamepad;

    private bool leaveGamepadMode;

    private uint highlighted;
    private DateTime highlightUntil;
    private bool scrollToHighlight;

    private bool? menuHasVendors;

    public ChecklistWindow(Configuration config, Action save, ItemActions actions, IDataManager data,
                           ITextureProvider textures, NativeTooltip tooltip, IGamepadState gamepad)
        : base("Keep in Mind###KeepInMind")
    {
        this.gamepad = gamepad;
        this.config = config;
        this.save = save;
        this.actions = actions;
        this.data = data;
        this.textures = textures;
        this.tooltip = tooltip;
        Size = new Vector2(300, 320);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(220, 140) };
    }

    public void Highlight(uint itemId)
    {
        highlighted = itemId;
        highlightUntil = DateTime.UtcNow + HighlightFor;
        scrollToHighlight = true;
    }

    public override void Draw()
    {
        // Once A is let go, so the game doesn't take the held press as its own.
        if (leaveGamepadMode && gamepad.Raw(GamepadButtons.South) == 0)
        {
            gamepad.EnableGamepadNav = false;
            leaveGamepadMode = false;
        }

        var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        using (var list = ImRaii.Child("##list", new Vector2(0, -footer)))
        {
            if (list)
                DrawList();
        }

        ImGui.Separator();
        DrawFooter();
    }

    private void DrawList()
    {
        if (config.Items.Count == 0)
        {
            ImGui.PushTextWrapPos();
            ImGui.TextDisabled("Nothing yet. Right-click an ingredient in the Crafting Log and choose Keep in mind.");
            ImGui.PopTextWrapPos();
            return;
        }

        var iconSize = new Vector2(ImGui.GetFrameHeight());
        KeptItem? remove = null;
        foreach (var kept in config.Items)
        {
            using var id = ImRaii.PushId((int)kept.ItemId);
            var name = ItemName(kept.ItemId);

            if (kept.ItemId == highlighted && DateTime.UtcNow < highlightUntil)
                DrawHighlight(iconSize.Y);

            var done = kept.Done;
            if (ImGui.Checkbox("##done", ref done))
            {
                kept.Done = done;
                save();
            }

            ImGui.SameLine();
            ImGui.Image(textures.GetFromGameIcon(new GameIconLookup(ItemIcon(kept.ItemId))).GetWrapOrEmpty().Handle,
                        iconSize);
            if (ImGui.IsItemHovered())
                tooltip.Hover(kept.ItemId);
            ImGui.SameLine();

            var spacing = ImGui.GetStyle().ItemSpacing.X;
            var countWidth = ImGui.CalcTextSize("9999").X + ImGui.GetStyle().FramePadding.X * 2 +
                             (ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X) * 2;
            var removeWidth = ImGui.GetFrameHeight();
            var nameWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X - countWidth - removeWidth - spacing * 2);
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled), kept.Done))
            {
                if (ImGui.Selectable(name, false, ImGuiSelectableFlags.None, new Vector2(nameWidth, 0)) &&
                    actions.OpenLog(kept.ItemId))
                {
                    leaveGamepadMode = gamepad.EnableGamepadNav;
                }
            }
            if (ImGui.IsItemHovered())
                tooltip.Hover(kept.ItemId);
            // Y is free on a row; X is ImGui's own menu button.
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right) ||
                (ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.GamepadFaceUp, false)))
            {
                menuHasVendors = actions.HasVendors(kept.ItemId);
                ImGui.OpenPopup(ItemMenu);
            }
            if (kept.Done)
                StrikeThrough(name);

            ImGui.SameLine();
            DrawNeeded(kept);

            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
                remove = kept;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove from the list");

            if (ImGui.BeginPopup(ItemMenu))
            {
                if (DrawItemMenu(kept, name))
                    remove = kept;
                ImGui.EndPopup();
            }

            if (scrollToHighlight && kept.ItemId == highlighted)
            {
                ImGui.SetScrollHereY();
                scrollToHighlight = false;
            }
        }

        if (remove != null)
        {
            config.Items.Remove(remove);
            save();
        }
    }

    private void DrawNeeded(KeptItem kept)
    {
        var button = new Vector2(ImGui.GetFrameHeight());
        var inner = ImGui.GetStyle().ItemInnerSpacing.X;
        var needed = kept.Needed;

        ImGui.PushButtonRepeat(true);
        if (ImGui.Button("-", button))
            needed--;
        ImGui.SameLine(0, inner);
        ImGui.SetNextItemWidth(ImGui.CalcTextSize("9999").X + ImGui.GetStyle().FramePadding.X * 2);
        ImGui.InputInt("##needed", ref needed, 0, 0);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"How many you need (you have {Stock.Have(kept.ItemId)})");
        ImGui.SameLine(0, inner);
        if (ImGui.Button("+", button))
            needed++;
        ImGui.PopButtonRepeat();

        needed = Math.Clamp(needed, 1, 9999);
        if (needed != kept.Needed)
        {
            kept.Needed = needed;
            save();
        }
    }

    private bool DrawItemMenu(KeptItem kept, string name)
    {
        ImGui.TextDisabled(name);
        ImGui.Separator();

        var any = false;
        if (!actions.VendorPluginLoaded)
        {
            ImGui.BeginDisabled();
            ImGui.Selectable("Vendor location (Item Vendor Location isn't loaded)");
            ImGui.EndDisabled();
            any = true;
        }
        else if (menuHasVendors == true)
        {
            if (ImGui.Selectable("Vendor location"))
                actions.ShowVendors(kept.ItemId);
            any = true;
        }

        if (!any)
            ImGui.TextDisabled("Not sold by vendors.");

        ImGui.Separator();
        if (ImGui.Selectable(kept.Done ? "Mark as not done" : "Mark as done"))
        {
            kept.Done = !kept.Done;
            save();
        }
        return ImGui.Selectable("Remove from the list");
    }

    private void DrawFooter()
    {
        var anyDone = config.Items.Any(i => i.Done);
        using (ImRaii.Disabled(!anyDone))
        {
            if (ImGui.Button("Clear completed"))
            {
                config.Items.RemoveAll(i => i.Done);
                save();
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(config.Items.Count == 0))
        {
            if (ImGui.Button("Clear all"))
                ImGui.OpenPopup(ClearAllPopup);
        }

        if (ImGui.BeginPopup(ClearAllPopup))
        {
            ImGui.TextUnformatted($"Remove all {config.Items.Count} items from the list?");
            if (ImGui.Button("Clear all"))
            {
                config.Items.Clear();
                save();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private void DrawHighlight(float height)
    {
        var left = (float)(highlightUntil - DateTime.UtcNow).TotalSeconds / (float)HighlightFor.TotalSeconds;
        var color = ImGui.GetStyle().Colors[(int)ImGuiCol.HeaderActive];
        color.W *= Math.Clamp(left, 0f, 1f);
        var start = ImGui.GetCursorScreenPos();
        var end = new Vector2(start.X + ImGui.GetContentRegionAvail().X, start.Y + height);
        ImGui.GetWindowDrawList().AddRectFilled(start, end, ImGui.GetColorU32(color), ImGui.GetStyle().FrameRounding);
    }

    private static void StrikeThrough(string text)
    {
        var min = ImGui.GetItemRectMin();
        var textWidth = Math.Min(ImGui.CalcTextSize(text).X, ImGui.GetItemRectSize().X);
        var y = min.Y + ImGui.GetItemRectSize().Y / 2;
        ImGui.GetWindowDrawList().AddLine(new Vector2(min.X, y), new Vector2(min.X + textWidth, y),
                                          ImGui.GetColorU32(ImGuiCol.TextDisabled), ImGuiHelpers.GlobalScale);
    }

    private string ItemName(uint itemId) =>
        data.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.Name.ExtractText() is { Length: > 0 } name
            ? name
            : $"Item #{itemId}";

    private uint ItemIcon(uint itemId) => data.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.Icon ?? 0;
}
