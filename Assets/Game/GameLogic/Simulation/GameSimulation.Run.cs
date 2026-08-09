using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Simulation
{
    public sealed partial class GameSimulation
    {
        /*
         * 이 partial 파일은 한 번의 런(run)이 어떤 단계로 진행되는지를 담당한다.
         *
         * Unity에서 흔히 사용하는 MonoBehaviour의 Start/Update 같은 화면 생명주기는 이곳에 없다.
         * 대신 외부에서 전달된 GameCommand를 검증한 뒤, 현재 phase를
         * 시작 타워 선택 → 계획 → 전투 → 드래프트 → 계획 … → 승리/패배 순으로 바꾼다.
         *
         * 이 파일의 중요한 책임은 크게 네 가지다.
         * 1. 플레이어가 보유한 타워와 카드가 실제로 사용 가능한지 검증한다.
         * 2. 카드의 슬롯 비용과 타워의 연산력 한도를 검사한다.
         * 3. 웨이브 스폰 일정과 웨이브 종료 조건을 관리한다.
         * 4. 드래프트 후보를 결정적 난수로 생성한다.
         *
         * 화면 버튼, 드래그 앤 드롭, 애니메이션은 이 메서드들을 직접 구현하는 대신
         * Submit(...)으로 명령을 보내고 CommandResult와 스냅샷을 읽어 표현하면 된다.
         */

        /// <summary>
        /// 런을 시작할 때 제시된 후보 중 하나를 최초 보유 타워로 확정한다.
        /// </summary>
        /// <remarks>
        /// stableId는 JSON 등 외부 데이터에서 사용하는 사람이 읽기 쉬운 문자열 ID다.
        /// 시뮬레이션 내부에서는 이를 정수 기반 <see cref="TowerDefinitionId"/>로 변환해 사용한다.
        /// 시작 선택은 정확히 한 번만 가능하며, 성공하면 런 단계가 계획 단계로 넘어간다.
        /// </remarks>
        private CommandResult ChooseStartingTower(string stableId)
        {
            // 현재 단계부터 확인해야 이미 시작한 런에서 시작 타워를 다시 얻는 일을 막을 수 있다.
            if (phase != RunPhase.AwaitingStartingTower)
            {
                return CommandResult.Reject(
                    CommandError.InvalidPhase,
                    "Starting tower can only be chosen once.");
            }

            // 문자열 ID가 콘텐츠에 없으면 이후 배열 조회가 불가능하므로 명시적으로 거절한다.
            if (!content.TryGetTowerId(stableId, out TowerDefinitionId towerId))
            {
                return CommandResult.Reject(
                    CommandError.UnknownContent,
                    "Unknown tower '" + stableId + "'.");
            }

            // 콘텐츠에 존재하는 것과 이번 런의 시작 후보로 제시된 것은 별개의 조건이다.
            bool offered = false;
            for (int i = 0;
                 i < run.StartingTowerChoicesInternal.Length;
                 i++)
            {
                if (run.StartingTowerChoicesInternal[i] == towerId)
                {
                    offered = true;
                    break;
                }
            }

            if (!offered)
            {
                return CommandResult.Reject(
                    CommandError.NotOwned,
                    "Tower is not a starting choice.");
            }

            // HashSet에 정의 ID를 넣으므로 같은 타워가 여러 경로로 해금돼도 중복되지 않는다.
            ownedTowerDefinitions.Add(towerId.Value);
            for (int i = 0;
                 i < run.InitiallyUnlockedTowersInternal.Length;
                 i++)
            {
                ownedTowerDefinitions.Add(
                    run.InitiallyUnlockedTowersInternal[i].Value);
            }

            // 이제부터는 타워 배치와 카드 편집이 허용되는 계획 단계다.
            phase = RunPhase.Planning;
            return CommandResult.Success();
        }

        /// <summary>
        /// 보유한 타워 정의를 지정된 고정 건설 지점에 실제 타워 인스턴스로 배치한다.
        /// </summary>
        /// <remarks>
        /// 타워 “정의”는 슬롯 수와 연산력 같은 공용 설계 데이터이고,
        /// <see cref="TowerState"/>는 이번 런에서만 존재하는 개별 타워다.
        /// 같은 정의의 타워라도 각 인스턴스는 별도의 ID, 카드 슬롯, 재사용 대기시간을 가진다.
        /// </remarks>
        private CommandResult PlaceTower(string stableId, int buildPointIndex)
        {
            // 고정 건설 지점과 명령 순서는 결정적이므로 전투 중에도 설치할 수 있다.
            // 카드 편집은 별도 규칙으로 전투 중 계속 잠근다.
            if (phase != RunPhase.Planning &&
                phase != RunPhase.Combat)
            {
                return CommandResult.Reject(
                    CommandError.InvalidPhase,
                    "Towers can only be placed during planning or combat.");
            }

            if (!content.TryGetTowerId(stableId, out TowerDefinitionId definitionId))
            {
                return CommandResult.Reject(
                    CommandError.UnknownContent,
                    "Unknown tower '" + stableId + "'.");
            }

            // 콘텐츠에 존재하더라도 플레이어가 아직 획득하지 않은 타워는 배치할 수 없다.
            if (!ownedTowerDefinitions.Contains(definitionId.Value))
            {
                return CommandResult.Reject(
                    CommandError.NotOwned,
                    "Tower has not been acquired.");
            }

            // 건설 지점은 RunConfig의 고정 배열 인덱스로 표현되므로 먼저 범위를 검사한다.
            if (buildPointIndex < 0 ||
                buildPointIndex >= run.BuildSpotsInternal.Length)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "Build point is out of range.");
            }

            if (!unlockedBuildSpots[buildPointIndex])
            {
                return CommandResult.Reject(
                    CommandError.BuildPointLocked,
                    "Build point is locked.");
            }

            // 자유 좌표 배치가 아니라 한 지점당 한 타워인 규칙을 전체 타워 목록에서 확인한다.
            for (int i = 0; i < towers.Count; i++)
            {
                if (towers[i].BuildPointIndex == buildPointIndex)
                {
                    return CommandResult.Reject(
                        CommandError.BuildPointOccupied,
                        "Build point is occupied.");
                    }
            }

            CompiledTowerDefinition definition =
                content.GetTower(definitionId);
            TowerConstructionQuote constructionQuote =
                CreateTowerConstructionQuote(
                    definitionId,
                    definition.StableId);
            if (!constructionQuote.CanAfford)
            {
                return CommandResult.Reject(
                    CommandError.InsufficientGold,
                    "Not enough gold to construct this tower.");
            }

            gold = checked(gold - constructionQuote.Cost);
            CreateTowerInstance(
                definitionId,
                buildPointIndex);
            return CommandResult.Success();
        }

        private CommandResult GrantDebugGold(int amount)
        {
            const int maximumSingleGrant = 1000000;
            if (amount <= 0 ||
                amount > maximumSingleGrant ||
                gold > int.MaxValue - amount)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "Debug gold amount is outside the safe range.");
            }

            gold = checked(gold + amount);
            AddPresentation(
                PresentationEventType.RewardGranted,
                -1,
                -1,
                amount,
                "debug.konami");
            return CommandResult.Success();
        }

        /// <summary>
        /// Stage 01 타워의 레벨을 한 단계 올리고 해당 단계의 골드를 지불한다.
        /// </summary>
        private CommandResult UpgradeTower(int towerInstanceId)
        {
            if (!IsLoadoutEditablePhase())
            {
                return CommandResult.Reject(
                    phase == RunPhase.Combat
                        ? CommandError.CombatLoadoutLocked
                        : CommandError.InvalidPhase,
                    "Towers can only be upgraded outside combat.");
            }

            TowerState tower =
                FindTower(new TowerId(towerInstanceId));
            if (tower == null)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "Tower instance does not exist.");
            }

            TowerUpgradeQuote upgradeQuote =
                CreateTowerUpgradeQuote(tower);
            if (upgradeQuote.IsMaximumLevel)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "Tower is already at maximum level.");
            }

            if (!upgradeQuote.HasNextLevel)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "Tower upgrade data is unavailable.");
            }

            if (!upgradeQuote.CanAfford)
            {
                return CommandResult.Reject(
                    CommandError.InsufficientGold,
                    "Not enough gold to upgrade this tower.");
            }

            gold = checked(gold - upgradeQuote.Cost);
            tower.Level = upgradeQuote.TargetLevel;
            AddPresentation(
                PresentationEventType.TowerUpgraded,
                tower.Id.Value,
                -1,
                tower.Level,
                content.GetTower(tower.DefinitionId).StableId);
            return CommandResult.Success();
        }

        /// <summary>
        /// 타워가 장착한 모든 카드에 사용할 탄환/적 해석을 선택한다.
        /// 카드별로 문맥을 섞지 않아 실행 문장의 주체가 항상 하나로 유지된다.
        /// </summary>
        private CommandResult SetTowerSubjectType(
            int towerInstanceId,
            int rawSubjectType)
        {
            if (!IsLoadoutEditablePhase())
            {
                return CommandResult.Reject(
                    phase == RunPhase.Combat
                        ? CommandError.CombatLoadoutLocked
                        : CommandError.InvalidPhase,
                    "Tower card interpretation cannot change during combat.");
            }

            if (rawSubjectType != (int)SubjectType.Projectile &&
                rawSubjectType != (int)SubjectType.Enemy)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "Unknown tower subject type.");
            }

            TowerState tower =
                FindTower(new TowerId(towerInstanceId));
            if (tower == null)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "Tower instance does not exist.");
            }

            SubjectType requested =
                (SubjectType)rawSubjectType;
            CompiledTowerDefinition definition =
                content.GetTower(tower.DefinitionId);
            if (requested == SubjectType.Projectile &&
                definition.Trigger != TowerTrigger.Attack)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "This tower trigger cannot create a projectile subject.");
            }

            tower.SubjectType = requested;
            for (int slot = 0;
                 slot < tower.CardSubjectTypes.Length;
                 slot++)
            {
                tower.CardSubjectTypes[slot] = requested;
            }
            AddPresentation(
                PresentationEventType.TowerSubjectTypeChanged,
                tower.Id.Value,
                -1,
                rawSubjectType,
                content.GetTower(tower.DefinitionId).StableId);
            return CommandResult.Success();
        }

        /// <summary>
        /// 한 슬롯만 독립적으로 탄환/적 해석으로 바꾼다. 기존 타워 단위
        /// 명령은 호환성을 위해 모든 슬롯을 한 번에 바꾸는 단축 명령으로
        /// 유지한다.
        /// </summary>
        private CommandResult SetTowerSlotSubjectType(
            int towerInstanceId,
            int slotIndex,
            int rawSubjectType)
        {
            if (!IsLoadoutEditablePhase())
            {
                return CommandResult.Reject(
                    phase == RunPhase.Combat
                        ? CommandError.CombatLoadoutLocked
                        : CommandError.InvalidPhase,
                    "Tower slot interpretation cannot change during combat.");
            }

            if (rawSubjectType != (int)SubjectType.Projectile &&
                rawSubjectType != (int)SubjectType.Enemy)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "Unknown tower slot subject type.");
            }

            TowerState tower =
                FindTower(new TowerId(towerInstanceId));
            if (tower == null)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "Tower instance does not exist.");
            }

            int unlockedSlots =
                GetTowerUnlockedSlotCount(tower);
            if (slotIndex < 0 || slotIndex >= unlockedSlots)
            {
                return CommandResult.Reject(
                    CommandError.SlotOutOfRange,
                    "Tower slot is not unlocked.");
            }

            SubjectType requested = (SubjectType)rawSubjectType;
            CompiledTowerDefinition definition =
                content.GetTower(tower.DefinitionId);
            if (requested == SubjectType.Projectile &&
                definition.Trigger != TowerTrigger.Attack)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "This tower trigger cannot create a projectile subject.");
            }

            tower.CardSubjectTypes[slotIndex] = requested;
            tower.SubjectType = requested;
            AddPresentation(
                PresentationEventType.TowerSubjectTypeChanged,
                tower.Id.Value,
                slotIndex,
                rawSubjectType,
                definition.StableId);
            return CommandResult.Success();
        }

        /// <summary>
        /// 보유 카드 인스턴스를 타워의 지정 슬롯부터 장착한다.
        /// </summary>
        /// <remarks>
        /// 카드는 정의 ID와 인스턴스 ID를 구분한다. 같은 종류의 카드를 두 장 보유하면
        /// 정의 ID는 같지만 인스턴스 ID는 다르다. 이 메서드는 실제 소유 카드 한 장을 이동한다.
        ///
        /// 장착 검사는 원본 슬롯 배열을 바로 고치지 않고 복사본(candidateSlots)에서 먼저 수행한다.
        /// 따라서 실패한 명령은 중간 상태를 남기지 않으며, 성공할 때만 실제 상태를 변경한다.
        /// </remarks>
        private CommandResult EquipCard(
            int cardInstanceId,
            int towerInstanceId,
            int slotIndex,
            bool bypassPhaseLock = false)
        {
            // 카드 배열은 전투 시작 후 프로그램처럼 고정되어야 하므로 계획 단계만 편집 가능하다.
            if (!bypassPhaseLock &&
                !IsLoadoutEditablePhase())
            {
                return CommandResult.Reject(
                    phase == RunPhase.Combat
                        ? CommandError.CombatLoadoutLocked
                        : CommandError.InvalidPhase,
                    "Cards cannot be changed during combat.");
            }

            // UI가 오래된 스냅샷의 ID를 보낼 수도 있으므로 두 인스턴스가 현재도 존재하는지 확인한다.
            CardInstanceState card = FindCardInstance(cardInstanceId);
            TowerState tower = FindTower(new TowerId(towerInstanceId));
            if (card == null || tower == null)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "Card or tower instance does not exist.");
            }

            CompiledCardDefinition definition = content.GetCard(card.DefinitionId);
            int unlockedSlotCount =
                GetTowerUnlockedSlotCount(tower);

            // 슬롯 비용이 2인 카드는 시작 칸과 바로 다음 칸을 함께 차지한다.
            if (slotIndex < 0 ||
                slotIndex + definition.SlotCost > unlockedSlotCount)
            {
                return CommandResult.Reject(
                    CommandError.SlotOutOfRange,
                    "Card does not fit in the requested slot.");
            }

            // 같은 타워 안에서 카드를 옮기는 경우를 위해 복사본에서 기존 점유를 먼저 지운다.
            int[] candidateSlots =
                (int[])tower.CardInstanceIds.Clone();
            ClearCardFromSlots(candidateSlots, cardInstanceId);
            if (!CanPlaceCardInSlots(
                    candidateSlots,
                    cardInstanceId,
                    definition.SlotCost,
                    slotIndex))
            {
                return CommandResult.Reject(
                    CommandError.SlotOccupied,
                    "Requested slot is occupied.");
            }

            // excludedCardInstanceId를 넘겨 이동 중인 카드의 기존 비용이 두 번 합산되지 않게 한다.
            int currentCompute = ComputeTowerCost(tower, cardInstanceId);
            if (currentCompute + definition.ComputeCost >
                GetTowerLevelBalance(tower).ComputeCapacity)
            {
                return CommandResult.Reject(
                    CommandError.ComputeCapacityExceeded,
                    "Tower compute capacity would be exceeded.");
            }

            // 이미 다른 타워에 장착돼 있었다면 이전 타워에서 먼저 제거한다.
            // 이전 타워의 실행 프로그램도 즉시 다시 컴파일해 낡은 카드가 남지 않게 한다.
            if (card.Equipped)
            {
                TowerState previousTower = FindTower(card.EquippedTowerId);
                if (previousTower != null)
                {
                    ClearCardFromTower(previousTower, card.InstanceId);
                    CompileTowerProgram(previousTower);
                }
            }

            // 모든 검증이 끝난 뒤에만 실제 슬롯 배열을 변경한다.
            PlaceCardInSlots(
                tower.CardInstanceIds,
                card.InstanceId,
                definition.SlotCost,
                slotIndex);

            card.Equipped = true;
            card.EquippedTowerId = tower.Id;
            card.EquippedSlot = slotIndex;

            // 슬롯 표현을 전투 실행용 연속 카드 배열로 바꾼다.
            CompileTowerProgram(tower);
            return CommandResult.Success();
        }

        private bool IsLoadoutEditablePhase()
        {
            return sandboxTestingMode ||
                   phase == RunPhase.Planning ||
                   phase == RunPhase.CardPackLoadout;
        }

        /// <summary>
        /// 현재 런에서 이 타워를 한 기 건설할 때의 비용과 가능 여부다.
        /// 무료 건설 수와 중복 할증을 포함한 모든 경제 정책은 이 조회에서만 계산한다.
        /// </summary>
        public TowerConstructionQuote GetTowerConstructionQuote(
            string stableId)
        {
            EnsureInitialized();
            if (!content.TryGetTowerId(
                    stableId,
                    out TowerDefinitionId definitionId))
            {
                return new TowerConstructionQuote(
                    stableId,
                    0,
                    false,
                    false,
                    false,
                    false);
            }

            return CreateTowerConstructionQuote(
                definitionId,
                stableId);
        }

        /// <summary>다음 레벨 비용, 최대 레벨, 단계 및 골드 판정을 함께 반환한다.</summary>
        public TowerUpgradeQuote GetTowerUpgradeQuote(
            int towerInstanceId)
        {
            EnsureInitialized();
            TowerState tower =
                FindTower(new TowerId(towerInstanceId));
            if (tower == null)
            {
                return new TowerUpgradeQuote(
                    towerInstanceId,
                    0,
                    0,
                    0,
                    0,
                    false,
                    false,
                    false,
                    false);
            }

            return CreateTowerUpgradeQuote(tower);
        }

        /// <summary>현재 레벨에 열린 카드 슬롯 수다.</summary>
        public int GetTowerUnlockedSlotCount(int towerInstanceId)
        {
            TowerState tower =
                FindTower(new TowerId(towerInstanceId));
            return tower == null
                ? 0
                : GetTowerUnlockedSlotCount(tower);
        }

        /// <summary>현재 레벨의 타워 사거리를 milli 단위로 반환한다.</summary>
        public int GetTowerRangeMilli(int towerInstanceId)
        {
            TowerState tower =
                FindTower(new TowerId(towerInstanceId));
            return tower == null
                ? 0
                : GetTowerLevelBalance(tower).RangeMilli;
        }

        private TowerConstructionQuote CreateTowerConstructionQuote(
            TowerDefinitionId definitionId,
            string stableId)
        {
            bool isUnlocked =
                ownedTowerDefinitions.Contains(
                    definitionId.Value);
            bool isEligible =
                isUnlocked &&
                (phase == RunPhase.Planning ||
                 phase == RunPhase.Combat);
            int constructionCost =
                CalculateTowerConstructionCost(
                    definitionId);
            return new TowerConstructionQuote(
                stableId,
                constructionCost,
                true,
                isUnlocked,
                isEligible,
                gold >= constructionCost);
        }

        private int CalculateTowerConstructionCost(
            TowerDefinitionId definitionId)
        {
            if (towers.Count < run.FreeInitialTowerCount)
            {
                return 0;
            }

            CompiledTowerDefinition definition =
                content.GetTower(definitionId);
            int sameTypeCount = 0;
            for (int index = 0; index < towers.Count; index++)
            {
                if (towers[index].DefinitionId.Value ==
                    definitionId.Value)
                {
                    sameTypeCount++;
                }
            }

            long multiplierBps =
                DeterministicMath.BasisPointScale +
                (long)definition.DuplicateCostStepBps *
                sameTypeCount;
            long scaled =
                (long)definition.ConstructionCost *
                multiplierBps;
            long roundedUp =
                (scaled +
                 DeterministicMath.BasisPointScale -
                 1L) /
                DeterministicMath.BasisPointScale;
            return (int)Math.Min(int.MaxValue, roundedUp);
        }

        private TowerUpgradeQuote CreateTowerUpgradeQuote(
            TowerState tower)
        {
            CompiledTowerDefinition definition =
                content.GetTower(tower.DefinitionId);
            bool hasNextLevel =
                definition.TryGetLevel(
                    tower.Level + 1,
                    out CompiledTowerLevelBalance nextLevel);
            int cost = hasNextLevel
                ? nextLevel.UpgradeCost
                : 0;
            bool isEligible =
                hasNextLevel &&
                IsLoadoutEditablePhase();
            return new TowerUpgradeQuote(
                tower.Id.Value,
                tower.Level,
                definition.MaxLevel,
                hasNextLevel
                    ? tower.Level + 1
                    : tower.Level,
                cost,
                true,
                hasNextLevel,
                isEligible,
                hasNextLevel && gold >= cost);
        }

        private CompiledTowerLevelBalance GetTowerLevelBalance(
            TowerState tower)
        {
            CompiledTowerDefinition definition =
                content.GetTower(tower.DefinitionId);
            if (definition.TryGetLevel(
                    tower.Level,
                    out CompiledTowerLevelBalance level))
            {
                return level;
            }

            throw new InvalidOperationException(
                "Tower '" + definition.StableId +
                "' has no level balance for level " +
                tower.Level + ".");
        }

        private int GetTowerUnlockedSlotCount(TowerState tower)
        {
            return Math.Min(
                tower.CardInstanceIds.Length,
                GetTowerLevelBalance(tower).UnlockedSlots);
        }

        /// <summary>
        /// 현재 장착된 카드를 다른 타워 또는 다른 슬롯으로 옮긴다.
        /// </summary>
        /// <remarks>
        /// 이동 규칙 자체는 장착과 같으므로 중복 구현하지 않고 <see cref="EquipCard"/>에 위임한다.
        /// 다만 “보유만 하고 아직 장착되지 않은 카드”를 Move 명령으로 보내는 실수를 구분한다.
        /// </remarks>
        private CommandResult MoveCard(
            int cardInstanceId,
            int towerInstanceId,
            int slotIndex)
        {
            CardInstanceState card = FindCardInstance(cardInstanceId);
            if (card == null || !card.Equipped)
            {
                // 전투 중이라는 더 구체적인 실패 이유를 먼저 반환하면 UI가 잠금 안내를 할 수 있다.
                if (phase == RunPhase.Combat ||
                    phase == RunPhase.CardPackChoice)
                {
                    return CommandResult.Reject(
                        CommandError.CombatLoadoutLocked,
                        "Cards cannot be moved during combat.");
                }

                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "Only an equipped card can be moved.");
            }

            return EquipCard(cardInstanceId, towerInstanceId, slotIndex);
        }

        /// <summary>
        /// 목적 슬롯의 기존 카드를 제거하고 새 카드를 한 번에 배치한다.
        /// 모든 범위·슬롯·연산력 검증을 복사본에서 마친 뒤 상태를 바꾸므로
        /// 실패해도 기존 장착 구성이 그대로 유지된다.
        /// </summary>
        private CommandResult ReplaceCard(
            int cardInstanceId,
            int towerInstanceId,
            int slotIndex)
        {
            if (!IsLoadoutEditablePhase())
            {
                return CommandResult.Reject(
                    phase == RunPhase.Combat
                        ? CommandError.CombatLoadoutLocked
                        : CommandError.InvalidPhase,
                    "Cards cannot be replaced during combat.");
            }

            CardInstanceState replacement =
                FindCardInstance(cardInstanceId);
            TowerState tower =
                FindTower(new TowerId(towerInstanceId));
            int unlockedSlotCount = tower == null
                ? 0
                : GetTowerUnlockedSlotCount(tower);
            if (replacement == null ||
                tower == null ||
                slotIndex < 0 ||
                slotIndex >= unlockedSlotCount)
            {
                return CommandResult.Reject(
                    CommandError.SlotOutOfRange,
                    "Card, tower, or replacement slot does not exist.");
            }

            int displacedCardInstanceId =
                tower.CardInstanceIds[slotIndex];
            if (displacedCardInstanceId < 0 ||
                displacedCardInstanceId == cardInstanceId)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "The replacement slot must contain another card.");
            }

            CardInstanceState displaced =
                FindCardInstance(displacedCardInstanceId);
            if (displaced == null ||
                displaced.EquippedSlot != slotIndex)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "Only a card's primary slot can be replaced.");
            }

            CompiledCardDefinition replacementDefinition =
                content.GetCard(replacement.DefinitionId);
            int[] candidateSlots =
                (int[])tower.CardInstanceIds.Clone();
            ClearCardFromSlots(
                candidateSlots,
                displacedCardInstanceId);
            ClearCardFromSlots(
                candidateSlots,
                cardInstanceId);
            if (slotIndex + replacementDefinition.SlotCost >
                    unlockedSlotCount ||
                !CanPlaceCardInSlots(
                    candidateSlots,
                    cardInstanceId,
                    replacementDefinition.SlotCost,
                    slotIndex))
            {
                return CommandResult.Reject(
                    CommandError.SlotOccupied,
                    "Replacement card does not fit in the target slot.");
            }

            PlaceCardInSlots(
                candidateSlots,
                cardInstanceId,
                replacementDefinition.SlotCost,
                slotIndex);
            int candidateComputeCost = 0;
            for (int slot = 0;
                 slot < unlockedSlotCount;
                 slot++)
            {
                int candidateCardId = candidateSlots[slot];
                if (candidateCardId < 0)
                {
                    continue;
                }

                CardInstanceState candidateCard =
                    FindCardInstance(candidateCardId);
                if (candidateCard != null)
                {
                    candidateComputeCost +=
                        content.GetCard(
                            candidateCard.DefinitionId)
                            .ComputeCost;
                }
            }

            if (candidateComputeCost >
                GetTowerLevelBalance(tower).ComputeCapacity)
            {
                return CommandResult.Reject(
                    CommandError.ComputeCapacityExceeded,
                    "Tower compute capacity would be exceeded.");
            }

            TowerState previousTower = replacement.Equipped
                ? FindTower(replacement.EquippedTowerId)
                : null;
            if (previousTower != null && previousTower != tower)
            {
                ClearCardFromTower(
                    previousTower,
                    replacement.InstanceId);
            }

            tower.CardInstanceIds = candidateSlots;
            displaced.Equipped = false;
            displaced.EquippedTowerId = TowerId.Invalid;
            displaced.EquippedSlot = -1;
            replacement.Equipped = true;
            replacement.EquippedTowerId = tower.Id;
            replacement.EquippedSlot = slotIndex;

            if (previousTower != null && previousTower != tower)
            {
                CompileTowerProgram(previousTower);
            }

            CompileTowerProgram(tower);
            return CommandResult.Success();
        }

        /// <summary>
        /// 장착된 카드를 타워에서 빼고 보유 카드 목록으로 되돌린다.
        /// </summary>
        private CommandResult UnequipCard(
            int cardInstanceId,
            bool bypassPhaseLock = false)
        {
            if (!bypassPhaseLock &&
                !IsLoadoutEditablePhase())
            {
                return CommandResult.Reject(
                    phase == RunPhase.Combat
                        ? CommandError.CombatLoadoutLocked
                        : CommandError.InvalidPhase,
                    "Cards can only be unequipped during planning.");
            }

            CardInstanceState card = FindCardInstance(cardInstanceId);
            if (card == null || !card.Equipped)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "Only an equipped card can be unequipped.");
            }

            TowerState tower = FindTower(card.EquippedTowerId);
            if (tower == null)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "Equipped tower does not exist.");
            }

            // 여러 칸을 차지하는 카드라면 주 슬롯과 연속 슬롯 표시를 함께 비운다.
            ClearCardFromTower(tower, card.InstanceId);
            card.Equipped = false;
            card.EquippedTowerId = TowerId.Invalid;
            card.EquippedSlot = -1;

            // 카드 순서가 바뀌었으므로 다음 전투에서 사용할 프로그램 배열도 다시 만든다.
            CompileTowerProgram(tower);
            return CommandResult.Success();
        }

        /// <summary>
        /// 한 타워 안에서 카드의 실행 순서를 바꾸거나 두 카드의 위치를 맞바꾼다.
        /// </summary>
        /// <remarks>
        /// 카드 프로그램은 슬롯의 왼쪽에서 오른쪽 순서로 실행되므로,
        /// 이 명령은 단순한 화면 정렬이 아니라 실제 전투 결과를 바꾸는 게임 명령이다.
        ///
        /// 슬롯 값의 의미:
        /// -1은 빈 슬롯, 0 이상의 값은 카드 인스턴스의 시작 슬롯,
        /// -2는 슬롯 비용이 2 이상인 카드가 이어서 점유한 보조 슬롯이다.
        /// 보조 슬롯 자체는 카드의 시작점이 아니므로 fromSlot으로 선택할 수 없다.
        /// </remarks>
        private CommandResult ReorderCard(int towerInstanceId, int fromSlot, int toSlot)
        {
            if (!IsLoadoutEditablePhase())
            {
                return CommandResult.Reject(
                    phase == RunPhase.Combat
                        ? CommandError.CombatLoadoutLocked
                        : CommandError.InvalidPhase,
                    "Cards cannot be reordered during combat.");
            }

            // 타워 존재 여부와 두 슬롯의 배열 범위를 한 번에 검증한다.
            TowerState tower = FindTower(new TowerId(towerInstanceId));
            int unlockedSlotCount = tower == null
                ? 0
                : GetTowerUnlockedSlotCount(tower);
            if (tower == null ||
                fromSlot < 0 ||
                toSlot < 0 ||
                fromSlot >= unlockedSlotCount ||
                toSlot >= unlockedSlotCount)
            {
                return CommandResult.Reject(
                    CommandError.SlotOutOfRange,
                    "Tower or slot does not exist.");
            }

            int fromCard = tower.CardInstanceIds[fromSlot];
            int toCard = tower.CardInstanceIds[toSlot];

            // 빈 칸에서는 옮길 카드가 없고, -2 칸은 다중 슬롯 카드의 중간이므로 시작점이 아니다.
            if (fromCard < 0 || toCard == -2)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "Only a card's primary slot can be moved.");
            }

            // 목적지가 비어 있으면 단순 이동, 카드가 있으면 두 카드 위치의 교환으로 처리한다.
            CardInstanceState fromState = FindCardInstance(fromCard);
            CardInstanceState toState = toCard >= 0
                ? FindCardInstance(toCard)
                : null;
            if (fromState == null || (toCard >= 0 && toState == null))
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "Card instance does not exist.");
            }

            int fromSlotCost = content.GetCard(fromState.DefinitionId).SlotCost;
            int toSlotCost = toState == null
                ? 0
                : content.GetCard(toState.DefinitionId).SlotCost;

            // 실제 배열을 손대기 전에 복사본에서 두 카드의 기존 점유를 모두 제거한다.
            int[] candidateSlots = (int[])tower.CardInstanceIds.Clone();
            ClearCardFromSlots(candidateSlots, fromCard);
            if (toCard >= 0)
            {
                ClearCardFromSlots(candidateSlots, toCard);
            }

            // 먼저 출발 카드를 목적지에 놓을 수 있는지 확인한다.
            if (!CanPlaceCardInSlots(
                    candidateSlots,
                    fromCard,
                    fromSlotCost,
                    toSlot))
            {
                return CommandResult.Reject(
                    CommandError.SlotOccupied,
                    "Reordered cards do not fit in the requested slots.");
            }

            PlaceCardInSlots(candidateSlots, fromCard, fromSlotCost, toSlot);
            if (toCard >= 0)
            {
                // 교환이라면 목적지 카드도 원래 출발 위치에 온전히 들어가야 명령 전체가 성공한다.
                if (!CanPlaceCardInSlots(
                        candidateSlots,
                        toCard,
                        toSlotCost,
                        fromSlot))
                {
                    return CommandResult.Reject(
                        CommandError.SlotOccupied,
                        "Reordered cards do not fit in the requested slots.");
                }

                PlaceCardInSlots(candidateSlots, toCard, toSlotCost, fromSlot);
            }

            // 모든 배치 가능 검사가 성공한 뒤 복사본을 실제 슬롯 상태로 채택한다.
            tower.CardInstanceIds = candidateSlots;
            UpdateCardSlot(fromCard, toSlot);
            if (toCard >= 0)
            {
                UpdateCardSlot(toCard, fromSlot);
            }

            CompileTowerProgram(tower);
            return CommandResult.Success();
        }

        /// <summary>
        /// 다음 웨이브의 스폰 일정을 만들고 계획 단계에서 전투 단계로 전환한다.
        /// </summary>
        /// <remarks>
        /// 웨이브를 시작하는 순간 각 타워의 카드 슬롯을 전투 실행용 프로그램으로 다시 컴파일한다.
        /// 이후 전투 중에는 카드 변경 명령이 거절되므로 한 웨이브 동안 프로그램 순서가 고정된다.
        /// 이 불변성이 같은 시드와 같은 명령 로그가 항상 같은 결과를 만드는 기반이다.
        /// </remarks>
        private CommandResult StartWave()
        {
            if (phase != RunPhase.Planning)
            {
                return CommandResult.Reject(
                    CommandError.InvalidPhase,
                    "A wave can only start during planning.");
            }

            // 공격 주체가 하나도 없는 실수 상태로 전투를 시작하지 않게 한다.
            if (towers.Count == 0)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "At least one tower must be placed.");
            }

            // currentWaveIndex는 아직 시작 전에는 -1일 수 있으므로 다음 인덱스를 계산해 검사한다.
            int nextWave = currentWaveIndex + 1;
            if (nextWave < 0 || nextWave >= content.WaveCount)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "No wave remains.");
            }

            currentWaveIndex = nextWave;
            ResetWaveCombatTelemetry();

            // 스폰 정의의 상대 틱을 현재 시뮬레이션 절대 틱으로 바꾸는 기준점이다.
            waveStartTick = tick;
            CompiledWaveDefinition wave =
                content.GetWave(currentWaveIndex);

            // 각 스폰 묶음은 “몇 마리를 냈는가”와 “다음 출현 절대 틱”을 별도로 추적한다.
            waveSpawns = new WaveSpawnRuntime[
                wave.SpawnsInternal.Length];
            for (int i = 0;
                 i < wave.SpawnsInternal.Length;
                 i++)
            {
                waveSpawns[i] = new WaveSpawnRuntime
                {
                    Definition = wave.SpawnsInternal[i],
                    TargetCount =
                        WaveEnemyStatResolver.ResolveSpawnCount(
                            wave.SpawnsInternal[i].Count,
                            currentStageNumber),
                    Spawned = 0,
                    NextTick =
                        tick +
                        wave.SpawnsInternal[i].FirstSpawnTick
                };
            }

            // 프로그램 스냅샷을 만들고 웨이브 단위 제한/재발동 기록을 초기화한다.
            for (int i = 0; i < towers.Count; i++)
            {
                CompileTowerProgram(towers[i]);
                towers[i].GoldGeneratedThisWave = 0;
                towers[i].AttackWindupRemaining = 0;
                towers[i].PendingAttackTargetId =
                    EntityId.Invalid;
                towers[i].TargetsInside.Clear();
                towers[i].LastTargetTriggerTick.Clear();
            }

            // 이전 웨이브의 제안은 전투 화면에 남아 있으면 안 된다.
            draftOffers.Clear();
            cardPackOffers.Clear();
            bossCardPackAwardedThisWave = false;
            phase = RunPhase.Combat;

            // Unity 화면은 이 표현 이벤트를 읽어 웨이브 시작 문구나 효과음을 재생할 수 있다.
            AddPresentation(
                PresentationEventType.WaveStarted,
                currentWaveIndex,
                -1,
                0,
                wave.StableId);
            return CommandResult.Success();
        }

        /// <summary>
        /// 최종 웨이브를 끝낸 현재 빌드와 경제를 유지한 채 다음 이어하기
        /// 스테이지를 준비한다. 전투 개체와 웨이브 원장만 새 스테이지 경계에서
        /// 정리하며 타워, 카드, 강화, 골드와 본진 체력은 그대로 이어진다.
        /// </summary>
        private CommandResult ContinueStage()
        {
            if (phase != RunPhase.Victory)
            {
                return CommandResult.Reject(
                    CommandError.InvalidPhase,
                    "A cleared stage is required before continuing.");
            }
            if (currentStageNumber == int.MaxValue)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "The maximum stage number has been reached.");
            }

            enemies.Clear();
            projectiles.Clear();
            hazards.Clear();
            hazardContactsThisTick.Clear();
            lineages.Clear();
            chainBudgets.Clear();
            cardPacks.Clear();
            draftOffers.Clear();
            cardPackOffers.Clear();
            waveSpawns = new WaveSpawnRuntime[0];

            currentStageNumber++;
            currentWaveIndex = -1;
            waveStartTick = tick;
            activeShimmeringLineageId = -1;
            activeCardPackId = -1;
            pendingCardInstanceId = -1;
            phaseAfterCardPack = RunPhase.Planning;
            waveRewardsPending = false;
            regularDraftPending = false;
            bossCardPackAwardedThisWave = false;
            victoryPending = false;
            phase = RunPhase.Planning;
            return CommandResult.Success();
        }

        /// <summary>
        /// 현재 드래프트 제안 중 하나를 선택해 새 카드 인스턴스로 소유 목록에 추가한다.
        /// </summary>
        private CommandResult SelectDraft(int offerIndex)
        {
            if (phase != RunPhase.Draft)
            {
                return CommandResult.Reject(
                    CommandError.InvalidPhase,
                    "There is no active draft.");
            }

            if (offerIndex < 0 || offerIndex >= draftOffers.Count)
            {
                return CommandResult.Reject(
                    CommandError.InvalidDraftChoice,
                    "Draft offer is out of range.");
            }

            // 제안에는 카드 정의 ID가 있고, 선택 시 이번 런에서 고유한 인스턴스 ID가 새로 생긴다.
            AddOwnedCard(draftOffers[offerIndex]);
            draftOffers.Clear();

            // 획득한 카드를 어느 타워에 둘지 결정할 수 있도록 다시 계획 단계로 돌아간다.
            phase = RunPhase.Planning;
            return CommandResult.Success();
        }

        /// <summary>
        /// 현재 틱까지 출현 시각이 도달한 적을 스폰 일정별로 생성한다.
        /// </summary>
        /// <remarks>
        /// 한 번의 Step 사이에 여러 출현 시각이 지나 있을 수 있으므로 if가 아니라 while을 쓴다.
        /// 이렇게 하면 처리 속도나 프레임률과 관계없이 놓친 적 없이 동일한 수가 생성된다.
        /// </remarks>
        private void ProcessWaveSpawns()
        {
            for (int i = 0; i < waveSpawns.Length; i++)
            {
                WaveSpawnRuntime runtime = waveSpawns[i];
                while (runtime.Spawned < runtime.TargetCount &&
                       tick >= runtime.NextTick &&
                       enemies.Count <
                       content.Safety.MaxActiveEnemies)
                {
                    SpawnEnemy(
                        runtime.Definition.EnemyId,
                        EnemySpawnOrigin.Scheduled,
                        eliteTraitIds:
                            runtime.Definition.EliteTraitIdsInternal);
                    runtime.Spawned++;

                    // 다음 출현 시각은 현재 프레임 시간이 아니라 이전 예정 시각에 간격을 더한다.
                    // 따라서 순간적인 처리 지연이 이후 스폰 간격을 밀어내지 않는다.
                    runtime.NextTick += runtime.Definition.IntervalTicks;
                }
            }
        }

        /// <summary>
        /// 적 정의로부터 새 적과 그 적의 보상 가계(lineage) 원장을 생성한다.
        /// </summary>
        /// <remarks>
        /// 최초 적은 자신의 EntityId를 LineageId로 사용한다.
        /// 이후 분열하거나 복제된 적은 같은 가계 원장을 공유하여,
        /// 개체 수가 늘어나도 원래 적보다 더 많은 골드와 웨이브 진행도를 만들지 못하게 한다.
        /// </remarks>
        private EnemyState SpawnEnemy(
            EnemyDefinitionId definitionId,
            EnemySpawnOrigin origin = EnemySpawnOrigin.Scheduled,
            int summonerEntityId = -1,
            int spawnHealthBps = 10_000,
            EliteTraitId[] eliteTraitIds = null)
        {
            CompiledEnemyDefinition definition = content.GetEnemy(definitionId);
            var id = new EntityId(nextEntityId++);
            EliteTraitId[] appliedEliteTraits =
                eliteTraitIds == null || eliteTraitIds.Length == 0
                    ? Array.Empty<EliteTraitId>()
                    : (EliteTraitId[])eliteTraitIds.Clone();
            ResolvedWaveEnemyStats resolvedStats =
                WaveEnemyStatResolver.Resolve(
                    content,
                    definition,
                    appliedEliteTraits);
            if (origin != EnemySpawnOrigin.Sandbox)
            {
                resolvedStats =
                    WaveEnemyStatResolver.ApplyEndlessStage(
                        resolvedStats,
                        currentStageNumber);
            }
            int cardPackBudget = 0;
            if (origin == EnemySpawnOrigin.Scheduled)
            {
                cardPackBudget =
                    definition.Rank == EnemyRank.Elite ||
                    appliedEliteTraits.Length > 0
                    ? run.EliteKillProgress
                    : definition.Rank == EnemyRank.Normal
                        ? run.NormalKillProgress
                        : 0;
            }

            // 위치는 Transform 좌표가 아니라 경로 진행도 0에서 계산한다.
            var enemy = new EnemyState
            {
                Id = id,
                DefinitionId = definitionId,
                LineageId = new LineageId(id.Value),
                Generation = 0,
                SpawnOrigin = origin,
                SummonerId = new EntityId(summonerEntityId),
                EliteTraitIds = appliedEliteTraits,
                PathProgressMilli = 0,
                Position = path.GetPosition(0),
                HealthMilli = resolvedStats.MaxHealthMilli,
                MaxHealthMilli = resolvedStats.MaxHealthMilli,
                Armor = resolvedStats.Armor,
                BaseSpeedMilliPerTick =
                    resolvedStats.SpeedMilliPerTick,
                RewardBudget = resolvedStats.RewardBudget,
                WaveProgressBudget = definition.WaveProgressBudget,
                CardPackProgressBudget = cardPackBudget,
                ShieldMilli = resolvedStats.ShieldMilli,
                EliteRenderScaleBps =
                    resolvedStats.RenderScaleBps,
                ControlThreshold = definition.ControlGaugeThreshold,
                ControlThresholdStep = definition.ControlGaugeStep,
                BossAbilityCooldownTicks =
                    definition.BossAbilityIntervalTicks
            };
            if (origin == EnemySpawnOrigin.BossSummon)
            {
                enemy.RewardBudget = 0;
                enemy.WaveProgressBudget = 0;
                enemy.CardPackProgressBudget = 0;
                enemy.HealthMilli = Math.Max(
                    1,
                    DeterministicMath.MultiplyBasisPoints(
                        enemy.HealthMilli,
                        spawnHealthBps));
                enemy.MaxHealthMilli = enemy.HealthMilli;
            }
            else if (origin == EnemySpawnOrigin.ShimmeringCarrier)
            {
                enemy.RewardBudget = 0;
                enemy.WaveProgressBudget = 0;
                enemy.CardPackProgressBudget = 0;
                enemy.IsShimmering = true;
                enemy.HealthMilli =
                    DeterministicMath.MultiplyBasisPoints(
                        enemy.HealthMilli,
                        run.ShimmeringHealthBps);
                enemy.MaxHealthMilli = enemy.HealthMilli;
                enemy.SpeedMultiplierBps =
                    run.ShimmeringSpeedBps;
                enemy.SizeMultiplierBps =
                    run.ShimmeringSizeBps;
            }
            else if (origin == EnemySpawnOrigin.Sandbox)
            {
                // 테스트 맵의 수동/무한 스폰은 정규 웨이브의 경제와 진행도를
                // 오염시키지 않는다. 카드 자체가 새로 만든 보상만 별도 규칙을 탄다.
                enemy.RewardBudget = 0;
                enemy.WaveProgressBudget = 0;
                enemy.CardPackProgressBudget = 0;
            }
            enemies.Add(enemy);

            // 적 본체와 별도로 가계 전체의 최초/최대 예산을 기록한다.
            lineages.Add(enemy.LineageId.Value, new LineageState
            {
                Id = enemy.LineageId,
                HighestGeneration = 0,
                SplitCount = 0,
                SpawnedEntityCount = 1,
                LiveMembers = 1,
                BaseRewardBudget = enemy.RewardBudget,
                MaxRewardBudget = enemy.RewardBudget,
                ProgressBudget = enemy.WaveProgressBudget,
                BaseCardPackProgress = cardPackBudget,
                IsShimmering = enemy.IsShimmering
            });

            // 프런트엔드는 정의 stableId로 어떤 스프라이트를 붙일지 결정할 수 있다.
            AddPresentation(
                PresentationEventType.EnemySpawned,
                enemy.Id.Value,
                -1,
                enemy.RewardBudget,
                definition.StableId);
            return enemy;
        }

        private bool UsesEliteControlRules(EnemyState enemy)
        {
            return enemy != null &&
                   content.GetEnemy(enemy.DefinitionId).Rank !=
                   EnemyRank.Normal;
        }

        /// <summary>
        /// 현재 웨이브가 완전히 끝났는지 확인하고 승리 또는 드래프트 단계로 전환한다.
        /// </summary>
        /// <remarks>
        /// “마지막 적이 보이지 않는다”만으로는 종료할 수 없다.
        /// 앞으로 생성될 스폰이 모두 끝났고, 예약 이벤트 큐도 비었으며,
        /// 살아 있는 적도 없어야 사망 연쇄·보상·추가 생성 효과가 모두 정산된 상태다.
        /// </remarks>
        private void CheckWaveCompletion()
        {
            // TestLab은 Runtime이 원하는 시점에 적을 계속 넣는 열린 전투다.
            // 빈 순간이 생겨도 정규 웨이브 보상/승리 단계로 넘어가지 않는다.
            if (sandboxTestingMode)
            {
                return;
            }

            // 전투가 아니거나, 아직 출현할 적 또는 처리할 이벤트가 있으면 종료 판정을 미룬다.
            if (phase != RunPhase.Combat ||
                !AllWaveSpawnsFinished() ||
                !eventQueue.IsEmpty)
            {
                return;
            }

            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i].Alive)
                {
                    return;
                }
            }

            // 웨이브 경계를 넘겨서는 안 되는 일시 전투 개체와 연쇄 예산을 비운다.
            projectiles.Clear();
            hazards.Clear();
            chainBudgets.Clear();
            GrantWaveCompletionReward();
            AddPresentation(
                PresentationEventType.WaveCompleted,
                currentWaveIndex,
                -1,
                gold,
                content.GetWave(currentWaveIndex).StableId);

            BeginWaveEndRewards(
                currentWaveIndex >=
                content.WaveCount - 1);
        }

        /// <summary>
        /// 모든 스폰 묶음이 정의된 개체 수만큼 적을 생성했는지 확인한다.
        /// </summary>
        private bool AllWaveSpawnsFinished()
        {
            for (int i = 0; i < waveSpawns.Length; i++)
            {
                if (waveSpawns[i].Spawned < waveSpawns[i].TargetCount)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 현재 빌드에 맞는 카드, 태그 시너지 카드, 티어 가중 무작위 카드를 차례로 제안한다.
        /// </summary>
        /// <remarks>
        /// 제안 슬롯마다 의도가 다르지만 한 제안 안에서는 같은 카드가 중복되지 않는다.
        /// 모든 무작위 선택은 드래프트 전용 PCG 난수 상태를 사용하므로 전투 난수 소비량이
        /// 달라져도 같은 드래프트 시드 흐름을 방해하지 않는다.
        /// </remarks>
        private void GenerateDraft()
        {
            draftOffers.Clear();
            if (content.CardCount == 0)
            {
                return;
            }

            // 첫 칸: 현재 배치된 타워 중 적어도 하나에 실제로 장착 가능한 카드.
            AddDraftCard(PickCompatibleDraftCard());

            // 둘째 칸: 현재 장착 카드와 태그가 가장 많이 겹치는 카드.
            AddDraftCard(PickSynergyDraftCard());

            // 나머지 칸: 설정된 티어 가중치로 뽑되, 이미 제안된 카드는 제외한다.
            while (draftOffers.Count < run.DraftOfferCount &&
                   draftOffers.Count < content.CardCount)
            {
                AddDraftCard(PickWeightedRandomCard());
            }

            AddPresentation(
                PresentationEventType.DraftGenerated,
                currentWaveIndex,
                -1,
                draftOffers.Count);
        }

        /// <summary>
        /// 현재 타워의 빈 슬롯과 남은 연산력에 들어갈 수 있는 카드 중 하나를 고른다.
        /// </summary>
        private CardId PickCompatibleDraftCard()
        {
            var candidates = new List<CardId>(content.CardCount);
            for (int cardIndex = 0;
                 cardIndex < content.CardCount;
                 cardIndex++)
            {
                CardId cardId = new CardId(cardIndex);
                if (IsDrafted(cardId))
                {
                    continue;
                }

                // 어느 한 타워에라도 들어갈 수 있으면 “장착 가능” 후보로 인정한다.
                for (int towerIndex = 0; towerIndex < towers.Count; towerIndex++)
                {
                    if (CanFitCard(
                            towers[towerIndex],
                            content.GetCard(cardId)))
                    {
                        candidates.Add(cardId);
                        break;
                    }
                }
            }

            return PickFromCandidates(candidates);
        }

        /// <summary>
        /// 현재 장착된 카드들과 가장 많은 태그를 공유하는 후보 중 하나를 고른다.
        /// </summary>
        /// <remarks>
        /// 최고 점수가 같은 후보가 여러 장이면 모두 후보 목록에 남긴 뒤
        /// 드래프트 전용 난수로 하나를 고른다. 목록 순회 순서가 고정되어 있어 결과도 결정적이다.
        /// </remarks>
        private CardId PickSynergyDraftCard()
        {
            int bestScore = int.MinValue;
            var candidates = new List<CardId>();
            for (int cardIndex = 0;
                 cardIndex < content.CardCount;
                 cardIndex++)
            {
                CardId cardId = new CardId(cardIndex);
                if (IsDrafted(cardId))
                {
                    continue;
                }

                int score = CountTagSynergy(
                    content.GetCard(cardId));
                if (score > bestScore)
                {
                    // 더 높은 점수가 나오면 이전 최고점 후보는 모두 버린다.
                    candidates.Clear();
                    candidates.Add(cardId);
                    bestScore = score;
                }
                else if (score == bestScore)
                {
                    // 동점 후보는 함께 보존해 특정 카드만 항상 우선되는 편향을 줄인다.
                    candidates.Add(cardId);
                }
            }

            return PickFromCandidates(candidates);
        }

        /// <summary>
        /// RunConfig의 카드 티어 가중치로 티어를 먼저 뽑고, 그 티어의 카드 중 하나를 고른다.
        /// </summary>
        /// <remarks>
        /// 음수 가중치는 0으로 취급한다. 뽑힌 티어에 남은 카드가 없다면
        /// 전체 미제안 카드로 대체하여 드래프트 칸이 불필요하게 비는 것을 막는다.
        /// </remarks>
        private CardId PickWeightedRandomCard()
        {
            int totalWeight = 0;
            for (int i = 0;
                 i < run.TierWeightsInternal.Length;
                 i++)
            {
                totalWeight += Math.Max(
                    0,
                    run.TierWeightsInternal[i]);
            }

            // 모든 가중치가 0이어도 NextInt(0)을 호출하지 않고 첫 티어를 기본으로 둔다.
            int roll = totalWeight == 0
                ? 0
                : draftRandom.NextInt(totalWeight);
            int selectedTier = 1;
            int cumulative = 0;
            for (int i = 0;
                 i < run.TierWeightsInternal.Length;
                 i++)
            {
                cumulative += Math.Max(
                    0,
                    run.TierWeightsInternal[i]);
                if (roll < cumulative)
                {
                    selectedTier = i + 1;
                    break;
                }
            }

            // 선택된 티어에 속하면서 이번 제안에 아직 들어가지 않은 카드만 모은다.
            var candidates = new List<CardId>();
            for (int i = 0; i < content.CardCount; i++)
            {
                CardId id = new CardId(i);
                if (!IsDrafted(id) &&
                    (int)content.GetCard(id).Tier == selectedTier)
                {
                    candidates.Add(id);
                }
            }

            if (candidates.Count == 0)
            {
                // 해당 티어가 소진됐을 때는 티어와 무관하게 남은 카드로 폴백한다.
                for (int i = 0; i < content.CardCount; i++)
                {
                    CardId id = new CardId(i);
                    if (!IsDrafted(id))
                    {
                        candidates.Add(id);
                    }
                }
            }

            return PickFromCandidates(candidates);
        }

        /// <summary>
        /// 정렬된 후보 목록에서 드래프트 전용 난수로 한 장을 선택한다.
        /// </summary>
        /// <returns>후보가 없으면 유효하지 않은 카드 ID를 반환한다.</returns>
        private CardId PickFromCandidates(List<CardId> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return CardId.Invalid;
            }

            return candidates[draftRandom.NextInt(candidates.Count)];
        }

        /// <summary>
        /// 유효하며 아직 제안되지 않은 카드만 드래프트 목록에 추가한다.
        /// </summary>
        /// <remarks>
        /// 선택 메서드가 후보를 찾지 못해 Invalid를 반환해도 이 경계에서 안전하게 무시한다.
        /// </remarks>
        private void AddDraftCard(CardId cardId)
        {
            if (cardId.IsValid && !IsDrafted(cardId))
            {
                draftOffers.Add(cardId);
            }
        }

        /// <summary>
        /// 한 번의 드래프트 제안 안에 같은 카드 정의가 이미 있는지 선형 검색한다.
        /// </summary>
        private bool IsDrafted(CardId id)
        {
            for (int i = 0; i < draftOffers.Count; i++)
            {
                if (draftOffers[i] == id)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 후보 카드의 각 태그가 현재 장착 카드의 태그와 몇 번 일치하는지 점수화한다.
        /// </summary>
        /// <remarks>
        /// 보유만 하고 장착하지 않은 카드는 현재 빌드가 아직 아니므로 시너지 계산에서 제외한다.
        /// 문자열 비교는 문화권에 영향받지 않는 Ordinal 방식이라 플랫폼마다 결과가 바뀌지 않는다.
        /// </remarks>
        private int CountTagSynergy(CompiledCardDefinition candidate)
        {
            int score = 0;
            for (int cardIndex = 0; cardIndex < cards.Count; cardIndex++)
            {
                CardInstanceState owned = cards[cardIndex];
                if (!owned.Equipped)
                {
                    continue;
                }

                // 콘텐츠 컴파일 단계에서 만들어진 불변 태그 배열끼리 정확히 비교한다.
                string[] ownedTags =
                    content.GetCard(owned.DefinitionId).TagsInternal;
                for (int candidateTag = 0;
                     candidateTag < candidate.TagsInternal.Length;
                     candidateTag++)
                {
                    for (int ownedTag = 0; ownedTag < ownedTags.Length; ownedTag++)
                    {
                        if (string.Equals(
                                candidate.TagsInternal[candidateTag],
                                ownedTags[ownedTag],
                                StringComparison.Ordinal))
                        {
                            score++;
                        }
                    }
                }
            }

            return score;
        }

        /// <summary>
        /// 카드 한 장이 현재 타워의 연산력과 연속 빈 슬롯 조건을 모두 만족하는지 확인한다.
        /// </summary>
        /// <remarks>
        /// 실제 장착 위치를 정하지 않는 드래프트용 사전 검사다.
        /// 가능한 시작 슬롯을 왼쪽부터 탐색하지만 상태는 전혀 변경하지 않는다.
        /// </remarks>
        private bool CanFitCard(TowerState tower, CompiledCardDefinition card)
        {
            CompiledTowerLevelBalance level =
                GetTowerLevelBalance(tower);

            // 연산력은 빈 슬롯 수와 별개의 제한이므로 먼저 저렴하게 검사한다.
            if (ComputeTowerCost(tower, -1) + card.ComputeCost >
                level.ComputeCapacity)
            {
                return false;
            }

            int unlockedSlotCount =
                GetTowerUnlockedSlotCount(tower);
            for (int slot = 0;
                slot + card.SlotCost <= unlockedSlotCount;
                 slot++)
            {
                // -3은 실제 카드 ID가 아닌 임시 검사 표식이다.
                // 기존 어느 슬롯과도 같지 않아 빈 자리 여부만 검사하게 된다.
                if (CanPlaceCardInSlots(
                        tower.CardInstanceIds,
                        -3,
                        card.SlotCost,
                        slot))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 카드 정의 한 장을 이번 런의 고유한 소유 카드 인스턴스로 만든다.
        /// </summary>
        /// <returns>나중에 장착 상태를 기록할 새 <see cref="CardInstanceState"/>.</returns>
        private CardInstanceState AddOwnedCard(CardId definitionId)
        {
            var state = new CardInstanceState
            {
                InstanceId = nextCardInstanceId++,
                DefinitionId = definitionId
            };
            cards.Add(state);
            return state;
        }

        /// <summary>
        /// 타워에 장착된 카드들의 총 연산 비용을 계산한다.
        /// </summary>
        /// <param name="tower">비용을 계산할 타워 인스턴스.</param>
        /// <param name="excludedCardInstanceId">
        /// 이동 중이라 잠시 합계에서 뺄 카드 ID. 제외할 카드가 없으면 음수 값을 사용한다.
        /// </param>
        /// <remarks>
        /// 슬롯 비용이 2 이상인 카드의 보조 칸은 -2이므로 자연스럽게 건너뛴다.
        /// 따라서 각 카드의 ComputeCost는 시작 슬롯에서 정확히 한 번만 합산된다.
        /// </remarks>
        private int ComputeTowerCost(TowerState tower, int excludedCardInstanceId)
        {
            int result = 0;
            for (int slot = 0; slot < tower.CardInstanceIds.Length; slot++)
            {
                int instanceId = tower.CardInstanceIds[slot];
                if (instanceId < 0 || instanceId == excludedCardInstanceId)
                {
                    continue;
                }

                CardInstanceState card = FindCardInstance(instanceId);
                if (card != null)
                {
                    result += content.GetCard(card.DefinitionId).ComputeCost;
                }
            }

            return result;
        }

        /// <summary>
        /// UI 친화적인 슬롯 배열을 전투 실행에 사용할 연속 프로그램 스냅샷으로 변환한다.
        /// </summary>
        /// <remarks>
        /// <c>CardInstanceIds</c>에는 빈 칸(-1)과 다중 슬롯 보조 칸(-2)이 섞여 있다.
        /// 반면 <c>Program</c>과 <c>ProgramInstances</c>에는 실제 카드 시작점만
        /// 왼쪽에서 오른쪽 순서로 복사한다.
        ///
        /// ToArray로 새 배열을 만드는 이유는 전투 실행 프레임이 카드 편집용 List나 슬롯 배열을
        /// 직접 바라보지 않게 하기 위해서다. 전투 시작 후 카드 명령을 잠그는 규칙과 함께,
        /// 한 활성화 연쇄가 변하지 않는 카드 순서를 읽도록 보장한다.
        /// </remarks>
        private void CompileTowerProgram(TowerState tower)
        {
            var definitions = new List<CardId>(tower.CardInstanceIds.Length);
            var instances = new List<int>(tower.CardInstanceIds.Length);
            var subjectTypes =
                new List<SubjectType>(tower.CardInstanceIds.Length);
            for (int slot = 0; slot < tower.CardInstanceIds.Length; slot++)
            {
                int instanceId = tower.CardInstanceIds[slot];
                if (instanceId < 0)
                {
                    continue;
                }

                // 유효한 시작 슬롯만 정의 ID와 인스턴스 ID의 같은 인덱스에 나란히 저장한다.
                CardInstanceState card = FindCardInstance(instanceId);
                if (card != null)
                {
                    definitions.Add(card.DefinitionId);
                    instances.Add(instanceId);
                    subjectTypes.Add(
                        slot < tower.CardSubjectTypes.Length
                            ? tower.CardSubjectTypes[slot]
                            : tower.SubjectType);
                }
            }

            tower.Program = definitions.ToArray();
            tower.ProgramInstances = instances.ToArray();
            tower.ProgramSubjectTypes = subjectTypes.ToArray();
        }

        /// <summary>
        /// 타워 슬롯에서 지정 카드가 차지하는 모든 칸을 비운다.
        /// </summary>
        private void ClearCardFromTower(TowerState tower, int cardInstanceId)
        {
            ClearCardFromSlots(tower.CardInstanceIds, cardInstanceId);
        }

        /// <summary>
        /// 슬롯 배열에서 카드의 시작 칸과 뒤따르는 보조 점유 칸(-2)을 함께 제거한다.
        /// </summary>
        private static void ClearCardFromSlots(
            int[] slots,
            int cardInstanceId)
        {
            for (int slot = 0; slot < slots.Length; slot++)
            {
                if (slots[slot] == cardInstanceId)
                {
                    slots[slot] = -1;
                    int next = slot + 1;

                    // -2가 연속되는 동안만 같은 다중 슬롯 카드의 나머지 점유로 본다.
                    while (next < slots.Length && slots[next] == -2)
                    {
                        slots[next] = -1;
                        next++;
                    }
                }
            }
        }

        /// <summary>
        /// 지정 위치부터 slotCost만큼의 연속 칸에 카드를 놓을 수 있는지 검사한다.
        /// </summary>
        /// <remarks>
        /// 같은 cardInstanceId가 들어 있는 칸은 같은 타워 안에서 위치를 옮기는 중인 것으로
        /// 허용한다. 그 외의 값이 있는 칸은 다른 카드가 점유한 것으로 본다.
        /// 이 메서드는 검사만 하며 슬롯 배열을 변경하지 않는다.
        /// </remarks>
        private static bool CanPlaceCardInSlots(
            int[] slots,
            int cardInstanceId,
            int slotCost,
            int slotIndex)
        {
            if (slots == null ||
                slotCost <= 0 ||
                slotIndex < 0 ||
                slotIndex + slotCost > slots.Length)
            {
                return false;
            }

            // 슬롯 비용만큼 모든 칸이 연속으로 비어 있어야 한다.
            for (int offset = 0; offset < slotCost; offset++)
            {
                int occupying = slots[slotIndex + offset];
                if (occupying != -1 && occupying != cardInstanceId)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 이미 검증된 연속 슬롯에 카드 시작 ID와 보조 점유 표식(-2)을 기록한다.
        /// </summary>
        /// <remarks>
        /// 이 메서드 자체는 범위나 충돌을 검사하지 않는다.
        /// 호출자는 반드시 먼저 <see cref="CanPlaceCardInSlots"/>를 통과해야 한다.
        /// </remarks>
        private static void PlaceCardInSlots(
            int[] slots,
            int cardInstanceId,
            int slotCost,
            int slotIndex)
        {
            for (int offset = 0; offset < slotCost; offset++)
            {
                // 첫 칸만 실제 카드 ID이고, 나머지는 같은 카드의 연속 점유를 뜻하는 -2다.
                slots[slotIndex + offset] =
                    offset == 0 ? cardInstanceId : -2;
            }
        }

        /// <summary>
        /// 카드 인스턴스가 기억하는 대표 슬롯 위치를 갱신한다.
        /// </summary>
        private void UpdateCardSlot(int cardInstanceId, int slot)
        {
            CardInstanceState card = FindCardInstance(cardInstanceId);
            if (card != null)
            {
                card.EquippedSlot = slot;
            }
        }

        /// <summary>
        /// 런 내부 카드 인스턴스 ID로 카드를 안전하게 찾는다.
        /// </summary>
        /// <remarks>
        /// 카드 인스턴스는 순서대로 추가되므로 ID를 List 인덱스로 바로 사용할 수 있다.
        /// 그래도 범위와 저장된 InstanceId를 함께 확인해 잘못된 명령이나 상태 손상을
        /// 조용히 다른 카드로 연결하지 않고 null로 보고한다.
        /// </remarks>
        private CardInstanceState FindCardInstance(int instanceId)
        {
            if (instanceId < 0 || instanceId >= cards.Count)
            {
                return null;
            }

            CardInstanceState card = cards[instanceId];
            return card.InstanceId == instanceId ? card : null;
        }
    }
}
