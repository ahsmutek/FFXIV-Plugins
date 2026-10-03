using System;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MinimapZoom;

// The game's minimap (the _NaviMap addon): its zoom value and its − button. ClientStructs
// doesn't map the zoom, so it's read at a fixed offset and checked against the map image's
// scale (which the game keeps equal to it) before anything is written. If a patch moves
// it, the check fails and nothing is touched.
internal static unsafe class NaviMap
{
    public const string AddonName = "_NaviMap";

    // The game's own zoom-out limit and how far one click of + or − moves.
    public const float GameMinimum = 1.25f;
    public const float Step = 0.25f;

    // ButtonClick event params of the + and − buttons.
    public const int ZoomInParam = 4;
    public const int ZoomOutParam = 3;

    private const int ZoomOffset = 0x264;
    private const uint MarkersNodeId = 18; // the component holding the map image and markers
    private const uint MapImageNodeId = 6; // the map image inside it
    private const uint ZoomOutNodeId = 3;  // the − button

    // Close enough to count as the same zoom.
    public const float Epsilon = 0.001f;

    private static AtkUnitBase* Addon()
    {
        var manager = RaptureAtkUnitManager.Instance();
        var addon = manager == null ? null : manager->GetAddonByName(AddonName);
        return addon != null && addon->IsReady ? addon : null;
    }

    /// <summary>The zoom, or null when the minimap isn't up or the value doesn't look right.</summary>
    public static float? Zoom()
    {
        var addon = Addon();
        if (addon == null)
            return null;

        var zoom = *(float*)((byte*)addon + ZoomOffset);
        var image = MapImage(addon);
        if (image == null || !float.IsFinite(zoom) || zoom is < 0.05f or > 20f || MathF.Abs(image->ScaleX - zoom) > Epsilon)
            return null;
        return zoom;
    }

    /// <summary>
    /// Sets the zoom, only when the current one checks out (see <see cref="Zoom"/>). Pass
    /// checkedBefore when it was checked just before the game changed it itself (a click).
    /// </summary>
    public static bool SetZoom(float zoom, bool checkedBefore = false)
    {
        var addon = Addon();
        if (addon == null || (!checkedBefore && Zoom() == null))
            return false;

        *(float*)((byte*)addon + ZoomOffset) = zoom;
        return true;
    }

    /// <summary>Greys the − button out or makes it clickable again.</summary>
    public static void SetZoomOutEnabled(bool enabled)
    {
        var addon = Addon();
        var node = addon == null ? null : Find(&addon->UldManager, ZoomOutNodeId);
        if (node == null || (int)node->Type < 1000 || node->NodeFlags.HasFlag(NodeFlags.Enabled) == enabled)
            return;

        var button = (AtkComponentButton*)((AtkComponentNode*)node)->Component;
        if (button != null)
            button->SetEnabledState(enabled);
    }

    private static AtkResNode* MapImage(AtkUnitBase* addon)
    {
        var markers = Find(&addon->UldManager, MarkersNodeId);
        if (markers == null || (int)markers->Type < 1000)
            return null;
        var component = ((AtkComponentNode*)markers)->Component;
        return component == null ? null : Find(&component->UldManager, MapImageNodeId);
    }

    private static AtkResNode* Find(AtkUldManager* uld, uint nodeId)
    {
        if (uld->NodeList == null)
            return null;
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node != null && node->NodeId == nodeId)
                return node;
        }

        return null;
    }
}
