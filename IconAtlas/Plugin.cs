using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
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

    private readonly Configuration config;
    private readonly WindowSystem windows = new("IconAtlas");
    private readonly AtlasWindow atlas;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        atlas = new AtlasWindow(config, Save, Textures);
        windows.AddWindow(atlas);

        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += atlas.Toggle;

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

    private void Save() => PluginInterface.SavePluginConfig(config);

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= atlas.Toggle;
        windows.RemoveAllWindows();
        atlas.Dispose();
    }
}
