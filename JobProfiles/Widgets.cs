using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Shortcuts;

namespace JobProfiles;

// Shared by the settings tabs: step lists (anything Flick Wheel's picker offers, plus typed
// commands) and a generic combo with search.
internal sealed class Widgets(ShortcutCatalog catalog, ShortcutPicker picker, IconCache icons)
{
    // The open searchable combo's search text; only one is open at a time.
    private string search = string.Empty;
    // A list whose Add was clicked; the picker opens for it at the window's top level.
    private (string Name, Action<Step> Add)? pendingAdd;

    // The steps run in order: icon and name for each (a command can be edited in place), a
    // remove button, then Add, which opens the picker. add puts the picked step in the
    // saved list (the one drawn may be a copy).
    public bool StepList(List<Step> list, string name, Action<Step> add)
    {
        var changed = false;
        int? remove = null;
        var size = new Vector2(ImGui.GetFrameHeight());
        for (var i = 0; i < list.Count; i++)
        {
            ImGui.PushID(i);
            var step = list[i];
            var info = catalog.Describe(step);
            if (info is { IconId: > 0 } shown)
                ImGui.Image(icons.Handle(shown.IconId, shown.KeyOutIcon), size);
            else
                ImGui.Dummy(size);
            ImGui.SameLine();

            var removeWidth = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X;
            // Where the remove button goes, measured in the cell so it works inside tables too.
            var removeX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight();
            var editable = step.Kind is ShortcutKind.Text or ShortcutKind.Command && step.Label == null;
            if (editable)
            {
                var command = step.Command ?? string.Empty;
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - removeWidth);
                if (ImGui.InputTextWithHint("##command", "/command, e.g. /gs change 3 or /wait 1", ref command, 500))
                {
                    step.Command = command;
                    changed = true;
                }
            }
            else
            {
                ImGui.AlignTextToFramePadding();
                var label = info?.Name ?? "(no longer in the game)";
                if (info != null && catalog.Available(step))
                    ImGui.TextUnformatted(label);
                else
                    ImGui.TextDisabled(label);
            }

            // The remove button sits at the right edge on every row.
            if (editable)
                ImGui.SameLine();
            else
                ImGui.SameLine(removeX);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
                remove = i;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove");
            ImGui.PopID();
        }

        if (remove is { } index)
        {
            list.RemoveAt(index);
            changed = true;
        }

        if (ImGui.SmallButton("Add"))
            pendingAdd = (name, add);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("A macro, menu, action, item, chocobo command or any chat command (/wait N pauses).");

        return changed;
    }

    /// <summary>Opens the picker for a list whose Add was clicked, and draws it. True when a step was added.</summary>
    public bool DrawPicker()
    {
        if (pendingAdd is var (name, add))
        {
            picker.Open(name, picked => add(Step.From(picked)));
            pendingAdd = null;
        }

        return picker.Draw();
    }

    // A step list in one line, e.g. "Healer HUD, /gs change 3".
    public string StepNames(List<Step>? list) =>
        list is { Count: > 0 } ? string.Join(", ", list.Select(s => catalog.Describe(s)?.Name ?? "?")) : "—";

    public bool Combo<T>(string id, ref T value, List<(T Value, string Label)> options, bool searchable = false,
                          float width = -1)
    {
        var current = value;
        var selected = Math.Max(0, options.FindIndex(o => EqualityComparer<T>.Default.Equals(o.Value, current)));
        var changed = false;
        ImGui.SetNextItemWidth(width);
        if (ImGui.BeginCombo(id, options[selected].Label, searchable ? ImGuiComboFlags.HeightLarge : ImGuiComboFlags.None))
        {
            // A search box at the top, empty and focused each time the list opens.
            if (searchable)
            {
                if (ImGui.IsWindowAppearing())
                {
                    search = string.Empty;
                    ImGui.SetKeyboardFocusHere();
                }
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##search", "Search", ref search, 64);
            }

            for (var i = 0; i < options.Count; i++)
            {
                if (search.Length > 0 && searchable &&
                    !options[i].Label.Contains(search, StringComparison.CurrentCultureIgnoreCase))
                    continue;
                if (ImGui.Selectable($"{options[i].Label}##{i}", i == selected) && i != selected)
                {
                    value = options[i].Value;
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }

        return changed;
    }
}
