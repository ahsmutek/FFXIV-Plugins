namespace Shortcuts;

// Saved as a number: append new kinds, never reorder.
public enum ShortcutKind
{
    MainCommand,
    GeneralAction,
    Action,
    Item,
    Command,       // Only available while the owning plugin is loaded.
    Macro,
    Toggle,
    Window,
    BuddyAction,
    Text,
    Emote,
}

public interface IShortcut
{
    ShortcutKind Kind { get; }
    uint RowId { get; }
    string? Command { get; }
    string? Label { get; }
    uint IconId { get; }
}

public readonly record struct PickedShortcut(ShortcutKind Kind, uint RowId, string? Command = null, string? Label = null,
                                             uint IconId = 0);
