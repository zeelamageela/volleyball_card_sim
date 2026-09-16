using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    /// <summary>Ported from tests/test_game.py's TestRally (full rally smoke test).</summary>
    public class RallyTests
    {
        private static Rally MakeRally(int seed = 0)
        {
            var rng = new Random(seed);
            var teamA = new Team("A", new Random(rng.Next()));
            var teamB = new Team("B", new Random(rng.Next()));
            teamA.DrawStartingHand();
            teamB.DrawStartingHand();
            var stratA = new RandomStrategy(new Random(rng.Next()));
            var stratB = new RandomStrategy(new Random(rng.Next()));
            return new Rally(teamA, teamB, stratA, stratB, rng);
        }

        [Test]
        public void RallyReturnsResult()
        {
            var rally = MakeRally();
            var result = rally.Play();
            Assert.That(result.WinnerName, Is.EqualTo("A").Or.EqualTo("B"));
            Assert.GreaterOrEqual(result.RallyLength, 0);
        }

        [Test]
        public void RallyWinnerNameIsValidAcrossManySeeds()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var rally = MakeRally(seed);
                var result = rally.Play();
                Assert.That(result.WinnerName, Is.EqualTo("A").Or.EqualTo("B"), $"seed {seed}");
            }
        }

        [Test]
        public void RallyAlwaysHasAReason()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var rally = MakeRally(seed);
                var result = rally.Play();
                Assert.IsNotEmpty(result.Reason, $"seed {seed}");
            }
        }

        /// <summary>
        /// Every successful chase (reception included) must skip the normal attack and
        /// hand the ball to the *other* team as a free ball -- the chasing team should
        /// never get a real Set/Hit sequence off the back of their own chase recovery.
        /// Searches across seeds for a rally that actually produces a chase-recovered
        /// reception (RandomStrategy makes this uncontrollable directly), since there's
        /// no smaller unit to isolate this exchange-level behavior in.
        /// </summary>
        [Test]
        public void ChaseRecoveredReceptionSendsFreeBallToServerNotReceiver()
        {
            bool foundCase = false;
            for (int seed = 0; seed < 500 && !foundCase; seed++)
            {
                var rng = new Random(seed);
                var teamA = new Team("A", new Random(rng.Next())); // servingTeam
                var teamB = new Team("B", new Random(rng.Next())); // receivingTeam
                teamA.DrawStartingHand();
                teamB.DrawStartingHand();
                var stratA = new RandomStrategy(new Random(rng.Next()));
                var stratB = new RandomStrategy(new Random(rng.Next()));
                var narrative = new List<string>();
                var rally = new Rally(teamA, teamB, stratA, stratB, rng, narrative);
                rally.Play();

                int freeBallIndex = narrative.FindIndex(l => l.Contains("SUCCEEDED — mandatory free ball to "));
                if (freeBallIndex < 0)
                {
                    continue;
                }
                foundCase = true;

                Assert.That(narrative[freeBallIndex], Does.Contain("mandatory free ball to A"), $"seed {seed}");

                string nextSetLine = narrative.Skip(freeBallIndex).FirstOrDefault(l => l.Contains("Set:"));
                if (nextSetLine != null)
                {
                    Assert.That(nextSetLine, Does.Contain("Set:     A"), $"seed {seed}");
                }
            }
            Assert.IsTrue(foundCase, "No seed among the first 500 produced a chase-recovered reception.");
        }

        // "≤ tip"/"> tip" (not the bare "≥ N"/"< N" a normal hit's dig line uses) is what
        // distinguishes a tip's dig line from a hit's in this narrative format.
        private static readonly Regex TipStuffedRegex = new(@"Tip:\s+(\d+) vs lowest blocker (\d+)\s+→\s+STUFFED");
        private static readonly Regex TipDugRegex = new(@"Dig:\s+\S+ card (\d+).*?≤ tip (\d+)\s+→\s+DUG");
        private static readonly Regex TipNotDugRegex = new(@"Dig:\s+\S+ card (\d+).*?> tip (\d+)\s+→\s+NOT DUG, no chase");

        /// <summary>
        /// A tip is checked backwards from a normal hit: a blocker's card *same or lower*
        /// than the tip stuffs it (including an exact tie -- no deflection for tips), and
        /// only a strictly higher blocker card lets it through; digging a tip then needs a
        /// card *same or lower* than the tip's own value. Both directions are the inverse
        /// of a hit's "equal-or-higher wins" rule. Searches across seeds for all three
        /// tip outcomes (RandomStrategy makes any one uncontrollable directly) and checks
        /// the actual numbers in each narrated line satisfy the corrected relationship,
        /// rather than just trusting the line was categorized correctly by the same code
        /// under test.
        /// </summary>
        [Test]
        public void TipResolutionUsesSameOrLowerInBothDirections()
        {
            bool foundStuffed = false, foundDug = false, foundNotDug = false;
            for (int seed = 0; seed < 1000 && !(foundStuffed && foundDug && foundNotDug); seed++)
            {
                var rally = MakeRallyWithNarrative(seed, out List<string> narrative);
                rally.Play();

                if (!foundStuffed)
                {
                    Match m = narrative.Select(l => TipStuffedRegex.Match(l)).FirstOrDefault(mm => mm.Success);
                    if (m != null)
                    {
                        int tip = int.Parse(m.Groups[1].Value);
                        int blocker = int.Parse(m.Groups[2].Value);
                        Assert.LessOrEqual(blocker, tip, $"seed {seed}: STUFFED should mean blocker <= tip");
                        foundStuffed = true;
                    }
                }
                if (!foundDug)
                {
                    Match m = narrative.Select(l => TipDugRegex.Match(l)).FirstOrDefault(mm => mm.Success);
                    if (m != null)
                    {
                        int dig = int.Parse(m.Groups[1].Value);
                        int tip = int.Parse(m.Groups[2].Value);
                        Assert.LessOrEqual(dig, tip, $"seed {seed}: DUG should mean dig <= tip");
                        foundDug = true;
                    }
                }
                if (!foundNotDug)
                {
                    Match m = narrative.Select(l => TipNotDugRegex.Match(l)).FirstOrDefault(mm => mm.Success);
                    if (m != null)
                    {
                        int dig = int.Parse(m.Groups[1].Value);
                        int tip = int.Parse(m.Groups[2].Value);
                        Assert.Greater(dig, tip, $"seed {seed}: NOT DUG should mean dig > tip");
                        foundNotDug = true;
                    }
                }
            }
            Assert.IsTrue(foundStuffed, "No seed among the first 1000 produced a tip STUFFED by the block.");
            Assert.IsTrue(foundDug, "No seed among the first 1000 produced a successfully dug tip.");
            Assert.IsTrue(foundNotDug, "No seed among the first 1000 produced a failed tip dig.");
        }

        // Matches every dig-failure narration variant: kill, tip, deflect, roll shot, and
        // heavy spin all end with "NOT DUG, no chase" except heavy spin, which parenthesizes it.
        private static readonly Regex DigFailureRegex = new(@"NOT DUG(, no chase|\s*\(no chase\))");

        /// <summary>
        /// Every dig failure (kill, tip, tied-deflection, roll shot, heavy spin) must end
        /// the rally immediately with a point to the attacker -- no chase attempt, no
        /// "broken dig" free-ball recovery. Chase now exists in exactly one place: a
        /// failed serve reception (covered separately by
        /// ChaseRecoveredReceptionSendsFreeBallToServerNotReceiver). Searches across seeds
        /// for a rally that actually produces a dig failure (RandomStrategy makes this
        /// uncontrollable directly) and asserts no "Chase" narration follows it.
        /// </summary>
        [Test]
        public void DigFailureEndsRallyImmediatelyWithNoChase()
        {
            bool foundCase = false;
            for (int seed = 0; seed < 500 && !foundCase; seed++)
            {
                var rally = MakeRallyWithNarrative(seed, out List<string> narrative);
                rally.Play();

                int failIndex = narrative.FindIndex(l => DigFailureRegex.IsMatch(l));
                if (failIndex < 0)
                {
                    continue;
                }
                foundCase = true;

                for (int i = failIndex + 1; i < narrative.Count; i++)
                {
                    Assert.That(narrative[i], Does.Not.Contain("Chase"),
                        $"seed {seed}: chase narration found after dig failure at index {failIndex}");
                }
            }
            Assert.IsTrue(foundCase, "No seed among the first 500 produced a dig failure (kill/tip/deflect/roll/heavy-spin).");
        }

        private static Rally MakeRallyWithNarrative(int seed, out List<string> narrative)
        {
            var rng = new Random(seed);
            var teamA = new Team("A", new Random(rng.Next()));
            var teamB = new Team("B", new Random(rng.Next()));
            teamA.DrawStartingHand();
            teamB.DrawStartingHand();
            var stratA = new RandomStrategy(new Random(rng.Next()));
            var stratB = new RandomStrategy(new Random(rng.Next()));
            narrative = new List<string>();
            return new Rally(teamA, teamB, stratA, stratB, rng, narrative);
        }
    }
}
