using System.Collections.Generic;
using System.Linq;

namespace VolleyballData
{
    /// <summary>
    /// Ported from the CSV-reading portion of src/cards.py's Deck._load_counts.
    /// Core's Deck already carries its own hardcoded standard/dummy fallback
    /// tables (used when no override is supplied), so this loader's job is just
    /// to read data/deck_types.csv directly -- callers pass the result for a
    /// given deck type as Deck's countsOverride parameter.
    /// </summary>
    public static class DeckCountsLoader
    {
        public static Dictionary<string, Dictionary<int, int>> Load(string csvPath)
        {
            var countsByType = new Dictionary<string, Dictionary<int, int>>();
            foreach (var row in CsvUtil.ReadRows(csvPath))
            {
                string deckType = CsvUtil.Get(row, "deck_type").Trim().ToLowerInvariant();
                if (deckType.Length == 0)
                {
                    continue;
                }
                if (!int.TryParse(CsvUtil.Get(row, "card_value").Trim(), out int value))
                {
                    continue;
                }
                if (!int.TryParse(CsvUtil.Get(row, "count").Trim(), out int count))
                {
                    continue;
                }
                if (value < 1 || value > 10)
                {
                    continue;
                }
                if (!countsByType.TryGetValue(deckType, out var counts))
                {
                    counts = new Dictionary<int, int>();
                    countsByType[deckType] = counts;
                }
                if (count <= 0)
                {
                    counts.Remove(value);
                }
                else
                {
                    counts[value] = count;
                }
            }

            foreach (string key in countsByType.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList())
            {
                countsByType.Remove(key);
            }
            return countsByType;
        }
    }
}
