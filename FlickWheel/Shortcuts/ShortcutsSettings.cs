using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

using IconAtlasClient;
using Shortcuts;

namespace FlickWheel;

internal sealed class ShortcutsSettings(Configuration config, ShortcutsModule module, ShortcutCatalog catalog, IconCache icons,
                                        WheelOverlay overlay, ShortcutPicker picker, IconPicker iconPicker,
                                        PluginIcons pluginIcons)
{
    // Far-apart hues first.
    private static readonly int[] HueOrder = [11, 6, 0, 2, 14, 8, 3, 16, 12, 5, 9, 15, 1, 4, 7, 10, 13, 17];
    public static int FirstHue => HueOrder[0];

    // An icon chosen in Icon Atlas arrives between frames; the next Draw reports it so it's saved.
    private bool iconPicked;

    private ShortcutWheel Wheel = null!;

    public bool Draw(ShortcutWheel wheel)
    {
        Wheel = wheel;
        var changed = iconPicked;
        iconPicked = false;
        var (headerChanged, deleted) = DrawHeader();
        if (deleted)
            return true;
        changed |= headerChanged;
        ImGui.Spacing();
        ImGui.TextWrapped("Groups are listed clockwise. Drag a shortcut sideways to move it; right-click it to rename, change its icon, move or remove it. Press + to add one.");
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
            picker.Open(addTo.Name, shortcut => addTo.Entries.Add(new ShortcutEntry
            {
                Kind = shortcut.Kind, RowId = shortcut.RowId, Command = shortcut.Command, Label = shortcut.Label,
                IconId = shortcut.IconId,
            }));
        changed |= picker.Draw();

        if (changed)
            overlay.PreviewWheel = module.IdOf(Wheel);
        return changed;
    }

    private (bool Changed, bool Deleted) DrawHeader()
    {
        var changed = false;
        var wheel = Wheel;

        var name = wheel.Name;
        ImGui.SetNextItemWidth(200f);
        if (ImGui.InputTextWithHint("Name", wheel == config.Shortcuts ? "Shortcuts" : "e.g. Emotes", ref name, 32))
        {
            wheel.Name = name;
            changed = true;
        }

        if (wheel == config.Shortcuts)
            return (changed, false);

        ImGui.SameLine();
        var canDelete = wheel.Groups.All(g => g.Entries.Count == 0) || ImGui.GetIO().KeyCtrl;
        ImGui.BeginDisabled(!canDelete);
        var deleted = ImGui.SmallButton("Delete wheel");
        ImGui.EndDisabled();
        if (!canDelete && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Hold Ctrl to delete a wheel that has shortcuts on it.");

        if (deleted)
        {
            var id = module.IdOf(wheel);
            config.MoreShortcutWheels.Remove(wheel);
            foreach (var set in config.WheelCycles)
                set.Wheels.Remove(id);
        }

        return (changed, deleted);
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
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(group.Name.Length > 0 ? group.Name : "(unnamed)");
        if (Wheel.PinnedGroup == index)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(pinned to top)");
        }

        ImGui.SameLine();
        if (ImGui.Button("..."))
            ImGui.OpenPopup("##group");
        var (menuChanged, removed, moveTo, copyTo) = DrawGroupMenu(group, index);
        changed |= menuChanged;

        if (copyTo != null)
        {
            copyTo.Groups.Add(group.Copy());
            changed = true;
        }

        if (removed || moveTo != null)
        {
            groups.RemoveAt(index);
            if (Wheel.PinnedGroup > index || Wheel.PinnedGroup >= groups.Count)
                Wheel.PinnedGroup = Math.Max(0, Wheel.PinnedGroup - 1);
            moveTo?.Groups.Add(group);
            ImGui.PopID();
            return (true, null, true);
        }

        // Applied after the row is drawn, not while it's being iterated.
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

    private (bool Changed, bool Removed, ShortcutWheel? MoveTo, ShortcutWheel? CopyTo) DrawGroupMenu(ShortcutGroup group, int index)
    {
        if (!ImGui.BeginPopup("##group"))
            return (false, false, null, null);

        var changed = false;
        var name = group.Name;
        ImGui.SetNextItemWidth(200f);
        if (ImGui.InputTextWithHint("##rename", "Group name", ref name, 32))
        {
            group.Name = name;
            changed = true;
        }

        ImGui.Separator();
        var pinned = Wheel.PinnedGroup == index;
        if (ImGui.MenuItem("Pin to top", pinned, !pinned))
        {
            Wheel.PinnedGroup = index;
            changed = true;
        }

        ShortcutWheel? moveTo = null;
        var others = module.AllWheels().Where(w => w != Wheel).ToList();
        if (others.Count > 0 && ImGui.BeginMenu("Move to wheel"))
        {
            foreach (var other in others)
            {
                if (ImGui.MenuItem($"{ShortcutsModule.TitleOf(other)}##{other.Uid}"))
                    moveTo = other;
            }
            ImGui.EndMenu();
        }

        ShortcutWheel? copyTo = null;
        if (others.Count > 0 && ImGui.BeginMenu("Copy to wheel"))
        {
            foreach (var other in others)
            {
                if (ImGui.MenuItem($"{ShortcutsModule.TitleOf(other)}##{other.Uid}"))
                    copyTo = other;
            }
            ImGui.EndMenu();
        }

        var canDelete = group.Entries.Count == 0 || ImGui.GetIO().KeyCtrl;
        var removed = ImGui.MenuItem("Delete", false, canDelete);
        if (!canDelete && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Hold Ctrl to delete a group that has shortcuts in it.");

        ImGui.EndPopup();
        return (changed, removed, moveTo, copyTo);
    }

    private RowIcon RowIconOf(ShortcutEntry entry)
    {
        if (catalog.Describe(entry) is not { } info)
            return new RowIcon(entry.Uid, null, "?", "This shortcut no longer exists in the game.", true);

        var available = catalog.Available(entry);
        var tooltip = available ? info.Name : entry.Kind switch
        {
            ShortcutKind.Command => $"{info.Name} (the plugin for this command isn't loaded)",
            ShortcutKind.Text => $"{info.Name} (commands have to start with /)",
            ShortcutKind.Macro => $"{info.Name} (this macro slot is empty)",
            ShortcutKind.Window => $"{info.Name} (you don't have the journal this week)",
            ShortcutKind.BuddyAction => $"{info.Name} (your chocobo isn't out)",
            _ => $"{info.Name} (locked)",
        };
        if (info.IconId == 0 && pluginIcons.Of(entry) is { } glyph)
            return new RowIcon(entry.Uid, null, glyph.Glyph.ToString(), tooltip, !available, IconFont: true);
        var texture = info.IconId != 0 ? icons.Get(info.IconId, info.KeyOutIcon) : (GameIcon?)null;
        return new RowIcon(entry.Uid, texture, Short(info.Name), tooltip, !available);
    }

    private static string Short(string name) => name.Length <= 3 ? name : name[..3];

    private bool DrawEntryMenu(List<ShortcutGroup> groups, int groupIndex, ShortcutEntry entry, ref Action? pending)
    {
        var changed = false;

        var label = entry.Label ?? string.Empty;
        ImGui.SetNextItemWidth(200f);
        if (ImGui.InputTextWithHint("Label", "Default name", ref label, 64))
        {
            entry.Label = label.Length > 0 ? label : null;
            changed = true;
        }

        if (iconPicker.Available)
        {
            iconPicker.Button("Choose icon…", entry.Label ?? "a Flick Wheel shortcut", entry.IconId, picked =>
            {
                entry.IconId = picked;
                iconPicked = true;
            });
            if (entry.IconId != 0)
            {
                ImGui.SameLine();
                if (ImGui.Button("Default icon"))
                {
                    entry.IconId = 0;
                    changed = true;
                }
            }
        }
        else
        {
            var icon = (int)entry.IconId;
            ImGui.SetNextItemWidth(200f);
            if (ImGui.InputInt("Icon ID (0 = default)", ref icon))
            {
                entry.IconId = (uint)Math.Max(0, icon);
                changed = true;
            }
            ImGui.TextDisabled("Load Icon Atlas to choose from every icon.");
        }

        if (entry.Kind is ShortcutKind.Command or ShortcutKind.Text)
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
