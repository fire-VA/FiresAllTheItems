using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FiresAllTheItems.Logic
{
    // Port of VNEI Logic/Indexing.cs — scans ZNetScene/ObjectDB into the Item + RecipeInfo
    // graph. 100% vanilla component walks; the public event seams are the mod API other
    // Fires mods use to add/disable items and feed custom recipes.
    public static class Indexing
    {
        // These vanilla members are genuinely private at runtime — compiling against the
        // publicized DLL passes, but Mono's JIT enforces the LIVE assembly's visibility and
        // throws FieldAccessException (proven in the 0.0.1 boot test). Harmony field-refs
        // bypass the access check with near-field speed.
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<int, GameObject>> NamedPrefabsRef =
            AccessTools.FieldRefAccess<ZNetScene, Dictionary<int, GameObject>>("m_namedPrefabs");
        private static readonly AccessTools.FieldRef<Player, HashSet<string>> KnownMaterialRef =
            AccessTools.FieldRefAccess<Player, HashSet<string>>("m_knownMaterial");
        private static readonly AccessTools.FieldRef<Player, HashSet<string>> KnownRecipesRef =
            AccessTools.FieldRefAccess<Player, HashSet<string>>("m_knownRecipes");
        private static readonly AccessTools.FieldRef<Player, Dictionary<string, int>> KnownStationsRef =
            AccessTools.FieldRefAccess<Player, Dictionary<string, int>>("m_knownStations");
        private static readonly AccessTools.FieldRef<Player, Dictionary<string, string>> KnownTextsRef =
            AccessTools.FieldRefAccess<Player, Dictionary<string, string>>("m_knownTexts");
        private static readonly AccessTools.FieldRef<Player, HashSet<string>> KnownBiomeRef =
            AccessTools.FieldRefAccess<Player, HashSet<string>>("m_knownBiome");

        internal static HashSet<string> KnownMaterial(Player player) => KnownMaterialRef(player);
        internal static Dictionary<string, int> KnownStations(Player player) => KnownStationsRef(player);

        public const string AnyItemName = "fati_any_item";
        public const string UnknownItemName = "fati_unknown_item";
        public const string AllStationsName = "fati_all_stations";
        public const string HandStationName = "fati_hand_station";
        public const string NoStationName = "fati_no_station";

        public static Item AllStations { get; private set; }
        public static Item HandStation { get; private set; }
        public static Item NoStation { get; private set; }

        private static int currentKnownCount;
        private static bool currentShowOnlyKnown;

        public static event Action<GameObject> OnIndexingItems;
        public static event Action<GameObject> OnDisableItems;
        public static event Action AfterDisableItems;
        public static event Action<Recipe> OnIndexingRecipes;
        public static event Action<GameObject> OnIndexingItemRecipes;
        public static event Action AfterIndexingItems;
        public static event Action AfterIndexingRecipes;
        public static event Action IndexFinished;
        public static event Action AfterUpdateKnownItems;

        private static Dictionary<string, Item> Items { get; } = new Dictionary<string, Item>();
        private static Dictionary<string, Item> ItemsByPreLocalizedName { get; } = new Dictionary<string, Item>();
        private static Dictionary<string, Item> ItemsByLocalizedName { get; } = new Dictionary<string, Item>();

        public static void IndexAll()
        {
            if (HasIndexed())
                return;
            if (ZNetScene.instance == null)
            {
                Ascend.Log?.LogWarning("Cannot index: ZNetScene.instance is null");
                return;
            }

            Ascend.Log?.LogInfo("Indexing items and recipes");
            if (!relocalizeSubscribed)
            {
                Localization.OnLanguageChange += RelocalizeItems;
                relocalizeSubscribed = true;
            }
            var pieceTables = new Dictionary<string, PieceTable>();
            List<GameObject> prefabs = GetPrefabs();
            IndexItems(prefabs, pieceTables);
            DisableItems(prefabs);
            ItemValidityFilter.Collect(prefabs);
            IndexRecipes();
            IndexItemRecipes(prefabs, pieceTables);
            ItemValidityFilter.Apply();
            foreach (RecipeInfo recipe in RecipeInfo.Recipes)
            {
                recipe.CalculateIsOnBlacklist();
                recipe.CalculateWidth();
            }
            Ascend.Log?.LogInfo($"Indexed {GetActiveItems().Count()} active items and {RecipeInfo.Recipes.Count} recipes");
            try { IndexFinished?.Invoke(); }
            catch (Exception ex) { Ascend.Log?.LogError(ex.ToString()); }
        }

        private static bool relocalizeSubscribed;

        // One language-change handler for the whole index. Each Item used to add its own, and every += on that static
        // Action copies the whole invocation list: quadratic in the item count, about 100 MB at spawn for 5,000 items.
        private static void RelocalizeItems()
        {
            var seen = new HashSet<Item>();
            foreach (Item item in Items.Values)
                if (seen.Add(item)) item.UpdateLocalizedName();
            foreach (Item item in ItemsByPreLocalizedName.Values)
                if (seen.Add(item)) item.UpdateLocalizedName();
            foreach (Item item in ItemsByLocalizedName.Values)
                if (seen.Add(item)) item.UpdateLocalizedName();
        }

        private static List<GameObject> GetPrefabs()
        {
            List<GameObject> list = new HashSet<GameObject>(ZNetScene.instance.m_prefabs)
                .Union(new HashSet<GameObject>(NamedPrefabsRef(ZNetScene.instance).Values))
                .ToList();
            list.RemoveAll(prefab => prefab == null);
            return list;
        }

        private static void IndexItems(List<GameObject> prefabs, Dictionary<string, PieceTable> pieceTables)
        {
            AddItem(new Item(AnyItemName, "Any item", string.Empty, null, ItemType.Undefined, null));
            AddItem(new Item(UnknownItemName, "Unknown item", string.Empty, null, ItemType.Undefined, null));
            AddItem(new Item(AllStationsName, "All stations", "", null, ItemType.Undefined, null));
            AddItem(new Item(HandStationName, "Hand", "", UI.FatiSprites.Get("hammer_icon"), ItemType.Undefined, null));
            AddItem(new Item(NoStationName, "No station", "", null, ItemType.Undefined, null));
            AllStations = GetItem(AllStationsName);
            HandStation = GetItem(HandStationName);
            NoStation = GetItem(NoStationName);
            DisableItem(AnyItemName, "is not an item");
            DisableItem(UnknownItemName, "is not an item");
            DisableItem(AllStationsName, "is not an item");
            DisableItem(HandStationName, "is not an item");
            DisableItem(NoStationName, "is not an item");

            foreach (GameObject prefab in prefabs)
            {
                string name = prefab.name;
                string fallbackLocalizedName = string.Empty;
                if (prefab.TryGetComponent(out HoverText hoverText))
                    fallbackLocalizedName = hoverText.m_text;
                if (string.IsNullOrEmpty(fallbackLocalizedName) && prefab.TryGetComponent(out CraftingStation craftingStation))
                    fallbackLocalizedName = craftingStation.m_name;

                if (name.StartsWith("TreasureChest"))
                    TryAddItem<Piece>(prefab, i => i.m_name, fallbackLocalizedName, ItemType.Piece, i => i.m_description, i => i.m_icon);

                if (prefab.TryGetComponent(out ItemDrop itemDrop))
                {
                    ItemDrop.ItemData itemData = itemDrop.m_itemData;
                    Sprite icon = null;
                    if (itemData.m_shared.m_icons != null && itemData.m_shared.m_icons.Length != 0)
                    {
                        try { icon = itemData.GetIcon(); }
                        catch { /* malformed variant index — icon renders lazily instead */ }
                    }
                    if (itemData.m_shared.m_damageModifiers == null)
                        itemData.m_shared.m_damageModifiers = new List<HitData.DamageModPair>();
                    ItemType itemType = ItemTypeHelper.GetItemType(itemData);
                    ItemDrop.ItemData.SharedData shared = itemData.m_shared;
                    AddItem(new Item(name, shared.m_name, shared.m_description, icon, itemType, prefab, shared.m_maxQuality)
                    {
                        itemDropType = shared.m_itemType,
                    });
                    if (shared.m_buildPieces != null && !pieceTables.ContainsKey(CleanupName(name)))
                        pieceTables.Add(CleanupName(name), shared.m_buildPieces);
                }

                TryAddItem<Character>(prefab, i => i.m_name, fallbackLocalizedName, ItemType.Creature);
                TryAddItem<MineRock>(prefab, i => i.m_name, fallbackLocalizedName, ItemType.Undefined);
                TryAddItem<MineRock5>(prefab, i => i.m_name, fallbackLocalizedName, ItemType.Undefined);
                TryAddItem<DropOnDestroyed>(prefab, i => string.Empty, fallbackLocalizedName, ItemType.Undefined);
                TryAddItem<Pickable>(prefab, i => string.Empty, fallbackLocalizedName, ItemType.Undefined);
                TryAddItem<SpawnArea>(prefab, i => string.Empty, fallbackLocalizedName, ItemType.Creature);
                TryAddItem<Destructible>(prefab, i => string.Empty, fallbackLocalizedName, ItemType.Undefined);
                TryAddItem<TreeBase>(prefab, i => string.Empty, fallbackLocalizedName, ItemType.Undefined);
                TryAddItem<Trader>(prefab, i => i.m_name, fallbackLocalizedName, ItemType.Undefined);

                try { OnIndexingItems?.Invoke(prefab); }
                catch (Exception ex) { Ascend.Log?.LogError(ex.ToString()); }
            }

            foreach (PieceTable pieceTable in pieceTables.Values)
            {
                if (pieceTable == null)
                    continue;
                foreach (GameObject piece in pieceTable.m_pieces)
                {
                    if (piece == null)
                        continue;
                    TryAddItem<Piece>(piece, i => i.m_name, piece.name, ItemType.Piece, i => i.m_description, i => i.m_icon);
                    try { OnIndexingItems?.Invoke(piece); }
                    catch (Exception ex) { Ascend.Log?.LogError(ex.ToString()); }
                }
            }

            try { AfterIndexingItems?.Invoke(); }
            catch (Exception ex) { Ascend.Log?.LogError(ex.ToString()); }
        }

        private static void DisableItems(List<GameObject> prefabs)
        {
            foreach (GameObject prefab in prefabs)
            {
                if (prefab.TryGetComponent(out Humanoid humanoid))
                {
                    DisableArray(prefab, humanoid.m_defaultItems);
                    DisableArray(prefab, humanoid.m_randomWeapon);
                    DisableArray(prefab, humanoid.m_randomShield);
                    DisableArray(prefab, humanoid.m_randomArmor);
                    if (humanoid.m_randomSets != null)
                    {
                        foreach (Humanoid.ItemSet randomSet in humanoid.m_randomSets)
                            DisableArray(prefab, randomSet?.m_items);
                    }
                    DisableEffectList(prefab, humanoid.m_hitEffects);
                    DisableEffectList(prefab, humanoid.m_critHitEffects);
                    DisableEffectList(prefab, humanoid.m_backstabHitEffects);
                    DisableEffectList(prefab, humanoid.m_deathEffects);
                    DisableEffectList(prefab, humanoid.m_waterEffects);
                    DisableEffectList(prefab, humanoid.m_tarEffects);
                    DisableEffectList(prefab, humanoid.m_slideEffects);
                    DisableEffectList(prefab, humanoid.m_jumpEffects);
                    DisableEffectList(prefab, humanoid.m_pickupEffects);
                    DisableEffectList(prefab, humanoid.m_dropEffects);
                    DisableEffectList(prefab, humanoid.m_consumeItemEffects);
                    DisableEffectList(prefab, humanoid.m_equipEffects);
                    DisableEffectList(prefab, humanoid.m_perfectBlockEffect);
                }
                try { OnDisableItems?.Invoke(prefab); }
                catch (Exception ex) { Ascend.Log?.LogError(ex.ToString()); }
            }
            try { AfterDisableItems?.Invoke(); }
            catch (Exception ex) { Ascend.Log?.LogError(ex.ToString()); }
        }

        private static void DisableArray(GameObject from, GameObject[] array)
        {
            if (array == null)
                return;
            foreach (GameObject go in array.Where(i => i != null))
            {
                ItemDrop itemDrop = go.GetComponent<ItemDrop>();
                bool hasIcons = itemDrop != null && (itemDrop.m_itemData?.m_shared?.m_icons?.Length ?? 0) > 0;
                if (!hasIcons)
                    DisableItem(go.name, "is defaultItem from " + from.name);
            }
        }

        private static void DisableEffectList(GameObject from, EffectList effectList)
        {
            if (effectList?.m_effectPrefabs == null)
                return;
            foreach (EffectList.EffectData effectData in effectList.m_effectPrefabs.Where(i => i != null && i.m_prefab != null))
                DisableItem(effectData.m_prefab.name, "is effect from " + from.name);
        }

        private static void IndexRecipes()
        {
            if (ObjectDB.instance == null)
            {
                Ascend.Log?.LogWarning("Cannot index recipes: ObjectDB.instance is null");
                return;
            }
            foreach (Recipe recipe in ObjectDB.instance.m_recipes)
            {
                if (!recipe.m_enabled || recipe.m_item == null)
                    continue;
                for (int quality = 1; quality <= recipe.m_item.m_itemData.m_shared.m_maxQuality; ++quality)
                    AddRecipeToItems(new RecipeInfo(recipe, quality));
                try { OnIndexingRecipes?.Invoke(recipe); }
                catch (Exception ex) { Ascend.Log?.LogError(ex.ToString()); }
            }
        }

        private static void IndexItemRecipes(List<GameObject> prefabs, Dictionary<string, PieceTable> pieceTables)
        {
            foreach (GameObject prefabIter in prefabs)
            {
                GameObject prefab = prefabIter;
                TryAddRecipeToItemsForEach<Smelter, Smelter.ItemConversion>(prefab, i => i.m_conversion, (s, i) => new RecipeInfo(i, s));
                TryAddRecipeToItemsForEach<Fermenter, Fermenter.ItemConversion>(prefab, i => i.m_conversion, (f, i) => new RecipeInfo(i, f));
                TryAddRecipeToItemsForEach<CookingStation, CookingStation.ItemConversion>(prefab, i => i.m_conversion, (c, i) => new RecipeInfo(i, c));

                if (prefab.TryGetComponent(out Incinerator incinerator))
                {
                    AddRecipeToItems(new RecipeInfo(incinerator));
                    foreach (Incinerator.IncineratorConversion conversion in incinerator.m_conversions)
                    {
                        if (conversion.m_requireOnlyOneIngredient)
                        {
                            foreach (Incinerator.Requirement requirement in conversion.m_requirements)
                                AddRecipeToItems(new RecipeInfo(conversion, requirement, incinerator));
                        }
                        else
                        {
                            AddRecipeToItems(new RecipeInfo(conversion, incinerator));
                        }
                    }
                }

                TryAddRecipeToItems<CharacterDrop>(prefab, BuildCreatureDropRecipe, i => i.GetComponent<Character>() != null);
                TryAddRecipeToItems<Growup>(prefab, i => new RecipeInfo(i));
                TryAddRecipeToItems<MineRock>(prefab, i => new RecipeInfo(prefab, i.m_dropItems));
                TryAddRecipeToItems<MineRock5>(prefab, i => new RecipeInfo(prefab, i.m_dropItems));
                TryAddRecipeToItems<DropOnDestroyed>(prefab, i => new RecipeInfo(prefab, i.m_dropWhenDestroyed));

                if (prefab.TryGetComponent(out Piece _) && prefab.TryGetComponent(out Container container) && container.m_defaultItems.m_drops.Count > 0)
                    AddRecipeToItems(new RecipeInfo(container.gameObject, container.m_defaultItems));

                TryAddRecipeToItems<Pickable>(prefab, i => new RecipeInfo(prefab, i));
                TryAddRecipeToItems<SpawnArea>(prefab, i => new RecipeInfo(i));

                if (prefab.TryGetComponent(out Destructible destructible))
                {
                    if (destructible.m_spawnWhenDestroyed != null && GetItem(destructible.m_spawnWhenDestroyed.name) != null)
                        AddRecipeToItems(new RecipeInfo(destructible));
                    else if (destructible.enabled && prefab.GetComponent<DropOnDestroyed>() == null && prefab.GetComponent<Plant>() == null)
                        DisableItem(prefab.name, "destructible.m_spawnWhenDestroyed is null or not indexed");
                }

                TryAddRecipeToItems<TreeBase>(prefab, i => new RecipeInfo(i));
                TryAddRecipeToItemsForEach<Trader, Trader.TradeItem>(prefab, i => i.m_items, (t, i) => new RecipeInfo(t, i));

                try { OnIndexingItemRecipes?.Invoke(prefab); }
                catch (Exception ex) { Ascend.Log?.LogError(ex.ToString()); }
            }

            foreach (KeyValuePair<string, PieceTable> pieceTable in pieceTables)
            {
                if (pieceTable.Value == null)
                    continue;
                foreach (GameObject piece in pieceTable.Value.m_pieces)
                {
                    if (piece == null)
                        continue;
                    if (piece.TryGetComponent(out Piece pieceComponent) && !pieceComponent.m_repairPiece)
                        AddRecipeToItems(new RecipeInfo(piece, pieceComponent, GetItem(pieceTable.Key)));
                }
            }

            try { AfterIndexingRecipes?.Invoke(); }
            catch (Exception ex) { Ascend.Log?.LogError(ex.ToString()); }
        }

        private static void TryAddItem<T>(GameObject target, Func<T, string> getName, string fallbackLocalizedName,
            ItemType itemType, Func<T, string> getDescription = null, Func<T, Sprite> getIcon = null) where T : Component
        {
            if (!target.TryGetComponent(out T component))
                return;
            try
            {
                string description = (getDescription != null ? getDescription(component) : null) ?? string.Empty;
                Sprite icon = getIcon != null ? getIcon(component) : null;
                string preLocalizeName = getName(component);
                if (string.IsNullOrEmpty(preLocalizeName))
                    preLocalizeName = fallbackLocalizedName;
                AddItem(new Item(target.name, preLocalizeName, description, icon, itemType, target));
            }
            catch (Exception ex)
            {
                Ascend.Log?.LogError(target.name + Environment.NewLine + ex);
            }
        }

        private static void TryAddRecipeToItems<T>(GameObject target, Func<T, RecipeInfo> getRecipe, Func<T, bool> check = null) where T : Component
        {
            if (!target.TryGetComponent(out T component))
                return;
            if (check != null && !check(component))
                return;
            RecipeInfo recipeInfo;
            try
            {
                recipeInfo = getRecipe(component);
            }
            catch (Exception ex)
            {
                Ascend.Log?.LogError(target.name + Environment.NewLine + ex);
                return;
            }
            AddRecipeToItems(recipeInfo);
        }

        private static void TryAddRecipeToItemsForEach<T1, T2>(GameObject target, Func<T1, List<T2>> getArray, Func<T1, T2, RecipeInfo> getRecipe) where T1 : Component
        {
            if (!target.TryGetComponent(out T1 component))
                return;
            foreach (T2 entry in getArray(component))
            {
                try { AddRecipeToItems(getRecipe(component, entry)); }
                catch (Exception ex) { Ascend.Log?.LogError(target.name + Environment.NewLine + ex); }
            }
        }

        public static bool HasIndexed() => Items.Count > 0;

        /// <summary>A creature's loot edge: its live Core creature rules when their drops section is on, else its prefab loot.</summary>
        private static RecipeInfo BuildCreatureDropRecipe(CharacterDrop characterDrop)
        {
            Character character = characterDrop.GetComponent<Character>();
            IReadOnlyList<FiresCore.Creatures.CreatureDropOverride> ruled = FiresCore.Creatures.CreatureOverrides.ActiveDropsFor(character.name);
            return ruled != null ? new RecipeInfo(character, ruled) : new RecipeInfo(characterDrop);
        }

        /// <summary>Rebuilds only the creature-loot edges after Core creature rules change.</summary>
        public static void ReindexCreatureDrops()
        {
            if (!HasIndexed() || ZNetScene.instance == null)
                return;
            var stale = new HashSet<RecipeInfo>(RecipeInfo.Recipes.Where(recipe => recipe.SourceCreature != null));
            foreach (Item item in Items.Values)
            {
                item.result.ExceptWith(stale);
                item.ingredient.ExceptWith(stale);
            }
            RecipeInfo.Recipes.RemoveAll(stale.Contains);
            int firstNew = RecipeInfo.Recipes.Count;
            foreach (GameObject prefab in GetPrefabs())
                TryAddRecipeToItems<CharacterDrop>(prefab, BuildCreatureDropRecipe, i => i.GetComponent<Character>() != null);
            foreach (RecipeInfo recipe in RecipeInfo.Recipes.Skip(firstNew))
            {
                recipe.CalculateIsOnBlacklist();
                recipe.CalculateWidth();
            }
            currentKnownCount = -1;
            UpdateKnown();
        }

        /// <summary>
        /// Rebuilds only the crafting-recipe edges after ObjectDB recipes change (Core recipe overrides), leaving the
        /// item set and every other recipe source (drops, conversions, pieces) untouched.
        /// </summary>
        public static void ReindexCraftingRecipes()
        {
            if (!HasIndexed())
                return;
            var stale = new HashSet<RecipeInfo>(RecipeInfo.Recipes.Where(recipe => recipe.SourceRecipe != null));
            foreach (Item item in Items.Values)
            {
                item.result.ExceptWith(stale);
                item.ingredient.ExceptWith(stale);
            }
            RecipeInfo.Recipes.RemoveAll(stale.Contains);
            int firstNew = RecipeInfo.Recipes.Count;
            IndexRecipes();
            foreach (RecipeInfo recipe in RecipeInfo.Recipes.Skip(firstNew))
            {
                recipe.CalculateIsOnBlacklist();
                recipe.CalculateWidth();
            }
            currentKnownCount = -1;
            UpdateKnown();
            Ascend.Log?.LogInfo($"Re-indexed crafting recipes after an override change ({RecipeInfo.Recipes.Count - firstNew} edges)");
        }

        public static void DisableItem(string name, string context)
        {
            Item item = GetItem(name);
            if (item == null)
                return;
            item.isActive = false;
        }

        public static void AddItem(Item item)
        {
            string key = CleanupName(item.internalName);
            if (!Items.ContainsKey(key))
                Items[key] = item;
            if (!string.IsNullOrEmpty(item.preLocalizeName)
                && (!ItemsByPreLocalizedName.TryGetValue(item.preLocalizeName, out Item byPre) || IsSaplingItem(byPre)))
                ItemsByPreLocalizedName[item.preLocalizeName] = item;
            if (!string.IsNullOrEmpty(item.localizedName)
                && (!ItemsByLocalizedName.TryGetValue(item.localizedName, out Item byLoc) || IsSaplingItem(byLoc)))
                ItemsByLocalizedName[item.localizedName] = item;
        }

        private static bool IsSaplingItem(Item item)
        {
            return item.internalName.StartsWith("sapling_") || (item.prefab != null && item.prefab.GetComponent<Plant>() != null);
        }

        public static void ItemObtainedInRecipe(string name, RecipeInfo recipeInfo)
        {
            Item item = GetItem(name);
            if (item != null)
                item.result.Add(recipeInfo);
        }

        public static void ItemUsedInRecipe(string name, RecipeInfo recipeInfo)
        {
            Item item = GetItem(name);
            if (item != null)
                item.ingredient.Add(recipeInfo);
        }

        public static void AddRecipeToItems(RecipeInfo recipeInfo)
        {
            foreach (Part part in recipeInfo.Ingredients.SelectMany(i => i.Value))
                ItemUsedInRecipe(part.item.internalName, recipeInfo);
            foreach (Part part in recipeInfo.Results.SelectMany(i => i.Value))
                ItemObtainedInRecipe(part.item.internalName, recipeInfo);
            foreach (Part station in recipeInfo.Stations)
                ItemUsedInRecipe(station.item.internalName, recipeInfo);
        }

        public static string CleanupName(string name) => name.Replace("JVLmock_", "").ToLower();

        public static IEnumerable<KeyValuePair<string, Item>> GetActiveItems() => Items.Where(i => i.Value.isActive);

        public static Item GetItem(string name)
        {
            if (Items.TryGetValue(CleanupName(name), out Item item))
                return item;
            if (ItemsByPreLocalizedName.TryGetValue(name, out item))
                return item;
            if (ItemsByLocalizedName.TryGetValue(name, out item))
                return item;
            return null;
        }

        public static void UpdateKnown()
        {
            if (Player.m_localPlayer == null)
                return;
            Player player = Player.m_localPlayer;
            int knownCount = KnownMaterialRef(player).Count
                + KnownRecipesRef(player).Count
                + KnownStationsRef(player).Count
                + KnownTextsRef(player).Count
                + KnownBiomeRef(player).Count;
            if (currentKnownCount == knownCount && currentShowOnlyKnown == Ascend.ShowOnlyKnown)
                return;
            currentKnownCount = knownCount;
            currentShowOnlyKnown = Ascend.ShowOnlyKnown;
            foreach (Item item in Items.Values)
                item.UpdateSelfKnown();
            foreach (RecipeInfo recipe in RecipeInfo.Recipes)
                recipe.UpdateKnown();
            foreach (Item item in Items.Values)
                item.UpdateKnown();
            AfterUpdateKnownItems?.Invoke();
        }
    }
}
