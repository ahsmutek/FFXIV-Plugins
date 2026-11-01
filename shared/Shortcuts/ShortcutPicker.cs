using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Controller;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace Shortcuts;

internal sealed class ShortcutPicker(ShortcutCatalog catalog, IconCache icons)
{
    private const string Popup = "##addShortcut";

    private string? targetName;
    private Action<PickedShortcut>? add;
    private string search = string.Empty;
    private string newLabel = string.Empty;
    private string newCommand = string.Empty;
    private int newIcon;
    private List<ShortcutChoice>? menus;
    private List<ShortcutChoice>? actions;
    private List<ShortcutChoice> items = [];
    private List<ShortcutChoice> macros = [];
    private List<ShortcutChoice> favoriteEmotes = [];
    private List<ShortcutChoice> allEmotes = [];
    // In the order ticked, across tabs.
    private readonly List<(ShortcutKind Kind, uint RowId)> selected = [];
    private readonly PadTabs tabs = new();

    // Call at the same ImGui ID level as Draw, or the popup won't find its opener.
    public void Open(string name, Action<PickedShortcut> onPick)
    {
        targetName = name;
        add = onPick;
        search = string.Empty;
        selected.Clear();
        items = catalog.Items();
        macros = catalog.Macros();
        favoriteEmotes = catalog.FavoriteEmotes();
        allEmotes = catalog.UnlockedEmotes();
        ImGui.OpenPopup(Popup);
    }

    public bool Draw()
    {
        if (!ImGui.BeginPopup(Popup))
            return false;

        if (add == null)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return false;
        }

        // Popups size to their contents; a fixed-size child stops wrapped text stretching it.
        var changed = false;
        ImGui.BeginChild("##pickerBody", new Vector2(600f, 500f) * ImGuiHelpers.GlobalScale);
        ImGui.TextDisabled($"Add to {(string.IsNullOrEmpty(targetName) ? "this list" : targetName)}");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Search", ref search, 64);

        if (ImGui.BeginTabBar("pickerTabs"))
        {
            if (tabs.Item("Menus"))
            {
                changed |= DrawChoices(menus ??= catalog.Menus(), "Nothing matches.");
                ImGui.EndTabItem();
            }
            if (tabs.Item("Actions"))
            {
                changed |= DrawChoices(actions ??= catalog.Actions(), "Nothing matches.");
                ImGui.EndTabItem();
            }
            if (tabs.Item("Items"))
            {
                changed |= DrawChoices(items, "No usable items in your bags match.");
                ImGui.EndTabItem();
            }
            if (tabs.Item("Macros"))
            {
                changed |= DrawChoices(macros, "No macros match.");
                ImGui.EndTabItem();
            }
            if (tabs.Item("Emotes"))
            {
                changed |= DrawEmotes();
                ImGui.EndTabItem();
            }
            if (tabs.Item("Commands"))
            {
                changed |= DrawCommandForm();
                ImGui.EndTabItem();
            }
            tabs.End();
            ImGui.EndTabBar();
        }

        ImGui.EndChild();
        ImGui.EndPopup();
        return changed;
    }

    private bool DrawEmotes()
    {
        var others = allEmotes.Where(e => favoriteEmotes.All(f => f.RowId != e.RowId));
        return DrawChoices(favoriteEmotes.Concat(others).ToList(), "No unlocked emotes match.");
    }

    private bool DrawChoices(List<ShortcutChoice> choices, string empty)
    {
        ImGui.BeginChild("choices", new Vector2(0, -ImGui.GetFrameHeightWithSpacing()));

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

            ImGui.PushID($"{choice.Kind}-{choice.RowId}");
            var key = (choice.Kind, choice.RowId);
            var on = selected.Contains(key);
            var toggled = ImGui.Checkbox("##pick", ref on);
            ImGui.SameLine();
            icons.Get(choice.IconId, false).Image(new Vector2(ImGui.GetFrameHeight()));
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            toggled |= ImGui.Selectable(choice.Name, selected.Contains(key), ImGuiSelectableFlags.DontClosePopups);
            if (toggled && !selected.Remove(key))
                selected.Add(key);
            ImGui.PopID();
        }

        if (!any)
            ImGui.TextDisabled(empty);

        ImGui.EndChild();
        return DrawAddSelected();
    }

    private bool DrawAddSelected()
    {
        ImGui.BeginDisabled(selected.Count == 0);
        var confirmed = ImGui.Button(selected.Count > 1 ? $"Add {selected.Count}" : "Add");
        ImGui.EndDisabled();
        if (selected.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear"))
                selected.Clear();
        }
        ImGui.SameLine();
        ImGui.TextDisabled(selected.Count == 0 ? "Click entries to tick them, on any tab." : "Added in the order you ticked them.");

        if (!confirmed)
            return false;
        foreach (var (kind, rowId) in selected)
            add!(new PickedShortcut(kind, rowId));
        selected.Clear();
        ImGui.CloseCurrentPopup();
        return true;
    }

    private bool DrawCommandForm()
    {
        var changed = false;
        ImGui.TextWrapped("Runs a command as if typed in chat: a game command like /gs change 3 or /hudlayout 2, or a plugin's, like /cbarding job. A plugin's command only shows while that plugin is loaded.");
        ImGui.SetNextItemWidth(-90f);
        ImGui.InputTextWithHint("Command", "/command arguments", ref newCommand, 128);
        ImGui.SetNextItemWidth(-90f);
        ImGui.InputTextWithHint("Label", "Name to show instead of the command", ref newLabel, 64);
        ImGui.SetNextItemWidth(-90f);
        ImGui.InputInt("Icon ID", ref newIcon);
        newIcon = Math.Max(0, newIcon);
        if (newIcon > 0)
        {
            icons.Get((uint)newIcon, false).Image(new Vector2(40f));
            ImGui.SameLine();
        }

        ImGui.BeginDisabled(newCommand.Trim().Length == 0 || !newCommand.TrimStart().StartsWith('/'));
        if (ImGui.Button("Add"))
        {
            var command = newCommand.Trim();
            add!(new PickedShortcut(catalog.IsPluginCommand(command) ? ShortcutKind.Command : ShortcutKind.Text, 0, command,
                                    newLabel.Trim().Length > 0 ? newLabel.Trim() : null, (uint)newIcon));
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
