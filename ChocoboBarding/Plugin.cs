using System.Collections.Generic;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

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

#if DEBUG
    [PluginService]
    private static IAgentLifecycle AgentLifecycle { get; set; } = null!;

    private readonly DebugProbe debugProbe;
#endif

    private readonly WindowSystem windows = new("ChocoboBarding");
    private readonly BardingWindow bardingWindow;
    private readonly BardingQueue queue;

    public Plugin()
    {
        CommandManager.AddHandler(
            BardingCommandName,
            new CommandInfo(OnBardingCommand)
            {
                HelpMessage = "Put barding on your chocobo. /cbarding \"Sophic\" sets the full set; " +
                              "/cbarding \"Head\" \"Body\" \"Legs\" sets each slot (- keeps a slot, none removes it). " +
                              "No name opens the barding list."
            });

        queue = new BardingQueue(Framework, Log);

        bardingWindow = new BardingWindow(Data, Unlocks, Textures);
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

    private void OnBardingCommand(string command, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            bardingWindow.Toggle();
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
