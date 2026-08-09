using System;
using System.Collections.Generic;
using System.Text;
using RuleforgeTD.Battle;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.UI
{
    public readonly struct StageOneEnemyStatusDisplay
    {
        public StageOneEnemyStatusDisplay(
            string effectId,
            string name,
            string description,
            int stacks,
            int maximumStacks,
            float remainingSeconds,
            float tickIntervalSeconds,
            int intensity,
            int armorIgnoreBps,
            int sourceCount,
            int sourceTowerId,
            string sourceCardName)
        {
            EffectId = effectId ?? string.Empty;
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            Stacks = Math.Max(0, stacks);
            MaximumStacks = Math.Max(0, maximumStacks);
            RemainingSeconds = Math.Max(0f, remainingSeconds);
            TickIntervalSeconds =
                Math.Max(0f, tickIntervalSeconds);
            Intensity = intensity;
            ArmorIgnoreBps = Math.Max(0, armorIgnoreBps);
            SourceCount = Math.Max(0, sourceCount);
            SourceTowerId = sourceTowerId;
            SourceCardName = sourceCardName ?? string.Empty;
        }

        public string EffectId { get; }
        public string Name { get; }
        public string Description { get; }
        public int Stacks { get; }
        public int MaximumStacks { get; }
        public float RemainingSeconds { get; }
        public float TickIntervalSeconds { get; }
        public int Intensity { get; }
        public int ArmorIgnoreBps { get; }
        public int SourceCount { get; }
        public int SourceTowerId { get; }
        public string SourceCardName { get; }
    }

    /// <summary>
    /// Immutable reader-facing enemy data. It combines the live simulation
    /// snapshot with the data-authored enemy definition so the UI never reads
    /// presentation-only MonoBehaviour state.
    /// </summary>
    public sealed class StageOneEnemyInspectionModel
    {
        public StageOneEnemyInspectionModel(
            in EnemySnapshot snapshot,
            CompiledEnemyDefinition definition,
            string name,
            string typeName,
            string description,
            string rankName,
            string bossAbilityName,
            string eliteTraitSummary,
            EnemyRank effectiveRank,
            float currentSpeedPerSecond,
            StageOneEnemyStatusDisplay[] statuses)
        {
            EntityId = snapshot.Id;
            DefinitionId = snapshot.DefinitionId ?? string.Empty;
            Name = name ?? string.Empty;
            TypeName = typeName ?? string.Empty;
            Description = description ?? string.Empty;
            RankName = rankName ?? string.Empty;
            BossAbilityName = bossAbilityName ?? string.Empty;
            EliteTraitSummary = eliteTraitSummary ?? string.Empty;
            Level = definition.Level;
            Rank = effectiveRank;
            IsAlive = snapshot.Alive;
            CurrentHealthMilli = snapshot.HealthMilli;
            MaximumHealthMilli = snapshot.MaxHealthMilli;
            BaseMaximumHealthMilli = definition.MaxHealthMilli;
            ShieldMilli = snapshot.ShieldMilli;
            Armor = snapshot.Armor;
            BaseArmor = definition.Armor;
            CurrentSpeedPerSecond =
                Math.Max(0f, currentSpeedPerSecond);
            SlowBps = snapshot.SlowBps;
            SizeMultiplierBps = snapshot.SizeMultiplierBps;
            FireResistanceBps = definition.FireResistanceBps;
            PoisonResistanceBps =
                definition.PoisonResistanceBps;
            ControlGauge = snapshot.ControlGauge;
            ControlThreshold = snapshot.ControlThreshold;
            RewardBudget = snapshot.RewardBudget;
            WaveProgressBudget = snapshot.WaveProgressBudget;
            Generation = snapshot.Generation;
            LineageId = snapshot.LineageId;
            DeathBindingCount = snapshot.DeathBindingCount;
            Statuses = statuses ??
                Array.Empty<StageOneEnemyStatusDisplay>();
        }

        private StageOneEnemyInspectionModel(
            StageOneEnemyInspectionModel source,
            bool isAlive,
            long currentHealthMilli)
        {
            EntityId = source.EntityId;
            DefinitionId = source.DefinitionId;
            Name = source.Name;
            TypeName = source.TypeName;
            Description = source.Description;
            RankName = source.RankName;
            BossAbilityName = source.BossAbilityName;
            EliteTraitSummary = source.EliteTraitSummary;
            Level = source.Level;
            Rank = source.Rank;
            IsAlive = isAlive;
            CurrentHealthMilli = Math.Max(
                0L,
                currentHealthMilli);
            MaximumHealthMilli = source.MaximumHealthMilli;
            BaseMaximumHealthMilli =
                source.BaseMaximumHealthMilli;
            ShieldMilli = source.ShieldMilli;
            Armor = source.Armor;
            BaseArmor = source.BaseArmor;
            CurrentSpeedPerSecond =
                source.CurrentSpeedPerSecond;
            SlowBps = source.SlowBps;
            SizeMultiplierBps = source.SizeMultiplierBps;
            FireResistanceBps = source.FireResistanceBps;
            PoisonResistanceBps =
                source.PoisonResistanceBps;
            ControlGauge = source.ControlGauge;
            ControlThreshold = source.ControlThreshold;
            RewardBudget = source.RewardBudget;
            WaveProgressBudget = source.WaveProgressBudget;
            Generation = source.Generation;
            LineageId = source.LineageId;
            DeathBindingCount = source.DeathBindingCount;
            Statuses = source.Statuses;
        }

        public int EntityId { get; }
        public string DefinitionId { get; }
        public string Name { get; }
        public string TypeName { get; }
        public string Description { get; }
        public string RankName { get; }
        public string BossAbilityName { get; }
        public string EliteTraitSummary { get; }
        public int Level { get; }
        public EnemyRank Rank { get; }
        public bool IsAlive { get; }
        public long CurrentHealthMilli { get; }
        public long MaximumHealthMilli { get; }
        public long BaseMaximumHealthMilli { get; }
        public long ShieldMilli { get; }
        public int Armor { get; }
        public int BaseArmor { get; }
        public float CurrentSpeedPerSecond { get; }
        public int SlowBps { get; }
        public int SizeMultiplierBps { get; }
        public int FireResistanceBps { get; }
        public int PoisonResistanceBps { get; }
        public int ControlGauge { get; }
        public int ControlThreshold { get; }
        public int RewardBudget { get; }
        public int WaveProgressBudget { get; }
        public int Generation { get; }
        public int LineageId { get; }
        public int DeathBindingCount { get; }
        public StageOneEnemyStatusDisplay[] Statuses { get; }

        /// <summary>
        /// Freezes the last reader-facing state at the moment of death. The
        /// retained model is independent of the pooled enemy view and makes
        /// the terminal zero-health state explicit.
        /// </summary>
        public StageOneEnemyInspectionModel AsDefeated()
        {
            return !IsAlive && CurrentHealthMilli <= 0L
                ? this
                : new StageOneEnemyInspectionModel(
                    this,
                    false,
                    0L);
        }
    }

    /// <summary>
    /// Single conversion boundary from simulation/content data to an enemy
    /// inspection model. No enemy stable ID is special-cased here.
    /// </summary>
    public static class StageOneEnemyInspectionModelFactory
    {
        private sealed class StatusAggregate
        {
            public string EffectId;
            public string Name;
            public string Description;
            public int Stacks;
            public int MaximumStacks;
            public int RemainingTicks;
            public int TickInterval;
            public int Intensity;
            public int ArmorIgnoreBps;
            public int SourceCount;
            public int SourceTowerId = -1;
            public CardId SourceCardId = CardId.Invalid;
            public bool HasMixedTowerSources;
            public bool HasMixedCardSources;
        }

        public static StageOneEnemyInspectionModel Create(
            in EnemySnapshot snapshot,
            CompiledEnemyDefinition definition,
            CompiledContent content,
            StageOneUiTextCatalog catalog)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            catalog = catalog ??
                StageOneUiTextCatalog.FromJson(null);
            int tickRate = Math.Max(1, content.Run.TickRate);
            string baseName = catalog.Get(definition.DisplayNameKey);
            string eliteSummary = BuildEliteTraitSummary(
                snapshot.EliteTraitIds,
                content,
                catalog,
                ref baseName);
            EnemyRank effectiveRank =
                snapshot.EliteTraitIds != null &&
                snapshot.EliteTraitIds.Length > 0 &&
                definition.Rank == EnemyRank.Normal
                    ? EnemyRank.Elite
                    : definition.Rank;
            float speedPerSecond =
                (snapshot.BaseSpeedMilliPerTick > 0
                    ? snapshot.BaseSpeedMilliPerTick
                    : definition.SpeedMilliPerTick) *
                tickRate /
                1000f *
                Math.Max(0, snapshot.SpeedMultiplierBps) /
                10000f *
                Math.Max(0, 10000 - snapshot.SlowBps) /
                10000f;
            string rankKey =
                "enemy_rank." +
                effectiveRank.ToString().ToLowerInvariant();
            string abilityName =
                definition.BossAbility == BossAbilityType.None
                    ? string.Empty
                    : catalog.Get(
                        "boss_ability." +
                        definition.BossAbility
                            .ToString()
                            .ToLowerInvariant());

            return new StageOneEnemyInspectionModel(
                snapshot,
                definition,
                baseName,
                catalog.Get(definition.TypeKey),
                catalog.Get(definition.DescriptionKey),
                catalog.Get(rankKey),
                abilityName,
                eliteSummary,
                effectiveRank,
                speedPerSecond,
                BuildStatuses(
                    snapshot.StatusDetails,
                    tickRate,
                    content,
                    catalog));
        }

        private static string BuildEliteTraitSummary(
            string[] stableTraitIds,
            CompiledContent content,
            StageOneUiTextCatalog catalog,
            ref string displayName)
        {
            if (stableTraitIds == null ||
                stableTraitIds.Length == 0)
            {
                return string.Empty;
            }

            var summary = new StringBuilder(256);
            var prefixes = new StringBuilder(32);
            for (int i = 0; i < stableTraitIds.Length; i++)
            {
                if (!content.TryGetEliteTraitId(
                        stableTraitIds[i],
                        out EliteTraitId traitId))
                {
                    continue;
                }

                CompiledEliteTraitDefinition trait =
                    content.GetEliteTrait(traitId);
                if (prefixes.Length > 0)
                {
                    prefixes.Append(' ');
                }
                prefixes.Append(catalog.Get(trait.PrefixKey));

                if (summary.Length > 0)
                {
                    summary.Append("\n\n");
                }
                summary.Append(catalog.Format(
                    "enemy_inspector.elite_trait_format",
                    trait.IconText,
                    catalog.Get(trait.DisplayNameKey),
                    catalog.Get(trait.DescriptionKey),
                    catalog.Get(trait.CounterHintKey)));
            }

            if (prefixes.Length > 0)
            {
                displayName = prefixes.ToString() + " " + displayName;
            }
            return summary.ToString();
        }

        private static StageOneEnemyStatusDisplay[] BuildStatuses(
            StatusSnapshot[] statuses,
            int tickRate,
            CompiledContent content,
            StageOneUiTextCatalog catalog)
        {
            if (statuses == null || statuses.Length == 0)
            {
                return Array.Empty<StageOneEnemyStatusDisplay>();
            }

            var aggregates = new List<StatusAggregate>(
                Math.Min(8, statuses.Length));
            for (int i = 0; i < statuses.Length; i++)
            {
                StatusSnapshot status = statuses[i];
                if (status.Stacks <= 0 ||
                    status.RemainingTicks <= 0 ||
                    !StageOneStatusEffectVisualCatalog.TryGet(
                        status.Type,
                        out StageOneStatusEffectVisualDefinition visual) ||
                    !visual.ShowDebuffIcon)
                {
                    continue;
                }

                StatusAggregate aggregate =
                    FindAggregate(
                        aggregates,
                        visual.EffectId);
                if (aggregate == null)
                {
                    aggregate = new StatusAggregate
                    {
                        EffectId = visual.EffectId,
                        Name = catalog.Get(visual.NameKey),
                        Description =
                            catalog.Get(visual.DescriptionKey),
                        SourceTowerId = status.SourceTowerId,
                        SourceCardId = status.SourceCardId
                    };
                    aggregates.Add(aggregate);
                }
                else
                {
                    aggregate.HasMixedTowerSources |=
                        aggregate.SourceTowerId !=
                        status.SourceTowerId;
                    aggregate.HasMixedCardSources |=
                        aggregate.SourceCardId !=
                        status.SourceCardId;
                }

                aggregate.Stacks = SaturatingAdd(
                    aggregate.Stacks,
                    status.Stacks);
                aggregate.MaximumStacks = SaturatingAdd(
                    aggregate.MaximumStacks,
                    Math.Max(
                        status.MaxStacks,
                        status.Stacks));
                aggregate.RemainingTicks = Math.Max(
                    aggregate.RemainingTicks,
                    status.RemainingTicks);
                if (status.TickInterval > 0 &&
                    (aggregate.TickInterval <= 0 ||
                     status.TickInterval <
                     aggregate.TickInterval))
                {
                    aggregate.TickInterval =
                        status.TickInterval;
                }

                aggregate.Intensity = Math.Max(
                    aggregate.Intensity,
                    status.Intensity);
                aggregate.ArmorIgnoreBps = Math.Max(
                    aggregate.ArmorIgnoreBps,
                    status.ArmorIgnoreBps);
                aggregate.SourceCount++;
            }

            if (aggregates.Count == 0)
            {
                return Array.Empty<StageOneEnemyStatusDisplay>();
            }

            var result =
                new StageOneEnemyStatusDisplay[aggregates.Count];
            for (int i = 0; i < aggregates.Count; i++)
            {
                StatusAggregate aggregate = aggregates[i];
                string cardName = string.Empty;
                if (!aggregate.HasMixedCardSources &&
                    aggregate.SourceCardId.IsValid &&
                    aggregate.SourceCardId.Value <
                    content.CardCount)
                {
                    cardName = catalog.GetCardName(
                        content.GetCard(
                            aggregate.SourceCardId).StableId);
                }

                result[i] = new StageOneEnemyStatusDisplay(
                    aggregate.EffectId,
                    aggregate.Name,
                    aggregate.Description,
                    aggregate.Stacks,
                    aggregate.MaximumStacks,
                    aggregate.RemainingTicks /
                    (float)tickRate,
                    aggregate.TickInterval /
                    (float)tickRate,
                    aggregate.Intensity,
                    aggregate.ArmorIgnoreBps,
                    aggregate.SourceCount,
                    aggregate.HasMixedTowerSources
                        ? -1
                        : aggregate.SourceTowerId,
                    cardName);
            }

            return result;
        }

        private static StatusAggregate FindAggregate(
            List<StatusAggregate> aggregates,
            string effectId)
        {
            for (int i = 0; i < aggregates.Count; i++)
            {
                if (string.Equals(
                        aggregates[i].EffectId,
                        effectId,
                        StringComparison.Ordinal))
                {
                    return aggregates[i];
                }
            }

            return null;
        }

        private static int SaturatingAdd(int left, int right)
        {
            long sum = (long)left + right;
            return sum >= int.MaxValue
                ? int.MaxValue
                : sum <= int.MinValue
                    ? int.MinValue
                    : (int)sum;
        }
    }
}
