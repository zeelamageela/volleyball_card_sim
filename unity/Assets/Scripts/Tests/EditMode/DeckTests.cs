using System;
using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    /// <summary>Ported from tests/test_game.py's TestDeck, updated for the flat 40-card deck.</summary>
    public class DeckTests
    {
        private static Deck MakeDeck(int seed = 0) => new Deck(new Random(seed));

        [Test]
        public void DeckHas40Cards()
        {
            Assert.AreEqual(40, MakeDeck().DrawPileSize);
        }

        [Test]
        public void DrawReducesPile()
        {
            var deck = MakeDeck();
            deck.Draw();
            Assert.AreEqual(39, deck.DrawPileSize);
        }

        [Test]
        public void DiscardIncreasesDiscardPile()
        {
            var deck = MakeDeck();
            var card = deck.Draw();
            deck.Discard(card);
            Assert.AreEqual(1, deck.DiscardPileSize);
        }

        [Test]
        public void ReshuffleWhenDrawEmpty()
        {
            var deck = MakeDeck();
            var cards = new Card[40];
            for (int i = 0; i < 40; i++)
            {
                cards[i] = deck.Draw();
            }
            foreach (var c in cards)
            {
                deck.Discard(c);
            }

            Assert.AreEqual(0, deck.DrawPileSize);
            Assert.AreEqual(40, deck.DiscardPileSize);

            deck.Draw(); // triggers reshuffle
            Assert.AreEqual(39, deck.DrawPileSize);
            Assert.AreEqual(0, deck.DiscardPileSize);
        }

        [Test]
        public void ThrowsWhenBothPilesEmpty()
        {
            var deck = MakeDeck();
            for (int i = 0; i < 40; i++)
            {
                deck.Draw();
            }
            Assert.Throws<InvalidOperationException>(() => deck.Draw());
        }

        [Test]
        public void DeckHas4CopiesOfEachValue()
        {
            var deck = MakeDeck();
            var counts = new int[11]; // index 1..10
            for (int i = 0; i < 40; i++)
            {
                counts[deck.Draw().Value]++;
            }
            for (int v = 1; v <= 10; v++)
            {
                Assert.AreEqual(4, counts[v], $"value {v}");
            }
        }

        [Test]
        public void DummyDeckHas28Cards()
        {
            var deck = new Deck(new Random(0), "dummy");
            Assert.AreEqual(28, deck.DrawPileSize);
        }

        [Test]
        public void PeekDoesNotRemoveCardAndMatchesNextDraw()
        {
            var deck = MakeDeck();
            var peeked = deck.Peek();

            Assert.IsNotNull(peeked);
            Assert.AreEqual(40, deck.DrawPileSize);
            Assert.AreEqual(peeked.Value, deck.Draw());
        }

        [Test]
        public void PeekReturnsNullWhenBothPilesEmpty()
        {
            var deck = MakeDeck();
            for (int i = 0; i < 40; i++)
            {
                deck.Draw();
            }
            Assert.IsNull(deck.Peek());
        }
    }
}
