using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 카드 인스턴스를 현재 타워 슬롯에 장착할 수 있는지 권위 규칙으로 조회한
    /// 읽기 전용 결과다. 조회는 시뮬레이션 상태를 변경하지 않는다.
    /// </summary>
    public readonly struct CardPlacementQuote
    {
        internal CardPlacementQuote(
            int cardInstanceId,
            int towerInstanceId,
            int slotIndex,
            bool isPhaseEligible,
            bool cardExists,
            bool towerExists,
            bool fitsUnlockedSlots,
            bool slotAvailable,
            bool fitsComputeCapacity,
            int slotCost,
            int cardComputeCost,
            int currentComputeCost,
            int computeCapacity,
            CommandError error,
            string message)
        {
            CardInstanceId = cardInstanceId;
            TowerInstanceId = towerInstanceId;
            SlotIndex = slotIndex;
            IsPhaseEligible = isPhaseEligible;
            CardExists = cardExists;
            TowerExists = towerExists;
            FitsUnlockedSlots = fitsUnlockedSlots;
            SlotAvailable = slotAvailable;
            FitsComputeCapacity = fitsComputeCapacity;
            SlotCost = slotCost;
            CardComputeCost = cardComputeCost;
            CurrentComputeCost = currentComputeCost;
            ComputeCapacity = computeCapacity;
            Error = error;
            Message = message ?? string.Empty;
        }

        public int CardInstanceId { get; }

        public int TowerInstanceId { get; }

        public int TowerId => TowerInstanceId;

        public int SlotIndex { get; }

        public bool IsPhaseEligible { get; }

        public bool CardExists { get; }

        public bool TowerExists { get; }

        public bool Exists => CardExists && TowerExists;

        public bool IsEligible => IsPhaseEligible && Exists;

        public bool FitsUnlockedSlots { get; }

        public bool SlotAvailable { get; }

        public bool FitsSlot => FitsUnlockedSlots && SlotAvailable;

        public bool FitsComputeCapacity { get; }

        public int SlotCost { get; }

        public int CardComputeCost { get; }

        public int ComputeCost => CardComputeCost;

        public int CurrentComputeCost { get; }

        public int ComputeCapacity { get; }

        public CommandError Error { get; }

        public string Message { get; }

        public bool CanPlace => Error == CommandError.None;

        public bool CanEquip => CanPlace;
    }

    public sealed partial class GameSimulation
    {
        /// <summary>
        /// 카드 장착 명령과 같은 순서로 단계, 인스턴스, 슬롯, 점유 및 연산력
        /// 조건을 검사한다. 타워 슬롯과 카드 장착 상태는 변경하지 않는다.
        /// </summary>
        public CardPlacementQuote GetCardPlacementQuote(
            int cardInstanceId,
            int towerInstanceId,
            int slotIndex)
        {
            EnsureInitialized();

            bool phaseEligible = IsLoadoutEditablePhase();
            CardInstanceState card = FindCardInstance(cardInstanceId);
            TowerState tower = FindTower(new TowerId(towerInstanceId));
            bool cardExists = card != null;
            bool towerExists = tower != null;
            if (!phaseEligible)
            {
                return CreateRejectedCardPlacementQuote(
                    cardInstanceId,
                    towerInstanceId,
                    slotIndex,
                    phaseEligible,
                    cardExists,
                    towerExists,
                    false,
                    false,
                    false,
                    0,
                    0,
                    0,
                    0,
                    phase == RunPhase.Combat
                        ? CommandError.CombatLoadoutLocked
                        : CommandError.InvalidPhase,
                    "Cards cannot be changed during combat.");
            }

            if (!cardExists || !towerExists)
            {
                return CreateRejectedCardPlacementQuote(
                    cardInstanceId,
                    towerInstanceId,
                    slotIndex,
                    true,
                    cardExists,
                    towerExists,
                    false,
                    false,
                    false,
                    0,
                    0,
                    0,
                    0,
                    CommandError.InvalidTarget,
                    "Card or tower instance does not exist.");
            }

            CompiledCardDefinition definition =
                content.GetCard(card.DefinitionId);
            CompiledTowerLevelBalance level =
                GetTowerLevelBalance(tower);
            int unlockedSlotCount =
                GetTowerUnlockedSlotCount(tower);
            bool fitsUnlockedSlots =
                slotIndex >= 0 &&
                slotIndex + definition.SlotCost <=
                    unlockedSlotCount;
            if (!fitsUnlockedSlots)
            {
                return CreateRejectedCardPlacementQuote(
                    cardInstanceId,
                    towerInstanceId,
                    slotIndex,
                    true,
                    true,
                    true,
                    false,
                    false,
                    false,
                    definition.SlotCost,
                    definition.ComputeCost,
                    0,
                    level.ComputeCapacity,
                    CommandError.SlotOutOfRange,
                    "Card does not fit in the requested slot.");
            }

            int[] candidateSlots =
                (int[])tower.CardInstanceIds.Clone();
            ClearCardFromSlots(candidateSlots, cardInstanceId);
            bool slotAvailable = CanPlaceCardInSlots(
                candidateSlots,
                cardInstanceId,
                definition.SlotCost,
                slotIndex);
            if (!slotAvailable)
            {
                return CreateRejectedCardPlacementQuote(
                    cardInstanceId,
                    towerInstanceId,
                    slotIndex,
                    true,
                    true,
                    true,
                    true,
                    false,
                    false,
                    definition.SlotCost,
                    definition.ComputeCost,
                    0,
                    level.ComputeCapacity,
                    CommandError.SlotOccupied,
                    "Requested slot is occupied.");
            }

            int currentComputeCost =
                ComputeTowerCost(tower, cardInstanceId);
            bool fitsComputeCapacity =
                currentComputeCost + definition.ComputeCost <=
                level.ComputeCapacity;
            if (!fitsComputeCapacity)
            {
                return CreateRejectedCardPlacementQuote(
                    cardInstanceId,
                    towerInstanceId,
                    slotIndex,
                    true,
                    true,
                    true,
                    true,
                    true,
                    false,
                    definition.SlotCost,
                    definition.ComputeCost,
                    currentComputeCost,
                    level.ComputeCapacity,
                    CommandError.ComputeCapacityExceeded,
                    "Tower compute capacity would be exceeded.");
            }

            return new CardPlacementQuote(
                cardInstanceId,
                towerInstanceId,
                slotIndex,
                true,
                true,
                true,
                true,
                true,
                true,
                definition.SlotCost,
                definition.ComputeCost,
                currentComputeCost,
                level.ComputeCapacity,
                CommandError.None,
                string.Empty);
        }

        private static CardPlacementQuote CreateRejectedCardPlacementQuote(
            int cardInstanceId,
            int towerInstanceId,
            int slotIndex,
            bool isPhaseEligible,
            bool cardExists,
            bool towerExists,
            bool fitsUnlockedSlots,
            bool slotAvailable,
            bool fitsComputeCapacity,
            int slotCost,
            int cardComputeCost,
            int currentComputeCost,
            int computeCapacity,
            CommandError error,
            string message)
        {
            return new CardPlacementQuote(
                cardInstanceId,
                towerInstanceId,
                slotIndex,
                isPhaseEligible,
                cardExists,
                towerExists,
                fitsUnlockedSlots,
                slotAvailable,
                fitsComputeCapacity,
                slotCost,
                cardComputeCost,
                currentComputeCost,
                computeCapacity,
                error,
                message);
        }
    }
}
