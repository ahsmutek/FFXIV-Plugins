using Controller;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Icons;

namespace GlamKit;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/glamkit";

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
    private static IAddonLifecycle AddonLifecycle { get; set; } = null!;

    [PluginService]
    private static IGameGui GameGui { get; set; } = null!;

    [PluginService]
    private static IGameInteropProvider GameInterop { get; set; } = null!;

    [PluginService]
    private static IChatGui ChatGui { get; set; } = null!;

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    [PluginService]
    private static IGamepadState Gamepad { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("GlamKit");
    private readonly SettingsWindow settings;
    private readonly KeeperWindow window;
    private readonly GlamOff glamOff;
    private readonly KeeperMenu menu;
    private readonly NativeTooltip tooltip;
    private readonly SlotHighlight highlight;
    private readonly ItemTooltip itemTooltip;
    private readonly Stored stored;
    private readonly Locations locations;
    private readonly PadNav padNav;

    public Plugin()
    {
        TablerIcons.Init(TextureProvider);
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        glamOff = new GlamOff(ContextMenu, GameGui, config);
        menu = new KeeperMenu(ContextMenu, GameGui, DataManager, config, SetKept, SetLater, SetWished);
        tooltip = new NativeTooltip(AddonLifecycle);
        highlight = new SlotHighlight(GameGui, DataManager, config);
        itemTooltip = new ItemTooltip(GameInterop, AddonLifecycle, DataManager, GameGui, Log, config);
        stored = new Stored(Framework, ClientState, ChatGui, DataManager, config, Save);
        locations = new Locations(Framework, ClientState, PluginInterface, Log, config);

        settings = new SettingsWindow(config, Save);
        window = new KeeperWindow(config, Save, locations, DataManager, TextureProvider, tooltip, () => settings.IsOpen = true);
        windows.AddWindow(window);
        windows.AddWindow(settings);
        padNav = new PadNav(GameInterop, Gamepad, Log, windows);
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenMainUi += window.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += settings.Toggle;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Show or hide the gear you're keeping for glam or for later, and your wishlist.",
        });
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim() == "debug")
            highlight.Dump(Log);
        else
            window.Toggle();
    }

    private void SetKept(uint itemId, bool kept)
    {
        config.Items.RemoveAll(i => i.ItemId == itemId);
        if (kept)
            config.Items.Add(new KeptItem { ItemId = itemId });
        Save();
    }

    private void SetLater(uint itemId, bool kept)
    {
        config.LaterItems.RemoveAll(i => i.ItemId == itemId);
        if (kept)
            config.LaterItems.Add(new KeptItem { ItemId = itemId });
        Save();
    }

    private void SetWished(uint itemId, bool wished)
    {
        config.WishItems.RemoveAll(i => i.ItemId == itemId);
        if (wished)
            config.WishItems.Add(new KeptItem { ItemId = itemId });
        Save();
    }

    private void Draw()
    {
        padNav.Update();
        windows.Draw();
        highlight.Draw();
        tooltip.EndFrame();
    }

    private void Save() => PluginInterface.SavePluginConfig(config);

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        glamOff.Dispose();
        menu.Dispose();
        itemTooltip.Dispose();
        stored.Dispose();
        locations.Dispose();
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenMainUi -= window.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= settings.Toggle;
        windows.RemoveAllWindows();
        padNav.Dispose();
        tooltip.Dispose();
    }
}
