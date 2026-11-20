using System.IO;
using System.Reflection;
using Dalamud.Plugin;

namespace IconAtlasClient;

internal static class PluginIconFile
{
    // Plugins load from memory and Dalamud keeps each one's DLL path internal, so it's read from the plugin it wraps.
    // Only dev plugins have their icon on disk, at images/icon.png beside the DLL; repo plugins' icons are online.
    public static string? Of(IExposedPlugin plugin)
    {
        foreach (var field in plugin.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (field.GetValue(plugin) is not { } inner ||
                inner.GetType().GetProperty("DllFile")?.GetValue(inner) is not FileInfo { DirectoryName: { } folder })
                continue;
            var path = Path.Combine(folder, "images", "icon.png");
            return File.Exists(path) ? path : null;
        }
        return null;
    }
}
