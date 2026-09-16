using System;

namespace VolleyballCore
{
    /// <summary>Ported from src/game.py's module-level resolve_attack / _cover_threshold / get_dig_defender_role.</summary>
    public static class AttackResolution
    {
        /// <summary>
        /// Determine attack outcome purely from numeric values.
        /// Attack &gt; Block  -&gt; Kill
        /// Attack == Block -&gt; Deflect (exact tie; ball falls to attacker's own side)
        /// Attack &lt; Block  -&gt; Stuffed
        /// </summary>
        public static AttackOutcomeType ResolveAttack(int attackValue, int blockValue)
        {
            if (attackValue > blockValue)
            {
                return AttackOutcomeType.Kill;
            }
            if (attackValue == blockValue)
            {
                return AttackOutcomeType.Deflect;
            }
            return AttackOutcomeType.Stuffed;
        }

        // Baseline threshold for the universal setter-cover mechanic.
        // Any team with a Libero or DS covers at this level (~50% of draws).
        private const int SetterCoverBase = 6;

        /// <summary>
        /// Return the dig-card threshold needed for a Libero/DS to cover the setter's
        /// zone, or 0 if the team has no eligible player (no Libero or DS on roster).
        /// </summary>
        internal static int CoverThreshold(Team team)
        {
            bool hasEligible = false;
            foreach (var p in team.Players)
            {
                if (p.Role == PlayerRole.Libero || p.Role == PlayerRole.Ds)
                {
                    hasEligible = true;
                    break;
                }
            }
            if (!hasEligible)
            {
                return 0;
            }

            int threshold = SetterCoverBase;
            if (team.AbilityEngine != null)
            {
                int abilityThreshold = team.AbilityEngine.SetterCoverThreshold();
                if (abilityThreshold > 0)
                {
                    threshold = Math.Min(threshold, abilityThreshold);
                }
            }
            return threshold;
        }

        /// <summary>
        /// Determine which defender digs based on attack lane and card parity.
        /// All even-value attacks -&gt; Libero. Odd attacks split by lane:
        /// lane 1/2 -&gt; Setter, lane 3 -&gt; DS.
        /// </summary>
        public static PlayerRole GetDigDefenderRole(int attackLane, int attackCardValue)
        {
            if (attackCardValue % 2 == 0)
            {
                return PlayerRole.Libero;
            }
            return attackLane == 1 || attackLane == 2 ? PlayerRole.Setter : PlayerRole.Ds;
        }
    }
}
