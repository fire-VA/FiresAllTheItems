using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FiresAllTheItems.Logic;
using FiresCore.UI;

namespace FiresAllTheItems.UI
{
    // Port of VNEI UI/TypeToggle.cs + ItemTypeToggle.cs — one category filter button above
    // the grid. LEFT-click toggles the category; RIGHT-click SOLOs it (hides every other
    // category; right-click again restores all) — exact VNEI OnPointerDown logic. Tooltip
    // shows the category's item count; empty categories gray out. Undefined (the world-object
    // junk bucket: rocks, effects, spawners) starts OFF so the grid opens clean.
    public class FatiTypeToggle : MonoBehaviour, IPointerDownHandler
    {
        public ItemType itemType;

        private bool isOn = true;
        private bool isEnabled = true;
        private Image iconImage;
        private TextMeshProUGUI fallbackLabel;
        private Image bg;
        private UITooltip tooltip;
        private FatiBaseUI baseUI;

        public bool IsOn
        {
            get => isOn;
            set
            {
                isOn = value;
                UpdateVisuals();
            }
        }

        public bool IsEnabled
        {
            get => isEnabled;
            private set
            {
                isEnabled = value;
                UpdateVisuals();
            }
        }

        public static FatiTypeToggle Create(Transform parent, FatiBaseUI baseUI, ItemType type, float x)
        {
            var go = new GameObject($"Toggle_{type}");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(30f, 30f);
            rect.anchoredPosition = new Vector2(x, -18f);

            var toggle = go.AddComponent<FatiTypeToggle>();
            toggle.itemType = type;
            toggle.baseUI = baseUI;
            toggle.isOn = type != ItemType.Undefined;

            toggle.bg = go.AddComponent<Image>();
            toggle.bg.raycastTarget = true;

            Sprite sprite = FatiGlyphs.Get(GetGlyphName(type));
            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(go.transform, false);
            var iconRect = iconGO.AddComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(3f, 3f);
            iconRect.offsetMax = new Vector2(-3f, -3f);
            toggle.iconImage = iconGO.AddComponent<Image>();
            toggle.iconImage.raycastTarget = false;
            toggle.iconImage.preserveAspect = true;
            if (sprite != null)
            {
                toggle.iconImage.sprite = sprite;
            }
            else
            {
                toggle.iconImage.enabled = false;
                toggle.fallbackLabel = UIBuilderHelper.CreateLabel(go.transform, "Letter", type.ToString().Substring(0, 1),
                    14f, Color.white, Vector2.zero, Vector2.one, TextAlignmentOptions.Center);
                toggle.fallbackLabel.raycastTarget = false;
            }

            toggle.tooltip = go.AddComponent<UITooltip>();
            toggle.tooltip.m_tooltipPrefab = FatiTab.GetTooltipPrefab();
            toggle.tooltip.Set(type.ToString(), "");

            toggle.UpdateVisuals();
            return toggle;
        }

        private static string GetGlyphName(ItemType type)
        {
            switch (type)
            {
                case ItemType.Creature: return FatiGlyphs.Creature;
                case ItemType.Piece: return FatiGlyphs.Piece;
                case ItemType.Item: return FatiGlyphs.Item;
                case ItemType.Food: return FatiGlyphs.Food;
                case ItemType.Armor: return FatiGlyphs.Armor;
                case ItemType.Weapon: return FatiGlyphs.Weapon;
                default: return FatiGlyphs.Undefined;
            }
        }

        // Post-index: count the category, gray out empty ones, show the count in the tooltip.
        public void RefreshCount()
        {
            int count = Indexing.GetActiveItems().Count(i => i.Value.itemType == itemType);
            tooltip.Set(itemType.ToString(), $"{count} items");
            IsEnabled = count > 0;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                // VNEI solo logic: right-click shows ONLY this category; right-clicking an
                // already-solo'd toggle restores all.
                bool restoreAll = IsOn && baseUI.TypeToggles.All(t => !t.IsOn || t == this);
                IsOn = true;
                foreach (FatiTypeToggle other in baseUI.TypeToggles)
                {
                    if (other != this)
                        other.IsOn = restoreAll;
                }
            }
            else if (eventData.button == PointerEventData.InputButton.Left)
            {
                IsOn = !IsOn;
            }
            else
            {
                return;
            }
            baseUI.OnTypeToggleChanged();
        }

        private void UpdateVisuals()
        {
            // VNEI's on/off/disabled semantics, repainted for line glyphs: a white stroke needs a
            // dark backing to read on parchment, and VNEI's light-gray tile washed it out.
            Color iconTint = !IsEnabled ? new Color(1f, 1f, 1f, 0.15f)
                : IsOn ? Color.white : new Color(1f, 1f, 1f, 0.28f);
            if (iconImage != null)
                iconImage.color = iconTint;
            if (fallbackLabel != null)
                fallbackLabel.color = iconTint;
            if (bg != null)
                bg.color = !IsEnabled ? new Color(0f, 0f, 0f, 0.15f)
                    : IsOn ? new Color(0f, 0f, 0f, 0.55f) : new Color(0f, 0f, 0f, 0.25f);
        }

        public bool IsDisabled(ItemType type) => !IsOn && type == itemType;
    }
}
