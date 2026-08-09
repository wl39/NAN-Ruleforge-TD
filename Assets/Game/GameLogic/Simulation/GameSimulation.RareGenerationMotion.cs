using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 환원 카드 한 장이 탄환에 부여한 1회 재발사 계약이다.
    /// 같은 카드 인스턴스가 반복 실행되어도 런타임은 하나만 유지한다.
    /// </summary>
    internal sealed class RareProjectileReturnRuntime
    {
        public TowerId TowerId;
        public CardId CardId;
        public int CardInstanceId;
        public CompiledEffectNode Node;
        public bool Used;

        public RareProjectileReturnRuntime Clone()
        {
            return (RareProjectileReturnRuntime)MemberwiseClone();
        }
    }

    /// <summary>
    /// 역행 탄환이 마지막 적중 뒤 한 번만 진행 방향을 뒤집도록 보존하는 상태다.
    /// </summary>
    internal sealed class RareProjectileRetrogradeRuntime
    {
        public TowerId TowerId;
        public CardId CardId;
        public int CardInstanceId;
        public CompiledEffectNode Node;
        public bool Used;

        public RareProjectileRetrogradeRuntime Clone()
        {
            return (RareProjectileRetrogradeRuntime)MemberwiseClone();
        }
    }

    /// <summary>
    /// 적 복제체와 원본 사이에 전달할 피해 비율을 양방향으로 기록한다.
    /// 전달 피해에는 Repeated 태그가 붙으므로 다시 반사되지 않는다.
    /// </summary>
    internal sealed class RareEnemyCloneRuntime
    {
        public EntityId OriginalId;
        public EntityId CloneId;
        public int DamageShareBps;

        public EntityId GetCounterpart(EntityId entityId)
        {
            if (entityId == OriginalId)
            {
                return CloneId;
            }

            return entityId == CloneId
                ? OriginalId
                : EntityId.Invalid;
        }
    }

    /// <summary>환원이 참조할 적의 과거 경로 위치 한 점이다.</summary>
    internal readonly struct RareEnemyPathSample
    {
        public RareEnemyPathSample(long tick, long pathProgressMilli)
        {
            Tick = tick;
            PathProgressMilli = pathProgressMilli;
        }

        public long Tick { get; }
        public long PathProgressMilli { get; }
    }

    /// <summary>
    /// 적 하나의 제한된 경로 이력이다. 오래된 표본부터 저장하며 고정 상한을 넘으면 제거한다.
    /// </summary>
    internal sealed class RareEnemyPathHistory
    {
        public readonly List<RareEnemyPathSample> Samples =
            new List<RareEnemyPathSample>(128);
    }

    /// <summary>역행 적이 남은 시간 동안 경로 반대 방향으로 이동하기 위한 상태다.</summary>
    internal sealed class RareEnemyRetrogradeRuntime
    {
        public TowerId TowerId;
        public CardId CardId;
        public int CardInstanceId;
        public EntityId SourceEntityId;
        public int RemainingTicks;
        public int SpeedBps;
    }

    /// <summary>
    /// 희귀 카드 중 생성·희생·과거 위치·역방향 이동을 담당하는 순수 게임 로직이다.
    /// 모든 생성과 피해는 기존 ChainBudget/EventQueue를 사용하며, 보조 Dictionary는
    /// EntityId 키를 정렬해 상태 해시에 넣으므로 Editor와 WebGL 결과가 동일하다.
    /// </summary>
    public sealed partial class GameSimulation
    {
        private const int RareDefaultDuplicateDamageBps = 6500;
        private const int RareDefaultCloneHealthBps = 5000;
        private const int RareDefaultCloneDamageShareBps = 5000;
        private const int RareDefaultSacrificeTransferBps = 10000;
        private const int RareDefaultSacrificeHealthBps = 2500;
        private const int RareDefaultEffectRadiusMilli = 3000;
        private const int RareDefaultTargetLimit = 4;
        private const int RareDefaultReturnLifetimeTicks = 90;
        private const int RareDefaultRetrogradeDurationTicks = 45;
        private const int RareMaximumHistoryTicks = 600;

        private readonly HashSet<int> rareDuplicateProjectileIds =
            new HashSet<int>();
        private readonly HashSet<int> rareDuplicateEnemyIds =
            new HashSet<int>();
        private readonly Dictionary<int, List<RareProjectileReturnRuntime>>
            rareProjectileReturns =
                new Dictionary<int, List<RareProjectileReturnRuntime>>();
        private readonly Dictionary<int, RareProjectileRetrogradeRuntime>
            rareProjectileRetrogrades =
                new Dictionary<int, RareProjectileRetrogradeRuntime>();
        private readonly Dictionary<int, RareEnemyCloneRuntime>
            rareEnemyCloneLinks =
                new Dictionary<int, RareEnemyCloneRuntime>();
        private readonly Dictionary<int, RareEnemyPathHistory>
            rareEnemyPathHistories =
                new Dictionary<int, RareEnemyPathHistory>();
        private readonly Dictionary<int, RareEnemyRetrogradeRuntime>
            rareEnemyRetrogrades =
                new Dictionary<int, RareEnemyRetrogradeRuntime>();
        private readonly List<int> rareGenerationMotionKeyScratch =
            new List<int>(128);
        private readonly List<EnemyState> rareGenerationMotionEnemyScratch =
            new List<EnemyState>(32);
        private readonly List<GameEvent> rareGenerationMotionEventScratch =
            new List<GameEvent>(32);

        /// <summary>
        /// 같은 GameSimulation 인스턴스를 새 런에 재사용할 때 희귀 카드의 이전 상태를 지운다.
        /// GameSimulation.Initialize에서 공용 상태 초기화와 함께 한 번 호출해야 한다.
        /// </summary>
        internal void ResetRareGenerationMotionState()
        {
            rareDuplicateProjectileIds.Clear();
            rareDuplicateEnemyIds.Clear();
            rareProjectileReturns.Clear();
            rareProjectileRetrogrades.Clear();
            rareEnemyCloneLinks.Clear();
            rareEnemyPathHistories.Clear();
            rareEnemyRetrogrades.Clear();
            rareGenerationMotionKeyScratch.Clear();
            rareGenerationMotionEnemyScratch.Clear();
            rareGenerationMotionEventScratch.Clear();
        }

        /// <summary>
        /// 복제의 탄환 해석이다. 현재 물리 수치, 이미 부착된 적중 효과와 카드 런타임을
        /// 복사한 유령 탄환 하나를 만들고, 복제체 자신은 다시 복제할 수 없게 표시한다.
        /// </summary>
        /// <returns>
        /// 생성된 탄환 ID다. executor는 유효한 ID일 때 기존 Split outcome을 사용해
        /// 원본과 복제체 모두 카드 오른쪽 continuation으로 진행시킨다.
        /// </returns>
        internal EntityId DuplicateRareProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState original = FindProjectile(context.SubjectId);
            if (original == null ||
                !original.Alive ||
                !CanCreateProjectileEntity(
                    original.Generation + 1) ||
                rareDuplicateProjectileIds.Contains(original.Id.Value) ||
                !TryReserveRareDuplicate(
                    context,
                    SubjectType.Projectile,
                    projectileSpawnCount: 1))
            {
                return EntityId.Invalid;
            }

            int damageBps = node.Amount > 0
                ? Math.Min(10000, node.Amount)
                : RareDefaultDuplicateDamageBps;
            var clone = new ProjectileState
            {
                Id = new EntityId(nextEntityId++),
                SourceTowerId = original.SourceTowerId,
                Generation = checked(original.Generation + 1),
                Position = original.Position,
                TargetId = original.TargetId,
                ApplyEnemyProgramOnHit =
                    original.ApplyEnemyProgramOnHit,
                DirectionXBps = original.DirectionXBps,
                DirectionYBps = original.DirectionYBps,
                Homing = original.Homing,
                VisualFlags =
                    original.VisualFlags |
                    CardEffectVisualFlags.Duplicate,
                DamageMilli =
                    DeterministicMath.MultiplyBasisPoints(
                        original.DamageMilli,
                        damageBps),
                SpeedMilliPerTick = original.SpeedMilliPerTick,
                RadiusMilli = original.RadiusMilli,
                LifetimeRemaining = Math.Max(
                    1,
                    original.LifetimeRemaining),
                PierceRemaining = original.PierceRemaining,
                PiercesUsed = original.PiercesUsed,
                PierceDamageMultiplierBps =
                    original.PierceDamageMultiplierBps,
                CriticalChanceBps = original.CriticalChanceBps,
                RootChainId = original.RootChainId,
                ActivationId = original.ActivationId,
                LastTrailPosition = original.Position
            };
            for (int i = 0; i < original.Bindings.Count; i++)
            {
                clone.Bindings.Add(original.Bindings[i].Clone());
            }
            foreach (int hitEnemyId in original.HitEnemies)
            {
                clone.HitEnemies.Add(hitEnemyId);
            }

            projectiles.Add(clone);
            CloneRareProjectileInheritedState(original, clone);
            InheritLegendaryProjectileState(
                original,
                clone);
            rareDuplicateProjectileIds.Add(clone.Id.Value);
            AddPresentation(
                PresentationEventType.ProjectileSpawned,
                clone.Id.Value,
                original.Id.Value,
                (int)Math.Min(int.MaxValue, clone.DamageMilli),
                "duplicate");
            AddRareGenerationMotionPresentation(
                "rare_duplicate_projectile",
                clone.Id,
                original.Id,
                clone.DamageMilli);
            return clone.Id;
        }

        /// <summary>
        /// 복제의 적 해석이다. 현재 상태를 가진 무보상 유령 복제체를 만들고,
        /// 원본과 복제체가 받은 피해 일부를 EventQueue를 통해 서로 공유하게 한다.
        /// </summary>
        internal EntityId DuplicateRareEnemy(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState original = FindEnemy(context.SubjectId);
            if (original == null ||
                !original.Alive ||
                original.Generation >=
                    content.Safety.MaxEnemySplitGeneration ||
                enemies.Count >=
                    content.Safety.MaxActiveEnemies ||
                rareDuplicateEnemyIds.Contains(original.Id.Value) ||
                rareEnemyCloneLinks.ContainsKey(original.Id.Value) ||
                !lineages.TryGetValue(
                    original.LineageId.Value,
                    out LineageState lineage))
            {
                return EntityId.Invalid;
            }

            GameEvent diagnosticEvent = WithDiagnosticDepth(
                CreateDiagnosticEvent(
                    EventType.EnemySplit,
                    context.RootChainId,
                    context.TowerId,
                    context.CardId,
                    context.SubjectId,
                    SubjectType.Enemy),
                context.Depth);
            if (lineage.SpawnedEntityCount >=
                content.Safety.MaxEnemiesPerLineage)
            {
                AddDiagnostic(
                    DiagnosticCode.EnemyLineageLimitReached,
                    diagnosticEvent,
                    (int)BudgetFailure.EnemyLineageEntityLimit);
                return EntityId.Invalid;
            }
            if (!TryPassSandboxEnemyCreationGate(
                    1,
                    in diagnosticEvent))
            {
                return EntityId.Invalid;
            }
            if (!TryReserveRareDuplicate(
                    context,
                    SubjectType.Enemy,
                    projectileSpawnCount: 0))
            {
                return EntityId.Invalid;
            }

            int healthBps = node.Amount > 0
                ? Math.Min(10000, node.Amount)
                : RareDefaultCloneHealthBps;
            long cloneMaxHealth = Math.Max(
                1000,
                DeterministicMath.MultiplyBasisPoints(
                    original.MaxHealthMilli,
                    healthBps));
            long cloneHealth = Math.Max(
                1000,
                Math.Min(
                    cloneMaxHealth,
                    DeterministicMath.MultiplyBasisPoints(
                        original.HealthMilli,
                        healthBps)));
            path.GetDirectionBasisPoints(
                original.PathProgressMilli,
                out int directionX,
                out int directionY);
            int separation = Math.Max(
                100,
                GetEnemyHitRadiusMilli(original));
            SimVector cloneOffset =
                original.PathLateralOffset +
                SimVector.FromMilliUnits(
                    DeterministicMath.MultiplyDivide(
                        directionY,
                        separation,
                        DeterministicMath.BasisPointScale),
                    DeterministicMath.MultiplyDivide(
                        -directionX,
                        separation,
                        DeterministicMath.BasisPointScale));
            var clone = new EnemyState
            {
                Id = new EntityId(nextEntityId++),
                DefinitionId = original.DefinitionId,
                LineageId = original.LineageId,
                Generation = checked(original.Generation + 1),
                SpawnOrigin = EnemySpawnOrigin.Split,
                SummonerId = original.SummonerId,
                EliteTraitIds =
                    (EliteTraitId[])original.EliteTraitIds.Clone(),
                PathProgressMilli = original.PathProgressMilli,
                PathLateralOffset = cloneOffset,
                Position =
                    path.GetPosition(original.PathProgressMilli) +
                    cloneOffset,
                HealthMilli = cloneHealth,
                MaxHealthMilli = cloneMaxHealth,
                Armor = original.Armor,
                BaseSpeedMilliPerTick =
                    original.BaseSpeedMilliPerTick,
                SpeedMultiplierBps = original.SpeedMultiplierBps,
                SizeMultiplierBps = original.SizeMultiplierBps,
                EliteRenderScaleBps =
                    original.EliteRenderScaleBps,
                AreaDamageTakenBps =
                    original.AreaDamageTakenBps,
                SingleDamageTakenBps =
                    original.SingleDamageTakenBps,
                VisualFlags =
                    original.VisualFlags |
                    CardEffectVisualFlags.Duplicate,
                RewardBudget = 0,
                WaveProgressBudget = 0,
                CardPackProgressBudget = 0,
                IsShimmering = false,
                ControlGauge = original.ControlGauge,
                ControlThreshold = original.ControlThreshold,
                ControlThresholdStep =
                    original.ControlThresholdStep,
                ShieldMilli =
                    DeterministicMath.MultiplyBasisPoints(
                        original.ShieldMilli,
                        healthBps),
                BossAbilityCooldownTicks =
                    original.BossAbilityCooldownTicks,
                BossCastRemainingTicks =
                    original.BossCastRemainingTicks,
                BossEnraged = original.BossEnraged,
                BossPhaseAnnounced =
                    original.BossPhaseAnnounced
            };
            CloneEnemyStatuses(original, clone);
            for (int i = 0; i < original.DeathBindings.Count; i++)
            {
                clone.DeathBindings.Add(
                    original.DeathBindings[i].Clone());
            }

            enemies.Add(clone);
            InheritRangeEntryLocks(original, clone);
            InheritLegendaryEnemyState(
                original,
                clone);
            lineage.HighestGeneration = Math.Max(
                lineage.HighestGeneration,
                clone.Generation);
            lineage.SpawnedEntityCount++;
            lineage.LiveMembers++;
            rareDuplicateEnemyIds.Add(clone.Id.Value);
            int damageShareBps = node.Amount2 > 0
                ? Math.Min(10000, node.Amount2)
                : RareDefaultCloneDamageShareBps;
            var link = new RareEnemyCloneRuntime
            {
                OriginalId = original.Id,
                CloneId = clone.Id,
                DamageShareBps = damageShareBps
            };
            rareEnemyCloneLinks[original.Id.Value] = link;
            rareEnemyCloneLinks[clone.Id.Value] = link;
            spatialIndex.Rebuild(enemies);
            AddPresentation(
                PresentationEventType.EnemySpawned,
                clone.Id.Value,
                original.Id.Value,
                0,
                "duplicate");
            AddRareGenerationMotionPresentation(
                "rare_duplicate_enemy",
                clone.Id,
                original.Id,
                damageShareBps);
            return clone.Id;
        }

        /// <summary>
        /// 희생의 탄환 해석이다. 같은 타워의 가장 가까운 살아 있는 탄환으로
        /// 피해·부착 효과를 넘기고, 카드 오른쪽 continuation도 그 탄환에 새 큐 작업으로 넘긴다.
        /// </summary>
        internal void SacrificeRareProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState source = FindProjectile(context.SubjectId);
            if (source == null || !source.Alive)
            {
                return;
            }

            ProjectileState recipient =
                SelectRareSacrificeProjectile(
                    source,
                    node.RadiusMilli > 0
                        ? node.RadiusMilli
                        : RareDefaultEffectRadiusMilli);
            if (recipient == null)
            {
                return;
            }

            int transferBps = node.Amount > 0
                ? Math.Min(30000, node.Amount)
                : RareDefaultSacrificeTransferBps;
            long transferredDamage =
                DeterministicMath.MultiplyBasisPoints(
                    source.DamageMilli,
                    transferBps);
            recipient.DamageMilli = SaturatingAdd(
                recipient.DamageMilli,
                transferredDamage);
            recipient.RadiusMilli = Math.Max(
                recipient.RadiusMilli,
                source.RadiusMilli);
            recipient.LifetimeRemaining = Math.Min(
                content.Safety.MaxProjectileLifetimeTicks,
                Math.Max(
                    recipient.LifetimeRemaining,
                    source.LifetimeRemaining));
            recipient.VisualFlags |=
                source.VisualFlags |
                CardEffectVisualFlags.Sacrifice;
            for (int i = 0; i < source.Bindings.Count; i++)
            {
                recipient.Bindings.Add(
                    source.Bindings[i].Clone());
            }
            MergeRareProjectileInheritedState(source, recipient);
            RedirectRareProjectileContinuation(
                context,
                recipient.Id);

            source.Alive = false;
            source.ExpirationQueued = false;
            AddPresentation(
                PresentationEventType.ProjectileExpired,
                source.Id.Value,
                recipient.Id.Value,
                (int)Math.Min(int.MaxValue, transferredDamage),
                "sacrifice");
            AddRareGenerationMotionPresentation(
                "rare_sacrifice_projectile",
                recipient.Id,
                source.Id,
                transferredDamage);
            AddUncommonAreaPresentation(
                "rare_sacrifice_projectile",
                source.Id,
                source.Id,
                node.RadiusMilli > 0
                    ? node.RadiusMilli
                    : RareDefaultEffectRadiusMilli,
                source.Position);
        }

        /// <summary>
        /// 희생의 적 해석이다. 현재 체력 일부를 잃는 피해와 주변 적에게 나누는 피해를
        /// 하나의 원자적 EventQueue 배치로 예약한다.
        /// </summary>
        internal void SacrificeRareEnemy(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState source = FindEnemy(context.SubjectId);
            if (source == null || !source.Alive)
            {
                return;
            }

            int healthBps = node.Amount > 0
                ? Math.Min(10000, node.Amount)
                : RareDefaultSacrificeHealthBps;
            long sacrificedHealth = Math.Max(
                1,
                DeterministicMath.MultiplyBasisPoints(
                    source.HealthMilli,
                    healthBps));
            int radius = node.RadiusMilli > 0
                ? node.RadiusMilli
                : RareDefaultEffectRadiusMilli;
            int limit = node.Limit > 0
                ? Math.Min(
                    content.Safety.MaxEnemiesPerLineage,
                    node.Limit)
                : RareDefaultTargetLimit;
            SimPosition areaCenter =
                GetEnemyHitboxCenter(source);
            CollectRareEnemyTargets(
                areaCenter,
                radius,
                source.Id,
                limit);

            rareGenerationMotionEventScratch.Clear();
            if (TryCreateDamageEvent(
                    source.Id,
                    context.TowerId,
                    context.CardId,
                    context.SourceEntityId,
                    sacrificedHealth,
                    DamageKind.Physical,
                    10000,
                    context.RootChainId,
                    context.ActivationId,
                    context.ParentEventId,
                    context.Depth + 1,
                    EventTags.SingleTarget |
                    EventTags.Repeated,
                    out GameEvent selfDamage))
            {
                rareGenerationMotionEventScratch.Add(selfDamage);
            }

            if (rareGenerationMotionEnemyScratch.Count > 0)
            {
                long sharedDamage = Math.Max(
                    1,
                    sacrificedHealth /
                    rareGenerationMotionEnemyScratch.Count);
                for (int i = 0;
                     i < rareGenerationMotionEnemyScratch.Count;
                     i++)
                {
                    EnemyState target =
                        rareGenerationMotionEnemyScratch[i];
                    if (TryCreateDamageEvent(
                            target.Id,
                            context.TowerId,
                            context.CardId,
                            source.Id,
                            sharedDamage,
                            DamageKind.Physical,
                            0,
                            context.RootChainId,
                            context.ActivationId,
                            context.ParentEventId,
                            context.Depth + 1,
                            EventTags.Area |
                            EventTags.Repeated,
                            out GameEvent sharedEvent))
                    {
                        rareGenerationMotionEventScratch.Add(
                            sharedEvent);
                    }
                }
            }

            if (rareGenerationMotionEventScratch.Count > 0 &&
                TryEnqueueBatch(rareGenerationMotionEventScratch))
            {
                source.VisualFlags |=
                    CardEffectVisualFlags.Sacrifice;
                AddRareGenerationMotionPresentation(
                    "rare_sacrifice_enemy",
                    source.Id,
                    context.SourceEntityId,
                    sacrificedHealth);
                AddUncommonAreaPresentation(
                    "rare_sacrifice_enemy",
                    source.Id,
                    source.Id,
                    radius,
                    areaCenter);
            }
        }

        /// <summary>
        /// 환원의 탄환 해석을 등록한다. 같은 카드 인스턴스는 한 번만 등록되며,
        /// 실제 재발사는 ProcessProjectileExpiredEvent의 마지막 훅에서 처리한다.
        /// </summary>
        internal void ConfigureRareProjectileReturn(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            if (!rareProjectileReturns.TryGetValue(
                    projectile.Id.Value,
                    out List<RareProjectileReturnRuntime> runtimes))
            {
                runtimes =
                    new List<RareProjectileReturnRuntime>(2);
                rareProjectileReturns.Add(
                    projectile.Id.Value,
                    runtimes);
            }
            for (int i = 0; i < runtimes.Count; i++)
            {
                if (runtimes[i].CardInstanceId ==
                    context.CardInstanceId)
                {
                    return;
                }
            }

            runtimes.Add(new RareProjectileReturnRuntime
            {
                TowerId = context.TowerId,
                CardId = context.CardId,
                CardInstanceId = context.CardInstanceId,
                Node = node
            });
            projectile.VisualFlags |=
                CardEffectVisualFlags.Return;
        }

        /// <summary>
        /// 환원의 적 해석이다. 기록된 표본 중 DurationTicks 이전에 가장 가까운
        /// 경로 위치로 되돌리며 체력과 상태이상은 전혀 변경하지 않는다.
        /// </summary>
        internal void RewindRareEnemy(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null ||
                !enemy.Alive ||
                !rareEnemyPathHistories.TryGetValue(
                    enemy.Id.Value,
                    out RareEnemyPathHistory history) ||
                history.Samples.Count == 0)
            {
                return;
            }

            int lookbackTicks = Math.Max(
                1,
                node.DurationTicks);
            long targetTick = Math.Max(0, tick - lookbackTicks);
            RareEnemyPathSample selected = history.Samples[0];
            for (int i = 1; i < history.Samples.Count; i++)
            {
                RareEnemyPathSample candidate =
                    history.Samples[i];
                if (candidate.Tick > targetTick)
                {
                    break;
                }
                selected = candidate;
            }

            long previousProgress = enemy.PathProgressMilli;
            enemy.PathProgressMilli = Math.Max(
                0,
                Math.Min(
                    path.TotalLengthMilli,
                    selected.PathProgressMilli));
            RefreshEnemyPosition(enemy);
            enemy.VisualFlags |=
                CardEffectVisualFlags.Return;
            AddPresentation(
                PresentationEventType.EnemyMoved,
                enemy.Id.Value,
                context.SourceEntityId.Value,
                (int)Math.Max(
                    int.MinValue,
                    Math.Min(
                        int.MaxValue,
                        enemy.PathProgressMilli -
                        previousProgress)),
                "return");
            AddRareGenerationMotionPresentation(
                "rare_rewind_enemy",
                enemy.Id,
                context.SourceEntityId,
                previousProgress - enemy.PathProgressMilli);
        }

        /// <summary>
        /// 역행의 탄환 해석을 등록한다. 관통/천공이 더 이상 이어지지 않는 마지막 적중에
        /// 한 번만 방향을 뒤집고 기존 적중 원장을 비워 되돌아가는 길에서 다시 맞힐 수 있게 한다.
        /// </summary>
        internal void ConfigureRareProjectileRetrograde(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            rareProjectileRetrogrades[
                projectile.Id.Value] =
                    new RareProjectileRetrogradeRuntime
                    {
                        TowerId = context.TowerId,
                        CardId = context.CardId,
                        CardInstanceId =
                            context.CardInstanceId,
                        Node = node
                    };
            projectile.VisualFlags |=
                CardEffectVisualFlags.Retrograde;
        }

        /// <summary>
        /// 역행의 적 해석이다. 여러 번 적용되면 남은 시간은 긴 값을, 이동 속도는
        /// 강한 값을 사용하되 별도 개체나 재귀 이벤트를 만들지 않는다.
        /// </summary>
        internal void ApplyRareEnemyRetrograde(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            int duration = node.DurationTicks > 0
                ? node.DurationTicks
                : RareDefaultRetrogradeDurationTicks;
            int speedBps = node.Amount > 0
                ? Math.Min(20000, node.Amount)
                : 10000;
            if (!rareEnemyRetrogrades.TryGetValue(
                    enemy.Id.Value,
                    out RareEnemyRetrogradeRuntime runtime))
            {
                runtime = new RareEnemyRetrogradeRuntime
                {
                    TowerId = context.TowerId,
                    CardId = context.CardId,
                    CardInstanceId = context.CardInstanceId,
                    SourceEntityId = context.SourceEntityId
                };
                rareEnemyRetrogrades.Add(
                    enemy.Id.Value,
                    runtime);
            }

            runtime.RemainingTicks = Math.Max(
                runtime.RemainingTicks,
                duration);
            runtime.SpeedBps = Math.Max(
                runtime.SpeedBps,
                speedBps);
            enemy.VisualFlags |=
                CardEffectVisualFlags.Retrograde;
            AddRareGenerationMotionPresentation(
                "rare_retrograde_enemy_start",
                enemy.Id,
                context.SourceEntityId,
                duration);
        }

        /// <summary>
        /// MoveEnemies 직전에 호출해 현재 경로 위치를 기록한다. 최대 600틱의 표본만
        /// 유지해 환원 카드가 없는 장시간 WebGL 전투에서도 메모리가 제한된다.
        /// </summary>
        internal void RecordRareGenerationMotionEnemyHistory()
        {
            int maximumSamples = Math.Max(
                2,
                Math.Min(
                    RareMaximumHistoryTicks,
                    content.Safety.MaxProjectileLifetimeTicks));
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState enemy = enemies[i];
                if (!enemy.Alive)
                {
                    continue;
                }

                if (!rareEnemyPathHistories.TryGetValue(
                        enemy.Id.Value,
                        out RareEnemyPathHistory history))
                {
                    history = new RareEnemyPathHistory();
                    rareEnemyPathHistories.Add(
                        enemy.Id.Value,
                        history);
                }

                if (history.Samples.Count > 0 &&
                    history.Samples[
                        history.Samples.Count - 1].Tick == tick)
                {
                    history.Samples[
                        history.Samples.Count - 1] =
                            new RareEnemyPathSample(
                                tick,
                                enemy.PathProgressMilli);
                }
                else
                {
                    history.Samples.Add(
                        new RareEnemyPathSample(
                            tick,
                            enemy.PathProgressMilli));
                }

                if (history.Samples.Count > maximumSamples)
                {
                    history.Samples.RemoveRange(
                        0,
                        history.Samples.Count -
                        maximumSamples);
                }
            }
        }

        /// <summary>
        /// MoveEnemies의 일반 전진보다 먼저 호출한다. true이면 이 틱의 이동을
        /// 역행이 직접 처리했으므로 일반 이동과 다른 직접 이동 효과를 건너뛴다.
        /// </summary>
        internal bool TryProcessRareEnemyMovement(
            EnemyState enemy)
        {
            if (enemy == null ||
                !enemy.Alive ||
                !rareEnemyRetrogrades.TryGetValue(
                    enemy.Id.Value,
                    out RareEnemyRetrogradeRuntime runtime) ||
                runtime.RemainingTicks <= 0)
            {
                return false;
            }

            int movementBps = MultiplyBps(
                enemy.SpeedMultiplierBps,
                Math.Max(1, runtime.SpeedBps));
            int distance = (int)
                DeterministicMath.MultiplyBasisPoints(
                    enemy.BaseSpeedMilliPerTick,
                    movementBps);
            long previous = enemy.PathProgressMilli;
            enemy.PathProgressMilli = Math.Max(
                0,
                enemy.PathProgressMilli -
                Math.Max(1, distance));
            RefreshEnemyPosition(enemy);
            runtime.RemainingTicks--;
            AddPresentation(
                PresentationEventType.EnemyMoved,
                enemy.Id.Value,
                runtime.SourceEntityId.Value,
                (int)Math.Max(
                    int.MinValue,
                    Math.Min(
                        int.MaxValue,
                        enemy.PathProgressMilli - previous)),
                "retrograde");
            if (runtime.RemainingTicks <= 0)
            {
                rareEnemyRetrogrades.Remove(
                    enemy.Id.Value);
                AddRareGenerationMotionPresentation(
                    "rare_retrograde_enemy_end",
                    enemy.Id,
                    runtime.SourceEntityId,
                    0);
            }
            return true;
        }

        /// <summary>
        /// ProcessProjectileHitEvent가 일반 관통/소멸을 결정하기 전에 호출한다.
        /// 관통 여지가 없을 때만 한 번 역행을 시작하며, true면 이번 적중에서 탄환을 유지한다.
        /// </summary>
        internal bool TryHandleRareProjectileHit(
            ProjectileState projectile,
            EnemyState target,
            in GameEvent gameEvent)
        {
            if (projectile == null ||
                target == null ||
                !projectile.Alive ||
                !rareProjectileRetrogrades.TryGetValue(
                    projectile.Id.Value,
                    out RareProjectileRetrogradeRuntime runtime) ||
                runtime.Used ||
                (projectile.PiercesUsed <
                     content.Safety.MaxPiercesPerProjectile &&
                 (projectile.PierceRemaining > 0 ||
                  HasActiveStatus(
                      target,
                      StatusType.Pierced))))
            {
                return false;
            }

            runtime.Used = true;
            projectile.DirectionXBps =
                -projectile.DirectionXBps;
            projectile.DirectionYBps =
                -projectile.DirectionYBps;
            projectile.Homing = false;
            projectile.TargetId = EntityId.Invalid;
            projectile.HitEnemies.Clear();
            projectile.LifetimeRemaining = Math.Min(
                content.Safety.MaxProjectileLifetimeTicks,
                Math.Max(
                    projectile.LifetimeRemaining,
                    runtime.Node.DurationTicks > 0
                        ? runtime.Node.DurationTicks
                        : RareDefaultRetrogradeDurationTicks));
            if (runtime.Node.Amount > 0)
            {
                projectile.DamageMilli =
                    DeterministicMath.MultiplyBasisPoints(
                        projectile.DamageMilli,
                        Math.Min(30000, runtime.Node.Amount));
            }
            AddRareGenerationMotionPresentation(
                "rare_retrograde_projectile",
                projectile.Id,
                target.Id,
                projectile.DamageMilli);
            return true;
        }

        /// <summary>
        /// ProcessProjectileExpiredEvent가 소멸 바인딩을 실행한 뒤 Alive를 끄기 직전에 호출한다.
        /// 사용하지 않은 환원 런타임 하나를 소비해 타워 위치에서 같은 탄환을 단 한 번 재발사한다.
        /// </summary>
        internal bool TryHandleRareProjectileExpired(
            ProjectileState projectile,
            in GameEvent gameEvent)
        {
            if (projectile == null ||
                !projectile.Alive ||
                !rareProjectileReturns.TryGetValue(
                    projectile.Id.Value,
                    out List<RareProjectileReturnRuntime> runtimes))
            {
                return false;
            }

            RareProjectileReturnRuntime selected = null;
            for (int i = 0; i < runtimes.Count; i++)
            {
                if (!runtimes[i].Used)
                {
                    selected = runtimes[i];
                    break;
                }
            }
            if (selected == null)
            {
                return false;
            }

            TowerState tower = FindTower(selected.TowerId);
            if (tower == null)
            {
                selected.Used = true;
                return false;
            }

            EnemyState target = SelectProjectileTarget(projectile);
            if (target == null)
            {
                selected.Used = true;
                return false;
            }

            selected.Used = true;
            GameEvent diagnosticEvent = WithDiagnosticDepth(
                CreateDiagnosticEvent(
                    EventType.ProjectileSpawned,
                    projectile.RootChainId,
                    selected.TowerId,
                    selected.CardId,
                    projectile.Id,
                    SubjectType.Projectile),
                gameEvent.Depth + 1);
            if (!TryReserveComposite(
                    in diagnosticEvent,
                    chainEventCount: 0,
                    queueSlotCount: 0,
                    projectileSpawnCount: 1,
                    cardTriggerCount: 0))
            {
                return false;
            }

            projectile.Position = tower.Position;
            projectile.LastTrailPosition = tower.Position;
            projectile.TargetId = target.Id;
            projectile.ExpirationQueued = false;
            projectile.HitEnemies.Clear();
            projectile.PiercesUsed = 0;
            projectile.LifetimeRemaining = Math.Min(
                content.Safety.MaxProjectileLifetimeTicks,
                selected.Node.DurationTicks > 0
                    ? selected.Node.DurationTicks
                    : RareDefaultReturnLifetimeTicks);
            if (selected.Node.Amount > 0)
            {
                projectile.DamageMilli =
                    DeterministicMath.MultiplyBasisPoints(
                        projectile.DamageMilli,
                        Math.Min(30000, selected.Node.Amount));
            }
            SetProjectileDirection(
                projectile,
                target.Position);
            AddPresentation(
                PresentationEventType.ProjectileSpawned,
                projectile.Id.Value,
                tower.Id.Value,
                (int)Math.Min(
                    int.MaxValue,
                    projectile.DamageMilli),
                "return");
            AddRareGenerationMotionPresentation(
                "rare_return_projectile",
                projectile.Id,
                new EntityId(tower.Id.Value),
                projectile.DamageMilli);
            return true;
        }

        /// <summary>
        /// ProcessDamageEvent가 실제 체력 피해를 확정한 뒤 호출한다. 복제 링크의 상대에게
        /// 공유 피해를 새 이벤트로 전달하며 Repeated 태그로 왕복 재귀를 차단한다.
        /// </summary>
        internal void HandleRareGenerationMotionDamageApplied(
            EnemyState enemy,
            in GameEvent gameEvent,
            long appliedAmount)
        {
            if (enemy == null ||
                appliedAmount <= 0 ||
                (gameEvent.Tags & EventTags.Repeated) != 0 ||
                !rareEnemyCloneLinks.TryGetValue(
                    enemy.Id.Value,
                    out RareEnemyCloneRuntime link))
            {
                return;
            }

            EnemyState counterpart =
                FindEnemy(link.GetCounterpart(enemy.Id));
            if (counterpart == null || !counterpart.Alive)
            {
                return;
            }

            long sharedDamage =
                DeterministicMath.MultiplyBasisPoints(
                    appliedAmount,
                    Math.Max(
                        0,
                        Math.Min(10000, link.DamageShareBps)));
            if (sharedDamage <= 0)
            {
                return;
            }

            EnqueueDamage(
                counterpart.Id,
                gameEvent.SourceTowerId,
                gameEvent.SourceCardId,
                enemy.Id,
                sharedDamage,
                DamageKind.Physical,
                0,
                gameEvent.RootChainId,
                gameEvent.ActivationId,
                gameEvent.EventId,
                gameEvent.Depth + 1,
                EventTags.SingleTarget |
                EventTags.Repeated);
            AddRareGenerationMotionPresentation(
                "rare_duplicate_health_share",
                counterpart.Id,
                enemy.Id,
                sharedDamage);
        }

        /// <summary>
        /// CleanupDeadEntities 시작 부분에서 호출해 제거된 개체의 희귀 카드 보조 상태를 정리한다.
        /// 삭제 키는 항상 정렬해 Dictionary 버킷 순서가 결과에 영향을 주지 않게 한다.
        /// </summary>
        internal void CleanupRareGenerationMotionState()
        {
            rareGenerationMotionKeyScratch.Clear();
            foreach (
                KeyValuePair<int, List<RareProjectileReturnRuntime>>
                pair in rareProjectileReturns)
            {
                ProjectileState projectile =
                    FindProjectile(new EntityId(pair.Key));
                if (projectile == null || !projectile.Alive)
                {
                    rareGenerationMotionKeyScratch.Add(
                        pair.Key);
                }
            }
            foreach (
                KeyValuePair<int, RareProjectileRetrogradeRuntime>
                pair in rareProjectileRetrogrades)
            {
                ProjectileState projectile =
                    FindProjectile(new EntityId(pair.Key));
                if (projectile == null || !projectile.Alive)
                {
                    rareGenerationMotionKeyScratch.Add(
                        pair.Key);
                }
            }
            foreach (int projectileId
                     in rareDuplicateProjectileIds)
            {
                ProjectileState projectile =
                    FindProjectile(
                        new EntityId(projectileId));
                if (projectile == null || !projectile.Alive)
                {
                    rareGenerationMotionKeyScratch.Add(
                        projectileId);
                }
            }
            rareGenerationMotionKeyScratch.Sort();
            for (int i = 0;
                 i < rareGenerationMotionKeyScratch.Count;
                 i++)
            {
                int key = rareGenerationMotionKeyScratch[i];
                rareProjectileReturns.Remove(key);
                rareProjectileRetrogrades.Remove(key);
                rareDuplicateProjectileIds.Remove(key);
            }

            rareGenerationMotionKeyScratch.Clear();
            foreach (KeyValuePair<int, RareEnemyPathHistory> pair
                     in rareEnemyPathHistories)
            {
                EnemyState enemy =
                    FindEnemy(new EntityId(pair.Key));
                if (enemy == null || !enemy.Alive)
                {
                    rareGenerationMotionKeyScratch.Add(
                        pair.Key);
                }
            }
            foreach (
                KeyValuePair<int, RareEnemyRetrogradeRuntime>
                pair in rareEnemyRetrogrades)
            {
                EnemyState enemy =
                    FindEnemy(new EntityId(pair.Key));
                if (enemy == null || !enemy.Alive)
                {
                    rareGenerationMotionKeyScratch.Add(
                        pair.Key);
                }
            }
            foreach (int enemyId in rareDuplicateEnemyIds)
            {
                EnemyState enemy =
                    FindEnemy(new EntityId(enemyId));
                if (enemy == null || !enemy.Alive)
                {
                    rareGenerationMotionKeyScratch.Add(
                        enemyId);
                }
            }
            rareGenerationMotionKeyScratch.Sort();
            for (int i = 0;
                 i < rareGenerationMotionKeyScratch.Count;
                 i++)
            {
                int key = rareGenerationMotionKeyScratch[i];
                rareEnemyPathHistories.Remove(key);
                rareEnemyRetrogrades.Remove(key);
                rareDuplicateEnemyIds.Remove(key);
            }

            rareGenerationMotionKeyScratch.Clear();
            foreach (KeyValuePair<int, RareEnemyCloneRuntime> pair
                     in rareEnemyCloneLinks)
            {
                RareEnemyCloneRuntime link = pair.Value;
                EnemyState original = FindEnemy(link.OriginalId);
                EnemyState clone = FindEnemy(link.CloneId);
                if (original == null ||
                    !original.Alive ||
                    clone == null ||
                    !clone.Alive)
                {
                    rareGenerationMotionKeyScratch.Add(
                        pair.Key);
                }
            }
            rareGenerationMotionKeyScratch.Sort();
            for (int i = 0;
                 i < rareGenerationMotionKeyScratch.Count;
                 i++)
            {
                int key = rareGenerationMotionKeyScratch[i];
                if (!rareEnemyCloneLinks.TryGetValue(
                        key,
                        out RareEnemyCloneRuntime link))
                {
                    continue;
                }
                rareEnemyCloneLinks.Remove(
                    link.OriginalId.Value);
                rareEnemyCloneLinks.Remove(
                    link.CloneId.Value);
            }
        }

        /// <summary>
        /// ComputeStateHash의 Finish 직전에 호출한다. 모든 Dictionary와 HashSet 키를
        /// 정렬한 뒤 희귀 카드의 미래 결과를 바꾸는 상태를 빠짐없이 기록한다.
        /// </summary>
        internal void AppendRareGenerationMotionStateHash(
            ref StableHashBuilder hash)
        {
            AppendSortedRareIdSet(
                ref hash,
                rareDuplicateProjectileIds);
            AppendSortedRareIdSet(
                ref hash,
                rareDuplicateEnemyIds);

            rareGenerationMotionKeyScratch.Clear();
            foreach (
                KeyValuePair<int, List<RareProjectileReturnRuntime>>
                pair in rareProjectileReturns)
            {
                rareGenerationMotionKeyScratch.Add(pair.Key);
            }
            rareGenerationMotionKeyScratch.Sort();
            hash.Add(rareGenerationMotionKeyScratch.Count);
            for (int i = 0;
                 i < rareGenerationMotionKeyScratch.Count;
                 i++)
            {
                int key = rareGenerationMotionKeyScratch[i];
                hash.Add(key);
                List<RareProjectileReturnRuntime> runtimes =
                    rareProjectileReturns[key];
                hash.Add(runtimes.Count);
                for (int runtimeIndex = 0;
                     runtimeIndex < runtimes.Count;
                     runtimeIndex++)
                {
                    RareProjectileReturnRuntime runtime =
                        runtimes[runtimeIndex];
                    hash.Add(runtime.TowerId);
                    hash.Add(runtime.CardId);
                    hash.Add(runtime.CardInstanceId);
                    AppendEffectNodeHash(
                        ref hash,
                        runtime.Node);
                    hash.Add(runtime.Used);
                }
            }

            rareGenerationMotionKeyScratch.Clear();
            foreach (
                KeyValuePair<int, RareProjectileRetrogradeRuntime>
                pair in rareProjectileRetrogrades)
            {
                rareGenerationMotionKeyScratch.Add(pair.Key);
            }
            rareGenerationMotionKeyScratch.Sort();
            hash.Add(rareGenerationMotionKeyScratch.Count);
            for (int i = 0;
                 i < rareGenerationMotionKeyScratch.Count;
                 i++)
            {
                int key = rareGenerationMotionKeyScratch[i];
                RareProjectileRetrogradeRuntime runtime =
                    rareProjectileRetrogrades[key];
                hash.Add(key);
                hash.Add(runtime.TowerId);
                hash.Add(runtime.CardId);
                hash.Add(runtime.CardInstanceId);
                AppendEffectNodeHash(ref hash, runtime.Node);
                hash.Add(runtime.Used);
            }

            rareGenerationMotionKeyScratch.Clear();
            foreach (KeyValuePair<int, RareEnemyCloneRuntime> pair
                     in rareEnemyCloneLinks)
            {
                rareGenerationMotionKeyScratch.Add(pair.Key);
            }
            rareGenerationMotionKeyScratch.Sort();
            hash.Add(rareGenerationMotionKeyScratch.Count);
            for (int i = 0;
                 i < rareGenerationMotionKeyScratch.Count;
                 i++)
            {
                int key = rareGenerationMotionKeyScratch[i];
                RareEnemyCloneRuntime link =
                    rareEnemyCloneLinks[key];
                hash.Add(key);
                hash.Add(link.OriginalId);
                hash.Add(link.CloneId);
                hash.Add(link.DamageShareBps);
            }

            rareGenerationMotionKeyScratch.Clear();
            foreach (KeyValuePair<int, RareEnemyPathHistory> pair
                     in rareEnemyPathHistories)
            {
                rareGenerationMotionKeyScratch.Add(pair.Key);
            }
            rareGenerationMotionKeyScratch.Sort();
            hash.Add(rareGenerationMotionKeyScratch.Count);
            for (int i = 0;
                 i < rareGenerationMotionKeyScratch.Count;
                 i++)
            {
                int key = rareGenerationMotionKeyScratch[i];
                RareEnemyPathHistory history =
                    rareEnemyPathHistories[key];
                hash.Add(key);
                hash.Add(history.Samples.Count);
                for (int sampleIndex = 0;
                     sampleIndex < history.Samples.Count;
                     sampleIndex++)
                {
                    RareEnemyPathSample sample =
                        history.Samples[sampleIndex];
                    hash.Add(sample.Tick);
                    hash.Add(sample.PathProgressMilli);
                }
            }

            rareGenerationMotionKeyScratch.Clear();
            foreach (
                KeyValuePair<int, RareEnemyRetrogradeRuntime>
                pair in rareEnemyRetrogrades)
            {
                rareGenerationMotionKeyScratch.Add(pair.Key);
            }
            rareGenerationMotionKeyScratch.Sort();
            hash.Add(rareGenerationMotionKeyScratch.Count);
            for (int i = 0;
                 i < rareGenerationMotionKeyScratch.Count;
                 i++)
            {
                int key = rareGenerationMotionKeyScratch[i];
                RareEnemyRetrogradeRuntime runtime =
                    rareEnemyRetrogrades[key];
                hash.Add(key);
                hash.Add(runtime.TowerId);
                hash.Add(runtime.CardId);
                hash.Add(runtime.CardInstanceId);
                hash.Add(runtime.SourceEntityId);
                hash.Add(runtime.RemainingTicks);
                hash.Add(runtime.SpeedBps);
            }
        }

        private bool TryReserveRareDuplicate(
            in EffectExecutionContext context,
            SubjectType subjectType,
            int projectileSpawnCount)
        {
            GameEvent diagnosticEvent = WithDiagnosticDepth(
                CreateDiagnosticEvent(
                    subjectType == SubjectType.Projectile
                        ? EventType.ProjectileSpawned
                        : EventType.EnemySplit,
                    context.RootChainId,
                    context.TowerId,
                    context.CardId,
                    context.SubjectId,
                    subjectType),
                context.Depth);
            int continuationCount =
                context.ContinuationCardCount;
            int missingOriginalContinuations = Math.Max(
                0,
                continuationCount -
                context.ReservedContinuationEvents);
            int newlyReservedContinuations = checked(
                missingOriginalContinuations +
                continuationCount);
            return TryReserveComposite(
                in diagnosticEvent,
                chainEventCount:
                    newlyReservedContinuations,
                queueSlotCount:
                    continuationCount > 0 ? 2 : 0,
                projectileSpawnCount:
                    projectileSpawnCount,
                cardTriggerCount:
                    newlyReservedContinuations,
                enemySpawnCount:
                    subjectType == SubjectType.Enemy ? 1 : 0);
        }

        private void CloneEnemyStatuses(
            EnemyState source,
            EnemyState target)
        {
            for (int i = 0; i < source.Statuses.Count; i++)
            {
                StatusInstance status = source.Statuses[i];
                target.Statuses.Add(new StatusInstance
                {
                    InstanceId = nextStatusInstanceId++,
                    Type = status.Type,
                    SourceEntityId = status.SourceEntityId,
                    SourceTowerId = status.SourceTowerId,
                    SourceCardId = status.SourceCardId,
                    SourceCardInstanceId =
                        status.SourceCardInstanceId,
                    Stacks = status.Stacks,
                    Intensity = status.Intensity,
                    RemainingTicks = status.RemainingTicks,
                    MaxStacks = status.MaxStacks,
                    TickInterval = status.TickInterval,
                    NextTick = status.NextTick,
                    Inherited = true,
                    Dispellable = status.Dispellable,
                    Limit = status.Limit,
                    RadiusMilli = status.RadiusMilli,
                    ArmorIgnoreBps =
                        status.ArmorIgnoreBps
                });
            }
        }

        private ProjectileState SelectRareSacrificeProjectile(
            ProjectileState source,
            int radiusMilli)
        {
            ProjectileState selected = null;
            long selectedDistance = long.MaxValue;
            for (int i = 0; i < projectiles.Count; i++)
            {
                ProjectileState candidate = projectiles[i];
                if (candidate == source ||
                    !candidate.Alive ||
                    candidate.ExpirationQueued ||
                    candidate.SourceTowerId !=
                    source.SourceTowerId)
                {
                    continue;
                }

                long distance = PathModel.DistanceMilli(
                    source.Position,
                    candidate.Position);
                if (distance > radiusMilli ||
                    (selected != null &&
                     (distance > selectedDistance ||
                      (distance == selectedDistance &&
                       candidate.Id.Value >
                       selected.Id.Value))))
                {
                    continue;
                }

                selected = candidate;
                selectedDistance = distance;
            }
            return selected;
        }

        private void RedirectRareProjectileContinuation(
            in EffectExecutionContext context,
            EntityId recipientId)
        {
            TowerState tower = FindTower(context.TowerId);
            if (tower == null)
            {
                return;
            }

            int currentIndex = context.CardIndex;
            if (currentIndex < 0)
            {
                for (int i = 0;
                     i < tower.ProgramInstances.Length;
                     i++)
                {
                    if (tower.ProgramInstances[i] ==
                        context.CardInstanceId)
                    {
                        currentIndex = i;
                        break;
                    }
                }
            }
            ProgramExecutionSpec execution =
                CreateProgramExecution(context);
            int nextIndex = FindNextProgramIndex(
                tower,
                currentIndex,
                SubjectType.Projectile,
                in execution);
            if (nextIndex < 0)
            {
                return;
            }

            EnqueueProgramPass(
                SubjectType.Projectile,
                recipientId,
                context.TowerId,
                nextIndex,
                context.RootChainId,
                context.ActivationId,
                context.ParentEventId,
                context.Depth,
                EventPhase.Projectile,
                in execution);
        }

        private void CollectRareEnemyTargets(
            SimPosition origin,
            int radiusMilli,
            EntityId excludedId,
            int limit)
        {
            rareGenerationMotionEnemyScratch.Clear();
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState enemy = enemies[i];
                if (!enemy.Alive ||
                    enemy.Id == excludedId ||
                    !DoesAreaCircleOverlapEnemyHitbox(
                        origin,
                        radiusMilli,
                        enemy))
                {
                    continue;
                }
                rareGenerationMotionEnemyScratch.Add(enemy);
            }
            rareGenerationMotionEnemyScratch.Sort(
                (left, right) =>
                    CompareTargetPriority(
                        origin,
                        left,
                        right));
            if (rareGenerationMotionEnemyScratch.Count >
                Math.Max(0, limit))
            {
                rareGenerationMotionEnemyScratch.RemoveRange(
                    Math.Max(0, limit),
                    rareGenerationMotionEnemyScratch.Count -
                    Math.Max(0, limit));
            }
        }

        private void CloneRareProjectileInheritedState(
            ProjectileState source,
            ProjectileState target)
        {
            InheritCommonProjectileRuntime(source, target);

            if (commonProjectileRicochets.TryGetValue(
                    source.Id.Value,
                    out List<ProjectileRicochetRuntime> ricochets))
            {
                var copies =
                    new List<ProjectileRicochetRuntime>(
                        ricochets.Count);
                for (int i = 0; i < ricochets.Count; i++)
                {
                    ProjectileRicochetRuntime runtime =
                        ricochets[i];
                    copies.Add(
                        new ProjectileRicochetRuntime
                        {
                            CardId = runtime.CardId,
                            CardInstanceId =
                                runtime.CardInstanceId,
                            Remaining = runtime.Remaining,
                            Used = runtime.Used,
                            DamageMultiplierBps =
                                runtime.DamageMultiplierBps,
                            RadiusMilli =
                                runtime.RadiusMilli
                        });
                }
                commonProjectileRicochets[
                    target.Id.Value] = copies;
            }

            if (uncommonProjectileEffects.TryGetValue(
                    source.Id.Value,
                    out List<UncommonProjectileEffectRuntime> effects))
            {
                var copies =
                    new List<UncommonProjectileEffectRuntime>(
                        effects.Count);
                for (int i = 0; i < effects.Count; i++)
                {
                    copies.Add(
                        effects[i].CloneFor(target.Id));
                }
                uncommonProjectileEffects[
                    target.Id.Value] = copies;
            }

            if (rareProjectileReturns.TryGetValue(
                    source.Id.Value,
                    out List<RareProjectileReturnRuntime> returns))
            {
                var copies =
                    new List<RareProjectileReturnRuntime>(
                        returns.Count);
                for (int i = 0; i < returns.Count; i++)
                {
                    copies.Add(returns[i].Clone());
                }
                rareProjectileReturns[
                    target.Id.Value] = copies;
            }
            if (rareProjectileRetrogrades.TryGetValue(
                    source.Id.Value,
                    out RareProjectileRetrogradeRuntime retrograde))
            {
                rareProjectileRetrogrades[
                    target.Id.Value] =
                        retrograde.Clone();
            }
        }

        private void MergeRareProjectileInheritedState(
            ProjectileState source,
            ProjectileState target)
        {
            if (commonProjectileRicochets.TryGetValue(
                    source.Id.Value,
                    out List<ProjectileRicochetRuntime> sourceRicochets))
            {
                if (!commonProjectileRicochets.TryGetValue(
                        target.Id.Value,
                        out List<ProjectileRicochetRuntime> targetRicochets))
                {
                    targetRicochets =
                        new List<ProjectileRicochetRuntime>(
                            sourceRicochets.Count);
                    commonProjectileRicochets.Add(
                        target.Id.Value,
                        targetRicochets);
                }
                for (int i = 0; i < sourceRicochets.Count; i++)
                {
                    ProjectileRicochetRuntime runtime =
                        sourceRicochets[i];
                    targetRicochets.Add(
                        new ProjectileRicochetRuntime
                        {
                            CardId = runtime.CardId,
                            CardInstanceId =
                                runtime.CardInstanceId,
                            Remaining = runtime.Remaining,
                            Used = runtime.Used,
                            DamageMultiplierBps =
                                runtime.DamageMultiplierBps,
                            RadiusMilli =
                                runtime.RadiusMilli
                        });
                }
            }

            if (commonProjectileAccelerations.TryGetValue(
                    source.Id.Value,
                    out List<ProjectileAccelerationRuntime> sourceAccelerations))
            {
                if (!commonProjectileAccelerations.TryGetValue(
                        target.Id.Value,
                        out List<ProjectileAccelerationRuntime> targetAccelerations))
                {
                    targetAccelerations =
                        new List<ProjectileAccelerationRuntime>(
                            sourceAccelerations.Count);
                    commonProjectileAccelerations.Add(
                        target.Id.Value,
                        targetAccelerations);
                }
                for (int i = 0;
                     i < sourceAccelerations.Count;
                     i++)
                {
                    targetAccelerations.Add(
                        sourceAccelerations[i].Clone());
                }
            }

            if (commonProjectileDelays.TryGetValue(
                    source.Id.Value,
                    out ProjectileDelayRuntime sourceDelay))
            {
                if (!commonProjectileDelays.TryGetValue(
                        target.Id.Value,
                        out ProjectileDelayRuntime targetDelay))
                {
                    targetDelay =
                        new ProjectileDelayRuntime
                        {
                            ReleaseDamageMultiplierBps =
                                10000
                        };
                    commonProjectileDelays.Add(
                        target.Id.Value,
                        targetDelay);
                }
                targetDelay.RemainingTicks = Math.Min(
                    content.Safety.MaxProjectileLifetimeTicks,
                    checked(
                        targetDelay.RemainingTicks +
                        sourceDelay.RemainingTicks));
                targetDelay.ReleaseDamageMultiplierBps =
                    MultiplyBps(
                        targetDelay.ReleaseDamageMultiplierBps,
                        sourceDelay.ReleaseDamageMultiplierBps);
            }

            if (uncommonProjectileEffects.TryGetValue(
                    source.Id.Value,
                    out List<UncommonProjectileEffectRuntime> sourceEffects))
            {
                if (!uncommonProjectileEffects.TryGetValue(
                        target.Id.Value,
                        out List<UncommonProjectileEffectRuntime> targetEffects))
                {
                    targetEffects =
                        new List<UncommonProjectileEffectRuntime>(
                            sourceEffects.Count);
                    uncommonProjectileEffects.Add(
                        target.Id.Value,
                        targetEffects);
                }
                for (int i = 0; i < sourceEffects.Count; i++)
                {
                    targetEffects.Add(
                        sourceEffects[i].CloneFor(target.Id));
                }
            }

            if (rareProjectileReturns.TryGetValue(
                    source.Id.Value,
                    out List<RareProjectileReturnRuntime> sourceReturns))
            {
                if (!rareProjectileReturns.TryGetValue(
                        target.Id.Value,
                        out List<RareProjectileReturnRuntime> targetReturns))
                {
                    targetReturns =
                        new List<RareProjectileReturnRuntime>(
                            sourceReturns.Count);
                    rareProjectileReturns.Add(
                        target.Id.Value,
                        targetReturns);
                }
                for (int i = 0; i < sourceReturns.Count; i++)
                {
                    targetReturns.Add(
                        sourceReturns[i].Clone());
                }
            }
            if (rareProjectileRetrogrades.TryGetValue(
                    source.Id.Value,
                    out RareProjectileRetrogradeRuntime sourceRetrograde) &&
                !rareProjectileRetrogrades.ContainsKey(
                    target.Id.Value))
            {
                rareProjectileRetrogrades.Add(
                    target.Id.Value,
                    sourceRetrograde.Clone());
            }
        }

        private static void AppendSortedRareIdSet(
            ref StableHashBuilder hash,
            HashSet<int> source)
        {
            int[] values = new int[source.Count];
            source.CopyTo(values);
            Array.Sort(values);
            hash.Add(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                hash.Add(values[i]);
            }
        }

        private void AddRareGenerationMotionPresentation(
            string contentId,
            EntityId subjectId,
            EntityId sourceId,
            long value)
        {
            AddPresentation(
                PresentationEventType.EffectTriggered,
                subjectId.Value,
                sourceId.Value,
                (int)Math.Max(
                    int.MinValue,
                    Math.Min(int.MaxValue, value)),
                contentId);
        }
    }
}
