using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 게임 규칙이 확정한 사실 중 화면·사운드가 표현할 만한 사건의 종류다.
    /// </summary>
    /// <remarks>
    /// 이 이벤트는 전투 판정용 <c>GameEvent</c>와 다르다. 표현 이벤트를 읽지
    /// 않거나 늦게 읽어도 피해, 이동, 보상 결과는 절대 바뀌지 않는다.
    /// </remarks>
    public enum PresentationEventType
    {
        /// <summary>새 적 논리 개체가 생성되었다.</summary>
        EnemySpawned = 0,
        /// <summary>적의 경로상 위치가 변했다.</summary>
        EnemyMoved = 1,
        /// <summary>적 체력에 실제 피해가 적용되었다.</summary>
        EnemyDamaged = 2,
        /// <summary>적의 사망이 최종 확정되었다.</summary>
        EnemyDied = 3,
        /// <summary>적이 경로 끝에 도달해 본진 피해를 주었다.</summary>
        EnemyLeaked = 4,
        /// <summary>새 탄환 논리 개체가 생성되었다.</summary>
        ProjectileSpawned = 5,
        /// <summary>탄환 위치가 한 틱만큼 이동했다.</summary>
        ProjectileMoved = 6,
        /// <summary>탄환이 적과 충돌했다.</summary>
        ProjectileHit = 7,
        /// <summary>탄환 수명이 끝나거나 소멸 조건을 만났다.</summary>
        ProjectileExpired = 8,
        /// <summary>상태이상이 신규 적용되거나 갱신되었다.</summary>
        StatusApplied = 9,
        /// <summary>상태이상이 만료·정리되었다.</summary>
        StatusRemoved = 10,
        /// <summary>계획 단계에서 타워가 건설 지점에 배치되었다.</summary>
        TowerPlaced = 11,
        /// <summary>타워 프로그램의 카드 한 장이 실행되었다.</summary>
        CardExecuted = 12,
        /// <summary>출처가 명시된 골드 보상이 지급되었다.</summary>
        RewardGranted = 13,
        /// <summary>웨이브 전투가 시작되었다.</summary>
        WaveStarted = 14,
        /// <summary>웨이브의 모든 가계가 정산되었다.</summary>
        WaveCompleted = 15,
        /// <summary>새 카드 드래프트 후보가 생성되었다.</summary>
        DraftGenerated = 16,
        /// <summary>마지막 웨이브 완료로 런이 승리했다.</summary>
        RunWon = 17,
        /// <summary>본진 체력 고갈로 런이 패배했다.</summary>
        RunLost = 18,
        /// <summary>무한 연쇄 방지 예산 때문에 효과 일부가 거절되었다.</summary>
        SafetyLimitReached = 19,
        ShimmeringCarrierSpawned = 20,
        CardPackDropped = 21,
        CardPackLost = 22,
        CardPackOpened = 23,
        BossAbilityTelegraphed = 24,
        BossAbilityActivated = 25,
        BossPhaseChanged = 26
    }

    /// <summary>
    /// 한 개의 표현 사건을 담는 엔진 독립 값이다.
    /// </summary>
    /// <remarks>
    /// Unity 앞단은 Type을 보고 이펙트나 애니메이션을 재생한다. SubjectId와
    /// SourceId의 의미, Value 단위, ContentId 사용 여부는 Type별로 달라진다.
    /// 화면 표현에 충분한 최소 데이터만 담고 실제 상태 원본은 Snapshot에서 읽는다.
    /// </remarks>
    public readonly struct SimulationPresentationEvent
    {
        public SimulationPresentationEvent(
            long tick,
            PresentationEventType type,
            int subjectId,
            int sourceId,
            int value,
            string contentId)
        {
            Tick = tick;
            Type = type;
            SubjectId = subjectId;
            SourceId = sourceId;
            Value = value;
            ContentId = contentId ?? string.Empty;
        }

        /// <summary>사건이 확정된 0 기반 시뮬레이션 틱이다.</summary>
        public long Tick { get; }

        /// <summary>화면에서 어떤 연출을 선택할지 나타낸다.</summary>
        public PresentationEventType Type { get; }

        /// <summary>주 대상 개체 ID다. 대상이 없을 때는 관례적으로 -1을 쓴다.</summary>
        public int SubjectId { get; }

        /// <summary>타워·탄환 등 사건을 만든 원인 개체 ID다.</summary>
        public int SourceId { get; }

        /// <summary>피해량, 이동량, 보상량 등 사건별 대표 정수 값이다.</summary>
        public int Value { get; }

        /// <summary>카드·적·진단 코드 등 선택적 안정 문자열 ID다.</summary>
        public string ContentId { get; }
    }

    /// <summary>
    /// 마지막 읽기 이후 쌓인 표현 이벤트의 읽기 전용 묶음이다.
    /// </summary>
    /// <remarks>
    /// 배열 자체를 외부에 노출하지 않고 개수와 인덱서만 제공한다. 이 버퍼를
    /// 소비하면 다음 ReadPresentationEvents 호출은 이후 사건만 반환한다.
    /// </remarks>
    public sealed class SimulationEventBuffer
    {
        private readonly SimulationPresentationEvent[] events;

        internal SimulationEventBuffer(SimulationPresentationEvent[] events)
        {
            this.events = events ?? new SimulationPresentationEvent[0];
        }

        /// <summary>이번 묶음에 들어 있는 사건 수다.</summary>
        public int Count => events.Length;

        /// <summary>발생 순서대로 사건을 읽는다.</summary>
        public SimulationPresentationEvent this[int index] => events[index];
    }

    /// <summary>
    /// 특정 틱에 외부에서 읽을 수 있는 적 한 개체의 상태 복사본이다.
    /// </summary>
    /// <remarks>
    /// 적은 Transform 좌표가 아니라 PathProgressMilli를 원본 위치로 사용한다.
    /// Position은 그 진행도를 경로 위 월드 좌표로 환산한 값이다.
    /// </remarks>
    public readonly struct EnemySnapshot
    {
        public EnemySnapshot(
            int id,
            string definitionId,
            int lineageId,
            long pathProgressMilli,
            SimPosition position,
            long healthMilli,
            long maxHealthMilli,
            int armor,
            int slowBps,
            int speedMultiplierBps,
            int sizeMultiplierBps,
            int controlGauge,
            int controlThreshold,
            int rewardBudget,
            int waveProgressBudget,
            int cardPackProgressBudget,
            int generation,
            bool alive,
            bool isShimmering,
            long shieldMilli,
            StatusType[] statuses,
            StatusSnapshot[] statusDetails,
            int deathBindingCount)
        {
            Id = id;
            DefinitionId = definitionId;
            LineageId = lineageId;
            PathProgressMilli = pathProgressMilli;
            Position = position;
            HealthMilli = healthMilli;
            MaxHealthMilli = maxHealthMilli;
            Armor = armor;
            SlowBps = slowBps;
            SpeedMultiplierBps = speedMultiplierBps;
            SizeMultiplierBps = sizeMultiplierBps;
            ControlGauge = controlGauge;
            ControlThreshold = controlThreshold;
            RewardBudget = rewardBudget;
            WaveProgressBudget = waveProgressBudget;
            CardPackProgressBudget = cardPackProgressBudget;
            Generation = generation;
            Alive = alive;
            IsShimmering = isShimmering;
            ShieldMilli = shieldMilli;
            Statuses = statuses;
            StatusDetails = statusDetails;
            DeathBindingCount = deathBindingCount;
        }

        /// <summary>런 안에서 재사용되지 않는 적 개체 ID다.</summary>
        public int Id { get; }
        /// <summary>raider 같은 적 데이터 안정 ID다.</summary>
        public string DefinitionId { get; }
        /// <summary>원본 스폰과 모든 분열체를 묶는 가계 ID다.</summary>
        public int LineageId { get; }
        /// <summary>경로 시작점부터 이동한 거리이며 1,000이 논리 거리 1이다.</summary>
        public long PathProgressMilli { get; }
        /// <summary>경로 진행도를 2차원 논리 좌표로 환산한 위치다.</summary>
        public SimPosition Position { get; }
        /// <summary>현재 체력의 milli 단위 값이다. 1,000이 체력 1이다.</summary>
        public long HealthMilli { get; }
        /// <summary>현재 최대 체력의 milli 단위 값이다.</summary>
        public long MaxHealthMilli { get; }
        /// <summary>물리 피해 감소에 사용하는 방어력 정수다.</summary>
        public int Armor { get; }
        /// <summary>모든 둔화를 합산한 최종 감소량의 basis point 값이다.</summary>
        public int SlowBps { get; }
        /// <summary>카드가 바꾼 기본 이동 속도 배율이다. 10,000이 100%다.</summary>
        public int SpeedMultiplierBps { get; }
        /// <summary>크기와 피격 반경에 적용되는 배율이다. 10,000이 100%다.</summary>
        public int SizeMultiplierBps { get; }
        /// <summary>정예·보스가 강한 제어를 받은 누적 게이지다.</summary>
        public int ControlGauge { get; }
        /// <summary>짧은 행동 방해가 발동하는 현재 게이지 임계치다.</summary>
        public int ControlThreshold { get; }
        /// <summary>이 가지가 아직 보유한 처치 골드 할당량이다.</summary>
        public int RewardBudget { get; }
        /// <summary>이 가지가 아직 보유한 웨이브 정산 기여도다.</summary>
        public int WaveProgressBudget { get; }
        public int CardPackProgressBudget { get; }
        /// <summary>원본은 0이며 분열될 때마다 증가하는 세대 깊이다.</summary>
        public int Generation { get; }
        /// <summary>현재 전투 대상으로 유효한 살아 있는 개체인지 나타낸다.</summary>
        public bool Alive { get; }
        public bool IsShimmering { get; }
        public long ShieldMilli { get; }
        /// <summary>UI 아이콘처럼 종류만 빠르게 그릴 때 쓰는 상태 목록이다.</summary>
        public StatusType[] Statuses { get; }
        /// <summary>출처·중첩·남은 시간까지 포함한 상태 상세 복사본이다.</summary>
        public StatusSnapshot[] StatusDetails { get; }
        /// <summary>사망 시 실행하도록 이 적에 예약된 카드 바인딩 수다.</summary>
        public int DeathBindingCount { get; }
    }

    /// <summary>적에게 붙은 상태이상 인스턴스 하나의 외부 읽기 모델이다.</summary>
    public readonly struct StatusSnapshot
    {
        public StatusSnapshot(
            int instanceId,
            StatusType type,
            int sourceEntityId,
            int sourceTowerId,
            CardId sourceCardId,
            int stacks,
            int intensity,
            int remainingTicks,
            int maxStacks,
            int tickInterval,
            int armorIgnoreBps)
        {
            InstanceId = instanceId;
            Type = type;
            SourceEntityId = sourceEntityId;
            SourceTowerId = sourceTowerId;
            SourceCardId = sourceCardId;
            Stacks = stacks;
            Intensity = intensity;
            RemainingTicks = remainingTicks;
            MaxStacks = maxStacks;
            TickInterval = tickInterval;
            ArmorIgnoreBps = armorIgnoreBps;
        }

        /// <summary>같은 런 안에서 상태 인스턴스를 구별하는 ID다.</summary>
        public int InstanceId { get; }
        /// <summary>화상, 중독, 둔화 같은 상태 종류다.</summary>
        public StatusType Type { get; }
        /// <summary>마지막으로 이 상태를 직접 적용한 적·탄환 개체 ID다.</summary>
        public int SourceEntityId { get; }
        /// <summary>피해와 보상이 귀속되는 타워 인스턴스 ID다.</summary>
        public int SourceTowerId { get; }
        /// <summary>상태를 만든 카드 정의 ID다.</summary>
        public CardId SourceCardId { get; }
        /// <summary>현재 중첩 수다.</summary>
        public int Stacks { get; }
        /// <summary>중첩당 피해나 둔화율처럼 상태 고유 강도다.</summary>
        public int Intensity { get; }
        /// <summary>만료까지 남은 고정 틱 수다.</summary>
        public int RemainingTicks { get; }
        /// <summary>이 인스턴스가 허용하는 최대 중첩이다.</summary>
        public int MaxStacks { get; }
        /// <summary>지속 피해가 반복되는 틱 간격이며 0이면 주기 효과가 없다.</summary>
        public int TickInterval { get; }
        /// <summary>이 상태의 피해가 무시하는 방어력 비율이다.</summary>
        public int ArmorIgnoreBps { get; }
    }

    /// <summary>탄환 논리 개체 하나의 현재 상태 복사본이다.</summary>
    public readonly struct ProjectileSnapshot
    {
        public ProjectileSnapshot(
            int id,
            int sourceTowerId,
            SimPosition position,
            long damageMilli,
            int remainingLifetimeTicks,
            int radiusMilli,
            int pierceRemaining,
            int piercesUsed,
            int directionXBps,
            int directionYBps,
            bool homing,
            int bindingCount)
        {
            Id = id;
            SourceTowerId = sourceTowerId;
            Position = position;
            DamageMilli = damageMilli;
            RemainingLifetimeTicks = remainingLifetimeTicks;
            RadiusMilli = radiusMilli;
            PierceRemaining = pierceRemaining;
            PiercesUsed = piercesUsed;
            DirectionXBps = directionXBps;
            DirectionYBps = directionYBps;
            Homing = homing;
            BindingCount = bindingCount;
        }

        /// <summary>런 안에서 재사용되지 않는 탄환 개체 ID다.</summary>
        public int Id { get; }
        /// <summary>이 탄환과 피해를 만든 타워 인스턴스 ID다.</summary>
        public int SourceTowerId { get; }
        /// <summary>현재 논리 좌표다.</summary>
        public SimPosition Position { get; }
        /// <summary>다음 직접 적중에 사용할 milli 피해량이다.</summary>
        public long DamageMilli { get; }
        /// <summary>자동 소멸까지 남은 고정 틱 수다.</summary>
        public int RemainingLifetimeTicks { get; }
        /// <summary>충돌 원의 반경이며 milli 거리 단위다.</summary>
        public int RadiusMilli { get; }
        /// <summary>카드가 추가한 사용 가능한 관통 횟수다.</summary>
        public int PierceRemaining { get; }
        /// <summary>천공 강제 관통까지 포함해 이미 사용한 총 관통 횟수다.</summary>
        public int PiercesUsed { get; }
        /// <summary>X축 진행 방향이다. 10,000이 한 축의 완전한 양의 방향이다.</summary>
        public int DirectionXBps { get; }
        /// <summary>Y축 진행 방향이다. 10,000이 한 축의 완전한 양의 방향이다.</summary>
        public int DirectionYBps { get; }
        /// <summary>매 틱 대상을 다시 추적하는 탄환인지 나타낸다.</summary>
        public bool Homing { get; }
        /// <summary>적중·소멸 시 실행할 효과 바인딩 수다.</summary>
        public int BindingCount { get; }
    }

    /// <summary>
    /// 하나의 원본 적 스폰과 모든 분열 후손이 공유하는 보상 원장 복사본이다.
    /// </summary>
    public readonly struct LineageSnapshot
    {
        public LineageSnapshot(
            int id,
            int highestGeneration,
            int splitCount,
            int spawnedEntityCount,
            int liveMembers,
            int baseRewardBudget,
            int maxRewardBudget,
            int paidReward,
            int forfeitedReward,
            int progressBudget,
            int consumedProgress,
            int baseCardPackProgress,
            int awardedCardPackProgress,
            int forfeitedCardPackProgress,
            bool isShimmering,
            int rewardAugmentCount)
        {
            Id = id;
            HighestGeneration = highestGeneration;
            SplitCount = splitCount;
            SpawnedEntityCount = spawnedEntityCount;
            LiveMembers = liveMembers;
            BaseRewardBudget = baseRewardBudget;
            MaxRewardBudget = maxRewardBudget;
            PaidReward = paidReward;
            ForfeitedReward = forfeitedReward;
            ProgressBudget = progressBudget;
            ConsumedProgress = consumedProgress;
            BaseCardPackProgress = baseCardPackProgress;
            AwardedCardPackProgress = awardedCardPackProgress;
            ForfeitedCardPackProgress = forfeitedCardPackProgress;
            IsShimmering = isShimmering;
            RewardAugmentCount = rewardAugmentCount;
        }

        /// <summary>가계 ID이며 최초 적의 ID를 기준으로 만든다.</summary>
        public int Id { get; }
        /// <summary>이 가계가 도달한 가장 깊은 분열 세대다.</summary>
        public int HighestGeneration { get; }
        /// <summary>가계 전체에서 성공한 분열 연산 횟수다.</summary>
        public int SplitCount { get; }
        /// <summary>사망 개체를 포함해 가계가 만든 누적 개체 수다.</summary>
        public int SpawnedEntityCount { get; }
        /// <summary>아직 살아 있어 정산되지 않은 가지 수다.</summary>
        public int LiveMembers { get; }
        /// <summary>원본 적 데이터가 제공한 최초 골드 예산이다.</summary>
        public int BaseRewardBudget { get; }
        /// <summary>위험 보상 증가까지 반영한 지급 가능 총상한이다.</summary>
        public int MaxRewardBudget { get; }
        /// <summary>이미 플레이어에게 지급한 가계 골드다.</summary>
        public int PaidReward { get; }
        /// <summary>본진 도달 등으로 지급 없이 소멸한 골드다.</summary>
        public int ForfeitedReward { get; }
        /// <summary>원본 가계가 제공하는 전체 웨이브 기여도다.</summary>
        public int ProgressBudget { get; }
        /// <summary>사망·도달로 이미 정산한 웨이브 기여도다.</summary>
        public int ConsumedProgress { get; }
        public int BaseCardPackProgress { get; }
        public int AwardedCardPackProgress { get; }
        public int ForfeitedCardPackProgress { get; }
        public bool IsShimmering { get; }
        /// <summary>중복 방지 원장에 기록된 보상 증액 키 수다.</summary>
        public int RewardAugmentCount { get; }
    }

    /// <summary>플레이어가 소유한 카드 한 장의 인스턴스 상태다.</summary>
    public readonly struct CardInstanceSnapshot
    {
        public CardInstanceSnapshot(
            int id,
            CardId definitionId,
            int level,
            bool equipped,
            int towerId,
            int slot)
        {
            Id = id;
            DefinitionId = definitionId;
            Level = level;
            Equipped = equipped;
            TowerId = towerId;
            Slot = slot;
        }

        /// <summary>동일 카드 여러 장을 구별하는 카드 인스턴스 ID다.</summary>
        public int Id { get; }
        /// <summary>카드 규칙 데이터를 찾기 위한 정의 ID다.</summary>
        public CardId DefinitionId { get; }
        /// <summary>현재 강화 레벨이다. Phase 1은 기본적으로 1을 사용한다.</summary>
        public int Level { get; }
        /// <summary>어떤 타워 슬롯에 장착되어 있는지 나타낸다.</summary>
        public bool Equipped { get; }
        /// <summary>장착 타워 ID이며 미장착이면 유효하지 않은 값이다.</summary>
        public int TowerId { get; }
        /// <summary>왼쪽부터 시작하는 0 기반 슬롯 번호다.</summary>
        public int Slot { get; }
    }

    /// <summary>고정 건설 지점 하나의 위치와 해금 상태를 담는 외부 읽기 모델이다.</summary>
    public readonly struct BuildSpotSnapshot
    {
        public BuildSpotSnapshot(
            int index,
            SimPosition position,
            int unlockCost,
            bool unlocked)
        {
            Index = index;
            Position = position;
            UnlockCost = unlockCost;
            Unlocked = unlocked;
        }

        /// <summary>배치 및 해금 명령에서 사용하는 0 기반 건설 지점 인덱스다.</summary>
        public int Index { get; }

        /// <summary>맵 표현 계층이 사이트를 배치할 논리 좌표다.</summary>
        public SimPosition Position { get; }

        /// <summary>잠금 해제에 필요한 골드다. 0이면 런 시작부터 해금된다.</summary>
        public int UnlockCost { get; }

        /// <summary>현재 타워 배치가 허용되는 지점이면 true다.</summary>
        public bool Unlocked { get; }
    }

    /// <summary>배치된 타워 한 개의 외부 읽기 모델이다.</summary>
    public readonly struct TowerSnapshot
    {
        public TowerSnapshot(
            int id,
            string definitionId,
            int buildPointIndex,
            SimPosition position,
            int[] cardInstanceIds)
        {
            Id = id;
            DefinitionId = definitionId;
            BuildPointIndex = buildPointIndex;
            Position = position;
            CardInstanceIds = cardInstanceIds;
        }

        /// <summary>배치된 타워 인스턴스 ID다.</summary>
        public int Id { get; }
        /// <summary>ballista 같은 타워 데이터 안정 ID다.</summary>
        public string DefinitionId { get; }
        /// <summary>타워가 차지한 고정 건설 지점의 0 기반 인덱스다.</summary>
        public int BuildPointIndex { get; }
        /// <summary>건설 지점에서 얻은 논리 좌표다.</summary>
        public SimPosition Position { get; }
        /// <summary>
        /// 슬롯 순서의 카드 인스턴스 ID 배열이며 빈 슬롯은 음수 값이다.
        /// </summary>
        public int[] CardInstanceIds { get; }
    }

    public readonly struct CardPackSnapshot
    {
        public CardPackSnapshot(
            int id,
            CardPackSource source,
            SimPosition position,
            bool worldDrop)
        {
            Id = id;
            Source = source;
            Position = position;
            WorldDrop = worldDrop;
        }

        public int Id { get; }
        public CardPackSource Source { get; }
        public SimPosition Position { get; }
        public bool WorldDrop { get; }
    }

    /// <summary>
    /// 특정 틱의 전체 게임 상태를 화면 계층에 전달하는 방어적 복사본이다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// UI는 이 객체만으로 맵의 적·탄환·타워·카드와 HUD를 다시 그릴 수 있다.
    /// Unity GameObject는 이 값의 시각적 대리자일 뿐 게임 규칙의 원본이 아니다.
    /// </para>
    /// <para>
    /// 포함된 배열은 호출 때 새로 복사된다. 외부에서 배열 값을 바꾸더라도 내부
    /// 시뮬레이션에는 영향이 없지만, 같은 Snapshot 객체를 공유하는 UI끼리는
    /// 배열을 수정하지 않는 것이 좋다.
    /// </para>
    /// </remarks>
    public sealed class SimulationSnapshot
    {
        internal SimulationSnapshot(
            long tick,
            RunPhase phase,
            int waveIndex,
            int baseHealth,
            int gold,
            EnemySnapshot[] enemies,
            ProjectileSnapshot[] projectiles,
            TowerSnapshot[] towers,
            CardInstanceSnapshot[] cards,
            CardId[] draftOffers,
            CardId[] cardPackOffers,
            LineageSnapshot[] lineages,
            string[] unlockedTowerIds,
            BuildSpotSnapshot[] buildSpots,
            CardPackSnapshot[] cardPacks,
            int cardPackProgress,
            int cardPackProgressBps,
            int nextCardPackThreshold,
            int[] rewardQueueCardPackIds,
            int pendingCardInstanceId,
            int towerConstructionCost)
        {
            Tick = tick;
            Phase = phase;
            WaveIndex = waveIndex;
            BaseHealth = baseHealth;
            Gold = gold;
            Enemies = enemies;
            Projectiles = projectiles;
            Towers = towers;
            Cards = cards;
            DraftOffers = draftOffers;
            CardPackOffers = cardPackOffers;
            Lineages = lineages;
            UnlockedTowerIds = unlockedTowerIds;
            BuildSpots = buildSpots;
            CardPacks = cardPacks;
            CardPackProgress = cardPackProgress;
            CardPackProgressBps = cardPackProgressBps;
            NextCardPackThreshold = nextCardPackThreshold;
            RewardQueueCardPackIds = rewardQueueCardPackIds;
            PendingCardInstanceId = pendingCardInstanceId;
            TowerConstructionCost = towerConstructionCost;
        }

        /// <summary>이 상태를 만든 시뮬레이션 틱이다.</summary>
        public long Tick { get; }
        /// <summary>현재 런 진행 단계다.</summary>
        public RunPhase Phase { get; }
        /// <summary>현재 웨이브의 0 기반 인덱스이며 시작 전에는 음수다.</summary>
        public int WaveIndex { get; }
        /// <summary>남은 본진 체력이다.</summary>
        public int BaseHealth { get; }
        /// <summary>현재 사용 가능한 골드다.</summary>
        public int Gold { get; }
        /// <summary>현재 월드에 남아 있는 적 복사본이다.</summary>
        public EnemySnapshot[] Enemies { get; }
        /// <summary>현재 월드에 남아 있는 탄환 복사본이다.</summary>
        public ProjectileSnapshot[] Projectiles { get; }
        /// <summary>배치된 타워 복사본이다.</summary>
        public TowerSnapshot[] Towers { get; }
        /// <summary>보유한 모든 카드 인스턴스와 장착 상태다.</summary>
        public CardInstanceSnapshot[] Cards { get; }
        /// <summary>드래프트 단계에 제시된 카드 정의 ID 목록이다.</summary>
        public CardId[] DraftOffers { get; }
        public CardId[] CardPackOffers { get; }
        /// <summary>적 분열 가계별 보상·진행도 원장 복사본이다.</summary>
        public LineageSnapshot[] Lineages { get; }
        /// <summary>현재 플레이어가 배치할 수 있는 타워 안정 ID 목록이다.</summary>
        public string[] UnlockedTowerIds { get; }

        /// <summary>고정 건설 지점별 위치, 원래 해금 비용과 현재 해금 상태다.</summary>
        public BuildSpotSnapshot[] BuildSpots { get; }
        public CardPackSnapshot[] CardPacks { get; }
        public int CardPackProgress { get; }
        /// <summary>현재 카드팩 구간의 진행률이다. 10,000이 100%다.</summary>
        public int CardPackProgressBps { get; }
        public int NextCardPackThreshold { get; }
        /// <summary>웨이브 종료 보상 처리 순서의 카드팩 ID 복사본이다.</summary>
        public int[] RewardQueueCardPackIds { get; }
        public int PendingCardInstanceId { get; }
        public int TowerConstructionCost { get; }
    }
}
