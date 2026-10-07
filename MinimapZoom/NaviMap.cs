using System;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MinimapZoom;

// ClientStructs doesn't map the zoom: it's read at a fixed offset and verified against the map image's scale, so a moved offset touches nothing.
internal static unsafe class NaviMap
{
    public const string AddonName = "_NaviMap";

    public const float GameMinimum = 1.25f;
    public const float Step = 0.25f;

    public const int ZoomInParam = 4;
    public const int ZoomOutParam = 3;

    private const int ZoomOffset = 0x264;
    // Percentage copy (125 = 1.25) the game rebuilds the minimap from after zoning; kept in step.
    private const int ZoomPercentOffset = 0x1A8;
    private const uint MarkersNodeId = 18; // the component holding the map image and markers
    private const uint MapImageNodeId = 6; // the map image inside it
    private const uint ZoomOutNodeId = 3;  // the − button

    public const float Epsilon = 0.001f;

    private static AtkUnitBase* Addon()
    {
        var manager = RaptureAtkUnitManager.Instance();
        var addon = manager == null ? null : manager->GetAddonByName(AddonName);
        return addon != null && addon->IsReady ? addon : null;
    }

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

    // checkedBefore: the zoom was verified just before the game changed it itself.
    public static bool SetZoom(float zoom, bool checkedBefore = false)
    {
        var addon = Addon();
        if (addon == null || (!checkedBefore && Zoom() == null))
            return false;

        *(float*)((byte*)addon + ZoomOffset) = zoom;

        var percent = (ushort*)((byte*)addon + ZoomPercentOffset);
        if (*percent is >= 5 and <= 2000)
            *percent = (ushort)MathF.Round(zoom * 100f);
        return true;
    }

    public static bool PercentInStep(float zoom)
    {
        var addon = Addon();
        if (addon == null)
            return true;
        var percent = *(ushort*)((byte*)addon + ZoomPercentOffset);
        return percent is < 5 or > 2000 || percent == (ushort)MathF.Round(zoom * 100f);
    }

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
