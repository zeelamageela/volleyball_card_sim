using System.Collections.Generic;

namespace VolleyballCore
{
    /// <summary>
    /// Wraps any strategy and emits a <see cref="DecisionRequestedEvent"/> into an
    /// event sink right before forwarding each real decision to it -- so a human
    /// player's decisions land in the same ordered timeline as the rally's own events
    /// (Rally emits into the same sink from the same thread). Answers pass through
    /// untouched. ChooseFreeBallTarget and CoverDrawsFromDeck aren't recorded: the
    /// first is always answered automatically (never a real prompt), the second is a
    /// capability query, not a decision.
    /// </summary>
    public sealed class DecisionRecordingStrategy : IStrategy
    {
        private readonly IStrategy _inner;
        private readonly string _team;
        private readonly IRallyEventSink _events;

        public DecisionRecordingStrategy(IStrategy inner, string team, IRallyEventSink events)
        {
            _inner = inner;
            _team = team;
            _events = events;
        }

        private void Record(DecisionKind kind) => _events?.Emit(new DecisionRequestedEvent(_team, kind));

        public (Card Card, GridPlayer Receiver) ChooseServe(List<Card> hand, List<GridPlayer> eligibleReceivers)
        {
            Record(DecisionKind.Serve);
            return _inner.ChooseServe(hand, eligibleReceivers);
        }

        public GridPlayer ChooseFreeBallTarget(List<GridPlayer> eligibleReceivers) =>
            _inner.ChooseFreeBallTarget(eligibleReceivers);

        public Card ChooseReceiveCard(List<Card> hand, int serveValue)
        {
            Record(DecisionKind.Receive);
            return _inner.ChooseReceiveCard(hand, serveValue);
        }

        public Card ChooseSetCard(List<Card> hand, bool brokenPlay = false)
        {
            Record(DecisionKind.Set);
            return _inner.ChooseSetCard(hand, brokenPlay);
        }

        public List<(int Lane, Card Card, AttackPosition Position)> ChooseHitCards(List<Card> hand, SetTemplate template)
        {
            Record(DecisionKind.HitCards);
            return _inner.ChooseHitCards(hand, template);
        }

        public Dictionary<PlayerRole, (int Lane, Card Card)> ChooseBlockCards(
            List<Card> hand, List<int> attackLanes, int wildThreshold = 0, int wideSpreadThreshold = 0)
        {
            Record(DecisionKind.Block);
            return _inner.ChooseBlockCards(hand, attackLanes, wildThreshold, wideSpreadThreshold);
        }

        public int ChooseAttackLane(Dictionary<int, Card> attackCards, Dictionary<int, int> blockLayout)
        {
            Record(DecisionKind.AttackLane);
            return _inner.ChooseAttackLane(attackCards, blockLayout);
        }

        public string ChooseTipOrHit(int attackValue, int blockValue)
        {
            Record(DecisionKind.TipOrHit);
            return _inner.ChooseTipOrHit(attackValue, blockValue);
        }

        public Card ChooseDigCard(List<Card> hand, int targetValue, DigType digType)
        {
            Record(DecisionKind.Dig);
            return _inner.ChooseDigCard(hand, targetValue, digType);
        }

        public Card ChooseChaseCard(List<Card> hand, int runningTotal, int targetValue)
        {
            Record(DecisionKind.Chase);
            return _inner.ChooseChaseCard(hand, runningTotal, targetValue);
        }

        public Card ChooseFreeBallDiscard(List<Card> hand)
        {
            Record(DecisionKind.FreeBallDiscard);
            return _inner.ChooseFreeBallDiscard(hand);
        }

        public Card? ChooseExchangeCard(List<Card> hand, Card deckTop)
        {
            Record(DecisionKind.Exchange);
            return _inner.ChooseExchangeCard(hand, deckTop);
        }

        public Card? ChooseCoverAttempt(List<Card> hand, int threshold)
        {
            Record(DecisionKind.Cover);
            return _inner.ChooseCoverAttempt(hand, threshold);
        }

        public bool CoverDrawsFromDeck() => _inner.CoverDrawsFromDeck();
    }
}
