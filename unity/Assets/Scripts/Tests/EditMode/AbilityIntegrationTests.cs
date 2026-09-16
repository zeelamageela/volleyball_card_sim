using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    /// <summary>
    /// End-to-end proof that a real AbilityEngine, wired into a live Team, actually
    /// changes Rally/Game outcomes -- not just that the engine's methods return the
    /// right values in isolation. Mirrors the intent of tests/test_rally_endings.py's
    /// "_endings_appear_with_*_team" statistical checks.
    /// </summary>
    public class AbilityIntegrationTests
    {
        private static Team MakeTeamWithEngine(string name, int seed, Dictionary<PlayerRole, PlayerCard> roster)
        {
            var team = new Team(name, new Random(seed));
            team.AbilityEngine = new AbilityEngine(roster);
            return team;
        }

        [Test]
        public void WipeBlockAbilityProducesWipeEndingsOverManyGames()
        {
            // card=1 wipe_block on OH: whenever OH attacks with a 1 into a real block,
            // it's an instant point regardless of block size.
            var roster = new Dictionary<PlayerRole, PlayerCard>
            {
                [PlayerRole.Oh] = new("Trickster", "OH", new List<Ability>
                {
                    new("Wipe", Trigger.OnAttack, "attack_card_value", "1", EffectType.WipeBlock, 1),
                }),
            };

            int wipeEndings = 0;
            int totalRallies = 0;
            for (int seed = 0; seed < 300; seed++)
            {
                var rng = new Random(seed);
                var teamA = MakeTeamWithEngine("A", rng.Next(), roster);
                var teamB = new Team("B", new Random(rng.Next()));
                var game = new Game(teamA, teamB, new SmartStrategy(new Random(rng.Next())), new SmartStrategy(new Random(rng.Next())), rng);
                var result = game.Play();
                totalRallies += result.RallyResults.Count;
                wipeEndings += result.RallyResults.Count(r => r.Reason.StartsWith("Wipe off the block"));
            }

            Assert.Greater(wipeEndings, 0, "expected at least one wipe-block ending across 300 games");
        }

        [Test]
        public void PierceBlockAbilityMeaningfullyImprovesWinRate()
        {
            // An attacker whose OH always pierces the block (any attack card ignores
            // block entirely) should beat a plain team decisively more often than a
            // fair SmartVsSmart matchup would predict (~50%). If PierceBlock's return
            // value were being ignored anywhere in Rally's resolution path, this would
            // regress toward 50%.
            var roster = new Dictionary<PlayerRole, PlayerCard>
            {
                [PlayerRole.Oh] = new("Blade", "OH", new List<Ability>
                {
                    new("Always Pierce", Trigger.OnAttack, "", "", EffectType.PierceBlock, 1),
                }),
            };

            int pierceWins = 0;
            const int games = 150;
            for (int seed = 0; seed < games; seed++)
            {
                var rng = new Random(seed);
                var teamA = MakeTeamWithEngine("Pierce", rng.Next(), roster);
                var teamB = new Team("Plain", new Random(rng.Next()));
                var game = new Game(
                    teamA, teamB,
                    new SmartStrategy(new Random(rng.Next())),
                    new SmartStrategy(new Random(rng.Next())),
                    rng);
                if (game.Play().WinnerName == "Pierce")
                {
                    pierceWins++;
                }
            }

            Assert.Greater(pierceWins, games * 6 / 10, "expected a clear edge over a fair 50/50 baseline");
        }

        [Test]
        public void DeepBenchPassiveGivesSixCardHand()
        {
            var team = new Team("Grind", new Random(0), passiveAbility: "Deep Bench");
            team.DrawStartingHand();
            Assert.AreEqual(6, team.Hand.Count);
        }

        [Test]
        public void NoAbilityEngineBehavesLikePythonNone()
        {
            // A team with no AbilityEngine at all (null) must behave identically to
            // every ability hook being absent -- this is the baseline every other
            // ability test is measured against.
            var team = new Team("Plain", new Random(0));
            Assert.IsNull(team.AbilityEngine);
            team.DrawStartingHand();
            Assert.AreEqual(GameConstants.HandSize, team.Hand.Count);
        }
    }
}
