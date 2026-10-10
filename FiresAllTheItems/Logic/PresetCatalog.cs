using System.Collections.Generic;
using UnityEngine;
using FiresCore.Bridge;

namespace FiresAllTheItems.Logic
{
    /// <summary>
    /// The Presets side of the grid's Creatures/Presets switch: one synthetic <see cref="Item"/> per monster
    /// preset, wearing the icon of the creature it targets. Presets live in FiresAdminPrefabs and are read
    /// through Core's <see cref="MonsterPresetBridge"/>, so with that mod absent the list is simply empty and
    /// the toggle stays disabled.
    ///
    /// These items are deliberately NOT in the main index: a preset is not craftable, droppable or an
    /// ingredient, and mixing it into the item list would put it in every search and recipe lookup.
    /// </summary>
    public static class PresetCatalog
    {
        private static readonly List<Item> Entries = new List<Item>();

        public static bool Available => MonsterPresetBridge.IsAvailable;

        public static IReadOnlyList<Item> All => Entries;

        public static int Count => Entries.Count;

        public static void Rebuild()
        {
            Entries.Clear();
            if (!MonsterPresetBridge.IsAvailable)
                return;

            foreach (string name in MonsterPresetBridge.ListNames())
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                MonsterPresetRequest preset = MonsterPresetBridge.Get(name);
                Item species = preset != null && !string.IsNullOrEmpty(preset.Creature)
                    ? Indexing.GetItem(preset.Creature)
                    : null;
                var entry = new Item(name, name, Describe(preset, species), species?.GetIcon(),
                    ItemType.Creature, species?.prefab)
                {
                    presetName = name,
                };
                Entries.Add(entry);
            }
            Ascend.Log?.LogDebug($"Preset catalog rebuilt: {Entries.Count} preset(s)");
        }

        public static void Clear() => Entries.Clear();

        private static string Describe(MonsterPresetRequest preset, Item species)
        {
            if (preset == null)
                return "This preset could not be read back.";
            string target = species != null ? species.GetPrimaryName()
                : string.IsNullOrEmpty(preset.Creature) ? "every creature" : preset.Creature;
            string respawn = preset.RespawnSeconds > 0f
                ? $"Respawns where it died after {Mathf.RoundToInt(preset.RespawnSeconds)}s."
                : "No respawn rule.";
            int rows = preset.Items != null ? preset.Items.Count : 0;
            return $"Monster preset targeting {target}. {respawn} {rows} drop row(s).\n\n"
                 + "A preset is literal: its stars, stats and drops are exactly what was set here, with no "
                 + "star or world-level scaling, and per-species creature rules do not apply to it.";
        }
    }
}
