using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace FlickWheel;

// Something the picker offers: what it would add, and how it's listed.
internal readonly record struct ShortcutChoice(ShortcutKind Kind, uint RowId, string Name, uint IconId, string Category);

// Looks up, checks and runs shortcut entries, and lists what can be added.
internal sealed class ShortcutCatalog
{
    // Actions offered in the picker. The Action sheet is mostly combat skills, so only
    // the useful out-of-combat ones are listed.
    private static readonly uint[] PickerActions =
    [
        26988, // the Aether Compass
    ];

    private readonly IDataManager data;
    private readonly ICommandManager commands;
    // Main menu icons are a gold glyph on a red tile; those get keyed out to match the job glyphs.
    private readonly HashSet<uint> menuIcons;

    public ShortcutCatalog(IDataManager data, ICommandManager commands)
    {
        this.data = data;
        this.commands = commands;
        menuIcons = data.GetExcelSheet<MainCommand>().Where(row => row.Icon > 0).Select(row => (uint)row.Icon).ToHashSet();
    }

    /// <summary>The name and icon an entry shows with, or null if its row doesn't exist.</summary>
    public (string Name, uint IconId, bool KeyOutIcon)? Describe(ShortcutEntry entry)
    {
        (string Name, uint Icon)? found = entry.Kind switch
        {
            ShortcutKind.MainCommand => data.GetExcelSheet<MainCommand>().TryGetRow(entry.RowId, out var row)
                ? (row.Name.ExtractText(), (uint)row.Icon) : null,
            ShortcutKind.GeneralAction => data.GetExcelSheet<GeneralAction>().TryGetRow(entry.RowId, out var row)
                ? (row.Name.ExtractText(), (uint)row.Icon) : null,
            ShortcutKind.Action => data.GetExcelSheet<LuminaAction>().TryGetRow(entry.RowId, out var row)
                ? (ActionName(row), (uint)row.Icon) : null,
            ShortcutKind.Item => data.GetExcelSheet<Item>().TryGetRow(entry.RowId, out var row)
                ? (row.Name.ExtractText(), (uint)row.Icon) : null,
            ShortcutKind.Command => (entry.Command ?? string.Empty, 0u),
            _ => null,
        };

        if (found is not { } info)
            return null;

        var name = string.IsNullOrWhiteSpace(entry.Label) ? info.Name : entry.Label;
        var icon = entry.IconId != 0 ? entry.IconId : info.Icon;
        return (name, icon, menuIcons.Contains(icon));
    }

    /// <summary>Whether the entry can be used now: the menu is unlocked, the plugin command exists.</summary>
    public unsafe bool Available(ShortcutEntry entry)
    {
        switch (entry.Kind)
        {
            case ShortcutKind.MainCommand:
                var ui = UIModule.Instance();
                return ui != null && ui->IsMainCommandUnlocked(entry.RowId);
            case ShortcutKind.Command:
                return CommandName(entry.Command) is { } name && commands.Commands.ContainsKey(name);
            default:
                return true;
        }
    }

    /// <summary>How many of an item you have (normal and high quality), or null for other kinds.</summary>
    public unsafe int? Count(ShortcutEntry entry)
    {
        if (entry.Kind != ShortcutKind.Item)
            return null;
        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return null;
        return inventory->GetInventoryItemCount(entry.RowId) + inventory->GetInventoryItemCount(entry.RowId, true);
    }

    public unsafe void Run(ShortcutEntry entry)
    {
        switch (entry.Kind)
        {
            case ShortcutKind.MainCommand:
                var ui = UIModule.Instance();
                if (ui != null)
                    ui->ExecuteMainCommand(entry.RowId);
                break;
            case ShortcutKind.GeneralAction:
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, entry.RowId);
                break;
            case ShortcutKind.Action:
                ActionManager.Instance()->UseAction(ActionType.Action, entry.RowId);
                break;
            case ShortcutKind.Item:
                var agent = AgentInventoryContext.Instance();
                if (agent != null)
                    agent->UseItem(entry.RowId);
                break;
            case ShortcutKind.Command:
                if (!string.IsNullOrWhiteSpace(entry.Command))
                    commands.ProcessCommand(entry.Command.Trim());
                break;
        }
    }

    // Game windows, grouped by the main menu's own categories. Settings sub-pages and
    // menu headings (no category or no icon) are left out.
    public List<ShortcutChoice> Menus()
    {
        var categories = data.GetExcelSheet<MainCommandCategory>();
        return data.GetExcelSheet<MainCommand>()
                   .Where(row => row.Icon > 0 && row.MainCommandCategory.RowId != 0 && row.Name.ExtractText().Trim().Length > 0)
                   .Select(row => new ShortcutChoice(
                       ShortcutKind.MainCommand, row.RowId, row.Name.ExtractText(), (uint)row.Icon,
                       categories.TryGetRow(row.MainCommandCategory.RowId, out var category) ? category.Name.ExtractText() : string.Empty))
                   .OrderBy(choice => choice.Category)
                   .ThenBy(choice => choice.Name)
                   .ToList();
    }

    public List<ShortcutChoice> Actions()
    {
        var general = data.GetExcelSheet<GeneralAction>()
                          .Where(row => row.Name.ExtractText().Length > 0)
                          .Select(row => new ShortcutChoice(ShortcutKind.GeneralAction, row.RowId, row.Name.ExtractText(), (uint)row.Icon, "General"))
                          .DistinctBy(choice => choice.Name);
        var actions = data.GetExcelSheet<LuminaAction>();
        var other = PickerActions.Where(id => actions.HasRow(id))
                                 .Select(id => actions.GetRow(id))
                                 .Select(row => new ShortcutChoice(ShortcutKind.Action, row.RowId, ActionName(row), (uint)row.Icon, "Other"));
        return general.Concat(other).ToList();
    }

    public unsafe List<ShortcutChoice> Items()
    {
        var choices = new List<ShortcutChoice>();
        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return choices;

        var sheet = data.GetExcelSheet<Item>();
        var seen = new HashSet<uint>();
        foreach (var type in new[] { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 })
        {
            var container = inventory->GetInventoryContainer(type);
            if (container == null)
                continue;
            for (var i = 0; i < container->Size; i++)
            {
                var id = container->GetInventorySlot(i)->ItemId;
                if (id == 0 || !seen.Add(id) || !sheet.TryGetRow(id, out var item) || item.ItemAction.RowId == 0)
                    continue;
                choices.Add(new ShortcutChoice(ShortcutKind.Item, id, item.Name.ExtractText(), (uint)item.Icon, "Items"));
            }
        }

        return choices.OrderBy(choice => choice.Name).ToList();
    }

    public List<string> PluginCommands() => commands.Commands.Keys.Order().ToList();

    // "the Aether Compass" → "Aether Compass".
    private static string ActionName(LuminaAction row)
    {
        var name = row.Name.ExtractText();
        return name.StartsWith("the ", StringComparison.Ordinal) ? char.ToUpper(name[4]) + name[5..] : name;
    }

    private static string? CommandName(string? command) =>
        command?.Trim().Split(' ', 2)[0] is { Length: > 0 } name ? name : null;
}
