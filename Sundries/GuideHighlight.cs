using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Inventory;

namespace Sundries;

internal sealed unsafe class GuideHighlight(IGameGui gameGui, IObjectTable objects, Configuration config, Action save)
    : IModule
{
    public string Name => "Guide Highlight";

    public string[] Description =>
    [
        "Borders the mount you're riding in the Mount Guide and the minion you have out",
        "in the Minion Guide.",
    ];

    public bool Enabled
    {
        get => config.GuideHighlightEnabled;
        set => config.GuideHighlightEnabled = value;
    }

    public void DrawSettings()
    {
        var color = config.GuideHighlightColor;
        if (Settings.Color("Border colour", ref color, Configuration.DefaultGuideHighlightColor))
        {
            config.GuideHighlightColor = color;
            save();
        }
    }

    public void Draw()
    {
        if (!Enabled || objects.LocalPlayer is not { } player)
            return;
        if (player.CurrentMount is { } mount && mount.ValueNullable is { } mountRow)
            DrawIn("MountNoteBook", mountRow.Icon);
        if (player.CurrentMinion is { } minion && minion.ValueNullable is { } minionRow)
            DrawIn("MinionNoteBook", minionRow.Icon);
    }

    private void DrawIn(string addonName, uint iconId)
    {
        var addon = (AtkUnitBase*)gameGui.GetAddonByName(addonName).Address;
        if (iconId == 0 || addon == null || !addon->IsVisible || addon->Alpha == 0)
            return;

        var slot = Slot(&addon->UldManager, iconId);
        if (slot != null && InventorySlots.Bounds(addon, slot, 0) is { } bounds)
            InventorySlots.DrawBorder(ImGui.GetBackgroundDrawList(), bounds, ImGui.GetColorU32(config.GuideHighlightColor));
    }

    private static AtkComponentDragDrop* Slot(AtkUldManager* uld, uint iconId)
    {
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null || (ushort)node->Type < 1000 || !InventorySlots.Shown(node))
                continue;
            var component = ((AtkComponentNode*)node)->Component;
            if (component == null)
                continue;
            if (component->GetComponentType() == ComponentType.DragDrop)
            {
                var slot = (AtkComponentDragDrop*)component;
                if (InventorySlots.IconOf(slot) == iconId)
                    return slot;
            }
            else if (Slot(&component->UldManager, iconId) is var found && found != null)
            {
                return found;
            }
        }
        return null;
    }
}
