using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FiresAllTheItems.Logic;
using FiresCore.UI;

namespace FiresAllTheItems.UI
{
    // Port of VNEI UI/DisplayItem.cs — one grid cell: vanilla item_background, icon, instant
    // UITooltip, quality label. Click routing: cheat-button spawns (admin-gated), LMB opens
    // the recipe view (P1 — logs until then).
    public class FatiDisplayItem : MonoBehaviour, IPointerClickHandler
    {
        private static readonly Color UnknownColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        // m_showTimer is private in assembly_guiutils (we don't ship a publicized copy of it);
        // Harmony field-ref keeps the VNEI instant-tooltip trick without reflection-per-frame cost.
        private static readonly HarmonyLib.AccessTools.FieldRef<UITooltip, float> ShowTimerRef =
            HarmonyLib.AccessTools.FieldRefAccess<UITooltip, float>("m_showTimer");

        private FatiBaseUI baseUI;
        private Image background;
        private Image icon;
        private UITooltip uiTooltip;
        private TextMeshProUGUI qualityText;
        private TextMeshProUGUI countText;
        private TextMeshProUGUI favoriteStar;
        private Item item;
        private int quality = 1;
        private static readonly Color FallbackSlot = new Color(0.132f, 0.132f, 0.132f, 0.502f);
        private const float MinSlotAlpha = 0.2f;
        private const float MaxSlotBrightness = 1.8f;
        private static bool _slotSpriteLogged;

        internal static void ResetSlotSpriteLog() => _slotSpriteLogged = false;

        // A vanilla slot is a WHITE sliced sprite with a WHITE Image tint; the dark look is the Button's
        // ColorTint writing m_NormalColor ({0.132, 0.132, 0.132, 0.502} on InventoryElement) to the
        // CanvasRenderer. So Image.color reads white on a slot that renders dark, and copying it - which
        // 0.0.16 and 0.0.17 both did, one via the sprite and one via the colour - yields white tiles.
        // Take the sprite from the Button's own targetGraphic and the tint from its normalColor.
        private static void ApplySlotBacking(Image target)
        {
            GameObject prefab = InventoryGui.instance?.m_playerGrid?.m_elementPrefab;
            Button button = prefab != null ? prefab.GetComponentInChildren<Button>(true) : null;
            Image slot = button != null ? button.targetGraphic as Image : null;
            if (slot == null && prefab != null)
                slot = prefab.GetComponent<Image>();

            if (slot != null && slot.sprite != null)
            {
                target.sprite = slot.sprite;
                // Sliced needs a border; without one Unity draws nothing useful, so fall back to Simple.
                target.type = slot.sprite.border == Vector4.zero ? Image.Type.Simple : slot.type;
                target.fillCenter = true;
                target.pixelsPerUnitMultiplier = slot.pixelsPerUnitMultiplier;
                Color tint = button != null ? button.colors.normalColor : slot.color;
                // A slot nobody can see is the bug this has already shipped twice. Whatever vanilla hands back,
                // the tile stays dark enough to read against the parchment.
                if (tint.a < MinSlotAlpha)
                    tint.a = FallbackSlot.a;
                if (tint.r + tint.g + tint.b > MaxSlotBrightness)
                    tint = new Color(FallbackSlot.r, FallbackSlot.g, FallbackSlot.b, tint.a);
                target.color = tint;
            }
            else
            {
                target.sprite = null;
                target.type = Image.Type.Simple;
                target.color = FallbackSlot;
            }

            if (_slotSpriteLogged)
                return;
            _slotSpriteLogged = true;
            Ascend.Log?.LogInfo(target.sprite != null
                ? $"Cell slots from the vanilla inventory element: sprite {target.sprite.name}, type {target.type}, tint #{ColorUtility.ToHtmlStringRGBA(target.color)} (button {(button != null ? "normalColor" : "absent, used Image.color")})."
                : "Vanilla inventory element unreadable - cells fall back to a flat tile.");
        }

        // Builds one square cell (default 48px). anchoredPosition is assigned by the caller.
        // The background is vanilla's inventory slot sprite, as VNEI uses, but held at 62% so the
        // parchment still reads through the grid. Pale building-piece icons are unreadable without
        // it. If the sprite scavenger misses, a flat dark tile stands in.
        public static FatiDisplayItem Create(Transform parent, FatiBaseUI baseUI, float size = 48f)
        {
            var go = new GameObject("FatiItem");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(size, size);

            var cell = go.AddComponent<FatiDisplayItem>();
            cell.baseUI = baseUI;

            cell.background = go.AddComponent<Image>();
            ApplySlotBacking(cell.background);
            cell.background.raycastTarget = true;

            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(go.transform, false);
            var iconRect = iconGO.AddComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            float iconPad = Mathf.Round(size * 0.146f);
            iconRect.offsetMin = new Vector2(iconPad, iconPad);
            iconRect.offsetMax = new Vector2(-iconPad, -iconPad);
            cell.icon = iconGO.AddComponent<Image>();
            cell.icon.raycastTarget = false;
            cell.icon.preserveAspect = true;

            cell.qualityText = UIBuilderHelper.CreateLabel(go.transform, "Quality", "", 12f, Color.white,
                new Vector2(0f, 0f), new Vector2(1f, 1f), TextAlignmentOptions.TopRight);
            cell.qualityText.raycastTarget = false;
            var qRect = cell.qualityText.rectTransform;
            qRect.offsetMin = new Vector2(2f, 2f);
            qRect.offsetMax = new Vector2(-4f, -1f);

            cell.countText = UIBuilderHelper.CreateLabel(go.transform, "Count", "", 11f, Color.white,
                new Vector2(0f, 0f), new Vector2(1f, 1f), TextAlignmentOptions.BottomRight);
            cell.countText.raycastTarget = false;
            var cRect = cell.countText.rectTransform;
            cRect.offsetMin = new Vector2(2f, 1f);
            cRect.offsetMax = new Vector2(-3f, -2f);
            cell.countText.gameObject.SetActive(false);

            cell.favoriteStar = UIBuilderHelper.CreateLabel(go.transform, "Fav", "★", 12f,
                new Color(1f, 0.72f, 0.23f), new Vector2(0f, 0f), new Vector2(1f, 1f), TextAlignmentOptions.TopLeft);
            cell.favoriteStar.raycastTarget = false;
            var fRect = cell.favoriteStar.rectTransform;
            fRect.offsetMin = new Vector2(2f, 2f);
            fRect.offsetMax = new Vector2(-2f, -1f);
            cell.favoriteStar.gameObject.SetActive(false);

            // Vanilla shared tooltip: copy the popup prefab off any live inventory element.
            cell.uiTooltip = go.AddComponent<UITooltip>();
            cell.uiTooltip.m_tooltipPrefab = FatiTab.GetTooltipPrefab();

            return cell;
        }

        private void Update()
        {
            // VNEI trick: force the show timer so tooltips appear instantly on hover.
            if (uiTooltip != null)
                ShowTimerRef(uiTooltip) = 1f;
        }

        public Item GetItem() => item;

        public void SetItem(Item target, int quality)
        {
            if (item != null)
            {
                item.onFavoriteChanged.RemoveListener(this);
                item.onKnownChanged.RemoveListener(this);
            }
            item = target;
            this.quality = quality;
            icon.gameObject.SetActive(item != null);
            UpdateIconAndTooltip();
            UpdateFavorite();
            if (item == null)
                return;
            item.onKnownChanged.AddListener(this, UpdateIconAndTooltip);
            item.onFavoriteChanged.AddListener(this, UpdateFavorite);
        }

        private void UpdateFavorite()
        {
            if (favoriteStar != null)
                favoriteStar.gameObject.SetActive(item != null && item.isFavorite);
        }

        private void UpdateIconAndTooltip()
        {
            if (item != null)
            {
                if (item.IsKnown)
                {
                    icon.color = Color.white;
                    icon.sprite = item.GetIcon();
                    uiTooltip.Set(item.preLocalizeName, item.GetTooltip(quality));
                    qualityText.text = item.maxQuality > 1 || quality > 1 ? $"{quality}" : "";
                }
                else
                {
                    icon.color = UnknownColor;
                    icon.sprite = FatiSprites.NoIcon;
                    uiTooltip.Set("Unknown", "");
                    qualityText.text = "";
                }
            }
            else
            {
                icon.sprite = null;
                uiTooltip.Set("", "");
                qualityText.text = "";
            }
        }

        // Spawning needs devcommands ON *and* vanilla admin — both halves, no Fires mod on the
        // server. Neither half works alone: Terminal.m_cheat flips LOCALLY for anyone who types
        // devcommands (the server only vets the RemoteCommand copy and answers "You are not
        // admin"), and Terminal.IsCheatsEnabled() additionally demands IsServer(), so it is false
        // for every dedi client including real admins. ZNet.LocalPlayerIsAdminOrHost() reads the
        // adminlist a VANILLA server pushes at connect (ZNet.SendAdminList) and is already true
        // for a single-player/listen host — there devcommands is the whole gate.
        public static bool PlayerAllowsCheating() => Player.m_localPlayer != null && Terminal.m_cheat && IsLocalAdmin();

        // Vanilla alone CANNOT answer this on a dedi client: ZNet.SendAdminList is the server's only
        // push, fires once at connect, and a client that misses it has no way to ask again — this
        // client sat at adminList=0 entries while the server logged it as admin. So the family's
        // canonical check does the work (IsServer || vanilla list || the Core admin push), with the
        // normalized list compare kept underneath for a list that arrived in a prefixed form.
        public static bool IsLocalAdmin()
        {
            if (ZNet.instance == null)
                return false;
            if (ZNet.instance.IsServer())
                return true;
            try
            {
                if (FiresCore.Sync.AdminSyncing.IsLocalAdmin())
                    return true;
            }
            catch { }
            return AdminListContainsLocal();
        }

        private static bool AdminListContainsLocal()
        {
            List<string> adminList = ZNet.instance != null ? ZNet.instance.GetAdminList() : null;
            if (adminList == null || adminList.Count == 0)
                return false;
            string local = NormalizeId(UserInfo.GetLocalUser().UserId.ToString());
            if (local.Length == 0)
                return false;
            for (int i = 0; i < adminList.Count; ++i)
            {
                if (string.Equals(NormalizeId(adminList[i]), local, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string NormalizeId(string id)
        {
            if (string.IsNullOrEmpty(id))
                return string.Empty;
            int split = id.LastIndexOf('_');
            return (split >= 0 ? id.Substring(split + 1) : id).Trim();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (baseUI.IsPicking)
            {
                if (item != null && eventData.button == PointerEventData.InputButton.Left)
                    baseUI.CompletePick(item);
                return;
            }
            if (item == null || !item.IsKnown)
                return;
            if ((int)Ascend.itemCheatButton.Value == (int)eventData.button)
            {
                if (Ascend.cheating.Value && PlayerAllowsCheating())
                {
                    if (item.prefab != null)
                        CheatItem();
                    return;
                }
                ReportCheatDenied();
            }
            if (eventData.button == PointerEventData.InputButton.Left)
                baseUI.ShowRecipe(item);
        }

        // A silent no-op reads as a broken mod; say which half of the gate failed.
        private static bool _gateLogged;

        private static void ReportCheatDenied()
        {
            string reason;
            if (!Ascend.cheating.Value)
                reason = "spawning is off (Cheating.Enable Cheating)";
            else if (!Terminal.m_cheat)
                reason = "type devcommands in the console (F5) first";
            else
                reason = "admins only — you are not on the server's admin list";
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "FATI: " + reason);

            if (_gateLogged)
                return;
            _gateLogged = true;
            List<string> adminList = ZNet.instance != null ? ZNet.instance.GetAdminList() : null;
            bool coreAdmin = false;
            try { coreAdmin = FiresCore.Sync.AdminSyncing.IsLocalAdmin(); } catch { }
            Ascend.Log?.LogInfo($"spawn gate denied — cheating={Ascend.cheating.Value}, m_cheat={Terminal.m_cheat}, " +
                $"isServer={(ZNet.instance != null && ZNet.instance.IsServer())}, " +
                $"localId='{UserInfo.GetLocalUser().UserId}', adminList={(adminList == null ? "null" : adminList.Count.ToString())} entries, " +
                $"vanillaListMatch={AdminListContainsLocal()}, coreAdmin={coreAdmin}");
        }

        public void SetCount(string count)
        {
            countText.text = count;
            countText.gameObject.SetActive(!string.IsNullOrEmpty(count));
        }

        private void CheatItem()
        {
            bool fullStack = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool spawnIngredients = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (item.prefab.TryGetComponent(out ItemDrop itemDrop))
            {
                Recipe recipe = ObjectDB.instance.GetRecipe(itemDrop.m_itemData);
                if (spawnIngredients && recipe != null)
                {
                    foreach (Piece.Requirement resource in recipe.m_resources)
                        SpawnItem(resource.m_resItem, resource.GetAmount(quality), 1, fullStack);
                    if (quality > 1)
                        SpawnItem(itemDrop, 1, quality - 1, fullStack);
                }
                else
                {
                    SpawnItem(itemDrop, 1, quality, fullStack);
                }
            }
            else if (item.prefab.TryGetComponent(out Piece piece))
            {
                foreach (Piece.Requirement resource in piece.m_resources)
                    SpawnItem(resource.m_resItem, resource.GetAmount(1), 1, fullStack);
            }
            else if (Ascend.cheatCreatures.Value && item.prefab.GetComponent<Character>() != null)
            {
                Transform playerTransform = Player.m_localPlayer.transform;
                UnityEngine.Object.Instantiate(item.prefab, playerTransform.position + playerTransform.forward * 2f, Quaternion.identity);
            }
        }

        private static void SpawnItem(ItemDrop itemDrop, int amount, int level, bool fullStack)
        {
            if (itemDrop == null)
                return;
            int stackSize = fullStack ? itemDrop.m_itemData.m_shared.m_maxStackSize : amount;
            if (stackSize <= 0)
                return;
            ItemDrop.ItemData picked = Player.m_localPlayer.PickupPrefab(itemDrop.gameObject, stackSize);
            if (picked != null)
            {
                picked.m_quality = level;
                picked.m_durability = picked.GetMaxDurability();
            }
        }
    }
}
