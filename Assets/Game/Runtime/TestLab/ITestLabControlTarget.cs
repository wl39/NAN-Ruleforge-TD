using System;
using System.Collections.Generic;

namespace RuleforgeTD.UnityView.TestLab
{
    /// <summary>
    /// 테스트 패널에 노출할 콘텐츠 한 항목이다.
    /// 패널은 GameSimulation이나 컴파일 콘텐츠 타입을 직접 알 필요가 없다.
    /// </summary>
    public readonly struct TestLabContentOption
    {
        public TestLabContentOption(
            string stableId,
            string displayName,
            string detail = "")
        {
            StableId = stableId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? StableId
                : displayName;
            Detail = detail ?? string.Empty;
        }

        public string StableId { get; }
        public string DisplayName { get; }
        public string Detail { get; }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Detail)
                ? DisplayName + "  [" + StableId + "]"
                : DisplayName + "  [" + StableId + "]  " + Detail;
        }
    }

    public readonly struct TestLabEnemyOption
    {
        public TestLabEnemyOption(
            TestLabContentOption content,
            long baseHealthMilli)
        {
            Content = content;
            BaseHealthMilli = Math.Max(1L, baseHealthMilli);
        }

        public TestLabContentOption Content { get; }
        public long BaseHealthMilli { get; }
        public string StableId => Content.StableId;
        public string DisplayName => Content.DisplayName;
    }

    public readonly struct TestLabTowerOption
    {
        public TestLabTowerOption(
            TestLabContentOption content,
            int slotCount,
            int maximumLevel)
        {
            Content = content;
            SlotCount = Math.Max(0, slotCount);
            MaximumLevel = Math.Max(1, maximumLevel);
        }

        public TestLabContentOption Content { get; }
        public int SlotCount { get; }
        public int MaximumLevel { get; }
        public string StableId => Content.StableId;
        public string DisplayName => Content.DisplayName;
    }

    public readonly struct TestLabPlacedTower
    {
        public TestLabPlacedTower(
            int instanceId,
            string definitionId,
            string displayName,
            int buildPointIndex,
            int level,
            string[] slotCardIds)
        {
            InstanceId = instanceId;
            DefinitionId = definitionId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? DefinitionId
                : displayName;
            BuildPointIndex = buildPointIndex;
            Level = Math.Max(1, level);
            SlotCardIds = slotCardIds == null
                ? Array.Empty<string>()
                : (string[])slotCardIds.Clone();
        }

        public int InstanceId { get; }
        public string DefinitionId { get; }
        public string DisplayName { get; }
        public int BuildPointIndex { get; }
        public int Level { get; }
        public string[] SlotCardIds { get; }
        public int SlotCount => SlotCardIds.Length;

        public string GetSlotLabel(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCardIds.Length)
            {
                return "슬롯 " + (slotIndex + 1);
            }

            string cardId = SlotCardIds[slotIndex];
            return "슬롯 " + (slotIndex + 1) + ": " +
                (string.IsNullOrWhiteSpace(cardId)
                    ? "(비어 있음)"
                    : cardId);
        }

        public override string ToString()
        {
            return DisplayName + " #" + InstanceId +
                "  Lv." + Level +
                "  (지점 " + BuildPointIndex + ")";
        }
    }

    public readonly struct TestLabRuntimeState
    {
        public TestLabRuntimeState(
            int gold,
            int baseHealth,
            int activeEnemyCount,
            TestLabPlacedTower[] placedTowers)
        {
            Gold = Math.Max(0, gold);
            BaseHealth = Math.Max(0, baseHealth);
            ActiveEnemyCount = Math.Max(0, activeEnemyCount);
            PlacedTowers = placedTowers == null
                ? Array.Empty<TestLabPlacedTower>()
                : (TestLabPlacedTower[])placedTowers.Clone();
        }

        public int Gold { get; }
        public int BaseHealth { get; }
        public int ActiveEnemyCount { get; }
        public TestLabPlacedTower[] PlacedTowers { get; }
    }

    public readonly struct TestLabEnemySpawnSpec
    {
        public TestLabEnemySpawnSpec(
            string enemyId,
            int count,
            int healthMultiplierBps,
            int maxHealthOverride,
            int speedMultiplierBps)
        {
            EnemyId = enemyId ?? string.Empty;
            Count = Math.Max(1, count);
            HealthMultiplierBps = Math.Max(1, healthMultiplierBps);
            MaxHealthOverride = Math.Max(0, maxHealthOverride);
            SpeedMultiplierBps = Math.Max(0, speedMultiplierBps);
        }

        public string EnemyId { get; }
        public int Count { get; }
        public int HealthMultiplierBps { get; }
        /// <summary>
        /// 일반 UI HP 단위의 절대 최대 체력이다. 0이면 정의 HP × 배율을 사용한다.
        /// GameLogic 내부 milli 변환은 샌드박스 facade 한 곳에서만 수행한다.
        /// </summary>
        public int MaxHealthOverride { get; }
        public int SpeedMultiplierBps { get; }
    }

    public readonly struct TestLabCommandResult
    {
        private TestLabCommandResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
        }

        public bool Succeeded { get; }
        public string Message { get; }

        public static TestLabCommandResult Success(string message)
        {
            return new TestLabCommandResult(true, message);
        }

        public static TestLabCommandResult Failure(string message)
        {
            return new TestLabCommandResult(false, message);
        }
    }

    /// <summary>
    /// 테스트 UI와 권위 시뮬레이션 사이의 유일한 변경 경계다.
    /// 실제 구현은 테스트 전용 simulation facade를 사용하고 일반 전투 UI는 모른다.
    /// </summary>
    public interface ITestLabControlTarget
    {
        IReadOnlyList<TestLabEnemyOption> EnemyOptions { get; }
        IReadOnlyList<TestLabTowerOption> TowerOptions { get; }
        IReadOnlyList<TestLabContentOption> CardOptions { get; }
        IReadOnlyList<TestLabContentOption> DebuffOptions { get; }
        int MaximumActiveEnemyCount { get; }
        int DefaultActiveEnemyLimit { get; }
        int MaximumSpawnBatchSize { get; }
        int MaximumCardGrantCount { get; }
        bool IsTowerLoadoutVisible { get; }
        TestLabRuntimeState ReadState();

        TestLabCommandResult SetActiveEnemyLimit(int maximumActiveEnemies);
        TestLabCommandResult SpawnEnemies(in TestLabEnemySpawnSpec spec);
        TestLabCommandResult SpawnEveryEnemyOnce(
            int healthMultiplierBps,
            int maxHealthOverride,
            int speedMultiplierBps,
            int activeEnemyCap);
        TestLabCommandResult RemoveAllEnemies();
        TestLabCommandResult ApplyDebuffToAllEnemies(
            string cardId);
        TestLabCommandResult SetGold(int amount);
        TestLabCommandResult SetBaseHealth(int amount);
        float SetCombatSpeed(float multiplier);
        TestLabCommandResult GrantCard(string cardId, int count);
        TestLabCommandResult GrantEveryCard(int countPerDefinition);
        TestLabCommandResult PlaceTower(
            string towerId,
            int preferredBuildPointIndex,
            int level);
        TestLabCommandResult PlaceEveryTower(int level);
        TestLabCommandResult SetTowerLevel(
            int towerInstanceId,
            int level);
        TestLabCommandResult EquipCard(
            string cardId,
            int towerInstanceId,
            int slotIndex);
        TestLabCommandResult RemoveCard(
            int towerInstanceId,
            int slotIndex);
    }
}
