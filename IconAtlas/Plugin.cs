using Controller;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace IconAtlas;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/iconatlas";

    [PluginService]
    private static IDalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService]
    private static ICommandManager CommandManager { get; set; } = null!;

    [PluginService]
    private static ITextureProvider Textures { get; set; } = null!;

    [PluginService]
    private static IGamepadState Gamepad { get; set; } = null!;

    [PluginService]
    private static IGameInteropProvider Interop { get; set; } = null!;

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("IconAtlas");
    private readonly AtlasWindow atlas;
    private readonly PadNav padNav;
    // Other plugins call IconAtlas.Pick(token, purpose, current icon) and hear back on IconAtlas.Picked(token, icon).
    // PickWithPlugins(token, purpose, current icon, current plugin) also offers plugins' own icons, answered on
    // PickedPlugin(token, plugin's internal name).
    private readonly ICallGateProvider<string, string, uint, object> pick;
    private readonly ICallGateProvider<string, string, uint, string, object> pickWithPlugins;
    private readonly ICallGateProvider<string, uint, object> picked;
    private readonly ICallGateProvider<string, string, object> pickedPlugin;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        atlas = new AtlasWindow(config, Save, Textures, PluginInterface);
        windows.AddWindow(atlas);
        padNav = new PadNav(Interop, Gamepad, Log, windows);

        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenMainUi += atlas.Toggle;

        pick = PluginInterface.GetIpcProvider<string, string, uint, object>("IconAtlas.Pick");
        pickWithPlugins = PluginInterface.GetIpcProvider<string, string, uint, string, object>("IconAtlas.PickWithPlugins");
        picked = PluginInterface.GetIpcProvider<string, uint, object>("IconAtlas.Picked");
        pickedPlugin = PluginInterface.GetIpcProvider<string, string, object>("IconAtlas.PickedPlugin");
        pick.RegisterAction(atlas.Pick);
        pickWithPlugins.RegisterAction(atlas.PickWithPlugins);
        atlas.Picked += OnPicked;
        atlas.PickedPlugin += OnPickedPlugin;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Browse game icons by ID range.\n/iconatlas <id> → Show that icon.\n/iconatlas <text> → Search the ranges.",
        });
    }

    private void OnCommand(string command, string args)
    {
        var text = args.Trim();
        if (text.Length == 0)
            atlas.Toggle();
        else if (uint.TryParse(text, out var id))
            atlas.Show(id);
        else
            atlas.Search(text);
    }

    private void OnPicked(string token, uint icon) => picked.SendMessage(token, icon);

    private void OnPickedPlugin(string token, string plugin) => pickedPlugin.SendMessage(token, plugin);

    private void Draw()
    {
        padNav.Update();
        windows.Draw();
    }

    private void Save() => PluginInterface.SavePluginConfig(config);

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        pick.UnregisterAction();
        pickWithPlugins.UnregisterAction();
        atlas.Picked -= OnPicked;
        atlas.PickedPlugin -= OnPickedPlugin;
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenMainUi -= atlas.Toggle;
        windows.RemoveAllWindows();
        padNav.Dispose();
        atlas.Dispose();
    }
}
