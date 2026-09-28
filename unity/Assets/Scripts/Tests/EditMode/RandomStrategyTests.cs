using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    /// <summary>
    /// Ported behavior checks for src/strategies.py's RandomStrategy. Since every
    /// decision is randomized, these assert legality/shape properties across many
    /// seeds rather than exact values.
    /// </summary>
    public class RandomStrategyTests
    {
        private static Card C(int v, CardColor color = CardColor.Red) => new(v, color);

        [Test]
        public void ChooseHitCardsStaysWithinLimitsAndUsesDistinctHandCards()
        {
            var hand = new List<Card> { C(9), C(8), C(7), C(2), C(1) };
            var template = new SetTemplate(new[] { 1, 2, 3 }, new[] { 1, 2, 3 }, 3);

            for (int seed = 0; seed < 100; seed++)
            {
                var strat = new RandomStrategy(new Random(seed));
                var placements = strat.ChooseHitCards(hand, template);

                Assert.GreaterOrEqual(placements.Count, 1, $"seed {seed}");
                Assert.LessOrEqual(placements.Count, template.MaxAttackers, $"seed {seed}");
                foreach (var (lane, card, position) in placements)
                {
                    Assert.Contains(lane, new List<int> { 1, 2, 3 }, $"seed {seed}");
                    Assert.Contains(card, hand, $"seed {seed}");
                }
                Assert.AreEqual(
                    placements.Count,
                    placements.Select(p => p.Card).Distinct().Count(),
                    $"seed {seed} -- duplicate card used");
            }
        }

        [Test]
        public void ChooseBlockCardsNeverExceedsHandSizeOrThreeBlockersAndStaysWithinReach()
        {
            var hand = new List<Card> { C(9), C(3), C(5) };
            var attackLanes = new List<int> { 1, 2 };
            for (int seed = 0; seed < 50; seed++)
            {
                var strat = new RandomStrategy(new Random(seed));
                var result = strat.ChooseBlockCards(hand, attackLanes);
                Assert.LessOrEqual(result.Count, hand.Count, $"seed {seed}");
                Assert.LessOrEqual(result.Count, 3, $"seed {seed}");
                foreach (var kv in result)
                {
                    Assert.Contains(kv.Value.Lane, attackLanes, $"seed {seed}");
                    CollectionAssert.Contains(PlayerRoleExtensions.BlockableLanes[kv.Key], kv.Value.Lane, $"seed {seed}");
                }
            }
        }

        [Test]
        public void ChooseAttackLaneReturnsAKeyFromAttackCards()
        {
            var attackCards = new Dictionary<int, Card> { { 1, C(5) }, { 3, C(7) } };
            var blockLayout = new Dictionary<int, int> { { 1, 2 }, { 3, 8 } };
            var strat = new RandomStrategy(new Random(0));
            int lane = strat.ChooseAttackLane(attackCards, blockLayout);
            Assert.Contains(lane, new List<int> { 1, 3 });
        }

        [Test]
        public void ChooseTipOrHitNeverTipsAboveFour()
        {
            var strat = new RandomStrategy(new Random(0));
            for (int i = 0; i < 30; i++)
            {
                Assert.AreEqual("hit", strat.ChooseTipOrHit(5, 10));
            }
        }

        [Test]
        public void NeverExchanges()
        {
            var strat = new RandomStrategy(new Random(0));
            Assert.IsNull(strat.ChooseExchangeCard(new List<Card> { C(1) }, C(10)));
        }

        [Test]
        public void CoverDrawsFromDeckIsFalse()
        {
            Assert.IsFalse(new RandomStrategy(new Random(0)).CoverDrawsFromDeck());
        }
    }
}
