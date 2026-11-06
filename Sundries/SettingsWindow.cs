using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Sundries;

internal sealed class SettingsWindow : Window
{
    private readonly IReadOnlyList<IModule> modules;
    private readonly Action save;

    public SettingsWindow(IReadOnlyList<IModule> modules, Action save)
        : base("Sundries###SundriesSettings", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.modules = modules;
        this.save = save;
    }

    public override void Draw()
    {
        for (var i = 0; i < modules.Count; i++)
        {
            var module = modules[i];
            if (i > 0)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            var enabled = module.Enabled;
            if (ImGui.Checkbox(module.Name, ref enabled))
            {
                module.Enabled = enabled;
                save();
            }

            ImGui.Indent();
            foreach (var line in module.Description)
                ImGui.TextDisabled(line);
            ImGui.Spacing();
            ImGui.BeginDisabled(!enabled);
            ImGui.PushID(module.Name);
            module.DrawSettings();
            ImGui.PopID();
            ImGui.EndDisabled();
            ImGui.Unindent();
        }
    }
}
