using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Core;
using UnityEngine;

namespace RuleforgeTD.UI
{
    public readonly struct StageOneCardDisplay
    {
        public StageOneCardDisplay(
            string stableId,
            string name,
            string description,
            int tier = 1)
        {
            StableId = stableId ?? string.Empty;
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            ProjectileDescription = Description;
            EnemyDescription = Description;
            Tier = Math.Max(1, Math.Min(5, tier));
        }

        public StageOneCardDisplay(
            string stableId,
            string name,
            string projectileDescription,
            string enemyDescription,
            bool useEnemyInterpretation,
            int tier = 1)
        {
            StableId = stableId ?? string.Empty;
            Name = name ?? string.Empty;
            ProjectileDescription =
                projectileDescription ?? string.Empty;
            EnemyDescription = enemyDescription ?? string.Empty;
            Description = useEnemyInterpretation
                ? EnemyDescription
                : ProjectileDescription;
            Tier = Math.Max(1, Math.Min(5, tier));
        }

        public string StableId { get; }
        public string Name { get; }
        public string Description { get; }
        public string ProjectileDescription { get; }
        public string EnemyDescription { get; }
        public int Tier { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(StableId);

        public string GetDescription(SubjectType targetType)
        {
            return targetType == SubjectType.Enemy
                ? EnemyDescription
                : ProjectileDescription;
        }
    }

    /// <summary>
    /// Small Stage 01 localization catalog backed by a Unity TextAsset.
    /// Missing or malformed entries intentionally fall back to their stable
    /// localization keys so prototype UI remains usable and diagnosable.
    /// </summary>
    public sealed class StageOneUiTextCatalog
    {
        private const string UnknownLocale = "und";

        private readonly Dictionary<string, string> values =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private StageOneUiTextCatalog()
        {
        }

        public string Locale { get; private set; } = UnknownLocale;
        public bool IsLoaded { get; private set; }

        public static StageOneUiTextCatalog Load(TextAsset jsonAsset)
        {
            return FromJson(jsonAsset == null ? null : jsonAsset.text);
        }

        public static StageOneUiTextCatalog FromJson(string json)
        {
            var catalog = new StageOneUiTextCatalog();
            if (string.IsNullOrWhiteSpace(json))
            {
                return catalog;
            }

            try
            {
                CatalogDto source = JsonUtility.FromJson<CatalogDto>(json);
                if (source == null)
                {
                    return catalog;
                }

                catalog.Locale = string.IsNullOrWhiteSpace(source.locale)
                    ? UnknownLocale
                    : source.locale.Trim();
                catalog.AddEntries(source.strings);
                catalog.AddCards(source.cards);
                catalog.AddTowers(source.towers);
                catalog.IsLoaded = true;
            }
            catch (Exception)
            {
                // Prototype presentation must not prevent the battle scene
                // from starting. Unknown strings will visibly expose their
                // stable keys through Get instead.
            }

            return catalog;
        }

        public bool Contains(string key)
        {
            return !string.IsNullOrWhiteSpace(key) &&
                values.ContainsKey(key);
        }

        public bool TryGet(string key, out string value)
        {
            if (!string.IsNullOrWhiteSpace(key) &&
                values.TryGetValue(key, out string resolved) &&
                resolved != null)
            {
                value = resolved;
                return true;
            }

            value = NormalizeFallbackKey(key);
            return false;
        }

        public string Get(string key)
        {
            TryGet(key, out string value);
            return value;
        }

        public string Format(string key, params object[] arguments)
        {
            string format = Get(key);
            try
            {
                return string.Format(
                    format,
                    arguments ?? Array.Empty<object>());
            }
            catch (FormatException)
            {
                return NormalizeFallbackKey(key);
            }
        }

        public string GetPhase(string phaseId)
        {
            return Get(BuildStableKey(
                "phase",
                NormalizePhaseId(phaseId),
                null));
        }

        public string GetCardName(string cardId)
        {
            return Get(BuildStableKey("card", cardId, "name"));
        }

        public string GetCardProjectileDescription(string cardId)
        {
            return Get(BuildStableKey(
                "card",
                cardId,
                "projectile"));
        }

        public string GetCardEnemyDescription(string cardId)
        {
            return Get(BuildStableKey("card", cardId, "enemy"));
        }

        public StageOneCardDisplay GetCardDisplay(
            string cardId,
            bool useEnemyInterpretation = false,
            int tier = 1)
        {
            return new StageOneCardDisplay(
                cardId,
                GetCardName(cardId),
                GetCardProjectileDescription(cardId),
                GetCardEnemyDescription(cardId),
                useEnemyInterpretation,
                tier);
        }

        public string GetTowerName(string towerId)
        {
            return Get(BuildStableKey("tower", towerId, "name"));
        }

        public string GetTowerDescription(string towerId)
        {
            return Get(BuildStableKey(
                "tower",
                towerId,
                "description"));
        }

        private static string NormalizeFallbackKey(string key)
        {
            return string.IsNullOrWhiteSpace(key)
                ? string.Empty
                : key.Trim();
        }

        private static string BuildStableKey(
            string category,
            string stableId,
            string suffix)
        {
            string normalizedId = string.IsNullOrWhiteSpace(stableId)
                ? "unknown"
                : stableId.Trim();
            string prefix = category + ".";
            string normalizedSuffix = string.IsNullOrWhiteSpace(suffix)
                ? string.Empty
                : "." + suffix;

            if (normalizedId.StartsWith(prefix, StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(normalizedSuffix) ||
                    normalizedId.EndsWith(
                        normalizedSuffix,
                        StringComparison.Ordinal))
                {
                    return normalizedId;
                }

                return normalizedId + normalizedSuffix;
            }

            return prefix + normalizedId + normalizedSuffix;
        }

        private static string NormalizePhaseId(string phaseId)
        {
            string source = string.IsNullOrWhiteSpace(phaseId)
                ? "unknown"
                : phaseId.Trim();
            const string prefix = "phase.";
            if (source.StartsWith(prefix, StringComparison.Ordinal))
            {
                source = source.Substring(prefix.Length);
            }

            var result = new System.Text.StringBuilder(source.Length + 4);
            for (int i = 0; i < source.Length; i++)
            {
                char character = source[i];
                if (character == '-' || character == ' ')
                {
                    if (result.Length > 0 &&
                        result[result.Length - 1] != '_')
                    {
                        result.Append('_');
                    }

                    continue;
                }

                if (char.IsUpper(character) &&
                    result.Length > 0 &&
                    result[result.Length - 1] != '_')
                {
                    result.Append('_');
                }

                result.Append(char.ToLowerInvariant(character));
            }

            return result.Length == 0 ? "unknown" : result.ToString();
        }

        private void AddEntries(TextEntryDto[] entries)
        {
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                Add(entries[i]?.key, entries[i]?.value);
            }
        }

        private void AddCards(CardTextDto[] cards)
        {
            if (cards == null)
            {
                return;
            }

            for (int i = 0; i < cards.Length; i++)
            {
                CardTextDto card = cards[i];
                if (card == null || string.IsNullOrWhiteSpace(card.id))
                {
                    continue;
                }

                Add(BuildStableKey("card", card.id, "name"), card.name);
                Add(
                    BuildStableKey("card", card.id, "projectile"),
                    card.projectile);
                Add(
                    BuildStableKey("card", card.id, "enemy"),
                    card.enemy);
            }
        }

        private void AddTowers(TowerTextDto[] towers)
        {
            if (towers == null)
            {
                return;
            }

            for (int i = 0; i < towers.Length; i++)
            {
                TowerTextDto tower = towers[i];
                if (tower == null ||
                    string.IsNullOrWhiteSpace(tower.id))
                {
                    continue;
                }

                Add(
                    BuildStableKey("tower", tower.id, "name"),
                    tower.name);
                Add(
                    BuildStableKey("tower", tower.id, "description"),
                    tower.description);
            }
        }

        private void Add(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                value == null)
            {
                return;
            }

            values[key.Trim()] = value;
        }

        [Serializable]
        private sealed class CatalogDto
        {
            public string locale;
            public TextEntryDto[] strings = Array.Empty<TextEntryDto>();
            public CardTextDto[] cards = Array.Empty<CardTextDto>();
            public TowerTextDto[] towers = Array.Empty<TowerTextDto>();
        }

        [Serializable]
        private sealed class TextEntryDto
        {
            public string key;
            public string value;
        }

        [Serializable]
        private sealed class CardTextDto
        {
            public string id;
            public string name;
            public string projectile;
            public string enemy;
        }

        [Serializable]
        private sealed class TowerTextDto
        {
            public string id;
            public string name;
            public string description;
        }
    }
}
