# Glam Off

Try on a glamoured item as it looks without its glamour.

## Usage

Open the item menu on glamoured gear (inventory, Armoury Chest, Character window, retainer,
saddlebag; right-click, or **X** on a controller) and choose **Try On Original**. The Fitting Room
opens with the item's own model and dye. The game's **Try On** still shows the glamour.

The entry only appears on items that have a glamour. Nothing about the item changes.

## How it works

The game's Fitting Room takes both an item and a glamour item. The plugin calls it the
same way the menu's **Try On** does, with the glamour left out. No hooks, signatures or
memory writes.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\GlamOff.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations).
