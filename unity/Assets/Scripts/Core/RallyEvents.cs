using System.Collections.Generic;

namespace VolleyballCore
{
    /// <summary>
    /// Typed, structured record of something that happened in a rally, emitted by
    /// <see cref="Rally"/> in exactly the order it happens, alongside (not instead of)
    /// the human-readable narrative text. The narrative is for logs; these are for
    /// presentation, which previously had to recover the same facts by regex-parsing
    /// that text -- and to recompute or guess facts Core already knew (the digging
    /// role, the free ball's receiver). Every event carries team NAMES, not Team
    /// objects, so a consumer on another thread never touches live game state.
    ///
    /// Plain sealed classes with read-only properties rather than C# records: Unity's
    /// .NET profile lacks the IsExternalInit type records need.
    /// </summary>
    public abstract class RallyEvent
    {
    }

    /// <summary>Receives events as a rally emits them.</summary>
    public interface IRallyEventSink
    {
        void Emit(RallyEvent rallyEvent);
    }

    /// <summary>
    /// Simple ordered, thread-safe event list -- Core runs the game on a background
    /// thread while presentation reads on the main thread.
    /// </summary>
    public sealed class RallyEventLog : IRallyEventSink
    {
        private readonly List<RallyEvent> _events = new();

        public void Emit(RallyEvent rallyEvent)
        {
            lock (_events)
            {
                _events.Add(rallyEvent);
            }
        }

        public int Count
        {
            get
            {
                lock (_events)
                {
                    return _events.Count;
                }
            }
        }

        /// <summary>Copy of every event from <paramref name="startIndex"/> on.</summary>
        public List<RallyEvent> Since(int startIndex)
        {
            lock (_events)
            {
                return startIndex >= _events.Count
                    ? new List<RallyEvent>()
                    : _events.GetRange(startIndex, _events.Count - startIndex);
            }
        }
    }

    /// <summary>The kind of attack actually played, after abilities and tip-or-hit.</summary>
    public enum ShotKind
    {
        Hit,
        Tip,
        Roll,
        HeavySpin,
        Seam,
    }

    public sealed class RallyStartedEvent : RallyEvent
    {
        public string ServingTeam { get; }
        public string ReceivingTeam { get; }

        public RallyStartedEvent(string servingTeam, string receivingTeam)
        {
            ServingTeam = servingTeam;
            ReceivingTeam = receivingTeam;
        }
    }

    public sealed class ServeEvent : RallyEvent
    {
        public string Team { get; }
        public Card Card { get; }
        /// <summary>Card value after serve abilities -- what the receiver has to match.</summary>
        public int EffectiveValue { get; }
        public PlayerRole Target { get; }

        public ServeEvent(string team, Card card, int effectiveValue, PlayerRole target)
        {
            Team = team;
            Card = card;
            EffectiveValue = effectiveValue;
            Target = target;
        }
    }

    public sealed class ReceiveEvent : RallyEvent
    {
        public string Team { get; }
        /// <summary>The serve's target -- the player who actually passes it.</summary>
        public PlayerRole Passer { get; }
        public Card Card { get; }
        public int ServeValue { get; }
        public bool Clean { get; }

        public ReceiveEvent(string team, PlayerRole passer, Card card, int serveValue, bool clean)
        {
            Team = team;
            Passer = passer;
            Card = card;
            ServeValue = serveValue;
            Clean = clean;
        }
    }

    /// <summary>
    /// A failed reception's scramble begins. Core names no chasing player -- which
    /// teammate runs it down is a presentation choice.
    /// </summary>
    public sealed class ChaseStartedEvent : RallyEvent
    {
        public string Team { get; }
        public int TargetValue { get; }
        public int StartingTotal { get; }

        public ChaseStartedEvent(string team, int targetValue, int startingTotal)
        {
            Team = team;
            TargetValue = targetValue;
            StartingTotal = startingTotal;
        }
    }

    public sealed class ChaseAttemptEvent : RallyEvent
    {
        public string Team { get; }
        public int Attempt { get; }
        public Card Card { get; }
        public int RunningTotal { get; }
        public int TargetValue { get; }

        public ChaseAttemptEvent(string team, int attempt, Card card, int runningTotal, int targetValue)
        {
            Team = team;
            Attempt = attempt;
            Card = card;
            RunningTotal = runningTotal;
            TargetValue = targetValue;
        }
    }

    public sealed class ChaseEndedEvent : RallyEvent
    {
        public string Team { get; }
        public bool Succeeded { get; }
        public int RunningTotal { get; }
        public int TargetValue { get; }

        public ChaseEndedEvent(string team, bool succeeded, int runningTotal, int targetValue)
        {
            Team = team;
            Succeeded = succeeded;
            RunningTotal = runningTotal;
            TargetValue = targetValue;
        }
    }

    /// <summary>The extra card a successful chase costs before its free ball crosses.</summary>
    public sealed class FreeBallDiscardEvent : RallyEvent
    {
        public string Team { get; }
        public Card Card { get; }

        public FreeBallDiscardEvent(string team, Card card)
        {
            Team = team;
            Card = card;
        }
    }

    public sealed class FreeBallEvent : RallyEvent
    {
        public string FromTeam { get; }
        public string ToTeam { get; }
        public PlayerRole Receiver { get; }

        public FreeBallEvent(string fromTeam, string toTeam, PlayerRole receiver)
        {
            FromTeam = fromTeam;
            ToTeam = toTeam;
            Receiver = receiver;
        }
    }

    public sealed class SetEvent : RallyEvent
    {
        public string Team { get; }
        public int Exchange { get; }
        public Card Card { get; }
        /// <summary>After set abilities/dig carry-over, clamped 1-10 -- what picked the template.</summary>
        public int EffectiveValue { get; }
        public SetTemplate Template { get; }
        public bool BrokenPlay { get; }
        /// <summary>True when the hand was empty and the set card was drawn blind (no decision).</summary>
        public bool Blind { get; }

        public SetEvent(string team, int exchange, Card card, int effectiveValue, SetTemplate template, bool brokenPlay, bool blind)
        {
            Team = team;
            Exchange = exchange;
            Card = card;
            EffectiveValue = effectiveValue;
            Template = template;
            BrokenPlay = brokenPlay;
            Blind = blind;
        }
    }

    /// <summary>One committed attack card. Card is null for a face-down blind draw until <see cref="RevealEvent"/>.</summary>
    public sealed class AttackCommitEvent : RallyEvent
    {
        public string Team { get; }
        public int Lane { get; }
        public AttackPosition Position { get; }
        public Card? Card { get; }

        public AttackCommitEvent(string team, int lane, AttackPosition position, Card? card)
        {
            Team = team;
            Lane = lane;
            Position = position;
            Card = card;
        }
    }

    /// <summary>
    /// The defending team's committed block, final (after block abilities). Lanes with
    /// no blocker are absent. QuickLanes were forced single blind-drawn blockers.
    /// </summary>
    public sealed class BlockCommitEvent : RallyEvent
    {
        public string Team { get; }
        public IReadOnlyDictionary<int, IReadOnlyList<Card>> CardsByLane { get; }
        public IReadOnlyDictionary<int, int> TotalByLane { get; }
        public IReadOnlyList<int> QuickLanes { get; }

        public BlockCommitEvent(string team, IReadOnlyDictionary<int, IReadOnlyList<Card>> cardsByLane,
            IReadOnlyDictionary<int, int> totalByLane, IReadOnlyList<int> quickLanes)
        {
            Team = team;
            CardsByLane = cardsByLane;
            TotalByLane = totalByLane;
            QuickLanes = quickLanes;
        }
    }

    /// <summary>The attack lane is final (after any lane-slide) -- the set goes to this hitter.</summary>
    public sealed class SwingEvent : RallyEvent
    {
        public string Team { get; }
        public int Lane { get; }
        public PlayerRole Hitter { get; }

        public SwingEvent(string team, int lane, PlayerRole hitter)
        {
            Team = team;
            Lane = lane;
            Hitter = hitter;
        }
    }

    public sealed class RevealEvent : RallyEvent
    {
        public string Team { get; }
        public int Lane { get; }
        public Card Card { get; }
        public AttackPosition Position { get; }

        public RevealEvent(string team, int lane, Card card, AttackPosition position)
        {
            Team = team;
            Lane = lane;
            Card = card;
            Position = position;
        }
    }

    /// <summary>Two attackers share the chosen lane: they resolve in this order.</summary>
    public sealed class ComboEvent : RallyEvent
    {
        public string Team { get; }
        public int Lane { get; }
        public IReadOnlyList<AttackCard> ResolveOrder { get; }

        public ComboEvent(string team, int lane, IReadOnlyList<AttackCard> resolveOrder)
        {
            Team = team;
            Lane = lane;
            ResolveOrder = resolveOrder;
        }
    }

    /// <summary>A non-final combo card was stuffed: the lane's highest blocker is removed and the next card resolves.</summary>
    public sealed class ComboCardStuffedEvent : RallyEvent
    {
        public string Team { get; }
        public int Lane { get; }
        /// <summary>1-based position of the stuffed card in the combo's resolve order.</summary>
        public int CardNumber { get; }
        public Card RemovedBlocker { get; }

        public ComboCardStuffedEvent(string team, int lane, int cardNumber, Card removedBlocker)
        {
            Team = team;
            Lane = lane;
            CardNumber = cardNumber;
            RemovedBlocker = removedBlocker;
        }
    }

    /// <summary>One attack card meets the block. Effective values are after abilities.</summary>
    public sealed class ResolveEvent : RallyEvent
    {
        public string Team { get; }
        public int Lane { get; }
        public PlayerRole? Hitter { get; }
        public Card AttackCard { get; }
        public AttackPosition Position { get; }
        public int EffectiveAttack { get; }
        public int LaneBlockTotal { get; }
        public int EffectiveBlock { get; }

        public ResolveEvent(string team, int lane, PlayerRole? hitter, Card attackCard, AttackPosition position,
            int effectiveAttack, int laneBlockTotal, int effectiveBlock)
        {
            Team = team;
            Lane = lane;
            Hitter = hitter;
            AttackCard = attackCard;
            Position = position;
            EffectiveAttack = effectiveAttack;
            LaneBlockTotal = laneBlockTotal;
            EffectiveBlock = effectiveBlock;
        }
    }

    public sealed class ShotEvent : RallyEvent
    {
        public string Team { get; }
        public ShotKind Shot { get; }

        public ShotEvent(string team, ShotKind shot)
        {
            Team = team;
            Shot = shot;
        }
    }

    /// <summary>
    /// A hit (or tip) against the block: Kill = through to the defense, Deflect =
    /// exact tie back onto the attacker's side, Stuffed = blocked. Only emitted where
    /// a block comparison actually happens (roll/heavy-spin shots bypass the block).
    /// </summary>
    public sealed class AttackOutcomeEvent : RallyEvent
    {
        public string Team { get; }
        public int Lane { get; }
        public ShotKind Shot { get; }
        public AttackOutcomeType Outcome { get; }
        public int Attack { get; }
        public int Block { get; }

        public AttackOutcomeEvent(string team, int lane, ShotKind shot, AttackOutcomeType outcome, int attack, int block)
        {
            Team = team;
            Lane = lane;
            Shot = shot;
            Outcome = outcome;
            Attack = attack;
            Block = block;
        }
    }

    /// <summary>The defense digs an attack that got past the block.</summary>
    public sealed class DigEvent : RallyEvent
    {
        public string Team { get; }
        /// <summary>The player the attack goes to (AttackResolution.GetDigDefenderRole).</summary>
        public PlayerRole Digger { get; }
        public Card Card { get; }
        public int EffectiveDig { get; }
        public int TargetValue { get; }
        public ShotKind Shot { get; }
        public bool Dug { get; }
        /// <summary>True when the dig card came from a Cover attempt (adjacent player covering the setter).</summary>
        public bool Covered { get; }

        public DigEvent(string team, PlayerRole digger, Card card, int effectiveDig, int targetValue, ShotKind shot, bool dug, bool covered)
        {
            Team = team;
            Digger = digger;
            Card = card;
            EffectiveDig = effectiveDig;
            TargetValue = targetValue;
            Shot = shot;
            Dug = dug;
            Covered = covered;
        }
    }

    /// <summary>
    /// An exact-tie hit fell back onto the ATTACKING team's own side and they dig it.
    /// Core names no digging player -- a presentation choice.
    /// </summary>
    public sealed class DeflectDigEvent : RallyEvent
    {
        public string Team { get; }
        public Card Card { get; }
        public int TargetValue { get; }
        public bool Dug { get; }

        public DeflectDigEvent(string team, Card card, int targetValue, bool dug)
        {
            Team = team;
            Card = card;
            TargetValue = targetValue;
            Dug = dug;
        }
    }

    /// <summary>A setter dig that would have caused a broken play didn't (Cover reached it, or Safe Setter).</summary>
    public sealed class BrokenPlayAvoidedEvent : RallyEvent
    {
        public string Team { get; }
        public string Reason { get; }

        public BrokenPlayAvoidedEvent(string team, string reason)
        {
            Team = team;
            Reason = reason;
        }
    }

    /// <summary>A passive ability changed a resolution (text only -- no ball movement of its own).</summary>
    public sealed class PassiveAbilityEvent : RallyEvent
    {
        public string Team { get; }
        public string Description { get; }

        public PassiveAbilityEvent(string team, string description)
        {
            Team = team;
            Description = description;
        }
    }

    /// <summary>The decision points a strategy can be asked about -- one per IStrategy method that makes a real choice.</summary>
    public enum DecisionKind
    {
        Serve,
        Receive,
        Set,
        Exchange,
        HitCards,
        Block,
        AttackLane,
        TipOrHit,
        Cover,
        Dig,
        Chase,
        FreeBallDiscard,
    }

    /// <summary>
    /// A strategy is about to be asked to decide (see DecisionRecordingStrategy) --
    /// emitted BEFORE the call, so for a human player it's in the stream no later than
    /// the prompt itself. Everything emitted after it happened after the answer.
    /// </summary>
    public sealed class DecisionRequestedEvent : RallyEvent
    {
        public string Team { get; }
        public DecisionKind Kind { get; }

        public DecisionRequestedEvent(string team, DecisionKind kind)
        {
            Team = team;
            Kind = kind;
        }
    }

    /// <summary>The game score after a rally (emitted by whatever runs the game, not by Rally).</summary>
    public sealed class GameScoreEvent : RallyEvent
    {
        public string TeamA { get; }
        public int ScoreA { get; }
        public string TeamB { get; }
        public int ScoreB { get; }

        public GameScoreEvent(string teamA, int scoreA, string teamB, int scoreB)
        {
            TeamA = teamA;
            ScoreA = scoreA;
            TeamB = teamB;
            ScoreB = scoreB;
        }
    }

    public sealed class RallyEndedEvent : RallyEvent
    {
        public string Winner { get; }
        public string Reason { get; }
        public int Exchanges { get; }

        public RallyEndedEvent(string winner, string reason, int exchanges)
        {
            Winner = winner;
            Reason = reason;
            Exchanges = exchanges;
        }
    }
}
