using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace FiresAllTheItems.Logic
{
    public static class ItemValidityFilter
    {
        public enum HiddenReason
        {
            NoIcon,
            CreatureOnly
        }

        private const string ConfigFolderName = "FiresAllTheItems";
        private const string AlwaysShowFileName = "alwaysshow.txt";
        private const string CommentPrefix = "#";
        private const int NamesLoggedPerReason = 25;

        private static readonly Dictionary<string, string> CarrierByItem = new Dictionary<string, string>();
        private static readonly Dictionary<string, List<string>> LoadoutByMonster = new Dictionary<string, List<string>>();
        private static readonly HashSet<string> IconlessItems = new HashSet<string>();
        private static readonly Dictionary<string, HiddenReason> HiddenItems = new Dictionary<string, HiddenReason>();
        private static HashSet<string> _alwaysShow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Collect(List<GameObject> prefabs)
        {
            CarrierByItem.Clear();
            LoadoutByMonster.Clear();
            IconlessItems.Clear();
            HiddenItems.Clear();
            _alwaysShow = LoadAlwaysShow();
            foreach (GameObject prefab in prefabs)
            {
                RecordIfIconless(prefab);
                if (prefab.TryGetComponent(out Humanoid humanoid))
                    RecordCarriedItems(prefab.name, humanoid);
            }
        }

        public static void Apply()
        {
            foreach (string name in IconlessItems)
                Hide(name, HiddenReason.NoIcon);
            foreach (string name in CarrierByItem.Keys)
                HideIfObtainableNowhere(name);
            LogSummary();
        }

        public static bool TryGetHiddenReason(string name, out HiddenReason reason) => HiddenItems.TryGetValue(name, out reason);

        public static IReadOnlyList<string> GetMonsterLoadout(string monster) =>
            LoadoutByMonster.TryGetValue(monster, out List<string> items) ? items : (IReadOnlyList<string>)Array.Empty<string>();

        public static IEnumerable<string> MonstersWithLoadouts => LoadoutByMonster.Keys;

        private static void RecordIfIconless(GameObject prefab)
        {
            if (prefab.TryGetComponent(out ItemDrop itemDrop) && !HasUsableIcon(itemDrop))
                IconlessItems.Add(prefab.name);
        }

        private static void RecordCarriedItems(string carrier, Humanoid humanoid)
        {
            List<string> loadout = CarriedPrefabs(humanoid).Select(prefab => prefab.name).Distinct().ToList();
            if (loadout.Count > 0)
                LoadoutByMonster[carrier] = loadout;
            foreach (GameObject carried in CarriedPrefabs(humanoid))
            {
                if (carried.TryGetComponent(out ItemDrop itemDrop) && HasUsableIcon(itemDrop) && !CarrierByItem.ContainsKey(carried.name))
                    CarrierByItem[carried.name] = carrier;
            }
        }

        private static IEnumerable<GameObject> CarriedPrefabs(Humanoid humanoid)
        {
            IEnumerable<GameObject> setItems = (humanoid.m_randomSets ?? Array.Empty<Humanoid.ItemSet>())
                .Where(set => set?.m_items != null)
                .SelectMany(set => set.m_items);
            return JoinArrays(humanoid.m_defaultItems, humanoid.m_randomWeapon, humanoid.m_randomShield, humanoid.m_randomArmor)
                .Concat(setItems)
                .Where(prefab => prefab != null);
        }

        private static IEnumerable<GameObject> JoinArrays(params GameObject[][] arrays) => arrays.Where(array => array != null).SelectMany(array => array);

        private static bool HasUsableIcon(ItemDrop itemDrop)
        {
            Sprite[] icons = itemDrop.m_itemData?.m_shared?.m_icons;
            return icons != null && icons.Length > 0 && icons[0] != null;
        }

        private static void HideIfObtainableNowhere(string name)
        {
            Item item = Indexing.GetItem(name);
            if (item != null && item.result.Count == 0 && item.ingredient.Count == 0)
                Hide(name, HiddenReason.CreatureOnly);
        }

        private static void Hide(string name, HiddenReason reason)
        {
            Item item = Indexing.GetItem(name);
            if (item == null || !item.isActive)
                return;
            if (_alwaysShow.Contains(name))
            {
                Ascend.Log?.LogInfo($"Validity filter kept '{name}' visible ({reason}): listed in {AlwaysShowFileName}");
                return;
            }
            Indexing.DisableItem(name, reason.ToString());
            HiddenItems[name] = reason;
        }

        private static HashSet<string> LoadAlwaysShow()
        {
            string path = Path.Combine(Paths.ConfigPath, ConfigFolderName, AlwaysShowFileName);
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, string.Empty);
            }
            IEnumerable<string> names = File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith(CommentPrefix));
            return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        }

        private static void LogSummary()
        {
            foreach (IGrouping<HiddenReason, string> group in HiddenItems.GroupBy(entry => entry.Value, entry => entry.Key))
            {
                List<string> names = group.OrderBy(name => name).Select(name => DescribeItem(name, group.Key)).ToList();
                string listed = string.Join(", ", names.Take(NamesLoggedPerReason));
                string remainder = names.Count > NamesLoggedPerReason ? $" (+{names.Count - NamesLoggedPerReason} more at Debug)" : string.Empty;
                Ascend.Log?.LogInfo($"Validity filter hid {names.Count} items - {DescribeReason(group.Key)}: {listed}{remainder}");
                Ascend.Log?.LogDebug($"Validity filter, all {group.Key}: {string.Join(", ", names)}");
            }
        }

        private static string DescribeItem(string name, HiddenReason reason) =>
            reason == HiddenReason.CreatureOnly ? $"{name} ({CarrierByItem[name]})" : name;

        private static string DescribeReason(HiddenReason reason) =>
            reason == HiddenReason.NoIcon ? "no icon, so they can't be held" : "carried by creatures and obtainable nowhere";
    }
}
