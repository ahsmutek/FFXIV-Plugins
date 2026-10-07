using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Shortcuts;

namespace JobProfiles;

internal sealed class Widgets(ShortcutCatalog catalog, ShortcutPicker picker, IconCache icons)
{
    private string search = string.Empty;
    private (string Name, Action<Step> Add)? pendingAdd;

    // add stores the picked step in the saved list (the one drawn may be a copy).
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

        if (list.Count == 0)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("Nothing yet: click Add to pick a macro, command, menu, item, emote…");
            ImGui.SameLine();
        }
        if (ImGui.SmallButton("Add"))
            pendingAdd = (name, add);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Pick a macro, game menu, action, item, emote or chat command.\nA /wait 2 command pauses before the next one.");

        return changed;
    }

    public bool DrawPicker()
    {
        if (pendingAdd is var (name, add))
        {
            picker.Open(name, picked => add(Step.From(picked)));
            pendingAdd = null;
        }

        return picker.Draw();
    }

    public string StepNames(List<Step>? list) =>
        list is { Count: > 0 } ? string.Join(", ", list.Select(s => catalog.Describe(s)?.Name ?? "?")) : "—";

    // width is at 100% UI scale; -1 fills the row.
    public bool Combo<T>(string id, ref T value, List<(T Value, string Label)> options, bool searchable = false,
                          float width = -1)
    {
        var current = value;
        var selected = Math.Max(0, options.FindIndex(o => EqualityComparer<T>.Default.Equals(o.Value, current)));
        var changed = false;
        if (width > 0)
        {
            var longest = options.Count > 0 ? options.Max(o => ImGui.CalcTextSize(o.Label).X) : 0f;
            var fit = longest + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetFrameHeight();
            width = MathF.Max(width * ImGuiHelpers.GlobalScale, fit);
        }
        ImGui.SetNextItemWidth(width);
        if (ImGui.BeginCombo(id, options[selected].Label, searchable ? ImGuiComboFlags.HeightLarge : ImGuiComboFlags.None))
        {
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
