using System.Collections.Generic;

namespace VolleyballCore
{
    /// <summary>
    /// Defines which lanes are available for attack after a set.
    /// Ported from src/players.py's SetTemplate dataclass.
    /// </summary>
    public sealed class SetTemplate
    {
        /// <summary>Lane indices available for front-row attackers.</summary>
        public IReadOnlyList<int> FrontLanes { get; }

        /// <summary>Lane indices available for back-row attackers (DS/Setter, NOT Libero).</summary>
        public IReadOnlyList<int> BackLanes { get; }

        /// <summary>Maximum number of attack cards to place.</summary>
        public int MaxAttackers { get; }

        public SetTemplate(IReadOnlyList<int> frontLanes, IReadOnlyList<int> backLanes, int maxAttackers)
        {
            FrontLanes = frontLanes;
            BackLanes = backLanes;
            MaxAttackers = maxAttackers;
        }

        private static readonly int[] AllLanes = { 1, 2, 3 };
        private static readonly int[] NoLanes = System.Array.Empty<int>();

        /// <summary>
        /// Universal set template (locked ruleset, 2026-09-05) -- the same for every
        /// team. Ported from src/players.py's _build_setter_templates()/SETTER_TEMPLATES.
        ///   1-3:  Quickset -- up to 2 lanes, front row only, single-blocker/no-stack block
        ///   4-7:  up to 2 lanes, any front/back mix, normal stacking
        ///   8-10: up to 3 lanes, any front/back mix, normal stacking
        /// </summary>
        public static readonly IReadOnlyDictionary<int, SetTemplate> Universal = BuildUniversal();

        /// <summary>
        /// Broken-play templates (setter can't receive their own set). Still
        /// per-team/CSV-driven in the Python engine and explicitly NOT yet
        /// revisited as of 2026-09-05 -- ported here as-is (see CHANGELOG.md).
        /// Ported from src/players.py's _build_broken_play_templates()/BROKEN_PLAY_TEMPLATES.
        /// </summary>
        public static readonly IReadOnlyDictionary<int, SetTemplate> BrokenPlay = BuildBrokenPlay();

        private static Dictionary<int, SetTemplate> BuildUniversal()
        {
            var templates = new Dictionary<int, SetTemplate>();

            foreach (int v in new[] { 1, 2, 3 })
            {
                templates[v] = new SetTemplate(AllLanes, NoLanes, 2);
            }
            foreach (int v in new[] { 4, 5, 6, 7 })
            {
                templates[v] = new SetTemplate(AllLanes, AllLanes, 2);
            }
            foreach (int v in new[] { 8, 9, 10 })
            {
                templates[v] = new SetTemplate(AllLanes, AllLanes, 3);
            }
            return templates;
        }

        private static Dictionary<int, SetTemplate> BuildBrokenPlay()
        {
            var templates = new Dictionary<int, SetTemplate>();

            foreach (int v in new[] { 1, 2, 3 })
            {
                templates[v] = new SetTemplate(new[] { 1, 2 }, new[] { 2 }, 2);
            }
            foreach (int v in new[] { 4, 5, 6, 7 })
            {
                templates[v] = new SetTemplate(new[] { 1, 3 }, new[] { 2 }, 1);
            }
            foreach (int v in new[] { 8, 9, 10 })
            {
                templates[v] = new SetTemplate(new[] { 2, 3 }, new[] { 2 }, 2);
            }
            return templates;
        }
    }
}
