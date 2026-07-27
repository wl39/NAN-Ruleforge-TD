using System;

namespace RuleforgeTD.GameLogic.Core
{
    /// <summary>
    /// Data-driven hard ceilings. Defaults match the initial design rules at 30 Hz.
    /// </summary>
    /// <remarks>
    /// 강한 조합 자체는 허용하되 브라우저를 멈추는 진짜 무한 연쇄는 막기 위한 상한 모음이다.
    /// 이 값들은 콘텐츠 JSON에서 컴파일되어 들어올 수 있으며, 시뮬레이션은 효과를 실행하기
    /// 전에 필요한 수량을 예약한다. 따라서 한도에 걸린 묶음 효과가 절반만 적용되는 것을 막는다.
    /// </remarks>
    public sealed class SafetyLimits
    {
        /// <summary>기본 설정에서 게임 시간 1초를 구성하는 고정 시뮬레이션 틱 수다.</summary>
        public const int DefaultTicksPerSecond = 30;

        /// <summary>하나의 루트 행동에서 파생 이벤트가 내려갈 수 있는 최대 깊이다.</summary>
        public int MaxChainDepth { get; }
        /// <summary>하나의 연쇄작용이 생성할 수 있는 이벤트 총량이다.</summary>
        public int MaxEventsPerChain { get; }
        /// <summary>하나의 연쇄작용이 생성할 수 있는 탄환 총량이다.</summary>
        public int MaxProjectileSpawnsPerChain { get; }
        /// <summary>
        /// 이전 콘텐츠 해시와 리플레이 호환성을 위해 보존하는 분열 횟수 힌트다.
        /// 분열 자체는 체력 하한과 <see cref="MaxEnemiesPerLineage"/>로 종료한다.
        /// </summary>
        public int MaxEnemySplitsPerLineage { get; }
        /// <summary>원본을 포함해 한 적 가계가 누적 생성할 수 있는 개체 수다.</summary>
        public int MaxEnemiesPerLineage { get; }
        /// <summary>탄환 하나가 도탄할 수 있는 최대 횟수다.</summary>
        public int MaxRicochetsPerProjectile { get; }
        /// <summary>탄환 하나가 관통할 수 있는 최대 횟수다.</summary>
        public int MaxPiercesPerProjectile { get; }
        /// <summary>탄환이 살아 있을 수 있는 최대 게임 시간이며 단위는 틱이다.</summary>
        public int MaxProjectileLifetimeTicks { get; }
        /// <summary>모든 연쇄를 합쳐 한 틱에 처리할 수 있는 이벤트 수다.</summary>
        public int MaxEventsPerTick { get; }
        /// <summary>이벤트 큐가 동시에 보유할 수 있는 최대 작업 수다.</summary>
        public int MaxQueuedEvents { get; }
        /// <summary>한 연쇄작용 안에서 카드가 실행될 수 있는 총 횟수다.</summary>
        public int MaxCardTriggersPerChain { get; }
        /// <summary>재귀 카드가 한 연쇄에서 추가 패스를 만들 수 있는 횟수다.</summary>
        public int MaxRecursionsPerChain { get; }
        /// <summary>우로보로스 등 신화 반복이 한 연쇄에서 허용되는 횟수다.</summary>
        public int MaxMythicRepeatsPerChain { get; }
        /// <summary>불길·독안개처럼 월드에 동시에 존재할 수 있는 위험 지대 수다.</summary>
        public int MaxActiveHazards { get; }
        /// <summary>최근 안전장치 기록을 보관하는 진단 원형 버퍼의 크기다.</summary>
        public int DiagnosticCapacity { get; }

        /// <summary>
        /// 모든 안전 상한을 명시하여 불변 설정을 만든다. 각 값은 1 이상이어야 한다.
        /// </summary>
        public SafetyLimits(
            int maxChainDepth,
            int maxEventsPerChain,
            int maxProjectileSpawnsPerChain,
            int maxEnemySplitsPerLineage,
            int maxEnemiesPerLineage,
            int maxRicochetsPerProjectile,
            int maxPiercesPerProjectile,
            int maxProjectileLifetimeTicks,
            int maxEventsPerTick,
            int maxQueuedEvents,
            int maxCardTriggersPerChain,
            int maxRecursionsPerChain,
            int maxMythicRepeatsPerChain,
            int maxActiveHazards,
            int diagnosticCapacity)
        {
            MaxChainDepth = RequirePositive(maxChainDepth, nameof(maxChainDepth));
            MaxEventsPerChain = RequirePositive(maxEventsPerChain, nameof(maxEventsPerChain));
            MaxProjectileSpawnsPerChain = RequirePositive(
                maxProjectileSpawnsPerChain,
                nameof(maxProjectileSpawnsPerChain));
            MaxEnemySplitsPerLineage = RequirePositive(
                maxEnemySplitsPerLineage,
                nameof(maxEnemySplitsPerLineage));
            MaxEnemiesPerLineage = RequirePositive(
                maxEnemiesPerLineage,
                nameof(maxEnemiesPerLineage));
            MaxRicochetsPerProjectile = RequirePositive(
                maxRicochetsPerProjectile,
                nameof(maxRicochetsPerProjectile));
            MaxPiercesPerProjectile = RequirePositive(
                maxPiercesPerProjectile,
                nameof(maxPiercesPerProjectile));
            MaxProjectileLifetimeTicks = RequirePositive(
                maxProjectileLifetimeTicks,
                nameof(maxProjectileLifetimeTicks));
            MaxEventsPerTick = RequirePositive(maxEventsPerTick, nameof(maxEventsPerTick));
            MaxQueuedEvents = RequirePositive(maxQueuedEvents, nameof(maxQueuedEvents));
            MaxCardTriggersPerChain = RequirePositive(
                maxCardTriggersPerChain,
                nameof(maxCardTriggersPerChain));
            MaxRecursionsPerChain = RequirePositive(
                maxRecursionsPerChain,
                nameof(maxRecursionsPerChain));
            MaxMythicRepeatsPerChain = RequirePositive(
                maxMythicRepeatsPerChain,
                nameof(maxMythicRepeatsPerChain));
            MaxActiveHazards = RequirePositive(
                maxActiveHazards,
                nameof(maxActiveHazards));
            DiagnosticCapacity = RequirePositive(diagnosticCapacity, nameof(diagnosticCapacity));

            if (MaxQueuedEvents < MaxEventsPerTick)
            {
                // 한 틱 예산보다 큐 자체가 더 작으면 “틱 예산 안인데 큐가 먼저 찬다”는
                // 서로 모순된 설정이 된다. 콘텐츠 로딩 단계에서 이를 즉시 거절한다.
                throw new ArgumentException(
                    "Queued event capacity cannot be lower than the per-tick event limit.",
                    nameof(maxQueuedEvents));
            }
        }

        /// <summary>
        /// Phase 1 설계 기준의 30Hz 기본 안전 설정을 만든다.
        /// </summary>
        public static SafetyLimits CreateDefault()
        {
            return new SafetyLimits(
                maxChainDepth: 8,
                maxEventsPerChain: 256,
                maxProjectileSpawnsPerChain: 64,
                maxEnemySplitsPerLineage: 255,
                maxEnemiesPerLineage: 256,
                maxRicochetsPerProjectile: 8,
                maxPiercesPerProjectile: 12,
                maxProjectileLifetimeTicks: 15 * DefaultTicksPerSecond,
                maxEventsPerTick: 4_096,
                maxQueuedEvents: 16_384,
                maxCardTriggersPerChain: 32,
                maxRecursionsPerChain: 1,
                maxMythicRepeatsPerChain: 3,
                maxActiveHazards: 2_048,
                diagnosticCapacity: 256);
        }

        private static int RequirePositive(int value, string parameterName)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Limit must be positive.");
            }

            return value;
        }
    }

    /// <summary>
    /// 연쇄 예산에서 한 번에 확보하려는 작업량을 나타내는 불변 요청값이다.
    /// </summary>
    /// <remarks>
    /// 이벤트, 탄환 생성, 카드 실행을 하나의 묶음으로 요청하면 <see cref="ChainBudget"/>이
    /// 전부 가능한 경우에만 사용량을 늘린다. 이것이 “원자적 예약”이다.
    /// </remarks>
    public readonly struct ChainReservation
    {
        /// <summary>예약할 이벤트들이 위치하는 연쇄 깊이다.</summary>
        public int Depth { get; }
        /// <summary>추가로 사용할 이벤트 수다.</summary>
        public int EventCount { get; }
        /// <summary>추가로 생성할 탄환 수다.</summary>
        public int ProjectileSpawnCount { get; }
        /// <summary>추가로 실행할 카드 수다.</summary>
        public int CardTriggerCount { get; }

        /// <summary>여러 종류의 연쇄 사용량을 한 요청으로 묶는다.</summary>
        public ChainReservation(
            int depth,
            int eventCount,
            int projectileSpawnCount = 0,
            int cardTriggerCount = 0)
        {
            Depth = depth;
            EventCount = eventCount;
            ProjectileSpawnCount = projectileSpawnCount;
            CardTriggerCount = cardTriggerCount;
        }

        /// <summary>지정한 깊이에서 이벤트 하나만 예약하는 편의 요청을 만든다.</summary>
        public static ChainReservation Event(int depth)
        {
            return new ChainReservation(depth, 1);
        }
    }

    /// <summary>
    /// Mutable budget for one root action. Composite reservations are atomic.
    /// </summary>
    /// <remarks>
    /// 타워 발사 한 번, 적 사망 한 번처럼 하나의 출발점에서 생긴 모든 파생 효과가 공유한다.
    /// 한도를 넘으면 해당 예약은 사용량을 전혀 바꾸지 않고 실패하므로 효과의 일부 실행을 막는다.
    /// </remarks>
    public sealed class ChainBudget
    {
        private readonly SafetyLimits _limits;

        /// <summary>이 예산이 추적하는 최상위 연쇄 ID다.</summary>
        public ChainId RootChainId { get; }
        /// <summary>현재까지 예약한 이벤트 수다.</summary>
        public int EventsUsed { get; private set; }
        /// <summary>현재까지 예약한 탄환 생성 수다.</summary>
        public int ProjectileSpawnsUsed { get; private set; }
        /// <summary>현재까지 예약한 카드 실행 수다.</summary>
        public int CardTriggersUsed { get; private set; }
        /// <summary>현재까지 사용한 재귀 패스 수다.</summary>
        public int RecursionsUsed { get; private set; }
        /// <summary>현재까지 사용한 신화 반복 패스 수다.</summary>
        public int MythicRepeatsUsed { get; private set; }

        /// <summary>루트 연쇄 ID와 공통 안전 상한을 연결한 빈 사용 예산을 만든다.</summary>
        public ChainBudget(ChainId rootChainId, SafetyLimits limits)
        {
            if (limits == null)
            {
                throw new ArgumentNullException(nameof(limits));
            }

            RootChainId = rootChainId;
            _limits = limits;
        }

        /// <summary>
        /// 묶음 요청의 모든 항목을 검사하고, 전부 들어갈 때만 사용량을 한꺼번에 증가시킨다.
        /// </summary>
        public bool TryReserve(in ChainReservation reservation, out BudgetFailure failure)
        {
            if (reservation.Depth < 0 ||
                reservation.EventCount < 0 ||
                reservation.ProjectileSpawnCount < 0 ||
                reservation.CardTriggerCount < 0)
            {
                failure = BudgetFailure.InvalidRequest;
                return false;
            }

            if (reservation.Depth > _limits.MaxChainDepth)
            {
                failure = BudgetFailure.ChainDepthLimit;
                return false;
            }

            if (!Fits(EventsUsed, reservation.EventCount, _limits.MaxEventsPerChain))
            {
                failure = BudgetFailure.ChainEventLimit;
                return false;
            }

            if (!Fits(
                    ProjectileSpawnsUsed,
                    reservation.ProjectileSpawnCount,
                    _limits.MaxProjectileSpawnsPerChain))
            {
                failure = BudgetFailure.ProjectileSpawnLimit;
                return false;
            }

            if (!Fits(
                    CardTriggersUsed,
                    reservation.CardTriggerCount,
                    _limits.MaxCardTriggersPerChain))
            {
                failure = BudgetFailure.CardTriggerLimit;
                return false;
            }

            // 모든 검사가 끝난 뒤에만 값을 바꾼다. 이 순서 덕분에 앞 항목만 차감되고
            // 뒤 항목에서 실패하는 부분 예약이 발생하지 않는다.
            EventsUsed += reservation.EventCount;
            ProjectileSpawnsUsed += reservation.ProjectileSpawnCount;
            CardTriggersUsed += reservation.CardTriggerCount;
            failure = BudgetFailure.None;
            return true;
        }

        /// <summary>지정한 깊이의 이벤트 하나를 예약한다.</summary>
        public bool TryReserveEvent(int depth, out BudgetFailure failure)
        {
            ChainReservation reservation = ChainReservation.Event(depth);
            return TryReserve(in reservation, out failure);
        }

        /// <summary>재귀 카드의 추가 실행 패스 하나를 예약한다.</summary>
        public bool TryReserveRecursion(out BudgetFailure failure)
        {
            if (!Fits(RecursionsUsed, 1, _limits.MaxRecursionsPerChain))
            {
                failure = BudgetFailure.RecursionLimit;
                return false;
            }

            RecursionsUsed++;
            failure = BudgetFailure.None;
            return true;
        }

        /// <summary>신화 카드의 전체 프로그램 반복 패스 하나를 예약한다.</summary>
        public bool TryReserveMythicRepeat(out BudgetFailure failure)
        {
            if (!Fits(MythicRepeatsUsed, 1, _limits.MaxMythicRepeatsPerChain))
            {
                failure = BudgetFailure.MythicRepeatLimit;
                return false;
            }

            MythicRepeatsUsed++;
            failure = BudgetFailure.None;
            return true;
        }

        private static bool Fits(int used, int requested, int limit)
        {
            return requested <= limit - used;
        }
    }

    /// <summary>
    /// Creation budget shared by all descendants of one enemy lineage.
    /// Split operations and total created entities have independent caps.
    /// </summary>
    /// <remarks>
    /// 적 분열은 한 개체가 사라져도 같은 혈통의 자식들이 계속 한 예산을 공유한다.
    /// “현재 화면에 몇 마리인가”가 아니라 런 중 이 가계가 누적 몇 개를 만들었는지 세므로,
    /// 자식을 죽였다가 다시 분열해 상한을 우회할 수 없다.
    /// </remarks>
    public sealed class LineageBudget
    {
        private readonly SafetyLimits _limits;

        /// <summary>원본과 모든 분열 자식을 묶는 가계 ID다.</summary>
        public LineageId LineageId { get; }
        /// <summary>지금까지 만들어진 자손 중 가장 높은 세대 번호다.</summary>
        public int HighestGeneration { get; private set; }
        /// <summary>이 가계가 사용한 분열 발동 횟수다.</summary>
        public int SplitsUsed { get; private set; }
        /// <summary>죽거나 제거된 개체까지 포함해 이 가계가 누적 생성한 수다.</summary>
        public int EntitiesCreated { get; private set; }

        /// <summary>가계 ID와 최초 개체 수로 분열 예산을 만든다.</summary>
        public LineageBudget(LineageId lineageId, SafetyLimits limits, int initialEntityCount = 1)
        {
            if (limits == null)
            {
                throw new ArgumentNullException(nameof(limits));
            }

            if (initialEntityCount <= 0 ||
                initialEntityCount > limits.MaxEnemiesPerLineage)
            {
                throw new ArgumentOutOfRangeException(nameof(initialEntityCount));
            }

            LineageId = lineageId;
            _limits = limits;
            EntitiesCreated = initialEntityCount;
        }

        /// <summary>
        /// 다음 세대 분열 한 번과 추가 개체 수를 함께 예약한다.
        /// 분열 횟수는 통계로만 기록하며, 누적 개체 수 상한을 만족해야 성공한다.
        /// </summary>
        public bool TryReserveSplit(
            int resultingGeneration,
            int additionalEntities,
            out BudgetFailure failure)
        {
            if (resultingGeneration <= 0 || additionalEntities <= 0)
            {
                failure = BudgetFailure.InvalidRequest;
                return false;
            }

            if (!Fits(EntitiesCreated, additionalEntities, _limits.MaxEnemiesPerLineage))
            {
                failure = BudgetFailure.EnemyLineageEntityLimit;
                return false;
            }

            EntitiesCreated += additionalEntities;
            SplitsUsed++;
            if (resultingGeneration > HighestGeneration)
            {
                HighestGeneration = resultingGeneration;
            }

            failure = BudgetFailure.None;
            return true;
        }

        private static bool Fits(int used, int requested, int limit)
        {
            return requested <= limit - used;
        }
    }

    /// <summary>
    /// 모든 연쇄가 공유하는 “현재 한 틱”의 이벤트 처리량 예산이다.
    /// </summary>
    /// <remarks>
    /// 개별 카드 연쇄가 각각 안전하더라도 수백 개가 같은 틱에 겹치면 브라우저가 멈출 수 있다.
    /// 이 예산은 그 총합을 제한하며, 새 틱이 시작될 때 사용량만 0으로 되돌린다.
    /// </remarks>
    public sealed class TickEventBudget
    {
        private readonly int _limit;

        /// <summary>현재 예산이 적용되는 시뮬레이션 틱이다. Begin 전에는 -1이다.</summary>
        public long Tick { get; private set; }
        /// <summary>현재 틱에서 예약된 이벤트 수다.</summary>
        public int EventsUsed { get; private set; }

        /// <summary>한 틱에 허용할 이벤트 수를 정해 예산을 만든다.</summary>
        public TickEventBudget(int limit)
        {
            if (limit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limit));
            }

            _limit = limit;
            Tick = -1L;
        }

        /// <summary>새 틱의 예산 계산을 시작하고 사용량을 0으로 초기화한다.</summary>
        public void Begin(long tick)
        {
            if (tick < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }

            Tick = tick;
            EventsUsed = 0;
        }

        /// <summary>
        /// 현재 틱에서 이벤트 <paramref name="count"/>개를 원자적으로 예약한다.
        /// </summary>
        public bool TryReserve(int count, out BudgetFailure failure)
        {
            if (count < 0)
            {
                failure = BudgetFailure.InvalidRequest;
                return false;
            }

            if (count > _limit - EventsUsed)
            {
                failure = BudgetFailure.TickEventLimit;
                return false;
            }

            EventsUsed += count;
            failure = BudgetFailure.None;
            return true;
        }
    }
}
