#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RuleforgeTD.GameLogic.Content;
using UnityEditor;
using UnityEngine;

namespace RuleforgeTD.Editor.AssetImport
{
    /// <summary>
    /// Creates per-stage runtime content from the common combat catalog while
    /// keeping route, build sites and starter ownership as stage-owned data.
    /// </summary>
    internal static class StageContentAuthoring
    {
        private const string BaseContentPath =
            "Assets/Game/Data/Logic/phase1-content.json";

        public static void Create(
            string destinationPath,
            Vector2[] pathPoints,
            Vector2[] buildSpots,
            int[] buildSpotUnlockCosts,
            string[] startingCards,
            int firstWaveEnemyCount,
            int firstWaveIntervalTicks)
        {
            if (pathPoints == null || pathPoints.Length < 2)
            {
                throw new ArgumentException(
                    "A stage needs at least two path points.",
                    nameof(pathPoints));
            }
            if (buildSpots == null || buildSpots.Length == 0 ||
                buildSpotUnlockCosts == null ||
                buildSpotUnlockCosts.Length != buildSpots.Length)
            {
                throw new ArgumentException(
                    "Build spots and unlock costs must be non-empty and aligned.");
            }
            if (startingCards == null ||
                startingCards.Length == 0 ||
                startingCards.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException(
                    "A stage needs at least one valid starting card.",
                    nameof(startingCards));
            }
            if (firstWaveEnemyCount <= 0 ||
                firstWaveIntervalTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(firstWaveEnemyCount),
                    "First-wave count and interval must both be positive.");
            }

            string json = File.ReadAllText(BaseContentPath);
            json = ReplaceIntegerArray(
                json,
                "pathPointXMilli",
                pathPoints.Select(point =>
                    Mathf.RoundToInt(point.x * 1000f)).ToArray());
            json = ReplaceIntegerArray(
                json,
                "pathPointYMilli",
                pathPoints.Select(point =>
                    Mathf.RoundToInt(point.y * 1000f)).ToArray());
            json = ReplaceIntegerArray(
                json,
                "buildSpotXMilli",
                buildSpots.Select(point =>
                    Mathf.RoundToInt(point.x * 1000f)).ToArray());
            json = ReplaceIntegerArray(
                json,
                "buildSpotYMilli",
                buildSpots.Select(point =>
                    Mathf.RoundToInt(point.y * 1000f)).ToArray());
            json = ReplaceIntegerArray(
                json,
                "buildSpotUnlockCosts",
                buildSpotUnlockCosts);
            json = ReplaceStringArray(
                json,
                "startingCards",
                startingCards);
            json = ReplaceFirstWaveSchedule(
                json,
                firstWaveEnemyCount,
                firstWaveIntervalTicks);
            File.WriteAllText(
                destinationPath,
                json,
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(
                destinationPath,
                ImportAssetOptions.ForceSynchronousImport);
        }

        public static string[] ReadStartingCards(TextAsset content)
        {
            if (content == null || string.IsNullOrWhiteSpace(content.text))
            {
                throw new ArgumentNullException(nameof(content));
            }

            ContentCatalogDto dto =
                JsonUtility.FromJson<ContentCatalogDto>(content.text);
            if (dto == null || dto.run == null ||
                dto.run.startingCards == null ||
                dto.run.startingCards.Length == 0)
            {
                throw new InvalidOperationException(
                    "Stage content has no starting-card definition.");
            }

            return (string[])dto.run.startingCards.Clone();
        }

        private static string ReplaceIntegerArray(
            string json,
            string propertyName,
            int[] values)
        {
            return ReplaceArray(
                json,
                propertyName,
                string.Join(", ", values));
        }

        private static string ReplaceStringArray(
            string json,
            string propertyName,
            string[] values)
        {
            string serialized = string.Join(
                ", ",
                values.Select(value =>
                    "\"" + EscapeJson(value.Trim()) + "\""));
            return ReplaceArray(json, propertyName, serialized);
        }

        private static string ReplaceArray(
            string json,
            string propertyName,
            string serializedValues)
        {
            string pattern =
                "(\\\"" +
                Regex.Escape(propertyName) +
                "\\\"\\s*:\\s*)\\[[^\\]]*\\]";
            var regex = new Regex(pattern);
            MatchCollection matches = regex.Matches(json);
            if (matches.Count != 1)
            {
                throw new InvalidOperationException(
                    "Expected exactly one " +
                    propertyName +
                    " array in the base content JSON.");
            }

            return regex.Replace(
                json,
                match =>
                    match.Groups[1].Value +
                    "[" +
                    serializedValues +
                    "]",
                1);
        }

        private static string EscapeJson(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private static string ReplaceFirstWaveSchedule(
            string json,
            int enemyCount,
            int intervalTicks)
        {
            const string pattern =
                "(\\\"id\\\"\\s*:\\s*\\\"wave_1\\\"" +
                "[\\s\\S]*?\\\"spawns\\\"\\s*:\\s*\\[\\s*\\{" +
                "\\s*\\\"enemyId\\\"\\s*:\\s*\\\"raider\\\"" +
                "\\s*,\\s*\\\"count\\\"\\s*:\\s*)\\d+" +
                "(\\s*,\\s*\\\"firstSpawnTick\\\"\\s*:\\s*0" +
                "\\s*,\\s*\\\"intervalTicks\\\"\\s*:\\s*)\\d+";
            var regex = new Regex(pattern);
            MatchCollection matches = regex.Matches(json);
            if (matches.Count != 1)
            {
                throw new InvalidOperationException(
                    "Expected exactly one raider schedule in wave_1.");
            }

            return regex.Replace(
                json,
                match =>
                    match.Groups[1].Value +
                    enemyCount +
                    match.Groups[2].Value +
                    intervalTicks,
                1);
        }
    }
}
#endif
