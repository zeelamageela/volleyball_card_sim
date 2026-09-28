using System.Collections.Generic;
using VolleyballCore;

/// <summary>
/// Request payloads HumanStrategy posts to a HumanDecisionChannel. Each carries
/// exactly what that IStrategy method's own parameters already gave it -- no
/// reaching into other Team/Rally state, so these are safe to read from the main
/// thread once taken (the background thread is confirmed blocked at that point).
/// Responses aren't wrapped: they're just the method's natural return type,
/// passed through HumanDecisionChannel.Post&lt;TResponse&gt;'s generic cast directly
/// (works for classes, structs, tuples and nullable structs alike).
/// </summary>
public sealed class ServeRequest
{
    public List<Card> Hand;
    public List<GridPlayer> EligibleReceivers;
}

public sealed class ReceiveRequest
{
    public List<Card> Hand;
    public int ServeValue;
}

public sealed class SetCardRequest
{
    public List<Card> Hand;
    public bool BrokenPlay;
}

public sealed class HitCardsRequest
{
    public List<Card> Hand;
    public SetTemplate Template;
}

public sealed class BlockCardsRequest
{
    public List<Card> Hand;
    public List<int> AttackLanes;
    public int WildThreshold;
    public int WideSpreadThreshold;
}

public sealed class AttackLaneRequest
{
    public Dictionary<int, Card> AttackCards;
    public Dictionary<int, int> BlockLayout;
}

public sealed class TipOrHitRequest
{
    public int AttackValue;
    public int BlockValue;
}

public sealed class DigCardRequest
{
    public List<Card> Hand;
    public int TargetValue;
    public DigType DigType;
}

public sealed class ChaseCardRequest
{
    public List<Card> Hand;
    public int RunningTotal;
    public int TargetValue;
}

/// <summary>The cost of a successful chase -- any card from hand, no threshold to meet.</summary>
public sealed class FreeBallDiscardRequest
{
    public List<Card> Hand;
}

public sealed class ExchangeCardRequest
{
    public List<Card> Hand;
    public Card DeckTop;
}

public sealed class CoverAttemptRequest
{
    public List<Card> Hand;
    public int Threshold;
}
