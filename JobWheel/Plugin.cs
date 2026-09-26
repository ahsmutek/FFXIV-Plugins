using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace JobWheel;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/jobwheel";

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

    private readonly Configuration config;
    private readonly WindowSystem windows = new("JobWheel");
    private readonly ConfigWindow configWindow;
    private readonly WheelInput input;
    private readonly WheelOverlay overlay;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        var catalog = new JobCatalog(Data, Unlocks, PlayerState, config);
        var switcher = new JobSwitcher(Condition, Toasts);
        input = new WheelInput(Framework, KeyState, Gamepad, Condition, PlayerState, catalog, switcher, config);
        overlay = new WheelOverlay(input, Textures, config);

        configWindow = new ConfigWindow(config, Save, catalog, Textures, KeyState);
        windows.AddWindow(configWindow);

        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += configWindow.Toggle;

        CommandManager.AddHandler(CommandName, new CommandInfo((_, _) => configWindow.Toggle())
        {
            HelpMessage = "Open Job Wheel settings. Hold Ctrl+, and flick the right stick to switch jobs.",
        });
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
        input.Dispose();
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= configWindow.Toggle;
        windows.RemoveAllWindows();
    }
}
