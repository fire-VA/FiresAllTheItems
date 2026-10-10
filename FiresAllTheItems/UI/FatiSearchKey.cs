namespace FiresAllTheItems.UI
{
    // Port of VNEI UI/SearchKey.cs — one space-separated search token with its prefix
    // semantics: '-'=negate, '@'=mod name, '#'=ItemDrop type, '^'=used-as-ingredient-of.
    public class FatiSearchKey
    {
        public string key;
        public readonly bool isNegative;
        public readonly bool isMod;
        public readonly bool isItemDropType;
        public readonly bool isIngredient;

        public FatiSearchKey(string searchKey)
        {
            key = searchKey;
            isNegative = key.StartsWith("-");
            if (isNegative)
                key = key.Substring(1);
            isMod = key.StartsWith("@");
            isItemDropType = key.StartsWith("#");
            isIngredient = key.StartsWith("^");
            if (isMod || isItemDropType || isIngredient)
                key = key.Substring(1);
        }
    }
}
