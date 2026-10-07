using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace KeepInMind;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/keepinmind";

    [PluginService]
    private static IDalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService]
    private static ICommandManager CommandManager { get; set; } = null!;

    [PluginService]
    private static IContextMenu ContextMenu { get; set; } = null!;

    [PluginService]
    private static IDataManager DataManager { get; set; } = null!;

    [PluginService]
    private static ITextureProvider TextureProvider { get; set; } = null!;

    [PluginService]
    private static IFramework Framework { get; set; } = null!;

    [PluginService]
    private static IClientState ClientState { get; set; } = null!;

    [PluginService]
    private static IGamepadState GamepadState { get; set; } = null!;

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("KeepInMind");
    private readonly ChecklistWindow window;
    private readonly RecipeNoteMenu menu;
    private readonly Stock stock;
    private readonly NativeTooltip tooltip = new();

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        // Older or hand-edited files: one entry per item.
        config.Items = [.. config.Items.DistinctBy(i => i.ItemId)];

        var actions = new ItemActions(PluginInterface, DataManager, Log);
        window = new ChecklistWindow(config, Save, actions, DataManager, TextureProvider, tooltip, GamepadState);
        windows.AddWindow(window);
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenMainUi += window.Toggle;
        // No settings: the list is all there is, so the settings button opens it too.
        PluginInterface.UiBuilder.OpenConfigUi += window.Toggle;

        menu = new RecipeNoteMenu(ContextMenu, DataManager, Add);
        stock = new Stock(Framework, ClientState, config, Save);

        CommandManager.AddHandler(CommandName, new CommandInfo((_, _) => window.Toggle())
        {
            HelpMessage = "Show or hide your Keep in Mind list.",
        });
    }

    // Adds an item, or adds to how many you need if it's already on the list (another recipe
    // needs it too), and shows the list with it lit up. Needing more un-ticks it.
    private void Add(uint itemId, int amount)
    {
        if (config.Items.Find(i => i.ItemId == itemId) is { } kept)
        {
            kept.Needed += amount;
            kept.Done = false;
        }
        else
        {
            config.Items.Add(new KeptItem { ItemId = itemId, Needed = amount });
        }
        Save();

        window.IsOpen = true;
        window.Highlight(itemId);
    }

    private void Draw()
    {
        windows.Draw();
        tooltip.EndFrame();
    }

    private void Save() => PluginInterface.SavePluginConfig(config);

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        menu.Dispose();
        stock.Dispose();
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenMainUi -= window.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= window.Toggle;
        windows.RemoveAllWindows();
        tooltip.Dispose();
    }
}
