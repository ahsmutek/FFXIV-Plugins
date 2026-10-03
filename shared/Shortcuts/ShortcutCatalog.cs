using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using Lumina.Excel.Sheets;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace Shortcuts;

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

    // Toggle rows. The chat log uses the Log Window Settings menu icon.
    private const uint ChatLogToggleRow = 0;
    private const uint ChatLogIcon = 45;

    // Game windows the main menu doesn't list, by RowId. Names come from the Addon sheet.
    // The Mogpendium's icon is the one it has in the Collection (McGuffinUIData row 6).
    private static readonly (AgentId Agent, uint NameRow, uint Icon)[] Windows =
    [
        (AgentId.WeeklyBingo, 5600, 25987),       // Wondrous Tails
        (AgentId.MoogleCollection, 15870, 26067), // Mogpendium
    ];
    private const uint WondrousTailsRow = 0;
    private const uint WondrousTailsJournal = 2002023; // EventItem row

    private readonly IDataManager data;
    private readonly ICommandManager commands;
    private readonly IBuddyList buddies;
    // Flick Wheel's chat log toggle; null in plugins that don't offer it.
    private readonly ChatLogToggle? chatLog;
    // Main menu icons are a gold glyph on a red tile; those get keyed out to match the job glyphs.
    private readonly HashSet<uint> menuIcons;

    public ShortcutCatalog(IDataManager data, ICommandManager commands, IBuddyList buddies, ChatLogToggle? chatLog = null)
    {
        this.data = data;
        this.commands = commands;
        this.buddies = buddies;
        this.chatLog = chatLog;
        menuIcons = data.GetExcelSheet<MainCommand>().Where(row => row.Icon > 0).Select(row => (uint)row.Icon).ToHashSet();
    }

    /// <summary>The name and icon an entry shows with, or null if its row doesn't exist.</summary>
    public unsafe (string Name, uint IconId, bool KeyOutIcon)? Describe(IShortcut entry)
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
            ShortcutKind.Command or ShortcutKind.Text => (entry.Command?.Trim() ?? string.Empty, 0u),
            ShortcutKind.Macro => entry.RowId < MacroSlots * 2 ? DescribeMacro(entry.RowId) : null,
            ShortcutKind.Toggle => entry.RowId == ChatLogToggleRow && chatLog != null
                ? (chatLog.Hidden ? "Show chat log" : "Hide chat log", ChatLogIcon) : null,
            ShortcutKind.Window => entry.RowId < Windows.Length
                ? (WindowName(entry.RowId), Windows[entry.RowId].Icon) : null,
            ShortcutKind.BuddyAction => data.GetExcelSheet<BuddyAction>().TryGetRow(entry.RowId, out var row)
                ? (row.Name.ExtractText(), (uint)row.Icon) : null,
            _ => null,
        };

        if (found is not { } info)
            return null;

        var name = string.IsNullOrWhiteSpace(entry.Label) ? info.Name : entry.Label;
        var icon = entry.IconId != 0 ? entry.IconId : info.Icon;
        return (name, icon, menuIcons.Contains(icon));
    }

    /// <summary>Whether the entry can be used now: the menu is unlocked, the plugin command exists.</summary>
    public unsafe bool Available(IShortcut entry)
    {
        switch (entry.Kind)
        {
            case ShortcutKind.MainCommand:
                var ui = UIModule.Instance();
                return ui != null && ui->IsMainCommandUnlocked(entry.RowId);
            case ShortcutKind.Command:
                return CommandName(entry.Command) is { } name && commands.Commands.ContainsKey(name);
            case ShortcutKind.Text:
                return entry.Command?.TrimStart().StartsWith('/') == true;
            case ShortcutKind.Macro:
                var macro = MacroOf(entry.RowId);
                return macro != null && macro->IsNotEmpty();
            case ShortcutKind.Window when entry.RowId == WondrousTailsRow:
                var player = PlayerState.Instance();
                return player != null && player->HasWeeklyBingoJournal;
            case ShortcutKind.BuddyAction:
                return buddies.CompanionBuddy != null;
            default:
                return true;
        }
    }

    /// <summary>How many of an item you have (normal and high quality), or null for other kinds.</summary>
    public unsafe int? Count(IShortcut entry)
    {
        if (entry.Kind != ShortcutKind.Item)
            return null;
        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return null;
        return inventory->GetInventoryItemCount(entry.RowId) + inventory->GetInventoryItemCount(entry.RowId, true);
    }

    public unsafe void Run(IShortcut entry)
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
            case ShortcutKind.BuddyAction:
                ActionManager.Instance()->UseAction(ActionType.BuddyAction, entry.RowId);
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
            case ShortcutKind.Text:
                if (entry.Command is { } line)
                    RunLine(line);
                break;
            case ShortcutKind.Macro:
                var macro = MacroOf(entry.RowId);
                var shell = RaptureShellModule.Instance();
                if (macro != null && macro->IsNotEmpty() && shell != null)
                    shell->ExecuteMacro(macro);
                break;
            case ShortcutKind.Toggle when entry.RowId == ChatLogToggleRow && chatLog != null:
                chatLog.Toggle();
                break;
            case ShortcutKind.Window when entry.RowId < Windows.Length:
                // Opens the window, or closes it if it's already up, as the main menu does.
                var window = AgentModule.Instance()->GetAgentByInternalId(Windows[entry.RowId].Agent);
                if (window == null)
                    break;
                if (window->IsAgentActive())
                    window->Hide();
                else if (entry.RowId == WondrousTailsRow)
                    // Showing its window directly leaves it closed: the journal has to be
                    // used, like from Key Items, so the game fetches this week's card.
                    ActionManager.Instance()->UseAction(ActionType.EventItem, WondrousTailsJournal);
                else
                    window->Show();
                break;
        }
    }

    // Game windows, grouped by the main menu's own categories. Settings sub-pages and
    // menu headings (no category or no icon) are left out. Windows the menu doesn't
    // list come last, under Other.
    public List<ShortcutChoice> Menus()
    {
        var categories = data.GetExcelSheet<MainCommandCategory>();
        var menus = data.GetExcelSheet<MainCommand>()
                        .Where(row => row.Icon > 0 && row.MainCommandCategory.RowId != 0 && row.Name.ExtractText().Trim().Length > 0)
                        .Select(row => new ShortcutChoice(
                            ShortcutKind.MainCommand, row.RowId, row.Name.ExtractText(), (uint)row.Icon,
                            categories.TryGetRow(row.MainCommandCategory.RowId, out var category) ? category.Name.ExtractText() : string.Empty))
                        .OrderBy(choice => choice.Category)
                        .ThenBy(choice => choice.Name);
        var other = Enumerable.Range(0, Windows.Length)
                              .Select(i => new ShortcutChoice(ShortcutKind.Window, (uint)i, WindowName((uint)i), Windows[i].Icon, "Other"));
        return menus.Concat(other).ToList();
    }

    private string WindowName(uint id) =>
        data.GetExcelSheet<Addon>().TryGetRow(Windows[id].NameRow, out var row) ? row.Text.ExtractText() : string.Empty;

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
        var chocobo = data.GetExcelSheet<BuddyAction>()
                          .Where(row => row.Icon > 0 && row.Name.ExtractText().Length > 0)
                          .Select(row => new ShortcutChoice(ShortcutKind.BuddyAction, row.RowId, row.Name.ExtractText(), (uint)row.Icon, "Companion"));
        var toggles = chatLog == null ? []
            : new[] { new ShortcutChoice(ShortcutKind.Toggle, ChatLogToggleRow, "Hide or show chat log", ChatLogIcon, "Toggles") };
        return general.Concat(chocobo).Concat(other).Concat(toggles).ToList();
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

    // Your macros, individual then shared, in slot order. Empty slots are left out.
    public unsafe List<ShortcutChoice> Macros()
    {
        var choices = new List<ShortcutChoice>();
        for (uint id = 0; id < MacroSlots * 2; id++)
        {
            var macro = MacroOf(id);
            if (macro == null || !macro->IsNotEmpty())
                continue;
            choices.Add(new ShortcutChoice(ShortcutKind.Macro, id, MacroName(macro, id), macro->IconId,
                                           id < MacroSlots ? "Individual" : "Shared"));
        }
        return choices;
    }

    public List<string> PluginCommands() => commands.Commands.Keys.Order().ToList();

    /// <summary>Whether the line starts with a command a loaded plugin registered.</summary>
    public bool IsPluginCommand(string? line) => CommandName(line) is { } name && commands.Commands.ContainsKey(name);

    // A line as if typed in chat: a plugin's command through Dalamud, anything else through
    // the game. Only lines starting with / run, so nothing is ever said in chat by accident.
    private unsafe void RunLine(string line)
    {
        line = line.Trim();
        if (!line.StartsWith('/') || commands.ProcessCommand(line))
            return;

        var ui = UIModule.Instance();
        if (ui == null)
            return;
        var text = Utf8String.FromString(line);
        ui->ProcessChatBoxEntry(text);
        text->Dtor(true);
    }

    // "the Aether Compass" → "Aether Compass".
    private static string ActionName(LuminaAction row)
    {
        var name = row.Name.ExtractText();
        return name.StartsWith("the ", StringComparison.Ordinal) ? char.ToUpper(name[4]) + name[5..] : name;
    }

    private const uint MacroSlots = 100;

    // RowId 0–99 is individual macro slot N, 100–199 shared slot N − 100.
    private static unsafe RaptureMacroModule.Macro* MacroOf(uint id)
    {
        var module = RaptureMacroModule.Instance();
        return module == null || id >= MacroSlots * 2 ? null : module->GetMacro(id / MacroSlots, id % MacroSlots);
    }

    // An emptied slot keeps its entry, named after the slot and without an icon.
    private static unsafe (string, uint) DescribeMacro(uint id)
    {
        var macro = MacroOf(id);
        return macro != null && macro->IsNotEmpty() ? (MacroName(macro, id), macro->IconId) : (MacroName(null, id), 0u);
    }

    // The macro's own name, or e.g. "Shared macro 12" when it has none.
    private static unsafe string MacroName(RaptureMacroModule.Macro* macro, uint id)
    {
        var name = macro != null ? macro->Name.ToString().Trim() : string.Empty;
        return name.Length > 0 ? name : $"{(id < MacroSlots ? "Individual" : "Shared")} macro {id % MacroSlots}";
    }

    private static string? CommandName(string? command) =>
        command?.Trim().Split(' ', 2)[0] is { Length: > 0 } name ? name : null;
}
