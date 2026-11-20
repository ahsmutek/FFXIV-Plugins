using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace IconAtlasClient;

internal sealed class IconPicker : IDisposable
{
    private readonly string owner;
    private readonly ICallGateSubscriber<string, string, uint, object> pick;
    private readonly ICallGateSubscriber<string, string, uint, string, object> pickWithPlugins;
    private readonly ICallGateSubscriber<string, uint, object> picked;
    private readonly ICallGateSubscriber<string, string, object> pickedPlugin;
    private string? token;
    private Action<uint>? onPicked;
    private Action<string>? onPluginPicked;

    public IconPicker(IDalamudPluginInterface pluginInterface, string owner)
    {
        this.owner = owner;
        pick = pluginInterface.GetIpcSubscriber<string, string, uint, object>("IconAtlas.Pick");
        pickWithPlugins = pluginInterface.GetIpcSubscriber<string, string, uint, string, object>("IconAtlas.PickWithPlugins");
        picked = pluginInterface.GetIpcSubscriber<string, uint, object>("IconAtlas.Picked");
        pickedPlugin = pluginInterface.GetIpcSubscriber<string, string, object>("IconAtlas.PickedPlugin");
        picked.Subscribe(OnPicked);
        pickedPlugin.Subscribe(OnPluginPicked);
    }

    public bool Available => pick.HasAction;

    public void Dispose()
    {
        picked.Unsubscribe(OnPicked);
        pickedPlugin.Unsubscribe(OnPluginPicked);
    }

    // Only the last request is answered; the token tells our answers apart from other plugins'.
    // With pluginPicked, plugins' own icons can be chosen too, and currentPlugin names the one in use.
    public void Open(string purpose, uint current, Action<uint> picked, string? currentPlugin = null,
                     Action<string>? pluginPicked = null)
    {
        token = $"{owner}:{Guid.NewGuid():N}";
        onPicked = picked;
        onPluginPicked = pluginPicked;
        if (pluginPicked != null && pickWithPlugins.HasAction)
            pickWithPlugins.InvokeAction(token, purpose, current, currentPlugin ?? string.Empty);
        else
            pick.InvokeAction(token, purpose, current);
    }

    public bool Button(string label, string purpose, uint current, Action<uint> picked, string? currentPlugin = null,
                       Action<string>? pluginPicked = null)
    {
        var available = Available;
        ImGui.BeginDisabled(!available);
        var clicked = ImGui.Button(label);
        ImGui.EndDisabled();
        if (!available && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Load the Icon Atlas plugin to choose from every game icon.");
        if (clicked)
            Open(purpose, current, picked, currentPlugin, pluginPicked);
        return clicked;
    }

    private void OnPicked(string answered, uint icon)
    {
        if (answered != token)
            return;
        token = null;
        onPicked?.Invoke(icon);
    }

    private void OnPluginPicked(string answered, string plugin)
    {
        if (answered != token)
            return;
        token = null;
        onPluginPicked?.Invoke(plugin);
    }
}
