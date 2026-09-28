using System;
using System.Collections.Generic;
using System.Linq;

namespace VolleyballCore
{
    /// <summary>
    /// Manages score, serving order, and runs rallies until GameConstants.PointsToWin.
    /// Ported from src/game.py's Game class.
    /// </summary>
    public sealed class Game
    {
        private readonly Team _teamA;
        private readonly Team _teamB;
        private readonly IStrategy _strategyA;
        private readonly IStrategy _strategyB;
        private readonly Random _rng;
        private readonly Dictionary<string, int> _scores;
        private Team _server;

        public Game(Team teamA, Team teamB, IStrategy strategyA, IStrategy strategyB, Random rng)
        {
            _teamA = teamA;
            _teamB = teamB;
            _strategyA = strategyA;
            _strategyB = strategyB;
            _rng = rng;
            _scores = new Dictionary<string, int> { { teamA.Name, 0 }, { teamB.Name, 0 } };
            // Randomly determine first server
            _server = rng.Choice(new[] { teamA, teamB });
        }

        public GameResult Play()
        {
            _teamA.DrawStartingHand();
            _teamB.DrawStartingHand();

            var rallyResults = new List<RallyResult>();

            while (_scores.Values.Max() < GameConstants.PointsToWin)
            {
                Team serving = _server;
                Team receiving = ReferenceEquals(serving, _teamA) ? _teamB : _teamA;
                IStrategy srvStrat = ReferenceEquals(serving, _teamA) ? _strategyA : _strategyB;
                IStrategy rcvStrat = ReferenceEquals(serving, _teamA) ? _strategyB : _strategyA;

                var rally = new Rally(serving, receiving, srvStrat, rcvStrat, _rng);
                RallyResult result = rally.Play();
                rallyResults.Add(result);

                _scores[result.WinnerName] += 1;
                // Winner of the rally earns the serve
                _server = result.WinnerName == _teamA.Name ? _teamA : _teamB;
            }

            string winner = _scores.OrderByDescending(kv => kv.Value).First().Key;
            return new GameResult(winner, new Dictionary<string, int>(_scores), rallyResults);
        }
    }
}
