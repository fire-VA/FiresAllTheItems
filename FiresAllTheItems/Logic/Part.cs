namespace FiresAllTheItems.Logic
{
    // Port of VNEI Logic/Part.cs — one item slot inside a recipe (item + count + quality).
    // Value equality matters: RecipeInfo.CombineGroupAmounts compares part lists.
    public class Part
    {
        public readonly Item item;
        public readonly Amount amount;
        public readonly int quality;

        public Part(Item item, Amount amount, int quality)
        {
            this.item = item;
            this.amount = amount;
            this.quality = quality;
        }

        public override bool Equals(object obj)
        {
            return obj is Part other && ReferenceEquals(item, other.item) && amount.Equals(other.amount) && quality == other.quality;
        }

        public override int GetHashCode()
        {
            int hash = item != null ? item.GetHashCode() : 0;
            return (hash * 397 ^ amount.GetHashCode()) * 397 ^ quality;
        }
    }
}
