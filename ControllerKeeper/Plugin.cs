using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace ControllerKeeper;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/controllerkeeper";

    [PluginService]
    private static IDalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService]
    private static ICommandManager CommandManager { get; set; } = null!;

    [PluginService]
    private static IFramework Framework { get; set; } = null!;

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("ControllerKeeper");
    private readonly ConfigWindow configWindow;
    private readonly PadKeeper keeper;
    private readonly RescanFilter filter;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        keeper = new PadKeeper(Framework, Log, config);
        filter = new RescanFilter(Framework, Log, config);

        configWindow = new ConfigWindow(config, Save, keeper, filter);
        windows.AddWindow(configWindow);
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += configWindow.Toggle;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open ControllerKeeper settings: pick the controller to keep, see skipped rescans.\n" +
                          "/controllerkeeper reconnect → Make the game rescan its controllers, like replugging the pad.",
        });
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("reconnect", System.StringComparison.OrdinalIgnoreCase))
            filter.Reconnect();
        else
            configWindow.Toggle();
    }

    private void Save() => PluginInterface.SavePluginConfig(config);

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= configWindow.Toggle;
        windows.RemoveAllWindows();
        filter.Dispose();
        keeper.Dispose();
    }
}
