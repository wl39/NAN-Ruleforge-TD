using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 신화 카드가 만드는 파생 적의 역할이다. 공용 EnemyState를 확장하지 않고도
    /// 이동·피해·보상 수명주기를 명시적인 정책으로 구분한다.
    /// </summary>
    internal enum MythicEnemyProxyKind
    {
        TimePast = 0,
        TimeFuture = 1,
        MirrorIllusion = 2
    }

    internal sealed class MythicMotionHistory
    {
        private const int Capacity = 128;
        private readonly SimPosition[] positions =
            new SimPosition[Capacity];
        private int head;
        private int count;

        public long LastRecordedTick = -1;

        public int Count => count;

        public void Record(long currentTick, SimPosition position)
        {
            if (LastRecordedTick == currentTick)
            {
                return;
            }

            positions[head] = position;
            head = (head + 1) % Capacity;
            count = Math.Min(Capacity, count + 1);
            LastRecordedTick = currentTick;
        }

        public bool TryGetTicksAgo(int ticksAgo, out SimPosition position)
        {
            if (ticksAgo < 0 || ticksAgo >= count)
            {
                position = SimPosition.Origin;
                return false;
            }

            int index = head - 1 - ticksAgo;
            while (index < 0)
            {
                index += Capacity;
            }
            position = positions[index];
            return true;
        }

        public SimPosition GetChronological(int index)
        {
            if (index < 0 || index >= count)
            {
                return SimPosition.Origin;
            }

            int oldest = head - count;
            while (oldest < 0)
            {
                oldest += Capacity;
            }
            return positions[(oldest + index) % Capacity];
        }
    }

    internal sealed class MythicCapturedProjectile
    {
        public EntityId SourceId;
        public TowerId SourceTowerId;
        public long DamageMilli;
        public CardEffectVisualFlags VisualFlags;
        public readonly List<EffectBinding> Bindings =
            new List<EffectBinding>(4);
    }

    internal sealed class MythicProjectileSingularityRuntime
    {
        public TowerId TowerId;
        public CardId CardId;
        public int CardInstanceId;
        public EntityId SourceEntityId;
        public ChainId RootChainId;
        public ActivationId ActivationId;
        public EventId ParentEventId;
        public int Depth;
        public CompiledEffectNode Node;
        public int RemainingTicks;
        public long NextTick;
        public bool Released;
        public readonly List<MythicCapturedProjectile> Captures =
            new List<MythicCapturedProjectile>(8);
    }

    internal sealed class MythicEnemySingularityRuntime
    {
        public TowerId TowerId;
        public CardId CardId;
        public int CardInstanceId;
        public EntityId SourceEntityId;
        public ChainId RootChainId;
        public ActivationId ActivationId;
        public EventId ParentEventId;
        public int Depth;
        public CompiledEffectNode Node;
        public int RemainingTicks;
        public long NextTick;
        public bool DeathPayloadConsumed;
    }

    internal sealed class MythicProjectilePhoenixRuntime
    {
        public TowerId TowerId;
        public CardId CardId;
        public int CardInstanceId;
        public EntityId SourceEntityId;
        public CompiledEffectNode Node;
        public bool Consumed;
    }

    internal sealed class MythicEnemyPhoenixRuntime
    {
        public TowerId TowerId;
        public CardId CardId;
        public int CardInstanceId;
        public EntityId SourceEntityId;
        public CompiledEffectNode Node;
        public bool Consumed;
        public int VulnerabilityBps;
        public int VulnerabilityTicks;
    }

    internal sealed class MythicTimeEchoRuntime
    {
        public EntityId EchoId;
        public EntityId SourceId;
        public MythicEnemyProxyKind Kind;
        public TowerId TowerId;
        public CardId CardId;
        public int CardInstanceId;
        public int DamageRelayBps;
        public int RemainingTicks;
        public bool LineageResolved;
    }

    internal sealed class MythicMirrorLinkRuntime
    {
        public EntityId PrimaryId;
        public EntityId IllusionId;
        public TowerId TowerId;
        public CardId CardId;
        public int CardInstanceId;
        public int SharedHealthBps;
        public int RadiusMilli;
        public bool LineageResolved;
    }

    internal sealed class MythicOuroborosRuntime
    {
        public SubjectType SubjectType;
        public EntityId SubjectId;
        public TowerId TowerId;
        public CardId CardId;
        public int CardInstanceId;
        public ChainId RootChainId;
        public CompiledEffectNode Node;
    }

    internal readonly struct MythicOuroborosVisitKey :
        IEquatable<MythicOuroborosVisitKey>
    {
        public MythicOuroborosVisitKey(
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

        public bool Equals(MythicOuroborosVisitKey other)
        {
            return RootChainId == other.RootChainId &&
                   SubjectType == other.SubjectType &&
                   SubjectId == other.SubjectId;
        }

        public override bool Equals(object obj)
        {
            return obj is MythicOuroborosVisitKey other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = RootChainId.Value;
                hash = (hash * 397) ^ (int)SubjectType;
                hash = (hash * 397) ^ SubjectId.Value;
                return hash;
            }
        }
    }

    /// <summary>
    /// 다섯 신화 카드의 파생 개체·링크·부활·프로그램 반복 상태를 한곳에
    /// 소유한다. 공용 상태 타입에 임시 필드를 흩뿌리지 않아 이후 독립 시스템으로
    /// 추출할 수 있고, 모든 순회는 EntityId와 거리의 안정 순서를 사용한다.
    /// </summary>
    public sealed partial class GameSimulation
    {
        private const int MythicMaximumEchoesPerSource = 6;
        private const int MythicMaximumReleaseTargets = 8;
        private const int MythicMaximumVulnerabilityBps = 20000;

        private readonly Dictionary<int, MythicProjectileSingularityRuntime>
            mythicProjectileSingularities =
                new Dictionary<int, MythicProjectileSingularityRuntime>();
        private readonly Dictionary<int, MythicEnemySingularityRuntime>
            mythicEnemySingularities =
                new Dictionary<int, MythicEnemySingularityRuntime>();
        private readonly Dictionary<int, MythicProjectilePhoenixRuntime>
            mythicProjectilePhoenixCores =
                new Dictionary<int, MythicProjectilePhoenixRuntime>();
        private readonly Dictionary<int, MythicEnemyPhoenixRuntime>
            mythicEnemyPhoenixCores =
                new Dictionary<int, MythicEnemyPhoenixRuntime>();
        private readonly Dictionary<int, MythicTimeEchoRuntime>
            mythicTimeEchoes =
                new Dictionary<int, MythicTimeEchoRuntime>();
        private readonly Dictionary<int, MythicMirrorLinkRuntime>
            mythicMirrorMembers =
                new Dictionary<int, MythicMirrorLinkRuntime>();
        private readonly Dictionary<int, MythicMotionHistory>
            mythicProjectileHistories =
                new Dictionary<int, MythicMotionHistory>();
        private readonly Dictionary<int, MythicMotionHistory>
            mythicEnemyHistories =
                new Dictionary<int, MythicMotionHistory>();
        private readonly Dictionary<int, MythicOuroborosRuntime>
            mythicProjectileOuroboros =
                new Dictionary<int, MythicOuroborosRuntime>();
        private readonly Dictionary<int, MythicOuroborosRuntime>
            mythicEnemyOuroboros =
                new Dictionary<int, MythicOuroborosRuntime>();
        private readonly HashSet<MythicOuroborosVisitKey>
            mythicOuroborosVisits =
                new HashSet<MythicOuroborosVisitKey>();
        private readonly HashSet<int> mythicRelayEventIds =
            new HashSet<int>();

        private readonly List<int> mythicKeyScratch =
            new List<int>(256);
        private readonly List<ProjectileState>
            mythicProjectileScratch =
                new List<ProjectileState>(64);
        private readonly List<EnemyState> mythicEnemyScratch =
            new List<EnemyState>(64);
        private readonly List<GameEvent> mythicEventScratch =
            new List<GameEvent>(64);
        private readonly List<MythicOuroborosVisitKey>
            mythicVisitScratch =
                new List<MythicOuroborosVisitKey>(64);
        private readonly HashSet<int> mythicActiveRootScratch =
            new HashSet<int>();

        /// <summary>
        /// Initialize에서 호출할 신화 카드 전용 초기화 훅이다.
        /// </summary>
        internal void ResetMythicCardState()
        {
            mythicProjectileSingularities.Clear();
            mythicEnemySingularities.Clear();
            mythicProjectilePhoenixCores.Clear();
            mythicEnemyPhoenixCores.Clear();
            mythicTimeEchoes.Clear();
            mythicMirrorMembers.Clear();
            mythicProjectileHistories.Clear();
            mythicEnemyHistories.Clear();
            mythicProjectileOuroboros.Clear();
            mythicEnemyOuroboros.Clear();
            mythicOuroborosVisits.Clear();
            mythicRelayEventIds.Clear();
            mythicKeyScratch.Clear();
            mythicProjectileScratch.Clear();
            mythicEnemyScratch.Clear();
            mythicEventScratch.Clear();
            mythicVisitScratch.Clear();
            mythicActiveRootScratch.Clear();
        }

        /// <summary>
        /// EffectRegistry의 Mythic executor가 사용하는 단일 진입점이다.
        /// 분기 생성 효과만 replacement outcome을 반환하고, 부착형 효과는 현재
        /// 대상의 프로그램을 그대로 진행시킨다.
        /// </summary>
        internal EffectExecutionOutcome ExecuteMythicEffect(
            in EffectExecutionContext context,
            EffectOperation operation,
            in CompiledEffectNode node)
        {
            switch (operation)
            {
                case EffectOperation.EnableProjectileSingularity:
                    ConfigureMythicProjectileSingularity(
                        context,
                        node);
                    return EffectExecutionOutcome.Continue();
                case EffectOperation.ApplyEnemySingularity:
                    ConfigureMythicEnemySingularity(
                        context,
                        node);
                    return EffectExecutionOutcome.Continue();
                case EffectOperation.EnableProjectilePhoenixCore:
                    ConfigureMythicProjectilePhoenix(
                        context,
                        node);
                    return EffectExecutionOutcome.Continue();
                case EffectOperation.ApplyEnemyPhoenixCore:
                    ConfigureMythicEnemyPhoenix(
                        context,
                        node);
                    return EffectExecutionOutcome.Continue();
                case EffectOperation.CreateProjectileTimeRift:
                    return CreateMythicProjectileTimeRift(
                        context,
                        node);
                case EffectOperation.ApplyEnemyTimeRift:
                    return CreateMythicEnemyTimeRift(
                        context,
                        node);
                case EffectOperation.CreateProjectileMirrorWorld:
                    return CreateMythicProjectileMirror(
                        context,
                        node);
                case EffectOperation.ApplyEnemyMirrorWorld:
                    return CreateMythicEnemyMirror(
                        context,
                        node);
                case EffectOperation.EnableProjectileOuroboros:
                    ConfigureMythicOuroboros(
                        context,
                        node);
                    return EffectExecutionOutcome.Continue();
                case EffectOperation.ApplyEnemyOuroboros:
                    ConfigureMythicOuroboros(
                        context,
                        node);
                    return EffectExecutionOutcome.Continue();
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(operation),
                        operation,
                        "Unsupported Mythic card operation.");
            }
        }

        private void ConfigureMythicProjectileSingularity(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            if (!mythicProjectileSingularities.TryGetValue(
                    projectile.Id.Value,
                    out MythicProjectileSingularityRuntime runtime))
            {
                runtime = new MythicProjectileSingularityRuntime();
                mythicProjectileSingularities.Add(
                    projectile.Id.Value,
                    runtime);
            }

            runtime.TowerId = context.TowerId;
            runtime.CardId = context.CardId;
            runtime.CardInstanceId = context.CardInstanceId;
            runtime.SourceEntityId = context.SourceEntityId;
            runtime.RootChainId = context.RootChainId;
            runtime.ActivationId = context.ActivationId;
            runtime.ParentEventId = context.ParentEventId;
            runtime.Depth = context.Depth;
            runtime.Node = node;
            runtime.RemainingTicks =
                Math.Max(runtime.RemainingTicks, node.DurationTicks);
            runtime.NextTick = Math.Max(
                tick,
                runtime.NextTick);
            projectile.VisualFlags |=
                CardEffectVisualFlags.Singularity;
            AddMythicPresentation(
                "singularity_projectile_attach",
                projectile.Id,
                context.SourceEntityId,
                node.RadiusMilli);
        }

        private void ConfigureMythicEnemySingularity(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null ||
                !enemy.Alive ||
                IsMythicEnemyProxy(enemy.Id))
            {
                return;
            }

            if (!mythicEnemySingularities.TryGetValue(
                    enemy.Id.Value,
                    out MythicEnemySingularityRuntime runtime))
            {
                runtime = new MythicEnemySingularityRuntime();
                mythicEnemySingularities.Add(
                    enemy.Id.Value,
                    runtime);
            }

            runtime.TowerId = context.TowerId;
            runtime.CardId = context.CardId;
            runtime.CardInstanceId = context.CardInstanceId;
            runtime.SourceEntityId = context.SourceEntityId;
            runtime.RootChainId = context.RootChainId;
            runtime.ActivationId = context.ActivationId;
            runtime.ParentEventId = context.ParentEventId;
            runtime.Depth = context.Depth;
            runtime.Node = node;
            runtime.RemainingTicks =
                Math.Max(runtime.RemainingTicks, node.DurationTicks);
            runtime.NextTick = Math.Max(tick, runtime.NextTick);
            enemy.VisualFlags |= CardEffectVisualFlags.Singularity;
            AddMythicPresentation(
                "singularity_enemy_attach",
                enemy.Id,
                context.SourceEntityId,
                node.RadiusMilli);
        }

        private void ConfigureMythicProjectilePhoenix(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            ProjectileState projectile =
                FindProjectile(context.SubjectId);
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            if (!mythicProjectilePhoenixCores.TryGetValue(
                    projectile.Id.Value,
                    out MythicProjectilePhoenixRuntime runtime))
            {
                runtime = new MythicProjectilePhoenixRuntime();
                mythicProjectilePhoenixCores.Add(
                    projectile.Id.Value,
                    runtime);
            }

            if (!runtime.Consumed)
            {
                runtime.TowerId = context.TowerId;
                runtime.CardId = context.CardId;
                runtime.CardInstanceId = context.CardInstanceId;
                runtime.SourceEntityId = context.SourceEntityId;
                runtime.Node = node;
            }
            projectile.VisualFlags |=
                CardEffectVisualFlags.PhoenixCore;
        }

        private void ConfigureMythicEnemyPhoenix(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState enemy = FindEnemy(context.SubjectId);
            if (enemy == null ||
                !enemy.Alive ||
                IsMythicEnemyProxy(enemy.Id))
            {
                return;
            }

            if (!mythicEnemyPhoenixCores.TryGetValue(
                    enemy.Id.Value,
                    out MythicEnemyPhoenixRuntime runtime))
            {
                runtime = new MythicEnemyPhoenixRuntime();
                mythicEnemyPhoenixCores.Add(
                    enemy.Id.Value,
                    runtime);
            }

            if (!runtime.Consumed)
            {
                runtime.TowerId = context.TowerId;
                runtime.CardId = context.CardId;
                runtime.CardInstanceId = context.CardInstanceId;
                runtime.SourceEntityId = context.SourceEntityId;
                runtime.Node = node;
            }
            enemy.VisualFlags |= CardEffectVisualFlags.PhoenixCore;
        }

        private EffectExecutionOutcome
            CreateMythicProjectileTimeRift(
                in EffectExecutionContext context,
                in CompiledEffectNode node)
        {
            ProjectileState original =
                FindProjectile(context.SubjectId);
            if (original == null ||
                !original.Alive ||
                !TryReserveMythicBranches(
                    context,
                    SubjectType.Projectile,
                    childCount: 2,
                    projectileSpawnCount: 2))
            {
                return EffectExecutionOutcome.Continue();
            }

            int span = ResolveMythicTemporalSpan(
                original.SpeedMilliPerTick,
                node.DurationTicks,
                node.RadiusMilli);
            SimPosition past = ResolveMythicProjectilePast(
                original,
                node.DurationTicks,
                span);
            SimPosition future = OffsetAlongProjectile(
                original.Position,
                original.DirectionXBps,
                original.DirectionYBps,
                span);
            int damageBps = Math.Max(1, node.Amount);
            ProjectileState pastEcho =
                CreateMythicProjectileCopy(
                    original,
                    past,
                    damageBps,
                    10000,
                    10000,
                    Math.Max(1, original.LifetimeRemaining),
                    CardEffectVisualFlags.TimeRift,
                    "time_rift_past");
            ProjectileState futureEcho =
                CreateMythicProjectileCopy(
                    original,
                    future,
                    damageBps,
                    10000,
                    10000,
                    Math.Max(1, original.LifetimeRemaining),
                    CardEffectVisualFlags.TimeRift,
                    "time_rift_future");
            GetOrCreateMythicProjectileHistory(
                pastEcho.Id).Record(tick, pastEcho.Position);
            GetOrCreateMythicProjectileHistory(
                futureEcho.Id).Record(tick, futureEcho.Position);
            return EffectExecutionOutcome.BranchThree(
                pastEcho.Id,
                futureEcho.Id,
                context.ContinuationCardCount);
        }

        private EffectExecutionOutcome CreateMythicEnemyTimeRift(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState original = FindEnemy(context.SubjectId);
            if (original == null ||
                !original.Alive ||
                IsMythicEnemyProxy(original.Id) ||
                CountMythicTimeEchoes(original.Id) >
                    MythicMaximumEchoesPerSource - 2 ||
                !CanCreateMythicEnemyProxies(
                    original,
                    2,
                    context) ||
                !TryReserveMythicBranches(
                    context,
                    SubjectType.Enemy,
                    childCount: 2,
                    projectileSpawnCount: 0))
            {
                return EffectExecutionOutcome.Continue();
            }

            int span = ResolveMythicTemporalSpan(
                original.BaseSpeedMilliPerTick,
                node.DurationTicks,
                node.RadiusMilli);
            long pastProgress = Math.Max(
                0,
                original.PathProgressMilli - span);
            long futureProgress = Math.Min(
                path.TotalLengthMilli,
                original.PathProgressMilli + span);
            SimPosition pastPosition =
                path.GetPosition(pastProgress) +
                original.PathLateralOffset;
            if (mythicEnemyHistories.TryGetValue(
                    original.Id.Value,
                    out MythicMotionHistory history) &&
                history.TryGetTicksAgo(
                    Math.Max(0, node.DurationTicks),
                    out SimPosition recordedPast))
            {
                pastPosition = recordedPast;
            }

            EnemyState pastEcho = CreateMythicEnemyProxy(
                original,
                pastProgress,
                pastPosition,
                CardEffectVisualFlags.TimeRift,
                "time_rift_past");
            EnemyState futureEcho = CreateMythicEnemyProxy(
                original,
                futureProgress,
                path.GetPosition(futureProgress) +
                    original.PathLateralOffset,
                CardEffectVisualFlags.TimeRift,
                "time_rift_future");
            int duration = Math.Max(1, node.DurationTicks);
            int relayBps = Math.Max(1, Math.Min(10000, node.Amount));
            RegisterMythicTimeEcho(
                pastEcho,
                original,
                MythicEnemyProxyKind.TimePast,
                context,
                relayBps,
                duration);
            RegisterMythicTimeEcho(
                futureEcho,
                original,
                MythicEnemyProxyKind.TimeFuture,
                context,
                relayBps,
                duration);
            FinalizeMythicEnemyProxyBatch(original, 2);
            return EffectExecutionOutcome.BranchThree(
                pastEcho.Id,
                futureEcho.Id,
                context.ContinuationCardCount);
        }

        private EffectExecutionOutcome
            CreateMythicProjectileMirror(
                in EffectExecutionContext context,
                in CompiledEffectNode node)
        {
            ProjectileState original =
                FindProjectile(context.SubjectId);
            TowerState tower = FindTower(context.TowerId);
            if (original == null ||
                !original.Alive ||
                tower == null ||
                !TryReserveMythicBranches(
                    context,
                    SubjectType.Projectile,
                    childCount: 1,
                    projectileSpawnCount: 1))
            {
                return EffectExecutionOutcome.Continue();
            }

            SimPosition mirrorPosition = ReflectAroundPoint(
                original.Position,
                tower.Position,
                node.RadiusMilli);
            ProjectileState mirror =
                CreateMythicProjectileCopy(
                    original,
                    mirrorPosition,
                    Math.Max(1, node.Amount),
                    10000,
                    10000,
                    Math.Max(1, original.LifetimeRemaining),
                    CardEffectVisualFlags.MirrorWorld,
                    "mirror_world");
            mirror.DirectionXBps = -original.DirectionXBps;
            mirror.DirectionYBps = -original.DirectionYBps;
            EnemyState target = SelectProjectileTarget(mirror);
            if (target != null)
            {
                mirror.TargetId = target.Id;
                SetProjectileDirection(mirror, target.Position);
            }
            return EffectExecutionOutcome.Split(
                mirror.Id,
                context.ContinuationCardCount,
                context.ContinuationCardCount);
        }

        private EffectExecutionOutcome CreateMythicEnemyMirror(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            EnemyState original = FindEnemy(context.SubjectId);
            TowerState tower = FindTower(context.TowerId);
            if (original == null ||
                !original.Alive ||
                IsMythicEnemyProxy(original.Id) ||
                tower == null ||
                mythicMirrorMembers.ContainsKey(
                    original.Id.Value) ||
                !CanCreateMythicEnemyProxies(
                    original,
                    1,
                    context) ||
                !TryReserveMythicBranches(
                    context,
                    SubjectType.Enemy,
                    childCount: 1,
                    projectileSpawnCount: 0))
            {
                return EffectExecutionOutcome.Continue();
            }

            SimPosition mirrorPosition = ReflectAroundPoint(
                original.Position,
                tower.Position,
                node.RadiusMilli);
            EnemyState illusion = CreateMythicEnemyProxy(
                original,
                original.PathProgressMilli,
                mirrorPosition,
                CardEffectVisualFlags.MirrorWorld,
                "mirror_world");
            var link = new MythicMirrorLinkRuntime
            {
                PrimaryId = original.Id,
                IllusionId = illusion.Id,
                TowerId = context.TowerId,
                CardId = context.CardId,
                CardInstanceId = context.CardInstanceId,
                SharedHealthBps = Math.Max(
                    1,
                    Math.Min(10000, node.Amount)),
                RadiusMilli = Math.Max(0, node.RadiusMilli)
            };
            mythicMirrorMembers.Add(
                original.Id.Value,
                link);
            mythicMirrorMembers.Add(
                illusion.Id.Value,
                link);
            FinalizeMythicEnemyProxyBatch(original, 1);
            return EffectExecutionOutcome.Split(
                illusion.Id,
                context.ContinuationCardCount,
                context.ContinuationCardCount);
        }

        private void ConfigureMythicOuroboros(
            in EffectExecutionContext context,
            in CompiledEffectNode node)
        {
            if (context.HasExecutionFlag(
                    EffectExecutionFlags.SuppressOuroboros) ||
                !SubjectExists(
                    context.SubjectType,
                    context.SubjectId))
            {
                return;
            }

            var runtime = new MythicOuroborosRuntime
            {
                SubjectType = context.SubjectType,
                SubjectId = context.SubjectId,
                TowerId = context.TowerId,
                CardId = context.CardId,
                CardInstanceId = context.CardInstanceId,
                RootChainId = context.RootChainId,
                Node = node
            };
            Dictionary<int, MythicOuroborosRuntime> runtimes =
                context.SubjectType == SubjectType.Projectile
                    ? mythicProjectileOuroboros
                    : mythicEnemyOuroboros;
            runtimes[context.SubjectId.Value] = runtime;
            mythicOuroborosVisits.Add(
                new MythicOuroborosVisitKey(
                    context.RootChainId,
                    context.SubjectType,
                    context.SubjectId));

            if (context.SubjectType == SubjectType.Projectile)
            {
                ProjectileState projectile =
                    FindProjectile(context.SubjectId);
                if (projectile != null)
                {
                    projectile.VisualFlags |=
                        CardEffectVisualFlags.Ouroboros;
                }
            }
            else
            {
                EnemyState enemy = FindEnemy(context.SubjectId);
                if (enemy != null)
                {
                    enemy.VisualFlags |=
                        CardEffectVisualFlags.Ouroboros;
                }
            }
        }

        /// <summary>
        /// 카드 프로그램의 마지막 카드가 끝날 때 호출되는 우로보로스 완료 훅이다.
        /// 반복 토큰과 첫 카드 이벤트는 EnqueueProgramPass가 하나의 ChainReservation으로
        /// 확보하므로 예약 실패 시 방문 집합이나 반복 횟수도 부분 소비되지 않는다.
        /// </summary>
        internal void HandleMythicProgramCompleted(
            SubjectType subjectType,
            EntityId subjectId,
            TowerId towerId,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            in ProgramExecutionSpec execution)
        {
            Dictionary<int, MythicOuroborosRuntime> runtimes =
                subjectType == SubjectType.Projectile
                    ? mythicProjectileOuroboros
                    : mythicEnemyOuroboros;
            if (execution.HasFlag(
                    EffectExecutionFlags.SuppressOuroboros) ||
                !runtimes.TryGetValue(
                    subjectId.Value,
                    out MythicOuroborosRuntime runtime) ||
                runtime.RootChainId != rootChainId ||
                runtime.TowerId != towerId)
            {
                return;
            }

            int configuredLimit = Math.Max(
                0,
                runtime.Node.Limit);
            int maximumRepeats = Math.Min(
                configuredLimit,
                content.Safety.MaxMythicRepeatsPerChain);
            if (execution.RepeatIndex >= maximumRepeats)
            {
                runtimes.Remove(subjectId.Value);
                return;
            }

            EntityId nextSubject = subjectId;
            if (subjectType == SubjectType.Enemy)
            {
                EnemyState current = FindEnemy(subjectId);
                EnemyState next = current == null
                    ? null
                    : SelectMythicOuroborosEnemy(
                        current.Position,
                        runtime.Node.RadiusMilli,
                        rootChainId,
                        subjectId);
                if (next == null)
                {
                    runtimes.Remove(subjectId.Value);
                    return;
                }
                nextSubject = next.Id;
            }
            else if (!SubjectExists(subjectType, subjectId))
            {
                runtimes.Remove(subjectId.Value);
                return;
            }

            TowerState tower = FindTower(towerId);
            int nextPowerBps = (int)
                DeterministicMath.MultiplyBasisPoints(
                    execution.PowerBps,
                    Math.Max(
                        1,
                        Math.Min(10000, runtime.Node.Amount)));
            var repeated = new ProgramExecutionSpec(
                execution.Direction,
                Math.Max(1, nextPowerBps),
                execution.RepeatIndex + 1,
                execution.Flags |
                    EffectExecutionFlags.Repeated);
            int entryIndex = FindProgramEntryIndex(
                tower,
                subjectType,
                in repeated);
            if (entryIndex < 0)
            {
                runtimes.Remove(subjectId.Value);
                return;
            }

            ActivationId repeatedActivation =
                CreateActivation();
            if (!EnqueueProgramPass(
                    subjectType,
                    nextSubject,
                    towerId,
                    entryIndex,
                    rootChainId,
                    repeatedActivation,
                    parentEventId,
                    depth + 1,
                    EventPhase.Projectile,
                    in repeated,
                    mythicRepeatCount: 1))
            {
                runtimes.Remove(subjectId.Value);
                return;
            }

            // 방문 확정은 전체 예약과 큐 등록이 모두 성공한 뒤에만 반영한다.
            mythicOuroborosVisits.Add(
                new MythicOuroborosVisitKey(
                    rootChainId,
                    subjectType,
                    nextSubject));
            runtimes.Remove(subjectId.Value);
            AddMythicPresentation(
                subjectType == SubjectType.Projectile
                    ? "ouroboros_projectile_repeat"
                    : "ouroboros_enemy_transfer",
                nextSubject,
                subjectId,
                nextPowerBps);
        }

        /// <summary>
        /// Step의 상태 처리 앞에서 호출할 전용 tick 훅이다. 이전 틱 위치를 기록하고
        /// 프록시 수명, 불사조 취약, 특이점의 결정적 주기 작업을 진행한다.
        /// </summary>
        internal void ProcessMythicCardRuntimeTick()
        {
            RecordMythicEnemyHistory();
            ProcessMythicPhoenixVulnerabilities();
            ProcessMythicTimeEchoLifetimes();
            ProcessMythicProjectileSingularities();
            ProcessMythicEnemySingularities();
        }

        /// <summary>
        /// MoveEnemies의 일반 이동보다 먼저 호출한다. 시간 잔상은 정지시키고,
        /// 거울 환영은 원본의 타워 반대편 위치를 따라가게 한다.
        /// </summary>
        /// <returns>true이면 이 모듈이 이동을 완전히 처리했으므로 일반 이동을 건너뛴다.</returns>
        internal bool ProcessMythicEnemyMovement(
            EnemyState enemy)
        {
            if (enemy == null || !enemy.Alive)
            {
                return false;
            }

            if (mythicTimeEchoes.ContainsKey(enemy.Id.Value))
            {
                return true;
            }

            if (!mythicMirrorMembers.TryGetValue(
                    enemy.Id.Value,
                    out MythicMirrorLinkRuntime link) ||
                enemy.Id != link.IllusionId)
            {
                return false;
            }

            EnemyState primary = FindEnemy(link.PrimaryId);
            TowerState tower = FindTower(link.TowerId);
            if (primary == null ||
                !primary.Alive ||
                tower == null)
            {
                ResolveMythicMirrorIllusion(link);
                return true;
            }

            SimPosition previous = enemy.Position;
            enemy.PathProgressMilli =
                primary.PathProgressMilli;
            enemy.PathLateralOffset =
                primary.PathLateralOffset;
            enemy.Position = ReflectAroundPoint(
                primary.Position,
                tower.Position,
                link.RadiusMilli);
            enemy.HealthMilli = primary.HealthMilli;
            enemy.MaxHealthMilli = primary.MaxHealthMilli;
            enemy.ShieldMilli = primary.ShieldMilli;
            if (enemy.Position != previous)
            {
                AddPresentation(
                    PresentationEventType.EnemyMoved,
                    enemy.Id.Value,
                    primary.Id.Value,
                    0,
                    "mirror_world");
            }
            return true;
        }

        /// <summary>
        /// MoveProjectiles에서 개별 탄환의 실제 이동 전에 호출한다. 시간 균열이
        /// 참조할 고정 용량 위치 기록만 갱신하며 일반 이동을 소유하지 않는다.
        /// </summary>
        internal bool ProcessMythicProjectileMovement(
            ProjectileState projectile)
        {
            if (projectile == null || !projectile.Alive)
            {
                return false;
            }

            GetOrCreateMythicProjectileHistory(
                projectile.Id).Record(
                    tick,
                    projectile.Position);
            return false;
        }

        /// <summary>
        /// ProjectileHit 확정 뒤 호출할 대칭 훅이다. 현재 신화 동작은 피해·소멸
        /// 단계에서 처리하지만 호출 위치를 고정해 후속 카드가 공용 파일을 바꾸지 않고
        /// 적중 정책을 확장할 수 있게 한다.
        /// </summary>
        internal bool TryHandleMythicProjectileHit(
            ProjectileState projectile,
            EnemyState target,
            in GameEvent gameEvent)
        {
            return false;
        }

        /// <summary>
        /// Rare 환원 뒤, Rare 환생보다 먼저 호출할 신화 탄환 대체 수명주기 훅이다.
        /// Phoenix가 성공하면 원본을 종료하고 강화 탄환 하나로 교체하되, 특이점처럼
        /// 최종 소멸에서만 실행할 상태는 새 탄환으로 원자적으로 이전한다.
        /// </summary>
        /// <returns>true이면 새 탄환이 원본을 대체했으므로 이번 소멸의 뒤 규칙을 건너뛴다.</returns>
        internal bool TryHandleMythicProjectilePhoenix(
            ProjectileState projectile,
            in GameEvent expirationEvent)
        {
            if (projectile == null || !projectile.Alive)
            {
                return false;
            }

            if (!mythicProjectilePhoenixCores.TryGetValue(
                    projectile.Id.Value,
                    out MythicProjectilePhoenixRuntime phoenix) ||
                phoenix.Consumed ||
                !CanCreateProjectileEntity(
                    checked(projectile.Generation + 1)))
            {
                return false;
            }

            GameEvent diagnosticEvent = WithDiagnosticDepth(
                CreateDiagnosticEvent(
                    EventType.ProjectileSpawned,
                    expirationEvent.RootChainId,
                    phoenix.TowerId,
                    phoenix.CardId,
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

            // 생성 예산이 원자적으로 확보된 뒤에만 일회성 권한을 소비한다.
            phoenix.Consumed = true;
            int damageBps = Math.Max(1, phoenix.Node.Amount);
            int speedBps = Math.Max(1, phoenix.Node.Amount2);
            int radiusBps = Math.Max(1, phoenix.Node.Amount3);
            ProjectileState reborn = CreateMythicProjectileCopy(
                projectile,
                projectile.Position,
                damageBps,
                speedBps,
                radiusBps,
                Math.Max(1, phoenix.Node.DurationTicks),
                CardEffectVisualFlags.PhoenixCore,
                "phoenix_core");
            TransferMythicProjectileLifecycleState(
                projectile,
                reborn);
            TransferLegendaryProjectileLifecycleState(
                projectile,
                reborn);
            EnemyState target = SelectProjectileTarget(reborn);
            if (target != null)
            {
                reborn.TargetId = target.Id;
                reborn.Homing = true;
                SetProjectileDirection(reborn, target.Position);
            }

            projectile.Alive = false;
            projectile.ExpirationQueued = true;
            mythicProjectilePhoenixCores.Remove(
                projectile.Id.Value);
            AddPresentation(
                PresentationEventType.ProjectileExpired,
                projectile.Id.Value,
                projectile.SourceTowerId.Value,
                0,
                "phoenix_core");
            AddMythicPresentation(
                "phoenix_projectile_reborn",
                reborn.Id,
                projectile.Id,
                (int)Math.Min(int.MaxValue, reborn.DamageMilli));
            return true;
        }

        /// <summary>
        /// 환원·Phoenix·환생이 모두 소멸을 취소하지 않은 최종 지점에서 호출한다.
        /// 특이점에 흡수된 payload는 이 경계에서만 한 번 방출되므로 대체 탄환과
        /// Legendary 마지막 명령이 같은 최초 소멸에서 함께 발동하지 않는다.
        /// </summary>
        internal void HandleMythicProjectileFinalExpired(
            ProjectileState projectile,
            in GameEvent expirationEvent)
        {
            if (projectile == null || !projectile.Alive)
            {
                return;
            }

            ReleaseMythicProjectileSingularity(
                projectile,
                expirationEvent);
        }

        /// <summary>
        /// Rare 환생처럼 이 모듈 밖에서 새 ProjectileState를 만드는 대체 수명주기가
        /// 호출한다. 최종 소멸까지 보류해야 하는 신화 상태를 새 ID의 독립 객체로
        /// 옮기고 원본 원장에서는 즉시 제거해 이중 방출을 막는다.
        /// </summary>
        internal void TransferMythicProjectileLifecycleState(
            ProjectileState source,
            ProjectileState target)
        {
            if (source == null ||
                target == null ||
                source.Id == target.Id)
            {
                return;
            }

            if (mythicProjectileSingularities.TryGetValue(
                    source.Id.Value,
                    out MythicProjectileSingularityRuntime singularity))
            {
                MythicProjectileSingularityRuntime transferred =
                    CloneMythicProjectileSingularity(
                        singularity);
                mythicProjectileSingularities.Remove(
                    source.Id.Value);
                mythicProjectileSingularities[
                    target.Id.Value] = transferred;
                target.VisualFlags |=
                    CardEffectVisualFlags.Singularity;
            }
        }

        private static MythicProjectileSingularityRuntime
            CloneMythicProjectileSingularity(
                MythicProjectileSingularityRuntime source)
        {
            var clone =
                new MythicProjectileSingularityRuntime
                {
                    TowerId = source.TowerId,
                    CardId = source.CardId,
                    CardInstanceId = source.CardInstanceId,
                    SourceEntityId = source.SourceEntityId,
                    RootChainId = source.RootChainId,
                    ActivationId = source.ActivationId,
                    ParentEventId = source.ParentEventId,
                    Depth = source.Depth,
                    Node = source.Node,
                    RemainingTicks = source.RemainingTicks,
                    NextTick = source.NextTick,
                    Released = source.Released
                };
            for (int captureIndex = 0;
                 captureIndex < source.Captures.Count;
                 captureIndex++)
            {
                MythicCapturedProjectile captured =
                    source.Captures[captureIndex];
                var capturedClone =
                    new MythicCapturedProjectile
                    {
                        SourceId = captured.SourceId,
                        SourceTowerId =
                            captured.SourceTowerId,
                        DamageMilli = captured.DamageMilli,
                        VisualFlags = captured.VisualFlags
                    };
                for (int bindingIndex = 0;
                     bindingIndex < captured.Bindings.Count;
                     bindingIndex++)
                {
                    capturedClone.Bindings.Add(
                        captured.Bindings[bindingIndex]
                            .Clone());
                }
                clone.Captures.Add(capturedClone);
            }
            return clone;
        }

        /// <summary>
        /// ProcessDamageEvent가 실제 체력을 차감한 직후, 0 체력 사망 이벤트를 만들기
        /// 전에 호출한다. 시간 잔상 전달, 거울 공유 체력, 특이점 피해 공유를 이 순서로
        /// 처리하며, 전파 피해는 이미 계산된 최종 피해라 방어력을 두 번 적용하지 않는다.
        /// </summary>
        internal void HandleMythicEnemyDamageApplied(
            EnemyState damaged,
            in GameEvent damageEvent,
            long appliedAmount)
        {
            if (damaged == null || !damaged.Alive)
            {
                return;
            }

            bool relayEvent =
                mythicRelayEventIds.Remove(
                    damageEvent.EventId.Value);
            EnemyState logicalTarget = damaged;
            if (mythicMirrorMembers.TryGetValue(
                    damaged.Id.Value,
                    out MythicMirrorLinkRuntime mirror))
            {
                logicalTarget = SynchronizeMythicMirrorHealth(
                    damaged,
                    mirror,
                    damageEvent);
            }

            if (!relayEvent &&
                mythicTimeEchoes.TryGetValue(
                    damaged.Id.Value,
                    out MythicTimeEchoRuntime echo))
            {
                EnemyState source = FindEnemy(echo.SourceId);
                // 잔상 자체는 피해를 전달하는 표적일 뿐 죽거나 보상을 만들지 않는다.
                damaged.HealthMilli =
                    Math.Max(1, damaged.MaxHealthMilli);
                damaged.ShieldMilli = 0;
                if (source == null || !source.Alive)
                {
                    ResolveMythicTimeEcho(echo);
                    return;
                }

                long relayAmount =
                    DeterministicMath.MultiplyBasisPoints(
                        appliedAmount,
                        echo.DamageRelayBps);
                if (relayAmount > 0)
                {
                    QueueMythicFinalDamage(
                        source,
                        relayAmount,
                        damageEvent,
                        echo.TowerId,
                        echo.CardId,
                        damaged.Id,
                        EventTags.Generated |
                        EventTags.Repeated |
                        EventTags.SingleTarget);
                }
            }

            if (!relayEvent &&
                logicalTarget != null &&
                logicalTarget.Alive &&
                appliedAmount > 0 &&
                mythicEnemySingularities.TryGetValue(
                    logicalTarget.Id.Value,
                    out MythicEnemySingularityRuntime singularity) &&
                singularity.RemainingTicks > 0)
            {
                ShareMythicSingularityDamage(
                    logicalTarget,
                    singularity,
                    appliedAmount,
                    damageEvent);
            }
        }

        /// <summary>
        /// 대상이 이미 사라져 공용 피해 파이프라인이 조기 종료한 relay 이벤트의
        /// 재전파 방지 표식을 정리한다. 정상 적용 이벤트는 위 damage hook이 소비한다.
        /// </summary>
        internal void DiscardMythicRelayEvent(
            EventId eventId)
        {
            if (eventId.IsValid)
            {
                mythicRelayEventIds.Remove(
                    eventId.Value);
            }
        }

        /// <summary>
        /// Rare 환생 판정 뒤 호출한다. 프록시 사망은 경제·웨이브 트리거 없이
        /// 정리하고, Phoenix는 동일 EnemyState를 되살린다. 최종 사망 부가 효과는
        /// 실행하지 않아 Rare/Legendary의 명시적인 최종 순서를 침범하지 않는다.
        /// </summary>
        internal bool TryHandleMythicEnemyLifecycle(
            EnemyState enemy,
            in GameEvent deathEvent)
        {
            if (enemy == null || !enemy.Alive)
            {
                return false;
            }

            if (mythicTimeEchoes.TryGetValue(
                    enemy.Id.Value,
                    out MythicTimeEchoRuntime echo))
            {
                ResolveMythicTimeEcho(echo);
                return true;
            }
            if (mythicMirrorMembers.TryGetValue(
                    enemy.Id.Value,
                    out MythicMirrorLinkRuntime proxyLink) &&
                enemy.Id == proxyLink.IllusionId)
            {
                ResolveMythicMirrorIllusion(proxyLink);
                return true;
            }

            if (mythicEnemyPhoenixCores.TryGetValue(
                    enemy.Id.Value,
                    out MythicEnemyPhoenixRuntime phoenix) &&
                !phoenix.Consumed)
            {
                ReviveMythicPhoenixEnemy(
                    enemy,
                    phoenix);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 모든 부활과 Rare/Legendary 최종 효과가 끝난 뒤 호출한다. 원본 소유
        /// 프록시를 먼저 정리하고 특이점의 사망 payload를 단 한 번 방출한다.
        /// </summary>
        internal void HandleMythicEnemyFinalDeath(
            EnemyState enemy,
            in GameEvent deathEvent)
        {
            if (enemy == null || !enemy.Alive)
            {
                return;
            }

            ResolveMythicOwnedProxies(enemy.Id);
            TriggerMythicSingularityDeath(
                enemy,
                deathEvent);
        }

        /// <summary>
        /// 기존 통합 호출부와 단위 테스트를 위한 호환 facade다. 새 중앙 수명주기는
        /// 두 분리 훅을 정해진 우선순위에 배치해야 한다.
        /// </summary>
        internal bool HandleMythicEnemyDeath(
            EnemyState enemy,
            in GameEvent deathEvent)
        {
            if (TryHandleMythicEnemyLifecycle(
                    enemy,
                    deathEvent))
            {
                return true;
            }

            HandleMythicEnemyFinalDeath(
                enemy,
                deathEvent);
            return false;
        }

        /// <summary>
        /// CleanupDeadEntities의 실제 List 제거 전에 호출한다. 소스가 유출되거나
        /// 다른 카드가 직접 제거한 경우에도 프록시 원장과 history를 함께 정리한다.
        /// </summary>
        internal void CleanupMythicCardState()
        {
            CleanupMythicOrphanProxies();
            CleanupMythicProjectileRuntime();
            CleanupMythicEnemyRuntime();
            CleanupMythicOuroborosRuntime();
        }

        private void RecordMythicEnemyHistory()
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState enemy = enemies[i];
                if (enemy == null ||
                    !enemy.Alive ||
                    IsMythicEnemyProxy(enemy.Id))
                {
                    continue;
                }

                GetOrCreateMythicEnemyHistory(
                    enemy.Id).Record(
                        tick,
                        enemy.Position);
            }
        }

        private void ProcessMythicPhoenixVulnerabilities()
        {
            CopySortedMythicKeys(
                mythicEnemyPhoenixCores);
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                int key = mythicKeyScratch[i];
                if (!mythicEnemyPhoenixCores.TryGetValue(
                        key,
                        out MythicEnemyPhoenixRuntime runtime) ||
                    runtime.VulnerabilityTicks <= 0)
                {
                    continue;
                }

                EnemyState enemy =
                    FindEnemy(new EntityId(key));
                if (enemy == null || !enemy.Alive)
                {
                    continue;
                }

                runtime.VulnerabilityTicks--;
                if (runtime.VulnerabilityTicks > 0 ||
                    runtime.VulnerabilityBps <= 0)
                {
                    continue;
                }

                enemy.AreaDamageTakenBps = Math.Max(
                    10000,
                    enemy.AreaDamageTakenBps -
                    runtime.VulnerabilityBps);
                enemy.SingleDamageTakenBps = Math.Max(
                    10000,
                    enemy.SingleDamageTakenBps -
                    runtime.VulnerabilityBps);
                runtime.VulnerabilityBps = 0;
                AddMythicPresentation(
                    "phoenix_vulnerability_expired",
                    enemy.Id,
                    runtime.SourceEntityId,
                    0);
            }
        }

        private void ProcessMythicTimeEchoLifetimes()
        {
            CopySortedMythicKeys(mythicTimeEchoes);
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                int key = mythicKeyScratch[i];
                if (!mythicTimeEchoes.TryGetValue(
                        key,
                        out MythicTimeEchoRuntime echo))
                {
                    continue;
                }

                EnemyState source = FindEnemy(echo.SourceId);
                EnemyState proxy = FindEnemy(echo.EchoId);
                if (source == null ||
                    !source.Alive ||
                    proxy == null ||
                    !proxy.Alive)
                {
                    ResolveMythicTimeEcho(echo);
                    continue;
                }

                echo.RemainingTicks--;
                if (echo.RemainingTicks <= 0)
                {
                    ResolveMythicTimeEcho(echo);
                }
            }
        }

        private void ProcessMythicProjectileSingularities()
        {
            CopySortedMythicKeys(
                mythicProjectileSingularities);
            for (int keyIndex = 0;
                 keyIndex < mythicKeyScratch.Count;
                 keyIndex++)
            {
                int key = mythicKeyScratch[keyIndex];
                if (!mythicProjectileSingularities.TryGetValue(
                        key,
                        out MythicProjectileSingularityRuntime runtime))
                {
                    continue;
                }

                ProjectileState host =
                    FindProjectile(new EntityId(key));
                if (host == null || !host.Alive)
                {
                    continue;
                }

                if (runtime.RemainingTicks > 0)
                {
                    runtime.RemainingTicks--;
                }
                if (runtime.RemainingTicks <= 0 ||
                    tick < runtime.NextTick ||
                    runtime.Captures.Count >=
                    Math.Max(1, runtime.Node.Limit))
                {
                    continue;
                }

                runtime.NextTick = tick +
                    Math.Max(1, runtime.Node.IntervalTicks);
                CollectMythicSingularityProjectiles(
                    host,
                    runtime);
                for (int candidateIndex = 0;
                     candidateIndex <
                     mythicProjectileScratch.Count;
                     candidateIndex++)
                {
                    if (runtime.Captures.Count >=
                        Math.Max(1, runtime.Node.Limit))
                    {
                        break;
                    }

                    ProjectileState candidate =
                        mythicProjectileScratch[candidateIndex];
                    if (candidate == null ||
                        !candidate.Alive ||
                        candidate.ExpirationQueued)
                    {
                        continue;
                    }

                    int pullDistance = (int)Math.Min(
                        int.MaxValue,
                        DeterministicMath.MultiplyBasisPoints(
                            PathModel.DistanceMilli(
                                candidate.Position,
                                host.Position),
                            Math.Max(
                                1,
                                Math.Min(
                                    10000,
                                    runtime.Node.Amount))));
                    candidate.Position =
                        PathModel.MoveTowards(
                            candidate.Position,
                            host.Position,
                            Math.Max(1, pullDistance));
                    int captureRadius = Math.Max(
                        100,
                        host.RadiusMilli +
                        candidate.RadiusMilli);
                    if (PathModel.IsWithin(
                            candidate.Position,
                            host.Position,
                            captureRadius))
                    {
                        CaptureMythicProjectile(
                            host,
                            candidate,
                            runtime);
                    }
                }
            }
        }

        private void CollectMythicSingularityProjectiles(
            ProjectileState host,
            MythicProjectileSingularityRuntime runtime)
        {
            mythicProjectileScratch.Clear();
            int radius = Math.Max(
                0,
                runtime.Node.RadiusMilli);
            for (int i = 0; i < projectiles.Count; i++)
            {
                ProjectileState candidate = projectiles[i];
                if (candidate == null ||
                    !candidate.Alive ||
                    candidate.ExpirationQueued ||
                    candidate.Id == host.Id ||
                    mythicProjectileSingularities.ContainsKey(
                        candidate.Id.Value) ||
                    !PathModel.IsWithin(
                        host.Position,
                        candidate.Position,
                        radius))
                {
                    continue;
                }
                mythicProjectileScratch.Add(candidate);
            }
            SortMythicProjectilesByDistance(
                mythicProjectileScratch,
                host.Position);
        }

        private void CaptureMythicProjectile(
            ProjectileState host,
            ProjectileState candidate,
            MythicProjectileSingularityRuntime runtime)
        {
            var capture = new MythicCapturedProjectile
            {
                SourceId = candidate.Id,
                SourceTowerId = candidate.SourceTowerId,
                DamageMilli = candidate.DamageMilli,
                VisualFlags = candidate.VisualFlags
            };
            for (int i = 0; i < candidate.Bindings.Count; i++)
            {
                EffectBinding binding =
                    candidate.Bindings[i].Clone();
                binding.ActiveTrailHazardId = -1;
                binding.TrailStarted = false;
                capture.Bindings.Add(binding);
            }
            runtime.Captures.Add(capture);

            long inheritedDamage =
                DeterministicMath.MultiplyBasisPoints(
                    candidate.DamageMilli,
                    Math.Max(1, runtime.Node.Amount2));
            host.DamageMilli = SaturatingAddPositive(
                host.DamageMilli,
                inheritedDamage);
            host.VisualFlags |= candidate.VisualFlags |
                CardEffectVisualFlags.Singularity;

            // Absorbed는 정상 수명 종료가 아니므로 expire/rebirth를 예약하지 않는다.
            candidate.Alive = false;
            candidate.ExpirationQueued = true;
            AddMythicPresentation(
                "singularity_absorb",
                host.Id,
                candidate.Id,
                runtime.Captures.Count);
        }

        private void ProcessMythicEnemySingularities()
        {
            CopySortedMythicKeys(
                mythicEnemySingularities);
            for (int keyIndex = 0;
                 keyIndex < mythicKeyScratch.Count;
                 keyIndex++)
            {
                int key = mythicKeyScratch[keyIndex];
                if (!mythicEnemySingularities.TryGetValue(
                        key,
                        out MythicEnemySingularityRuntime runtime))
                {
                    continue;
                }

                EnemyState host =
                    FindEnemy(new EntityId(key));
                if (host == null ||
                    !host.Alive ||
                    host.DeathQueued)
                {
                    continue;
                }

                runtime.RemainingTicks--;
                if (runtime.RemainingTicks <= 0)
                {
                    mythicEnemySingularities.Remove(key);
                    continue;
                }
                if (tick < runtime.NextTick)
                {
                    continue;
                }

                runtime.NextTick = tick +
                    Math.Max(1, runtime.Node.IntervalTicks);
                CollectMythicNearbyEnemies(
                    host.Position,
                    runtime.Node.RadiusMilli,
                    host.Id,
                    Math.Max(1, runtime.Node.Limit),
                    includeProxies: false);
                for (int candidateIndex = 0;
                     candidateIndex < mythicEnemyScratch.Count;
                     candidateIndex++)
                {
                    EnemyState candidate =
                        mythicEnemyScratch[candidateIndex];
                    long previous =
                        candidate.PathProgressMilli;
                    long delta =
                        host.PathProgressMilli -
                        candidate.PathProgressMilli;
                    long pulled =
                        DeterministicMath.MultiplyBasisPoints(
                            delta,
                            Math.Max(
                                1,
                                Math.Min(
                                    10000,
                                    runtime.Node.Amount)));
                    candidate.PathProgressMilli = Math.Max(
                        0,
                        Math.Min(
                            path.TotalLengthMilli,
                            candidate.PathProgressMilli +
                            pulled));
                    RefreshEnemyPosition(candidate);
                    long moved = Math.Abs(
                        candidate.PathProgressMilli - previous);
                    if (moved > 0)
                    {
                        TriggerBleedFromMovement(
                            candidate,
                            moved);
                        AddPresentation(
                            PresentationEventType.EnemyMoved,
                            candidate.Id.Value,
                            host.Id.Value,
                            (int)Math.Min(int.MaxValue, moved),
                            "singularity");
                    }
                }
            }
        }

        private void ReleaseMythicProjectileSingularity(
            ProjectileState host,
            in GameEvent expirationEvent)
        {
            if (!mythicProjectileSingularities.TryGetValue(
                    host.Id.Value,
                    out MythicProjectileSingularityRuntime runtime) ||
                runtime.Released)
            {
                return;
            }

            runtime.Released = true;
            if (runtime.Captures.Count == 0)
            {
                mythicProjectileSingularities.Remove(
                    host.Id.Value);
                return;
            }

            long totalDamage = 0;
            for (int i = 0; i < runtime.Captures.Count; i++)
            {
                long scaled =
                    DeterministicMath.MultiplyBasisPoints(
                        runtime.Captures[i].DamageMilli,
                        Math.Max(1, runtime.Node.Amount2));
                totalDamage =
                    SaturatingAddPositive(
                        totalDamage,
                        scaled);
            }

            CollectMythicNearbyEnemies(
                host.Position,
                runtime.Node.RadiusMilli,
                EntityId.Invalid,
                MythicMaximumReleaseTargets,
                includeProxies: true);
            mythicEventScratch.Clear();
            for (int i = 0;
                 i < mythicEnemyScratch.Count;
                 i++)
            {
                if (TryCreateDamageEvent(
                        mythicEnemyScratch[i].Id,
                        runtime.TowerId,
                        runtime.CardId,
                        host.Id,
                        totalDamage,
                        DamageKind.Explosion,
                        0,
                        expirationEvent.RootChainId,
                        expirationEvent.ActivationId,
                        expirationEvent.EventId,
                        expirationEvent.Depth + 1,
                        EventTags.Generated |
                        EventTags.Area,
                        out GameEvent damage))
                {
                    mythicEventScratch.Add(damage);
                }
            }

            bool damageBatchAccepted =
                TryEnqueueBatch(mythicEventScratch);
            if (damageBatchAccepted)
            {
                AddUncommonAreaPresentation(
                    "mythic_singularity",
                    host.Id,
                    host.Id,
                    runtime.Node.RadiusMilli,
                    host.Position);
                ReleaseMythicCapturedBindings(
                    host,
                    runtime,
                    expirationEvent);
            }
            AddMythicPresentation(
                "singularity_release",
                host.Id,
                runtime.SourceEntityId,
                runtime.Captures.Count);
            mythicProjectileSingularities.Remove(
                host.Id.Value);
        }

        private void ReleaseMythicCapturedBindings(
            ProjectileState host,
            MythicProjectileSingularityRuntime runtime,
            in GameEvent expirationEvent)
        {
            for (int captureIndex = 0;
                 captureIndex < runtime.Captures.Count;
                 captureIndex++)
            {
                MythicCapturedProjectile capture =
                    runtime.Captures[captureIndex];
                var carrier = new ProjectileState
                {
                    Id = capture.SourceId,
                    SourceTowerId = capture.SourceTowerId.IsValid
                        ? capture.SourceTowerId
                        : runtime.TowerId,
                    Position = host.Position,
                    DamageMilli = Math.Max(
                        1,
                        DeterministicMath.MultiplyBasisPoints(
                            capture.DamageMilli,
                            Math.Max(1, runtime.Node.Amount2))),
                    RootChainId = expirationEvent.RootChainId,
                    ActivationId = expirationEvent.ActivationId,
                    VisualFlags = capture.VisualFlags,
                    Alive = true
                };
                for (int targetIndex = 0;
                     targetIndex < mythicEnemyScratch.Count;
                     targetIndex++)
                {
                    EnemyState target =
                        mythicEnemyScratch[targetIndex];
                    if (target == null || !target.Alive)
                    {
                        continue;
                    }
                    for (int bindingIndex = 0;
                         bindingIndex < capture.Bindings.Count;
                         bindingIndex++)
                    {
                        EffectBinding binding =
                            capture.Bindings[bindingIndex];
                        if (binding.Kind ==
                            BindingKind.Explosion)
                        {
                            // 합산 범위 피해가 이미 원자 배치됐으므로 중첩 폭발은
                            // 다시 실행하지 않는다.
                            continue;
                        }
                        if (binding.Used &&
                            binding.Trigger !=
                            BindingTrigger.OnHit)
                        {
                            // 원본 탄환에서 이미 소비된 최초 1회 효과는 흡수로
                            // 되살리지 않는다. 매 적중 효과만 새 범위 대상에 전달한다.
                            continue;
                        }
                        ExecuteProjectileBinding(
                            carrier,
                            target,
                            binding,
                            expirationEvent);
                    }
                }
            }
        }

        private void ShareMythicSingularityDamage(
            EnemyState host,
            MythicEnemySingularityRuntime runtime,
            long appliedAmount,
            in GameEvent sourceEvent)
        {
            CollectMythicNearbyEnemies(
                host.Position,
                runtime.Node.RadiusMilli,
                host.Id,
                Math.Max(1, runtime.Node.Limit),
                includeProxies: false);
            if (mythicEnemyScratch.Count == 0)
            {
                return;
            }

            long totalShare =
                DeterministicMath.MultiplyBasisPoints(
                    appliedAmount,
                    Math.Max(
                        0,
                        Math.Min(10000, runtime.Node.Amount2)));
            long perTarget = totalShare /
                mythicEnemyScratch.Count;
            if (perTarget <= 0)
            {
                return;
            }

            mythicEventScratch.Clear();
            for (int i = 0;
                 i < mythicEnemyScratch.Count;
                 i++)
            {
                EnemyState target = mythicEnemyScratch[i];
                mythicEventScratch.Add(
                    CreateMythicFinalDamageEvent(
                        target,
                        perTarget,
                        sourceEvent,
                        runtime.TowerId,
                        runtime.CardId,
                        host.Id,
                        EventTags.Generated |
                        EventTags.Repeated |
                        EventTags.Area));
            }
            EnqueueMythicFinalDamageBatch(
                mythicEventScratch);
        }

        private void QueueMythicFinalDamage(
            EnemyState target,
            long finalDamage,
            in GameEvent sourceEvent,
            TowerId sourceTowerId,
            CardId sourceCardId,
            EntityId sourceEntityId,
            EventTags tags)
        {
            if (target == null ||
                !target.Alive ||
                finalDamage <= 0)
            {
                return;
            }

            mythicEventScratch.Clear();
            mythicEventScratch.Add(
                CreateMythicFinalDamageEvent(
                    target,
                    finalDamage,
                    sourceEvent,
                    sourceTowerId,
                    sourceCardId,
                    sourceEntityId,
                    tags));
            EnqueueMythicFinalDamageBatch(
                mythicEventScratch);
        }

        private GameEvent CreateMythicFinalDamageEvent(
            EnemyState target,
            long finalDamage,
            in GameEvent sourceEvent,
            TowerId sourceTowerId,
            CardId sourceCardId,
            EntityId sourceEntityId,
            EventTags tags)
        {
            return new GameEvent(
                tick,
                EventPhase.Damage,
                EventType.DamageRequested,
                sourceEvent.RootChainId,
                sourceEvent.EventId,
                sourceEvent.ActivationId,
                sourceTowerId,
                sourceCardId,
                sourceEntityId,
                target.Id,
                SubjectType.Enemy,
                sourceEvent.Depth + 1,
                Math.Max(0, target.Generation),
                tags,
                sourceEvent.RewardOrigin,
                payloadA: (int)DamageKind.Physical,
                payloadB: 10000,
                payloadValue: Math.Max(1, finalDamage));
        }

        private bool EnqueueMythicFinalDamageBatch(
            List<GameEvent> events)
        {
            if (events == null || events.Count == 0)
            {
                return true;
            }

            GameEvent diagnostic = events[0];
            int maximumDepth = diagnostic.Depth;
            for (int i = 1; i < events.Count; i++)
            {
                if (events[i].RootChainId !=
                    diagnostic.RootChainId)
                {
                    throw new InvalidOperationException(
                        "Mythic damage relays must share one root chain.");
                }
                maximumDepth = Math.Max(
                    maximumDepth,
                    events[i].Depth);
            }
            diagnostic = WithDiagnosticDepth(
                diagnostic,
                maximumDepth);
            if (!TryReserveComposite(
                    in diagnostic,
                    chainEventCount: events.Count,
                    queueSlotCount: events.Count,
                    projectileSpawnCount: 0,
                    cardTriggerCount: 0))
            {
                return false;
            }

            for (int i = 0; i < events.Count; i++)
            {
                GameEvent relay = events[i];
                if (!EnqueueReserved(
                        in relay,
                        out GameEvent scheduled))
                {
                    throw new InvalidOperationException(
                        "A Mythic damage relay lost an atomic queue slot.");
                }
                mythicRelayEventIds.Add(
                    scheduled.EventId.Value);
            }
            return true;
        }

        private EnemyState SynchronizeMythicMirrorHealth(
            EnemyState damaged,
            MythicMirrorLinkRuntime link,
            in GameEvent damageEvent)
        {
            EnemyState primary = FindEnemy(link.PrimaryId);
            EnemyState illusion = FindEnemy(link.IllusionId);
            if (primary == null ||
                illusion == null ||
                !primary.Alive ||
                !illusion.Alive)
            {
                return primary ?? damaged;
            }

            EnemyState other = damaged.Id == link.PrimaryId
                ? illusion
                : primary;
            other.HealthMilli = Math.Min(
                other.MaxHealthMilli,
                damaged.HealthMilli);
            other.ShieldMilli = damaged.ShieldMilli;
            if (damaged.HealthMilli == 0 &&
                primary.HealthMilli == 0 &&
                !primary.DeathQueued &&
                damaged.Id != primary.Id)
            {
                QueueMythicPrimaryDeath(
                    primary,
                    damageEvent);
            }
            return primary;
        }

        private void QueueMythicPrimaryDeath(
            EnemyState primary,
            in GameEvent damageEvent)
        {
            var death = new GameEvent(
                tick,
                EventPhase.Death,
                EventType.EnemyDied,
                damageEvent.RootChainId,
                damageEvent.EventId,
                damageEvent.ActivationId,
                damageEvent.SourceTowerId,
                damageEvent.SourceCardId,
                damageEvent.SourceEntityId,
                primary.Id,
                SubjectType.Enemy,
                damageEvent.Depth + 1,
                primary.Generation,
                EventTags.Death |
                EventTags.Generated,
                damageEvent.RewardOrigin);
            primary.DeathQueued = true;
            if (!TryEnqueue(in death, out _))
            {
                ProcessEnemyDeathEvent(in death);
            }
        }

        private void ReviveMythicPhoenixEnemy(
            EnemyState enemy,
            MythicEnemyPhoenixRuntime runtime)
        {
            runtime.Consumed = true;
            enemy.HealthMilli = Math.Max(
                1000,
                Math.Min(
                    enemy.MaxHealthMilli,
                    DeterministicMath.MultiplyBasisPoints(
                        enemy.MaxHealthMilli,
                        Math.Max(1, runtime.Node.Amount))));
            enemy.ShieldMilli = 0;
            enemy.DeathQueued = false;
            enemy.Alive = true;

            int convertedStacks =
                RemoveMythicPhoenixStatuses(enemy);
            int maximumStacks = runtime.Node.MaxStacks > 0
                ? runtime.Node.MaxStacks
                : 1;
            int vulnerabilityStacks = Math.Max(
                0,
                Math.Min(maximumStacks, convertedStacks));
            long requestedVulnerability =
                (long)Math.Max(
                    0,
                    runtime.Node.Amount2) *
                vulnerabilityStacks;
            int vulnerability = Math.Min(
                MythicMaximumVulnerabilityBps,
                (int)Math.Min(
                    int.MaxValue,
                    requestedVulnerability));
            int vulnerabilityHeadroom = Math.Min(
                Math.Max(
                    0,
                    30000 - enemy.AreaDamageTakenBps),
                Math.Max(
                    0,
                    30000 -
                    enemy.SingleDamageTakenBps));
            int appliedVulnerability = Math.Min(
                vulnerability,
                vulnerabilityHeadroom);
            runtime.VulnerabilityBps =
                appliedVulnerability;
            runtime.VulnerabilityTicks =
                appliedVulnerability > 0
                    ? Math.Max(
                        1,
                        runtime.Node.DurationTicks)
                    : 0;
            enemy.AreaDamageTakenBps +=
                appliedVulnerability;
            enemy.SingleDamageTakenBps +=
                appliedVulnerability;

            if (mythicMirrorMembers.TryGetValue(
                    enemy.Id.Value,
                    out MythicMirrorLinkRuntime mirror))
            {
                EnemyState illusion =
                    FindEnemy(mirror.IllusionId);
                if (illusion != null && illusion.Alive)
                {
                    illusion.HealthMilli =
                        enemy.HealthMilli;
                    illusion.MaxHealthMilli =
                        enemy.MaxHealthMilli;
                    illusion.ShieldMilli = 0;
                }
            }

            AddMythicPresentation(
                "phoenix_enemy_reborn",
                enemy.Id,
                runtime.SourceEntityId,
                appliedVulnerability);
        }

        private int RemoveMythicPhoenixStatuses(
            EnemyState enemy)
        {
            int convertedStacks = 0;
            for (int i = enemy.Statuses.Count - 1;
                 i >= 0;
                 i--)
            {
                StatusInstance status = enemy.Statuses[i];
                if (!status.Dispellable ||
                    !IsMythicPhoenixConvertible(
                        status.Type))
                {
                    continue;
                }
                convertedStacks = Math.Min(
                    1000,
                    convertedStacks +
                    Math.Max(1, status.Stacks));
                enemy.Statuses.RemoveAt(i);
                AddPresentation(
                    PresentationEventType.StatusRemoved,
                    enemy.Id.Value,
                    status.SourceEntityId.Value,
                    (int)status.Type,
                    "phoenix_core");
            }
            return convertedStacks;
        }

        private static bool IsMythicPhoenixConvertible(
            StatusType statusType)
        {
            switch (statusType)
            {
                case StatusType.Burn:
                case StatusType.Poison:
                case StatusType.Slow:
                case StatusType.Mark:
                case StatusType.Pierced:
                case StatusType.Stun:
                case StatusType.Bleed:
                case StatusType.Delay:
                case StatusType.Curse:
                case StatusType.Bind:
                case StatusType.Airborne:
                case StatusType.Shock:
                case StatusType.Chill:
                case StatusType.Frozen:
                case StatusType.Seal:
                case StatusType.Corrosion:
                case StatusType.Fear:
                    return true;
                default:
                    return false;
            }
        }

        private void TriggerMythicSingularityDeath(
            EnemyState enemy,
            in GameEvent deathEvent)
        {
            if (!mythicEnemySingularities.TryGetValue(
                    enemy.Id.Value,
                    out MythicEnemySingularityRuntime runtime) ||
                runtime.DeathPayloadConsumed)
            {
                return;
            }

            runtime.DeathPayloadConsumed = true;
            var explosion = new CompiledEffectNode(
                EffectOperation.ApplyEnemySingularity,
                Math.Max(1, runtime.Node.Amount3),
                0,
                0,
                0,
                0,
                0,
                Math.Max(1, runtime.Node.RadiusMilli),
                Math.Max(1, runtime.Node.Limit),
                0,
                string.Empty);
            ExecuteExplosionWithPresentation(
                GetEnemyHitboxCenter(enemy),
                enemy.MaxHealthMilli,
                runtime.TowerId,
                runtime.CardId,
                enemy.Id,
                explosion,
                deathEvent.RootChainId,
                deathEvent.ActivationId,
                deathEvent.EventId,
                deathEvent.Depth + 1,
                explosion.Limit,
                "mythic_singularity",
                enemy.Id);
            AddMythicPresentation(
                "singularity_enemy_death",
                enemy.Id,
                runtime.SourceEntityId,
                runtime.Node.RadiusMilli);
        }

        private void ResolveMythicOwnedProxies(
            EntityId sourceId)
        {
            CopySortedMythicKeys(mythicTimeEchoes);
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                if (mythicTimeEchoes.TryGetValue(
                        mythicKeyScratch[i],
                        out MythicTimeEchoRuntime echo) &&
                    echo.SourceId == sourceId)
                {
                    ResolveMythicTimeEcho(echo);
                }
            }

            if (mythicMirrorMembers.TryGetValue(
                    sourceId.Value,
                    out MythicMirrorLinkRuntime mirror) &&
                sourceId == mirror.PrimaryId)
            {
                ResolveMythicMirrorIllusion(mirror);
            }
        }

        private void ResolveMythicTimeEcho(
            MythicTimeEchoRuntime echo)
        {
            if (echo == null)
            {
                return;
            }

            EnemyState proxy = FindEnemy(echo.EchoId);
            if (proxy != null)
            {
                proxy.Alive = false;
                proxy.HealthMilli = 0;
                proxy.RewardBudget = 0;
                proxy.WaveProgressBudget = 0;
                proxy.CardPackProgressBudget = 0;
                if (!echo.LineageResolved)
                {
                    echo.LineageResolved = true;
                    DecrementLineage(proxy);
                }
            }
            mythicTimeEchoes.Remove(echo.EchoId.Value);
            AddMythicPresentation(
                "time_rift_echo_resolved",
                echo.EchoId,
                echo.SourceId,
                (int)echo.Kind);
        }

        private void ResolveMythicMirrorIllusion(
            MythicMirrorLinkRuntime link)
        {
            if (link == null)
            {
                return;
            }

            EnemyState illusion =
                FindEnemy(link.IllusionId);
            if (illusion != null)
            {
                illusion.Alive = false;
                illusion.HealthMilli = 0;
                illusion.RewardBudget = 0;
                illusion.WaveProgressBudget = 0;
                illusion.CardPackProgressBudget = 0;
                if (!link.LineageResolved)
                {
                    link.LineageResolved = true;
                    DecrementLineage(illusion);
                }
            }
            mythicMirrorMembers.Remove(
                link.PrimaryId.Value);
            mythicMirrorMembers.Remove(
                link.IllusionId.Value);
            AddMythicPresentation(
                "mirror_illusion_resolved",
                link.IllusionId,
                link.PrimaryId,
                0);
        }

        private bool TryReserveMythicBranches(
            in EffectExecutionContext context,
            SubjectType subjectType,
            int childCount,
            int projectileSpawnCount)
        {
            if (subjectType == SubjectType.Projectile)
            {
                ProjectileState source =
                    FindProjectile(context.SubjectId);
                if (source == null ||
                    checked(source.Generation + 1) >
                        content.Safety
                            .MaxProjectileCloneGeneration ||
                    childCount >
                        content.Safety.MaxActiveProjectiles -
                        projectiles.Count)
                {
                    return false;
                }
            }

            int continuationCount =
                context.ContinuationCardCount;
            int missingOriginalContinuations = Math.Max(
                0,
                continuationCount -
                context.ReservedContinuationEvents);
            int newlyReservedContinuations = checked(
                missingOriginalContinuations +
                checked(continuationCount * childCount));
            GameEvent diagnostic = WithDiagnosticDepth(
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
            return TryReserveComposite(
                in diagnostic,
                chainEventCount:
                    newlyReservedContinuations,
                queueSlotCount:
                    continuationCount > 0
                        ? childCount + 1
                        : 0,
                projectileSpawnCount:
                    projectileSpawnCount,
                cardTriggerCount:
                    newlyReservedContinuations,
                enemySpawnCount:
                    subjectType == SubjectType.Enemy
                        ? childCount
                        : 0);
        }

        private bool CanCreateMythicEnemyProxies(
            EnemyState source,
            int requestedCount,
            in EffectExecutionContext context)
        {
            if (source == null ||
                requestedCount <= 0 ||
                source.Generation + 1 >
                    content.Safety.MaxEnemySplitGeneration ||
                enemies.Count >
                    content.Safety.MaxActiveEnemies -
                    requestedCount ||
                !lineages.TryGetValue(
                    source.LineageId.Value,
                    out LineageState lineage))
            {
                return false;
            }

            GameEvent diagnostic = WithDiagnosticDepth(
                CreateDiagnosticEvent(
                    EventType.EnemySplit,
                    context.RootChainId,
                    context.TowerId,
                    context.CardId,
                    source.Id,
                    SubjectType.Enemy),
                context.Depth);
            if (lineage.SpawnedEntityCount >
                content.Safety.MaxEnemiesPerLineage -
                requestedCount)
            {
                AddDiagnostic(
                    DiagnosticCode.EnemyLineageLimitReached,
                    diagnostic,
                    (int)BudgetFailure
                        .EnemyLineageEntityLimit);
                return false;
            }
            return TryPassSandboxEnemyCreationGate(
                requestedCount,
                in diagnostic);
        }

        private ProjectileState CreateMythicProjectileCopy(
            ProjectileState source,
            SimPosition position,
            int damageBps,
            int speedBps,
            int radiusBps,
            int lifetimeTicks,
            CardEffectVisualFlags visualFlag,
            string presentationId)
        {
            var copy = new ProjectileState
            {
                Id = new EntityId(nextEntityId++),
                SourceTowerId = source.SourceTowerId,
                Generation = checked(source.Generation + 1),
                Position = position,
                TargetId = source.TargetId,
                ApplyEnemyProgramOnHit =
                    source.ApplyEnemyProgramOnHit,
                DirectionXBps = source.DirectionXBps,
                DirectionYBps = source.DirectionYBps,
                Homing = source.Homing,
                VisualFlags =
                    source.VisualFlags | visualFlag,
                DamageMilli = Math.Max(
                    1,
                    DeterministicMath.MultiplyBasisPoints(
                        source.DamageMilli,
                        damageBps)),
                SpeedMilliPerTick = Math.Max(
                    1,
                    (int)DeterministicMath
                        .MultiplyBasisPoints(
                            source.SpeedMilliPerTick,
                            speedBps)),
                RadiusMilli = Math.Max(
                    1,
                    (int)DeterministicMath
                        .MultiplyBasisPoints(
                            source.RadiusMilli,
                            radiusBps)),
                LifetimeRemaining = Math.Min(
                    content.Safety.MaxProjectileLifetimeTicks,
                    Math.Max(1, lifetimeTicks)),
                PierceRemaining = source.PierceRemaining,
                PiercesUsed = source.PiercesUsed,
                PierceDamageMultiplierBps =
                    source.PierceDamageMultiplierBps,
                CriticalChanceBps =
                    source.CriticalChanceBps,
                RootChainId = source.RootChainId,
                ActivationId = source.ActivationId,
                LastTrailPosition = position
            };
            for (int i = 0; i < source.Bindings.Count; i++)
            {
                EffectBinding binding =
                    source.Bindings[i].Clone();
                binding.ActiveTrailHazardId = -1;
                binding.TrailStarted = false;
                binding.TrailStartPosition = position;
                copy.Bindings.Add(binding);
            }

            projectiles.Add(copy);
            // 기존 희귀/고급 런타임의 복사 정책을 한 경로로 호출한다. 각 helper가
            // 명시적으로 제외한 재귀 생성·환생 권한은 신화 파생체에서도 되살리지 않는다.
            CloneRareProjectileInheritedState(
                source,
                copy);
            InheritRareResonanceAbsorbTimeMutationProjectileRuntime(
                source,
                copy);
            InheritRareProjectileRuntime(
                source.Id,
                copy.Id);
            InheritLegendaryProjectileState(
                source,
                copy);
            AddPresentation(
                PresentationEventType.ProjectileSpawned,
                copy.Id.Value,
                source.Id.Value,
                (int)Math.Min(
                    int.MaxValue,
                    copy.DamageMilli),
                presentationId);
            return copy;
        }

        private EnemyState CreateMythicEnemyProxy(
            EnemyState source,
            long pathProgress,
            SimPosition position,
            CardEffectVisualFlags visualFlag,
            string presentationId)
        {
            var proxy = new EnemyState
            {
                Id = new EntityId(nextEntityId++),
                DefinitionId = source.DefinitionId,
                LineageId = source.LineageId,
                Generation = checked(source.Generation + 1),
                // BossSummon origin은 공용 보스 능력 처리에서 제외된다. SummonerId를
                // Invalid로 두어 실제 보스 소환 수에도 포함되지 않는다.
                SpawnOrigin = EnemySpawnOrigin.BossSummon,
                SummonerId = EntityId.Invalid,
                EliteTraitIds =
                    (EliteTraitId[])source.EliteTraitIds.Clone(),
                PathProgressMilli = pathProgress,
                PathLateralOffset =
                    source.PathLateralOffset,
                Position = position,
                HealthMilli = Math.Max(
                    1,
                    source.HealthMilli),
                MaxHealthMilli = Math.Max(
                    1,
                    source.MaxHealthMilli),
                Armor = source.Armor,
                BaseSpeedMilliPerTick =
                    source.BaseSpeedMilliPerTick,
                SpeedMultiplierBps =
                    source.SpeedMultiplierBps,
                SizeMultiplierBps =
                    source.SizeMultiplierBps,
                EliteRenderScaleBps =
                    source.EliteRenderScaleBps,
                AreaDamageTakenBps =
                    source.AreaDamageTakenBps,
                SingleDamageTakenBps =
                    source.SingleDamageTakenBps,
                VisualFlags =
                    source.VisualFlags | visualFlag,
                RewardBudget = 0,
                WaveProgressBudget = 0,
                CardPackProgressBudget = 0,
                IsShimmering = false,
                ControlThreshold =
                    source.ControlThreshold,
                ControlThresholdStep =
                    source.ControlThresholdStep,
                ShieldMilli = 0,
                BossAbilityCooldownTicks =
                    int.MaxValue,
                BossCastRemainingTicks = 0,
                BossEnraged = source.BossEnraged,
                BossPhaseAnnounced = true
            };
            enemies.Add(proxy);
            InheritRangeEntryLocks(source, proxy);
            AddPresentation(
                PresentationEventType.EnemySpawned,
                proxy.Id.Value,
                source.Id.Value,
                0,
                presentationId);
            return proxy;
        }

        private void RegisterMythicTimeEcho(
            EnemyState echo,
            EnemyState source,
            MythicEnemyProxyKind kind,
            in EffectExecutionContext context,
            int relayBps,
            int durationTicks)
        {
            mythicTimeEchoes.Add(
                echo.Id.Value,
                new MythicTimeEchoRuntime
                {
                    EchoId = echo.Id,
                    SourceId = source.Id,
                    Kind = kind,
                    TowerId = context.TowerId,
                    CardId = context.CardId,
                    CardInstanceId =
                        context.CardInstanceId,
                    DamageRelayBps =
                        Math.Max(
                            1,
                            Math.Min(10000, relayBps)),
                    RemainingTicks =
                        Math.Max(1, durationTicks)
                });
        }

        private void FinalizeMythicEnemyProxyBatch(
            EnemyState source,
            int count)
        {
            if (lineages.TryGetValue(
                    source.LineageId.Value,
                    out LineageState lineage))
            {
                lineage.SpawnedEntityCount = checked(
                    lineage.SpawnedEntityCount + count);
                lineage.LiveMembers = checked(
                    lineage.LiveMembers + count);
                lineage.HighestGeneration = Math.Max(
                    lineage.HighestGeneration,
                    source.Generation + 1);
            }
            spatialIndex.Rebuild(enemies);
        }

        private int CountMythicTimeEchoes(
            EntityId sourceId)
        {
            int count = 0;
            foreach (KeyValuePair<int, MythicTimeEchoRuntime> pair
                     in mythicTimeEchoes)
            {
                if (pair.Value.SourceId == sourceId)
                {
                    count++;
                }
            }
            return count;
        }

        private bool IsMythicEnemyProxy(
            EntityId entityId)
        {
            if (mythicTimeEchoes.ContainsKey(entityId.Value))
            {
                return true;
            }
            return mythicMirrorMembers.TryGetValue(
                       entityId.Value,
                       out MythicMirrorLinkRuntime link) &&
                   link.IllusionId == entityId;
        }

        /// <summary>
        /// 보스 능력·누수·사건 타워처럼 프록시를 일반 적과 구분해야 하는 공용
        /// 루프에서 사용할 읽기 전용 정책 조회다.
        /// </summary>
        internal bool IsMythicEnemyProxyForLifecycle(
            EntityId entityId)
        {
            return IsMythicEnemyProxy(entityId);
        }

        private MythicMotionHistory
            GetOrCreateMythicProjectileHistory(
                EntityId projectileId)
        {
            if (!mythicProjectileHistories.TryGetValue(
                    projectileId.Value,
                    out MythicMotionHistory history))
            {
                history = new MythicMotionHistory();
                mythicProjectileHistories.Add(
                    projectileId.Value,
                    history);
            }
            return history;
        }

        private MythicMotionHistory
            GetOrCreateMythicEnemyHistory(
                EntityId enemyId)
        {
            if (!mythicEnemyHistories.TryGetValue(
                    enemyId.Value,
                    out MythicMotionHistory history))
            {
                history = new MythicMotionHistory();
                mythicEnemyHistories.Add(
                    enemyId.Value,
                    history);
            }
            return history;
        }

        private SimPosition ResolveMythicProjectilePast(
            ProjectileState projectile,
            int ticksAgo,
            int fallbackSpan)
        {
            if (mythicProjectileHistories.TryGetValue(
                    projectile.Id.Value,
                    out MythicMotionHistory history) &&
                history.TryGetTicksAgo(
                    Math.Max(0, ticksAgo),
                    out SimPosition past))
            {
                return past;
            }

            return OffsetAlongProjectile(
                projectile.Position,
                -projectile.DirectionXBps,
                -projectile.DirectionYBps,
                fallbackSpan);
        }

        private static int ResolveMythicTemporalSpan(
            int speedMilliPerTick,
            int durationTicks,
            int radiusMilli)
        {
            long byTime = Math.Max(
                1,
                (long)Math.Max(1, speedMilliPerTick) *
                Math.Max(1, durationTicks));
            long bounded = radiusMilli > 0
                ? Math.Min(byTime, radiusMilli)
                : byTime;
            return (int)Math.Max(
                1,
                Math.Min(int.MaxValue, bounded));
        }

        private static SimPosition OffsetAlongProjectile(
            SimPosition position,
            int directionXBps,
            int directionYBps,
            int distanceMilli)
        {
            long offsetX =
                DeterministicMath.MultiplyDivide(
                    directionXBps,
                    distanceMilli,
                    DeterministicMath.BasisPointScale);
            long offsetY =
                DeterministicMath.MultiplyDivide(
                    directionYBps,
                    distanceMilli,
                    DeterministicMath.BasisPointScale);
            return SimPosition.FromMilliUnits(
                SaturatingAddSigned(
                    position.X.MilliUnits,
                    offsetX),
                SaturatingAddSigned(
                    position.Y.MilliUnits,
                    offsetY));
        }

        private static SimPosition ReflectAroundPoint(
            SimPosition source,
            SimPosition pivot,
            int maximumRadiusMilli)
        {
            long deltaX = SaturatingSubtractSigned(
                source.X.MilliUnits,
                pivot.X.MilliUnits);
            long deltaY = SaturatingSubtractSigned(
                source.Y.MilliUnits,
                pivot.Y.MilliUnits);
            SimPosition reflected = SimPosition.FromMilliUnits(
                SaturatingSubtractSigned(
                    pivot.X.MilliUnits,
                    deltaX),
                SaturatingSubtractSigned(
                    pivot.Y.MilliUnits,
                    deltaY));
            if (maximumRadiusMilli > 0 &&
                !PathModel.IsWithin(
                    pivot,
                    reflected,
                    maximumRadiusMilli))
            {
                return PathModel.MoveTowards(
                    pivot,
                    reflected,
                    maximumRadiusMilli);
            }
            return reflected;
        }

        private EnemyState SelectMythicOuroborosEnemy(
            SimPosition origin,
            int radiusMilli,
            ChainId rootChainId,
            EntityId excludedId)
        {
            CollectMythicNearbyEnemies(
                origin,
                radiusMilli,
                excludedId,
                int.MaxValue,
                includeProxies: false);
            for (int i = 0; i < mythicEnemyScratch.Count; i++)
            {
                EnemyState candidate = mythicEnemyScratch[i];
                if (!mythicOuroborosVisits.Contains(
                        new MythicOuroborosVisitKey(
                            rootChainId,
                            SubjectType.Enemy,
                            candidate.Id)))
                {
                    return candidate;
                }
            }
            return null;
        }

        private void CollectMythicNearbyEnemies(
            SimPosition origin,
            int radiusMilli,
            EntityId excludedId,
            int limit,
            bool includeProxies)
        {
            mythicEnemyScratch.Clear();
            int radius = Math.Max(0, radiusMilli);
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState candidate = enemies[i];
                if (candidate == null ||
                    !candidate.Alive ||
                    candidate.DeathQueued ||
                    candidate.Id == excludedId ||
                    (!includeProxies &&
                     IsMythicEnemyProxy(candidate.Id)) ||
                    !PathModel.IsWithin(
                        origin,
                        candidate.Position,
                        radius))
                {
                    continue;
                }
                mythicEnemyScratch.Add(candidate);
            }
            SortMythicEnemiesByDistance(
                mythicEnemyScratch,
                origin);
            int maximum = Math.Max(0, limit);
            if (mythicEnemyScratch.Count > maximum)
            {
                mythicEnemyScratch.RemoveRange(
                    maximum,
                    mythicEnemyScratch.Count - maximum);
            }
        }

        private void CleanupMythicOrphanProxies()
        {
            CopySortedMythicKeys(mythicTimeEchoes);
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                if (!mythicTimeEchoes.TryGetValue(
                        mythicKeyScratch[i],
                        out MythicTimeEchoRuntime echo))
                {
                    continue;
                }
                EnemyState source = FindEnemy(echo.SourceId);
                EnemyState proxy = FindEnemy(echo.EchoId);
                if (source == null ||
                    !source.Alive ||
                    proxy == null ||
                    !proxy.Alive)
                {
                    ResolveMythicTimeEcho(echo);
                }
            }

            // 같은 link가 두 key에 들어 있으므로 primary key에서만 검사한다.
            CopySortedMythicKeys(mythicMirrorMembers);
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                if (!mythicMirrorMembers.TryGetValue(
                        mythicKeyScratch[i],
                        out MythicMirrorLinkRuntime link) ||
                    link.PrimaryId.Value !=
                    mythicKeyScratch[i])
                {
                    continue;
                }
                EnemyState primary = FindEnemy(link.PrimaryId);
                EnemyState illusion =
                    FindEnemy(link.IllusionId);
                if (primary == null ||
                    !primary.Alive ||
                    illusion == null ||
                    !illusion.Alive)
                {
                    ResolveMythicMirrorIllusion(link);
                }
            }
        }

        private void CleanupMythicProjectileRuntime()
        {
            RemoveDeadMythicProjectileKeys(
                mythicProjectileSingularities);
            RemoveDeadMythicProjectileKeys(
                mythicProjectilePhoenixCores);
            RemoveDeadMythicProjectileKeys(
                mythicProjectileHistories);
        }

        private void CleanupMythicEnemyRuntime()
        {
            RemoveDeadMythicEnemyKeys(
                mythicEnemySingularities);
            RemoveDeadMythicEnemyKeys(
                mythicEnemyPhoenixCores);
            RemoveDeadMythicEnemyKeys(
                mythicEnemyHistories);
        }

        private void CleanupMythicOuroborosRuntime()
        {
            RemoveDeadMythicProjectileKeys(
                mythicProjectileOuroboros);
            RemoveDeadMythicEnemyKeys(
                mythicEnemyOuroboros);
            mythicActiveRootScratch.Clear();
            foreach (KeyValuePair<int, MythicOuroborosRuntime> pair
                     in mythicProjectileOuroboros)
            {
                mythicActiveRootScratch.Add(
                    pair.Value.RootChainId.Value);
            }
            foreach (KeyValuePair<int, MythicOuroborosRuntime> pair
                     in mythicEnemyOuroboros)
            {
                mythicActiveRootScratch.Add(
                    pair.Value.RootChainId.Value);
            }

            mythicVisitScratch.Clear();
            foreach (MythicOuroborosVisitKey visit
                     in mythicOuroborosVisits)
            {
                if (!mythicActiveRootScratch.Contains(
                        visit.RootChainId.Value))
                {
                    mythicVisitScratch.Add(visit);
                }
            }
            for (int i = 0; i < mythicVisitScratch.Count; i++)
            {
                mythicOuroborosVisits.Remove(
                    mythicVisitScratch[i]);
            }
        }

        private void RemoveDeadMythicProjectileKeys<T>(
            Dictionary<int, T> values)
        {
            CopySortedMythicKeys(values);
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                int key = mythicKeyScratch[i];
                ProjectileState projectile =
                    FindProjectile(new EntityId(key));
                if (projectile == null || !projectile.Alive)
                {
                    values.Remove(key);
                }
            }
        }

        private void RemoveDeadMythicEnemyKeys<T>(
            Dictionary<int, T> values)
        {
            CopySortedMythicKeys(values);
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                int key = mythicKeyScratch[i];
                EnemyState enemy =
                    FindEnemy(new EntityId(key));
                if (enemy == null || !enemy.Alive)
                {
                    values.Remove(key);
                }
            }
        }

        private void CopySortedMythicKeys<T>(
            Dictionary<int, T> values)
        {
            mythicKeyScratch.Clear();
            foreach (int key in values.Keys)
            {
                mythicKeyScratch.Add(key);
            }
            mythicKeyScratch.Sort();
        }

        private static void SortMythicProjectilesByDistance(
            List<ProjectileState> values,
            SimPosition origin)
        {
            for (int i = 1; i < values.Count; i++)
            {
                ProjectileState current = values[i];
                ulong currentDistance =
                    origin.DistanceSquaredRaw(
                        current.Position);
                int insert = i - 1;
                while (insert >= 0)
                {
                    ProjectileState previous =
                        values[insert];
                    ulong previousDistance =
                        origin.DistanceSquaredRaw(
                            previous.Position);
                    if (previousDistance <
                            currentDistance ||
                        (previousDistance ==
                             currentDistance &&
                         previous.Id.Value <
                             current.Id.Value))
                    {
                        break;
                    }
                    values[insert + 1] = previous;
                    insert--;
                }
                values[insert + 1] = current;
            }
        }

        private static void SortMythicEnemiesByDistance(
            List<EnemyState> values,
            SimPosition origin)
        {
            for (int i = 1; i < values.Count; i++)
            {
                EnemyState current = values[i];
                ulong currentDistance =
                    origin.DistanceSquaredRaw(
                        current.Position);
                int insert = i - 1;
                while (insert >= 0)
                {
                    EnemyState previous =
                        values[insert];
                    ulong previousDistance =
                        origin.DistanceSquaredRaw(
                            previous.Position);
                    if (previousDistance <
                            currentDistance ||
                        (previousDistance ==
                             currentDistance &&
                         previous.Id.Value <
                             current.Id.Value))
                    {
                        break;
                    }
                    values[insert + 1] = previous;
                    insert--;
                }
                values[insert + 1] = current;
            }
        }

        private static long SaturatingAddPositive(
            long left,
            long right)
        {
            if (right <= 0)
            {
                return Math.Max(0, left);
            }
            return left > long.MaxValue - right
                ? long.MaxValue
                : left + right;
        }

        private static long SaturatingAddSigned(
            long left,
            long right)
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

        private static long SaturatingSubtractSigned(
            long left,
            long right)
        {
            if (right > 0 && left < long.MinValue + right)
            {
                return long.MinValue;
            }
            if (right < 0 && left > long.MaxValue + right)
            {
                return long.MaxValue;
            }
            return left - right;
        }

        private void AddMythicPresentation(
            string effectId,
            EntityId subjectId,
            EntityId sourceId,
            int value)
        {
            AddPresentation(
                PresentationEventType.EffectTriggered,
                subjectId.Value,
                sourceId.Value,
                value,
                effectId);
        }

        /// <summary>
        /// ComputeStateHash 끝에서 호출해 본 파일이 소유한 미래 판정 상태를 안정 순서로
        /// 추가한다. Dictionary의 삽입 순서와 CLR 구현은 결과에 영향을 주지 않는다.
        /// </summary>
        internal void AppendMythicCardStateHash(
            ref StableHashBuilder hash)
        {
            AppendMythicProjectileSingularityHash(ref hash);
            AppendMythicEnemySingularityHash(ref hash);
            AppendMythicPhoenixHash(ref hash);
            AppendMythicTimeEchoHash(ref hash);
            AppendMythicMirrorHash(ref hash);
            AppendMythicHistoryHash(
                ref hash,
                mythicProjectileHistories);
            AppendMythicHistoryHash(
                ref hash,
                mythicEnemyHistories);
            AppendMythicOuroborosHash(ref hash);
            AppendMythicRelayHash(ref hash);
        }

        private void AppendMythicProjectileSingularityHash(
            ref StableHashBuilder hash)
        {
            CopySortedMythicKeys(
                mythicProjectileSingularities);
            hash.Add(mythicKeyScratch.Count);
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                int key = mythicKeyScratch[i];
                MythicProjectileSingularityRuntime runtime =
                    mythicProjectileSingularities[key];
                hash.Add(key);
                AppendMythicSourceHash(
                    ref hash,
                    runtime.TowerId,
                    runtime.CardId,
                    runtime.CardInstanceId,
                    runtime.SourceEntityId);
                hash.Add(runtime.RootChainId);
                hash.Add(runtime.ActivationId);
                hash.Add(runtime.ParentEventId);
                hash.Add(runtime.Depth);
                AppendMythicNodeHash(
                    ref hash,
                    runtime.Node);
                hash.Add(runtime.RemainingTicks);
                hash.Add(runtime.NextTick);
                hash.Add(runtime.Released);
                hash.Add(runtime.Captures.Count);
                for (int captureIndex = 0;
                     captureIndex < runtime.Captures.Count;
                     captureIndex++)
                {
                    MythicCapturedProjectile capture =
                        runtime.Captures[captureIndex];
                    hash.Add(capture.SourceId);
                    hash.Add(capture.SourceTowerId);
                    hash.Add(capture.DamageMilli);
                    hash.Add((ulong)capture.VisualFlags);
                    hash.Add(capture.Bindings.Count);
                    for (int bindingIndex = 0;
                         bindingIndex < capture.Bindings.Count;
                         bindingIndex++)
                    {
                        AppendMythicBindingHash(
                            ref hash,
                            capture.Bindings[bindingIndex]);
                    }
                }
            }
        }

        private void AppendMythicEnemySingularityHash(
            ref StableHashBuilder hash)
        {
            CopySortedMythicKeys(
                mythicEnemySingularities);
            hash.Add(mythicKeyScratch.Count);
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                int key = mythicKeyScratch[i];
                MythicEnemySingularityRuntime runtime =
                    mythicEnemySingularities[key];
                hash.Add(key);
                AppendMythicSourceHash(
                    ref hash,
                    runtime.TowerId,
                    runtime.CardId,
                    runtime.CardInstanceId,
                    runtime.SourceEntityId);
                hash.Add(runtime.RootChainId);
                hash.Add(runtime.ActivationId);
                hash.Add(runtime.ParentEventId);
                hash.Add(runtime.Depth);
                AppendMythicNodeHash(
                    ref hash,
                    runtime.Node);
                hash.Add(runtime.RemainingTicks);
                hash.Add(runtime.NextTick);
                hash.Add(runtime.DeathPayloadConsumed);
            }
        }

        private void AppendMythicPhoenixHash(
            ref StableHashBuilder hash)
        {
            CopySortedMythicKeys(
                mythicProjectilePhoenixCores);
            hash.Add(mythicKeyScratch.Count);
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                int key = mythicKeyScratch[i];
                MythicProjectilePhoenixRuntime runtime =
                    mythicProjectilePhoenixCores[key];
                hash.Add(key);
                AppendMythicSourceHash(
                    ref hash,
                    runtime.TowerId,
                    runtime.CardId,
                    runtime.CardInstanceId,
                    runtime.SourceEntityId);
                AppendMythicNodeHash(
                    ref hash,
                    runtime.Node);
                hash.Add(runtime.Consumed);
            }

            CopySortedMythicKeys(
                mythicEnemyPhoenixCores);
            hash.Add(mythicKeyScratch.Count);
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                int key = mythicKeyScratch[i];
                MythicEnemyPhoenixRuntime runtime =
                    mythicEnemyPhoenixCores[key];
                hash.Add(key);
                AppendMythicSourceHash(
                    ref hash,
                    runtime.TowerId,
                    runtime.CardId,
                    runtime.CardInstanceId,
                    runtime.SourceEntityId);
                AppendMythicNodeHash(
                    ref hash,
                    runtime.Node);
                hash.Add(runtime.Consumed);
                hash.Add(runtime.VulnerabilityBps);
                hash.Add(runtime.VulnerabilityTicks);
            }
        }

        private void AppendMythicTimeEchoHash(
            ref StableHashBuilder hash)
        {
            CopySortedMythicKeys(mythicTimeEchoes);
            hash.Add(mythicKeyScratch.Count);
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                MythicTimeEchoRuntime echo =
                    mythicTimeEchoes[mythicKeyScratch[i]];
                hash.Add(echo.EchoId);
                hash.Add(echo.SourceId);
                hash.Add((int)echo.Kind);
                AppendMythicSourceHash(
                    ref hash,
                    echo.TowerId,
                    echo.CardId,
                    echo.CardInstanceId,
                    echo.SourceId);
                hash.Add(echo.DamageRelayBps);
                hash.Add(echo.RemainingTicks);
                hash.Add(echo.LineageResolved);
            }
        }

        private void AppendMythicMirrorHash(
            ref StableHashBuilder hash)
        {
            CopySortedMythicKeys(mythicMirrorMembers);
            int primaryCount = 0;
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                MythicMirrorLinkRuntime link =
                    mythicMirrorMembers[
                        mythicKeyScratch[i]];
                if (link.PrimaryId.Value ==
                    mythicKeyScratch[i])
                {
                    primaryCount++;
                }
            }
            hash.Add(primaryCount);
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                MythicMirrorLinkRuntime link =
                    mythicMirrorMembers[
                        mythicKeyScratch[i]];
                if (link.PrimaryId.Value !=
                    mythicKeyScratch[i])
                {
                    continue;
                }
                hash.Add(link.PrimaryId);
                hash.Add(link.IllusionId);
                AppendMythicSourceHash(
                    ref hash,
                    link.TowerId,
                    link.CardId,
                    link.CardInstanceId,
                    link.PrimaryId);
                hash.Add(link.SharedHealthBps);
                hash.Add(link.RadiusMilli);
                hash.Add(link.LineageResolved);
            }
        }

        private void AppendMythicHistoryHash(
            ref StableHashBuilder hash,
            Dictionary<int, MythicMotionHistory> histories)
        {
            CopySortedMythicKeys(histories);
            hash.Add(mythicKeyScratch.Count);
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                int key = mythicKeyScratch[i];
                MythicMotionHistory history =
                    histories[key];
                hash.Add(key);
                hash.Add(history.LastRecordedTick);
                hash.Add(history.Count);
                for (int entry = 0;
                     entry < history.Count;
                     entry++)
                {
                    hash.Add(
                        history.GetChronological(entry));
                }
            }
        }

        private void AppendMythicOuroborosHash(
            ref StableHashBuilder hash)
        {
            AppendMythicOuroborosRuntimeHash(
                ref hash,
                mythicProjectileOuroboros);
            AppendMythicOuroborosRuntimeHash(
                ref hash,
                mythicEnemyOuroboros);

            mythicVisitScratch.Clear();
            foreach (MythicOuroborosVisitKey visit
                     in mythicOuroborosVisits)
            {
                mythicVisitScratch.Add(visit);
            }
            SortMythicVisits(mythicVisitScratch);
            hash.Add(mythicVisitScratch.Count);
            for (int i = 0; i < mythicVisitScratch.Count; i++)
            {
                MythicOuroborosVisitKey visit =
                    mythicVisitScratch[i];
                hash.Add(visit.RootChainId);
                hash.Add((int)visit.SubjectType);
                hash.Add(visit.SubjectId);
            }
        }

        private void AppendMythicOuroborosRuntimeHash(
            ref StableHashBuilder hash,
            Dictionary<int, MythicOuroborosRuntime> runtimes)
        {
            CopySortedMythicKeys(runtimes);
            hash.Add(mythicKeyScratch.Count);
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                MythicOuroborosRuntime runtime =
                    runtimes[mythicKeyScratch[i]];
                hash.Add((int)runtime.SubjectType);
                hash.Add(runtime.SubjectId);
                AppendMythicSourceHash(
                    ref hash,
                    runtime.TowerId,
                    runtime.CardId,
                    runtime.CardInstanceId,
                    runtime.SubjectId);
                hash.Add(runtime.RootChainId);
                AppendMythicNodeHash(
                    ref hash,
                    runtime.Node);
            }
        }

        private void AppendMythicRelayHash(
            ref StableHashBuilder hash)
        {
            mythicKeyScratch.Clear();
            foreach (int eventId in mythicRelayEventIds)
            {
                mythicKeyScratch.Add(eventId);
            }
            mythicKeyScratch.Sort();
            hash.Add(mythicKeyScratch.Count);
            for (int i = 0; i < mythicKeyScratch.Count; i++)
            {
                hash.Add(mythicKeyScratch[i]);
            }
        }

        private static void AppendMythicSourceHash(
            ref StableHashBuilder hash,
            TowerId towerId,
            CardId cardId,
            int cardInstanceId,
            EntityId sourceEntityId)
        {
            hash.Add(towerId);
            hash.Add(cardId);
            hash.Add(cardInstanceId);
            hash.Add(sourceEntityId);
        }

        private static void AppendMythicBindingHash(
            ref StableHashBuilder hash,
            EffectBinding binding)
        {
            hash.Add((int)binding.Trigger);
            hash.Add((int)binding.Kind);
            hash.Add(binding.CardId);
            hash.Add(binding.CardInstanceId);
            AppendMythicNodeHash(
                ref hash,
                binding.Node);
            hash.Add(binding.Used);
            hash.Add(binding.TriggerCount);
            hash.Add(binding.TrailStarted);
            hash.Add(binding.TrailStartPosition);
            hash.Add(binding.ActiveTrailHazardId);
        }

        private static void AppendMythicNodeHash(
            ref StableHashBuilder hash,
            in CompiledEffectNode node)
        {
            hash.Add((int)node.Operation);
            hash.Add(node.Amount);
            hash.Add(node.Amount2);
            hash.Add(node.Amount3);
            hash.Add(node.DurationTicks);
            hash.Add(node.IntervalTicks);
            hash.Add(node.MaxStacks);
            hash.Add(node.RadiusMilli);
            hash.Add(node.Limit);
            hash.Add(node.ChanceBps);
            hash.Add(node.ReferenceId);
        }

        private static void SortMythicVisits(
            List<MythicOuroborosVisitKey> values)
        {
            for (int i = 1; i < values.Count; i++)
            {
                MythicOuroborosVisitKey current =
                    values[i];
                int insert = i - 1;
                while (insert >= 0 &&
                       CompareMythicVisits(
                           values[insert],
                           current) > 0)
                {
                    values[insert + 1] =
                        values[insert];
                    insert--;
                }
                values[insert + 1] = current;
            }
        }

        private static int CompareMythicVisits(
            MythicOuroborosVisitKey left,
            MythicOuroborosVisitKey right)
        {
            int result = left.RootChainId.Value.CompareTo(
                right.RootChainId.Value);
            if (result != 0)
            {
                return result;
            }
            result = ((int)left.SubjectType).CompareTo(
                (int)right.SubjectType);
            return result != 0
                ? result
                : left.SubjectId.Value.CompareTo(
                    right.SubjectId.Value);
        }
    }
}
