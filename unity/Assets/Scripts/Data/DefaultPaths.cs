using System.IO;
using UnityEngine;

namespace VolleyballData
{
    /// <summary>
    /// Editor/Play-Mode convenience: resolves the shared data/ folder that lives
    /// alongside the Unity project (sibling to unity/), matching how the Python
    /// CLI resolves the same folder relative to the repo root.
    ///
    /// Not used by build players -- once this ships as a standalone/mobile
    /// player, the CSVs need to be copied into Assets/StreamingAssets and read
    /// via Application.streamingAssetsPath instead. That's a build-pipeline
    /// concern for later, not needed for Editor Play Mode testing.
    /// </summary>
    public static class DefaultPaths
    {
        public static string DataDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data"));

        public static string PlayerCardsCsv => Path.Combine(DataDir, "player_cards.csv");
        public static string TeamsCsv => Path.Combine(DataDir, "teams.csv");
        public static string TeamPassivesCsv => Path.Combine(DataDir, "team_passives.csv");
        public static string SetTemplatesCsv => Path.Combine(DataDir, "set_templates.csv");
        public static string DeckTypesCsv => Path.Combine(DataDir, "deck_types.csv");
    }
}
