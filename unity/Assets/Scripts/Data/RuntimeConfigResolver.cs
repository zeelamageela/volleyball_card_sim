using System.Collections.Generic;
using System.IO;
using VolleyballCore;

namespace VolleyballData
{
    /// <summary>Ported from src/runtime_config.py's resolve_team_runtime_config.</summary>
    public static class RuntimeConfigResolver
    {
        public static TeamRuntimeConfig Resolve(
            string rosterPath,
            string teamName,
            string teamsCsvPath,
            string passivesCsvPath,
            string setTemplatesCsvPath)
        {
            var teamsByName = TeamConfigsLoader.Load(teamsCsvPath, passivesCsvPath);
            var bundles = SetTemplateBundlesLoader.Load(setTemplatesCsvPath);

            string rosterLookup = rosterPath != null ? Path.GetFileName(rosterPath).ToLowerInvariant() : null;

            TeamCsvConfig selected = null;
            if (rosterLookup != null)
            {
                foreach (var cfg in teamsByName.Values)
                {
                    if (cfg.RosterFile.Trim().ToLowerInvariant() == rosterLookup)
                    {
                        selected = cfg;
                        break;
                    }
                }
            }
            if (selected == null && !string.IsNullOrEmpty(teamName))
            {
                teamsByName.TryGetValue(teamName.Trim().ToLowerInvariant(), out selected);
            }

            if (selected == null)
            {
                string fallbackName = !string.IsNullOrEmpty(teamName)
                    ? teamName
                    : (rosterPath != null ? Path.GetFileNameWithoutExtension(rosterPath) : "Team");
                return new TeamRuntimeConfig(
                    fallbackName, rosterPath, fallbackName, null, "standard", true,
                    new Dictionary<int, SetTemplate>(SetTemplate.Universal),
                    new Dictionary<int, SetTemplate>(SetTemplate.BrokenPlay));
            }

            string dataDir = Path.GetDirectoryName(Path.GetFullPath(teamsCsvPath));
            string resolvedRoster = rosterPath;
            if (resolvedRoster == null && !string.IsNullOrEmpty(selected.RosterFile))
            {
                resolvedRoster = Path.Combine(dataDir ?? "", selected.RosterFile);
            }

            // Normal-play set templates are universal for every team (locked
            // 2026-09-05); per-team CSV "normal" bundles are no longer applied.
            // Broken-play templates remain per-team/CSV-driven (open question,
            // not yet revisited).
            bundles.TryGetValue(selected.SetTemplate.ToLowerInvariant(), out var chosenBundle);
            var normal = new Dictionary<int, SetTemplate>(SetTemplate.Universal);
            var broken = new Dictionary<int, SetTemplate>(SetTemplate.BrokenPlay);
            if (chosenBundle != null)
            {
                foreach (var kv in chosenBundle.Broken)
                {
                    broken[kv.Key] = kv.Value;
                }
            }

            return new TeamRuntimeConfig(
                selected.TeamName,
                resolvedRoster,
                selected.SetTemplate,
                selected.PassiveAbility,
                string.IsNullOrEmpty(selected.DeckType) ? "standard" : selected.DeckType,
                selected.UseHand,
                normal,
                broken);
        }
    }
}
