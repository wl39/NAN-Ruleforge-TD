using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 희귀 카드 중 처형·기생·환생·연쇄의 권위 상태와 지연 실행 규칙을 보관한다.
    /// 모든 파생 피해와 카드 프로그램은 기존 EventQueue/ChainBudget을 통과한다.
    /// </summary>
    public sealed partial class GameSimulation
    {
        private const int DefaultExecuteThresholdBps = 2500;
        private const int DefaultExecuteDamageBps = 30000;
        private const int DefaultParasiteDamageBps = 5000;
        private const int DefaultParasiteDamageMilli = 1500;
        private const int DefaultParasiteDurationTicks = 90;
        private const int DefaultParasiteIntervalTicks = 15;
        private const int DefaultRareTransferRadiusMilli = 3000;
        private const int DefaultProjectileRebirthDamageBps = 13000;
        private const int DefaultEnemyRebirthHealthBps = 3000;
        private const int DefaultRebirthSpeedBps = 7500;
        private const int DefaultRebirthProjectileLifetimeTicks = 45;
        private const int DefaultChainPowerBps = 6500;

        private readonly Dictionary<int, RareProjectileRuntime>
            rareDeathChainProjectiles =
                new Dictionary<int, RareProjectileRuntime>();
        private readonly Dictionary<int, RareEnemyRuntime>
            rareDeathChainEnemies =
                new Dictionary<int, RareEnemyRuntime>();
        private readonly Dictionary<int, int> rareChainScaleByActivation =
            new Dictionary<int, int>();
        private readonly HashSet<RareChainVisitKey> rareChainVisits =
            new HashSet<RareChainVisitKey>();
        private readonly List<int> rareDeathChainKeyScratch =
            new List<int>();

        /// <summary>
        /// 희귀 4종 executor의 공통 진입점이다. operation은 데이터가 선택한
        /// 탄환/적 해석이며 지연 상태는 대상 EntityId에 격리해 보관한다.
        /// </summary>
        internal void ExecuteRareDeathChainEffect(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node)
        {
            switch (operation)
            {
                case EffectOperation.EnableProjectileExecute:
                    ConfigureProjectileExecute(context, node);
                    break;
                case EffectOperation.ApplyEnemyExecute:
                    ApplyEnemyExecute(context, node);
                    break;
                case EffectOperation.EnableProjectileParasite:
                    ConfigureProjectileParasite(context, node);
                    break;
                case EffectOperation.ApplyEnemyParasite:
                    ApplyEnemyParasite(context, node);
                    break;
                case EffectOperation.EnableProjectileRebirth:
                    ConfigureProjectileRebirth(context, node);
                    break;
                case EffectOperation.ApplyEnemyRebirth:
                    ApplyEnemyRebirth(context, node);
                    break;
                case EffectOperation.EnableProjectileChain:
                    ConfigureProjectileChain(context, node);
                    break;
                case EffectOperation.ApplyEnemyChain:
                    ApplyEnemyChain(context, node);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(operation),
                        operation,
                        "Unsupported rare death/chain operation.");
            }
        }

        /// <summary>
        /// 처형 탄환의 적중 피해를 대상의 적중 전 체력 비율로 결정한다.
        /// 실제 방어·취약 계산은 이후 EnqueueDamage가 그대로 수행한다.
        /// </summary>
        internal long ModifyRareProjectileHitDamage(
            ProjectileState projectile,
            EnemyState target,
            long damage,
            in GameEvent hitEvent)
        {
            if (projectile == null ||
                target == null ||
                damage <= 0 ||
                !rareDeathChainProjectiles.TryGetValue(
                    projectile.Id.Value,
                    out RareProjectileRuntime runtime) ||
                runtime.Execute == null ||
                runtime.Execute.Consumed ||
                !IsAtOrBelowHealthThreshold(
                    target,
                    ResolveExecuteThreshold(
                        runtime.Execute.Node)))
            {
                return damage;
            }

            runtime.Execute.Consumed = true;
            runtime.Execute.ForceExpiration = true;
            int damageBps = runtime.Execute.Node.Amount2 > 0
                ? runtime.Execute.Node.Amount2
                : DefaultExecuteDamageBps;
            long increased = DeterministicMath.MultiplyBasisPoints(
                damage,
                Math.Max(10000, damageBps));
            AddRareDeathChainPresentation(
                "execute",
                target.Id,
                projectile.Id,
                increased);
            return Math.Max(damage, increased);
        }

        /// <summary>
        /// 기본 적중 바인딩이 실행된 뒤 호출한다. 처형은 즉시 탄환 소멸을 예약하고,
        /// 기생은 적에게 부착해 이후 일반 관통/도탄/소멸 처리를 인수한다.
        /// true이면 호출자가 이번 적중의 일반 생존 판정을 건너뛰어야 한다.
        /// </summary>
        internal bool HandleRareProjectileHit(
            ProjectileState projectile,
            EnemyState target,
            in GameEvent hitEvent)
        {
            if (projectile == null ||
                target == null ||
                !rareDeathChainProjectiles.TryGetValue(
                    projectile.Id.Value,
                    out RareProjectileRuntime runtime))
            {
                return false;
            }

            if (runtime.Execute != null &&
                runtime.Execute.ForceExpiration)
            {
                runtime.Execute.ForceExpiration = false;
                projectile.PierceRemaining = 0;
                ScheduleProjectileExpiration(
                    projectile,
                    hitEvent.EventId);
                return true;
            }

            if (runtime.Parasite != null)
            {
                RareProjectileParasiteRuntime parasite =
                    runtime.Parasite;
                if (!parasite.Attached)
                {
                    parasite.Attached = true;
                    parasite.TargetId = target.Id;
                    parasite.RemainingTicks = ResolveDuration(
                        parasite.Node);
                    parasite.NextPulseTick =
                        tick + ResolveInterval(parasite.Node);
                    projectile.Position = target.Position;
                    projectile.TargetId = target.Id;
                    projectile.Homing = false;
                    projectile.ExpirationQueued = false;
                    projectile.LifetimeRemaining = Math.Max(
                        projectile.LifetimeRemaining,
                        checked(parasite.RemainingTicks + 1));
                    AddRareDeathChainPresentation(
                        "parasite_attach",
                        target.Id,
                        projectile.Id,
                        parasite.RemainingTicks);
                    return true;
                }

                if (parasite.PulsePending &&
                    parasite.PulseActivationId ==
                    hitEvent.ActivationId)
                {
                    projectile.DamageMilli =
                        parasite.DamageBeforePulse;
                    parasite.PulsePending = false;
                    parasite.PulseActivationId =
                        ActivationId.Invalid;
                    AddRareDeathChainPresentation(
                        "parasite_tick",
                        target.Id,
                        projectile.Id,
                        parasite.PulsesCompleted);
                    return true;
                }

                if (parasite.Attached &&
                    parasite.TargetId == target.Id)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// MoveProjectiles의 일반 이동 전에 호출한다. 부착된 기생 탄환은 숙주를
        /// 따라가며, 정해진 틱마다 남은 카드 프로그램과 재적중을 큐에 예약한다.
        /// </summary>
        internal bool ProcessRareProjectileTick(
            ProjectileState projectile)
        {
            if (projectile == null ||
                !projectile.Alive ||
                !rareDeathChainProjectiles.TryGetValue(
                    projectile.Id.Value,
                    out RareProjectileRuntime runtime) ||
                runtime.Parasite == null ||
                !runtime.Parasite.Attached)
            {
                return false;
            }

            RareProjectileParasiteRuntime parasite =
                runtime.Parasite;
            EnemyState target = FindEnemy(parasite.TargetId);
            if (target == null ||
                !target.Alive ||
                target.DeathQueued)
            {
                ScheduleProjectileExpiration(
                    projectile,
                    parasite.LastParentEventId);
                return true;
            }

            projectile.Position = target.Position;
            projectile.TargetId = target.Id;
            parasite.RemainingTicks--;
            if (parasite.RemainingTicks <= 0 ||
                (parasite.Node.Limit > 0 &&
                 parasite.PulsesCompleted >=
                 parasite.Node.Limit))
            {
                ScheduleProjectileExpiration(
                    projectile,
                    parasite.LastParentEventId);
                return true;
            }

            if (parasite.PulsePending ||
                tick < parasite.NextPulseTick)
            {
                return true;
            }

            parasite.NextPulseTick +=
                ResolveInterval(parasite.Node);
            parasite.PulsesCompleted++;
            parasite.PulsePending = true;
            parasite.DamageBeforePulse =
                projectile.DamageMilli;
            projectile.DamageMilli =
                DeterministicMath.MultiplyBasisPoints(
                    projectile.DamageMilli,
                    parasite.Node.Amount > 0
                        ? Math.Min(
                            10000,
                            parasite.Node.Amount)
                        : DefaultParasiteDamageBps);

            // 오른쪽 카드가 지난 pulse에 만든 바인딩을 제거한 뒤 다시 실행해
            // pulse 횟수만큼 바인딩 개수가 누적되는 것을 막는다.
            while (projectile.Bindings.Count >
                   parasite.BindingStartIndex)
            {
                projectile.Bindings.RemoveAt(
                    projectile.Bindings.Count - 1);
            }

            ChainId pulseChain = CreateRootChain();
            ActivationId pulseActivation =
                CreateActivation();
            projectile.RootChainId = pulseChain;
            projectile.ActivationId = pulseActivation;
            parasite.PulseActivationId =
                pulseActivation;
            projectile.HitEnemies.Remove(target.Id.Value);

            if (parasite.NextCardIndex >= 0 &&
                EnqueueProgram(
                    SubjectType.Projectile,
                    projectile.Id,
                    parasite.TowerId,
                    parasite.NextCardIndex,
                    pulseChain,
                    pulseActivation,
                    parasite.LastParentEventId,
                    parasite.Depth + 1,
                    EventPhase.Projectile))
            {
                return true;
            }

            // 기생 뒤에 카드가 없거나 카드 실행 예약이 거절돼도 약화된
            // 기생 재적중 자체는 별도 예산을 통과한 경우에만 실행한다.
            QueueParasitePulseHit(
                projectile,
                target,
                parasite,
                pulseChain,
                pulseActivation,
                parasite.LastParentEventId,
                parasite.Depth + 1);
            return true;
        }

        /// <summary>
        /// 카드 배열의 마지막 카드가 끝난 지점에서 호출한다. 기생 pulse는 이 시점에
        /// 합성 적중을 예약하고, 탄환 연쇄는 가까운 미방문 탄환에 같은 프로그램을 전달한다.
        /// </summary>
        internal void HandleRareProgramCompleted(
            SubjectType subjectType,
            EntityId subjectId,
            TowerId towerId,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth)
        {
            if (subjectType == SubjectType.Projectile &&
                rareDeathChainProjectiles.TryGetValue(
                    subjectId.Value,
                    out RareProjectileRuntime projectileRuntime))
            {
                RareProjectileParasiteRuntime parasite =
                    projectileRuntime.Parasite;
                if (parasite != null &&
                    parasite.Attached &&
                    parasite.PulsePending &&
                    parasite.PulseActivationId == activationId)
                {
                    ProjectileState projectile =
                        FindProjectile(subjectId);
                    EnemyState target =
                        FindEnemy(parasite.TargetId);
                    if (projectile != null &&
                        target != null &&
                        target.Alive)
                    {
                        QueueParasitePulseHit(
                            projectile,
                            target,
                            parasite,
                            rootChainId,
                            activationId,
                            parentEventId,
                            depth + 1);
                    }
                    else
                    {
                        RestoreParasitePulseDamage(
                            projectile,
                            parasite);
                    }
                }

                TriggerProjectileChains(
                    subjectId,
                    towerId,
                    rootChainId,
                    activationId,
                    parentEventId,
                    depth);
            }

            // 연쇄로 전달된 프로그램의 위력 배율은 해당 activation이 끝나면
            // 바로 제거한다. 같은 대상의 다음 정상 공격에 새지 않는다.
            rareChainScaleByActivation.Remove(
                activationId.Value);
        }

        /// <summary>
        /// 연쇄로 전달된 activation의 효과 노드 수치를 약화한다.
        /// 대상 수처럼 구조를 나타내는 작은 정수는 유지하고 위력·시간 수치만 줄인다.
        /// </summary>
        internal CompiledEffectNode ScaleRareChainNode(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            if (!rareChainScaleByActivation.TryGetValue(
                    context.ActivationId.Value,
                    out int scaleBps) ||
                scaleBps >= 10000)
            {
                return node;
            }

            int amount = IsProgramStructuralAmount(node.Operation)
                ? node.Amount
                : ScalePositive(node.Amount, scaleBps);
            return new CompiledEffectNode(
                node.Operation,
                amount,
                ScalePositive(node.Amount2, scaleBps),
                ScalePositive(node.Amount3, scaleBps),
                ScalePositive(node.DurationTicks, scaleBps),
                node.IntervalTicks,
                node.MaxStacks,
                node.RadiusMilli,
                node.Limit,
                node.ChanceBps,
                node.ReferenceId);
        }

        /// <summary>
        /// 적에게 실제 피해가 반영된 직후 호출한다. 처형 표식의 임계값을 처음
        /// 넘은 한 번만 잔여 체력 피해 이벤트로 바꿔 큐에 넣는다.
        /// </summary>
        internal void HandleRareEnemyDamaged(
            EnemyState enemy,
            in GameEvent damageEvent)
        {
            if (enemy == null ||
                !enemy.Alive ||
                enemy.HealthMilli <= 0 ||
                !rareDeathChainEnemies.TryGetValue(
                    enemy.Id.Value,
                    out RareEnemyRuntime runtime))
            {
                return;
            }

            TryTriggerEnemyExecute(
                enemy,
                runtime,
                damageEvent.RootChainId,
                damageEvent.ActivationId,
                damageEvent.EventId,
                damageEvent.Depth + 1);
        }

        /// <summary>
        /// 적 사망 처리의 가장 앞에서 호출한다. 한 번뿐인 환생이 있으면 사망을
        /// 취소하고 true를 반환한다. 최종 사망이면 기생·연쇄를 전이하고 false다.
        /// </summary>
        internal bool TryHandleRareEnemyRebirth(
            EnemyState enemy,
            in GameEvent deathEvent)
        {
            if (enemy == null ||
                !rareDeathChainEnemies.TryGetValue(
                    enemy.Id.Value,
                    out RareEnemyRuntime runtime))
            {
                return false;
            }

            if (runtime.Rebirth != null &&
                !runtime.Rebirth.Consumed)
            {
                runtime.Rebirth.Consumed = true;
                int healthBps =
                    runtime.Rebirth.Node.Amount > 0
                        ? Math.Min(
                            10000,
                            runtime.Rebirth.Node.Amount)
                        : DefaultEnemyRebirthHealthBps;
                enemy.HealthMilli = Math.Max(
                    1,
                    DeterministicMath.MultiplyBasisPoints(
                        enemy.MaxHealthMilli,
                        healthBps));
                enemy.DeathQueued = false;
                enemy.Alive = true;
                runtime.RebirthSpirit = true;
                runtime.RebirthSpeedBps =
                    runtime.Rebirth.Node.Amount2 > 0
                        ? runtime.Rebirth.Node.Amount2
                        : DefaultRebirthSpeedBps;
                AddRareDeathChainPresentation(
                    "rebirth",
                    enemy.Id,
                    runtime.Rebirth.SourceEntityId,
                    enemy.HealthMilli);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Runs Rare death transfers only after every rarity has declined to
        /// revive the enemy. Keeping final-death work separate prevents
        /// parasite/chain effects from firing before Phoenix Core.
        /// </summary>
        internal void HandleRareEnemyFinalDeath(
            EnemyState enemy,
            in GameEvent deathEvent)
        {
            if (enemy == null ||
                !rareDeathChainEnemies.TryGetValue(
                    enemy.Id.Value,
                    out RareEnemyRuntime runtime))
            {
                return;
            }

            TransferEnemyParasites(
                enemy,
                runtime,
                deathEvent);
            TriggerEnemyChains(
                enemy,
                runtime,
                deathEvent);
        }

        /// <summary>
        /// Compatibility facade for focused Rare tests. The central death
        /// pipeline uses the split rebirth/final-death methods so other
        /// lifecycle rules can compose between them.
        /// </summary>
        internal bool HandleRareEnemyDeath(
            EnemyState enemy,
            in GameEvent deathEvent)
        {
            if (TryHandleRareEnemyRebirth(
                    enemy,
                    deathEvent))
            {
                return true;
            }

            HandleRareEnemyFinalDeath(
                enemy,
                deathEvent);
            return false;
        }

        /// <summary>
        /// 환생한 영혼의 역방향 이동을 처리한다. 동일 EnemyState를 재사용하므로
        /// Lineage 보상은 늘지 않고 최종 사망에서 원래 예산이 딱 한 번 지급된다.
        /// </summary>
        internal bool ProcessRareEnemyMovement(
            EnemyState enemy)
        {
            if (enemy == null ||
                !enemy.Alive ||
                !rareDeathChainEnemies.TryGetValue(
                    enemy.Id.Value,
                    out RareEnemyRuntime runtime) ||
                !runtime.RebirthSpirit)
            {
                return false;
            }

            int movementBps = (int)
                DeterministicMath.MultiplyBasisPoints(
                    Math.Max(0, enemy.SpeedMultiplierBps),
                    Math.Max(
                        0,
                        runtime.RebirthSpeedBps));
            int distance = (int)
                DeterministicMath.MultiplyBasisPoints(
                    Math.Max(
                        0,
                        enemy.BaseSpeedMilliPerTick),
                    movementBps);
            if (distance <= 0)
            {
                return true;
            }

            long previousProgress =
                enemy.PathProgressMilli;
            enemy.PathProgressMilli = Math.Max(
                0,
                enemy.PathProgressMilli - distance);
            RefreshEnemyPosition(enemy);
            long moved = previousProgress -
                         enemy.PathProgressMilli;
            if (moved > 0)
            {
                TriggerBleedFromMovement(enemy, moved);
                AddPresentation(
                    PresentationEventType.EnemyMoved,
                    enemy.Id.Value,
                    -1,
                    -(int)Math.Min(int.MaxValue, moved),
                    "rebirth");
            }
            return true;
        }

        /// <summary>
        /// 탄환 소멸 확정 직전에 호출한다. 환생 카드가 남아 있으면 현재 탄환을
        /// 복사한 강화 유령을 하나 만들되 새 유령에는 환생 권한을 상속하지 않는다.
        /// </summary>
        internal bool HandleRareProjectileExpired(
            ProjectileState projectile,
            in GameEvent expirationEvent)
        {
            if (projectile == null ||
                !rareDeathChainProjectiles.TryGetValue(
                    projectile.Id.Value,
                    out RareProjectileRuntime runtime) ||
                runtime.Rebirth == null ||
                runtime.Rebirth.Consumed ||
                !CanCreateProjectileEntity(
                    projectile.Generation + 1))
            {
                return false;
            }

            runtime.Rebirth.Consumed = true;
            GameEvent diagnosticEvent = WithDiagnosticDepth(
                CreateDiagnosticEvent(
                    EventType.ProjectileSpawned,
                    expirationEvent.RootChainId,
                    runtime.Rebirth.TowerId,
                    runtime.Rebirth.CardId,
                    projectile.Id,
                    SubjectType.Projectile),
                expirationEvent.Depth + 1);
            if (!TryReserveComposite(
                    in diagnosticEvent,
                    chainEventCount: 0,
                    queueSlotCount: 0,
                    projectileSpawnCount: 1,
                    cardTriggerCount: 0))
            {
                return false;
            }

            int damageBps =
                runtime.Rebirth.Node.Amount > 0
                    ? runtime.Rebirth.Node.Amount
                    : DefaultProjectileRebirthDamageBps;
            var ghost = new ProjectileState
            {
                Id = new EntityId(nextEntityId++),
                SourceTowerId = projectile.SourceTowerId,
                Generation = projectile.Generation + 1,
                Position = projectile.Position,
                TargetId = projectile.TargetId,
                ApplyEnemyProgramOnHit =
                    projectile.ApplyEnemyProgramOnHit,
                DirectionXBps = projectile.DirectionXBps,
                DirectionYBps = projectile.DirectionYBps,
                Homing = true,
                VisualFlags = projectile.VisualFlags,
                DamageMilli =
                    DeterministicMath.MultiplyBasisPoints(
                        projectile.DamageMilli,
                        Math.Max(1, damageBps)),
                SpeedMilliPerTick =
                    projectile.SpeedMilliPerTick,
                RadiusMilli = projectile.RadiusMilli,
                LifetimeRemaining = Math.Min(
                    content.Safety
                        .MaxProjectileLifetimeTicks,
                    runtime.Rebirth.Node.DurationTicks > 0
                        ? runtime.Rebirth.Node.DurationTicks
                        : DefaultRebirthProjectileLifetimeTicks),
                PierceRemaining =
                    projectile.PierceRemaining,
                PiercesUsed = 0,
                PierceDamageMultiplierBps =
                    projectile.PierceDamageMultiplierBps,
                CriticalChanceBps =
                    projectile.CriticalChanceBps,
                RootChainId =
                    expirationEvent.RootChainId,
                ActivationId =
                    expirationEvent.ActivationId,
                LastTrailPosition =
                    projectile.Position
            };
            for (int i = 0;
                 i < projectile.Bindings.Count;
                 i++)
            {
                ghost.Bindings.Add(
                    projectile.Bindings[i].Clone());
            }

            EnemyState target =
                SelectNearestRareEnemy(
                    ghost.Position,
                    int.MaxValue,
                    EntityId.Invalid,
                    ChainId.Invalid);
            if (target != null)
            {
                ghost.TargetId = target.Id;
                SetProjectileDirection(
                    ghost,
                    target.Position);
            }

            projectiles.Add(ghost);
            TransferMythicProjectileLifecycleState(
                projectile,
                ghost);
            TransferLegendaryProjectileLifecycleState(
                projectile,
                ghost);
            CloneRareProjectileInheritedState(
                projectile,
                ghost);
            InheritRareResonanceAbsorbTimeMutationProjectileRuntime(
                projectile,
                ghost);
            InheritRareProjectileRuntime(
                projectile.Id,
                ghost.Id);
            AddPresentation(
                PresentationEventType.ProjectileSpawned,
                ghost.Id.Value,
                projectile.Id.Value,
                (int)Math.Min(
                    int.MaxValue,
                    ghost.DamageMilli),
                "rebirth");
            AddRareDeathChainPresentation(
                "rebirth",
                ghost.Id,
                projectile.Id,
                ghost.DamageMilli);
            return true;
        }

        /// <summary>
        /// 적 기생체의 지속 피해를 EntityId 순으로 처리한다. 매 피해 틱은 새
        /// RootChain으로 EnqueueDamage를 거쳐 장시간 상태도 예산을 우회하지 않는다.
        /// </summary>
        internal void ProcessRareDeathChainRuntime()
        {
            rareDeathChainKeyScratch.Clear();
            foreach (int key in rareDeathChainEnemies.Keys)
            {
                rareDeathChainKeyScratch.Add(key);
            }
            rareDeathChainKeyScratch.Sort();

            for (int keyIndex = 0;
                 keyIndex <
                 rareDeathChainKeyScratch.Count;
                 keyIndex++)
            {
                int key =
                    rareDeathChainKeyScratch[keyIndex];
                EnemyState enemy =
                    FindEnemy(new EntityId(key));
                if (enemy == null ||
                    !enemy.Alive ||
                    enemy.DeathQueued ||
                    !rareDeathChainEnemies.TryGetValue(
                        key,
                        out RareEnemyRuntime runtime))
                {
                    continue;
                }

                int parasiteIndex = 0;
                while (parasiteIndex <
                       runtime.Parasites.Count)
                {
                    RareEnemyParasiteRuntime parasite =
                        runtime.Parasites[parasiteIndex];
                    parasite.RemainingTicks--;
                    if (parasite.RemainingTicks <= 0)
                    {
                        runtime.Parasites.RemoveAt(
                            parasiteIndex);
                        continue;
                    }

                    if (tick >= parasite.NextTick)
                    {
                        parasite.NextTick +=
                            ResolveInterval(
                                parasite.Node);
                        parasite.TicksApplied++;
                        ChainId chain =
                            CreateRootChain();
                        EnqueueDamage(
                            enemy.Id,
                            parasite.TowerId,
                            parasite.CardId,
                            parasite.SourceEntityId,
                            parasite.Node.Amount > 0
                                ? parasite.Node.Amount
                                : DefaultParasiteDamageMilli,
                            DamageKind.Poison,
                            Math.Max(
                                0,
                                Math.Min(
                                    10000,
                                    parasite.Node.ChanceBps)),
                            chain,
                            CreateActivation(),
                            EventId.Invalid,
                            0,
                            EventTags.DamageOverTime |
                            EventTags.Repeated);
                        AddRareDeathChainPresentation(
                            "parasite_tick",
                            enemy.Id,
                            parasite.SourceEntityId,
                            parasite.TicksApplied);
                    }
                    parasiteIndex++;
                }
            }
        }

        /// <summary>새 런 초기화에서 희귀 4종 권위 상태를 모두 비운다.</summary>
        internal void ResetRareDeathChainState()
        {
            rareDeathChainProjectiles.Clear();
            rareDeathChainEnemies.Clear();
            rareChainScaleByActivation.Clear();
            rareChainVisits.Clear();
            rareDeathChainKeyScratch.Clear();
        }

        /// <summary>
        /// 제거된 개체의 희귀 카드 보조 상태를 정리한다. 키를 먼저 정렬해
        /// Dictionary 버킷 순서가 정리 순서나 리플레이 결과에 영향을 주지 않게 한다.
        /// </summary>
        internal void CleanupRareDeathChainState()
        {
            rareDeathChainKeyScratch.Clear();
            foreach (int key in rareDeathChainProjectiles.Keys)
            {
                ProjectileState projectile =
                    FindProjectile(new EntityId(key));
                if (projectile == null || !projectile.Alive)
                {
                    rareDeathChainKeyScratch.Add(key);
                }
            }
            rareDeathChainKeyScratch.Sort();
            for (int i = 0;
                 i < rareDeathChainKeyScratch.Count;
                 i++)
            {
                rareDeathChainProjectiles.Remove(
                    rareDeathChainKeyScratch[i]);
            }

            rareDeathChainKeyScratch.Clear();
            foreach (int key in rareDeathChainEnemies.Keys)
            {
                EnemyState enemy =
                    FindEnemy(new EntityId(key));
                if (enemy == null || !enemy.Alive)
                {
                    rareDeathChainKeyScratch.Add(key);
                }
            }
            rareDeathChainKeyScratch.Sort();
            for (int i = 0;
                 i < rareDeathChainKeyScratch.Count;
                 i++)
            {
                rareDeathChainEnemies.Remove(
                    rareDeathChainKeyScratch[i]);
            }
        }

        /// <summary>
        /// 희귀 4종의 모든 지연 상태를 안정 정렬해 전체 시뮬레이션 해시에 추가한다.
        /// </summary>
        internal void AppendRareDeathChainStateHash(
            ref StableHashBuilder hash)
        {
            int[] projectileKeys =
                new int[
                    rareDeathChainProjectiles.Count];
            rareDeathChainProjectiles.Keys.CopyTo(
                projectileKeys,
                0);
            Array.Sort(projectileKeys);
            hash.Add(projectileKeys.Length);
            for (int i = 0;
                 i < projectileKeys.Length;
                 i++)
            {
                hash.Add(projectileKeys[i]);
                AppendRareProjectileRuntimeHash(
                    ref hash,
                    rareDeathChainProjectiles[
                        projectileKeys[i]]);
            }

            int[] enemyKeys =
                new int[rareDeathChainEnemies.Count];
            rareDeathChainEnemies.Keys.CopyTo(
                enemyKeys,
                0);
            Array.Sort(enemyKeys);
            hash.Add(enemyKeys.Length);
            for (int i = 0;
                 i < enemyKeys.Length;
                 i++)
            {
                hash.Add(enemyKeys[i]);
                AppendRareEnemyRuntimeHash(
                    ref hash,
                    rareDeathChainEnemies[
                        enemyKeys[i]]);
            }

            int[] activationKeys =
                new int[
                    rareChainScaleByActivation.Count];
            rareChainScaleByActivation.Keys.CopyTo(
                activationKeys,
                0);
            Array.Sort(activationKeys);
            hash.Add(activationKeys.Length);
            for (int i = 0;
                 i < activationKeys.Length;
                 i++)
            {
                hash.Add(activationKeys[i]);
                hash.Add(
                    rareChainScaleByActivation[
                        activationKeys[i]]);
            }

            var visits =
                new List<RareChainVisitKey>(
                    rareChainVisits);
            visits.Sort(
                RareChainVisitKey.Compare);
            hash.Add(visits.Count);
            for (int i = 0; i < visits.Count; i++)
            {
                hash.Add(visits[i].RootChainId);
                hash.Add((int)visits[i].SubjectType);
                hash.Add(visits[i].SubjectId);
            }
        }

        internal int GetRareEnemyParasiteCount(
            EntityId enemyId)
        {
            return rareDeathChainEnemies.TryGetValue(
                    enemyId.Value,
                    out RareEnemyRuntime runtime)
                ? runtime.Parasites.Count
                : 0;
        }

        internal bool IsRareProjectileParasiteAttached(
            EntityId projectileId)
        {
            return rareDeathChainProjectiles.TryGetValue(
                       projectileId.Value,
                       out RareProjectileRuntime runtime) &&
                   runtime.Parasite != null &&
                   runtime.Parasite.Attached;
        }

        internal int GetRareChainScale(
            ActivationId activationId)
        {
            return rareChainScaleByActivation.TryGetValue(
                    activationId.Value,
                    out int scale)
                ? scale
                : 10000;
        }

        private void ConfigureProjectileExecute(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            RareProjectileRuntime runtime =
                GetOrCreateRareProjectileRuntime(
                    projectile.Id);
            runtime.Execute = new RareEffectRuntime(
                context,
                node);
        }

        private void ApplyEnemyExecute(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy =
                FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            RareEnemyRuntime runtime =
                GetOrCreateRareEnemyRuntime(enemy.Id);
            RareEffectRuntime mark =
                FindRareEffect(
                    runtime.ExecuteMarks,
                    context.TowerId,
                    context.CardInstanceId);
            if (mark == null)
            {
                mark = new RareEffectRuntime(
                    context,
                    node);
                runtime.ExecuteMarks.Add(mark);
            }
            else
            {
                mark.Node = node;
                mark.Consumed = false;
            }

            TryTriggerEnemyExecute(
                enemy,
                runtime,
                context.RootChainId,
                context.ActivationId,
                context.ParentEventId,
                context.Depth + 1);
            AddRareDeathChainPresentation(
                "execute_mark",
                enemy.Id,
                context.SourceEntityId,
                ResolveExecuteThreshold(node));
        }

        private void ConfigureProjectileParasite(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            TowerState tower =
                FindTower(context.TowerId);
            if (projectile == null ||
                !projectile.Alive ||
                tower == null)
            {
                return;
            }

            int currentIndex = context.CardIndex;
            if (currentIndex < 0)
            {
                currentIndex =
                    tower.Program.Length -
                    context.ContinuationCardCount -
                    1;
            }
            ProgramExecutionSpec execution =
                CreateProgramExecution(context);
            int nextIndex = FindNextProgramIndex(
                tower,
                currentIndex,
                SubjectType.Projectile,
                in execution);
            RareProjectileRuntime runtime =
                GetOrCreateRareProjectileRuntime(
                    projectile.Id);
            runtime.Parasite =
                new RareProjectileParasiteRuntime
                {
                    TowerId = context.TowerId,
                    CardId = context.CardId,
                    CardInstanceId =
                        context.CardInstanceId,
                    SourceEntityId =
                        context.SourceEntityId,
                    Node = node,
                    NextCardIndex = nextIndex,
                    BindingStartIndex =
                        projectile.Bindings.Count,
                    LastParentEventId =
                        context.ParentEventId,
                    Depth = context.Depth
                };
        }

        private void ApplyEnemyParasite(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy =
                FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            RareEnemyRuntime runtime =
                GetOrCreateRareEnemyRuntime(enemy.Id);
            RareEnemyParasiteRuntime parasite = null;
            for (int i = 0;
                 i < runtime.Parasites.Count;
                 i++)
            {
                RareEnemyParasiteRuntime candidate =
                    runtime.Parasites[i];
                if (candidate.TowerId ==
                        context.TowerId &&
                    candidate.CardInstanceId ==
                        context.CardInstanceId)
                {
                    parasite = candidate;
                    break;
                }
            }
            if (parasite == null)
            {
                parasite =
                    new RareEnemyParasiteRuntime();
                runtime.Parasites.Add(parasite);
            }

            parasite.TowerId = context.TowerId;
            parasite.CardId = context.CardId;
            parasite.CardInstanceId =
                context.CardInstanceId;
            parasite.SourceEntityId =
                context.SourceEntityId;
            parasite.Node = node;
            parasite.RemainingTicks =
                ResolveDuration(node);
            parasite.NextTick =
                tick + ResolveInterval(node);
            parasite.TicksApplied = 0;
            parasite.TransferCount = 0;
            AddRareDeathChainPresentation(
                "parasite_attach",
                enemy.Id,
                context.SourceEntityId,
                parasite.RemainingTicks);
        }

        private void ConfigureProjectileRebirth(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            RareProjectileRuntime runtime =
                GetOrCreateRareProjectileRuntime(
                    projectile.Id);
            if (runtime.Rebirth == null ||
                ResolveProjectileRebirthPower(node) >
                ResolveProjectileRebirthPower(
                    runtime.Rebirth.Node))
            {
                runtime.Rebirth =
                    new RareEffectRuntime(
                        context,
                        node);
            }
        }

        private void ApplyEnemyRebirth(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy =
                FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            RareEnemyRuntime runtime =
                GetOrCreateRareEnemyRuntime(enemy.Id);
            if (runtime.Rebirth == null ||
                ResolveEnemyRebirthPower(node) >
                ResolveEnemyRebirthPower(
                    runtime.Rebirth.Node))
            {
                runtime.Rebirth =
                    new RareEffectRuntime(
                        context,
                        node);
            }
        }

        private void ConfigureProjectileChain(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            RareProjectileRuntime runtime =
                GetOrCreateRareProjectileRuntime(
                    projectile.Id);
            AddOrReplaceChain(
                runtime.Chains,
                context,
                node);
            rareChainVisits.Add(
                new RareChainVisitKey(
                    context.RootChainId,
                    SubjectType.Projectile,
                    projectile.Id));
        }

        private void ApplyEnemyChain(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy =
                FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            RareEnemyRuntime runtime =
                GetOrCreateRareEnemyRuntime(enemy.Id);
            AddOrReplaceChain(
                runtime.Chains,
                context,
                node);
            rareChainVisits.Add(
                new RareChainVisitKey(
                    context.RootChainId,
                    SubjectType.Enemy,
                    enemy.Id));
        }

        private void TryTriggerEnemyExecute(
            EnemyState enemy,
            RareEnemyRuntime runtime,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth)
        {
            for (int i = 0;
                 i < runtime.ExecuteMarks.Count;
                 i++)
            {
                RareEffectRuntime mark =
                    runtime.ExecuteMarks[i];
                if (mark.Consumed ||
                    !IsAtOrBelowHealthThreshold(
                        enemy,
                        ResolveExecuteThreshold(
                            mark.Node)))
                {
                    continue;
                }

                long lethalDamage =
                    enemy.HealthMilli >
                    long.MaxValue -
                    Math.Max(0, enemy.ShieldMilli)
                        ? long.MaxValue
                        : enemy.HealthMilli +
                          Math.Max(
                              0,
                              enemy.ShieldMilli);
                var executeDamage = new GameEvent(
                    tick,
                    EventPhase.Damage,
                    EventType.DamageRequested,
                    rootChainId,
                    parentEventId,
                    activationId,
                    mark.TowerId,
                    mark.CardId,
                    mark.SourceEntityId,
                    enemy.Id,
                    SubjectType.Enemy,
                    depth,
                    enemy.Generation,
                    EventTags.SingleTarget |
                    EventTags.Repeated,
                    RewardOrigin.EnemyDrop,
                    payloadA: (int)DamageKind.Physical,
                    payloadValue: lethalDamage);
                if (!TryEnqueue(
                        in executeDamage,
                        out _))
                {
                    continue;
                }

                mark.Consumed = true;
                AddRareDeathChainPresentation(
                    "execute",
                    enemy.Id,
                    mark.SourceEntityId,
                    enemy.HealthMilli);
                break;
            }
        }

        private void TriggerProjectileChains(
            EntityId sourceId,
            TowerId towerId,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth)
        {
            if (!rareDeathChainProjectiles.TryGetValue(
                    sourceId.Value,
                    out RareProjectileRuntime runtime))
            {
                return;
            }

            for (int chainIndex =
                     runtime.Chains.Count - 1;
                 chainIndex >= 0;
                 chainIndex--)
            {
                RareChainRuntime chain =
                    runtime.Chains[chainIndex];
                if (chain.ActivationId !=
                        activationId ||
                    chain.TowerId != towerId)
                {
                    continue;
                }
                runtime.Chains.RemoveAt(chainIndex);

                ProjectileState source =
                    FindProjectile(sourceId);
                ProjectileState target =
                    source == null
                        ? null
                        : SelectNearestRareProjectile(
                            source,
                            ResolveRareTransferRadius(chain.Node),
                            rootChainId);
                if (target == null)
                {
                    continue;
                }

                TowerState tower =
                    FindTower(chain.TowerId);
                int firstCard = tower == null
                    ? -1
                    : FindFirstProgramIndex(
                        tower,
                        SubjectType.Projectile);
                if (firstCard < 0)
                {
                    continue;
                }

                ActivationId chainedActivation =
                    CreateActivation();
                int powerBps =
                    ResolveChainPower(chain.Node);
                target.DamageMilli =
                    DeterministicMath.MultiplyBasisPoints(
                        target.DamageMilli,
                        powerBps);
                rareChainScaleByActivation[
                    chainedActivation.Value] =
                    powerBps;
                rareChainVisits.Add(
                    new RareChainVisitKey(
                        rootChainId,
                        SubjectType.Projectile,
                        target.Id));
                if (!EnqueueProgram(
                        SubjectType.Projectile,
                        target.Id,
                        chain.TowerId,
                        firstCard,
                        rootChainId,
                        chainedActivation,
                        parentEventId,
                        depth + 1,
                        EventPhase.Projectile))
                {
                    rareChainScaleByActivation.Remove(
                        chainedActivation.Value);
                    continue;
                }

                AddRareDeathChainPresentation(
                    "chain",
                    target.Id,
                    source.Id,
                    powerBps);
            }
        }

        private void TriggerEnemyChains(
            EnemyState source,
            RareEnemyRuntime runtime,
            in GameEvent deathEvent)
        {
            rareChainVisits.Add(
                new RareChainVisitKey(
                    deathEvent.RootChainId,
                    SubjectType.Enemy,
                    source.Id));
            for (int chainIndex = 0;
                 chainIndex < runtime.Chains.Count;
                 chainIndex++)
            {
                RareChainRuntime chain =
                    runtime.Chains[chainIndex];
                EnemyState target =
                    SelectNearestRareEnemy(
                        source.Position,
                        ResolveRareTransferRadius(chain.Node),
                        source.Id,
                        deathEvent.RootChainId);
                if (target == null)
                {
                    continue;
                }

                TowerState tower =
                    FindTower(chain.TowerId);
                int firstCard = tower == null
                    ? -1
                    : FindFirstProgramIndex(
                        tower,
                        SubjectType.Enemy);
                if (firstCard < 0)
                {
                    continue;
                }

                ActivationId chainedActivation =
                    CreateActivation();
                int powerBps =
                    ResolveChainPower(chain.Node);
                rareChainScaleByActivation[
                    chainedActivation.Value] =
                    powerBps;
                rareChainVisits.Add(
                    new RareChainVisitKey(
                        deathEvent.RootChainId,
                        SubjectType.Enemy,
                        target.Id));
                if (!EnqueueProgram(
                        SubjectType.Enemy,
                        target.Id,
                        chain.TowerId,
                        firstCard,
                        deathEvent.RootChainId,
                        chainedActivation,
                        deathEvent.EventId,
                        deathEvent.Depth + 1,
                        EventPhase.Death))
                {
                    rareChainScaleByActivation.Remove(
                        chainedActivation.Value);
                    continue;
                }

                AddRareDeathChainPresentation(
                    "chain",
                    target.Id,
                    source.Id,
                    powerBps);
            }
        }

        private void TransferEnemyParasites(
            EnemyState source,
            RareEnemyRuntime sourceRuntime,
            in GameEvent deathEvent)
        {
            if (sourceRuntime.Parasites.Count == 0)
            {
                return;
            }

            for (int i = 0;
                 i < sourceRuntime.Parasites.Count;
                 i++)
            {
                RareEnemyParasiteRuntime parasite =
                    sourceRuntime.Parasites[i];
                if (parasite.RemainingTicks <= 0 ||
                    (parasite.Node.Limit > 0 &&
                     parasite.TransferCount >=
                     parasite.Node.Limit))
                {
                    continue;
                }

                EnemyState target =
                    SelectNearestRareEnemy(
                        source.Position,
                        ResolveRareTransferRadius(parasite.Node),
                        source.Id,
                        ChainId.Invalid);
                if (target == null)
                {
                    continue;
                }

                RareEnemyRuntime targetRuntime =
                    GetOrCreateRareEnemyRuntime(
                        target.Id);
                var transferred =
                    parasite.Clone();
                transferred.TransferCount++;
                transferred.NextTick =
                    tick +
                    ResolveInterval(
                        transferred.Node);
                targetRuntime.Parasites.Add(
                    transferred);
                AddRareDeathChainPresentation(
                    "parasite_transfer",
                    target.Id,
                    source.Id,
                    transferred.TransferCount);
            }
            sourceRuntime.Parasites.Clear();
        }

        private void QueueParasitePulseHit(
            ProjectileState projectile,
            EnemyState target,
            RareProjectileParasiteRuntime parasite,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth)
        {
            var hit = new GameEvent(
                tick,
                EventPhase.Projectile,
                EventType.ProjectileHit,
                rootChainId,
                parentEventId,
                activationId,
                projectile.SourceTowerId,
                parasite.CardId,
                projectile.Id,
                target.Id,
                SubjectType.Enemy,
                depth,
                projectile.Generation,
                EventTags.Projectile |
                EventTags.SingleTarget |
                EventTags.Repeated,
                RewardOrigin.EnemyDrop);
            if (!TryEnqueue(in hit, out _))
            {
                RestoreParasitePulseDamage(
                    projectile,
                    parasite);
            }
        }

        private static void RestoreParasitePulseDamage(
            ProjectileState projectile,
            RareProjectileParasiteRuntime parasite)
        {
            if (projectile != null)
            {
                projectile.DamageMilli =
                    parasite.DamageBeforePulse;
            }
            parasite.PulsePending = false;
            parasite.PulseActivationId =
                ActivationId.Invalid;
        }

        private ProjectileState SelectNearestRareProjectile(
            ProjectileState source,
            int radiusMilli,
            ChainId rootChainId)
        {
            ProjectileState selected = null;
            ulong selectedDistance =
                ulong.MaxValue;
            for (int i = 0;
                 i < projectiles.Count;
                 i++)
            {
                ProjectileState candidate =
                    projectiles[i];
                if (candidate == null ||
                    !candidate.Alive ||
                    candidate.ExpirationQueued ||
                    candidate.Id == source.Id ||
                    rareChainVisits.Contains(
                        new RareChainVisitKey(
                            rootChainId,
                            SubjectType.Projectile,
                            candidate.Id)) ||
                    !PathModel.IsWithin(
                        source.Position,
                        candidate.Position,
                        radiusMilli))
                {
                    continue;
                }

                ulong distance =
                    source.Position.DistanceSquaredRaw(
                        candidate.Position);
                if (selected == null ||
                    distance < selectedDistance ||
                    (distance == selectedDistance &&
                     candidate.Id < selected.Id))
                {
                    selected = candidate;
                    selectedDistance = distance;
                }
            }
            return selected;
        }

        private EnemyState SelectNearestRareEnemy(
            SimPosition origin,
            int radiusMilli,
            EntityId excludedId,
            ChainId chainId)
        {
            EnemyState selected = null;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState candidate =
                    enemies[i];
                if (candidate == null ||
                    !candidate.Alive ||
                    candidate.DeathQueued ||
                    candidate.Id == excludedId ||
                    (chainId.IsValid &&
                     rareChainVisits.Contains(
                         new RareChainVisitKey(
                             chainId,
                             SubjectType.Enemy,
                             candidate.Id))) ||
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

        private void InheritRareProjectileRuntime(
            EntityId sourceId,
            EntityId targetId)
        {
            if (!rareDeathChainProjectiles.TryGetValue(
                    sourceId.Value,
                    out RareProjectileRuntime source))
            {
                return;
            }

            var inherited =
                new RareProjectileRuntime();
            if (source.Execute != null)
            {
                inherited.Execute =
                    source.Execute.Clone();
                inherited.Execute.Consumed = false;
                inherited.Execute.ForceExpiration =
                    false;
            }
            if (source.Parasite != null)
            {
                inherited.Parasite =
                    source.Parasite.CloneDetached();
            }
            for (int i = 0;
                 i < source.Chains.Count;
                 i++)
            {
                inherited.Chains.Add(
                    source.Chains[i].Clone());
            }
            // 환생 권한은 의도적으로 복사하지 않는다.
            rareDeathChainProjectiles[
                targetId.Value] = inherited;
        }

        private RareProjectileRuntime
            GetOrCreateRareProjectileRuntime(
                EntityId projectileId)
        {
            if (!rareDeathChainProjectiles.TryGetValue(
                    projectileId.Value,
                    out RareProjectileRuntime runtime))
            {
                runtime =
                    new RareProjectileRuntime();
                rareDeathChainProjectiles.Add(
                    projectileId.Value,
                    runtime);
            }
            return runtime;
        }

        private RareEnemyRuntime
            GetOrCreateRareEnemyRuntime(
                EntityId enemyId)
        {
            if (!rareDeathChainEnemies.TryGetValue(
                    enemyId.Value,
                    out RareEnemyRuntime runtime))
            {
                runtime = new RareEnemyRuntime();
                rareDeathChainEnemies.Add(
                    enemyId.Value,
                    runtime);
            }
            return runtime;
        }

        private static RareEffectRuntime FindRareEffect(
            List<RareEffectRuntime> effects,
            TowerId towerId,
            int cardInstanceId)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].TowerId == towerId &&
                    effects[i].CardInstanceId ==
                    cardInstanceId)
                {
                    return effects[i];
                }
            }
            return null;
        }

        private static void AddOrReplaceChain(
            List<RareChainRuntime> chains,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            for (int i = 0; i < chains.Count; i++)
            {
                RareChainRuntime current =
                    chains[i];
                if (current.TowerId ==
                        context.TowerId &&
                    current.CardInstanceId ==
                        context.CardInstanceId &&
                    current.ActivationId ==
                        context.ActivationId)
                {
                    current.Node = node;
                    return;
                }
            }
            chains.Add(
                new RareChainRuntime(
                    context,
                    node));
        }

        private static bool IsAtOrBelowHealthThreshold(
            EnemyState enemy,
            int thresholdBps)
        {
            if (enemy == null ||
                enemy.MaxHealthMilli <= 0)
            {
                return false;
            }
            long thresholdHealth =
                DeterministicMath.MultiplyBasisPoints(
                    enemy.MaxHealthMilli,
                    thresholdBps);
            return enemy.HealthMilli <=
                   Math.Max(1, thresholdHealth);
        }

        private static int ResolveExecuteThreshold(
            in CompiledEffectNode node)
        {
            return Math.Max(
                1,
                Math.Min(
                    10000,
                    node.Amount > 0
                        ? node.Amount
                        : DefaultExecuteThresholdBps));
        }

        private static int ResolveDuration(
            in CompiledEffectNode node)
        {
            return Math.Max(
                1,
                node.DurationTicks > 0
                    ? node.DurationTicks
                    : DefaultParasiteDurationTicks);
        }

        private static int ResolveInterval(
            in CompiledEffectNode node)
        {
            return Math.Max(
                1,
                node.IntervalTicks > 0
                    ? node.IntervalTicks
                    : DefaultParasiteIntervalTicks);
        }

        private static int ResolveRareTransferRadius(
            in CompiledEffectNode node)
        {
            return Math.Max(
                1,
                node.RadiusMilli > 0
                    ? node.RadiusMilli
                    : DefaultRareTransferRadiusMilli);
        }

        private static int ResolveChainPower(
            in CompiledEffectNode node)
        {
            return Math.Max(
                1,
                Math.Min(
                    10000,
                    node.Amount > 0
                        ? node.Amount
                        : DefaultChainPowerBps));
        }

        private static int ResolveProjectileRebirthPower(
            in CompiledEffectNode node)
        {
            return node.Amount > 0
                ? node.Amount
                : DefaultProjectileRebirthDamageBps;
        }

        private static int ResolveEnemyRebirthPower(
            in CompiledEffectNode node)
        {
            return node.Amount > 0
                ? node.Amount
                : DefaultEnemyRebirthHealthBps;
        }

        private static int ScalePositive(
            int value,
            int scaleBps)
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
                        scaleBps)));
        }

        private void AddRareDeathChainPresentation(
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

        private static void AppendRareProjectileRuntimeHash(
            ref StableHashBuilder hash,
            RareProjectileRuntime runtime)
        {
            AppendNullableRareEffectHash(
                ref hash,
                runtime.Execute);
            AppendNullableRareEffectHash(
                ref hash,
                runtime.Rebirth);
            hash.Add(runtime.Parasite != null);
            if (runtime.Parasite != null)
            {
                RareProjectileParasiteRuntime parasite =
                    runtime.Parasite;
                hash.Add(parasite.TowerId);
                hash.Add(parasite.CardId);
                hash.Add(parasite.CardInstanceId);
                hash.Add(parasite.SourceEntityId);
                AppendEffectNodeHash(
                    ref hash,
                    parasite.Node);
                hash.Add(parasite.NextCardIndex);
                hash.Add(parasite.BindingStartIndex);
                hash.Add(parasite.Attached);
                hash.Add(parasite.TargetId);
                hash.Add(parasite.RemainingTicks);
                hash.Add(parasite.NextPulseTick);
                hash.Add(parasite.PulsesCompleted);
                hash.Add(parasite.PulsePending);
                hash.Add(parasite.PulseActivationId);
                hash.Add(parasite.DamageBeforePulse);
                hash.Add(parasite.LastParentEventId);
                hash.Add(parasite.Depth);
            }
            AppendRareChainsHash(
                ref hash,
                runtime.Chains);
        }

        private static void AppendRareEnemyRuntimeHash(
            ref StableHashBuilder hash,
            RareEnemyRuntime runtime)
        {
            hash.Add(runtime.ExecuteMarks.Count);
            for (int i = 0;
                 i < runtime.ExecuteMarks.Count;
                 i++)
            {
                AppendNullableRareEffectHash(
                    ref hash,
                    runtime.ExecuteMarks[i]);
            }
            AppendNullableRareEffectHash(
                ref hash,
                runtime.Rebirth);
            hash.Add(runtime.RebirthSpirit);
            hash.Add(runtime.RebirthSpeedBps);
            hash.Add(runtime.Parasites.Count);
            for (int i = 0;
                 i < runtime.Parasites.Count;
                 i++)
            {
                RareEnemyParasiteRuntime parasite =
                    runtime.Parasites[i];
                hash.Add(parasite.TowerId);
                hash.Add(parasite.CardId);
                hash.Add(parasite.CardInstanceId);
                hash.Add(parasite.SourceEntityId);
                AppendEffectNodeHash(
                    ref hash,
                    parasite.Node);
                hash.Add(parasite.RemainingTicks);
                hash.Add(parasite.NextTick);
                hash.Add(parasite.TicksApplied);
                hash.Add(parasite.TransferCount);
            }
            AppendRareChainsHash(
                ref hash,
                runtime.Chains);
        }

        private static void AppendNullableRareEffectHash(
            ref StableHashBuilder hash,
            RareEffectRuntime effect)
        {
            hash.Add(effect != null);
            if (effect == null)
            {
                return;
            }
            hash.Add(effect.TowerId);
            hash.Add(effect.CardId);
            hash.Add(effect.CardInstanceId);
            hash.Add(effect.SourceEntityId);
            hash.Add(effect.RootChainId);
            hash.Add(effect.ActivationId);
            hash.Add(effect.ParentEventId);
            hash.Add(effect.Depth);
            AppendEffectNodeHash(
                ref hash,
                effect.Node);
            hash.Add(effect.Consumed);
            hash.Add(effect.ForceExpiration);
        }

        private static void AppendRareChainsHash(
            ref StableHashBuilder hash,
            List<RareChainRuntime> chains)
        {
            hash.Add(chains.Count);
            for (int i = 0; i < chains.Count; i++)
            {
                RareChainRuntime chain = chains[i];
                hash.Add(chain.TowerId);
                hash.Add(chain.CardId);
                hash.Add(chain.CardInstanceId);
                hash.Add(chain.SourceEntityId);
                hash.Add(chain.RootChainId);
                hash.Add(chain.ActivationId);
                hash.Add(chain.ParentEventId);
                hash.Add(chain.Depth);
                AppendEffectNodeHash(
                    ref hash,
                    chain.Node);
            }
        }

        private sealed class RareProjectileRuntime
        {
            public RareEffectRuntime Execute;
            public RareProjectileParasiteRuntime Parasite;
            public RareEffectRuntime Rebirth;
            public readonly List<RareChainRuntime> Chains =
                new List<RareChainRuntime>();
        }

        private sealed class RareEnemyRuntime
        {
            public readonly List<RareEffectRuntime>
                ExecuteMarks =
                    new List<RareEffectRuntime>();
            public readonly List<RareEnemyParasiteRuntime>
                Parasites =
                    new List<RareEnemyParasiteRuntime>();
            public RareEffectRuntime Rebirth;
            public bool RebirthSpirit;
            public int RebirthSpeedBps;
            public readonly List<RareChainRuntime> Chains =
                new List<RareChainRuntime>();
        }

        private sealed class RareEffectRuntime
        {
            public RareEffectRuntime(
                in EffectExecutionContext context,
                in CompiledEffectNode node)
            {
                TowerId = context.TowerId;
                CardId = context.CardId;
                CardInstanceId =
                    context.CardInstanceId;
                SourceEntityId =
                    context.SourceEntityId;
                RootChainId =
                    context.RootChainId;
                ActivationId =
                    context.ActivationId;
                ParentEventId =
                    context.ParentEventId;
                Depth = context.Depth;
                Node = node;
            }

            public TowerId TowerId;
            public CardId CardId;
            public int CardInstanceId;
            public EntityId SourceEntityId;
            public ChainId RootChainId;
            public ActivationId ActivationId;
            public EventId ParentEventId;
            public int Depth;
            public CompiledEffectNode Node;
            public bool Consumed;
            public bool ForceExpiration;

            public RareEffectRuntime Clone()
            {
                return (RareEffectRuntime)
                    MemberwiseClone();
            }
        }

        private sealed class RareProjectileParasiteRuntime
        {
            public TowerId TowerId;
            public CardId CardId;
            public int CardInstanceId;
            public EntityId SourceEntityId;
            public CompiledEffectNode Node;
            public int NextCardIndex;
            public int BindingStartIndex;
            public bool Attached;
            public EntityId TargetId =
                EntityId.Invalid;
            public int RemainingTicks;
            public long NextPulseTick;
            public int PulsesCompleted;
            public bool PulsePending;
            public ActivationId PulseActivationId =
                ActivationId.Invalid;
            public long DamageBeforePulse;
            public EventId LastParentEventId;
            public int Depth;

            public RareProjectileParasiteRuntime
                CloneDetached()
            {
                var clone =
                    (RareProjectileParasiteRuntime)
                    MemberwiseClone();
                clone.Attached = false;
                clone.TargetId = EntityId.Invalid;
                clone.RemainingTicks = 0;
                clone.NextPulseTick = 0;
                clone.PulsesCompleted = 0;
                clone.PulsePending = false;
                clone.PulseActivationId =
                    ActivationId.Invalid;
                clone.DamageBeforePulse = 0;
                return clone;
            }
        }

        private sealed class RareEnemyParasiteRuntime
        {
            public TowerId TowerId;
            public CardId CardId;
            public int CardInstanceId;
            public EntityId SourceEntityId;
            public CompiledEffectNode Node;
            public int RemainingTicks;
            public long NextTick;
            public int TicksApplied;
            public int TransferCount;

            public RareEnemyParasiteRuntime Clone()
            {
                return (RareEnemyParasiteRuntime)
                    MemberwiseClone();
            }
        }

        private sealed class RareChainRuntime
        {
            public RareChainRuntime(
                in EffectExecutionContext context,
                in CompiledEffectNode node)
            {
                TowerId = context.TowerId;
                CardId = context.CardId;
                CardInstanceId =
                    context.CardInstanceId;
                SourceEntityId =
                    context.SourceEntityId;
                RootChainId =
                    context.RootChainId;
                ActivationId =
                    context.ActivationId;
                ParentEventId =
                    context.ParentEventId;
                Depth = context.Depth;
                Node = node;
            }

            public TowerId TowerId;
            public CardId CardId;
            public int CardInstanceId;
            public EntityId SourceEntityId;
            public ChainId RootChainId;
            public ActivationId ActivationId;
            public EventId ParentEventId;
            public int Depth;
            public CompiledEffectNode Node;

            public RareChainRuntime Clone()
            {
                return (RareChainRuntime)
                    MemberwiseClone();
            }
        }

        private readonly struct RareChainVisitKey
        {
            public RareChainVisitKey(
                ChainId rootChainId,
                SubjectType subjectType,
                EntityId subjectId)
            {
                RootChainId = rootChainId;
                SubjectType = subjectType;
                SubjectId = subjectId;
            }

            public ChainId RootChainId { get; }
            public SubjectType SubjectType { get; }
            public EntityId SubjectId { get; }

            public override bool Equals(object obj)
            {
                return obj is RareChainVisitKey other &&
                       RootChainId ==
                       other.RootChainId &&
                       SubjectType ==
                       other.SubjectType &&
                       SubjectId == other.SubjectId;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = RootChainId.Value;
                    hash =
                        (hash * 397) ^
                        (int)SubjectType;
                    hash =
                        (hash * 397) ^
                        SubjectId.Value;
                    return hash;
                }
            }

            public static int Compare(
                RareChainVisitKey left,
                RareChainVisitKey right)
            {
                int result =
                    left.RootChainId.CompareTo(
                        right.RootChainId);
                if (result != 0)
                {
                    return result;
                }
                result =
                    ((int)left.SubjectType).CompareTo(
                        (int)right.SubjectType);
                return result != 0
                    ? result
                    : left.SubjectId.CompareTo(
                        right.SubjectId);
            }
        }
    }
}
