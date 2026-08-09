using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 전설 카드 9종의 프로그램 문법, 지연 실행 상태, 생명주기 훅을 한곳에서
    /// 소유한다. 모든 카드 재실행과 파생 피해는 기존 EventQueue/ChainBudget을
    /// 통과하며, Dictionary 순회가 필요한 경로는 EntityId 순으로 정렬한다.
    /// </summary>
    public sealed partial class GameSimulation
    {
        private const int LegendaryBasisPoints = 10000;

        private const int DefaultDualPowerBps = 10000;
        private const int DefaultDualApplicationLimit = 1;

        private const int DefaultProjectileOrbitPowerBps = 7500;
        private const int DefaultProjectileOrbitDurationTicks = 150;
        private const int DefaultProjectileOrbitIntervalTicks = 10;
        private const int DefaultProjectileOrbitRadiusMilli = 900;
        private const int DefaultProjectileOrbitHitLimit = 6;
        private const int DefaultEnemyOrbitLengthMilli = 1500;
        private const int DefaultEnemyOrbitDurationTicks = 180;
        private const int DefaultEnemyOrbitHitLimit = 4;

        private const int DefaultOverclonePowerBps = 7500;
        private const int DefaultOvercloneGenerationLimit = 2;

        private const int DefaultForbiddenDealGoldCost = 2;
        private const int DefaultForbiddenDealReplayPowerBps = 10000;
        private const int DefaultForbiddenDealReplayLimit = 1;
        private const int DefaultForbiddenDealGoldAmount = 1;
        private const int DefaultForbiddenDealGrowthBps = 10500;
        private const int DefaultForbiddenDealDurationTicks = 180;
        private const int DefaultForbiddenDealIntervalTicks = 30;
        private const int DefaultForbiddenDealPulseLimit = 6;

        private const int DefaultLastCommandPowerBps = 10000;
        private const int DefaultLastCommandRadiusMilli = 2500;
        private const int DefaultProjectileLastCommandLimit = 1;
        private const int DefaultEnemyLastCommandTargetLimit = 8;

        private const int DefaultFateLockPowerBps = 10000;
        private const int DefaultEnemyFateLockPowerBps = 8500;
        private const int DefaultEnemyFateLockDurationTicks = 120;
        private const int DefaultEnemyFateLockCharges = 1;

        private const int DefaultOverloadReplayPowerBps = 10000;
        private const int DefaultOverloadExplosionPowerBps = 7000;
        private const int DefaultOverloadExplosionRadiusMilli = 1500;
        private const int DefaultEnemyOverloadSpeedBps = 12500;
        private const int DefaultEnemyOverloadResistanceBps = 3000;
        private const int DefaultEnemyOverloadDurationTicks = 90;

        private static readonly int[] LegendaryOrbitXBps =
        {
            10000, 7071, 0, -7071,
            -10000, -7071, 0, 7071
        };

        private static readonly int[] LegendaryOrbitYBps =
        {
            0, 7071, 10000, 7071,
            0, -7071, -10000, -7071
        };

        private readonly Dictionary<int, LegendaryProjectileRuntime>
            legendaryProjectileRuntimes =
                new Dictionary<int, LegendaryProjectileRuntime>();

        private readonly Dictionary<int, LegendaryEnemyRuntime>
            legendaryEnemyRuntimes =
                new Dictionary<int, LegendaryEnemyRuntime>();

        private readonly List<LegendaryRecursionRequest>
            legendaryRecursionRequests =
                new List<LegendaryRecursionRequest>();

        private readonly List<int> legendaryKeyScratch =
            new List<int>();

        private readonly List<EnemyState> legendaryEnemyScratch =
            new List<EnemyState>();

        /// <summary>
        /// EffectRegistry의 전설 executor가 호출하는 단일 진입점이다. 즉시 끝나는
        /// 문법 표식과 이후 적중·소멸·사망에 쓰일 런타임 등록을 구분한다.
        /// </summary>
        internal EffectExecutionOutcome ExecuteLegendaryEffect(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node)
        {
            switch (operation)
            {
                case EffectOperation.EnableRecursion:
                    ConfigureLegendaryRecursion(context, node);
                    break;
                case EffectOperation.ReverseProgramOrder:
                    // 방향은 pass 생성 전에 ProgramGrammar가 고정한다. 실행 중
                    // 토글하지 않아 역순 카드 여러 장도 한 번만 뒤집힌다.
                    break;
                case EffectOperation.EnableProjectileDualInterpretation:
                    ConfigureProjectileDualInterpretation(context, node);
                    break;
                case EffectOperation.ApplyEnemyDualInterpretation:
                    ApplyEnemyDualInterpretation(context, node);
                    break;
                case EffectOperation.EnableProjectileInfiniteOrbit:
                    ConfigureProjectileInfiniteOrbit(context, node);
                    break;
                case EffectOperation.ApplyEnemyInfiniteOrbit:
                    ApplyEnemyInfiniteOrbit(context, node);
                    break;
                case EffectOperation.EnableProjectileOverclone:
                    ConfigureProjectileOverclone(context, node);
                    break;
                case EffectOperation.ApplyEnemyOverclone:
                    ApplyEnemyOverclone(context, node);
                    break;
                case EffectOperation.EnableProjectileForbiddenDeal:
                    ConfigureProjectileForbiddenDeal(context, node);
                    break;
                case EffectOperation.ApplyEnemyForbiddenDeal:
                    ApplyEnemyForbiddenDeal(context, node);
                    break;
                case EffectOperation.EnableProjectileLastCommand:
                    ConfigureProjectileLastCommand(context, node);
                    break;
                case EffectOperation.ApplyEnemyLastCommand:
                    ApplyEnemyLastCommand(context, node);
                    break;
                case EffectOperation.EnableProjectileFateLock:
                    ConfigureProjectileFateLock(context, node);
                    break;
                case EffectOperation.ApplyEnemyFateLock:
                    ApplyEnemyFateLock(context, node);
                    break;
                case EffectOperation.EnableProjectileOverload:
                case EffectOperation.ApplyEnemyOverload:
                    ConfigureLegendaryOverload(context, operation, node);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(operation),
                        operation,
                        "Unsupported Legendary operation.");
            }

            return EffectExecutionOutcome.Continue();
        }

        /// <summary>
        /// 한 프로그램 pass가 끝난 뒤 재귀와 과부하의 후속 pass를 큐에 넣고,
        /// 마지막 명령·과부하 replay의 완료 동작을 확정한다.
        /// </summary>
        internal void HandleLegendaryProgramCompleted(
            SubjectType subjectType,
            EntityId subjectId,
            TowerId towerId,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            in ProgramExecutionSpec execution)
        {
            if (execution.HasFlag(EffectExecutionFlags.LastCommand))
            {
                ProjectileState lastProjectile =
                    subjectType == SubjectType.Projectile
                        ? FindProjectile(subjectId)
                        : null;
                if (lastProjectile != null &&
                    lastProjectile.Alive)
                {
                    ScheduleProjectileExpiration(
                        lastProjectile,
                        parentEventId);
                }
                return;
            }

            if (TryCompleteLegendaryOverloadReplay(
                    subjectType,
                    subjectId,
                    towerId,
                    rootChainId,
                    activationId,
                    parentEventId,
                    depth,
                    in execution))
            {
                return;
            }

            if (!execution.HasFlag(
                    EffectExecutionFlags.SuppressRecursion))
            {
                TryScheduleLegendaryRecursion(
                    subjectType,
                    subjectId,
                    towerId,
                    rootChainId,
                    activationId,
                    parentEventId,
                    depth,
                    in execution);
            }

            if (!execution.HasFlag(
                    EffectExecutionFlags.SuppressOverload))
            {
                TryScheduleLegendaryOverload(
                    subjectType,
                    subjectId,
                    towerId,
                    rootChainId,
                    activationId,
                    parentEventId,
                    depth,
                    in execution);
            }
        }

        /// <summary>
        /// 재귀 카드는 현재 pass가 끝났을 때 같은 방향의 첫 실제 행동 카드만
        /// 한 번 실행한다. RootChain의 recursionCount와 카드 이벤트를 함께 예약한다.
        /// </summary>
        private void ConfigureLegendaryRecursion(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            if (context.HasExecutionFlag(
                    EffectExecutionFlags.SuppressRecursion))
            {
                return;
            }

            int limit = node.Limit > 0
                ? Math.Min(1, node.Limit)
                : 1;
            if (limit <= 0)
            {
                return;
            }

            for (int i = 0;
                 i < legendaryRecursionRequests.Count;
                 i++)
            {
                LegendaryRecursionRequest existing =
                    legendaryRecursionRequests[i];
                if (!existing.Consumed &&
                    existing.RootChainId == context.RootChainId &&
                    existing.SubjectType == context.SubjectType &&
                    existing.SubjectId == context.SubjectId &&
                    existing.TowerId == context.TowerId &&
                    existing.CardInstanceId ==
                        context.CardInstanceId)
                {
                    return;
                }
            }

            legendaryRecursionRequests.Add(
                new LegendaryRecursionRequest
                {
                    RootChainId = context.RootChainId,
                    SubjectType = context.SubjectType,
                    SubjectId = context.SubjectId,
                    TowerId = context.TowerId,
                    CardId = context.CardId,
                    CardInstanceId = context.CardInstanceId,
                    Direction = context.TraversalDirection,
                    PowerBps = context.PowerBps
                });
        }

        private void TryScheduleLegendaryRecursion(
            SubjectType subjectType,
            EntityId subjectId,
            TowerId towerId,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            in ProgramExecutionSpec execution)
        {
            LegendaryRecursionRequest selected = null;
            for (int i = 0;
                 i < legendaryRecursionRequests.Count;
                 i++)
            {
                LegendaryRecursionRequest request =
                    legendaryRecursionRequests[i];
                if (!request.Consumed &&
                    request.RootChainId == rootChainId &&
                    request.SubjectType == subjectType &&
                    request.SubjectId == subjectId &&
                    request.TowerId == towerId)
                {
                    selected = request;
                    break;
                }
            }

            if (selected == null)
            {
                return;
            }

            selected.Consumed = true;
            TowerState tower = FindTower(towerId);
            int cardIndex = FindFirstLegendaryActionIndex(
                tower,
                subjectType,
                selected.Direction);
            if (cardIndex < 0)
            {
                return;
            }

            var recursionExecution =
                new ProgramExecutionSpec(
                    selected.Direction,
                    selected.PowerBps,
                    checked(execution.RepeatIndex + 1),
                    execution.Flags |
                    EffectExecutionFlags.Repeated |
                    EffectExecutionFlags.SingleCard |
                    EffectExecutionFlags.SuppressRecursion);
            TryScheduleLegendaryProgramPass(
                subjectType,
                subjectId,
                towerId,
                cardIndex,
                rootChainId,
                activationId,
                parentEventId,
                checked(depth + 1),
                EventPhase.Scheduled,
                in recursionExecution,
                recursionCount: 1);
        }

        private void ConfigureProjectileDualInterpretation(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            TowerState tower = FindTower(context.TowerId);
            if (projectile == null ||
                !projectile.Alive ||
                tower == null)
            {
                return;
            }

            int nextIndex = FindNextLegendaryActionIndex(
                tower,
                context.CardIndex,
                SubjectType.Projectile,
                context.TraversalDirection);
            if (nextIndex < 0)
            {
                return;
            }

            LegendaryProjectileRuntime runtime =
                GetOrCreateLegendaryProjectileRuntime(
                    projectile.Id);
            LegendaryDualRuntime dual =
                FindDualRuntime(
                    runtime.Duals,
                    context.CardInstanceId);
            if (dual == null)
            {
                dual = new LegendaryDualRuntime();
                runtime.Duals.Add(dual);
            }

            dual.TowerId = context.TowerId;
            dual.CardId = context.CardId;
            dual.CardInstanceId = context.CardInstanceId;
            dual.TargetCardIndex = nextIndex;
            dual.Direction = context.TraversalDirection;
            dual.PowerBps = MultiplyLegendaryPower(
                context.PowerBps,
                node.Amount > 0
                    ? node.Amount
                    : DefaultDualPowerBps);
            dual.RemainingApplications =
                node.Limit > 0
                    ? node.Limit
                    : DefaultDualApplicationLimit;
            dual.RootChainId = context.RootChainId;
            dual.ActivationId = context.ActivationId;
            dual.AppliedCardIndex = context.CardIndex;
        }

        private void ApplyEnemyDualInterpretation(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            TowerState tower = FindTower(context.TowerId);
            if (enemy == null ||
                !enemy.Alive ||
                tower == null)
            {
                return;
            }

            int nextIndex = FindNextLegendaryActionIndex(
                tower,
                context.CardIndex,
                SubjectType.Enemy,
                context.TraversalDirection);
            if (nextIndex < 0)
            {
                return;
            }

            SpawnLegendaryDualGhost(
                enemy,
                tower,
                nextIndex,
                context,
                node);
        }

        /// <summary>
        /// 탄환 적중 직후 호출한다. 이중 해석의 적 pass, 금단 거래 replay,
        /// 무한 궤도의 생존 인수를 고정된 순서로 처리한다.
        /// </summary>
        internal bool HandleLegendaryProjectileHit(
            ProjectileState projectile,
            EnemyState target,
            in GameEvent hitEvent)
        {
            if (projectile == null ||
                target == null ||
                !legendaryProjectileRuntimes.TryGetValue(
                    projectile.Id.Value,
                    out LegendaryProjectileRuntime runtime))
            {
                return false;
            }

            ProcessProjectileDualInterpretation(
                projectile,
                target,
                hitEvent,
                runtime);

            if (ProcessProjectileForbiddenDeals(
                    projectile,
                    hitEvent,
                    runtime))
            {
                return true;
            }

            if (runtime.Orbit != null)
            {
                LegendaryProjectileOrbitRuntime orbit =
                    runtime.Orbit;
                if (!orbit.Active)
                {
                    orbit.Active = true;
                    orbit.TargetId = target.Id;
                    orbit.HitsApplied = 1;
                    orbit.NextHitTick =
                        tick + orbit.IntervalTicks;
                    orbit.RemainingTicks = Math.Max(
                        1,
                        orbit.RemainingTicks);
                    projectile.ExpirationQueued = false;
                    projectile.LifetimeRemaining = Math.Max(
                        projectile.LifetimeRemaining,
                        checked(orbit.RemainingTicks + 1));
                    return true;
                }

                orbit.TargetId = target.Id;
                orbit.HitsApplied++;
                if (orbit.HitsApplied >= orbit.HitLimit ||
                    orbit.RemainingTicks <= 0)
                {
                    orbit.Active = false;
                    ScheduleProjectileExpiration(
                        projectile,
                        hitEvent.EventId);
                    return true;
                }

                projectile.DamageMilli =
                    DeterministicMath.MultiplyBasisPoints(
                        projectile.DamageMilli,
                        orbit.RepeatPowerBps);
                orbit.NextHitTick =
                    tick + orbit.IntervalTicks;
                projectile.ExpirationQueued = false;
                return true;
            }

            return false;
        }

        /// <summary>
        /// MoveProjectiles의 일반 이동 전에 호출한다. 활성 궤도 탄환의 위치와
        /// 주기 적중 이벤트를 정수 좌표로 갱신하고 일반 직선 이동을 소비한다.
        /// </summary>
        internal bool ProcessLegendaryProjectileTick(
            ProjectileState projectile)
        {
            if (projectile == null ||
                !projectile.Alive ||
                !legendaryProjectileRuntimes.TryGetValue(
                    projectile.Id.Value,
                    out LegendaryProjectileRuntime runtime) ||
                runtime.Orbit == null ||
                !runtime.Orbit.Active)
            {
                return false;
            }

            LegendaryProjectileOrbitRuntime orbit =
                runtime.Orbit;
            orbit.RemainingTicks--;
            if (orbit.RemainingTicks <= 0 ||
                orbit.HitsApplied >= orbit.HitLimit)
            {
                orbit.Active = false;
                ScheduleProjectileExpiration(
                    projectile,
                    EventId.Invalid);
                return true;
            }

            EnemyState target = FindEnemy(orbit.TargetId);
            if (target == null ||
                !target.Alive ||
                target.DeathQueued)
            {
                target = SelectProjectileTarget(projectile);
                if (target == null)
                {
                    orbit.Active = false;
                    ScheduleProjectileExpiration(
                        projectile,
                        EventId.Invalid);
                    return true;
                }
                orbit.TargetId = target.Id;
            }

            orbit.PhaseIndex =
                (orbit.PhaseIndex + 1) %
                LegendaryOrbitXBps.Length;
            long offsetX = DeterministicMath.MultiplyDivide(
                LegendaryOrbitXBps[orbit.PhaseIndex],
                orbit.RadiusMilli,
                LegendaryBasisPoints);
            long offsetY = DeterministicMath.MultiplyDivide(
                LegendaryOrbitYBps[orbit.PhaseIndex],
                orbit.RadiusMilli,
                LegendaryBasisPoints);
            projectile.Position = SimPosition.FromMilliUnits(
                target.Position.X.MilliUnits + offsetX,
                target.Position.Y.MilliUnits + offsetY);
            projectile.TargetId = target.Id;

            AddPresentation(
                PresentationEventType.ProjectileMoved,
                projectile.Id.Value,
                projectile.SourceTowerId.Value,
                orbit.RadiusMilli,
                "infinite_orbit");

            if (tick >= orbit.NextHitTick)
            {
                projectile.HitEnemies.Remove(
                    target.Id.Value);
                var hit = new GameEvent(
                    tick,
                    EventPhase.Projectile,
                    EventType.ProjectileHit,
                    projectile.RootChainId,
                    EventId.Invalid,
                    projectile.ActivationId,
                    projectile.SourceTowerId,
                    orbit.CardId,
                    projectile.Id,
                    target.Id,
                    SubjectType.Enemy,
                    0,
                    projectile.Generation,
                    EventTags.Projectile |
                    EventTags.SingleTarget |
                    EventTags.Repeated,
                    RewardOrigin.EnemyDrop);
                if (TryEnqueue(in hit, out _))
                {
                    orbit.NextHitTick =
                        tick + orbit.IntervalTicks;
                }
            }

            return true;
        }

        private void ConfigureProjectileInfiniteOrbit(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            LegendaryProjectileRuntime runtime =
                GetOrCreateLegendaryProjectileRuntime(
                    projectile.Id);
            runtime.Orbit =
                new LegendaryProjectileOrbitRuntime
                {
                    TowerId = context.TowerId,
                    CardId = context.CardId,
                    CardInstanceId = context.CardInstanceId,
                    AppliedCardIndex = context.CardIndex,
                    Direction = context.TraversalDirection,
                    RepeatPowerBps =
                        MultiplyLegendaryPower(
                            context.PowerBps,
                            node.Amount > 0
                                ? node.Amount
                                : DefaultProjectileOrbitPowerBps),
                    RemainingTicks =
                        node.DurationTicks > 0
                            ? node.DurationTicks
                            : DefaultProjectileOrbitDurationTicks,
                    IntervalTicks =
                        node.IntervalTicks > 0
                            ? node.IntervalTicks
                            : DefaultProjectileOrbitIntervalTicks,
                    RadiusMilli =
                        node.RadiusMilli > 0
                            ? node.RadiusMilli
                            : DefaultProjectileOrbitRadiusMilli,
                    HitLimit =
                        node.Limit > 0
                            ? node.Limit
                            : DefaultProjectileOrbitHitLimit
                };
            projectile.LifetimeRemaining = Math.Max(
                projectile.LifetimeRemaining,
                checked(runtime.Orbit.RemainingTicks + 1));
        }

        private void ApplyEnemyInfiniteOrbit(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            LegendaryEnemyRuntime runtime =
                GetOrCreateLegendaryEnemyRuntime(enemy.Id);
            long loopLength = Math.Max(
                1,
                MultiplyLegendaryPower(
                    context.PowerBps,
                    node.Amount > 0
                        ? node.Amount
                        : DefaultEnemyOrbitLengthMilli));
            runtime.Orbit =
                new LegendaryEnemyOrbitRuntime
                {
                    TowerId = context.TowerId,
                    CardId = context.CardId,
                    CardInstanceId = context.CardInstanceId,
                    AppliedCardIndex = context.CardIndex,
                    Direction = context.TraversalDirection,
                    AnchorProgressMilli =
                        enemy.PathProgressMilli,
                    LoopLengthMilli = Math.Min(
                        loopLength,
                        Math.Max(
                            1,
                            path.TotalLengthMilli -
                            enemy.PathProgressMilli)),
                    RemainingTicks =
                        node.DurationTicks > 0
                            ? node.DurationTicks
                            : DefaultEnemyOrbitDurationTicks,
                    HitLimit =
                        node.Limit > 0
                            ? node.Limit
                            : DefaultEnemyOrbitHitLimit
                };
        }

        /// <summary>
        /// 적 이동 훅. 무한 궤도가 활성인 동안 경로의 짧은 구간을 삼각파처럼
        /// 왕복하며 일반 이동을 대신한다.
        /// </summary>
        internal bool TryProcessLegendaryEnemyMovement(
            EnemyState enemy)
        {
            if (enemy == null ||
                !enemy.Alive ||
                !legendaryEnemyRuntimes.TryGetValue(
                    enemy.Id.Value,
                    out LegendaryEnemyRuntime runtime) ||
                runtime.Orbit == null ||
                runtime.Orbit.RemainingTicks <= 0 ||
                runtime.Orbit.HitsTaken >=
                    runtime.Orbit.HitLimit)
            {
                return false;
            }

            LegendaryEnemyOrbitRuntime orbit =
                runtime.Orbit;
            int slowBps = GetSlowBps(enemy);
            int movementBps = (int)
                DeterministicMath.MultiplyBasisPoints(
                    enemy.SpeedMultiplierBps,
                    LegendaryBasisPoints - slowBps);
            int distance = (int)
                DeterministicMath.MultiplyBasisPoints(
                    enemy.BaseSpeedMilliPerTick,
                    movementBps);
            if (distance <= 0)
            {
                return true;
            }

            long period = checked(
                orbit.LoopLengthMilli * 2L);
            orbit.TravelMilli =
                (orbit.TravelMilli + distance) %
                period;
            long offset =
                orbit.TravelMilli <= orbit.LoopLengthMilli
                    ? orbit.TravelMilli
                    : period - orbit.TravelMilli;
            enemy.PathProgressMilli = Math.Max(
                0,
                Math.Min(
                    path.TotalLengthMilli,
                    orbit.AnchorProgressMilli + offset));
            RefreshEnemyPosition(enemy);
            TriggerBleedFromMovement(enemy, distance);
            AddPresentation(
                PresentationEventType.EnemyMoved,
                enemy.Id.Value,
                -1,
                distance,
                "infinite_orbit");
            return true;
        }

        /// <summary>
        /// 최종 피해 적용 뒤 호출한다. 무한 궤도의 피격 종료 조건만 세며,
        /// 피해나 사망을 직접 재귀 처리하지 않는다.
        /// </summary>
        internal void HandleLegendaryEnemyDamaged(
            EnemyState enemy,
            in GameEvent damageEvent,
            long appliedDamage)
        {
            if (enemy == null ||
                appliedDamage <= 0 ||
                !legendaryEnemyRuntimes.TryGetValue(
                    enemy.Id.Value,
                    out LegendaryEnemyRuntime runtime) ||
                runtime.Orbit == null)
            {
                return;
            }

            runtime.Orbit.HitsTaken++;
            if (runtime.Orbit.HitsTaken >=
                runtime.Orbit.HitLimit)
            {
                runtime.Orbit.RemainingTicks = 0;
            }
        }

        internal void HandleLegendaryEnemyDamaged(
            EnemyState enemy,
            in GameEvent damageEvent)
        {
            HandleLegendaryEnemyDamaged(
                enemy,
                in damageEvent,
                Math.Max(0, damageEvent.PayloadValue));
        }

        private void ConfigureProjectileOverclone(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            LegendaryProjectileRuntime runtime =
                GetOrCreateLegendaryProjectileRuntime(
                    projectile.Id);
            runtime.Overclone =
                CreateOvercloneRuntime(context, node);
        }

        private void ApplyEnemyOverclone(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            LegendaryEnemyRuntime runtime =
                GetOrCreateLegendaryEnemyRuntime(enemy.Id);
            runtime.Overclone =
                CreateOvercloneRuntime(context, node);
        }

        /// <summary>
        /// Split/Duplicate가 자식을 실제 목록에 넣은 직후 호출한다. 과잉 복제가
        /// 활성인 경우에만 이미 실행된 바인딩과 런타임을 독립 복사한다.
        /// </summary>
        internal void InheritLegendaryProjectileState(
            ProjectileState source,
            ProjectileState target)
        {
            if (source == null ||
                target == null ||
                !legendaryProjectileRuntimes.TryGetValue(
                    source.Id.Value,
                    out LegendaryProjectileRuntime sourceRuntime) ||
                sourceRuntime.Overclone == null ||
                sourceRuntime.Overclone.RemainingGenerations <= 0)
            {
                return;
            }

            LegendaryOvercloneRuntime rule =
                sourceRuntime.Overclone;
            CopyLegendaryProjectileBindings(
                source,
                target,
                rule);

            // 공용·고급·희귀 런타임은 각 생성 경로가 자신의 복제 정책에 따라
            // 먼저 한 번만 상속한다. 이 계층은 전설 전용 상태와 과잉 복제가
            // 허용한 바인딩만 담당해 동일 child ID에 중복 등록하지 않는다.
            LegendaryProjectileRuntime inherited =
                CloneLegendaryProjectileRuntime(
                    sourceRuntime,
                    rule);
            legendaryProjectileRuntimes[target.Id.Value] =
                inherited;
        }

        /// <summary>
        /// 환생·불사조처럼 소멸한 탄환을 새 EntityId로 교체할 때 호출한다.
        /// 일반 복제와 달리 과잉 복제를 요구하지 않으며, "진짜 최종 소멸"까지
        /// 따라가야 하는 미사용 마지막 명령 권한만 새 탄환으로 이전한다.
        /// 원본 권한은 동시에 소비해 두 개체에서 재발동하지 않게 한다.
        /// </summary>
        internal void TransferLegendaryProjectileLifecycleState(
            ProjectileState source,
            ProjectileState target)
        {
            if (source == null ||
                target == null ||
                !legendaryProjectileRuntimes.TryGetValue(
                    source.Id.Value,
                    out LegendaryProjectileRuntime sourceRuntime))
            {
                return;
            }

            LegendaryProjectileRuntime targetRuntime = null;
            for (int i = 0;
                 i < sourceRuntime.LastCommands.Count;
                 i++)
            {
                LegendaryLastCommandRuntime command =
                    sourceRuntime.LastCommands[i];
                if (command.Used ||
                    command.RemainingUses <= 0)
                {
                    continue;
                }

                if (targetRuntime == null)
                {
                    targetRuntime =
                        GetOrCreateLegendaryProjectileRuntime(
                            target.Id);
                }
                LegendaryLastCommandRuntime existing =
                    FindLastCommand(
                        targetRuntime.LastCommands,
                        command.CardInstanceId);
                if (existing == null)
                {
                    targetRuntime.LastCommands.Add(
                        command.Clone());
                }
                else
                {
                    existing.TowerId = command.TowerId;
                    existing.CardId = command.CardId;
                    existing.AppliedCardIndex =
                        command.AppliedCardIndex;
                    existing.Direction = command.Direction;
                    existing.PowerBps = command.PowerBps;
                    existing.RadiusMilli =
                        command.RadiusMilli;
                    existing.TargetLimit =
                        command.TargetLimit;
                    existing.RemainingUses =
                        command.RemainingUses;
                    existing.Used = false;
                }

                command.RemainingUses = 0;
                command.Used = true;
            }
        }

        /// <summary>
        /// 적 Split/Duplicate 직후 호출한다. 기존 상태 복사본의 세기와 지속시간을
        /// 제한 수만큼 감쇠하고, 누락된 사망 바인딩을 독립 복사한다.
        /// </summary>
        internal void InheritLegendaryEnemyState(
            EnemyState source,
            EnemyState target)
        {
            if (source == null ||
                target == null ||
                !legendaryEnemyRuntimes.TryGetValue(
                    source.Id.Value,
                    out LegendaryEnemyRuntime sourceRuntime) ||
                sourceRuntime.Overclone == null ||
                sourceRuntime.Overclone.RemainingGenerations <= 0)
            {
                return;
            }

            LegendaryOvercloneRuntime rule =
                sourceRuntime.Overclone;
            target.Statuses.Clear();
            int affected = 0;
            for (int i = 0;
                 i < source.Statuses.Count &&
                 affected < rule.InheritedEffectLimit;
                 i++)
            {
                StatusInstance sourceStatus =
                    source.Statuses[i];
                target.Statuses.Add(
                    CloneLegendaryInheritedStatus(
                        sourceStatus,
                        rule.PowerBps));
                affected++;
            }

            CopyLegendaryEnemyDeathBindings(
                source,
                target,
                rule);
            legendaryEnemyRuntimes[target.Id.Value] =
                CloneLegendaryEnemyRuntime(
                    sourceRuntime,
                    rule);
        }

        private void ConfigureProjectileForbiddenDeal(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            if (context.HasExecutionFlag(
                    EffectExecutionFlags.Repeated))
            {
                return;
            }

            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            LegendaryProjectileRuntime runtime =
                GetOrCreateLegendaryProjectileRuntime(
                    projectile.Id);
            LegendaryProjectileDealRuntime deal =
                FindProjectileDeal(
                    runtime.Deals,
                    context.CardInstanceId);
            if (deal == null)
            {
                deal = new LegendaryProjectileDealRuntime();
                runtime.Deals.Add(deal);
            }

            deal.TowerId = context.TowerId;
            deal.CardId = context.CardId;
            deal.CardInstanceId = context.CardInstanceId;
            deal.RootChainId = context.RootChainId;
            deal.ActivationId = context.ActivationId;
            deal.AppliedCardIndex = context.CardIndex;
            deal.Direction = context.TraversalDirection;
            deal.GoldCost =
                node.Amount > 0
                    ? node.Amount
                    : DefaultForbiddenDealGoldCost;
            deal.ReplayPowerBps =
                node.Amount2 > 0
                    ? node.Amount2
                    : MultiplyLegendaryPower(
                        context.PowerBps,
                        DefaultForbiddenDealReplayPowerBps);
            deal.RemainingReplays =
                node.Limit > 0
                    ? node.Limit
                    : DefaultForbiddenDealReplayLimit;
        }

        private void ApplyEnemyForbiddenDeal(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            if (context.HasExecutionFlag(
                    EffectExecutionFlags.Repeated))
            {
                return;
            }

            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            LegendaryEnemyRuntime runtime =
                GetOrCreateLegendaryEnemyRuntime(enemy.Id);
            LegendaryEnemyDealRuntime deal =
                FindEnemyDeal(
                    runtime.Deals,
                    context.CardInstanceId);
            if (deal == null)
            {
                deal = new LegendaryEnemyDealRuntime();
                runtime.Deals.Add(deal);
            }

            deal.TowerId = context.TowerId;
            deal.CardId = context.CardId;
            deal.CardInstanceId = context.CardInstanceId;
            deal.SourceEntityId = enemy.Id;
            deal.AppliedCardIndex = context.CardIndex;
            deal.Direction = context.TraversalDirection;
            deal.GoldAmount =
                node.Amount > 0
                    ? node.Amount
                    : DefaultForbiddenDealGoldAmount;
            deal.HealthAndSizeGrowthBps =
                node.Amount2 >= LegendaryBasisPoints
                    ? node.Amount2
                    : ScaleLegendaryMultiplierBonus(
                        DefaultForbiddenDealGrowthBps,
                        context.PowerBps);
            deal.SpeedGrowthBps =
                node.Amount3 >= LegendaryBasisPoints
                    ? node.Amount3
                    : ScaleLegendaryMultiplierBonus(
                        DefaultForbiddenDealGrowthBps,
                        context.PowerBps);
            deal.RemainingTicks =
                node.DurationTicks > 0
                    ? node.DurationTicks
                    : DefaultForbiddenDealDurationTicks;
            deal.IntervalTicks =
                node.IntervalTicks > 0
                    ? node.IntervalTicks
                    : DefaultForbiddenDealIntervalTicks;
            deal.PulseLimit =
                node.Limit > 0
                    ? node.Limit
                    : DefaultForbiddenDealPulseLimit;
            deal.NextPulseTick =
                tick + deal.IntervalTicks;
            deal.PulsesApplied = 0;
        }

        private void ConfigureProjectileLastCommand(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            if (context.HasExecutionFlag(
                    EffectExecutionFlags.Repeated))
            {
                return;
            }

            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            LegendaryProjectileRuntime runtime =
                GetOrCreateLegendaryProjectileRuntime(
                    projectile.Id);
            LegendaryLastCommandRuntime command =
                FindLastCommand(
                    runtime.LastCommands,
                    context.CardInstanceId);
            if (command == null)
            {
                command = new LegendaryLastCommandRuntime();
                runtime.LastCommands.Add(command);
            }

            PopulateLastCommand(
                command,
                context,
                node,
                DefaultProjectileLastCommandLimit);
        }

        private void ApplyEnemyLastCommand(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            if (context.HasExecutionFlag(
                    EffectExecutionFlags.Repeated))
            {
                return;
            }

            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            LegendaryEnemyRuntime runtime =
                GetOrCreateLegendaryEnemyRuntime(enemy.Id);
            LegendaryLastCommandRuntime command =
                FindLastCommand(
                    runtime.LastCommands,
                    context.CardInstanceId);
            if (command == null)
            {
                command = new LegendaryLastCommandRuntime();
                runtime.LastCommands.Add(command);
            }

            PopulateLastCommand(
                command,
                context,
                node,
                DefaultEnemyLastCommandTargetLimit);
        }

        /// <summary>
        /// 환원/환생처럼 소멸을 취소하는 훅이 모두 끝난 뒤 호출한다. 마지막 명령이
        /// 있으면 원본 탄환을 pass 완료까지 살려 두고 역순 replay를 예약한다.
        /// </summary>
        internal bool HandleLegendaryProjectileExpired(
            ProjectileState projectile,
            in GameEvent expirationEvent)
        {
            if (projectile == null ||
                !projectile.Alive ||
                !legendaryProjectileRuntimes.TryGetValue(
                    projectile.Id.Value,
                    out LegendaryProjectileRuntime runtime))
            {
                return false;
            }

            for (int i = 0;
                 i < runtime.LastCommands.Count;
                 i++)
            {
                LegendaryLastCommandRuntime command =
                    runtime.LastCommands[i];
                if (command.Used ||
                    command.RemainingUses <= 0)
                {
                    continue;
                }

                TowerState tower = FindTower(command.TowerId);
                var execution =
                    new ProgramExecutionSpec(
                        -1,
                        command.PowerBps,
                        1,
                        EffectExecutionFlags.Repeated |
                        EffectExecutionFlags.LastCommand |
                        EffectExecutionFlags.SuppressRecursion |
                        EffectExecutionFlags.SuppressOverload);
                int entryIndex = FindProgramEntryIndex(
                    tower,
                    SubjectType.Projectile,
                    in execution);
                command.Used = true;
                command.RemainingUses--;
                projectile.ExpirationQueued = false;
                if (entryIndex >= 0 &&
                    TryScheduleLegendaryProgramPass(
                        SubjectType.Projectile,
                        projectile.Id,
                        command.TowerId,
                        entryIndex,
                        expirationEvent.RootChainId,
                        expirationEvent.ActivationId,
                        expirationEvent.EventId,
                        checked(expirationEvent.Depth + 1),
                        EventPhase.Projectile,
                        in execution))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 환생이 사망을 취소하지 않은 최종 사망 지점에서 호출한다. 주변 적을
        /// 결정적 우선순위로 골라 공유 scheduler를 통해 역순 pass를 예약한다.
        /// </summary>
        internal void HandleLegendaryEnemyDeath(
            EnemyState enemy,
            in GameEvent deathEvent)
        {
            if (enemy == null ||
                !legendaryEnemyRuntimes.TryGetValue(
                    enemy.Id.Value,
                    out LegendaryEnemyRuntime runtime))
            {
                return;
            }

            for (int commandIndex = 0;
                 commandIndex < runtime.LastCommands.Count;
                 commandIndex++)
            {
                LegendaryLastCommandRuntime command =
                    runtime.LastCommands[commandIndex];
                if (command.Used ||
                    command.RemainingUses <= 0)
                {
                    continue;
                }

                command.Used = true;
                command.RemainingUses--;
                SelectLegendaryDeathTargets(
                    enemy,
                    command.RadiusMilli,
                    command.TargetLimit);
                if (legendaryEnemyScratch.Count == 0)
                {
                    continue;
                }

                AddUncommonAreaPresentation(
                    "legendary_last_command",
                    enemy.Id,
                    enemy.Id,
                    command.RadiusMilli,
                    GetEnemyHitboxCenter(enemy));

                TowerState tower = FindTower(command.TowerId);
                var execution =
                    new ProgramExecutionSpec(
                        -1,
                        command.PowerBps,
                        1,
                        EffectExecutionFlags.Repeated |
                        EffectExecutionFlags.LastCommand |
                        EffectExecutionFlags.SuppressRecursion |
                        EffectExecutionFlags.SuppressOverload);
                int entryIndex = FindProgramEntryIndex(
                    tower,
                    SubjectType.Enemy,
                    in execution);
                if (entryIndex < 0)
                {
                    continue;
                }

                TryScheduleLegendaryEnemyPasses(
                    legendaryEnemyScratch,
                    command.TowerId,
                    entryIndex,
                    deathEvent.RootChainId,
                    deathEvent.ActivationId,
                    deathEvent.EventId,
                    checked(deathEvent.Depth + 1),
                    EventPhase.Death,
                    in execution);
            }
        }

        private void ConfigureProjectileFateLock(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            LegendaryProjectileRuntime runtime =
                GetOrCreateLegendaryProjectileRuntime(
                    projectile.Id);
            runtime.FateLock =
                new LegendaryFateLockRuntime
                {
                    TowerId = context.TowerId,
                    CardId = context.CardId,
                    CardInstanceId = context.CardInstanceId,
                    AppliedCardIndex = context.CardIndex,
                    Direction = context.TraversalDirection,
                    PowerBps =
                        MultiplyLegendaryPower(
                            context.PowerBps,
                            node.Amount > 0
                                ? node.Amount
                                : DefaultFateLockPowerBps)
                };
        }

        private void ApplyEnemyFateLock(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            LegendaryEnemyRuntime runtime =
                GetOrCreateLegendaryEnemyRuntime(enemy.Id);
            runtime.FateLock =
                new LegendaryFateLockRuntime
                {
                    TowerId = context.TowerId,
                    CardId = context.CardId,
                    CardInstanceId = context.CardInstanceId,
                    AppliedCardIndex = context.CardIndex,
                    Direction = context.TraversalDirection,
                    PowerBps =
                        MultiplyLegendaryPower(
                            context.PowerBps,
                            node.Amount > 0
                                ? node.Amount
                                : DefaultEnemyFateLockPowerBps),
                    RemainingTicks =
                        node.DurationTicks > 0
                            ? node.DurationTicks
                            : DefaultEnemyFateLockDurationTicks,
                    Charges =
                        node.MaxStacks > 0
                            ? node.MaxStacks
                            : node.Limit > 0
                                ? node.Limit
                                : DefaultEnemyFateLockCharges
                };
        }

        /// <summary>
        /// 치명타 난수를 소비하기 전에 호출한다. true이면 전설 정책이 결과를
        /// 확정했으므로 combatRandom을 소비하지 않는다.
        /// </summary>
        internal bool ResolveLegendaryCritical(
            ProjectileState projectile,
            EnemyState target,
            int chanceBps,
            out bool critical,
            out int effectPowerBps)
        {
            critical = false;
            effectPowerBps = LegendaryBasisPoints;
            if (chanceBps <= 0)
            {
                return false;
            }

            if (target != null &&
                legendaryEnemyRuntimes.TryGetValue(
                    target.Id.Value,
                    out LegendaryEnemyRuntime enemyRuntime) &&
                enemyRuntime.FateLock != null &&
                enemyRuntime.FateLock.Charges > 0 &&
                enemyRuntime.FateLock.RemainingTicks > 0)
            {
                enemyRuntime.FateLock.Charges--;
                critical = true;
                effectPowerBps =
                    enemyRuntime.FateLock.PowerBps;
                return true;
            }

            if (projectile == null ||
                !legendaryProjectileRuntimes.TryGetValue(
                    projectile.Id.Value,
                    out LegendaryProjectileRuntime projectileRuntime) ||
                projectileRuntime.FateLock == null)
            {
                return false;
            }

            LegendaryFateLockRuntime fate =
                projectileRuntime.FateLock;
            int adjustedChance = (int)Math.Max(
                1,
                Math.Min(
                    LegendaryBasisPoints,
                    DeterministicMath.MultiplyBasisPoints(
                        chanceBps,
                        fate.PowerBps)));
            fate.AccumulatorBps = checked(
                fate.AccumulatorBps +
                adjustedChance);
            critical =
                fate.AccumulatorBps >=
                LegendaryBasisPoints;
            if (critical)
            {
                fate.AccumulatorBps -=
                    LegendaryBasisPoints;
            }
            return true;
        }

        internal bool ResolveLegendaryCritical(
            ProjectileState projectile,
            EnemyState target,
            out bool critical,
            out int effectPowerBps)
        {
            return ResolveLegendaryCritical(
                projectile,
                target,
                projectile == null
                    ? 0
                    : projectile.CriticalChanceBps,
                out critical,
                out effectPowerBps);
        }

        /// <summary>
        /// 적 운명 고정의 위력 감쇠는 기본 피해가 아니라 확률 효과로 얻은
        /// 추가 피해에만 적용한다.
        /// </summary>
        internal static long ApplyLegendaryCriticalDamagePolicy(
            long baseDamage,
            long criticalDamage,
            int effectPowerBps)
        {
            if (criticalDamage <= baseDamage ||
                effectPowerBps >= LegendaryBasisPoints)
            {
                return criticalDamage;
            }

            long bonus = criticalDamage - baseDamage;
            return checked(
                baseDamage +
                DeterministicMath.MultiplyBasisPoints(
                    bonus,
                    Math.Max(1, effectPowerBps)));
        }

        private void ConfigureLegendaryOverload(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node)
        {
            if (context.HasExecutionFlag(
                    EffectExecutionFlags.SuppressOverload))
            {
                return;
            }

            LegendaryOverloadRuntime overload =
                new LegendaryOverloadRuntime
                {
                    TowerId = context.TowerId,
                    CardId = context.CardId,
                    CardInstanceId = context.CardInstanceId,
                    RootChainId = context.RootChainId,
                    ActivationId = context.ActivationId,
                    AppliedCardIndex = context.CardIndex,
                    Direction = context.TraversalDirection,
                    ReplayPowerBps =
                        MultiplyLegendaryPower(
                            context.PowerBps,
                            node.Amount > 0
                                ? node.Amount
                                : DefaultOverloadReplayPowerBps),
                    SecondaryPowerBps =
                        ResolveLegendaryOverloadSecondaryPower(
                            operation,
                            node.Amount2,
                            context.PowerBps),
                    TertiaryPowerBps =
                        node.Amount3 > 0
                            ? node.Amount3
                            : DefaultEnemyOverloadResistanceBps,
                    DurationTicks =
                        node.DurationTicks > 0
                            ? node.DurationTicks
                            : DefaultEnemyOverloadDurationTicks,
                    RadiusMilli =
                        node.RadiusMilli > 0
                            ? node.RadiusMilli
                            : DefaultOverloadExplosionRadiusMilli
                };

            if (context.SubjectType == SubjectType.Projectile)
            {
                ProjectileState projectile =
                    FindProjectile(context.SubjectId);
                if (projectile != null && projectile.Alive)
                {
                    GetOrCreateLegendaryProjectileRuntime(
                        projectile.Id).Overload = overload;
                }
            }
            else
            {
                EnemyState enemy = FindEnemy(context.SubjectId);
                if (enemy != null && enemy.Alive)
                {
                    GetOrCreateLegendaryEnemyRuntime(
                        enemy.Id).Overload = overload;
                }
            }
        }

        private void TryScheduleLegendaryOverload(
            SubjectType subjectType,
            EntityId subjectId,
            TowerId towerId,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            in ProgramExecutionSpec execution)
        {
            LegendaryOverloadRuntime overload =
                GetLegendaryOverload(
                    subjectType,
                    subjectId);
            if (overload == null ||
                overload.ReplayActive ||
                overload.Completed ||
                overload.RootChainId != rootChainId ||
                overload.TowerId != towerId)
            {
                return;
            }

            TowerState tower = FindTower(towerId);
            var replay =
                new ProgramExecutionSpec(
                    execution.Direction,
                    overload.ReplayPowerBps,
                    checked(execution.RepeatIndex + 1),
                    execution.Flags |
                    EffectExecutionFlags.Repeated |
                    EffectExecutionFlags.SuppressRecursion |
                    EffectExecutionFlags.SuppressOverload);
            int entryIndex = FindProgramEntryIndex(
                tower,
                subjectType,
                in replay);
            if (entryIndex < 0)
            {
                overload.Completed = true;
                return;
            }

            if (TryScheduleLegendaryProgramPass(
                    subjectType,
                    subjectId,
                    towerId,
                    entryIndex,
                    rootChainId,
                    activationId,
                    parentEventId,
                    checked(depth + 1),
                    EventPhase.Scheduled,
                    in replay))
            {
                overload.ReplayActive = true;
                overload.ReplayIndex = replay.RepeatIndex;
            }
        }

        private bool TryCompleteLegendaryOverloadReplay(
            SubjectType subjectType,
            EntityId subjectId,
            TowerId towerId,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            in ProgramExecutionSpec execution)
        {
            if (!execution.HasFlag(
                    EffectExecutionFlags.SuppressOverload))
            {
                return false;
            }

            LegendaryOverloadRuntime overload =
                GetLegendaryOverload(
                    subjectType,
                    subjectId);
            if (overload == null ||
                !overload.ReplayActive ||
                overload.Completed ||
                overload.RootChainId != rootChainId ||
                overload.TowerId != towerId ||
                overload.ReplayIndex != execution.RepeatIndex)
            {
                return false;
            }

            overload.ReplayActive = false;
            overload.Completed = true;
            if (subjectType == SubjectType.Projectile)
            {
                ProjectileState projectile =
                    FindProjectile(subjectId);
                if (projectile == null || !projectile.Alive)
                {
                    return true;
                }

                var explosionNode =
                    new CompiledEffectNode(
                        EffectOperation.BindExplosion,
                        overload.SecondaryPowerBps,
                        0,
                        0,
                        0,
                        0,
                        0,
                        overload.RadiusMilli,
                        0,
                        0,
                        string.Empty);
                ExecuteExplosionWithPresentation(
                    projectile.Position,
                    projectile.DamageMilli,
                    overload.TowerId,
                    overload.CardId,
                    projectile.Id,
                    explosionNode,
                    rootChainId,
                    activationId,
                    parentEventId,
                    checked(depth + 1),
                    int.MaxValue,
                    "legendary_overload",
                    projectile.Id);
                ScheduleProjectileExpiration(
                    projectile,
                    parentEventId);
                return true;
            }

            EnemyState enemy = FindEnemy(subjectId);
            if (enemy == null ||
                !enemy.Alive ||
                enemy.DeathQueued)
            {
                return true;
            }

            LegendaryEnemyRuntime runtime =
                GetOrCreateLegendaryEnemyRuntime(enemy.Id);
            ApplyEnemyOverloadBuff(
                enemy,
                runtime,
                overload);
            return true;
        }

        /// <summary>
        /// ProcessStatuses보다 앞에서 한 번 호출한다. 금단 거래의 주기 보상·성장,
        /// 운명 고정과 무한 궤도의 지속시간, 과부하 버프 만료를 ID 순으로 처리한다.
        /// </summary>
        internal void ProcessLegendaryRuntime()
        {
            legendaryKeyScratch.Clear();
            foreach (int key in legendaryEnemyRuntimes.Keys)
            {
                legendaryKeyScratch.Add(key);
            }
            legendaryKeyScratch.Sort();

            for (int keyIndex = 0;
                 keyIndex < legendaryKeyScratch.Count;
                 keyIndex++)
            {
                int key = legendaryKeyScratch[keyIndex];
                EnemyState enemy =
                    FindEnemy(new EntityId(key));
                if (enemy == null ||
                    !enemy.Alive ||
                    enemy.DeathQueued ||
                    !legendaryEnemyRuntimes.TryGetValue(
                        key,
                        out LegendaryEnemyRuntime runtime))
                {
                    continue;
                }

                ProcessLegendaryEnemyDeals(
                    enemy,
                    runtime);

                if (runtime.Orbit != null &&
                    runtime.Orbit.RemainingTicks > 0)
                {
                    runtime.Orbit.RemainingTicks--;
                }

                if (runtime.FateLock != null &&
                    runtime.FateLock.RemainingTicks > 0)
                {
                    runtime.FateLock.RemainingTicks--;
                    if (runtime.FateLock.RemainingTicks == 0)
                    {
                        runtime.FateLock.Charges = 0;
                    }
                }

                if (runtime.OverloadBuff != null)
                {
                    runtime.OverloadBuff.RemainingTicks--;
                    if (runtime.OverloadBuff.RemainingTicks <= 0)
                    {
                        RemoveEnemyOverloadBuff(
                            enemy,
                            runtime);
                    }
                }
            }
        }

        /// <summary>
        /// 상태 적용 코드가 세기나 지속시간을 확정하기 전에 호출할 수 있는
        /// 과부하 저항 훅이다.
        /// </summary>
        internal int ResolveLegendaryEnemyStatusValue(
            EnemyState enemy,
            int value)
        {
            if (enemy == null ||
                value <= 0 ||
                !legendaryEnemyRuntimes.TryGetValue(
                    enemy.Id.Value,
                    out LegendaryEnemyRuntime runtime) ||
                runtime.OverloadBuff == null ||
                runtime.OverloadBuff.RemainingTicks <= 0)
            {
                return value;
            }

            return Math.Max(
                1,
                (int)DeterministicMath.MultiplyBasisPoints(
                    value,
                    LegendaryBasisPoints -
                    runtime.OverloadBuff.ResistanceBps));
        }

        internal int GetLegendaryEnemyStatusResistanceBps(
            EnemyState enemy)
        {
            if (enemy == null ||
                !legendaryEnemyRuntimes.TryGetValue(
                    enemy.Id.Value,
                    out LegendaryEnemyRuntime runtime) ||
                runtime.OverloadBuff == null ||
                runtime.OverloadBuff.RemainingTicks <= 0)
            {
                return 0;
            }

            return runtime.OverloadBuff.ResistanceBps;
        }

        private void SpawnLegendaryDualGhost(
            EnemyState source,
            TowerState tower,
            int cardIndex,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            CompiledTowerDefinition definition =
                content.GetTower(tower.DefinitionId);
            int ghostGeneration = checked(source.Generation + 1);
            if (!CanCreateProjectileEntity(ghostGeneration))
            {
                return;
            }
            EntityId ghostId = new EntityId(nextEntityId);
            EnemyState target =
                SelectLegendaryGhostTarget(source);
            var execution =
                new ProgramExecutionSpec(
                    context.TraversalDirection,
                    MultiplyLegendaryPower(
                        context.PowerBps,
                        node.Amount > 0
                            ? node.Amount
                            : DefaultDualPowerBps),
                    checked(context.RepeatIndex + 1),
                    context.ExecutionFlags |
                    EffectExecutionFlags.Repeated |
                    EffectExecutionFlags.SingleCard |
                    EffectExecutionFlags.DualInterpretation |
                    EffectExecutionFlags.SuppressRecursion |
                    EffectExecutionFlags.SuppressOverload);

            if (!TryScheduleLegendaryProgramPass(
                    SubjectType.Projectile,
                    ghostId,
                    tower.Id,
                    cardIndex,
                    context.RootChainId,
                    context.ActivationId,
                    context.ParentEventId,
                    checked(context.Depth + 1),
                    EventPhase.Scheduled,
                    in execution,
                    projectileSpawnCount: 1))
            {
                return;
            }

            nextEntityId++;
            var ghost = new ProjectileState
            {
                Id = ghostId,
                SourceTowerId = tower.Id,
                Generation = ghostGeneration,
                Position = source.Position,
                TargetId =
                    target == null
                        ? source.Id
                        : target.Id,
                ApplyEnemyProgramOnHit = false,
                Homing = true,
                VisualFlags =
                    CardEffectVisualFlags.DualInterpretation,
                DamageMilli = DeterministicMath.MultiplyBasisPoints(
                    definition.BaseDamageMilli,
                    execution.PowerBps),
                SpeedMilliPerTick =
                    definition.ProjectileSpeedMilliPerTick,
                LifetimeRemaining = Math.Min(
                    Math.Max(
                        1,
                        definition.ProjectileLifetimeTicks),
                    content.Safety.MaxProjectileLifetimeTicks),
                RootChainId = context.RootChainId,
                ActivationId = context.ActivationId,
                LastTrailPosition = source.Position
            };
            // 원본 적과 같은 위치에서 생성되므로 그 적을 즉시 다시 맞히지 않는다.
            ghost.HitEnemies.Add(source.Id.Value);
            SetProjectileDirection(
                ghost,
                target == null
                    ? SimPosition.FromMilliUnits(
                        source.Position.X.MilliUnits + 1000,
                        source.Position.Y.MilliUnits)
                    : target.Position);
            projectiles.Add(ghost);
            AddPresentation(
                PresentationEventType.ProjectileSpawned,
                ghost.Id.Value,
                source.Id.Value,
                (int)Math.Min(
                    int.MaxValue,
                    ghost.DamageMilli),
                "dual_interpretation");
        }

        private EnemyState SelectLegendaryGhostTarget(
            EnemyState source)
        {
            EnemyState selected = null;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState candidate = enemies[i];
                if (candidate == source ||
                    !candidate.Alive ||
                    candidate.DeathQueued)
                {
                    continue;
                }

                if (selected == null ||
                    CompareTargetPriority(
                        source.Position,
                        candidate,
                        selected) < 0)
                {
                    selected = candidate;
                }
            }
            return selected;
        }

        private void ProcessProjectileDualInterpretation(
            ProjectileState projectile,
            EnemyState target,
            in GameEvent hitEvent,
            LegendaryProjectileRuntime runtime)
        {
            for (int i = 0; i < runtime.Duals.Count; i++)
            {
                LegendaryDualRuntime dual =
                    runtime.Duals[i];
                if (dual.RemainingApplications <= 0)
                {
                    continue;
                }

                var execution =
                    new ProgramExecutionSpec(
                        dual.Direction,
                        dual.PowerBps,
                        1,
                        EffectExecutionFlags.Repeated |
                        EffectExecutionFlags.SingleCard |
                        EffectExecutionFlags.DualInterpretation |
                        EffectExecutionFlags.SuppressRecursion |
                        EffectExecutionFlags.SuppressOverload);
                if (TryScheduleLegendaryProgramPass(
                        SubjectType.Enemy,
                        target.Id,
                        dual.TowerId,
                        dual.TargetCardIndex,
                        hitEvent.RootChainId,
                        hitEvent.ActivationId,
                        hitEvent.EventId,
                        checked(hitEvent.Depth + 1),
                        hitEvent.Phase,
                        in execution))
                {
                    dual.RemainingApplications--;
                }
            }
        }

        private bool ProcessProjectileForbiddenDeals(
            ProjectileState projectile,
            in GameEvent hitEvent,
            LegendaryProjectileRuntime runtime)
        {
            for (int i = 0; i < runtime.Deals.Count; i++)
            {
                LegendaryProjectileDealRuntime deal =
                    runtime.Deals[i];
                if (deal.RemainingReplays <= 0)
                {
                    continue;
                }

                if (gold < deal.GoldCost)
                {
                    deal.RemainingReplays = 0;
                    ScheduleProjectileExpiration(
                        projectile,
                        hitEvent.EventId);
                    AddPresentation(
                        PresentationEventType.EffectTriggered,
                        projectile.Id.Value,
                        deal.TowerId.Value,
                        deal.GoldCost,
                        "forbidden_deal_failed");
                    return true;
                }

                TowerState tower = FindTower(deal.TowerId);
                var replay =
                    new ProgramExecutionSpec(
                        deal.Direction,
                        deal.ReplayPowerBps,
                        1,
                        EffectExecutionFlags.Repeated |
                        EffectExecutionFlags.SuppressRecursion |
                        EffectExecutionFlags.SuppressOverload);
                int entryIndex = FindProgramEntryIndex(
                    tower,
                    SubjectType.Projectile,
                    in replay);
                if (entryIndex < 0)
                {
                    deal.RemainingReplays = 0;
                    continue;
                }

                // 골드는 전체 pass의 카드/큐 예산이 확보된 뒤에만 차감한다.
                if (TryScheduleLegendaryProgramPass(
                        SubjectType.Projectile,
                        projectile.Id,
                        deal.TowerId,
                        entryIndex,
                        hitEvent.RootChainId,
                        hitEvent.ActivationId,
                        hitEvent.EventId,
                        checked(hitEvent.Depth + 1),
                        hitEvent.Phase,
                        in replay))
                {
                    gold -= deal.GoldCost;
                    deal.RemainingReplays--;
                    AddPresentation(
                        PresentationEventType.EffectTriggered,
                        projectile.Id.Value,
                        deal.TowerId.Value,
                        deal.GoldCost,
                        "forbidden_deal");
                }
            }

            return false;
        }

        private void ProcessLegendaryEnemyDeals(
            EnemyState enemy,
            LegendaryEnemyRuntime runtime)
        {
            for (int i = 0; i < runtime.Deals.Count; i++)
            {
                LegendaryEnemyDealRuntime deal =
                    runtime.Deals[i];
                if (deal.RemainingTicks <= 0)
                {
                    continue;
                }
                if (deal.PulsesApplied >= deal.PulseLimit)
                {
                    deal.RemainingTicks = 0;
                    continue;
                }

                if (tick >= deal.NextPulseTick)
                {
                    ChainId chainId = CreateRootChain();
                    ActivationId activationId =
                        CreateActivation();
                    var reward = new GameEvent(
                        tick,
                        EventPhase.Reward,
                        EventType.RewardGranted,
                        chainId,
                        EventId.Invalid,
                        activationId,
                        deal.TowerId,
                        deal.CardId,
                        enemy.Id,
                        enemy.Id,
                        SubjectType.Enemy,
                        0,
                        enemy.Generation,
                        EventTags.Economic |
                        EventTags.Repeated,
                        RewardOrigin.CardBounty,
                        payloadValue: deal.GoldAmount);
                    if (TryEnqueue(in reward, out _))
                    {
                        GrowForbiddenDealEnemy(
                            enemy,
                            deal);
                        deal.PulsesApplied++;
                        deal.NextPulseTick =
                            tick + deal.IntervalTicks;
                        AddPresentation(
                            PresentationEventType.EffectTriggered,
                            enemy.Id.Value,
                            deal.TowerId.Value,
                            deal.PulsesApplied,
                            "forbidden_deal_growth");
                    }
                }

                deal.RemainingTicks--;
            }
        }

        private static void GrowForbiddenDealEnemy(
            EnemyState enemy,
            LegendaryEnemyDealRuntime deal)
        {
            long previousMax = enemy.MaxHealthMilli;
            long grownMax =
                DeterministicMath.MultiplyBasisPoints(
                    previousMax,
                    deal.HealthAndSizeGrowthBps);
            long addedHealth = Math.Max(
                0,
                grownMax - previousMax);
            enemy.MaxHealthMilli = Math.Max(
                previousMax,
                grownMax);
            enemy.HealthMilli = Math.Min(
                enemy.MaxHealthMilli,
                checked(enemy.HealthMilli + addedHealth));
            enemy.SizeMultiplierBps = Math.Max(
                1,
                MultiplyBps(
                    enemy.SizeMultiplierBps,
                    deal.HealthAndSizeGrowthBps));
            enemy.SpeedMultiplierBps = Math.Max(
                1,
                MultiplyBps(
                    enemy.SpeedMultiplierBps,
                    deal.SpeedGrowthBps));
        }

        private static LegendaryOvercloneRuntime
            CreateOvercloneRuntime(
                in EffectExecutionContext context,
                in CompiledEffectNode node)
        {
            return new LegendaryOvercloneRuntime
            {
                TowerId = context.TowerId,
                CardId = context.CardId,
                CardInstanceId = context.CardInstanceId,
                AppliedCardIndex = context.CardIndex,
                Direction = context.TraversalDirection,
                PowerBps =
                    MultiplyLegendaryPower(
                        context.PowerBps,
                        node.Amount > 0
                            ? node.Amount
                            : DefaultOverclonePowerBps),
                RemainingGenerations =
                    node.Limit > 0
                        ? node.Limit
                        : DefaultOvercloneGenerationLimit,
                InheritedEffectLimit =
                    node.Limit > 0
                        ? node.Limit
                        : DefaultOvercloneGenerationLimit
            };
        }

        private void CopyLegendaryProjectileBindings(
            ProjectileState source,
            ProjectileState target,
            LegendaryOvercloneRuntime rule)
        {
            // Duplicate/Afterimage 생성기는 기본 의미를 위해 바인딩을 먼저 복사한다.
            // 과잉 복제가 활성인 경로에서는 그 전체 복사본을 그대로 두지 않고,
            // 허용된 수만 감쇠한 독립 인스턴스로 다시 구성한다.
            target.Bindings.Clear();
            int copied = 0;
            for (int i = 0;
                 i < source.Bindings.Count &&
                 copied < rule.InheritedEffectLimit;
                 i++)
            {
                EffectBinding sourceBinding =
                    source.Bindings[i];

                EffectBinding copy =
                    sourceBinding.Clone();
                copy.Node = ScaleProgramPassNode(
                    copy.Node,
                    rule.PowerBps);
                copy.Used = false;
                copy.TriggerCount = 0;
                copy.TrailStarted = false;
                copy.ActiveTrailHazardId = -1;
                target.Bindings.Add(copy);
                copied++;
            }
        }

        private void CopyLegendaryEnemyDeathBindings(
            EnemyState source,
            EnemyState target,
            LegendaryOvercloneRuntime rule)
        {
            target.DeathBindings.Clear();
            int copied = 0;
            for (int i = 0;
                 i < source.DeathBindings.Count &&
                 copied < rule.InheritedEffectLimit;
                 i++)
            {
                EffectBinding sourceBinding =
                    source.DeathBindings[i];

                EffectBinding copy =
                    sourceBinding.Clone();
                copy.Node = ScaleProgramPassNode(
                    copy.Node,
                    rule.PowerBps);
                copy.Used = false;
                copy.TriggerCount = 0;
                target.DeathBindings.Add(copy);
                copied++;
            }
        }

        private StatusInstance CloneLegendaryInheritedStatus(
            StatusInstance source,
            int powerBps)
        {
            return new StatusInstance
            {
                InstanceId = nextStatusInstanceId++,
                Type = source.Type,
                SourceEntityId = source.SourceEntityId,
                SourceTowerId = source.SourceTowerId,
                SourceCardId = source.SourceCardId,
                SourceCardInstanceId =
                    source.SourceCardInstanceId,
                Stacks = source.Stacks,
                Intensity = Math.Max(
                    1,
                    ScaleLegendaryInt(
                        source.Intensity,
                        powerBps)),
                RemainingTicks = Math.Max(
                    1,
                    ScaleLegendaryInt(
                        source.RemainingTicks,
                        powerBps)),
                MaxStacks = source.MaxStacks,
                TickInterval = source.TickInterval,
                NextTick = source.NextTick,
                Inherited = true,
                Dispellable = source.Dispellable,
                Limit = source.Limit,
                RadiusMilli = source.RadiusMilli,
                ArmorIgnoreBps =
                    source.ArmorIgnoreBps
            };
        }

        private LegendaryProjectileRuntime
            CloneLegendaryProjectileRuntime(
                LegendaryProjectileRuntime source,
                LegendaryOvercloneRuntime rule)
        {
            var target =
                new LegendaryProjectileRuntime();
            for (int i = 0;
                 i < source.Duals.Count;
                 i++)
            {
                LegendaryDualRuntime dual =
                    source.Duals[i];
                if (IsAppliedBeforeOverclone(
                        dual.AppliedCardIndex,
                        rule))
                {
                    LegendaryDualRuntime copy =
                        dual.Clone();
                    copy.PowerBps =
                        MultiplyLegendaryPower(
                            copy.PowerBps,
                            rule.PowerBps);
                    target.Duals.Add(copy);
                }
            }
            for (int i = 0;
                 i < source.Deals.Count;
                 i++)
            {
                LegendaryProjectileDealRuntime deal =
                    source.Deals[i];
                if (IsAppliedBeforeOverclone(
                        deal.AppliedCardIndex,
                        rule))
                {
                    LegendaryProjectileDealRuntime copy =
                        deal.Clone();
                    copy.ReplayPowerBps =
                        MultiplyLegendaryPower(
                            copy.ReplayPowerBps,
                            rule.PowerBps);
                    target.Deals.Add(copy);
                }
            }
            for (int i = 0;
                 i < source.LastCommands.Count;
                 i++)
            {
                LegendaryLastCommandRuntime command =
                    source.LastCommands[i];
                if (IsAppliedBeforeOverclone(
                        command.AppliedCardIndex,
                        rule))
                {
                    LegendaryLastCommandRuntime copy =
                        command.Clone();
                    copy.Used = false;
                    copy.PowerBps =
                        MultiplyLegendaryPower(
                            copy.PowerBps,
                            rule.PowerBps);
                    target.LastCommands.Add(copy);
                }
            }

            if (source.Orbit != null &&
                IsAppliedBeforeOverclone(
                    source.Orbit.AppliedCardIndex,
                    rule))
            {
                target.Orbit = source.Orbit.CloneDetached();
                target.Orbit.RepeatPowerBps =
                    MultiplyLegendaryPower(
                        target.Orbit.RepeatPowerBps,
                        rule.PowerBps);
                target.Orbit.RemainingTicks =
                    ScaleLegendaryInt(
                        target.Orbit.RemainingTicks,
                        rule.PowerBps);
            }
            if (source.FateLock != null &&
                IsAppliedBeforeOverclone(
                    source.FateLock.AppliedCardIndex,
                    rule))
            {
                target.FateLock =
                    source.FateLock.Clone();
                target.FateLock.PowerBps =
                    MultiplyLegendaryPower(
                        target.FateLock.PowerBps,
                        rule.PowerBps);
            }

            target.Overclone = rule.CloneForChild();
            return target;
        }

        private LegendaryEnemyRuntime
            CloneLegendaryEnemyRuntime(
                LegendaryEnemyRuntime source,
                LegendaryOvercloneRuntime rule)
        {
            var target = new LegendaryEnemyRuntime();
            for (int i = 0;
                 i < source.Deals.Count;
                 i++)
            {
                LegendaryEnemyDealRuntime deal =
                    source.Deals[i];
                if (IsAppliedBeforeOverclone(
                        deal.AppliedCardIndex,
                        rule))
                {
                    LegendaryEnemyDealRuntime copy =
                        deal.Clone();
                    copy.HealthAndSizeGrowthBps =
                        ScaleLegendaryMultiplierBonus(
                            copy.HealthAndSizeGrowthBps,
                            rule.PowerBps);
                    copy.SpeedGrowthBps =
                        ScaleLegendaryMultiplierBonus(
                            copy.SpeedGrowthBps,
                            rule.PowerBps);
                    copy.RemainingTicks =
                        ScaleLegendaryInt(
                            copy.RemainingTicks,
                            rule.PowerBps);
                    target.Deals.Add(copy);
                }
            }
            for (int i = 0;
                 i < source.LastCommands.Count;
                 i++)
            {
                LegendaryLastCommandRuntime command =
                    source.LastCommands[i];
                if (IsAppliedBeforeOverclone(
                        command.AppliedCardIndex,
                        rule))
                {
                    LegendaryLastCommandRuntime copy =
                        command.Clone();
                    copy.Used = false;
                    copy.PowerBps =
                        MultiplyLegendaryPower(
                            copy.PowerBps,
                            rule.PowerBps);
                    target.LastCommands.Add(copy);
                }
            }
            if (source.Orbit != null &&
                IsAppliedBeforeOverclone(
                    source.Orbit.AppliedCardIndex,
                    rule))
            {
                target.Orbit = source.Orbit.Clone();
                target.Orbit.RemainingTicks =
                    ScaleLegendaryInt(
                        target.Orbit.RemainingTicks,
                        rule.PowerBps);
            }
            if (source.FateLock != null &&
                IsAppliedBeforeOverclone(
                    source.FateLock.AppliedCardIndex,
                    rule))
            {
                target.FateLock =
                    source.FateLock.Clone();
                target.FateLock.PowerBps =
                    MultiplyLegendaryPower(
                        target.FateLock.PowerBps,
                        rule.PowerBps);
                target.FateLock.RemainingTicks =
                    ScaleLegendaryInt(
                        target.FateLock.RemainingTicks,
                        rule.PowerBps);
            }
            target.Overclone = rule.CloneForChild();
            return target;
        }

        private static bool IsAppliedBeforeOverclone(
            int appliedCardIndex,
            LegendaryOvercloneRuntime rule)
        {
            return rule.Direction > 0
                ? appliedCardIndex < rule.AppliedCardIndex
                : appliedCardIndex > rule.AppliedCardIndex;
        }

        private static void PopulateLastCommand(
            LegendaryLastCommandRuntime command,
            in EffectExecutionContext context,
            in CompiledEffectNode node,
            int defaultLimit)
        {
            command.TowerId = context.TowerId;
            command.CardId = context.CardId;
            command.CardInstanceId =
                context.CardInstanceId;
            command.AppliedCardIndex =
                context.CardIndex;
            command.Direction =
                context.TraversalDirection;
            command.PowerBps =
                MultiplyLegendaryPower(
                    context.PowerBps,
                    node.Amount > 0
                        ? node.Amount
                        : DefaultLastCommandPowerBps);
            command.RadiusMilli =
                node.RadiusMilli > 0
                    ? node.RadiusMilli
                    : DefaultLastCommandRadiusMilli;
            command.TargetLimit =
                node.Limit > 0
                    ? node.Limit
                    : defaultLimit;
            command.RemainingUses =
                node.Limit > 0 &&
                defaultLimit ==
                    DefaultProjectileLastCommandLimit
                    ? node.Limit
                    : DefaultProjectileLastCommandLimit;
            command.Used = false;
        }

        private void SelectLegendaryDeathTargets(
            EnemyState source,
            int radiusMilli,
            int limit)
        {
            legendaryEnemyScratch.Clear();
            int clampedRadius = Math.Max(
                1,
                radiusMilli > 0
                    ? radiusMilli
                    : DefaultLastCommandRadiusMilli);
            int clampedLimit = Math.Max(
                1,
                Math.Min(
                    DefaultEnemyLastCommandTargetLimit,
                    limit));
            SimPosition areaCenter =
                GetEnemyHitboxCenter(source);
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState candidate = enemies[i];
                if (candidate == source ||
                    !candidate.Alive ||
                    candidate.DeathQueued ||
                    !DoesAreaCircleOverlapEnemyHitbox(
                        areaCenter,
                        clampedRadius,
                        candidate))
                {
                    continue;
                }
                legendaryEnemyScratch.Add(candidate);
            }
            legendaryEnemyScratch.Sort(
                (left, right) =>
                    CompareTargetPriority(
                        areaCenter,
                        left,
                        right));
            if (legendaryEnemyScratch.Count >
                clampedLimit)
            {
                legendaryEnemyScratch.RemoveRange(
                    clampedLimit,
                    legendaryEnemyScratch.Count -
                    clampedLimit);
            }
        }

        private void ApplyEnemyOverloadBuff(
            EnemyState enemy,
            LegendaryEnemyRuntime runtime,
            LegendaryOverloadRuntime overload)
        {
            if (runtime.OverloadBuff != null)
            {
                RemoveEnemyOverloadBuff(
                    enemy,
                    runtime);
            }

            var buff =
                new LegendaryEnemyOverloadBuffRuntime
                {
                    SpeedBps = Math.Max(
                        LegendaryBasisPoints,
                        overload.SecondaryPowerBps),
                    ResistanceBps = Math.Max(
                        0,
                        Math.Min(
                            9000,
                            overload.TertiaryPowerBps)),
                    RemainingTicks = Math.Max(
                        1,
                        overload.DurationTicks)
                };
            enemy.SpeedMultiplierBps = Math.Max(
                1,
                MultiplyBps(
                    enemy.SpeedMultiplierBps,
                    buff.SpeedBps));
            runtime.OverloadBuff = buff;
            AddPresentation(
                PresentationEventType.EffectTriggered,
                enemy.Id.Value,
                overload.TowerId.Value,
                buff.RemainingTicks,
                "overload_enemy");
        }

        private static void RemoveEnemyOverloadBuff(
            EnemyState enemy,
            LegendaryEnemyRuntime runtime)
        {
            LegendaryEnemyOverloadBuffRuntime buff =
                runtime.OverloadBuff;
            if (buff == null)
            {
                return;
            }

            enemy.SpeedMultiplierBps = Math.Max(
                1,
                (int)DeterministicMath.MultiplyDivide(
                    enemy.SpeedMultiplierBps,
                    LegendaryBasisPoints,
                    Math.Max(1, buff.SpeedBps)));
            runtime.OverloadBuff = null;
        }

        /// <summary>
        /// 공유 pass scheduler를 호출한다. 유령 탄환 생성량과 재귀 토큰을
        /// 카드/이벤트 예산과 같은 복합 예약에 전달한다.
        /// </summary>
        private bool TryScheduleLegendaryProgramPass(
            SubjectType subjectType,
            EntityId subjectId,
            TowerId towerId,
            int cardIndex,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            EventPhase phase,
            in ProgramExecutionSpec execution,
            int recursionCount = 0,
            int projectileSpawnCount = 0)
        {
            return EnqueueProgramPass(
                subjectType,
                subjectId,
                towerId,
                cardIndex,
                rootChainId,
                activationId,
                parentEventId,
                depth,
                phase,
                in execution,
                recursionCount:
                    recursionCount,
                projectileSpawnCount:
                    projectileSpawnCount);
        }

        /// <summary>
        /// 마지막 명령의 여러 대상에게 공유 pass scheduler를 ID 정렬 순으로
        /// 호출한다. 각 pass는 공유 코어에서 전체 카드 예산을 원자 예약한다.
        /// </summary>
        private bool TryScheduleLegendaryEnemyPasses(
            List<EnemyState> targets,
            TowerId towerId,
            int cardIndex,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            EventPhase phase,
            in ProgramExecutionSpec execution)
        {
            return EnqueueEnemyProgramPassBatch(
                targets,
                towerId,
                cardIndex,
                rootChainId,
                activationId,
                parentEventId,
                depth,
                phase,
                in execution);
        }

        private int FindFirstLegendaryActionIndex(
            TowerState tower,
            SubjectType subjectType,
            int direction)
        {
            if (tower == null)
            {
                return -1;
            }

            var execution =
                new ProgramExecutionSpec(
                    direction,
                    LegendaryBasisPoints,
                    0,
                    EffectExecutionFlags.None);
            int cursor =
                direction > 0
                    ? -1
                    : tower.Program.Length;
            while ((cursor = FindNextProgramIndex(
                       tower,
                       cursor,
                       subjectType,
                       in execution)) >= 0)
            {
                if (!IsLegendaryGrammarCard(
                        tower,
                        cursor,
                        subjectType))
                {
                    return cursor;
                }
            }
            return -1;
        }

        private int FindNextLegendaryActionIndex(
            TowerState tower,
            int currentIndex,
            SubjectType subjectType,
            int direction)
        {
            if (tower == null)
            {
                return -1;
            }

            var execution =
                new ProgramExecutionSpec(
                    direction,
                    LegendaryBasisPoints,
                    0,
                    EffectExecutionFlags.None);
            int cursor = currentIndex;
            while ((cursor = FindNextProgramIndex(
                       tower,
                       cursor,
                       subjectType,
                       in execution)) >= 0)
            {
                if (!IsLegendaryGrammarCard(
                        tower,
                        cursor,
                        subjectType))
                {
                    return cursor;
                }
            }
            return -1;
        }

        private bool IsLegendaryGrammarCard(
            TowerState tower,
            int cardIndex,
            SubjectType subjectType)
        {
            if (tower == null ||
                cardIndex < 0 ||
                cardIndex >= tower.Program.Length)
            {
                return false;
            }

            CompiledCardDefinition card =
                content.GetCard(
                    tower.Program[cardIndex]);
            CompiledEffectNode[] nodes =
                subjectType == SubjectType.Projectile
                    ? card.ProjectileEffectsInternal
                    : card.EnemyEffectsInternal;
            if (nodes.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                switch (nodes[i].Operation)
                {
                    case EffectOperation.EnableRecursion:
                    case EffectOperation.ReverseProgramOrder:
                    case EffectOperation.EnableProjectileDualInterpretation:
                    case EffectOperation.ApplyEnemyDualInterpretation:
                        break;
                    default:
                        return false;
                }
            }
            return true;
        }

        private LegendaryOverloadRuntime GetLegendaryOverload(
            SubjectType subjectType,
            EntityId subjectId)
        {
            if (subjectType == SubjectType.Projectile)
            {
                return legendaryProjectileRuntimes.TryGetValue(
                        subjectId.Value,
                        out LegendaryProjectileRuntime projectile)
                    ? projectile.Overload
                    : null;
            }

            return legendaryEnemyRuntimes.TryGetValue(
                    subjectId.Value,
                    out LegendaryEnemyRuntime enemy)
                ? enemy.Overload
                : null;
        }

        private LegendaryProjectileRuntime
            GetOrCreateLegendaryProjectileRuntime(
                EntityId projectileId)
        {
            if (!legendaryProjectileRuntimes.TryGetValue(
                    projectileId.Value,
                    out LegendaryProjectileRuntime runtime))
            {
                runtime =
                    new LegendaryProjectileRuntime();
                legendaryProjectileRuntimes.Add(
                    projectileId.Value,
                    runtime);
            }
            return runtime;
        }

        private LegendaryEnemyRuntime
            GetOrCreateLegendaryEnemyRuntime(
                EntityId enemyId)
        {
            if (!legendaryEnemyRuntimes.TryGetValue(
                    enemyId.Value,
                    out LegendaryEnemyRuntime runtime))
            {
                runtime =
                    new LegendaryEnemyRuntime();
                legendaryEnemyRuntimes.Add(
                    enemyId.Value,
                    runtime);
            }
            return runtime;
        }

        private static LegendaryDualRuntime FindDualRuntime(
            List<LegendaryDualRuntime> values,
            int cardInstanceId)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].CardInstanceId ==
                    cardInstanceId)
                {
                    return values[i];
                }
            }
            return null;
        }

        private static LegendaryProjectileDealRuntime
            FindProjectileDeal(
                List<LegendaryProjectileDealRuntime> values,
                int cardInstanceId)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].CardInstanceId ==
                    cardInstanceId)
                {
                    return values[i];
                }
            }
            return null;
        }

        private static LegendaryEnemyDealRuntime FindEnemyDeal(
            List<LegendaryEnemyDealRuntime> values,
            int cardInstanceId)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].CardInstanceId ==
                    cardInstanceId)
                {
                    return values[i];
                }
            }
            return null;
        }

        private static LegendaryLastCommandRuntime
            FindLastCommand(
                List<LegendaryLastCommandRuntime> values,
                int cardInstanceId)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].CardInstanceId ==
                    cardInstanceId)
                {
                    return values[i];
                }
            }
            return null;
        }

        private static int MultiplyLegendaryPower(
            int left,
            int right)
        {
            return Math.Max(
                1,
                Math.Min(
                    LegendaryBasisPoints,
                    (int)DeterministicMath.MultiplyBasisPoints(
                        Math.Max(1, left),
                        Math.Max(1, right))));
        }

        private static int ScaleLegendaryInt(
            int value,
            int powerBps)
        {
            if (value <= 0)
            {
                return value;
            }
            return (int)Math.Max(
                1,
                Math.Min(
                    int.MaxValue,
                    DeterministicMath.MultiplyBasisPoints(
                        value,
                        powerBps)));
        }

        private static int ScaleLegendaryMultiplierBonus(
            int multiplierBps,
            int powerBps)
        {
            if (multiplierBps <= LegendaryBasisPoints)
            {
                return MultiplyLegendaryPower(
                    multiplierBps,
                    powerBps);
            }

            int bonus = multiplierBps -
                        LegendaryBasisPoints;
            return checked(
                LegendaryBasisPoints +
                ScaleLegendaryInt(
                    bonus,
                    powerBps));
        }

        private static int
            ResolveLegendaryOverloadSecondaryPower(
                EffectOperation operation,
                int compiledAmount2,
                int passPowerBps)
        {
            if (operation ==
                EffectOperation.EnableProjectileOverload)
            {
                return compiledAmount2 > 0
                    ? compiledAmount2
                    : MultiplyLegendaryPower(
                        DefaultOverloadExplosionPowerBps,
                        passPowerBps);
            }

            // Speed is a multiplier around 10000. Scale only its bonus when
            // ScaleProgramPassNode has reduced the raw field below 10000.
            return compiledAmount2 >= LegendaryBasisPoints
                ? compiledAmount2
                : ScaleLegendaryMultiplierBonus(
                    DefaultEnemyOverloadSpeedBps,
                    passPowerBps);
        }

        /// <summary>새 런 시작 시 전설 카드의 모든 권위 상태를 비운다.</summary>
        internal void ResetLegendaryState()
        {
            legendaryProjectileRuntimes.Clear();
            legendaryEnemyRuntimes.Clear();
            legendaryRecursionRequests.Clear();
            legendaryKeyScratch.Clear();
            legendaryEnemyScratch.Clear();
        }

        /// <summary>
        /// 틱 말 실제 제거가 끝난 뒤 죽은 개체의 전설 런타임과 끝난 재귀 요청을
        /// 정렬된 키 순으로 제거한다.
        /// </summary>
        internal void CleanupLegendaryState()
        {
            legendaryKeyScratch.Clear();
            foreach (int key in legendaryProjectileRuntimes.Keys)
            {
                ProjectileState projectile =
                    FindProjectile(new EntityId(key));
                if (projectile == null || !projectile.Alive)
                {
                    legendaryKeyScratch.Add(key);
                }
            }
            legendaryKeyScratch.Sort();
            for (int i = 0;
                 i < legendaryKeyScratch.Count;
                 i++)
            {
                legendaryProjectileRuntimes.Remove(
                    legendaryKeyScratch[i]);
            }

            legendaryKeyScratch.Clear();
            foreach (int key in legendaryEnemyRuntimes.Keys)
            {
                EnemyState enemy =
                    FindEnemy(new EntityId(key));
                if (enemy == null || !enemy.Alive)
                {
                    legendaryKeyScratch.Add(key);
                }
            }
            legendaryKeyScratch.Sort();
            for (int i = 0;
                 i < legendaryKeyScratch.Count;
                 i++)
            {
                legendaryEnemyRuntimes.Remove(
                    legendaryKeyScratch[i]);
            }

            for (int i =
                     legendaryRecursionRequests.Count - 1;
                 i >= 0;
                 i--)
            {
                LegendaryRecursionRequest request =
                    legendaryRecursionRequests[i];
                if (request.Consumed ||
                    !SubjectExists(
                        request.SubjectType,
                        request.SubjectId))
                {
                    legendaryRecursionRequests.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 전설 런타임의 모든 미래 판정 상태를 EntityId/CardInstanceId 순으로
        /// 안정 해시에 추가한다.
        /// </summary>
        internal void AppendLegendaryStateHash(
            ref StableHashBuilder hash)
        {
            int[] projectileKeys =
                new int[
                    legendaryProjectileRuntimes.Count];
            legendaryProjectileRuntimes.Keys.CopyTo(
                projectileKeys,
                0);
            Array.Sort(projectileKeys);
            hash.Add(projectileKeys.Length);
            for (int i = 0;
                 i < projectileKeys.Length;
                 i++)
            {
                int key = projectileKeys[i];
                hash.Add(key);
                AppendLegendaryProjectileRuntimeHash(
                    ref hash,
                    legendaryProjectileRuntimes[key]);
            }

            int[] enemyKeys =
                new int[legendaryEnemyRuntimes.Count];
            legendaryEnemyRuntimes.Keys.CopyTo(
                enemyKeys,
                0);
            Array.Sort(enemyKeys);
            hash.Add(enemyKeys.Length);
            for (int i = 0;
                 i < enemyKeys.Length;
                 i++)
            {
                int key = enemyKeys[i];
                hash.Add(key);
                AppendLegendaryEnemyRuntimeHash(
                    ref hash,
                    legendaryEnemyRuntimes[key]);
            }

            var recursion =
                new List<LegendaryRecursionRequest>(
                    legendaryRecursionRequests);
            recursion.Sort(
                LegendaryRecursionRequest.Compare);
            hash.Add(recursion.Count);
            for (int i = 0;
                 i < recursion.Count;
                 i++)
            {
                LegendaryRecursionRequest request =
                    recursion[i];
                hash.Add(request.RootChainId);
                hash.Add((int)request.SubjectType);
                hash.Add(request.SubjectId);
                hash.Add(request.TowerId);
                hash.Add(request.CardId);
                hash.Add(request.CardInstanceId);
                hash.Add(request.Direction);
                hash.Add(request.PowerBps);
                hash.Add(request.Consumed);
            }
        }

        private static void
            AppendLegendaryProjectileRuntimeHash(
                ref StableHashBuilder hash,
                LegendaryProjectileRuntime runtime)
        {
            AppendLegendaryDualListHash(
                ref hash,
                runtime.Duals);
            hash.Add(runtime.Orbit != null);
            if (runtime.Orbit != null)
            {
                AppendLegendaryProjectileOrbitHash(
                    ref hash,
                    runtime.Orbit);
            }
            AppendLegendaryOvercloneHash(
                ref hash,
                runtime.Overclone);
            AppendLegendaryProjectileDealListHash(
                ref hash,
                runtime.Deals);
            AppendLegendaryLastCommandListHash(
                ref hash,
                runtime.LastCommands);
            AppendLegendaryFateLockHash(
                ref hash,
                runtime.FateLock);
            AppendLegendaryOverloadHash(
                ref hash,
                runtime.Overload);
        }

        private static void AppendLegendaryEnemyRuntimeHash(
            ref StableHashBuilder hash,
            LegendaryEnemyRuntime runtime)
        {
            hash.Add(runtime.Orbit != null);
            if (runtime.Orbit != null)
            {
                LegendaryEnemyOrbitRuntime orbit =
                    runtime.Orbit;
                hash.Add(orbit.TowerId);
                hash.Add(orbit.CardId);
                hash.Add(orbit.CardInstanceId);
                hash.Add(orbit.AppliedCardIndex);
                hash.Add(orbit.Direction);
                hash.Add(orbit.AnchorProgressMilli);
                hash.Add(orbit.LoopLengthMilli);
                hash.Add(orbit.TravelMilli);
                hash.Add(orbit.RemainingTicks);
                hash.Add(orbit.HitLimit);
                hash.Add(orbit.HitsTaken);
            }
            AppendLegendaryOvercloneHash(
                ref hash,
                runtime.Overclone);
            AppendLegendaryEnemyDealListHash(
                ref hash,
                runtime.Deals);
            AppendLegendaryLastCommandListHash(
                ref hash,
                runtime.LastCommands);
            AppendLegendaryFateLockHash(
                ref hash,
                runtime.FateLock);
            AppendLegendaryOverloadHash(
                ref hash,
                runtime.Overload);
            hash.Add(runtime.OverloadBuff != null);
            if (runtime.OverloadBuff != null)
            {
                hash.Add(runtime.OverloadBuff.SpeedBps);
                hash.Add(
                    runtime.OverloadBuff.ResistanceBps);
                hash.Add(
                    runtime.OverloadBuff.RemainingTicks);
            }
        }

        private static void AppendLegendaryDualListHash(
            ref StableHashBuilder hash,
            List<LegendaryDualRuntime> values)
        {
            LegendaryDualRuntime[] sorted =
                values.ToArray();
            Array.Sort(
                sorted,
                (left, right) =>
                    left.CardInstanceId.CompareTo(
                        right.CardInstanceId));
            hash.Add(sorted.Length);
            for (int i = 0; i < sorted.Length; i++)
            {
                LegendaryDualRuntime value = sorted[i];
                hash.Add(value.TowerId);
                hash.Add(value.CardId);
                hash.Add(value.CardInstanceId);
                hash.Add(value.TargetCardIndex);
                hash.Add(value.Direction);
                hash.Add(value.PowerBps);
                hash.Add(value.RemainingApplications);
                hash.Add(value.RootChainId);
                hash.Add(value.ActivationId);
                hash.Add(value.AppliedCardIndex);
            }
        }

        private static void
            AppendLegendaryProjectileOrbitHash(
                ref StableHashBuilder hash,
                LegendaryProjectileOrbitRuntime orbit)
        {
            hash.Add(orbit.TowerId);
            hash.Add(orbit.CardId);
            hash.Add(orbit.CardInstanceId);
            hash.Add(orbit.AppliedCardIndex);
            hash.Add(orbit.Direction);
            hash.Add(orbit.RepeatPowerBps);
            hash.Add(orbit.RemainingTicks);
            hash.Add(orbit.IntervalTicks);
            hash.Add(orbit.RadiusMilli);
            hash.Add(orbit.HitLimit);
            hash.Add(orbit.HitsApplied);
            hash.Add(orbit.Active);
            hash.Add(orbit.TargetId);
            hash.Add(orbit.NextHitTick);
            hash.Add(orbit.PhaseIndex);
        }

        private static void AppendLegendaryOvercloneHash(
            ref StableHashBuilder hash,
            LegendaryOvercloneRuntime value)
        {
            hash.Add(value != null);
            if (value == null)
            {
                return;
            }
            hash.Add(value.TowerId);
            hash.Add(value.CardId);
            hash.Add(value.CardInstanceId);
            hash.Add(value.AppliedCardIndex);
            hash.Add(value.Direction);
            hash.Add(value.PowerBps);
            hash.Add(value.RemainingGenerations);
            hash.Add(value.InheritedEffectLimit);
        }

        private static void
            AppendLegendaryProjectileDealListHash(
                ref StableHashBuilder hash,
                List<LegendaryProjectileDealRuntime> values)
        {
            LegendaryProjectileDealRuntime[] sorted =
                values.ToArray();
            Array.Sort(
                sorted,
                (left, right) =>
                    left.CardInstanceId.CompareTo(
                        right.CardInstanceId));
            hash.Add(sorted.Length);
            for (int i = 0; i < sorted.Length; i++)
            {
                LegendaryProjectileDealRuntime value =
                    sorted[i];
                hash.Add(value.TowerId);
                hash.Add(value.CardId);
                hash.Add(value.CardInstanceId);
                hash.Add(value.RootChainId);
                hash.Add(value.ActivationId);
                hash.Add(value.AppliedCardIndex);
                hash.Add(value.Direction);
                hash.Add(value.GoldCost);
                hash.Add(value.ReplayPowerBps);
                hash.Add(value.RemainingReplays);
            }
        }

        private static void
            AppendLegendaryEnemyDealListHash(
                ref StableHashBuilder hash,
                List<LegendaryEnemyDealRuntime> values)
        {
            LegendaryEnemyDealRuntime[] sorted =
                values.ToArray();
            Array.Sort(
                sorted,
                (left, right) =>
                    left.CardInstanceId.CompareTo(
                        right.CardInstanceId));
            hash.Add(sorted.Length);
            for (int i = 0; i < sorted.Length; i++)
            {
                LegendaryEnemyDealRuntime value =
                    sorted[i];
                hash.Add(value.TowerId);
                hash.Add(value.CardId);
                hash.Add(value.CardInstanceId);
                hash.Add(value.SourceEntityId);
                hash.Add(value.AppliedCardIndex);
                hash.Add(value.Direction);
                hash.Add(value.GoldAmount);
                hash.Add(value.HealthAndSizeGrowthBps);
                hash.Add(value.SpeedGrowthBps);
                hash.Add(value.RemainingTicks);
                hash.Add(value.IntervalTicks);
                hash.Add(value.PulseLimit);
                hash.Add(value.PulsesApplied);
                hash.Add(value.NextPulseTick);
            }
        }

        private static void
            AppendLegendaryLastCommandListHash(
                ref StableHashBuilder hash,
                List<LegendaryLastCommandRuntime> values)
        {
            LegendaryLastCommandRuntime[] sorted =
                values.ToArray();
            Array.Sort(
                sorted,
                (left, right) =>
                    left.CardInstanceId.CompareTo(
                        right.CardInstanceId));
            hash.Add(sorted.Length);
            for (int i = 0; i < sorted.Length; i++)
            {
                LegendaryLastCommandRuntime value =
                    sorted[i];
                hash.Add(value.TowerId);
                hash.Add(value.CardId);
                hash.Add(value.CardInstanceId);
                hash.Add(value.AppliedCardIndex);
                hash.Add(value.Direction);
                hash.Add(value.PowerBps);
                hash.Add(value.RadiusMilli);
                hash.Add(value.TargetLimit);
                hash.Add(value.RemainingUses);
                hash.Add(value.Used);
            }
        }

        private static void AppendLegendaryFateLockHash(
            ref StableHashBuilder hash,
            LegendaryFateLockRuntime value)
        {
            hash.Add(value != null);
            if (value == null)
            {
                return;
            }
            hash.Add(value.TowerId);
            hash.Add(value.CardId);
            hash.Add(value.CardInstanceId);
            hash.Add(value.AppliedCardIndex);
            hash.Add(value.Direction);
            hash.Add(value.PowerBps);
            hash.Add(value.AccumulatorBps);
            hash.Add(value.RemainingTicks);
            hash.Add(value.Charges);
        }

        private static void AppendLegendaryOverloadHash(
            ref StableHashBuilder hash,
            LegendaryOverloadRuntime value)
        {
            hash.Add(value != null);
            if (value == null)
            {
                return;
            }
            hash.Add(value.TowerId);
            hash.Add(value.CardId);
            hash.Add(value.CardInstanceId);
            hash.Add(value.RootChainId);
            hash.Add(value.ActivationId);
            hash.Add(value.AppliedCardIndex);
            hash.Add(value.Direction);
            hash.Add(value.ReplayPowerBps);
            hash.Add(value.SecondaryPowerBps);
            hash.Add(value.TertiaryPowerBps);
            hash.Add(value.DurationTicks);
            hash.Add(value.RadiusMilli);
            hash.Add(value.ReplayActive);
            hash.Add(value.ReplayIndex);
            hash.Add(value.Completed);
        }

        private sealed class LegendaryProjectileRuntime
        {
            public readonly List<LegendaryDualRuntime> Duals =
                new List<LegendaryDualRuntime>();
            public LegendaryProjectileOrbitRuntime Orbit;
            public LegendaryOvercloneRuntime Overclone;
            public readonly List<LegendaryProjectileDealRuntime> Deals =
                new List<LegendaryProjectileDealRuntime>();
            public readonly List<LegendaryLastCommandRuntime> LastCommands =
                new List<LegendaryLastCommandRuntime>();
            public LegendaryFateLockRuntime FateLock;
            public LegendaryOverloadRuntime Overload;
        }

        private sealed class LegendaryEnemyRuntime
        {
            public LegendaryEnemyOrbitRuntime Orbit;
            public LegendaryOvercloneRuntime Overclone;
            public readonly List<LegendaryEnemyDealRuntime> Deals =
                new List<LegendaryEnemyDealRuntime>();
            public readonly List<LegendaryLastCommandRuntime> LastCommands =
                new List<LegendaryLastCommandRuntime>();
            public LegendaryFateLockRuntime FateLock;
            public LegendaryOverloadRuntime Overload;
            public LegendaryEnemyOverloadBuffRuntime OverloadBuff;
        }

        private sealed class LegendaryRecursionRequest
        {
            public ChainId RootChainId;
            public SubjectType SubjectType;
            public EntityId SubjectId;
            public TowerId TowerId;
            public CardId CardId;
            public int CardInstanceId;
            public int Direction;
            public int PowerBps;
            public bool Consumed;

            public static int Compare(
                LegendaryRecursionRequest left,
                LegendaryRecursionRequest right)
            {
                int result =
                    left.RootChainId.Value.CompareTo(
                        right.RootChainId.Value);
                if (result != 0)
                {
                    return result;
                }
                result = ((int)left.SubjectType).CompareTo(
                    (int)right.SubjectType);
                if (result != 0)
                {
                    return result;
                }
                result = left.SubjectId.Value.CompareTo(
                    right.SubjectId.Value);
                if (result != 0)
                {
                    return result;
                }
                result = left.TowerId.Value.CompareTo(
                    right.TowerId.Value);
                return result != 0
                    ? result
                    : left.CardInstanceId.CompareTo(
                        right.CardInstanceId);
            }
        }

        private sealed class LegendaryDualRuntime
        {
            public TowerId TowerId;
            public CardId CardId;
            public int CardInstanceId;
            public int TargetCardIndex;
            public int Direction;
            public int PowerBps;
            public int RemainingApplications;
            public ChainId RootChainId;
            public ActivationId ActivationId;
            public int AppliedCardIndex;

            public LegendaryDualRuntime Clone()
            {
                return (LegendaryDualRuntime)
                    MemberwiseClone();
            }
        }

        private sealed class LegendaryProjectileOrbitRuntime
        {
            public TowerId TowerId;
            public CardId CardId;
            public int CardInstanceId;
            public int AppliedCardIndex;
            public int Direction;
            public int RepeatPowerBps;
            public int RemainingTicks;
            public int IntervalTicks;
            public int RadiusMilli;
            public int HitLimit;
            public int HitsApplied;
            public bool Active;
            public EntityId TargetId =
                EntityId.Invalid;
            public long NextHitTick;
            public int PhaseIndex;

            public LegendaryProjectileOrbitRuntime
                CloneDetached()
            {
                var clone =
                    (LegendaryProjectileOrbitRuntime)
                    MemberwiseClone();
                clone.Active = false;
                clone.TargetId = EntityId.Invalid;
                clone.HitsApplied = 0;
                clone.NextHitTick = 0;
                clone.PhaseIndex = 0;
                return clone;
            }
        }

        private sealed class LegendaryEnemyOrbitRuntime
        {
            public TowerId TowerId;
            public CardId CardId;
            public int CardInstanceId;
            public int AppliedCardIndex;
            public int Direction;
            public long AnchorProgressMilli;
            public long LoopLengthMilli;
            public long TravelMilli;
            public int RemainingTicks;
            public int HitLimit;
            public int HitsTaken;

            public LegendaryEnemyOrbitRuntime Clone()
            {
                return (LegendaryEnemyOrbitRuntime)
                    MemberwiseClone();
            }
        }

        private sealed class LegendaryOvercloneRuntime
        {
            public TowerId TowerId;
            public CardId CardId;
            public int CardInstanceId;
            public int AppliedCardIndex;
            public int Direction;
            public int PowerBps;
            public int RemainingGenerations;
            public int InheritedEffectLimit;

            public LegendaryOvercloneRuntime
                CloneForChild()
            {
                var clone =
                    (LegendaryOvercloneRuntime)
                    MemberwiseClone();
                clone.RemainingGenerations = Math.Max(
                    0,
                    RemainingGenerations - 1);
                return clone;
            }
        }

        private sealed class LegendaryProjectileDealRuntime
        {
            public TowerId TowerId;
            public CardId CardId;
            public int CardInstanceId;
            public ChainId RootChainId;
            public ActivationId ActivationId;
            public int AppliedCardIndex;
            public int Direction;
            public int GoldCost;
            public int ReplayPowerBps;
            public int RemainingReplays;

            public LegendaryProjectileDealRuntime Clone()
            {
                return (LegendaryProjectileDealRuntime)
                    MemberwiseClone();
            }
        }

        private sealed class LegendaryEnemyDealRuntime
        {
            public TowerId TowerId;
            public CardId CardId;
            public int CardInstanceId;
            public EntityId SourceEntityId;
            public int AppliedCardIndex;
            public int Direction;
            public int GoldAmount;
            public int HealthAndSizeGrowthBps;
            public int SpeedGrowthBps;
            public int RemainingTicks;
            public int IntervalTicks;
            public int PulseLimit;
            public int PulsesApplied;
            public long NextPulseTick;

            public LegendaryEnemyDealRuntime Clone()
            {
                return (LegendaryEnemyDealRuntime)
                    MemberwiseClone();
            }
        }

        private sealed class LegendaryLastCommandRuntime
        {
            public TowerId TowerId;
            public CardId CardId;
            public int CardInstanceId;
            public int AppliedCardIndex;
            public int Direction;
            public int PowerBps;
            public int RadiusMilli;
            public int TargetLimit;
            public int RemainingUses;
            public bool Used;

            public LegendaryLastCommandRuntime Clone()
            {
                return (LegendaryLastCommandRuntime)
                    MemberwiseClone();
            }
        }

        private sealed class LegendaryFateLockRuntime
        {
            public TowerId TowerId;
            public CardId CardId;
            public int CardInstanceId;
            public int AppliedCardIndex;
            public int Direction;
            public int PowerBps;
            public int AccumulatorBps;
            public int RemainingTicks;
            public int Charges;

            public LegendaryFateLockRuntime Clone()
            {
                return (LegendaryFateLockRuntime)
                    MemberwiseClone();
            }
        }

        private sealed class LegendaryOverloadRuntime
        {
            public TowerId TowerId;
            public CardId CardId;
            public int CardInstanceId;
            public ChainId RootChainId;
            public ActivationId ActivationId;
            public int AppliedCardIndex;
            public int Direction;
            public int ReplayPowerBps;
            public int SecondaryPowerBps;
            public int TertiaryPowerBps;
            public int DurationTicks;
            public int RadiusMilli;
            public bool ReplayActive;
            public int ReplayIndex;
            public bool Completed;
        }

        private sealed class
            LegendaryEnemyOverloadBuffRuntime
        {
            public int SpeedBps;
            public int ResistanceBps;
            public int RemainingTicks;
        }
    }
}
