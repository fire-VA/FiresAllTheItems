using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using FiresAllTheItems.Logic;

namespace FiresAllTheItems
{
    // FiresAllTheItems (FATI) — Jotunn-free VNEI replacement. Full parity first (item grid,
    // recipes, admin spawn, copy IDs), then our own extensions (recipe/drop editing).
    // Feature parity target: VNEI 0.17.5 (MIT; see THIRDPARTY-NOTICES.md).
    // CLIENT-SIDE: nothing of ours is required on the server — browsing, recipes and search work
    // against a pure-vanilla dedi. Spawning needs devcommands ON plus admin, and vanilla cannot
    // prove admin to a client on its own (ZNet.SendAdminList is a one-shot server push that a
    // joining client routinely misses), so the admin half reads Core's synced signal when present
    // and the vanilla list otherwise — a Fires server answers it, a vanilla one may not.
    // Depends ONLY on FiresUnifiedCore, client-side (UIBuilderHelper, ItemIconRenderer, help panel).
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.Fire.FiresUnifiedCore", "0.2.31")]
    public class Ascend : BaseUnityPlugin
    {
        public const string PluginGUID = "com.Fire.FiresAllTheItems";
        public const string PluginName = "FiresAllTheItems";
        public const string PluginVersion = "0.0.24";

        public static Ascend Instance { get; private set; }
        public static BepInEx.Logging.ManualLogSource Log;

        // Mirrors VNEI's config surface (grown as parity features land).
        public static ConfigEntry<int> columnCount;
        public static ConfigEntry<int> rowCount;
        public static ConfigEntry<string> tabName;
        public static ConfigEntry<bool> useBlacklist;
        public static ConfigEntry<bool> showOnlyKnown;
        public static ConfigEntry<bool> cheating;
        public static ConfigEntry<bool> cheatCreatures;
        public static ConfigEntry<MouseButtonWrapper> itemCheatButton;
        public static ConfigEntry<KeyboardShortcut> viewRecipeHotkey;

        public static bool ShowOnlyKnown => showOnlyKnown != null && showOnlyKnown.Value;

        private static HashSet<string> _blacklist = new HashSet<string>();
        private Harmony _harmony;

        // Default blacklist ported from VNEI's ItemBlacklist.json (dev/test prefabs that
        // pollute the grid). Users extend via config\FiresAllTheItems\blacklist.txt.
        private static readonly string[] DefaultBlacklist =
        {
            "Rock_destructible_test", "Player", "TrainingDummy", "HealthUpgrade_Bonemass",
            "HealthUpgrade_GDKing", "StaminaUpgrade_Greydwarf", "StaminaUpgrade_Troll",
            "StaminaUpgrade_Wraith", "IronOre", "MineRock_Iron", "Pickable_BogIronOre",
            "Trailership", "FirTree_oldLog", "TrophyDraugrFem", "CapeTest", "DvergerArbalest",
            "DvergerArbalest_shoot", "DvergerStaffSupport", "DvergerTest", "EvilHeart_Forest",
            "EvilHeart_Swamp", "TorchMist", "FishAnglerRaw", "fallenvalkyrie_screech",
            "fallenvalkyrie_swoopattack", "fallenvalkyrie_wingspin",
        };

        public enum MouseButtonWrapper
        {
            None = -1,
            Left = 0,
            Right = 1,
            Middle = 2,
        }

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            columnCount = Config.Bind("UI", "Items Horizontal", 12,
                new ConfigDescription("Grid columns in the item browser.", new AcceptableValueRange<int>(1, 25)));
            rowCount = Config.Bind("UI", "Items Vertical", 6,
                new ConfigDescription("Grid rows in the item browser.", new AcceptableValueRange<int>(1, 25)));
            tabName = Config.Bind("UI", "Tab Name", "FATI",
                "Label of the injected crafting-panel tab.");
            useBlacklist = Config.Bind("General", "Use Item Blacklist", true,
                "Hide known dev/test prefabs from the grid.");
            showOnlyKnown = Config.Bind("General", "Show Only Known", false,
                "Only show items the player has discovered.");
            cheating = Config.Bind("Cheating", "Enable Cheating", true,
                "Allow spawning items from the grid. Runtime-gated regardless of this setting: devcommands must be ON and you must be the host or on the server's admin list.");
            cheatCreatures = Config.Bind("Cheating", "Enable Spawning Creatures", false,
                "Allow spawning creatures from the grid (same devcommands + admin gate).");
            itemCheatButton = Config.Bind("Cheating", "Item Cheat Mouse Button", MouseButtonWrapper.Right,
                "Mouse button that spawns the clicked item.");
            viewRecipeHotkey = Config.Bind("Hotkeys", "View Recipe Hotkey", new KeyboardShortcut(KeyCode.R),
                "Press over any item tooltip (inventory, chests, FATI grid) to open that item's recipes.");

            LoadBlacklist();

            // Grid dimensions / filters react live; the panel rebuilds itself on next open.
            columnCount.SettingChanged += (s, e) => UI.FatiTab.MarkUiDirty();
            rowCount.SettingChanged += (s, e) => UI.FatiTab.MarkUiDirty();
            tabName.SettingChanged += (s, e) => UI.FatiTab.UpdateTabName();
            showOnlyKnown.SettingChanged += (s, e) => Indexing.UpdateKnown();
            useBlacklist.SettingChanged += (s, e) => UI.FatiBaseUI.MarkSearchDirty();
            FiresCore.Recipes.RecipeOverrides.Changed += OnRecipeOverridesChanged;
            FiresCore.Recipes.RecipeOverrides.SubmitResult += OnRecipeSubmitResult;
            FiresCore.Creatures.CreatureOverrides.Changed += OnCreatureOverridesChanged;
            FiresCore.Creatures.CreatureOverrides.SubmitResult += OnCreatureSubmitResult;

            _harmony = new Harmony(PluginGUID);
            _harmony.PatchAll();

            try { UI.FatiHelpContent.Register(); }
            catch (System.Exception ex) { Debug.LogWarning($"[{PluginName}] Help registration failed: {ex.Message}"); }

            Debug.Log($"[{PluginName}] Awake() — version {PluginVersion}");
        }

        private static void OnRecipeOverridesChanged()
        {
            Indexing.ReindexCraftingRecipes();
            UI.FatiTab.OnRecipesChanged();
        }

        private static void OnRecipeSubmitResult(bool ok, string message)
        {
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "FATI: " + message);
            UI.FatiTab.OnRecipeSubmitResult(ok);
        }

        private static void OnCreatureOverridesChanged()
        {
            Indexing.ReindexCreatureDrops();
            UI.FatiTab.OnRecipesChanged();
        }

        private static void OnCreatureSubmitResult(bool ok, string message)
        {
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "FATI: " + message);
            UI.FatiTab.OnCreatureSubmitResult(ok);
        }

        private bool _indexTried;

        // VNEI's R-over-tooltip lookup: resolve the hovered tooltip's topic/text to an item.
        // UITooltip.m_current is private in assembly_guiutils; one reflection read per keypress.
        private static readonly System.Reflection.FieldInfo TooltipCurrentField =
            HarmonyLib.AccessTools.Field(typeof(UITooltip), "m_current");
        private static readonly System.Text.RegularExpressions.Regex RichTagRegex =
            new System.Text.RegularExpressions.Regex("<[^>]*>");

        private void HandleViewRecipeHotkey()
        {
            if (!FiresCore.Input.Chord.IsDown(viewRecipeHotkey.Value))
                return;
            var current = TooltipCurrentField?.GetValue(null) as UITooltip;
            if (current == null)
                return;
            string topic = (current.m_topic ?? "").Trim();
            string text = (current.m_text ?? "").Trim();
            Item item = null;
            if (topic.Length > 0)
                item = Indexing.GetItem(topic);
            if (item == null && text.Length > 0)
                item = Indexing.GetItem(text);
            if (item == null && RichTagRegex.IsMatch(topic))
                item = Indexing.GetItem(RichTagRegex.Replace(topic, string.Empty).Trim());
            if (item != null)
                UI.FatiTab.OpenRecipeFor(item);
        }

        private void Update()
        {
            if (Indexing.HasIndexed())
                HandleViewRecipeHotkey();
            // VNEI pattern: index lazily once the local player exists (ObjectDB/ZNetScene are
            // fully populated by then, including every mod's prefabs). Never runs headless.
            // ONE attempt per world session — a failed index must not retry per frame
            // (0.0.1 spammed 635 errors that way); the latch re-arms on logout.
            if (_indexTried)
            {
                if (Player.m_localPlayer == null)
                    _indexTried = false;
                return;
            }
            if (!Indexing.HasIndexed() && Player.m_localPlayer != null && ZNetScene.instance != null)
            {
                _indexTried = true;
                try
                {
                    Indexing.IndexAll();
                    Indexing.UpdateKnown();
                    FavoritesSave.Load();
                }
                catch (Exception ex)
                {
                    Log.LogError($"IndexAll failed: {ex}");
                }
            }
        }

        public static bool IsItemBlacklisted(Item item)
        {
            return _blacklist.Contains(item.internalName) || _blacklist.Contains(Indexing.CleanupName(item.internalName));
        }

        private static void LoadBlacklist()
        {
            _blacklist = new HashSet<string>(DefaultBlacklist);
            try
            {
                string dir = Path.Combine(Paths.ConfigPath, "FiresAllTheItems");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "blacklist.txt");
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, "");
                }
                else
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            _blacklist.Add(trimmed);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Blacklist file load failed: {ex.Message}");
            }
        }
    }
}
