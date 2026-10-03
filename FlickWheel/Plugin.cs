using System;
using System.IO;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FlickWheel;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/flick";
    // The plugin used to be called Job Wheel; its command still works.
    private const string OldCommandName = "/jobwheel";
    private const string OldConfigFile = "JobWheel.json";

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
        config = LoadConfig();
        if (config.Migrate())
            Save();

        // The core first; modules are added once the overlay exists, since their settings use its preview.
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
        CommandManager.AddHandler(OldCommandName, new CommandInfo(OnCommand)
        {
            ShowInHelp = false,
        });
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("chat", StringComparison.OrdinalIgnoreCase))
            chatLog.Toggle();
        else
            configWindow.Toggle();
    }

    // First load after the rename from Job Wheel: carry the old settings over. The old
    // file names the old assembly in its "$type" entries, so those are rewritten and the
    // result saved as this plugin's config, then loaded as usual. The old file is kept.
    private static Configuration LoadConfig()
    {
        if (PluginInterface.GetPluginConfig() is Configuration saved)
            return saved;

        var oldPath = Path.Combine(PluginInterface.ConfigFile.DirectoryName!, OldConfigFile);
        if (File.Exists(oldPath))
        {
            try
            {
                var text = File.ReadAllText(oldPath)
                               .Replace("JobWheel.", "FlickWheel.")
                               .Replace(", JobWheel", ", FlickWheel");
                File.WriteAllText(PluginInterface.ConfigFile.FullName, text);
                if (PluginInterface.GetPluginConfig() is Configuration migrated)
                    return migrated;
            }
            catch (Exception)
            {
                // An unreadable old file just means starting from defaults.
            }
        }

        return new Configuration();
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
        CommandManager.RemoveHandler(OldCommandName);
        PluginInterface.UiBuilder.Draw -= Draw;
        input.Dispose();
        chatLog.Dispose();
        PluginInterface.UiBuilder.OpenConfigUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= configWindow.Toggle;
        windows.RemoveAllWindows();
        icons.Dispose();
    }
}
