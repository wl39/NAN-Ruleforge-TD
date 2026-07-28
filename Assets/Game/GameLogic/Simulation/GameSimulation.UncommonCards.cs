using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 탄환 해석 고급 카드는 적중 이후 또는 매 이동 틱에 실행할 상태가 필요하다.
    /// 기존 Phase 1 바인딩을 카드별 필드로 계속 키우지 않고 이 작은 공통 레코드로 보관한다.
    /// </summary>
    internal sealed class UncommonProjectileEffectRuntime
    {
        public EffectOperation Operation;
        public CompiledEffectNode Node;
        public TowerId TowerId;
        public CardId CardId;
        public int CardInstanceId;
        public EntityId SourceEntityId;
        public ChainId RootChainId;
        public ActivationId ActivationId;
        public EventId ParentEventId;
        public int Depth;

        public bool Used;
        public bool Anchored;
        public bool Returning;
        public int TriggerCount;
        public int DelayRemaining;
        public int RemainingTicks;
        public long NextTick;
        public EntityId AnchorTargetId = EntityId.Invalid;
        public SimPosition AnchorPosition;
        public readonly HashSet<int> Contacts = new HashSet<int>();

        public UncommonProjectileEffectRuntime CloneFor(
            EntityId newSourceEntityId)
        {
            return new UncommonProjectileEffectRuntime
            {
                Operation = Operation,
                Node = Node,
                TowerId = TowerId,
                CardId = CardId,
                CardInstanceId = CardInstanceId,
                SourceEntityId = newSourceEntityId,
                RootChainId = RootChainId,
                ActivationId = ActivationId,
                ParentEventId = ParentEventId,
                Depth = Depth,
                Used = Used,
                Anchored = false,
                Returning = false,
                TriggerCount = TriggerCount,
                DelayRemaining = DelayRemaining,
                RemainingTicks = RemainingTicks,
                NextTick = NextTick,
                AnchorTargetId = EntityId.Invalid,
                AnchorPosition = AnchorPosition
            };
        }

        public EffectExecutionContext CreateEnemyContext(
            EntityId targetId,
            EntityId sourceEntityId,
            EventId parentEventId,
            int depth)
        {
            return new EffectExecutionContext(
                SubjectType.Enemy,
                targetId,
                TowerId,
                CardId,
                CardInstanceId,
                sourceEntityId,
                RootChainId,
                ActivationId,
                parentEventId,
                depth,
                0,
                0);
        }
    }

    internal sealed class EnemyAfterimageLink
    {
        public EntityId PhantomId;
        public EntityId OriginalId;
        public long ExpireTick;
        public int DamageTransferBps;
    }

    /// <summary>
    /// Phase 2 고급 카드 15장의 권위 있는 전투 로직이다.
    /// 모든 범위 피해와 연쇄 피해는 기존 EventQueue/ChainBudget을 통과하고,
    /// 공간 후보는 공통 우선순위로 정렬해 같은 입력에서 같은 결과를 만든다.
    /// </summary>
    public sealed partial class GameSimulation
    {
        private const int DefaultControlDurationTicks = 30;
        private const int DefaultStatusDurationTicks = 90;
        private const int DefaultPulseIntervalTicks = 15;
        private const int DefaultEffectRadiusMilli = 1500;
        private const int DefaultDamageBps = 5000;
        private const int DefaultLifestealBps = 1000;
        private const int DefaultFearHasteBps = 12500;

        private readonly Dictionary<int, List<UncommonProjectileEffectRuntime>>
            uncommonProjectileEffects =
                new Dictionary<int, List<UncommonProjectileEffectRuntime>>();
        private readonly Dictionary<int, EnemyAfterimageLink>
            uncommonAfterimageLinks =
                new Dictionary<int, EnemyAfterimageLink>();
        private readonly Dictionary<int, long> corrosionHealthFloors =
            new Dictionary<int, long>();
        private readonly HashSet<int> airborneRicochetTriggeredStatuses =
            new HashSet<int>();
        private readonly List<EnemyState> uncommonEnemyScratch =
            new List<EnemyState>(32);
        private readonly List<GameEvent> uncommonEventScratch =
            new List<GameEvent>(32);
        private readonly List<int> uncommonKeyScratch =
            new List<int>(64);

        /// <summary>
        /// 같은 GameSimulation 인스턴스를 새 런에 재사용할 때 이전 런의 고급 카드
        /// 보조 상태가 남지 않도록 초기화한다.
        /// </summary>
        internal void ResetUncommonCardState()
        {
            uncommonProjectileEffects.Clear();
            uncommonAfterimageLinks.Clear();
            corrosionHealthFloors.Clear();
            airborneRicochetTriggeredStatuses.Clear();
            uncommonEnemyScratch.Clear();
            uncommonEventScratch.Clear();
            uncommonKeyScratch.Clear();
        }

        /// <summary>
        /// EffectRegistry의 공용 고급 카드 executor가 호출하는 단일 진입점이다.
        /// operation 자체가 탄환/적 해석을 명시하므로 카드 ID 문자열 분기는 하지 않는다.
        /// </summary>
        internal void ExecuteUncommonEffect(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node)
        {
            switch (operation)
            {
                case EffectOperation.BindCurse:
                case EffectOperation.CreateBindTrap:
                case EffectOperation.MakeAirborneProjectile:
                case EffectOperation.BindShock:
                case EffectOperation.BindFreeze:
                case EffectOperation.EnableProjectilePulse:
                case EffectOperation.EnableProjectileMagnet:
                case EffectOperation.EnableProjectileReflect:
                case EffectOperation.EnableProjectileContagion:
                case EffectOperation.BindSeal:
                case EffectOperation.BindCorrosion:
                case EffectOperation.EnableProjectileOrbit:
                case EffectOperation.BindLifesteal:
                case EffectOperation.BindFear:
                    AddUncommonProjectileEffect(context, operation, node);
                    break;

                case EffectOperation.CreateAfterimageProjectile:
                    AddUncommonProjectileEffect(context, operation, node);
                    SpawnAfterimageProjectile(context, node);
                    break;

                case EffectOperation.ApplyCurse:
                    ApplyCurse(context, node);
                    break;
                case EffectOperation.ApplyBind:
                    ApplyStrongControlStatus(
                        context,
                        StatusType.Bind,
                        node);
                    break;
                case EffectOperation.ApplyAirborne:
                    ApplyAirborneStatus(context, node);
                    break;
                case EffectOperation.ApplyShock:
                    ApplyShock(context, node);
                    break;
                case EffectOperation.ApplyFreeze:
                    ApplyFreeze(context, node);
                    break;
                case EffectOperation.ApplyAfterimage:
                    ApplyEnemyAfterimage(context, node);
                    break;
                case EffectOperation.ApplyEnemyPulse:
                    ApplyTimedUncommonStatus(
                        context,
                        StatusType.Pulse,
                        node);
                    break;
                case EffectOperation.ApplyEnemyMagnet:
                    ApplyTimedUncommonStatus(
                        context,
                        StatusType.Magnet,
                        node);
                    break;
                case EffectOperation.ApplyEnemyReflect:
                    ApplyNonTickingUncommonStatus(
                        context,
                        StatusType.Reflect,
                        node);
                    break;
                case EffectOperation.ApplyEnemyContagion:
                    ApplyTimedUncommonStatus(
                        context,
                        StatusType.Contagion,
                        node);
                    break;
                case EffectOperation.ApplySeal:
                    ApplyNonTickingUncommonStatus(
                        context,
                        StatusType.Seal,
                        node);
                    break;
                case EffectOperation.ApplyCorrosion:
                    ApplyCorrosion(context, node);
                    break;
                case EffectOperation.ApplyEnemyOrbit:
                    ApplyTimedUncommonStatus(
                        context,
                        StatusType.Orbit,
                        node);
                    break;
                case EffectOperation.ApplyLifesteal:
                    ApplyNonTickingUncommonStatus(
                        context,
                        StatusType.Lifesteal,
                        node);
                    break;
                case EffectOperation.ApplyFear:
                    ApplyTimedUncommonStatus(
                        context,
                        StatusType.Fear,
                        node);
                    break;
            }
        }

        private void AddUncommonProjectileEffect(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node)
        {
            ProjectileState projectile = FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            if (!uncommonProjectileEffects.TryGetValue(
                    projectile.Id.Value,
                    out List<UncommonProjectileEffectRuntime> effects))
            {
                effects = new List<UncommonProjectileEffectRuntime>(4);
                uncommonProjectileEffects.Add(
                    projectile.Id.Value,
                    effects);
            }

            var runtime = new UncommonProjectileEffectRuntime
            {
                Operation = operation,
                Node = node,
                TowerId = context.TowerId,
                CardId = context.CardId,
                CardInstanceId = context.CardInstanceId,
                SourceEntityId = context.SubjectId,
                RootChainId = context.RootChainId,
                ActivationId = context.ActivationId,
                ParentEventId = context.ParentEventId,
                Depth = context.Depth,
                // 잔상 지연은 새 유령 탄환에만 적용한다. 원본 탄환은 카드
                // 실행 뒤에도 같은 틱부터 정상적으로 계속 비행해야 한다.
                DelayRemaining = 0,
                RemainingTicks = Math.Max(
                    1,
                    node.DurationTicks > 0
                        ? node.DurationTicks
                        : projectile.LifetimeRemaining),
                NextTick = tick + Math.Max(
                    1,
                    node.IntervalTicks > 0
                        ? node.IntervalTicks
                        : DefaultPulseIntervalTicks),
                AnchorPosition = projectile.Position
            };
            effects.Add(runtime);

            if (operation == EffectOperation.CreateBindTrap ||
                operation == EffectOperation.EnableProjectileOrbit)
            {
                projectile.LifetimeRemaining = Math.Max(
                    projectile.LifetimeRemaining,
                    runtime.RemainingTicks);
            }
        }

        private void SpawnAfterimageProjectile(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState original = FindProjectile(context.SubjectId);
            if (original == null || !original.Alive)
            {
                return;
            }

            GameEvent diagnosticEvent = WithDiagnosticDepth(
                CreateDiagnosticEvent(
                    EventType.ProjectileSpawned,
                    context.RootChainId,
                    context.TowerId,
                    context.CardId,
                    context.SubjectId,
                    SubjectType.Projectile),
                context.Depth);
            if (!TryReserveComposite(
                    in diagnosticEvent,
                    chainEventCount: 0,
                    queueSlotCount: 0,
                    projectileSpawnCount: 1,
                    cardTriggerCount: 0))
            {
                return;
            }

            int damageBps = node.Amount > 0
                ? Math.Min(10000, node.Amount)
                : DefaultDamageBps;
            var ghost = new ProjectileState
            {
                Id = new EntityId(nextEntityId++),
                SourceTowerId = original.SourceTowerId,
                Generation = original.Generation + 1,
                Position = original.Position,
                TargetId = original.TargetId,
                ApplyEnemyProgramOnHit =
                    original.ApplyEnemyProgramOnHit,
                DirectionXBps = original.DirectionXBps,
                DirectionYBps = original.DirectionYBps,
                Homing = original.Homing,
                VisualFlags = original.VisualFlags,
                DamageMilli = DeterministicMath.MultiplyBasisPoints(
                    original.DamageMilli,
                    damageBps),
                SpeedMilliPerTick = original.SpeedMilliPerTick,
                RadiusMilli = original.RadiusMilli,
                LifetimeRemaining = Math.Min(
                    content.Safety.MaxProjectileLifetimeTicks,
                    checked(
                        original.LifetimeRemaining +
                        Math.Max(1, node.DurationTicks))),
                PierceRemaining = original.PierceRemaining,
                PierceDamageMultiplierBps =
                    original.PierceDamageMultiplierBps,
                CriticalChanceBps = original.CriticalChanceBps,
                RootChainId = original.RootChainId,
                ActivationId = original.ActivationId,
                LastTrailPosition = original.Position
            };

            for (int i = 0; i < original.Bindings.Count; i++)
            {
                ghost.Bindings.Add(original.Bindings[i].Clone());
            }

            if (uncommonProjectileEffects.TryGetValue(
                    original.Id.Value,
                    out List<UncommonProjectileEffectRuntime> sourceEffects))
            {
                var ghostEffects =
                    new List<UncommonProjectileEffectRuntime>(
                        sourceEffects.Count);
                for (int i = 0; i < sourceEffects.Count; i++)
                {
                    UncommonProjectileEffectRuntime copied =
                        sourceEffects[i].CloneFor(ghost.Id);
                    copied.DelayRemaining = Math.Max(
                        copied.DelayRemaining,
                        Math.Max(1, node.DurationTicks));
                    copied.Used =
                        copied.Operation ==
                        EffectOperation.CreateAfterimageProjectile;
                    ghostEffects.Add(copied);
                }
                uncommonProjectileEffects.Add(
                    ghost.Id.Value,
                    ghostEffects);
            }

            // 잔상보다 왼쪽에서 이미 적용된 가속/지연 런타임은 현재 탄환
            // 상태의 일부다. 새 ID용 독립 복사본으로 이어받아 카드 순서가
            // 실제 비행 결과에 반영되게 한다.
            InheritCommonProjectileRuntime(
                original,
                ghost);
            projectiles.Add(ghost);
            AddPresentation(
                PresentationEventType.ProjectileSpawned,
                ghost.Id.Value,
                original.Id.Value,
                (int)Math.Min(int.MaxValue, ghost.DamageMilli),
                "afterimage");
            AddUncommonPresentation(
                "afterimage_spawn",
                ghost.Id,
                original.Id,
                ghost.DamageMilli);
        }

        private void ApplyCurse(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            ApplyStatus(
                context,
                StatusType.Curse,
                WithDefaults(
                    node,
                    amount: node.Amount > 0 ? node.Amount : 1500,
                    durationTicks:
                        node.DurationTicks > 0
                            ? node.DurationTicks
                            : DefaultStatusDurationTicks,
                    intervalTicks: 0,
                    maxStacks: node.MaxStacks > 0
                        ? node.MaxStacks
                        : 3,
                    radiusMilli: ResolveRadius(node),
                    limit: node.Limit > 0 ? node.Limit : 5000));
        }

        private void ApplyStrongControlStatus(
            in EffectExecutionContext context,
            StatusType statusType,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            CompiledEnemyDefinition definition =
                content.GetEnemy(enemy.DefinitionId);
            if (definition.Rank != EnemyRank.Normal)
            {
                ApplyControlGauge(
                    enemy,
                    context,
                    Math.Max(1, node.Amount));
                return;
            }

            ApplyStatus(
                context,
                statusType,
                WithDefaults(
                    node,
                    amount: Math.Max(1, node.Amount),
                    durationTicks:
                        node.DurationTicks > 0
                            ? node.DurationTicks
                            : DefaultControlDurationTicks,
                    intervalTicks: 0,
                    maxStacks: 1,
                    radiusMilli: ResolveRadius(node),
                    limit: node.Limit));
        }

        private void ApplyAirborneStatus(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            ApplyStrongControlStatus(
                context,
                StatusType.Airborne,
                node);
            StatusInstance airborne = FindStatus(
                enemy,
                StatusType.Airborne,
                context.TowerId,
                context.CardId);
            if (airborne != null)
            {
                // Airborne에서 Amount는 정예 제어 게이지, Amount2는 착지
                // 충돌 피해다. 공용 상태 레코드의 보조 정수 필드에 후자를 보존한다.
                airborne.ArmorIgnoreBps = Math.Max(
                    airborne.ArmorIgnoreBps,
                    Math.Max(0, node.Amount2));
            }
        }

        private void ApplyTimedUncommonStatus(
            in EffectExecutionContext context,
            StatusType statusType,
            in CompiledEffectNode node)
        {
            ApplyStatus(
                context,
                statusType,
                WithDefaults(
                    node,
                    amount: Math.Max(1, node.Amount),
                    durationTicks:
                        node.DurationTicks > 0
                            ? node.DurationTicks
                            : DefaultStatusDurationTicks,
                    intervalTicks:
                        node.IntervalTicks > 0
                            ? node.IntervalTicks
                            : DefaultPulseIntervalTicks,
                    maxStacks:
                        node.MaxStacks > 0 ? node.MaxStacks : 1,
                    radiusMilli: ResolveRadius(node),
                    limit: node.Limit));
        }

        private void ApplyNonTickingUncommonStatus(
            in EffectExecutionContext context,
            StatusType statusType,
            in CompiledEffectNode node)
        {
            ApplyStatus(
                context,
                statusType,
                WithDefaults(
                    node,
                    amount: Math.Max(1, node.Amount),
                    durationTicks:
                        node.DurationTicks > 0
                            ? node.DurationTicks
                            : DefaultStatusDurationTicks,
                    intervalTicks: 0,
                    maxStacks:
                        node.MaxStacks > 0 ? node.MaxStacks : 1,
                    radiusMilli: node.RadiusMilli,
                    limit: node.Limit));
        }

        private void ApplyShock(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            CompiledEffectNode effective = WithDefaults(
                node,
                amount: node.Amount > 0 ? node.Amount : 1000,
                durationTicks:
                    node.DurationTicks > 0
                        ? node.DurationTicks
                        : DefaultStatusDurationTicks,
                intervalTicks: 0,
                maxStacks: node.MaxStacks > 0 ? node.MaxStacks : 3,
                radiusMilli: ResolveRadius(node),
                limit: node.Limit > 0 ? node.Limit : 3);
            ApplyStatus(context, StatusType.Shock, effective);

            EnemyState enemy = FindEnemy(context.SubjectId);
            StatusInstance shock = FindStatus(
                enemy,
                StatusType.Shock,
                context.TowerId,
                context.CardId);
            if (shock == null || shock.Stacks < shock.MaxStacks)
            {
                return;
            }

            shock.Stacks = 0;
            ExecuteChainDamage(
                enemy,
                shock.Intensity,
                shock.RadiusMilli,
                Math.Max(1, shock.Limit),
                context.TowerId,
                context.CardId,
                context.SourceEntityId,
                context.RootChainId,
                context.ActivationId,
                context.ParentEventId,
                context.Depth + 1,
                "shock_chain");
        }

        private void ApplyFreeze(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null ||
                !enemy.Alive ||
                HasActiveStatus(enemy, StatusType.FreezeImmunity))
            {
                return;
            }

            CompiledEffectNode chillNode = WithDefaults(
                node,
                amount: Math.Max(1, node.Amount),
                durationTicks:
                    node.DurationTicks > 0
                        ? node.DurationTicks
                        : DefaultStatusDurationTicks,
                intervalTicks: 0,
                maxStacks: node.MaxStacks > 0 ? node.MaxStacks : 3,
                radiusMilli: ResolveRadius(node),
                limit: node.Limit);
            ApplyStatus(context, StatusType.Chill, chillNode);
            StatusInstance chill = FindStatus(
                enemy,
                StatusType.Chill,
                context.TowerId,
                context.CardId);
            if (chill == null || chill.Stacks < chill.MaxStacks)
            {
                return;
            }

            enemy.Statuses.Remove(chill);
            var frozenNode = new CompiledEffectNode(
                EffectOperation.ApplyFreeze,
                Math.Max(1, node.Amount),
                node.Amount2,
                node.Amount3,
                node.Amount3 > 0
                    ? node.Amount3
                    : DefaultControlDurationTicks,
                0,
                1,
                ResolveRadius(node),
                node.Limit,
                node.ChanceBps,
                node.ReferenceId);
            ApplyStrongControlStatus(
                context,
                StatusType.Frozen,
                frozenNode);
            // Limit은 투사체 소멸 파편의 최대 대상 수로 예약한다.
            // 빙결 해제 후 면역 시간은 Frozen 상태의 TickInterval에 별도로 보관해
            // 두 의미가 같은 필드를 덮어쓰지 않게 한다.
            StatusInstance frozen = FindStatus(
                enemy,
                StatusType.Frozen,
                context.TowerId,
                context.CardId);
            if (frozen != null)
            {
                frozen.TickInterval = node.IntervalTicks > 0
                    ? node.IntervalTicks
                    : DefaultControlDurationTicks;
                frozen.NextTick = long.MaxValue;
            }
        }

        private void ApplyCorrosion(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            if (!corrosionHealthFloors.ContainsKey(enemy.Id.Value))
            {
                int minimumHealthBps =
                    node.Limit > 0
                        ? Math.Max(1000, Math.Min(10000, node.Limit))
                        : 3000;
                corrosionHealthFloors.Add(
                    enemy.Id.Value,
                    Math.Max(
                        1000,
                        DeterministicMath.MultiplyBasisPoints(
                            enemy.MaxHealthMilli,
                            minimumHealthBps)));
            }

            ApplyTimedUncommonStatus(
                context,
                StatusType.Corrosion,
                WithDefaults(
                    node,
                    amount: node.Amount > 0 ? node.Amount : 1,
                    durationTicks:
                        node.DurationTicks > 0
                            ? node.DurationTicks
                            : DefaultStatusDurationTicks,
                    intervalTicks:
                        node.IntervalTicks > 0
                            ? node.IntervalTicks
                            : DefaultPulseIntervalTicks,
                    maxStacks:
                        node.MaxStacks > 0 ? node.MaxStacks : 10,
                    radiusMilli: 0,
                    limit: node.Limit > 0 ? node.Limit : 3000));
            StatusInstance corrosion = FindStatus(
                enemy,
                StatusType.Corrosion,
                context.TowerId,
                context.CardId);
            if (corrosion != null)
            {
                // ChanceBps는 이 operation에서 확률이 아니라 틱당 최대 체력
                // 감소 bps로 컴파일된다. 상태 공통 필드에 보존해 틱 처리에서 읽는다.
                corrosion.ArmorIgnoreBps = Math.Max(
                    corrosion.ArmorIgnoreBps,
                    node.ChanceBps > 0
                        ? node.ChanceBps
                        : 250);
            }
        }

        /// <summary>
        /// 저주가 이미 있는 적에게 새 상태가 적용될 때 지속시간을 한 번만 증폭한다.
        /// ApplyStatusCore에서 실제로 사용할 노드를 만들기 전에 호출해야 재적용마다
        /// 기존 남은 시간을 다시 곱하는 현상이 생기지 않는다.
        /// </summary>
        internal CompiledEffectNode AdjustStatusNodeForCurse(
            EnemyState enemy,
            StatusType newStatusType,
            in CompiledEffectNode node)
        {
            if (enemy == null ||
                newStatusType == StatusType.Curse ||
                newStatusType == StatusType.FreezeImmunity ||
                newStatusType == StatusType.FearHaste ||
                node.DurationTicks <= 0)
            {
                return node;
            }

            int durationBonusBps = GetCurseStrengthBps(enemy);
            if (durationBonusBps <= 0)
            {
                return node;
            }

            return new CompiledEffectNode(
                node.Operation,
                node.Amount,
                node.Amount2,
                node.Amount3,
                (int)Math.Min(
                    1_000_000,
                    DeterministicMath.MultiplyBasisPoints(
                        node.DurationTicks,
                        10000 + durationBonusBps)),
                node.IntervalTicks,
                node.MaxStacks,
                node.RadiusMilli,
                node.Limit,
                node.ChanceBps,
                node.ReferenceId);
        }

        /// <summary>
        /// MoveEnemies 시작 부분에서 호출한다. 공포는 역주행하고, 회전은 느린
        /// 경로 진행과 결정적 8방향 원운동을 사용하며, 강한 제어는 이동만 소비한다.
        /// true이면 이 틱의 일반 이동 처리를 건너뛴다.
        /// </summary>
        internal bool TryProcessUncommonEnemyMovement(
            EnemyState enemy)
        {
            if (enemy == null || !enemy.Alive)
            {
                return false;
            }

            // 잔상은 별도의 공격 가능한 논리 hitbox지만 경로를 전진하거나
            // 본진에 누출되는 실제 적이 아니다. 수명 동안 생성 위치에 고정한다.
            if (uncommonAfterimageLinks.ContainsKey(
                    enemy.Id.Value))
            {
                return true;
            }

            if (HasAnyStatus(
                    enemy,
                    StatusType.Bind,
                    StatusType.Airborne,
                    StatusType.Frozen))
            {
                return true;
            }

            StatusInstance fear = FindFirstActiveStatus(
                enemy,
                StatusType.Fear);
            if (fear != null)
            {
                int speedBps = fear.Intensity > 0
                    ? Math.Min(20000, fear.Intensity)
                    : 10000;
                int distance = (int)
                    DeterministicMath.MultiplyBasisPoints(
                        enemy.BaseSpeedMilliPerTick,
                        speedBps);
                long previous = enemy.PathProgressMilli;
                enemy.PathProgressMilli = Math.Max(
                    0,
                    enemy.PathProgressMilli - Math.Max(1, distance));
                RefreshEnemyPosition(enemy);
                AddPresentation(
                    PresentationEventType.EnemyMoved,
                    enemy.Id.Value,
                    fear.SourceEntityId.Value,
                    (int)Math.Max(
                        int.MinValue,
                        Math.Min(
                            int.MaxValue,
                            enemy.PathProgressMilli - previous)),
                    "fear");
                return true;
            }

            StatusInstance orbit = FindFirstActiveStatus(
                enemy,
                StatusType.Orbit);
            if (orbit != null)
            {
                int progressBps = orbit.Limit > 0
                    ? Math.Max(1000, Math.Min(8000, orbit.Limit))
                    : 2500;
                int distance = (int)
                    DeterministicMath.MultiplyBasisPoints(
                        enemy.BaseSpeedMilliPerTick,
                        progressBps);
                enemy.PathProgressMilli = Math.Min(
                    path.TotalLengthMilli,
                    enemy.PathProgressMilli + Math.Max(1, distance));

                int radius = orbit.RadiusMilli > 0
                    ? orbit.RadiusMilli
                    : DefaultEffectRadiusMilli / 2;
                int phaseIndex = (int)(
                    (tick + enemy.Id.Value) & 7L);
                GetEightDirectionOffset(
                    phaseIndex,
                    radius,
                    out int offsetX,
                    out int offsetY);
                // 궤도 위치는 표시/충돌 Position에만 더한다. 분열체가 가진
                // 원래 PathLateralOffset을 덮어쓰면 효과 종료 후에도 경로에서
                // 벗어난 상태가 영구적으로 남으므로 권위 경로 오프셋은 보존한다.
                enemy.Position =
                    path.GetPosition(enemy.PathProgressMilli) +
                    enemy.PathLateralOffset +
                    SimVector.FromMilliUnits(offsetX, offsetY);
                AddPresentation(
                    PresentationEventType.EnemyMoved,
                    enemy.Id.Value,
                    orbit.SourceEntityId.Value,
                    distance,
                    "orbit");
                return true;
            }

            StatusInstance haste = FindFirstActiveStatus(
                enemy,
                StatusType.FearHaste);
            if (haste != null)
            {
                int hasteBps = haste.Intensity > 0
                    ? Math.Max(10000, haste.Intensity)
                    : DefaultFearHasteBps;
                int distance = (int)
                    DeterministicMath.MultiplyBasisPoints(
                        enemy.BaseSpeedMilliPerTick,
                        MultiplyBps(
                            enemy.SpeedMultiplierBps,
                            hasteBps));
                enemy.PathProgressMilli = Math.Min(
                    path.TotalLengthMilli,
                    enemy.PathProgressMilli + Math.Max(1, distance));
                RefreshEnemyPosition(enemy);
                AddPresentation(
                    PresentationEventType.EnemyMoved,
                    enemy.Id.Value,
                    haste.SourceEntityId.Value,
                    distance,
                    "fear_haste");
                if (enemy.PathProgressMilli >= path.TotalLengthMilli)
                {
                    LeakEnemy(enemy);
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// MoveProjectiles에서 수명 차감 뒤 일반 유도/이동 전에 호출한다.
        /// true이면 지연·함정·공전이 이 틱 위치를 직접 처리했으므로 일반 이동을 건너뛴다.
        /// </summary>
        internal bool ProcessUncommonProjectileTick(
            ProjectileState projectile)
        {
            if (projectile == null ||
                !projectile.Alive ||
                !uncommonProjectileEffects.TryGetValue(
                    projectile.Id.Value,
                    out List<UncommonProjectileEffectRuntime> effects))
            {
                return false;
            }

            bool consumeMovement = false;
            for (int effectIndex = 0;
                 effectIndex < effects.Count;
                 effectIndex++)
            {
                UncommonProjectileEffectRuntime effect =
                    effects[effectIndex];
                if (effect.DelayRemaining > 0)
                {
                    effect.DelayRemaining--;
                    consumeMovement = true;
                    continue;
                }

                switch (effect.Operation)
                {
                    case EffectOperation.CreateBindTrap:
                        if (effect.Anchored)
                        {
                            effect.RemainingTicks--;
                            if (effect.RemainingTicks <= 0)
                            {
                                ScheduleProjectileExpiration(
                                    projectile,
                                    effect.ParentEventId);
                                consumeMovement = true;
                                break;
                            }
                            projectile.Position =
                                effect.AnchorPosition;
                            ProcessBindTrapPulse(
                                projectile,
                                effect);
                            consumeMovement = true;
                        }
                        break;
                    case EffectOperation.EnableProjectilePulse:
                        ProcessProjectilePulse(
                            projectile,
                            effect);
                        break;
                    case EffectOperation.EnableProjectileMagnet:
                        ProcessProjectileMagnet(
                            projectile,
                            effect);
                        break;
                    case EffectOperation.EnableProjectileReflect:
                        if (ProcessReflectedProjectile(
                                projectile,
                                effect))
                        {
                            consumeMovement = true;
                        }
                        break;
                    case EffectOperation.EnableProjectileContagion:
                        ProcessProjectileContagion(
                            projectile,
                            effect);
                        break;
                    case EffectOperation.EnableProjectileOrbit:
                        if (effect.Anchored)
                        {
                            ProcessOrbitingProjectile(
                                projectile,
                                effect);
                            consumeMovement = true;
                        }
                        break;
                }
            }

            return consumeMovement;
        }

        /// <summary>
        /// 기존 OnHit 바인딩 처리 뒤, 관통/소멸 판정 전에 호출한다.
        /// true이면 함정·귀환·공전 같은 카드가 탄환 생존을 인수했으므로
        /// 이번 적중에서 일반 소멸 예약을 하지 않는다.
        /// </summary>
        internal bool HandleUncommonProjectileHit(
            ProjectileState projectile,
            EnemyState target,
            in GameEvent parentEvent)
        {
            if (projectile == null || target == null)
            {
                return false;
            }

            bool keepAlive = HandleEnemyReflect(
                projectile,
                target,
                parentEvent);
            if (!uncommonProjectileEffects.TryGetValue(
                    projectile.Id.Value,
                    out List<UncommonProjectileEffectRuntime> effects))
            {
                return keepAlive;
            }

            for (int effectIndex = 0;
                 effectIndex < effects.Count;
                 effectIndex++)
            {
                UncommonProjectileEffectRuntime effect =
                    effects[effectIndex];
                EffectExecutionContext context =
                    effect.CreateEnemyContext(
                        target.Id,
                        projectile.Id,
                        parentEvent.EventId,
                        parentEvent.Depth + 1);
                switch (effect.Operation)
                {
                    case EffectOperation.BindCurse:
                        ApplyCurse(context, effect.Node);
                        break;
                    case EffectOperation.CreateBindTrap:
                        if (!effect.Anchored)
                        {
                            effect.Anchored = true;
                            effect.AnchorPosition = target.Position;
                            effect.NextTick = tick;
                            projectile.Position = target.Position;
                            projectile.TargetId = EntityId.Invalid;
                            keepAlive = true;
                            AddUncommonPresentation(
                                "bind_pulse",
                                target.Id,
                                projectile.Id,
                                0);
                        }
                        break;
                    case EffectOperation.MakeAirborneProjectile:
                        if (!effect.Used)
                        {
                            ExecuteAirborneLanding(
                                projectile,
                                target,
                                effect,
                                parentEvent);
                            effect.Used = true;
                        }
                        break;
                    case EffectOperation.BindShock:
                        ApplyShock(context, effect.Node);
                        ExecuteChainDamage(
                            target,
                            ResolveDamage(
                                projectile.DamageMilli,
                                effect.Node.Amount2),
                            ResolveRadius(effect.Node),
                            Math.Max(1, effect.Node.Limit),
                            effect.TowerId,
                            effect.CardId,
                            projectile.Id,
                            effect.RootChainId,
                            effect.ActivationId,
                            parentEvent.EventId,
                            parentEvent.Depth + 1,
                            "shock_chain");
                        break;
                    case EffectOperation.BindFreeze:
                        ApplyFreeze(context, effect.Node);
                        break;
                    case EffectOperation.EnableProjectileReflect:
                        if (!effect.Returning &&
                            effect.TriggerCount <
                            ResolveTriggerLimit(effect.Node, 1))
                        {
                            effect.Returning = true;
                            projectile.Position = target.Position;
                            keepAlive = true;
                            AddUncommonPresentation(
                                "reflect_turn",
                                projectile.Id,
                                target.Id,
                                effect.TriggerCount + 1);
                        }
                        break;
                    case EffectOperation.BindSeal:
                        if (!effect.Used)
                        {
                            ApplyNonTickingUncommonStatus(
                                context,
                                StatusType.Seal,
                                effect.Node);
                            effect.Used = true;
                        }
                        break;
                    case EffectOperation.BindCorrosion:
                        ApplyCorrosion(context, effect.Node);
                        break;
                    case EffectOperation.EnableProjectileOrbit:
                        if (!effect.Anchored)
                        {
                            effect.Anchored = true;
                            effect.AnchorTargetId = target.Id;
                            effect.AnchorPosition = target.Position;
                            effect.NextTick = tick;
                            keepAlive = true;
                        }
                        break;
                    case EffectOperation.BindFear:
                        ApplyTimedUncommonStatus(
                            context,
                            StatusType.Fear,
                            effect.Node);
                        break;
                }
                effect.TriggerCount++;
            }

            return keepAlive;
        }

        /// <summary>
        /// 탄환이 실제로 소멸하기 직전에 호출한다. 빙결 탄환의 파편 피해는
        /// 대상 전체를 먼저 확정한 뒤 원자적 이벤트 배치로 예약한다.
        /// </summary>
        internal void HandleUncommonProjectileExpired(
            ProjectileState projectile,
            in GameEvent parentEvent)
        {
            if (projectile == null ||
                !uncommonProjectileEffects.TryGetValue(
                    projectile.Id.Value,
                    out List<UncommonProjectileEffectRuntime> effects))
            {
                return;
            }

            for (int i = 0; i < effects.Count; i++)
            {
                UncommonProjectileEffectRuntime effect = effects[i];
                if (effect.Operation != EffectOperation.BindFreeze)
                {
                    continue;
                }

                ExecuteUncommonAreaDamage(
                    projectile.Position,
                    ResolveDamage(
                        projectile.DamageMilli,
                        effect.Node.Amount2),
                    ResolveRadius(effect.Node),
                    Math.Max(1, effect.Node.Limit),
                    effect.TowerId,
                    effect.CardId,
                    projectile.Id,
                    effect.RootChainId,
                    effect.ActivationId,
                    parentEvent.EventId,
                    parentEvent.Depth + 1,
                    DamageKind.Physical,
                    EventTags.Area,
                    "freeze_shard");
            }
        }

        /// <summary>
        /// ProcessStatuses가 주기 시점에 도달한 각 고급 상태를 처리할 때 호출한다.
        /// 연쇄/전염 대상은 EntityId까지 포함한 공통 정렬로 항상 같은 순서가 된다.
        /// </summary>
        internal void ProcessUncommonStatusTick(
            EnemyState enemy,
            StatusInstance status)
        {
            if (enemy == null ||
                status == null ||
                !enemy.Alive ||
                status.RemainingTicks <= 0)
            {
                return;
            }

            switch (status.Type)
            {
                case StatusType.Pulse:
                    SpreadOneStatus(
                        enemy,
                        status,
                        moveSource: false);
                    AddUncommonPresentation(
                        "pulse",
                        enemy.Id,
                        status.SourceEntityId,
                        status.Stacks);
                    break;
                case StatusType.Magnet:
                    PullProjectilesToEnemy(enemy, status);
                    break;
                case StatusType.Contagion:
                    SpreadOneStatus(
                        enemy,
                        status,
                        moveSource: true);
                    break;
                case StatusType.Corrosion:
                    TickCorrosion(enemy, status);
                    break;
                case StatusType.Orbit:
                    DamageOrbitCollisions(enemy, status);
                    break;
            }
        }

        /// <summary>
        /// 상태를 목록에서 제거하기 직전에 호출한다. 에어본 착지와 빙결 면역,
        /// 공포 종료 가속은 모두 별도 상태/피해 사건으로 전환된다.
        /// </summary>
        internal void HandleUncommonStatusExpired(
            EnemyState enemy,
            StatusInstance status)
        {
            if (enemy == null || status == null)
            {
                return;
            }

            EffectExecutionContext context = ContextFromStatus(
                enemy,
                status);
            switch (status.Type)
            {
                case StatusType.Airborne:
                    ExecuteUncommonAreaDamage(
                        enemy.Position,
                        Math.Max(
                            1,
                            status.ArmorIgnoreBps > 0
                                ? status.ArmorIgnoreBps
                                : status.Intensity),
                        status.RadiusMilli > 0
                            ? status.RadiusMilli
                            : DefaultEffectRadiusMilli,
                        status.Limit > 0 ? status.Limit : 8,
                        status.SourceTowerId,
                        status.SourceCardId,
                        enemy.Id,
                        context.RootChainId,
                        context.ActivationId,
                        EventId.Invalid,
                        0,
                        DamageKind.Collision,
                        EventTags.Area | EventTags.Control,
                        "airborne_land");
                    // 에어본 착지는 실제 강제이동 카드의 후속 접점이므로
                    // 적 도탄 상태가 있다면 동일 컨텍스트의 충돌 연쇄로 이어 간다.
                    if (!airborneRicochetTriggeredStatuses.Remove(
                            status.InstanceId))
                    {
                        TryEnemyRicochetAfterForcedMovement(
                            enemy,
                            context);
                    }
                    break;
                case StatusType.Frozen:
                    ApplyStatus(
                        context,
                        StatusType.FreezeImmunity,
                        new CompiledEffectNode(
                            EffectOperation.ApplyFreeze,
                            1,
                            0,
                            0,
                            Math.Max(
                                1,
                                status.TickInterval > 0
                                    ? status.TickInterval
                                    : DefaultControlDurationTicks),
                            0,
                            1,
                            0,
                            0,
                            0,
                            null));
                    break;
                case StatusType.Fear:
                    ApplyStatus(
                        context,
                        StatusType.FearHaste,
                        new CompiledEffectNode(
                            EffectOperation.ApplyFear,
                            status.Limit > 10000
                                ? status.Limit
                                : DefaultFearHasteBps,
                            0,
                            0,
                            Math.Max(
                                1,
                                status.TickInterval > 0
                                    ? status.TickInterval * 2
                                    : DefaultControlDurationTicks),
                            0,
                            1,
                            0,
                            0,
                            0,
                            null));
                    break;
            }
        }

        /// <summary>
        /// CalculateDamage의 표식/취약 계산 뒤에 호출한다. 저주는 상태이상 피해
        /// 태그가 있는 요청만 증폭하며 일반 직접 피해를 올리지 않는다.
        /// </summary>
        internal long ModifyDamageForUncommonStatuses(
            EnemyState enemy,
            long amount,
            EventTags tags)
        {
            if (enemy == null ||
                amount <= 0 ||
                (tags & EventTags.DamageOverTime) == 0)
            {
                return amount;
            }

            int curseBps = GetCurseStrengthBps(enemy);
            return curseBps <= 0
                ? amount
                : DeterministicMath.MultiplyBasisPoints(
                    amount,
                    10000 + curseBps);
        }

        /// <summary>
        /// ProcessDamageEvent가 보호막과 체력에 실제 반영한 피해량으로 호출한다.
        /// 흡혈은 요청 피해가 아니라 확정 피해만 회복하고, 잔상 링크는 새 피해
        /// 이벤트로 전달해 사망/보상 순서를 우회하지 않는다.
        /// </summary>
        internal void HandleUncommonDamageApplied(
            EnemyState enemy,
            in GameEvent gameEvent,
            long appliedAmount)
        {
            if (enemy == null || appliedAmount <= 0)
            {
                return;
            }

            int healBps = 0;
            ProjectileState sourceProjectile =
                FindProjectile(gameEvent.SourceEntityId);
            if (sourceProjectile != null &&
                uncommonProjectileEffects.TryGetValue(
                    sourceProjectile.Id.Value,
                    out List<UncommonProjectileEffectRuntime> effects))
            {
                for (int i = 0; i < effects.Count; i++)
                {
                    if (effects[i].Operation ==
                        EffectOperation.BindLifesteal)
                    {
                        int candidate = effects[i].Node.Amount > 0
                            ? effects[i].Node.Amount
                            : DefaultLifestealBps;
                        healBps = Math.Min(
                            10000,
                            healBps + Math.Max(0, candidate));
                    }
                }
            }

            for (int i = 0; i < enemy.Statuses.Count; i++)
            {
                StatusInstance status = enemy.Statuses[i];
                if (status.Type == StatusType.Lifesteal &&
                    status.RemainingTicks > 0)
                {
                    int candidate = status.Intensity > 0
                        ? status.Intensity
                        : DefaultLifestealBps;
                    healBps = Math.Min(
                        10000,
                        healBps + Math.Max(0, candidate));
                }
            }

            if (healBps > 0)
            {
                int heal = (int)Math.Min(
                    int.MaxValue,
                    DeterministicMath.MultiplyBasisPoints(
                        appliedAmount,
                        healBps) / 1000L);
                HealBase(
                    heal,
                    enemy.Id,
                    gameEvent.SourceEntityId);
            }

            if (!uncommonAfterimageLinks.TryGetValue(
                    enemy.Id.Value,
                    out EnemyAfterimageLink link))
            {
                return;
            }

            EnemyState original = FindEnemy(link.OriginalId);
            if (original == null ||
                !original.Alive ||
                original.Id == enemy.Id)
            {
                return;
            }

            long transferred = DeterministicMath.MultiplyBasisPoints(
                appliedAmount,
                Math.Max(
                    0,
                    Math.Min(10000, link.DamageTransferBps)));
            EnqueueDamage(
                original.Id,
                gameEvent.SourceTowerId,
                gameEvent.SourceCardId,
                enemy.Id,
                transferred,
                DamageKind.Physical,
                0,
                gameEvent.RootChainId,
                gameEvent.ActivationId,
                gameEvent.EventId,
                gameEvent.Depth + 1,
                EventTags.SingleTarget | EventTags.Repeated);
        }

        /// <summary>
        /// 적이 사망 확정되어 상태 목록이 정리되기 전에 호출한다.
        /// 저주 사망 전염은 InstanceId 순으로 읽고 각 저주마다 가장 가까운 대상
        /// 하나만 선택해 체인 예산 안에서 확산한다.
        /// </summary>
        internal void HandleUncommonEnemyDeath(
            EnemyState enemy,
            in GameEvent gameEvent)
        {
            if (enemy == null)
            {
                return;
            }

            var curses = new List<StatusInstance>();
            for (int i = 0; i < enemy.Statuses.Count; i++)
            {
                StatusInstance status = enemy.Statuses[i];
                if (status.Type == StatusType.Curse &&
                    status.RemainingTicks > 0)
                {
                    curses.Add(status);
                }
            }
            curses.Sort((left, right) =>
                left.InstanceId.CompareTo(right.InstanceId));

            for (int i = 0; i < curses.Count; i++)
            {
                StatusInstance curse = curses[i];
                EnemyState target = SelectNearestUncommonEnemy(
                    enemy.Position,
                    curse.RadiusMilli > 0
                        ? curse.RadiusMilli
                        : DefaultEffectRadiusMilli,
                    enemy.Id);
                if (target == null)
                {
                    continue;
                }

                var context = new EffectExecutionContext(
                    SubjectType.Enemy,
                    target.Id,
                    curse.SourceTowerId,
                    curse.SourceCardId,
                    curse.SourceCardInstanceId,
                    enemy.Id,
                    gameEvent.RootChainId,
                    gameEvent.ActivationId,
                    gameEvent.EventId,
                    gameEvent.Depth + 1,
                    0,
                    0);
                ApplyStatus(
                    context,
                    StatusType.Curse,
                    new CompiledEffectNode(
                        EffectOperation.ApplyCurse,
                        Math.Max(1, curse.Intensity),
                        0,
                        0,
                        Math.Max(
                            1,
                            curse.RemainingTicks / 2),
                        0,
                        Math.Max(1, curse.MaxStacks),
                        curse.RadiusMilli,
                        curse.Limit,
                        0,
                        null));
                AddUncommonPresentation(
                    "curse",
                    target.Id,
                    enemy.Id,
                    curse.Stacks);
            }
        }

        internal bool IsEnemySpecialAbilitySealed(
            EnemyState enemy)
        {
            return enemy != null &&
                   HasActiveStatus(enemy, StatusType.Seal);
        }

        /// <summary>
        /// CleanupDeadEntities 시작 부분에서 호출해 만료된 잔상과 제거된 개체의
        /// 보조 상태를 정리한다. ID 사전은 키를 정렬해 삭제 순서도 결정적으로 유지한다.
        /// </summary>
        internal void CleanupUncommonCardState()
        {
            uncommonKeyScratch.Clear();
            foreach (KeyValuePair<int, EnemyAfterimageLink> pair
                     in uncommonAfterimageLinks)
            {
                EnemyAfterimageLink link = pair.Value;
                EnemyState phantom = FindEnemy(link.PhantomId);
                EnemyState original = FindEnemy(link.OriginalId);
                if (phantom != null &&
                    phantom.Alive &&
                    (tick >= link.ExpireTick ||
                     original == null ||
                     !original.Alive))
                {
                    phantom.Alive = false;
                    DecrementLineage(phantom);
                }

                if (phantom == null || !phantom.Alive)
                {
                    uncommonKeyScratch.Add(pair.Key);
                }
            }
            uncommonKeyScratch.Sort();
            for (int i = 0; i < uncommonKeyScratch.Count; i++)
            {
                uncommonAfterimageLinks.Remove(
                    uncommonKeyScratch[i]);
            }

            uncommonKeyScratch.Clear();
            foreach (
                KeyValuePair<int, List<UncommonProjectileEffectRuntime>>
                pair in uncommonProjectileEffects)
            {
                ProjectileState projectile =
                    FindProjectile(new EntityId(pair.Key));
                if (projectile == null || !projectile.Alive)
                {
                    uncommonKeyScratch.Add(pair.Key);
                }
            }
            uncommonKeyScratch.Sort();
            for (int i = 0; i < uncommonKeyScratch.Count; i++)
            {
                uncommonProjectileEffects.Remove(
                    uncommonKeyScratch[i]);
            }

            uncommonKeyScratch.Clear();
            foreach (KeyValuePair<int, long> pair
                     in corrosionHealthFloors)
            {
                EnemyState enemy =
                    FindEnemy(new EntityId(pair.Key));
                if (enemy == null ||
                    !enemy.Alive ||
                    !HasActiveStatus(
                        enemy,
                        StatusType.Corrosion))
                {
                    uncommonKeyScratch.Add(pair.Key);
                }
            }
            uncommonKeyScratch.Sort();
            for (int i = 0; i < uncommonKeyScratch.Count; i++)
            {
                corrosionHealthFloors.Remove(
                    uncommonKeyScratch[i]);
            }

            // 에어본이 끝나기 전에 적이 사망하면 일반 만료 훅을 지나지 않는다.
            // 그런 상태 인스턴스 ID를 보조 집합에 남겨 두지 않아 장기 런의 메모리와
            // 상태 해시가 과거 사망 순서에 불필요하게 의존하지 않도록 정리한다.
            uncommonKeyScratch.Clear();
            foreach (int statusInstanceId
                     in airborneRicochetTriggeredStatuses)
            {
                if (!HasActiveAirborneStatusInstance(
                        statusInstanceId))
                {
                    uncommonKeyScratch.Add(statusInstanceId);
                }
            }
            uncommonKeyScratch.Sort();
            for (int i = 0; i < uncommonKeyScratch.Count; i++)
            {
                airborneRicochetTriggeredStatuses.Remove(
                    uncommonKeyScratch[i]);
            }
        }

        internal ProjectileEffectVisualFlags
            GetProjectileUncommonEffectFlags(EntityId projectileId)
        {
            if (!uncommonProjectileEffects.TryGetValue(
                    projectileId.Value,
                    out List<UncommonProjectileEffectRuntime> effects))
            {
                return ProjectileEffectVisualFlags.None;
            }

            ProjectileEffectVisualFlags result =
                ProjectileEffectVisualFlags.None;
            for (int i = 0; i < effects.Count; i++)
            {
                result |= GetVisualFlag(effects[i].Operation);
            }
            return result;
        }

        private bool HasActiveAirborneStatusInstance(
            int statusInstanceId)
        {
            for (int enemyIndex = 0;
                 enemyIndex < enemies.Count;
                 enemyIndex++)
            {
                EnemyState enemy = enemies[enemyIndex];
                if (!enemy.Alive)
                {
                    continue;
                }

                for (int statusIndex = 0;
                     statusIndex < enemy.Statuses.Count;
                     statusIndex++)
                {
                    StatusInstance status =
                        enemy.Statuses[statusIndex];
                    if (status.InstanceId == statusInstanceId &&
                        status.Type == StatusType.Airborne &&
                        status.RemainingTicks > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// ComputeStateHash의 Finish 직전에 호출한다. 런타임 Dictionary/HashSet은
        /// 키와 접촉 ID를 정렬한 뒤 기록해 내부 버킷 순서에 의존하지 않는다.
        /// </summary>
        internal void AppendUncommonStateHash(
            ref StableHashBuilder hash)
        {
            uncommonKeyScratch.Clear();
            foreach (
                KeyValuePair<int, List<UncommonProjectileEffectRuntime>>
                pair in uncommonProjectileEffects)
            {
                uncommonKeyScratch.Add(pair.Key);
            }
            uncommonKeyScratch.Sort();
            hash.Add(uncommonKeyScratch.Count);
            for (int keyIndex = 0;
                 keyIndex < uncommonKeyScratch.Count;
                 keyIndex++)
            {
                int key = uncommonKeyScratch[keyIndex];
                hash.Add(key);
                List<UncommonProjectileEffectRuntime> effects =
                    uncommonProjectileEffects[key];
                hash.Add(effects.Count);
                for (int effectIndex = 0;
                     effectIndex < effects.Count;
                     effectIndex++)
                {
                    AppendUncommonEffectHash(
                        ref hash,
                        effects[effectIndex]);
                }
            }

            uncommonKeyScratch.Clear();
            foreach (KeyValuePair<int, EnemyAfterimageLink> pair
                     in uncommonAfterimageLinks)
            {
                uncommonKeyScratch.Add(pair.Key);
            }
            uncommonKeyScratch.Sort();
            hash.Add(uncommonKeyScratch.Count);
            for (int i = 0; i < uncommonKeyScratch.Count; i++)
            {
                EnemyAfterimageLink link =
                    uncommonAfterimageLinks[
                        uncommonKeyScratch[i]];
                hash.Add(link.PhantomId);
                hash.Add(link.OriginalId);
                hash.Add(link.ExpireTick);
                hash.Add(link.DamageTransferBps);
            }

            uncommonKeyScratch.Clear();
            foreach (KeyValuePair<int, long> pair
                     in corrosionHealthFloors)
            {
                uncommonKeyScratch.Add(pair.Key);
            }
            uncommonKeyScratch.Sort();
            hash.Add(uncommonKeyScratch.Count);
            for (int i = 0; i < uncommonKeyScratch.Count; i++)
            {
                int key = uncommonKeyScratch[i];
                hash.Add(key);
                hash.Add(corrosionHealthFloors[key]);
            }

            int[] airborneStatuses =
                new int[
                    airborneRicochetTriggeredStatuses.Count];
            airborneRicochetTriggeredStatuses.CopyTo(
                airborneStatuses);
            Array.Sort(airborneStatuses);
            hash.Add(airborneStatuses.Length);
            for (int i = 0; i < airborneStatuses.Length; i++)
            {
                hash.Add(airborneStatuses[i]);
            }
        }

        private void ApplyEnemyAfterimage(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState original = FindEnemy(context.SubjectId);
            if (original == null || !original.Alive)
            {
                return;
            }

            CompiledEffectNode effective = WithDefaults(
                node,
                amount: node.Amount > 0
                    ? Math.Min(10000, node.Amount)
                    : DefaultDamageBps,
                durationTicks:
                    node.DurationTicks > 0
                        ? node.DurationTicks
                        : DefaultStatusDurationTicks,
                intervalTicks: 0,
                maxStacks: 1,
                radiusMilli: ResolveRadius(node),
                limit: node.Limit);
            ApplyStatus(
                context,
                StatusType.Afterimage,
                effective);

            foreach (KeyValuePair<int, EnemyAfterimageLink> pair
                     in uncommonAfterimageLinks)
            {
                EnemyAfterimageLink existing = pair.Value;
                if (existing.OriginalId == original.Id)
                {
                    existing.ExpireTick = Math.Max(
                        existing.ExpireTick,
                        tick + effective.DurationTicks);
                    existing.DamageTransferBps =
                        Math.Max(
                            existing.DamageTransferBps,
                            effective.Amount);
                    return;
                }
            }

            if (!lineages.TryGetValue(
                    original.LineageId.Value,
                    out LineageState lineage) ||
                lineage.SpawnedEntityCount >=
                content.Safety.MaxEnemiesPerLineage)
            {
                return;
            }

            long trailDistance = effective.RadiusMilli > 0
                ? Math.Min(2000, effective.RadiusMilli)
                : 750;
            long phantomProgress = Math.Max(
                0,
                original.PathProgressMilli - trailDistance);
            var phantom = new EnemyState
            {
                Id = new EntityId(nextEntityId++),
                DefinitionId = original.DefinitionId,
                LineageId = original.LineageId,
                Generation = original.Generation,
                SpawnOrigin = EnemySpawnOrigin.Split,
                SummonerId = original.Id,
                PathProgressMilli = phantomProgress,
                PathLateralOffset =
                    original.PathLateralOffset,
                Position =
                    path.GetPosition(phantomProgress) +
                    original.PathLateralOffset,
                HealthMilli = original.MaxHealthMilli,
                MaxHealthMilli = original.MaxHealthMilli,
                Armor = original.Armor,
                BaseSpeedMilliPerTick =
                    original.BaseSpeedMilliPerTick,
                SpeedMultiplierBps =
                    original.SpeedMultiplierBps,
                SizeMultiplierBps =
                    original.SizeMultiplierBps,
                AreaDamageTakenBps =
                    original.AreaDamageTakenBps,
                SingleDamageTakenBps =
                    original.SingleDamageTakenBps,
                VisualFlags = original.VisualFlags,
                RewardBudget = 0,
                WaveProgressBudget = 0,
                CardPackProgressBudget = 0,
                ControlThreshold = original.ControlThreshold,
                ControlThresholdStep =
                    original.ControlThresholdStep
            };
            enemies.Add(phantom);
            InheritRangeEntryLocks(original, phantom);
            lineage.SpawnedEntityCount++;
            lineage.LiveMembers++;
            uncommonAfterimageLinks.Add(
                phantom.Id.Value,
                new EnemyAfterimageLink
                {
                    PhantomId = phantom.Id,
                    OriginalId = original.Id,
                    ExpireTick =
                        tick + effective.DurationTicks,
                    DamageTransferBps = effective.Amount
                });
            spatialIndex.Rebuild(enemies);
            AddPresentation(
                PresentationEventType.EnemySpawned,
                phantom.Id.Value,
                original.Id.Value,
                0,
                "afterimage");
            AddUncommonPresentation(
                "afterimage_spawn",
                phantom.Id,
                original.Id,
                effective.Amount);
        }

        private void ProcessBindTrapPulse(
            ProjectileState projectile,
            UncommonProjectileEffectRuntime effect)
        {
            if (tick < effect.NextTick)
            {
                return;
            }

            effect.NextTick += Math.Max(
                1,
                effect.Node.IntervalTicks > 0
                    ? effect.Node.IntervalTicks
                    : DefaultPulseIntervalTicks);
            CollectNearbyEnemies(
                projectile.Position,
                ResolveRadius(effect.Node),
                EntityId.Invalid,
                effect.Node.Limit);
            for (int i = 0; i < uncommonEnemyScratch.Count; i++)
            {
                EnemyState target = uncommonEnemyScratch[i];
                EffectExecutionContext context =
                    effect.CreateEnemyContext(
                        target.Id,
                        projectile.Id,
                        EventId.Invalid,
                        effect.Depth + 1);
                ApplyStrongControlStatus(
                    context,
                    StatusType.Bind,
                    effect.Node);
            }
            AddUncommonPresentation(
                "bind_pulse",
                projectile.Id,
                projectile.Id,
                uncommonEnemyScratch.Count);
        }

        private void ProcessProjectilePulse(
            ProjectileState projectile,
            UncommonProjectileEffectRuntime effect)
        {
            if (tick < effect.NextTick)
            {
                return;
            }

            effect.NextTick += Math.Max(
                1,
                effect.Node.IntervalTicks > 0
                    ? effect.Node.IntervalTicks
                    : DefaultPulseIntervalTicks);
            long damage = ResolveDamage(
                projectile.DamageMilli,
                effect.Node.Amount);
            ExecuteUncommonAreaDamage(
                projectile.Position,
                damage,
                ResolveRadius(effect.Node),
                effect.Node.Limit,
                effect.TowerId,
                effect.CardId,
                projectile.Id,
                effect.RootChainId,
                effect.ActivationId,
                effect.ParentEventId,
                effect.Depth + 1,
                DamageKind.Physical,
                EventTags.Area,
                "pulse");
        }

        private void ProcessProjectileMagnet(
            ProjectileState projectile,
            UncommonProjectileEffectRuntime effect)
        {
            if (tick < effect.NextTick)
            {
                return;
            }

            effect.NextTick += Math.Max(
                1,
                effect.Node.IntervalTicks > 0
                    ? effect.Node.IntervalTicks
                    : 3);
            int radius = ResolveRadius(effect.Node);
            ProjectileState nearest = null;
            ulong nearestDistance = ulong.MaxValue;
            for (int i = 0; i < projectiles.Count; i++)
            {
                ProjectileState candidate = projectiles[i];
                if (candidate == null ||
                    !candidate.Alive ||
                    candidate.Id == projectile.Id ||
                    candidate.SourceTowerId !=
                    projectile.SourceTowerId ||
                    effect.Contacts.Contains(candidate.Id.Value))
                {
                    continue;
                }

                ulong distance =
                    projectile.Position.DistanceSquaredRaw(
                        candidate.Position);
                if (distance > (ulong)((long)radius * radius) ||
                    (nearest != null &&
                     (distance > nearestDistance ||
                      (distance == nearestDistance &&
                       candidate.Id.Value >
                       nearest.Id.Value))))
                {
                    continue;
                }

                nearest = candidate;
                nearestDistance = distance;
            }

            if (nearest == null)
            {
                return;
            }

            long dx = projectile.Position.X.MilliUnits -
                      nearest.Position.X.MilliUnits;
            long dy = projectile.Position.Y.MilliUnits -
                      nearest.Position.Y.MilliUnits;
            int pullBps = effect.Node.Amount > 0
                ? Math.Max(1000, Math.Min(10000, effect.Node.Amount))
                : 4000;
            nearest.Position = SimPosition.FromMilliUnits(
                nearest.Position.X.MilliUnits +
                DeterministicMath.MultiplyBasisPoints(
                    dx,
                    pullBps),
                nearest.Position.Y.MilliUnits +
                DeterministicMath.MultiplyBasisPoints(
                    dy,
                    pullBps));

            int mergeRadius = checked(
                projectile.RadiusMilli +
                nearest.RadiusMilli);
            if (!PathModel.IsWithin(
                    projectile.Position,
                    nearest.Position,
                    mergeRadius))
            {
                return;
            }

            effect.Contacts.Add(nearest.Id.Value);
            projectile.DamageMilli = checked(
                projectile.DamageMilli +
                DeterministicMath.MultiplyBasisPoints(
                    nearest.DamageMilli,
                    effect.Node.Amount2 > 0
                        ? effect.Node.Amount2
                        : DefaultDamageBps));
            projectile.RadiusMilli = Math.Min(
                4000,
                checked(
                    projectile.RadiusMilli +
                    Math.Max(1, nearest.RadiusMilli / 2)));
            if (nearest.Bindings.Count > 0)
            {
                projectile.Bindings.Add(
                    nearest.Bindings[0].Clone());
            }
            CopyFirstUncommonEffect(
                nearest,
                projectile,
                EffectOperation.EnableProjectileMagnet);
            nearest.Alive = false;
            AddUncommonPresentation(
                "magnet_merge",
                projectile.Id,
                nearest.Id,
                projectile.DamageMilli);
        }

        private bool ProcessReflectedProjectile(
            ProjectileState projectile,
            UncommonProjectileEffectRuntime effect)
        {
            if (!effect.Returning)
            {
                return false;
            }

            TowerState tower = FindTower(projectile.SourceTowerId);
            if (tower == null)
            {
                effect.Returning = false;
                return false;
            }

            long distance = PathModel.DistanceMilli(
                projectile.Position,
                tower.Position);
            if (distance <= projectile.SpeedMilliPerTick)
            {
                projectile.Position = tower.Position;
                effect.Returning = false;
                EnemyState target =
                    SelectProjectileTarget(projectile);
                if (target == null)
                {
                    ScheduleProjectileExpiration(
                        projectile,
                        effect.ParentEventId);
                    return true;
                }

                projectile.TargetId = target.Id;
                projectile.Homing = true;
                SetProjectileDirection(
                    projectile,
                    target.Position);
                return true;
            }

            SetProjectileDirection(
                projectile,
                tower.Position);
            return false;
        }

        private void ProcessProjectileContagion(
            ProjectileState projectile,
            UncommonProjectileEffectRuntime effect)
        {
            if (tick < effect.NextTick)
            {
                return;
            }

            effect.NextTick += Math.Max(
                1,
                effect.Node.IntervalTicks > 0
                    ? effect.Node.IntervalTicks
                    : 3);
            int radius = ResolveRadius(effect.Node);
            ProjectileState nearest = null;
            ulong nearestDistance = ulong.MaxValue;
            for (int i = 0; i < projectiles.Count; i++)
            {
                ProjectileState candidate = projectiles[i];
                if (candidate == null ||
                    !candidate.Alive ||
                    candidate.Id == projectile.Id ||
                    candidate.SourceTowerId !=
                    projectile.SourceTowerId ||
                    effect.Contacts.Contains(candidate.Id.Value))
                {
                    continue;
                }

                ulong distance =
                    projectile.Position.DistanceSquaredRaw(
                        candidate.Position);
                if (distance > (ulong)((long)radius * radius) ||
                    (nearest != null &&
                     (distance > nearestDistance ||
                      (distance == nearestDistance &&
                       candidate.Id.Value >
                       nearest.Id.Value))))
                {
                    continue;
                }

                nearest = candidate;
                nearestDistance = distance;
            }

            if (nearest == null)
            {
                return;
            }

            bool copied = CopyFirstStatusBinding(
                projectile,
                nearest);
            if (!copied)
            {
                copied = CopyFirstUncommonEffect(
                    projectile,
                    nearest,
                    EffectOperation.EnableProjectileContagion);
            }
            if (!copied)
            {
                return;
            }

            effect.Contacts.Add(nearest.Id.Value);
            AddUncommonPresentation(
                "contagion_transfer",
                nearest.Id,
                projectile.Id,
                1);
        }

        private void ProcessOrbitingProjectile(
            ProjectileState projectile,
            UncommonProjectileEffectRuntime effect)
        {
            EnemyState anchor = FindEnemy(effect.AnchorTargetId);
            if (anchor == null || !anchor.Alive)
            {
                effect.Anchored = false;
                ScheduleProjectileExpiration(
                    projectile,
                    effect.ParentEventId);
                return;
            }

            effect.RemainingTicks--;
            if (effect.RemainingTicks <= 0)
            {
                ScheduleProjectileExpiration(
                    projectile,
                    effect.ParentEventId);
                return;
            }

            int radius = ResolveRadius(effect.Node);
            int phaseIndex = (int)(
                (tick + projectile.Id.Value) & 7L);
            GetEightDirectionOffset(
                phaseIndex,
                radius,
                out int offsetX,
                out int offsetY);
            projectile.Position = SimPosition.FromMilliUnits(
                anchor.Position.X.MilliUnits + offsetX,
                anchor.Position.Y.MilliUnits + offsetY);
            projectile.TargetId = anchor.Id;
            if (tick < effect.NextTick)
            {
                return;
            }

            effect.NextTick += Math.Max(
                1,
                effect.Node.IntervalTicks > 0
                    ? effect.Node.IntervalTicks
                    : DefaultPulseIntervalTicks);
            EnqueueDamage(
                anchor.Id,
                effect.TowerId,
                effect.CardId,
                projectile.Id,
                ResolveDamage(
                    projectile.DamageMilli,
                    effect.Node.Amount),
                DamageKind.Physical,
                0,
                effect.RootChainId,
                effect.ActivationId,
                effect.ParentEventId,
                effect.Depth + 1,
                EventTags.Projectile |
                EventTags.SingleTarget |
                EventTags.Repeated);
            AddUncommonPresentation(
                "orbit_hit",
                anchor.Id,
                projectile.Id,
                projectile.DamageMilli);
        }

        private void ExecuteAirborneLanding(
            ProjectileState projectile,
            EnemyState directTarget,
            UncommonProjectileEffectRuntime effect,
            in GameEvent parentEvent)
        {
            int radius = ResolveRadius(effect.Node);
            CollectNearbyEnemies(
                directTarget.Position,
                radius,
                EntityId.Invalid,
                effect.Node.Limit);
            for (int i = 0; i < uncommonEnemyScratch.Count; i++)
            {
                EnemyState target = uncommonEnemyScratch[i];
                EffectExecutionContext context =
                    effect.CreateEnemyContext(
                        target.Id,
                        projectile.Id,
                        parentEvent.EventId,
                        parentEvent.Depth + 1);
                ApplyAirborneStatus(
                    context,
                    effect.Node);
                StatusInstance airborne = FindStatus(
                    target,
                    StatusType.Airborne,
                    context.TowerId,
                    context.CardId);
                if (airborne != null &&
                    airborneRicochetTriggeredStatuses.Add(
                        airborne.InstanceId))
                {
                    TryEnemyRicochetAfterForcedMovement(
                        target,
                        context);
                }
            }

            ExecuteUncommonAreaDamage(
                directTarget.Position,
                ResolveDamage(
                    projectile.DamageMilli,
                    effect.Node.Amount2),
                radius,
                effect.Node.Limit,
                effect.TowerId,
                effect.CardId,
                projectile.Id,
                effect.RootChainId,
                effect.ActivationId,
                parentEvent.EventId,
                parentEvent.Depth + 1,
                DamageKind.Collision,
                EventTags.Area | EventTags.Control,
                "airborne_land");
        }

        private void ExecuteChainDamage(
            EnemyState origin,
            long damageMilli,
            int radiusMilli,
            int limit,
            TowerId sourceTowerId,
            CardId sourceCardId,
            EntityId sourceEntityId,
            ChainId chainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            string presentationId)
        {
            if (origin == null || damageMilli <= 0)
            {
                return;
            }

            CollectNearbyEnemies(
                origin.Position,
                radiusMilli > 0
                    ? radiusMilli
                    : DefaultEffectRadiusMilli,
                origin.Id,
                limit);
            uncommonEventScratch.Clear();
            for (int i = 0; i < uncommonEnemyScratch.Count; i++)
            {
                EnemyState target = uncommonEnemyScratch[i];
                if (TryCreateDamageEvent(
                        target.Id,
                        sourceTowerId,
                        sourceCardId,
                        sourceEntityId,
                        damageMilli,
                        DamageKind.Physical,
                        0,
                        chainId,
                        activationId,
                        parentEventId,
                        depth,
                        EventTags.SingleTarget |
                        EventTags.Repeated,
                        out GameEvent damageEvent))
                {
                    uncommonEventScratch.Add(damageEvent);
                }
            }

            if (TryEnqueueBatch(uncommonEventScratch) &&
                uncommonEventScratch.Count > 0)
            {
                AddUncommonPresentation(
                    presentationId,
                    uncommonEnemyScratch[0].Id,
                    origin.Id,
                    damageMilli);
            }
        }

        private void ExecuteUncommonAreaDamage(
            SimPosition position,
            long damageMilli,
            int radiusMilli,
            int limit,
            TowerId sourceTowerId,
            CardId sourceCardId,
            EntityId sourceEntityId,
            ChainId chainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            DamageKind damageKind,
            EventTags tags,
            string presentationId)
        {
            if (damageMilli <= 0)
            {
                return;
            }

            CollectNearbyEnemies(
                position,
                radiusMilli > 0
                    ? radiusMilli
                    : DefaultEffectRadiusMilli,
                EntityId.Invalid,
                limit);
            uncommonEventScratch.Clear();
            for (int i = 0; i < uncommonEnemyScratch.Count; i++)
            {
                EnemyState target = uncommonEnemyScratch[i];
                if (TryCreateDamageEvent(
                        target.Id,
                        sourceTowerId,
                        sourceCardId,
                        sourceEntityId,
                        damageMilli,
                        damageKind,
                        0,
                        chainId,
                        activationId,
                        parentEventId,
                        depth,
                        tags,
                        out GameEvent damageEvent))
                {
                    uncommonEventScratch.Add(damageEvent);
                }
            }

            if (TryEnqueueBatch(uncommonEventScratch) &&
                uncommonEventScratch.Count > 0)
            {
                AddUncommonPresentation(
                    presentationId,
                    uncommonEnemyScratch[0].Id,
                    sourceEntityId,
                    damageMilli);
            }
        }

        private void CollectNearbyEnemies(
            SimPosition origin,
            int radiusMilli,
            EntityId excludedId,
            int limit)
        {
            uncommonEnemyScratch.Clear();
            int maximumEnemyRadius = checked(
                run.EnemyBaseHitRadiusMilli * 3);
            spatialIndex.Query(
                origin,
                checked(
                    Math.Max(1, radiusMilli) +
                    maximumEnemyRadius),
                spatialScratch);
            for (int i = 0; i < spatialScratch.Count; i++)
            {
                EnemyState enemy = FindEnemy(spatialScratch[i]);
                if (enemy == null ||
                    !enemy.Alive ||
                    enemy.DeathQueued ||
                    enemy.Id == excludedId ||
                    !PathModel.IsWithin(
                        origin,
                        enemy.Position,
                        checked(
                            Math.Max(1, radiusMilli) +
                            GetEnemyHitRadiusMilli(enemy))))
                {
                    continue;
                }
                uncommonEnemyScratch.Add(enemy);
            }
            uncommonEnemyScratch.Sort((left, right) =>
                CompareTargetPriority(origin, left, right));
            int boundedLimit = limit <= 0
                ? uncommonEnemyScratch.Count
                : Math.Min(limit, uncommonEnemyScratch.Count);
            if (boundedLimit < uncommonEnemyScratch.Count)
            {
                uncommonEnemyScratch.RemoveRange(
                    boundedLimit,
                    uncommonEnemyScratch.Count - boundedLimit);
            }
        }

        private EnemyState SelectNearestUncommonEnemy(
            SimPosition origin,
            int radiusMilli,
            EntityId excludedId)
        {
            CollectNearbyEnemies(
                origin,
                radiusMilli,
                excludedId,
                1);
            return uncommonEnemyScratch.Count == 0
                ? null
                : uncommonEnemyScratch[0];
        }

        private void PullProjectilesToEnemy(
            EnemyState enemy,
            StatusInstance status)
        {
            int radius = status.RadiusMilli > 0
                ? status.RadiusMilli
                : DefaultEffectRadiusMilli;
            int changed = 0;
            for (int i = 0; i < projectiles.Count; i++)
            {
                ProjectileState projectile = projectiles[i];
                if (projectile == null ||
                    !projectile.Alive ||
                    !PathModel.IsWithin(
                        enemy.Position,
                        projectile.Position,
                        radius))
                {
                    continue;
                }

                projectile.TargetId = enemy.Id;
                projectile.Homing = true;
                SetProjectileDirection(
                    projectile,
                    enemy.Position);
                changed++;
            }
            if (changed > 0)
            {
                AddUncommonPresentation(
                    "magnet_merge",
                    enemy.Id,
                    status.SourceEntityId,
                    changed);
            }
        }

        private void SpreadOneStatus(
            EnemyState source,
            StatusInstance trigger,
            bool moveSource)
        {
            StatusInstance transferable = null;
            for (int i = 0; i < source.Statuses.Count; i++)
            {
                StatusInstance candidate = source.Statuses[i];
                if (!IsTransferableStatus(candidate.Type) ||
                    candidate.RemainingTicks <= 0 ||
                    (transferable != null &&
                     candidate.InstanceId >
                     transferable.InstanceId))
                {
                    continue;
                }
                transferable = candidate;
            }

            if (transferable == null)
            {
                return;
            }

            EnemyState target = SelectNearestUncommonEnemy(
                source.Position,
                trigger.RadiusMilli > 0
                    ? trigger.RadiusMilli
                    : DefaultEffectRadiusMilli,
                source.Id);
            if (target == null)
            {
                return;
            }

            var context = new EffectExecutionContext(
                SubjectType.Enemy,
                target.Id,
                transferable.SourceTowerId,
                transferable.SourceCardId,
                transferable.SourceCardInstanceId,
                source.Id,
                CreateRootChain(),
                CreateActivation(),
                EventId.Invalid,
                0,
                0,
                0);
            CompiledEffectNode transferredNode =
                StatusNodeFrom(transferable);
            if (transferable.Type == StatusType.Chill)
            {
                ApplyFreeze(context, transferredNode);
            }
            else if (transferable.Type == StatusType.Bleed)
            {
                ApplyBleed(context, transferredNode);
            }
            else if (transferable.Type == StatusType.Corrosion)
            {
                ApplyCorrosion(context, transferredNode);
            }
            else
            {
                ApplyStatus(
                    context,
                    transferable.Type,
                    transferredNode);
            }
            if (moveSource)
            {
                source.Statuses.Remove(transferable);
            }
            AddUncommonPresentation(
                "contagion_transfer",
                target.Id,
                source.Id,
                (int)transferable.Type);
        }

        private void TickCorrosion(
            EnemyState enemy,
            StatusInstance status)
        {
            int armorLoss = Math.Max(
                1,
                checked(status.Intensity * Math.Max(1, status.Stacks)));
            enemy.Armor = Math.Max(0, enemy.Armor - armorLoss);
            if (!corrosionHealthFloors.TryGetValue(
                    enemy.Id.Value,
                    out long floor))
            {
                floor = Math.Max(
                    1000,
                    DeterministicMath.MultiplyBasisPoints(
                        enemy.MaxHealthMilli,
                        status.Limit > 0
                            ? status.Limit
                            : 3000));
                corrosionHealthFloors[enemy.Id.Value] = floor;
            }

            int reductionBps = status.ArmorIgnoreBps > 0
                ? Math.Min(3000, status.ArmorIgnoreBps)
                : 250;
            long reducedMax = Math.Max(
                floor,
                DeterministicMath.MultiplyBasisPoints(
                    enemy.MaxHealthMilli,
                    10000 - reductionBps));
            enemy.MaxHealthMilli = reducedMax;
            enemy.HealthMilli = Math.Min(
                enemy.HealthMilli,
                enemy.MaxHealthMilli);
            AddUncommonPresentation(
                "corrosion_tick",
                enemy.Id,
                status.SourceEntityId,
                armorLoss);
        }

        private void DamageOrbitCollisions(
            EnemyState enemy,
            StatusInstance status)
        {
            EnemyState target = SelectNearestUncommonEnemy(
                enemy.Position,
                Math.Max(
                    GetEnemyHitRadiusMilli(enemy) * 2,
                    status.RadiusMilli),
                enemy.Id);
            if (target == null)
            {
                return;
            }

            ChainId chainId = CreateRootChain();
            ActivationId activationId = CreateActivation();
            uncommonEventScratch.Clear();
            long damage = Math.Max(1, status.Intensity);
            if (TryCreateDamageEvent(
                    enemy.Id,
                    status.SourceTowerId,
                    status.SourceCardId,
                    enemy.Id,
                    damage,
                    DamageKind.Collision,
                    0,
                    chainId,
                    activationId,
                    EventId.Invalid,
                    0,
                    EventTags.Control,
                    out GameEvent selfDamage))
            {
                uncommonEventScratch.Add(selfDamage);
            }
            if (TryCreateDamageEvent(
                    target.Id,
                    status.SourceTowerId,
                    status.SourceCardId,
                    enemy.Id,
                    damage,
                    DamageKind.Collision,
                    0,
                    chainId,
                    activationId,
                    EventId.Invalid,
                    0,
                    EventTags.Control,
                    out GameEvent targetDamage))
            {
                uncommonEventScratch.Add(targetDamage);
            }
            if (uncommonEventScratch.Count == 2 &&
                TryEnqueueBatch(uncommonEventScratch))
            {
                AddUncommonPresentation(
                    "orbit_hit",
                    target.Id,
                    enemy.Id,
                    damage);
            }
        }

        private bool HandleEnemyReflect(
            ProjectileState projectile,
            EnemyState hitEnemy,
            in GameEvent parentEvent)
        {
            StatusInstance reflect = FindFirstActiveStatus(
                hitEnemy,
                StatusType.Reflect);
            if (reflect == null)
            {
                return false;
            }

            EnemyState next = SelectProjectileTarget(projectile);
            if (next == null)
            {
                return false;
            }

            projectile.Position = hitEnemy.Position;
            projectile.TargetId = next.Id;
            projectile.Homing = true;
            SetProjectileDirection(
                projectile,
                next.Position);
            AddUncommonPresentation(
                "reflect_turn",
                next.Id,
                projectile.Id,
                1);
            return true;
        }

        private bool CopyFirstStatusBinding(
            ProjectileState source,
            ProjectileState target)
        {
            for (int i = 0; i < source.Bindings.Count; i++)
            {
                EffectBinding candidate = source.Bindings[i];
                if (candidate.Kind != BindingKind.Burn &&
                    candidate.Kind != BindingKind.Poison &&
                    candidate.Kind != BindingKind.Mark &&
                    candidate.Kind != BindingKind.Stun)
                {
                    continue;
                }

                bool duplicate = false;
                for (int j = 0; j < target.Bindings.Count; j++)
                {
                    EffectBinding existing = target.Bindings[j];
                    if (existing.Kind == candidate.Kind &&
                        existing.CardInstanceId ==
                        candidate.CardInstanceId)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                {
                    EffectBinding copied = candidate.Clone();
                    copied.Used = false;
                    copied.TriggerCount = 0;
                    copied.TrailStarted = false;
                    copied.ActiveTrailHazardId = -1;
                    target.Bindings.Add(copied);
                    return true;
                }
            }
            return false;
        }

        private bool CopyFirstUncommonEffect(
            ProjectileState source,
            ProjectileState target,
            EffectOperation excludedOperation)
        {
            if (!uncommonProjectileEffects.TryGetValue(
                    source.Id.Value,
                    out List<UncommonProjectileEffectRuntime> sourceEffects))
            {
                return false;
            }

            if (!uncommonProjectileEffects.TryGetValue(
                    target.Id.Value,
                    out List<UncommonProjectileEffectRuntime> targetEffects))
            {
                targetEffects =
                    new List<UncommonProjectileEffectRuntime>(2);
                uncommonProjectileEffects.Add(
                    target.Id.Value,
                    targetEffects);
            }

            for (int i = 0; i < sourceEffects.Count; i++)
            {
                UncommonProjectileEffectRuntime candidate =
                    sourceEffects[i];
                if (candidate.Operation == excludedOperation ||
                    !IsStatusProducingProjectileOperation(
                        candidate.Operation))
                {
                    continue;
                }

                bool duplicate = false;
                for (int j = 0; j < targetEffects.Count; j++)
                {
                    if (targetEffects[j].Operation ==
                            candidate.Operation &&
                        targetEffects[j].CardInstanceId ==
                            candidate.CardInstanceId)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                {
                    UncommonProjectileEffectRuntime copied =
                        candidate.CloneFor(target.Id);
                    copied.Used = false;
                    copied.TriggerCount = 0;
                    copied.Returning = false;
                    targetEffects.Add(copied);
                    return true;
                }
            }
            return false;
        }

        private EffectExecutionContext ContextFromStatus(
            EnemyState enemy,
            StatusInstance status)
        {
            return new EffectExecutionContext(
                SubjectType.Enemy,
                enemy.Id,
                status.SourceTowerId,
                status.SourceCardId,
                status.SourceCardInstanceId,
                status.SourceEntityId,
                CreateRootChain(),
                CreateActivation(),
                EventId.Invalid,
                0,
                0,
                0);
        }

        private static StatusInstance FindStatus(
            EnemyState enemy,
            StatusType type,
            TowerId towerId,
            CardId cardId)
        {
            if (enemy == null)
            {
                return null;
            }

            for (int i = 0; i < enemy.Statuses.Count; i++)
            {
                StatusInstance status = enemy.Statuses[i];
                if (status.Type == type &&
                    status.SourceTowerId == towerId &&
                    status.SourceCardId == cardId &&
                    status.RemainingTicks > 0)
                {
                    return status;
                }
            }
            return null;
        }

        private static StatusInstance FindFirstActiveStatus(
            EnemyState enemy,
            StatusType type)
        {
            StatusInstance selected = null;
            if (enemy == null)
            {
                return null;
            }

            for (int i = 0; i < enemy.Statuses.Count; i++)
            {
                StatusInstance candidate = enemy.Statuses[i];
                if (candidate.Type != type ||
                    candidate.RemainingTicks <= 0 ||
                    (selected != null &&
                     candidate.InstanceId >
                     selected.InstanceId))
                {
                    continue;
                }
                selected = candidate;
            }
            return selected;
        }

        private bool HasAnyStatus(
            EnemyState enemy,
            StatusType first,
            StatusType second,
            StatusType third)
        {
            return HasActiveStatus(enemy, first) ||
                   HasActiveStatus(enemy, second) ||
                   HasActiveStatus(enemy, third);
        }

        private static int GetCurseStrengthBps(
            EnemyState enemy)
        {
            if (enemy == null)
            {
                return 0;
            }

            long result = 0;
            int capBps = 10000;
            for (int i = 0; i < enemy.Statuses.Count; i++)
            {
                StatusInstance status = enemy.Statuses[i];
                if (status.Type != StatusType.Curse ||
                    status.RemainingTicks <= 0)
                {
                    continue;
                }

                result = Math.Min(
                    int.MaxValue,
                    result +
                    (long)Math.Max(0, status.Intensity) *
                    Math.Max(1, status.Stacks));
                if (status.Limit > 0)
                {
                    capBps = Math.Min(
                        capBps,
                        status.Limit);
                }
            }
            return (int)Math.Min(
                Math.Max(0, capBps),
                result);
        }

        private static bool IsTransferableStatus(
            StatusType type)
        {
            switch (type)
            {
                case StatusType.Burn:
                case StatusType.Poison:
                case StatusType.Slow:
                case StatusType.Mark:
                case StatusType.Pierced:
                case StatusType.Bleed:
                case StatusType.Curse:
                case StatusType.Chill:
                case StatusType.Seal:
                case StatusType.Corrosion:
                case StatusType.Fear:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsStatusProducingProjectileOperation(
            EffectOperation operation)
        {
            switch (operation)
            {
                case EffectOperation.BindCurse:
                case EffectOperation.BindShock:
                case EffectOperation.BindFreeze:
                case EffectOperation.BindSeal:
                case EffectOperation.BindCorrosion:
                case EffectOperation.BindFear:
                    return true;
                default:
                    return false;
            }
        }

        private static CompiledEffectNode StatusNodeFrom(
            StatusInstance status)
        {
            return new CompiledEffectNode(
                ResolveApplyOperation(status.Type),
                status.Intensity,
                0,
                0,
                Math.Max(1, status.RemainingTicks),
                status.TickInterval,
                status.MaxStacks,
                status.RadiusMilli,
                status.Limit,
                status.ArmorIgnoreBps,
                null);
        }

        private static EffectOperation ResolveApplyOperation(
            StatusType type)
        {
            switch (type)
            {
                case StatusType.Burn:
                    return EffectOperation.ApplyBurn;
                case StatusType.Poison:
                    return EffectOperation.ApplyPoison;
                case StatusType.Slow:
                    return EffectOperation.ApplySlow;
                case StatusType.Mark:
                    return EffectOperation.ApplyMark;
                case StatusType.Bleed:
                    return EffectOperation.ApplyBleed;
                case StatusType.Pierced:
                    return EffectOperation.AddPierce;
                case StatusType.Curse:
                    return EffectOperation.ApplyCurse;
                case StatusType.Chill:
                    return EffectOperation.ApplyFreeze;
                case StatusType.Seal:
                    return EffectOperation.ApplySeal;
                case StatusType.Corrosion:
                    return EffectOperation.ApplyCorrosion;
                case StatusType.Fear:
                    return EffectOperation.ApplyFear;
                default:
                    return EffectOperation.AddPierce;
            }
        }

        private static CompiledEffectNode WithDefaults(
            in CompiledEffectNode source,
            int amount,
            int durationTicks,
            int intervalTicks,
            int maxStacks,
            int radiusMilli,
            int limit)
        {
            return new CompiledEffectNode(
                source.Operation,
                amount,
                source.Amount2,
                source.Amount3,
                durationTicks,
                intervalTicks,
                maxStacks,
                radiusMilli,
                limit,
                source.ChanceBps,
                source.ReferenceId);
        }

        private static int ResolveRadius(
            in CompiledEffectNode node)
        {
            return node.RadiusMilli > 0
                ? node.RadiusMilli
                : DefaultEffectRadiusMilli;
        }

        private static int ResolveTriggerLimit(
            in CompiledEffectNode node,
            int fallback)
        {
            return node.Limit > 0
                ? node.Limit
                : Math.Max(1, fallback);
        }

        private static long ResolveDamage(
            long baseDamageMilli,
            int damageBps)
        {
            return DeterministicMath.MultiplyBasisPoints(
                Math.Max(1, baseDamageMilli),
                damageBps > 0
                    ? Math.Min(30000, damageBps)
                    : DefaultDamageBps);
        }

        private void HealBase(
            int amount,
            EntityId subjectId,
            EntityId sourceId)
        {
            if (amount <= 0 || baseHealth >= run.BaseHealth)
            {
                return;
            }

            int actual = Math.Min(
                amount,
                run.BaseHealth - baseHealth);
            baseHealth += actual;
            AddUncommonPresentation(
                "lifesteal_heal",
                subjectId,
                sourceId,
                actual);
        }

        private void AddUncommonPresentation(
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

        private static void GetEightDirectionOffset(
            int phase,
            int radius,
            out int x,
            out int y)
        {
            int diagonal = (int)
                DeterministicMath.MultiplyBasisPoints(
                    radius,
                    7071);
            switch (phase & 7)
            {
                case 0:
                    x = radius;
                    y = 0;
                    break;
                case 1:
                    x = diagonal;
                    y = diagonal;
                    break;
                case 2:
                    x = 0;
                    y = radius;
                    break;
                case 3:
                    x = -diagonal;
                    y = diagonal;
                    break;
                case 4:
                    x = -radius;
                    y = 0;
                    break;
                case 5:
                    x = -diagonal;
                    y = -diagonal;
                    break;
                case 6:
                    x = 0;
                    y = -radius;
                    break;
                default:
                    x = diagonal;
                    y = -diagonal;
                    break;
            }
        }

        private static ProjectileEffectVisualFlags GetVisualFlag(
            EffectOperation operation)
        {
            switch (operation)
            {
                case EffectOperation.BindCurse:
                    return ProjectileEffectVisualFlags.Curse;
                case EffectOperation.CreateBindTrap:
                    return ProjectileEffectVisualFlags.Bind;
                case EffectOperation.MakeAirborneProjectile:
                    return ProjectileEffectVisualFlags.Airborne;
                case EffectOperation.BindShock:
                    return ProjectileEffectVisualFlags.Shock;
                case EffectOperation.BindFreeze:
                    return ProjectileEffectVisualFlags.Freeze;
                case EffectOperation.CreateAfterimageProjectile:
                    return ProjectileEffectVisualFlags.Afterimage;
                case EffectOperation.EnableProjectilePulse:
                    return ProjectileEffectVisualFlags.Pulse;
                case EffectOperation.EnableProjectileMagnet:
                    return ProjectileEffectVisualFlags.Magnet;
                case EffectOperation.EnableProjectileReflect:
                    return ProjectileEffectVisualFlags.Reflect;
                case EffectOperation.EnableProjectileContagion:
                    return ProjectileEffectVisualFlags.Contagion;
                case EffectOperation.BindSeal:
                    return ProjectileEffectVisualFlags.Seal;
                case EffectOperation.BindCorrosion:
                    return ProjectileEffectVisualFlags.Corrosion;
                case EffectOperation.EnableProjectileOrbit:
                    return ProjectileEffectVisualFlags.Orbit;
                case EffectOperation.BindLifesteal:
                    return ProjectileEffectVisualFlags.Lifesteal;
                case EffectOperation.BindFear:
                    return ProjectileEffectVisualFlags.Fear;
                default:
                    return ProjectileEffectVisualFlags.None;
            }
        }

        private static void AppendUncommonEffectHash(
            ref StableHashBuilder hash,
            UncommonProjectileEffectRuntime effect)
        {
            hash.Add((int)effect.Operation);
            AppendEffectNodeHash(ref hash, effect.Node);
            hash.Add(effect.TowerId);
            hash.Add(effect.CardId);
            hash.Add(effect.CardInstanceId);
            hash.Add(effect.SourceEntityId);
            hash.Add(effect.RootChainId);
            hash.Add(effect.ActivationId);
            hash.Add(effect.ParentEventId);
            hash.Add(effect.Depth);
            hash.Add(effect.Used);
            hash.Add(effect.Anchored);
            hash.Add(effect.Returning);
            hash.Add(effect.TriggerCount);
            hash.Add(effect.DelayRemaining);
            hash.Add(effect.RemainingTicks);
            hash.Add(effect.NextTick);
            hash.Add(effect.AnchorTargetId);
            hash.Add(effect.AnchorPosition);
            int[] contacts = new int[effect.Contacts.Count];
            effect.Contacts.CopyTo(contacts);
            Array.Sort(contacts);
            hash.Add(contacts.Length);
            for (int i = 0; i < contacts.Length; i++)
            {
                hash.Add(contacts[i]);
            }
        }
    }
}

namespace RuleforgeTD.GameLogic.Effects
{
    using RuleforgeTD.GameLogic.Content;
    using RuleforgeTD.GameLogic.Simulation;

    /// <summary>
    /// 고급 카드 operation은 데이터가 탄환/적 해석을 이미 구분하므로 하나의
    /// 무상태 adapter를 공유하고 실제 규칙은 GameSimulation partial에 둔다.
    /// </summary>
    internal sealed class UncommonEffectExecutor : IEffectExecutor
    {
        private readonly EffectOperation operation;

        public UncommonEffectExecutor(EffectOperation operation)
        {
            this.operation = operation;
        }

        public EffectExecutionOutcome Execute(
            GameSimulation simulation,
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            simulation.ExecuteUncommonEffect(
                context,
                operation,
                node);
            return EffectExecutionOutcome.Continue();
        }
    }
}
