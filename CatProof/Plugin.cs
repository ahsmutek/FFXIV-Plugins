using Controller;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace CatProof;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/catproof";
    // ABGR
    private const uint LockedColor = 0xFF00B5FF;
    private const uint UnlockedColor = 0xFFB0B0B0;

    [PluginService]
    private static IDalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService]
    private static ICommandManager CommandManager { get; set; } = null!;

    [PluginService]
    private static IChatGui Chat { get; set; } = null!;

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    [PluginService]
    private static IGamepadState Gamepad { get; set; } = null!;

    [PluginService]
    private static IGameInteropProvider Interop { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("CatProof");
    private readonly KeyLock keyLock;
    private readonly ConfigWindow configWindow;
    private readonly PadNav padNav;
    private readonly ICallGateProvider<(uint Glyph, uint Color)> wheelIcon;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        keyLock = new KeyLock(Interop, Log);

        var indicator = new LockIndicator(config, Save, keyLock);
        configWindow = new ConfigWindow(config, Save, keyLock, indicator);
        windows.AddWindow(indicator);
        windows.AddWindow(configWindow);
        padNav = new PadNav(Interop, Gamepad, Log, windows);
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi += configWindow.Toggle;

        // Flick Wheel draws this on /catproof shortcuts that have no icon of their own.
        wheelIcon = PluginInterface.GetIpcProvider<(uint Glyph, uint Color)>($"FlickWheel.Icon {CommandName}");
        wheelIcon.RegisterFunc(() => ((uint)config.Icon, keyLock.Locked ? LockedColor : UnlockedColor));

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Lock or unlock the keyboard.\n" +
                          "/catproof on → Lock the keyboard.\n" +
                          "/catproof off → Unlock the keyboard.\n" +
                          "/catproof config → Open settings.",
        });
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "":
                keyLock.Locked = !keyLock.Locked;
                break;
            case "on":
                keyLock.Locked = true;
                break;
            case "off":
                keyLock.Locked = false;
                break;
            case "config":
                configWindow.Toggle();
                break;
            default:
                Chat.PrintError("Usage: /catproof [on|off|config]");
                break;
        }
    }

    private void Draw()
    {
        padNav.Update();
        windows.Draw();
    }

    private void Save() => PluginInterface.SavePluginConfig(config);

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        wheelIcon.UnregisterFunc();
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= configWindow.Toggle;
        PluginInterface.UiBuilder.OpenMainUi -= configWindow.Toggle;
        windows.RemoveAllWindows();
        padNav.Dispose();
        keyLock.Dispose();
    }
}
