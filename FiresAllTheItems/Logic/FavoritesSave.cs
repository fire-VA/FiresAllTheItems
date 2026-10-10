using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace FiresAllTheItems.Logic
{
    // Port of VNEI Logic/FavouritesSave.cs — one internal name per line under the Fires
    // config folder. Loaded after indexing, saved on every star toggle.
    public static class FavoritesSave
    {
        private static string GetFilePath()
        {
            string dir = Path.Combine(Paths.ConfigPath, "FiresAllTheItems");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "favourites.txt");
        }

        public static void Load()
        {
            try
            {
                string path = GetFilePath();
                if (!File.Exists(path))
                    return;
                foreach (string line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrEmpty(line))
                        continue;
                    Item item = Indexing.GetItem(line);
                    if (item != null)
                        item.isFavorite = true;
                }
            }
            catch (System.Exception ex)
            {
                Ascend.Log?.LogWarning($"Favorites load failed: {ex.Message}");
            }
        }

        public static void Save()
        {
            try
            {
                using (StreamWriter writer = File.CreateText(GetFilePath()))
                {
                    foreach (KeyValuePair<string, Item> activeItem in Indexing.GetActiveItems())
                    {
                        if (activeItem.Value.isFavorite)
                            writer.WriteLine(activeItem.Value.internalName);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Ascend.Log?.LogWarning($"Favorites save failed: {ex.Message}");
            }
        }
    }
}
