using System;
using System.Collections.Generic;

namespace VolleyballCore
{
    /// <summary>
    /// Ported from src/cards.py's Deck.
    ///
    /// Standard flat 40-card deck (4 copies each of Ace(1)-10, locked ruleset
    /// 2026-09-05) or a modified "dummy" 28-card deck used for AI difficulty
    /// scaling.
    ///
    /// Core has no engine/file-I/O dependency (VolleyballCore.asmdef sets
    /// noEngineReferences: true), so unlike the Python Deck, this class does
    /// NOT read data/deck_types.csv itself. The two tables below are the
    /// hardcoded fallbacks -- the same role Python's _STANDARD_COUNTS /
    /// _DUMMY_COUNTS play when the CSV is absent. A caller outside Core (which
    /// can read the CSV, e.g. via Unity's StreamingAssets) may override them
    /// via the countsOverride constructor parameter.
    ///
    /// Note: this uses System.Random, not Python's random module -- the two
    /// use different algorithms, so an identical seed does NOT produce an
    /// identical card sequence across languages. That's expected; only the
    /// game *logic* needs to match, not the raw RNG stream.
    /// </summary>
    public sealed class Deck
    {
        private static readonly IReadOnlyDictionary<int, int> StandardCounts = new Dictionary<int, int>
        {
            { 1, 4 }, { 2, 4 }, { 3, 4 }, { 4, 4 }, { 5, 4 },
            { 6, 4 }, { 7, 4 }, { 8, 4 }, { 9, 4 }, { 10, 4 },
        };

        private static readonly IReadOnlyDictionary<int, int> DummyCounts = new Dictionary<int, int>
        {
            { 1, 2 }, { 2, 2 }, { 3, 2 }, { 4, 3 }, { 5, 2 },
            { 6, 2 }, { 7, 4 }, { 8, 5 }, { 9, 4 }, { 10, 2 },
        };

        private readonly Random _rng;
        private readonly List<Card> _drawPile = new();
        private readonly List<Card> _discardPile = new();

        public Deck(Random rng, string deckType = "standard", IReadOnlyDictionary<int, int> countsOverride = null)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            BuildAndShuffle(countsOverride ?? DefaultCountsFor(deckType));
        }

        public int DrawPileSize => _drawPile.Count;
        public int DiscardPileSize => _discardPile.Count;
        public int TotalSize => _drawPile.Count + _discardPile.Count;

        public Card Draw()
        {
            if (_drawPile.Count == 0)
            {
                if (_discardPile.Count == 0)
                    throw new InvalidOperationException("Both draw pile and discard pile are empty.");
                ReshuffleDiscard();
            }
            int lastIndex = _drawPile.Count - 1;
            Card card = _drawPile[lastIndex];
            _drawPile.RemoveAt(lastIndex);
            return card;
        }

        public void Discard(Card card) => _discardPile.Add(card);

        /// <summary>Return the top card without drawing it, or null if both piles are empty.</summary>
        public Card? Peek()
        {
            if (_drawPile.Count == 0)
            {
                if (_discardPile.Count == 0)
                    return null;
                ReshuffleDiscard();
            }
            return _drawPile[_drawPile.Count - 1];
        }

        private static IReadOnlyDictionary<int, int> DefaultCountsFor(string deckType)
        {
            string normalized = deckType?.Trim().ToLowerInvariant();
            return normalized == "dummy" ? DummyCounts : StandardCounts;
        }

        private void BuildAndShuffle(IReadOnlyDictionary<int, int> counts)
        {
            foreach (var pair in counts)
            {
                for (int i = 0; i < pair.Value; i++)
                {
                    CardColor color = i % 2 == 0 ? CardColor.Red : CardColor.Black;
                    _drawPile.Add(new Card(pair.Key, color));
                }
            }
            Shuffle(_drawPile);
        }

        private void ReshuffleDiscard()
        {
            _drawPile.AddRange(_discardPile);
            _discardPile.Clear();
            Shuffle(_drawPile);
        }

        private void Shuffle(List<Card> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
