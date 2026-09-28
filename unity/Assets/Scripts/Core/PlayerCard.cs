using System.Collections.Generic;

namespace VolleyballCore
{
    /// <summary>Ported from src/abilities.py's PlayerCard dataclass.</summary>
    public sealed class PlayerCard
    {
        public string PlayerName { get; }
        public string RoleName { get; } // string role, e.g. "OH"
        public List<Ability> Abilities { get; }

        public PlayerCard(string playerName, string roleName, List<Ability> abilities = null)
        {
            PlayerName = playerName;
            RoleName = roleName;
            Abilities = abilities ?? new List<Ability>();
        }
    }
}
