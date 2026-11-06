using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Controller;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Sundries;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/sundries";

    [PluginService]
    private static IDalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService]
    private static ICommandManager CommandManager { get; set; } = null!;

    [PluginService]
    private static IGameGui GameGui { get; set; } = null!;

    [PluginService]
    private static IObjectTable Objects { get; set; } = null!;

    [PluginService]
    private static IFramework Framework { get; set; } = null!;

    [PluginService]
    private static IAddonLifecycle AddonLifecycle { get; set; } = null!;

    [PluginService]
    private static IClientState ClientState { get; set; } = null!;

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    [PluginService]
    private static IGamepadState Gamepad { get; set; } = null!;

    [PluginService]
    private static IGameInteropProvider Interop { get; set; } = null!;

    private readonly Configuration config;
    private readonly List<IModule> modules;
    private readonly WindowSystem windows = new("Sundries");
    private readonly SettingsWindow settings;
    private readonly PadNav padNav;

    public Plugin()
    {
        if (PluginInterface.GetPluginConfig() is Configuration saved)
        {
            config = saved;
        }
        else
        {
            config = new Configuration();
            ImportMinimapZoom();
            Save();
        }

        modules =
        [
            new GuideHighlight(GameGui, Objects, config, Save),
            new MinimapZoom(Framework, AddonLifecycle, ClientState, config, Save, Log),
        ];

        settings = new SettingsWindow(modules, Save);
        windows.AddWindow(settings);
        padNav = new PadNav(Interop, Gamepad, Log, windows);
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += settings.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += settings.Toggle;

        CommandManager.AddHandler(CommandName, new CommandInfo((_, _) => settings.Toggle())
        {
            HelpMessage = "Open Sundries settings.",
        });
    }

    // Minimap Zoom was its own plugin before; its settings carry over the first time Sundries loads.
    private void ImportMinimapZoom()
    {
        var file = Path.Combine(PluginInterface.ConfigFile.DirectoryName ?? "", "MinimapZoom.json");
        try
        {
            if (!File.Exists(file))
                return;
            var old = JsonDocument.Parse(File.ReadAllText(file)).RootElement;
            if (old.TryGetProperty("Enabled", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
                config.MinimapZoomEnabled = enabled.GetBoolean();
            if (old.TryGetProperty("Minimum", out var minimum) && minimum.ValueKind == JsonValueKind.Number)
                config.MinimapMinimum = minimum.GetSingle();
            if (old.TryGetProperty("Zoom", out var zoom) && zoom.ValueKind == JsonValueKind.Number)
                config.MinimapZoom = zoom.GetSingle();
            Log.Information("Imported Minimap Zoom settings.");
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warning(e, "Couldn't import Minimap Zoom settings.");
        }
    }

    private void Draw()
    {
        foreach (var module in modules)
            module.Draw();
        padNav.Update();
        windows.Draw();
    }

    private void Save() => PluginInterface.SavePluginConfig(config);

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= settings.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= settings.Toggle;
        windows.RemoveAllWindows();
        padNav.Dispose();
        foreach (var module in modules)
            module.Dispose();
    }
}
