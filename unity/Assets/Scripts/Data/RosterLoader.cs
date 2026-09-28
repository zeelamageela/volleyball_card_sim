using System;
using System.Collections.Generic;
using VolleyballCore;

namespace VolleyballData
{
    /// <summary>Ported from src/abilities.py's load_roster.</summary>
    public static class RosterLoader
    {
        /// <summary>
        /// Build {PlayerRole: PlayerCard} from a roster CSV (required columns:
        /// player_name, role). Players not found in playerCards get a plain card
        /// with no abilities.
        /// </summary>
        public static Dictionary<PlayerRole, PlayerCard> Load(string rosterPath, Dictionary<string, PlayerCard> playerCards)
        {
            var roster = new Dictionary<PlayerRole, PlayerCard>();
            foreach (var row in CsvUtil.ReadRows(rosterPath))
            {
                string name = CsvUtil.Get(row, "player_name").Trim();
                string roleStr = CsvUtil.Get(row, "role").Trim();
                string roleKey = EnumParsers.CanonicalRoleKey(roleStr);

                if (!EnumParsers.TryParseRole(roleKey, out PlayerRole role))
                {
                    throw new ArgumentException($"Unknown role '{roleStr}' for player '{name}' in {rosterPath}");
                }

                PlayerCard card;
                if (playerCards.TryGetValue($"{name}::{roleKey}", out var byRole))
                {
                    card = byRole;
                }
                else if (playerCards.TryGetValue(name, out var byName))
                {
                    card = byName;
                }
                else
                {
                    card = new PlayerCard(name, roleStr);
                }
                roster[role] = card;
            }
            return roster;
        }
    }
}
