#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
            int firstWaveIntervalTicks,
            WaveDefinitionDto[] waveOverrides)
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
            ValidateWaveOverrides(waveOverrides);

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
            for (int waveIndex = 0;
                 waveIndex < waveOverrides.Length;
                 waveIndex++)
            {
                json = ReplaceWaveSpawns(
                    json,
                    waveOverrides[waveIndex]);
            }
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

        private static void ValidateWaveOverrides(
            WaveDefinitionDto[] waveOverrides)
        {
            if (waveOverrides == null)
            {
                throw new ArgumentNullException(nameof(waveOverrides));
            }

            var waveIds = new HashSet<string>(
                StringComparer.Ordinal);
            for (int waveIndex = 0;
                 waveIndex < waveOverrides.Length;
                 waveIndex++)
            {
                WaveDefinitionDto wave = waveOverrides[waveIndex];
                if (wave == null ||
                    string.IsNullOrWhiteSpace(wave.id) ||
                    wave.spawns == null ||
                    wave.spawns.Length == 0 ||
                    !waveIds.Add(wave.id))
                {
                    throw new ArgumentException(
                        "Wave overrides need unique IDs and non-empty spawns.",
                        nameof(waveOverrides));
                }

                for (int spawnIndex = 0;
                     spawnIndex < wave.spawns.Length;
                     spawnIndex++)
                {
                    WaveSpawnDto spawn = wave.spawns[spawnIndex];
                    if (spawn == null ||
                        string.IsNullOrWhiteSpace(spawn.enemyId) ||
                        spawn.count <= 0 ||
                        spawn.firstSpawnTick < 0 ||
                        spawn.intervalTicks <= 0 ||
                        spawn.eliteTraitIds != null &&
                        spawn.eliteTraitIds.Any(
                            string.IsNullOrWhiteSpace))
                    {
                        throw new ArgumentException(
                            "Every wave override spawn needs valid content " +
                            "and positive count/interval values.",
                            nameof(waveOverrides));
                    }
                }
            }
        }

        private static string ReplaceWaveSpawns(
            string json,
            WaveDefinitionDto wave)
        {
            string waveIdPattern =
                "\\\"id\\\"\\s*:\\s*\\\"" +
                Regex.Escape(wave.id) +
                "\\\"";
            var waveIdRegex = new Regex(waveIdPattern);
            MatchCollection matches = waveIdRegex.Matches(json);
            if (matches.Count != 1)
            {
                throw new InvalidOperationException(
                    "Expected exactly one wave definition for '" +
                    wave.id +
                    "'.");
            }

            Match waveMatch = matches[0];
            int spawnsPropertyIndex = json.IndexOf(
                "\"spawns\"",
                waveMatch.Index + waveMatch.Length,
                StringComparison.Ordinal);
            int nextWaveIndex = json.IndexOf(
                "\"id\"",
                waveMatch.Index + waveMatch.Length,
                StringComparison.Ordinal);
            if (spawnsPropertyIndex < 0 ||
                nextWaveIndex >= 0 &&
                spawnsPropertyIndex > nextWaveIndex)
            {
                throw new InvalidOperationException(
                    "Could not locate spawns for '" + wave.id + "'.");
            }

            int arrayStart = json.IndexOf(
                '[',
                spawnsPropertyIndex);
            int arrayEnd = FindArrayEnd(json, arrayStart);
            string serialized = SerializeWaveSpawns(wave.spawns);
            return json.Substring(0, arrayStart) +
                serialized +
                json.Substring(arrayEnd + 1);
        }

        private static int FindArrayEnd(string json, int arrayStart)
        {
            if (arrayStart < 0 || arrayStart >= json.Length ||
                json[arrayStart] != '[')
            {
                throw new InvalidOperationException(
                    "Wave spawn array start is invalid.");
            }

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int index = arrayStart; index < json.Length; index++)
            {
                char value = json[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (value == '\\')
                    {
                        escaped = true;
                    }
                    else if (value == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (value == '"')
                {
                    inString = true;
                }
                else if (value == '[')
                {
                    depth++;
                }
                else if (value == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return index;
                    }
                }
            }

            throw new InvalidOperationException(
                "Wave spawn array is not closed.");
        }

        private static string SerializeWaveSpawns(
            WaveSpawnDto[] spawns)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[");
            for (int spawnIndex = 0;
                 spawnIndex < spawns.Length;
                 spawnIndex++)
            {
                WaveSpawnDto spawn = spawns[spawnIndex];
                builder.Append("        {\"enemyId\": \"")
                    .Append(EscapeJson(spawn.enemyId))
                    .Append("\", \"count\": ")
                    .Append(spawn.count)
                    .Append(", \"firstSpawnTick\": ")
                    .Append(spawn.firstSpawnTick)
                    .Append(", \"intervalTicks\": ")
                    .Append(spawn.intervalTicks);
                if (spawn.eliteTraitIds != null &&
                    spawn.eliteTraitIds.Length > 0)
                {
                    builder.Append(", \"eliteTraitIds\": [")
                        .Append(string.Join(
                            ", ",
                            spawn.eliteTraitIds.Select(
                                traitId =>
                                    "\"" +
                                    EscapeJson(traitId) +
                                    "\"")))
                        .Append(']');
                }
                builder.Append('}');
                if (spawnIndex < spawns.Length - 1)
                {
                    builder.Append(',');
                }
                builder.AppendLine();
            }
            builder.Append("      ]");
            return builder.ToString();
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
