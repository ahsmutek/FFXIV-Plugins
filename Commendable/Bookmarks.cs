using System;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;

namespace Commendable;

internal sealed class Bookmarks : IDisposable
{
    private readonly IContextMenu contextMenu;
    private readonly ITargetManager targets;
    private readonly IChatGui chat;
    private readonly Configuration config;
    private readonly Sessions sessions;

    public Bookmarks(IContextMenu contextMenu, ITargetManager targets, IChatGui chat, Configuration config, Sessions sessions)
    {
        this.contextMenu = contextMenu;
        this.targets = targets;
        this.chat = chat;
        this.config = config;
        this.sessions = sessions;
        contextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose() => contextMenu.OnMenuOpened -= OnMenuOpened;

    // A note only means something on a bookmark, so removing the bookmark clears it.
    public static void Toggle(DutySession session, PlayerRecord player)
    {
        player.Bookmarked = !player.Bookmarked;
        if (!player.Bookmarked)
            player.Note = "";
        session.Version++;
    }

    public static void SetNote(DutySession session, PlayerRecord player, string note)
    {
        player.Note = note;
        player.Bookmarked |= note.Length > 0;
        session.Version++;
    }

    public void ToggleTarget()
    {
        if (!config.Bookmarks)
        {
            chat.Print("Bookmarks are turned off in /commendable config.", Sessions.ChatTag);
            return;
        }

        if ((targets.Target ?? targets.SoftTarget) is not IPlayerCharacter target ||
            sessions.Current is not { } session ||
            Find(session, target.Name.TextValue, target.HomeWorld.RowId) is not { } player)
        {
            chat.Print("Target a party member in your duty to bookmark them.", Sessions.ChatTag);
            return;
        }

        Toggle(session, player);
        chat.Print(player.Bookmarked ? $"Bookmarked {player.Key.Name}." : $"Removed the bookmark for {player.Key.Name}.",
                   Sessions.ChatTag);
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!config.Bookmarks || args.MenuType != ContextMenuType.Default || args.Target is not MenuTargetDefault target ||
            sessions.Current is not { } session ||
            Find(session, target.TargetName, target.TargetHomeWorld.RowId) is not { } player)
            return;

        args.AddMenuItem(new MenuItem
        {
            Name = player.Bookmarked ? "Remove Commendable bookmark" : "Bookmark for commendation",
            Prefix = SeIconChar.BoxedLetterC,
            PrefixColor = 37,
            OnClicked = _ => Toggle(session, player),
        });
    }

    private static PlayerRecord? Find(DutySession session, string name, uint worldId) =>
        session.Players.TryGetValue(new PlayerKey(name, worldId), out var player) && player.LeftAt == null ? player : null;
}
