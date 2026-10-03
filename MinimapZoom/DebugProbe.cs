#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MinimapZoom;

/// <summary>
/// Debug-build only: /minimapzoomdebug snap / diff / watch / set / hold, used to find where the
/// minimap keeps its zoom. Memory is read as floats from two regions: "navi" (the NaviMap
/// addon past its AtkUnitBase) and "agent" (AgentMap). Offsets are from the region's start
/// address, so they can be pasted straight into set / watch / hold. Also logs the events the
/// minimap receives, and can list its buttons and re-enable a greyed-out one (events / buttons / enable).
/// </summary>
internal sealed unsafe class DebugProbe : IDisposable
{
    private const string CommandName = "/minimapzoomdebug";
    private const string Tag = "[Minimap Zoom]";
    // The minimap is a HUD addon, so its name starts with an underscore.
    private const string NaviMapName = "_NaviMap";
    // The NaviMap addon is read this far past its start (ClientStructs doesn't map its size).
    private const int NaviBytes = 0x1000;
    // More changed values than this are cut from the chat output (all go to /xllog).
    private const int MaxPrinted = 40;

    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly IAddonLifecycle lifecycle;

    // Log every event the minimap receives (a click on its buttons, the mouse wheel…).
    private bool logEvents = true;

    private Snapshot? snapshot;
    private (string Region, int Offset)? watching;
    private (string Region, int Offset, uint Value)? holding;
    private long nextWatch;
    private uint? lastWatched;

    public DebugProbe(ICommandManager commands, IFramework framework, IChatGui chat, IPluginLog log, IAddonLifecycle lifecycle)
    {
        this.commands = commands;
        this.framework = framework;
        this.chat = chat;
        this.log = log;
        this.lifecycle = lifecycle;
        lifecycle.RegisterListener(AddonEvent.PreReceiveEvent, NaviMapName, OnNaviEvent);
        framework.Update += OnUpdate;
        commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Debug: find the minimap zoom. /minimapzoomdebug snap | diff | watch <navi|agent> <offset> | " +
                          "set <navi|agent> <offset> <value> | hold <navi|agent> <offset> <value> | hold off | stop | " +
                          "events on|off | nodes | enable <node path>",
        });
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        lifecycle.UnregisterListener(OnNaviEvent);
        commands.RemoveHandler(CommandName);
    }

    // Region name → (start address, length in bytes). Missing when it isn't loaded.
    private static Dictionary<string, (nint Start, int Length)> Regions()
    {
        var regions = new Dictionary<string, (nint, int)>();
        var manager = RaptureAtkUnitManager.Instance();
        var navi = manager == null ? null : manager->GetAddonByName(NaviMapName);
        if (navi != null)
            regions["navi"] = ((nint)navi, NaviBytes);

        var agent = AgentMap.Instance();
        if (agent != null)
            regions["agent"] = ((nint)agent, sizeof(AgentMap));

        return regions;
    }

    private static uint[] Read(nint start, int length)
    {
        var values = new uint[length / 4];
        var words = (uint*)start;
        for (var i = 0; i < values.Length; i++)
            values[i] = words[i];
        return values;
    }

    // Every memory word, plus each NaviMap UI node's scale, position and size (the zoom
    // may be drawn by scaling the map image rather than stored as a number).
    private static Snapshot Snap()
    {
        var nodes = new Dictionary<string, string>();
        var manager = RaptureAtkUnitManager.Instance();
        var navi = manager == null ? null : manager->GetAddonByName(NaviMapName);
        if (navi != null)
            AddNodes(&navi->UldManager, "node", nodes, 0);

        return new Snapshot(Regions().ToDictionary(r => r.Key, r => Read(r.Value.Start, r.Value.Length)), nodes);
    }

    private static void AddNodes(AtkUldManager* uld, string path, Dictionary<string, string> nodes, int depth)
    {
        if (uld == null || uld->NodeList == null || depth > 4)
            return;
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null)
                continue;
            var key = $"{path}/{node->NodeId}";
            nodes[key] = string.Create(CultureInfo.InvariantCulture,
                $"scale {node->ScaleX:G4},{node->ScaleY:G4} pos {node->X:G5},{node->Y:G5} size {node->Width}×{node->Height} rot {node->Rotation:G4}");
            if ((int)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null)
                    AddNodes(&component->UldManager, key, nodes, depth + 1);
            }
        }
    }

    private sealed record Snapshot(Dictionary<string, uint[]> Memory, Dictionary<string, string> Nodes);

    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
        try
        {
            switch (verb)
            {
                case "snap":
                    snapshot = Snap();
                    if (!snapshot.Memory.ContainsKey("navi"))
                        chat.PrintError($"{Tag} The minimap ({NaviMapName}) isn't loaded; only the map agent is in this snapshot.");
                    chat.Print($"{Tag} Snapshot taken ({string.Join(", ", snapshot.Memory.Select(s => $"{s.Key} {s.Value.Length * 4:X} bytes"))}, {snapshot.Nodes.Count} UI nodes). " +
                               $"Zoom the minimap, then /minimapzoomdebug diff.");
                    break;
                case "diff":
                    Diff();
                    break;
                case "watch" when parts.Length >= 3:
                    watching = (parts[1].ToLowerInvariant(), ParseOffset(parts[2]));
                    lastWatched = null;
                    chat.Print($"{Tag} Watching {parts[1]} 0x{watching.Value.Offset:X}; changes go to chat.");
                    break;
                case "set" when parts.Length >= 4:
                    Write(parts[1].ToLowerInvariant(), ParseOffset(parts[2]), ParseValue(parts[3]));
                    chat.Print($"{Tag} Wrote {parts[3]} to {parts[1]} 0x{ParseOffset(parts[2]):X}.");
                    break;
                case "hold" when parts.Length >= 2 && parts[1].Equals("off", StringComparison.OrdinalIgnoreCase):
                    holding = null;
                    chat.Print($"{Tag} Hold off.");
                    break;
                case "hold" when parts.Length >= 4:
                    holding = (parts[1].ToLowerInvariant(), ParseOffset(parts[2]), ParseValue(parts[3]));
                    chat.Print($"{Tag} Holding {parts[1]} 0x{holding.Value.Offset:X} at {parts[3]} every frame. /minimapzoomdebug hold off to stop.");
                    break;
                case "events" when parts.Length >= 2:
                    logEvents = parts[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                    chat.Print($"{Tag} Minimap event logging {(logEvents ? "on" : "off")}.");
                    break;
                case "nodes" or "buttons":
                    ListNodes();
                    break;
                case "enable" when parts.Length >= 2:
                    EnableNode(parts[1]);
                    break;
                case "stop":
                    watching = null;
                    holding = null;
                    chat.Print($"{Tag} Stopped watching and holding.");
                    break;
                default:
                    chat.Print($"{Tag} Usage: snap | diff | watch <navi|agent> <offset> | set <navi|agent> <offset> <value> | " +
                               "hold <navi|agent> <offset> <value> | hold off | stop | events on|off | nodes | enable <node path>");
                    break;
            }
        }
        catch (FormatException)
        {
            chat.PrintError($"{Tag} Couldn't read that number. Offsets are hex (0x2A4 or 2A4); values are decimals (0.5) or whole numbers ending in i (3i).");
        }
    }


    private void OnNaviEvent(AddonEvent type, AddonArgs args)
    {
        if (!logEvents || args is not AddonReceiveEventArgs e)
            return;
        var eventType = (AtkEventType)e.AtkEventType;
        // Hovering fires these every frame.
        if (eventType is AtkEventType.MouseMove or AtkEventType.MouseOver or AtkEventType.MouseOut)
            return;

        var atkEvent = (AtkEvent*)e.AtkEvent;
        var node = atkEvent != null && atkEvent->Node != null ? $" node {atkEvent->Node->NodeId}" : string.Empty;
        var line = $"{Tag} Event {eventType} ({e.AtkEventType}) param {e.EventParam}{node}";
        log.Information(line);
        chat.Print(line);
    }

    // Every NaviMap UI node to /xllog; the clickable ones (components, or nodes that take
    // mouse events) also to chat. Paths are node ids from the addon down, e.g. 3/12.
    private void ListNodes()
    {
        var nodes = Nodes();
        if (nodes.Count == 0)
        {
            chat.Print($"{Tag} No minimap nodes found (is the minimap shown?).");
            return;
        }

        var shown = 0;
        foreach (var (path, address, componentType) in nodes)
        {
            var node = (AtkResNode*)address;
            var clickable = componentType != null || node->NodeFlags.HasFlag(NodeFlags.EmitsEvents);
            var line = $"{Tag} Node {path}: {(componentType is { } t ? $"component {t}" : node->Type.ToString())}, " +
                       $"{(node->IsVisible() ? "shown" : "hidden")}, " +
                       $"{(node->NodeFlags.HasFlag(NodeFlags.Enabled) ? "enabled" : "disabled")}" +
                       $"{(node->NodeFlags.HasFlag(NodeFlags.EmitsEvents) ? ", takes events" : string.Empty)}, " +
                       $"at {node->X:G4},{node->Y:G4} size {node->Width}×{node->Height}";
            log.Information(line);
            if (clickable && node->IsVisible())
            {
                chat.Print(line);
                shown++;
            }
        }

        chat.Print($"{Tag} {nodes.Count} nodes in /xllog, {shown} visible clickable ones above.");
    }

    private void EnableNode(string path)
    {
        foreach (var (nodePath, address, componentType) in Nodes())
        {
            if (nodePath != path)
                continue;
            var node = (AtkResNode*)address;
            node->NodeFlags |= NodeFlags.Enabled;
            if (componentType == ComponentType.Button)
                ((AtkComponentButton*)((AtkComponentNode*)node)->Component)->SetEnabledState(true);
            chat.Print($"{Tag} Enabled node {path}. Click it and watch the events.");
            return;
        }

        chat.PrintError($"{Tag} No node {path}; see /minimapzoomdebug nodes.");
    }

    // Path, node address, and the component type for component nodes.
    private static List<(string Path, nint Node, ComponentType? Component)> Nodes()
    {
        var list = new List<(string, nint, ComponentType?)>();
        var manager = RaptureAtkUnitManager.Instance();
        var navi = manager == null ? null : manager->GetAddonByName(NaviMapName);
        if (navi != null)
            AddNodes(&navi->UldManager, string.Empty, list, 0);
        return list;
    }

    private static void AddNodes(AtkUldManager* uld, string path, List<(string, nint, ComponentType?)> list, int depth)
    {
        if (uld == null || uld->NodeList == null || depth > 4)
            return;
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null)
                continue;
            var key = path.Length == 0 ? node->NodeId.ToString(CultureInfo.InvariantCulture) : $"{path}/{node->NodeId}";
            if ((int)node->Type < 1000)
            {
                list.Add((key, (nint)node, null));
                continue;
            }

            var component = ((AtkComponentNode*)node)->Component;
            var info = component == null ? null : (AtkUldComponentInfo*)component->UldManager.Objects;
            list.Add((key, (nint)node, info != null ? info->ComponentType : (ComponentType?)null));
            if (component != null)
                AddNodes(&component->UldManager, key, list, depth + 1);
        }
    }
    // Memory words and UI nodes that changed since the snapshot; the new values become the snapshot.
    private void Diff()
    {
        if (snapshot == null)
        {
            chat.Print($"{Tag} Take a snapshot first: /minimapzoomdebug snap.");
            return;
        }

        var now = Snap();
        var changes = new List<string>();
        foreach (var (region, after) in now.Memory)
        {
            if (!snapshot.Memory.TryGetValue(region, out var before))
                continue;
            for (var i = 0; i < Math.Min(before.Length, after.Length); i++)
            {
                if (before[i] != after[i] && Describe(before[i]) is { } a && Describe(after[i]) is { } b)
                    changes.Add($"{region} 0x{i * 4:X}: {a} → {b}");
            }
        }

        foreach (var (key, after) in now.Nodes)
        {
            if (snapshot.Nodes.TryGetValue(key, out var before) && before != after)
                changes.Add($"{key}: {before} → {after}");
        }

        snapshot = now;
        foreach (var line in changes)
            log.Information($"{Tag} {line}");
        foreach (var line in changes.Take(MaxPrinted))
            chat.Print($"{Tag} {line}");
        chat.Print(changes.Count > MaxPrinted
            ? $"{Tag} {changes.Count} changed ({changes.Count - MaxPrinted} more in /xllog). New snapshot taken."
            : $"{Tag} {changes.Count} changed ({now.Nodes.Count} UI nodes checked). New snapshot taken.");
    }

    // A word as a plausible float ("1.5f") or small integer ("3"); null for pointer-like noise.
    private static string? Describe(uint word)
    {
        var asInt = (int)word;
        if (Math.Abs((long)asInt) <= 100000)
            return asInt.ToString(CultureInfo.InvariantCulture);
        var asFloat = BitConverter.UInt32BitsToSingle(word);
        return float.IsFinite(asFloat) && Math.Abs(asFloat) is >= 0.0001f and <= 100000f
            ? asFloat.ToString("G6", CultureInfo.InvariantCulture) + "f"
            : null;
    }

    private void OnUpdate(IFramework _)
    {
        if (holding is var (holdRegion, holdOffset, value))
            Write(holdRegion, holdOffset, value, quiet: true);

        if (watching is not var (region, offset) || Environment.TickCount64 < nextWatch)
            return;
        nextWatch = Environment.TickCount64 + 250;
        if (Address(region, offset) is not { } address)
            return;
        var current = *(uint*)address;
        if (lastWatched != current)
        {
            chat.Print($"{Tag} {region} 0x{offset:X} = {BitConverter.UInt32BitsToSingle(current).ToString("G6", CultureInfo.InvariantCulture)}f / {(int)current}");
            lastWatched = current;
        }
    }

    private void Write(string region, int offset, uint value, bool quiet = false)
    {
        if (Address(region, offset) is { } address)
            *(uint*)address = value;
        else if (!quiet)
            chat.PrintError($"{Tag} {region} isn't loaded or 0x{offset:X} is outside it.");
    }

    private static nint? Address(string region, int offset) =>
        Regions().TryGetValue(region, out var r) && offset >= 0 && offset + 4 <= r.Length && offset % 4 == 0
            ? r.Start + offset
            : null;

    private static int ParseOffset(string text) =>
        int.Parse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text, NumberStyles.HexNumber,
                  CultureInfo.InvariantCulture);

    // "0.5" is written as a float, "3i" as a whole number.
    private static uint ParseValue(string text) =>
        text.EndsWith('i')
            ? (uint)int.Parse(text[..^1], CultureInfo.InvariantCulture)
            : BitConverter.SingleToUInt32Bits(float.Parse(text, CultureInfo.InvariantCulture));
}
#endif
