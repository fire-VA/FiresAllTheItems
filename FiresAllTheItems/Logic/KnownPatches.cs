using HarmonyLib;

namespace FiresAllTheItems.Logic
{
    // Port of VNEI Patches/KnownRecipesPatches.cs — refresh known-state when the player
    // discovers something. Indexing.UpdateKnown is change-hash gated, so these are cheap.
    [HarmonyPatch]
    public static class KnownPatches
    {
        // Never let our refresh throw inside vanilla discovery call sites — a failure here
        // spams every AddKnownItem (proven by the 0.0.1 FieldAccessException relay storm).
        private static void SafeUpdateKnown()
        {
            try { Indexing.UpdateKnown(); }
            catch (System.Exception ex) { Ascend.Log?.LogWarning($"UpdateKnown failed: {ex.Message}"); }
        }

        [HarmonyPatch(typeof(Player), "AddKnownItem")]
        [HarmonyPostfix]
        public static void AddKnownItemPatch() => SafeUpdateKnown();

        [HarmonyPatch(typeof(Player), "AddKnownRecipe")]
        [HarmonyPostfix]
        public static void AddKnownRecipePatch() => SafeUpdateKnown();

        [HarmonyPatch(typeof(Player), "AddKnownStation")]
        [HarmonyPostfix]
        public static void AddKnownStationPatch() => SafeUpdateKnown();

        [HarmonyPatch(typeof(Player), "UpdateKnownRecipesList")]
        [HarmonyPostfix]
        public static void UpdateKnownRecipesListPatch() => SafeUpdateKnown();
    }
}
