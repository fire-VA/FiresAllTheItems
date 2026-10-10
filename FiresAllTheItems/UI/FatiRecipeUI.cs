using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FiresAllTheItems.Logic;
using FiresCore.UI;

namespace FiresAllTheItems.UI
{
    // Port of VNEI UI/RecipeUI.cs + RecipeScroll.cs + CraftingStationList.cs + StationOrder.cs,
    // merged and runtime-built. One scroll with three view states (All/Obtaining/Using — VNEI's
    // split double-scroll is a later polish pass). Rows are VIRTUALIZED exactly like VNEI:
    // spawned/destroyed as they scroll into a ±40px window, because a hub item like Wood has
    // hundreds of recipe rows.
    public class FatiRecipeUI : MonoBehaviour
    {
        private enum View
        {
            Combined,
            Obtaining,
            Using,
        }

        private const float RowHeight = 70f;
        private const float EditGateCheckSeconds = 0.5f;
        private const float DescriptionRightEdgeBesideEditButton = 324f;
        private const float DescriptionRightMargin = 14f;

        private FatiBaseUI baseUI;
        private FatiRecipeEditor editor;
        private FatiCreatureEditor creatureEditor;
        private FatiPresetEditor presetEditor;
        private Button editButton;
        private RectTransform descriptionRect;
        private float nextEditGateCheck;
        private RectTransform rootRect;
        private FatiDisplayItem infoIcon;
        private TextMeshProUGUI infoName;
        private TextMeshProUGUI infoInternalName;
        private TextMeshProUGUI infoModName;
        private TextMeshProUGUI infoDescription;
        private Button obtainingButton;
        private Button usingButton;
        private Button allButton;
        private RectTransform stationRow;
        private ScrollRect scrollRect;
        private RectTransform scrollContent;

        private Item currentItem;
        private View currentView = View.Combined;
        private Item activeStation;
        private readonly List<(Item station, GameObject go, Image icon)> stationElements = new List<(Item, GameObject, Image)>();
        private List<RecipeInfo> recipes = new List<RecipeInfo>();
        private readonly Dictionary<RecipeInfo, RectTransform> rows = new Dictionary<RecipeInfo, RectTransform>();

        public static FatiRecipeUI Create(RectTransform root, FatiBaseUI baseUI)
        {
            var go = new GameObject("RecipeView");
            go.transform.SetParent(root, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var ui = go.AddComponent<FatiRecipeUI>();
            ui.baseUI = baseUI;
            ui.rootRect = rect;
            ui.BuildChrome();
            go.SetActive(false);
            return ui;
        }

        private TextMeshProUGUI sectionTitle;
        private TextMeshProUGUI favoriteLabel;

        private void ToggleFavorite()
        {
            if (currentItem == null)
                return;
            currentItem.UpdateFavorite(!currentItem.isFavorite);
            FavoritesSave.Save();
            UpdateFavoriteLabel();
        }

        private void UpdateFavoriteLabel()
        {
            if (favoriteLabel != null)
                favoriteLabel.color = currentItem != null && currentItem.isFavorite
                    ? new Color(1f, 0.72f, 0.23f)
                    : new Color(0.45f, 0.42f, 0.38f);
        }

        private void BuildChrome()
        {
            Transform parent = rootRect;

            var back = UIBuilderHelper.CreateParchmentButton(parent, "<", new Vector2(0f, 1f), new Vector2(0f, 1f),
                () => baseUI.ShowSearch());
            var backRect = back.GetComponent<RectTransform>();
            backRect.pivot = new Vector2(0f, 1f);
            backRect.sizeDelta = new Vector2(34f, 28f);
            backRect.anchoredPosition = new Vector2(8f, -6f);

            infoIcon = FatiDisplayItem.Create(parent, baseUI, 52f);
            ((RectTransform)infoIcon.transform).anchoredPosition = new Vector2(60f, -44f);

            infoName = UIBuilderHelper.CreateLabel(parent, "Name", "", 20f, new Color(1f, 0.72f, 0.23f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), TextAlignmentOptions.TopLeft);
            var nameRect = infoName.rectTransform;
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.sizeDelta = new Vector2(380f, 26f);
            nameRect.anchoredPosition = new Vector2(96f, -18f);

            infoInternalName = UIBuilderHelper.CreateLabel(parent, "InternalName", "", 12f, new Color(0.8f, 0.8f, 0.8f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), TextAlignmentOptions.TopLeft);
            var internalRect = infoInternalName.rectTransform;
            internalRect.pivot = new Vector2(0f, 1f);
            internalRect.sizeDelta = new Vector2(230f, 18f);
            internalRect.anchoredPosition = new Vector2(96f, -46f);

            var copy = UIBuilderHelper.CreateParchmentButton(parent, "Copy ID", new Vector2(0f, 1f), new Vector2(0f, 1f), CopyId);
            var copyRect = copy.GetComponent<RectTransform>();
            copyRect.pivot = new Vector2(0f, 1f);
            copyRect.sizeDelta = new Vector2(64f, 18f);
            copyRect.anchoredPosition = new Vector2(330f, -46f);

            var favButton = UIBuilderHelper.CreateParchmentButton(parent, "★", new Vector2(0f, 1f), new Vector2(0f, 1f), ToggleFavorite);
            var favRect = favButton.GetComponent<RectTransform>();
            favRect.pivot = new Vector2(0f, 1f);
            favRect.sizeDelta = new Vector2(24f, 18f);
            favRect.anchoredPosition = new Vector2(400f, -46f);
            favoriteLabel = favButton.GetComponentInChildren<TextMeshProUGUI>();

            editButton = UIBuilderHelper.CreateParchmentButton(parent, "Edit recipes", new Vector2(0f, 1f), new Vector2(0f, 1f), OpenEditor);
            var editRect = editButton.GetComponent<RectTransform>();
            editRect.pivot = new Vector2(0f, 1f);
            editRect.sizeDelta = new Vector2(94f, 18f);
            editRect.anchoredPosition = new Vector2(330f, -68f);
            editButton.gameObject.SetActive(false);

            infoModName = UIBuilderHelper.CreateLabel(parent, "ModName", "", 12f, new Color(1f, 0.72f, 0.23f),
                new Vector2(1f, 1f), new Vector2(1f, 1f), TextAlignmentOptions.TopRight);
            var modRect = infoModName.rectTransform;
            modRect.pivot = new Vector2(1f, 1f);
            modRect.sizeDelta = new Vector2(180f, 18f);
            modRect.anchoredPosition = new Vector2(-14f, -18f);

            infoDescription = UIBuilderHelper.CreateLabel(parent, "Description", "", 12f, new Color(0.9f, 0.88f, 0.82f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), TextAlignmentOptions.TopLeft);
            descriptionRect = infoDescription.rectTransform;
            descriptionRect.pivot = new Vector2(0.5f, 1f);
            descriptionRect.offsetMin = new Vector2(96f, -100f);
            descriptionRect.offsetMax = new Vector2(-DescriptionRightMargin, -66f);
            infoDescription.enableWordWrapping = true;
            infoDescription.overflowMode = TextOverflowModes.Ellipsis;

            // Station filter row.
            var stationGO = new GameObject("Stations");
            stationGO.transform.SetParent(parent, false);
            stationRow = stationGO.AddComponent<RectTransform>();
            stationRow.anchorMin = new Vector2(0f, 1f);
            stationRow.anchorMax = new Vector2(1f, 1f);
            stationRow.offsetMin = new Vector2(20f, -140f);
            stationRow.offsetMax = new Vector2(-20f, -102f);

            // Section title left, view switch buttons right — same strip.
            sectionTitle = UIBuilderHelper.CreateLabel(parent, "SectionTitle", "Recipes", 17f, new Color(1f, 0.72f, 0.23f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), TextAlignmentOptions.MidlineLeft);
            var titleRect = sectionTitle.rectTransform;
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.sizeDelta = new Vector2(220f, 24f);
            titleRect.anchoredPosition = new Vector2(20f, -146f);

            allButton = CreateViewButton(parent, "All", -14f, View.Combined);
            usingButton = CreateViewButton(parent, "Using", -102f, View.Using);
            obtainingButton = CreateViewButton(parent, "Obtaining", -190f, View.Obtaining);

            // Plate under the rows, matching the grid: translucent so the parchment reads through,
            // dark enough that ingredient icons stop competing with the rune pattern. Added before
            // the scroll area so it draws underneath it.
            AddRecipePlate(parent, new Vector2(8f, 44f), new Vector2(-8f, -170f), new Color(0.76f, 0.64f, 0.40f, 0.30f));
            AddRecipePlate(parent, new Vector2(10f, 46f), new Vector2(-10f, -172f), new Color(0.10f, 0.08f, 0.06f, 0.55f));

            // Recipe rows scroll — transparent, sitting on the parchment like VNEI; bottom
            // offset clears the shared search/pager bar.
            UIBuilderHelper.ScrollAreaResult scroll = UIBuilderHelper.CreateScrollArea(parent, "Recipes",
                Vector2.zero, Vector2.one);
            var scrollRoot = (RectTransform)scroll.Root.transform;
            scrollRoot.offsetMin = new Vector2(10f, 46f);
            scrollRoot.offsetMax = new Vector2(-10f, -172f);
            scrollRect = scroll.ScrollRect;
            scrollContent = scroll.Content;
            scrollContent.anchorMin = new Vector2(0f, 1f);
            scrollContent.anchorMax = new Vector2(0f, 1f);
            scrollContent.pivot = new Vector2(0f, 1f);
            scrollRect.horizontal = true;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 60f;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scrollRect.onValueChanged.AddListener(_ => UpdateHidden());

            editor = FatiRecipeEditor.Create(rootRect, baseUI);
            creatureEditor = FatiCreatureEditor.Create(rootRect, baseUI);
            presetEditor = FatiPresetEditor.Create(rootRect, baseUI);
        }

        private void OpenEditor()
        {
            if (FatiPresetEditor.CanEdit(currentItem))
                presetEditor.Open(currentItem);
            else if (FatiRecipeEditor.CanEdit(currentItem))
                editor.Open(currentItem);
            else if (FatiCreatureEditor.CanEdit(currentItem))
                creatureEditor.Open(currentItem);
        }

        private void Update()
        {
            if (Time.time < nextEditGateCheck)
                return;
            nextEditGateCheck = Time.time + EditGateCheckSeconds;
            RefreshEditButton();
        }

        private void RefreshEditButton()
        {
            string label = FatiPresetEditor.CanEdit(currentItem) ? "Edit preset"
                : FatiRecipeEditor.CanEdit(currentItem) ? "Edit recipes"
                : FatiCreatureEditor.CanEdit(currentItem) ? "Edit creature"
                : null;
            bool canEdit = label != null;
            if (canEdit)
                editButton.GetComponentInChildren<TextMeshProUGUI>().text = label;
            if (editButton.gameObject.activeSelf == canEdit)
                return;
            editButton.gameObject.SetActive(canEdit);
            descriptionRect.anchorMax = canEdit ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
            descriptionRect.offsetMax = new Vector2(canEdit ? DescriptionRightEdgeBesideEditButton : -DescriptionRightMargin,
                descriptionRect.offsetMax.y);
        }

        public void OnRecipesChanged()
        {
            if (currentItem != null)
                SetItem(currentItem);
            editor.OnRecipesChanged();
            creatureEditor.OnRulesChanged();
        }

        public void OnRecipeSubmitResult(bool ok) => editor.OnSubmitResult(ok);

        public void OnCreatureSubmitResult(bool ok) => creatureEditor.OnSubmitResult(ok);

        private static void AddRecipePlate(Transform parent, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var go = new GameObject("RecipePlate");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private Button CreateViewButton(Transform parent, string label, float xFromRight, View view)
        {
            var button = UIBuilderHelper.CreateParchmentButton(parent, label, new Vector2(1f, 1f), new Vector2(1f, 1f),
                () => SetView(view));
            var rect = button.GetComponent<RectTransform>();
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(84f, 22f);
            rect.anchoredPosition = new Vector2(xFromRight, -146f);
            return button;
        }

        private void CopyId()
        {
            if (currentItem == null)
                return;
            GUIUtility.systemCopyBuffer = currentItem.internalName;
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, $"Copied '{currentItem.internalName}'");
        }

        public Item GetItem() => currentItem;

        public void SetItem(Item item)
        {
            if (editor.IsOpen && editor.EditedItem != item)
                editor.Close();
            if (creatureEditor.IsOpen && creatureEditor.EditedItem != item)
                creatureEditor.Close();
            currentItem = item;
            RefreshEditButton();
            infoName.text = Localization.instance.Localize(item.preLocalizeName);
            infoInternalName.text = item.internalName;
            infoModName.text = item.GetModName();
            infoDescription.text = Localization.instance.Localize(item.GetDescription());
            infoIcon.SetItem(item, 1);

            // Station list: "All" sentinel + this item's stations, vanilla-ish ordering.
            List<Item> stations = item.GetStations()
                .OrderBy(StationOrderByType)
                .ThenBy(StationOrderByName)
                .ToList();
            if (stations.Count > 1)
                stations.Insert(0, Indexing.AllStations);
            if (!stations.Contains(activeStation))
                activeStation = stations.FirstOrDefault();
            SetStations(stations);
            UpdateFavoriteLabel();
            UpdateRecipeView();
        }

        private void SetView(View view)
        {
            currentView = view;
            UpdateRecipeView();
        }

        private void UpdateRecipeView()
        {
            ClearRows();
            if (currentItem == null)
                return;

            obtainingButton.interactable = currentItem.result.Count != 0;
            usingButton.interactable = currentItem.ingredient.Count != 0;
            View view = currentView;
            if (view == View.Obtaining && currentItem.result.Count == 0)
                view = View.Combined;
            if (view == View.Using && currentItem.ingredient.Count == 0)
                view = View.Combined;

            IEnumerable<RecipeInfo> source;
            switch (view)
            {
                case View.Obtaining:
                    source = currentItem.result;
                    sectionTitle.text = "Obtaining";
                    break;
                case View.Using:
                    source = currentItem.ingredient;
                    sectionTitle.text = "Using";
                    break;
                default:
                    source = currentItem.result.Concat(currentItem.ingredient);
                    sectionTitle.text = "Recipes";
                    break;
            }

            // VNEI SetRecipes: hide other items' upgrade chains, order by station level, dedup.
            recipes = FilterByStation(source)
                .Where(r => !r.IsUpgrade(out Item upgradeItem) || ReferenceEquals(upgradeItem, currentItem))
                .OrderBy(r => r.Stations.Sum(s => s.quality))
                .Distinct()
                .ToList();
            if (Ascend.useBlacklist.Value)
                recipes = recipes.Where(r => !r.IsOnBlacklist).ToList();

            float maxWidth = 0f;
            float height = 0f;
            foreach (RecipeInfo recipe in recipes)
            {
                maxWidth = Mathf.Max(maxWidth, recipe.Width);
                height += RowHeight;
            }
            scrollContent.sizeDelta = new Vector2(maxWidth + 60f, height + 5f);
            scrollContent.anchoredPosition = Vector2.zero;
            UpdateHidden();
        }

        private IEnumerable<RecipeInfo> FilterByStation(IEnumerable<RecipeInfo> source)
        {
            if (activeStation == null || activeStation == Indexing.AllStations)
                return source;
            if (activeStation == Indexing.NoStation)
                return source.Where(r => r.Stations.Count == 0 || r.Stations.All(s => s.item == null));
            return source.Where(r => r.Stations.Count >= 1 && r.Stations[0].item == activeStation);
        }

        // ---- station filter row ----

        private void SetStations(List<Item> stations)
        {
            foreach ((Item _, GameObject go, Image _) in stationElements)
                Destroy(go);
            stationElements.Clear();

            const float elementSize = 36f;
            const float spacing = 5f;
            float rowWidth = stationRow.rect.width;
            float step = elementSize + spacing;
            float startX = Mathf.Max(0f, (rowWidth - step * stations.Count + spacing) / 2f);
            for (int i = 0; i < stations.Count; ++i)
            {
                Item station = stations[i];
                var go = new GameObject($"Station_{station.internalName}");
                go.transform.SetParent(stationRow, false);
                var rect = go.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(elementSize, elementSize);
                rect.anchoredPosition = new Vector2(startX + step * i, 0f);

                var bg = go.AddComponent<Image>();
                bg.color = new Color(0f, 0f, 0f, 0.35f);
                bg.raycastTarget = true;

                var iconGO = new GameObject("Icon");
                iconGO.transform.SetParent(go.transform, false);
                var iconRect = iconGO.AddComponent<RectTransform>();
                iconRect.anchorMin = Vector2.zero;
                iconRect.anchorMax = Vector2.one;
                iconRect.offsetMin = new Vector2(3f, 3f);
                iconRect.offsetMax = new Vector2(-3f, -3f);
                var icon = iconGO.AddComponent<Image>();
                icon.raycastTarget = false;
                icon.preserveAspect = true;
                Sprite sprite = station.GetIcon();
                if (sprite != null)
                    icon.sprite = sprite;
                else
                    icon.enabled = false;

                var tooltip = go.AddComponent<UITooltip>();
                tooltip.m_tooltipPrefab = FatiTab.GetTooltipPrefab();
                tooltip.Set(station.preLocalizeName, "");

                Item captured = station;
                var button = go.AddComponent<Button>();
                button.targetGraphic = bg;
                button.onClick.AddListener(() =>
                {
                    activeStation = captured;
                    UpdateStationTints();
                    UpdateRecipeView();
                });

                stationElements.Add((station, go, icon));
            }
            UpdateStationTints();
        }

        private void UpdateStationTints()
        {
            foreach ((Item station, GameObject go, Image icon) in stationElements)
            {
                bool active = station == activeStation;
                icon.color = active ? Color.white : Color.gray;
                go.GetComponent<Image>().color = active ? new Color(0.25f, 0.2f, 0.1f, 0.6f) : new Color(0f, 0f, 0f, 0.35f);
            }
        }

        // ---- virtualized recipe rows (VNEI RecipeScroll.UpdateHidden) ----

        private void ClearRows()
        {
            foreach (RectTransform row in rows.Values)
                Destroy(row.gameObject);
            rows.Clear();
            recipes.Clear();
        }

        private void UpdateHidden()
        {
            if (recipes.Count == 0)
                return;
            Rect viewRect = ((RectTransform)scrollRect.transform).rect;
            Vector2 contentPos = scrollContent.anchoredPosition;
            float posY = -35f;
            foreach (RecipeInfo recipe in recipes)
            {
                bool hidden = posY > -contentPos.y + 40f || posY < -contentPos.y - viewRect.height - 40f;
                if (hidden && rows.ContainsKey(recipe))
                {
                    Destroy(rows[recipe].gameObject);
                    rows.Remove(recipe);
                }
                else if (!hidden && !rows.ContainsKey(recipe))
                {
                    rows.Add(recipe, SpawnRecipeRow(recipe, posY));
                }
                posY -= RowHeight;
            }
        }

        private RectTransform SpawnRecipeRow(RecipeInfo recipe, float posY)
        {
            var rowGO = new GameObject("Row");
            rowGO.transform.SetParent(scrollContent, false);
            var row = rowGO.AddComponent<RectTransform>();
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(0f, 1f);
            row.pivot = new Vector2(0f, 1f);
            row.sizeDelta = new Vector2(Mathf.Max(recipe.Width + 60f, 100f), RowHeight);
            row.anchoredPosition = new Vector2(10f, posY + 35f);

            float posX = 0f;
            foreach (KeyValuePair<Amount, List<Part>> ingredient in recipe.Ingredients)
            {
                if (recipe.Ingredients.Count > 1 || ingredient.Key.min != 1 || ingredient.Key.max != 1 || Math.Abs(ingredient.Key.chance - 1f) > 0.01f)
                    SpawnAmountLabel(row, ingredient.Key, ref posX);
                foreach (Part part in ingredient.Value)
                    SpawnPartCell(row, part, ref posX);
            }

            SpawnArrow(row, ref posX);
            if (recipe.Stations.Count > 0)
            {
                foreach (Part station in recipe.Stations)
                {
                    FatiDisplayItem cell = FatiDisplayItem.Create(row, baseUI, 32f);
                    var cellRect = (RectTransform)cell.transform;
                    cellRect.pivot = new Vector2(0f, 0.5f);
                    cellRect.anchoredPosition = new Vector2(posX, -52f);
                    cell.SetItem(station.item, station.quality);
                    posX += 18f;
                }
                posX += 16f;
            }

            foreach (KeyValuePair<Amount, List<Part>> result in recipe.Results)
            {
                if (recipe.Results.Count > 1 || result.Key.min != 1 || result.Key.max != 1 || Math.Abs(result.Key.chance - 1f) > 0.01f)
                    SpawnAmountLabel(row, result.Key, ref posX);
                foreach (Part part in result.Value)
                    SpawnPartCell(row, part, ref posX);
            }
            return row;
        }

        private void SpawnPartCell(RectTransform row, Part part, ref float posX)
        {
            FatiDisplayItem cell = FatiDisplayItem.Create(row, baseUI, 46f);
            var rect = (RectTransform)cell.transform;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(posX + 2f, -35f);
            cell.SetItem(part.item, part.quality);
            cell.SetCount(part.amount.ToString());
            posX += 50f;
        }

        private void SpawnAmountLabel(RectTransform row, Amount amount, ref float posX)
        {
            TextMeshProUGUI label = UIBuilderHelper.CreateLabel(row, "GroupAmount", amount.ToString(), 13f, Color.white,
                new Vector2(0f, 1f), new Vector2(0f, 1f), TextAlignmentOptions.Center);
            var rect = label.rectTransform;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(50f, 46f);
            rect.anchoredPosition = new Vector2(posX, -35f);
            posX += 50f;
        }

        private void SpawnArrow(RectTransform row, ref float posX)
        {
            TextMeshProUGUI arrow = UIBuilderHelper.CreateLabel(row, "Arrow", ">", 22f, new Color(1f, 0.72f, 0.23f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), TextAlignmentOptions.Center);
            var rect = arrow.rectTransform;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(28f, 46f);
            rect.anchoredPosition = new Vector2(posX + 2f, -35f);
            posX += 32f;
        }

        // ---- VNEI StationOrder ----

        private static int StationOrderByType(Item item)
        {
            if (item == Indexing.AllStations)
                return 10;
            if (item == Indexing.NoStation)
                return 15;
            if (item == Indexing.HandStation)
                return 20;
            if (item.prefab == null)
                return 100;
            if (item.prefab.GetComponent<CraftingStation>() != null)
                return 30;
            if (item.prefab.TryGetComponent(out ItemDrop itemDrop) && itemDrop.m_itemData.m_shared.m_buildPieces != null)
                return 40;
            if (item.prefab.GetComponent<Smelter>() != null)
                return 50;
            if (item.prefab.GetComponent<CookingStation>() != null)
                return 60;
            if (item.prefab.GetComponent<Fermenter>() != null)
                return 70;
            return item.prefab.GetComponent<Incinerator>() != null ? 80 : 100;
        }

        private static int StationOrderByName(Item item)
        {
            if (item.prefab == null)
                return 100 + item.preLocalizeName.Length;
            switch (item.prefab.name)
            {
                case "piece_workbench": return 10;
                case "forge": return 20;
                case "piece_stonecutter": return 30;
                case "piece_cauldron": return 40;
                case "piece_artisanstation": return 50;
                case "blackforge": return 60;
                case "piece_magetable": return 70;
                case "Hammer": return 10;
                case "Hoe": return 20;
                case "Cultivator": return 30;
                default: return 100 + item.prefab.name.Length;
            }
        }
    }
}
