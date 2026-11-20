using System;
using System.IO;
using Controller;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Icons;

namespace Commendable;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/commendable";
    private const uint WheelIconColor = 0xFF4CC8F0;

    [PluginService]
    private static IDalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService]
    private static ICommandManager CommandManager { get; set; } = null!;

    [PluginService]
    private static IClientState ClientState { get; set; } = null!;

    [PluginService]
    private static IDutyState DutyState { get; set; } = null!;

    [PluginService]
    private static IFramework Framework { get; set; } = null!;

    [PluginService]
    private static IPlayerState PlayerState { get; set; } = null!;

    [PluginService]
    private static ICondition Condition { get; set; } = null!;

    [PluginService]
    private static IDataManager DataManager { get; set; } = null!;

    [PluginService]
    private static IChatGui ChatGui { get; set; } = null!;

    [PluginService]
    private static IContextMenu ContextMenu { get; set; } = null!;

    [PluginService]
    private static ITargetManager Targets { get; set; } = null!;

    [PluginService]
    private static IAddonLifecycle AddonLifecycle { get; set; } = null!;

    [PluginService]
    private static IGameGui GameGui { get; set; } = null!;

    [PluginService]
    private static IGameInteropProvider Interop { get; set; } = null!;

    [PluginService]
    private static IGamepadState Gamepad { get; set; } = null!;

    [PluginService]
    private static ITextureProvider Textures { get; set; } = null!;

    [PluginService]
    private static IPluginLog Log { get; set; } = null!;

    private readonly Configuration config;
    private readonly WindowSystem windows = new("Commendable");
    private readonly Sessions sessions;
    private readonly EffectCapture effects;
    private readonly PartyCapture party;
    private readonly Bookmarks bookmarks;
    private readonly SettingsWindow settings;
    private readonly SummaryWindow summary;
    private readonly PadNav padNav;
    private readonly ICallGateProvider<(uint Glyph, uint Color)> wheelIcon;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        TablerIcons.Init(Textures);

        var journalFolder = Path.Combine(PluginInterface.GetPluginConfigDirectory(), "Journal");
        sessions = new Sessions(ClientState, DutyState, Framework, PlayerState, DataManager, ChatGui, Log, config,
                                journalFolder);
        effects = new EffectCapture(Interop, DataManager, Log, sessions);
        party = new PartyCapture(Framework, Condition, sessions);
        bookmarks = new Bookmarks(ContextMenu, Targets, ChatGui, config, sessions);

        settings = new SettingsWindow(config, Save);
        summary = new SummaryWindow(config, Save, sessions, DataManager, Textures, GameGui, () => settings.IsOpen = true);
        windows.AddWindow(summary);
        windows.AddWindow(settings);
        padNav = new PadNav(Interop, Gamepad, Log, windows);

        sessions.Completed += OnCompleted;
        sessions.Left += OnLeft;
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, SummaryWindow.CommendationWindow, OnCommendationWindow);
        AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, SummaryWindow.CommendationWindow, OnCommendationWindowClosed);
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenMainUi += Cycle;
        PluginInterface.UiBuilder.OpenConfigUi += settings.Toggle;

        // Flick Wheel draws this on /commendable shortcuts that have no icon of their own.
        wheelIcon = PluginInterface.GetIpcProvider<(uint Glyph, uint Color)>($"FlickWheel.Icon {CommandName}");
        wheelIcon.RegisterFunc(() => ((uint)FontAwesomeIcon.Award, WheelIconColor));

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Show who helped you this duty, or bring it back from the game; hides it if it's already in use.\n" +
                          $"{CommandName} last → the previous duty\n" +
                          $"{CommandName} mark → bookmark the party member you're targeting\n" +
                          $"{CommandName} replay [n] → show a saved duty again (1 = the latest)\n" +
                          $"{CommandName} config → settings",
        });
    }

    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        switch (parts.Length == 0 ? "" : parts[0].ToLowerInvariant())
        {
            case "":
                Cycle();
                break;
            case "last":
                if (sessions.Last == null)
                    ChatGui.Print("There's no previous duty yet.", Sessions.ChatTag);
                summary.Show(SummaryView.Last);
                break;
            case "config":
                settings.Toggle();
                break;
            case "mark":
                bookmarks.ToggleTarget();
                break;
            case "replay":
                Replay(parts.Length > 1 ? parts[1] : null);
                break;
            default:
                ChatGui.PrintError($"Unknown option \"{parts[0]}\". Try last, mark, replay or config.", Sessions.ChatTag);
                break;
        }
    }

    // Brings an open summary back to the controller instead of closing it, since B only hands the controller back to the game.
    // Mid-duty it never takes the controller, so this only shows or hides it.
    private void Cycle()
    {
        if (!summary.IsOpen)
            summary.Show(SummaryView.Current);
        else if (!summary.IsFocused && !sessions.Recording)
            summary.BringToFront();
        else
            summary.IsOpen = false;
    }

    private void Replay(string? arg)
    {
        var index = 1;
        if (arg != null && (!int.TryParse(arg, out index) || index < 1))
        {
            ChatGui.PrintError($"Use {CommandName} replay for the latest saved duty, or replay 2 for the one before.", Sessions.ChatTag);
            return;
        }

        sessions.Flush();
        var saved = Journal.Saved(sessions.JournalFolder);
        if (index > saved.Count)
        {
            ChatGui.Print(saved.Count == 0 ? "No saved duties yet." : $"Only {saved.Count} duties are saved.", Sessions.ChatTag);
            return;
        }

        DutySession? replayed;
        try
        {
            replayed = Interpreter.Replay(Journal.Read(saved[index - 1]));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warning(e, "Couldn't read a saved duty.");
            ChatGui.PrintError("Couldn't read that saved duty.", Sessions.ChatTag);
            return;
        }

        if (replayed == null)
        {
            ChatGui.PrintError("That saved duty is empty.", Sessions.ChatTag);
            return;
        }
        summary.ShowReplay(replayed);
    }

    private void OnCompleted(DutySession session)
    {
        if (config.AutoOpen && !config.WaitForCommendations && summary.HasRows(session))
            summary.OpenWithoutFocus(SummaryView.Current);
    }

    private void OnLeft(DutySession session) => summary.IsOpen = false;

    // Commending closes the game's window; the game logs no message for it.
    private void OnCommendationWindowClosed(AddonEvent type, AddonArgs args) => summary.IsOpen = false;

    private void OnCommendationWindow(AddonEvent type, AddonArgs args)
    {
        if (config.AutoOpen && config.WaitForCommendations && sessions.Current is { } session && summary.HasRows(session))
            summary.OpenWithoutFocus(SummaryView.Current);
    }

    private void Draw()
    {
        padNav.Update();
        windows.Draw();
    }

    private void Save()
    {
        PluginInterface.SavePluginConfig(config);
        summary.Invalidate();
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        wheelIcon.UnregisterFunc();
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, SummaryWindow.CommendationWindow, OnCommendationWindow);
        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, SummaryWindow.CommendationWindow, OnCommendationWindowClosed);
        sessions.Completed -= OnCompleted;
        sessions.Left -= OnLeft;
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Cycle;
        PluginInterface.UiBuilder.OpenConfigUi -= settings.Toggle;
        windows.RemoveAllWindows();
        padNav.Dispose();
        bookmarks.Dispose();
        party.Dispose();
        effects.Dispose();
        sessions.Dispose();
    }
}
