using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>웨이브 종료 화면과 전투 HUD가 사용하는 카드별 누적값이다.</summary>
    public readonly struct CardCombatStatSnapshot
    {
        public CardCombatStatSnapshot(
            string cardId,
            long damageMilli,
            int activationCount)
        {
            CardId = cardId ?? string.Empty;
            DamageMilli = damageMilli;
            ActivationCount = activationCount;
        }

        public string CardId { get; }
        public long DamageMilli { get; }
        public int ActivationCount { get; }
    }

    /// <summary>짧은 전투 사건을 개별 팝업 대신 합산해 보여 주는 읽기 모델이다.</summary>
    public sealed class CombatTelemetrySnapshot
    {
        internal CombatTelemetrySnapshot(
            int currentKillStreak,
            int highestKillStreak,
            int largestChainKillCount,
            int explosionTriggerCount,
            int shockChainHitCount,
            int ricochetCount,
            int statusSpreadCount,
            int recentGold,
            int waveGold,
            int cardBountyGold,
            int cardBountyRemaining,
            int eventsProcessedThisTick,
            int queuedEventCount,
            CardCombatStatSnapshot[] cardStats)
        {
            CurrentKillStreak = currentKillStreak;
            HighestKillStreak = highestKillStreak;
            LargestChainKillCount = largestChainKillCount;
            ExplosionTriggerCount = explosionTriggerCount;
            ShockChainHitCount = shockChainHitCount;
            RicochetCount = ricochetCount;
            StatusSpreadCount = statusSpreadCount;
            RecentGold = recentGold;
            WaveGold = waveGold;
            CardBountyGold = cardBountyGold;
            CardBountyRemaining = cardBountyRemaining;
            EventsProcessedThisTick = eventsProcessedThisTick;
            QueuedEventCount = queuedEventCount;
            CardStats = cardStats ?? Array.Empty<CardCombatStatSnapshot>();
        }

        public int CurrentKillStreak { get; }
        public int HighestKillStreak { get; }
        public int LargestChainKillCount { get; }
        public int ExplosionTriggerCount { get; }
        public int ShockChainHitCount { get; }
        public int RicochetCount { get; }
        public int StatusSpreadCount { get; }
        public int RecentGold { get; }
        public int WaveGold { get; }
        public int CardBountyGold { get; }
        public int CardBountyRemaining { get; }
        public int EventsProcessedThisTick { get; }
        public int QueuedEventCount { get; }
        public CardCombatStatSnapshot[] CardStats { get; }
    }

    public readonly struct WaveForecastSpawn
    {
        public WaveForecastSpawn(
            string enemyId,
            EnemyRank rank,
            int count,
            int firstSpawnTick,
            int intervalTicks,
            string[] eliteTraitIds,
            ResolvedWaveEnemyStats stats)
        {
            EnemyId = enemyId ?? string.Empty;
            Rank = rank;
            Count = count;
            FirstSpawnTick = firstSpawnTick;
            IntervalTicks = intervalTicks;
            EliteTraitIds = eliteTraitIds == null
                ? Array.Empty<string>()
                : (string[])eliteTraitIds.Clone();
            Stats = stats;
        }

        public string EnemyId { get; }
        public EnemyRank Rank { get; }
        public int Count { get; }
        public int FirstSpawnTick { get; }
        public int IntervalTicks { get; }
        public string[] EliteTraitIds { get; }
        public ResolvedWaveEnemyStats Stats { get; }
        public bool IsElite =>
            Rank == EnemyRank.Elite || EliteTraitIds.Length > 0;
        public bool IsBoss => Rank == EnemyRank.Boss;
    }

    /// <summary>다음 웨이브 예고가 실제 스폰 데이터와 같은 숫자를 사용하도록 만든다.</summary>
    public sealed class WaveForecastSnapshot
    {
        internal WaveForecastSnapshot(
            int stageNumber,
            int waveIndex,
            string waveId,
            WaveArchetype archetype,
            int totalCount,
            int normalCount,
            int eliteCount,
            int bossCount,
            int bossActiveSummonLimit,
            WaveForecastSpawn[] spawns)
        {
            StageNumber = Math.Max(1, stageNumber);
            WaveIndex = waveIndex;
            WaveId = waveId ?? string.Empty;
            Archetype = archetype;
            TotalCount = totalCount;
            NormalCount = normalCount;
            EliteCount = eliteCount;
            BossCount = bossCount;
            BossActiveSummonLimit = bossActiveSummonLimit;
            Spawns = spawns ?? Array.Empty<WaveForecastSpawn>();
        }

        public int StageNumber { get; }
        public int WaveIndex { get; }
        public string WaveId { get; }
        public WaveArchetype Archetype { get; }
        public int TotalCount { get; }
        public int NormalCount { get; }
        public int EliteCount { get; }
        public int BossCount { get; }
        public int BossActiveSummonLimit { get; }
        public WaveForecastSpawn[] Spawns { get; }
        public bool IsAvailable => WaveIndex >= 0 && Spawns.Length > 0;
    }

    public sealed partial class GameSimulation
    {
        private long[] waveCardDamageMilli = Array.Empty<long>();
        private int[] waveCardActivationCounts = Array.Empty<int>();
        private readonly Dictionary<int, int> waveKillsByChain =
            new Dictionary<int, int>();
        private readonly HashSet<int> waveEconomyBonusLineages =
            new HashSet<int>();
        private long lastTelemetryKillTick = long.MinValue;
        private long recentGoldWindowStartTick;
        private int currentKillStreak;
        private int economyKillStreak;
        private int highestKillStreak;
        private int largestChainKillCount;
        private int explosionTriggerCount;
        private int shockChainHitCount;
        private int ricochetCount;
        private int statusSpreadCount;
        private int recentGold;
        private int waveGold;
        private int cardBountyGold;

        public CombatTelemetrySnapshot GetCombatTelemetrySnapshot()
        {
            EnsureInitialized();
            var stats = new List<CardCombatStatSnapshot>();
            for (int cardIndex = 0;
                 cardIndex < content.CardCount;
                 cardIndex++)
            {
                long damage = waveCardDamageMilli[cardIndex];
                int activations = waveCardActivationCounts[cardIndex];
                if (damage <= 0L && activations <= 0)
                {
                    continue;
                }

                stats.Add(new CardCombatStatSnapshot(
                    content.GetCard(new CardId(cardIndex)).StableId,
                    damage,
                    activations));
            }

            stats.Sort((left, right) =>
            {
                int damageComparison =
                    right.DamageMilli.CompareTo(left.DamageMilli);
                return damageComparison != 0
                    ? damageComparison
                    : string.CompareOrdinal(left.CardId, right.CardId);
            });

            return new CombatTelemetrySnapshot(
                ResolveCurrentKillStreak(),
                highestKillStreak,
                largestChainKillCount,
                explosionTriggerCount,
                shockChainHitCount,
                ricochetCount,
                statusSpreadCount,
                ResolveRecentGold(),
                waveGold,
                cardBountyGold,
                CalculateCardBountyRemaining(),
                eventsProcessedThisTick,
                eventQueue.Count,
                stats.ToArray());
        }

        public WaveForecastSnapshot GetWaveForecast(int waveIndex)
        {
            EnsureInitialized();
            if (waveIndex < 0 || waveIndex >= content.WaveCount)
            {
                throw new ArgumentOutOfRangeException(nameof(waveIndex));
            }

            CompiledWaveDefinition wave = content.GetWave(waveIndex);
            CompiledWaveSpawn[] definitions = wave.Spawns;
            var spawns = new WaveForecastSpawn[definitions.Length];
            int summonLimit = 0;
            long totalCount = 0L;
            long normalCount = 0L;
            long eliteCount = 0L;
            long bossCount = 0L;
            for (int i = 0; i < definitions.Length; i++)
            {
                CompiledWaveSpawn spawn = definitions[i];
                CompiledEnemyDefinition enemy =
                    content.GetEnemy(spawn.EnemyId);
                EliteTraitId[] traitIds =
                    spawn.EliteTraitIdsInternal;
                var traitStableIds = new string[traitIds.Length];
                for (int traitIndex = 0;
                     traitIndex < traitIds.Length;
                     traitIndex++)
                {
                    traitStableIds[traitIndex] =
                        content.GetEliteTrait(
                            traitIds[traitIndex]).StableId;
                }
                int resolvedCount =
                    WaveEnemyStatResolver.ResolveSpawnCount(
                        spawn.Count,
                        currentStageNumber);
                spawns[i] = new WaveForecastSpawn(
                    enemy.StableId,
                    enemy.Rank,
                    resolvedCount,
                    spawn.FirstSpawnTick,
                    spawn.IntervalTicks,
                    traitStableIds,
                    WaveEnemyStatResolver.Resolve(
                        content,
                        spawn,
                        currentStageNumber));
                totalCount = AddCountSaturated(
                    totalCount,
                    resolvedCount);
                if (enemy.Rank == EnemyRank.Boss)
                {
                    bossCount = AddCountSaturated(
                        bossCount,
                        resolvedCount);
                }
                else if (enemy.Rank == EnemyRank.Elite ||
                         traitIds.Length > 0)
                {
                    eliteCount = AddCountSaturated(
                        eliteCount,
                        resolvedCount);
                }
                else
                {
                    normalCount = AddCountSaturated(
                        normalCount,
                        resolvedCount);
                }
                if (enemy.Rank == EnemyRank.Boss)
                {
                    summonLimit = Math.Max(
                        summonLimit,
                        enemy.BossMaxActiveSummons);
                }
            }

            return new WaveForecastSnapshot(
                currentStageNumber,
                waveIndex,
                wave.StableId,
                wave.Archetype,
                (int)totalCount,
                (int)normalCount,
                (int)eliteCount,
                (int)bossCount,
                summonLimit,
                spawns);
        }

        private static long AddCountSaturated(
            long total,
            int amount)
        {
            return Math.Min(
                int.MaxValue,
                Math.Max(0L, total) + Math.Max(0, amount));
        }

        /// <summary>계획 중에는 곧 시작할 웨이브, 전투 중에는 그 다음 웨이브다.</summary>
        public WaveForecastSnapshot GetUpcomingWaveForecast()
        {
            EnsureInitialized();
            int waveIndex = currentWaveIndex + 1;
            return waveIndex >= 0 && waveIndex < content.WaveCount
                ? GetWaveForecast(waveIndex)
                : null;
        }

        private void InitializeCombatTelemetry()
        {
            waveCardDamageMilli = new long[content.CardCount];
            waveCardActivationCounts = new int[content.CardCount];
            ResetWaveCombatTelemetry();
        }

        private void ResetWaveCombatTelemetry()
        {
            Array.Clear(
                waveCardDamageMilli,
                0,
                waveCardDamageMilli.Length);
            Array.Clear(
                waveCardActivationCounts,
                0,
                waveCardActivationCounts.Length);
            waveKillsByChain.Clear();
            waveEconomyBonusLineages.Clear();
            lastTelemetryKillTick = long.MinValue;
            recentGoldWindowStartTick = tick;
            currentKillStreak = 0;
            economyKillStreak = 0;
            highestKillStreak = 0;
            largestChainKillCount = 0;
            explosionTriggerCount = 0;
            shockChainHitCount = 0;
            ricochetCount = 0;
            statusSpreadCount = 0;
            recentGold = 0;
            waveGold = 0;
            cardBountyGold = 0;
        }

        private void RecordCardActivation(CardId cardId)
        {
            if (cardId.IsValid &&
                cardId.Value < waveCardActivationCounts.Length)
            {
                waveCardActivationCounts[cardId.Value]++;
            }
        }

        private void RecordCardDamage(CardId cardId, long amountMilli)
        {
            if (!cardId.IsValid ||
                cardId.Value >= waveCardDamageMilli.Length ||
                amountMilli <= 0L)
            {
                return;
            }

            waveCardDamageMilli[cardId.Value] = checked(
                waveCardDamageMilli[cardId.Value] + amountMilli);
        }

        private void RecordEnemyKillTelemetry(
            EnemyState enemy,
            in GameEvent gameEvent)
        {
            bool continuesStreak =
                lastTelemetryKillTick != long.MinValue &&
                tick - lastTelemetryKillTick <=
                run.KillStreakWindowTicks;
            if (continuesStreak)
            {
                currentKillStreak++;
            }
            else
            {
                currentKillStreak = 1;
            }
            lastTelemetryKillTick = tick;
            highestKillStreak = Math.Max(
                highestKillStreak,
                currentKillStreak);

            int chainKey = gameEvent.RootChainId.Value;
            waveKillsByChain.TryGetValue(
                chainKey,
                out int chainKills);
            chainKills++;
            waveKillsByChain[chainKey] = chainKills;
            largestChainKillCount = Math.Max(
                largestChainKillCount,
                chainKills);

            // 분열·복제는 여러 개체가 죽어도 하나의 원본 보상 예산만 가진다.
            // 경제 보너스 역시 lineage당 한 번만 계산해 개체 수 조작으로 늘지 않게 한다.
            if (!waveEconomyBonusLineages.Add(
                    enemy.LineageId.Value))
            {
                return;
            }

            if (!continuesStreak)
            {
                economyKillStreak = 0;
            }
            economyKillStreak++;
            if (run.KillStreakBonusGold > 0 &&
                economyKillStreak %
                run.KillStreakBonusInterval == 0)
            {
                GrantTelemetryReward(
                    run.KillStreakBonusGold,
                    RewardOrigin.KillStreak,
                    enemy.Id.Value,
                    gameEvent.SourceTowerId.Value);
            }

            if ((content.GetEnemy(enemy.DefinitionId).Rank ==
                    EnemyRank.Elite ||
                 enemy.EliteTraitIds.Length > 0) &&
                run.EliteKillBonusGold > 0)
            {
                GrantTelemetryReward(
                    run.EliteKillBonusGold,
                    RewardOrigin.EliteBonus,
                    enemy.Id.Value,
                    gameEvent.SourceTowerId.Value);
            }
        }

        private void GrantWaveCompletionReward()
        {
            int amount = checked(
                run.WaveCompletionBaseGold +
                run.WaveCompletionGoldPerWave *
                Math.Max(0, currentWaveIndex));
            GrantTelemetryReward(
                amount,
                RewardOrigin.WaveCompletion,
                currentWaveIndex,
                -1);
        }

        private void GrantTelemetryReward(
            int amount,
            RewardOrigin origin,
            int subjectId,
            int sourceId)
        {
            if (amount <= 0)
            {
                return;
            }

            gold = checked(gold + amount);
            RecordGoldTelemetry(amount, origin);
            AddPresentation(
                PresentationEventType.RewardGranted,
                subjectId,
                sourceId,
                amount,
                origin.ToString());
        }

        private void RecordGoldTelemetry(
            int amount,
            RewardOrigin origin)
        {
            if (amount <= 0)
            {
                return;
            }

            if (tick - recentGoldWindowStartTick >
                content.Safety.PopupAggregateTicks)
            {
                recentGoldWindowStartTick = tick;
                recentGold = 0;
            }
            recentGold = checked(recentGold + amount);
            waveGold = checked(waveGold + amount);
            if (origin == RewardOrigin.CardBounty)
            {
                cardBountyGold = checked(cardBountyGold + amount);
            }
        }

        private int ResolveRecentGold()
        {
            return tick - recentGoldWindowStartTick <=
                   content.Safety.PopupAggregateTicks
                ? recentGold
                : 0;
        }

        private int ResolveCurrentKillStreak()
        {
            return lastTelemetryKillTick != long.MinValue &&
                   tick - lastTelemetryKillTick <=
                       run.KillStreakWindowTicks
                ? currentKillStreak
                : 0;
        }

        private int CalculateCardBountyRemaining()
        {
            int total = 0;
            for (int towerIndex = 0;
                 towerIndex < towers.Count;
                 towerIndex++)
            {
                TowerState tower = towers[towerIndex];
                int towerLimit = 0;
                for (int cardIndex = 0;
                     cardIndex < tower.Program.Length;
                     cardIndex++)
                {
                    CompiledCardDefinition card =
                        content.GetCard(tower.Program[cardIndex]);
                    CompiledEffectNode[] nodes =
                        card.ProjectileEffectsInternal;
                    for (int nodeIndex = 0;
                         nodeIndex < nodes.Length;
                         nodeIndex++)
                    {
                        if (nodes[nodeIndex].Operation ==
                            EffectOperation.BindGoldOnHit)
                        {
                            towerLimit = Math.Max(
                                towerLimit,
                                nodes[nodeIndex].Amount2);
                        }
                    }
                }

                total = checked(
                    total + Math.Max(
                        0,
                        towerLimit -
                        tower.GoldGeneratedThisWave));
            }
            return total;
        }

        private void RecordExplosionTrigger()
        {
            explosionTriggerCount++;
        }

        private void RecordShockChainHits(int count)
        {
            shockChainHitCount = checked(
                shockChainHitCount + Math.Max(0, count));
        }

        private void RecordRicochet()
        {
            ricochetCount++;
        }

        private void RecordStatusSpread()
        {
            statusSpreadCount++;
        }

        private bool CanCreateProjectileEntity(int generation)
        {
            return generation <=
                       content.Safety.MaxProjectileCloneGeneration &&
                   projectiles.Count <
                       content.Safety.MaxActiveProjectiles;
        }

        private bool CanCreateEnemyEntity(int generation)
        {
            return generation <=
                       content.Safety.MaxEnemySplitGeneration &&
                   enemies.Count <
                       content.Safety.MaxActiveEnemies;
        }

        private void AppendCombatTelemetryHash(
            ref StableHashBuilder hash)
        {
            hash.Add(lastTelemetryKillTick);
            hash.Add(recentGoldWindowStartTick);
            hash.Add(currentKillStreak);
            hash.Add(economyKillStreak);
            hash.Add(highestKillStreak);
            hash.Add(largestChainKillCount);
            hash.Add(explosionTriggerCount);
            hash.Add(shockChainHitCount);
            hash.Add(ricochetCount);
            hash.Add(statusSpreadCount);
            hash.Add(recentGold);
            hash.Add(waveGold);
            hash.Add(cardBountyGold);
            hash.Add(waveCardDamageMilli.Length);
            for (int i = 0; i < waveCardDamageMilli.Length; i++)
            {
                hash.Add(waveCardDamageMilli[i]);
                hash.Add(waveCardActivationCounts[i]);
            }

            var chainIds = new List<int>(waveKillsByChain.Keys);
            chainIds.Sort();
            hash.Add(chainIds.Count);
            for (int i = 0; i < chainIds.Count; i++)
            {
                hash.Add(chainIds[i]);
                hash.Add(waveKillsByChain[chainIds[i]]);
            }

            var lineageIds = new List<int>(
                waveEconomyBonusLineages);
            lineageIds.Sort();
            hash.Add(lineageIds.Count);
            for (int i = 0; i < lineageIds.Count; i++)
            {
                hash.Add(lineageIds[i]);
            }
        }
    }
}
