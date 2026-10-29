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
    private static IGameInteropProvider Interop { get; set; } = null!;

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("HuntMap");
    private readonly HuntWindow window;
    private readonly MapMarkers markers;
    private readonly PadBlocker blocker;
    private readonly Arrivals arrivals;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        var marks = new Marks(Data);
        var checks = new Checks(config, Save);
        var bills = new Bills(Data, marks, checks);
        markers = new MapMarkers(Framework, GameGui, ClientState, config, Save, marks, bills, checks);
        arrivals = new Arrivals(Framework, ClientState, Objects, config, bills, checks, markers);
        window = new HuntWindow(marks, bills, checks, config, Save, markers, ClientState, Objects, GameGui, Gamepad, Textures);
        windows.AddWindow(window);
        blocker = new PadBlocker(Interop, Log);

        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += window.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += window.OpenSettings;
        Framework.Update += OnUpdate;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Show where hunt marks can spawn.\n/huntmap <name> → Search for it.",
        });
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Length > 0)
            window.OpenSearch(args.Trim());
        else
            window.Toggle();
    }

    private void OnUpdate(IFramework framework) =>
        blocker.Active = window.IsOpen && window.Focused && DateTime.UtcNow - window.LastDrawn < DrawnWithin;

    private void Save() => PluginInterface.SavePluginConfig(config);

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= window.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= window.OpenSettings;
        blocker.Dispose();
        arrivals.Dispose();
        markers.Dispose();
        windows.RemoveAllWindows();
    }
}
