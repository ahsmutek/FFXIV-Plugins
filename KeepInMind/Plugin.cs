using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Icons;

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
    private static IAddonLifecycle AddonLifecycle { get; set; } = null!;

    [PluginService]
    private static IGameGui GameGui { get; set; } = null!;

    [PluginService]
    private static IGameInteropProvider GameInterop { get; set; } = null!;

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("KeepInMind");
    private readonly ChecklistWindow window;
    private readonly SettingsWindow settings;
    private readonly LogMenu menu;
    private readonly Stock stock;
    private readonly NativeTooltip tooltip;
    private readonly SlotHighlight highlight;
    private readonly ItemTooltip itemTooltip;

    public Plugin()
    {
        TablerIcons.Init(TextureProvider);
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        config.Items = [.. config.Items.DistinctBy(i => i.ItemId)];

        tooltip = new NativeTooltip(AddonLifecycle);
        var sources = new Sources(DataManager);
        var actions = new ItemActions(PluginInterface, sources, Log);
        stock = new Stock(Framework, ClientState, PluginInterface, Log, config, Save);
        highlight = new SlotHighlight(GameGui, DataManager, config, stock);
        itemTooltip = new ItemTooltip(GameInterop, DataManager, GameGui, Log, config, stock);
        settings = new SettingsWindow(config, Save);
        window = new ChecklistWindow(config, Save, actions, sources, stock, DataManager, TextureProvider, tooltip, GamepadState,
                                     () => settings.IsOpen = true);
        windows.AddWindow(window);
        windows.AddWindow(settings);
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenMainUi += window.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += settings.Toggle;

        menu = new LogMenu(ContextMenu, DataManager, sources, Add);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Show or hide your Keep in Mind list.",
        });
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim() == "debug")
            highlight.Dump(Log);
        else
            window.Toggle();
    }

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
        highlight.Draw();
        tooltip.EndFrame();
    }

    private void Save() => PluginInterface.SavePluginConfig(config);

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        menu.Dispose();
        itemTooltip.Dispose();
        stock.Dispose();
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenMainUi -= window.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= settings.Toggle;
        windows.RemoveAllWindows();
        tooltip.Dispose();
    }
}
