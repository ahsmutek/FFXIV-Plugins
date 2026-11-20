using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using IconAtlasClient;
using Shortcuts;

namespace FlickWheel;

internal readonly record struct PluginGlyph(char Glyph, uint Color);

// A plugin can give its command an icon by providing IPC "FlickWheel.Icon /command", which returns a
// FontAwesome glyph and its ABGR colour (glyph 0 = none). Used for command shortcuts without an icon of their own;
// otherwise they show their plugin's own icon, where it's on this PC.
internal sealed class PluginIcons : IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly Dictionary<string, ICallGateSubscriber<(uint Glyph, uint Color)>> subscribers = new();
    private readonly Dictionary<string, string?> owners = new();
    private readonly Dictionary<string, string?> files = new();
    // Set by an event that can come from Dalamud's loading thread; the caches are cleared on ours.
    private volatile bool stale;

    public PluginIcons(IDalamudPluginInterface pluginInterface, ICommandManager commands)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        pluginInterface.ActivePluginsChanged += OnPluginsChanged;
    }

    public void Dispose() => pluginInterface.ActivePluginsChanged -= OnPluginsChanged;

    public PluginGlyph? Of(ShortcutEntry entry)
    {
        if (entry.Kind != ShortcutKind.Command || entry.IconId != 0 || entry.IconPlugin != null ||
            CommandName(entry.Command) is not { } name)
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

    // The plugin whose own icon the entry shows: one chosen for it, or for a command, the plugin that runs it.
    public string? PluginOf(ShortcutEntry entry)
    {
        if (entry.IconId != 0)
            return null;
        if (entry.IconPlugin != null)
            return entry.IconPlugin;
        return entry.Kind == ShortcutKind.Command && CommandName(entry.Command) is { } name ? OwnerOf(name) : null;
    }

    public string? FileOf(ShortcutEntry entry) => PluginOf(entry) is { } plugin ? FileOfPlugin(plugin) : null;

    public string? FileOfPlugin(string internalName)
    {
        ClearIfStale();
        if (!files.TryGetValue(internalName, out var file))
            files[internalName] = file = pluginInterface.InstalledPlugins
                                                        .Where(p => p.InternalName == internalName)
                                                        .Select(PluginIconFile.Of)
                                                        .FirstOrDefault(path => path != null);
        return file;
    }

    private string? OwnerOf(string command)
    {
        ClearIfStale();
        if (!commands.Commands.TryGetValue(command, out var info))
            return null;
        if (!owners.TryGetValue(command, out var owner))
            owners[command] = owner = pluginInterface.GetPlugin(info.Handler.Method.Module.Assembly)?.InternalName;
        return owner;
    }

    private void OnPluginsChanged(IActivePluginsChangedEventArgs args) => stale = true;

    private void ClearIfStale()
    {
        if (!stale)
            return;
        stale = false;
        owners.Clear();
        files.Clear();
    }

    private static string? CommandName(string? command) =>
        command?.Trim().Split(' ', 2)[0].ToLowerInvariant() is { Length: > 1 } name && name[0] == '/' ? name : null;
}
