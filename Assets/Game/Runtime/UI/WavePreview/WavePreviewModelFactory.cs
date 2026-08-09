using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;
using UnityEngine;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// 권위 예고와 플레이어 카드 상태를 표시 전용 모델로 바꾸는 경계다.
    /// 모든 문구와 추천 관계는 데이터/로컬라이제이션에서 읽는다.
    /// </summary>
    public static class WavePreviewModelFactory
    {
        private const float PreviewReferenceWorldScale = 1.65f;

        private sealed class ResolvedForecastGroup
        {
            public string EnemyStableId;
            public string[] EliteTraitStableIds;
            public int Count;
            public ResolvedWaveEnemyStats Stats;
            public EnemyRank Rank;
            public bool IsElite =>
                Rank == EnemyRank.Elite ||
                EliteTraitStableIds.Length > 0;
            public bool IsBoss => Rank == EnemyRank.Boss;
        }

        public static WavePreviewModel Create(
            WaveForecastSnapshot forecast,
            CompiledContent content,
            CardInstanceSnapshot[] cardInstances,
            IWavePreviewLocalization text,
            IEnemyPreviewSpriteProvider spriteProvider,
            bool loadoutLocked)
        {
            if (forecast == null)
            {
                throw new ArgumentNullException(nameof(forecast));
            }
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var ownedCards = new HashSet<int>();
            var equippedCards = new HashSet<int>();
            CardInstanceSnapshot[] cards = cardInstances ??
                Array.Empty<CardInstanceSnapshot>();
            for (int i = 0; i < cards.Length; i++)
            {
                ownedCards.Add(cards[i].DefinitionId.Value);
                if (cards[i].Equipped)
                {
                    equippedCards.Add(cards[i].DefinitionId.Value);
                }
            }

            ResolvedForecastGroup[] forecastGroups =
                GroupForecastSpawns(forecast.Spawns);
            var groups =
                new WavePreviewGroupModel[forecastGroups.Length];
            int equippedCoverageCount = 0;
            int ownedCoverageCount = 0;
            int normalCount = 0;
            int eliteCount = 0;
            int bossCount = 0;
            for (int i = 0; i < forecastGroups.Length; i++)
            {
                ResolvedForecastGroup group = forecastGroups[i];
                if (!content.TryGetEnemyId(
                        group.EnemyStableId,
                        out EnemyDefinitionId enemyId))
                {
                    continue;
                }

                CompiledEnemyDefinition enemy = content.GetEnemy(enemyId);
                string name = text.Get(enemy.DisplayNameKey);
                string traitDetails = BuildTraitDetails(
                    group,
                    content,
                    text,
                    ref name);
                string rankKey = group.IsBoss
                    ? "enemy_rank.boss"
                    : group.IsElite
                        ? "enemy_rank.elite"
                        : "enemy_rank.normal";
                if (group.IsBoss)
                {
                    bossCount += group.Count;
                }
                else if (group.IsElite)
                {
                    eliteCount += group.Count;
                }
                else
                {
                    normalCount += group.Count;
                }

                string recommendations = BuildRecommendations(
                    enemy,
                    group,
                    content,
                    text,
                    ownedCards,
                    equippedCards,
                    out bool hasOwned,
                    out bool hasEquipped);
                if (hasEquipped)
                {
                    equippedCoverageCount++;
                }
                if (hasOwned)
                {
                    ownedCoverageCount++;
                }

                string shield = group.Stats.ShieldMilli > 0
                    ? FormatMilli(group.Stats.ShieldMilli)
                    : text.Get("wave_preview.no_shield");
                var detailSections =
                    new List<WavePreviewDetailSectionModel>();
                string stats = text.Format(
                    "wave_preview.stats_format",
                    FormatMilli(group.Stats.MaxHealthMilli),
                    group.Stats.Armor,
                    text.Get(enemy.SpeedRatingKey),
                    FormatSpeed(
                        group.Stats.SpeedMilliPerTick,
                        content.Run.TickRate),
                    shield);
                stats += "\n" + text.Format(
                    "wave_preview.resistance_format",
                    FormatBasisPoints(enemy.FireResistanceBps),
                    FormatBasisPoints(enemy.PoisonResistanceBps),
                    enemy.ControlGaugeStep > 0
                        ? text.Format(
                            "wave_preview.control_resistance_format",
                            enemy.ControlGaugeThreshold,
                            enemy.ControlGaugeStep)
                        : text.Get(
                            "wave_preview.control_resistance.none"));
                detailSections.Add(new WavePreviewDetailSectionModel(
                    text.Get("wave_preview.stats_label"),
                    stats));

                string features = BuildListBody(
                    text,
                    enemy.FeatureKeys);
                if (!string.IsNullOrEmpty(features))
                {
                    detailSections.Add(new WavePreviewDetailSectionModel(
                        text.Get("wave_preview.features_label"),
                        features));
                }
                if (!string.IsNullOrEmpty(traitDetails))
                {
                    detailSections.Add(new WavePreviewDetailSectionModel(
                        text.Get("wave_preview.elite_traits_label"),
                        traitDetails));
                }
                string abilities = BuildAbilityBody(
                    text,
                    enemy,
                    content);
                if (!string.IsNullOrEmpty(abilities))
                {
                    detailSections.Add(new WavePreviewDetailSectionModel(
                        text.Get("wave_preview.abilities_label"),
                        abilities));
                }

                string weaknesses = BuildListBody(
                    text,
                    enemy.WeaknessKeys);
                if (!string.IsNullOrEmpty(weaknesses))
                {
                    detailSections.Add(new WavePreviewDetailSectionModel(
                        text.Get("wave_preview.weaknesses_label"),
                        weaknesses));
                }
                detailSections.Add(new WavePreviewDetailSectionModel(
                    text.Get("wave_preview.recommendations_label"),
                    recommendations + "\n" + text.Get(
                        "wave_preview.recommendation_advisory")));

                string detail = ComposeDetailText(
                    name,
                    group.Count,
                    text.Get(rankKey),
                    detailSections,
                    text);

                Sprite sprite = null;
                RuntimeAnimatorController previewAnimator = null;
                float previewVisualScale = Mathf.Clamp(
                    group.Stats.RenderScaleBps / 10000f,
                    0.8f,
                    1.45f);
                if (spriteProvider != null)
                {
                    spriteProvider.TryGetEnemyPreviewSprite(
                        group.EnemyStableId,
                        out sprite);
                    spriteProvider.TryGetEnemyPreviewAnimatorController(
                        group.EnemyStableId,
                        out previewAnimator);
                    if (spriteProvider.TryGetEnemyPreviewScaleMultiplier(
                            group.EnemyStableId,
                            out float authoredScale))
                    {
                        previewVisualScale = Mathf.Clamp(
                            previewVisualScale *
                            authoredScale /
                            PreviewReferenceWorldScale,
                            0.8f,
                            1.45f);
                    }
                }
                ResolveElitePreviewColors(
                    group,
                    content,
                    out Color previewTint,
                    out Color previewOutlineColor);
                groups[i] = new WavePreviewGroupModel(
                    name,
                    text.Get(rankKey),
                    group.Count,
                    sprite,
                    group.IsElite,
                    group.IsBoss,
                    detail,
                    hasOwned,
                    hasEquipped,
                    previewAnimator,
                    detailSections.ToArray(),
                    previewTint,
                    previewOutlineColor,
                    previewVisualScale);
            }

            string coverageKey = equippedCoverageCount == groups.Length &&
                                 groups.Length > 0
                ? "wave_preview.coverage.good"
                : equippedCoverageCount > 0 || ownedCoverageCount > 0
                    ? "wave_preview.coverage.partial"
                    : "wave_preview.coverage.weak";
            string composition = text.Format(
                "wave_preview.composition_format",
                normalCount,
                eliteCount,
                bossCount);
            return new WavePreviewModel(
                forecast.WaveIndex + 1,
                text.Format(
                    "wave_preview.title_format",
                    forecast.WaveIndex + 1),
                text.Format(
                    "wave_preview.total_format",
                    forecast.TotalCount),
                composition,
                text.Get(coverageKey),
                loadoutLocked,
                groups);
        }

        private static void ResolveElitePreviewColors(
            ResolvedForecastGroup group,
            CompiledContent content,
            out Color bodyTint,
            out Color outlineColor)
        {
            bodyTint = Color.white;
            outlineColor = Color.clear;
            string[] traitIds = group.EliteTraitStableIds ??
                Array.Empty<string>();
            for (int i = 0; i < traitIds.Length; i++)
            {
                if (!content.TryGetEliteTraitId(
                        traitIds[i],
                        out EliteTraitId traitId))
                {
                    continue;
                }

                CompiledEliteTraitDefinition trait =
                    content.GetEliteTrait(traitId);
                bodyTint = ParsePreviewColor(
                    trait.BodyTint,
                    Color.white);
                outlineColor = ParsePreviewColor(
                    trait.OutlineColor,
                    Color.clear);
                return;
            }
        }

        private static Color ParsePreviewColor(
            string value,
            Color fallback)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   ColorUtility.TryParseHtmlString(
                       value,
                       out Color parsed)
                ? parsed
                : fallback;
        }

        private static string BuildTraitDetails(
            ResolvedForecastGroup group,
            CompiledContent content,
            IWavePreviewLocalization text,
            ref string name)
        {
            var result = new StringBuilder();
            string[] traitIds = group.EliteTraitStableIds;
            for (int i = 0; i < traitIds.Length; i++)
            {
                if (!content.TryGetEliteTraitId(
                        traitIds[i],
                        out EliteTraitId traitId))
                {
                    continue;
                }

                CompiledEliteTraitDefinition trait =
                    content.GetEliteTrait(traitId);
                name = text.Format(
                    "wave_preview.elite_name_format",
                    text.Get(trait.PrefixKey),
                    name);
                if (result.Length > 0)
                {
                    result.Append("\n");
                }
                result.Append(text.Format(
                    "wave_preview.elite_trait_format",
                    trait.IconText,
                    text.Get(trait.DisplayNameKey),
                    text.Get(trait.DescriptionKey),
                    text.Get(trait.CounterHintKey)));
            }

            return result.ToString();
        }

        private static string BuildRecommendations(
            CompiledEnemyDefinition enemy,
            ResolvedForecastGroup group,
            CompiledContent content,
            IWavePreviewLocalization text,
            HashSet<int> ownedCards,
            HashSet<int> equippedCards,
            out bool hasOwned,
            out bool hasEquipped)
        {
            hasOwned = false;
            hasEquipped = false;
            var result = new StringBuilder();
            var recommendations = new List<CardId>();
            var recommendationIds = new HashSet<int>();
            AddRecommendations(
                enemy.RecommendedCardIds,
                recommendations,
                recommendationIds);
            var tags = new List<string>();
            var tagKeys = new HashSet<string>(StringComparer.Ordinal);
            AddTags(enemy.RecommendedTagKeys, tags, tagKeys);
            string[] traitStableIds = group.EliteTraitStableIds;
            for (int traitIndex = 0;
                 traitIndex < traitStableIds.Length;
                 traitIndex++)
            {
                if (!content.TryGetEliteTraitId(
                        traitStableIds[traitIndex],
                        out EliteTraitId traitId))
                {
                    continue;
                }

                CompiledEliteTraitDefinition trait =
                    content.GetEliteTrait(traitId);
                AddRecommendations(
                    trait.RecommendedCardIds,
                    recommendations,
                    recommendationIds);
                AddTags(
                    trait.RecommendedTagKeys,
                    tags,
                    tagKeys);
            }

            for (int i = 0; i < recommendations.Count; i++)
            {
                CardId cardId = recommendations[i];
                bool equipped = equippedCards.Contains(cardId.Value);
                bool owned = ownedCards.Contains(cardId.Value);
                hasEquipped |= equipped;
                hasOwned |= owned;
                if (i > 0)
                {
                    result.Append("  ·  ");
                }

                string cardName = text.ResolveDisplayName(
                    content.GetCard(cardId));
                if (equipped)
                {
                    result.Append(text.Format(
                        "wave_preview.card_equipped_format",
                        cardName));
                }
                else if (owned)
                {
                    result.Append(text.Format(
                        "wave_preview.card_owned_format",
                        cardName));
                }
                else
                {
                    result.Append(cardName);
                }
            }

            if (tags.Count > 0)
            {
                result.Append("\n");
                result.Append(text.Get(
                    "wave_preview.recommended_tags_label"));
                result.Append(" ");
                for (int i = 0; i < tags.Count; i++)
                {
                    if (i > 0)
                    {
                        result.Append(", ");
                    }
                    result.Append(text.Get(tags[i]));
                }
            }

            return result.ToString();
        }

        private static ResolvedForecastGroup[] GroupForecastSpawns(
            WaveForecastSpawn[] source)
        {
            WaveForecastSpawn[] spawns = source ??
                Array.Empty<WaveForecastSpawn>();
            var groups = new List<ResolvedForecastGroup>();
            for (int i = 0; i < spawns.Length; i++)
            {
                WaveForecastSpawn spawn = spawns[i];
                ResolvedForecastGroup group = null;
                for (int groupIndex = 0;
                     groupIndex < groups.Count;
                     groupIndex++)
                {
                    ResolvedForecastGroup candidate =
                        groups[groupIndex];
                    if (string.Equals(
                            candidate.EnemyStableId,
                            spawn.EnemyId,
                            StringComparison.Ordinal) &&
                        HaveSameTraits(
                            candidate.EliteTraitStableIds,
                            spawn.EliteTraitIds))
                    {
                        group = candidate;
                        break;
                    }
                }

                if (group == null)
                {
                    group = new ResolvedForecastGroup
                    {
                        EnemyStableId = spawn.EnemyId,
                        EliteTraitStableIds = spawn.EliteTraitIds == null
                            ? Array.Empty<string>()
                            : (string[])spawn.EliteTraitIds.Clone(),
                        Stats = spawn.Stats,
                        Rank = spawn.Rank
                    };
                    groups.Add(group);
                }

                group.Count = checked(group.Count + spawn.Count);
            }

            return groups.ToArray();
        }

        private static bool HaveSameTraits(
            string[] left,
            string[] right)
        {
            int leftLength = left == null ? 0 : left.Length;
            int rightLength = right == null ? 0 : right.Length;
            if (leftLength != rightLength)
            {
                return false;
            }

            for (int i = 0; i < leftLength; i++)
            {
                if (!string.Equals(
                        left[i],
                        right[i],
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddRecommendations(
            CardId[] source,
            List<CardId> destination,
            HashSet<int> unique)
        {
            CardId[] cards = source ?? Array.Empty<CardId>();
            for (int i = 0; i < cards.Length; i++)
            {
                if (cards[i].IsValid && unique.Add(cards[i].Value))
                {
                    destination.Add(cards[i]);
                }
            }
        }

        private static void AddTags(
            string[] source,
            List<string> destination,
            HashSet<string> unique)
        {
            string[] tags = source ?? Array.Empty<string>();
            for (int i = 0; i < tags.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(tags[i]) &&
                    unique.Add(tags[i]))
                {
                    destination.Add(tags[i]);
                }
            }
        }

        private static string BuildListBody(
            IWavePreviewLocalization text,
            string[] valueKeys)
        {
            string[] keys = valueKeys ?? Array.Empty<string>();
            if (keys.Length == 0)
            {
                return string.Empty;
            }

            var result = new StringBuilder();
            for (int i = 0; i < keys.Length; i++)
            {
                if (i > 0)
                {
                    result.Append("\n");
                }
                result.Append("- ");
                result.Append(text.Get(keys[i]));
            }
            return result.ToString();
        }

        private static string BuildAbilityBody(
            IWavePreviewLocalization text,
            CompiledEnemyDefinition enemy,
            CompiledContent content)
        {
            string[] keys = enemy.SpecialAbilityKeys ??
                Array.Empty<string>();
            if (keys.Length == 0)
            {
                return string.Empty;
            }

            var result = new StringBuilder();
            for (int i = 0; i < keys.Length; i++)
            {
                if (i > 0)
                {
                    result.Append("\n");
                }
                result.Append("- ");
                switch (enemy.BossAbility)
                {
                    case BossAbilityType.Shield:
                        result.Append(text.Format(
                            keys[i],
                            FormatTicks(
                                enemy.BossAbilityIntervalTicks,
                                content.Run.TickRate),
                            FormatBasisPoints(enemy.BossShieldBps),
                            FormatBasisPoints(
                                enemy.BossPhaseHealthBps),
                            FormatTicks(
                                enemy.BossEnragedAbilityIntervalTicks,
                                content.Run.TickRate)));
                        break;
                    case BossAbilityType.Summon:
                        string summonName =
                            enemy.BossSummonEnemyId.IsValid
                                ? text.Get(
                                    content.GetEnemy(
                                        enemy.BossSummonEnemyId)
                                        .DisplayNameKey)
                                : string.Empty;
                        result.Append(text.Format(
                            keys[i],
                            FormatTicks(
                                enemy.BossAbilityIntervalTicks,
                                content.Run.TickRate),
                            summonName,
                            enemy.BossSummonCount,
                            FormatBasisPoints(
                                enemy.BossSummonHealthBps),
                            FormatBasisPoints(
                                enemy.BossPhaseHealthBps),
                            FormatTicks(
                                enemy.BossEnragedAbilityIntervalTicks,
                                content.Run.TickRate),
                            enemy.BossEnragedSummonCount,
                            enemy.BossMaxActiveSummons));
                        break;
                    case BossAbilityType.Teleport:
                        result.Append(text.Format(
                            keys[i],
                            FormatTicks(
                                enemy.BossCastTicks,
                                content.Run.TickRate),
                            FormatTicks(
                                enemy.BossAbilityIntervalTicks,
                                content.Run.TickRate),
                            FormatBasisPoints(
                                enemy.BossTeleportDistanceBps),
                            FormatBasisPoints(
                                enemy.BossPhaseHealthBps),
                            FormatTicks(
                                enemy.BossEnragedAbilityIntervalTicks,
                                content.Run.TickRate),
                            FormatBasisPoints(
                                enemy.BossEnragedTeleportDistanceBps)));
                        break;
                    default:
                        result.Append(text.Get(keys[i]));
                        break;
                }
            }

            return result.ToString();
        }

        private static string ComposeDetailText(
            string name,
            int count,
            string rank,
            List<WavePreviewDetailSectionModel> sections,
            IWavePreviewLocalization text)
        {
            var result = new StringBuilder();
            result.Append(text.Format(
                "wave_preview.detail_header_format",
                name,
                count,
                rank));
            for (int i = 0; i < sections.Count; i++)
            {
                result.Append("\n\n");
                result.Append(sections[i].Title);
                if (!string.IsNullOrEmpty(sections[i].Body))
                {
                    result.Append("\n");
                    result.Append(sections[i].Body);
                }
            }

            return result.ToString();
        }

        private static string FormatMilli(long milli)
        {
            return (milli / 1000d).ToString(
                "0.##",
                CultureInfo.InvariantCulture);
        }

        private static string FormatSpeed(int speed, int tickRate)
        {
            return (speed * tickRate / 1000d).ToString(
                "0.##",
                CultureInfo.InvariantCulture);
        }

        private static string FormatTicks(int ticks, int tickRate)
        {
            return (ticks / (double)Math.Max(1, tickRate)).ToString(
                "0.##",
                CultureInfo.InvariantCulture);
        }

        private static string FormatBasisPoints(int basisPoints)
        {
            return (basisPoints / 100d).ToString(
                "0.#",
                CultureInfo.InvariantCulture) + "%";
        }
    }
}
