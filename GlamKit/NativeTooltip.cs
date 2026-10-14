using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace GlamKit;

// The game only anchors tooltips to its own nodes, so this drives ItemDetail directly, as Chat 2 does.
internal sealed unsafe class NativeTooltip : IDisposable
{
    private const string Addon = "ItemDetail";
    private const float Gap = 8f;
    private const int HiddenFrames = 2;
    private static readonly TimeSpan CloseAfter = TimeSpan.FromSeconds(0.15);

    // The game re-anchors ItemDetail to the cursor on each of these, and a move only shows if made in time,
    // so it's moved back after every one.
    private static readonly AddonEvent[] PlaceOn =
        [AddonEvent.PostRefresh, AddonEvent.PostRequestedUpdate, AddonEvent.PostUpdate, AddonEvent.PreDraw];

    private readonly IAddonLifecycle lifecycle;

    private uint hoveredThisFrame;
    private uint shown;
    private DateTime lastHover;
    private int hiddenFor;
    private Vector2 avoidMin;
    private Vector2 avoidMax;
    private Vector2 cursor;
    private Vector2 screen;

    public NativeTooltip(IAddonLifecycle lifecycle)
    {
        this.lifecycle = lifecycle;
        foreach (var e in PlaceOn)
            lifecycle.RegisterListener(e, Addon, OnUpdated);
    }

    // The game draws under ImGui, so the tooltip is moved beside the window instead of under the cursor.
    public void Hover(uint itemId, Vector2 windowMin, Vector2 windowMax)
    {
        hoveredThisFrame = itemId;
        avoidMin = windowMin;
        avoidMax = windowMax;
        cursor = ImGui.GetMousePos();
        screen = ImGui.GetMainViewport().Size;
    }

    public void EndFrame()
    {
        var hovered = hoveredThisFrame;
        hoveredThisFrame = 0;
        if (hovered != 0)
            lastHover = DateTime.UtcNow;
        if (hovered == shown)
            return;

        // A short grace, so crossing the gap between a row's icon and name doesn't close it.
        if (hovered == 0)
        {
            if (DateTime.UtcNow - lastHover < CloseAfter)
                return;
            Close();
        }
        else
        {
            // Switching straight to another item, so it never blinks closed in between.
            Open(hovered);
        }
        shown = hovered;
    }

    private void OnUpdated(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (shown == 0 || addon == null || addon->RootNode == null)
            return;

        Place(addon);
        // Kept invisible for the first frames after a new item, while the game still settles its position.
        if (type == AddonEvent.PreDraw && hiddenFor > 0)
        {
            hiddenFor--;
            addon->RootNode->ToggleVisibility(hiddenFor == 0);
        }
    }

    private void Place(AtkUnitBase* addon)
    {
        if (addon == null || addon->RootNode == null)
            return;

        var size = new Vector2(addon->RootNode->Width, addon->RootNode->Height) * addon->Scale;
        var x = avoidMax.X + Gap + size.X <= screen.X ? avoidMax.X + Gap : avoidMin.X - Gap - size.X;
        var y = Math.Clamp(cursor.Y - size.Y / 2, 0, Math.Max(0, screen.Y - size.Y));
        addon->SetPosition((short)Math.Max(0, x), (short)y);
    }

    public void Dispose()
    {
        foreach (var e in PlaceOn)
            lifecycle.UnregisterListener(e, Addon, OnUpdated);
        if (shown != 0)
            Close();
        shown = 0;
    }

    private void Open(uint itemId)
    {
        var agent = AgentItemDetail.Instance();
        var addon = ItemDetail();
        var stage = AtkStage.Instance();
        if (agent == null || addon == null || stage == null)
            return;

        agent->DetailKind = DetailKind.Item;
        agent->TypeOrId = itemId;
        agent->Index = 0;
        agent->Flag1 &= 0xEF;
        agent->ItemId = itemId;
        agent->Flag2 = 1; // needed for the tooltip to show
        agent->Flag3 = 0; // skips an early return in Show
        agent->AddonId = addon->Id;

        stage->TooltipManager.TooltipType |= 2; // skips an early return too
        addon->Show(false, 15);
        Place(addon);
        if (addon->RootNode != null)
            addon->RootNode->ToggleVisibility(false);
        hiddenFor = HiddenFrames;
    }

    private static AtkUnitBase* ItemDetail()
    {
        var manager = RaptureAtkUnitManager.Instance();
        return manager == null ? null : manager->GetAddonByName(Addon);
    }

    private void Close()
    {
        // Hide first, so closing doesn't play the window close sound.
        var addon = ItemDetail();
        if (addon != null)
        {
            addon->Hide(true, false, 0);
            // The game's own tooltips reuse this window, so it can't be left invisible.
            if (addon->RootNode != null)
                addon->RootNode->ToggleVisibility(true);
        }
        hiddenFor = 0;

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
