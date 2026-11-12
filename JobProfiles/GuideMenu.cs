using System;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Shortcuts;

namespace JobProfiles;

internal sealed class GuideMenu : IDisposable
{
    private readonly IContextMenu contextMenu;
    private readonly Action<ShortcutKind, uint> open;

    public GuideMenu(IContextMenu contextMenu, Action<ShortcutKind, uint> open)
    {
        this.contextMenu = contextMenu;
        this.open = open;
        contextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose() => contextMenu.OnMenuOpened -= OnMenuOpened;

    private unsafe void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType != ContextMenuType.Default)
            return;

        // Right-clicking an entry in the guides selects it.
        AgentMinionMountBase.SelectionData* selection;
        ShortcutKind kind;
        if (args.AddonName == "MountNoteBook")
        {
            var agent = AgentMountNoteBook.Instance();
            selection = agent == null ? null : agent->CurrentSelection;
            kind = ShortcutKind.Mount;
        }
        else if (args.AddonName == "MinionNoteBook")
        {
            var agent = AgentMinionNoteBook.Instance();
            selection = agent == null ? null : agent->CurrentSelection;
            kind = ShortcutKind.Minion;
        }
        else
        {
            return;
        }
        if (selection == null || selection->Id == 0)
            return;

        var id = selection->Id;
        args.AddMenuItem(new MenuItem
        {
            Name = "Job Profiles",
            Prefix = SeIconChar.BoxedLetterJ,
            PrefixColor = 37,
            OnClicked = _ => open(kind, id),
        });
    }
}
