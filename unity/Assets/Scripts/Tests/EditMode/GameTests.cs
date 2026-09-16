using System;
using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    /// <summary>Ported from tests/test_game.py's TestGame (full game smoke test).</summary>
    public class GameTests
    {
        private static Game MakeGame(int seed = 0)
        {
            var rng = new Random(seed);
            var stratA = new RandomStrategy(new Random(rng.Next()));
            var stratB = new RandomStrategy(new Random(rng.Next()));
            var teamA = new Team("Team A", new Random(rng.Next()));
            var teamB = new Team("Team B", new Random(rng.Next()));
            return new Game(teamA, teamB, stratA, stratB, rng);
        }

        [Test]
        public void GameProducesWinner()
        {
            var result = MakeGame().Play();
            Assert.That(result.WinnerName, Is.EqualTo("Team A").Or.EqualTo("Team B"));
        }

        [Test]
        public void WinnerReachedPointsToWin()
        {
            var result = MakeGame().Play();
            Assert.AreEqual(GameConstants.PointsToWin, result.Scores[result.WinnerName]);
        }

        [Test]
        public void LoserHasFewerPointsThanWinner()
        {
            var result = MakeGame().Play();
            string loser = result.WinnerName == "Team A" ? "Team B" : "Team A";
            Assert.Less(result.Scores[loser], GameConstants.PointsToWin);
        }

        [Test]
        public void GameHasRallies()
        {
            var result = MakeGame().Play();
            Assert.Greater(result.TotalRallies, 0);
        }

        [Test]
        public void DeterministicWithSeed()
        {
            var result1 = MakeGame(99).Play();
            var result2 = MakeGame(99).Play();
            Assert.AreEqual(result1.WinnerName, result2.WinnerName);
            CollectionAssert.AreEquivalent(result1.Scores, result2.Scores);
        }

        [Test]
        public void MultipleGamesRunWithoutError()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                MakeGame(seed).Play();
            }
        }
    }
}
