using System.Collections.Generic;
using System.IO;

namespace VolleyballData
{
    /// <summary>Ported from src/runtime_config.py's load_team_configs.</summary>
    public static class TeamConfigsLoader
    {
        public static Dictionary<string, TeamCsvConfig> Load(string teamsCsvPath, string passivesCsvPath = null)
        {
            var passivesByTeam = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(passivesCsvPath) && File.Exists(passivesCsvPath))
            {
                foreach (var row in CsvUtil.ReadRows(passivesCsvPath))
                {
                    string team = CsvUtil.Get(row, "team_name").Trim();
                    string passive = CsvUtil.Get(row, "passive_name").Trim();
                    bool active = AsBool(CsvUtil.Get(row, "is_active"), false);
                    if (team.Length > 0 && passive.Length > 0 && active)
                    {
                        passivesByTeam[team.ToLowerInvariant()] = passive;
                    }
                }
            }

            var teams = new Dictionary<string, TeamCsvConfig>();
            foreach (var row in CsvUtil.ReadRows(teamsCsvPath))
            {
                string teamName = CsvUtil.Get(row, "team_name").Trim();
                string rosterFile = CsvUtil.Get(row, "roster_file").Trim();
                if (teamName.Length == 0)
                {
                    continue;
                }

                string passive = CleanPassive(CsvUtil.Get(row, "passive_ability"));
                if (passive == null)
                {
                    passivesByTeam.TryGetValue(teamName.ToLowerInvariant(), out passive);
                }

                string setTemplateRaw = CsvUtil.Get(row, "set_template").Trim();
                string setTemplate = setTemplateRaw.Length > 0 ? setTemplateRaw : teamName;

                string deckTypeRaw = CsvUtil.Get(row, "deck_type").Trim();
                string deckType = deckTypeRaw.Length > 0 ? deckTypeRaw : "standard";

                teams[teamName.ToLowerInvariant()] = new TeamCsvConfig(
                    teamName, rosterFile, setTemplate, passive, deckType,
                    AsBool(CsvUtil.Get(row, "use_hand"), true));
            }
            return teams;
        }

        private static bool AsBool(string value, bool defaultValue)
        {
            string v = (value ?? "").Trim().ToLowerInvariant();
            if (v == "true" || v == "1" || v == "yes" || v == "y")
            {
                return true;
            }
            if (v == "false" || v == "0" || v == "no" || v == "n")
            {
                return false;
            }
            return defaultValue;
        }

        private static string CleanPassive(string value)
        {
            string v = (value ?? "").Trim();
            if (v.Length == 0)
            {
                return null;
            }
            string lower = v.ToLowerInvariant();
            if (lower == "tbd" || lower == "none" || lower == "null" || v == "-" || v == "—")
            {
                return null;
            }
            return v;
        }
    }
}
