using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Controller;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Recipe = Lumina.Excel.Sheets.Recipe;

namespace GlamKit;

// The game's item menu needs the item in a bag, so /glamkit opens an empty game menu and fills it through Dalamud,
// as plugins add to the game's menus. Item Vendor Location only adds its entry to item menus, so its IPC is used.
internal sealed unsafe class ItemMenu : IDisposable
{
    private const string VendorPlugin = "ItemVendorLocation";
    private const string Addon = "ContextMenu";
    private const string FallbackPopup = "##itemMenu";
    private const int ShowWithinFrames = 10;
    private const uint NoObject = 0xE0000000;

    private sealed record Entry(string Name, Action OnClick, bool Enabled = true, SeIconChar? Prefix = null,
                                ushort PrefixColor = 0, bool OpensWindow = false);

    private readonly IContextMenu contextMenu;
    private readonly IGameGui gameGui;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IDataManager data;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly Func<uint, bool> owned;
    private readonly Action<uint, bool> setKept;
    private readonly Action<uint, bool> setLater;
    private readonly Action<uint, bool> setWished;
    private readonly ICallGateSubscriber<uint, bool, HashSet<(uint, uint, (float, float))>?> getItemVendors;
    private readonly ICallGateSubscriber<uint, object?> openVendorResults;
    private readonly ushort pink;

    private HashSet<uint>? craftable;
    private List<Entry>? opening;
    private List<Entry> fallback = [];
    private bool fallbackRequested;
    private Entry? clicked;
    private bool shown;
    private bool seen;
    private int waited;
    private bool fromPad;
    private bool unfocus;
    private bool openedWindow;
    private bool focusBack;
    private float top;
    private Vector2 avoidMin;
    private Vector2 avoidMax;

    public ItemMenu(IContextMenu contextMenu, IGameGui gameGui, IDalamudPluginInterface pluginInterface, IDataManager data,
                    IChatGui chat, IPluginLog log, Configuration config, Func<uint, bool> owned,
                    Action<uint, bool> setKept, Action<uint, bool> setLater, Action<uint, bool> setWished)
    {
        this.contextMenu = contextMenu;
        this.gameGui = gameGui;
        this.pluginInterface = pluginInterface;
        this.data = data;
        this.chat = chat;
        this.log = log;
        this.config = config;
        this.owned = owned;
        this.setKept = setKept;
        this.setLater = setLater;
        this.setWished = setWished;
        getItemVendors = pluginInterface.GetIpcSubscriber<uint, bool, HashSet<(uint, uint, (float, float))>?>(
            "ItemVendorLocation.GetItemVendors");
        openVendorResults = pluginInterface.GetIpcSubscriber<uint, object?>("ItemVendorLocation.OpenVendorResults");
        pink = ItemTooltip.Nearest(data, ItemTooltip.Pink);
        contextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose() => contextMenu.OnMenuOpened -= OnMenuOpened;

    // True only while Open waits for Dalamud's menu event, so other handlers can leave this menu alone.
    public bool Opening => opening != null;

    public bool IsOpen => shown;

    // The game draws under ImGui, so the menu goes beside the window rather than at the row.
    public void Open(uint itemId, string name, float rowTop, Vector2 windowMin, Vector2 windowMax,
                     IEnumerable<(string Name, Action OnClick)>? extras = null)
    {
        var entries = Entries(itemId, name);
        if (extras != null)
            entries.AddRange(extras.Select(e => new Entry(e.Name, e.OnClick)));
        var agent = AgentContext.Instance();
        if (agent != null)
        {
            top = rowTop;
            avoidMin = windowMin;
            avoidMax = windowMax;
            // Dalamud passes these on as the menu's target, and they still hold the last player right-clicked.
            agent->TargetName.Clear();
            agent->TargetContentId = 0;
            agent->TargetAccountId = 0;
            agent->TargetObjectId = NoObject;
            agent->TargetHomeWorldId = 0;
            agent->CurrentContextMenuTarget = null;
            agent->ClearMenu();
            agent->SetPosition((int)windowMax.X, (int)rowTop);

            opening = entries;
            agent->OpenContextMenu(false);
            var filled = opening == null;
            opening = null;
            if (filled)
            {
                shown = true;
                seen = false;
                waited = 0;
                openedWindow = false;
                fromPad = PadNav.Navigating;
                unfocus = fromPad;
                return;
            }
            log.Debug("The game menu didn't open through Dalamud, so the item menu is ImGui's.");
        }
        fallback = entries;
        fallbackRequested = true;
    }

    // Call once a frame before the windows draw: runs a chosen entry, and keeps the menu beside the window.
    public void Update()
    {
        // The game only gets the pad while no plugin window has focus.
        if (unfocus)
        {
            unfocus = false;
            ImGuiP.FocusWindow(default);
        }
        if (clicked != null)
        {
            var entry = clicked;
            clicked = null;
            openedWindow = entry.OpensWindow;
            entry.OnClick();
        }
        if (!shown)
            return;

        var addon = (AtkUnitBase*)gameGui.GetAddonByName(Addon).Address;
        if (addon != null && addon->IsVisible && addon->RootNode != null)
        {
            seen = true;
            Place(addon);
            return;
        }
        if (!seen && ++waited < ShowWithinFrames)
            return;
        shown = false;
        focusBack = fromPad && !openedWindow;
    }

    // Whether the window should take focus back, as the pad went to the game for the menu.
    public bool TakeFocusBack()
    {
        var back = focusBack;
        focusBack = false;
        return back;
    }

    // Call inside the window, at the same level each frame.
    public void DrawFallback()
    {
        if (fallbackRequested)
        {
            fallbackRequested = false;
            ImGui.OpenPopup(FallbackPopup);
        }
        using var popup = ImRaii.Popup(FallbackPopup);
        if (!popup)
            return;
        foreach (var entry in fallback)
        {
            var label = entry.Prefix is { } prefix ? $"{prefix.ToIconString()} {entry.Name}" : entry.Name;
            if (ImGui.Selectable(label, false, entry.Enabled ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.Disabled))
                entry.OnClick();
        }
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (opening == null)
        {
            shown = false;
            return;
        }
        foreach (var entry in opening)
        {
            args.AddMenuItem(new MenuItem
            {
                Name = entry.Name,
                IsEnabled = entry.Enabled,
                Prefix = entry.Prefix,
                PrefixColor = entry.PrefixColor,
                // Run next frame, inside ImGui's, for the clipboard.
                OnClicked = _ => clicked = entry,
            });
        }
        opening = null;
    }

    private void Place(AtkUnitBase* addon)
    {
        var size = new Vector2(addon->RootNode->Width, addon->RootNode->Height) * addon->Scale;
        var position = NativeTooltip.Beside(size, top, avoidMin, avoidMax, ImGui.GetMainViewport().Size);
        if (addon->X != (short)position.X || addon->Y != (short)position.Y)
            addon->SetPosition((short)position.X, (short)position.Y);
    }

    private List<Entry> Entries(uint itemId, string name)
    {
        var gear = KeeperMenu.IsGear(data, itemId);
        var entries = new List<Entry>();
        if (gear)
            entries.Add(new Entry("Try On", () => AgentTryon.TryOn(0, itemId, 0, 0, 0, false), OpensWindow: true));
        // Only the game can put a link in the chat box; the printed one's own menu has Link.
        entries.Add(new Entry("Link", () => chat.Print(new SeStringBuilder().AddItemLink(itemId, false).Build(), "GlamKit")));
        if (IsCraftable(itemId))
            entries.Add(new Entry("Open in Crafting Log", () => OpenRecipe(itemId), OpensWindow: true));
        if (SoldByVendor(itemId) is { } sold)
            entries.Add(new Entry("Vendor location", () => ShowVendors(itemId), sold, OpensWindow: true));
        entries.Add(new Entry("Copy Item Name", () => ImGui.SetClipboardText(name)));

        if (gear && config.GlamKeeperEnabled)
        {
            var kept = config.IsKept(itemId);
            entries.Add(new Entry(kept ? "Stop Keeping for Glam" : "Keep for Glam", () => setKept(itemId, !kept),
                                  Prefix: SeIconChar.BoxedLetterG, PrefixColor: KeeperMenu.GlamColor));
        }
        if (gear && config.KeepForLaterEnabled)
        {
            var kept = config.IsKeptForLater(itemId);
            entries.Add(new Entry(kept ? "Stop Keeping for Later" : "Keep for Later", () => setLater(itemId, !kept),
                                  Prefix: SeIconChar.BoxedLetterL, PrefixColor: KeeperMenu.LaterColor));
        }
        // An item you have would come straight off the wishlist.
        var wished = config.IsWished(itemId);
        if (config.WishlistEnabled && (wished || !owned(itemId)))
        {
            entries.Add(new Entry(wished ? "Remove from Wishlist" : "Add to Wishlist", () => setWished(itemId, !wished),
                                  Prefix: SeIconChar.BoxedLetterW, PrefixColor: pink));
        }
        return entries;
    }

    private bool IsCraftable(uint itemId)
    {
        craftable ??= data.GetExcelSheet<Recipe>().Select(r => r.ItemResult.RowId).ToHashSet();
        return craftable.Contains(itemId);
    }

    private static void OpenRecipe(uint itemId)
    {
        var recipes = AgentRecipeNote.Instance();
        if (recipes != null)
            recipes->SearchRecipeByItemId(itemId);
    }

    // Null when Item Vendor Location isn't loaded.
    private bool? SoldByVendor(uint itemId)
    {
        if (!pluginInterface.InstalledPlugins.Any(p => p.IsLoaded && p.InternalName == VendorPlugin))
            return null;
        try
        {
            return getItemVendors.InvokeFunc(itemId, false) is { Count: > 0 };
        }
        catch (Exception e)
        {
            log.Debug(e, "Item Vendor Location didn't answer GetItemVendors.");
            return false;
        }
    }

    private void ShowVendors(uint itemId)
    {
        try
        {
            openVendorResults.InvokeFunc(itemId);
        }
        catch (Exception e)
        {
            log.Warning(e, "Couldn't open Item Vendor Location's results.");
        }
    }
}
