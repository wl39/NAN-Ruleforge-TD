using System;
using System.Collections.Generic;
using RuleforgeTD.Battle;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;
using RuleforgeTD.GameLogic.Simulation.Testing;
using RuleforgeTD.UI;
using UnityEngine;

namespace RuleforgeTD.UnityView.TestLab
{
    /// <summary>
    /// StageOne 표현 호스트와 GameLogic의 샌드박스 포트를 결합하는 TestLab 전용 adapter.
    /// UI는 ITestLabControlTarget만 보고, 이 타입만 두 구체 시스템을 함께 안다.
    /// </summary>
    internal sealed class TestLabBattleControlTarget :
        ITestLabControlTarget,
        IDisposable
    {
        private const int ConfiguredMaximumActiveEnemies = 2000;
        private const int ConfiguredMaximumSpawnBatchSize = 500;
        private const int ConfiguredDefaultActiveEnemyLimit = 200;
        private const int EmptyCardSlot = -1;
        private const int ContinuationCardSlot = -2;

        private readonly StageOneBattleController battle;
        private readonly GameSimulation simulation;
        private readonly ISandboxSimulationControl sandbox;
        private readonly TestLabEnemyOption[] enemyOptions;
        private readonly TestLabTowerOption[] towerOptions;
        private readonly TestLabContentOption[] cardOptions;
        private readonly TestLabContentOption[] debuffOptions;
        private bool disposed;

        public TestLabBattleControlTarget(
            StageOneBattleController battleController)
        {
            battle = battleController ??
                throw new ArgumentNullException(
                    nameof(battleController));
            if (!battle.IsInitialized)
            {
                battle.InitializeNow();
            }

            simulation = battle.AuthoritativeSimulation;
            CompiledContent content = battle.LoadedContent;
            if (simulation == null || content == null)
            {
                throw new InvalidOperationException(
                    "TestLab requires an initialized StageOne battle.");
            }

            sandbox = SandboxSimulationControl.Attach(
                simulation,
                new SandboxSimulationLimits(
                    ConfiguredMaximumActiveEnemies,
                    ConfiguredMaximumSpawnBatchSize));
            SandboxControlResult enter =
                sandbox.EnterSandboxMode();
            if (!enter.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not enter TestLab sandbox mode: " +
                    enter.Message);
            }
            SandboxControlResult initialEnemyLimit =
                sandbox.SetActiveEnemyLimit(
                    ConfiguredDefaultActiveEnemyLimit);
            if (!initialEnemyLimit.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not apply the TestLab enemy limit: " +
                    initialEnemyLimit.Message);
            }

            StageOneUiTextCatalog textCatalog =
                battle.LoadedTextCatalog;
            if (textCatalog == null)
            {
                throw new InvalidOperationException(
                    "TestLab requires StageOne's initialized text catalog.");
            }
            enemyOptions = BuildEnemyOptions(
                content,
                textCatalog);
            towerOptions = BuildTowerOptions(
                content,
                textCatalog);
            cardOptions = BuildCardOptions(
                content,
                textCatalog);
            debuffOptions = BuildDebuffOptions(
                sandbox.DebuffCardIds,
                cardOptions);
            battle.SynchronizeAuthoritativeState();
        }

        public IReadOnlyList<TestLabEnemyOption> EnemyOptions =>
            enemyOptions;
        public IReadOnlyList<TestLabTowerOption> TowerOptions =>
            towerOptions;
        public IReadOnlyList<TestLabContentOption> CardOptions =>
            cardOptions;
        public IReadOnlyList<TestLabContentOption> DebuffOptions =>
            debuffOptions;
        public int MaximumActiveEnemyCount =>
            sandbox.Limits.MaxActiveEnemies;
        public int DefaultActiveEnemyLimit =>
            ConfiguredDefaultActiveEnemyLimit;
        public int MaximumSpawnBatchSize =>
            sandbox.Limits.MaxSpawnBatchSize;
        public int MaximumCardGrantCount =>
            sandbox.MaximumCardGrantCount;
        public bool IsTowerLoadoutVisible =>
            battle.LoadoutView != null &&
            battle.LoadoutView.IsVisible;

        public TestLabCommandResult SetActiveEnemyLimit(
            int maximumActiveEnemies)
        {
            EnsureAvailable();
            return Convert(
                sandbox.SetActiveEnemyLimit(
                    maximumActiveEnemies),
                "활성 적 상한을 " +
                maximumActiveEnemies +
                "마리로 설정했습니다.");
        }

        public TestLabRuntimeState ReadState()
        {
            EnsureAvailable();
            SimulationSnapshot snapshot =
                simulation.GetSnapshot();
            int activeEnemies = 0;
            for (int i = 0; i < snapshot.Enemies.Length; i++)
            {
                if (snapshot.Enemies[i].Alive)
                {
                    activeEnemies++;
                }
            }

            var cardIds =
                new Dictionary<int, string>(
                    snapshot.Cards.Length);
            for (int i = 0; i < snapshot.Cards.Length; i++)
            {
                CardInstanceSnapshot card = snapshot.Cards[i];
                cardIds[card.Id] =
                    simulation.Content
                        .GetCard(card.DefinitionId)
                        .StableId;
            }

            var towers =
                new TestLabPlacedTower[snapshot.Towers.Length];
            for (int i = 0; i < snapshot.Towers.Length; i++)
            {
                TowerSnapshot tower = snapshot.Towers[i];
                var slots = new string[
                    tower.CardInstanceIds == null
                        ? 0
                        : tower.CardInstanceIds.Length];
                for (int slot = 0; slot < slots.Length; slot++)
                {
                    int instanceId =
                        ResolveSlotOwnerCardInstanceId(
                            tower,
                            slot);
                    slots[slot] =
                        instanceId >= 0 &&
                        cardIds.TryGetValue(
                            instanceId,
                            out string cardId)
                            ? cardId
                            : string.Empty;
                }

                towers[i] = new TestLabPlacedTower(
                    tower.Id,
                    tower.DefinitionId,
                    FindTowerDisplayName(
                        tower.DefinitionId),
                    tower.BuildPointIndex,
                    tower.Level,
                    slots);
            }

            return new TestLabRuntimeState(
                snapshot.Gold,
                snapshot.BaseHealth,
                activeEnemies,
                towers);
        }

        public TestLabCommandResult SpawnEnemies(
            in TestLabEnemySpawnSpec spec)
        {
            EnsureAvailable();
            var request = new SandboxEnemySpawnRequest(
                spec.EnemyId,
                spec.Count,
                spec.MaxHealthOverride,
                spec.HealthMultiplierBps,
                spec.SpeedMultiplierBps);
            SandboxControlResult result =
                sandbox.SpawnEnemies(in request);
            Synchronize();
            return Convert(
                result,
                spec.Count + "마리 생성 완료");
        }

        public TestLabCommandResult SpawnEveryEnemyOnce(
            int healthMultiplierBps,
            int maxHealthOverride,
            int speedMultiplierBps,
            int activeEnemyCap)
        {
            EnsureAvailable();
            int available = Math.Max(
                0,
                Math.Min(
                    activeEnemyCap,
                    MaximumActiveEnemyCount) -
                ReadState().ActiveEnemyCount);
            if (available <= 0)
            {
                return TestLabCommandResult.Failure(
                    "활성 적 상한에 도달했습니다.");
            }
            if (available < enemyOptions.Length)
            {
                return TestLabCommandResult.Failure(
                    "모든 적을 1마리씩 생성하려면 활성 여유가 " +
                    enemyOptions.Length +
                    "칸 필요하지만 " + available +
                    "칸만 남았습니다.");
            }

            int spawned = 0;
            for (int i = 0;
                 i < enemyOptions.Length;
                 i++)
            {
                var request = new SandboxEnemySpawnRequest(
                    enemyOptions[i].StableId,
                    1,
                    maxHealthOverride,
                    healthMultiplierBps,
                    speedMultiplierBps);
                SandboxControlResult result =
                    sandbox.SpawnEnemies(in request);
                if (!result.Succeeded)
                {
                    Synchronize();
                    return Convert(result, string.Empty);
                }

                spawned++;
            }

            Synchronize();
            return TestLabCommandResult.Success(
                "구현된 적 " + spawned +
                "종을 1마리씩 생성했습니다.");
        }

        public TestLabCommandResult RemoveAllEnemies()
        {
            EnsureAvailable();
            int before = ReadState().ActiveEnemyCount;
            SandboxControlResult result =
                sandbox.RemoveAllEnemies();
            Synchronize();
            return Convert(
                result,
                "현재 적 " + before +
                "마리를 제거했습니다.");
        }

        public TestLabCommandResult ApplyDebuffToAllEnemies(
            string cardId)
        {
            EnsureAvailable();
            SandboxControlResult result =
                sandbox.ApplyDebuffToAllEnemies(cardId);
            Synchronize();
            return Convert(
                result,
                cardId + " 디버프를 현재 적 " +
                result.AffectedCount +
                "마리에게 적용했습니다.");
        }

        public TestLabCommandResult SetGold(int amount)
        {
            EnsureAvailable();
            SandboxControlResult result =
                sandbox.SetGold(amount);
            Synchronize();
            return Convert(
                result,
                "골드를 " + amount + "로 설정했습니다.");
        }

        public TestLabCommandResult SetBaseHealth(int amount)
        {
            EnsureAvailable();
            SandboxControlResult result =
                sandbox.SetBaseHealth(amount);
            Synchronize();
            return Convert(
                result,
                "기지 체력을 " + amount +
                "로 설정했습니다.");
        }

        public float SetCombatSpeed(float multiplier)
        {
            EnsureAvailable();
            return battle.SetSpeed(multiplier);
        }

        public TestLabCommandResult GrantCard(
            string cardId,
            int count)
        {
            EnsureAvailable();
            if (count <= 0 ||
                count > sandbox.MaximumCardGrantCount)
            {
                return TestLabCommandResult.Failure(
                    "한 번에 지급할 카드 수량은 1부터 " +
                    sandbox.MaximumCardGrantCount +
                    " 사이여야 합니다.");
            }
            if (simulation.GetSnapshot().Cards.Length >
                sandbox.MaximumOwnedCardCount - count)
            {
                return TestLabCommandResult.Failure(
                    "카드 인벤토리 상한을 초과합니다.");
            }

            SandboxControlResult result =
                sandbox.GrantCards(cardId, count);
            Synchronize();
            return Convert(
                result,
                cardId + " 카드 " + count +
                "장을 지급했습니다.");
        }

        public TestLabCommandResult GrantEveryCard(
            int countPerDefinition)
        {
            EnsureAvailable();
            if (countPerDefinition <= 0 ||
                countPerDefinition >
                    sandbox.MaximumCardGrantCount)
            {
                return TestLabCommandResult.Failure(
                    "정의당 카드 수량은 1부터 " +
                    sandbox.MaximumCardGrantCount +
                    " 사이여야 합니다.");
            }

            SandboxControlResult result =
                sandbox.GrantEveryCard(
                    countPerDefinition);
            Synchronize();
            return Convert(
                result,
                "전체 " + cardOptions.Length +
                "개 카드 정의에서 총 " +
                result.AffectedCount +
                "장을 지급했습니다.");
        }

        public TestLabCommandResult PlaceTower(
            string towerId,
            int preferredBuildPointIndex,
            int level)
        {
            EnsureAvailable();
            if (!TryFindTowerOption(
                    towerId,
                    out TestLabTowerOption option))
            {
                return TestLabCommandResult.Failure(
                    "알 수 없는 타워 정의입니다: " +
                    towerId);
            }
            if (level < 1 ||
                level > option.MaximumLevel)
            {
                return TestLabCommandResult.Failure(
                    towerId + " 타워 레벨은 1부터 " +
                    option.MaximumLevel +
                    " 사이여야 합니다.");
            }

            int buildPoint = preferredBuildPointIndex >= 0
                ? preferredBuildPointIndex
                : FindFirstEmptyBuildPoint();
            if (buildPoint < 0)
            {
                return TestLabCommandResult.Failure(
                    "비어 있는 건설 지점이 없습니다.");
            }

            SandboxControlResult unlock =
                sandbox.UnlockTower(towerId);
            if (!unlock.Succeeded)
            {
                return Convert(unlock, string.Empty);
            }

            SandboxControlResult placement =
                sandbox.PlaceTower(towerId, buildPoint);
            if (!placement.Succeeded)
            {
                Synchronize();
                return Convert(placement, string.Empty);
            }

            int towerInstanceId =
                placement.FirstAffectedId;
            SandboxControlResult setLevel =
                sandbox.SetTowerLevel(
                    towerInstanceId,
                    Math.Max(1, level));
            Synchronize();
            if (!setLevel.Succeeded)
            {
                return Convert(setLevel, string.Empty);
            }

            battle.SelectTowerContext(towerInstanceId);
            return TestLabCommandResult.Success(
                towerId + " 타워를 지점 " +
                buildPoint + "에 Lv." + level +
                "로 배치했습니다.");
        }

        public TestLabCommandResult PlaceEveryTower(int level)
        {
            EnsureAvailable();
            SimulationSnapshot snapshot =
                simulation.GetSnapshot();
            var placedDefinitionIds =
                new HashSet<string>(
                    StringComparer.Ordinal);
            for (int i = 0;
                 i < snapshot.Towers.Length;
                 i++)
            {
                placedDefinitionIds.Add(
                    snapshot.Towers[i].DefinitionId);
            }

            var missing =
                new List<TestLabTowerOption>();
            for (int i = 0;
                 i < towerOptions.Length;
                 i++)
            {
                if (!placedDefinitionIds.Contains(
                        towerOptions[i].StableId))
                {
                    missing.Add(towerOptions[i]);
                }
            }

            if (missing.Count == 0)
            {
                return TestLabCommandResult.Success(
                    "모든 구현 타워가 이미 배치되어 있습니다.");
            }

            int[] emptyBuildPoints =
                FindEmptyBuildPoints(snapshot);
            if (emptyBuildPoints.Length < missing.Count)
            {
                return TestLabCommandResult.Failure(
                    "모든 타워를 배치하려면 빈 건설 지점 " +
                    missing.Count + "개가 필요하지만 " +
                    emptyBuildPoints.Length +
                    "개만 남았습니다.");
            }

            int placed = 0;
            for (int i = 0; i < missing.Count; i++)
            {
                TestLabTowerOption option =
                    missing[i];
                SandboxControlResult unlock =
                    sandbox.UnlockTower(option.StableId);
                if (!unlock.Succeeded)
                {
                    Synchronize();
                    return Convert(unlock, string.Empty);
                }

                SandboxControlResult placement =
                    sandbox.PlaceTower(
                        option.StableId,
                        emptyBuildPoints[i]);
                if (!placement.Succeeded)
                {
                    Synchronize();
                    return Convert(placement, string.Empty);
                }

                SandboxControlResult setLevel =
                    sandbox.SetTowerLevel(
                        placement.FirstAffectedId,
                        Math.Min(
                            Math.Max(1, level),
                            option.MaximumLevel));
                if (!setLevel.Succeeded)
                {
                    Synchronize();
                    return Convert(setLevel, string.Empty);
                }

                placed++;
            }

            Synchronize();
            return TestLabCommandResult.Success(
                "누락된 타워 " + placed +
                "종을 빈 건설 지점에 배치했습니다.");
        }

        public TestLabCommandResult SetTowerLevel(
            int towerInstanceId,
            int level)
        {
            EnsureAvailable();
            SandboxControlResult result =
                sandbox.SetTowerLevel(
                    towerInstanceId,
                    level);
            Synchronize();
            return Convert(
                result,
                "타워 #" + towerInstanceId +
                "의 레벨을 " + level +
                "로 설정했습니다.");
        }

        public TestLabCommandResult EquipCard(
            string cardId,
            int towerInstanceId,
            int slotIndex)
        {
            EnsureAvailable();
            SandboxControlResult equip =
                sandbox.ReplaceCard(
                    cardId,
                    towerInstanceId,
                    slotIndex);
            Synchronize();
            return Convert(
                equip,
                cardId + " 카드를 타워 #" +
                towerInstanceId + " 슬롯 " +
                (slotIndex + 1) + "에 장착했습니다.");
        }

        public TestLabCommandResult RemoveCard(
            int towerInstanceId,
            int slotIndex)
        {
            EnsureAvailable();
            TowerSnapshot tower = FindTower(
                simulation.GetSnapshot(),
                towerInstanceId);
            if (tower.Id < 0 ||
                tower.CardInstanceIds == null ||
                slotIndex < 0 ||
                slotIndex >= tower.CardInstanceIds.Length)
            {
                return TestLabCommandResult.Failure(
                    "타워 또는 슬롯이 유효하지 않습니다.");
            }

            int cardInstanceId =
                ResolveSlotOwnerCardInstanceId(
                    tower,
                    slotIndex);
            if (cardInstanceId < 0)
            {
                return TestLabCommandResult.Success(
                    "이미 비어 있는 슬롯입니다.");
            }

            SandboxControlResult result =
                sandbox.UnequipCard(cardInstanceId);
            Synchronize();
            return Convert(
                result,
                "슬롯 " + (slotIndex + 1) +
                "의 카드를 제거했습니다.");
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            sandbox.ExitSandboxMode();
        }

        private int FindFirstEmptyBuildPoint()
        {
            SimulationSnapshot snapshot =
                simulation.GetSnapshot();
            for (int spot = 0;
                 spot < snapshot.BuildSpots.Length;
                 spot++)
            {
                bool occupied = false;
                for (int tower = 0;
                     tower < snapshot.Towers.Length;
                     tower++)
                {
                    if (snapshot.Towers[tower]
                        .BuildPointIndex == spot)
                    {
                        occupied = true;
                        break;
                    }
                }

                if (!occupied)
                {
                    return spot;
                }
            }

            return -1;
        }

        private static int[] FindEmptyBuildPoints(
            SimulationSnapshot snapshot)
        {
            var empty = new List<int>();
            for (int spot = 0;
                 spot < snapshot.BuildSpots.Length;
                 spot++)
            {
                bool occupied = false;
                for (int tower = 0;
                     tower < snapshot.Towers.Length;
                     tower++)
                {
                    if (snapshot.Towers[tower]
                        .BuildPointIndex == spot)
                    {
                        occupied = true;
                        break;
                    }
                }

                if (!occupied)
                {
                    empty.Add(spot);
                }
            }

            return empty.ToArray();
        }

        private bool TryFindTowerOption(
            string stableId,
            out TestLabTowerOption option)
        {
            for (int i = 0; i < towerOptions.Length; i++)
            {
                if (string.Equals(
                        towerOptions[i].StableId,
                        stableId,
                        StringComparison.Ordinal))
                {
                    option = towerOptions[i];
                    return true;
                }
            }

            option = default(TestLabTowerOption);
            return false;
        }

        private static TowerSnapshot FindTower(
            SimulationSnapshot snapshot,
            int towerId)
        {
            for (int i = 0; i < snapshot.Towers.Length; i++)
            {
                if (snapshot.Towers[i].Id == towerId)
                {
                    return snapshot.Towers[i];
                }
            }

            return new TowerSnapshot(
                -1,
                string.Empty,
                -1,
                SimPosition.Origin,
                Array.Empty<int>(),
                1,
                SubjectType.Projectile);
        }

        /// <summary>
        /// 다중 슬롯 카드의 -2 continuation을 왼쪽 owner 카드 인스턴스로
        /// 역추적한다. 빈 슬롯이나 손상된 continuation 체인은 -1을 반환한다.
        /// 상태 라벨과 제거 명령이 반드시 같은 슬롯 의미를 사용하도록 두 경로가
        /// 이 helper를 공유한다.
        /// </summary>
        internal static int ResolveSlotOwnerCardInstanceId(
            TowerSnapshot tower,
            int slotIndex)
        {
            int[] slots = tower.CardInstanceIds;
            if (slots == null ||
                slotIndex < 0 ||
                slotIndex >= slots.Length)
            {
                return EmptyCardSlot;
            }

            int instanceId = slots[slotIndex];
            if (instanceId >= 0)
            {
                return instanceId;
            }
            if (instanceId != ContinuationCardSlot)
            {
                return EmptyCardSlot;
            }

            for (int ownerSlot = slotIndex - 1;
                 ownerSlot >= 0;
                 ownerSlot--)
            {
                int ownerCandidate = slots[ownerSlot];
                if (ownerCandidate >= 0)
                {
                    return ownerCandidate;
                }
                if (ownerCandidate != ContinuationCardSlot)
                {
                    break;
                }
            }

            return EmptyCardSlot;
        }

        private string FindTowerDisplayName(string stableId)
        {
            for (int i = 0; i < towerOptions.Length; i++)
            {
                if (string.Equals(
                        towerOptions[i].StableId,
                        stableId,
                        StringComparison.Ordinal))
                {
                    return towerOptions[i].DisplayName;
                }
            }

            return stableId;
        }

        private void Synchronize()
        {
            battle.SynchronizeAuthoritativeState();
        }

        private static TestLabCommandResult Convert(
            SandboxControlResult result,
            string successMessage)
        {
            if (result.Succeeded)
            {
                return TestLabCommandResult.Success(
                    string.IsNullOrWhiteSpace(successMessage)
                        ? "완료"
                        : successMessage);
            }

            return TestLabCommandResult.Failure(
                string.IsNullOrWhiteSpace(result.Message)
                    ? result.Error.ToString()
                    : result.Message);
        }

        private static TestLabEnemyOption[] BuildEnemyOptions(
            CompiledContent content,
            StageOneUiTextCatalog text)
        {
            CompiledEnemyDefinition[] definitions =
                content.Enemies;
            var result =
                new TestLabEnemyOption[definitions.Length];
            for (int i = 0; i < definitions.Length; i++)
            {
                CompiledEnemyDefinition definition =
                    definitions[i];
                string displayName =
                    ResolveDisplayName(
                        text,
                        definition.DisplayNameKey,
                        definition.StableId);
                result[i] = new TestLabEnemyOption(
                    new TestLabContentOption(
                        definition.StableId,
                        displayName,
                        definition.Rank.ToString()),
                    definition.MaxHealthMilli);
            }

            return result;
        }

        private static TestLabTowerOption[] BuildTowerOptions(
            CompiledContent content,
            StageOneUiTextCatalog text)
        {
            CompiledTowerDefinition[] definitions =
                content.Towers;
            var result =
                new TestLabTowerOption[definitions.Length];
            for (int i = 0; i < definitions.Length; i++)
            {
                CompiledTowerDefinition definition =
                    definitions[i];
                result[i] = new TestLabTowerOption(
                    new TestLabContentOption(
                        definition.StableId,
                        ResolveDisplayName(
                            text,
                            definition.DisplayNameKey,
                            definition.StableId),
                        definition.Trigger + " / " +
                        definition.SubjectTypeMode),
                    definition.SlotCount,
                    definition.MaxLevel);
            }

            return result;
        }

        private static TestLabContentOption[] BuildCardOptions(
            CompiledContent content,
            StageOneUiTextCatalog text)
        {
            CompiledCardDefinition[] definitions =
                content.Cards;
            var result =
                new TestLabContentOption[definitions.Length];
            for (int i = 0; i < definitions.Length; i++)
            {
                CompiledCardDefinition definition =
                    definitions[i];
                StageOneCardDisplay display =
                    text.GetCardDisplay(
                        definition,
                        SubjectType.Projectile);
                result[i] = new TestLabContentOption(
                    definition.StableId,
                    display.Name,
                    definition.Tier +
                    " / 연산 " +
                    definition.ComputeCost);
            }

            return result;
        }

        private static TestLabContentOption[] BuildDebuffOptions(
            string[] debuffCardIds,
            TestLabContentOption[] allCards)
        {
            if (debuffCardIds == null ||
                debuffCardIds.Length == 0)
            {
                return Array.Empty<TestLabContentOption>();
            }

            var result =
                new List<TestLabContentOption>(
                    debuffCardIds.Length);
            for (int debuffIndex = 0;
                 debuffIndex < debuffCardIds.Length;
                 debuffIndex++)
            {
                string stableId =
                    debuffCardIds[debuffIndex];
                for (int cardIndex = 0;
                     cardIndex < allCards.Length;
                     cardIndex++)
                {
                    if (!string.Equals(
                            allCards[cardIndex].StableId,
                            stableId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    result.Add(allCards[cardIndex]);
                    break;
                }
            }

            return result.ToArray();
        }

        private static string ResolveDisplayName(
            StageOneUiTextCatalog text,
            string key,
            string stableId)
        {
            if (text != null &&
                text.TryGet(key, out string value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            if (string.IsNullOrWhiteSpace(stableId))
            {
                return "(unknown)";
            }

            string[] words = stableId.Split('_');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] =
                        char.ToUpperInvariant(words[i][0]) +
                        words[i].Substring(1);
                }
            }

            return string.Join(" ", words);
        }

        private void EnsureAvailable()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(TestLabBattleControlTarget));
            }
        }
    }
}
