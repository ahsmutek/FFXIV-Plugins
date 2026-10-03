using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace JobProfiles;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/jobprofiles";

    [PluginService]
    private static IDalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService]
    private static ICommandManager CommandManager { get; set; } = null!;

    [PluginService]
    private static IFramework Framework { get; set; } = null!;

    [PluginService]
    private static IPlayerState PlayerState { get; set; } = null!;

    [PluginService]
    private static ICondition Condition { get; set; } = null!;

    [PluginService]
    private static IDataManager Data { get; set; } = null!;

    [PluginService]
    private static ITextureProvider Textures { get; set; } = null!;

    [PluginService]
    private static IToastGui Toasts { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("JobProfiles");
    private readonly ConfigWindow configWindow;
    private readonly ProfileWatcher watcher;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (config.Migrate())
            Save();

        var groups = new JobGroups(Data);
        watcher = new ProfileWatcher(Framework, PlayerState, Condition, config, groups, new Actions(Toasts));

        configWindow = new ConfigWindow(config, Save, groups, PlayerState, Textures, watcher.ApplyNow);
        windows.AddWindow(configWindow);
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += configWindow.Toggle;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Job Profiles settings.\n" +
                          "/jobprofiles apply → Apply your current job's HUD layout and macro again.",
        });
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("apply", StringComparison.OrdinalIgnoreCase))
            watcher.ApplyNow();
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
        watcher.Dispose();
    }
}
