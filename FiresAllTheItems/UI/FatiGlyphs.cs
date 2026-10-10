using System.Reflection;
using FiresCore.UI.Minigames;
using UnityEngine;

namespace FiresAllTheItems.UI
{
    // Flat line-icon glyphs embedded in the DLL as 64px PNGs (Tabler Icons, MIT - see
    // THIRDPARTY-NOTICES.md), used for the category toggle row and the search field.
    // Vanilla has no icon set that reads at 24px: the scavenged sprites the toggles used
    // before (pvp_on for weapons, ac_bkg for armor) were stand-ins, not symbols.
    //
    // Decoding is Core's job. FiresMinigameSprites owns embedded-PNG-to-Sprite for the whole family,
    // caches per assembly and name, and drives Texture2D.LoadImage by reflection - a direct call would
    // drag UnityEngine.ImageConversionModule and netstandard 2.1 into this net48 build. This file only
    // names the resources.
    //
    // Safe against an older Core: that helper landed in Core 0.1.75 and this mod already declares a
    // minimum of 0.2.31, so any Core that satisfies the BepInDependency carries it.
    public static class FatiGlyphs
    {
        public const string Undefined = "fati_undefined.png";
        public const string Creature = "fati_creature.png";
        public const string Piece = "fati_piece.png";
        public const string Item = "fati_item.png";
        public const string Food = "fati_food.png";
        public const string Armor = "fati_armor.png";
        public const string Weapon = "fati_weapon.png";
        public const string Favorite = "fati_favorite.png";
        public const string Search = "fati_search.png";
        public const string Preset = "fati_preset.png";

        public static Sprite Get(string fileName) =>
            string.IsNullOrEmpty(fileName)
                ? null
                : FiresMinigameSprites.Load(Assembly.GetExecutingAssembly(), fileName);
    }
}
