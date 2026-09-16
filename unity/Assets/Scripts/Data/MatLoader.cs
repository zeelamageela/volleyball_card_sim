using System.Collections.Generic;
using System.IO;
using VolleyballCore;

namespace VolleyballData
{
    /// <summary>Ported from src/mats.py's load_mat / load_all_mats.</summary>
    public static class MatLoader
    {
        /// <summary>A mat is a real, hand-using team on the standard deck (not a dummy AI opponent).</summary>
        private static bool IsPickableMat(TeamRuntimeConfig cfg) => cfg.UseHand && cfg.DeckType == "standard";

        /// <summary>Load one mat by team name (e.g. "Blitz").</summary>
        public static Mat LoadMat(
            string teamName, string teamsCsvPath, string playerCardsCsvPath,
            string passivesCsvPath, string setTemplatesCsvPath)
        {
            var cfg = RuntimeConfigResolver.Resolve(null, teamName, teamsCsvPath, passivesCsvPath, setTemplatesCsvPath);
            var playerCards = PlayerCardsLoader.Load(playerCardsCsvPath);
            return BuildMat(cfg, playerCards);
        }

        /// <summary>Load every pickable preset mat (today: Blitz, Grind, Spread, Backline).</summary>
        public static List<Mat> LoadAllMats(
            string teamsCsvPath, string playerCardsCsvPath, string passivesCsvPath, string setTemplatesCsvPath)
        {
            var teamConfigs = TeamConfigsLoader.Load(teamsCsvPath, passivesCsvPath);
            var playerCards = PlayerCardsLoader.Load(playerCardsCsvPath);
            var mats = new List<Mat>();
            foreach (var cfgRaw in teamConfigs.Values)
            {
                var cfg = RuntimeConfigResolver.Resolve(null, cfgRaw.TeamName, teamsCsvPath, passivesCsvPath, setTemplatesCsvPath);
                if (IsPickableMat(cfg))
                {
                    mats.Add(BuildMat(cfg, playerCards));
                }
            }
            return mats;
        }

        private static Mat BuildMat(TeamRuntimeConfig cfg, Dictionary<string, PlayerCard> playerCards)
        {
            if (string.IsNullOrEmpty(cfg.RosterPath) || !File.Exists(cfg.RosterPath))
            {
                throw new FileNotFoundException($"No roster file found for mat '{cfg.TeamName}'");
            }
            var seats = RosterLoader.Load(cfg.RosterPath, playerCards);
            return new Mat(cfg.TeamName, seats, cfg.PassiveAbility, cfg.DeckType, cfg.UseHand, cfg.SetterTemplates, cfg.BrokenPlayTemplates);
        }
    }
}
