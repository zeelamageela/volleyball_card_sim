using System.Collections.Generic;
using VolleyballCore;

namespace VolleyballData
{
    /// <summary>Ported from src/runtime_config.py's TeamRuntimeConfig dataclass.</summary>
    public sealed class TeamRuntimeConfig
    {
        public string TeamName { get; }
        public string RosterPath { get; } // null if unresolved
        public string SetTemplate { get; }
        public string PassiveAbility { get; }
        public string DeckType { get; }
        public bool UseHand { get; }
        public IReadOnlyDictionary<int, SetTemplate> SetterTemplates { get; }
        public IReadOnlyDictionary<int, SetTemplate> BrokenPlayTemplates { get; }

        public TeamRuntimeConfig(
            string teamName, string rosterPath, string setTemplate, string passiveAbility,
            string deckType, bool useHand,
            IReadOnlyDictionary<int, SetTemplate> setterTemplates,
            IReadOnlyDictionary<int, SetTemplate> brokenPlayTemplates)
        {
            TeamName = teamName;
            RosterPath = rosterPath;
            SetTemplate = setTemplate;
            PassiveAbility = passiveAbility;
            DeckType = deckType;
            UseHand = useHand;
            SetterTemplates = setterTemplates;
            BrokenPlayTemplates = brokenPlayTemplates;
        }
    }
}
