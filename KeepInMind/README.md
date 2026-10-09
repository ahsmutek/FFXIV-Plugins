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
- With a controller: **A** opens an item's log, **Y** its menu.
- `/keepinmind` shows or hides the list; the cog opens the settings.

Nothing is crafted, gathered or bought for you, and recipes aren't expanded into
sub-ingredients.

## Groups and colours

- The list is grouped into Botanist, Miner, Fisher, NPC vendor, Crafting and your own groups.
  Reorder groups in the settings or from a group header's right-click menu; an item with several
  sources goes in whichever comes first. Turn grouping off, or sort by name or most still needed
  instead of your own order, in the settings. In your own order, drag an item up or down within
  its group (or use Move up / Move down in its menu).
- Items you hold in a retainer or the saddlebag show a moneybag: a green + if that and your bags
  cover what you need, a yellow − if not; this needs
  [Allagan Tools](https://github.com/Critical-Impact/InventoryTools). An item's name takes the
  colour you gave it, else its group's, else its source's. All colours can be changed.
- NPC vendor items need Item Vendor Location to be loaded.
- Completed items can stay in place, move to a collapsed **Completed** section, or be hidden.

## How it works

- The menu entry reads the right-clicked item from `AgentRecipeNote` or `AgentGatheringNote`, as
  Item Vendor Location and GatherBuddy do.
- Item Vendor Location and Allagan Tools only add to the game's own menus and tooltips, so the
  list calls their IPC (`GetItemVendors`, `OpenVendorResults`; `ItemCountOwned`). Checked against
  Item Vendor Location 2.14.0.1.
- Gathering classes come from the `GatheringPointBase` sheet.
- The game only anchors tooltips to its own windows, so the plugin shows the `ItemDetail`
  window directly, as [Chat 2](https://github.com/Infiziert90/ChatTwo) does, and moves it beside
  the list because game windows draw under ImGui.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\KeepInMind.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
