using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    /// <summary>
    /// DecisionRecordingStrategy: decisions join the rally's event stream in the exact
    /// order they happen, without changing any answer. Both teams are recorded here so
    /// every decision point gets exercised; in the game only the human's strategy is.
    /// </summary>
    public class DecisionRecordingStrategyTests
    {
        private const int Seeds = 300;

        /// <summary>Counts real decision calls per kind, independently of the recorder.</summary>
        private sealed class CountingStrategy : IStrategy
        {
            private readonly IStrategy _inner;
            public readonly Dictionary<DecisionKind, int> Calls = new();

            public CountingStrategy(IStrategy inner) => _inner = inner;

            private void Count(DecisionKind kind) => Calls[kind] = Calls.GetValueOrDefault(kind) + 1;

            public (Card Card, GridPlayer Receiver) ChooseServe(List<Card> hand, List<GridPlayer> eligibleReceivers)
            {
                Count(DecisionKind.Serve);
                return _inner.ChooseServe(hand, eligibleReceivers);
            }

            public GridPlayer ChooseFreeBallTarget(List<GridPlayer> eligibleReceivers) => _inner.ChooseFreeBallTarget(eligibleReceivers);

            public Card ChooseReceiveCard(List<Card> hand, int serveValue)
            {
                Count(DecisionKind.Receive);
                return _inner.ChooseReceiveCard(hand, serveValue);
            }

            public Card ChooseSetCard(List<Card> hand, bool brokenPlay = false)
            {
                Count(DecisionKind.Set);
                return _inner.ChooseSetCard(hand, brokenPlay);
            }

            public List<(int Lane, Card Card, AttackPosition Position)> ChooseHitCards(List<Card> hand, SetTemplate template)
            {
                Count(DecisionKind.HitCards);
                return _inner.ChooseHitCards(hand, template);
            }

            public Dictionary<PlayerRole, (int Lane, Card Card)> ChooseBlockCards(
                List<Card> hand, List<int> attackLanes, int wildThreshold = 0, int wideSpreadThreshold = 0)
            {
                Count(DecisionKind.Block);
                return _inner.ChooseBlockCards(hand, attackLanes, wildThreshold, wideSpreadThreshold);
            }

            public int ChooseAttackLane(Dictionary<int, Card> attackCards, Dictionary<int, int> blockLayout)
            {
                Count(DecisionKind.AttackLane);
                return _inner.ChooseAttackLane(attackCards, blockLayout);
            }

            public string ChooseTipOrHit(int attackValue, int blockValue)
            {
                Count(DecisionKind.TipOrHit);
                return _inner.ChooseTipOrHit(attackValue, blockValue);
            }

            public Card ChooseDigCard(List<Card> hand, int targetValue, DigType digType)
            {
                Count(DecisionKind.Dig);
                return _inner.ChooseDigCard(hand, targetValue, digType);
            }

            public Card ChooseChaseCard(List<Card> hand, int runningTotal, int targetValue)
            {
                Count(DecisionKind.Chase);
                return _inner.ChooseChaseCard(hand, runningTotal, targetValue);
            }

            public Card ChooseFreeBallDiscard(List<Card> hand)
            {
                Count(DecisionKind.FreeBallDiscard);
                return _inner.ChooseFreeBallDiscard(hand);
            }

            public Card? ChooseExchangeCard(List<Card> hand, Card deckTop)
            {
                Count(DecisionKind.Exchange);
                return _inner.ChooseExchangeCard(hand, deckTop);
            }

            public Card? ChooseCoverAttempt(List<Card> hand, int threshold)
            {
                Count(DecisionKind.Cover);
                return _inner.ChooseCoverAttempt(hand, threshold);
            }

            public bool CoverDrawsFromDeck() => _inner.CoverDrawsFromDeck();
        }

        private sealed class RecordedRally
        {
            public RallyResult Result;
            public List<string> Narrative;
            public List<RallyEvent> Events;
            public CountingStrategy CountA;
            public CountingStrategy CountB;
        }

        private static RecordedRally Play(int seed, bool smart, bool record)
        {
            var rng = new Random(seed);
            var teamA = new Team("A", new Random(rng.Next()));
            var teamB = new Team("B", new Random(rng.Next()));
            teamA.DrawStartingHand();
            teamB.DrawStartingHand();
            IStrategy innerA = smart ? new SmartStrategy(new Random(rng.Next())) : new RandomStrategy(new Random(rng.Next()));
            IStrategy innerB = smart ? new SmartStrategy(new Random(rng.Next())) : new RandomStrategy(new Random(rng.Next()));
            var countA = new CountingStrategy(innerA);
            var countB = new CountingStrategy(innerB);
            var log = new RallyEventLog();
            IStrategy stratA = record ? new DecisionRecordingStrategy(countA, "A", log) : countA;
            IStrategy stratB = record ? new DecisionRecordingStrategy(countB, "B", log) : countB;
            var narrative = new List<string>();
            var result = new Rally(teamA, teamB, stratA, stratB, rng, narrative, log).Play();
            return new RecordedRally { Result = result, Narrative = narrative, Events = log.Since(0), CountA = countA, CountB = countB };
        }

        private static IEnumerable<(int Seed, bool Smart)> AllCases()
        {
            for (int seed = 0; seed < Seeds; seed++)
            {
                yield return (seed, false);
                yield return (seed, true);
            }
        }

        [Test]
        public void RecordingDoesNotChangeAnyAnswer()
        {
            foreach (var (seed, smart) in AllCases())
            {
                var plain = Play(seed, smart, record: false);
                var recorded = Play(seed, smart, record: true);
                string where = $"seed {seed} smart={smart}";
                Assert.AreEqual(plain.Result.WinnerName, recorded.Result.WinnerName, where);
                Assert.AreEqual(plain.Result.Reason, recorded.Result.Reason, where);
                CollectionAssert.AreEqual(plain.Narrative, recorded.Narrative, where);
                Assert.AreEqual(0, plain.Events.OfType<DecisionRequestedEvent>().Count(), where);
            }
        }

        [Test]
        public void EveryDecisionCallIsRecordedExactlyOnce()
        {
            var seenKinds = new HashSet<DecisionKind>();
            foreach (var (seed, smart) in AllCases())
            {
                var r = Play(seed, smart, record: true);
                string where = $"seed {seed} smart={smart}";
                foreach (var (team, counts) in new[] { ("A", r.CountA.Calls), ("B", r.CountB.Calls) })
                {
                    foreach (DecisionKind kind in Enum.GetValues(typeof(DecisionKind)))
                    {
                        int recorded = r.Events.OfType<DecisionRequestedEvent>().Count(d => d.Team == team && d.Kind == kind);
                        Assert.AreEqual(counts.GetValueOrDefault(kind), recorded, $"{where} team {team} {kind}");
                        if (recorded > 0)
                        {
                            seenKinds.Add(kind);
                        }
                    }
                }
            }

            // Plain teams have no abilities, so Exchange and Cover can't come up here --
            // everything else must, or the seed range isn't exercising enough.
            foreach (DecisionKind kind in Enum.GetValues(typeof(DecisionKind)))
            {
                if (kind is DecisionKind.Exchange or DecisionKind.Cover)
                {
                    continue;
                }
                Assert.IsTrue(seenKinds.Contains(kind), $"no {kind} decision across {Seeds} seeds");
            }
        }

        /// <summary>
        /// Each decision sits in the stream directly before the event its answer
        /// produces -- the ordering presentation will rely on to know that a decision
        /// comes between two touches of the ball.
        /// </summary>
        [Test]
        public void DecisionsImmediatelyPrecedeTheirOutcome()
        {
            foreach (var (seed, smart) in AllCases())
            {
                var events = Play(seed, smart, record: true).Events;
                string where = $"seed {seed} smart={smart}";

                void AssertPrecededBy(int index, string team, DecisionKind kind)
                {
                    var previous = index > 0 ? events[index - 1] as DecisionRequestedEvent : null;
                    Assert.NotNull(previous, $"{where}: {events[index].GetType().Name} at {index} not preceded by a decision");
                    Assert.AreEqual(team, previous.Team, where);
                    Assert.AreEqual(kind, previous.Kind, where);
                }

                // The rally opens with the serve decision (hands are full at rally start).
                Assert.IsInstanceOf<RallyStartedEvent>(events[0], where);
                var firstDecision = events[1] as DecisionRequestedEvent;
                Assert.NotNull(firstDecision, where);
                Assert.AreEqual(DecisionKind.Serve, firstDecision.Kind, where);

                for (int i = 0; i < events.Count; i++)
                {
                    switch (events[i])
                    {
                        case ServeEvent serve:
                            AssertPrecededBy(i, serve.Team, DecisionKind.Serve);
                            break;
                        case ReceiveEvent receive:
                            AssertPrecededBy(i, receive.Team, DecisionKind.Receive);
                            break;
                        case SetEvent set when !set.Blind:
                            AssertPrecededBy(i, set.Team, DecisionKind.Set);
                            break;
                        case SetEvent set:
                            // A blind-drawn set is never a decision.
                            Assert.IsFalse(i > 0 && events[i - 1] is DecisionRequestedEvent { Kind: DecisionKind.Set }, where);
                            break;
                        case SwingEvent swing:
                            AssertPrecededBy(i, swing.Team, DecisionKind.AttackLane);
                            break;
                        case ChaseAttemptEvent attempt when events[i - 1] is DecisionRequestedEvent:
                            AssertPrecededBy(i, attempt.Team, DecisionKind.Chase);
                            break;
                        case FreeBallDiscardEvent discard:
                            AssertPrecededBy(i, discard.Team, DecisionKind.FreeBallDiscard);
                            break;
                    }
                }
            }
        }
    }
}
