using System;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.GameLogic.Simulation.Testing
{
    /// <summary>
    /// 테스트 맵이 한 번에 생성할 적과 동시에 유지할 적의 상한이다.
    /// 무한 소환의 시간표는 Runtime 계층이 소유하고, 이 값은 각 요청이
    /// 브라우저를 멈출 정도로 누적되는 것만 시뮬레이션 경계에서 막는다.
    /// </summary>
    public readonly struct SandboxSimulationLimits
    {
        public const int DefaultMaxActiveEnemies = 256;
        public const int DefaultMaxSpawnBatchSize = 32;
        public const int MaximumCardGrantCount = 128;
        public const int MaximumOwnedCardCount = 8192;

        public SandboxSimulationLimits(
            int maxActiveEnemies,
            int maxSpawnBatchSize)
        {
            if (maxActiveEnemies <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxActiveEnemies));
            }
            if (maxSpawnBatchSize <= 0 ||
                maxSpawnBatchSize > maxActiveEnemies)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxSpawnBatchSize));
            }

            MaxActiveEnemies = maxActiveEnemies;
            MaxSpawnBatchSize = maxSpawnBatchSize;
        }

        public int MaxActiveEnemies { get; }
        public int MaxSpawnBatchSize { get; }

        public static SandboxSimulationLimits Default =>
            new SandboxSimulationLimits(
                DefaultMaxActiveEnemies,
                DefaultMaxSpawnBatchSize);
    }

    /// <summary>
    /// 테스트 적 생성 한 묶음의 데이터다.
    /// MaxHealthOverride가 0이면 콘텐츠 기본 체력에 HealthMultiplierBps를 적용하고,
    /// 양수이면 해당 HP를 그대로 사용한다. HP는 UI에 표시하는 일반 단위이며
    /// 시뮬레이션 내부에서는 1 HP = 1000 milli HP로 변환된다.
    /// </summary>
    public readonly struct SandboxEnemySpawnRequest
    {
        public SandboxEnemySpawnRequest(
            string enemyStableId,
            int count = 1,
            long maxHealthOverride = 0,
            int healthMultiplierBps = 10000,
            int speedMultiplierBps = 10000)
        {
            EnemyStableId = enemyStableId ?? string.Empty;
            Count = count;
            MaxHealthOverride = maxHealthOverride;
            HealthMultiplierBps = healthMultiplierBps;
            SpeedMultiplierBps = speedMultiplierBps;
        }

        public string EnemyStableId { get; }
        public int Count { get; }
        public long MaxHealthOverride { get; }
        public int HealthMultiplierBps { get; }
        public int SpeedMultiplierBps { get; }
    }

    public enum SandboxControlError
    {
        None = 0,
        NotInitialized = 1,
        NotInSandboxMode = 2,
        UnknownContent = 3,
        InvalidValue = 4,
        CapacityExceeded = 5,
        InvalidTarget = 6,
        BuildPointOccupied = 7,
        LoadoutRejected = 8,
        IdentityExhausted = 9
    }

    /// <summary>
    /// 테스트 조작의 성공 여부와 새로 만든 엔티티/인스턴스 ID를 함께 반환한다.
    /// 배열은 방어적 복사본이라 테스트 UI가 수정해도 권위 상태가 바뀌지 않는다.
    /// </summary>
    public readonly struct SandboxControlResult
    {
        private readonly int[] affectedIds;

        private SandboxControlResult(
            bool succeeded,
            SandboxControlError error,
            string message,
            int[] affectedIds)
        {
            Succeeded = succeeded;
            Error = error;
            Message = message ?? string.Empty;
            this.affectedIds = affectedIds == null
                ? Array.Empty<int>()
                : (int[])affectedIds.Clone();
        }

        public bool Succeeded { get; }
        public SandboxControlError Error { get; }
        public string Message { get; }
        public int AffectedCount =>
            affectedIds == null ? 0 : affectedIds.Length;
        public int FirstAffectedId =>
            affectedIds == null || affectedIds.Length == 0
                ? -1
                : affectedIds[0];
        public int[] AffectedIds =>
            affectedIds == null
                ? Array.Empty<int>()
                : (int[])affectedIds.Clone();

        internal static SandboxControlResult Success(
            params int[] affectedIds)
        {
            return new SandboxControlResult(
                true,
                SandboxControlError.None,
                string.Empty,
                affectedIds);
        }

        internal static SandboxControlResult Reject(
            SandboxControlError error,
            string message)
        {
            return new SandboxControlResult(
                false,
                error,
                message,
                Array.Empty<int>());
        }
    }

    /// <summary>
    /// 일반 런 규칙을 우회하는 테스트 맵 전용 포트다.
    /// Runtime은 이 인터페이스만 보며 GameSimulation의 내부 컬렉션에는 접근하지 않는다.
    /// </summary>
    public interface ISandboxSimulationControl
    {
        SandboxSimulationLimits Limits { get; }
        int MaximumCardGrantCount { get; }
        int MaximumOwnedCardCount { get; }
        string[] DebuffCardIds { get; }

        SandboxControlResult EnterSandboxMode();
        SandboxControlResult ExitSandboxMode();
        SandboxControlResult SetActiveEnemyLimit(int maximumActiveEnemies);
        SandboxControlResult SpawnEnemies(
            in SandboxEnemySpawnRequest request);
        SandboxControlResult RemoveAllEnemies();
        SandboxControlResult ApplyDebuffToAllEnemies(
            string cardStableId);
        SandboxControlResult SetGold(int amount);
        SandboxControlResult SetBaseHealth(int amount);
        SandboxControlResult GrantCards(
            string cardStableId,
            int count = 1);
        SandboxControlResult GrantEveryCard(
            int countPerDefinition = 1);
        SandboxControlResult UnlockTower(string towerStableId);
        SandboxControlResult PlaceTower(
            string towerStableId,
            int buildPointIndex);
        SandboxControlResult SetTowerLevel(
            int towerInstanceId,
            int level);
        SandboxControlResult EquipCard(
            int cardInstanceId,
            int towerInstanceId,
            int slotIndex);
        SandboxControlResult UnequipCard(int cardInstanceId);
        SandboxControlResult ReplaceCard(
            string cardStableId,
            int towerInstanceId,
            int slotIndex);
    }

    /// <summary>
    /// GameSimulation에 테스트 포트를 의도적으로 연결하는 유일한 공개 팩토리다.
    /// Attach 자체는 상태를 바꾸지 않으며 EnterSandboxMode 호출부터 우회 규칙이 적용된다.
    /// </summary>
    public sealed class SandboxSimulationControl :
        ISandboxSimulationControl
    {
        private readonly GameSimulation simulation;

        private SandboxSimulationControl(
            GameSimulation simulation,
            SandboxSimulationLimits limits)
        {
            this.simulation = simulation ??
                throw new ArgumentNullException(nameof(simulation));
            Limits = limits;
        }

        public SandboxSimulationLimits Limits { get; }
        public int MaximumCardGrantCount =>
            SandboxSimulationLimits.MaximumCardGrantCount;
        public int MaximumOwnedCardCount =>
            SandboxSimulationLimits.MaximumOwnedCardCount;
        public string[] DebuffCardIds =>
            simulation.SandboxGetDebuffCardIds();

        public static ISandboxSimulationControl Attach(
            GameSimulation simulation)
        {
            return Attach(
                simulation,
                SandboxSimulationLimits.Default);
        }

        public static ISandboxSimulationControl Attach(
            GameSimulation simulation,
            SandboxSimulationLimits limits)
        {
            if (limits.MaxActiveEnemies <= 0 ||
                limits.MaxSpawnBatchSize <= 0 ||
                limits.MaxSpawnBatchSize >
                    limits.MaxActiveEnemies)
            {
                throw new ArgumentException(
                    "Sandbox simulation limits are invalid.",
                    nameof(limits));
            }

            return new SandboxSimulationControl(
                simulation,
                limits);
        }

        public SandboxControlResult EnterSandboxMode()
        {
            return simulation.SandboxEnterMode(Limits);
        }

        public SandboxControlResult ExitSandboxMode()
        {
            return simulation.SandboxExitMode();
        }

        public SandboxControlResult SetActiveEnemyLimit(
            int maximumActiveEnemies)
        {
            return simulation.SandboxSetActiveEnemyLimit(
                maximumActiveEnemies);
        }

        public SandboxControlResult SpawnEnemies(
            in SandboxEnemySpawnRequest request)
        {
            return simulation.SandboxSpawnEnemies(in request);
        }

        public SandboxControlResult RemoveAllEnemies()
        {
            return simulation.SandboxRemoveAllEnemies();
        }

        public SandboxControlResult ApplyDebuffToAllEnemies(
            string cardStableId)
        {
            return simulation.SandboxApplyDebuffToAllEnemies(
                cardStableId);
        }

        public SandboxControlResult SetGold(int amount)
        {
            return simulation.SandboxSetGold(amount);
        }

        public SandboxControlResult SetBaseHealth(int amount)
        {
            return simulation.SandboxSetBaseHealth(amount);
        }

        public SandboxControlResult GrantCards(
            string cardStableId,
            int count = 1)
        {
            return simulation.SandboxGrantCards(
                cardStableId,
                count);
        }

        public SandboxControlResult GrantEveryCard(
            int countPerDefinition = 1)
        {
            return simulation.SandboxGrantEveryCard(
                countPerDefinition);
        }

        public SandboxControlResult UnlockTower(
            string towerStableId)
        {
            return simulation.SandboxUnlockTower(
                towerStableId);
        }

        public SandboxControlResult PlaceTower(
            string towerStableId,
            int buildPointIndex)
        {
            return simulation.SandboxPlaceTower(
                towerStableId,
                buildPointIndex);
        }

        public SandboxControlResult SetTowerLevel(
            int towerInstanceId,
            int level)
        {
            return simulation.SandboxSetTowerLevel(
                towerInstanceId,
                level);
        }

        public SandboxControlResult EquipCard(
            int cardInstanceId,
            int towerInstanceId,
            int slotIndex)
        {
            return simulation.SandboxEquipCard(
                cardInstanceId,
                towerInstanceId,
                slotIndex);
        }

        public SandboxControlResult UnequipCard(
            int cardInstanceId)
        {
            return simulation.SandboxUnequipCard(
                cardInstanceId);
        }

        public SandboxControlResult ReplaceCard(
            string cardStableId,
            int towerInstanceId,
            int slotIndex)
        {
            return simulation.SandboxReplaceCard(
                cardStableId,
                towerInstanceId,
                slotIndex);
        }
    }
}
