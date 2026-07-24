using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// Ruleforge TD 한 판의 모든 규칙 상태를 소유하고, 명령을 받아 고정 틱 단위로 진행하는
    /// 게임 로직의 유일한 공개 진입점이다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 이 클래스는 <c>MonoBehaviour</c>가 아니며 Unity 장면, 프레임 시간, 오브젝트의
    /// <c>Transform</c>을 전혀 알지 못한다. 따라서 Unity 앞단은 이 객체에 명령을 보내고
    /// <see cref="GetSnapshot"/> 및 <see cref="ReadPresentationEvents"/>의 결과를 화면에
    /// 옮겨 그리기만 하면 된다.
    /// </para>
    /// <para>
    /// 파일이 <c>partial</c>로 선언된 이유는 하나의 시뮬레이션을 역할별 파일로 나누기
    /// 위해서다. 이 파일은 생명주기, 공개 API, 스냅샷, 상태 해시를 담당하고,
    /// <c>GameSimulation.Run.cs</c>, <c>GameSimulation.Combat.cs</c>,
    /// <c>GameSimulation.Effects.cs</c>, <c>GameSimulation.Events.cs</c>가 같은 객체의
    /// 나머지 구현을 제공한다.
    /// </para>
    /// </remarks>
    public sealed partial class GameSimulation
    {
        // 초기화할 때 컴파일된 불변 콘텐츠와 현재 런의 규칙을 보관한다.
        // StableId 문자열을 매 틱 찾지 않고 정수 ID로 접근하기 때문에 빠르고 결정적이다.
        private CompiledContent content;
        private CompiledRunDefinition run;
        private EffectRegistry effectRegistry;

        // 경로와 공간 검색은 화면 좌표나 Unity 물리 엔진 대신 정수 기반 로직 모델을 쓴다.
        private PathModel path;
        private SpatialHashGrid spatialIndex;

        // 파생 효과는 메서드 안에서 즉시 재귀 호출하지 않고 이 큐에 넣는다.
        // diagnostics는 안전 예산 때문에 거절된 효과의 원인을 최근 항목부터 보존한다.
        private EventQueue eventQueue;
        private DiagnosticRingBuffer diagnostics;

        // 난수 소비 순서가 서로 영향을 주지 않도록 전투, 드래프트, 웨이브를 별도 스트림으로
        // 분리한다. 같은 seed와 같은 명령 로그라면 세 스트림의 결과도 항상 동일하다.
        private Pcg32 combatRandom;
        private Pcg32 draftRandom;
        private Pcg32 waveRandom;

        // 아래 컬렉션들이 현재 한 판의 실제 권위 상태(authoritative state)다.
        // 화면에 보이는 GameObject나 EnemyHealth 컴포넌트는 이 값의 복사본일 뿐이다.
        private readonly List<EnemyState> enemies = new List<EnemyState>(256);
        private readonly List<ProjectileState> projectiles = new List<ProjectileState>(512);
        private readonly List<TowerState> towers = new List<TowerState>(16);
        private readonly List<CardInstanceState> cards = new List<CardInstanceState>(64);
        private readonly List<HazardState> hazards = new List<HazardState>(64);

        // 카드 프로그램의 실행 도중 필요한 커서와 문맥을 프레임으로 보관한다.
        // 끝난 프레임의 인덱스는 freeProgramFrames에 넣어 재사용함으로써 반복 할당을 줄인다.
        private readonly List<ProgramFrame> programFrames = new List<ProgramFrame>(256);
        private readonly Stack<int> freeProgramFrames = new Stack<int>();

        // 표현 이벤트는 고정 크기 원형 버퍼다. 소비자가 늦더라도 메모리가 끝없이 늘어나지
        // 않으며, 가득 찬 경우 가장 오래된 표현 이벤트부터 덮어쓴다.
        private SimulationPresentationEvent[] presentationEvents =
            new SimulationPresentationEvent[0];
        private int presentationEventHead;
        private int presentationEventCount;

        // 매 틱 또는 특정 계산에서 잠시 쓰는 재사용 버퍼들이다.
        // 전투 중 GC 할당을 줄이는 것이 특히 WebGL에서 중요하다.
        private readonly List<CardId> draftOffers = new List<CardId>(3);
        private readonly List<EntityId> spatialScratch = new List<EntityId>(256);
        private readonly List<EntityId> sweepScratch = new List<EntityId>(256);
        private readonly HashSet<int> sweepIds = new HashSet<int>();

        // 해금된 타워, RootChain별 안전 예산, 적 가계별 보상 원장을 보관한다.
        private readonly HashSet<int> ownedTowerDefinitions = new HashSet<int>();
        private readonly Dictionary<int, ChainBudget> chainBudgets =
            new Dictionary<int, ChainBudget>();
        private readonly Dictionary<int, LineageState> lineages =
            new Dictionary<int, LineageState>();

        // 런의 큰 진행 상태와 현재 웨이브에서 공유하는 수치다.
        private RunPhase phase;
        private long tick;
        private int currentWaveIndex;
        private long waveStartTick;
        private int baseHealth;
        private int gold;

        // 한 틱 및 한 연쇄에서 폭주를 막기 위한 계수와, 새 상태에 부여할 단조 증가 ID다.
        // ID를 재사용하지 않아 동률 정렬과 리플레이 비교가 안정적으로 유지된다.
        private int eventsProcessedThisTick;
        private int eventsEnqueuedThisTick;
        private int nextEntityId;
        private int nextTowerId;
        private int nextCardInstanceId;
        private int nextStatusInstanceId;
        private int nextHazardId;
        private int nextChainId;
        private int nextActivationId;
        private bool initialized;

        // 같은 콘텐츠라도 런 설정이 다르면 상태 해시가 달라지도록 설정 지문도 포함한다.
        private ulong runDefinitionHash;

        // 현재 웨이브의 각 스폰 묶음이 몇 마리까지 생성됐고 다음 생성 틱이 언제인지 기록한다.
        private WaveSpawnRuntime[] waveSpawns = new WaveSpawnRuntime[0];

        /// <summary>현재 런 단계다. 계획, 전투, 드래프트, 승패 등의 명령 허용 여부를 결정한다.</summary>
        public RunPhase Phase => phase;

        /// <summary>초기화 후 지금까지 진행된 논리 틱 수다. 기본 설정에서는 30틱이 1초다.</summary>
        public long Tick => tick;

        /// <summary>현재 진행 중이거나 방금 끝난 웨이브의 0부터 시작하는 인덱스다.</summary>
        public int CurrentWaveIndex => currentWaveIndex;

        /// <summary>적이 경로 끝에 도착했을 때 감소하는 본진의 남은 체력이다.</summary>
        public int BaseHealth => baseHealth;

        /// <summary>현재 보유 골드다. 보상 원장을 거친 확정 값만 반영된다.</summary>
        public int Gold => gold;

        /// <summary>이 시뮬레이션이 사용하는 읽기 전용 컴파일 콘텐츠다.</summary>
        public CompiledContent Content => content;

        /// <summary>안전 예산 초과 등 개발 중 원인 추적이 필요한 최근 진단 기록이다.</summary>
        public DiagnosticRingBuffer Diagnostics => diagnostics;

        /// <summary>
        /// 콘텐츠가 제공하는 기본 런 설정과 seed로 새 게임을 초기화한다.
        /// </summary>
        /// <param name="compiledContent">문자열/초 단위 원본을 정수 ID/틱으로 변환한 콘텐츠다.</param>
        /// <param name="seed">결정적 난수의 시작값이다. 동일 입력은 동일 결과를 만든다.</param>
        public void Initialize(CompiledContent compiledContent, ulong seed)
        {
            Initialize(compiledContent, RunConfig.FromContent(compiledContent), seed);
        }

        /// <summary>
        /// 지정한 콘텐츠, 런 규칙, seed로 기존 상태를 모두 비우고 새 런을 준비한다.
        /// </summary>
        /// <remarks>
        /// 초기화 직후에는 전투가 자동 시작되지 않는다. 시작 카드는 인벤토리에 들어오고
        /// 단계는 <see cref="RunPhase.AwaitingStartingTower"/>가 되므로, 앞단이 먼저
        /// 시작 타워 선택 명령을 보내야 한다.
        /// </remarks>
        public void Initialize(
            CompiledContent compiledContent,
            RunConfig runConfig,
            ulong seed)
        {
            if (compiledContent == null)
            {
                throw new ArgumentNullException(nameof(compiledContent));
            }
            if (runConfig == null)
            {
                throw new ArgumentNullException(nameof(runConfig));
            }

            content = compiledContent;
            run = runConfig.Definition;
            ValidateRunConfig(run);
            runDefinitionHash = runConfig.DefinitionHash;
            effectRegistry = EffectRegistry.CreateDefault();
            ValidateExecutors();

            // 공간 셀 크기와 큐/진단 용량도 로직 데이터에서 확정한다.
            path = new PathModel(run.PathPointsInternal);
            spatialIndex = new SpatialHashGrid(2000);
            eventQueue = new EventQueue(Math.Max(
                content.Safety.MaxQueuedEvents,
                content.Safety.MaxEventsPerTick));
            diagnostics = new DiagnosticRingBuffer(content.Safety.DiagnosticCapacity);
            combatRandom = Pcg32.ForDomain(seed, 0x434F4D424154UL);
            draftRandom = Pcg32.ForDomain(seed, 0x4452414654UL);
            waveRandom = Pcg32.ForDomain(seed, 0x57415645UL);

            // 같은 GameSimulation 인스턴스를 새 런에 재사용할 수 있으므로 모든 가변 상태를
            // 명시적으로 초기값으로 되돌린다.
            enemies.Clear();
            projectiles.Clear();
            towers.Clear();
            cards.Clear();
            hazards.Clear();
            programFrames.Clear();
            freeProgramFrames.Clear();
            presentationEvents = new SimulationPresentationEvent[
                content.Safety.MaxEventsPerTick];
            presentationEventHead = 0;
            presentationEventCount = 0;
            draftOffers.Clear();
            ownedTowerDefinitions.Clear();
            chainBudgets.Clear();
            lineages.Clear();

            tick = 0;
            currentWaveIndex = -1;
            waveStartTick = 0;
            baseHealth = run.BaseHealth;
            gold = run.StartingGold;
            eventsProcessedThisTick = 0;
            eventsEnqueuedThisTick = 0;
            nextEntityId = 0;
            nextTowerId = 0;
            nextCardInstanceId = 0;
            nextStatusInstanceId = 0;
            nextHazardId = 0;
            nextChainId = 0;
            nextActivationId = 0;
            waveSpawns = new WaveSpawnRuntime[0];
            phase = RunPhase.AwaitingStartingTower;

            // 시작 카드도 정의 그 자체가 아니라 고유 InstanceId를 가진 소유 카드로 만든다.
            // 같은 카드 정의를 여러 장 얻어도 장착 위치와 강화 레벨을 개별 관리할 수 있다.
            for (int i = 0; i < run.StartingCardsInternal.Length; i++)
            {
                AddOwnedCard(run.StartingCardsInternal[i]);
            }

            initialized = true;
        }

        /// <summary>
        /// 플레이어 또는 자동화가 보낸 명령 하나를 현재 런 단계의 규칙에 따라 즉시 검증하고
        /// 적용한다.
        /// </summary>
        /// <remarks>
        /// 잘못된 명령은 예외 대신 <see cref="CommandResult"/>의 명시적 오류로 거절된다.
        /// 특히 전투 중 카드 변경처럼 게임 규칙상 허용되지 않는 요청도 여기서 차단된다.
        /// 명령의 실제 구현은 주로 <c>GameSimulation.Run.cs</c>에 있다.
        /// </remarks>
        public CommandResult Submit(in GameCommand command)
        {
            EnsureInitialized();

            // GameCommand는 여러 명령이 공유하는 작은 값 객체다. Type에 따라 같은 숫자 필드를
            // 타워 ID, 카드 인스턴스 ID, 슬롯 번호 등으로 해석해 해당 전용 메서드로 전달한다.
            switch (command.Type)
            {
                case GameCommandType.ChooseStartingTower:
                    return ChooseStartingTower(command.ContentId);
                case GameCommandType.PlaceTower:
                    return PlaceTower(command.ContentId, command.PrimaryId);
                case GameCommandType.EquipCard:
                    return EquipCard(
                        command.PrimaryId,
                        command.SecondaryId,
                        command.TertiaryId);
                case GameCommandType.MoveCard:
                    return MoveCard(
                        command.PrimaryId,
                        command.SecondaryId,
                        command.TertiaryId);
                case GameCommandType.UnequipCard:
                    return UnequipCard(command.PrimaryId);
                case GameCommandType.ReorderCard:
                    return ReorderCard(
                        command.PrimaryId,
                        command.SecondaryId,
                        command.TertiaryId);
                case GameCommandType.SelectDraft:
                    return SelectDraft(command.PrimaryId);
                case GameCommandType.StartWave:
                    return StartWave();
                default:
                    return CommandResult.Reject(
                        CommandError.InvalidTarget,
                        "Unsupported command.");
            }
        }

        /// <summary>
        /// 시뮬레이션을 정확히 한 논리 틱 전진시킨다.
        /// </summary>
        /// <remarks>
        /// Unity의 <c>Update</c> 한 번과 같은 뜻이 아니다. 실제 시간이 느리거나 빨라도
        /// 앞단이 호출 횟수만 조절하며, 이 메서드 내부 순서는 항상 같다. 그 결과 일시정지는
        /// 호출을 멈추는 것으로, 2배속은 같은 실제 시간에 두 배 많이 호출하는 것으로 구현한다.
        /// </remarks>
        public void Step()
        {
            EnsureInitialized();

            // 틱 단위 안전 상한은 매 틱 새로 계산한다. RootChain 단위 예산은 별도로 유지된다.
            eventsProcessedThisTick = 0;
            eventsEnqueuedThisTick = 0;

            if (phase == RunPhase.Combat)
            {
                // 1) 예약된 적을 생성하고, 이미 붙은 지속 효과를 먼저 처리한다.
                ProcessWaveSpawns();
                ProcessStatuses();
                DrainEventsThrough(EventPhase.Status);

                // 2) 적을 경로 위에서 이동시킨 뒤 위치가 바뀐 결과로 공간 인덱스를 재구축한다.
                MoveEnemies();
                DrainEventsThrough(EventPhase.Movement);
                spatialIndex.Rebuild(enemies);

                // 3) 장판과 타워가 현재 공간 인덱스를 보고 효과/공격 이벤트를 예약한다.
                ProcessHazards();
                ProcessTowers();
                DrainEventsThrough(EventPhase.Tower);

                // 4) 탄환 이동과 충돌을 계산한 뒤 피해, 사망, 보상을 엄격한 단계 순으로 확정한다.
                // 사망 처리 전에 보상이 실행되는 등의 순서 역전을 막기 위한 핵심 구조다.
                MoveProjectiles();
                DrainEventsThrough(EventPhase.Projectile);
                DrainEventsThrough(EventPhase.Damage);
                DrainEventsThrough(EventPhase.Death);
                DrainEventsThrough(EventPhase.Reward);

                // 5) 죽은 개체를 실제 목록에서 정리하고 웨이브 종료 여부를 마지막에 판단한다.
                CleanupDeadEntities();
                CheckWaveCompletion();
                DrainEventsThrough(EventPhase.Wave);
            }
            else
            {
                // 계획/드래프트 단계에도 앞서 예약된 화면 알림은 순서대로 소비할 수 있다.
                DrainEventsThrough(EventPhase.Presentation);
            }

            // 모든 처리에서 현재 tick 값을 공통 시간표로 사용한 뒤 맨 마지막에 증가시킨다.
            tick++;
        }

        /// <summary>
        /// UI와 렌더링 계층이 안전하게 읽을 수 있는 현재 상태의 방어적 복사본을 만든다.
        /// </summary>
        /// <remarks>
        /// 반환 배열을 앞단에서 수정해도 내부 시뮬레이션은 바뀌지 않는다. 자주 호출하면 복사
        /// 비용이 있으므로 일반적으로 화면 갱신 또는 디버그 표시가 필요할 때 한 번 받아 쓴다.
        /// 단위는 각 Snapshot 형식의 주석처럼 milli 단위와 basis point를 유지한다.
        /// </remarks>
        public SimulationSnapshot GetSnapshot()
        {
            EnsureInitialized();

            // 제거 대기 중 상태까지 포함해 내부 적 순서를 그대로 스냅샷에 옮긴다.
            var enemySnapshots = new List<EnemySnapshot>(enemies.Count);
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState enemy = enemies[i];
                if (enemy == null)
                {
                    continue;
                }

                CompiledEnemyDefinition definition = content.GetEnemy(enemy.DefinitionId);
                var statusTypes = new StatusType[enemy.Statuses.Count];
                var statusDetails =
                    new StatusSnapshot[enemy.Statuses.Count];
                for (int statusIndex = 0; statusIndex < enemy.Statuses.Count; statusIndex++)
                {
                    StatusInstance status = enemy.Statuses[statusIndex];
                    statusTypes[statusIndex] = status.Type;
                    statusDetails[statusIndex] = new StatusSnapshot(
                        status.InstanceId,
                        status.Type,
                        status.SourceEntityId.Value,
                        status.SourceTowerId.Value,
                        status.SourceCardId,
                        status.Stacks,
                        status.Intensity,
                        status.RemainingTicks,
                        status.MaxStacks,
                        status.TickInterval,
                        status.ArmorIgnoreBps);
                }
                int slowBps = GetSlowBps(enemy);
                enemySnapshots.Add(new EnemySnapshot(
                    enemy.Id.Value,
                    definition.StableId,
                    enemy.LineageId.Value,
                    enemy.PathProgressMilli,
                    enemy.Position,
                    enemy.HealthMilli,
                    enemy.MaxHealthMilli,
                    enemy.Armor,
                    slowBps,
                    enemy.SpeedMultiplierBps,
                    enemy.SizeMultiplierBps,
                    enemy.ControlGauge,
                    enemy.ControlThreshold,
                    enemy.RewardBudget,
                    enemy.WaveProgressBudget,
                    enemy.Generation,
                    enemy.Alive,
                    statusTypes,
                    statusDetails,
                    enemy.DeathBindings.Count));
            }

            // 이미 소멸한 탄환은 화면에 다시 생성되지 않도록 제외한다.
            var projectileSnapshots = new List<ProjectileSnapshot>(projectiles.Count);
            for (int i = 0; i < projectiles.Count; i++)
            {
                ProjectileState projectile = projectiles[i];
                if (projectile == null || !projectile.Alive)
                {
                    continue;
                }

                projectileSnapshots.Add(new ProjectileSnapshot(
                    projectile.Id.Value,
                    projectile.SourceTowerId.Value,
                    projectile.Position,
                    projectile.DamageMilli,
                    projectile.LifetimeRemaining,
                    projectile.RadiusMilli,
                    projectile.PierceRemaining,
                    projectile.PiercesUsed,
                    projectile.DirectionXBps,
                    projectile.DirectionYBps,
                    projectile.Homing,
                    projectile.Bindings.Count));
            }

            // 슬롯 배열은 내부 참조를 노출하지 않도록 타워마다 새 배열에 복사한다.
            var towerSnapshots = new TowerSnapshot[towers.Count];
            for (int i = 0; i < towers.Count; i++)
            {
                TowerState tower = towers[i];
                int[] slots = new int[tower.CardInstanceIds.Length];
                Array.Copy(tower.CardInstanceIds, slots, slots.Length);
                towerSnapshots[i] = new TowerSnapshot(
                    tower.Id.Value,
                    content.GetTower(tower.DefinitionId).StableId,
                    tower.BuildPointIndex,
                    tower.Position,
                    slots);
            }

            // 카드 인벤토리는 장착되지 않은 카드까지 모두 노출해 계획 UI가 구성될 수 있게 한다.
            var cardSnapshots = new CardInstanceSnapshot[cards.Count];
            for (int i = 0; i < cards.Count; i++)
            {
                CardInstanceState card = cards[i];
                cardSnapshots[i] = new CardInstanceSnapshot(
                    card.InstanceId,
                    card.DefinitionId,
                    card.Level,
                    card.Equipped,
                    card.EquippedTowerId.Value,
                card.EquippedSlot);
            }

            // Dictionary 순회 순서는 런타임에 의존할 수 있으므로 lineage ID를 정렬한 뒤 복사한다.
            var lineageIds = new List<int>(lineages.Keys);
            lineageIds.Sort();
            var lineageSnapshots =
                new LineageSnapshot[lineageIds.Count];
            for (int i = 0; i < lineageIds.Count; i++)
            {
                LineageState lineage = lineages[lineageIds[i]];
                lineageSnapshots[i] = new LineageSnapshot(
                    lineage.Id.Value,
                    lineage.HighestGeneration,
                    lineage.SplitCount,
                    lineage.SpawnedEntityCount,
                    lineage.LiveMembers,
                    lineage.BaseRewardBudget,
                    lineage.MaxRewardBudget,
                    lineage.PaidReward,
                    lineage.ForfeitedReward,
                    lineage.ProgressBudget,
                    lineage.ConsumedProgress,
                    lineage.AppliedRewardAugments.Count);
            }

            // HashSet도 동일하게 정렬해, UI 표시와 리플레이 비교가 컬렉션 내부 순서에 흔들리지 않는다.
            int[] unlockedDefinitionIds =
                new int[ownedTowerDefinitions.Count];
            ownedTowerDefinitions.CopyTo(unlockedDefinitionIds);
            Array.Sort(unlockedDefinitionIds);
            var unlockedTowerIds =
                new string[unlockedDefinitionIds.Length];
            for (int i = 0; i < unlockedDefinitionIds.Length; i++)
            {
                unlockedTowerIds[i] = content.GetTower(
                    new TowerDefinitionId(
                        unlockedDefinitionIds[i])).StableId;
            }

            return new SimulationSnapshot(
                tick,
                phase,
                currentWaveIndex,
                baseHealth,
                gold,
                enemySnapshots.ToArray(),
                projectileSnapshots.ToArray(),
                towerSnapshots,
                cardSnapshots,
                draftOffers.ToArray(),
                lineageSnapshots,
                unlockedTowerIds);
        }

        /// <summary>
        /// 마지막으로 읽은 이후 발생한 화면 표현용 이벤트를 발생 순서대로 반환하고 버퍼를 비운다.
        /// </summary>
        /// <remarks>
        /// 이 이벤트는 애니메이션, VFX, 사운드, 전투 로그를 위한 알림이다. 게임 판정의 원본이
        /// 아니므로 앞단이 일부를 놓쳐도 전투 결과는 바뀌지 않는다. 한 번 읽은 이벤트는 다음
        /// 호출에 다시 나오지 않는다.
        /// </remarks>
        public SimulationEventBuffer ReadPresentationEvents()
        {
            EnsureInitialized();

            // 원형 버퍼는 논리적 첫 항목이 배열 중간에 있을 수 있어 모듈러 연산으로 순서대로 푼다.
            var copy =
                new SimulationPresentationEvent[presentationEventCount];
            for (int i = 0; i < presentationEventCount; i++)
            {
                copy[i] = presentationEvents[
                    (presentationEventHead + i) %
                    presentationEvents.Length];
            }

            presentationEventHead = 0;
            presentationEventCount = 0;
            return new SimulationEventBuffer(copy);
        }

        /// <summary>
        /// 현재 시뮬레이션의 판정에 영향을 주는 상태 전체를 안정적인 64비트 값으로 요약한다.
        /// </summary>
        /// <remarks>
        /// 같은 콘텐츠, 런 설정, seed, 명령 로그가 매 틱 같은 해시를 내는지 비교하면
        /// Editor와 WebGL의 결정성 회귀를 빠르게 찾을 수 있다. 보안용 암호 해시가 아니라
        /// 디버그/리플레이 검증용이며, 컬렉션은 필요할 때 명시적으로 정렬해서 넣는다.
        /// 새 권위 상태 필드를 추가할 때는 반드시 이 메서드에도 포함해야 한다.
        /// </remarks>
        public ulong ComputeStateHash()
        {
            EnsureInitialized();
            StableHashBuilder hash = default(StableHashBuilder);

            // 먼저 콘텐츠/런 지문과 전역 진행 카운터를 넣는다.
            hash.Add(content.Version);
            hash.Add(content.ContentHash);
            hash.Add(runDefinitionHash);
            hash.Add(initialized);
            hash.Add(tick);
            hash.Add((int)phase);
            hash.Add(currentWaveIndex);
            hash.Add(waveStartTick);
            hash.Add(baseHealth);
            hash.Add(gold);
            hash.Add(eventsProcessedThisTick);
            hash.Add(eventsEnqueuedThisTick);
            hash.Add(nextEntityId);
            hash.Add(nextTowerId);
            hash.Add(nextCardInstanceId);
            hash.Add(nextStatusInstanceId);
            hash.Add(nextHazardId);
            hash.Add(nextChainId);
            hash.Add(nextActivationId);
            hash.Add(combatRandom.State);
            hash.Add(combatRandom.StreamIncrement);
            hash.Add(draftRandom.State);
            hash.Add(draftRandom.StreamIncrement);
            hash.Add(waveRandom.State);
            hash.Add(waveRandom.StreamIncrement);

            // 아직 처리되지 않은 이벤트 역시 미래 결과를 바꾸므로 현재 상태의 일부다.
            eventQueue.AppendStateHash(ref hash);

            // 웨이브 스폰 예약의 진행도도 같은 현재 화면만으로는 알 수 없는 미래 상태다.
            hash.Add(waveSpawns.Length);
            for (int i = 0; i < waveSpawns.Length; i++)
            {
                WaveSpawnRuntime spawn = waveSpawns[i];
                hash.Add(spawn.Definition.EnemyId.Value);
                hash.Add(spawn.Definition.Count);
                hash.Add(spawn.Definition.FirstSpawnTick);
                hash.Add(spawn.Definition.IntervalTicks);
                hash.Add(spawn.Spawned);
                hash.Add(spawn.NextTick);
            }

            AddSortedIntSet(ref hash, ownedTowerDefinitions);

            // 타워와 장착 프로그램은 List의 안정된 순서로 추가한다.
            hash.Add(towers.Count);
            for (int i = 0; i < towers.Count; i++)
            {
                TowerState tower = towers[i];
                hash.Add(tower.Id);
                hash.Add(tower.DefinitionId.Value);
                hash.Add(tower.BuildPointIndex);
                hash.Add(tower.Position);
                hash.Add(tower.CooldownRemaining);
                hash.Add(tower.GoldGeneratedThisWave);
                hash.Add(tower.CardInstanceIds.Length);
                for (int slot = 0; slot < tower.CardInstanceIds.Length; slot++)
                {
                    hash.Add(tower.CardInstanceIds[slot]);
                }

                hash.Add(tower.Program.Length);
                for (int cardIndex = 0;
                     cardIndex < tower.Program.Length;
                     cardIndex++)
                {
                    hash.Add(tower.Program[cardIndex]);
                    hash.Add(tower.ProgramInstances[cardIndex]);
                }

                AddSortedIntSet(ref hash, tower.TargetsInside);
                AddSortedIntLongDictionary(
                    ref hash,
                    tower.LastTargetTriggerTick);
            }

            // 같은 정의 카드라도 인스턴스 ID, 레벨, 장착 상태가 다르므로 모두 포함한다.
            hash.Add(cards.Count);
            for (int i = 0; i < cards.Count; i++)
            {
                CardInstanceState card = cards[i];
                hash.Add(card.InstanceId);
                hash.Add(card.DefinitionId);
                hash.Add(card.Level);
                hash.Add(card.Equipped);
                hash.Add(card.EquippedTowerId);
                hash.Add(card.EquippedSlot);
            }

            // 적은 기본 전투 수치뿐 아니라 상태이상, 사망 바인딩, 보상 변경 이력까지 포함한다.
            hash.Add(enemies.Count);
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState enemy = enemies[i];
                hash.Add(enemy.Id);
                hash.Add(enemy.DefinitionId.Value);
                hash.Add(enemy.LineageId.Value);
                hash.Add(enemy.Generation);
                hash.Add(enemy.PathProgressMilli);
                hash.Add(enemy.Position);
                hash.Add(enemy.HealthMilli);
                hash.Add(enemy.MaxHealthMilli);
                hash.Add(enemy.Armor);
                hash.Add(enemy.BaseSpeedMilliPerTick);
                hash.Add(enemy.SpeedMultiplierBps);
                hash.Add(enemy.SizeMultiplierBps);
                hash.Add(enemy.AreaDamageTakenBps);
                hash.Add(enemy.SingleDamageTakenBps);
                hash.Add(enemy.RewardBudget);
                hash.Add(enemy.WaveProgressBudget);
                hash.Add(enemy.Alive);
                hash.Add(enemy.RewardClaimed);
                hash.Add(enemy.DeathQueued);
                hash.Add(enemy.ControlGauge);
                hash.Add(enemy.ControlThreshold);
                hash.Add(enemy.ControlThresholdStep);
                hash.Add(enemy.Statuses.Count);
                for (int statusIndex = 0; statusIndex < enemy.Statuses.Count; statusIndex++)
                {
                    StatusInstance status = enemy.Statuses[statusIndex];
                    hash.Add(status.InstanceId);
                    hash.Add((int)status.Type);
                    hash.Add(status.SourceEntityId);
                    hash.Add(status.SourceTowerId);
                    hash.Add(status.SourceCardId);
                    hash.Add(status.SourceCardInstanceId);
                    hash.Add(status.Stacks);
                    hash.Add(status.Intensity);
                    hash.Add(status.RemainingTicks);
                    hash.Add(status.MaxStacks);
                    hash.Add(status.TickInterval);
                    hash.Add(status.NextTick);
                    hash.Add(status.Inherited);
                    hash.Add(status.Dispellable);
                    hash.Add(status.Limit);
                    hash.Add(status.RadiusMilli);
                    hash.Add(status.ArmorIgnoreBps);
                }

                hash.Add(enemy.DeathBindings.Count);
                for (int bindingIndex = 0;
                     bindingIndex < enemy.DeathBindings.Count;
                     bindingIndex++)
                {
                    AppendBindingHash(
                        ref hash,
                        enemy.DeathBindings[bindingIndex]);
                }

                AddSortedIntSet(ref hash, enemy.RewardModifiers);
            }

            // 탄환의 충돌 이력과 바인딩은 다음 틱의 관통/보상 결과를 바꾸므로 빠뜨리면 안 된다.
            hash.Add(projectiles.Count);
            for (int i = 0; i < projectiles.Count; i++)
            {
                ProjectileState projectile = projectiles[i];
                hash.Add(projectile.Id);
                hash.Add(projectile.SourceTowerId);
                hash.Add(projectile.Generation);
                hash.Add(projectile.Position);
                hash.Add(projectile.TargetId);
                hash.Add(projectile.DirectionXBps);
                hash.Add(projectile.DirectionYBps);
                hash.Add(projectile.Homing);
                hash.Add(projectile.DamageMilli);
                hash.Add(projectile.SpeedMilliPerTick);
                hash.Add(projectile.RadiusMilli);
                hash.Add(projectile.LifetimeRemaining);
                hash.Add(projectile.PierceRemaining);
                hash.Add(projectile.PiercesUsed);
                hash.Add(projectile.PierceDamageMultiplierBps);
                hash.Add(projectile.CriticalChanceBps);
                hash.Add(projectile.Alive);
                hash.Add(projectile.ExpirationQueued);
                hash.Add(projectile.RootChainId);
                hash.Add(projectile.ActivationId);
                hash.Add(projectile.Bindings.Count);
                for (int bindingIndex = 0;
                     bindingIndex < projectile.Bindings.Count;
                     bindingIndex++)
                {
                    AppendBindingHash(
                        ref hash,
                        projectile.Bindings[bindingIndex]);
                }

                AddSortedIntSet(ref hash, projectile.HitEnemies);
                hash.Add(projectile.UniqueGoldHits);
                hash.Add(projectile.LastTrailPosition);
            }

            // 장판은 남은 시간과 이미 적용한 적 목록까지 미래 판정에 관여한다.
            hash.Add(hazards.Count);
            for (int i = 0; i < hazards.Count; i++)
            {
                HazardState hazard = hazards[i];
                hash.Add(hazard.Id);
                hash.Add((int)hazard.Kind);
                hash.Add(hazard.Position);
                hash.Add(hazard.RadiusMilli);
                hash.Add(hazard.RemainingTicks);
                hash.Add(hazard.SourceTowerId);
                hash.Add(hazard.SourceCardId);
                hash.Add(hazard.SourceCardInstanceId);
                hash.Add(hazard.SourceEntityId);
                hash.Add(hazard.RootChainId);
                AppendEffectNodeHash(ref hash, hazard.Node);
                AddSortedIntSet(ref hash, hazard.AppliedEnemies);
            }

            // 프로그램 프레임 목록에는 완료되어 재사용 대기 중인 슬롯도 있다.
            // 활성 여부와 free stack의 순서까지 넣어 내부 실행 상태를 정확히 구분한다.
            hash.Add(programFrames.Count);
            bool[] freeFrames = new bool[programFrames.Count];
            int[] freeFrameOrder = freeProgramFrames.ToArray();
            for (int i = 0; i < freeFrameOrder.Length; i++)
            {
                int frameIndex = freeFrameOrder[i];
                if (frameIndex >= 0 && frameIndex < freeFrames.Length)
                {
                    freeFrames[frameIndex] = true;
                }
            }

            for (int i = 0; i < programFrames.Count; i++)
            {
                bool active = !freeFrames[i];
                hash.Add(active);
                if (active)
                {
                    AppendProgramFrameHash(ref hash, programFrames[i]);
                }
            }

            hash.Add(freeFrameOrder.Length);
            for (int i = 0; i < freeFrameOrder.Length; i++)
            {
                hash.Add(freeFrameOrder[i]);
            }

            // Dictionary는 키를 정렬한 뒤 RootChain별 소비 예산을 기록한다.
            var chainIds = new List<int>(chainBudgets.Keys);
            chainIds.Sort();
            hash.Add(chainIds.Count);
            for (int i = 0; i < chainIds.Count; i++)
            {
                ChainBudget budget = chainBudgets[chainIds[i]];
                hash.Add(chainIds[i]);
                hash.Add(budget.RootChainId);
                hash.Add(budget.EventsUsed);
                hash.Add(budget.ProjectileSpawnsUsed);
                hash.Add(budget.CardTriggersUsed);
                hash.Add(budget.RecursionsUsed);
                hash.Add(budget.MythicRepeatsUsed);
            }

            // lineage 보상 원장도 ID 순으로 정렬해 분열/복제 후 총보상 보존 상태를 기록한다.
            var lineageIds = new List<int>(lineages.Keys);
            lineageIds.Sort();
            hash.Add(lineageIds.Count);
            for (int i = 0; i < lineageIds.Count; i++)
            {
                LineageState lineage = lineages[lineageIds[i]];
                hash.Add(lineage.Id);
                hash.Add(lineage.HighestGeneration);
                hash.Add(lineage.SplitCount);
                hash.Add(lineage.SpawnedEntityCount);
                hash.Add(lineage.LiveMembers);
                hash.Add(lineage.BaseRewardBudget);
                hash.Add(lineage.MaxRewardBudget);
                hash.Add(lineage.PaidReward);
                hash.Add(lineage.ForfeitedReward);
                hash.Add(lineage.ProgressBudget);
                hash.Add(lineage.ConsumedProgress);
                AppendRewardAugmentKeys(
                    ref hash,
                    lineage.AppliedRewardAugments);
            }

            // 현재 보여 주는 드래프트 순서 자체가 플레이어의 다음 선택지를 바꾸므로 해시에 포함한다.
            hash.Add(draftOffers.Count);
            for (int i = 0; i < draftOffers.Count; i++)
            {
                hash.Add(draftOffers[i]);
            }

            return hash.Finish();
        }

        /// <summary>탄환/적에 붙은 지연 실행 바인딩 하나를 상태 해시에 추가한다.</summary>
        private static void AppendBindingHash(
            ref StableHashBuilder hash,
            EffectBinding binding)
        {
            hash.Add((int)binding.Trigger);
            hash.Add((int)binding.Kind);
            hash.Add(binding.CardId);
            hash.Add(binding.CardInstanceId);
            AppendEffectNodeHash(ref hash, binding.Node);
            hash.Add(binding.Used);
            hash.Add(binding.TriggerCount);
        }

        /// <summary>컴파일된 효과 노드의 모든 수치 인자를 정해진 순서로 해시에 추가한다.</summary>
        private static void AppendEffectNodeHash(
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

        /// <summary>아직 끝나지 않은 카드 프로그램 실행 프레임을 상태 해시에 추가한다.</summary>
        private static void AppendProgramFrameHash(
            ref StableHashBuilder hash,
            in ProgramFrame frame)
        {
            hash.Add((int)frame.SubjectType);
            hash.Add(frame.SubjectId);
            hash.Add(frame.TowerId);
            hash.Add(frame.CardIndex);
            hash.Add(frame.RootChainId);
            hash.Add(frame.ActivationId);
            hash.Add(frame.ParentEventId);
            hash.Add(frame.Depth);
            hash.Add(frame.ReservedContinuationEvents);
        }

        /// <summary>
        /// 순회 순서가 보장되지 않는 정수 집합을 오름차순으로 정렬해 안정적으로 해시한다.
        /// </summary>
        private static void AddSortedIntSet(
            ref StableHashBuilder hash,
            HashSet<int> values)
        {
            int[] sorted = new int[values.Count];
            values.CopyTo(sorted);
            Array.Sort(sorted);
            hash.Add(sorted.Length);
            for (int i = 0; i < sorted.Length; i++)
            {
                hash.Add(sorted[i]);
            }
        }

        /// <summary>
        /// 정수 키 Dictionary를 키 오름차순으로 해시해 런타임의 내부 버킷 순서를 제거한다.
        /// </summary>
        private static void AddSortedIntLongDictionary(
            ref StableHashBuilder hash,
            Dictionary<int, long> values)
        {
            int[] sortedKeys = new int[values.Count];
            values.Keys.CopyTo(sortedKeys, 0);
            Array.Sort(sortedKeys);
            hash.Add(sortedKeys.Length);
            for (int i = 0; i < sortedKeys.Length; i++)
            {
                hash.Add(sortedKeys[i]);
                hash.Add(values[sortedKeys[i]]);
            }
        }

        /// <summary>
        /// 보상 증가가 어느 타워/카드/종류에서 한 번 적용됐는지 정렬해 해시에 추가한다.
        /// </summary>
        private static void AppendRewardAugmentKeys(
            ref StableHashBuilder hash,
            HashSet<RewardAugmentKey> values)
        {
            var sorted = new List<RewardAugmentKey>(values);
            sorted.Sort((left, right) =>
            {
                int towerComparison =
                    left.TowerId.Value.CompareTo(right.TowerId.Value);
                if (towerComparison != 0)
                {
                    return towerComparison;
                }

                int cardComparison =
                    left.CardInstanceId.CompareTo(right.CardInstanceId);
                return cardComparison != 0
                    ? cardComparison
                    : ((int)left.Kind).CompareTo((int)right.Kind);
            });

            hash.Add(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
            {
                hash.Add(sorted[i].TowerId);
                hash.Add(sorted[i].CardInstanceId);
                hash.Add((int)sorted[i].Kind);
            }
        }

        /// <summary>
        /// 주어진 효과 연산을 현재 기본 실행기 목록이 처리할 수 있는지 콘텐츠 도구가 확인할 때 쓴다.
        /// </summary>
        public static bool IsEffectOperationSupported(EffectOperation operation)
        {
            return EffectRegistry.CreateDefault().IsRegistered(operation);
        }

        /// <summary>
        /// 모든 카드의 탄환/적 해석이 등록된 실행기로 구성됐는지 초기화 시 한 번 검사한다.
        /// </summary>
        private void ValidateExecutors()
        {
            for (int cardIndex = 0;
                 cardIndex < content.CardCount;
                 cardIndex++)
            {
                CompiledCardDefinition card =
                    content.GetCard(new CardId(cardIndex));
                ValidateNodes(
                    card.StableId,
                    card.ProjectileEffectsInternal);
                ValidateNodes(
                    card.StableId,
                    card.EnemyEffectsInternal);
            }
        }

        /// <summary>
        /// 카드 한쪽 해석이 비어 있거나 알 수 없는 효과 연산을 쓰면 실행 전에 콘텐츠 오류로 막는다.
        /// </summary>
        private void ValidateNodes(string cardId, CompiledEffectNode[] nodes)
        {
            if (nodes == null || nodes.Length == 0)
            {
                throw new ContentValidationException(
                    "Card '" + cardId + "' has an empty interpretation.");
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                if (!effectRegistry.IsRegistered(nodes[i].Operation))
                {
                    throw new ContentValidationException(
                        "Card '" + cardId + "' uses unregistered operation " +
                        nodes[i].Operation + ".");
                }
            }
        }

        /// <summary>초기화 전 API 호출이 조용히 잘못 동작하지 않도록 즉시 개발 오류를 알린다.</summary>
        private void EnsureInitialized()
        {
            if (!initialized)
            {
                throw new InvalidOperationException("GameSimulation.Initialize must be called first.");
            }
        }

        /// <summary>
        /// 런 데이터가 현재 콘텐츠 범위와 결정적 시뮬레이션의 필수 조건을 만족하는지 검사한다.
        /// </summary>
        /// <remarks>
        /// 이 검사는 플레이 중 명령 오류가 아니라 제작/로딩 단계의 설정 오류를 찾기 위한 것이다.
        /// 따라서 잘못된 값은 <see cref="ArgumentException"/>으로 즉시 실패시킨다.
        /// </remarks>
        private void ValidateRunConfig(CompiledRunDefinition definition)
        {
            if (definition.TickRate != SafetyLimits.DefaultTicksPerSecond ||
                definition.BaseHealth <= 0 ||
                definition.StartingGold < 0 ||
                definition.StartingTowerChoicesInternal.Length == 0 ||
                definition.StartingCardsInternal.Length == 0 ||
                definition.BuildSpotsInternal.Length == 0 ||
                definition.PathPointsInternal.Length < 2 ||
                definition.DraftOfferCount <= 0 ||
                definition.TierWeightsInternal.Length != 5 ||
                definition.CriticalDamageBps < 10000 ||
                definition.ControlInterruptTicks <= 0 ||
                definition.MaxControlGaugeThreshold <= 0 ||
                definition.EnemyBaseHitRadiusMilli <= 0)
            {
                throw new ArgumentException(
                    "RunConfig contains an invalid deterministic run definition.",
                    nameof(definition));
            }

            for (int i = 0;
                 i < definition.StartingTowerChoicesInternal.Length;
                 i++)
            {
                int id = definition.StartingTowerChoicesInternal[i].Value;
                if (id < 0 || id >= content.TowerCount)
                {
                    throw new ArgumentException(
                        "RunConfig references an incompatible starting tower.",
                        nameof(definition));
                }
            }

            for (int i = 0;
                 i < definition.InitiallyUnlockedTowersInternal.Length;
                 i++)
            {
                int id = definition.InitiallyUnlockedTowersInternal[i].Value;
                if (id < 0 || id >= content.TowerCount)
                {
                    throw new ArgumentException(
                        "RunConfig references an incompatible unlocked tower.",
                        nameof(definition));
                }
            }

            for (int i = 0;
                 i < definition.StartingCardsInternal.Length;
                 i++)
            {
                int id = definition.StartingCardsInternal[i].Value;
                if (id < 0 || id >= content.CardCount)
                {
                    throw new ArgumentException(
                        "RunConfig references an incompatible starting card.",
                        nameof(definition));
                }
            }

            int totalTierWeight = 0;
            for (int i = 0;
                 i < definition.TierWeightsInternal.Length;
                 i++)
            {
                if (definition.TierWeightsInternal[i] < 0)
                {
                    throw new ArgumentException(
                        "RunConfig tier weights cannot be negative.",
                        nameof(definition));
                }

                totalTierWeight = checked(
                    totalTierWeight +
                    definition.TierWeightsInternal[i]);
            }

            if (totalTierWeight <= 0)
            {
                throw new ArgumentException(
                    "RunConfig tier weights require a positive total.",
                    nameof(definition));
            }
        }

        /// <summary>
        /// 판정 결과를 바꾸지 않는 화면용 알림 하나를 고정 크기 원형 버퍼에 기록한다.
        /// </summary>
        /// <remarks>
        /// 버퍼가 가득 차면 가장 오래된 항목을 덮어쓴다. 이는 소비되지 않은 VFX 알림 때문에
        /// WebGL 메모리가 무한히 증가하는 일을 막으며, 권위 상태에는 아무 영향이 없다.
        /// </remarks>
        private void AddPresentation(
            PresentationEventType type,
            int subjectId = -1,
            int sourceId = -1,
            int value = 0,
            string contentId = null)
        {
            var presentationEvent = new SimulationPresentationEvent(
                tick,
                type,
                subjectId,
                sourceId,
                value,
                contentId);
            if (presentationEventCount < presentationEvents.Length)
            {
                // 아직 빈 칸이 있으면 논리적 꼬리 위치에 이어 쓴다.
                int writeIndex =
                    (presentationEventHead + presentationEventCount) %
                    presentationEvents.Length;
                presentationEvents[writeIndex] = presentationEvent;
                presentationEventCount++;
                return;
            }

            // 가득 찼다면 현재 머리(가장 오래된 이벤트)를 덮고 머리를 한 칸 전진시킨다.
            presentationEvents[presentationEventHead] = presentationEvent;
            presentationEventHead =
                (presentationEventHead + 1) %
                presentationEvents.Length;
        }

        /// <summary>
        /// 웨이브 데이터의 스폰 묶음 하나에 대한 런타임 진행도다.
        /// </summary>
        /// <remarks>
        /// <see cref="Definition"/>은 불변 설정이고, <see cref="Spawned"/>와
        /// <see cref="NextTick"/>만 웨이브가 진행되면서 변한다.
        /// </remarks>
        private sealed class WaveSpawnRuntime
        {
            public CompiledWaveSpawn Definition;
            public int Spawned;
            public long NextTick;
        }
    }
}
