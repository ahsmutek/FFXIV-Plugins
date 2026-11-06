# Keep in Mind

A checklist of ingredients to gather or buy by hand while you craft.

## Usage

- In the Crafting Log or Gathering Log, right-click an item and choose **Keep in mind**. It goes on
  one list shared by every recipe and job.
- Each item needs the amount its recipe takes (1 from the Gathering Log); change it with − and +.
  Adding an item that's already listed adds to its amount and un-ticks it.
- Items tick themselves once your bags hold enough (NQ and HQ together). Un-ticking by hand
  sticks until the count drops and climbs back.
- Click an item to open it in the Gathering Log, Fish Guide or Crafting Log. Hover it for the
  game's own tooltip, including what other plugins add.
- Right-click an item for **Vendor location**, via
  [Item Vendor Location](https://github.com/electr0sheep/ItemVendorLocation), and to give it its
  own **Colour** or move it to a **Group** of your own.
- **Clear completed** removes ticked items; **Clear all** empties the list.
- With a controller: **A** opens an item's log, **Y** its menu, **B** goes back to the game and leaves the list open.
- `/keepinmind` shows the list or brings it back from the game; run it while you're using the list to hide it. The cog opens the settings.

Nothing is crafted, gathered or bought for you, and recipes aren't expanded into
sub-ingredients.

## Groups and colours

- The list is grouped into the zones you gather in, NPC vendor, Crafting and your own groups.
  Zones follow game order (expansion, then region) and sit where the first of Botanist, Miner and
  Fisher is in the group order; an item found in several zones goes under the one with its
  lowest-level node. Gatherable items show their class's icon. Reorder groups in the settings or
  from a group header's right-click menu; an item with several sources goes in whichever comes
  first. **Retainers first** puts items you still need that a
  retainer holds under that retainer's name (the one holding the most), then the saddlebag, before
  the other groups. Turn grouping off, or sort by inventory order, name or most still needed
  instead of your own order, in the settings. In your own order, drag an item up or down within
  its group (or use Move up / Move down in its menu).
- Items you hold in a retainer or the saddlebag show a moneybag: a green + if that and your bags
  cover what you need, a yellow − if not; this needs
  [Allagan Tools](https://github.com/Critical-Impact/InventoryTools). An item's name takes the
  colour you gave it, else its group's, else its source's. All colours can be changed.
- The game's item tooltip shows how many of a listed item you need and have, under its
  description, in blue: `Keep in Mind: need 12, have 5, 4 in retainers`.
- In your inventory, saddlebag and a retainer's inventory, slots holding an item you still need get
  a border in the same green or yellow.
- NPC vendor items need Item Vendor Location to be loaded.
- Completed items can stay in place, move to a collapsed **Completed** section, or be hidden.

## How it works

- The menu entry reads the right-clicked item from `AgentRecipeNote` or `AgentGatheringNote`, as
  Item Vendor Location and GatherBuddy do.
- Item Vendor Location and Allagan Tools only add to the game's own menus and tooltips, so the
  list calls their IPC (`GetItemVendors`, `OpenVendorResults`; `ItemCountOwned`,
  `GetItemCountsByCharacter`). Checked against Item Vendor Location 2.14.0.1 and Allagan Tools
  15.0.12.
- Slots are matched to items through `ItemOrderModule`'s sorters (bags, saddlebag, active
  retainer), which list items in the order the windows show them, and the page each grid shows
  comes from its window's tab (the large retainer window keeps all five pages open, stacked two
  to a tab). A slot is only bordered if it also shows that item's icon. Borders are drawn with
  ImGui over the slot nodes, and skipped where any game window drawn above the slot's window
  (lower draw order index) overlaps it.
- The game fills the item tooltip's text during `ItemDetail`'s update, so the line is added by
  hooking the function that lays it out, found by the same signature as Simple Tweaks uses.
- Retainer names come from `RetainerManager` and are remembered, so they show before the game
  has loaded them.
- Gathering classes come from the `GatheringPointBase` sheet, zones from `GatheringPoint`,
  `FishingSpot` and `SpearfishingItem`.
- The game only anchors tooltips to its own windows, so the plugin shows the `ItemDetail`
  window directly, as [Chat 2](https://github.com/Infiziert90/ChatTwo) does, and moves it beside
  the list because game windows draw under ImGui.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\KeepInMind.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
