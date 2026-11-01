using System;
using System.Linq;
using Controller;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

using IconAtlasClient;
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

    [PluginService]
    private static IGameInteropProvider Interop { get; set; } = null!;

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("FlickWheel");
    private readonly ConfigWindow configWindow;
    private readonly PadBlocker blocker;
    private readonly WheelInput input;
    private readonly WheelOverlay overlay;
    private readonly IconCache icons;
    private readonly IFontHandle glyphFont;
    private readonly ChatLogToggle chatLog;
    private readonly IconPicker iconPicker;
    private readonly PadNav padNav;
    private readonly ICallGateProvider<(string Uid, string Name)[]> hotkeysIpc;
    private readonly ICallGateProvider<string, bool, object> holdIpc;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (config.Migrate())
            Save();

        // Modules need the overlay for their settings preview.
        var wheels = new WheelSet();
        blocker = new PadBlocker(Interop, Log);
        input = new WheelInput(Framework, KeyState, Gamepad, blocker, Condition, PlayerState, wheels, config, Save);
        icons = new IconCache(Data, Textures);
        glyphFont = PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(
            tk => tk.AddFontAwesomeIconFont(new SafeFontConfig { SizePx = 64 })));
        overlay = new WheelOverlay(input, wheels, icons, glyphFont, config, Save);

        var catalog = new JobCatalog(Data, Unlocks, PlayerState, config);
        var switcher = new JobSwitcher(Condition, Toasts);
        wheels.Add(new JobsModule(config, catalog, switcher, icons, overlay));
        chatLog = new ChatLogToggle(Framework);
        iconPicker = new IconPicker(PluginInterface, "FlickWheel");
        var shortcuts = new ShortcutsModule(config, Save, new ShortcutCatalog(Data, CommandManager, Buddies, chatLog), icons,
                                            overlay, iconPicker, new PluginIcons(PluginInterface));
        wheels.Add(shortcuts);

        configWindow = new ConfigWindow(config, Save, wheels, shortcuts, overlay, input, KeyState);
        windows.AddWindow(configWindow);
        padNav = new PadNav(Interop, Gamepad, Log, windows);

        // For Job Profiles: a button can hold one of these hotkeys open, without it needing a key.
        hotkeysIpc = PluginInterface.GetIpcProvider<(string Uid, string Name)[]>("FlickWheel.Hotkeys");
        hotkeysIpc.RegisterFunc(() => config.WheelCycles.Select(h => (h.Uid, h.Name.Length > 0 ? h.Name : "Unnamed")).ToArray());
        holdIpc = PluginInterface.GetIpcProvider<string, bool, object>("FlickWheel.Hold");
        holdIpc.RegisterAction(input.Hold);

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
        padNav.Update();
        windows.Draw();
        overlay.Draw();
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        hotkeysIpc.UnregisterFunc();
        holdIpc.UnregisterAction();
        PluginInterface.UiBuilder.Draw -= Draw;
        input.Dispose();
        blocker.Dispose();
        chatLog.Dispose();
        iconPicker.Dispose();
        PluginInterface.UiBuilder.OpenConfigUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= configWindow.Toggle;
        windows.RemoveAllWindows();
        padNav.Dispose();
        icons.Dispose();
        glyphFont.Dispose();
    }
}
