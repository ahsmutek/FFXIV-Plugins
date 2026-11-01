using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Shortcuts;

namespace FlickWheel;

internal readonly record struct PluginGlyph(char Glyph, uint Color);

// A plugin can give its command an icon by providing IPC "FlickWheel.Icon /command", which returns a
// FontAwesome glyph and its ABGR colour (glyph 0 = none). Used for command shortcuts without an icon of their own.
internal sealed class PluginIcons(IDalamudPluginInterface pluginInterface)
{
    private readonly Dictionary<string, ICallGateSubscriber<(uint Glyph, uint Color)>> subscribers = new();

    public PluginGlyph? Of(IShortcut entry)
    {
        if (entry.Kind != ShortcutKind.Command || entry.IconId != 0 || CommandName(entry.Command) is not { } name)
            return null;

        if (!subscribers.TryGetValue(name, out var subscriber))
            subscribers[name] = subscriber =
                pluginInterface.GetIpcSubscriber<(uint Glyph, uint Color)>($"FlickWheel.Icon {name}");
        if (!subscriber.HasFunction)
            return null;

        try
        {
            var (glyph, color) = subscriber.InvokeFunc();
            return glyph is > 0 and <= char.MaxValue ? new PluginGlyph((char)glyph, color) : null;
        }
        catch (IpcError)
        {
            return null;
        }
    }

    private static string? CommandName(string? command) =>
        command?.Trim().Split(' ', 2)[0].ToLowerInvariant() is { Length: > 1 } name && name[0] == '/' ? name : null;
}
