* v0.0.24 - Valheim 1.0 release (covers 0.0.8 to 0.0.24)
  - Creature editor on the recipe view: a master switch hands a creature back to vanilla or its own mod without
    losing the values; Drops, Stars and Stats each apply only when their own Override is on. Loot is picked straight
    from the item grid. Saves go through FiresUnifiedCore, which re-checks admin on the server and sends them to
    every player.
  - Recipe and preset editors, built from the same editor widgets.
  - Creatures/Presets switch: monster presets from FiresAdminPrefabs are listed with the icon of the creature they
    target, and have their own glyph. Without FiresAdminPrefabs the list is empty and the switch is off.
  - Items that can't really exist in a world (dev and test prefabs, broken entries) are filtered out of the grid.
  - The grid rebuilds itself when the Crafting panel changes size (a layout switch or an inventory mod re-applying
    its layout mid-session), instead of keeping the old columns and rows.
  - New console command `fati_export`: writes the whole ObjectDB (items, recipes, build pieces, conversions,
    creatures and drops, gatherables, boss altars, the plugin list) to `BepInEx\FiresTests\objectdb_export.json`.
    Read-only.
  - Credits: the VNEI MIT notice is now in THIRDPARTY-NOTICES.md.

* v0.0.7 - updated for Valheim 1.0

* v0.0.6 - maintenance and fixes

