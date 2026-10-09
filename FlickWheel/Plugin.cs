using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

using Shortcuts;

namespace FlickWheel;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/flick";

    [PluginService]
    private static IDalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService]
    private static ICommandManager CommandManager { get; set; } = null!;

    [PluginService]
    private static IFramework Framework { get; set; } = null!;

    [PluginService]
    private static IKeyState KeyState { get; set; } = null!;

    [PluginService]
    private static IGamepadState Gamepad { get; set; } = null!;

    [PluginService]
    private static ICondition Condition { get; set; } = null!;

    [PluginService]
    private static IPlayerState PlayerState { get; set; } = null!;

    [PluginService]
    private static IDataManager Data { get; set; } = null!;

    [PluginService]
    private static IUnlockState Unlocks { get; set; } = null!;

    [PluginService]
    private static ITextureProvider Textures { get; set; } = null!;

    [PluginService]
    private static IToastGui Toasts { get; set; } = null!;

    [PluginService]
    private static IBuddyList Buddies { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("FlickWheel");
    private readonly ConfigWindow configWindow;
    private readonly WheelInput input;
    private readonly WheelOverlay overlay;
    private readonly IconCache icons;
    private readonly ChatLogToggle chatLog;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (config.Migrate())
            Save();

        // Modules need the overlay for their settings preview.
        var wheels = new WheelSet(config);
        input = new WheelInput(Framework, KeyState, Gamepad, Condition, PlayerState, wheels, config, Save);
        icons = new IconCache(Data, Textures);
        overlay = new WheelOverlay(input, wheels, icons, config, Save);

        var catalog = new JobCatalog(Data, Unlocks, PlayerState, config);
        var switcher = new JobSwitcher(Condition, Toasts);
        wheels.Add(new JobsModule(config, catalog, switcher, icons, overlay));
        chatLog = new ChatLogToggle(Framework);
        wheels.Add(new ShortcutsModule(config, Save, new ShortcutCatalog(Data, CommandManager, Buddies, chatLog), icons, overlay));

        configWindow = new ConfigWindow(config, Save, wheels, overlay, KeyState);
        windows.AddWindow(configWindow);

        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += configWindow.Toggle;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Flick Wheel settings. Hold your hotkey (Ctrl+, by default) and flick the right stick toward an entry.\n" +
                          "/flick chat → Hide or show the chat log.",
        });
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("chat", StringComparison.OrdinalIgnoreCase))
            chatLog.Toggle();
        else
            configWindow.Toggle();
    }

    private void Save() => PluginInterface.SavePluginConfig(config);

    private void Draw()
    {
        windows.Draw();
        overlay.Draw();
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= Draw;
        input.Dispose();
        chatLog.Dispose();
        PluginInterface.UiBuilder.OpenConfigUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= configWindow.Toggle;
        windows.RemoveAllWindows();
        icons.Dispose();
    }
}
