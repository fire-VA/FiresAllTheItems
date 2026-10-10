using UnityEngine;

namespace FiresAllTheItems.Logic
{
    // Port of VNEI Logic/Amount.cs — min/max count with a drop chance, e.g. "15% 2-3x".
    public readonly struct Amount
    {
        public readonly int min;
        public readonly int max;
        public readonly float chance;

        public static Amount Zero { get; } = new Amount(0, 0, 0f);
        public static Amount One { get; } = new Amount(1);

        public Amount(int min, int max, float chance = 1f)
        {
            this.min = min;
            this.max = max;
            this.chance = chance;
        }

        public Amount(int amount, float chance = 1f)
        {
            min = amount;
            max = amount;
            this.chance = chance;
        }

        public static bool IsSameMinMax(Amount a, Amount b) => a.min == b.min && a.max == b.max;

        public override string ToString()
        {
            int pct = Mathf.RoundToInt(chance * 100f);
            string str = "";
            bool hasChance = false;
            if (pct != 100)
            {
                str += $"{pct}% ";
                hasChance = true;
            }
            if (min == max)
            {
                if (!hasChance || max != 1)
                    str += $"{max}x";
            }
            else
            {
                str += $"{min}-{max}x";
            }
            return str;
        }

        public bool Equals(Amount other) => min == other.min && max == other.max && chance.Equals(other.chance);

        public override bool Equals(object obj) => obj is Amount other && Equals(other);

        public override int GetHashCode() => (min * 397 ^ max) * 397 ^ chance.GetHashCode();
    }
}
