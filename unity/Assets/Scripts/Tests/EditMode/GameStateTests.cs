using System.Collections.Generic;
using NUnit.Framework;
using VolleyballCore;

namespace VolleyballCore.Tests
{
    public class GameStateTests
    {
        [Test]
        public void GameResultComputesTotalRalliesAndAverage()
        {
            var rallies = new List<RallyResult>
            {
                new RallyResult("A", "Stuffed", 2),
                new RallyResult("B", "Kill", 4),
            };
            var result = new GameResult("A", new Dictionary<string, int> { { "A", 1 }, { "B", 1 } }, rallies);

            Assert.AreEqual(2, result.TotalRallies);
            Assert.AreEqual(3.0, result.AvgRallyLength);
        }

        [Test]
        public void GameResultAverageIsZeroWithNoRallies()
        {
            var result = new GameResult("A", new Dictionary<string, int>(), new List<RallyResult>());
            Assert.AreEqual(0.0, result.AvgRallyLength);
        }
    }
}
