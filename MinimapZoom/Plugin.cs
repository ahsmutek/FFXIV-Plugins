using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace MinimapZoom;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/minimapzoom";

    [PluginService]
    private static IDalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService]
    private static ICommandManager CommandManager { get; set; } = null!;

    [PluginService]
    private static IFramework Framework { get; set; } = null!;

    [PluginService]
    private static IAddonLifecycle AddonLifecycle { get; set; } = null!;

#if DEBUG
    [PluginService]
    private static IChatGui Chat { get; set; } = null!;

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    private readonly DebugProbe debugProbe;
#endif

    private readonly Configuration config;
    private readonly WindowSystem windows = new("MinimapZoom");
    private readonly ConfigWindow configWindow;
    private readonly ZoomExtender extender;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        extender = new ZoomExtender(Framework, AddonLifecycle, config, Save);

        configWindow = new ConfigWindow(config, Save, extender);
        windows.AddWindow(configWindow);
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += configWindow.Toggle;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Minimap Zoom settings.\n/minimapzoom reset → Back to the game's furthest zoom (1.25).",
        });

#if DEBUG
        debugProbe = new DebugProbe(CommandManager, Framework, Chat, Log, AddonLifecycle);
#endif
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("reset", StringComparison.OrdinalIgnoreCase))
            extender.ResetToGame();
        else
            configWindow.Toggle();
    }

    private void Save() => PluginInterface.SavePluginConfig(config);

    public void Dispose()
    {
#if DEBUG
        debugProbe.Dispose();
#endif
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= configWindow.Toggle;
        windows.RemoveAllWindows();
        extender.Dispose();
    }
}
