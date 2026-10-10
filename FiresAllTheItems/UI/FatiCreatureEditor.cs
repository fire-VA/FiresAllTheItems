using System.Linq;
using FiresAllTheItems.Logic;
using FiresCore.Creatures;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static FiresAllTheItems.UI.FatiEditorWidgets;

namespace FiresAllTheItems.UI
{
    /// <summary>
    /// Per-creature rules editor laid over the recipe view: a master switch that hands the creature back to vanilla or
    /// its owning mod without losing the values, then Drops, Stars and Stats sections that each apply only when their
    /// own Override switch is on. Saves go through Core's <see cref="CreatureOverrides"/>, which the server re-checks
    /// for admin and pushes to every player. Loot is picked straight from the FATI item grid.
    /// </summary>
    public class FatiCreatureEditor : MonoBehaviour
    {
        private const float DropRowHeight = 32f;
        private const float IconSize = 28f;
        private const float SectionTitleIndent = 64f;
        private const float MinColumn = 136f;
        private const float MaxColumn = 224f;
        private const float ChanceColumn = 312f;
        private const float StarsTailInset = 84f;
        private const float OnePerPlayerTailInset = 36f;
        private const float ChanceStep = 0.05f;
        private const float MultiplierStep = 0.25f;
        private const float MinMultiplier = 0.25f;
        private const float MaxMultiplier = 100f;
        private const int VanillaHudStars = 2;

        private FatiBaseUI baseUI;
        private RectTransform content;
        private TextMeshProUGUI titleLabel;
        private TextMeshProUGUI statusLabel;
        private Item item;
        private CreatureOverride draft;
        private float submitDeadline = -1f;
        private float nextGateCheck;

        public bool IsOpen => gameObject.activeSelf;
        public Item EditedItem => item;

        public static bool CanEdit(Item target) =>
            target?.prefab != null && target.prefab.GetComponent<Character>() != null
            && target.prefab.GetComponent<Player>() == null && FatiDisplayItem.PlayerAllowsCheating();

        public static FatiCreatureEditor Create(RectTransform parent, FatiBaseUI baseUI)
        {
            Image backdrop = CreateBackdrop(parent, "CreatureEditor");
            var editor = backdrop.gameObject.AddComponent<FatiCreatureEditor>();
            editor.baseUI = baseUI;
            editor.titleLabel = AddLabel(backdrop.rectTransform, "", 10f, 4f, 360f, Gold, 16f);
            editor.statusLabel = AddLabel(backdrop.rectTransform, "", 376f, 4f, 120f, Muted, CaptionFontSize);
            AddButton(backdrop.rectTransform, "Done", 6f, 4f, 62f, editor.Close, fromRight: true);
            editor.content = CreateCardScroll(backdrop.rectTransform);
            backdrop.gameObject.SetActive(false);
            return editor;
        }

        public void Open(Item target)
        {
            item = target;
            ReloadDraft();
            gameObject.SetActive(true);
            Rebuild();
        }

        public void Close() => gameObject.SetActive(false);

        public void OnRulesChanged()
        {
            if (IsOpen && submitDeadline < 0f)
                Rebuild();
        }

        public void OnSubmitResult(bool ok)
        {
            if (submitDeadline < 0f)
                return;
            submitDeadline = -1f;
            statusLabel.text = "";
            if (ok)
                ReloadDraft();
            if (IsOpen)
                Rebuild();
        }

        private void Update()
        {
            if (submitDeadline > 0f && Time.time > submitDeadline)
            {
                submitDeadline = -1f;
                statusLabel.text = "";
                Message("no answer from the server. It needs FiresUnifiedCore 0.2.31 or newer, and edits are admin-only.");
            }
            if (Time.time < nextGateCheck)
                return;
            nextGateCheck = Time.time + GateCheckSeconds;
            if (!CanEdit(item))
                Close();
        }

        private bool HasSavedRules => CreatureOverrides.TryGet(item.internalName, out _);

        private bool HasLootComponent => item.prefab.GetComponent<CharacterDrop>() != null;

        private float VanillaHealth => item.prefab != null && item.prefab.TryGetComponent(out Character character) ? character.m_health : 0f;

        private void ReloadDraft()
        {
            draft = CreatureOverrides.TryGet(item.internalName, out CreatureOverride saved)
                ? saved.Clone()
                : CreatureOverrides.DescribeDefaults(item.prefab);
        }

        private void Rebuild()
        {
            float scrollY = content.anchoredPosition.y;
            foreach (Transform child in content)
                Destroy(child.gameObject);

            titleLabel.text = "Creature rules for " + item.GetPrimaryName();
            float bottom = AddMasterCard(0f) + CardGap;
            bottom = AddDropsCard(bottom) + CardGap;
            bottom = AddStarsCard(bottom) + CardGap;
            bottom = AddStatsCard(bottom) + CardGap;
            bottom = AddSaveRow(bottom);

            content.sizeDelta = new Vector2(0f, bottom);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, scrollY);
        }

        private float AddMasterCard(float top)
        {
            RectTransform card = AddCard(content);
            float y = CardInset;
            AddSwitch(card, draft.Enabled ? "On" : "Off", draft.Enabled, CardInset, y, 48f, ToggleMaster);
            string state = HasSavedRules ? (draft.Enabled ? "rules active" : "rules off, back to vanilla") : "no saved rules";
            AddLabel(card, $"Rules<size={CaptionFontSize}><color={MutedHex}>  ·  {state}</color></size>", SectionTitleIndent, y, 330f, Gold);
            if (HasSavedRules)
                AddButton(card, "Reset to original", CardInset, y, 130f, RemoveSavedRules, fromRight: true);
            y += LineHeight;
            AddStretchedLabel(card, "Off hands this creature back to vanilla or its owning mod and keeps your values for later.",
                SectionTitleIndent, y, Muted);
            return FinishCard(card, top, y + SmallButtonHeight);
        }

        private float AddDropsCard(float top)
        {
            RectTransform card = AddCard(content);
            float y = CardInset;
            AddSectionHeader(card, "Drops", draft.OverrideDrops, () => { draft.OverrideDrops = !draft.OverrideDrops; Rebuild(); }, y);
            y += LineHeight;

            if (!HasLootComponent)
            {
                AddStretchedLabel(card, "This creature has no loot component, so drops can't be added to it.", SectionTitleIndent, y, Muted);
                return FinishCard(card, top, y + SmallButtonHeight);
            }
            if (!draft.OverrideDrops)
            {
                AddStretchedLabel(card, "Uses its own loot: " + SummarizeDrops(CreatureOverrides.DescribeDefaults(item.prefab)),
                    SectionTitleIndent, y, Muted);
                return FinishCard(card, top, y + SmallButtonHeight);
            }

            AddLabel(card, "Item", CardInset, y, 150f, Muted, CaptionFontSize);
            AddLabel(card, "Min", MinColumn, y, StepperWidth, Muted, CaptionFontSize, TextAlignmentOptions.Midline);
            AddLabel(card, "Max", MaxColumn, y, StepperWidth, Muted, CaptionFontSize, TextAlignmentOptions.Midline);
            AddLabel(card, "Chance", ChanceColumn, y, StepperWidth, Muted, CaptionFontSize, TextAlignmentOptions.Midline);
            y += SmallButtonHeight - 4f;
            for (int index = 0; index < draft.Drops.Count; ++index)
                y = AddDropRow(card, draft.Drops[index], index, y);
            AddButton(card, "+ Add drop", CardInset, y, 110f, () => PickDrop(-1));
            return FinishCard(card, top, y + SmallButtonHeight);
        }

        private float AddDropRow(RectTransform card, CreatureDropOverride drop, int index, float y)
        {
            Item loot = Indexing.GetItem(drop.Item);
            AddIcon(card, loot?.GetIcon(), CardInset, y, IconSize, () => PickDrop(index));
            AddLabel(card, loot?.GetPrimaryName() ?? drop.Item, CardInset + IconSize + 6f, y + 3f, 92f, Ink);
            AddIntStepper(card, MinColumn, y + 3f, drop.AmountMin, 0, 9999,
                value => { drop.AmountMin = value; drop.AmountMax = Mathf.Max(drop.AmountMax, value); Rebuild(); });
            AddIntStepper(card, MaxColumn, y + 3f, drop.AmountMax, 0, 9999,
                value => { drop.AmountMax = value; drop.AmountMin = Mathf.Min(drop.AmountMin, value); Rebuild(); });
            AddFloatStepper(card, ChanceColumn, y + 3f, drop.Chance, ChanceStep, 0f, 1f, Percent, value => { drop.Chance = value; Rebuild(); });
            AddSwitch(card, "★", drop.LevelMultiplier, StarsTailInset, y + 3f, 44f,
                () => { drop.LevelMultiplier = !drop.LevelMultiplier; Rebuild(); }, fromRight: true);
            AddSwitch(card, "1/pl", drop.OnePerPlayer, OnePerPlayerTailInset, y + 3f, 40f,
                () => { drop.OnePerPlayer = !drop.OnePerPlayer; Rebuild(); }, fromRight: true);
            AddButton(card, "X", CardInset, y + 3f, 24f, () => { draft.Drops.Remove(drop); Rebuild(); }, fromRight: true);
            return y + DropRowHeight;
        }

        private float AddStarsCard(float top)
        {
            RectTransform card = AddCard(content);
            float y = CardInset;
            AddSectionHeader(card, "Stars", draft.OverrideStars, () => { draft.OverrideStars = !draft.OverrideStars; Rebuild(); }, y);
            y += LineHeight;
            if (!draft.OverrideStars)
            {
                AddStretchedLabel(card, "Spawners and spawn mods pick its level as usual.", SectionTitleIndent, y, Muted);
                return FinishCard(card, top, y + SmallButtonHeight);
            }

            AddLabel(card, "Min stars", CardInset, y, 70f, Muted);
            AddIntStepper(card, 80f, y, draft.MinStars, 0, CreatureOverrides.MaxStars,
                value => { draft.MinStars = value; draft.MaxStars = Mathf.Max(draft.MaxStars, value); Rebuild(); });
            AddLabel(card, "Max stars", 200f, y, 70f, Muted);
            AddIntStepper(card, 272f, y, draft.MaxStars, 0, CreatureOverrides.MaxStars,
                value => { draft.MaxStars = value; draft.MinStars = Mathf.Min(draft.MinStars, value); Rebuild(); });
            y += LineHeight;
            AddLabel(card, "Chance per extra star", CardInset, y, 150f, Muted);
            AddFloatStepper(card, 160f, y, draft.StarChance, ChanceStep, 0f, 1f, Percent, value => { draft.StarChance = value; Rebuild(); });
            y += LineHeight;
            string note = "Overrides the level any spawner or spawn mod picks, for new spawns.";
            if (draft.MaxStars > VanillaHudStars)
                note += $" Vanilla's health bar only draws {VanillaHudStars} stars; higher levels still count.";
            AddStretchedLabel(card, note, CardInset, y, Muted);
            return FinishCard(card, top, y + SmallButtonHeight);
        }

        private float AddStatsCard(float top)
        {
            RectTransform card = AddCard(content);
            float y = CardInset;
            AddSectionHeader(card, "Health and damage", draft.OverrideStats, () => { draft.OverrideStats = !draft.OverrideStats; Rebuild(); }, y);
            y += LineHeight;
            if (!draft.OverrideStats)
            {
                AddStretchedLabel(card, "Vanilla health and damage, scaled by stars as usual.", SectionTitleIndent, y, Muted);
                return FinishCard(card, top, y + SmallButtonHeight);
            }

            float vanillaHealth = VanillaHealth;
            int health = Mathf.RoundToInt(draft.HealthValue > 0f ? draft.HealthValue : vanillaHealth);
            AddLabel(card, "Health", CardInset, y, 56f, Muted);
            AddIntStepper(card, 64f, y, health, 1, 99999,
                value => { draft.HealthValue = value; Rebuild(); });
            AddLabel(card, vanillaHealth > 0f ? $"default {Mathf.RoundToInt(vanillaHealth)}" : "",
                64f + StepperWidth + 8f, y, 96f, Muted, CaptionFontSize);
            if (draft.HealthValue > 0f && !Mathf.Approximately(draft.HealthValue, vanillaHealth))
                AddButton(card, "Reset", 64f + StepperWidth + 108f, y, 56f, () => { draft.HealthValue = vanillaHealth; Rebuild(); });
            y += LineHeight;

            AddLabel(card, "Per star", CardInset, y, 56f, Muted);
            AddLabel(card, "health", 64f, y, 48f, Muted, CaptionFontSize);
            AddFloatStepper(card, 112f, y, draft.HealthPerStar, MultiplierStep, 1f, MaxMultiplier, Times,
                value => { draft.HealthPerStar = value; Rebuild(); });
            AddLabel(card, "damage", 112f + StepperWidth + 10f, y, 52f, Muted, CaptionFontSize);
            AddFloatStepper(card, 112f + StepperWidth + 64f, y, draft.DamagePerStar, MultiplierStep, 1f, MaxMultiplier, Times,
                value => { draft.DamagePerStar = value; Rebuild(); });
            y += LineHeight;

            int oneStar = Mathf.RoundToInt(health * draft.HealthPerStar);
            int twoStar = Mathf.RoundToInt(health * (1f + 2f * (draft.HealthPerStar - 1f)));
            AddStretchedLabel(card, $"A 0-star has {health} health; one star {oneStar}, two {twoStar}. "
                + "Vanilla is x2 health and x1.5 damage per star. Shift-click steps by ten.", CardInset, y, Muted);
            return FinishCard(card, top, y + SmallButtonHeight);
        }

        private float AddSaveRow(float top)
        {
            AddButton(content, "Save for everyone", CardInset, top, 140f, SaveDraft, fromRight: true);
            AddButton(content, "Cancel", CardInset + 146f, top, 70f, () => { ReloadDraft(); Rebuild(); }, fromRight: true);
            return top + SmallButtonHeight + CardInset;
        }

        private static void AddSectionHeader(RectTransform card, string title, bool on, System.Action toggle, float y)
        {
            AddSwitch(card, on ? "Override" : "Default", on, CardInset, y, 74f, toggle);
            AddLabel(card, title, 90f, y, 300f, Gold);
        }

        private void ToggleMaster()
        {
            draft.Enabled = !draft.Enabled;
            if (!CreatureOverrides.TryGet(item.internalName, out CreatureOverride saved))
            {
                Rebuild();
                return;
            }
            CreatureOverride flipped = saved.Clone();
            flipped.Enabled = draft.Enabled;
            Send(flipped);
        }

        private void SaveDraft() => Send(draft.Clone());

        private void RemoveSavedRules()
        {
            BeginWaiting();
            CreatureOverrides.Remove(item.internalName);
        }

        private void Send(CreatureOverride entry)
        {
            BeginWaiting();
            CreatureOverrides.Submit(entry);
        }

        private void BeginWaiting()
        {
            submitDeadline = Time.time + SubmitTimeoutSeconds;
            statusLabel.text = "Saving…";
        }

        private void PickDrop(int replaceIndex)
        {
            string prompt = replaceIndex < 0
                ? "Click an item to add it as a drop (Esc cancels)"
                : $"Click an item to replace {DisplayName(draft.Drops[replaceIndex].Item)} (Esc cancels)";
            baseUI.BeginPick(prompt, picked =>
            {
                if (draft == null)
                    return;
                if (picked.prefab == null || picked.prefab.GetComponent<ItemDrop>() == null)
                {
                    Message($"{picked.GetPrimaryName()} isn't an item, so it can't drop.");
                    return;
                }
                if (replaceIndex < 0)
                    draft.Drops.Add(new CreatureDropOverride { Item = picked.internalName });
                else if (replaceIndex < draft.Drops.Count)
                    draft.Drops[replaceIndex].Item = picked.internalName;
                Rebuild();
            });
        }

        private static string SummarizeDrops(CreatureOverride entry)
        {
            if (entry.Drops.Count == 0)
                return "nothing";
            return string.Join(", ", entry.Drops.Select(drop =>
                $"{DisplayName(drop.Item)} {drop.AmountMin}–{drop.AmountMax} @ {Percent(drop.Chance)}"));
        }

        private static string Percent(float chance) => Mathf.RoundToInt(chance * 100f) + "%";

        private static string Times(float multiplier) => "×" + multiplier.ToString("0.##");

        private static string DisplayName(string prefab) => Indexing.GetItem(prefab)?.GetPrimaryName() ?? prefab;
    }
}
