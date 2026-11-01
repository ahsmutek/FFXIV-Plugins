using System;
using System.Runtime.InteropServices;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace GlamKit;

// The game fills the tooltip's strings during ItemDetail's update, so they're changed just before it lays them out, as Simple Tweaks does.
internal sealed unsafe class ItemTooltip : IDisposable
{
    private const string GenerateSignature =
        "48 89 5C 24 ?? 55 56 57 41 54 41 55 41 56 41 57 48 83 EC ?? 48 8B 42 ?? 4C 8B EA";

    private const string Line = "Kept for glam: move it to the glamour dresser";
    private const string LaterLine = "Kept for later: worth keeping for its stats";
    private const int Description = 13;
    private const int ClassJobCategory = 22;

    // The number holding which parts of the tooltip show, and the description's bit in it.
    private const int Visibility = 5;
    private const int DescriptionShown = 2;

    // Collectables add 500,000 to the item ID and HQ 1,000,000.
    private const uint ItemIdModifier = 500_000;

    // The UIColor row nearest this, as the game colours text by row.
    private const uint Purple = 0xC78CFF;
    private const uint Teal = 0x59D9BF;

    private delegate void GenerateDelegate(AddonItemDetail* addon, NumberArrayData* numbers, StringArrayData* strings);

    private readonly IGameGui gameGui;
    private readonly IAddonLifecycle lifecycle;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly JobLevels jobLevels;
    private readonly Hook<GenerateDelegate>? hook;
    private readonly ushort purple;
    private readonly ushort teal;
    private byte[] lastJobLine = [];

    public ItemTooltip(IGameInteropProvider interop, IAddonLifecycle lifecycle, IDataManager data, IGameGui gameGui,
                       IPluginLog log, Configuration config)
    {
        this.lifecycle = lifecycle;
        this.gameGui = gameGui;
        this.log = log;
        this.config = config;
        jobLevels = new JobLevels(data, config);
        purple = Nearest(data, Purple);
        teal = Nearest(data, Teal);
        try
        {
            hook = interop.HookFromSignature<GenerateDelegate>(GenerateSignature, Generate);
            hook.Enable();
        }
        catch (Exception e)
        {
            log.Warning(e, "Couldn't find the item tooltip, so it won't say an item is kept.");
        }
        lifecycle.RegisterListener(AddonEvent.PreDraw, "ItemDetail", UncolourEquipLevel);
    }

    public void Dispose()
    {
        lifecycle.UnregisterListener(AddonEvent.PreDraw, "ItemDetail", UncolourEquipLevel);
        hook?.Dispose();
    }

    private void Generate(AddonItemDetail* addon, NumberArrayData* numbers, StringArrayData* strings)
    {
        try
        {
            AddLine(numbers, strings);
            AddJobLevels(strings);
        }
        catch (Exception e)
        {
            log.Error(e, "Couldn't add to the item tooltip.");
        }
        hook!.Original(addon, numbers, strings);
    }

    private uint HoveredItem()
    {
        var agent = AgentItemDetail.Instance();
        return (uint)((agent != null && agent->ItemId != 0 ? agent->ItemId : gameGui.HoveredItem) % ItemIdModifier);
    }

    private void AddJobLevels(StringArrayData* strings)
    {
        if (!config.JobLevelsEnabled || strings == null || strings->Size <= ClassJobCategory)
            return;
        var current = strings->StringArray[ClassJobCategory].Value;
        if (current == null)
            return;
        var currentBytes = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(current);
        if (currentBytes.Length == 0 || currentBytes.SequenceEqual(lastJobLine))
            return;

        var line = jobLevels.Line(HoveredItem(), SeString.Parse(currentBytes));
        if (line == null)
            return;

        var bytes = line.EncodeWithNullTerminator();
        lastJobLine = bytes[..^1];
        fixed (byte* text = bytes)
            strings->SetValue(ClassJobCategory, text, false, true, false);
    }

    // The game colours the equip level against the current job even when that job can't wear the item. It sets the
    // colour on the text node after the tooltip is built, so it's reset to the description's before each draw.
    private void UncolourEquipLevel(AddonEvent type, AddonArgs args)
    {
        var addon = (AddonItemDetail*)args.Addon.Address;
        if (!config.JobLevelsEnabled || !config.UncolourUnwearableLevel || addon == null ||
            addon->LevelText == null || addon->DescriptionText == null ||
            jobLevels.CurrentJobCanWear(HoveredItem()) != false)
            return;
        addon->LevelText->TextColor = addon->DescriptionText->TextColor;
        addon->LevelText->EdgeColor = addon->DescriptionText->EdgeColor;
    }

    private void AddLine(NumberArrayData* numbers, StringArrayData* strings)
    {
        if (strings == null || strings->Size <= Description)
            return;
        var itemId = HoveredItem();
        var glam = config.GlamKeeperEnabled && config.IsKept(itemId);
        var later = config.KeepForLaterEnabled && config.IsKeptForLater(itemId);
        if (!glam && !later)
            return;

        // Items without a description of their own have it hidden, so it's shown, as Allagan Tools does.
        if (numbers != null && numbers->Size > Visibility)
            numbers->IntArray[Visibility] |= DescriptionShown;

        var current = strings->StringArray[Description].Value;
        var description = current == null ? new SeString() : SeString.Parse(current);
        if (description.TextValue.Contains(Line) || description.TextValue.Contains(LaterLine))
            return;

        var builder = new SeStringBuilder().Append(description);
        if (description.Payloads.Count > 0)
            builder.Add(NewLinePayload.Payload).Add(NewLinePayload.Payload);
        if (glam)
            builder.AddUiForeground(Line, purple);
        if (glam && later)
            builder.Add(NewLinePayload.Payload);
        if (later)
            builder.AddUiForeground(LaterLine, teal);

        var bytes = builder.Build().EncodeWithNullTerminator();
        fixed (byte* text = bytes)
            strings->SetValue(Description, text, false, true, false);
    }

    internal static ushort Nearest(IDataManager data, uint rgb)
    {
        static int Distance(uint a, uint b) =>
            Math.Abs((int)(a >> 16 & 0xFF) - (int)(b >> 16 & 0xFF)) +
            Math.Abs((int)(a >> 8 & 0xFF) - (int)(b >> 8 & 0xFF)) +
            Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF));

        // Rows are RGBA; row 0 is the default text colour, so it is skipped.
        var best = (Row: 0u, Distance: int.MaxValue);
        foreach (var row in data.GetExcelSheet<UIColor>())
        {
            var distance = Distance(row.Dark >> 8, rgb);
            if (row.RowId != 0 && distance < best.Distance)
                best = (row.RowId, distance);
        }
        return (ushort)best.Row;
    }
}
