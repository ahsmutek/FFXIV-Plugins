using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace FlickWheel;

// The Shortcuts settings tab: color style, then each group with its shortcuts.
internal sealed class ShortcutsSettings(Configuration config, ShortcutCatalog catalog, IconCache icons,
                                        WheelOverlay overlay, ShortcutPicker picker)
{
    // Hues handed out to new groups and by Auto colors: far apart first, near neighbours later.
    private static readonly int[] HueOrder = [11, 6, 0, 2, 14, 8, 3, 16, 12, 5, 9, 15, 1, 4, 7, 10, 13, 17];

    private ShortcutWheel Wheel => config.Shortcuts!;

    public bool Draw()
    {
        var changed = false;
        ImGui.TextWrapped("Groups are listed clockwise. Drag a shortcut sideways to move it within its group; right-click it to rename, change its icon, move it to another group or remove it. Press + to add a shortcut.");
        var groups = Wheel.Groups;
        ImGui.Spacing();
        changed |= DrawColorStyle(groups);

        ShortcutGroup? addTo = null;
        for (var g = 0; g < groups.Count; g++)
        {
            ImGui.Spacing();
            ImGui.Separator();
            var (groupChanged, add, removed) = DrawGroup(groups, g);
            changed |= groupChanged;
            addTo ??= add;
            if (removed)
                break; // the list changed under the loop; the rest draws next frame
        }

        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button("Add group"))
        {
            groups.Add(new ShortcutGroup { Name = $"Group {groups.Count + 1}", Hue = NextHue(groups) });
            changed = true;
        }

        if (addTo != null)
            picker.Open(addTo);
        changed |= picker.Draw();

        if (changed)
            overlay.PreviewWheel = ShortcutsModule.WheelId; // show the wheel being edited
        return changed;
    }

    private static int NextHue(List<ShortcutGroup> groups) =>
        HueOrder.FirstOrDefault(hue => groups.All(g => g.Hue != hue), HueOrder[0]);

    private bool DrawColorStyle(List<ShortcutGroup> groups)
    {
        var changed = false;
        ImGui.TextDisabled("Colors");
        var style = Wheel.Style;
        if (GroupEditor.StyleButtons(ref style))
        {
            Wheel.Style = style;
            changed = true;
        }

        if (ImGui.SmallButton("Auto colors"))
        {
            for (var g = 0; g < groups.Count; g++)
                groups[g].Hue = HueOrder[g % HueOrder.Length];
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Give every group a different color.");

        return changed;
    }

    // One group: arrows, color, name, pin, delete, its shortcuts and a + button. Returns the
    // group to add a shortcut to when + was pressed, and whether the group was deleted.
    private (bool Changed, ShortcutGroup? AddTo, bool Removed) DrawGroup(List<ShortcutGroup> groups, int index)
    {
        var changed = false;
        var group = groups[index];
        ImGui.PushID(group.Uid);

        if (GroupEditor.MoveArrows(index, groups.Count) is var step and not 0)
        {
            var other = index + step;
            (groups[index], groups[other]) = (groups[other], groups[index]);
            if (Wheel.PinnedGroup == index)
                Wheel.PinnedGroup = other;
            else if (Wheel.PinnedGroup == other)
                Wheel.PinnedGroup = index;
            changed = true;
        }
        ImGui.SameLine();

        var hue = group.Hue;
        var custom = group.Color;
        if (GroupEditor.ColorSwatch(ref hue, ref custom, Wheel.Style))
        {
            group.Hue = hue;
            group.Color = custom;
            changed = true;
        }
        var color = ShortcutsModule.ColorOf(group, Wheel.Style);
        ImGui.SameLine();
        var name = group.Name;
        ImGui.SetNextItemWidth(140f);
        if (ImGui.InputText("##name", ref name, 32))
        {
            group.Name = name;
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Pin to top", Wheel.PinnedGroup == index))
        {
            Wheel.PinnedGroup = index;
            changed = true;
        }

        // A group with shortcuts in it needs Ctrl held, so it can't go by accident.
        ImGui.SameLine();
        var canDelete = group.Entries.Count == 0 || ImGui.GetIO().KeyCtrl;
        ImGui.BeginDisabled(!canDelete);
        var removed = ImGui.SmallButton("Delete");
        ImGui.EndDisabled();
        if (!canDelete && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Hold Ctrl to delete a group that has shortcuts in it.");

        if (removed)
        {
            groups.RemoveAt(index);
            if (Wheel.PinnedGroup > index || Wheel.PinnedGroup >= groups.Count)
                Wheel.PinnedGroup = Math.Max(0, Wheel.PinnedGroup - 1);
            ImGui.PopID();
            return (true, null, true);
        }

        // Remove and Move to happen after the row is drawn, not while it's being walked.
        Action? pending = null;
        var entries = group.Entries;
        var move = GroupEditor.IconRow(entries.Count, color, i => RowIconOf(entries[i]), i =>
        {
            changed |= DrawEntryMenu(groups, index, entries[i], ref pending);
        });
        if (move is var (from, to))
        {
            var entry = entries[from];
            entries.RemoveAt(from);
            entries.Insert(to, entry);
            changed = true;
        }
        if (pending != null)
        {
            pending();
            changed = true;
        }

        if (entries.Count > 0)
            ImGui.SameLine(0, 4f);
        var add = ImGui.Button("+", new Vector2(40f));

        ImGui.PopID();
        return (changed, add ? group : null, false);
    }

    private RowIcon RowIconOf(ShortcutEntry entry)
    {
        if (catalog.Describe(entry) is not { } info)
            return new RowIcon(entry.Uid, null, "?", "This shortcut no longer exists in the game.", true);

        var available = catalog.Available(entry);
        var tooltip = available ? info.Name : entry.Kind == ShortcutKind.Command
            ? $"{info.Name} (the plugin for this command isn't loaded)"
            : $"{info.Name} (locked)";
        var texture = info.IconId != 0 ? icons.Handle(info.IconId, info.KeyOutIcon) : (ImTextureID?)null;
        return new RowIcon(entry.Uid, texture, Short(info.Name), tooltip, !available);
    }

    // First letters for icon-less entries, e.g. "/cbarding job" → "/cb".
    private static string Short(string name) => name.Length <= 3 ? name : name[..3];

    private static bool DrawEntryMenu(List<ShortcutGroup> groups, int groupIndex, ShortcutEntry entry, ref Action? pending)
    {
        var changed = false;

        var label = entry.Label ?? string.Empty;
        ImGui.SetNextItemWidth(200f);
        if (ImGui.InputTextWithHint("Label", "Default name", ref label, 64))
        {
            entry.Label = label.Length > 0 ? label : null;
            changed = true;
        }

        var icon = (int)entry.IconId;
        ImGui.SetNextItemWidth(200f);
        if (ImGui.InputInt("Icon ID (0 = default)", ref icon))
        {
            entry.IconId = (uint)Math.Max(0, icon);
            changed = true;
        }

        if (entry.Kind == ShortcutKind.Command)
        {
            var command = entry.Command ?? string.Empty;
            ImGui.SetNextItemWidth(200f);
            if (ImGui.InputText("Command", ref command, 128))
            {
                entry.Command = command;
                changed = true;
            }
        }

        ImGui.Separator();
        var source = groups[groupIndex];
        if (groups.Count > 1 && ImGui.BeginMenu("Move to"))
        {
            foreach (var target in groups.Where(g => g != source))
            {
                if (ImGui.MenuItem($"{(target.Name.Length > 0 ? target.Name : "(unnamed)")}##{target.Uid}"))
                {
                    pending = () =>
                    {
                        source.Entries.Remove(entry);
                        target.Entries.Add(entry);
                    };
                }
            }
            ImGui.EndMenu();
        }

        if (ImGui.MenuItem("Remove"))
            pending = () => source.Entries.Remove(entry);

        return changed;
    }
}
