using System;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace KeepInMind;

// Adds how many you need to the game's item tooltip, under the description. The game fills the tooltip's
// strings during ItemDetail's update, so they're changed just before it lays them out, as Simple Tweaks does.
internal sealed unsafe class ItemTooltip : IDisposable
{
    private const string GenerateSignature =
        "48 89 5C 24 ?? 55 56 57 41 54 41 55 41 56 41 57 48 83 EC ?? 48 8B 42 ?? 4C 8B EA";

    private const string Prefix = "Keep in Mind: ";
    private const int Description = 13;

    // Collectables add 500,000 to the item ID and HQ 1,000,000.
    private const uint ItemIdModifier = 500_000;

    // The UIColor row nearest this, as the game colours text by row.
    private const uint Blue = 0x66B3FF;

    private delegate void* GenerateDelegate(AtkUnitBase* addon, NumberArrayData* numbers, StringArrayData* strings);

    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly Stock stock;
    private readonly Hook<GenerateDelegate>? hook;
    private readonly ushort blue;

    public ItemTooltip(IGameInteropProvider interop, IDataManager data, IGameGui gameGui, IPluginLog log, Configuration config, Stock stock)
    {
        this.gameGui = gameGui;
        this.log = log;
        this.config = config;
        this.stock = stock;
        blue = Nearest(data, Blue);
        try
        {
            hook = interop.HookFromSignature<GenerateDelegate>(GenerateSignature, Generate);
            hook.Enable();
        }
        catch (Exception e)
        {
            log.Warning(e, "Couldn't find the item tooltip, so it won't show how many you need.");
        }
    }

    public void Dispose() => hook?.Dispose();

    private void* Generate(AtkUnitBase* addon, NumberArrayData* numbers, StringArrayData* strings)
    {
        try
        {
            AddLine(strings);
        }
        catch (Exception e)
        {
            log.Error(e, "Couldn't add to the item tooltip.");
        }
        return hook!.Original(addon, numbers, strings);
    }

    private void AddLine(StringArrayData* strings)
    {
        var agent = AgentItemDetail.Instance();
        var itemId = (uint)((agent != null && agent->ItemId != 0 ? agent->ItemId : gameGui.HoveredItem) % ItemIdModifier);
        if (strings == null || strings->Size <= Description ||
            config.Items.Find(i => i.ItemId == itemId) is not { } kept)
        {
            return;
        }

        var current = strings->StringArray[Description].Value;
        var description = current == null ? new SeString() : SeString.Parse(current);
        if (description.TextValue.Contains(Prefix))
            return;

        var builder = new SeStringBuilder().Append(description);
        if (description.Payloads.Count > 0)
            builder.Add(NewLinePayload.Payload).Add(NewLinePayload.Payload);
        builder.AddUiForeground(Prefix + Line(kept), blue);

        var bytes = builder.Build().EncodeWithNullTerminator();
        fixed (byte* text = bytes)
            strings->SetValue(Description, text, false, true, false);
    }

    private static ushort Nearest(IDataManager data, uint rgb)
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

    private string Line(KeptItem kept)
    {
        var line = $"need {kept.Needed}, have {Stock.Have(kept.ItemId)}";
        var stored = stock.Stored(kept.ItemId);
        if (stored.Retainers > 0)
            line += $", {stored.Retainers} in retainers";
        if (stored.Saddlebag > 0)
            line += $", {stored.Saddlebag} in saddlebag";
        return kept.Done ? line + " (done)" : line;
    }
}
