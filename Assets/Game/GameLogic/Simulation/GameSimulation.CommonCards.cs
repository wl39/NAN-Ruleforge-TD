using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 도탄 카드 한 장이 탄환에 부여한 남은 횟수와 감쇠 규칙이다.
    /// 같은 카드를 여러 장 장착했을 때도 카드 실행 순서대로 횟수를 소비한다.
    /// </summary>
    internal sealed class ProjectileRicochetRuntime
    {
        public CardId CardId;
        public int CardInstanceId;
        public int Remaining;
        public int Used;
        public int DamageMultiplierBps;
        public int RadiusMilli;
    }

    /// <summary>
    /// 가속 카드가 적용된 이후 실제로 비행한 거리에 따라 피해 배율을 선형으로 올리는 상태다.
    /// AppliedBonusBps를 따로 저장해 이후 거대화 같은 카드가 바꾼 피해도 보존한다.
    /// </summary>
    internal sealed class ProjectileAccelerationRuntime
    {
        public CardId CardId;
        public int CardInstanceId;
        public long DistanceTravelledMilli;
        public int BonusBpsPerThousandMilli;
        public int MaximumBonusBps;
        public int AppliedBonusBps;

        public ProjectileAccelerationRuntime Clone()
        {
            return (ProjectileAccelerationRuntime)MemberwiseClone();
        }
    }

    /// <summary>
    /// 지연 카드가 탄환을 정지시키는 남은 틱과 해제 순간의 피해 배율이다.
    /// </summary>
    internal sealed class ProjectileDelayRuntime
    {
        public int RemainingTicks;
        public int ReleaseDamageMultiplierBps;

        public ProjectileDelayRuntime Clone()
        {
            return (ProjectileDelayRuntime)MemberwiseClone();
        }
    }

    /// <summary>
    /// 아직 별도 파일이 없던 Common 카드 5장의 권위 전투 규칙이다.
    /// 화면 표현은 PresentationEvent/Snapshot만 읽고 이 상태를 직접 변경하지 않는다.
    /// </summary>
    public sealed partial class GameSimulation
    {
        // Dictionary는 EntityId로만 조회하고, 상태 해시에서는 키를 정렬한다.
        // 따라서 내부 버킷 순서가 Editor/WebGL 결과에 영향을 주지 않는다.
        private readonly Dictionary<int, List<ProjectileRicochetRuntime>>
            commonProjectileRicochets =
                new Dictionary<int, List<ProjectileRicochetRuntime>>();
        private readonly Dictionary<int, List<ProjectileAccelerationRuntime>>
            commonProjectileAccelerations =
                new Dictionary<int, List<ProjectileAccelerationRuntime>>();
        private readonly Dictionary<int, ProjectileDelayRuntime>
            commonProjectileDelays =
                new Dictionary<int, ProjectileDelayRuntime>();
        private readonly Dictionary<int, long>
            commonProjectileTravelDistances =
                new Dictionary<int, long>();

        /// <summary>
        /// 같은 GameSimulation 인스턴스로 새 런을 시작할 때 이전 EntityId의 부가 상태를 지운다.
        /// Initialize의 다른 권위 컬렉션 Clear와 같은 위치에서 한 번 호출해야 한다.
        /// </summary>
        internal void ResetCommonCardRuntime()
        {
            commonProjectileRicochets.Clear();
            commonProjectileAccelerations.Clear();
            commonProjectileDelays.Clear();
            commonProjectileTravelDistances.Clear();
        }

        /// <summary>
        /// 탄환에 도탄 횟수와 가까운 새 대상 검색 반경을 카드 인스턴스별로 부여한다.
        /// 전역 MaxRicochetsPerProjectile을 넘는 횟수는 저장 단계부터 잘라낸다.
        /// </summary>
        internal void ConfigureProjectileRicochet(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile = FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            int alreadyConfigured =
                GetProjectileRicochetsUsed(projectile.Id) +
                GetProjectileRicochetsRemaining(projectile.Id);
            int available = Math.Max(
                0,
                content.Safety.MaxRicochetsPerProjectile -
                alreadyConfigured);
            int granted = Math.Min(
                available,
                Math.Max(0, node.Amount));
            if (granted <= 0)
            {
                return;
            }

            if (!commonProjectileRicochets.TryGetValue(
                    projectile.Id.Value,
                    out List<ProjectileRicochetRuntime> runtimes))
            {
                runtimes = new List<ProjectileRicochetRuntime>(2);
                commonProjectileRicochets.Add(
                    projectile.Id.Value,
                    runtimes);
            }

            runtimes.Add(new ProjectileRicochetRuntime
            {
                CardId = context.CardId,
                CardInstanceId = context.CardInstanceId,
                Remaining = granted,
                DamageMultiplierBps =
                    node.Amount2 > 0 ? node.Amount2 : 10000,
                RadiusMilli = Math.Max(1, node.RadiusMilli)
            });
        }

        /// <summary>
        /// 직접 적 해석의 도탄 상태를 적용한다.
        /// StatusInstance의 ArmorIgnoreBps 칸은 이 상태에서만 충돌 피해 milli를 보존한다.
        /// 해당 필드는 출혈/도탄 계산에서 방어 무시로 읽지 않으므로 의미 충돌이 없다.
        /// </summary>
        internal void ApplyEnemyRicochet(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            ApplyStatusCore(
                enemy,
                context,
                StatusType.Ricochet,
                node);
            StatusInstance status = FindCommonStatus(
                enemy,
                StatusType.Ricochet,
                context.TowerId,
                context.CardId);
            if (status != null)
            {
                status.TickInterval = 0;
                status.ArmorIgnoreBps = Math.Max(
                    status.ArmorIgnoreBps,
                    Math.Max(0, node.Amount2));
            }
        }

        /// <summary>
        /// 적중/관통마다 적용된 출혈이 이후 이동 거리를 누적할 수 있도록 상태를 생성한다.
        /// NextTick은 출혈에서만 1,000 milli 미만의 이동 거리 나머지로 사용한다.
        /// </summary>
        internal void ApplyBleed(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            StatusInstance before = FindCommonStatus(
                enemy,
                StatusType.Bleed,
                context.TowerId,
                context.CardId);
            ApplyStatusCore(enemy, context, StatusType.Bleed, node);
            StatusInstance applied = before ??
                FindCommonStatus(
                    enemy,
                    StatusType.Bleed,
                    context.TowerId,
                    context.CardId);
            if (applied != null)
            {
                // 출혈은 시간 틱이 아니라 이동 거리만으로 발동한다.
                applied.TickInterval = 0;
                if (before == null)
                {
                    applied.NextTick = 0;
                }
            }
        }

        /// <summary>
        /// 탄환의 즉시 속도 배율과 이후 거리 기반 피해 증가 규칙을 설정한다.
        /// Amount=속도 배율, Amount2=1,000 milli당 피해 보너스 bps,
        /// Limit=누적 피해 보너스 상한 bps다.
        /// </summary>
        internal void AccelerateProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile = FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            projectile.SpeedMilliPerTick = ClampPositiveInt(
                DeterministicMath.MultiplyBasisPoints(
                    projectile.SpeedMilliPerTick,
                    Math.Max(1, node.Amount)));

            if (!commonProjectileAccelerations.TryGetValue(
                    projectile.Id.Value,
                    out List<ProjectileAccelerationRuntime> runtimes))
            {
                runtimes = new List<ProjectileAccelerationRuntime>(2);
                commonProjectileAccelerations.Add(
                    projectile.Id.Value,
                    runtimes);
            }

            runtimes.Add(new ProjectileAccelerationRuntime
            {
                CardId = context.CardId,
                CardInstanceId = context.CardInstanceId,
                BonusBpsPerThousandMilli = Math.Max(0, node.Amount2),
                MaximumBonusBps = Math.Max(0, node.Limit)
            });
        }

        /// <summary>
        /// 적 속도와 가계 보상을 함께 늘린다.
        /// Amount=속도 배율, Amount2=기본 보상 대비 증가 bps, Limit=전체 보너스 상한 bps다.
        /// </summary>
        internal void AccelerateEnemy(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            enemy.SpeedMultiplierBps = MultiplyBps(
                enemy.SpeedMultiplierBps,
                Math.Max(1, node.Amount));
            IncreaseEnemyReward(
                enemy,
                context.TowerId,
                context.CardInstanceId,
                RewardAugmentKind.Accelerate,
                Math.Max(0, node.Amount2),
                Math.Max(0, node.Limit));
        }

        /// <summary>
        /// 탄환을 유도 상태로 만들고 현재 시점의 우선 대상을 즉시 다시 고른다.
        /// 이후 MoveProjectiles가 매 틱 같은 결정적 우선순위로 방향을 갱신한다.
        /// </summary>
        internal void EnableProjectileHoming(
            in EffectExecutionContext context)
        {
            ProjectileState projectile = FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            projectile.Homing = true;
            EnemyState target = SelectCommonProjectileTarget(projectile);
            if (target != null)
            {
                projectile.TargetId = target.Id;
                SetProjectileDirection(projectile, target.Position);
            }
        }

        /// <summary>적을 모든 유도 탄환의 우선 대상 상태로 만든다.</summary>
        internal void ApplyHomingPriority(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ApplyStatus(context, StatusType.HomingPriority, node);
        }

        /// <summary>
        /// 탄환을 DurationTicks 동안 정지시키고 마지막 정지 틱에 Amount 피해 배율을 적용한다.
        /// 여러 지연 카드는 정지 시간을 더하고 해제 배율을 카드 순서대로 곱한다.
        /// </summary>
        internal void DelayProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile = FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            int requestedTicks = Math.Max(1, node.DurationTicks);
            int releaseMultiplier =
                node.Amount > 0 ? node.Amount : 10000;
            if (!commonProjectileDelays.TryGetValue(
                    projectile.Id.Value,
                    out ProjectileDelayRuntime runtime))
            {
                runtime = new ProjectileDelayRuntime
                {
                    ReleaseDamageMultiplierBps = 10000
                };
                commonProjectileDelays.Add(
                    projectile.Id.Value,
                    runtime);
            }

            runtime.RemainingTicks = Math.Min(
                content.Safety.MaxProjectileLifetimeTicks,
                checked(runtime.RemainingTicks + requestedTicks));
            runtime.ReleaseDamageMultiplierBps = MultiplyBps(
                runtime.ReleaseDamageMultiplierBps,
                releaseMultiplier);
        }

        /// <summary>
        /// 적의 이동과 특수 행동만 멈추는 지연 상태를 적용한다.
        /// ProcessStatuses는 먼저 실행되므로 화상·중독 등 지속 피해 시간은 정상적으로 흐른다.
        /// </summary>
        internal void ApplyDelay(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ApplyStatus(context, StatusType.Delay, node);
        }

        /// <summary>
        /// 한 적중이 실제 도탄으로 이어질 수 있으면 새 표적/방향/피해를 확정한다.
        /// 대상은 아직 맞히지 않은 반경 안 적 중 거리, 진행도, EntityId 순으로 고른다.
        /// </summary>
        internal bool TryRicochetProjectile(
            ProjectileState projectile,
            EnemyState hitTarget)
        {
            if (projectile == null ||
                hitTarget == null ||
                !projectile.Alive ||
                !commonProjectileRicochets.TryGetValue(
                    projectile.Id.Value,
                    out List<ProjectileRicochetRuntime> runtimes) ||
                GetProjectileRicochetsUsed(projectile.Id) >=
                    content.Safety.MaxRicochetsPerProjectile)
            {
                return false;
            }

            for (int runtimeIndex = 0;
                 runtimeIndex < runtimes.Count;
                 runtimeIndex++)
            {
                ProjectileRicochetRuntime runtime =
                    runtimes[runtimeIndex];
                if (runtime.Remaining <= 0)
                {
                    continue;
                }

                EnemyState next = SelectRicochetTarget(
                    projectile,
                    hitTarget.Position,
                    runtime.RadiusMilli);
                if (next == null)
                {
                    return false;
                }

                runtime.Remaining--;
                runtime.Used++;
                projectile.DamageMilli =
                    DeterministicMath.MultiplyBasisPoints(
                        projectile.DamageMilli,
                        runtime.DamageMultiplierBps);
                projectile.Position = hitTarget.Position;
                projectile.TargetId = next.Id;
                SetProjectileDirection(projectile, next.Position);
                AddPresentation(
                    PresentationEventType.ProjectileRicochet,
                    next.Id.Value,
                    projectile.Id.Value,
                    GetProjectileRicochetsRemaining(projectile.Id),
                    "ricochet");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 적이 밀치기/에어본 같은 강제 이동을 마친 뒤 Ricochet 상태가 있으면
        /// 가까운 적 위치로 튕기고 양쪽 충돌 피해를 원자적으로 예약한다.
        /// </summary>
        internal bool TryEnemyRicochetAfterForcedMovement(
            EnemyState enemy,
            in EffectExecutionContext context)
        {
            if (enemy == null || !enemy.Alive)
            {
                return false;
            }

            StatusInstance status = FindStrongestActiveStatus(
                enemy,
                StatusType.Ricochet);
            if (status == null)
            {
                return false;
            }

            EnemyState target = SelectEnemyRicochetTarget(
                enemy,
                Math.Max(1, status.RadiusMilli));
            if (target == null)
            {
                return false;
            }

            long collisionDamage = Math.Max(
                1,
                (long)Math.Max(1, status.ArmorIgnoreBps) *
                Math.Max(1, status.Stacks));
            var damageEvents = new List<GameEvent>(2);
            if (TryCreateDamageEvent(
                    enemy.Id,
                    status.SourceTowerId,
                    status.SourceCardId,
                    status.SourceEntityId,
                    collisionDamage,
                    DamageKind.Collision,
                    0,
                    context.RootChainId,
                    context.ActivationId,
                    context.ParentEventId,
                    context.Depth + 1,
                    EventTags.Control | EventTags.SingleTarget,
                    out GameEvent selfDamage))
            {
                damageEvents.Add(selfDamage);
            }
            if (TryCreateDamageEvent(
                    target.Id,
                    status.SourceTowerId,
                    status.SourceCardId,
                    status.SourceEntityId,
                    collisionDamage,
                    DamageKind.Collision,
                    0,
                    context.RootChainId,
                    context.ActivationId,
                    context.ParentEventId,
                    context.Depth + 1,
                    EventTags.Control | EventTags.SingleTarget,
                    out GameEvent targetDamage))
            {
                damageEvents.Add(targetDamage);
            }

            if (damageEvents.Count != 2 ||
                !TryEnqueueBatch(damageEvents))
            {
                return false;
            }

            long previousProgress = enemy.PathProgressMilli;
            enemy.PathProgressMilli = target.PathProgressMilli;
            RefreshEnemyPosition(enemy);
            spatialIndex.Rebuild(enemies);
            long ricochetDistance = Math.Abs(
                enemy.PathProgressMilli -
                previousProgress);
            TriggerBleedFromMovement(
                enemy,
                ricochetDistance,
                context);
            AddPresentation(
                PresentationEventType.EnemyRicochet,
                enemy.Id.Value,
                target.Id.Value,
                (int)Math.Min(
                    int.MaxValue,
                    ricochetDistance),
                "ricochet");
            return true;
        }

        /// <summary>
        /// 실제 경로 이동량을 출혈 상태별로 누적하고 1,000 milli마다 물리 피해를 예약한다.
        /// 강제 이동은 원래 EffectContext를 전달하고, 일반 이동은 아래 overload가 새 체인을 만든다.
        /// </summary>
        internal void TriggerBleedFromMovement(
            EnemyState enemy,
            long distanceMilli,
            in EffectExecutionContext movementContext)
        {
            TriggerBleedFromMovementCore(
                enemy,
                distanceMilli,
                movementContext.RootChainId,
                movementContext.ActivationId,
                movementContext.ParentEventId,
                movementContext.Depth + 1);
        }

        /// <summary>일반 경로 이동이 발생시킨 출혈 피해를 독립 RootChain으로 예약한다.</summary>
        internal void TriggerBleedFromMovement(
            EnemyState enemy,
            long distanceMilli)
        {
            TriggerBleedFromMovementCore(
                enemy,
                distanceMilli,
                ChainId.Invalid,
                ActivationId.Invalid,
                EventId.Invalid,
                0);
        }

        /// <summary>
        /// 지연 중인 탄환이면 남은 틱을 하나 소비하고 이번 틱 이동을 막는다.
        /// 0이 된 순간 해제 피해 배율을 적용하지만 실제 이동은 다음 틱부터 재개한다.
        /// </summary>
        internal bool ShouldPauseProjectileForDelay(
            ProjectileState projectile)
        {
            if (projectile == null ||
                !commonProjectileDelays.TryGetValue(
                    projectile.Id.Value,
                    out ProjectileDelayRuntime runtime) ||
                runtime.RemainingTicks <= 0)
            {
                return false;
            }

            runtime.RemainingTicks--;
            if (runtime.RemainingTicks == 0)
            {
                projectile.DamageMilli =
                    DeterministicMath.MultiplyBasisPoints(
                        projectile.DamageMilli,
                        runtime.ReleaseDamageMultiplierBps);
                commonProjectileDelays.Remove(
                    projectile.Id.Value);
            }

            return true;
        }

        /// <summary>적 지연 상태가 남아 있으면 이번 틱의 이동/특수 행동을 막는다.</summary>
        internal bool IsEnemyDelayed(EnemyState enemy)
        {
            return enemy != null &&
                   HasActiveStatus(enemy, StatusType.Delay);
        }

        /// <summary>
        /// 이동이 확정된 탄환의 실제 직선거리를 누적하고 모든 가속 카드의 피해 보너스를 갱신한다.
        /// </summary>
        internal void RecordCommonProjectileMovement(
            ProjectileState projectile,
            SimPosition previousPosition)
        {
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            long distance = PathModel.DistanceMilli(
                previousPosition,
                projectile.Position);
            if (distance <= 0)
            {
                return;
            }

            commonProjectileTravelDistances.TryGetValue(
                projectile.Id.Value,
                out long previousTotal);
            commonProjectileTravelDistances[
                projectile.Id.Value] = SaturatingAdd(
                    previousTotal,
                    distance);

            if (!commonProjectileAccelerations.TryGetValue(
                    projectile.Id.Value,
                    out List<ProjectileAccelerationRuntime> runtimes))
            {
                return;
            }

            for (int i = 0; i < runtimes.Count; i++)
            {
                ProjectileAccelerationRuntime runtime = runtimes[i];
                runtime.DistanceTravelledMilli = SaturatingAdd(
                    runtime.DistanceTravelledMilli,
                    distance);
                long rawBonus = DeterministicMath.MultiplyDivide(
                    runtime.DistanceTravelledMilli,
                    runtime.BonusBpsPerThousandMilli,
                    1000);
                int nextBonus = (int)Math.Min(
                    Math.Max(0, runtime.MaximumBonusBps),
                    Math.Min(int.MaxValue, Math.Max(0, rawBonus)));
                if (nextBonus <= runtime.AppliedBonusBps)
                {
                    continue;
                }

                int oldFactor = checked(
                    10000 + runtime.AppliedBonusBps);
                int newFactor = checked(10000 + nextBonus);
                projectile.DamageMilli =
                    DeterministicMath.MultiplyDivide(
                        projectile.DamageMilli,
                        newFactor,
                        oldFactor);
                runtime.AppliedBonusBps = nextBonus;
            }
        }

        /// <summary>
        /// 분열 자식은 이전 바인딩인 도탄은 상속하지 않지만,
        /// 이미 물리 수치를 바꾼 가속/지연 규칙은 독립 복사해 이어받는다.
        /// </summary>
        internal void InheritCommonProjectileRuntime(
            ProjectileState original,
            ProjectileState child)
        {
            if (original == null || child == null)
            {
                return;
            }

            if (commonProjectileAccelerations.TryGetValue(
                    original.Id.Value,
                    out List<ProjectileAccelerationRuntime> accelerations))
            {
                var clones =
                    new List<ProjectileAccelerationRuntime>(
                        accelerations.Count);
                for (int i = 0; i < accelerations.Count; i++)
                {
                    clones.Add(accelerations[i].Clone());
                }
                commonProjectileAccelerations.Add(
                    child.Id.Value,
                    clones);
            }

            if (commonProjectileDelays.TryGetValue(
                    original.Id.Value,
                    out ProjectileDelayRuntime delay))
            {
                commonProjectileDelays.Add(
                    child.Id.Value,
                    delay.Clone());
            }

            // 새 탄환 자체가 날아간 거리는 0에서 시작한다.
            commonProjectileTravelDistances[
                child.Id.Value] = 0;
        }

        /// <summary>소멸한 탄환의 부가 상태를 모두 제거해 장시간 WebGL 런의 메모리를 제한한다.</summary>
        internal void ForgetCommonProjectileRuntime(EntityId projectileId)
        {
            commonProjectileRicochets.Remove(projectileId.Value);
            commonProjectileAccelerations.Remove(projectileId.Value);
            commonProjectileDelays.Remove(projectileId.Value);
            commonProjectileTravelDistances.Remove(projectileId.Value);
        }

        internal int GetProjectileRicochetsUsed(EntityId projectileId)
        {
            if (!commonProjectileRicochets.TryGetValue(
                    projectileId.Value,
                    out List<ProjectileRicochetRuntime> runtimes))
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < runtimes.Count; i++)
            {
                total = checked(total + runtimes[i].Used);
            }
            return total;
        }

        internal int GetProjectileRicochetsRemaining(
            EntityId projectileId)
        {
            if (!commonProjectileRicochets.TryGetValue(
                    projectileId.Value,
                    out List<ProjectileRicochetRuntime> runtimes))
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < runtimes.Count; i++)
            {
                total = checked(total + runtimes[i].Remaining);
            }
            return total;
        }

        internal long GetProjectileTravelDistanceMilli(
            EntityId projectileId)
        {
            return commonProjectileTravelDistances.TryGetValue(
                projectileId.Value,
                out long distance)
                ? distance
                : 0L;
        }

        internal int GetProjectileDelayRemainingTicks(
            EntityId projectileId)
        {
            return commonProjectileDelays.TryGetValue(
                projectileId.Value,
                out ProjectileDelayRuntime runtime)
                ? Math.Max(0, runtime.RemainingTicks)
                : 0;
        }

        /// <summary>Snapshot에 노출할 Common 카드 투사체 표현 비트를 계산한다.</summary>
        internal ProjectileEffectVisualFlags
            GetCommonProjectileVisualFlags(
            ProjectileState projectile)
        {
            if (projectile == null)
            {
                return ProjectileEffectVisualFlags.None;
            }

            ProjectileEffectVisualFlags flags =
                ProjectileEffectVisualFlags.None;
            if (commonProjectileRicochets.ContainsKey(
                    projectile.Id.Value))
            {
                flags |= ProjectileEffectVisualFlags.Ricochet;
            }
            for (int i = 0; i < projectile.Bindings.Count; i++)
            {
                if (projectile.Bindings[i].Kind ==
                    BindingKind.Bleed)
                {
                    flags |= ProjectileEffectVisualFlags.Bleed;
                    break;
                }
            }
            if (commonProjectileAccelerations.ContainsKey(
                    projectile.Id.Value))
            {
                flags |= ProjectileEffectVisualFlags.Accelerate;
            }
            if (projectile.Homing)
            {
                flags |= ProjectileEffectVisualFlags.Homing;
            }
            if (commonProjectileDelays.ContainsKey(
                    projectile.Id.Value))
            {
                flags |= ProjectileEffectVisualFlags.Delay;
            }
            return flags;
        }

        /// <summary>
        /// 중앙 ComputeStateHash 끝부분에서 호출해 별도 Dictionary 상태도 리플레이 지문에 넣는다.
        /// </summary>
        internal void AppendCommonCardStateHash(
            ref StableHashBuilder hash)
        {
            AppendRicochetRuntimeHash(ref hash);
            AppendAccelerationRuntimeHash(ref hash);
            AppendDelayRuntimeHash(ref hash);
            AppendTravelDistanceHash(ref hash);
        }

        /// <summary>
        /// HomingPriority, Mark, 거리, 경로 진행도, EntityId 순으로 유도 대상을 고른다.
        /// </summary>
        internal EnemyState SelectCommonProjectileTarget(
            ProjectileState projectile)
        {
            EnemyState selected = null;
            bool selectedPriority = false;
            bool selectedMarked = false;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState enemy = enemies[i];
                if (!enemy.Alive ||
                    enemy.DeathQueued ||
                    projectile.HitEnemies.Contains(enemy.Id.Value))
                {
                    continue;
                }

                bool priority = HasActiveStatus(
                    enemy,
                    StatusType.HomingPriority);
                bool marked = HasActiveStatus(enemy, StatusType.Mark);
                if (selected == null ||
                    (priority && !selectedPriority) ||
                    (priority == selectedPriority &&
                     marked && !selectedMarked) ||
                    (priority == selectedPriority &&
                     marked == selectedMarked &&
                     CompareTargetPriority(
                         projectile.Position,
                         enemy,
                         selected) < 0))
                {
                    selected = enemy;
                    selectedPriority = priority;
                    selectedMarked = marked;
                }
            }
            return selected;
        }

        private void TriggerBleedFromMovementCore(
            EnemyState enemy,
            long distanceMilli,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth)
        {
            if (enemy == null ||
                !enemy.Alive ||
                distanceMilli <= 0)
            {
                return;
            }

            for (int i = 0; i < enemy.Statuses.Count; i++)
            {
                StatusInstance status = enemy.Statuses[i];
                if (status.Type != StatusType.Bleed ||
                    status.RemainingTicks <= 0)
                {
                    continue;
                }

                long accumulated = SaturatingAdd(
                    Math.Max(0, status.NextTick),
                    distanceMilli);
                long travelledUnits = accumulated / 1000L;
                if (travelledUnits <= 0)
                {
                    status.NextTick = accumulated;
                    continue;
                }

                long damage = checked(
                    (long)Math.Max(1, status.Intensity) *
                    Math.Max(1, status.Stacks) *
                    travelledUnits);
                ChainId damageChain = rootChainId.IsValid
                    ? rootChainId
                    : CreateRootChain();
                ActivationId damageActivation =
                    activationId.IsValid
                        ? activationId
                        : CreateActivation();
                if (EnqueueDamage(
                        enemy.Id,
                        status.SourceTowerId,
                        status.SourceCardId,
                        status.SourceEntityId,
                        damage,
                        DamageKind.Physical,
                        0,
                        damageChain,
                        damageActivation,
                        parentEventId,
                        depth,
                        EventTags.DamageOverTime |
                        EventTags.SingleTarget))
                {
                    status.NextTick =
                        accumulated % 1000L;
                }
                else
                {
                    // 예산 실패 시 이동 거리를 소비하지 않고 다음 이동 때 다시 시도한다.
                    status.NextTick = accumulated;
                }
            }
        }

        private EnemyState SelectRicochetTarget(
            ProjectileState projectile,
            SimPosition origin,
            int radiusMilli)
        {
            EnemyState selected = null;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState candidate = enemies[i];
                if (!candidate.Alive ||
                    candidate.DeathQueued ||
                    projectile.HitEnemies.Contains(
                        candidate.Id.Value) ||
                    !PathModel.IsWithin(
                        origin,
                        candidate.Position,
                        radiusMilli))
                {
                    continue;
                }

                if (selected == null ||
                    CompareTargetPriority(
                        origin,
                        candidate,
                        selected) < 0)
                {
                    selected = candidate;
                }
            }
            return selected;
        }

        private EnemyState SelectEnemyRicochetTarget(
            EnemyState source,
            int radiusMilli)
        {
            EnemyState selected = null;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState candidate = enemies[i];
                if (!candidate.Alive ||
                    candidate.DeathQueued ||
                    candidate.Id == source.Id ||
                    !PathModel.IsWithin(
                        source.Position,
                        candidate.Position,
                        radiusMilli))
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

        private static StatusInstance FindCommonStatus(
            EnemyState enemy,
            StatusType type,
            TowerId towerId,
            CardId cardId)
        {
            for (int i = 0; i < enemy.Statuses.Count; i++)
            {
                StatusInstance status = enemy.Statuses[i];
                if (status.Type == type &&
                    status.SourceTowerId == towerId &&
                    status.SourceCardId == cardId)
                {
                    return status;
                }
            }
            return null;
        }

        private static StatusInstance FindStrongestActiveStatus(
            EnemyState enemy,
            StatusType type)
        {
            StatusInstance selected = null;
            for (int i = 0; i < enemy.Statuses.Count; i++)
            {
                StatusInstance candidate = enemy.Statuses[i];
                if (candidate.Type != type ||
                    candidate.RemainingTicks <= 0)
                {
                    continue;
                }

                if (selected == null ||
                    candidate.Intensity > selected.Intensity ||
                    (candidate.Intensity == selected.Intensity &&
                     candidate.Stacks > selected.Stacks) ||
                    (candidate.Intensity == selected.Intensity &&
                     candidate.Stacks == selected.Stacks &&
                     candidate.InstanceId < selected.InstanceId))
                {
                    selected = candidate;
                }
            }
            return selected;
        }

        private void AppendRicochetRuntimeHash(
            ref StableHashBuilder hash)
        {
            int[] keys = new int[
                commonProjectileRicochets.Count];
            commonProjectileRicochets.Keys.CopyTo(keys, 0);
            Array.Sort(keys);
            hash.Add(keys.Length);
            for (int keyIndex = 0;
                 keyIndex < keys.Length;
                 keyIndex++)
            {
                int key = keys[keyIndex];
                hash.Add(key);
                List<ProjectileRicochetRuntime> values =
                    commonProjectileRicochets[key];
                hash.Add(values.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    ProjectileRicochetRuntime value = values[i];
                    hash.Add(value.CardId);
                    hash.Add(value.CardInstanceId);
                    hash.Add(value.Remaining);
                    hash.Add(value.Used);
                    hash.Add(value.DamageMultiplierBps);
                    hash.Add(value.RadiusMilli);
                }
            }
        }

        private void AppendAccelerationRuntimeHash(
            ref StableHashBuilder hash)
        {
            int[] keys = new int[
                commonProjectileAccelerations.Count];
            commonProjectileAccelerations.Keys.CopyTo(keys, 0);
            Array.Sort(keys);
            hash.Add(keys.Length);
            for (int keyIndex = 0;
                 keyIndex < keys.Length;
                 keyIndex++)
            {
                int key = keys[keyIndex];
                hash.Add(key);
                List<ProjectileAccelerationRuntime> values =
                    commonProjectileAccelerations[key];
                hash.Add(values.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    ProjectileAccelerationRuntime value = values[i];
                    hash.Add(value.CardId);
                    hash.Add(value.CardInstanceId);
                    hash.Add(value.DistanceTravelledMilli);
                    hash.Add(value.BonusBpsPerThousandMilli);
                    hash.Add(value.MaximumBonusBps);
                    hash.Add(value.AppliedBonusBps);
                }
            }
        }

        private void AppendDelayRuntimeHash(
            ref StableHashBuilder hash)
        {
            int[] keys =
                new int[commonProjectileDelays.Count];
            commonProjectileDelays.Keys.CopyTo(keys, 0);
            Array.Sort(keys);
            hash.Add(keys.Length);
            for (int i = 0; i < keys.Length; i++)
            {
                ProjectileDelayRuntime value =
                    commonProjectileDelays[keys[i]];
                hash.Add(keys[i]);
                hash.Add(value.RemainingTicks);
                hash.Add(value.ReleaseDamageMultiplierBps);
            }
        }

        private void AppendTravelDistanceHash(
            ref StableHashBuilder hash)
        {
            int[] keys =
                new int[commonProjectileTravelDistances.Count];
            commonProjectileTravelDistances.Keys.CopyTo(keys, 0);
            Array.Sort(keys);
            hash.Add(keys.Length);
            for (int i = 0; i < keys.Length; i++)
            {
                hash.Add(keys[i]);
                hash.Add(commonProjectileTravelDistances[keys[i]]);
            }
        }

        private static int ClampPositiveInt(long value)
        {
            return (int)Math.Max(
                1L,
                Math.Min(int.MaxValue, value));
        }

        private static long SaturatingAdd(long left, long right)
        {
            if (right > 0 && left > long.MaxValue - right)
            {
                return long.MaxValue;
            }
            if (right < 0 && left < long.MinValue - right)
            {
                return long.MinValue;
            }
            return left + right;
        }
    }

    /// <summary>
    /// Common 5장의 데이터 operation을 GameSimulation의 전용 규칙 메서드로 연결한다.
    /// EffectRegistry는 operation별로 이 executor 인스턴스를 하나씩 등록한다.
    /// </summary>
    internal sealed class CommonCardEffectExecutor :
        IEffectExecutor
    {
        private readonly EffectOperation operation;

        public CommonCardEffectExecutor(
            EffectOperation operation)
        {
            this.operation = operation;
        }

        public EffectExecutionOutcome Execute(
            GameSimulation simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            switch (operation)
            {
                case EffectOperation.ConfigureProjectileRicochet:
                    simulation.ConfigureProjectileRicochet(
                        context,
                        node);
                    break;
                case EffectOperation.ApplyEnemyRicochet:
                    simulation.ApplyEnemyRicochet(
                        context,
                        node);
                    break;
                case EffectOperation.BindBleed:
                    simulation.AddProjectileBinding(
                        context,
                        BindingTrigger.OnHit,
                        BindingKind.Bleed,
                        node);
                    break;
                case EffectOperation.ApplyBleed:
                    simulation.ApplyBleed(context, node);
                    break;
                case EffectOperation.AccelerateProjectile:
                    simulation.AccelerateProjectile(
                        context,
                        node);
                    break;
                case EffectOperation.AccelerateEnemy:
                    simulation.AccelerateEnemy(
                        context,
                        node);
                    break;
                case EffectOperation.EnableProjectileHoming:
                    simulation.EnableProjectileHoming(context);
                    break;
                case EffectOperation.ApplyHomingPriority:
                    simulation.ApplyHomingPriority(
                        context,
                        node);
                    break;
                case EffectOperation.DelayProjectile:
                    simulation.DelayProjectile(context, node);
                    break;
                case EffectOperation.ApplyDelay:
                    simulation.ApplyDelay(context, node);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unsupported common card operation " +
                        operation + ".");
            }

            return EffectExecutionOutcome.Continue();
        }
    }
}
