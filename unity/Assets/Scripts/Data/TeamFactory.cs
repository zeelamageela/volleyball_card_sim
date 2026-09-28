using System;
using System.Collections.Generic;
using VolleyballCore;

namespace VolleyballData
{
    /// <summary>
    /// Builds a ready-to-play VolleyballCore.Team (with its AbilityEngine wired up)
    /// from a loaded Mat. Mirrors the combination of Team construction and
    /// build_ability_engine that main.py performs together.
    /// </summary>
    public static class TeamFactory
    {
        public static Team BuildTeam(Mat mat, Random rng)
        {
            var team = new Team(mat.Name, rng, mat.UseHand, mat.DeckType, mat.PassiveAbility, mat.SetterTemplates, mat.BrokenPlayTemplates)
            {
                AbilityEngine = new AbilityEngine(new Dictionary<PlayerRole, PlayerCard>(mat.Seats)),
            };
            return team;
        }
    }
}
