using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ControllerKeeper;

internal sealed class ConfigWindow : Window
{
    private readonly Configuration config;
    private readonly Action save;
    private readonly PadKeeper keeper;
    private readonly RescanFilter filter;

    public ConfigWindow(Configuration config, Action save, PadKeeper keeper, RescanFilter filter)
        : base("ControllerKeeper###ControllerKeeperConfig")
    {
        this.config = config;
        this.save = save;
        this.keeper = keeper;
        this.filter = filter;
        Size = new Vector2(520, 360);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var changed = false;

        var skip = config.SkipRescans;
        if (ImGui.Checkbox("Skip controller rescans when no controller changed", ref skip))
        {
            config.SkipRescans = skip;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The game freezes pad input for a moment whenever Windows reports any hardware change.\nThis only lets it rescan when a controller was actually added, removed or swapped.");
        ImGui.TextDisabled($"This session: {filter.Skipped} skipped, {filter.Passed} let through.");
        if (ImGui.SmallButton("Reconnect controllers"))
            filter.Reconnect();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Makes the game rescan its controllers, like unplugging and replugging the pad.\nAlso /controllerkeeper reconnect, e.g. from a keyboard macro.");

        ImGui.Spacing();
        var keep = config.KeepChosen;
        if (ImGui.Checkbox("Keep the game on my chosen controller", ref keep))
        {
            config.KeepChosen = keep;
            changed = true;
        }
        ImGui.TextDisabled($"Switched back {keeper.Switches} time(s) this session.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Controllers the game sees");
        var controllers = PadKeeper.Controllers();
        if (controllers.Count == 0)
            ImGui.TextDisabled("None (log in, or plug a controller in).");

        var chosen = keeper.ChosenIndex(controllers);
        foreach (var c in controllers)
        {
            ImGui.PushID(c.Index);
            if (ImGui.RadioButton("##choose", chosen == c.Index))
            {
                keeper.Choose(c);
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Keep the game on this one");
            ImGui.SameLine();
            ImGui.TextUnformatted(c.Name.Length > 0 ? c.Name : c.InstanceName);
            if (c.Active)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(in use)");
            }
            ImGui.PopID();
        }

        if (config.Instance != null && chosen == null)
            ImGui.TextDisabled($"Your chosen controller ({config.Name}) isn't connected.");

        if (config.Instance != null && ImGui.SmallButton("Forget my choice"))
        {
            config.Instance = config.Product = null;
            config.Name = null;
            changed = true;
        }

        if (changed)
            save();
    }
}
