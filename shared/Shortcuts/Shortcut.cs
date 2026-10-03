namespace Shortcuts;

// Shared by Flick Wheel and Job Profiles, compiled into each. Saved as a number, so the
// order must never change; new kinds go on the end.
public enum ShortcutKind
{
    MainCommand,   // opens a game window, like the main menu does (MainCommand row)
    GeneralAction, // Teleport, Return, Sprint… (GeneralAction row)
    Action,        // an action used from the hotbar, e.g. the Aether Compass (Action row)
    Item,          // uses an item from your bags (Item row)
    Command,       // runs a plugin's chat command, e.g. /cbarding job; only while that plugin is loaded
    Macro,         // runs one of your macros (RowId 0–99 individual slot, 100–199 shared slot)
    Toggle,        // one of the plugins' own toggles (see ShortcutCatalog: the chat log)
    Window,        // opens a game window the main menu doesn't list, e.g. Wondrous Tails (see ShortcutCatalog.Windows)
    BuddyAction,   // a chocobo command: Follow, the stances, Withdraw (BuddyAction row)
    Text,          // a line typed as if in chat: any game or plugin command, e.g. /gs change 3
}

// A saved shortcut, as each plugin stores it (Flick Wheel's ShortcutEntry, Job Profiles' Step).
public interface IShortcut
{
    ShortcutKind Kind { get; }
    // The sheet row for Kind (unused for Command and Text).
    uint RowId { get; }
    // The command line for Command and Text.
    string? Command { get; }
    // Shown instead of the game's name when set.
    string? Label { get; }
    // Game icon to show instead of the default one; 0 = default.
    uint IconId { get; }
}

// What the picker adds: a new shortcut, for the plugin to store its own way.
public readonly record struct PickedShortcut(ShortcutKind Kind, uint RowId, string? Command = null, string? Label = null,
                                             uint IconId = 0);
