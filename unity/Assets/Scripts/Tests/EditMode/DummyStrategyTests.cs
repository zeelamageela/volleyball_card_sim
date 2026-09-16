using System.Collections.Generic;
using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    /// <summary>Ported behavior checks for src/strategies.py's DummyStrategy.</summary>
    public class DummyStrategyTests
    {
        private readonly DummyStrategy _strat = new();

        private static Card C(int v, CardColor color = CardColor.Red) => new(v, color);

        [Test]
        public void ChooseServeTargetsFirstReceiverOnEvenCard()
        {
            var hand = new List<Card> { C(4) };
            var receivers = new List<GridPlayer>
            {
                new(PlayerRole.Ds, 5), new(PlayerRole.Libero, 6),
            };
            var (card, receiver) = _strat.ChooseServe(hand, receivers);
            Assert.AreEqual(4, card.Value);
            Assert.AreEqual(PlayerRole.Ds, receiver.Role);
        }

        [Test]
        public void ChooseServeTargetsLastReceiverOnOddCard()
        {
            var hand = new List<Card> { C(5) };
            var receivers = new List<GridPlayer>
            {
                new(PlayerRole.Ds, 5), new(PlayerRole.Libero, 6),
            };
            var (_, receiver) = _strat.ChooseServe(hand, receivers);
            Assert.AreEqual(PlayerRole.Libero, receiver.Role);
        }

        [Test]
        public void ChooseHitCardsUsesTopCardOnFirstFrontLane()
        {
            var hand = new List<Card> { C(9), C(2) };
            var template = new SetTemplate(new[] { 3, 1 }, new[] { 2 }, 2);
            var placements = _strat.ChooseHitCards(hand, template);
            Assert.AreEqual(1, placements.Count);
            Assert.AreEqual(3, placements[0].Lane); // first element of front_lanes as given, not sorted
            Assert.AreEqual(9, placements[0].Card.Value);
            Assert.AreEqual(AttackPosition.Front, placements[0].Position);
        }

        [Test]
        public void ChooseBlockCardsSingleLaneAllThreeBlockersConverge()
        {
            // Lane 2 (MB's home) is reachable by all three front-row roles, so a
            // single-lane attack there triple-blocks -- each blind blocker just takes
            // the next unused hand card, in Oh/Mb/Opp order.
            var hand = new List<Card> { C(1), C(2), C(3), C(4) };
            var result = _strat.ChooseBlockCards(hand, new List<int> { 2 });
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual((2, C(1)), result[PlayerRole.Oh]);
            Assert.AreEqual((2, C(2)), result[PlayerRole.Mb]);
            Assert.AreEqual((2, C(3)), result[PlayerRole.Opp]);
        }

        [Test]
        public void ChooseBlockCardsTwoOutsideLanesDoublesLeftmostViaMb()
        {
            // Neither lane 1 nor 3 is MB's home, so MB blindly falls back to whichever
            // comes first in its own reachable-lanes order (1, 2, 3) -- lane 1 here --
            // doubling it, while OPP (whose home, 3, IS attacked) covers lane 3 alone.
            var hand = new List<Card> { C(2), C(4), C(6) };
            var result = _strat.ChooseBlockCards(hand, new List<int> { 1, 3 });
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual((1, C(2)), result[PlayerRole.Oh]);
            Assert.AreEqual((1, C(4)), result[PlayerRole.Mb]);
            Assert.AreEqual((3, C(6)), result[PlayerRole.Opp]);
        }

        [Test]
        public void ChooseBlockCardsThreeLanesSpreadsOneCardEach()
        {
            // Every blocker's own home lane is attacked, so each covers their own --
            // full spread, no doubling.
            var hand = new List<Card> { C(1), C(2), C(3) };
            var result = _strat.ChooseBlockCards(hand, new List<int> { 1, 2, 3 });
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual((1, C(1)), result[PlayerRole.Oh]);
            Assert.AreEqual((2, C(2)), result[PlayerRole.Mb]);
            Assert.AreEqual((3, C(3)), result[PlayerRole.Opp]);
        }

        [Test]
        public void ChooseTipOrHitAlwaysTips()
        {
            Assert.AreEqual("tip", _strat.ChooseTipOrHit(3, 10));
        }

        [Test]
        public void CoverDrawsFromDeckIsTrue()
        {
            Assert.IsTrue(_strat.CoverDrawsFromDeck());
        }

        [Test]
        public void ChooseCoverAttemptNeverCalled_ReturnsNull()
        {
            Assert.IsNull(_strat.ChooseCoverAttempt(new List<Card> { C(9) }, 5));
        }

        [Test]
        public void NeverExchanges()
        {
            Assert.IsNull(_strat.ChooseExchangeCard(new List<Card> { C(1) }, C(10)));
        }
    }
}
