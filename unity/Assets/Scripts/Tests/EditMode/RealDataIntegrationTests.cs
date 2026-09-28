using System;
using System.Linq;
using NUnit.Framework;
using VolleyballCore;
using VolleyballData;

namespace VolleyballCore.Tests
{
    /// <summary>
    /// End-to-end verification against the REAL project data (data/*.csv), not
    /// synthetic fixtures -- this is the capstone check that the whole chain
    /// (CSV -> PlayerCard/Mat -> Team -> AbilityEngine -> Rally -> Game) works
    /// with the actual game content, mirroring what list_mats.py and main.py
    /// do in Python. Relies on VolleyballData.DefaultPaths (Application.dataPath),
    /// which is only meaningful inside the Editor process -- exactly where
    /// EditMode tests run.
    /// </summary>
    public class RealDataIntegrationTests
    {
        [Test]
        public void PlayerCardsLoaderLoadsRealCsvWithKnownEntries()
        {
            var cards = PlayerCardsLoader.Load(DefaultPaths.PlayerCardsCsv);

            Assert.IsTrue(cards.ContainsKey("Strike"), "expected Blitz's Strike in player_cards.csv");
            Assert.AreEqual(1, cards["Strike"].Abilities.Count);
            Assert.AreEqual("Precision Pierce", cards["Strike"].Abilities[0].AbilityName);
            Assert.AreEqual(EffectType.PierceBlock, cards["Strike"].Abilities[0].Effect);

            Assert.IsTrue(cards.ContainsKey("Sarge"), "expected Blitz's Setter Sarge in player_cards.csv");
            Assert.AreEqual(0, cards["Sarge"].Abilities.Count);
        }

        [Test]
        public void PlayerCardsLoaderDisambiguatesDuplicateNameAcrossRoles()
        {
            // "Echo" appears as both Grind's blank Setter and Backline's DS with
            // "Transition Dig" -- this is the exact bug an earlier throwaway script
            // got wrong (naive name-only lookup) that src/mats.py's real code path
            // (and this port of it) must get right.
            var cards = PlayerCardsLoader.Load(DefaultPaths.PlayerCardsCsv);

            Assert.IsTrue(cards.ContainsKey("Echo::SETTER"));
            Assert.AreEqual(0, cards["Echo::SETTER"].Abilities.Count);

            Assert.IsTrue(cards.ContainsKey("Echo::DS"));
            Assert.AreEqual(1, cards["Echo::DS"].Abilities.Count);
            Assert.AreEqual("Transition Dig", cards["Echo::DS"].Abilities[0].AbilityName);
        }

        [Test]
        public void MatLoaderLoadsAllFourPickableMats()
        {
            var mats = MatLoader.LoadAllMats(
                DefaultPaths.TeamsCsv, DefaultPaths.PlayerCardsCsv, DefaultPaths.TeamPassivesCsv, DefaultPaths.SetTemplatesCsv);

            var names = mats.Select(m => m.Name).ToList();
            CollectionAssert.AreEquivalent(new[] { "Blitz", "Grind", "Spread", "Backline" }, names);
        }

        [Test]
        public void BlitzAndGrindHaveOnlyFrontRowAbilities()
        {
            var blitz = MatLoader.LoadMat(
                "Blitz", DefaultPaths.TeamsCsv, DefaultPaths.PlayerCardsCsv, DefaultPaths.TeamPassivesCsv, DefaultPaths.SetTemplatesCsv);

            int filledSeats = blitz.Seats.Values.Count(c => c.Abilities.Count > 0);
            Assert.AreEqual(3, filledSeats, "Blitz should have exactly 3 seats with an ability (front row only)");
            Assert.AreEqual(0, blitz.Seat(PlayerRole.Setter).Abilities.Count);
            Assert.AreEqual(0, blitz.Seat(PlayerRole.Ds).Abilities.Count);
            Assert.AreEqual(0, blitz.Seat(PlayerRole.Libero).Abilities.Count);
        }

        [Test]
        public void SpreadAndBacklineAreFullyFilled()
        {
            var spread = MatLoader.LoadMat(
                "Spread", DefaultPaths.TeamsCsv, DefaultPaths.PlayerCardsCsv, DefaultPaths.TeamPassivesCsv, DefaultPaths.SetTemplatesCsv);
            var backline = MatLoader.LoadMat(
                "Backline", DefaultPaths.TeamsCsv, DefaultPaths.PlayerCardsCsv, DefaultPaths.TeamPassivesCsv, DefaultPaths.SetTemplatesCsv);

            Assert.AreEqual(6, spread.Seats.Values.Count(c => c.Abilities.Count > 0));
            Assert.AreEqual(6, backline.Seats.Values.Count(c => c.Abilities.Count > 0));
        }

        [Test]
        public void TeamsCsvPassiveAbilityTakesPrecedenceOverTeamPassivesCsv()
        {
            // teams.csv sets Grind's passive_ability column to the literal string
            // "No Passive" -- since that's non-blank and not one of the recognized
            // "empty" tokens (tbd/none/null/-/—), it wins outright and team_passives.csv's
            // separate "Deep Bench" row for Grind is never consulted. Verified this
            // matches Python's actual resolve_team_runtime_config('Grind') output
            // exactly -- this is existing project data behavior, not a porting bug
            // (worth flagging: Grind's "Deep Bench" entry in team_passives.csv is
            // effectively dead as a result).
            var grind = MatLoader.LoadMat(
                "Grind", DefaultPaths.TeamsCsv, DefaultPaths.PlayerCardsCsv, DefaultPaths.TeamPassivesCsv, DefaultPaths.SetTemplatesCsv);
            Assert.AreEqual("No Passive", grind.PassiveAbility);
        }

        [Test]
        public void FullPipelineBlitzVsPlainRunsRealGamesWithoutError()
        {
            var blitzMat = MatLoader.LoadMat(
                "Blitz", DefaultPaths.TeamsCsv, DefaultPaths.PlayerCardsCsv, DefaultPaths.TeamPassivesCsv, DefaultPaths.SetTemplatesCsv);

            for (int seed = 0; seed < 30; seed++)
            {
                var rng = new Random(seed);
                var teamA = TeamFactory.BuildTeam(blitzMat, new Random(rng.Next()));
                var teamB = new Team("Plain", new Random(rng.Next()));
                var game = new Game(
                    teamA, teamB,
                    new SmartStrategy(new Random(rng.Next())),
                    new SmartStrategy(new Random(rng.Next())),
                    rng);
                var result = game.Play();
                Assert.That(result.WinnerName, Is.EqualTo("Blitz").Or.EqualTo("Plain"), $"seed {seed}");
            }
        }

        [Test]
        public void DeckCountsLoaderReadsRealDeckTypesCsv()
        {
            var counts = DeckCountsLoader.Load(DefaultPaths.DeckTypesCsv);
            Assert.IsTrue(counts.ContainsKey("standard"));
            for (int v = 1; v <= 10; v++)
            {
                Assert.AreEqual(4, counts["standard"][v], $"value {v}");
            }
        }
    }
}
