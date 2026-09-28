namespace VolleyballData
{
    /// <summary>Ported from src/runtime_config.py's TeamCsvConfig dataclass.</summary>
    public sealed class TeamCsvConfig
    {
        public string TeamName { get; }
        public string RosterFile { get; }
        public string SetTemplate { get; }
        public string PassiveAbility { get; } // null = none
        public string DeckType { get; }
        public bool UseHand { get; }

        public TeamCsvConfig(
            string teamName, string rosterFile, string setTemplate,
            string passiveAbility, string deckType, bool useHand)
        {
            TeamName = teamName;
            RosterFile = rosterFile;
            SetTemplate = setTemplate;
            PassiveAbility = passiveAbility;
            DeckType = deckType;
            UseHand = useHand;
        }
    }
}
