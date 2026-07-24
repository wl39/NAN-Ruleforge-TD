using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Simulation
{
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
    /// 카드가 탄환이나 적에게 "나중에 실행할 효과"를 붙일 때의 발동 시점이다.
    /// </summary>
    internal enum BindingTrigger
    {
        /// <summary>관통을 포함한 모든 유효 적중마다 발동한다.</summary>
        OnHit = 0,
        /// <summary>첫 적중과 소멸 중 먼저 일어난 사건에서 한 번만 발동한다.</summary>
        OnFirstHitOrExpire = 1,
        /// <summary>적 사망이 최종 확정된 뒤 발동한다.</summary>
        OnDeath = 2,
        /// <summary>탄환의 최초 유효 적중에서만 발동한다.</summary>
        OnFirstHit = 3
    }

    /// <summary>
    /// 지연 실행 바인딩이 실제로 수행할 Phase 1 효과 종류다.
    /// </summary>
    internal enum BindingKind
    {
        Burn = 0,
        Poison = 1,
        Explosion = 2,
        Knockback = 3,
        Mark = 4,
        Gold = 5,
        Stun = 6
    }

    /// <summary>
    /// 가계 보상을 증가시키는 서로 다른 원인을 중복 방지 원장에서 구분한다.
    /// </summary>
    internal enum RewardAugmentKind
    {
        GoldBounty = 0,
        Enlarge = 1
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

        // 기본 공격 또는 주기 발동까지 남은 고정 틱.
        public int CooldownRemaining;

        // 계획 단계에서 편집하는 슬롯 상태.
        public int[] CardInstanceIds;

        // 웨이브 시작 시 위 슬롯을 복사한 불변 실행 프로그램과 카드 인스턴스 배열.
        public CardId[] Program;
        public int[] ProgramInstances;

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

        // 이동의 원본은 경로 진행 거리다. Position은 공간 검색용 환산 좌표다.
        public long PathProgressMilli;
        public SimPosition Position;

        // 체력·방어와 기본 이동 능력.
        public long HealthMilli;
        public long MaxHealthMilli;
        public int Armor;
        public int BaseSpeedMilliPerTick;

        // 카드가 적용한 배율. 10,000 basis point가 100%다.
        public int SpeedMultiplierBps = 10000;
        public int SizeMultiplierBps = 10000;
        public int AreaDamageTakenBps = 10000;
        public int SingleDamageTakenBps = 10000;

        // 이 분열 가지에 현재 배정된 골드와 웨이브 기여도.
        public int RewardBudget;
        public int WaveProgressBudget;

        // 사망 요청과 실제 제거 사이의 중복 처리를 막는 생명주기 플래그.
        public bool Alive = true;
        public bool RewardClaimed;
        public bool DeathQueued;

        // 정예·보스의 강한 제어 효과를 완전 면역 대신 누적하는 게이지.
        public int ControlGauge;
        public int ControlThreshold;
        public int ControlThresholdStep;

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

        // 동일 카드 인스턴스가 여러 분열 가지에서 보상을 중복 증가시키는 것을 막는다.
        public readonly HashSet<RewardAugmentKey> AppliedRewardAugments =
            new HashSet<RewardAugmentKey>();
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
        // 영역 정체성, 종류, 공간과 남은 수명.
        public int Id;
        public BindingKind Kind;
        public SimPosition Position;
        public int RadiusMilli;
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
            int reservedContinuationEvents)
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
    }
}
