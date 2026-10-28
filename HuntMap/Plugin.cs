using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace HuntMap;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/huntmap";
    // The window stops drawing in cutscenes and with the UI hidden; the pad goes back to the game then.
    private static readonly TimeSpan DrawnWithin = TimeSpan.FromMilliseconds(200);

    [PluginService]
    private static IDalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService]
    private static ICommandManager CommandManager { get; set; } = null!;

    [PluginService]
    private static IFramework Framework { get; set; } = null!;

    [PluginService]
    private static IGamepadState Gamepad { get; set; } = null!;

    [PluginService]
    private static IClientState ClientState { get; set; } = null!;

    [PluginService]
    private static IObjectTable Objects { get; set; } = null!;

    [PluginService]
    private static IGameGui GameGui { get; set; } = null!;

    [PluginService]
    private static IDataManager Data { get; set; } = null!;

    [PluginService]
    private static ITextureProvider Textures { get; set; } = null!;

    [PluginService]
    private static IAddonLifecycle AddonLifecycle { get; set; } = null!;

    [PluginService]
    private static IGameInteropProvider Interop { get; set; } = null!;

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("HuntMap");
    private readonly HuntWindow window;
    private readonly MapMarkers markers;
    private readonly PadBlocker blocker;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        var bills = new Bills(Data, new Marks());
        markers = new MapMarkers(Framework, AddonLifecycle, bills, config);
        window = new HuntWindow(bills, config, Save, markers, ClientState, Objects, GameGui, Gamepad, Textures);
        windows.AddWindow(window);
        blocker = new PadBlocker(Interop, Log);

        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += window.Toggle;
        Framework.Update += OnUpdate;

        CommandManager.AddHandler(CommandName, new CommandInfo((_, _) => window.Toggle())
        {
            HelpMessage = "Show where the targets of your Elite Mark Bills can spawn.",
        });
    }

    private void OnUpdate(IFramework framework) =>
        blocker.Active = window.IsOpen && DateTime.UtcNow - window.LastDrawn < DrawnWithin;

    private void Save() => PluginInterface.SavePluginConfig(config);

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= window.Toggle;
        blocker.Dispose();
        markers.Dispose();
        windows.RemoveAllWindows();
    }
}
