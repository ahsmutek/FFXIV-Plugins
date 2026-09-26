using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace FlickWheel;

// A wheel of your own shortcuts: game windows, Teleport and other general actions,
// items (with how many are left) and plugin commands, in groups you name and color.
internal sealed class ShortcutsModule : IWheelModule
{
    private const string WheelId = "shortcuts";
    private const string PickerPopup = "##addShortcut";

    private readonly Configuration config;
    private readonly ShortcutCatalog catalog;
    private readonly IconCache icons;
    private readonly WheelOverlay overlay;

    // Picker state: which group it adds to, the search text, the Commands tab's fields,
    // and its lists (game data once, the bag contents each time it opens).
    private int pickerGroup = -1;
    private string search = string.Empty;
    private string newLabel = string.Empty;
    private string newCommand = string.Empty;
    private int newIcon;
    private List<ShortcutChoice>? menus;
    private List<ShortcutChoice>? actions;
    private List<ShortcutChoice> items = [];

    public ShortcutsModule(Configuration config, Action save, ShortcutCatalog catalog, IconCache icons, WheelOverlay overlay)
    {
        this.config = config;
        this.catalog = catalog;
        this.icons = icons;
        this.overlay = overlay;
        if (config.Shortcuts == null)
        {
            config.Shortcuts = Starter();
            save();
        }

        Wheels = [new ShortcutsWheel(this)];
        SettingsTabs = [new("Shortcuts", DrawSettings)];
    }

    public string Id => "shortcuts";
    public string Name => "Shortcuts";
    public IReadOnlyList<IWheel> Wheels { get; }
    public IReadOnlyList<SettingsTab> SettingsTabs { get; }

    private ShortcutWheel Wheel => config.Shortcuts!;

    public void OnSettingsOpen() { }

    // What a new install starts with; everything can be changed or removed.
    private static ShortcutWheel Starter() => new()
    {
        Groups =
        [
            new ShortcutGroup
            {
                Name = "Travel",
                Color = Abgr(.14f, .36f, .34f, .88f),
                Entries =
                [
                    new ShortcutEntry { Kind = ShortcutKind.GeneralAction, RowId = 7 }, // Teleport
                    new ShortcutEntry { Kind = ShortcutKind.GeneralAction, RowId = 8 }, // Return
                    new ShortcutEntry { Kind = ShortcutKind.Action, RowId = 26988 },    // Aether Compass
                ],
            },
            new ShortcutGroup
            {
                Name = "Windows",
                Color = Abgr(.16f, .26f, .52f, .88f),
                Entries =
                [
                    new ShortcutEntry { Kind = ShortcutKind.MainCommand, RowId = 33 }, // Duty Finder
                    new ShortcutEntry { Kind = ShortcutKind.MainCommand, RowId = 5 },  // Timers
                    new ShortcutEntry { Kind = ShortcutKind.MainCommand, RowId = 4 },  // Journal
                ],
            },
            new ShortcutGroup
            {
                Name = "Chocobo",
                Color = Abgr(.50f, .36f, .10f, .88f),
                Entries =
                [
                    new ShortcutEntry { Kind = ShortcutKind.Item, RowId = 4868 }, // Gysahl Greens
                    // Only shows while the Chocobo Barding plugin is loaded. Icon: the Companion menu.
                    new ShortcutEntry { Kind = ShortcutKind.Command, Command = "/cbarding job", Label = "Barding for my job", IconId = 49 },
                ],
            },
        ],
    };

    private static uint Abgr(float r, float g, float b, float a) =>
        ((uint)(a * 255) << 24) | ((uint)(b * 255) << 16) | ((uint)(g * 255) << 8) | (uint)(r * 255);

    private static Vector4 ColorOf(ShortcutGroup group) => ImGui.ColorConvertU32ToFloat4(group.Color);

    private sealed class ShortcutsWheel(ShortcutsModule module) : IWheel
    {
        public string Id => WheelId;
        public string Title => "Shortcuts wheel";
        public int MaxSlots => module.Wheel.Groups.Sum(g => g.Entries.Count);

        public WheelRing Layout()
        {
            var groups = module.Wheel.Groups;
            var slots = groups.SelectMany((group, index) => group.Entries.Select(entry => new RingSlot(entry.Uid, index, ColorOf(group))))
                              .ToArray();
            var pinned = groups.Count > 0 ? Math.Clamp(module.Wheel.PinnedGroup, 0, groups.Count - 1) : 0;
            return new WheelRing(slots, (pinned, 0f));
        }

        // Entries that can't be used right now (a locked menu, a plugin that isn't loaded)
        // leave their slot empty. Items you're out of stay, faded.
        public List<WheelEntry> Entries(WheelRing ring)
        {
            var byUid = module.Wheel.Groups.SelectMany(g => g.Entries).ToDictionary(e => e.Uid);
            var entries = new List<WheelEntry>();
            for (var slot = 0; slot < ring.Slots.Length; slot++)
            {
                if (!byUid.TryGetValue(ring.Slots[slot].Key, out var entry) || !module.catalog.Available(entry) ||
                    module.catalog.Describe(entry) is not { } info)
                    continue;

                var count = module.catalog.Count(entry);
                entries.Add(new WheelEntry(slot, info.Name, info.IconId, () => module.catalog.Run(entry),
                                           KeyOutIcon: info.KeyOutIcon, Faded: count == 0, Count: count));
            }

            return entries;
        }
    }

    private bool DrawSettings()
    {
        var changed = false;
        ImGui.TextWrapped("Groups are listed clockwise. Drag a shortcut sideways to move it within its group; right-click it to rename, change its icon, move it to another group or remove it. Press + to add a shortcut.");
        ImGui.Spacing();
        overlay.DrawPreviewControls();

        var groups = Wheel.Groups;
        var openPicker = false;
        for (var g = 0; g < groups.Count; g++)
        {
            ImGui.Spacing();
            ImGui.Separator();
            var (groupChanged, pick, removed) = DrawGroup(groups, g);
            changed |= groupChanged;
            openPicker |= pick;
            if (removed)
                break; // the list changed under the loop; the rest draws next frame
        }

        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button("Add group"))
        {
            groups.Add(new ShortcutGroup { Name = $"Group {groups.Count + 1}", Color = Abgr(.30f, .31f, .36f, .88f) });
            changed = true;
        }

        if (openPicker)
        {
            search = string.Empty;
            items = catalog.Items();
            ImGui.OpenPopup(PickerPopup);
        }
        changed |= DrawPicker();

        if (changed)
            overlay.PreviewWheel = WheelId; // show the wheel being edited
        return changed;
    }

    // One group: arrows, color, name, pin, delete, then its shortcuts and a + button.
    private (bool Changed, bool OpenPicker, bool Removed) DrawGroup(List<ShortcutGroup> groups, int index)
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

        var color = ColorOf(group);
        if (ImGui.ColorEdit4("##color", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
        {
            group.Color = ImGui.ColorConvertFloat4ToU32(color);
            changed = true;
        }
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
        var removed = false;
        var canDelete = group.Entries.Count == 0 || ImGui.GetIO().KeyCtrl;
        ImGui.BeginDisabled(!canDelete);
        if (ImGui.SmallButton("Delete"))
        {
            groups.RemoveAt(index);
            if (Wheel.PinnedGroup > index || Wheel.PinnedGroup >= groups.Count)
                Wheel.PinnedGroup = Math.Max(0, Wheel.PinnedGroup - 1);
            changed = removed = true;
        }
        ImGui.EndDisabled();
        if (!canDelete && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Hold Ctrl to delete a group that has shortcuts in it.");

        if (removed)
        {
            ImGui.PopID();
            return (true, false, true);
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
        var openPicker = false;
        if (ImGui.Button("+", new Vector2(40f)))
        {
            pickerGroup = index;
            openPicker = true;
        }

        ImGui.PopID();
        return (changed, openPicker, false);
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

    // Right-click menu for one shortcut: its label, icon and command, then move or remove.
    private bool DrawEntryMenu(List<ShortcutGroup> groups, int groupIndex, ShortcutEntry entry, ref Action? pending)
    {
        var changed = false;

        var label = entry.Label ?? string.Empty;
        ImGui.SetNextItemWidth(200f);
        if (ImGui.InputTextWithHint("Label", "Game name", ref label, 64))
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
        if (groups.Count > 1 && ImGui.BeginMenu("Move to"))
        {
            for (var g = 0; g < groups.Count; g++)
            {
                if (g == groupIndex)
                    continue;
                var target = groups[g];
                if (ImGui.MenuItem($"{(target.Name.Length > 0 ? target.Name : "(unnamed)")}##{target.Uid}"))
                {
                    var source = groups[groupIndex];
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
        {
            var source = groups[groupIndex];
            pending = () => source.Entries.Remove(entry);
        }

        return changed;
    }

    // The add-a-shortcut popup: tabs for game windows, actions, items and plugin commands.
    private bool DrawPicker()
    {
        ImGui.SetNextWindowSize(new Vector2(420f, 480f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup(PickerPopup))
            return false;

        var changed = false;
        if (pickerGroup < 0 || pickerGroup >= Wheel.Groups.Count)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return false;
        }

        var group = Wheel.Groups[pickerGroup];
        ImGui.TextDisabled($"Add to {(group.Name.Length > 0 ? group.Name : "this group")}");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Search", ref search, 64);

        if (ImGui.BeginTabBar("pickerTabs"))
        {
            if (ImGui.BeginTabItem("Menus"))
            {
                changed |= DrawChoices(group, menus ??= catalog.Menus(), "Nothing matches.");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Actions"))
            {
                changed |= DrawChoices(group, actions ??= catalog.Actions(), "Nothing matches.");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Items"))
            {
                changed |= DrawChoices(group, items, "No usable items in your bags match.");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Commands"))
            {
                changed |= DrawCommandForm(group);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        ImGui.EndPopup();
        return changed;
    }

    // A scrolling list of choices with category headings; clicking one adds it.
    private bool DrawChoices(ShortcutGroup group, List<ShortcutChoice> choices, string empty)
    {
        var changed = false;
        ImGui.BeginChild("choices", new Vector2(0, 0));

        var category = (string?)null;
        var any = false;
        foreach (var choice in choices)
        {
            if (search.Length > 0 && !choice.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;
            any = true;

            if (choice.Category != category)
            {
                category = choice.Category;
                ImGui.Spacing();
                ImGui.TextDisabled(category);
            }

            ImGui.Image(icons.Handle(choice.IconId, false), new Vector2(ImGui.GetFrameHeight()));
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            if (ImGui.Selectable($"{choice.Name}##{choice.Kind}-{choice.RowId}"))
            {
                group.Entries.Add(new ShortcutEntry { Kind = choice.Kind, RowId = choice.RowId });
                changed = true;
                ImGui.CloseCurrentPopup();
            }
        }

        if (!any)
            ImGui.TextDisabled(empty);

        ImGui.EndChild();
        return changed;
    }

    // A plugin command with your own label and icon. The registered commands are listed
    // below so one can be picked instead of typed.
    private bool DrawCommandForm(ShortcutGroup group)
    {
        var changed = false;
        ImGui.TextWrapped("Runs a plugin's command, like /cbarding job. It only shows on the wheel while that plugin is loaded.");
        ImGui.SetNextItemWidth(-90f);
        ImGui.InputTextWithHint("Command", "/command arguments", ref newCommand, 128);
        ImGui.SetNextItemWidth(-90f);
        ImGui.InputTextWithHint("Label", "Shown in the middle of the wheel", ref newLabel, 64);
        ImGui.SetNextItemWidth(-90f);
        ImGui.InputInt("Icon ID", ref newIcon);
        newIcon = Math.Max(0, newIcon);
        if (newIcon > 0)
        {
            ImGui.Image(icons.Handle((uint)newIcon, false), new Vector2(40f));
            ImGui.SameLine();
        }

        ImGui.BeginDisabled(newCommand.Trim().Length == 0 || !newCommand.TrimStart().StartsWith('/'));
        if (ImGui.Button("Add"))
        {
            group.Entries.Add(new ShortcutEntry
            {
                Kind = ShortcutKind.Command,
                Command = newCommand.Trim(),
                Label = newLabel.Trim().Length > 0 ? newLabel.Trim() : null,
                IconId = (uint)newIcon,
            });
            newCommand = newLabel = string.Empty;
            newIcon = 0;
            changed = true;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.TextDisabled("Plugin commands");
        ImGui.BeginChild("commands", new Vector2(0, 0));
        foreach (var command in catalog.PluginCommands())
        {
            if (search.Length > 0 && !command.Contains(search, StringComparison.OrdinalIgnoreCase))
                continue;
            if (ImGui.Selectable(command))
                newCommand = command + " ";
        }
        ImGui.EndChild();

        return changed;
    }
}
