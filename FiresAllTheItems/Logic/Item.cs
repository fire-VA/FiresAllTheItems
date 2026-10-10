using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FiresAllTheItems.Logic
{
    // Port of VNEI Logic/Item.cs. Differences from VNEI:
    //  - icon render is LAZY (first GetIcon() call) through FiresCore.UI.ItemIconRenderer
    //    instead of Jotunn RenderManager at index time — no index-time hitch.
    //  - mod attribution via our ModNames registry (null → "Valheim").
    public class Item
    {
        public readonly string internalName;
        public readonly string preLocalizeName;
        public string localizedName;
        public readonly string description;
        public string localizedDescription;
        public readonly GameObject prefab;
        public readonly bool isOnBlacklist;
        public readonly ItemType itemType;
        public ItemDrop.ItemData.ItemType itemDropType;
        public readonly BepInEx.BepInPlugin mod;
        public bool isActive = true;

        /// <summary>Set only on the synthetic entries <see cref="PresetCatalog"/> builds; null for a real item.</summary>
        public string presetName;
        public int maxQuality;
        public bool isFavorite;
        private bool isKnown;
        private readonly Dictionary<int, string> tooltipsCache = new Dictionary<int, string>();
        public readonly HashSet<RecipeInfo> result = new HashSet<RecipeInfo>();
        public readonly HashSet<RecipeInfo> ingredient = new HashSet<RecipeInfo>();
        public readonly FatiComponentEvent onFavoriteChanged = new FatiComponentEvent();
        public readonly FatiComponentEvent onKnownChanged = new FatiComponentEvent();
        private Sprite icon;
        private bool iconRenderTried;

        public bool IsKnown => isKnown || !Ascend.ShowOnlyKnown;

        public bool IsSelfKnown { get; private set; }

        public static event Action OnAnyFavoriteChanged;

        public Item(string name, string preLocalizeName, string description, Sprite icon,
            ItemType itemType, GameObject prefab, int maxQuality = 1)
        {
            internalName = name;
            this.preLocalizeName = preLocalizeName ?? "";
            this.description = description ?? "";
            this.prefab = prefab;
            this.itemType = itemType;
            this.maxQuality = maxQuality;
            isOnBlacklist = Ascend.IsItemBlacklisted(this);
            UpdateLocalizedName();
            if (prefab != null)
                mod = ModNames.GetModByPrefabName(prefab.name);
            if (icon != null)
                SetIcon(icon);
        }

        public string GetNameContext() => internalName + Environment.NewLine + GetModName();

        public string GetDescription() => description;

        public string GetTooltip(int quality)
        {
            if (tooltipsCache.TryGetValue(quality, out string cached))
                return cached;
            string tooltip = GenerateTooltip(quality);
            tooltipsCache.Add(quality, tooltip);
            return tooltip;
        }

        internal void UpdateLocalizedName()
        {
            localizedName = Localization.instance.Localize(preLocalizeName);
            localizedDescription = Localization.instance.Localize(description);
        }

        private string GenerateTooltip(int quality)
        {
            if (prefab != null && prefab.TryGetComponent(out ItemDrop itemDrop))
            {
                // Heal null m_dropPrefab before GetTooltip — the classic SetupVisEquipment/
                // tooltip NRE trap (see reference_itemdrop_dropprefab_vis).
                if (itemDrop.m_itemData.m_dropPrefab == null)
                    itemDrop.m_itemData.m_dropPrefab = prefab;
                try
                {
                    return ItemDrop.ItemData.GetTooltip(itemDrop.m_itemData, quality, true, Game.m_worldLevel);
                }
                catch (Exception ex)
                {
                    Ascend.Log?.LogWarning($"Failed to generate tooltip for '{internalName}': {ex.Message}");
                    return string.Empty;
                }
            }
            return description;
        }

        public void SetIcon(Sprite sprite)
        {
            if (icon == null)
                icon = sprite;
        }

        public Sprite GetIcon()
        {
            if (icon != null)
                return icon;
            // Lazy render: only prefabs that actually reach a visible grid cell pay the cost.
            if (!iconRenderTried && prefab != null)
            {
                iconRenderTried = true;
                try { icon = FiresCore.UI.ItemIconRenderer.Render(prefab, 128); }
                catch (Exception ex) { Ascend.Log?.LogDebug($"icon render failed for {internalName}: {ex.Message}"); }
            }
            return icon != null ? icon : UI.FatiSprites.NoIcon;
        }

        public string GetPrimaryName() => localizedName != null && localizedName.Length > 0 ? localizedName : internalName;

        public string GetModName() => mod != null ? mod.Name : "Valheim";

        public void UpdateFavorite(bool favorite)
        {
            isFavorite = favorite;
            onFavoriteChanged.Invoke();
            OnAnyFavoriteChanged?.Invoke();
        }

        public void UpdateSelfKnown()
        {
            isKnown = false;
            if (!Ascend.ShowOnlyKnown)
                IsSelfKnown = true;
            else
                IsSelfKnown = Indexing.KnownMaterial(Player.m_localPlayer).Contains(preLocalizeName)
                    || Indexing.KnownStations(Player.m_localPlayer).ContainsKey(preLocalizeName);
        }

        public void UpdateKnown()
        {
            if (IsSelfKnown)
            {
                isKnown = true;
                onKnownChanged.Invoke();
            }
            else if (result.Any(recipe => recipe.IsSelfKnown) || ingredient.Any(recipe => recipe.IsSelfKnown))
            {
                isKnown = true;
                onKnownChanged.Invoke();
            }
            else
            {
                isKnown = false;
                onKnownChanged.Invoke();
            }
        }

        public IEnumerable<Item> GetStations()
        {
            var stations = new List<Item>();
            stations.AddRange(ingredient.SelectMany(r => r.GetStationItems()));
            stations.AddRange(result.SelectMany(r => r.GetStationItems()));
            return stations.Distinct().ToList();
        }
    }
}
