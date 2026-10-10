using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using FiresAllTheItems.Logic;
using FiresCore.UI;

namespace FiresAllTheItems.UI
{
    // Port of VNEI UI/BaseUI.cs + SearchUI.cs, attached-to-crafting mode: the panel lives
    // INSIDE InventoryGui's Crafting transform (so it sits on the vanilla parchment, no own
    // background) and shows a pooled, PAGINATED item grid with search + category toggles.
    // Built entirely at runtime through FiresCore.UI.UIBuilderHelper (no bundle, no Jotunn).
    public class FatiBaseUI : MonoBehaviour
    {
        private static bool _searchDirtyStatic;

        private RectTransform root;
        private RectTransform searchView;
        private FatiRecipeUI recipeUI;
        private RectTransform spawnRect;
        private TMP_InputField searchField;
        private TextMeshProUGUI pageText;
        private readonly List<FatiTypeToggle> typeToggles = new List<FatiTypeToggle>();
        private UnityEngine.UI.Image favoriteStar;
        private bool favoritesOnly;
        private Action<Item> pickCallback;
        private GameObject pickBanner;
        private TextMeshProUGUI pickBannerLabel;

        public bool IsPicking => pickCallback != null;

        /// <summary>The Crafting panel size this grid was sized against; FatiTab rebuilds when it stops matching.</summary>
        public Vector2 BuiltPanelSize { get; private set; }

        internal IReadOnlyList<FatiTypeToggle> TypeToggles => typeToggles;

        private readonly List<ListItem> listItems = new List<ListItem>();
        private readonly List<ListItem> presetItems = new List<ListItem>();
        private bool presetMode;
        private UnityEngine.UI.Image presetGlyph;
        private UnityEngine.UI.Image presetBackground;
        private static readonly Color PresetOffColor = new Color(1f, 1f, 1f, 0.28f);
        private readonly List<FatiDisplayItem> displayItems = new List<FatiDisplayItem>();
        private bool hasInit;
        private int columns;
        private int rows;
        private int currentPage;
        private int maxPages;

        private class ListItem
        {
            public readonly Item item;
            public bool isActive;

            public ListItem(Item item)
            {
                this.item = item;
            }
        }

        // Builds the panel into the vanilla Crafting transform, sized off its parchment like
        // VNEI: cols=(w-10)/50, rows=(h-160)/50, anchoredPosition (0,-50).
        public static FatiBaseUI Create(RectTransform craftingPanel)
        {
            var go = new GameObject("FATI_BaseUI");
            go.transform.SetParent(craftingPanel, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -50f);

            var ui = go.AddComponent<FatiBaseUI>();
            ui.root = rect;
            // Attached mode fills the crafting parchment exactly like VNEI: cols=(w-10)/50,
            // rows=(h-160)/50, derived from the panel — the config counts are for the future
            // standalone window, NOT the attached view (0.0.1 wrongly clamped by config and
            // left a small grid floating mid-panel).
            Vector2 panelSize = craftingPanel.sizeDelta;
            ui.BuiltPanelSize = panelSize;
            ui.columns = Mathf.Max(1, (int)((panelSize.x - 10f) / 50f));
            ui.rows = Mathf.Max(1, (int)((panelSize.y - 160f) / 50f));
            rect.sizeDelta = new Vector2(ui.columns * 50f + 10f, ui.rows * 50f + 100f);

            // Two swappable views inside the same root: the search grid and the recipe view.
            var searchGO = new GameObject("SearchView");
            searchGO.transform.SetParent(rect, false);
            ui.searchView = searchGO.AddComponent<RectTransform>();
            ui.searchView.anchorMin = Vector2.zero;
            ui.searchView.anchorMax = Vector2.one;
            ui.searchView.offsetMin = Vector2.zero;
            ui.searchView.offsetMax = Vector2.zero;

            ui.BuildChrome();
            ui.RebuildCells();
            ui.recipeUI = FatiRecipeUI.Create(rect, ui);
            return ui;
        }

        public void ShowSearch()
        {
            if (recipeUI != null)
                recipeUI.gameObject.SetActive(false);
            searchView.gameObject.SetActive(true);
        }

        public void ShowRecipe(Item item)
        {
            EndPick();
            recipeUI.SetItem(item);
            recipeUI.gameObject.SetActive(true);
            searchView.gameObject.SetActive(false);
        }

        /// <summary>Turns the item grid into a picker; the next left-click hands that item to the caller.</summary>
        public void BeginPick(string prompt, Action<Item> onPicked)
        {
            pickCallback = onPicked;
            pickBannerLabel.text = prompt;
            pickBanner.SetActive(true);
            ShowSearch();
        }

        public void CompletePick(Item item)
        {
            Action<Item> callback = pickCallback;
            EndPick();
            ReturnToRecipeView();
            callback?.Invoke(item);
        }

        public void CancelPick()
        {
            EndPick();
            ReturnToRecipeView();
        }

        private void EndPick()
        {
            pickCallback = null;
            if (pickBanner != null)
                pickBanner.SetActive(false);
        }

        private void ReturnToRecipeView()
        {
            recipeUI.gameObject.SetActive(true);
            searchView.gameObject.SetActive(false);
        }

        private void OnDisable() => EndPick();

        private void BuildChrome()
        {
            // Category filter row spread evenly across the panel top (user preference over
            // VNEI's right-corner cluster).
            ItemType[] toggleOrder =
            {
                ItemType.Undefined, ItemType.Creature, ItemType.Piece, ItemType.Item,
                ItemType.Food, ItemType.Armor, ItemType.Weapon,
            };
            float rootWidth = root.sizeDelta.x;
            int slots = toggleOrder.Length + 2;   // +1 favorites star, +1 presets switch
            float step = (rootWidth - 40f) / slots;
            for (int i = 0; i < toggleOrder.Length; ++i)
            {
                float x = 20f + step * (i + 0.5f);
                typeToggles.Add(FatiTypeToggle.Create(searchView, this, toggleOrder[i], x));
            }

            // Favorites filter: star glyph at the row's end; click = show starred items only.
            var favGO = new GameObject("FavToggle");
            favGO.transform.SetParent(searchView, false);
            var favRect = favGO.AddComponent<RectTransform>();
            favRect.anchorMin = new Vector2(0f, 1f);
            favRect.anchorMax = new Vector2(0f, 1f);
            favRect.sizeDelta = new Vector2(30f, 30f);
            favRect.anchoredPosition = new Vector2(20f + step * (slots - 0.5f), -18f);
            var favBg = favGO.AddComponent<UnityEngine.UI.Image>();
            favBg.color = new Color(0f, 0f, 0f, 0.25f);
            var starGO = new GameObject("Star");
            starGO.transform.SetParent(favGO.transform, false);
            var starRect = starGO.AddComponent<RectTransform>();
            starRect.anchorMin = Vector2.zero;
            starRect.anchorMax = Vector2.one;
            starRect.offsetMin = new Vector2(3f, 3f);
            starRect.offsetMax = new Vector2(-3f, -3f);
            favoriteStar = starGO.AddComponent<UnityEngine.UI.Image>();
            favoriteStar.sprite = FatiGlyphs.Get(FatiGlyphs.Favorite);
            favoriteStar.preserveAspect = true;
            favoriteStar.raycastTarget = false;
            favoriteStar.color = new Color(1f, 1f, 1f, 0.28f);
            var favTip = favGO.AddComponent<UITooltip>();
            favTip.m_tooltipPrefab = FatiTab.GetTooltipPrefab();
            favTip.Set("Favorites", "Show only starred items");
            var favButton = favGO.AddComponent<UnityEngine.UI.Button>();
            favButton.targetGraphic = favBg;
            favButton.onClick.AddListener(() =>
            {
                favoritesOnly = !favoritesOnly;
                favoriteStar.color = favoritesOnly ? new Color(1f, 0.72f, 0.23f) : new Color(1f, 1f, 1f, 0.28f);
                favBg.color = favoritesOnly ? new Color(0f, 0f, 0f, 0.55f) : new Color(0f, 0f, 0f, 0.25f);
                UpdateSearch(true);
            });

            BuildPresetToggle(20f + step * (slots - 1.5f));

            // Calming plate under the grid: the panel's rune pattern reads straight through the
            // icons otherwise. Translucent, so the parchment still shows — same treatment as the
            // search field, one step lighter. Created BEFORE the grid so it draws underneath.
            var gridPlateGO = new GameObject("GridPlate");
            gridPlateGO.transform.SetParent(searchView, false);
            var gridPlateRect = gridPlateGO.AddComponent<RectTransform>();
            gridPlateRect.anchorMin = new Vector2(0f, 1f);
            gridPlateRect.anchorMax = new Vector2(0f, 1f);
            gridPlateRect.pivot = new Vector2(0f, 1f);
            gridPlateRect.anchoredPosition = new Vector2(3f, -38f);
            gridPlateRect.sizeDelta = new Vector2(columns * 50f + 4f, rows * 50f + 4f);
            var gridPlateImage = gridPlateGO.AddComponent<UnityEngine.UI.Image>();
            gridPlateImage.color = new Color(0.76f, 0.64f, 0.40f, 0.30f);
            gridPlateImage.raycastTarget = false;

            var gridFillGO = new GameObject("GridFill");
            gridFillGO.transform.SetParent(searchView, false);
            var gridFillRect = gridFillGO.AddComponent<RectTransform>();
            gridFillRect.anchorMin = new Vector2(0f, 1f);
            gridFillRect.anchorMax = new Vector2(0f, 1f);
            gridFillRect.pivot = new Vector2(0f, 1f);
            gridFillRect.anchoredPosition = new Vector2(5f, -40f);
            gridFillRect.sizeDelta = new Vector2(columns * 50f, rows * 50f);
            var gridFillImage = gridFillGO.AddComponent<UnityEngine.UI.Image>();
            gridFillImage.color = new Color(0.10f, 0.08f, 0.06f, 0.55f);
            gridFillImage.raycastTarget = false;

            // Grid area between the toggle row and the bottom bar.
            var spawnGO = new GameObject("Grid");
            spawnGO.transform.SetParent(searchView, false);
            spawnRect = spawnGO.AddComponent<RectTransform>();
            spawnRect.anchorMin = new Vector2(0f, 1f);
            spawnRect.anchorMax = new Vector2(0f, 1f);
            spawnRect.pivot = new Vector2(0f, 1f);
            spawnRect.anchoredPosition = new Vector2(5f, -40f);
            spawnRect.sizeDelta = new Vector2(columns * 50f, rows * 50f);
            // Invisible raycast target so the wheel pages even over empty grid area.
            var gridCatcher = spawnGO.AddComponent<UnityEngine.UI.Image>();
            gridCatcher.color = Color.clear;
            gridCatcher.raycastTarget = true;
            var wheel = spawnGO.AddComponent<FatiScrollHandler>();
            wheel.baseUI = this;

            // Bottom bar: search left, pager right. Parented to ROOT, not the search view —
            // VNEI keeps this bar visible in the recipe view too, and typing snaps back to
            // the grid (UpdateSearch always re-shows the search view).
            //
            // The pager is built FIRST so the search field can be sized off its measured left
            // edge: 0.0.7 clamped the field to 220px and left ~230px of the bar empty.
            float right = -5f;
            right = AddPagerButton(">>|", right, () => LastPage());
            right = AddPagerButton(">", right, () => SwitchPage(1));
            pageText = UIBuilderHelper.CreateLabel(root, "PageText", "1/1", 14f, Color.white,
                new Vector2(1f, 0f), new Vector2(1f, 0f), TextAlignmentOptions.Center);
            var pageRect = pageText.rectTransform;
            pageRect.pivot = new Vector2(1f, 0f);
            pageRect.sizeDelta = new Vector2(52f, 30f);
            pageRect.anchoredPosition = new Vector2(right, 10f);
            right -= 56f;
            right = AddPagerButton("<", right, () => SwitchPage(-1));
            float lastAnchor = right;
            AddPagerButton("|<<", right, () => FirstPage());
            float pagerLeftEdge = lastAnchor - PagerButtonWidth;

            const float searchLeft = 8f;
            const float searchGap = 10f;
            float searchWidth = Mathf.Max(60f, root.sizeDelta.x + pagerLeftEdge - searchLeft - searchGap);

            // Light plate UNDER the field (earlier sibling = drawn below) so the box has a visible
            // edge: at 0.45 alpha the field vanished into the panel's rune pattern.
            var plateGO = new GameObject("SearchPlate");
            plateGO.transform.SetParent(root, false);
            var plateRect = plateGO.AddComponent<RectTransform>();
            plateRect.anchorMin = new Vector2(0f, 0f);
            plateRect.anchorMax = new Vector2(0f, 0f);
            plateRect.pivot = new Vector2(0f, 0f);
            plateRect.anchoredPosition = new Vector2(searchLeft - 2f, 8f);
            plateRect.sizeDelta = new Vector2(searchWidth + 4f, 34f);
            var plateImage = plateGO.AddComponent<UnityEngine.UI.Image>();
            plateImage.color = new Color(0.78f, 0.66f, 0.42f, 0.75f);
            plateImage.raycastTarget = false;

            searchField = UIBuilderHelper.CreateInputField(root, "Search",
                "search...   -exclude   @mod   #type   ^ingredient",
                new Vector2(0f, 0f), new Vector2(0f, 0f), 14);
            var searchRect = searchField.GetComponent<RectTransform>();
            searchRect.pivot = new Vector2(0f, 0f);
            searchRect.anchoredPosition = new Vector2(searchLeft, 10f);
            searchRect.sizeDelta = new Vector2(searchWidth, 30f);
            searchField.GetComponent<UnityEngine.UI.Image>().color = new Color(0.16f, 0.13f, 0.10f, 0.96f);
            var searchText = searchField.textComponent as TMPro.TextMeshProUGUI;
            if (searchText != null)
                searchText.color = Color.white;
            // Core's placeholder is parchment ink (dark brown) — invisible on this field's
            // black backing, which is half of why the bar read as empty chrome.
            var searchPlaceholder = searchField.placeholder as TMPro.TextMeshProUGUI;
            if (searchPlaceholder != null)
                searchPlaceholder.color = new Color(0.88f, 0.83f, 0.72f, 0.7f);

            // Magnifier glyph in the left gutter; the text viewport starts after it.
            var magGO = new GameObject("Magnifier");
            magGO.transform.SetParent(searchField.transform, false);
            var magRect = magGO.AddComponent<RectTransform>();
            magRect.anchorMin = new Vector2(0f, 0.5f);
            magRect.anchorMax = new Vector2(0f, 0.5f);
            magRect.sizeDelta = new Vector2(18f, 18f);
            magRect.anchoredPosition = new Vector2(17f, 0f);
            var magImage = magGO.AddComponent<UnityEngine.UI.Image>();
            magImage.sprite = FatiGlyphs.Get(FatiGlyphs.Search);
            magImage.preserveAspect = true;
            magImage.raycastTarget = false;
            magImage.color = new Color(0.88f, 0.83f, 0.72f, 0.9f);
            if (searchField.textViewport != null)
                searchField.textViewport.offsetMin = new Vector2(30f, 2f);

            searchField.onValueChanged.AddListener(_ => UpdateSearch(true));
            BuildPickBanner();
        }

        private void BuildPickBanner()
        {
            UnityEngine.UI.Image plate = UIBuilderHelper.CreateImage(root, "PickBanner", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Color(0.10f, 0.08f, 0.06f, 0.96f));
            pickBanner = plate.gameObject;
            var bannerRect = plate.rectTransform;
            bannerRect.pivot = new Vector2(0.5f, 1f);
            bannerRect.offsetMin = new Vector2(4f, -PickBannerHeight);
            bannerRect.offsetMax = new Vector2(-4f, 0f);
            plate.gameObject.AddComponent<UnityEngine.UI.Outline>().effectColor = new Color(0.78f, 0.66f, 0.42f, 0.75f);

            pickBannerLabel = UIBuilderHelper.CreateLabel(plate.transform, "Prompt", "", 14f, new Color(1f, 0.72f, 0.23f),
                Vector2.zero, Vector2.one, TextAlignmentOptions.MidlineLeft);
            pickBannerLabel.rectTransform.offsetMin = new Vector2(10f, 0f);
            pickBannerLabel.rectTransform.offsetMax = new Vector2(-90f, 0f);

            var cancel = UIBuilderHelper.CreateParchmentButton(plate.transform, "Cancel", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), CancelPick);
            var cancelRect = cancel.GetComponent<RectTransform>();
            cancelRect.pivot = new Vector2(1f, 0.5f);
            cancelRect.sizeDelta = new Vector2(70f, 24f);
            cancelRect.anchoredPosition = new Vector2(-6f, 0f);
            pickBanner.SetActive(false);
        }

        private const float PagerButtonWidth = 30f;
        private const float PickBannerHeight = 34f;

        private float AddPagerButton(string label, float right, Action onClick)
        {
            var button = UIBuilderHelper.CreateParchmentButton(root, label,
                new Vector2(1f, 0f), new Vector2(1f, 0f), () => onClick());
            var rect = button.GetComponent<RectTransform>();
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(PagerButtonWidth, 30f);
            rect.anchoredPosition = new Vector2(right, 10f);
            return right - (PagerButtonWidth + 4f);
        }

        private void RebuildCells()
        {
            foreach (FatiDisplayItem cell in displayItems)
                Destroy(cell.gameObject);
            displayItems.Clear();
            for (int i = 0; i < columns * rows; ++i)
            {
                FatiDisplayItem cell = FatiDisplayItem.Create(spawnRect, this);
                cell.gameObject.SetActive(false);
                displayItems.Add(cell);
            }
        }

        private void Update()
        {
            if (!hasInit && Indexing.HasIndexed() && Player.m_localPlayer != null)
            {
                hasInit = true;
                Init();
            }
            if (hasInit && _searchDirtyStatic && searchView.gameObject.activeSelf)
            {
                _searchDirtyStatic = false;
                UpdateSearch(true);
            }
            if (IsPicking && Input.GetKeyDown(KeyCode.Escape))
                CancelPick();
        }

        private void Init()
        {
            listItems.Clear();
            foreach (KeyValuePair<string, Item> activeItem in Indexing.GetActiveItems())
                listItems.Add(new ListItem(activeItem.Value));
            // Alphabetical by localized name (internal name fallback) — the raw ZNetScene
            // order the 0.0.2 grid showed reads as random noise.
            listItems.Sort((a, b) => string.Compare(a.item.GetPrimaryName(), b.item.GetPrimaryName(), StringComparison.OrdinalIgnoreCase));
            foreach (FatiTypeToggle toggle in typeToggles)
                toggle.RefreshCount();
            Indexing.AfterUpdateKnownItems += OnKnownChanged;
            UpdateSearch(true);
        }

        private void OnDestroy() => Indexing.AfterUpdateKnownItems -= OnKnownChanged;

        private void OnKnownChanged()
        {
            // Only refresh while the GRID is the visible view — refreshing from the recipe
            // view would snap the user back to the grid mid-read (VNEI gates the same way).
            if (searchView != null && searchView.gameObject.activeSelf && gameObject.activeInHierarchy)
                UpdateSearch(true);
        }

        public void OnTypeToggleChanged() => UpdateSearch(true);

        public void OnRecipesChanged()
        {
            if (recipeUI != null)
                recipeUI.OnRecipesChanged();
        }

        public void OnRecipeSubmitResult(bool ok)
        {
            if (recipeUI != null)
                recipeUI.OnRecipeSubmitResult(ok);
        }

        public void OnCreatureSubmitResult(bool ok)
        {
            if (recipeUI != null)
                recipeUI.OnCreatureSubmitResult(ok);
        }

        public static void MarkSearchDirty() => _searchDirtyStatic = true;

        // Creatures/Presets switch. Presets are FiresAdminPrefabs' monster presets, read through Core's bridge;
        // with that mod absent the list is empty and the switch says so rather than showing an empty grid.
        private void BuildPresetToggle(float x)
        {
            var go = new GameObject("PresetToggle");
            go.transform.SetParent(searchView, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(30f, 30f);
            rect.anchoredPosition = new Vector2(x, -18f);

            var bg = go.AddComponent<UnityEngine.UI.Image>();
            bg.color = new Color(0f, 0f, 0f, 0.25f);

            var glyphGO = new GameObject("Glyph");
            glyphGO.transform.SetParent(go.transform, false);
            var glyphRect = glyphGO.AddComponent<RectTransform>();
            glyphRect.anchorMin = Vector2.zero;
            glyphRect.anchorMax = Vector2.one;
            glyphRect.offsetMin = new Vector2(3f, 3f);
            glyphRect.offsetMax = new Vector2(-3f, -3f);
            presetGlyph = glyphGO.AddComponent<UnityEngine.UI.Image>();
            presetGlyph.sprite = FatiGlyphs.Get(FatiGlyphs.Preset);
            presetGlyph.preserveAspect = true;
            presetGlyph.raycastTarget = false;
            presetGlyph.color = PresetOffColor;

            var tip = go.AddComponent<UITooltip>();
            tip.m_tooltipPrefab = FatiTab.GetTooltipPrefab();
            tip.Set("Presets", "Switch the grid between creatures and monster presets");

            presetBackground = bg;
            var button = go.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(TogglePresetMode);
        }

        private void TogglePresetMode()
        {
            if (!presetMode && !PresetCatalog.Available)
            {
                FatiEditorWidgets.Message("monster presets need FiresAdminPrefabs on this world.");
                return;
            }
            presetMode = !presetMode;
            presetGlyph.color = presetMode ? new Color(1f, 0.72f, 0.23f) : PresetOffColor;
            presetBackground.color = presetMode ? new Color(0f, 0f, 0f, 0.55f) : new Color(0f, 0f, 0f, 0.25f);
            currentPage = 0;
            if (presetMode)
                RefreshPresets();
            else
                UpdateSearch(true);
        }

        /// <summary>Re-reads the preset list from the owning mod and redraws the grid.</summary>
        public void RefreshPresets()
        {
            PresetCatalog.Rebuild();
            presetItems.Clear();
            foreach (Item preset in PresetCatalog.All)
                presetItems.Add(new ListItem(preset));
            UpdateSearch(true);
        }

        public void UpdateSearch(bool recalculateActive)
        {
            if (!hasInit)
                return;
            // VNEI: any search/page interaction returns to the grid view.
            ShowSearch();
            if (recalculateActive)
                RecalculateActive();

            int cellCount = displayItems.Count;
            List<ListItem> source = presetMode ? presetItems : listItems;
            List<ListItem> filtered = source.Where(i => i.isActive && (presetMode || i.item.IsKnown)).ToList();
            maxPages = Mathf.Max(Mathf.CeilToInt(filtered.Count / (float)cellCount) - 1, 0);
            currentPage = Mathf.Clamp(currentPage, 0, maxPages);
            List<ListItem> page = filtered.Skip(currentPage * cellCount).Take(cellCount).ToList();
            pageText.text = $"{currentPage + 1}/{maxPages + 1}";

            for (int i = 0; i < displayItems.Count; ++i)
            {
                displayItems[i].gameObject.SetActive(true);
                displayItems[i].SetItem(i < page.Count ? page[i].item : null, 1);
                var rect = (RectTransform)displayItems[i].transform;
                int col = i % columns;
                int row = i / columns;
                rect.anchoredPosition = new Vector2((col + 0.5f) * 50f, -(row + 0.5f) * 50f);
            }
        }

        private void RecalculateActive()
        {
            bool useBlacklist = Ascend.useBlacklist.Value;
            FatiSearchKey[] searchKeys = (searchField != null ? searchField.text : string.Empty)
                .Split()
                .Where(k => k.Length > 0)
                .Select(k => new FatiSearchKey(k))
                .ToArray();
            Parallel.ForEach(listItems, i => i.isActive = CalculateActive(i.item, useBlacklist, searchKeys));
            foreach (ListItem entry in presetItems)
                entry.isActive = searchKeys.Length == 0 || searchKeys.All(key => IsItemSearched(entry.item, key));
        }

        private static bool IsItemSearched(Item item, FatiSearchKey search)
        {
            return item.GetPrimaryName().IndexOf(search.key, StringComparison.OrdinalIgnoreCase) >= 0
                || item.internalName.IndexOf(search.key, StringComparison.OrdinalIgnoreCase) >= 0
                || (item.localizedDescription ?? "").IndexOf(search.key, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool CalculateActive(Item item, bool useBlacklist, FatiSearchKey[] searchKeys)
        {
            if (useBlacklist && item.isOnBlacklist)
                return false;
            if (typeToggles.Any(toggle => toggle.IsDisabled(item.itemType)))
                return false;
            if (favoritesOnly && !item.isFavorite)
                return false;
            foreach (FatiSearchKey search in searchKeys)
            {
                bool matched;
                if (search.isMod)
                    matched = item.GetModName().IndexOf(search.key, StringComparison.OrdinalIgnoreCase) >= 0;
                else if (search.isItemDropType)
                    matched = item.itemDropType.ToString().IndexOf(search.key, StringComparison.OrdinalIgnoreCase) >= 0;
                else if (search.isIngredient)
                    matched = item.result.Any(r => r.Ingredients.Values.Any(list => list.Any(p => IsItemSearched(p.item, search))));
                else
                    matched = IsItemSearched(item, search);
                if (search.isNegative)
                    matched = !matched;
                if (!matched)
                    return false;
            }
            return true;
        }

        public void SwitchPage(int skip)
        {
            currentPage = Mathf.Clamp(currentPage + skip, 0, maxPages);
            UpdateSearch(false);
        }

        public void FirstPage()
        {
            currentPage = 0;
            UpdateSearch(false);
        }

        public void LastPage()
        {
            currentPage = maxPages;
            UpdateSearch(false);
        }

        public void SetVisibility(bool visible) => gameObject.SetActive(visible);

        // Mouse wheel over the grid flips pages (VNEI ScrollHandler).
        private class FatiScrollHandler : MonoBehaviour, IScrollHandler
        {
            public FatiBaseUI baseUI;

            public void OnScroll(PointerEventData eventData)
            {
                if (Mathf.Abs(eventData.scrollDelta.y) < 0.01f)
                    return;
                baseUI.SwitchPage(eventData.scrollDelta.y < 0f ? 1 : -1);
            }
        }
    }
}
