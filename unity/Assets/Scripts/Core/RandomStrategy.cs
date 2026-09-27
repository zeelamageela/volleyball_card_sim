using System;
using System.Collections.Generic;
using System.Linq;

namespace VolleyballCore
{
    /// <summary>All decisions made uniformly at random within legal constraints.
    /// Ported from src/strategies.py's RandomStrategy.</summary>
    public sealed class RandomStrategy : IStrategy
    {
        private readonly Random _rng;

        public RandomStrategy(Random rng) => _rng = rng;

        public (Card Card, GridPlayer Receiver) ChooseServe(List<Card> hand, List<GridPlayer> eligibleReceivers) =>
            (_rng.Choice(hand), _rng.Choice(eligibleReceivers));

        public GridPlayer ChooseFreeBallTarget(List<GridPlayer> eligibleReceivers) => _rng.Choice(eligibleReceivers);

        public Card ChooseReceiveCard(List<Card> hand, int serveValue) => _rng.Choice(hand);

        public Card ChooseSetCard(List<Card> hand, bool brokenPlay = false) => _rng.Choice(hand);

        public List<(int Lane, Card Card, AttackPosition Position)> ChooseHitCards(List<Card> hand, SetTemplate template)
        {
            var available = new List<Card>(hand);
            var result = new List<(int, Card, AttackPosition)>();
            int maxCards = Math.Min(template.MaxAttackers, available.Count);

            // Randomly decide how many cards to place (1 to maxCards)
            int numToPlace = _rng.Next(1, maxCards + 1);

            // Build pool of available (lane, position) slots
            var slots = new List<(int Lane, AttackPosition Position)>();
            foreach (int lane in template.FrontLanes)
            {
                slots.Add((lane, AttackPosition.Front));
            }
            foreach (int lane in template.BackLanes)
            {
                slots.Add((lane, AttackPosition.Back));
            }

            // Randomly pick slots and assign cards
            if (slots.Count > 0 && numToPlace > 0)
            {
                var chosenSlots = Sample(slots, Math.Min(numToPlace, slots.Count));
                foreach (var (lane, position) in chosenSlots)
                {
                    if (available.Count == 0)
                    {
                        break;
                    }
                    Card card = _rng.Choice(available);
                    result.Add((lane, card, position));
                    available.Remove(card);
                }
            }

            return result;
        }

        public Dictionary<PlayerRole, (int Lane, Card Card)> ChooseBlockCards(
            List<Card> hand, List<int> attackLanes, int wildThreshold = 0, int wideSpreadThreshold = 0)
        {
            var result = new Dictionary<PlayerRole, (int, Card)>();
            if (hand.Count == 0 || attackLanes.Count == 0)
            {
                return result;
            }
            var available = new List<Card>(hand);
            Shuffle(available);

            int cardIdx = 0;
            foreach (PlayerRole role in new[] { PlayerRole.Oh, PlayerRole.Mb, PlayerRole.Opp })
            {
                if (cardIdx >= available.Count)
                {
                    break;
                }
                var legalLanes = PlayerRoleExtensions.BlockableLanes[role].Where(attackLanes.Contains).ToList();
                if (legalLanes.Count == 0)
                {
                    continue;
                }
                int lane = _rng.Choice(legalLanes);
                result[role] = (lane, available[cardIdx]);
                cardIdx++;
            }
            return result;
        }

        public int ChooseAttackLane(Dictionary<int, Card> attackCards, Dictionary<int, int> blockLayout) =>
            // Prefer the lane with the least block coverage -- avoid the double-block
            attackCards.Keys.OrderBy(l => blockLayout.GetValueOrDefault(l, 0)).First();

        public string ChooseTipOrHit(int attackValue, int blockValue) =>
            attackValue <= 4 ? _rng.Choice(new[] { "tip", "hit" }) : "hit";

        public Card ChooseDigCard(List<Card> hand, int targetValue, DigType digType) =>
            // Tip dig needs card <= target -> play lowest; normal/deflect needs card >= target -> play highest
            digType == DigType.Tip
                ? hand.OrderBy(c => c.Value).First()
                : hand.OrderByDescending(c => c.Value).First();

        public Card ChooseChaseCard(List<Card> hand, int runningTotal, int targetValue) => _rng.Choice(hand);

        public Card ChooseFreeBallDiscard(List<Card> hand) => _rng.Choice(hand);

        public Card? ChooseExchangeCard(List<Card> hand, Card deckTop) => null; // Random strategy never exchanges

        public Card? ChooseCoverAttempt(List<Card> hand, int threshold)
        {
            // 50/50 whether to attempt; if yes, pick a random card
            if (hand.Count == 0 || !_rng.Choice(new[] { true, false }))
            {
                return null;
            }
            return _rng.Choice(hand);
        }

        public bool CoverDrawsFromDeck() => false;

        private void Shuffle(List<Card> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>Equivalent of Python's random.sample -- k unique random elements, no replacement.</summary>
        private List<T> Sample<T>(List<T> population, int k)
        {
            var pool = new List<T>(population);
            var result = new List<T>();
            for (int i = 0; i < k; i++)
            {
                int idx = _rng.Next(pool.Count);
                result.Add(pool[idx]);
                pool.RemoveAt(idx);
            }
            return result;
        }
    }
}
