using System.Collections.Generic;

namespace VolleyballCore
{
    /// <summary>Ported from src/players.py's PlayerRole enum.</summary>
    public enum PlayerRole
    {
        Setter, // pos 1 -- back row; cannot be served to
        Opp,    // pos 2 -- front row Right
        Mb,     // pos 3 -- front row Middle
        Oh,     // pos 4 -- front row Left
        Ds,     // pos 5 -- back row
        Libero, // pos 6 -- back row; cannot attack
    }

    public static class PlayerRoleExtensions
    {
        private static readonly HashSet<PlayerRole> FrontRowRolesSet = new()
        {
            PlayerRole.Oh, PlayerRole.Mb, PlayerRole.Opp,
        };

        private static readonly HashSet<PlayerRole> BackRowRolesSet = new()
        {
            PlayerRole.Setter, PlayerRole.Ds, PlayerRole.Libero,
        };

        /// <summary>Lane index (1=Left/OH, 2=Middle/MB, 3=Right/OPP) -> attacking role.</summary>
        public static readonly IReadOnlyDictionary<int, PlayerRole> LaneToRole = new Dictionary<int, PlayerRole>
        {
            { 1, PlayerRole.Oh },
            { 2, PlayerRole.Mb },
            { 3, PlayerRole.Opp },
        };

        /// <summary>
        /// Lanes a front-row blocker can reach: their own lane (see LaneToRole) plus
        /// whichever lane(s) are adjacent to it. MB, in the middle, can reach all three.
        /// The two teams' courts are physically mirrored, so OH's lane 1 sits across the
        /// net from the opponent's lane 3, not their lane 1: OH reaches lanes 3 and 2,
        /// while OPP, mirrored the other way, reaches lanes 1 and 2.
        /// </summary>
        public static readonly IReadOnlyDictionary<PlayerRole, IReadOnlyList<int>> BlockableLanes =
            new Dictionary<PlayerRole, IReadOnlyList<int>>
            {
                { PlayerRole.Oh, new[] { 3, 2 } },
                { PlayerRole.Mb, new[] { 1, 2, 3 } },
                { PlayerRole.Opp, new[] { 1, 2 } },
            };

        /// <summary>
        /// Attacking lane index -> the DEFENDING team's own primary (home-reach) blocker
        /// for that lane, i.e. LaneToRole's mirror-image counterpart: the two teams'
        /// courts are physically mirrored (see BlockableLanes), so the attacker's lane 1
        /// faces the defender's OPP, not their OH, across the net. Presentation-facing
        /// (e.g. positioning a revealed block value above the physically correct
        /// blocker) -- Core's own quick-set blind block doesn't track a specific role at
        /// all (see Rally.cs's PhaseBlockCommit), so this is the same "primary" choice
        /// DummyStrategy/SmartStrategy already converge on via BlockableLanes for a
        /// single forced blocker.
        /// </summary>
        public static readonly IReadOnlyDictionary<int, PlayerRole> LaneToDefendingRole = new Dictionary<int, PlayerRole>
        {
            { 1, PlayerRole.Opp },
            { 2, PlayerRole.Mb },
            { 3, PlayerRole.Oh },
        };

        public static bool IsFrontRow(this PlayerRole role) => FrontRowRolesSet.Contains(role);
        public static bool IsBackRow(this PlayerRole role) => BackRowRolesSet.Contains(role);

        /// <summary>Matches Python's PlayerRole.value display strings exactly.</summary>
        public static string DisplayName(this PlayerRole role) => role switch
        {
            PlayerRole.Setter => "Setter",
            PlayerRole.Opp => "OPP",
            PlayerRole.Mb => "MB",
            PlayerRole.Oh => "OH",
            PlayerRole.Ds => "DS",
            PlayerRole.Libero => "Libero",
            _ => role.ToString(),
        };
    }
}
