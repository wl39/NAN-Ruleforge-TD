using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Content
{
    // "Compiled" 타입은 JSON을 그대로 담는 DTO와 달리 전투가 직접 읽는 런타임 데이터다.
    // 문자열 enum과 문자열 참조는 이미 검증되어 enum/정수 ID로 바뀌었고,
    // 배열은 외부에서 원본을 바꾸지 못하도록 복사본만 공개한다.
    // 여기서 말하는 컴파일은 C# 빌드가 아니라 "디자이너 데이터 검증 및 변환" 단계다.

    /// <summary>
    /// 검증이 끝난 카드 효과 한 단계다.
    /// struct이며 읽기 전용이므로 카드 실행 중 실수로 밸런스 원본을 바꿀 수 없다.
    /// 각 숫자 칸의 구체적인 뜻은 Operation에 등록된 executor가 해석한다.
    /// </summary>
    public readonly struct CompiledEffectNode
    {
        /// <summary>컴파일러만이 완전한 효과 노드를 만들 때 사용하는 생성자다.</summary>
        public CompiledEffectNode(
            EffectOperation operation,
            int amount,
            int amount2,
            int amount3,
            int durationTicks,
            int intervalTicks,
            int maxStacks,
            int radiusMilli,
            int limit,
            int chanceBps,
            string referenceId)
        {
            Operation = operation;
            Amount = amount;
            Amount2 = amount2;
            Amount3 = amount3;
            DurationTicks = durationTicks;
            IntervalTicks = intervalTicks;
            MaxStacks = maxStacks;
            RadiusMilli = radiusMilli;
            Limit = limit;
            ChanceBps = chanceBps;
            ReferenceId = referenceId ?? string.Empty;
        }

        /// <summary>이 노드를 담당할 실행 코드를 고르는 연산 종류다.</summary>
        public EffectOperation Operation { get; }

        // Amount 계열은 연산별 매개변수다. 공통 형식을 사용하기 때문에
        // 어떤 카드는 일부 값만 사용하며, 사용 범위는 ContentCompiler가 검사한다.
        public int Amount { get; }
        public int Amount2 { get; }
        public int Amount3 { get; }

        /// <summary>지속시간. 30Hz 시뮬레이션의 정수 틱 단위다.</summary>
        public int DurationTicks { get; }

        /// <summary>주기 효과 사이의 간격을 나타내는 정수 틱이다.</summary>
        public int IntervalTicks { get; }

        /// <summary>상태이상 등 누적 가능한 효과의 최대 중첩 수다.</summary>
        public int MaxStacks { get; }

        /// <summary>범위 반지름을 월드 단위의 1/1000인 milli 정수로 표현한 값이다.</summary>
        public int RadiusMilli { get; }

        /// <summary>횟수나 효과 상한 등 Operation별 제한값이다.</summary>
        public int Limit { get; }

        /// <summary>확률의 basis point 값이다. 10000이 100%다.</summary>
        public int ChanceBps { get; }

        /// <summary>다른 콘텐츠를 가리키는 선택적 안정 문자열 ID다.</summary>
        public string ReferenceId { get; }
    }

    /// <summary>
    /// 한 카드의 검증 완료 런타임 정의다.
    /// Id는 빠른 배열 접근용 정수 ID, StableId는 JSON·저장 데이터용 문자열 ID다.
    /// ProjectileEffects와 EnemyEffects가 모두 존재하므로 타워 문맥에 따른 이중 해석을 보장한다.
    /// </summary>
    public sealed class CompiledCardDefinition
    {
        public const int VisualStyleCapacity = 64;

        // 배열은 C#에서 참조형이다. 그대로 반환하면 호출자가 원소를 바꾸어
        // 실행 중인 밸런스를 변조할 수 있으므로, 내부 원본과 외부 복사본을 분리한다.
        private string[] tags = Array.Empty<string>();
        private CompiledEffectNode[] projectileEffects =
            Array.Empty<CompiledEffectNode>();
        private CompiledEffectNode[] enemyEffects =
            Array.Empty<CompiledEffectNode>();

        /// <summary>현재 컴파일된 카드 배열의 위치를 감싼 빠른 런타임 ID다.</summary>
        public CardId Id { get; internal set; }

        /// <summary>콘텐츠 파일과 저장 데이터에서 사용하는 변경에 신중해야 할 문자열 ID다.</summary>
        public string StableId { get; internal set; }

        /// <summary>실제 표시 문자열을 로컬라이제이션 테이블에서 찾는 키다.</summary>
        public string DisplayNameKey { get; internal set; }

        /// <summary>카드 슬롯의 축약 표기를 찾는 로컬라이제이션 키다.</summary>
        public string SymbolKey { get; internal set; }

        /// <summary>64-bit 카드 표현 플래그에서 사용하는 비트 위치다. -1은 미지정이다.</summary>
        public int VisualStyleIndex { get; internal set; }

        /// <summary>
        /// 표현 이벤트와 스냅샷이 전달할 카드별 비트다.
        /// Simulation enum에 Content 계층이 의존하지 않도록 범용 ulong으로 노출한다.
        /// </summary>
        public ulong VisualEffectFlag =>
            VisualStyleIndex < 0 ||
            VisualStyleIndex >= VisualStyleCapacity
                ? 0UL
                : 1UL << VisualStyleIndex;

        /// <summary>드래프트와 규칙 복잡도를 나타내는 카드 티어다.</summary>
        public CardTier Tier { get; internal set; }

        /// <summary>타워 연산력에서 차감되는 비용이다.</summary>
        public int ComputeCost { get; internal set; }

        /// <summary>타워 카드 슬롯에서 차지하는 칸 수다.</summary>
        public int SlotCost { get; internal set; }

        /// <summary>
        /// 빌드 시너지 태그의 복사본을 반환한다.
        /// 반환된 배열을 수정해도 시뮬레이션의 원본 콘텐츠에는 영향이 없다.
        /// </summary>
        public string[] Tags
        {
            get { return (string[])tags.Clone(); }
            internal set
            {
                tags = value == null
                    ? Array.Empty<string>()
                    : (string[])value.Clone();
            }
        }
        /// <summary>탄환 문맥에서 순서대로 실행할 효과 노드의 복사본이다.</summary>
        public CompiledEffectNode[] ProjectileEffects
        {
            get { return (CompiledEffectNode[])projectileEffects.Clone(); }
            internal set
            {
                projectileEffects = value == null
                    ? Array.Empty<CompiledEffectNode>()
                    : (CompiledEffectNode[])value.Clone();
            }
        }
        /// <summary>적 문맥에서 순서대로 실행할 효과 노드의 복사본이다.</summary>
        public CompiledEffectNode[] EnemyEffects
        {
            get { return (CompiledEffectNode[])enemyEffects.Clone(); }
            internal set
            {
                enemyEffects = value == null
                    ? Array.Empty<CompiledEffectNode>()
                    : (CompiledEffectNode[])value.Clone();
            }
        }

        // GameLogic 어셈블리 내부의 성능 민감 코드는 이미 신뢰된 원본 배열을 읽는다.
        // internal 접근자는 외부 UI나 Unity 표현 계층에는 보이지 않는다.
        internal string[] TagsInternal => tags;
        internal CompiledEffectNode[] ProjectileEffectsInternal =>
            projectileEffects;
        internal CompiledEffectNode[] EnemyEffectsInternal => enemyEffects;
    }

    /// <summary>
    /// 검증이 끝난 타워 원형이다.
    /// Trigger + SubjectTypeMode + Selector가 카드 문장의 앞부분을 만들고,
    /// 나머지 값은 슬롯 제한과 기본 전투 성능을 정의한다.
    /// </summary>
    public sealed class CompiledTowerLevelBalance
    {
        public int UpgradeCost { get; internal set; }
        public int UnlockedSlots { get; internal set; }
        public int ComputeCapacity { get; internal set; }
        public int CooldownTicks { get; internal set; }
        public int RangeMilli { get; internal set; }
        public int SelectorRadiusMilli { get; internal set; }
        public int TargetLimit { get; internal set; }
        public int PerTargetCooldownTicks { get; internal set; }
        public int VolleyCount { get; internal set; }
    }

    public sealed class CompiledTowerDefinition
    {
        /// <summary>컴파일된 타워 배열을 빠르게 조회하는 정수 ID다.</summary>
        public TowerDefinitionId Id { get; internal set; }

        /// <summary>JSON·저장 데이터·명령에서 사용하는 안정 문자열 ID다.</summary>
        public string StableId { get; internal set; }

        /// <summary>로컬라이제이션 표시 키다.</summary>
        public string DisplayNameKey { get; internal set; }

        /// <summary>카드 프로그램을 시작시키는 사건이다.</summary>
        public TowerTrigger Trigger { get; internal set; }

        /// <summary>카드를 탄환/적 중 어느 해석으로 실행할지 정한다.</summary>
        public SubjectTypeMode SubjectTypeMode { get; internal set; }

        /// <summary>트리거 순간 실제 효과 대상 선택 방식이다.</summary>
        public SubjectSelector Selector { get; internal set; }

        /// <summary>장착 가능한 총 카드 칸 수다.</summary>
        public int SlotCount { get; internal set; }

        /// <summary>장착 카드 ComputeCost 합의 상한이다.</summary>
        public int ComputeCapacity { get; internal set; }

        /// <summary>첫 무료 타워 이후의 기본 건설비다.</summary>
        public int ConstructionCost { get; internal set; }

        /// <summary>같은 타워 정의 한 기당 추가되는 건설비 basis point다.</summary>
        public int DuplicateCostStepBps { get; internal set; }

        /// <summary>재발동 대기시간의 정수 틱 수다.</summary>
        public int CooldownTicks { get; internal set; }

        /// <summary>공격 준비 시작부터 실제 탄환 생성까지의 정수 틱 수다.</summary>
        public int AttackWindupTicks { get; internal set; }

        /// <summary>공격/감지 범위의 milli 정수 반지름이다.</summary>
        public int RangeMilli { get; internal set; }

        /// <summary>기본 피해량의 milli 정수 값이다.</summary>
        public int BaseDamageMilli { get; internal set; }

        /// <summary>탄환의 틱당 milli 이동 거리다.</summary>
        public int ProjectileSpeedMilliPerTick { get; internal set; }

        /// <summary>기본 탄환 수명의 정수 틱 수다.</summary>
        public int ProjectileLifetimeTicks { get; internal set; }

        /// <summary>이벤트 주변 대상을 고를 때 쓰는 milli 정수 반지름이다.</summary>
        public int SelectorRadiusMilli { get; internal set; }

        /// <summary>한 발동에서 선택할 수 있는 최대 대상 수다.</summary>
        public int TargetLimit { get; internal set; }

        /// <summary>같은 적에 대한 재발동 금지 시간의 틱 수다.</summary>
        public int PerTargetCooldownTicks { get; internal set; }

        private CompiledTowerLevelBalance[] levels =
            Array.Empty<CompiledTowerLevelBalance>();

        /// <summary>이 타워가 데이터로 정의한 전체 레벨 수다.</summary>
        public int LevelCount => levels.Length;

        /// <summary>
        /// 이 타워가 도달할 수 있는 최대 1 기반 레벨이다.
        /// </summary>
        public int MaxLevel => levels.Length;

        /// <summary>
        /// 요청한 1 기반 레벨이 실제 데이터 범위에 있을 때만 밸런스를 반환한다.
        /// 범위를 조용히 보정하지 않아 잘못된 진행 상태가 숨겨지지 않게 한다.
        /// </summary>
        public bool TryGetLevel(
            int towerLevel,
            out CompiledTowerLevelBalance level)
        {
            int index = towerLevel - 1;
            if (index < 0 || index >= levels.Length)
            {
                level = null;
                return false;
            }

            level = levels[index];
            return level != null;
        }

        public CompiledTowerLevelBalance GetLevel(int towerLevel)
        {
            return TryGetLevel(towerLevel, out CompiledTowerLevelBalance level)
                ? level
                : null;
        }

        internal CompiledTowerLevelBalance[] LevelsInternal
        {
            get { return levels; }
            set
            {
                levels = value ??
                    Array.Empty<CompiledTowerLevelBalance>();
            }
        }
    }

    /// <summary>
    /// 검증이 끝난 적 원형이다.
    /// 실제 적 엔티티가 생성될 때 체력·속도·저항·보상 가계의 초기값으로 사용한다.
    /// </summary>
    public sealed class CompiledEnemyDefinition
    {
        /// <summary>컴파일된 적 배열을 빠르게 조회하는 정수 ID다.</summary>
        public EnemyDefinitionId Id { get; internal set; }

        /// <summary>웨이브 JSON과 저장·로그에서 쓰는 안정 문자열 ID다.</summary>
        public string StableId { get; internal set; }

        /// <summary>로컬라이제이션 표시 키다.</summary>
        public string DisplayNameKey { get; internal set; }

        /// <summary>콘텐츠가 명시한 전투 레벨이며 웨이브나 분열 세대와 무관하다.</summary>
        public int Level { get; internal set; }

        /// <summary>현지화된 적 유형 이름을 찾는 키다.</summary>
        public string TypeKey { get; internal set; }

        /// <summary>현지화된 적 설명을 찾는 키다.</summary>
        public string DescriptionKey { get; internal set; }

        /// <summary>일반/정예/보스 제어 규칙을 고르는 등급이다.</summary>
        public EnemyRank Rank { get; internal set; }

        /// <summary>최대 체력의 milli 정수 값이다.</summary>
        public int MaxHealthMilli { get; internal set; }

        /// <summary>피해 공식의 방어력 단계에 쓰는 값이다.</summary>
        public int Armor { get; internal set; }

        /// <summary>경로를 따라 한 틱에 이동하는 milli 거리다.</summary>
        public int SpeedMilliPerTick { get; internal set; }

        /// <summary>분열 가계 전체가 나누어 갖는 골드 총예산이다.</summary>
        public int RewardBudget { get; internal set; }

        /// <summary>분열 가계 전체가 나누어 갖는 웨이브 진행도 총예산이다.</summary>
        public int WaveProgressBudget { get; internal set; }

        /// <summary>경로 끝 도달 시 본진에 주는 피해다.</summary>
        public int LeakDamage { get; internal set; }

        /// <summary>화염 저항의 basis point 값이다.</summary>
        public int FireResistanceBps { get; internal set; }

        /// <summary>독 저항의 basis point 값이다.</summary>
        public int PoisonResistanceBps { get; internal set; }

        /// <summary>정예·보스의 행동 방해가 발동하는 제어 게이지 기준값이다.</summary>
        public int ControlGaugeThreshold { get; internal set; }

        /// <summary>강한 제어 1회가 기본적으로 누적하는 게이지 값이다.</summary>
        public int ControlGaugeStep { get; internal set; }

        /// <summary>보스의 데이터 기반 핵심 능력이며 일반 적은 None이다.</summary>
        public BossAbilityType BossAbility { get; internal set; }

        public int BossAbilityIntervalTicks { get; internal set; }
        public int BossEnragedAbilityIntervalTicks { get; internal set; }
        public int BossPhaseHealthBps { get; internal set; }
        public int BossShieldBps { get; internal set; }
        public EnemyDefinitionId BossSummonEnemyId { get; internal set; }
        public int BossSummonCount { get; internal set; }
        public int BossEnragedSummonCount { get; internal set; }
        public int BossMaxActiveSummons { get; internal set; }
        public int BossSummonHealthBps { get; internal set; }
        public int BossCastTicks { get; internal set; }
        public int BossTeleportDistanceBps { get; internal set; }
        public int BossEnragedTeleportDistanceBps { get; internal set; }

        /// <summary>다음 웨이브 예고에서 쓰는 검증 완료 설명/추천 데이터다.</summary>
        public string SpeedRatingKey { get; internal set; }
        public string[] FeatureKeys { get; internal set; }
        public string[] SpecialAbilityKeys { get; internal set; }
        public string[] WeaknessKeys { get; internal set; }
        public CardId[] RecommendedCardIds { get; internal set; }
        public string[] RecommendedTagKeys { get; internal set; }
    }

    /// <summary>
    /// 기본 적 정의와 조합되는 검증 완료 엘리트 특성이다. 색상은 GameLogic이
    /// UnityEngine에 의존하지 않도록 검증된 8자리 RGBA hex 문자열로 보관한다.
    /// </summary>
    public sealed class CompiledEliteTraitDefinition
    {
        public EliteTraitId Id { get; internal set; }
        public string StableId { get; internal set; }
        public string DisplayNameKey { get; internal set; }
        public string PrefixKey { get; internal set; }
        public string DescriptionKey { get; internal set; }
        public string CounterHintKey { get; internal set; }
        public string IconText { get; internal set; }
        public string BodyTint { get; internal set; }
        public string OutlineColor { get; internal set; }
        public string HealthBarColor { get; internal set; }
        public string ShieldBarColor { get; internal set; }
        public int OutlineWidthMilli { get; internal set; }
        public int HealthMultiplierBps { get; internal set; }
        public int ArmorMultiplierBps { get; internal set; }
        public int SpeedMultiplierBps { get; internal set; }
        public int RewardMultiplierBps { get; internal set; }
        public int ShieldBaseHealthBps { get; internal set; }
        public int RenderScaleBps { get; internal set; }
        public CardId[] RecommendedCardIds { get; internal set; }
        public string[] RecommendedTagKeys { get; internal set; }
    }

    /// <summary>
    /// 검증 완료 웨이브의 적 생성 묶음이다.
    /// 문자열 enemyId 대신 배열 조회가 가능한 EnemyDefinitionId를 보관한다.
    /// </summary>
    public readonly struct CompiledWaveSpawn
    {
        /// <summary>컴파일러가 유효한 적 참조와 시간값으로 생성 묶음을 만든다.</summary>
        public CompiledWaveSpawn(
            EnemyDefinitionId enemyId,
            int count,
            int firstSpawnTick,
            int intervalTicks,
            EliteTraitId[] eliteTraitIds = null)
        {
            EnemyId = enemyId;
            Count = count;
            FirstSpawnTick = firstSpawnTick;
            IntervalTicks = intervalTicks;
            this.eliteTraitIds = eliteTraitIds == null
                ? Array.Empty<EliteTraitId>()
                : (EliteTraitId[])eliteTraitIds.Clone();
        }

        private readonly EliteTraitId[] eliteTraitIds;

        /// <summary>생성할 적 원형의 런타임 정수 ID다.</summary>
        public EnemyDefinitionId EnemyId { get; }

        /// <summary>생성할 총 개체 수다.</summary>
        public int Count { get; }

        /// <summary>웨이브 시작 기준 첫 생성 상대 틱이다.</summary>
        public int FirstSpawnTick { get; }

        /// <summary>연속 생성 사이의 틱 간격이다.</summary>
        public int IntervalTicks { get; }

        /// <summary>이 스폰 묶음이 기본 적에 조합할 엘리트 특성 복사본이다.</summary>
        public EliteTraitId[] EliteTraitIds => eliteTraitIds == null
            ? Array.Empty<EliteTraitId>()
            : (EliteTraitId[])eliteTraitIds.Clone();

        internal EliteTraitId[] EliteTraitIdsInternal =>
            eliteTraitIds ?? Array.Empty<EliteTraitId>();
    }

    /// <summary>검증 완료 웨이브 한 개와 그 생성 예약 목록이다.</summary>
    public sealed class CompiledWaveDefinition
    {
        // 외부에는 복사본을 주고, 시뮬레이션 내부만 원본을 읽는 불변성 패턴이다.
        private CompiledWaveSpawn[] spawns =
            Array.Empty<CompiledWaveSpawn>();

        /// <summary>웨이브 순서를 빠르게 가리키는 정수 ID다.</summary>
        public WaveId Id { get; internal set; }

        /// <summary>콘텐츠 식별과 로그를 위한 안정 문자열 ID다.</summary>
        public string StableId { get; internal set; }

        public WaveArchetype Archetype { get; internal set; }

        public int TotalSpawnCount { get; internal set; }
        public int NormalSpawnCount { get; internal set; }
        public int EliteSpawnCount { get; internal set; }
        public int BossSpawnCount { get; internal set; }

        /// <summary>적 생성 묶음 배열의 방어적 복사본이다.</summary>
        public CompiledWaveSpawn[] Spawns
        {
            get { return (CompiledWaveSpawn[])spawns.Clone(); }
            internal set
            {
                spawns = value == null
                    ? Array.Empty<CompiledWaveSpawn>()
                    : (CompiledWaveSpawn[])value.Clone();
            }
        }

        internal CompiledWaveSpawn[] SpawnsInternal => spawns;
    }

    /// <summary>
    /// 검증 완료된 한 런의 고정 설정이다.
    /// 플레이 중 변하는 상태가 아니라 시작 자원, 경로, 배치점, 드래프트 규칙 같은
    /// "이번 콘텐츠 버전에서 변하지 않는 규칙"만 담는다.
    /// </summary>
    public sealed class CompiledRunDefinition
    {
        // 컬렉션은 외부에 복사본만 노출한다. 이 덕분에 UI가 받은 배열을 정렬하거나
        // 수정해도 시뮬레이션의 경로·드래프트 결과가 달라지지 않는다.
        private TowerDefinitionId[] startingTowerChoices =
            Array.Empty<TowerDefinitionId>();
        private TowerDefinitionId[] initiallyUnlockedTowers =
            Array.Empty<TowerDefinitionId>();
        private CardId[] startingCards = Array.Empty<CardId>();
        private SimPosition[] buildSpots = Array.Empty<SimPosition>();
        private int[] buildSpotUnlockCosts = Array.Empty<int>();
        private SimPosition[] pathPoints = Array.Empty<SimPosition>();
        private int[] tierWeights = Array.Empty<int>();
        private int[] regularDraftWaveIndices = Array.Empty<int>();
        private int[] bossCardPackWaveIndices = Array.Empty<int>();
        private int[] cardPackProgressThresholds = Array.Empty<int>();

        /// <summary>초당 고정 시뮬레이션 틱 수다. 현재 30으로 검증된다.</summary>
        public int TickRate { get; internal set; }

        /// <summary>새 런의 본진 시작 체력이다.</summary>
        public int BaseHealth { get; internal set; }

        /// <summary>새 런의 시작 골드다.</summary>
        public int StartingGold { get; internal set; }

        /// <summary>건설비를 면제하는 런 최초 타워 수다.</summary>
        public int FreeInitialTowerCount { get; internal set; }

        /// <summary>런 시작 화면에서 하나를 선택할 수 있는 타워 ID 복사본이다.</summary>
        public TowerDefinitionId[] StartingTowerChoices
        {
            get { return (TowerDefinitionId[])startingTowerChoices.Clone(); }
            internal set
            {
                startingTowerChoices = value == null
                    ? Array.Empty<TowerDefinitionId>()
                    : (TowerDefinitionId[])value.Clone();
            }
        }
        /// <summary>시작 선택과 별개로 처음부터 건설 가능한 타워 ID 복사본이다.</summary>
        public TowerDefinitionId[] InitiallyUnlockedTowers
        {
            get
            {
                return (TowerDefinitionId[])initiallyUnlockedTowers.Clone();
            }
            internal set
            {
                initiallyUnlockedTowers = value == null
                    ? Array.Empty<TowerDefinitionId>()
                    : (TowerDefinitionId[])value.Clone();
            }
        }
        /// <summary>시작 카드 인벤토리를 구성하는 카드 ID 복사본이다.</summary>
        public CardId[] StartingCards
        {
            get { return (CardId[])startingCards.Clone(); }
            internal set
            {
                startingCards = value == null
                    ? Array.Empty<CardId>()
                    : (CardId[])value.Clone();
            }
        }
        /// <summary>타워를 놓을 수 있는 고정 위치의 복사본이다.</summary>
        public SimPosition[] BuildSpots
        {
            get { return (SimPosition[])buildSpots.Clone(); }
            internal set
            {
                buildSpots = value == null
                    ? Array.Empty<SimPosition>()
                    : (SimPosition[])value.Clone();
            }
        }

        /// <summary>
        /// 건설 지점 인덱스와 일치하는 해금 골드 비용 복사본이다.
        /// 0인 지점은 새 런에서 즉시 해금된다.
        /// </summary>
        public int[] BuildSpotUnlockCosts
        {
            get { return (int[])buildSpotUnlockCosts.Clone(); }
            internal set
            {
                buildSpotUnlockCosts = value == null
                    ? Array.Empty<int>()
                    : (int[])value.Clone();
            }
        }

        /// <summary>적이 순서대로 따라가는 경로 꼭짓점의 복사본이다.</summary>
        public SimPosition[] PathPoints
        {
            get { return (SimPosition[])pathPoints.Clone(); }
            internal set
            {
                pathPoints = value == null
                    ? Array.Empty<SimPosition>()
                    : (SimPosition[])value.Clone();
            }
        }
        /// <summary>웨이브 뒤 제시할 서로 다른 카드 선택지 수다.</summary>
        public int DraftOfferCount { get; internal set; }

        public int[] RegularDraftWaveIndices
        {
            get { return (int[])regularDraftWaveIndices.Clone(); }
            internal set
            {
                regularDraftWaveIndices = value == null
                    ? Array.Empty<int>()
                    : (int[])value.Clone();
            }
        }

        public int[] BossCardPackWaveIndices
        {
            get { return (int[])bossCardPackWaveIndices.Clone(); }
            internal set
            {
                bossCardPackWaveIndices = value == null
                    ? Array.Empty<int>()
                    : (int[])value.Clone();
            }
        }

        public int[] CardPackProgressThresholds
        {
            get { return (int[])cardPackProgressThresholds.Clone(); }
            internal set
            {
                cardPackProgressThresholds = value == null
                    ? Array.Empty<int>()
                    : (int[])value.Clone();
            }
        }

        public int NormalKillProgress { get; internal set; }
        public int EliteKillProgress { get; internal set; }
        public int SplitCardPackProgressBps { get; internal set; }
        public int ShimmeringHealthBps { get; internal set; }
        public int ShimmeringSpeedBps { get; internal set; }
        public int ShimmeringSizeBps { get; internal set; }

        /// <summary>일반~신화 5개 티어의 드래프트 상대 가중치 복사본이다.</summary>
        public int[] TierWeights
        {
            get { return (int[])tierWeights.Clone(); }
            internal set
            {
                tierWeights = value == null
                    ? Array.Empty<int>()
                    : (int[])value.Clone();
            }
        }
        /// <summary>치명타 피해 배율의 basis point 값이다.</summary>
        public int CriticalDamageBps { get; internal set; }

        /// <summary>방어력 감쇠 분수의 기준값이다.</summary>
        public int ArmorMitigationScale { get; internal set; }

        /// <summary>광역·폭발·다중 대상 피해의 방어 민감도다.</summary>
        public int AreaArmorSensitivityBps { get; internal set; }

        /// <summary>화상/화염 피해의 방어 민감도다.</summary>
        public int BurnArmorSensitivityBps { get; internal set; }

        /// <summary>정예·보스 제어 게이지 충족 시 행동 방해 지속 틱이다.</summary>
        public int ControlInterruptTicks { get; internal set; }

        /// <summary>반복 제어에 따라 증가할 수 있는 게이지 기준값의 상한이다.</summary>
        public int MaxControlGaugeThreshold { get; internal set; }

        /// <summary>이동 제한 상태에서 같은 경로 진행도에 허용되는 최대 틱 수다.</summary>
        public int MovementEscapeStationaryTicks { get; internal set; }

        /// <summary>장기 고정 뒤 이동 제한을 무시하는 틱 수다.</summary>
        public int MovementEscapeImmunityTicks { get; internal set; }

        /// <summary>적 캡슐 히트박스의 기본 milli 반지름이다.</summary>
        public int EnemyBaseHitRadiusMilli { get; internal set; }

        /// <summary>경로 기준점에서 캡슐 중심까지의 Y축 milli 오프셋이다.</summary>
        public int EnemyHitboxCenterOffsetYMilli { get; internal set; }

        /// <summary>적 캡슐 히트박스의 기본 Y축 milli 반높이다.</summary>
        public int EnemyHitboxHalfHeightMilli { get; internal set; }

        public int KillStreakWindowTicks { get; internal set; }
        public int KillStreakBonusInterval { get; internal set; }
        public int KillStreakBonusGold { get; internal set; }
        public int EliteKillBonusGold { get; internal set; }
        public int WaveCompletionBaseGold { get; internal set; }
        public int WaveCompletionGoldPerWave { get; internal set; }

        // 아래 Internal 접근자는 같은 GameLogic 어셈블리의 성능 민감 코드가
        // 매 틱 Clone 비용 없이 읽도록 제공된다. 외부 표현 계층에는 공개되지 않는다.
        internal TowerDefinitionId[] StartingTowerChoicesInternal =>
            startingTowerChoices;
        internal TowerDefinitionId[] InitiallyUnlockedTowersInternal =>
            initiallyUnlockedTowers;
        internal CardId[] StartingCardsInternal => startingCards;
        internal SimPosition[] BuildSpotsInternal => buildSpots;
        internal int[] BuildSpotUnlockCostsInternal =>
            buildSpotUnlockCosts;
        internal SimPosition[] PathPointsInternal => pathPoints;
        internal int[] TierWeightsInternal => tierWeights;
        internal int[] RegularDraftWaveIndicesInternal =>
            regularDraftWaveIndices;
        internal int[] BossCardPackWaveIndicesInternal =>
            bossCardPackWaveIndices;
        internal int[] CardPackProgressThresholdsInternal =>
            cardPackProgressThresholds;
    }

    /// <summary>
    /// 시뮬레이션 초기화에 전달하는 검증 완료 콘텐츠 묶음이다.
    /// 문자열 안정 ID를 정수 ID로 찾는 표와 카드·타워·적·웨이브 배열,
    /// 안전 예산 및 런 규칙을 하나의 읽기 전용 경계로 제공한다.
    /// </summary>
    public sealed class CompiledContent
    {
        // 숫자 ID.Value를 배열 인덱스로 쓰면 매 틱 문자열 비교를 하지 않아도 된다.
        // 반대로 외부 JSON/저장은 아래 Dictionary를 통해 안정 문자열 ID를 사용한다.
        private readonly CompiledCardDefinition[] cards;
        private readonly CompiledTowerDefinition[] towers;
        private readonly CompiledEnemyDefinition[] enemies;
        private readonly CompiledEliteTraitDefinition[] eliteTraits;
        private readonly CompiledWaveDefinition[] waves;
        private readonly Dictionary<string, CardId> cardIds;
        private readonly Dictionary<string, TowerDefinitionId> towerIds;
        private readonly Dictionary<string, EnemyDefinitionId> enemyIds;
        private readonly Dictionary<string, EliteTraitId> eliteTraitIds;

        internal CompiledContent(
            int version,
            ulong contentHash,
            CompiledCardDefinition[] cards,
            CompiledTowerDefinition[] towers,
            CompiledEnemyDefinition[] enemies,
            CompiledEliteTraitDefinition[] eliteTraits,
            CompiledWaveDefinition[] waves,
            SafetyLimits safety,
            CompiledRunDefinition run,
            Dictionary<string, CardId> cardIds,
            Dictionary<string, TowerDefinitionId> towerIds,
            Dictionary<string, EnemyDefinitionId> enemyIds,
            Dictionary<string, EliteTraitId> eliteTraitIds)
        {
            Version = version;
            ContentHash = contentHash;

            // 생성 시에도 배열을 복사한다. 컴파일러가 가진 임시 배열과
            // 완성 콘텐츠가 같은 배열 인스턴스를 공유하지 않도록 하는 방어선이다.
            this.cards = (CompiledCardDefinition[])cards.Clone();
            this.towers = (CompiledTowerDefinition[])towers.Clone();
            this.enemies = (CompiledEnemyDefinition[])enemies.Clone();
            this.eliteTraits =
                (CompiledEliteTraitDefinition[])eliteTraits.Clone();
            this.waves = (CompiledWaveDefinition[])waves.Clone();
            Safety = safety;
            Run = run;
            this.cardIds = cardIds;
            this.towerIds = towerIds;
            this.enemyIds = enemyIds;
            this.eliteTraitIds = eliteTraitIds;
        }

        /// <summary>JSON에 기록된 콘텐츠 스키마/밸런스 버전이다.</summary>
        public int Version { get; }

        /// <summary>
        /// 전투 결과에 영향을 주는 컴파일된 콘텐츠 전체의 안정 해시다.
        /// 리플레이와 WebGL 검증에서 "정말 같은 규칙으로 실행했는가"를 확인한다.
        /// </summary>
        public ulong ContentHash { get; }

        /// <summary>모든 카드 정의 배열의 방어적 복사본이다.</summary>
        public CompiledCardDefinition[] Cards =>
            (CompiledCardDefinition[])cards.Clone();

        /// <summary>모든 타워 정의 배열의 방어적 복사본이다.</summary>
        public CompiledTowerDefinition[] Towers =>
            (CompiledTowerDefinition[])towers.Clone();

        /// <summary>모든 적 정의 배열의 방어적 복사본이다.</summary>
        public CompiledEnemyDefinition[] Enemies =>
            (CompiledEnemyDefinition[])enemies.Clone();

        public CompiledEliteTraitDefinition[] EliteTraits =>
            (CompiledEliteTraitDefinition[])eliteTraits.Clone();

        /// <summary>모든 웨이브 정의 배열의 방어적 복사본이다.</summary>
        public CompiledWaveDefinition[] Waves =>
            (CompiledWaveDefinition[])waves.Clone();

        /// <summary>컴파일된 카드 수다.</summary>
        public int CardCount => cards.Length;

        /// <summary>컴파일된 타워 수다.</summary>
        public int TowerCount => towers.Length;

        /// <summary>컴파일된 적 원형 수다.</summary>
        public int EnemyCount => enemies.Length;

        public int EliteTraitCount => eliteTraits.Length;

        /// <summary>런에 포함된 웨이브 수다.</summary>
        public int WaveCount => waves.Length;

        /// <summary>이 콘텐츠에 적용되는 연쇄작용 안전 예산이다.</summary>
        public SafetyLimits Safety { get; }

        /// <summary>이 콘텐츠에 적용되는 고정 런 설정이다.</summary>
        public CompiledRunDefinition Run { get; }

        /// <summary>
        /// 카드 안정 문자열 ID를 빠른 런타임 CardId로 변환한다.
        /// 없는 ID는 예외 대신 false를 반환하므로 UI 입력 검증에 적합하다.
        /// </summary>
        /// <param name="stableId">JSON의 CardDefinitionDto.id와 같은 문자열이다.</param>
        /// <param name="id">성공하면 대응하는 런타임 정수 ID를 받는다.</param>
        /// <returns>해당 안정 ID가 현재 콘텐츠에 있으면 true다.</returns>
        public bool TryGetCardId(string stableId, out CardId id)
        {
            return cardIds.TryGetValue(stableId, out id);
        }

        /// <summary>타워 안정 문자열 ID를 빠른 런타임 ID로 변환한다.</summary>
        /// <param name="stableId">JSON의 TowerDefinitionDto.id와 같은 문자열이다.</param>
        /// <param name="id">성공하면 대응하는 런타임 정수 ID를 받는다.</param>
        /// <returns>해당 안정 ID가 현재 콘텐츠에 있으면 true다.</returns>
        public bool TryGetTowerId(string stableId, out TowerDefinitionId id)
        {
            return towerIds.TryGetValue(stableId, out id);
        }

        /// <summary>적 안정 문자열 ID를 빠른 런타임 ID로 변환한다.</summary>
        /// <param name="stableId">JSON의 EnemyDefinitionDto.id와 같은 문자열이다.</param>
        /// <param name="id">성공하면 대응하는 런타임 정수 ID를 받는다.</param>
        /// <returns>해당 안정 ID가 현재 콘텐츠에 있으면 true다.</returns>
        public bool TryGetEnemyId(string stableId, out EnemyDefinitionId id)
        {
            return enemyIds.TryGetValue(stableId, out id);
        }

        public bool TryGetEliteTraitId(
            string stableId,
            out EliteTraitId id)
        {
            return eliteTraitIds.TryGetValue(stableId, out id);
        }

        /// <summary>
        /// 검증된 CardId로 카드 정의를 가져온다.
        /// ID가 유효하다는 내부 계약을 전제로 하며 잘못된 값이면 배열 예외가 발생한다.
        /// </summary>
        public CompiledCardDefinition GetCard(CardId id)
        {
            return cards[id.Value];
        }

        /// <summary>검증된 TowerDefinitionId로 타워 정의를 가져온다.</summary>
        public CompiledTowerDefinition GetTower(TowerDefinitionId id)
        {
            return towers[id.Value];
        }

        /// <summary>검증된 EnemyDefinitionId로 적 원형을 가져온다.</summary>
        public CompiledEnemyDefinition GetEnemy(EnemyDefinitionId id)
        {
            return enemies[id.Value];
        }

        public CompiledEliteTraitDefinition GetEliteTrait(EliteTraitId id)
        {
            return eliteTraits[id.Value];
        }

        /// <summary>검증된 WaveId로 웨이브 정의를 가져온다.</summary>
        public CompiledWaveDefinition GetWave(WaveId id)
        {
            return waves[id.Value];
        }

        /// <summary>0부터 시작하는 웨이브 순번으로 웨이브 정의를 가져온다.</summary>
        public CompiledWaveDefinition GetWave(int index)
        {
            return waves[index];
        }
    }

    /// <summary>
    /// JSON 콘텐츠에 오류가 하나 이상 있어 안전한 CompiledContent를 만들 수 없을 때 발생한다.
    /// 메시지에는 한 번의 검사에서 수집한 오류들이 줄바꿈으로 함께 들어간다.
    /// </summary>
    public sealed class ContentValidationException : Exception
    {
        /// <summary>사람이 수정할 수 있도록 수집된 검증 메시지로 예외를 만든다.</summary>
        public ContentValidationException(string message)
            : base(message)
        {
        }
    }
}
