using System.Collections;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresAllTheItems.UI
{
    // Port of VNEI Patches/InventoryGuiPatch.cs + UI/MainVneiHandler.cs (attached mode only,
    // no Auga): clones the vanilla Upgrade tab button into a FATI tab; active = hide the
    // vanilla RecipeList/Decription panes and show our grid in their place.
    [HarmonyPatch]
    public static class FatiTab
    {
        private static Button _tab;
        private static FatiBaseUI _baseUI;
        private static GameObject _tooltipPrefab;
        private static bool _uiDirty;
        private static bool _tearingDown;

        public static bool TabActive { get; private set; }

        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        [HarmonyPostfix]
        [HarmonyPriority(200)]
        public static void AwakePatch(InventoryGui __instance)
        {
            FatiSprites.Reset();
            FatiDisplayItem.ResetSlotSpriteLog();
            _tooltipPrefab = null;
            _tearingDown = false;
            TabActive = false;
            GetTabButton();
            __instance.StartCoroutine(UpdateOtherTabs());
        }

        // At logout, vanilla's teardown path presses the craft tab; without this flag our
        // SetTabInactive re-activates GameObjects mid-destroy — Unity logs
        // "GameObjects can not be made active when they are being destroyed" x6 (seen 0.0.2).
        [HarmonyPatch(typeof(InventoryGui), "OnDestroy")]
        [HarmonyPrefix]
        public static void OnDestroyPatch()
        {
            _tearingDown = true;
            _tab = null;
            _baseUI = null;
            _tooltipPrefab = null;
        }

        // The destruction cascade at logout starts with ZNet.Shutdown, BEFORE InventoryGui's
        // own OnDestroy — arm the flag there too (same pattern as Core's env-box panel guard).
        // Re-armed false on the next InventoryGui.Awake.
        [HarmonyPatch(typeof(ZNet), "Shutdown")]
        [HarmonyPrefix]
        public static void ZNetShutdownPatch() => _tearingDown = true;

        // Vanilla shared tooltip popup prefab, scavenged from the player inventory grid.
        public static GameObject GetTooltipPrefab()
        {
            if (_tooltipPrefab != null)
                return _tooltipPrefab;
            if (InventoryGui.instance != null && InventoryGui.instance.m_playerGrid != null
                && InventoryGui.instance.m_playerGrid.m_elementPrefab != null
                && InventoryGui.instance.m_playerGrid.m_elementPrefab.TryGetComponent(out UITooltip tooltip))
            {
                _tooltipPrefab = tooltip.m_tooltipPrefab;
            }
            return _tooltipPrefab;
        }

        private static IEnumerator UpdateOtherTabs()
        {
            var wait = new WaitForSeconds(1f);
            while (true)
            {
                yield return wait;
                HookOtherTabs();
            }
        }

        public static Button GetTabButton()
        {
            if (InventoryGui.instance == null)
                return null;
            if (_tab != null)
                return _tab;

            Button tabUpgrade = InventoryGui.instance.m_tabUpgrade;
            _tab = Object.Instantiate(tabUpgrade, tabUpgrade.transform.parent);
            _tab.gameObject.SetActive(true);
            _tab.transform.SetSiblingIndex(_tab.transform.parent.childCount - 2);
            _tab.name = "FATI";
            _tab.onClick.RemoveAllListeners();
            _tab.onClick.AddListener(SetTabActive);
            UpdateTabName();
            SetTabInactive();
            return _tab;
        }

        public static void UpdateTabName()
        {
            if (_tab == null)
                return;
            Transform textTransform = _tab.transform.Find("Text");
            if (textTransform != null && textTransform.TryGetComponent(out TextMeshProUGUI label))
                label.text = Ascend.tabName.Value;
        }

        // Config changed (grid size) — rebuild the panel on next activation.
        public static void MarkUiDirty() => _uiDirty = true;

        public static void OnRecipesChanged()
        {
            if (_baseUI != null)
                _baseUI.OnRecipesChanged();
        }

        public static void OnRecipeSubmitResult(bool ok)
        {
            if (_baseUI != null)
                _baseUI.OnRecipeSubmitResult(ok);
        }

        public static void OnCreatureSubmitResult(bool ok)
        {
            if (_baseUI != null)
                _baseUI.OnCreatureSubmitResult(ok);
        }

        private static FatiBaseUI GetBaseUI()
        {
            if (InventoryGui.instance == null)
                return null;
            var crafting = (RectTransform)InventoryGui.instance.m_inventoryRoot.Find("Crafting");
            if (crafting == null)
                return _baseUI;

            // The grid's columns and rows come from the Crafting panel's size, read once when it is built.
            // VAInventory's unified layout re-applies its own rects on every InventoryGui.Show, so that panel
            // can change size after we built against it - flipping [UI] Layout mid-session, or a redeploy while
            // the game is up. Compare rather than assume an ordering, and reuse the rebuild path that exists.
            if (_baseUI != null && _baseUI.BuiltPanelSize != crafting.sizeDelta)
            {
                Ascend.Log?.LogInfo($"Crafting panel resized {_baseUI.BuiltPanelSize} -> {crafting.sizeDelta}; rebuilding the FATI grid.");
                _uiDirty = true;
            }

            if (_baseUI != null && _uiDirty)
            {
                _uiDirty = false;
                Object.Destroy(_baseUI.gameObject);
                _baseUI = null;
            }
            if (_baseUI == null)
            {
                _baseUI = FatiBaseUI.Create(crafting);
                _baseUI.SetVisibility(false);
            }
            return _baseUI;
        }

        private static void HookOtherTabs()
        {
            if (InventoryGui.instance == null || _tab == null)
                return;
            foreach (Button button in InventoryGui.instance.m_tabCraft.transform.parent.GetComponentsInChildren<Button>())
            {
                if (button == _tab)
                    continue;
                button.onClick.RemoveListener(SetTabInactive);
                button.onClick.AddListener(SetTabInactive);
            }
        }

        public static void UpdateTabPosition()
        {
            Button tab = GetTabButton();
            if (tab == null)
                return;
            var rect = (RectTransform)tab.transform;
            for (int index = rect.GetSiblingIndex() - 1; index >= 0; --index)
            {
                if (rect.parent.GetChild(index).gameObject.activeSelf)
                {
                    Vector2 anchored = ((RectTransform)rect.parent.GetChild(index)).anchoredPosition;
                    rect.anchoredPosition = anchored + new Vector2(107f, 0f);
                    break;
                }
            }
        }

        public static void SetTabActive()
        {
            if (InventoryGui.instance == null || _tearingDown)
                return;
            TabActive = true;
            Button tab = GetTabButton();
            if (tab != null)
                tab.interactable = false;
            InventoryGui.instance.m_inventoryRoot.Find("Crafting/RecipeList")?.gameObject.SetActive(false);
            InventoryGui.instance.m_inventoryRoot.Find("Crafting/Decription")?.gameObject.SetActive(false);
            SetRepairVisible(false);
            GetBaseUI()?.SetVisibility(true);
            InventoryGui.instance.m_tabCraft.interactable = true;
            InventoryGui.instance.m_tabUpgrade.interactable = true;
            UpdateTabPosition();
        }

        public static void SetTabInactive()
        {
            if (InventoryGui.instance == null || _tearingDown)
                return;
            TabActive = false;
            Button tab = GetTabButton();
            if (tab != null)
                tab.interactable = true;
            if (_baseUI != null)
                _baseUI.SetVisibility(false);
            InventoryGui.instance.m_inventoryRoot.Find("Crafting/RecipeList")?.gameObject.SetActive(true);
            InventoryGui.instance.m_inventoryRoot.Find("Crafting/Decription")?.gameObject.SetActive(true);
            SetRepairVisible(true);
        }

        // The repair hammer + its plate double up over our grid, so they hide with the vanilla
        // panes while FATI is active. Vanilla's UpdateRepair re-shows them on every crafting
        // refresh whenever a station is in range, hence the enforcing postfix below.
        private static void SetRepairVisible(bool visible)
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null)
                return;
            if (gui.m_repairButton != null)
                gui.m_repairButton.gameObject.SetActive(visible);
            if (gui.m_repairPanel != null)
                gui.m_repairPanel.gameObject.SetActive(visible);
            if (gui.m_repairPanelSelection != null)
                gui.m_repairPanelSelection.gameObject.SetActive(visible);
            if (!visible && gui.m_repairButtonGlow != null)
                gui.m_repairButtonGlow.gameObject.SetActive(false);
        }

        [HarmonyPatch(typeof(InventoryGui), "UpdateRepair")]
        [HarmonyPostfix]
        public static void UpdateRepairPatch()
        {
            if (TabActive && !_tearingDown)
                SetRepairVisible(false);
        }

        // Entry point for the view-recipe hotkey (R over any vanilla tooltip).
        public static void OpenRecipeFor(Logic.Item item)
        {
            if (item == null || InventoryGui.instance == null || _tearingDown)
                return;
            SetTabActive();
            GetBaseUI()?.ShowRecipe(item);
        }

        [HarmonyPatch(typeof(InventoryGui), "UpdateCraftingPanel")]
        [HarmonyPostfix]
        public static void UpdateCraftingPanelPatch()
        {
            UpdateTabPosition();
            if (TabActive)
                SetTabActive();
        }

        [HarmonyPatch(typeof(InventoryGui), "OnTabCraftPressed")]
        [HarmonyPatch(typeof(InventoryGui), "OnTabUpgradePressed")]
        [HarmonyPrefix]
        public static void OnVanillaTabPressedPatch() => SetTabInactive();

        [HarmonyPatch(typeof(InventoryGui), "SetupCrafting")]
        [HarmonyPostfix]
        public static void SetupCraftingPatch()
        {
            if (TabActive)
                SetTabActive();
        }
    }
}
