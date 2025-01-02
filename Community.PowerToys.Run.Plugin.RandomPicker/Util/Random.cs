using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;


namespace Community.PowerToys.Run.Plugin.RandomPicker.Util
{

    public class RandomItem
    {
        /// <summary>
        /// Gets or sets the random item value
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Gets or sets the weight of the random item, signifying how relatively likely the item is to get picked
        /// </summary>
        public long Weight { get; set; }
    }

    public class RandomParser
    {
        /// <summary>
        /// Generates a list of RandomItem from a random definition
        /// </summary>
        /// <param name="randomDefinition">
        /// A string where items are delimited by semicolons (;).
        /// The items consist of a value part (any string except the delimiters), and an optional weight (positive whole number) part separated by a colon (:).
        /// The default weight is 1.
        /// </param>
        /// <returns>The parsed list of RandomItems</returns>
        public static List<RandomItem> Parse(string randomDefinition)
        {
            var itemDefinitions = randomDefinition.Split(";");
            var items = new List<RandomItem>();

            foreach (var itemDefinition in itemDefinitions)
            {
                var itemParts = itemDefinition.Split(":");
                try
                {
                    var randomItem = new RandomItem()
                    {
                        Value = itemParts[0],
                        Weight = itemParts.Length == 1 ? 1 : long.Parse(
                            itemParts[1],
                            NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                            CultureInfo.InvariantCulture),
                    };
                    items.Add(randomItem);
                }
                catch (OverflowException)
                {
                    throw new ArgumentException($"The weights must not exceed {long.MaxValue}");
                }
            }

            return items;
        }
    }

    public class RandomSelector
    {
        public static int Select(List<RandomItem> randomItems)
        {
            long lastWeightSum = 0;
            long weightSum = 0;
            foreach (RandomItem randomItem in randomItems)
            {
                weightSum += randomItem.Weight;
                if (weightSum < lastWeightSum)
                {
                    throw new ArgumentException($"The sum of the provided weights cannot be more than: {long.MaxValue}");
                }

                lastWeightSum = weightSum;
            }

            long randomWeightPoint = new Random().NextInt64(0, weightSum);
            for (var i = 0; i < randomItems.Count; i++)
            {
                if (randomWeightPoint < randomItems[i].Weight)
                {
                    return i;
                }

                randomWeightPoint -= randomItems[i].Weight;
            }

            throw new UnreachableException($"Random selection failed for unknown reason, this is a bug");
        }
    }
}
