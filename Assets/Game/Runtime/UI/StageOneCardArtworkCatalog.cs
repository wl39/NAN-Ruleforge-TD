using System.Collections.Generic;
using UnityEngine;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Resolves card artwork by stable content id. Keeping the convention in
    /// one place lets every card surface pick up new artwork automatically.
    /// </summary>
    public static class StageOneCardArtworkCatalog
    {
        public const string ResourceRoot =
            "RuleforgeTD/UI/Cards/Artwork/";

        private static readonly Dictionary<string, Sprite> Cache =
            new Dictionary<string, Sprite>();

        public static Sprite Load(string stableId)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                return null;
            }

            string normalizedId = stableId.Trim();
            if (Cache.TryGetValue(normalizedId, out Sprite sprite))
            {
                return sprite;
            }

            sprite = Resources.Load<Sprite>(
                ResourceRoot + normalizedId);
            Cache[normalizedId] = sprite;
            return sprite;
        }

        public static void ClearCache()
        {
            Cache.Clear();
        }
    }
}
