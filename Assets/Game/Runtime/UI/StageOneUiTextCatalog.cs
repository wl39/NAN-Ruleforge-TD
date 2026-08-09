using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
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
            int tier = 1,
            string symbolKey = null)
        {
            StableId = stableId ?? string.Empty;
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            ProjectileDescription = Description;
            EnemyDescription = Description;
            Tier = Math.Max(1, Math.Min(5, tier));
            SymbolKey = symbolKey ?? string.Empty;
        }

        public StageOneCardDisplay(
            string stableId,
            string name,
            string projectileDescription,
            string enemyDescription,
            bool useEnemyInterpretation,
            int tier = 1,
            string symbolKey = null)
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
            SymbolKey = symbolKey ?? string.Empty;
        }

        public string StableId { get; }
        public string Name { get; }
        public string Description { get; }
        public string ProjectileDescription { get; }
        public string EnemyDescription { get; }
        public string SymbolKey { get; }
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
    /// 모든 전투 스테이지가 공유하는 Unity TextAsset 기반 문자열 카탈로그다.
    /// 타입 이름은 기존 호출부 호환을 위해 유지한다. 누락되거나 잘못된 항목은
    /// 안정 localization key로 대체해 UI를 진단할 수 있게 한다.
    /// </summary>
    public sealed class StageOneUiTextCatalog :
        IWavePreviewLocalization
    {
        private const string UnknownLocale = "und";

        private readonly Dictionary<string, string> values =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> valueOwners =
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

        /// <summary>
        /// 기본 로컬라이제이션과 카드 모듈 안의 localization object를
        /// GameLogic composer와 같은 order/moduleId 순서로 엄격하게
        /// 병합한다. 모듈 병합에서는 locale 불일치와 중복 키를 허용하지
        /// 않아 Stage01과 TestLab이 서로 다른 문자열을 조용히 선택하지
        /// 못하게 한다.
        /// </summary>
        public static StageOneUiTextCatalog Load(
            TextAsset baseJsonAsset,
            IReadOnlyList<TextAsset> cardModuleAssets)
        {
            if (baseJsonAsset == null)
            {
                throw new ArgumentNullException(
                    nameof(baseJsonAsset));
            }
            if (cardModuleAssets == null)
            {
                throw new ArgumentNullException(
                    nameof(cardModuleAssets));
            }

            var catalog = new StageOneUiTextCatalog();
            CatalogDto baseSource = ParseCatalogStrict(
                baseJsonAsset.text,
                "base localization '" +
                baseJsonAsset.name +
                "'");
            if (string.IsNullOrWhiteSpace(baseSource.locale))
            {
                throw new InvalidOperationException(
                    "Base localization must define a locale.");
            }

            catalog.Locale = baseSource.locale.Trim();
            catalog.AddCatalogStrict(
                baseSource,
                "base localization '" +
                baseJsonAsset.name +
                "'");

            List<ModuleLocalizationSource> modules =
                ParseModuleLocalizations(cardModuleAssets);
            modules.Sort(CompareModuleSources);
            for (int moduleIndex = 0;
                 moduleIndex < modules.Count;
                 moduleIndex++)
            {
                ModuleLocalizationSource module =
                    modules[moduleIndex];
                if (module.Localization == null)
                {
                    continue;
                }

                string moduleLocale =
                    module.Localization.locale;
                if (string.IsNullOrWhiteSpace(moduleLocale))
                {
                    throw new InvalidOperationException(
                        "Card content module '" +
                        module.ModuleId +
                        "' localization must define locale '" +
                        catalog.Locale +
                        "'.");
                }
                if (!string.Equals(
                        catalog.Locale,
                        moduleLocale.Trim(),
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Card content module '" +
                        module.ModuleId +
                        "' localization locale '" +
                        moduleLocale.Trim() +
                        "' does not match base locale '" +
                        catalog.Locale +
                        "'.");
                }

                catalog.AddCatalogStrict(
                    module.Localization,
                    "card content module '" +
                    module.ModuleId +
                    "'");
            }

            catalog.IsLoaded = true;
            return catalog;
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

        /// <summary>
        /// 실제 화면에 표시될 localization 값만 안정 키 순서로 연결한다.
        /// Editor font 검증이 moduleId, operation 이름 같은 비표시 JSON
        /// 메타데이터의 glyph까지 요구하지 않게 하는 읽기 전용 출력이다.
        /// </summary>
        public string GetFontCoverageText()
        {
            var keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                result.Append(values[keys[i]]);
            }

            return result.ToString();
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

        /// <summary>
        /// 컴파일된 카드가 선언한 이름 키를 그대로 사용한다. production
        /// 카드 흐름은 stable-id 명명 규약을 다시 추론하지 않는다.
        /// </summary>
        public string ResolveDisplayName(
            CompiledCardDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return RequireLocalizedValue(
                definition.DisplayNameKey,
                "card '" + definition.StableId + "' name");
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
            int tier = 1,
            string symbolKey = null)
        {
            // 기존 직접 호출자는 명명 규약으로 호환하고, 실제 게임 흐름은
            // CompiledCardDefinition.SymbolKey를 명시적으로 전달한다.
            string resolvedSymbolKey = symbolKey ??
                BuildStableKey(
                    "card_symbol",
                    cardId,
                    null);
            return new StageOneCardDisplay(
                cardId,
                GetCardName(cardId),
                GetCardProjectileDescription(cardId),
                GetCardEnemyDescription(cardId),
                useEnemyInterpretation,
                tier,
                resolvedSymbolKey);
        }

        /// <summary>
        /// Stage01과 TestLab이 함께 사용하는 카드 표시 해석 경계다.
        /// 이름과 심볼은 CompiledCardDefinition의 명시 키를 사용하고,
        /// 두 설명은 카드 stable id의 표준 키를 사용한다.
        /// </summary>
        public StageOneCardDisplay GetCardDisplay(
            CompiledCardDefinition definition,
            SubjectType subjectType)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            string projectileKey = BuildStableKey(
                "card",
                definition.StableId,
                "projectile");
            string enemyKey = BuildStableKey(
                "card",
                definition.StableId,
                "enemy");
            RequireLocalizedValue(
                definition.SymbolKey,
                "card '" + definition.StableId + "' symbol");
            return new StageOneCardDisplay(
                definition.StableId,
                ResolveDisplayName(definition),
                RequireLocalizedValue(
                    projectileKey,
                    "card '" + definition.StableId +
                    "' projectile description"),
                RequireLocalizedValue(
                    enemyKey,
                    "card '" + definition.StableId +
                    "' enemy description"),
                subjectType == SubjectType.Enemy,
                (int)definition.Tier,
                definition.SymbolKey);
        }

        /// <summary>
        /// Editor 설치·prebuild와 Runtime 초기화가 함께 사용하는 완전성
        /// 검사다. 신규 모듈 카드가 키 문자열 자체를 fallback으로 표시한
        /// 채 실행되는 것을 금지한다.
        /// </summary>
        public void ValidateCardDefinitions(
            CompiledContent content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var errors = new List<string>();
            CompiledCardDefinition[] definitions = content.Cards;
            for (int i = 0; i < definitions.Length; i++)
            {
                CompiledCardDefinition definition = definitions[i];
                if (definition == null)
                {
                    errors.Add("Compiled card at index " + i + " is null.");
                    continue;
                }

                ValidateRequiredCardValue(
                    definition.StableId,
                    "displayNameKey",
                    definition.DisplayNameKey,
                    errors);
                ValidateRequiredCardValue(
                    definition.StableId,
                    "symbolKey",
                    definition.SymbolKey,
                    errors);
                ValidateRequiredCardValue(
                    definition.StableId,
                    "projectile description",
                    BuildStableKey(
                        "card",
                        definition.StableId,
                        "projectile"),
                    errors);
                ValidateRequiredCardValue(
                    definition.StableId,
                    "enemy description",
                    BuildStableKey(
                        "card",
                        definition.StableId,
                        "enemy"),
                    errors);
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Card localization validation failed:\n" +
                    string.Join("\n", errors));
            }
        }

        /// <summary>
        /// 웨이브 예고에 노출되는 적·엘리트·추천 설명이 키 자체로 화면에
        /// 새어 나오지 않도록 Runtime과 빌드 전 검증에서 함께 호출한다.
        /// </summary>
        public void ValidateWavePreviewDefinitions(
            CompiledContent content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var errors = new List<string>();
            string[] interfaceKeys =
            {
                "wave_preview.title_format",
                "wave_preview.total_format",
                "wave_preview.composition_format",
                "wave_preview.no_shield",
                "wave_preview.detail_header_format",
                "wave_preview.stats_format",
                "wave_preview.stats_label",
                "wave_preview.resistance_format",
                "wave_preview.control_resistance_format",
                "wave_preview.control_resistance.none",
                "wave_preview.features_label",
                "wave_preview.abilities_label",
                "wave_preview.elite_traits_label",
                "wave_preview.weaknesses_label",
                "wave_preview.recommendations_label",
                "wave_preview.recommended_tags_label",
                "wave_preview.recommendation_advisory",
                "wave_preview.elite_name_format",
                "wave_preview.elite_trait_format",
                "wave_preview.card_owned_format",
                "wave_preview.card_equipped_format",
                "wave_preview.coverage.good",
                "wave_preview.coverage.partial",
                "wave_preview.coverage.weak",
                "wave_preview.loadout_locked",
                "wave_preview.loadout_locked_label",
                "wave_preview.close",
                "enemy_rank.normal",
                "enemy_rank.elite",
                "enemy_rank.boss"
            };
            ValidateWavePreviewKeys(
                "wave preview interface",
                interfaceKeys,
                errors);

            CompiledEnemyDefinition[] enemies = content.Enemies;
            for (int i = 0; i < enemies.Length; i++)
            {
                CompiledEnemyDefinition enemy = enemies[i];
                string context = "enemy '" + enemy.StableId + "'";
                ValidateWavePreviewKeys(
                    context,
                    new[]
                    {
                        enemy.DisplayNameKey,
                        enemy.SpeedRatingKey
                    },
                    errors);
                ValidateWavePreviewKeys(
                    context + " features",
                    enemy.FeatureKeys,
                    errors);
                ValidateWavePreviewKeys(
                    context + " abilities",
                    enemy.SpecialAbilityKeys,
                    errors);
                ValidateWavePreviewKeys(
                    context + " weaknesses",
                    enemy.WeaknessKeys,
                    errors);
                ValidateWavePreviewKeys(
                    context + " recommended tags",
                    enemy.RecommendedTagKeys,
                    errors);
            }

            CompiledEliteTraitDefinition[] traits =
                content.EliteTraits;
            for (int i = 0; i < traits.Length; i++)
            {
                CompiledEliteTraitDefinition trait = traits[i];
                ValidateWavePreviewKeys(
                    "elite trait '" + trait.StableId + "'",
                    new[]
                    {
                        trait.DisplayNameKey,
                        trait.PrefixKey,
                        trait.DescriptionKey,
                        trait.CounterHintKey
                    },
                    errors);
                ValidateWavePreviewKeys(
                    "elite trait '" + trait.StableId +
                    "' recommended tags",
                    trait.RecommendedTagKeys,
                    errors);
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Wave preview localization validation failed:\n" +
                    string.Join("\n", errors));
            }
        }

        private void ValidateWavePreviewKeys(
            string context,
            string[] keys,
            List<string> errors)
        {
            string[] source = keys ?? Array.Empty<string>();
            for (int i = 0; i < source.Length; i++)
            {
                string key = source[i];
                if (string.IsNullOrWhiteSpace(key) ||
                    !values.TryGetValue(
                        key == null ? string.Empty : key.Trim(),
                        out string value) ||
                    string.IsNullOrWhiteSpace(value))
                {
                    errors.Add(
                        context + " is missing localized value for key '" +
                        (key ?? string.Empty) + "'.");
                }
            }
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

        private static CatalogDto ParseCatalogStrict(
            string json,
            string sourceName)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException(
                    sourceName + " is empty.");
            }

            try
            {
                CatalogDto result =
                    JsonUtility.FromJson<CatalogDto>(json);
                if (result == null)
                {
                    throw new InvalidOperationException(
                        sourceName + " produced no localization data.");
                }

                return result;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    sourceName + " could not be parsed.",
                    exception);
            }
        }

        private static List<ModuleLocalizationSource>
            ParseModuleLocalizations(
                IReadOnlyList<TextAsset> moduleAssets)
        {
            var result = new List<ModuleLocalizationSource>(
                moduleAssets.Count);
            var moduleIds = new HashSet<string>(
                StringComparer.Ordinal);
            for (int moduleIndex = 0;
                 moduleIndex < moduleAssets.Count;
                 moduleIndex++)
            {
                TextAsset moduleAsset = moduleAssets[moduleIndex];
                if (moduleAsset == null)
                {
                    throw new InvalidOperationException(
                        "Card content module asset at index " +
                        moduleIndex +
                        " is null.");
                }

                ModuleEnvelopeDto source;
                try
                {
                    source = JsonUtility.FromJson<ModuleEnvelopeDto>(
                        moduleAsset.text);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        "Card content module localization JSON '" +
                        moduleAsset.name +
                        "' could not be parsed.",
                        exception);
                }

                if (source == null)
                {
                    throw new InvalidOperationException(
                        "Card content module localization JSON '" +
                        moduleAsset.name +
                        "' produced no module data.");
                }
                if (source.schemaVersion !=
                    CardContentModuleDto.CurrentSchemaVersion)
                {
                    throw new InvalidOperationException(
                        "Card content module '" +
                        moduleAsset.name +
                        "' must use schema version " +
                        CardContentModuleDto.CurrentSchemaVersion +
                        ".");
                }
                if (string.IsNullOrWhiteSpace(source.moduleId))
                {
                    throw new InvalidOperationException(
                        "Card content module '" +
                        moduleAsset.name +
                        "' has no moduleId.");
                }
                if (!moduleIds.Add(source.moduleId))
                {
                    throw new InvalidOperationException(
                        "Duplicate card content module id '" +
                        source.moduleId +
                        "' in localization merge.");
                }

                result.Add(new ModuleLocalizationSource(
                    source.moduleId,
                    source.order,
                    source.localization));
            }

            return result;
        }

        private static int CompareModuleSources(
            ModuleLocalizationSource left,
            ModuleLocalizationSource right)
        {
            int orderComparison = left.Order.CompareTo(right.Order);
            return orderComparison != 0
                ? orderComparison
                : StringComparer.Ordinal.Compare(
                    left.ModuleId,
                    right.ModuleId);
        }

        private void AddCatalogStrict(
            CatalogDto source,
            string owner)
        {
            AddEntriesStrict(source.strings, owner);
            AddCardsStrict(source.cards, owner);
            AddTowersStrict(source.towers, owner);
        }

        private void AddEntriesStrict(
            TextEntryDto[] entries,
            string owner)
        {
            TextEntryDto[] source =
                entries ?? Array.Empty<TextEntryDto>();
            for (int i = 0; i < source.Length; i++)
            {
                TextEntryDto entry = source[i];
                AddStrict(
                    entry == null ? null : entry.key,
                    entry == null ? null : entry.value,
                    owner + " strings[" + i + "]");
            }
        }

        private void AddCardsStrict(
            CardTextDto[] cards,
            string owner)
        {
            CardTextDto[] source =
                cards ?? Array.Empty<CardTextDto>();
            for (int i = 0; i < source.Length; i++)
            {
                CardTextDto card = source[i];
                if (card == null ||
                    string.IsNullOrWhiteSpace(card.id))
                {
                    throw new InvalidOperationException(
                        owner + " cards[" + i + "] has no id.");
                }

                string entryOwner = owner + " card '" + card.id + "'";
                AddStrict(
                    BuildStableKey("card", card.id, "name"),
                    card.name,
                    entryOwner + " name");
                AddStrict(
                    BuildStableKey("card", card.id, "projectile"),
                    card.projectile,
                    entryOwner + " projectile description");
                AddStrict(
                    BuildStableKey("card", card.id, "enemy"),
                    card.enemy,
                    entryOwner + " enemy description");
            }
        }

        private void AddTowersStrict(
            TowerTextDto[] towers,
            string owner)
        {
            TowerTextDto[] source =
                towers ?? Array.Empty<TowerTextDto>();
            for (int i = 0; i < source.Length; i++)
            {
                TowerTextDto tower = source[i];
                if (tower == null ||
                    string.IsNullOrWhiteSpace(tower.id))
                {
                    throw new InvalidOperationException(
                        owner + " towers[" + i + "] has no id.");
                }

                string entryOwner =
                    owner + " tower '" + tower.id + "'";
                AddStrict(
                    BuildStableKey("tower", tower.id, "name"),
                    tower.name,
                    entryOwner + " name");
                AddStrict(
                    BuildStableKey(
                        "tower",
                        tower.id,
                        "description"),
                    tower.description,
                    entryOwner + " description");
            }
        }

        private void AddStrict(
            string key,
            string value,
            string owner)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    owner + " has an empty localization key.");
            }
            if (value == null)
            {
                throw new InvalidOperationException(
                    owner + " has a null localization value.");
            }

            string normalizedKey = key.Trim();
            if (valueOwners.TryGetValue(
                    normalizedKey,
                    out string existingOwner))
            {
                throw new InvalidOperationException(
                    "Duplicate localization key '" +
                    normalizedKey +
                    "' from " +
                    owner +
                    "; it was already defined by " +
                    existingOwner +
                    ".");
            }

            values.Add(normalizedKey, value);
            valueOwners.Add(normalizedKey, owner);
        }

        private string RequireLocalizedValue(
            string key,
            string context)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    context + " has no localization key.");
            }
            if (!values.TryGetValue(
                    key.Trim(),
                    out string value) ||
                string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    context +
                    " is missing localization value for key '" +
                    key.Trim() +
                    "'.");
            }

            return value;
        }

        private void ValidateRequiredCardValue(
            string cardId,
            string fieldName,
            string key,
            List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                errors.Add(
                    "Card '" + cardId + "' has no " + fieldName + ".");
                return;
            }
            if (!values.TryGetValue(
                    key.Trim(),
                    out string value) ||
                string.IsNullOrWhiteSpace(value))
            {
                errors.Add(
                    "Card '" + cardId + "' " + fieldName +
                    " key '" + key.Trim() +
                    "' has no non-empty localized value.");
            }
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

        private sealed class ModuleLocalizationSource
        {
            public ModuleLocalizationSource(
                string moduleId,
                int order,
                CatalogDto localization)
            {
                ModuleId = moduleId;
                Order = order;
                Localization = localization;
            }

            public string ModuleId { get; }
            public int Order { get; }
            public CatalogDto Localization { get; }
        }

        [Serializable]
        private sealed class ModuleEnvelopeDto
        {
            public int schemaVersion;
            public string moduleId;
            public int order;
            public CatalogDto localization;
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
