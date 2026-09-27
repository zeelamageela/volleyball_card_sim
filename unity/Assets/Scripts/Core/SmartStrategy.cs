using System;
using System.Collections.Generic;
using System.Linq;

namespace VolleyballCore
{
    /// <summary>
    /// Optimized strategy that exploits opponent patterns and makes intelligent
    /// decisions. Ported from src/strategies.py's SmartStrategy.
    ///
    /// Key tactics:
    /// 1. Plays high cards on important plays (blocking, digging)
    /// 2. Saves strong cards for critical moments
    /// 3. Exploits DummyStrategy patterns (always doubles lane 2, predictable attacks)
    /// 4. Makes optimal lane choices (attack weak blocks, avoid strong ones)
    /// </summary>
    public sealed class SmartStrategy : IStrategy
    {
        private readonly Random _rng;

        public SmartStrategy(Random rng) => _rng = rng;

        public (Card Card, GridPlayer Receiver) ChooseServe(List<Card> hand, List<GridPlayer> eligibleReceivers)
        {
            // Serve with highest card to maximize ace potential
            Card card = hand.OrderByDescending(c => c.Value).First();
            // Target a random receiver (no pattern to exploit here)
            GridPlayer receiver = _rng.Choice(eligibleReceivers);
            return (card, receiver);
        }

        public GridPlayer ChooseFreeBallTarget(List<GridPlayer> eligibleReceivers) => _rng.Choice(eligibleReceivers);

        public Card ChooseReceiveCard(List<Card> hand, int serveValue)
        {
            // Play the minimum card that can succeed (save high cards)
            var candidates = hand.Where(c => c.Value >= serveValue).ToList();
            if (candidates.Count > 0)
            {
                return candidates.OrderBy(c => c.Value).First();
            }
            // If no card can succeed, play lowest to minimize loss
            return hand.OrderBy(c => c.Value).First();
        }

        public Card ChooseSetCard(List<Card> hand, bool brokenPlay = false)
        {
            if (brokenPlay)
            {
                // Broken play templates: 1-3 -> 2 attackers (decent), 4-7 -> 1 (worst), 8-10 -> 2 (decent).
                // Strongly avoid mid-range cards; prefer high (8-10) then low (1-3).
                var high = hand.Where(c => c.Value >= 8).ToList();
                if (high.Count > 0)
                {
                    return high.OrderByDescending(c => c.Value).First();
                }
                var low = hand.Where(c => c.Value <= 3).ToList();
                if (low.Count > 0)
                {
                    return low.OrderByDescending(c => c.Value).First();
                }
                // Stuck with 4-7 -- play lowest to minimise disruption
                return hand.OrderBy(c => c.Value).First();
            }
            // Normal play: prefer mid-high cards (6-9) for good lane options
            var midHigh = hand.Where(c => c.Value >= 6 && c.Value <= 9).ToList();
            if (midHigh.Count > 0)
            {
                return midHigh.OrderByDescending(c => c.Value).First();
            }
            var decent = hand.Where(c => c.Value >= 4 && c.Value <= 9).ToList();
            if (decent.Count > 0)
            {
                return decent.OrderByDescending(c => c.Value).First();
            }
            return hand.OrderBy(c => c.Value).First();
        }

        public List<(int Lane, Card Card, AttackPosition Position)> ChooseHitCards(List<Card> hand, SetTemplate template)
        {
            // Tactical matching + multi-lane pressure + setter targeting
            var result = new List<(int Lane, Card Card, AttackPosition Position)>();
            var available = hand.OrderByDescending(c => c.Value).ToList();

            // TACTIC: intentionally match a high-value duplicate (front+back same value,
            // same lane) to waste the defender's block cards on that lane -- conservative:
            // only high values (8+), and only when we have spare cards.
            var valueCounts = new Dictionary<int, int>();
            foreach (var card in available)
            {
                valueCounts[card.Value] = valueCounts.GetValueOrDefault(card.Value, 0) + 1;
            }

            int? bestMatchValue = null;
            if (available.Count >= 4 && template.MaxAttackers >= 3)
            {
                foreach (int value in valueCounts.Keys.OrderByDescending(v => v))
                {
                    if (valueCounts[value] >= 2 && value >= 8)
                    {
                        bestMatchValue = value;
                        break;
                    }
                }
            }

            int? matchingLane = null;
            if (bestMatchValue.HasValue && template.FrontLanes.Count > 0 && template.BackLanes.Count > 0)
            {
                var commonLanes = template.FrontLanes.Intersect(template.BackLanes).ToList();
                if (commonLanes.Count > 0)
                {
                    // Prefer lane 2 for matching (dummy always double-blocks it)
                    matchingLane = commonLanes.Contains(2) ? 2 : commonLanes.Min();
                }
            }

            if (matchingLane.HasValue && bestMatchValue.HasValue && result.Count + 2 <= template.MaxAttackers)
            {
                var matchingCards = available.Where(c => c.Value == bestMatchValue.Value).ToList();
                if (matchingCards.Count >= 2 && available.Count - 2 >= 1)
                {
                    result.Add((matchingLane.Value, matchingCards[0], AttackPosition.Front));
                    result.Add((matchingLane.Value, matchingCards[1], AttackPosition.Back));
                    available.Remove(matchingCards[0]);
                    available.Remove(matchingCards[1]);
                }
            }

            // EXPLOIT: DummyStrategy always double-blocks lane 2, so prefer lanes 1/3 when available.
            var preferredFront = template.FrontLanes.Where(l => l != 2).ToList();
            if (preferredFront.Count == 0)
            {
                preferredFront = new List<int>(template.FrontLanes);
            }

            // Build attack plan: front-row first, then back-row. Prioritize multi-lane attacks.
            var attackPlan = new List<(int Lane, AttackPosition Position)>();
            foreach (int lane in preferredFront.OrderBy(l => l))
            {
                attackPlan.Add((lane, AttackPosition.Front));
            }
            if (template.FrontLanes.Contains(2) && !preferredFront.Contains(2))
            {
                attackPlan.Add((2, AttackPosition.Front));
            }

            // Back-row lanes -- new distinct lanes first, then shared lanes
            var frontLaneSet = new HashSet<int>(template.FrontLanes);
            foreach (int lane in template.BackLanes.Where(l => !frontLaneSet.Contains(l)).OrderBy(l => l))
            {
                attackPlan.Add((lane, AttackPosition.Back));
            }
            foreach (int lane in template.BackLanes.Where(l => frontLaneSet.Contains(l)).OrderBy(l => l))
            {
                attackPlan.Add((lane, AttackPosition.Back));
            }

            // Filter out slots already used by tactical matching
            var usedSlots = new HashSet<(int, AttackPosition)>(result.Select(t => (t.Lane, t.Position)));
            attackPlan = attackPlan.Where(t => !usedSlots.Contains((t.Lane, t.Position))).ToList();

            foreach (var (lane, position) in attackPlan)
            {
                if (result.Count >= template.MaxAttackers || available.Count == 0)
                {
                    break;
                }

                // MATCHING GUARD: never place same value front+back on the same lane
                // (attacker-attacker parity would cost us the rally under older rules;
                // kept conservative even though that mechanic was retired).
                int? frontValOnLane = result
                    .Where(t => t.Lane == lane && t.Position == AttackPosition.Front)
                    .Select(t => (int?)t.Card.Value)
                    .FirstOrDefault();

                Card card;
                if (lane == 1 || lane == 2)
                {
                    // TACTICAL: target opponent's setter with odd cards on lanes 1/2
                    var candidates = available.Where(c => c.Value % 2 == 1).ToList();
                    if (position == AttackPosition.Back && frontValOnLane.HasValue)
                    {
                        candidates = candidates.Where(c => c.Value != frontValOnLane.Value).ToList();
                    }
                    if (candidates.Count == 0)
                    {
                        candidates = available.Where(c =>
                            position != AttackPosition.Back || !frontValOnLane.HasValue || c.Value != frontValOnLane.Value
                        ).ToList();
                    }
                    card = candidates.Count > 0 ? candidates[0] : available[0];
                }
                else // lane 3
                {
                    if (position == AttackPosition.Back && frontValOnLane.HasValue)
                    {
                        var safe = available.Where(c => c.Value != frontValOnLane.Value).ToList();
                        card = safe.Count > 0 ? safe[0] : available[0];
                    }
                    else
                    {
                        card = available[0];
                    }
                }

                result.Add((lane, card, position));
                available.Remove(card);
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

            var sortedHand = hand.OrderByDescending(c => c.Value).ToList();
            int cardIdx = 0;
            var coveredLanes = new HashSet<int>();

            // Two passes: first give every attacked lane at least one blocker if
            // possible (best coverage -- an attacked lane left completely open is
            // worse than any double-block), preferring whichever lane fewer other
            // blockers could help with later (save MB's flexibility for wherever it's
            // still needed). Any blockers left over after that double up instead of
            // sitting idle.
            foreach (bool preferUncovered in new[] { true, false })
            {
                foreach (PlayerRole role in new[] { PlayerRole.Oh, PlayerRole.Mb, PlayerRole.Opp })
                {
                    if (result.ContainsKey(role) || cardIdx >= sortedHand.Count)
                    {
                        continue;
                    }
                    var legalLanes = PlayerRoleExtensions.BlockableLanes[role].Where(attackLanes.Contains).ToList();
                    var candidates = preferUncovered ? legalLanes.Where(l => !coveredLanes.Contains(l)).ToList() : legalLanes;
                    if (candidates.Count == 0)
                    {
                        continue;
                    }
                    int lane = candidates
                        .OrderBy(l => PlayerRoleExtensions.BlockableLanes.Values.Count(bl => bl.Contains(l)))
                        .First();
                    result[role] = (lane, sortedHand[cardIdx]);
                    coveredLanes.Add(lane);
                    cardIdx++;
                }
            }

            return result;
        }

        public int ChooseAttackLane(Dictionary<int, Card> attackCards, Dictionary<int, int> blockLayout)
        {
            int Score(int lane) => attackCards[lane].Value - blockLayout.GetValueOrDefault(lane, 0);
            // Prefer lanes with known values; blind draws have value=0
            var known = attackCards.Where(kv => kv.Value.Value > 0).Select(kv => kv.Key).ToList();
            var pool = known.Count > 0 ? known : attackCards.Keys.ToList();
            return pool.OrderByDescending(Score).First();
        }

        public string ChooseTipOrHit(int attackValue, int blockValue)
        {
            // Tip only if it gives an advantage: valid at <=4, and only when the block is strong
            if (attackValue <= 4 && blockValue > attackValue + 2)
            {
                return "tip";
            }
            return "hit";
        }

        public Card ChooseDigCard(List<Card> hand, int targetValue, DigType digType)
        {
            if (digType == DigType.Tip)
            {
                // Need card <= target. Play highest card that qualifies.
                var candidates = hand.Where(c => c.Value <= targetValue).ToList();
                if (candidates.Count > 0)
                {
                    return candidates.OrderByDescending(c => c.Value).First();
                }
                return hand.OrderBy(c => c.Value).First();
            }
            // Normal dig: need card >= target. Play lowest card that qualifies.
            var normalCandidates = hand.Where(c => c.Value >= targetValue).ToList();
            if (normalCandidates.Count > 0)
            {
                return normalCandidates.OrderBy(c => c.Value).First();
            }
            return hand.OrderByDescending(c => c.Value).First();
        }

        public Card ChooseChaseCard(List<Card> hand, int runningTotal, int targetValue)
        {
            // Play the minimum card that gets us to target
            int needed = targetValue - runningTotal;
            var candidates = hand.Where(c => c.Value >= needed).ToList();
            if (candidates.Count > 0)
            {
                return candidates.OrderBy(c => c.Value).First();
            }
            // If we can't reach target, play highest to get closest
            return hand.OrderByDescending(c => c.Value).First();
        }

        /// <summary>No threshold to satisfy here -- discard the weakest card, keeping the
        /// stronger ones in hand for whatever comes next.</summary>
        public Card ChooseFreeBallDiscard(List<Card> hand) => hand.OrderBy(c => c.Value).First();

        public Card? ChooseExchangeCard(List<Card> hand, Card deckTop)
        {
            // Swap out the worst hand card if deck top is significantly better
            Card worst = hand.OrderBy(c => c.Value).First();
            if (deckTop.Value >= worst.Value + 3)
            {
                return worst;
            }
            return null;
        }

        public Card? ChooseCoverAttempt(List<Card> hand, int threshold)
        {
            // Smart always tries when a qualifying card exists -- use the lowest one
            // (keeps high cards available for attack/block on the next exchange).
            if (hand.Count == 0)
            {
                return null;
            }
            var qualifying = hand.Where(c => c.Value >= threshold).ToList();
            if (qualifying.Count > 0)
            {
                return qualifying.OrderBy(c => c.Value).First();
            }
            return null;
        }

        public bool CoverDrawsFromDeck() => false;
    }
}
