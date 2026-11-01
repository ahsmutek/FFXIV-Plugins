using System;
using Controller;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Icons;
using Shortcuts;

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
    private static IPartyList PartyList { get; set; } = null!;

    [PluginService]
    private static IDataManager Data { get; set; } = null!;

    [PluginService]
    private static ITextureProvider Textures { get; set; } = null!;

    [PluginService]
    private static IClientState ClientState { get; set; } = null!;

    [PluginService]
    private static IGamepadState Gamepad { get; set; } = null!;

    [PluginService]
    private static IKeyState KeyState { get; set; } = null!;

    [PluginService]
    private static IGameInteropProvider Interop { get; set; } = null!;

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    [PluginService]
    private static IBuddyList Buddies { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("JobProfiles");
    private readonly ConfigWindow configWindow;
    private readonly IconCache icons;
    private readonly MacroRunner runner;
    private readonly ProfileWatcher watcher;
    private readonly BindingWatcher bindings;
    private readonly PadBlocker blocker;
    private readonly ButtonsTab buttonsTab;
    private readonly PadNav padNav;

    public Plugin()
    {
        TablerIcons.Init(Textures);
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (config.Migrate())
            Save();

        var groups = new JobGroups(Data);
        var duties = new Duties(ClientState, Data);
        var catalog = new ShortcutCatalog(Data, CommandManager, Buddies);
        icons = new IconCache(Data, Textures);
        runner = new MacroRunner(Framework, Condition, catalog);
        watcher = new ProfileWatcher(Framework, PlayerState, config, groups, runner);
        var flickWheel = new FlickWheelLink(PluginInterface);
        bindings = new BindingWatcher(Framework, Gamepad, KeyState, PlayerState, Condition, PartyList, config, groups, duties, runner, flickWheel);
        blocker = new PadBlocker(Interop, config, bindings, Log);

        var widgets = new Widgets(catalog, new ShortcutPicker(catalog, icons), icons);
        buttonsTab = new ButtonsTab(config, duties, bindings, widgets, flickWheel, KeyState);
        configWindow = new ConfigWindow(config, Save, groups, PlayerState, Textures, watcher.ApplyNow, widgets, buttonsTab);
        windows.AddWindow(configWindow);
        padNav = new PadNav(Interop, Gamepad, Log, windows);
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += configWindow.Toggle;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Job Profiles settings.\n" +
                          "/jobprofiles apply → Switch to your current job's HUD layout and run its actions again.",
        });
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("apply", StringComparison.OrdinalIgnoreCase))
            watcher.ApplyNow();
        else
            configWindow.Toggle();
    }

    private void Draw()
    {
        padNav.Paused = buttonsTab.Recording;
        padNav.Update();
        bindings.Navigating = padNav.Active;
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
        blocker.Dispose();
        bindings.Dispose();
        watcher.Dispose();
        runner.Dispose();
        icons.Dispose();
    }
}
