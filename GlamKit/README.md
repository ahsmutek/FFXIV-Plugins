# GlamKit

Small glamour helpers. Each can be turned off in settings (`/xlsettings` → GlamKit's cog, or the cog in
`/glamkit`).

## Glam Off

Try on a glamoured item as it looks without its glamour.

Open the item menu on glamoured gear (inventory, Armoury Chest, Character window, retainer,
saddlebag, Grand Company Expert Delivery, armoire, Glamour Creation, Fitting Room; right-click, or **X** on a
controller) and choose **Glam Off**. The Fitting Room opens with the item's own model and dye. The
game's **Try On** still shows the glamour.

The entry only appears on items that have a glamour. Nothing about the item changes.

## Glam Keeper

For gear you wear for its stats but want in the glamour dresser once you replace it.

Open the item menu on gear and choose **Keep for Glam** (or **Stop Keeping for Glam**). Kept gear:

- is bordered in your bags, saddlebag, retainer and Armoury Chest windows, and has a hanger after its name
  in Expert Delivery, the armoire and Glamour Creation, and beside its icon in the venture report (colour in
  settings);
- says "Kept for glam" in its tooltip;
- stops being kept once it's in the glamour dresser or armoire, with a line in chat. The game only knows
  what's in them once you've opened them this session.

`/glamkit` lists everything kept and where it is now (Equipped, Armoury Chest, Inventory, and with
Allagan Tools, Saddlebag and Retainer). Right-click an item there (or X on a controller) for the game's item menu,
beside the window, with **Try On**, **Link**, **Open in Crafting Log** for crafted gear, **Vendor location** with Item
Vendor Location, **Copy Item Name**, and the keep and wishlist entries. Link prints the item's link in your chat log,
only for you; the game's menu on that link puts it in the chat box.

Gear is kept by item, so every copy of the same item counts as kept.

## Keep for Later

For gear worth keeping for its stats or item level rather than its looks, e.g. an off-job set.

Open the item menu on gear and choose **Keep for Later** (or **Stop Keeping for Later**). It works alongside
Keep for Glam, and a piece can be both. Gear kept for later:

- is bordered in teal in your bags, saddlebag, retainer and Armoury Chest windows (inside the glam border if
  it's both), and has a bookmark after its name or icon wherever Glam Keeper puts a hanger (colour in settings);
- says "Kept for later" in its tooltip;
- has its own tab in `/glamkit`, and stays kept until you stop keeping it, glamour dresser or not.

## Wishlist

For items you want but don't have yet.

Open the item menu in a shop, on the Market Board, in the Fitting Room or on an item link in chat and choose
**Add to Wishlist**, or search by name in `/glamkit`'s Wishlist tab. Wished items:

- have a heart after their name in shop and Market Board lists, and in the glam lists above (colour in settings);
- say "On your wishlist" in their tooltip;
- come off the list once they're in your inventory, Armoury Chest, glamour dresser or armoire, with a line in chat.

## Job Levels

A gear tooltip's job line (e.g. "PLD WAR DRK GNB") shows your level on each job instead:
"PLD 100  WAR 87  DRK 52  GNB –". For a level 50 item, 50–60 is green, 45–49 yellow and anything else grey
(both ranges are in settings); – means you haven't started that job.

A ▲ after a level, e.g. "PLD 100▲", means the item would be an upgrade for that job, judged the way Recommended Gear
appears to: more of the job's main stat (HQ bonus included) than the job's gear sets have in that slot, or as much
and a higher item level. Crafters and gatherers have no main stat, so they go by item level. Your current job is
also compared with what you're wearing. The job has to be high enough to wear the item, rings are compared with the
weaker ring, pieces a gear set is missing count as empty, and other jobs without a gear set aren't marked.
Settings can turn the arrow off for some kinds of gear, e.g. earrings.

Gear that more than 10 jobs can wear keeps the game's text and adds up to 5 jobs, the ones it's an upgrade for
and then the green ones, lowest first, e.g. "· WAR 52  BLM 55", or the job nearest its level if there are none.
Level 1 gear like that is left as it is.

The game colours the equip level against your current job even when that job can't wear the item; GlamKit
colours it only when your job can wear it (optional). It all works on any job and in every item tooltip, Market Board included.

## Gear Sets

A gear tooltip says which jobs' gear sets use the item, e.g. "Equipped - NIN, MNK".
Sets are matched by item, so any copy of it counts.

## Compare as Another Job

The game's **Item Comparison** compares an item with what your current job is wearing, even when that job can't wear
it. GlamKit compares it with a job that can instead: one the item is an upgrade for (Job Levels' ▲), or else your
highest-level one.
The window's slot name says which job, e.g. "Body · WAR".

The arrows on the window's top edge, e.g. "◀ WAR 90 ▶", flip through every job of yours that can wear the item and has a
gear set, your current one included; on a controller, L1 and R1 do the same while they're shown.

The item is compared with the piece from that job's best gear set, as long as it's in your bags, Armoury Chest or equipped.
If it's somewhere else, e.g. with a retainer, there's nothing to compare it with.

## Upgrades

`/glamkit`'s Upgrades tab lists gear in your glamour dresser and armoire that's an upgrade for one of your jobs, by Job
Levels' ▲ and its settings, so you can take it out and wear it. A gear set's piece left there counts too, as the set is
missing it. Group the list by job, slot, dresser or armoire, or not at all; your current job comes first. It comes from
the last time you opened each, kept for each character.

If a piece isn't really an upgrade for a job, choose **Not an Upgrade for WAR** (or whichever job) in its item menu to
take it off the list for that job. **Show hidden** brings those back, and **Show as Upgrade for WAR** undoes it.

## How it works

Glam Off calls the Fitting Room the same way the menu's **Try On** does, with the glamour left out.

Glam Keeper only reads your inventory and changes your own UI: menu entries, ImGui borders and tooltip
text. It never presses anything or sends anything to the server. The tooltip
line uses one hook, the same as Keep in Mind's; if a patch breaks it, only the tooltip line goes. Keep for
Later and the wishlist work the same way; the wishlist's hearts are drawn after matching names in shop and
Market Board lists.

Job Levels reads your levels, gear sets and equipped gear from the client and rewrites the tooltip's job line
through that same hook. The equip level is recoloured just before the tooltip draws.

Compare as Another Job hooks the game's comparison and, after the game reads your equipped piece, swaps in the gear set's
copy from your Armoury Chest or bags. A comparison the game opens some other way is redone the same way once it opens,
keeping the item's own materia and dye. If a patch breaks the hook, Item Comparison works as it always has. The game
colours equip levels for your current job, so they're recoloured for the job shown, in the colours GlamKit has seen
the game use: after your first comparison of gear your current job can wear, they're right from then on.

Upgrades reads the dresser's and armoire's contents from the client once you've opened them and saves the item lists
in GlamKit's settings file on your PC.

`/glamkit`'s item menu is the game's context menu, opened empty and filled through Dalamud the way plugins add to the
game's menus. If it doesn't open, an ImGui menu with the same entries shows instead.

`/glamkit debug` writes what each inventory window shows to the Dalamud log, for when a border is
missing or misplaced.

## Build

```sh
dotnet build -c Debug
```

Then load `bin\Debug\GlamKit.dll` as a Dalamud dev plugin
(`/xlsettings` → Experimental → Dev Plugin Locations). If you had Glam Off loaded, remove it: GlamKit
replaces it.
