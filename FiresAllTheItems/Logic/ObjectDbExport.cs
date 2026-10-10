using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace FiresAllTheItems.Logic
{
    // `fati_export`: the whole ObjectDB as one JSON file, for the autoplay bot's goal tree (Tools\AUTOPLAY_PROGRESSION.md,
    // Fire 2026-09-28: the bot never runs on hand-typed recipes). Read-only. It holds:
    // - items, recipes, and the pieces of every build tool, with their stations and upgrades;
    // - smelter-type conversions and producers;
    // - creatures and their drops, and what can be chopped, mined or picked;
    // - boss altars and Vegvisirs, read from the location prefabs;
    // - the plugin list, so a vanilla-profile export (BepInEx + Core + FATI only) is plainly the vanilla baseline.
    // Runs as an F5 test, 'fati_test' (FDT's runner only runs '*_test' commands and reads FiresTests\<command>.txt), or as
    // the plain command 'fati_export': [FatiTest] BEGIN / 1/1 export / END, echoed to the console and BepInEx\FiresTests\fati_test.txt.
    public static class ObjectDbExport
    {
        private const string CommandName = "fati_export";
        private const string TestCommandName = "fati_test";
        private const string ContractMarker = "[F5 test]";
        private const string TestPrefix = "[FatiTest]";
        private const string TestsFolder = "FiresTests";
        private const string ResultsFile = TestCommandName + ".txt";
        private const string ExportFile = "objectdb_export.json";
        private const int HashChars = 12;
        private const int LocationsPerFrame = 4;
        private const string NumberFormat = "0.###";

        private static bool _running;

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        private static class Terminal_InitTerminal
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand(CommandName,
                    "Writes the ObjectDB (items, recipes, pieces, stations, drops, altars) as JSON to BepInEx\\FiresTests\\" + ExportFile,
                    args => Start(args.Context));
                new Terminal.ConsoleCommand(TestCommandName,
                    ContractMarker + " [FiresAllTheItems] The ObjectDB export (" + CommandName + ") as a test: writes BepInEx\\FiresTests\\" + ExportFile +
                    " and passes when it is written.",
                    args => Start(args.Context));
            }
        }

        private static void Start(Terminal terminal)
        {
            if (_running)
            {
                terminal.AddString("fati_export is already running.");
                return;
            }
            if (ObjectDB.instance == null || ZNetScene.instance == null || ZoneSystem.instance == null)
            {
                terminal.AddString("Join a world first.");
                return;
            }
            Ascend.Instance.StartCoroutine(Run());
        }

        private static IEnumerator Run()
        {
            _running = true;
            float started = Time.realtimeSinceStartup;
            Write($"{TestPrefix} BEGIN FiresAllTheItems {Ascend.PluginVersion} ({DllHash()}) on {Role()} '{PlayerName()}' world '{WorldName()}': 1 steps, ~30 s");
            var json = new Json();
            string result = null, detail = null;
            try
            {
                json.Open();
                WriteHeader(json);
                WriteItems(json);
                WriteRecipes(json);
                WritePieces(json);
                WriteCreatures(json);
                WriteGatherables(json);
            }
            catch (Exception e)
            {
                result = "FAIL";
                detail = $"threw {e.GetType().Name}: {e.Message}";
                Ascend.Log.LogError(e);
            }
            if (result == null)
            {
                IEnumerator locations = WriteLocations(json);
                while (true)
                {
                    bool more;
                    try
                    {
                        more = locations.MoveNext();
                    }
                    catch (Exception e)
                    {
                        result = "FAIL";
                        detail = $"threw {e.GetType().Name} reading locations: {e.Message}";
                        Ascend.Log.LogError(e);
                        break;
                    }
                    if (!more) break;
                    yield return locations.Current;
                }
            }
            if (result == null)
            {
                json.Close();
                string path = Path.Combine(Paths.BepInExRootPath, TestsFolder, ExportFile);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, json.ToString(), Encoding.UTF8);
                    result = "PASS";
                    detail = $"{json.Counts} written to {path} ({new FileInfo(path).Length / 1024} KB)";
                }
                catch (IOException e)
                {
                    result = "FAIL";
                    detail = $"could not write {path}: {e.Message}";
                }
            }
            Write($"{TestPrefix} 1/1 export {result} - {detail}");
            Write($"{TestPrefix} END {result}: {(result == "PASS" ? 1 : 0)} pass, {(result == "PASS" ? 0 : 1)} fail, 0 skip in {Time.realtimeSinceStartup - started:0} s");
            _running = false;
        }

        private static void WriteHeader(Json json)
        {
            json.Field("exported", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            json.Field("gameVersion", Version.GetVersionString());
            json.Field("world", WorldName());
            json.Field("exporter", $"FiresAllTheItems {Ascend.PluginVersion}");
            json.OpenArray("plugins");
            foreach (PluginInfo plugin in Chainloader.PluginInfos.Values.OrderBy(p => p.Metadata.GUID))
            {
                json.OpenObject();
                json.Field("guid", plugin.Metadata.GUID);
                json.Field("name", plugin.Metadata.Name);
                json.Field("version", plugin.Metadata.Version.ToString());
                json.CloseObject();
            }
            json.CloseArray();
        }

        private static void WriteItems(Json json)
        {
            json.OpenArray("items");
            foreach (GameObject prefab in ObjectDB.instance.m_items.Where(p => p != null))
            {
                ItemDrop drop = prefab.GetComponent<ItemDrop>();
                if (drop == null) continue;
                ItemDrop.ItemData.SharedData s = drop.m_itemData.m_shared;
                json.OpenObject();
                json.Field("prefab", prefab.name);
                Named(json, s.m_name);
                json.Field("mod", ModOf(prefab.name));
                json.Field("type", s.m_itemType.ToString());
                json.Field("weight", s.m_weight);
                json.Field("maxStack", s.m_maxStackSize);
                json.Field("maxQuality", s.m_maxQuality);
                json.Field("teleportable", s.m_teleportable);
                json.Field("toolTier", s.m_toolTier);
                json.Field("skill", s.m_skillType.ToString());
                if (s.m_food > 0f || s.m_foodStamina > 0f || s.m_foodEitr > 0f)
                {
                    json.OpenObject("food");
                    json.Field("health", s.m_food);
                    json.Field("stamina", s.m_foodStamina);
                    json.Field("eitr", s.m_foodEitr);
                    json.Field("seconds", s.m_foodBurnTime);
                    json.Field("regen", s.m_foodRegen);
                    json.CloseObject();
                }
                if (s.m_buildPieces != null) json.Field("buildTool", s.m_buildPieces.name);
                json.CloseObject();
            }
            json.CloseArray();
        }

        private static void WriteRecipes(Json json)
        {
            json.OpenArray("recipes");
            foreach (Recipe recipe in ObjectDB.instance.m_recipes.Where(r => r != null && r.m_item != null))
            {
                json.OpenObject();
                json.Field("recipe", recipe.name);
                json.Field("item", recipe.m_item.gameObject.name);
                json.Field("mod", ModOf(recipe.m_item.gameObject.name));
                json.Field("amount", recipe.m_amount);
                json.Field("enabled", recipe.m_enabled);
                json.Field("station", StationName(recipe.m_craftingStation));
                json.Field("minStationLevel", recipe.m_minStationLevel);
                json.Field("repairStation", StationName(recipe.m_repairStation));
                json.Field("onlyOneIngredient", recipe.m_requireOnlyOneIngredient);
                Requirements(json, recipe.m_resources);
                json.CloseObject();
            }
            json.CloseArray();
        }

        /// <summary>Every piece of every build tool (hammer, hoe, cultivator, modded hammers), with the station parts it carries.</summary>
        private static void WritePieces(Json json)
        {
            json.OpenArray("pieces");
            var seen = new HashSet<string>();
            foreach (GameObject tool in ObjectDB.instance.m_items.Where(p => p != null))
            {
                PieceTable table = tool.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
                if (table == null) continue;
                foreach (GameObject prefab in table.m_pieces.Where(p => p != null))
                {
                    Piece piece = prefab.GetComponent<Piece>();
                    if (piece == null || !seen.Add(prefab.name)) continue;
                    json.OpenObject();
                    json.Field("prefab", prefab.name);
                    Named(json, piece.m_name);
                    json.Field("mod", ModOf(prefab.name));
                    json.Field("tool", tool.name);
                    json.Field("category", piece.m_category.ToString());
                    json.Field("enabled", piece.m_enabled);
                    json.Field("station", StationName(piece.m_craftingStation));
                    json.Field("isUpgrade", piece.m_isUpgrade);
                    json.Field("comfort", piece.m_comfort);
                    Requirements(json, piece.m_resources);
                    StationParts(json, prefab);
                    json.CloseObject();
                }
            }
            json.CloseArray();
        }

        /// <summary>What a piece does as a station: crafting station, upgrade of one, smelter, fermenter, cooker or producer.</summary>
        private static void StationParts(Json json, GameObject prefab)
        {
            CraftingStation station = prefab.GetComponent<CraftingStation>();
            if (station != null)
            {
                json.OpenObject("craftingStation");
                Named(json, station.m_name);
                json.Field("buildRange", station.m_rangeBuild);
                json.Field("needsRoof", station.m_craftRequireRoof);
                json.CloseObject();
            }
            StationExtension extension = prefab.GetComponent<StationExtension>();
            if (extension != null)
            {
                json.OpenObject("upgradeOf");
                json.Field("station", StationName(extension.m_craftingStation));
                json.Field("maxDistance", extension.m_maxStationDistance);
                json.CloseObject();
            }
            Smelter smelter = prefab.GetComponent<Smelter>();
            if (smelter != null)
            {
                json.OpenObject("smelter");
                Named(json, smelter.m_name);
                json.Field("fuel", PrefabName(smelter.m_fuelItem));
                json.Field("maxOre", smelter.m_maxOre);
                json.Field("maxFuel", smelter.m_maxFuel);
                json.Field("fuelPerProduct", smelter.m_fuelPerProduct);
                json.Field("secondsPerProduct", smelter.m_secPerProduct);
                Conversions(json, smelter.m_conversion.Select(c => (PrefabName(c.m_from), PrefabName(c.m_to), 1, 0f)));
                json.CloseObject();
            }
            Fermenter fermenter = prefab.GetComponent<Fermenter>();
            if (fermenter != null)
            {
                json.OpenObject("fermenter");
                json.Field("seconds", fermenter.m_fermentationDuration);
                Conversions(json, fermenter.m_conversion.Select(c => (PrefabName(c.m_from), PrefabName(c.m_to), c.m_producedItems, 0f)));
                json.CloseObject();
            }
            CookingStation cooker = prefab.GetComponent<CookingStation>();
            if (cooker != null)
            {
                json.OpenObject("cookingStation");
                Named(json, cooker.m_name);
                json.Field("fuel", PrefabName(cooker.m_fuelItem));
                Conversions(json, cooker.m_conversion.Select(c => (PrefabName(c.m_from), PrefabName(c.m_to), 1, c.m_cookTime)));
                json.CloseObject();
            }
            Beehive hive = prefab.GetComponent<Beehive>();
            if (hive != null) Producer(json, "beehive", hive.m_honeyItem, hive.m_secPerUnit, hive.m_maxHoney);
            SapCollector sap = prefab.GetComponent<SapCollector>();
            if (sap != null) Producer(json, "sapCollector", sap.m_spawnItem, sap.m_secPerUnit, 0);
        }

        private static void Producer(Json json, string kind, ItemDrop item, float secondsPerUnit, int max)
        {
            json.OpenObject(kind);
            json.Field("item", PrefabName(item));
            json.Field("secondsPerUnit", secondsPerUnit);
            if (max > 0) json.Field("max", max);
            json.CloseObject();
        }

        private static void Conversions(Json json, IEnumerable<(string from, string to, int amount, float seconds)> conversions)
        {
            json.OpenArray("conversions");
            foreach ((string from, string to, int amount, float seconds) in conversions)
            {
                json.OpenObject();
                json.Field("from", from);
                json.Field("to", to);
                if (amount != 1) json.Field("amount", amount);
                if (seconds > 0f) json.Field("seconds", seconds);
                json.CloseObject();
            }
            json.CloseArray();
        }

        private static void WriteCreatures(Json json)
        {
            json.OpenArray("creatures");
            foreach (GameObject prefab in ScenePrefabs())
            {
                Character character = prefab.GetComponent<Character>();
                if (character == null) continue;
                json.OpenObject();
                json.Field("prefab", prefab.name);
                Named(json, character.m_name);
                json.Field("mod", ModOf(prefab.name));
                json.Field("health", character.m_health);
                json.Field("faction", character.m_faction.ToString());
                json.Field("boss", character.m_boss);
                if (!string.IsNullOrEmpty(character.m_defeatSetGlobalKey)) json.Field("defeatKey", character.m_defeatSetGlobalKey);
                CharacterDrop drops = prefab.GetComponent<CharacterDrop>();
                if (drops != null)
                {
                    json.OpenArray("drops");
                    foreach (CharacterDrop.Drop d in drops.m_drops.Where(d => d.m_prefab != null))
                    {
                        json.OpenObject();
                        json.Field("item", d.m_prefab.name);
                        json.Field("min", d.m_amountMin);
                        json.Field("max", d.m_amountMax);
                        json.Field("chance", d.m_chance);
                        if (d.m_onePerPlayer) json.Field("onePerPlayer", true);
                        json.CloseObject();
                    }
                    json.CloseArray();
                }
                json.CloseObject();
            }
            json.CloseArray();
        }

        /// <summary>What can be picked, chopped or mined, with the tool tier it needs and what it drops.</summary>
        private static void WriteGatherables(Json json)
        {
            json.OpenArray("gatherables");
            foreach (GameObject prefab in ScenePrefabs())
            {
                string kind = null;
                int minToolTier = 0;
                var drops = new List<DropTable>();
                Pickable pickable = prefab.GetComponent<Pickable>();
                if (pickable != null && pickable.m_itemPrefab != null)
                {
                    json.OpenObject();
                    json.Field("prefab", prefab.name);
                    json.Field("mod", ModOf(prefab.name));
                    json.Field("kind", "pick");
                    json.Field("item", pickable.m_itemPrefab.name);
                    json.Field("amount", pickable.m_amount);
                    if (pickable.m_extraDrops != null && pickable.m_extraDrops.m_drops.Count > 0) Drops(json, "extraDrops", pickable.m_extraDrops);
                    json.CloseObject();
                    continue;
                }
                TreeBase tree = prefab.GetComponent<TreeBase>();
                TreeLog log = prefab.GetComponent<TreeLog>();
                MineRock rock = prefab.GetComponent<MineRock>();
                MineRock5 rock5 = prefab.GetComponent<MineRock5>();
                Destructible destructible = prefab.GetComponent<Destructible>();
                if (tree != null) { kind = "tree"; minToolTier = tree.m_minToolTier; drops.Add(tree.m_dropWhenDestroyed); }
                else if (log != null) { kind = "log"; minToolTier = log.m_minToolTier; drops.Add(log.m_dropWhenDestroyed); }
                else if (rock != null) { kind = "rock"; minToolTier = rock.m_minToolTier; drops.Add(rock.m_dropItems); }
                else if (rock5 != null) { kind = "rock"; minToolTier = rock5.m_minToolTier; drops.Add(rock5.m_dropItems); }
                else if (destructible != null && prefab.GetComponent<Character>() == null)
                {
                    DropOnDestroyed onDestroyed = prefab.GetComponent<DropOnDestroyed>();
                    if (onDestroyed == null) continue;
                    kind = "destructible";
                    minToolTier = destructible.m_minToolTier;
                    drops.Add(onDestroyed.m_dropWhenDestroyed);
                }
                if (kind == null) continue;
                json.OpenObject();
                json.Field("prefab", prefab.name);
                json.Field("mod", ModOf(prefab.name));
                json.Field("kind", kind);
                json.Field("minToolTier", minToolTier);
                if (tree != null && tree.m_logPrefab != null) json.Field("log", tree.m_logPrefab.name);
                foreach (DropTable table in drops.Where(t => t != null)) Drops(json, "drops", table);
                json.CloseObject();
            }
            json.CloseArray();
        }

        private static void Drops(Json json, string name, DropTable table)
        {
            json.OpenObject(name);
            json.Field("min", table.m_dropMin);
            json.Field("max", table.m_dropMax);
            json.Field("chance", table.m_dropChance);
            if (table.m_oneOfEach) json.Field("oneOfEach", true);
            json.OpenArray("items");
            foreach (DropTable.DropData d in table.m_drops.Where(d => d.m_item != null))
            {
                json.OpenObject();
                json.Field("item", d.m_item.name);
                json.Field("stackMin", d.m_stackMin);
                json.Field("stackMax", d.m_stackMax);
                json.Field("weight", d.m_weight);
                json.CloseObject();
            }
            json.CloseArray();
            json.CloseObject();
        }

        /// <summary>
        /// Every enabled location, with the boss altars (OfferingBowl) and Vegvisirs inside it. A location prefab is a
        /// soft reference in 1.0: one the game doesn't hold is loaded for the read and released straight after, a few
        /// per frame.
        /// </summary>
        private static IEnumerator WriteLocations(Json json)
        {
            json.OpenArray("locations");
            int done = 0;
            foreach (ZoneSystem.ZoneLocation location in ZoneSystem.instance.m_locations.Where(l => l != null && l.m_enable && l.m_prefab.IsValid))
            {
                bool loadedHere = !location.m_prefab.IsLoaded;
                if (loadedHere) location.m_prefab.Load();
                GameObject prefab = location.m_prefab.Asset;
                if (prefab != null)
                {
                    json.OpenObject();
                    json.Field("prefab", location.m_prefabName);
                    json.Field("mod", ModOf(location.m_prefabName));
                    json.Field("biome", location.m_biome.ToString());
                    Altars(json, prefab);
                    Vegvisirs(json, prefab);
                    json.CloseObject();
                }
                if (loadedHere) location.m_prefab.Release();
                if (++done % LocationsPerFrame == 0) yield return null;
            }
            json.CloseArray();
        }

        private static void Altars(Json json, GameObject location)
        {
            OfferingBowl[] bowls = location.GetComponentsInChildren<OfferingBowl>(true);
            if (bowls.Length == 0) return;
            json.OpenArray("altars");
            foreach (OfferingBowl bowl in bowls)
            {
                json.OpenObject();
                Named(json, bowl.m_name);
                json.Field("offer", PrefabName(bowl.m_bossItem));
                json.Field("count", bowl.m_bossItems);
                json.Field("boss", bowl.m_bossPrefab != null ? bowl.m_bossPrefab.name : null);
                if (bowl.m_itemPrefab != null) json.Field("spawnsItem", bowl.m_itemPrefab.gameObject.name);
                if (!string.IsNullOrEmpty(bowl.m_setGlobalKey)) json.Field("setsKey", bowl.m_setGlobalKey);
                if (bowl.m_useItemStands) json.Field("itemStands", true);
                json.CloseObject();
            }
            json.CloseArray();
        }

        private static void Vegvisirs(Json json, GameObject location)
        {
            Vegvisir[] stones = location.GetComponentsInChildren<Vegvisir>(true);
            if (stones.Length == 0) return;
            json.OpenArray("vegvisirs");
            foreach (Vegvisir.VegvisrLocation target in stones.SelectMany(v => v.m_locations))
            {
                json.OpenObject();
                json.Field("pointsTo", target.m_locationName);
                json.Field("pin", target.m_pinName);
                json.CloseObject();
            }
            json.CloseArray();
        }

        private static void Requirements(Json json, Piece.Requirement[] requirements)
        {
            json.OpenArray("requires");
            foreach (Piece.Requirement r in (requirements ?? Array.Empty<Piece.Requirement>()).Where(r => r?.m_resItem != null))
            {
                json.OpenObject();
                json.Field("item", r.m_resItem.gameObject.name);
                json.Field("amount", r.m_amount);
                json.Field("perLevel", r.m_amountPerLevel);
                if (!r.m_recover) json.Field("recover", false);
                json.CloseObject();
            }
            json.CloseArray();
        }

        private static void Named(Json json, string token)
        {
            json.Field("token", token);
            json.Field("name", Localization.instance != null ? Localization.instance.Localize(token) : token);
        }

        private static IEnumerable<GameObject> ScenePrefabs() =>
            ZNetScene.instance.m_prefabs.Where(p => p != null).Distinct();

        private static string StationName(CraftingStation station) => station != null ? station.gameObject.name : null;

        private static string PrefabName(ItemDrop item) => item != null ? item.gameObject.name : null;

        /// <summary>The mod that registered a prefab with FATI, or null (vanilla, or a mod that doesn't register; the vanilla-profile export settles which).</summary>
        private static string ModOf(string prefab) => ModNames.GetModByPrefabName(prefab)?.GUID;

        private static void Write(string text)
        {
            Ascend.Log.LogMessage(text);
            global::Console.instance?.Print(text);
            try
            {
                string folder = Path.Combine(Paths.BepInExRootPath, TestsFolder);
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, ResultsFile), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
            }
            catch (IOException e)
            {
                Ascend.Log.LogWarning($"{TestPrefix} could not write {ResultsFile}: {e.Message}");
            }
        }

        private static string DllHash()
        {
            try
            {
                using (SHA256 sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(typeof(ObjectDbExport).Assembly.Location))).Replace("-", "").Substring(0, HashChars);
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        private static string Role()
        {
            if (ZNet.instance != null && ZNet.instance.IsServer()) return "server";
            return Environment.GetCommandLineArgs().Contains("-firesbot") ? "bot" : "client";
        }

        private static string PlayerName() => Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "?";

        private static string WorldName() => ZNet.instance?.GetWorldName() ?? "?";

        /// <summary>
        /// A minimal JSON writer: objects and arrays opened and closed in order, commas and escaping handled, and counts
        /// of the top-level arrays for the step line.
        /// </summary>
        private sealed class Json
        {
            private readonly StringBuilder _text = new StringBuilder();
            private readonly Stack<bool> _hasItem = new Stack<bool>();
            private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
            private string _topArray;

            internal string Counts => string.Join(", ", _counts.Select(kv => $"{kv.Value} {kv.Key}"));

            internal void Open() => Begin(null, "{");

            internal void Close() => End("}");

            internal void OpenObject(string name = null)
            {
                if (name == null && _hasItem.Count == 2 && _topArray != null) _counts[_topArray]++;
                Begin(name, "{");
            }

            internal void CloseObject() => End("}");

            internal void OpenArray(string name)
            {
                if (_hasItem.Count == 1)
                {
                    _topArray = name;
                    _counts[name] = 0;
                }
                Begin(name, "[");
            }

            internal void CloseArray() => End("]");

            internal void Field(string name, string value) => Value(name, value == null ? "null" : Quote(value));

            internal void Field(string name, int value) => Value(name, value.ToString(CultureInfo.InvariantCulture));

            internal void Field(string name, float value) => Value(name, value.ToString(NumberFormat, CultureInfo.InvariantCulture));

            internal void Field(string name, bool value) => Value(name, value ? "true" : "false");

            public override string ToString() => _text.ToString();

            private void Begin(string name, string bracket)
            {
                Separate(name);
                _text.Append(bracket);
                _hasItem.Push(false);
            }

            private void End(string bracket)
            {
                _hasItem.Pop();
                _text.Append(bracket);
            }

            private void Value(string name, string literal)
            {
                Separate(name);
                _text.Append(literal);
            }

            private void Separate(string name)
            {
                if (_hasItem.Count > 0)
                {
                    if (_hasItem.Peek()) _text.Append(',');
                    _hasItem.Pop();
                    _hasItem.Push(true);
                }
                if (name != null) _text.Append(Quote(name)).Append(':');
            }

            private static string Quote(string s)
            {
                var sb = new StringBuilder(s.Length + 2).Append('"');
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else sb.Append(c);
                            break;
                    }
                }
                return sb.Append('"').ToString();
            }
        }
    }
}
