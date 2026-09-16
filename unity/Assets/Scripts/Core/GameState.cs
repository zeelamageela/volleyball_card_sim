using System.Collections.Generic;
using System.Linq;

namespace VolleyballCore
{
    /// <summary>Ported from src/game_state.py's AttackOutcomeType.</summary>
    public enum AttackOutcomeType
    {
        Kill,    // attack > block (ball breaks through to defender)
        Deflect, // attack == block, exact tie (ball falls to attacker's own side)
        Stuffed, // block >= attack, not tied (clean block, defense wins rally)
    }

    /// <summary>Ported from src/game_state.py's ChaseOutcome.</summary>
    public enum ChaseOutcome
    {
        Success, // running total reached target within the attempt cap
        Failed,  // attempts exhausted without reaching target -> opponent wins the point
    }

    /// <summary>Ported from src/game_state.py's ChaseResult.</summary>
    public readonly struct ChaseResult
    {
        public ChaseOutcome Outcome { get; }
        public ChaseResult(ChaseOutcome outcome) => Outcome = outcome;
    }

    /// <summary>Ported from src/game_state.py's RallyResult.</summary>
    public sealed class RallyResult
    {
        public string WinnerName { get; }
        public string Reason { get; }

        /// <summary>Number of attack exchanges before the point was scored.</summary>
        public int RallyLength { get; }

        public RallyResult(string winnerName, string reason, int rallyLength)
        {
            WinnerName = winnerName;
            Reason = reason;
            RallyLength = rallyLength;
        }
    }

    /// <summary>Ported from src/game_state.py's GameResult.</summary>
    public sealed class GameResult
    {
        public string WinnerName { get; }
        public IReadOnlyDictionary<string, int> Scores { get; }
        public IReadOnlyList<RallyResult> RallyResults { get; }

        public GameResult(string winnerName, IReadOnlyDictionary<string, int> scores, IReadOnlyList<RallyResult> rallyResults)
        {
            WinnerName = winnerName;
            Scores = scores;
            RallyResults = rallyResults;
        }

        public int TotalRallies => RallyResults.Count;

        public double AvgRallyLength =>
            RallyResults.Count == 0 ? 0.0 : RallyResults.Average(r => r.RallyLength);
    }
}
