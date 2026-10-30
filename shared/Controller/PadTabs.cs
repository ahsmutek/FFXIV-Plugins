using Dalamud.Bindings.ImGui;

namespace Controller;

// L1 and R1 switch tabs in the focused window's tab bar. Its tabs open with Item in place of ImGui.BeginTabItem,
// and End goes just before ImGui.EndTabBar.
internal sealed class PadTabs
{
    private int count;
    private int current;
    private int select = -1;

    public bool Item(string label)
    {
        var index = count++;
        var open = ImGui.BeginTabItem(label, index == select ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None);
        if (open)
            current = index;
        return open;
    }

    public void End()
    {
        select = -1;
        // Not from a popup over the window, which has its own tabs.
        if (count > 0 && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows | ImGuiFocusedFlags.NoPopupHierarchy))
        {
            var step = (ImGui.IsKeyPressed(ImGuiKey.GamepadR1, false) ? 1 : 0) -
                       (ImGui.IsKeyPressed(ImGuiKey.GamepadL1, false) ? 1 : 0);
            if (step != 0)
                select = ((current + step) % count + count) % count;
        }
        count = 0;
    }
}
