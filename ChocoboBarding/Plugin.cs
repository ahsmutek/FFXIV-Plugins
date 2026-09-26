using System.Collections.Generic;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace ChocoboBarding;

public sealed class Plugin : IDalamudPlugin
{
    private const string BardingCommandName = "/cbarding";

    [PluginService]
    private static ICommandManager CommandManager { get; set; } = null!;

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    [PluginService]
    private static IChatGui Chat { get; set; } = null!;

    [PluginService]
    private static IDataManager Data { get; set; } = null!;

    [PluginService]
    private static IUnlockState Unlocks { get; set; } = null!;

    [PluginService]
    private static IDalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService]
    private static ITextureProvider Textures { get; set; } = null!;

    [PluginService]
    private static IFramework Framework { get; set; } = null!;

    [PluginService]
    private static IPlayerState PlayerState { get; set; } = null!;

#if DEBUG
    [PluginService]
    private static IAgentLifecycle AgentLifecycle { get; set; } = null!;

    private readonly DebugProbe debugProbe;
#endif

    private const string JobKeyword = "job";
    private const string GreensKeyword = "greens";

    // Item row for Gysahl Greens, which summon the chocobo companion.
    private const uint GysahlGreensItemId = 4868;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("ChocoboBarding");
    private readonly BardingWindow bardingWindow;
    private readonly BardingQueue queue;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        CommandManager.AddHandler(
            BardingCommandName,
            new CommandInfo(OnBardingCommand)
            {
                HelpMessage = "Put barding on your chocobo. /cbarding \"Sophic\" sets the full set; " +
                              "/cbarding \"Head\" \"Body\" \"Legs\" sets each slot (- keeps a slot, none removes it). " +
                              "/cbarding job uses the barding picked for your current job. " +
                              "/cbarding greens uses Gysahl Greens to summon your chocobo. " +
                              "No name opens the barding list."
            });

        queue = new BardingQueue(Framework, PlayerState, Log);

        bardingWindow = new BardingWindow(Data, Unlocks, Textures, PlayerState, config, Save);
        windows.AddWindow(bardingWindow);
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += bardingWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += bardingWindow.Toggle;

#if DEBUG
        debugProbe = new DebugProbe(AgentLifecycle, CommandManager, Log, Chat, Data, Unlocks);
#endif
    }

    public void Dispose()
    {
#if DEBUG
        debugProbe.Dispose();
#endif
        CommandManager.RemoveHandler(BardingCommandName);
        queue.Dispose();
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= bardingWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= bardingWindow.Toggle;
        windows.RemoveAllWindows();
    }

    private void Save() => PluginInterface.SavePluginConfig(config);

    private void OnBardingCommand(string command, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            bardingWindow.Toggle();
            return;
        }

        if (args.Trim().Equals(JobKeyword, System.StringComparison.OrdinalIgnoreCase))
        {
            ApplyJobBarding();
            return;
        }

        if (args.Trim().Equals(GreensKeyword, System.StringComparison.OrdinalIgnoreCase))
        {
            UseGreens();
            return;
        }

        var tokens = Barding.Tokenize(args);
        if (tokens.Count is not (1 or 3))
        {
            Chat.PrintError("[Chocobo Barding] Use 1 name (full set) or 3 (head, body, legs), e.g. /cbarding \"Suzaku\" \"Windswept\" -");
            return;
        }

        // Check every slot before changing anything.
        var choices = new List<SlotChoice>();
        var described = new List<string>();
        foreach (var token in tokens)
        {
            var slotLabel = tokens.Count == 3 ? $"{Barding.SlotNames[choices.Count]}: " : string.Empty;
            if (!TryResolve(token, slotLabel, out var choice, out var description))
                return;

            choices.Add(choice);
            described.Add(description);
        }

        if (choices.Count == 1)
        {
            choices.AddRange([choices[0], choices[0]]);
            described.AddRange([described[0], described[0]]);
        }

        queue.Replace(choices);
        Log.Information($"Requested head={described[0]}, body={described[1]}, legs={described[2]}.");
    }

    /// <summary>Uses one Gysahl Greens, as if clicked in the inventory, and says how many are left.</summary>
    private static unsafe void UseGreens()
    {
        var inventory = InventoryManager.Instance();
        var agent = AgentInventoryContext.Instance();
        if (inventory == null || agent == null)
            return;

        var count = inventory->GetInventoryItemCount(GysahlGreensItemId);
        if (count <= 0)
        {
            Chat.PrintError("[Chocobo Barding] You don't have any Gysahl Greens.");
            return;
        }

        agent->UseItem(GysahlGreensItemId);
        Chat.Print($"[Chocobo Barding] Gysahl Greens left: {count - 1}");
    }

    private void ApplyJobBarding()
    {
        if (!PlayerState.IsLoaded)
            return;

        var job = PlayerState.ClassJob;
        if (Barding.ForJob(config, Data, job.RowId) is not { } choices)
        {
            Chat.PrintError($"[Chocobo Barding] No barding set for {Barding.JobName(job.Value)}. Pick one in /cbarding → Jobs.");
            return;
        }

        // Check every slot before changing anything.
        var sheet = Data.GetExcelSheet<BuddyEquip>();
        for (var slot = 0; slot < choices.Length; slot++)
        {
            if (choices[slot].Action != SlotAction.Set)
                continue;

            if (sheet.GetRowOrDefault(choices[slot].RowId) is not { } row || !Unlocks.IsBuddyEquipUnlocked(row))
            {
                Chat.PrintError($"[Chocobo Barding] {Barding.SlotNames[slot]}: You don't own the barding picked for {Barding.JobName(job.Value)}.");
                return;
            }
        }

        queue.Replace(choices);
        Log.Information($"Requested barding for {Barding.JobName(job.Value)}: {string.Join(", ", choices)}.");
    }

    private static bool TryResolve(string token, string slotLabel, out SlotChoice choice, out string description)
    {
        choice = SlotChoice.Keep;
        description = token;

        if (token == Barding.KeepToken)
            return true;

        if (token.Equals(Barding.RemoveToken, System.StringComparison.OrdinalIgnoreCase))
        {
            choice = SlotChoice.Remove;
            return true;
        }

        if (Barding.Find(Data, token) is not { } row)
        {
            var suggestions = Barding.Suggest(Data, Unlocks, token);
            Chat.PrintError(suggestions.Length > 0
                ? $"[Chocobo Barding] {slotLabel}No barding named \"{token}\". Did you mean: {string.Join(", ", suggestions)}?"
                : $"[Chocobo Barding] {slotLabel}No barding named \"{token}\".");
            return false;
        }

        if (!Unlocks.IsBuddyEquipUnlocked(row))
        {
            Chat.PrintError($"[Chocobo Barding] {slotLabel}You don't own {row.Name.ExtractText()}.");
            return false;
        }

        choice = SlotChoice.Set(row.RowId);
        description = $"{row.Name.ExtractText()} (#{row.RowId})";
        return true;
    }
}
