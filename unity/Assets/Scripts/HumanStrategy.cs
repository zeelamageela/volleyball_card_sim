using System.Collections.Generic;
using VolleyballCore;

/// <summary>
/// IStrategy implementation for a human player. Runs on the background thread
/// inside Rally.Play()'s call stack -- every method here must stay pure C#, no
/// UnityEngine API calls, ever. Each method hands its own parameters to
/// HumanDecisionChannel and blocks until the main thread (GameRunner) supplies
/// an answer from a UI click.
///
/// All ~12 decision points are now wired through the channel. Response types
/// aren't wrapped in custom classes -- tuples, nullable structs, and
/// collections all cast fine through HumanDecisionChannel.Post&lt;TResponse&gt;'s
/// generic cast directly.
/// </summary>
public sealed class HumanStrategy : IStrategy
{
    private readonly HumanDecisionChannel _channel;

    public HumanStrategy(HumanDecisionChannel channel) => _channel = channel;

    public (Card Card, GridPlayer Receiver) ChooseServe(List<Card> hand, List<GridPlayer> eligibleReceivers) =>
        _channel.Post<(Card, GridPlayer)>(new ServeRequest { Hand = hand, EligibleReceivers = eligibleReceivers });

    // Not a real decision -- always aims at the Libero (or the other back-row
    // passer if, for whatever reason, the Libero isn't in the eligible pool) rather
    // than asking the human to pick. EligibleReceivers() (Team.cs) is always exactly
    // {Ds, Libero} in this game's fixed-role model (no real rotation), so this never
    // actually falls through to the raw first-eligible catch-all in practice.
    public GridPlayer ChooseFreeBallTarget(List<GridPlayer> eligibleReceivers)
    {
        // GridPlayer is a struct, so List<T>.Find can't fall through via ?? (its
        // "not found" default isn't null) -- explicit index search instead.
        int liberoIndex = eligibleReceivers.FindIndex(p => p.Role == PlayerRole.Libero);
        if (liberoIndex >= 0)
        {
            return eligibleReceivers[liberoIndex];
        }
        int dsIndex = eligibleReceivers.FindIndex(p => p.Role == PlayerRole.Ds);
        return dsIndex >= 0 ? eligibleReceivers[dsIndex] : eligibleReceivers[0];
    }

    public Card ChooseReceiveCard(List<Card> hand, int serveValue) =>
        _channel.Post<Card>(new ReceiveRequest { Hand = hand, ServeValue = serveValue });

    public Card ChooseSetCard(List<Card> hand, bool brokenPlay = false) =>
        _channel.Post<Card>(new SetCardRequest { Hand = hand, BrokenPlay = brokenPlay });

    public List<(int Lane, Card Card, AttackPosition Position)> ChooseHitCards(List<Card> hand, SetTemplate template) =>
        _channel.Post<List<(int, Card, AttackPosition)>>(new HitCardsRequest { Hand = hand, Template = template });

    public Dictionary<PlayerRole, (int Lane, Card Card)> ChooseBlockCards(
        List<Card> hand, List<int> attackLanes, int wildThreshold = 0, int wideSpreadThreshold = 0) =>
        _channel.Post<Dictionary<PlayerRole, (int, Card)>>(new BlockCardsRequest
        {
            Hand = hand,
            AttackLanes = attackLanes,
            WildThreshold = wildThreshold,
            WideSpreadThreshold = wideSpreadThreshold,
        });

    public int ChooseAttackLane(Dictionary<int, Card> attackCards, Dictionary<int, int> blockLayout) =>
        _channel.Post<int>(new AttackLaneRequest { AttackCards = attackCards, BlockLayout = blockLayout });

    public string ChooseTipOrHit(int attackValue, int blockValue) =>
        _channel.Post<string>(new TipOrHitRequest { AttackValue = attackValue, BlockValue = blockValue });

    public Card ChooseDigCard(List<Card> hand, int targetValue, DigType digType) =>
        _channel.Post<Card>(new DigCardRequest { Hand = hand, TargetValue = targetValue, DigType = digType });

    public Card ChooseChaseCard(List<Card> hand, int runningTotal, int targetValue) =>
        _channel.Post<Card>(new ChaseCardRequest { Hand = hand, RunningTotal = runningTotal, TargetValue = targetValue });

    public Card ChooseFreeBallDiscard(List<Card> hand) =>
        _channel.Post<Card>(new FreeBallDiscardRequest { Hand = hand });

    public Card? ChooseExchangeCard(List<Card> hand, Card deckTop) =>
        _channel.Post<Card?>(new ExchangeCardRequest { Hand = hand, DeckTop = deckTop });

    public Card? ChooseCoverAttempt(List<Card> hand, int threshold) =>
        _channel.Post<Card?>(new CoverAttemptRequest { Hand = hand, Threshold = threshold });

    public bool CoverDrawsFromDeck() => false;
}
