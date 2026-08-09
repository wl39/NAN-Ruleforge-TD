using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;

namespace RuleforgeTD.GameLogic.Simulation
{
    internal enum EnemySpawnOrigin
    {
        Scheduled = 0,
        Split = 1,
        BossSummon = 2,
        ShimmeringCarrier = 3,
        Sandbox = 4
    }

    public enum CardPackSource
    {
        ShimmeringCarrier = 0,
        Boss = 1
    }

    /// <summary>
    /// 피해 계산에서 어떤 저항과 방어 규칙을 적용할지 구분하는 내부 분류다.
    /// 화면에 보이는 속성 이름이 아니라 계산 파이프라인 선택용이다.
    /// </summary>
    internal enum DamageKind
    {
        Physical = 0,
        Fire = 1,
        Poison = 2,
        Explosion = 3,
        Collision = 4
    }

    /// <summary>
    /// 가계 보상을 증가시키는 서로 다른 원인을 중복 방지 원장에서 구분한다.
    /// </summary>
    internal enum RewardAugmentKind
    {
        GoldBounty = 0,
        Enlarge = 1,
        Accelerate = 2
    }

    /// <summary>
    /// 같은 타워·같은 카드 인스턴스·같은 증액 종류가 한 적 가계의 보상을
    /// 여러 번 늘리지 못하게 하는 복합 키다.
    /// </summary>
    internal readonly struct RewardAugmentKey
    {
        public RewardAugmentKey(
            TowerId towerId,
            int cardInstanceId,
            RewardAugmentKind kind)
        {
            TowerId = towerId;
            CardInstanceId = cardInstanceId;
            Kind = kind;
        }

        /// <summary>증액을 실행한 타워 인스턴스다.</summary>
        public TowerId TowerId { get; }
        /// <summary>동일 정의 카드 여러 장을 구분하는 카드 인스턴스 ID다.</summary>
        public int CardInstanceId { get; }
        /// <summary>골드 카드, 거대화 등 증액 규칙 종류다.</summary>
        public RewardAugmentKind Kind { get; }

        public override bool Equals(object obj)
        {
            return obj is RewardAugmentKey other &&
                   TowerId == other.TowerId &&
                   CardInstanceId == other.CardInstanceId &&
                   Kind == other.Kind;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = TowerId.Value;
                hash = (hash * 397) ^ CardInstanceId;
                hash = (hash * 397) ^ (int)Kind;
                return hash;
            }
        }
    }

    /// <summary>
    /// 카드 실행 시 즉시 끝나지 않고 적중·소멸·사망 사건에 연결되는 효과다.
    /// </summary>
    /// <remarks>
    /// 예를 들어 탄환 해석의 화상은 "지금 화상을 적용"하는 것이 아니라
    /// 탄환에 OnHit/Burn 바인딩을 추가한다. 이후 실제 적중 사건이 이 값을 읽는다.
    /// </remarks>
    internal sealed class EffectBinding
    {
        // Trigger는 언제, Kind는 무엇을 실행할지를 나타낸다.
        public BindingTrigger Trigger;
        public BindingKind Kind;

        // 출처 정보는 지속 피해, 처치, 보상을 원래 카드와 타워에 귀속할 때 필요하다.
        public CardId CardId;
        public int CardInstanceId;

        // 밸런스 JSON에서 컴파일된 수치 묶음이다.
        public CompiledEffectNode Node;

        // 최초 1회 계열 바인딩의 소비 여부와 총 발동 횟수를 추적한다.
        public bool Used;
        public int TriggerCount;

        // 화상 탄환은 현재 만들고 있는 불길 선분을 매 이동 틱 끝점까지 늘린다.
        // 카드 인스턴스별로 보존해야 같은 탄환에 화상 카드가 여러 장 있어도
        // 각 카드의 출처와 중첩 규칙이 서로 섞이지 않는다.
        public bool TrailStarted;
        public SimPosition TrailStartPosition;
        public int ActiveTrailHazardId = -1;

        /// <summary>
        /// 수치는 그대로 공유하고 사용 여부 같은 실행 상태는 독립된 얕은 복사본을 만든다.
        /// </summary>
        public EffectBinding Clone()
        {
            return (EffectBinding)MemberwiseClone();
        }
    }

    /// <summary>
    /// 적 하나에 실제로 부착된 상태이상 인스턴스의 내부 원본 상태다.
    /// </summary>
    internal sealed class StatusInstance
    {
        // 정체성과 피해/보상 출처.
        public int InstanceId;
        public StatusType Type;
        public EntityId SourceEntityId;
        public TowerId SourceTowerId;
        public CardId SourceCardId;
        public int SourceCardInstanceId;

        // 상태의 현재 세기와 수명. RemainingTicks와 NextTick은 모두 고정 틱 기준이다.
        public int Stacks;
        public int Intensity;
        public int RemainingTicks;
        public int MaxStacks;
        public int TickInterval;
        public long NextTick;

        // 후속 생성 카드가 명시적으로 상태 상속을 허용할 때 사용할 계약 필드다.
        public bool Inherited;
        public bool Dispellable;

        // 상태별 선택적 수치다. 예: 전체 상한, 전염 반경, 방어 무시율.
        public int Limit;
        public int RadiusMilli;
        public int ArmorIgnoreBps;
    }

    /// <summary>
    /// 플레이어가 실제로 소유한 카드 한 장의 런타임 상태다.
    /// 카드 정의와 달리 장착 위치와 강화 레벨은 카드 인스턴스마다 다를 수 있다.
    /// </summary>
    internal sealed class CardInstanceState
    {
        public int InstanceId;
        public CardId DefinitionId;
        public int Level = 1;
        public bool Equipped;
        public TowerId EquippedTowerId = TowerId.Invalid;
        public int EquippedSlot = -1;
    }

    /// <summary>
    /// 건설 지점에 배치된 타워 한 개의 규칙 상태다.
    /// MonoBehaviour나 타워 프리팹이 아니라 전투 판정의 원본이다.
    /// </summary>
    internal sealed class TowerState
    {
        // 타워 인스턴스 정체성, 데이터 정의, 배치 위치.
        public TowerId Id;
        public TowerDefinitionId DefinitionId;
        public int BuildPointIndex;
        public SimPosition Position;
        public int Level = 1;
        public SubjectType SubjectType = SubjectType.Projectile;

        // 기본 공격 또는 주기 발동까지 남은 고정 틱.
        public int CooldownRemaining;

        // 공격 준비 연출과 실제 탄환 생성 사이의 결정론적 대기 상태.
        public int AttackWindupRemaining;
        public EntityId PendingAttackTargetId = EntityId.Invalid;

        // 계획 단계에서 편집하는 슬롯 상태.
        public int[] CardInstanceIds;
        public SubjectType[] CardSubjectTypes;

        // 웨이브 시작 시 위 슬롯을 복사한 불변 실행 프로그램과 카드 인스턴스 배열.
        public CardId[] Program;
        public int[] ProgramInstances;
        public SubjectType[] ProgramSubjectTypes;

        // 범위 진입 타워가 같은 적을 매 틱 재발동하지 않도록 기록하는 대상별 상태.
        public readonly Dictionary<int, long> LastTargetTriggerTick =
            new Dictionary<int, long>();
        public readonly HashSet<int> TargetsInside = new HashSet<int>();

        // 골드 카드의 타워당·웨이브당 상한 계산용 누적값.
        public int GoldGeneratedThisWave;
    }

    /// <summary>
    /// 적 논리 개체 하나의 모든 전투 원본 상태다.
    /// 분열한 두 적은 서로 다른 EnemyState지만 같은 LineageId를 공유한다.
    /// </summary>
    internal sealed class EnemyState
    {
        // 개체/정의/가계 정체성과 분열 세대.
        public EntityId Id;
        public EnemyDefinitionId DefinitionId;
        public LineageId LineageId;
        public int Generation;
        public EnemySpawnOrigin SpawnOrigin;
        public EntityId SummonerId = EntityId.Invalid;

        // 기본 적 원형과 조합된 엘리트 특성이다. 현재 스폰은 한 개만 허용하지만
        // 배열을 유지해 향후 복합 특성에서도 개체/스냅샷 스키마를 바꾸지 않는다.
        public EliteTraitId[] EliteTraitIds = Array.Empty<EliteTraitId>();

        // 이동의 원본은 경로 진행 거리다. Position은 공간 검색용 환산 좌표다.
        public long PathProgressMilli;
        // 분열 시 진행 방향 기준 좌우로 갈라진 가지의 경로 수직 오프셋이다.
        // 경로 진행도와 별도로 보존해 다음 틱에도 분열체가 다시 겹치지 않게 한다.
        public SimVector PathLateralOffset = SimVector.Zero;
        public SimPosition Position;

        // 체력·방어와 기본 이동 능력.
        public long HealthMilli;
        public long MaxHealthMilli;
        public int Armor;
        public int BaseSpeedMilliPerTick;

        // 장기 이동 제한 탈출 모듈의 결정적 감시 상태다. 경로 진행도가 실제로
        // 변하면 감시를 다시 시작하며, 탈출 중에도 기존 디버프 수명은 정상적으로 흐른다.
        public bool MovementEscapeWatchInitialized;
        public long MovementEscapeWatchProgressMilli;
        public long MovementEscapeStationarySinceTick;
        public long MovementEscapeUntilTick;

        // 카드가 적용한 배율. 10,000 basis point가 100%다.
        public int SpeedMultiplierBps = 10000;
        public int SizeMultiplierBps = 10000;
        public int EliteRenderScaleBps = 10000;
        public int AreaDamageTakenBps = 10000;
        public int SingleDamageTakenBps = 10000;
        public CardEffectVisualFlags VisualFlags;

        // 이 분열 가지에 현재 배정된 골드와 웨이브 기여도.
        public int RewardBudget;
        public int WaveProgressBudget;
        public int CardPackProgressBudget;
        public bool IsShimmering;

        // 사망 요청과 실제 제거 사이의 중복 처리를 막는 생명주기 플래그.
        public bool Alive = true;
        public bool RewardClaimed;
        public bool DeathQueued;

        // 정예·보스의 강한 제어 효과를 완전 면역 대신 누적하는 게이지.
        public int ControlGauge;
        public int ControlThreshold;
        public int ControlThresholdStep;

        // 보스 전용 상태. 일반 적은 모두 기본값을 유지한다.
        public long ShieldMilli;
        public int BossAbilityCooldownTicks;
        public int BossCastRemainingTicks;
        public bool BossEnraged;
        public bool BossPhaseAnnounced;

        // 현재 상태, 사망 시 실행 효과, 보상 중복 방지 상태.
        public readonly List<StatusInstance> Statuses = new List<StatusInstance>(4);
        public readonly List<EffectBinding> DeathBindings = new List<EffectBinding>(2);
        public readonly HashSet<int> RewardModifiers = new HashSet<int>();
    }

    /// <summary>
    /// 원본 적 한 마리와 모든 분열 후손이 공유하는 보상·생성 원장이다.
    /// </summary>
    /// <remarks>
    /// 분열은 EnemyState를 늘리지만 이 원장의 총 골드와 진행도를 늘리지 않는다.
    /// 지급, 몰수, 살아 있는 가지 할당을 합하면 항상 MaxRewardBudget과 같아야 한다.
    /// </remarks>
    internal sealed class LineageState
    {
        // 가계 정체성과 생성 안전 한도 추적.
        public LineageId Id;
        public int HighestGeneration;
        public int SplitCount;
        public int SpawnedEntityCount;
        public int LiveMembers;

        // 골드 예산 원장.
        public int BaseRewardBudget;
        public int MaxRewardBudget;
        public int PaidReward;
        public int ForfeitedReward;

        // 웨이브 완료 판정을 위한 기여도 원장.
        public int ProgressBudget;
        public int ConsumedProgress;

        // 카드팩 처치 진행도는 골드/웨이브 예산과 독립적으로 0.51 분열 규칙을 사용한다.
        public int BaseCardPackProgress;
        public int AwardedCardPackProgress;
        public int ForfeitedCardPackProgress;
        public bool IsShimmering;
        public bool ShimmeringFailed;
        public SimPosition LastResolvedPosition;

        // 동일 카드 인스턴스가 여러 분열 가지에서 보상을 중복 증가시키는 것을 막는다.
        public readonly HashSet<RewardAugmentKey> AppliedRewardAugments =
            new HashSet<RewardAugmentKey>();
    }

    internal sealed class CardPackState
    {
        public int Id;
        public CardPackSource Source;
        public SimPosition Position;
        public bool WorldDrop;
    }

    /// <summary>
    /// 화면 프리팹과 무관하게 이동·충돌·카드 바인딩을 계산하는 탄환 원본 상태다.
    /// </summary>
    internal sealed class ProjectileState
    {
        // 탄환 정체성, 출처 타워, 파생 세대.
        public EntityId Id;
        public TowerId SourceTowerId;
        public int Generation;

        // 현재 위치, 선택 대상, 정규화된 방향. 방향 축은 basis point로 저장한다.
        public SimPosition Position;
        public EntityId TargetId;
        public int DirectionXBps;
        public int DirectionYBps;
        public bool Homing;
        public bool ApplyEnemyProgramOnHit;
        public CardEffectVisualFlags VisualFlags;

        // 전투 수치와 수명.
        public long DamageMilli;
        public int SpeedMilliPerTick;
        public int RadiusMilli = 150;
        public int LifetimeRemaining;
        public int PierceRemaining;
        public int PiercesUsed;
        public int PierceDamageMultiplierBps = 9000;
        public int CriticalChanceBps;

        // 소멸 사건을 한 번만 예약하기 위한 생명주기 상태.
        public bool Alive = true;
        public bool ExpirationQueued;

        // 연쇄 안전 예산과 사건 계보를 연결하는 ID.
        public ChainId RootChainId;
        public ActivationId ActivationId;

        // 적중·소멸 시 발동할 카드 효과와 이미 맞힌 적 원장.
        public readonly List<EffectBinding> Bindings = new List<EffectBinding>(4);
        public readonly HashSet<int> HitEnemies = new HashSet<int>();

        // 골드 카드의 탄환당 상한과 화상 흔적 생성 간격을 추적한다.
        public int UniqueGoldHits;
        public SimPosition LastTrailPosition;
    }

    /// <summary>
    /// 화상 불길이나 독안개처럼 일정 시간 위치에 남아 적에게 적용되는 논리 영역이다.
    /// </summary>
    internal sealed class HazardState
    {
        // 영역 정체성, 종류, 연속 선분 공간과 남은 수명.
        public int Id;
        public BindingKind Kind;
        public SimPosition StartPosition;
        public SimPosition EndPosition;
        public int RadiusMilli;
        public int DurationTicks;
        public int RemainingTicks;

        // 상태 피해와 보상을 원래 타워·카드·탄환에 귀속하기 위한 출처.
        public TowerId SourceTowerId;
        public CardId SourceCardId;
        public int SourceCardInstanceId;
        public EntityId SourceEntityId;
        public ChainId RootChainId;

        // 영역이 적용할 컴파일된 효과 수치.
        public CompiledEffectNode Node;

        // 같은 영역이 같은 적에게 매 틱 무한 재적용되지 않도록 하는 원장.
        public readonly HashSet<int> AppliedEnemies = new HashSet<int>();
    }

    /// <summary>
    /// 같은 탄환·카드가 만든 서로 맞닿은 불길 조각들이 한 틱에 같은 적에게
    /// 화상을 여러 번 중첩하지 않도록 묶는 결정적 접촉 키다.
    /// </summary>
    internal readonly struct HazardContactKey
    {
        public HazardContactKey(
            int sourceEntityId,
            int sourceCardInstanceId,
            int enemyId)
        {
            SourceEntityId = sourceEntityId;
            SourceCardInstanceId = sourceCardInstanceId;
            EnemyId = enemyId;
        }

        public int SourceEntityId { get; }
        public int SourceCardInstanceId { get; }
        public int EnemyId { get; }

        public override bool Equals(object obj)
        {
            return obj is HazardContactKey other &&
                   SourceEntityId == other.SourceEntityId &&
                   SourceCardInstanceId ==
                   other.SourceCardInstanceId &&
                   EnemyId == other.EnemyId;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SourceEntityId;
                hash = (hash * 397) ^ SourceCardInstanceId;
                hash = (hash * 397) ^ EnemyId;
                return hash;
            }
        }
    }

    /// <summary>
    /// Immutable traversal metadata shared by every CardExecute event in one
    /// program pass. High-tier grammar cards compose by creating a new pass
    /// instead of mutating the tower's frozen loadout.
    /// </summary>
    internal readonly struct ProgramExecutionSpec
    {
        public ProgramExecutionSpec(
            int direction,
            int powerBps,
            int repeatIndex,
            EffectExecutionFlags flags)
        {
            Direction = direction < 0 ? -1 : 1;
            PowerBps = Math.Max(1, Math.Min(10000, powerBps));
            RepeatIndex = Math.Max(0, repeatIndex);
            Flags = flags;
        }

        /// <summary>+1은 왼쪽→오른쪽, -1은 오른쪽→왼쪽 순회다.</summary>
        public int Direction { get; }

        /// <summary>반복 전달 시 효과 수치에 곱할 basis-point 위력이다.</summary>
        public int PowerBps { get; }

        /// <summary>동일 규칙 안에서 몇 번째 반복 패스인지 나타낸다.</summary>
        public int RepeatIndex { get; }

        /// <summary>재진입 억제·단일 카드 실행 등 패스 문법 표식이다.</summary>
        public EffectExecutionFlags Flags { get; }

        public bool HasFlag(EffectExecutionFlags flag)
        {
            return (Flags & flag) != 0;
        }

        public ProgramExecutionSpec WithFlags(
            EffectExecutionFlags flags)
        {
            return new ProgramExecutionSpec(
                Direction,
                PowerBps,
                RepeatIndex,
                Flags | flags);
        }

        public ProgramExecutionSpec WithPowerBps(
            int powerBps)
        {
            return new ProgramExecutionSpec(
                Direction,
                powerBps,
                RepeatIndex,
                Flags);
        }

        public static ProgramExecutionSpec Forward =>
            new ProgramExecutionSpec(
                1,
                10000,
                0,
                EffectExecutionFlags.None);
    }

    /// <summary>
    /// 카드 배열에서 "어느 대상에게 몇 번째 카드를 실행 중인가"를 저장하는 작업 프레임이다.
    /// </summary>
    /// <remarks>
    /// C# 호출 스택으로 다음 카드를 즉시 재귀 호출하지 않고, 이 작은 값을 이벤트 큐에
    /// 연결한다. 그래서 분열·폭발 연쇄가 커져도 브라우저 호출 스택이 폭주하지 않는다.
    /// </remarks>
    internal readonly struct ProgramFrame
    {
        public ProgramFrame(
            SubjectType subjectType,
            EntityId subjectId,
            TowerId towerId,
            int cardIndex,
            ChainId rootChainId,
            ActivationId activationId,
            EventId parentEventId,
            int depth,
            int reservedContinuationEvents,
            in ProgramExecutionSpec execution)
        {
            SubjectType = subjectType;
            SubjectId = subjectId;
            TowerId = towerId;
            CardIndex = cardIndex;
            RootChainId = rootChainId;
            ActivationId = activationId;
            ParentEventId = parentEventId;
            Depth = depth;
            ReservedContinuationEvents = reservedContinuationEvents;
            Execution = execution;
        }

        /// <summary>탄환 해석과 적 해석 중 어느 프로그램을 사용할지 결정한다.</summary>
        public SubjectType SubjectType { get; }
        /// <summary>현재 카드를 적용할 논리 개체다.</summary>
        public EntityId SubjectId { get; }
        /// <summary>프로그램과 카드 출처를 소유한 타워다.</summary>
        public TowerId TowerId { get; }
        /// <summary>왼쪽부터 실행할 현재 카드의 0 기반 인덱스다.</summary>
        public int CardIndex { get; }
        /// <summary>전체 파생 연쇄가 공유하는 안전 예산 ID다.</summary>
        public ChainId RootChainId { get; }
        /// <summary>한 번의 타워 발동을 구분하는 ID다.</summary>
        public ActivationId ActivationId { get; }
        /// <summary>이 실행을 만든 상위 사건 ID다.</summary>
        public EventId ParentEventId { get; }
        /// <summary>연쇄 재귀 깊이다.</summary>
        public int Depth { get; }
        /// <summary>분열 전에 원자적으로 선예약한 남은 카드 사건 수다.</summary>
        public int ReservedContinuationEvents { get; }

        /// <summary>이 프레임과 모든 continuation이 공유하는 프로그램 순회 명세다.</summary>
        public ProgramExecutionSpec Execution { get; }
    }
}
