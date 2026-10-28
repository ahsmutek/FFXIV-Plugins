using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace PlaceFinder;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/place";
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
    private readonly WindowSystem windows = new("PlaceFinder");
    private readonly FinderWindow finder;
    private readonly PadBlocker blocker;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        finder = new FinderWindow(new Places(Data), config, Save, ClientState, GameGui, Gamepad, Textures);
        windows.AddWindow(finder);
        blocker = new PadBlocker(Interop, Log);

        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += finder.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += finder.OpenSettings;
        Framework.Update += OnUpdate;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Find a named place on the map.\n/place <name> → Search for it.",
        });
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Length > 0)
            finder.OpenSearch(args.Trim());
        else
            finder.Toggle();
    }

    private void OnUpdate(IFramework framework) =>
        blocker.Active = finder.IsOpen && DateTime.UtcNow - finder.LastDrawn < DrawnWithin;

    private void Save() => PluginInterface.SavePluginConfig(config);

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= finder.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= finder.OpenSettings;
        blocker.Dispose();
        windows.RemoveAllWindows();
    }
}
