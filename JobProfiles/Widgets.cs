using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Icons;
using Shortcuts;

namespace JobProfiles;

internal sealed class Widgets(ShortcutCatalog catalog, ShortcutPicker picker, CollectionPicker collectionPicker, IconCache icons)
{
    private string search = string.Empty;
    private (string Name, Action<Step> Add)? pendingAdd;
    private ChooseRequest? pendingChoose;

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
                icons.Get(shown.IconId, shown.KeyOutIcon).Image(size);
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

            // SameLine's offset counts from the enclosing group, which a button line is, so the X is placed by window position.
            ImGui.SameLine();
            if (!editable)
                ImGui.SetCursorPosX(removeX);
            if (TablerIcons.Button(TablerIcon.X))
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

    public void Choose(ChooseRequest request) => pendingChoose = request;

    // Dimmed when they belong to the page this one follows, and so are changed there.
    public void Collection(List<uint> ids, ShortcutKind kind, bool dimmed)
    {
        var size = new Vector2(CollectionPicker.Cell * ImGuiHelpers.GlobalScale);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var faded = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.4f));
        for (var i = 0; i < ids.Count; i++)
        {
            if (i > 0 && ImGui.GetItemRectMax().X + spacing + size.X <= right)
                ImGui.SameLine();
            var step = new Step { Kind = kind, RowId = ids[i] };
            var info = catalog.Describe(step);
            var unlocked = info != null && catalog.Available(step);
            var min = ImGui.GetCursorScreenPos();
            ImGui.Dummy(size);
            icons.Get(info?.IconId ?? 0, false).Draw(ImGui.GetWindowDrawList(), min, min + size,
                                                     unlocked && !dimmed ? uint.MaxValue : faded);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(info == null ? "(no longer in the game)" : unlocked ? info.Value.Name : $"{info.Value.Name} (locked)");
        }
    }

    public bool DrawPicker()
    {
        if (pendingAdd is var (name, add))
        {
            picker.Open(name, picked => add(Step.From(picked)));
            pendingAdd = null;
        }
        if (pendingChoose is { } request)
        {
            collectionPicker.Open(request);
            pendingChoose = null;
        }

        return picker.Draw() | collectionPicker.Draw();
    }

    public string StepNames(List<Step>? list) =>
        list is { Count: > 0 } ? string.Join(", ", list.Select(s => catalog.Describe(s)?.Name ?? "?")) : "—";

    // width is at 100% UI scale; a negative one leaves that many pixels at the row's end (-1 fills it).
    public bool Combo<T>(string id, ref T value, List<(T Value, string Label)> options, bool searchable = false,
                          float width = -1)
    {
        var current = value;
        var selected = Math.Max(0, options.FindIndex(o => EqualityComparer<T>.Default.Equals(o.Value, current)));
        var changed = false;
        ImGui.SetNextItemWidth(ComboWidth(options.Select(o => o.Label), width));
        if (ImGui.BeginCombo(id, options[selected].Label, searchable ? ImGuiComboFlags.HeightLarge : ImGuiComboFlags.None))
        {
            if (searchable)
                SearchBox();

            for (var i = 0; i < options.Count; i++)
            {
                if (!Matches(searchable, options[i].Label))
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

    // values stays in the options' order; empty shows as none.
    public bool MultiCombo<T>(string id, List<T> values, List<(T Value, string Label)> options, string none,
                              bool searchable = false, float width = -1)
    {
        var changed = false;
        var preview = values.Count == 0
            ? none
            : string.Join(", ", options.Where(o => values.Contains(o.Value)).Select(o => o.Label));
        ImGui.SetNextItemWidth(ComboWidth(options.Select(o => o.Label).Append(none), width));
        if (ImGui.BeginCombo(id, preview, searchable ? ImGuiComboFlags.HeightLarge : ImGuiComboFlags.None))
        {
            if (searchable)
                SearchBox();

            if (ImGui.Selectable(none, values.Count == 0) && values.Count > 0)
            {
                values.Clear();
                changed = true;
            }

            for (var i = 0; i < options.Count; i++)
            {
                if (!Matches(searchable, options[i].Label))
                    continue;
                var ticked = values.Contains(options[i].Value);
                if (!ImGui.Checkbox($"{options[i].Label}##{i}", ref ticked))
                    continue;
                var keep = values.ToHashSet();
                if (ticked)
                    keep.Add(options[i].Value);
                else
                    keep.Remove(options[i].Value);
                values.Clear();
                values.AddRange(options.Select(o => o.Value).Where(keep.Contains));
                changed = true;
            }
            ImGui.EndCombo();
        }

        return changed;
    }

    public static float ComboWidth(IEnumerable<string> labels, float width)
    {
        if (width <= 0)
            return width;
        var longest = labels.Select(l => ImGui.CalcTextSize(l).X).DefaultIfEmpty(0f).Max();
        var fit = longest + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetFrameHeight();
        return MathF.Max(width * ImGuiHelpers.GlobalScale, fit);
    }

    private void SearchBox()
    {
        if (ImGui.IsWindowAppearing())
        {
            search = string.Empty;
            ImGui.SetKeyboardFocusHere();
        }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Search", ref search, 64);
    }

    private bool Matches(bool searchable, string label) =>
        !searchable || search.Length == 0 || label.Contains(search, StringComparison.CurrentCultureIgnoreCase);
}
