using System;
using System.Collections.Generic;
using System.Linq;

namespace VolleyballCore
{
    /// <summary>
    /// Truly blind opponent -- no hand optimization whatsoever. Ported from
    /// src/strategies.py's DummyStrategy.
    ///
    /// Every card decision uses hand[0] (the top card as drawn, no sorting or
    /// selection). Non-card decisions (lane, tip/hit) use the top card's parity
    /// as a tie-breaker so behavior remains deterministic and testable.
    ///
    /// Cover attempts draw the top card of the deck directly
    /// (CoverDrawsFromDeck() == true), bypassing the hand entirely.
    /// </summary>
    public sealed class DummyStrategy : IStrategy
    {
        public (Card Card, GridPlayer Receiver) ChooseServe(List<Card> hand, List<GridPlayer> eligibleReceivers)
        {
            Card card = hand[0];
            GridPlayer receiver = card.Value % 2 == 0 ? eligibleReceivers[0] : eligibleReceivers[^1];
            return (card, receiver);
        }

        public GridPlayer ChooseFreeBallTarget(List<GridPlayer> eligibleReceivers) => eligibleReceivers[0];

        public Card ChooseReceiveCard(List<Card> hand, int serveValue) => hand[0];

        public Card ChooseSetCard(List<Card> hand, bool brokenPlay = false) => hand[0];

        public List<(int Lane, Card Card, AttackPosition Position)> ChooseHitCards(List<Card> hand, SetTemplate template)
        {
            // Blind: use the top card on the first available lane
            if (template.FrontLanes.Count > 0 && hand.Count > 0)
            {
                return new List<(int, Card, AttackPosition)> { (template.FrontLanes[0], hand[0], AttackPosition.Front) };
            }
            if (template.BackLanes.Count > 0 && hand.Count > 0)
            {
                return new List<(int, Card, AttackPosition)> { (template.BackLanes[0], hand[0], AttackPosition.Back) };
            }
            return new List<(int, Card, AttackPosition)>();
        }

        public Dictionary<PlayerRole, (int Lane, Card Card)> ChooseBlockCards(
            List<Card> hand, List<int> attackLanes, int wildThreshold = 0, int wideSpreadThreshold = 0)
        {
            // Blind: each blocker (in a fixed order) takes the next unused hand card,
            // no sorting or evaluation. Lane choice is deterministic too -- their own
            // lane if it's attacked, otherwise whichever adjacent lane comes first.
            var result = new Dictionary<PlayerRole, (int, Card)>();
            int cardIdx = 0;
            foreach (PlayerRole role in new[] { PlayerRole.Oh, PlayerRole.Mb, PlayerRole.Opp })
            {
                if (cardIdx >= hand.Count)
                {
                    break;
                }
                var legalLanes = PlayerRoleExtensions.BlockableLanes[role].Where(attackLanes.Contains).ToList();
                if (legalLanes.Count == 0)
                {
                    continue;
                }
                int homeLane = PlayerRoleExtensions.LaneToRole.First(kv => kv.Value == role).Key;
                int lane = legalLanes.Contains(homeLane) ? homeLane : legalLanes[0];
                result[role] = (lane, hand[cardIdx]);
                cardIdx++;
            }
            return result;
        }

        public int ChooseAttackLane(Dictionary<int, Card> attackCards, Dictionary<int, int> blockLayout) =>
            // Blind: pick the first lane available
            attackCards.Keys.First();

        public string ChooseTipOrHit(int attackValue, int blockValue) => "tip";

        public Card ChooseDigCard(List<Card> hand, int targetValue, DigType digType) => hand[0];

        public Card ChooseChaseCard(List<Card> hand, int runningTotal, int targetValue) => hand[0];

        public Card ChooseFreeBallDiscard(List<Card> hand) => hand[0];

        public Card? ChooseExchangeCard(List<Card> hand, Card deckTop) => null; // Dummy never exchanges

        public bool CoverDrawsFromDeck() => true; // Blind deck flip -- no hand selection

        public Card? ChooseCoverAttempt(List<Card> hand, int threshold) => null; // Not called when CoverDrawsFromDeck() is true
    }
}
