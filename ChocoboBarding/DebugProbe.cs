#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Game.Agent;
using Dalamud.Game.Agent.AgentArgTypes;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace ChocoboBarding;

internal sealed class DebugProbe : IDisposable
{
    private const string CommandName = "/cbdebug";

    private readonly IAgentLifecycle agentLifecycle;
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly IChatGui chat;
    private readonly IDataManager data;
    private readonly IUnlockState unlocks;

    private bool enabled = true;

    public DebugProbe(IAgentLifecycle agentLifecycle, ICommandManager commandManager, IPluginLog log,
                      IChatGui chat, IDataManager data, IUnlockState unlocks)
    {
        this.agentLifecycle = agentLifecycle;
        this.commandManager = commandManager;
        this.log = log;
        this.chat = chat;
        this.data = data;
        this.unlocks = unlocks;

        agentLifecycle.RegisterListener(AgentEvent.PreReceiveEvent, AgentId.Buddy, OnBuddyEvent);
        agentLifecycle.RegisterListener(AgentEvent.PreReceiveEventWithResult, AgentId.Buddy, OnBuddyEvent);

        commandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                HelpMessage = "Log Buddy/Companion agent events to /xllog. Usage: /cbdebug [on|off|status|unlocks|help]"
            });
    }

    public void Dispose()
    {
        agentLifecycle.UnregisterListener(OnBuddyEvent);
        commandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "":
            case "status":
                chat.Print($"[CB Debug] Logging is {(enabled ? "ON" : "OFF")}.");
                break;

            case "on":
                enabled = true;
                chat.Print("[CB Debug] Logging ON.");
                break;

            case "off":
                enabled = false;
                chat.Print("[CB Debug] Logging OFF.");
                break;

            case "unlocks":
                var owned = Barding.Owned(data, unlocks).Select(row => $"#{row.RowId} {row.Name.ExtractText()}").ToList();
                log.Information($"[CBDebug] Owned barding ({owned.Count}): {string.Join(", ", owned)}");
                chat.Print($"[CB Debug] {owned.Count} owned barding written to /xllog.");
                break;

            case "help":
                chat.Print("[CB Debug] 1) /xllog  2) Companion → Appearance  3) change one barding piece  4) copy the [CBDebug] log lines.");
                break;

            default:
                chat.Print("[CB Debug] Usage: /cbdebug [on|off|status|unlocks|help]");
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

        if (values is [20, int slot, int rowId])
        {
            var row = data.GetExcelSheet<BuddyEquip>().GetRowOrDefault((uint)rowId);
            sb.AppendLine();
            sb.Append("  => slot ")
              .Append(slot)
              .Append(", BuddyEquip #")
              .Append(rowId)
              .Append(" = ")
              .Append(row is { } r ? $"\"{r.Name.ExtractText()}\"" : "<not found>");
        }

        log.Information(sb.ToString());
    }
}
#endif
