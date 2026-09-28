using System.Collections.Generic;
using VolleyballCore;

namespace VolleyballData
{
    /// <summary>
    /// A team mat: the fixed 6-seat team identity picked for a run. Ported from
    /// src/mats.py's Mat. Each seat holds one player and (optionally) one passive
    /// ability -- some seats are intentionally blank, which is a valid design, not
    /// a bug.
    /// </summary>
    public sealed class Mat
    {
        // Seat order matches on-court position 1-6 (VolleyballCore.Team's player list).
        private static readonly PlayerRole[] SeatOrder =
        {
            PlayerRole.Setter, PlayerRole.Opp, PlayerRole.Mb, PlayerRole.Oh, PlayerRole.Ds, PlayerRole.Libero,
        };

        public string Name { get; }
        public IReadOnlyDictionary<PlayerRole, PlayerCard> Seats { get; }
        public string PassiveAbility { get; }
        public string DeckType { get; }
        public bool UseHand { get; }
        public IReadOnlyDictionary<int, SetTemplate> SetterTemplates { get; }
        public IReadOnlyDictionary<int, SetTemplate> BrokenPlayTemplates { get; }

        public Mat(
            string name,
            IReadOnlyDictionary<PlayerRole, PlayerCard> seats,
            string passiveAbility,
            string deckType,
            bool useHand,
            IReadOnlyDictionary<int, SetTemplate> setterTemplates,
            IReadOnlyDictionary<int, SetTemplate> brokenPlayTemplates)
        {
            Name = name;
            Seats = seats;
            PassiveAbility = passiveAbility;
            DeckType = deckType;
            UseHand = useHand;
            SetterTemplates = setterTemplates;
            BrokenPlayTemplates = brokenPlayTemplates;
        }

        public PlayerCard Seat(PlayerRole role) => Seats[role];

        /// <summary>Human-readable summary: one line per seat, plus its ability if any.</summary>
        public string Describe()
        {
            string header = $"{Name} mat";
            if (!string.IsNullOrEmpty(PassiveAbility))
            {
                header += $"  (team passive: {PassiveAbility})";
            }
            var lines = new List<string> { header };

            foreach (var role in SeatOrder)
            {
                if (!Seats.TryGetValue(role, out var card))
                {
                    lines.Add($"  {role.DisplayName(),-8} —");
                    continue;
                }
                if (card.Abilities.Count == 0)
                {
                    lines.Add($"  {role.DisplayName(),-8} {card.PlayerName,-10} (no ability)");
                    continue;
                }
                foreach (var a in card.Abilities)
                {
                    string cond = !string.IsNullOrEmpty(a.ConditionField) ? $"{a.ConditionField}{a.ConditionValue}" : "always";
                    lines.Add(
                        $"  {role.DisplayName(),-8} {card.PlayerName,-10} {a.AbilityName}"
                        + $"  [{a.Trigger}, {cond} -> {a.Effect}={a.EffectValue}]");
                }
            }
            return string.Join("\n", lines);
        }
    }
}
