using System;
using System.Collections.Frozen;
using System.Linq;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Recipe = Lumina.Excel.Sheets.Recipe;

namespace KeepInMind;

// The game keeps the right-clicked item in the log's agent.
internal sealed class LogMenu : IDisposable
{
    // 4 when a Gathering Log item was right-clicked, 1 for a location; offset as in GatherBuddy.
    private const int GatheringNoteContextKind = 0x38;

    private readonly IContextMenu contextMenu;
    private readonly Sources sources;
    private readonly Action<uint, int> add;

    private readonly FrozenSet<uint> ingredients;

    public LogMenu(IContextMenu contextMenu, IDataManager data, Sources sources, Action<uint, int> add)
    {
        this.contextMenu = contextMenu;
        this.sources = sources;
        this.add = add;
        ingredients = data.GetExcelSheet<Recipe>()
                          .SelectMany(r => r.Ingredient.Select(i => i.RowId))
                          .Where(id => id != 0)
                          .ToFrozenSet();
        contextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose() => contextMenu.OnMenuOpened -= OnMenuOpened;

    private unsafe void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType != ContextMenuType.Default)
            return;

        uint itemId;
        int amount;
        if (args.AddonName == "RecipeNote")
        {
            itemId = BaseItemId(AgentRecipeNote.Instance()->ContextMenuResultItemId);
            if (!ingredients.Contains(itemId))
                return;
            amount = AmountInShownRecipe(itemId);
        }
        else if (args.AddonName == "GatheringNote")
        {
            if (*(byte*)(args.AgentPtr + GatheringNoteContextKind) != 4)
                return;
            itemId = BaseItemId(AgentGatheringNote.Instance()->ContextMenuItemId);
            if (!sources.Gatherables.Contains(itemId))
                return;
            amount = 1;
        }
        else
        {
            return;
        }

        args.AddMenuItem(new MenuItem
        {
            Name = "Keep in mind",
            Prefix = SeIconChar.BoxedLetterK,
            PrefixColor = 37,
            OnClicked = _ => add(itemId, amount),
        });
    }

    private static unsafe int AmountInShownRecipe(uint itemId)
    {
        var note = RecipeNote.Instance();
        var recipe = note == null || note->RecipeList == null ? null : note->RecipeList->SelectedRecipe;
        if (recipe == null)
            return 1;

        foreach (ref var ingredient in recipe->Ingredients)
        {
            if (ingredient.ItemId == itemId && ingredient.Amount > 0)
                return ingredient.Amount;
        }
        return 1;
    }

    private static uint BaseItemId(uint id) => id switch
    {
        >= 1000000 => id - 1000000,
        >= 500000 => id - 500000,
        _ => id,
    };
}
