using System;
using System.Collections.Generic;

namespace VolleyballCore
{
    public static class RandomExtensions
    {
        /// <summary>Equivalent of Python's random.Random.choice() for a list.</summary>
        public static T Choice<T>(this Random rng, IReadOnlyList<T> items) => items[rng.Next(items.Count)];
    }
}
