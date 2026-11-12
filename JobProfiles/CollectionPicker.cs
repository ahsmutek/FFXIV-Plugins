using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Shortcuts;

namespace JobProfiles;

// Sources are the other pages' own lists, to copy from.
internal sealed record ChooseRequest(ShortcutKind Kind, string Title, IReadOnlyList<uint> Current, Action<List<uint>> Save,
                                     Func<List<(string Label, List<uint> Ids)>> Sources);

// A grid like the game's Mount and Minion Guides, quick to move through with the D-pad.
// Ticks go to a draft that only replaces the saved list on Save.
internal sealed class CollectionPicker(ShortcutCatalog catalog, IconCache icons, IDataManager data)
{
    public const float Cell = 48f;

    private const string Popup = "##chooseCollection";
    private const string SingleSeat = "Single-seat";
    private const string MultiSeat = "Multi-seat";

    private ChooseRequest? request;
    private List<uint> draft = [];
    private List<ShortcutChoice> owned = [];
    private HashSet<uint> ownedIds = [];
    // Re-sorted only on opening, filters and bulk changes, so a tick doesn't move the grid under the cursor.
    private List<ShortcutChoice> top = [];
    private List<ShortcutChoice> rest = [];
    private List<(string Label, List<uint> Ids)> sources = [];
    private readonly Dictionary<uint, string[]> tags = new();
    private List<string> allTags = [];
    private string search = string.Empty;
    private bool hideChosen;
    private string? tag;
    private string? pointed;

    private bool Unsaved => request != null && !draft.SequenceEqual(request.Current);

    public void Open(ChooseRequest request)
    {
        this.request = request;
        draft = [.. request.Current];
        owned = request.Kind == ShortcutKind.Mount ? catalog.OwnedMounts() : catalog.OwnedMinions();
        ownedIds = owned.Select(o => o.RowId).ToHashSet();
        sources = request.Sources();
        tags.Clear();
        foreach (var id in ownedIds.Concat(draft))
            tags[id] = Tags(id);
        allTags = TagOrder().Where(t => tags.Values.Any(ts => ts.Contains(t))).ToList();
        search = string.Empty;
        hideChosen = false;
        tag = null;
        pointed = null;
        Sort();
        ImGui.OpenPopup(Popup);
    }

    public bool Draw()
    {
        if (!ImGui.BeginPopup(Popup))
            return false;

        if (request == null)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return false;
        }

        ImGui.BeginChild("##collectionBody", new Vector2(640f, 540f) * ImGuiHelpers.GlobalScale);
        var saved = DrawHeader();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Search", ref search, 64);
        DrawFilters();

        var shownTop = hideChosen ? [] : top.Where(Shows).ToList();
        var shownRest = rest.Where(Shows).ToList();
        DrawBulk(shownTop.Concat(shownRest).ToList());

        pointed = null;
        ImGui.BeginChild("##grid");
        if (shownTop.Count + shownRest.Count == 0)
            ImGui.TextDisabled(owned.Count == 0 && top.Count == 0 ? $"No {Plural} unlocked." : "Nothing matches.");
        if (shownTop.Count > 0)
        {
            ImGui.TextDisabled("Chosen");
            DrawGrid(shownTop);
            if (shownRest.Count > 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Not chosen");
            }
        }
        DrawGrid(shownRest);
        ImGui.EndChild();

        // Y saves from anywhere in the grid, without moving up to the button.
        if (Unsaved && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) &&
            ImGui.IsKeyPressed(ImGuiKey.GamepadFaceUp, false))
            saved = Save();

        ImGui.EndChild();
        ImGui.EndPopup();
        return saved;
    }

    private bool DrawHeader()
    {
        var saved = false;
        ImGui.BeginDisabled(!Unsaved);
        if (ImGui.Button("Save"))
            saved = Save();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Y on a controller.");

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(Unsaved ? $"{request!.Title}: not saved yet" : request!.Title);
        return saved;
    }

    private bool Save()
    {
        request!.Save([.. draft]);
        ImGui.CloseCurrentPopup();
        return true;
    }

    private void DrawGrid(List<ShortcutChoice> shown)
    {
        var size = new Vector2(Cell * ImGuiHelpers.GlobalScale);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var columns = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + spacing) / (size.X + spacing)));
        for (var i = 0; i < shown.Count; i++)
        {
            if (i % columns != 0)
                ImGui.SameLine();
            DrawCell(shown[i], size);
        }
    }

    private void Sort()
    {
        var byId = owned.ToDictionary(o => o.RowId);
        top = draft.Select(id => byId.TryGetValue(id, out var choice) ? choice : Missing(id)).ToList();
        rest = owned.Where(o => !draft.Contains(o.RowId)).ToList();
    }

    private void DrawFilters()
    {
        if (ImGui.Checkbox("Hide chosen", ref hideChosen))
            Sort();

        if (allTags.Count > 0)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150f * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginCombo("##type", tag ?? "Any type"))
            {
                if (ImGui.Selectable("Any type", tag == null))
                    tag = null;
                foreach (var t in allTags)
                {
                    if (ImGui.Selectable(t, tag == t))
                        tag = t;
                }
                ImGui.EndCombo();
            }
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(sources.Count == 0);
        ImGui.SetNextItemWidth(200f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("##copy", "Copy from…"))
        {
            foreach (var (label, ids) in sources)
            {
                if (!ImGui.Selectable($"{label} ({ids.Count})"))
                    continue;
                draft.AddRange(ids.Where(id => !draft.Contains(id)).ToList());
                foreach (var id in ids)
                    tags.TryAdd(id, Tags(id));
                Sort();
            }
            ImGui.EndCombo();
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(sources.Count == 0
                ? $"No other role or job has {Plural} of its own."
                : $"Adds another role's or job's {Plural} to these.");
    }

    private void DrawBulk(List<ShortcutChoice> shown)
    {
        ImGui.BeginDisabled(shown.All(c => draft.Contains(c.RowId) || !ownedIds.Contains(c.RowId)));
        if (ImGui.Button("Tick all shown"))
        {
            draft.AddRange(shown.Where(c => ownedIds.Contains(c.RowId) && !draft.Contains(c.RowId)).Select(c => c.RowId).ToList());
            Sort();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!shown.Any(c => draft.Contains(c.RowId)));
        if (ImGui.Button("Untick all shown"))
        {
            var untick = shown.Select(c => c.RowId).ToHashSet();
            draft.RemoveAll(untick.Contains);
            Sort();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(pointed == null ? $"{draft.Count} chosen" : $"{draft.Count} chosen · {pointed}");
    }

    private void DrawCell(ShortcutChoice choice, Vector2 size)
    {
        ImGui.PushID((int)choice.RowId);
        var ticked = draft.Contains(choice.RowId);
        var min = ImGui.GetCursorScreenPos();
        if (ImGui.Selectable("##cell", ticked, ImGuiSelectableFlags.DontClosePopups, size) && !draft.Remove(choice.RowId))
            draft.Add(choice.RowId);
        if (ImGui.IsItemHovered() || ImGui.IsItemFocused())
            pointed = choice.Name;

        var drawList = ImGui.GetWindowDrawList();
        var inset = new Vector2(3f * ImGuiHelpers.GlobalScale);
        icons.Get(choice.IconId, false).Draw(drawList, min + inset, min + size - inset,
                                              ownedIds.Contains(choice.RowId) ? uint.MaxValue : ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.4f)));
        if (ticked)
            drawList.AddRect(min, min + size, ImGui.GetColorU32(ImGuiCol.CheckMark), ImGui.GetStyle().FrameRounding, 0,
                             2f * ImGuiHelpers.GlobalScale);
        ImGui.PopID();
    }

    private bool Shows(ShortcutChoice choice) =>
        (search.Length == 0 || choice.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)) &&
        (tag == null || tags.GetValueOrDefault(choice.RowId)?.Contains(tag) == true);

    // A chosen one no longer unlocked or in the game, still listed so it can be unticked.
    private ShortcutChoice Missing(uint id) =>
        catalog.Describe(new Step { Kind = request!.Kind, RowId = id }) is { } info
            ? new ShortcutChoice(request.Kind, id, $"{info.Name} (locked)", info.IconId, string.Empty)
            : new ShortcutChoice(request.Kind, id, "(no longer in the game)", 0, string.Empty);

    // The Minion Guide's race and behaviour, or a mount's seats.
    private string[] Tags(uint id)
    {
        if (request!.Kind == ShortcutKind.Mount)
            return data.GetExcelSheet<Mount>().TryGetRow(id, out var mount) ? [mount.ExtraSeats > 0 ? MultiSeat : SingleSeat] : [];
        if (!data.GetExcelSheet<Companion>().TryGetRow(id, out var minion))
            return [];
        return new[] { minion.MinionRace.ValueNullable?.Name.ExtractText(), minion.Behavior.ValueNullable?.Name.ExtractText() }
               .OfType<string>().Where(t => t.Length > 0).ToArray();
    }

    private IEnumerable<string> TagOrder()
    {
        if (request!.Kind == ShortcutKind.Mount)
            return [SingleSeat, MultiSeat];
        var races = data.GetExcelSheet<MinionRace>().Select(r => r.Name.ExtractText());
        var behaviours = data.GetExcelSheet<CompanionMove>().Select(b => b.Name.ExtractText());
        return races.Concat(behaviours).Where(t => t.Length > 0).Distinct();
    }

    private string Plural => request?.Kind == ShortcutKind.Mount ? "mounts" : "minions";
}
