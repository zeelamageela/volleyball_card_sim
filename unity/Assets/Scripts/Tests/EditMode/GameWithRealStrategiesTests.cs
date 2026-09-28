using System;
using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    /// <summary>
    /// Integration smoke tests: full games through Rally/Game using the real ported
    /// strategies (not the throwaway random fixture) -- the combination that
    /// actually matters for gameplay, mirroring main.py's default pvd mode
    /// (SmartStrategy vs DummyStrategy).
    /// </summary>
    public class GameWithRealStrategiesTests
    {
        [Test]
        public void SmartVsDummyRunsManyGamesWithoutError()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                var rng = new Random(seed);
                var teamA = new Team("Smart", new Random(rng.Next()));
                var teamB = new Team("Dummy", new Random(rng.Next()));
                var game = new Game(teamA, teamB, new SmartStrategy(new Random(rng.Next())), new DummyStrategy(), rng);
                var result = game.Play();

                Assert.That(result.WinnerName, Is.EqualTo("Smart").Or.EqualTo("Dummy"), $"seed {seed}");
                Assert.AreEqual(GameConstants.PointsToWin, result.Scores[result.WinnerName], $"seed {seed}");
            }
        }

        [Test]
        public void SmartVsSmartRunsManyGamesWithoutError()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                var rng = new Random(seed);
                var teamA = new Team("A", new Random(rng.Next()));
                var teamB = new Team("B", new Random(rng.Next()));
                var game = new Game(
                    teamA, teamB,
                    new SmartStrategy(new Random(rng.Next())),
                    new SmartStrategy(new Random(rng.Next())),
                    rng);
                var result = game.Play();

                Assert.That(result.WinnerName, Is.EqualTo("A").Or.EqualTo("B"), $"seed {seed}");
            }
        }

        [Test]
        public void SmartVsDummyWinsMostGames()
        {
            // Sanity check: a strategic player should beat a blind one more often than not.
            int smartWins = 0;
            const int games = 100;
            for (int seed = 0; seed < games; seed++)
            {
                var rng = new Random(seed);
                var teamA = new Team("Smart", new Random(rng.Next()));
                var teamB = new Team("Dummy", new Random(rng.Next()));
                var game = new Game(teamA, teamB, new SmartStrategy(new Random(rng.Next())), new DummyStrategy(), rng);
                var result = game.Play();
                if (result.WinnerName == "Smart")
                {
                    smartWins++;
                }
            }
            Assert.Greater(smartWins, games / 2);
        }
    }
}
