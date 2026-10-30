using System;
using Controller;
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

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    [PluginService]
    private static IGamepadState Gamepad { get; set; } = null!;

    [PluginService]
    private static IGameInteropProvider Interop { get; set; } = null!;

    [PluginService]
    private static IClientState ClientState { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("MinimapZoom");
    private readonly ConfigWindow configWindow;
    private readonly ZoomExtender extender;
    private readonly PadNav padNav;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        extender = new ZoomExtender(Framework, AddonLifecycle, ClientState, config, Save, Log);

        configWindow = new ConfigWindow(config, Save, extender);
        windows.AddWindow(configWindow);
        padNav = new PadNav(Interop, Gamepad, Log, windows);
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += configWindow.Toggle;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Minimap Zoom settings.\n/minimapzoom reset → Back to the game's furthest zoom (1.25).",
        });
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("reset", StringComparison.OrdinalIgnoreCase))
            extender.ResetToGame();
        else
            configWindow.Toggle();
    }

    private void Draw()
    {
        padNav.Update();
        windows.Draw();
    }

    private void Save() => PluginInterface.SavePluginConfig(config);

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= configWindow.Toggle;
        windows.RemoveAllWindows();
        padNav.Dispose();
        extender.Dispose();
    }
}
