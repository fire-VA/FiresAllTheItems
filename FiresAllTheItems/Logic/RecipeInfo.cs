using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FiresAllTheItems.Logic
{
    // Port of VNEI Logic/RecipeInfo.cs — one "recipe" edge in the item graph. Constructors
    // exist for every vanilla source: crafting Recipe (per quality), Smelter/Fermenter/
    // CookingStation conversions, Incinerator, CharacterDrop, piece build, Pickable, drop
    // tables, SpawnArea, Destructible, TreeBase (recursive log chain), Trader, Growup.
    public class RecipeInfo
    {
        public Dictionary<Amount, List<Part>> Ingredients { get; private set; } = new Dictionary<Amount, List<Part>>();
        public Dictionary<Amount, List<Part>> Results { get; private set; } = new Dictionary<Amount, List<Part>>();
        public List<Part> Stations { get; private set; } = new List<Part>();
        public bool IsOnBlacklist { get; private set; }
        public static List<RecipeInfo> Recipes { get; private set; } = new List<RecipeInfo>();
        public Recipe SourceRecipe { get; private set; }
        public string SourceCreature { get; private set; }
        public bool IsSelfKnown { get; private set; }
        public float Width { get; private set; }

        public IEnumerable<Item> GetStationItems()
        {
            if (Stations.Count >= 1)
                return new[] { Stations[0].item ?? Indexing.NoStation };
            return new[] { Indexing.NoStation };
        }

        public void UpdateKnown() => IsSelfKnown = CalcSelfKnown();

        private static bool AllSelfKnown(Dictionary<Amount, List<Part>> list)
        {
            return list.Where(pair => pair.Key.max != 0 && pair.Value != null)
                .All(pair => pair.Value.All(part => part.item.IsSelfKnown));
        }

        private bool CalcSelfKnown()
        {
            return !Ascend.ShowOnlyKnown
                || !Stations.Any(s => !s.item.IsSelfKnown) && (AllSelfKnown(Ingredients) || AllSelfKnown(Results));
        }

        public void AddIngredient(string name, Amount groupAmount, Amount count, int quality)
        {
            Item item = Indexing.GetItem(name);
            if (item == null)
                return;
            if (!Ingredients.ContainsKey(groupAmount))
                Ingredients.Add(groupAmount, new List<Part>());
            Ingredients[groupAmount].Add(new Part(item, count, quality));
        }

        public void AddIngredient<T>(T target, Amount groupAmount, Amount count, int quality, string context) where T : UnityEngine.Object
        {
            if (target != null)
                AddIngredient(target.name, groupAmount, count, quality);
            else
                Ascend.Log?.LogDebug($"cannot add ingredient to '{context}', item is null (uses amount {count})");
        }

        public void AddResult(string name, Amount groupAmount, Amount count, int quality)
        {
            Item item = Indexing.GetItem(name);
            if (item == null)
                return;
            if (!Results.ContainsKey(groupAmount))
                Results.Add(groupAmount, new List<Part>());
            Results[groupAmount].Add(new Part(item, count, quality));
        }

        public void AddResult<T>(T target, Amount groupAmount, Amount count, int quality, string context) where T : UnityEngine.Object
        {
            if (target != null)
                AddResult(target.name, groupAmount, count, quality);
            else
                Ascend.Log?.LogDebug($"cannot add result to '{context}', item is null (uses amount {count})");
        }

        public void AddStation(Item item, int level)
        {
            if (item == null)
                return;
            Stations.Add(new Part(item, new Amount(1), level));
        }

        public void AddStation(string name, int level) => AddStation(Indexing.GetItem(name), level);

        public void AddStation<T>(T target, int level) where T : UnityEngine.Object
        {
            if (target != null)
                AddStation(target.name, level);
            else
                Ascend.Log?.LogDebug("cannot set station: is null");
        }

        public void CalculateIsOnBlacklist()
        {
            if (Ingredients.SelectMany(i => i.Value).Any(i => i.item.isOnBlacklist))
            {
                IsOnBlacklist = true;
                return;
            }
            if (Results.SelectMany(i => i.Value).Any(i => i.item.isOnBlacklist))
                IsOnBlacklist = true;
        }

        public RecipeInfo() => Recipes.Add(this);

        public RecipeInfo(Recipe recipe, int quality) : this()
        {
            SourceRecipe = recipe;
            if (recipe.GetRequiredStation(quality) != null)
                AddStation(recipe.GetRequiredStation(quality), recipe.GetRequiredStationLevel(quality));
            else
                AddStation(Indexing.HandStation, 1);
            AddResult(recipe.m_item, Amount.One, new Amount(recipe.m_amount), quality, recipe.name);
            if (quality > 1)
                AddIngredient(recipe.m_item, Amount.One, Amount.One, quality - 1, recipe.name);
            foreach (Piece.Requirement resource in recipe.m_resources)
            {
                // 1.0's upgrade tokens (Upgrader0Weapon = "Wooden Battle Idol" and friends) ride on
                // the craft recipe but are only ever consumed at an upgrader station: vanilla's
                // requirement list gates them on station.m_upgrader == m_upgraderResource, so a
                // plain craft never shows them. VNEI 0.17.5 predates the field and listed them as a
                // craft cost — a Hammer read as wood + stone + idol. They still appear on the
                // quality>1 rows, where GetAmount folds m_amount back in.
                if (quality <= 1 && resource.m_upgraderResource)
                    continue;
                var amount = new Amount(resource.GetAmount(quality));
                if (!Amount.IsSameMinMax(amount, Amount.Zero))
                    AddIngredient(resource.m_resItem, Amount.One, amount, 1, recipe.name);
            }
        }

        public RecipeInfo(Smelter.ItemConversion conversion, Smelter smelter) : this()
        {
            AddStation(smelter, 1);
            AddIngredient(conversion.m_from, Amount.One, Amount.One, 1, smelter.name);
            if (smelter.m_fuelItem != null)
                AddIngredient(smelter.m_fuelItem, Amount.One, new Amount(smelter.m_fuelPerProduct), 1, smelter.name);
            AddResult(conversion.m_to, Amount.One, Amount.One, 1, smelter.name);
        }

        public RecipeInfo(Fermenter.ItemConversion conversion, Fermenter fermenter) : this()
        {
            AddStation(fermenter, 1);
            AddIngredient(conversion.m_from, Amount.One, Amount.One, 1, fermenter.name);
            AddResult(conversion.m_to, Amount.One, new Amount(conversion.m_producedItems), 1, fermenter.name);
        }

        public RecipeInfo(CookingStation.ItemConversion conversion, CookingStation cookingStation) : this()
        {
            AddStation(cookingStation, 1);
            AddIngredient(conversion.m_from, Amount.One, Amount.One, 1, cookingStation.name);
            AddResult(conversion.m_to, Amount.One, Amount.One, 1, cookingStation.name);
        }

        public RecipeInfo(Incinerator incinerator) : this()
        {
            AddStation(incinerator, 1);
            AddResult(incinerator.m_defaultResult, Amount.One, Amount.One, 1, incinerator.name);
            AddIngredient(Indexing.AnyItemName, Amount.One, new Amount(incinerator.m_defaultCost), 1);
        }

        public RecipeInfo(Incinerator.IncineratorConversion conversion, Incinerator incinerator) : this()
        {
            AddStation(incinerator, 1);
            AddResult(conversion.m_result, Amount.One, new Amount(conversion.m_resultAmount), 1, incinerator.name);
            foreach (Incinerator.Requirement requirement in conversion.m_requirements)
                AddIngredient(requirement.m_resItem, Amount.One, new Amount(requirement.m_amount), 1, incinerator.name);
        }

        public RecipeInfo(Incinerator.IncineratorConversion conversion, Incinerator.Requirement requirement, Incinerator incinerator) : this()
        {
            AddStation(incinerator, 1);
            AddResult(conversion.m_result, Amount.One, new Amount(conversion.m_resultAmount), 1, incinerator.name);
            AddIngredient(requirement.m_resItem, Amount.One, new Amount(requirement.m_amount), 1, incinerator.name);
        }

        public RecipeInfo(CharacterDrop characterDrop) : this()
        {
            Character character = characterDrop.GetComponent<Character>();
            SourceCreature = character.name;
            AddIngredient(character, Amount.One, Amount.One, 1, character.name);
            foreach (CharacterDrop.Drop drop in characterDrop.m_drops)
            {
                var count = new Amount(drop.m_amountMin, drop.m_amountMax, drop.m_chance);
                AddResult(drop.m_prefab, Amount.One, count, 1, character.name);
            }
        }

        public RecipeInfo(Character character, IEnumerable<FiresCore.Creatures.CreatureDropOverride> drops) : this()
        {
            SourceCreature = character.name;
            AddIngredient(character, Amount.One, Amount.One, 1, character.name);
            foreach (FiresCore.Creatures.CreatureDropOverride drop in drops)
                AddResult(drop.Item, Amount.One, new Amount(drop.AmountMin, drop.AmountMax, drop.Chance), 1);
        }

        public RecipeInfo(GameObject prefab, Piece piece, Item crafter) : this()
        {
            Stations.Add(new Part(crafter, new Amount(1), 1));
            if (piece.m_craftingStation != null)
                AddStation(piece.m_craftingStation, 1);
            foreach (Piece.Requirement resource in piece.m_resources)
                AddIngredient(resource.m_resItem, Amount.One, new Amount(resource.m_amount), 1, prefab.name);
            if (piece.TryGetComponent(out Plant plant) && plant.m_grownPrefabs != null && plant.m_grownPrefabs.Length >= 1)
            {
                foreach (GameObject grownPrefab in plant.m_grownPrefabs)
                    AddResult(grownPrefab, Amount.One, new Amount(1, 1f / plant.m_grownPrefabs.Length), 1, prefab.name);
                Indexing.DisableItem(prefab.name, "Plant has grownPrefabs");
            }
            else
            {
                AddResult(prefab, Amount.One, Amount.One, 1, prefab.name);
            }
        }

        public RecipeInfo(GameObject prefab, Pickable pickable) : this()
        {
            AddIngredient(prefab, Amount.One, Amount.One, 1, prefab.name);
            AddResult(pickable.m_itemPrefab, Amount.One, new Amount(pickable.m_amount), 1, pickable.name);
            if (pickable.m_extraDrops != null && pickable.m_extraDrops.m_drops.Count > 0)
                AddDropTable(prefab, pickable.m_extraDrops);
            CombineGroupAmounts(Results);
        }

        public RecipeInfo(GameObject from, DropTable dropTable) : this()
        {
            AddIngredient(from, Amount.One, Amount.One, 1, from.name);
            AddDropTable(from, dropTable);
            if (dropTable.m_drops != null && dropTable.m_drops.Count == 0)
                Indexing.DisableItem(from.name, "No drops in DropTable");
        }

        public RecipeInfo(SpawnArea spawnArea) : this()
        {
            AddIngredient(spawnArea, Amount.One, Amount.One, 1, spawnArea.name);
            foreach (SpawnArea.SpawnData prefab in spawnArea.m_prefabs)
                AddResult(prefab.m_prefab, Amount.One, new Amount(1, 1f / spawnArea.m_prefabs.Count), 1, spawnArea.name);
        }

        public RecipeInfo(Destructible destructible) : this()
        {
            AddIngredient(destructible, Amount.One, Amount.One, 1, destructible.name);
            AddResult(destructible.m_spawnWhenDestroyed, Amount.One, Amount.One, 1, destructible.name);
        }

        private void AddTreeLog(GameObject logPrefab, int depth, string name, int treeCount)
        {
            if (depth > 50)
            {
                Ascend.Log?.LogWarning("TreeLog is recursively " + name);
                return;
            }
            if (logPrefab == null || !logPrefab.TryGetComponent(out TreeLog treeLog))
                return;
            if (treeLog.m_subLogPoints != null)
                AddTreeLog(treeLog.m_subLogPrefab, depth + 1, name, treeLog.m_subLogPoints.Length);
            AddDropTable(treeLog.gameObject, treeLog.m_dropWhenDestroyed, treeCount);
            Indexing.DisableItem(treeLog.gameObject.name, name);
        }

        public RecipeInfo(TreeBase treeBase) : this()
        {
            AddIngredient(treeBase, Amount.One, Amount.One, 1, treeBase.name);
            AddTreeLog(treeBase.m_logPrefab, 0, treeBase.name, 1);
            AddDropTable(treeBase.gameObject, treeBase.m_dropWhenDestroyed);
            if (treeBase.m_stubPrefab != null && treeBase.m_stubPrefab.TryGetComponent(out DropOnDestroyed stubDrop))
            {
                AddDropTable(treeBase.gameObject, stubDrop.m_dropWhenDestroyed);
                Indexing.DisableItem(treeBase.m_stubPrefab.name, treeBase.gameObject.name);
            }
        }

        public RecipeInfo(Trader trader, Trader.TradeItem tradeItem) : this()
        {
            AddStation(trader, 1);
            if (StoreGui.instance != null && StoreGui.instance.m_coinPrefab != null)
                AddIngredient(StoreGui.instance.m_coinPrefab, Amount.One, new Amount(tradeItem.m_price), 1, trader.name);
            AddResult(tradeItem.m_prefab, Amount.One, new Amount(tradeItem.m_stack), 1, trader.name);
        }

        public RecipeInfo(Growup growup) : this()
        {
            AddIngredient(growup.gameObject, Amount.One, Amount.One, 1, "growup base");
            AddResult(growup.m_grownPrefab, Amount.One, Amount.One, 1, "growup result");
        }

        // Generic constructor — the extension seam other Fires mods (and later the
        // recipe-editing layer) use to feed custom recipes into the graph.
        public RecipeInfo(Amount ingredientAmount, IEnumerable<(string, int)> ingredients,
            Amount resultAmount, IEnumerable<(string, int)> results, string station,
            int ingredientQuality = 1, int resultQuality = 1, int stationLevel = 1) : this()
        {
            foreach ((string, int) ingredient in ingredients)
                AddIngredient(ingredient.Item1, ingredientAmount, new Amount(ingredient.Item2), ingredientQuality);
            foreach ((string, int) result in results)
                AddResult(result.Item1, resultAmount, new Amount(result.Item2), resultQuality);
            AddStation(station, stationLevel);
        }

        private void AddDropTable(GameObject from, DropTable dropTable, int groupMultiplier = 1)
        {
            if (dropTable == null)
                return;
            var groupAmount = new Amount(dropTable.m_dropMin * groupMultiplier, dropTable.m_dropMax * groupMultiplier, dropTable.m_dropChance);
            float totalWeight = dropTable.m_drops.Sum(i => i.m_weight);
            foreach (DropTable.DropData drop in dropTable.m_drops)
            {
                float chance = totalWeight == 0f ? 1f : drop.m_weight / totalWeight;
                AddResult(drop.m_item, groupAmount, new Amount(drop.m_stackMin, drop.m_stackMax, chance), 1, from != null ? from.name : "unknown");
            }
        }

        public bool IngredientsAndResultSame() => Ingredients.SequenceEqual(Results);

        public void CalculateWidth()
        {
            float width = Mathf.Max(1, Stations.Count) * 50f;
            foreach (KeyValuePair<Amount, List<Part>> ingredient in Ingredients)
            {
                if (Ingredients.Count > 1 || ingredient.Key.min != 1 || ingredient.Key.max != 1 || Math.Abs(ingredient.Key.chance - 1f) > 0.01f)
                    width += 50f;
                width += ingredient.Value.Count * 50f;
            }
            foreach (KeyValuePair<Amount, List<Part>> result in Results)
            {
                if (Results.Count > 1 || result.Key.min != 1 || result.Key.max != 1 || Math.Abs(result.Key.chance - 1f) > 0.01f)
                    width += 50f;
                width += result.Value.Count * 50f;
            }
            Width = width;
        }

        private void CombineGroupAmounts(Dictionary<Amount, List<Part>> groups)
        {
            if (groups.Keys.Count <= 1
                || groups.Keys.Any(i => Math.Abs(i.chance - groups.Keys.First().chance) > 0.001f)
                || groups.Values.Count <= 1
                || groups.Values.Any(i => !i.SequenceEqual(groups.Values.First())))
                return;
            var key = new Amount(groups.Keys.Sum(i => i.min), groups.Keys.Sum(i => i.max), groups.Keys.First().chance);
            List<Part> parts = groups.Values.First();
            groups.Clear();
            groups.Add(key, parts);
        }

        public bool IsUpgrade(out Item item)
        {
            List<Part> ingredientUpgradables = Ingredients.Values.SelectMany(i => i).Where(i => i.item.maxQuality > 1).ToList();
            List<Part> resultUpgradables = Results.Values.SelectMany(i => i).Where(i => i.item.maxQuality > 1).ToList();
            if (ingredientUpgradables.Count != 1 || resultUpgradables.Count != 1)
            {
                item = null;
                return false;
            }
            Part a = ingredientUpgradables[0];
            Part b = resultUpgradables[0];
            item = a.item;
            return a.item == b.item && a.quality < b.quality;
        }
    }
}
