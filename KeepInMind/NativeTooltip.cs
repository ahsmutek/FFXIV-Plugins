using System;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace KeepInMind;

// The game only anchors tooltips to its own nodes, so this drives ItemDetail directly, as Chat 2 does.
internal sealed unsafe class NativeTooltip : IDisposable
{
    private uint hoveredThisFrame;
    private uint shown;

    public void Hover(uint itemId) => hoveredThisFrame = itemId;

    public void EndFrame()
    {
        var hovered = hoveredThisFrame;
        hoveredThisFrame = 0;
        if (hovered == shown)
            return;

        if (shown != 0)
            Close();
        if (hovered != 0)
            Open(hovered);
        shown = hovered;
    }

    public void Dispose()
    {
        if (shown != 0)
            Close();
        shown = 0;
    }

    private static void Open(uint itemId)
    {
        var agent = AgentItemDetail.Instance();
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("ItemDetail");
        if (agent == null || addon == null)
            return;

        agent->DetailKind = DetailKind.Item;
        agent->TypeOrId = itemId;
        agent->Index = 0;
        agent->Flag1 &= 0xEF;
        agent->ItemId = itemId;
        agent->Flag2 = 1; // needed for the tooltip to show
        agent->Flag3 = 0; // skips an early return in Show
        agent->AddonId = addon->Id;

        AtkStage.Instance()->TooltipManager.TooltipType |= 2; // skips an early return too
        addon->Show(false, 15);
    }

    private static void Close()
    {
        // Hide first, so closing doesn't play the window close sound.
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("ItemDetail");
        if (addon != null)
            addon->Hide(true, false, 0);

        var agent = AgentItemDetail.Instance();
        if (agent == null)
            return;

        var eventData = stackalloc AtkValue[1];
        var values = stackalloc AtkValue[1];
        values->Type = ValueType.Int;
        values->Int = -1;
        agent->ReceiveEvent(eventData, values, 1, 1);
    }
}
