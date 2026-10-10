using UnityEngine;

namespace FiresAllTheItems.UI
{
    // FATI's sections for the shared FiresCore help panel (registration only).
    internal static class FatiHelpContent
    {
        private const string ModId = "Fires All The Items";
        private static bool _registered;

        public static void Register()
        {
            if (_registered || Application.isBatchMode) return;
            _registered = true;

            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "The FATI Item Browser", 0, BuildOverview);
            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "Search & Filters", 1, BuildSearch);
            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "Spawning & Recipes", 2, BuildSpawning);
            FiresCore.Help.HelpRegistry.RegisterSection(ModId, "", "Config Reference", 3, BuildConfig, adminOnly: true);
        }

        private static void BuildOverview(FiresCore.Help.HelpContentWriter w)
        {
            w.Header("The FATI Item Browser");
            w.Paragraph("Fires All The Items adds a browsable catalog of every item, piece, food, weapon, " +
                "and creature the game knows about — vanilla and modded — right inside the crafting panel.");
            w.Divider();

            w.SubHeader("Opening It");
            w.Bullet("Open your inventory. A new FATI tab sits beside Craft and Upgrade.");
            w.Bullet("Click it and the crafting pane becomes a scrollable icon grid.");
            w.Bullet("Click Craft or Upgrade to go back to vanilla crafting.");
            w.Divider();

            w.SubHeader("The Grid");
            w.Bullet("Default 12x6 icons; resize via UI.Items Horizontal / Items Vertical (1-25 each).");
            w.Bullet("Hover any icon for its full tooltip — stats, description, quality.");
            w.Bullet("Undiscovered items render greyed out until you've seen them in the world.");
            w.Bullet("Star an item to favorite it; favorites persist across sessions.");
        }

        private static void BuildSearch(FiresCore.Help.HelpContentWriter w)
        {
            w.Header("Search & Filters");
            w.SubHeader("Search Box");
            w.Paragraph("Space-separated terms narrow the grid. Prefix operators unlock precise queries:");
            w.CodeBlock("sword           name contains 'sword'\n" +
                "-iron           EXCLUDE matches\n" +
                "@modname        only items from that mod\n" +
                "#shield         only that ItemDrop type\n" +
                "^Bronze         items used as an INGREDIENT of Bronze");
            w.Divider();

            w.SubHeader("Category Toggles");
            w.Bullet("Buttons above the grid: Creature, Piece, Item, Food, Armor, Weapon.");
            w.Bullet("Left-click toggles a category on/off; right-click SOLOS it (only that category).");
            w.Bullet("The gold star at the end filters to your favorites only.");
            w.Divider();

            w.SubHeader("Blacklist");
            w.Paragraph("Known dev/test junk prefabs are hidden by default (General.Use Item Blacklist). " +
                "Extend the list yourself in config/FiresAllTheItems/blacklist.txt — one prefab per line.");
        }

        private static void BuildSpawning(FiresCore.Help.HelpContentWriter w)
        {
            w.Header("Spawning & Recipes");
            w.SubHeader("Recipes (everyone)");
            w.Bullet("Left-click any item to open its recipe view — ingredients, station, and what it's used in.");
            w.Bullet("Press R (Hotkeys.View Recipe Hotkey) over ANY tooltip — inventory, chest, or grid — " +
                "to jump straight to that item's recipes.");
            w.Divider();

            w.SubHeader("Spawning (devcommands + admin)");
            w.Paragraph("Right-click an icon to spawn the item into your inventory. Two things must be true: " +
                "you typed devcommands in the console (F5), and you are the host or on the server's admin list. " +
                "Browsing and recipes stay free for everyone.");
            w.Bullet("Hold Shift — spawn a full stack.");
            w.Bullet("Hold Ctrl — spawn the item's crafting INGREDIENTS instead of the item.");
            w.Bullet("Pieces spawn their build materials.");
            w.Bullet("Creatures spawn in front of you — only if Cheating.Enable Spawning Creatures is on.");
        }

        private static void BuildConfig(FiresCore.Help.HelpContentWriter w)
        {
            w.AdminHeader("Config Reference");
            w.Paragraph("com.Fire.FiresAllTheItems.cfg — all client-local.");
            w.Bullet("UI.Items Horizontal / Items Vertical — grid size (1-25)");
            w.Bullet("UI.Tab Name — label of the injected tab (default FATI)");
            w.Bullet("General.Use Item Blacklist — hide dev/test prefabs");
            w.Bullet("General.Show Only Known — hide undiscovered items entirely");
            w.Bullet("Cheating.Enable Cheating — allow spawning (still devcommands + admin gated at runtime)");
            w.Bullet("Cheating.Enable Spawning Creatures — allow creature spawns");
            w.Bullet("Cheating.Item Cheat Mouse Button — which button spawns (default Right)");
            w.Bullet("Hotkeys.View Recipe Hotkey — recipe jump key (default R)");
        }
    }
}
