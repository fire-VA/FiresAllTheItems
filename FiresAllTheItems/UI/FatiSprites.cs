using System.Collections.Generic;
using UnityEngine;

namespace FiresAllTheItems.UI
{
    // Vanilla sprite scavenger — replaces Jotunn GUIManager.GetSprite. One lazy sweep of all
    // loaded sprites into a name→sprite cache (first hit wins), then dictionary lookups.
    // Same sprites VNEI styled with: item_background, mapicon_trader/bed, food_icon, ac_bkg,
    // pvp_on, texts_button.
    public static class FatiSprites
    {
        private static Dictionary<string, Sprite> _cache;

        public static Sprite NoIcon => Get("hammer_icon");

        public static Sprite Get(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            EnsureCache();
            return _cache.TryGetValue(name, out Sprite sprite) && sprite != null ? sprite : null;
        }

        private static void EnsureCache()
        {
            if (_cache != null)
                return;
            _cache = new Dictionary<string, Sprite>(System.StringComparer.OrdinalIgnoreCase);
            foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite == null || string.IsNullOrEmpty(sprite.name))
                    continue;
                if (!_cache.ContainsKey(sprite.name))
                    _cache[sprite.name] = sprite;
            }
            Ascend.Log?.LogDebug($"FatiSprites cached {_cache.Count} loaded sprites");
        }

        // A GUI rebuild (logout/login) can unload sprites the cache points at; callers that
        // survive scene loads should Reset on InventoryGui.Awake.
        public static void Reset() => _cache = null;
    }
}
