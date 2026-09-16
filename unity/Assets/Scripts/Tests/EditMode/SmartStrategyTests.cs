using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    /// <summary>Ported behavior checks for src/strategies.py's SmartStrategy.</summary>
    public class SmartStrategyTests
    {
        private static Card C(int v, CardColor color = CardColor.Red) => new(v, color);
        private static SmartStrategy Strat(int seed = 0) => new(new Random(seed));

        [Test]
        public void ChooseServePlaysHighestCard()
        {
            var hand = new List<Card> { C(3), C(9), C(5) };
            var receivers = new List<GridPlayer> { new(PlayerRole.Ds, 5) };
            var (card, _) = Strat().ChooseServe(hand, receivers);
            Assert.AreEqual(9, card.Value);
        }

        [Test]
        public void ChooseReceiveCardPlaysMinimumSufficientCard()
        {
            var hand = new List<Card> { C(3), C(7), C(9) };
            Assert.AreEqual(7, Strat().ChooseReceiveCard(hand, 6).Value);
        }

        [Test]
        public void ChooseReceiveCardPlaysLowestWhenNoneSucceed()
        {
            var hand = new List<Card> { C(3), C(5), C(2) };
            Assert.AreEqual(2, Strat().ChooseReceiveCard(hand, 10).Value);
        }

        [Test]
        public void ChooseSetCardNormalPrefersMidHigh()
        {
            var hand = new List<Card> { C(2), C(7), C(10) };
            Assert.AreEqual(7, Strat().ChooseSetCard(hand).Value);
        }

        [Test]
        public void ChooseSetCardBrokenPlayPrefersHighThenLow()
        {
            var hand = new List<Card> { C(5), C(9), C(2) };
            Assert.AreEqual(9, Strat().ChooseSetCard(hand, brokenPlay: true).Value);
        }

        [Test]
        public void ChooseTipOrHitTipsOnlyWhenBlockIsStrong()
        {
            Assert.AreEqual("tip", Strat().ChooseTipOrHit(2, 6)); // block > attack+2
            Assert.AreEqual("hit", Strat().ChooseTipOrHit(2, 3)); // block not strong enough
            Assert.AreEqual("hit", Strat().ChooseTipOrHit(5, 10)); // attack > 4, never tips
        }

        [Test]
        public void ChooseDigCardNormalPlaysLowestQualifying()
        {
            var hand = new List<Card> { C(9), C(6), C(3) };
            Assert.AreEqual(6, Strat().ChooseDigCard(hand, 5, DigType.Normal).Value);
        }

        [Test]
        public void ChooseDigCardTipPlaysHighestQualifying()
        {
            var hand = new List<Card> { C(2), C(4), C(9) };
            Assert.AreEqual(4, Strat().ChooseDigCard(hand, 5, DigType.Tip).Value);
        }

        [Test]
        public void ChooseChaseCardPlaysMinimumSufficientCard()
        {
            var hand = new List<Card> { C(2), C(5), C(8) };
            // need 4 more to reach target from running total
            Assert.AreEqual(5, Strat().ChooseChaseCard(hand, 6, 10).Value);
        }

        [Test]
        public void ChooseExchangeCardSwapsWorstWhenDeckTopSignificantlyBetter()
        {
            var hand = new List<Card> { C(2), C(6), C(8) };
            Assert.AreEqual(2, Strat().ChooseExchangeCard(hand, C(10)).Value.Value);
            Assert.IsNull(Strat().ChooseExchangeCard(hand, C(4)));
        }

        [Test]
        public void ChooseCoverAttemptPicksLowestQualifying()
        {
            var hand = new List<Card> { C(9), C(6), C(3) };
            Assert.AreEqual(6, Strat().ChooseCoverAttempt(hand, 5).Value.Value);
            Assert.IsNull(Strat().ChooseCoverAttempt(hand, 10));
        }

        [Test]
        public void ChooseAttackLanePrefersBestScoreAmongKnownLanes()
        {
            var attackCards = new Dictionary<int, Card> { { 1, C(8) }, { 2, C(6) }, { 3, C(0) } };
            var blockLayout = new Dictionary<int, int> { { 1, 5 }, { 2, 1 } };
            // lane 1 score = 8-5=3, lane 2 score = 6-1=5, lane 3 excluded (blind, value=0)
            Assert.AreEqual(2, Strat().ChooseAttackLane(attackCards, blockLayout));
        }

        [Test]
        public void ChooseHitCardsNeverExceedsMaxAttackers()
        {
            var rng = new Random(1);
            for (int trial = 0; trial < 50; trial++)
            {
                var hand = new List<Card> { C(9), C(8), C(7), C(2), C(1) };
                var template = new SetTemplate(new[] { 1, 2, 3 }, new[] { 1, 2, 3 }, 3);
                var placements = Strat(trial).ChooseHitCards(hand, template);
                Assert.LessOrEqual(placements.Count, template.MaxAttackers);
                // all cards distinct and from hand
                Assert.AreEqual(placements.Count, placements.Select(p => p.Card).Distinct().Count());
            }
        }

        [Test]
        public void ChooseBlockCardsSingleOutsideLaneOnlyReachableBlockersConverge()
        {
            // Lane 1 can only be reached by OH (home) and MB (adjacent) -- OPP can't
            // help there at all -- so exactly those two converge on it.
            var hand = new List<Card> { C(7), C(5), C(3) };
            var result = Strat().ChooseBlockCards(hand, new List<int> { 1 });
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.ContainsKey(PlayerRole.Oh));
            Assert.IsTrue(result.ContainsKey(PlayerRole.Mb));
            Assert.IsFalse(result.ContainsKey(PlayerRole.Opp));
            Assert.AreEqual(1, result[PlayerRole.Oh].Lane);
            Assert.AreEqual(1, result[PlayerRole.Mb].Lane);
        }

        [Test]
        public void ChooseBlockCardsSpreadsCoverageBeforeDoublingUp()
        {
            // Two attacked lanes, three blockers available: every attacked lane should
            // get at least one blocker before any lane gets a second.
            var hand = new List<Card> { C(7), C(5), C(3) };
            var result = Strat().ChooseBlockCards(hand, new List<int> { 1, 3 });
            Assert.AreEqual(3, result.Count);
            var lanesUsed = result.Values.Select(v => v.Lane).ToList();
            Assert.Contains(1, lanesUsed);
            Assert.Contains(3, lanesUsed);
        }
    }
}
