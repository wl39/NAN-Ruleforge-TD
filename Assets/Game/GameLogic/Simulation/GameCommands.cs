using System;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 한 번의 런에서 사용할 규칙 묶음과 그 규칙의 결정성 지문을 보관한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="CompiledContent"/>가 전체 콘텐츠 카탈로그라면, RunConfig는 그중
    /// 시작 카드, 시작 타워, 경로, 건설 지점처럼 "이번 런을 어떻게 진행할지"에
    /// 해당하는 설정이다.
    /// </para>
    /// <para>
    /// DefinitionHash는 리플레이 비교용이다. 같은 시드라도 경로나 시작 카드가
    /// 다르면 다른 런이므로, 해당 차이가 상태 해시에 반드시 반영되게 한다.
    /// </para>
    /// </remarks>
    public sealed class RunConfig
    {
        /// <summary>
        /// 검증·컴파일이 끝난 런 정의를 감싸고 즉시 지문을 계산한다.
        /// 생성 이후 정의가 바뀌지 않는다는 전제에서 사용한다.
        /// </summary>
        public RunConfig(CompiledRunDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            DefinitionHash = ComputeDefinitionHash(definition);
        }

        /// <summary>시뮬레이션이 실제로 읽는 컴파일된 런 규칙이다.</summary>
        public CompiledRunDefinition Definition { get; }

        /// <summary>
        /// 런 규칙의 모든 결정적 필드를 정해진 순서로 계산한 64비트 지문이다.
        /// </summary>
        public ulong DefinitionHash { get; }

        /// <summary>
        /// 콘텐츠에 포함된 기본 Run 정의로 RunConfig를 만든다.
        /// 별도 모드나 테스트 규칙을 주입하지 않을 때 사용하는 편의 함수다.
        /// </summary>
        public static RunConfig FromContent(CompiledContent content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            return new RunConfig(content.Run);
        }

        private static ulong ComputeDefinitionHash(
            CompiledRunDefinition definition)
        {
            // 해시에는 "전투 결과에 영향을 줄 수 있는 값"을 모두, 항상 같은
            // 순서로 넣는다. 컬렉션 길이를 먼저 넣는 이유는 [1, 23]과
            // [12, 3]처럼 값의 경계가 다른 배열을 명확히 구분하기 위해서다.
            StableHashBuilder hash = default(StableHashBuilder);
            hash.Add(definition.TickRate);
            hash.Add(definition.BaseHealth);
            hash.Add(definition.StartingGold);

            TowerDefinitionId[] startingChoices =
                definition.StartingTowerChoices;
            hash.Add(startingChoices.Length);
            for (int i = 0; i < startingChoices.Length; i++)
            {
                hash.Add(startingChoices[i].Value);
            }

            TowerDefinitionId[] initiallyUnlocked =
                definition.InitiallyUnlockedTowers;
            hash.Add(initiallyUnlocked.Length);
            for (int i = 0; i < initiallyUnlocked.Length; i++)
            {
                hash.Add(initiallyUnlocked[i].Value);
            }

            CardId[] startingCards = definition.StartingCards;
            hash.Add(startingCards.Length);
            for (int i = 0; i < startingCards.Length; i++)
            {
                hash.Add(startingCards[i].Value);
            }

            AppendPositions(ref hash, definition.BuildSpots);
            AppendPositions(ref hash, definition.PathPoints);
            hash.Add(definition.DraftOfferCount);

            int[] tierWeights = definition.TierWeights;
            hash.Add(tierWeights.Length);
            for (int i = 0; i < tierWeights.Length; i++)
            {
                hash.Add(tierWeights[i]);
            }

            hash.Add(definition.CriticalDamageBps);
            hash.Add(definition.ControlInterruptTicks);
            hash.Add(definition.MaxControlGaugeThreshold);
            hash.Add(definition.EnemyBaseHitRadiusMilli);
            return hash.Finish();
        }

        private static void AppendPositions(
            ref StableHashBuilder hash,
            SimPosition[] positions)
        {
            hash.Add(positions.Length);
            for (int i = 0; i < positions.Length; i++)
            {
                hash.Add(positions[i]);
            }
        }
    }

    /// <summary>
    /// 런의 큰 진행 단계를 나타낸다.
    /// UI는 이 값에 따라 시작 선택, 카드 편집, 전투, 드래프트 화면을 전환한다.
    /// </summary>
    public enum RunPhase
    {
        /// <summary>아직 시작 타워를 고르지 않은 최초 상태다.</summary>
        AwaitingStartingTower = 0,

        /// <summary>타워를 놓고 카드를 편집할 수 있는 웨이브 사이 상태다.</summary>
        Planning = 1,

        /// <summary>고정 틱 전투가 진행되며 카드 편집은 잠기는 상태다.</summary>
        Combat = 2,

        /// <summary>웨이브 보상 카드 중 하나를 선택하는 상태다.</summary>
        Draft = 3,

        /// <summary>마지막 웨이브까지 방어한 종료 상태다.</summary>
        Victory = 4,

        /// <summary>본진 체력이 0이 된 종료 상태다.</summary>
        Defeat = 5
    }

    /// <summary>
    /// 외부 입력을 시뮬레이션에 전달할 때 사용하는 명령 종류다.
    /// 버튼, 드래그 앤 드롭, 네트워크 리플레이 모두 결국 이 명령으로 변환된다.
    /// </summary>
    public enum GameCommandType
    {
        ChooseStartingTower = 0,
        PlaceTower = 1,
        EquipCard = 2,
        ReorderCard = 3,
        SelectDraft = 4,
        StartWave = 5,
        MoveCard = 6,
        UnequipCard = 7
    }

    /// <summary>
    /// 명령이 거절된 이유를 기계적으로 구분하는 코드다.
    /// 화면에는 이 값 자체보다 로컬라이즈된 안내 문구를 보여주는 것이 좋다.
    /// </summary>
    public enum CommandError
    {
        /// <summary>오류가 없고 명령이 승인되었다.</summary>
        None = 0,

        /// <summary>현재 런 단계에서 허용되지 않는 명령이다.</summary>
        InvalidPhase = 1,

        /// <summary>카드·타워 등 요청한 콘텐츠 ID가 존재하지 않는다.</summary>
        UnknownContent = 2,

        /// <summary>인스턴스 ID나 슬롯 등 명령 대상이 유효하지 않다.</summary>
        InvalidTarget = 3,

        /// <summary>플레이어가 보유하지 않은 타워나 카드를 사용하려 했다.</summary>
        NotOwned = 4,

        /// <summary>이미 다른 타워가 있는 고정 건설 지점이다.</summary>
        BuildPointOccupied = 5,

        /// <summary>타워가 제공하지 않는 슬롯 번호다.</summary>
        SlotOutOfRange = 6,

        /// <summary>목적지 슬롯에 이미 카드가 있다.</summary>
        SlotOccupied = 7,

        /// <summary>장착 카드 비용 합계가 타워 연산력을 초과한다.</summary>
        ComputeCapacityExceeded = 8,

        /// <summary>현재 드래프트 제안에 없는 인덱스를 선택했다.</summary>
        InvalidDraftChoice = 9,

        /// <summary>전투 중이라 장착·이동·재정렬을 할 수 없다.</summary>
        CombatLoadoutLocked = 10
    }

    /// <summary>
    /// <see cref="GameSimulation.Submit"/>의 처리 결과다.
    /// </summary>
    /// <remarks>
    /// 거절된 명령은 시뮬레이션 상태를 바꾸지 않는다. 따라서 UI는 Accepted가
    /// false일 때 원래 드래그 위치로 카드를 되돌리는 식으로 안전하게 처리할 수 있다.
    /// </remarks>
    public readonly struct CommandResult
    {
        private CommandResult(bool accepted, CommandError error, string message)
        {
            Accepted = accepted;
            Error = error;
            Message = message ?? string.Empty;
        }

        /// <summary>명령이 검증을 통과해 적용되었으면 true다.</summary>
        public bool Accepted { get; }

        /// <summary>거절 사유 코드이며 성공 시 <see cref="CommandError.None"/>이다.</summary>
        public CommandError Error { get; }

        /// <summary>개발·디버그용 상세 설명이다. 최종 UI 문구의 원본으로 쓰지 않는다.</summary>
        public string Message { get; }

        /// <summary>상태 변경이 정상 적용되었음을 나타내는 결과를 만든다.</summary>
        public static CommandResult Success()
        {
            return new CommandResult(true, CommandError.None, string.Empty);
        }

        /// <summary>상태 변경 없이 명령을 거절하는 결과를 만든다.</summary>
        public static CommandResult Reject(CommandError error, string message)
        {
            return new CommandResult(false, error, message);
        }
    }

    /// <summary>
    /// 플레이어 의도를 시뮬레이션에 전달하는 작고 직렬화 가능한 값이다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 명령마다 별도 클래스를 만들지 않고 공통 필드 세 개를 재사용한다.
    /// 각 필드의 의미는 <see cref="Type"/>에 따라 달라지므로, 외부에서는 아래의
    /// 정적 생성 함수를 사용해야 한다.
    /// </para>
    /// <para>
    /// 이 값과 Step 호출 순서를 저장하면 같은 콘텐츠·시드에서 리플레이할 수 있다.
    /// Unity의 GameObject나 화면 좌표는 포함하지 않는다.
    /// </para>
    /// </remarks>
    public readonly struct GameCommand
    {
        private GameCommand(
            GameCommandType type,
            string contentId,
            int primaryId,
            int secondaryId,
            int tertiaryId)
        {
            Type = type;
            ContentId = contentId ?? string.Empty;
            PrimaryId = primaryId;
            SecondaryId = secondaryId;
            TertiaryId = tertiaryId;
        }

        /// <summary>이 명령을 어떻게 해석할지 결정하는 종류다.</summary>
        public GameCommandType Type { get; }

        /// <summary>타워 정의처럼 문자열 안정 ID가 필요한 명령에서 사용한다.</summary>
        public string ContentId { get; }

        /// <summary>명령별 첫 번째 정수 인자다.</summary>
        public int PrimaryId { get; }

        /// <summary>명령별 두 번째 정수 인자다.</summary>
        public int SecondaryId { get; }

        /// <summary>명령별 세 번째 정수 인자다.</summary>
        public int TertiaryId { get; }

        /// <summary>런 시작 시 보유할 대표 타워를 선택한다.</summary>
        public static GameCommand ChooseStartingTower(string towerId)
        {
            return new GameCommand(
                GameCommandType.ChooseStartingTower,
                towerId,
                -1,
                -1,
                -1);
        }

        /// <summary>보유 타워 정의를 고정 건설 지점에 배치한다.</summary>
        public static GameCommand PlaceTower(string towerId, int buildPointIndex)
        {
            return new GameCommand(
                GameCommandType.PlaceTower,
                towerId,
                buildPointIndex,
                -1,
                -1);
        }

        /// <summary>인벤토리 카드 인스턴스를 특정 타워 슬롯에 장착한다.</summary>
        public static GameCommand EquipCard(
            int cardInstanceId,
            int towerInstanceId,
            int slotIndex)
        {
            return new GameCommand(
                GameCommandType.EquipCard,
                string.Empty,
                cardInstanceId,
                towerInstanceId,
                slotIndex);
        }

        /// <summary>
        /// 같은 타워 안의 두 슬롯 위치를 바꿔 카드 실행 순서를 변경한다.
        /// </summary>
        public static GameCommand ReorderCard(
            int towerInstanceId,
            int fromSlot,
            int toSlot)
        {
            return new GameCommand(
                GameCommandType.ReorderCard,
                string.Empty,
                towerInstanceId,
                fromSlot,
                toSlot);
        }

        /// <summary>
        /// 이미 장착되었을 수도 있는 카드를 목적 타워·슬롯으로 이동한다.
        /// EquipCard와 달리 원래 슬롯 해제를 함께 처리하는 UI 드래그용 명령이다.
        /// </summary>
        public static GameCommand MoveCard(
            int cardInstanceId,
            int towerInstanceId,
            int slotIndex)
        {
            return new GameCommand(
                GameCommandType.MoveCard,
                string.Empty,
                cardInstanceId,
                towerInstanceId,
                slotIndex);
        }

        /// <summary>장착 카드를 타워에서 빼 인벤토리로 돌려놓는다.</summary>
        public static GameCommand UnequipCard(int cardInstanceId)
        {
            return new GameCommand(
                GameCommandType.UnequipCard,
                string.Empty,
                cardInstanceId,
                -1,
                -1);
        }

        /// <summary>현재 제시된 드래프트 카드의 0 기반 인덱스를 선택한다.</summary>
        public static GameCommand SelectDraft(int offerIndex)
        {
            return new GameCommand(
                GameCommandType.SelectDraft,
                string.Empty,
                offerIndex,
                -1,
                -1);
        }

        /// <summary>
        /// 계획을 확정하고 다음 웨이브 전투를 시작한다.
        /// 이 순간 타워별 카드 프로그램이 불변 복사본으로 고정된다.
        /// </summary>
        public static GameCommand StartWave()
        {
            return new GameCommand(
                GameCommandType.StartWave,
                string.Empty,
                -1,
                -1,
                -1);
        }
    }
}
