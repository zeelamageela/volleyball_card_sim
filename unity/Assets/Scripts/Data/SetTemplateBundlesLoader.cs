using System;
using System.Collections.Generic;
using VolleyballCore;

namespace VolleyballData
{
    /// <summary>Ported from src/runtime_config.py's load_template_bundles.</summary>
    public static class SetTemplateBundlesLoader
    {
        public static Dictionary<string, SetTemplateBundle> Load(string csvPath)
        {
            var bundles = new Dictionary<string, SetTemplateBundle>();
            foreach (var row in CsvUtil.ReadRows(csvPath))
            {
                string templateName = CsvUtil.Get(row, "template_name").Trim();
                string setType = CsvUtil.Get(row, "set_type").Trim().ToLowerInvariant();
                if (templateName.Length == 0 || (setType != "normal" && setType != "broken"))
                {
                    continue;
                }

                var (lo, hi) = ParseCardRange(CsvUtil.Get(row, "card_range"));
                var frontLanes = new List<int>();
                var backLanes = new List<int>();
                foreach (int lane in new[] { 1, 2, 3 })
                {
                    if (LaneEnabled(CsvUtil.Get(row, $"lane{lane}_front")))
                    {
                        frontLanes.Add(lane);
                    }
                }
                foreach (int lane in new[] { 1, 2, 3 })
                {
                    if (LaneEnabled(CsvUtil.Get(row, $"lane{lane}_back")))
                    {
                        backLanes.Add(lane);
                    }
                }
                string maxHittersRaw = CsvUtil.Get(row, "max_hitters").Trim();
                int maxHitters = maxHittersRaw.Length > 0 ? int.Parse(maxHittersRaw) : 1;

                string key = templateName.ToLowerInvariant();
                if (!bundles.TryGetValue(key, out var bundle))
                {
                    bundle = new SetTemplateBundle();
                    bundles[key] = bundle;
                }
                var target = setType == "normal" ? bundle.Normal : bundle.Broken;
                for (int v = Math.Max(1, lo); v <= Math.Min(10, hi); v++)
                {
                    target[v] = new SetTemplate(frontLanes, backLanes, maxHitters);
                }
            }
            return bundles;
        }

        private static (int Lo, int Hi) ParseCardRange(string spec)
        {
            string raw = (spec ?? "").Trim();
            if (raw.Length == 0)
            {
                throw new FormatException("empty card_range");
            }
            int dashIdx = raw.IndexOf('-');
            if (dashIdx >= 0)
            {
                int lo = int.Parse(raw.Substring(0, dashIdx).Trim());
                int hi = int.Parse(raw.Substring(dashIdx + 1).Trim());
                if (lo > hi)
                {
                    (lo, hi) = (hi, lo);
                }
                return (lo, hi);
            }
            int val = int.Parse(raw);
            return (val, val);
        }

        private static bool LaneEnabled(string cell)
        {
            string token = (cell ?? "").Trim().ToLowerInvariant();
            return token != "" && token != "-" && token != "—" && token != "none" && token != "n/a";
        }
    }
}
