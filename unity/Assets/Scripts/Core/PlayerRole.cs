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
        /// whichever lane(s) are adjacent to it. MB, in the middle, can reach all three;
        /// OH and OPP, on the outside, can only reach their own lane plus MB's.
        /// </summary>
        public static readonly IReadOnlyDictionary<PlayerRole, IReadOnlyList<int>> BlockableLanes =
            new Dictionary<PlayerRole, IReadOnlyList<int>>
            {
                { PlayerRole.Oh, new[] { 1, 2 } },
                { PlayerRole.Mb, new[] { 1, 2, 3 } },
                { PlayerRole.Opp, new[] { 2, 3 } },
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
