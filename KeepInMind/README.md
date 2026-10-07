# Keep in Mind

A checklist of ingredients to gather or buy by hand while you craft.

## Usage

- In the Crafting Log, right-click an ingredient and choose **Keep in mind**. It goes on one
  list shared by every recipe and job.
- Each item needs the amount its recipe takes; change it with − and +. Adding an item that's
  already listed adds to its amount and un-ticks it.
- Items tick themselves once your bags hold enough (NQ and HQ together). Un-ticking by hand
  sticks until the count drops and climbs back.
- Click an item to open it in the Gathering Log, Fish Guide or Crafting Log. Hover it for the
  game's own tooltip, including what other plugins add.
- Right-click an item for **Vendor location**, via
  [Item Vendor Location](https://github.com/electr0sheep/ItemVendorLocation) (greyed out
  while it isn't loaded).
- **Clear completed** removes ticked items; **Clear all** empties the list.
- With a controller: **A** opens an item's log, **Y** its menu.
- `/keepinmind` shows or hides the list.

Nothing is crafted, gathered or bought for you, and recipes aren't expanded into
sub-ingredients.

## How it works

- The menu entry reads the right-clicked item from `AgentRecipeNote`, as Item Vendor
  Location does.
- Item Vendor Location only adds its entry to the game's own menus, so the list calls its
  IPC (`GetItemVendors`, `OpenVendorResults`). Checked against version 2.14.0.1.
- The game only anchors tooltips to its own windows, so the plugin shows the `ItemDetail`
  window directly, as [Chat 2](https://github.com/Infiziert90/ChatTwo) does.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\KeepInMind.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
