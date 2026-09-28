using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    /// <summary>
    /// Rally's typed event stream (RallyEvents.cs): emitted alongside every narrative
    /// line, in order, without changing the rally itself. Uses many seeds and both
    /// strategy types so the rarer paths (chase, combo, deflect, tip, cover) get
    /// exercised.
    /// </summary>
    public class RallyEventTests
    {
        private const int Seeds = 400;

        private static (RallyResult Result, List<string> Narrative, List<RallyEvent> Events) PlayRally(int seed, bool smart)
        {
            var rng = new Random(seed);
            var teamA = new Team("A", new Random(rng.Next()));
            var teamB = new Team("B", new Random(rng.Next()));
            teamA.DrawStartingHand();
            teamB.DrawStartingHand();
            IStrategy stratA = smart ? new SmartStrategy(new Random(rng.Next())) : new RandomStrategy(new Random(rng.Next()));
            IStrategy stratB = smart ? new SmartStrategy(new Random(rng.Next())) : new RandomStrategy(new Random(rng.Next()));
            var narrative = new List<string>();
            var log = new RallyEventLog();
            var result = new Rally(teamA, teamB, stratA, stratB, rng, narrative, log).Play();
            return (result, narrative, log.Since(0));
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
        public void EveryRallyIsBracketedByStartAndEnd()
        {
            foreach (var (seed, smart) in AllCases())
            {
                var (result, _, events) = PlayRally(seed, smart);
                string where = $"seed {seed} smart={smart}";

                var started = events.First() as RallyStartedEvent;
                Assert.NotNull(started, where);
                Assert.AreEqual("A", started.ServingTeam, where);
                Assert.AreEqual("B", started.ReceivingTeam, where);

                var ended = events.Last() as RallyEndedEvent;
                Assert.NotNull(ended, where);
                Assert.AreEqual(result.WinnerName, ended.Winner, where);
                Assert.AreEqual(result.Reason, ended.Reason, where);
                Assert.AreEqual(result.RallyLength, ended.Exchanges, where);

                Assert.AreEqual(1, events.OfType<RallyStartedEvent>().Count(), where);
                Assert.AreEqual(1, events.OfType<RallyEndedEvent>().Count(), where);
            }
        }

        [Test]
        public void EmittingEventsDoesNotChangeTheRally()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                var withEvents = PlayRally(seed, smart: true);

                var rng = new Random(seed);
                var teamA = new Team("A", new Random(rng.Next()));
                var teamB = new Team("B", new Random(rng.Next()));
                teamA.DrawStartingHand();
                teamB.DrawStartingHand();
                var narrative = new List<string>();
                var result = new Rally(teamA, teamB, new SmartStrategy(new Random(rng.Next())),
                    new SmartStrategy(new Random(rng.Next())), rng, narrative).Play();

                Assert.AreEqual(result.WinnerName, withEvents.Result.WinnerName, $"seed {seed}");
                Assert.AreEqual(result.Reason, withEvents.Result.Reason, $"seed {seed}");
                CollectionAssert.AreEqual(narrative, withEvents.Narrative, $"seed {seed}");
            }
        }

        /// <summary>
        /// Every kind of narrative line has exactly as many matching events -- the
        /// stream is a complete, structured mirror of the text, not a partial one.
        /// </summary>
        [Test]
        public void EventCountsMirrorNarrativeLines()
        {
            var seenTypes = new HashSet<Type>();
            foreach (var (seed, smart) in AllCases())
            {
                var (_, narrative, events) = PlayRally(seed, smart);
                string where = $"seed {seed} smart={smart}";
                var lines = narrative.Select(l => l.Trim()).ToList();
                int Lines(Func<string, bool> match) => lines.Count(match);
                int Events<T>() where T : RallyEvent => events.OfType<T>().Count();

                Assert.AreEqual(Lines(l => l.StartsWith("Serve:")), Events<ServeEvent>(), where);
                Assert.AreEqual(Lines(l => l.StartsWith("Receive:")), Events<ReceiveEvent>(), where);
                Assert.AreEqual(Lines(l => l.StartsWith("Chase:") && l.Contains("starting at")), Events<ChaseStartedEvent>(), where);
                Assert.AreEqual(Lines(l => l.StartsWith("Chase ") && !l.StartsWith("Chase:")), Events<ChaseAttemptEvent>(), where);
                Assert.AreEqual(Lines(l => l == "Chase:   SUCCEEDED" || l.StartsWith("Chase:   FAILED")), Events<ChaseEndedEvent>(), where);
                Assert.AreEqual(Lines(l => l.StartsWith("Chase:") && l.Contains("discard")), Events<FreeBallDiscardEvent>(), where);
                Assert.AreEqual(Lines(l => l.Contains("mandatory free ball")), Events<FreeBallEvent>(), where);
                Assert.AreEqual(Lines(l => l.StartsWith("Set:")), Events<SetEvent>(), where);
                Assert.AreEqual(Lines(l => l.StartsWith("Attack:")), Events<AttackCommitEvent>(), where);
                Assert.AreEqual(Lines(l => l.StartsWith("Swing:")), Events<SwingEvent>(), where);
                Assert.AreEqual(Lines(l => l.StartsWith("Reveal:")), Events<RevealEvent>(), where);
                Assert.AreEqual(Lines(l => l.StartsWith("Combo:") && l.Contains("resolve order")), Events<ComboEvent>(), where);
                Assert.AreEqual(Lines(l => l.StartsWith("Combo:") && l.Contains("stuffed")), Events<ComboCardStuffedEvent>(), where);
                Assert.AreEqual(Lines(l => l.StartsWith("Resolve:")), Events<ResolveEvent>(), where);
                Assert.AreEqual(Lines(l => l.StartsWith("Shot:")), Events<ShotEvent>(), where);
                Assert.AreEqual(Lines(l => l.StartsWith("Dig:")), Events<DigEvent>(), where);
                Assert.AreEqual(Lines(l => l.StartsWith("Deflect:")), Events<DeflectDigEvent>(), where);
                Assert.AreEqual(Lines(l => l.StartsWith("Cover:") || l.Contains("Safe Setter")), Events<BrokenPlayAvoidedEvent>(), where);
                Assert.AreEqual(Lines(l => l.StartsWith("Passive: Back Court")), Events<PassiveAbilityEvent>(), where);
                // One block commit per exchange; the text only narrates quick-set blind blocks.
                Assert.AreEqual(Events<SetEvent>(), Events<BlockCommitEvent>(), where);
                // Outcome: and tip-STUFFED lines, plus one silent "tip got past the block"
                // outcome for every tip that reached a dig.
                int tipDigs = events.OfType<DigEvent>().Count(d => d.Shot == ShotKind.Tip);
                Assert.AreEqual(Lines(l => l.StartsWith("Outcome:") || l.StartsWith("Tip:")) + tipDigs,
                    Events<AttackOutcomeEvent>(), where);

                foreach (var e in events)
                {
                    seenTypes.Add(e.GetType());
                }
            }

            // Guard against the seed range silently never reaching the rarer paths.
            foreach (var type in new[]
            {
                typeof(ChaseStartedEvent), typeof(FreeBallEvent), typeof(RevealEvent), typeof(ComboEvent),
                typeof(DeflectDigEvent), typeof(DigEvent), typeof(AttackOutcomeEvent),
            })
            {
                Assert.IsTrue(seenTypes.Contains(type), $"no {type.Name} across {Seeds} seeds");
            }
        }

        /// <summary>The facts presentation needs line up across related events.</summary>
        [Test]
        public void RelatedEventsAgree()
        {
            foreach (var (seed, smart) in AllCases())
            {
                var (_, _, events) = PlayRally(seed, smart);
                string where = $"seed {seed} smart={smart}";

                // Serve is always immediately received, by the player it targeted.
                int serveIndex = events.FindIndex(e => e is ServeEvent);
                var serve = (ServeEvent)events[serveIndex];
                var receive = events[serveIndex + 1] as ReceiveEvent;
                Assert.NotNull(receive, where);
                Assert.AreEqual(serve.Target, receive.Passer, where);
                Assert.AreEqual("A", serve.Team, where);
                Assert.AreEqual("B", receive.Team, where);

                // A free ball always goes back to the serving team's back row.
                foreach (var freeBall in events.OfType<FreeBallEvent>())
                {
                    Assert.AreEqual("B", freeBall.FromTeam, where);
                    Assert.AreEqual("A", freeBall.ToTeam, where);
                    Assert.That(freeBall.Receiver, Is.EqualTo(PlayerRole.Ds).Or.EqualTo(PlayerRole.Libero), where);
                }

                // Within each exchange: Set, then its AttackCommits, then the other
                // team's BlockCommit, then a Swing by the setting team. A dig is always
                // by the team that didn't swing (at the role Core computed from the
                // resolved lane and card); a deflection is dug by the team that did.
                SetEvent lastSet = null;
                SwingEvent lastSwing = null;
                ResolveEvent lastResolve = null;
                foreach (var e in events)
                {
                    switch (e)
                    {
                        case SetEvent set:
                            lastSet = set;
                            lastSwing = null;
                            break;
                        case AttackCommitEvent commit:
                            Assert.NotNull(lastSet, where);
                            Assert.AreEqual(lastSet.Team, commit.Team, where);
                            break;
                        case BlockCommitEvent block:
                            Assert.NotNull(lastSet, where);
                            Assert.AreNotEqual(lastSet.Team, block.Team, where);
                            break;
                        case SwingEvent swing:
                            Assert.NotNull(lastSet, where);
                            Assert.AreEqual(lastSet.Team, swing.Team, where);
                            Assert.AreEqual(PlayerRoleExtensions.LaneToRole[swing.Lane], swing.Hitter, where);
                            lastSwing = swing;
                            break;
                        case ResolveEvent resolve:
                            Assert.NotNull(lastSwing, where);
                            Assert.AreEqual(lastSwing.Team, resolve.Team, where);
                            Assert.AreEqual(lastSwing.Lane, resolve.Lane, where);
                            lastResolve = resolve;
                            break;
                        case DigEvent dig:
                            Assert.NotNull(lastSwing, where);
                            Assert.AreNotEqual(lastSwing.Team, dig.Team, where);
                            Assert.AreEqual(
                                AttackResolution.GetDigDefenderRole(lastResolve.Lane, lastResolve.AttackCard.Value),
                                dig.Digger, where);
                            break;
                        case DeflectDigEvent deflect:
                            Assert.NotNull(lastSwing, where);
                            Assert.AreEqual(lastSwing.Team, deflect.Team, where);
                            break;
                    }
                }
            }
        }
    }
}
