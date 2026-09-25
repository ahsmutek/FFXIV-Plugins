using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Game.Agent;
using Dalamud.Game.Agent.AgentArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace ChocoboBarding;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/cbdebug";
    private const string BardingCommandName = "/cbarding";

    [PluginService]
    private static IAgentLifecycle AgentLifecycle { get; set; } = null!;

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

    private readonly WindowSystem windows = new("ChocoboBarding");
    private readonly BardingWindow bardingWindow;

    private bool enabled = true;

    public Plugin()
    {
        AgentLifecycle.RegisterListener(
            AgentEvent.PreReceiveEvent,
            AgentId.Buddy,
            OnBuddyEvent);

        AgentLifecycle.RegisterListener(
            AgentEvent.PreReceiveEventWithResult,
            AgentId.Buddy,
            OnBuddyEvent);

        CommandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                HelpMessage = "Log Buddy/Companion agent events to /xllog. Usage: /cbdebug [on|off|status|unlocks|help]"
            });

        CommandManager.AddHandler(
            BardingCommandName,
            new CommandInfo(OnBardingCommand)
            {
                HelpMessage = "Put a full barding set on your chocobo. Usage: /cbarding \"Sophic Barding\" (no name opens the barding list)"
            });

        bardingWindow = new BardingWindow(Data, Unlocks, Textures);
        windows.AddWindow(bardingWindow);
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += bardingWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += bardingWindow.Toggle;

        Log.Information("Chocobo Barding loaded. Logging Buddy agent ReceiveEvent calls.");
    }

    public void Dispose()
    {
        AgentLifecycle.UnregisterListener(OnBuddyEvent);
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(BardingCommandName);
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

        if (Barding.Find(Data, args) is not { } row)
        {
            var suggestions = Barding.Suggest(Data, Unlocks, args);
            Chat.PrintError(suggestions.Length > 0
                ? $"[Chocobo Barding] No barding named \"{args.Trim('"', ' ')}\". Did you mean: {string.Join(", ", suggestions)}?"
                : $"[Chocobo Barding] No barding named \"{args.Trim('"', ' ')}\".");
            return;
        }

        if (!Unlocks.IsBuddyEquipUnlocked(row))
        {
            Chat.PrintError($"[Chocobo Barding] You don't own {row.Name.ExtractText()}.");
            return;
        }

        if (!Barding.Apply(row.RowId))
        {
            Chat.PrintError("[Chocobo Barding] Companion agent is not available.");
            return;
        }

        Log.Information($"Requested {row.Name.ExtractText()} (BuddyEquip #{row.RowId}) for all slots.");
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "":
            case "status":
                Chat.Print($"[CB Debug] Logging is {(enabled ? "ON" : "OFF")}.");
                break;

            case "on":
                enabled = true;
                Chat.Print("[CB Debug] Logging ON.");
                break;

            case "off":
                enabled = false;
                Chat.Print("[CB Debug] Logging OFF.");
                break;

            case "unlocks":
                var owned = Barding.Owned(Data, Unlocks).Select(row => $"#{row.RowId} {row.Name.ExtractText()}").ToList();
                Log.Information($"[CBDebug] Owned barding ({owned.Count}): {string.Join(", ", owned)}");
                Chat.Print($"[CB Debug] {owned.Count} owned barding written to /xllog.");
                break;

            case "help":
                Chat.Print("[CB Debug] 1) /xllog  2) Companion → Appearance  3) change one barding piece  4) copy the [CBDebug] log lines.");
                break;

            default:
                Chat.Print("[CB Debug] Usage: /cbdebug [on|off|status|unlocks|help]");
                break;
        }
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "<null>",
            string s => $"\"{s}\"",
            byte[] bytes => $"byte[{bytes.Length}] {Convert.ToHexString(bytes)}",
            _ => value.ToString() ?? "<null>"
        };
    }

    private void OnBuddyEvent(AgentEvent type, AgentArgs args)
    {
        if (!enabled || args is not AgentReceiveEventArgs e)
            return;

        var sb = new StringBuilder();
        sb.Append("[CBDebug] ")
          .Append(type)
          .Append(" | Agent=")
          .Append(args.AgentId)
          .Append(" | EventKind=")
          .Append(e.EventKind)
          .Append(" | ValueCount=")
          .Append(e.ValueCount);

        var index = 0;
        var values = new List<object?>();
        foreach (var value in e.AtkValueEnumerable)
        {
            sb.AppendLine();
            sb.Append("  [")
              .Append(index++)
              .Append("] ")
              .Append(value.ValueType)
              .Append(" = ");

            if (value.IsNull)
            {
                sb.Append("<null ptr>");
                continue;
            }

            try
            {
                var raw = value.GetValue();
                values.Add(raw);
                sb.Append(FormatValue(raw));
            }
            catch (NotImplementedException)
            {
                sb.Append("<unsupported value type>");
            }
            catch (Exception ex)
            {
                sb.Append("<read failed: ")
                  .Append(ex.GetType().Name)
                  .Append('>');
            }
        }

        // Event 20 = barding picked: [slot, BuddyEquip row]
        if (values is [20, int slot, int rowId])
        {
            var row = Data.GetExcelSheet<BuddyEquip>().GetRowOrDefault((uint)rowId);
            sb.AppendLine();
            sb.Append("  => slot ")
              .Append(slot)
              .Append(", BuddyEquip #")
              .Append(rowId)
              .Append(" = ")
              .Append(row is { } r ? $"\"{r.Name.ExtractText()}\"" : "<not found>");
        }

        Log.Information(sb.ToString());
    }
}
