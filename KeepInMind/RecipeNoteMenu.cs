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

// The game keeps the right-clicked item in AgentRecipeNote.
internal sealed class RecipeNoteMenu : IDisposable
{
    private readonly IContextMenu contextMenu;
    private readonly Action<uint, int> add;

    private readonly FrozenSet<uint> ingredients;

    public RecipeNoteMenu(IContextMenu contextMenu, IDataManager data, Action<uint, int> add)
    {
        this.contextMenu = contextMenu;
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
        if (args.MenuType != ContextMenuType.Default || args.AddonName != "RecipeNote")
            return;

        var itemId = BaseItemId(AgentRecipeNote.Instance()->ContextMenuResultItemId);
        if (!ingredients.Contains(itemId))
            return;

        var amount = AmountInShownRecipe(itemId);
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
