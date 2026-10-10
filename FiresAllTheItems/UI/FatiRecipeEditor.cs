using System;
using System.Collections.Generic;
using System.Linq;
using FiresAllTheItems.Logic;
using FiresCore.Recipes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static FiresAllTheItems.UI.FatiEditorWidgets;

namespace FiresAllTheItems.UI
{
    /// <summary>
    /// Recipe editor laid over the recipe view's lower half for the current item: every ObjectDB recipe that produces
    /// it (disabled ones included) with an on/off toggle, full editing of ingredients, station and output, reset to
    /// original, and custom recipes. Saves go through Core's <see cref="RecipeOverrides"/>, which the server re-checks
    /// for admin and pushes to every player. Ingredients are picked straight from the FATI item grid.
    /// </summary>
    public class FatiRecipeEditor : MonoBehaviour
    {
        private const float IngredientRowHeight = 32f;
        private const float IconSize = 28f;
        private const float SummaryIndent = 64f;
        private const float AmountColumn = 170f;
        private const float PerLevelColumn = 290f;
        private const float UpgradeColumn = 410f;
        private const string DefaultCustomStation = "piece_workbench";

        private sealed class StationChoice
        {
            public string Prefab;
            public string Label;
        }

        private FatiBaseUI baseUI;
        private RectTransform content;
        private TextMeshProUGUI titleLabel;
        private TextMeshProUGUI statusLabel;
        private Item item;
        private string openName;
        private RecipeOverride draft;
        private bool draftIsNew;
        private bool awaitingDraftSave;
        private float submitDeadline = -1f;
        private float nextGateCheck;
        private List<StationChoice> stationChoices;

        public bool IsOpen => gameObject.activeSelf;
        public Item EditedItem => item;

        public static bool CanEdit(Item target) =>
            target?.prefab != null && target.prefab.GetComponent<ItemDrop>() != null && FatiDisplayItem.PlayerAllowsCheating();

        public static FatiRecipeEditor Create(RectTransform parent, FatiBaseUI baseUI)
        {
            Image backdrop = CreateBackdrop(parent, "RecipeEditor");
            var editor = backdrop.gameObject.AddComponent<FatiRecipeEditor>();
            editor.baseUI = baseUI;
            editor.titleLabel = AddLabel(backdrop.rectTransform, "", 10f, 4f, 300f, Gold, 16f);
            editor.statusLabel = AddLabel(backdrop.rectTransform, "", 316f, 4f, 120f, Muted, CaptionFontSize);
            AddButton(backdrop.rectTransform, "Done", 6f, 4f, 62f, editor.Close, fromRight: true);
            AddButton(backdrop.rectTransform, "New recipe", 74f, 4f, 100f, editor.StartNewRecipe, fromRight: true);
            editor.content = CreateCardScroll(backdrop.rectTransform);
            backdrop.gameObject.SetActive(false);
            return editor;
        }

        public void Open(Item target)
        {
            item = target;
            CloseDraft();
            gameObject.SetActive(true);
            Rebuild();
        }

        public void Close()
        {
            CloseDraft();
            gameObject.SetActive(false);
        }

        public void OnRecipesChanged()
        {
            if (IsOpen)
                Rebuild();
        }

        public void OnSubmitResult(bool ok)
        {
            if (submitDeadline < 0f)
                return;
            submitDeadline = -1f;
            statusLabel.text = "";
            if (ok && awaitingDraftSave)
                CloseDraft();
            awaitingDraftSave = false;
            if (IsOpen)
                Rebuild();
        }

        private void Update()
        {
            if (submitDeadline > 0f && Time.time > submitDeadline)
            {
                submitDeadline = -1f;
                awaitingDraftSave = false;
                statusLabel.text = "";
                Message("no answer from the server. It needs FiresUnifiedCore 0.2.31 or newer, and edits are admin-only.");
            }
            if (Time.time < nextGateCheck)
                return;
            nextGateCheck = Time.time + GateCheckSeconds;
            if (!CanEdit(item))
                Close();
        }

        private void CloseDraft()
        {
            openName = null;
            draft = null;
            draftIsNew = false;
        }

        private void Rebuild()
        {
            float scrollY = content.anchoredPosition.y;
            foreach (Transform child in content)
                Destroy(child.gameObject);

            titleLabel.text = "Recipes for " + item.GetPrimaryName();
            float bottom = 0f;
            List<Recipe> recipes = RecipesForItem();
            foreach (Recipe recipe in recipes)
                bottom = AddRecipeCard(recipe.name, recipe, bottom) + CardGap;
            if (draftIsNew && recipes.All(recipe => recipe.name != draft.Name))
                bottom = AddRecipeCard(draft.Name, null, bottom) + CardGap;
            if (bottom <= 0f)
            {
                AddLabel(content, "No recipe makes this item yet. Use New recipe to add one.", CardInset, CardInset, 500f, Muted);
                bottom = SmallButtonHeight + 2f * CardInset;
            }

            content.sizeDelta = new Vector2(0f, bottom);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, scrollY);
        }

        private List<Recipe> RecipesForItem()
        {
            if (ObjectDB.instance == null || item == null)
                return new List<Recipe>();
            return ObjectDB.instance.m_recipes
                .Where(recipe => recipe != null && recipe.m_item != null && !string.IsNullOrEmpty(recipe.name)
                    && recipe.m_item.name == item.internalName)
                .ToList();
        }

        private float AddRecipeCard(string name, Recipe recipe, float top)
        {
            bool open = openName == name && draft != null;
            RecipeOverride shown = open ? draft : RecipeOverrides.Describe(recipe);
            RectTransform card = AddCard(content);

            float y = CardInset;
            AddSwitch(card, shown.Enabled ? "On" : "Off", shown.Enabled, CardInset, y, 48f, () => ToggleEnabled(recipe, open));
            AddLabel(card, $"{name}<size={CaptionFontSize}><color={MutedHex}>  ·  {TagFor(name, recipe)}</color></size>",
                SummaryIndent, y, 330f, Gold);
            if (!open)
            {
                AddButton(card, "Edit", CardInset, y, 52f, () => OpenDraft(recipe), fromRight: true);
                if (RecipeOverrides.IsCustomName(name))
                    AddButton(card, "Delete", CardInset + 58f, y, 60f, () => RemoveSaved(name), fromRight: true);
                else if (RecipeOverrides.TryGet(name, out _))
                    AddButton(card, "Reset", CardInset + 58f, y, 60f, () => RemoveSaved(name), fromRight: true);
            }
            y += LineHeight;

            if (open)
                y = AddDraftEditor(card, recipe, y);
            else
            {
                string summary = Summarize(shown);
                if (!shown.Enabled)
                    summary = $"<color={MutedHex}>(off, hidden from crafting)</color>  " + summary;
                AddStretchedLabel(card, summary, SummaryIndent, y, shown.Enabled ? Ink : Muted);
                y += SmallButtonHeight;
            }
            return FinishCard(card, top, y);
        }

        private float AddDraftEditor(RectTransform card, Recipe recipe, float y)
        {
            AddLabel(card, "Ingredient", CardInset, y, 150f, Muted, CaptionFontSize);
            AddLabel(card, "Amount", AmountColumn, y, StepperWidth, Muted, CaptionFontSize, TextAlignmentOptions.Midline);
            AddLabel(card, "Per level", PerLevelColumn, y, StepperWidth, Muted, CaptionFontSize, TextAlignmentOptions.Midline);
            y += SmallButtonHeight - 4f;

            for (int index = 0; index < draft.Resources.Count; ++index)
                y = AddIngredientRow(card, draft.Resources[index], index, y);

            AddButton(card, "+ Add ingredient", CardInset, y, 150f, () => PickIngredient(-1));
            y += LineHeight + 6f;

            AddLabel(card, "Station", CardInset, y, 60f, Muted);
            AddButton(card, "<", 70f, y, StepButtonWidth, () => CycleStation(-1));
            AddLabel(card, StationLabel(draft.CraftingStation), 94f, y, 150f, Ink, LabelFontSize, TextAlignmentOptions.Midline);
            AddButton(card, ">", 246f, y, StepButtonWidth, () => CycleStation(1));
            y += LineHeight;

            AddLabel(card, "Min level", CardInset, y, 70f, Muted);
            AddIntStepper(card, 80f, y, draft.MinStationLevel, 1, 20, value => { draft.MinStationLevel = value; Rebuild(); });
            AddLabel(card, "Makes", 190f, y, 50f, Muted);
            AddIntStepper(card, 244f, y, draft.Amount, 1, 9999, value => { draft.Amount = value; Rebuild(); });
            y += LineHeight + 6f;

            if (recipe != null && !RecipeOverrides.IsCustomName(recipe.name) && RecipeOverrides.TryGet(recipe.name, out _))
                AddButton(card, "Reset to original", CardInset, y, 130f, () => RemoveSaved(recipe.name));
            AddButton(card, "Save for everyone", CardInset, y, 140f, SaveDraft, fromRight: true);
            AddButton(card, "Cancel", CardInset + 146f, y, 70f, () => { CloseDraft(); Rebuild(); }, fromRight: true);
            return y + SmallButtonHeight;
        }

        private float AddIngredientRow(RectTransform card, RecipeOverrideResource resource, int index, float y)
        {
            Item ingredient = Indexing.GetItem(resource.Item);
            AddIcon(card, ingredient?.GetIcon(), CardInset, y, IconSize, () => PickIngredient(index));
            AddLabel(card, ingredient?.GetPrimaryName() ?? resource.Item, CardInset + IconSize + 6f, y + 3f, 122f, Ink);
            AddIntStepper(card, AmountColumn, y + 3f, resource.Amount, 0, 9999, value => { resource.Amount = value; Rebuild(); });
            AddIntStepper(card, PerLevelColumn, y + 3f, resource.AmountPerLevel, 0, 9999, value => { resource.AmountPerLevel = value; Rebuild(); });
            AddSwitch(card, "Upgrade", resource.UpgraderResource, UpgradeColumn, y + 3f, 66f,
                () => { resource.UpgraderResource = !resource.UpgraderResource; Rebuild(); });
            AddButton(card, "X", CardInset, y + 3f, 24f, () => { draft.Resources.Remove(resource); Rebuild(); }, fromRight: true);
            return y + IngredientRowHeight;
        }

        private void ToggleEnabled(Recipe recipe, bool open)
        {
            if (open)
            {
                draft.Enabled = !draft.Enabled;
                Rebuild();
                return;
            }
            RecipeOverride entry = RecipeOverrides.Describe(recipe);
            entry.Enabled = !entry.Enabled;
            Send(entry, isDraftSave: false);
        }

        private void OpenDraft(Recipe recipe)
        {
            openName = recipe.name;
            draft = RecipeOverrides.Describe(recipe);
            draftIsNew = false;
            Rebuild();
        }

        private void StartNewRecipe()
        {
            string name = RecipeOverrides.NewCustomName(item.internalName);
            openName = name;
            draft = new RecipeOverride
            {
                Name = name,
                Item = item.internalName,
                IsCustom = true,
                CraftingStation = StationChoices().Any(choice => choice.Prefab == DefaultCustomStation) ? DefaultCustomStation : "",
            };
            draftIsNew = true;
            Rebuild();
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Max(0f, content.sizeDelta.y));
        }

        private void SaveDraft()
        {
            if (draft != null)
                Send(draft.Clone(), isDraftSave: true);
        }

        private void RemoveSaved(string recipeName)
        {
            if (openName == recipeName)
                CloseDraft();
            BeginWaiting(isDraftSave: false);
            RecipeOverrides.Remove(recipeName);
        }

        private void Send(RecipeOverride entry, bool isDraftSave)
        {
            BeginWaiting(isDraftSave);
            RecipeOverrides.Submit(entry);
        }

        private void BeginWaiting(bool isDraftSave)
        {
            awaitingDraftSave = isDraftSave;
            submitDeadline = Time.time + SubmitTimeoutSeconds;
            statusLabel.text = "Saving…";
        }

        private void PickIngredient(int replaceIndex)
        {
            string prompt = replaceIndex < 0
                ? "Click an item to add it as an ingredient (Esc cancels)"
                : $"Click an item to replace {DisplayName(draft.Resources[replaceIndex].Item)} (Esc cancels)";
            baseUI.BeginPick(prompt, picked =>
            {
                if (draft == null)
                    return;
                if (picked.prefab == null || picked.prefab.GetComponent<ItemDrop>() == null)
                {
                    Message($"{picked.GetPrimaryName()} isn't an item, so it can't be an ingredient.");
                    return;
                }
                if (replaceIndex < 0)
                    draft.Resources.Add(new RecipeOverrideResource { Item = picked.internalName });
                else if (replaceIndex < draft.Resources.Count)
                    draft.Resources[replaceIndex].Item = picked.internalName;
                Rebuild();
            });
        }

        private void CycleStation(int direction)
        {
            List<StationChoice> choices = StationChoices();
            int current = Mathf.Max(0, choices.FindIndex(choice => choice.Prefab == draft.CraftingStation));
            draft.CraftingStation = choices[(current + direction + choices.Count) % choices.Count].Prefab;
            Rebuild();
        }

        private List<StationChoice> StationChoices()
        {
            if (stationChoices != null)
                return stationChoices;
            stationChoices = new List<StationChoice> { new StationChoice { Prefab = "", Label = "None (hand)" } };
            if (ZNetScene.instance == null)
                return stationChoices;
            stationChoices.AddRange(ZNetScene.instance.m_prefabs
                .Where(prefab => prefab != null && prefab.GetComponent<CraftingStation>() != null)
                .GroupBy(prefab => prefab.name)
                .Select(group => new StationChoice
                {
                    Prefab = group.Key,
                    Label = Localization.instance.Localize(group.First().GetComponent<CraftingStation>().m_name),
                })
                .OrderBy(choice => choice.Label, StringComparer.OrdinalIgnoreCase));
            return stationChoices;
        }

        private string StationLabel(string prefab)
        {
            if (string.IsNullOrEmpty(prefab))
                return StationChoices()[0].Label;
            return StationChoices().FirstOrDefault(choice => choice.Prefab == prefab)?.Label ?? prefab;
        }

        private string Summarize(RecipeOverride entry)
        {
            string ingredients = string.Join(", ", entry.Resources.Where(resource => !resource.UpgraderResource)
                .Select(resource => $"{DisplayName(resource.Item)} ×{resource.Amount}"));
            if (ingredients.Length == 0)
                ingredients = "no ingredients";
            string station = string.IsNullOrEmpty(entry.CraftingStation)
                ? "hand craft"
                : $"{StationLabel(entry.CraftingStation)} {entry.MinStationLevel}";
            string text = $"{ingredients}  →  ×{entry.Amount}  ·  {station}";
            List<string> upgrades = entry.Resources.Where(resource => resource.UpgraderResource)
                .Select(resource => DisplayName(resource.Item)).ToList();
            if (upgrades.Count > 0)
                text += "  ·  upgrade: " + string.Join(", ", upgrades);
            return text;
        }

        private static string TagFor(string name, Recipe recipe)
        {
            if (recipe == null)
                return "new, unsaved";
            if (RecipeOverrides.IsCustomName(name))
                return "custom";
            return RecipeOverrides.TryGet(name, out _) ? "edited" : "original";
        }

        private static string DisplayName(string prefab) => Indexing.GetItem(prefab)?.GetPrimaryName() ?? prefab;
    }
}
