using System.Collections.Generic;
using FiresAllTheItems.Logic;
using FiresCore.Bridge;
using UnityEngine.UI;
using TMPro;
using UnityEngine;
using static FiresAllTheItems.UI.FatiEditorWidgets;

namespace FiresAllTheItems.UI
{
    /// <summary>
    /// Editor for one monster preset, the Presets half of the grid's Creatures/Presets switch. A preset belongs
    /// to FiresAdminPrefabs; everything here goes through Core's <see cref="MonsterPresetBridge"/>, and one Save
    /// is the whole chain - the owner writes its yaml, pushes it and regenerates its rules, so nothing is
    /// sequenced from this side.
    ///
    /// A preset is LITERAL. Its drops do not scale with stars or world level, and per-species creature rules are
    /// skipped for preset instances, so this editor never borrows the Creatures tab's multipliers.
    /// </summary>
    public class FatiPresetEditor : MonoBehaviour
    {
        private const float RowHeight = 32f;
        private const float IconSize = 28f;
        private const float NameColumn = 44f;
        private const float ChanceColumn = 300f;
        private const float StackColumn = 400f;
        private const float SectionTitleIndent = 64f;
        private const float RespawnStep = 15f;
        private const float MaxRespawn = 86400f;
        private const float ChanceStep = 0.05f;
        private const int MaxStack = 9999;

        private FatiBaseUI baseUI;
        private RectTransform content;
        private TextMeshProUGUI titleLabel;
        private TextMeshProUGUI statusLabel;
        private Item item;
        private MonsterPresetRequest draft;
        private float nextGateCheck;

        public bool IsOpen => gameObject.activeSelf;
        public Item EditedItem => item;

        public static bool CanEdit(Item target) =>
            target != null && !string.IsNullOrEmpty(target.presetName) && MonsterPresetBridge.IsAvailable;

        private static bool CanWrite => FatiDisplayItem.PlayerAllowsCheating();

        public static FatiPresetEditor Create(RectTransform parent, FatiBaseUI baseUI)
        {
            Image backdrop = CreateBackdrop(parent, "PresetEditor");
            var editor = backdrop.gameObject.AddComponent<FatiPresetEditor>();
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

        public void Close()
        {
            if (baseUI != null && baseUI.IsPicking)
                baseUI.CancelPick();
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (Time.time < nextGateCheck)
                return;
            nextGateCheck = Time.time + GateCheckSeconds;
            if (!CanEdit(item))
                Close();
        }

        private void ReloadDraft()
        {
            draft = MonsterPresetBridge.TryGet(item.presetName, out MonsterPresetRequest stored) ? stored : null;
            statusLabel.text = "";
        }

        private void Rebuild()
        {
            float scrollY = content.anchoredPosition.y;
            foreach (Transform child in content)
                Destroy(child.gameObject);

            titleLabel.text = "Preset " + item.presetName;
            if (draft == null)
            {
                AddStretchedLabel(content, "This preset could not be read back from FiresAdminPrefabs.",
                    CardInset, 0f, Muted);
                content.sizeDelta = new Vector2(0f, LineHeight * 2f);
                return;
            }

            float bottom = AddPresetCard(0f) + CardGap;
            bottom = AddDropsCard(bottom) + CardGap;
            bottom = AddSaveRow(bottom);

            content.sizeDelta = new Vector2(0f, bottom);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, scrollY);
        }

        private float AddPresetCard(float top)
        {
            RectTransform card = AddCard(content);
            float y = CardInset;

            AddLabel(card, "Preset", CardInset, y, 200f, Gold);
            y += LineHeight;

            string target = string.IsNullOrEmpty(draft.Creature) ? "every creature (no prefab named)" : draft.Creature;
            AddLabel(card, "Creature", CardInset, y, 90f, Muted, LabelFontSize);
            AddLabel(card, target, NameColumn + 56f, y, 300f, Ink, LabelFontSize);
            y += LineHeight;

            AddLabel(card, "Respawn", CardInset, y, 90f, Muted, LabelFontSize);
            AddFloatStepper(card, NameColumn + 56f, y, draft.RespawnSeconds, RespawnStep, 0f, MaxRespawn,
                value => value <= 0f ? "off" : Mathf.RoundToInt(value) + "s",
                value => { draft.RespawnSeconds = value; Rebuild(); });
            y += LineHeight;

            ParseRange(draft.ItemAmount, out int pickMin, out int pickMax, out bool picking);
            AddSwitch(card, picking ? "Pick" : "All rows", picking, CardInset, y, 74f, () =>
            {
                draft.ItemAmount = picking ? null : "1";
                Rebuild();
            });
            if (picking)
            {
                AddIntStepper(card, NameColumn + 56f, y, pickMin, 0, MaxStack,
                    value => { draft.ItemAmount = FormatRange(value, Mathf.Max(value, pickMax)); Rebuild(); });
                AddIntStepper(card, NameColumn + 56f + StepperWidth + 10f, y, pickMax, pickMin, MaxStack,
                    value => { draft.ItemAmount = FormatRange(pickMin, value); Rebuild(); });
            }
            else
            {
                AddLabel(card, "every row rolls its own chance", NameColumn + 56f, y, 280f, Muted, CaptionFontSize);
            }
            y += LineHeight;

            return FinishCard(card, top, y);
        }

        private float AddDropsCard(float top)
        {
            RectTransform card = AddCard(content);
            float y = CardInset;

            AddLabel(card, "Drops", CardInset, y, 200f, Gold);
            AddButton(card, "Add drop", CardInset, y, 84f, BeginAddDrop, fromRight: true);
            y += LineHeight;

            AddStretchedLabel(card, "Literal: these roll exactly as set, with no star or world-level scaling.",
                SectionTitleIndent, y, Muted);
            y += LineHeight;

            if (draft.Items == null)
                draft.Items = new List<MonsterPresetItem>();
            if (draft.Items.Count == 0)
            {
                AddStretchedLabel(card, "No drops. The creature drops nothing when it dies.", SectionTitleIndent, y, Muted);
                y += LineHeight;
            }
            else
            {
                for (int index = 0; index < draft.Items.Count; ++index)
                    y = AddDropRow(card, draft.Items[index], index, y);
            }

            return FinishCard(card, top, y);
        }

        private float AddDropRow(RectTransform card, MonsterPresetItem drop, int index, float y)
        {
            Item dropItem = Indexing.GetItem(drop.Prefab);
            AddIcon(card, dropItem != null ? dropItem.GetIcon() : FatiSprites.NoIcon, CardInset, y, IconSize,
                () => { if (dropItem != null) baseUI.ShowRecipe(dropItem); });
            AddLabel(card, dropItem != null ? dropItem.GetPrimaryName() : drop.Prefab,
                NameColumn, y + 4f, ChanceColumn - NameColumn - 8f, Ink, LabelFontSize);

            AddFloatStepper(card, ChanceColumn, y + 4f, Mathf.Clamp01(drop.Chance), ChanceStep, 0f, 1f,
                value => Mathf.RoundToInt(value * 100f) + "%",
                value => { drop.Chance = value; Rebuild(); });

            ParseRange(drop.Stack, out int min, out int max, out bool _);
            AddIntStepper(card, StackColumn, y + 4f, min, 1, MaxStack,
                value => { drop.Stack = FormatRange(value, Mathf.Max(value, max)); Rebuild(); });
            AddIntStepper(card, StackColumn + StepperWidth + 8f, y + 4f, max, min, MaxStack,
                value => { drop.Stack = FormatRange(min, value); Rebuild(); });

            AddButton(card, "x", CardInset, y + 4f, 24f, () => { draft.Items.RemoveAt(index); Rebuild(); }, fromRight: true);
            return y + RowHeight;
        }

        private float AddSaveRow(float top)
        {
            RectTransform card = AddCard(content);
            float y = CardInset;

            if (!CanWrite)
            {
                AddStretchedLabel(card, "Read-only: saving a preset needs devcommands and admin.", CardInset, y, Muted);
                return FinishCard(card, top, y + LineHeight);
            }

            AddButton(card, "Delete preset", CardInset, y, 110f, DeletePreset);
            AddButton(card, "Save for everyone", CardInset, y, 140f, SavePreset, fromRight: true);
            return FinishCard(card, top, y + LineHeight);
        }

        private void BeginAddDrop()
        {
            if (!CanWrite)
            {
                Message("adding a drop needs devcommands and admin.");
                return;
            }
            baseUI.BeginPick("Pick the item this preset drops", picked =>
            {
                draft.Items.Add(new MonsterPresetItem { Prefab = picked.internalName, Chance = 1f, Stack = "1" });
                Rebuild();
            });
        }

        private void SavePreset()
        {
            // The same object TryGet handed back, so the owner's untouched keys round-trip unchanged.
            if (MonsterPresetBridge.Save(draft))
            {
                statusLabel.text = "saved";
                baseUI.RefreshPresets();
            }
            else
            {
                statusLabel.text = "refused";
                Message("the preset was refused. Saving is admin-only, and FiresAdminPrefabs logs why.");
            }
        }

        private void DeletePreset()
        {
            if (!MonsterPresetBridge.Delete(item.presetName))
            {
                Message("the delete was refused. Deleting is admin-only, and FiresAdminPrefabs logs why.");
                return;
            }
            baseUI.RefreshPresets();
            Close();
        }

        /// <summary>Reads Expand World's count-or-range form: "3" is 3..3, "2;4" is an INCLUSIVE 2..4.</summary>
        private static void ParseRange(string text, out int min, out int max, out bool present)
        {
            min = 1;
            max = 1;
            present = !string.IsNullOrEmpty(text);
            if (!present)
                return;
            string[] parts = text.Split(';');
            if (!int.TryParse(parts[0].Trim(), out min))
                min = 1;
            max = min;
            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int parsedMax))
                max = parsedMax;
            if (max < min)
                max = min;
        }

        private static string FormatRange(int min, int max) => min == max ? min.ToString() : $"{min};{max}";
    }
}
