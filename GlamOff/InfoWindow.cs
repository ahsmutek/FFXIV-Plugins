using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace GlamOff;

internal sealed class InfoWindow : Window
{
    public InfoWindow()
        : base("Glam Off###GlamOffInfo", ImGuiWindowFlags.AlwaysAutoResize)
    {
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Open the item menu on glamoured gear (right-click, or X on a controller)");
        ImGui.TextUnformatted("and choose \"Try On Original\" to see it without its glamour.");
        ImGui.Spacing();
        ImGui.TextDisabled("The game's own \"Try On\" still shows the glamour. Nothing to set up.");
    }
}
