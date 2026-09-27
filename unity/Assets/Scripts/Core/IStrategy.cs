using System.Collections.Generic;

namespace VolleyballCore
{
    /// <summary>
    /// One method per decision point in a rally. Ported from src/strategies.py's
    /// BaseStrategy. choose_armed_attack_lane is deliberately NOT ported -- it's
    /// dead code in Python too, unreachable since the two-tier chase
    /// (armed-attack/free-ball) was retired 2026-09-05.
    /// </summary>
    public interface IStrategy
    {
        /// <summary>Return (card to serve with, receiver to target).</summary>
        (Card Card, GridPlayer Receiver) ChooseServe(List<Card> hand, List<GridPlayer> eligibleReceivers);

        /// <summary>
        /// Every successful chase (reception or dig) skips the normal attack and instead
        /// crosses the net as a mandatory, guaranteed free ball -- no card is played and
        /// there's no risk of failure, this only decides which back-row opponent it's
        /// narratively/visually sent to.
        /// </summary>
        GridPlayer ChooseFreeBallTarget(List<GridPlayer> eligibleReceivers);

        /// <summary>Return card to play for receive. Success if card.Value >= serveValue.</summary>
        Card ChooseReceiveCard(List<Card> hand, int serveValue);

        /// <summary>Return card to play as the set. brokenPlay=true means a non-setter is setting.</summary>
        Card ChooseSetCard(List<Card> hand, bool brokenPlay = false);

        /// <summary>
        /// Return (lane, card, position) placements for attack -- up to
        /// template.MaxAttackers total, distinct cards from hand, front lanes
        /// from template.FrontLanes, back lanes from template.BackLanes.
        /// </summary>
        List<(int Lane, Card Card, AttackPosition Position)> ChooseHitCards(List<Card> hand, SetTemplate template);

        /// <summary>
        /// Each front-row player (Oh/Mb/Opp) may commit at most one card to block a
        /// lane within their reach -- their own lane or an adjacent one, see
        /// PlayerRoleExtensions.BlockableLanes -- restricted to lanes actually being
        /// attacked. Return one entry per blocker who chooses to block; omit a role
        /// entirely to leave them out of the block. Multiple blockers may converge on
        /// the same lane (a real multi-blocker stack, e.g. OH and MB both on lane 2) --
        /// that lane's block value is the sum of every card placed on it.
        /// </summary>
        Dictionary<PlayerRole, (int Lane, Card Card)> ChooseBlockCards(
            List<Card> hand, List<int> attackLanes, int wildThreshold = 0, int wideSpreadThreshold = 0);

        /// <summary>Attacker sees the block layout and chooses which lane to commit to.</summary>
        int ChooseAttackLane(Dictionary<int, Card> attackCards, Dictionary<int, int> blockLayout);

        /// <summary>Return "tip" or "hit". Tip is only valid within the tip threshold; otherwise must return "hit".</summary>
        string ChooseTipOrHit(int attackValue, int blockValue);

        /// <summary>Return card to attempt the dig with.</summary>
        Card ChooseDigCard(List<Card> hand, int targetValue, DigType digType);

        /// <summary>Return a card to add to runningTotal during a chase attempt.</summary>
        Card ChooseChaseCard(List<Card> hand, int runningTotal, int targetValue);

        /// <summary>
        /// A successful chase costs a card on top of the cards spent chasing itself --
        /// this one is discarded (not committed toward anything) as the price of sending
        /// the mandatory free ball across. Any card is valid; there's no value threshold
        /// to satisfy, unlike every other hand decision in the game.
        /// </summary>
        Card ChooseFreeBallDiscard(List<Card> hand);

        /// <summary>
        /// Called before hit placement when the team has the exchange_card ability.
        /// Return a card from hand to discard in exchange for deckTop, or null to decline.
        /// </summary>
        Card? ChooseExchangeCard(List<Card> hand, Card deckTop);

        /// <summary>
        /// Called when a kill or roll-shot dig lands in the setter's zone and an
        /// eligible Libero/DS could intercept. Return the card to use for the cover
        /// attempt (it also serves as the dig card), or null to skip coverage
        /// (setter digs, broken play). Cover succeeds if the card's value >= threshold.
        /// Only called when CoverDrawsFromDeck() is false.
        /// </summary>
        Card? ChooseCoverAttempt(List<Card> hand, int threshold);

        /// <summary>
        /// True if this strategy flips the top of the deck for cover instead of
        /// selecting from hand (e.g. a hand-less Dummy strategy). Default: false.
        /// </summary>
        bool CoverDrawsFromDeck();
    }
}
