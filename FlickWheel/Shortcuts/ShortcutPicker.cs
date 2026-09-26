using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace FlickWheel;

// The add-a-shortcut popup: tabs for game windows, actions, items and plugin commands.
internal sealed class ShortcutPicker(ShortcutCatalog catalog, IconCache icons)
{
    private const string Popup = "##addShortcut";

    private ShortcutGroup? target;
    private string search = string.Empty;
    // Commands tab fields.
    private string newLabel = string.Empty;
    private string newCommand = string.Empty;
    private int newIcon;
    // Game data lists are built once; the bag contents each time the picker opens.
    private List<ShortcutChoice>? menus;
    private List<ShortcutChoice>? actions;
    private List<ShortcutChoice> items = [];

    // Call at the same ImGui ID level as Draw, or the popup won't find its opener.
    public void Open(ShortcutGroup group)
    {
        target = group;
        search = string.Empty;
        items = catalog.Items();
        ImGui.OpenPopup(Popup);
    }

    /// <summary>Draws the popup while it's open. True when a shortcut was added.</summary>
    public bool Draw()
    {
        if (!ImGui.BeginPopup(Popup))
            return false;

        if (target == null)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return false;
        }

        // Popups always size to their contents, so everything sits in a fixed-size child;
        // otherwise wrapped text would stretch the popup across the screen.
        var changed = false;
        ImGui.BeginChild("##pickerBody", new Vector2(600f, 500f) * ImGuiHelpers.GlobalScale);
        ImGui.TextDisabled($"Add to {(target.Name.Length > 0 ? target.Name : "this group")}");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Search", ref search, 64);

        if (ImGui.BeginTabBar("pickerTabs"))
        {
            if (ImGui.BeginTabItem("Menus"))
            {
                changed |= DrawChoices(menus ??= catalog.Menus(), "Nothing matches.");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Actions"))
            {
                changed |= DrawChoices(actions ??= catalog.Actions(), "Nothing matches.");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Items"))
            {
                changed |= DrawChoices(items, "No usable items in your bags match.");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Commands"))
            {
                changed |= DrawCommandForm();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        ImGui.EndChild();
        ImGui.EndPopup();
        return changed;
    }

    private bool DrawChoices(List<ShortcutChoice> choices, string empty)
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
                target!.Entries.Add(new ShortcutEntry { Kind = choice.Kind, RowId = choice.RowId });
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
    private bool DrawCommandForm()
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
            target!.Entries.Add(new ShortcutEntry
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
