using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;
using RuleforgeTD.GameLogic.Simulation.Testing;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// TestLab 전용 상태 변경 포트의 내부 구현이다.
    /// 일반 Submit 명령은 이 파일을 통하지 않으며, 공개 진입은
    /// SandboxSimulationControl.Attach로 제한된다.
    /// </summary>
    public sealed partial class GameSimulation
    {
        private const long MaximumSandboxHealthOverride =
            1_000_000_000L;
        private const int MaximumSandboxHealthMultiplierBps =
            10_000_000;
        private const int MaximumSandboxSpeedMultiplierBps =
            1_000_000;
        private const int MaximumSandboxIntegerResource =
            1_000_000_000;
        private const int MilliHealthPerHealth = 1000;

        private bool sandboxTestingMode;
        private int sandboxActiveEnemyHardLimit;
        private int sandboxActiveEnemyLimit;
        private int sandboxSpawnBatchLimit;
        private readonly List<int>
            sandboxCompletedLineageScratch =
                new List<int>(256);

        internal SandboxControlResult SandboxEnterMode(
            SandboxSimulationLimits limits)
        {
            if (!initialized)
            {
                return SandboxNotInitialized();
            }

            sandboxTestingMode = true;
            sandboxActiveEnemyHardLimit =
                limits.MaxActiveEnemies;
            sandboxActiveEnemyLimit =
                limits.MaxActiveEnemies;
            sandboxSpawnBatchLimit =
                limits.MaxSpawnBatchSize;
            phase = RunPhase.Combat;
            currentWaveIndex = -1;
            waveStartTick = tick;
            waveSpawns = new WaveSpawnRuntime[0];
            draftOffers.Clear();
            cardPackOffers.Clear();
            cardPacks.Clear();
            cardPackProgress = 0;
            nextCardPackThresholdIndex = 0;
            activeShimmeringLineageId = -1;
            activeCardPackId = -1;
            pendingCardInstanceId = -1;
            phaseAfterCardPack = RunPhase.Combat;
            waveRewardsPending = false;
            regularDraftPending = false;
            bossCardPackAwardedThisWave = false;
            victoryPending = false;

            for (int definitionIndex = 0;
                 definitionIndex < content.TowerCount;
                 definitionIndex++)
            {
                ownedTowerDefinitions.Add(definitionIndex);
            }
            for (int spotIndex = 0;
                 spotIndex < unlockedBuildSpots.Length;
                 spotIndex++)
            {
                unlockedBuildSpots[spotIndex] = true;
            }

            ClearSandboxBattlefield();
            for (int towerIndex = 0;
                 towerIndex < towers.Count;
                 towerIndex++)
            {
                TowerState tower = towers[towerIndex];
                tower.CooldownRemaining = 0;
                tower.AttackWindupRemaining = 0;
                tower.PendingAttackTargetId =
                    EntityId.Invalid;
                tower.GoldGeneratedThisWave = 0;
                tower.TargetsInside.Clear();
                tower.LastTargetTriggerTick.Clear();
                CompileTowerProgram(tower);
            }

            return SandboxControlResult.Success();
        }

        internal SandboxControlResult SandboxExitMode()
        {
            if (!initialized)
            {
                return SandboxNotInitialized();
            }
            if (!sandboxTestingMode)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.NotInSandboxMode,
                    "Sandbox mode is not active.");
            }

            ClearSandboxBattlefield();
            sandboxTestingMode = false;
            sandboxActiveEnemyHardLimit = 0;
            sandboxActiveEnemyLimit = 0;
            sandboxSpawnBatchLimit = 0;
            phase = RunPhase.Planning;
            currentWaveIndex = -1;
            waveSpawns = new WaveSpawnRuntime[0];
            return SandboxControlResult.Success();
        }

        internal SandboxControlResult
            SandboxSetActiveEnemyLimit(
                int maximumActiveEnemies)
        {
            SandboxControlResult modeCheck =
                RequireSandboxMode();
            if (!modeCheck.Succeeded)
            {
                return modeCheck;
            }
            if (maximumActiveEnemies <= 0 ||
                maximumActiveEnemies >
                    sandboxActiveEnemyHardLimit)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.InvalidValue,
                    "The active enemy limit must be within the facade hard limit.");
            }

            // 이미 살아 있는 적은 임의로 제거하지 않는다. 현재 수보다 낮춰도
            // 새 수동/파생 생성을 즉시 막고 자연 사망·제거로 새 상한까지 배출한다.
            sandboxActiveEnemyLimit =
                maximumActiveEnemies;
            return SandboxControlResult.Success();
        }

        internal SandboxControlResult SandboxSpawnEnemies(
            in SandboxEnemySpawnRequest request)
        {
            SandboxControlResult modeCheck =
                RequireSandboxMode();
            if (!modeCheck.Succeeded)
            {
                return modeCheck;
            }

            if (request.Count <= 0 ||
                request.Count > sandboxSpawnBatchLimit)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.InvalidValue,
                    "Enemy count must be within the configured batch limit.");
            }
            if (request.MaxHealthOverride < 0 ||
                request.MaxHealthOverride >
                    MaximumSandboxHealthOverride)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.InvalidValue,
                    "Enemy health override is outside the safe range.");
            }
            if (request.MaxHealthOverride == 0 &&
                (request.HealthMultiplierBps <= 0 ||
                 request.HealthMultiplierBps >
                    MaximumSandboxHealthMultiplierBps))
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.InvalidValue,
                    "Enemy health multiplier is outside the safe range.");
            }
            if (request.SpeedMultiplierBps < 0 ||
                request.SpeedMultiplierBps >
                    MaximumSandboxSpeedMultiplierBps)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.InvalidValue,
                    "Enemy speed multiplier is outside the safe range.");
            }
            if (string.IsNullOrEmpty(
                    request.EnemyStableId))
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.UnknownContent,
                    "Enemy id is required.");
            }
            if (!content.TryGetEnemyId(
                    request.EnemyStableId,
                    out EnemyDefinitionId definitionId))
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.UnknownContent,
                    "Unknown enemy '" +
                    request.EnemyStableId + "'.");
            }

            if (!HasSandboxEnemyCapacity(
                    request.Count))
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.CapacityExceeded,
                    "The active sandbox enemy limit would be exceeded.");
            }
            if (nextEntityId >
                int.MaxValue - request.Count)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.IdentityExhausted,
                    "Enemy identity space is exhausted.");
            }

            CompiledEnemyDefinition definition =
                content.GetEnemy(definitionId);
            long maxHealthMilli =
                request.MaxHealthOverride > 0
                    ? checked(
                        request.MaxHealthOverride *
                        MilliHealthPerHealth)
                    : Math.Max(
                        1L,
                        DeterministicMath.MultiplyBasisPoints(
                            (long)definition.MaxHealthMilli,
                            request.HealthMultiplierBps));

            var entityIds = new int[request.Count];
            for (int spawnIndex = 0;
                 spawnIndex < request.Count;
                 spawnIndex++)
            {
                EnemyState enemy = SpawnEnemy(
                    definitionId,
                    EnemySpawnOrigin.Sandbox);
                enemy.HealthMilli = maxHealthMilli;
                enemy.MaxHealthMilli = maxHealthMilli;
                enemy.SpeedMultiplierBps =
                    request.SpeedMultiplierBps;
                entityIds[spawnIndex] = enemy.Id.Value;
            }

            spatialIndex.Rebuild(enemies);
            return SandboxControlResult.Success(entityIds);
        }

        internal SandboxControlResult
            SandboxRemoveAllEnemies()
        {
            SandboxControlResult modeCheck =
                RequireSandboxMode();
            if (!modeCheck.Succeeded)
            {
                return modeCheck;
            }

            ClearSandboxBattlefield();
            return SandboxControlResult.Success();
        }

        internal string[] SandboxGetDebuffCardIds()
        {
            if (!initialized)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            for (int cardIndex = 0;
                 cardIndex < content.CardCount;
                 cardIndex++)
            {
                CompiledCardDefinition card =
                    content.Cards[cardIndex];
                if (HasSandboxDebuffNode(
                        card.EnemyEffectsInternal))
                {
                    result.Add(card.StableId);
                }
            }

            return result.ToArray();
        }

        /// <summary>
        /// TestLab의 현재 적 전체에 선택한 카드의 적 대상 디버프 노드만 적용한다.
        /// 강도·지속시간·중첩은 별도 테스트 상수가 아니라 컴파일된 카드 데이터를
        /// 그대로 사용하므로 밸런스 데이터 변경이 도구에도 즉시 반영된다.
        /// </summary>
        internal SandboxControlResult
            SandboxApplyDebuffToAllEnemies(
                string cardStableId)
        {
            SandboxControlResult modeCheck =
                RequireSandboxMode();
            if (!modeCheck.Succeeded)
            {
                return modeCheck;
            }
            if (string.IsNullOrEmpty(cardStableId) ||
                !content.TryGetCardId(
                    cardStableId,
                    out CardId cardId))
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.UnknownContent,
                    "Unknown card '" +
                    (cardStableId ?? string.Empty) + "'.");
            }

            CompiledCardDefinition card =
                content.GetCard(cardId);
            CompiledEffectNode[] nodes =
                card.EnemyEffectsInternal;
            if (!HasSandboxDebuffNode(nodes))
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.InvalidValue,
                    "Card '" + cardStableId +
                    "' does not define an enemy debuff.");
            }

            var targetIds = new List<int>(enemies.Count);
            for (int enemyIndex = 0;
                 enemyIndex < enemies.Count;
                 enemyIndex++)
            {
                EnemyState enemy = enemies[enemyIndex];
                if (enemy.Alive)
                {
                    targetIds.Add(enemy.Id.Value);
                }
            }

            for (int targetIndex = 0;
                 targetIndex < targetIds.Count;
                 targetIndex++)
            {
                EntityId targetId =
                    new EntityId(targetIds[targetIndex]);
                var context = new EffectExecutionContext(
                    SubjectType.Enemy,
                    targetId,
                    TowerId.Invalid,
                    cardId,
                    -1,
                    targetId,
                    CreateRootChain(),
                    CreateActivation(),
                    EventId.Invalid,
                    0,
                    0,
                    0);
                MarkEnemyCardVisual(targetId, card);

                for (int nodeIndex = 0;
                     nodeIndex < nodes.Length;
                     nodeIndex++)
                {
                    CompiledEffectNode node =
                        nodes[nodeIndex];
                    if (!IsSandboxDebuffOperation(
                            node.Operation))
                    {
                        continue;
                    }

                    effectRegistry.Get(node.Operation)
                        .Execute(this, context, node);
                }
            }

            return SandboxControlResult.Success(
                targetIds.ToArray());
        }

        private static bool HasSandboxDebuffNode(
            CompiledEffectNode[] nodes)
        {
            if (nodes == null)
            {
                return false;
            }

            for (int nodeIndex = 0;
                 nodeIndex < nodes.Length;
                 nodeIndex++)
            {
                if (IsSandboxDebuffOperation(
                        nodes[nodeIndex].Operation))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 적에게 StatusInstance를 부여하는 카드 연산만 TestLab 디버프 막대에 노출한다.
        /// 분열·즉시 밀치기·보상 변경 같은 비상태 효과가 섞이지 않도록 의미 단위로
        /// 분류하며, 실제 수치와 실행은 EffectRegistry와 카드 데이터가 계속 소유한다.
        /// </summary>
        private static bool IsSandboxDebuffOperation(
            EffectOperation operation)
        {
            switch (operation)
            {
                case EffectOperation.AddPierce:
                case EffectOperation.ApplyBurn:
                case EffectOperation.ApplySlow:
                case EffectOperation.ApplyMark:
                case EffectOperation.ApplyPoison:
                case EffectOperation.ApplyStun:
                case EffectOperation.ApplyEnemyRicochet:
                case EffectOperation.ApplyBleed:
                case EffectOperation.ApplyHomingPriority:
                case EffectOperation.ApplyDelay:
                case EffectOperation.ApplyCurse:
                case EffectOperation.ApplyBind:
                case EffectOperation.ApplyAirborne:
                case EffectOperation.ApplyShock:
                case EffectOperation.ApplyFreeze:
                case EffectOperation.ApplyAfterimage:
                case EffectOperation.ApplyEnemyPulse:
                case EffectOperation.ApplyEnemyMagnet:
                case EffectOperation.ApplyEnemyReflect:
                case EffectOperation.ApplyEnemyContagion:
                case EffectOperation.ApplySeal:
                case EffectOperation.ApplyCorrosion:
                case EffectOperation.ApplyEnemyOrbit:
                case EffectOperation.ApplyLifesteal:
                case EffectOperation.ApplyFear:
                    return true;
                default:
                    return false;
            }
        }

        internal SandboxControlResult SandboxSetGold(
            int amount)
        {
            SandboxControlResult modeCheck =
                RequireSandboxMode();
            if (!modeCheck.Succeeded)
            {
                return modeCheck;
            }
            if (amount < 0 ||
                amount > MaximumSandboxIntegerResource)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.InvalidValue,
                    "Gold is outside the safe range.");
            }

            gold = amount;
            return SandboxControlResult.Success();
        }

        internal SandboxControlResult SandboxSetBaseHealth(
            int amount)
        {
            SandboxControlResult modeCheck =
                RequireSandboxMode();
            if (!modeCheck.Succeeded)
            {
                return modeCheck;
            }
            if (amount <= 0 ||
                amount > MaximumSandboxIntegerResource)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.InvalidValue,
                    "Base health is outside the safe range.");
            }

            baseHealth = amount;
            return SandboxControlResult.Success();
        }

        internal SandboxControlResult SandboxGrantCards(
            string stableId,
            int count)
        {
            SandboxControlResult modeCheck =
                RequireSandboxMode();
            if (!modeCheck.Succeeded)
            {
                return modeCheck;
            }
            if (count <= 0 ||
                count >
                    SandboxSimulationLimits
                        .MaximumCardGrantCount ||
                cards.Count >
                    SandboxSimulationLimits
                        .MaximumOwnedCardCount -
                    count)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.CapacityExceeded,
                    "Card grant count exceeds the sandbox inventory limit.");
            }
            if (string.IsNullOrEmpty(stableId))
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.UnknownContent,
                    "Card id is required.");
            }
            if (!content.TryGetCardId(
                    stableId,
                    out CardId definitionId))
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.UnknownContent,
                    "Unknown card '" + stableId + "'.");
            }

            var instanceIds = new int[count];
            for (int index = 0; index < count; index++)
            {
                CardInstanceState card =
                    AddOwnedCard(definitionId);
                instanceIds[index] = card.InstanceId;
            }
            return SandboxControlResult.Success(instanceIds);
        }

        /// <summary>
        /// 현재 컴파일 콘텐츠에 등록된 모든 카드 정의를 직접 순회해 지급한다.
        /// TestLab UI가 별도의 카드 ID 목록을 유지하지 않으므로 새 카드가
        /// 카탈로그에 추가되면 선택 목록과 전체 지급이 같은 원본을 따른다.
        /// 용량과 ID 공간을 먼저 검증해 중간까지만 지급되는 상태도 막는다.
        /// </summary>
        internal SandboxControlResult SandboxGrantEveryCard(
            int countPerDefinition)
        {
            SandboxControlResult modeCheck =
                RequireSandboxMode();
            if (!modeCheck.Succeeded)
            {
                return modeCheck;
            }
            if (countPerDefinition <= 0 ||
                countPerDefinition >
                    SandboxSimulationLimits
                        .MaximumCardGrantCount)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.InvalidValue,
                    "Card count per definition is outside the safe range.");
            }

            long required =
                (long)content.CardCount *
                countPerDefinition;
            if (required >
                SandboxSimulationLimits
                    .MaximumOwnedCardCount -
                cards.Count)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.CapacityExceeded,
                    "All-card grant exceeds the sandbox inventory limit.");
            }
            if (required > int.MaxValue - nextCardInstanceId)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.IdentityExhausted,
                    "Card identity space is exhausted.");
            }

            var instanceIds = new int[(int)required];
            int resultIndex = 0;
            for (int cardIndex = 0;
                 cardIndex < content.CardCount;
                 cardIndex++)
            {
                CardId definitionId =
                    new CardId(cardIndex);
                for (int copyIndex = 0;
                     copyIndex < countPerDefinition;
                     copyIndex++)
                {
                    CardInstanceState card =
                        AddOwnedCard(definitionId);
                    instanceIds[resultIndex++] =
                        card.InstanceId;
                }
            }

            return SandboxControlResult.Success(instanceIds);
        }

        internal SandboxControlResult SandboxUnlockTower(
            string stableId)
        {
            SandboxControlResult modeCheck =
                RequireSandboxMode();
            if (!modeCheck.Succeeded)
            {
                return modeCheck;
            }
            if (string.IsNullOrEmpty(stableId))
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.UnknownContent,
                    "Tower id is required.");
            }
            if (!content.TryGetTowerId(
                    stableId,
                    out TowerDefinitionId definitionId))
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.UnknownContent,
                    "Unknown tower '" + stableId + "'.");
            }

            ownedTowerDefinitions.Add(definitionId.Value);
            return SandboxControlResult.Success(
                definitionId.Value);
        }

        internal SandboxControlResult SandboxPlaceTower(
            string stableId,
            int buildPointIndex)
        {
            SandboxControlResult modeCheck =
                RequireSandboxMode();
            if (!modeCheck.Succeeded)
            {
                return modeCheck;
            }
            if (string.IsNullOrEmpty(stableId))
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.UnknownContent,
                    "Tower id is required.");
            }
            if (!content.TryGetTowerId(
                    stableId,
                    out TowerDefinitionId definitionId))
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.UnknownContent,
                    "Unknown tower '" + stableId + "'.");
            }
            if (buildPointIndex < 0 ||
                buildPointIndex >=
                    run.BuildSpotsInternal.Length)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.InvalidTarget,
                    "Build point is out of range.");
            }
            for (int towerIndex = 0;
                 towerIndex < towers.Count;
                 towerIndex++)
            {
                if (towers[towerIndex].BuildPointIndex ==
                    buildPointIndex)
                {
                    return SandboxControlResult.Reject(
                        SandboxControlError.BuildPointOccupied,
                        "Build point is occupied.");
                }
            }
            if (nextTowerId == int.MaxValue)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.IdentityExhausted,
                    "Tower identity space is exhausted.");
            }

            ownedTowerDefinitions.Add(definitionId.Value);
            unlockedBuildSpots[buildPointIndex] = true;
            TowerState tower = CreateTowerInstance(
                definitionId,
                buildPointIndex);
            CompileTowerProgram(tower);
            return SandboxControlResult.Success(
                tower.Id.Value);
        }

        internal SandboxControlResult SandboxSetTowerLevel(
            int towerInstanceId,
            int level)
        {
            SandboxControlResult modeCheck =
                RequireSandboxMode();
            if (!modeCheck.Succeeded)
            {
                return modeCheck;
            }

            TowerState tower =
                FindTower(new TowerId(towerInstanceId));
            if (tower == null)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.InvalidTarget,
                    "Tower instance does not exist.");
            }
            CompiledTowerDefinition definition =
                content.GetTower(tower.DefinitionId);
            if (!definition.TryGetLevel(
                    level,
                    out CompiledTowerLevelBalance targetLevel))
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.InvalidValue,
                    "Tower level is outside authored data.");
            }

            tower.Level = level;
            int unlockedSlots = Math.Min(
                tower.CardInstanceIds.Length,
                targetLevel.UnlockedSlots);
            for (int cardIndex = 0;
                 cardIndex < cards.Count;
                 cardIndex++)
            {
                CardInstanceState card = cards[cardIndex];
                if (!card.Equipped ||
                    card.EquippedTowerId != tower.Id)
                {
                    continue;
                }

                CompiledCardDefinition cardDefinition =
                    content.GetCard(card.DefinitionId);
                if (card.EquippedSlot < 0 ||
                    card.EquippedSlot +
                        cardDefinition.SlotCost >
                    unlockedSlots)
                {
                    UnequipCard(
                        card.InstanceId,
                        bypassPhaseLock: true);
                }
            }

            while (ComputeTowerCost(tower, -1) >
                   targetLevel.ComputeCapacity)
            {
                CardInstanceState rightmost = null;
                for (int cardIndex = 0;
                     cardIndex < cards.Count;
                     cardIndex++)
                {
                    CardInstanceState candidate =
                        cards[cardIndex];
                    if (candidate.Equipped &&
                        candidate.EquippedTowerId ==
                            tower.Id &&
                        (rightmost == null ||
                         candidate.EquippedSlot >
                            rightmost.EquippedSlot))
                    {
                        rightmost = candidate;
                    }
                }

                if (rightmost == null)
                {
                    break;
                }
                UnequipCard(
                    rightmost.InstanceId,
                    bypassPhaseLock: true);
            }

            tower.CooldownRemaining = 0;
            tower.AttackWindupRemaining = 0;
            tower.PendingAttackTargetId =
                EntityId.Invalid;
            CompileTowerProgram(tower);
            AddPresentation(
                PresentationEventType.TowerUpgraded,
                tower.Id.Value,
                -1,
                tower.Level,
                definition.StableId);
            return SandboxControlResult.Success(
                tower.Id.Value);
        }

        internal SandboxControlResult SandboxEquipCard(
            int cardInstanceId,
            int towerInstanceId,
            int slotIndex)
        {
            SandboxControlResult modeCheck =
                RequireSandboxMode();
            if (!modeCheck.Succeeded)
            {
                return modeCheck;
            }

            CommandResult result = EquipCard(
                cardInstanceId,
                towerInstanceId,
                slotIndex,
                bypassPhaseLock: true);
            return ConvertSandboxCommandResult(
                result,
                cardInstanceId);
        }

        internal SandboxControlResult SandboxUnequipCard(
            int cardInstanceId)
        {
            SandboxControlResult modeCheck =
                RequireSandboxMode();
            if (!modeCheck.Succeeded)
            {
                return modeCheck;
            }

            CommandResult result = UnequipCard(
                cardInstanceId,
                bypassPhaseLock: true);
            return ConvertSandboxCommandResult(
                result,
                cardInstanceId);
        }

        /// <summary>
        /// 테스트 UI가 안정 카드 ID만으로 슬롯 하나를 교체한다.
        /// 겹치는 다중 슬롯 카드, 연산력, 보유 카드 추가를 복사본에서 모두
        /// 검증한 뒤 한 번에 커밋하므로 실패 시 인벤토리와 프로그램이 그대로다.
        /// </summary>
        internal SandboxControlResult SandboxReplaceCard(
            string cardStableId,
            int towerInstanceId,
            int slotIndex)
        {
            SandboxControlResult modeCheck =
                RequireSandboxMode();
            if (!modeCheck.Succeeded)
            {
                return modeCheck;
            }
            if (string.IsNullOrEmpty(cardStableId) ||
                !content.TryGetCardId(
                    cardStableId,
                    out CardId definitionId))
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.UnknownContent,
                    string.IsNullOrEmpty(cardStableId)
                        ? "Card id is required."
                        : "Unknown card '" +
                          cardStableId + "'.");
            }

            TowerState tower =
                FindTower(new TowerId(towerInstanceId));
            if (tower == null)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.InvalidTarget,
                    "Tower instance does not exist.");
            }

            CompiledCardDefinition definition =
                content.GetCard(definitionId);
            int unlockedSlots =
                GetTowerUnlockedSlotCount(tower);
            if (slotIndex < 0 ||
                slotIndex >= unlockedSlots ||
                definition.SlotCost >
                    unlockedSlots - slotIndex)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.InvalidTarget,
                    "Card does not fit in the requested slot.");
            }

            int replacementEnd =
                slotIndex + definition.SlotCost;
            var overlappingCards =
                new List<CardInstanceState>(2);
            for (int cardIndex = 0;
                 cardIndex < cards.Count;
                 cardIndex++)
            {
                CardInstanceState equipped =
                    cards[cardIndex];
                if (!equipped.Equipped ||
                    equipped.EquippedTowerId !=
                        tower.Id)
                {
                    continue;
                }

                CompiledCardDefinition equippedDefinition =
                    content.GetCard(
                        equipped.DefinitionId);
                int equippedStart =
                    equipped.EquippedSlot;
                int equippedEnd =
                    equippedStart +
                    equippedDefinition.SlotCost;
                if (equippedStart < replacementEnd &&
                    slotIndex < equippedEnd)
                {
                    overlappingCards.Add(equipped);
                }
            }

            CardInstanceState replacement = null;
            for (int cardIndex = 0;
                 cardIndex < cards.Count;
                 cardIndex++)
            {
                CardInstanceState candidate =
                    cards[cardIndex];
                if (!candidate.Equipped &&
                    candidate.DefinitionId ==
                        definitionId)
                {
                    replacement = candidate;
                    break;
                }
            }
            if (replacement == null)
            {
                for (int overlapIndex = 0;
                     overlapIndex <
                        overlappingCards.Count;
                     overlapIndex++)
                {
                    CardInstanceState candidate =
                        overlappingCards[
                            overlapIndex];
                    if (candidate.DefinitionId ==
                        definitionId)
                    {
                        replacement = candidate;
                        break;
                    }
                }
            }

            bool grantReplacement =
                replacement == null;
            if (grantReplacement &&
                cards.Count >=
                    SandboxSimulationLimits
                        .MaximumOwnedCardCount)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.CapacityExceeded,
                    "The sandbox card inventory limit is reached.");
            }
            if (grantReplacement &&
                nextCardInstanceId == int.MaxValue)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.IdentityExhausted,
                    "Card identity space is exhausted.");
            }

            int replacementInstanceId =
                grantReplacement
                    ? nextCardInstanceId
                    : replacement.InstanceId;
            int[] candidateSlots =
                (int[])tower.CardInstanceIds.Clone();
            for (int overlapIndex = 0;
                 overlapIndex <
                    overlappingCards.Count;
                 overlapIndex++)
            {
                ClearCardFromSlots(
                    candidateSlots,
                    overlappingCards[
                        overlapIndex].InstanceId);
            }
            if (!CanPlaceCardInSlots(
                    candidateSlots,
                    replacementInstanceId,
                    definition.SlotCost,
                    slotIndex))
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.LoadoutRejected,
                    "Requested slot could not be cleared atomically.");
            }
            PlaceCardInSlots(
                candidateSlots,
                replacementInstanceId,
                definition.SlotCost,
                slotIndex);

            long candidateCompute = 0;
            for (int candidateSlot = 0;
                 candidateSlot <
                    candidateSlots.Length;
                 candidateSlot++)
            {
                int instanceId =
                    candidateSlots[candidateSlot];
                if (instanceId < 0)
                {
                    continue;
                }

                if (instanceId ==
                    replacementInstanceId)
                {
                    candidateCompute +=
                        definition.ComputeCost;
                    continue;
                }

                CardInstanceState existing =
                    FindCardInstance(instanceId);
                if (existing == null)
                {
                    return SandboxControlResult.Reject(
                        SandboxControlError.LoadoutRejected,
                        "Tower loadout contains an invalid card instance.");
                }
                candidateCompute +=
                    content.GetCard(
                        existing.DefinitionId)
                        .ComputeCost;
            }
            if (candidateCompute >
                GetTowerLevelBalance(tower)
                    .ComputeCapacity)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.LoadoutRejected,
                    "Tower compute capacity would be exceeded.");
            }

            // 이 지점부터는 실패 가능한 검증이 없다. 새 카드가 필요해도
            // 슬롯·연산력·인벤토리를 모두 통과한 뒤에만 인스턴스를 만든다.
            if (grantReplacement)
            {
                replacement =
                    AddOwnedCard(definitionId);
                if (replacement.InstanceId !=
                    replacementInstanceId)
                {
                    throw new InvalidOperationException(
                        "Sandbox card identity changed during atomic replacement.");
                }
            }

            var affectedIds =
                new int[
                    1 + overlappingCards.Count];
            affectedIds[0] =
                replacement.InstanceId;
            int affectedCount = 1;
            for (int overlapIndex = 0;
                 overlapIndex <
                    overlappingCards.Count;
                 overlapIndex++)
            {
                CardInstanceState displaced =
                    overlappingCards[overlapIndex];
                if (displaced.InstanceId ==
                    replacement.InstanceId)
                {
                    continue;
                }

                displaced.Equipped = false;
                displaced.EquippedTowerId =
                    TowerId.Invalid;
                displaced.EquippedSlot = -1;
                affectedIds[affectedCount++] =
                    displaced.InstanceId;
            }

            tower.CardInstanceIds = candidateSlots;
            replacement.Equipped = true;
            replacement.EquippedTowerId = tower.Id;
            replacement.EquippedSlot = slotIndex;
            CompileTowerProgram(tower);

            if (affectedCount != affectedIds.Length)
            {
                Array.Resize(
                    ref affectedIds,
                    affectedCount);
            }
            return SandboxControlResult.Success(
                affectedIds);
        }

        private void ClearSandboxBattlefield()
        {
            enemies.Clear();
            lineages.Clear();
            projectiles.Clear();
            hazards.Clear();
            eventQueue.Clear();
            chainBudgets.Clear();
            sandboxCompletedLineageScratch.Clear();
            programFrames.Clear();
            freeProgramFrames.Clear();
            hazardContactsThisTick.Clear();
            activeShimmeringLineageId = -1;
            spatialIndex.Rebuild(enemies);
            ResetCommonCardRuntime();
            ResetUncommonCardState();
            ResetRareGenerationMotionState();
            ResetRareResonanceAbsorbTimeMutationState();
            ResetRareDeathChainState();
            ResetLegendaryState();
            ResetMythicCardState();

            for (int towerIndex = 0;
                 towerIndex < towers.Count;
                 towerIndex++)
            {
                towers[towerIndex].TargetsInside.Clear();
                towers[towerIndex]
                    .LastTargetTriggerTick.Clear();
                towers[towerIndex]
                    .PendingAttackTargetId =
                    EntityId.Invalid;
            }

            presentationEventHead = 0;
            presentationEventCount = 0;
        }

        /// <summary>
        /// 열린 TestLab 전투에서 이미 정산이 끝난 가계 원장을 제거한다.
        /// 정규 런은 승리 화면과 통계가 전체 가계 기록을 사용하므로 이 경로를
        /// 호출하지 않는다. 키 수집 버퍼는 매 틱 재사용해 무한 소환 중 할당도
        /// 함께 제한한다.
        /// </summary>
        private void CleanupCompletedSandboxLineages()
        {
            sandboxCompletedLineageScratch.Clear();
            foreach (KeyValuePair<int, LineageState> pair
                     in lineages)
            {
                if (pair.Value != null &&
                    pair.Value.LiveMembers == 0)
                {
                    sandboxCompletedLineageScratch.Add(
                        pair.Key);
                }
            }

            for (int index = 0;
                 index <
                    sandboxCompletedLineageScratch.Count;
                 index++)
            {
                lineages.Remove(
                    sandboxCompletedLineageScratch[index]);
            }
            sandboxCompletedLineageScratch.Clear();
        }

        private int CountActiveSandboxEnemies()
        {
            int activeEnemyCount = 0;
            for (int enemyIndex = 0;
                 enemyIndex < enemies.Count;
                 enemyIndex++)
            {
                if (enemies[enemyIndex].Alive)
                {
                    activeEnemyCount++;
                }
            }
            return activeEnemyCount;
        }

        /// <summary>
        /// 수동 및 모든 파생 적 생성이 공유하는 권위 상한 검사다.
        /// 정규 런에서는 즉시 통과하므로 TestLab 설정이 일반 전투에 영향을 주지 않는다.
        /// </summary>
        private bool HasSandboxEnemyCapacity(
            int requestedCount)
        {
            if (!sandboxTestingMode)
            {
                return true;
            }
            if (requestedCount <= 0)
            {
                return false;
            }

            int activeEnemyCount =
                CountActiveSandboxEnemies();
            return activeEnemyCount <=
                sandboxActiveEnemyLimit -
                requestedCount;
        }

        private bool TryPassSandboxEnemyCreationGate(
            int requestedCount,
            in GameEvent diagnosticEvent)
        {
            if (HasSandboxEnemyCapacity(
                    requestedCount))
            {
                return true;
            }

            AddDiagnostic(
                DiagnosticCode
                    .SandboxActiveEnemyLimitReached,
                diagnosticEvent,
                sandboxActiveEnemyLimit);
            return false;
        }

        private SandboxControlResult RequireSandboxMode()
        {
            if (!initialized)
            {
                return SandboxNotInitialized();
            }
            if (!sandboxTestingMode)
            {
                return SandboxControlResult.Reject(
                    SandboxControlError.NotInSandboxMode,
                    "EnterSandboxMode must be called first.");
            }
            return SandboxControlResult.Success();
        }

        private static SandboxControlResult
            SandboxNotInitialized()
        {
            return SandboxControlResult.Reject(
                SandboxControlError.NotInitialized,
                "GameSimulation.Initialize must be called first.");
        }

        private static SandboxControlResult
            ConvertSandboxCommandResult(
                in CommandResult result,
                int affectedId)
        {
            if (result.Accepted)
            {
                return SandboxControlResult.Success(
                    affectedId);
            }

            SandboxControlError error;
            switch (result.Error)
            {
                case CommandError.UnknownContent:
                    error =
                        SandboxControlError.UnknownContent;
                    break;
                case CommandError.InvalidTarget:
                case CommandError.SlotOutOfRange:
                    error =
                        SandboxControlError.InvalidTarget;
                    break;
                case CommandError.BuildPointOccupied:
                    error =
                        SandboxControlError.BuildPointOccupied;
                    break;
                default:
                    error =
                        SandboxControlError.LoadoutRejected;
                    break;
            }
            return SandboxControlResult.Reject(
                error,
                result.Message);
        }
    }
}
