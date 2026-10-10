using System;
using FiresCore.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresAllTheItems.UI
{
    /// <summary>
    /// The small building blocks both FATI editors (recipes, creatures) are laid out from, all on top of Core's
    /// UIBuilderHelper: top-left-anchored buttons, labels, steppers, icons and cards on a dark editor backdrop.
    /// </summary>
    internal static class FatiEditorWidgets
    {
        public const float HeaderHeight = 30f;
        public const float SmallButtonHeight = 22f;
        public const float LineHeight = 26f;
        public const float LabelFontSize = 13f;
        public const float CaptionFontSize = 11f;
        public const float CardGap = 8f;
        public const float CardInset = 8f;
        public const float CardRightMargin = 4f;
        public const float StepButtonWidth = 22f;
        public const float StepValueWidth = 44f;
        public const float SubmitTimeoutSeconds = 8f;
        public const float GateCheckSeconds = 0.5f;

        private const int ShiftStepMultiplier = 10;

        public static readonly Color Gold = new Color(1f, 0.72f, 0.23f);
        public static readonly Color Ink = new Color(0.92f, 0.89f, 0.82f);
        public static readonly Color Muted = new Color(0.66f, 0.62f, 0.55f);
        public static readonly Color OnColor = new Color(0.55f, 0.85f, 0.45f);
        public static readonly Color BackdropColor = new Color(0.06f, 0.05f, 0.04f, 0.94f);
        public static readonly Color CardColor = new Color(0.14f, 0.11f, 0.08f, 0.92f);
        public static readonly Color CardEdgeColor = new Color(0.76f, 0.64f, 0.40f, 0.45f);
        public const string MutedHex = "#A89E8C";

        /// <summary>The dark panel an editor lives on, laid over the recipe view below its header.</summary>
        public static Image CreateBackdrop(RectTransform parent, string name)
        {
            Image backdrop = UIBuilderHelper.CreateImage(parent, name, Vector2.zero, Vector2.one, BackdropColor);
            RectTransform rect = backdrop.rectTransform;
            rect.offsetMin = new Vector2(8f, 44f);
            rect.offsetMax = new Vector2(-8f, -102f);
            backdrop.gameObject.AddComponent<Outline>().effectColor = CardEdgeColor;
            return backdrop;
        }

        public static RectTransform CreateCardScroll(RectTransform root)
        {
            UIBuilderHelper.ScrollAreaResult scroll = UIBuilderHelper.CreateScrollArea(root, "Cards", Vector2.zero, Vector2.one);
            var scrollRoot = (RectTransform)scroll.Root.transform;
            scrollRoot.offsetMin = Vector2.zero;
            scrollRoot.offsetMax = new Vector2(0f, -HeaderHeight - 4f);
            scroll.ScrollRect.scrollSensitivity = 40f;
            scroll.ScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll.Content;
        }

        public static RectTransform AddCard(RectTransform content)
        {
            Image cardImage = UIBuilderHelper.CreateImage(content, "Card", new Vector2(0f, 1f), new Vector2(1f, 1f), CardColor);
            cardImage.gameObject.AddComponent<Outline>().effectColor = CardEdgeColor;
            RectTransform card = cardImage.rectTransform;
            card.pivot = new Vector2(0.5f, 1f);
            return card;
        }

        public static float FinishCard(RectTransform card, float top, float contentBottom)
        {
            float height = contentBottom + CardInset;
            card.offsetMin = new Vector2(0f, -(top + height));
            card.offsetMax = new Vector2(-CardRightMargin, -top);
            return top + height;
        }

        public static Button AddButton(RectTransform parent, string text, float x, float y, float width, Action onClick, bool fromRight = false)
        {
            Vector2 anchor = fromRight ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
            Button button = UIBuilderHelper.CreateParchmentButton(parent, text, anchor, anchor, () => onClick());
            var rect = button.GetComponent<RectTransform>();
            rect.pivot = anchor;
            rect.sizeDelta = new Vector2(width, SmallButtonHeight);
            rect.anchoredPosition = new Vector2(fromRight ? -x : x, -y);
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
                label.fontSize = LabelFontSize;
            return button;
        }

        public static Button AddSwitch(RectTransform parent, string text, bool on, float x, float y, float width, Action onClick, bool fromRight = false)
        {
            Button button = AddButton(parent, text, x, y, width, onClick, fromRight);
            button.GetComponentInChildren<TextMeshProUGUI>().color = on ? OnColor : Muted;
            return button;
        }

        public static TextMeshProUGUI AddLabel(RectTransform parent, string text, float x, float y, float width, Color color,
            float fontSize = LabelFontSize, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            TextMeshProUGUI label = UIBuilderHelper.CreateLabel(parent, "Label", text, fontSize, color,
                new Vector2(0f, 1f), new Vector2(0f, 1f), alignment);
            RectTransform rect = label.rectTransform;
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, SmallButtonHeight);
            rect.anchoredPosition = new Vector2(x, -y);
            label.raycastTarget = false;
            return label;
        }

        /// <summary>A label stretched to the card's right edge, clipped with an ellipsis instead of wrapping.</summary>
        public static TextMeshProUGUI AddStretchedLabel(RectTransform parent, string text, float x, float y, Color color)
        {
            TextMeshProUGUI label = AddLabel(parent, text, x, y, 0f, color);
            RectTransform rect = label.rectTransform;
            rect.anchorMax = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(-x - CardInset, SmallButtonHeight);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        public static Image AddIcon(RectTransform parent, Sprite sprite, float x, float y, float size, Action onClick)
        {
            Image icon = UIBuilderHelper.CreateImage(parent, "Icon", new Vector2(0f, 1f), new Vector2(0f, 1f), Color.white);
            RectTransform rect = icon.rectTransform;
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(x, -y);
            icon.sprite = sprite;
            icon.preserveAspect = true;
            icon.gameObject.AddComponent<Button>().onClick.AddListener(() => onClick());
            return icon;
        }

        public static void AddIntStepper(RectTransform parent, float x, float y, int value, int min, int max, Action<int> assign)
        {
            AddButton(parent, "-", x, y, StepButtonWidth, () => assign(Mathf.Clamp(value - StepMultiplier(), min, max)));
            AddLabel(parent, value.ToString(), x + StepButtonWidth, y, StepValueWidth, Ink, LabelFontSize, TextAlignmentOptions.Midline);
            AddButton(parent, "+", x + StepButtonWidth + StepValueWidth, y, StepButtonWidth, () => assign(Mathf.Clamp(value + StepMultiplier(), min, max)));
        }

        public static void AddFloatStepper(RectTransform parent, float x, float y, float value, float step, float min, float max,
            Func<float, string> format, Action<float> assign)
        {
            AddButton(parent, "-", x, y, StepButtonWidth, () => assign(Snap(Mathf.Clamp(value - step * StepMultiplier(), min, max), step)));
            AddLabel(parent, format(value), x + StepButtonWidth, y, StepValueWidth, Ink, LabelFontSize, TextAlignmentOptions.Midline);
            AddButton(parent, "+", x + StepButtonWidth + StepValueWidth, y, StepButtonWidth, () => assign(Snap(Mathf.Clamp(value + step * StepMultiplier(), min, max), step)));
        }

        public static float StepperWidth => StepButtonWidth * 2f + StepValueWidth;

        public static void Message(string text) => Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "FATI: " + text);

        private static int StepMultiplier() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? ShiftStepMultiplier : 1;

        private static float Snap(float value, float step) => Mathf.Round(value / step) * step;
    }
}
