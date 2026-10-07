# Keep in Mind

A simple checklist of ingredients to gather or buy by hand while you craft.

## Usage

- In the Crafting Log, right-click an ingredient and choose **Keep in mind**. It goes on
  one list shared by every recipe and crafting job, and the list opens.
- Each item shows how many you need: the amount the recipe shown in the Crafting Log
  takes. Change it with − and + (or type a number).
- With a controller (Dalamud's gamepad mode): **A** on an item's name opens its log,
  **Y / Triangle** opens its right-click menu, **A** on − / + changes how many. Opening an
  item's log turns gamepad mode off for you, so the controller goes straight back to the
  game on that log.
- An item is only listed once. Adding one that's already there (another recipe needs it
  too) adds that recipe's amount to it, un-ticks it, and lights up its row.
- An item ticks itself once your bags (and crystals) hold as many as it needs, NQ and HQ
  together; hover its count to see how many you have. It stays ticked if you then use
  them up, and un-ticking it by hand sticks until the count drops and climbs back.
- You can also tick the box yourself; **×** removes it. **Clear completed** removes the ticked
  ones, **Clear all** empties the list after asking.
- Hover an item for the game's own item tooltip, so you see how many you have, plus
  whatever other plugins add to it (e.g. where Allagan Tools says they are).
- Click an item to open it in the Gathering Log (mining, botany), the Fish Guide (fish,
  spearfishing) or the Crafting Log (craftable items), the same way the game's own "Search
  for Item by Gathering/Crafting Method" does. Gathering wins when an item is both.
- Right-click an item in the list for **Vendor location**: opens [Item Vendor Location](https://github.com/electr0sheep/ItemVendorLocation)'s vendor list for
  the item, when it knows vendors for it (greyed out while that plugin isn't loaded).
- The list is saved between sessions. `/keepinmind` (or the plugin's settings button,
  as there are no settings) shows or hides it.

Nothing is crafted, gathered or bought for you, and recipes aren't expanded into their
sub-ingredients: the list holds exactly what you add.

## How it works

The **Keep in mind** entry is added through Dalamud's context menu service, for the
Crafting Log (`RecipeNote`) only. The game keeps the right-clicked item in
`AgentRecipeNote.ContextMenuResultItemId`, the same field Item Vendor Location reads for
its own entry in that menu. It only appears on items some recipe
uses as an ingredient, so the crafted item itself doesn't get it.

The list's right-click menu is an ImGui popup, not a game context menu. Item Vendor Location
adds its entry only to the game's own menus, and only for game windows it recognises
(Crafting Log, recipe tree, chat, inventory…), taking the item from that window's game
state. A plugin window can't open such a menu without faking that state, so the list uses
its IPC instead: `ItemVendorLocation.GetItemVendors` decides whether to show the entry,
`ItemVendorLocation.OpenVendorResults` opens its results window for the item. Its own menu
entry may instead print a single vendor in chat, depending on its settings; the IPC always
opens the results window.

The game anchors its tooltips to its own windows, so for the hover tooltip the plugin fills
in the game's item detail agent and shows its `ItemDetail` window directly, closing it when
the mouse leaves the item. This is how [Chat 2](https://github.com/Infiziert90/ChatTwo)
shows native tooltips for item links in its ImGui chat window.

Checked against Item Vendor Location 2.14.0.1.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\KeepInMind.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
