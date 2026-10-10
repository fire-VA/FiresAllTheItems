# FiresAllTheItems

**Updated for Valheim 1.0.**

An item browser for Valheim, in the spirit of VNEI, with no Jotunn dependency. Part of the Fires mod family.

## What it does

- A searchable grid of every item, build piece and creature, with category filters and favourites. It shows modded
  entries too and names the mod each one comes from.
- Recipes both ways: what an item is made from, and what it goes into, with crafting stations and drop chances.
- Hover an item's tooltip and press the recipe hotkey to jump to it.
- Admins with devcommands on can spawn items and creatures from the grid, and edit recipes, creature drops, stars and
  stats. Edits are checked on the server and sent to every player.
- Monster presets from FiresAdminPrefabs get their own list and editor.
- `fati_export` writes the whole ObjectDB to `BepInEx\FiresTests\objectdb_export.json`.

## Install

Client-side. Browsing, recipes and search work on any server, vanilla included. Spawning and editing need admin; a
server running FiresUnifiedCore confirms admin reliably, a vanilla one may not.

Requires **FiresUnifiedCore**.

## Credits

FiresAllTheItems began as a port of **[VNEI](https://github.com/MSchmoecker/VNEI)** by Maximilian Schmöcker (MIT).

The category and search glyphs are ten icons from **[Tabler Icons](https://github.com/tabler/tabler-icons)**,
MIT licensed, (c) 2020-2024 Pawel Kuna. They were rasterized from the webfont and are recoloured at
runtime; no upstream file is redistributed. Each one is listed against its original icon name in
**THIRDPARTY-NOTICES.md**, included in this package, together with VNEI's licence.
