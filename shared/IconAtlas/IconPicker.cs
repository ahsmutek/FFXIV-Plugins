using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace IconAtlasClient;

// Lets the user choose an icon in the Icon Atlas plugin, over its IconAtlas.Pick and IconAtlas.Picked IPC.
internal sealed class IconPicker : IDisposable
{
    private readonly string owner;
    private readonly ICallGateSubscriber<string, string, uint, object> pick;
    private readonly ICallGateSubscriber<string, uint, object> picked;
    private string? token;
    private Action<uint>? onPicked;

    public IconPicker(IDalamudPluginInterface pluginInterface, string owner)
    {
        this.owner = owner;
        pick = pluginInterface.GetIpcSubscriber<string, string, uint, object>("IconAtlas.Pick");
        picked = pluginInterface.GetIpcSubscriber<string, uint, object>("IconAtlas.Picked");
        picked.Subscribe(OnPicked);
    }

    public bool Available => pick.HasAction;

    public void Dispose() => picked.Unsubscribe(OnPicked);

    // Only the last request is answered; the token tells our answers apart from other plugins'.
    public void Open(string purpose, uint current, Action<uint> picked)
    {
        token = $"{owner}:{Guid.NewGuid():N}";
        onPicked = picked;
        pick.InvokeAction(token, purpose, current);
    }

    // A "Choose icon" button, disabled with a hint when Icon Atlas isn't loaded.
    public bool Button(string label, string purpose, uint current, Action<uint> picked)
    {
        var available = Available;
        ImGui.BeginDisabled(!available);
        var clicked = ImGui.Button(label);
        ImGui.EndDisabled();
        if (!available && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Load the Icon Atlas plugin to choose from every game icon.");
        if (clicked)
            Open(purpose, current, picked);
        return clicked;
    }

    private void OnPicked(string answered, uint icon)
    {
        if (answered != token)
            return;
        token = null;
        onPicked?.Invoke(icon);
    }
}
