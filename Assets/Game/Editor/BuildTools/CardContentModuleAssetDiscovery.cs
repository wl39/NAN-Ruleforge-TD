using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RuleforgeTD.Editor.BuildTools
{
    /// <summary>
    /// Discovers card module TextAssets in deterministic asset-path order.
    /// This boundary has no catalog or scene dependency, so every editor
    /// composition root can reuse the same discovery behavior.
    /// </summary>
    public static class CardContentModuleAssetDiscovery
    {
        public const string ModuleRoot =
            "Assets/Game/Data/Cards";

        public static string[] DiscoverAssetPaths()
        {
            return DiscoverAssetPaths(ModuleRoot);
        }

        public static string[] DiscoverAssetPaths(string moduleRoot)
        {
            if (string.IsNullOrWhiteSpace(moduleRoot))
            {
                throw new ArgumentException(
                    "A module asset root is required.",
                    nameof(moduleRoot));
            }

            string normalizedRoot = moduleRoot.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(normalizedRoot))
            {
                return Array.Empty<string>();
            }

            string[] guids = AssetDatabase.FindAssets(
                "t:TextAsset",
                new[] { normalizedRoot });
            var paths = new List<string>(guids.Length);
            string requiredPrefix = normalizedRoot + "/";
            for (int i = 0; i < guids.Length; i++)
            {
                string path =
                    AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path) ||
                    !path.StartsWith(
                        requiredPrefix,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        Path.GetExtension(path),
                        ".json",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                paths.Add(path);
            }

            paths.Sort(StringComparer.Ordinal);
            return paths.ToArray();
        }

        public static TextAsset[] DiscoverTextAssets()
        {
            string[] paths = DiscoverAssetPaths();
            var result = new TextAsset[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                result[i] =
                    AssetDatabase.LoadAssetAtPath<TextAsset>(
                        paths[i]);
                if (result[i] == null)
                {
                    throw new InvalidOperationException(
                        "Discovered card module is not a TextAsset: " +
                        paths[i]);
                }
            }

            return result;
        }
    }
}
