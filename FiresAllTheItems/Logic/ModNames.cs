using System.Collections.Generic;
using BepInEx;

namespace FiresAllTheItems.Logic
{
    // Port of VNEI Logic/ModNames.cs minus Jotunn ModQuery. Unattributed prefabs display
    // as "Valheim" (VNEI's null behavior). Other Fires mods self-register their prefabs via
    // SetModOfPrefab; automatic registration-hook attribution is a planned v2
    // (Tools\FIRES_ALLTHEITEMS_GAMEPLAN.md).
    public static class ModNames
    {
        private static readonly Dictionary<string, BepInPlugin> SourceMod = new Dictionary<string, BepInPlugin>();

        public static void SetModOfPrefab(string prefabName, BepInPlugin mod)
        {
            if (SourceMod.ContainsKey(prefabName))
                return;
            SourceMod[prefabName] = mod;
        }

        public static BepInPlugin GetModByPrefabName(string name)
        {
            return SourceMod.TryGetValue(name, out BepInPlugin plugin) ? plugin : null;
        }
    }
}
