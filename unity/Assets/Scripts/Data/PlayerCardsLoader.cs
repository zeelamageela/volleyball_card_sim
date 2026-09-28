using System.Collections.Generic;
using VolleyballCore;

namespace VolleyballData
{
    /// <summary>Ported from src/abilities.py's load_player_cards.</summary>
    public static class PlayerCardsLoader
    {
        /// <summary>
        /// Load all player-ability definitions from a CSV. A player may appear on
        /// multiple rows -- one row per ability. To define a player with no
        /// abilities, include a single row and leave ability_name blank.
        ///
        /// Duplicate display names across roles are supported: when a name appears
        /// on multiple roles, rows are stored under role-aware keys of the form
        /// "Name::ROLE" and the first occurrence also keeps the legacy plain-name
        /// key (aliased to the same PlayerCard instance as whichever role-qualified
        /// entry was seen first in the file -- matches Python's object-reference
        /// aliasing exactly, not a copy).
        /// </summary>
        public static Dictionary<string, PlayerCard> Load(string path)
        {
            var cards = new Dictionary<string, PlayerCard>();
            var rows = CsvUtil.ReadRows(path);

            var nameRoles = new Dictionary<string, HashSet<string>>();
            foreach (var row in rows)
            {
                string name = CsvUtil.Get(row, "player_name").Trim();
                if (name.Length == 0)
                {
                    continue;
                }
                string roleKey = EnumParsers.CanonicalRoleKey(CsvUtil.Get(row, "role"));
                if (!nameRoles.TryGetValue(name, out var roles))
                {
                    roles = new HashSet<string>();
                    nameRoles[name] = roles;
                }
                roles.Add(roleKey);
            }

            var ambiguousNames = new HashSet<string>();
            foreach (var kv in nameRoles)
            {
                if (kv.Value.Count > 1)
                {
                    ambiguousNames.Add(kv.Key);
                }
            }

            foreach (var row in rows)
            {
                string name = CsvUtil.Get(row, "player_name").Trim();
                if (name.Length == 0)
                {
                    continue;
                }
                string roleStr = CsvUtil.Get(row, "role").Trim();
                string roleKey = EnumParsers.CanonicalRoleKey(roleStr);
                bool ambiguous = ambiguousNames.Contains(name);
                string lookupKey = ambiguous ? $"{name}::{roleKey}" : name;

                if (!cards.ContainsKey(lookupKey))
                {
                    cards[lookupKey] = new PlayerCard(name, roleStr);
                    if (ambiguous && !cards.ContainsKey(name))
                    {
                        cards[name] = cards[lookupKey];
                    }
                }

                string abilityName = CsvUtil.Get(row, "ability_name").Trim();
                if (abilityName.Length == 0)
                {
                    continue; // plain player, no ability on this row
                }

                int.TryParse(CsvUtil.Get(row, "effect_value").Trim(), out int effectValue);
                bool isActive = CsvUtil.Get(row, "is_active").Trim().ToLowerInvariant() == "true";

                cards[lookupKey].Abilities.Add(new Ability(
                    abilityName,
                    EnumParsers.ParseTrigger(CsvUtil.Get(row, "trigger")),
                    CsvUtil.Get(row, "condition_field").Trim(),
                    CsvUtil.Get(row, "condition_value").Trim(),
                    EnumParsers.ParseEffect(CsvUtil.Get(row, "effect")),
                    effectValue,
                    isActive,
                    CsvUtil.Get(row, "description").Trim()));
            }

            return cards;
        }
    }
}
