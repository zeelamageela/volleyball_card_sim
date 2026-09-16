using System.Collections.Generic;
using VolleyballCore;

namespace VolleyballData
{
    /// <summary>One template_name's normal/broken set-template rows from set_templates.csv.</summary>
    public sealed class SetTemplateBundle
    {
        public Dictionary<int, SetTemplate> Normal { get; } = new();
        public Dictionary<int, SetTemplate> Broken { get; } = new();
    }
}
