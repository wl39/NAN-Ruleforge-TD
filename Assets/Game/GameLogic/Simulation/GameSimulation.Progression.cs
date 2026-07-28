using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.GameLogic.Simulation
{
    /// <summary>
    /// 처치 카드팩, 웨이브 보상 큐, 보스 고유 능력을 담당하는 진행 모듈이다.
    /// 모든 판정은 고정 틱과 정수 수치만 사용하며 표현 에셋을 참조하지 않는다.
    /// </summary>
    public sealed partial class GameSimulation
    {
        private bool victoryPending;

        private void AwardCardPackProgress(EnemyState enemy)
        {
            if (enemy == null)
            {
                return;
            }

            CompiledEnemyDefinition definition =
                content.GetEnemy(enemy.DefinitionId);
            if (definition.Rank == EnemyRank.Boss)
            {
                AwardBossCardPackIfEligible(enemy);
            }

            int amount = Math.Max(
                0,
                enemy.CardPackProgressBudget);
            if (amount == 0)
            {
                return;
            }

            enemy.CardPackProgressBudget = 0;
            cardPackProgress = checked(
                cardPackProgress + amount);
            if (lineages.TryGetValue(
                    enemy.LineageId.Value,
                    out LineageState lineage))
            {
                lineage.AwardedCardPackProgress = checked(
                    lineage.AwardedCardPackProgress + amount);
            }

            TrySpawnShimmeringCarrier(enemy.DefinitionId);
        }

        private void AwardBossCardPackIfEligible(
            EnemyState enemy)
        {
            if (bossCardPackAwardedThisWave ||
                !ContainsWaveIndex(
                    run.BossCardPackWaveIndicesInternal,
                    currentWaveIndex))
            {
                return;
            }
            if (lineages.TryGetValue(
                    enemy.LineageId.Value,
                    out LineageState lineage) &&
                lineage.LiveMembers != 1)
            {
                return;
            }

            bossCardPackAwardedThisWave = true;
            var pack = new CardPackState
            {
                Id = nextCardPackId++,
                Source = CardPackSource.Boss,
                Position = enemy.Position,
                WorldDrop = false
            };
            cardPacks.Add(pack);
            AddPresentation(
                PresentationEventType.CardPackDropped,
                pack.Id,
                enemy.Id.Value,
                (int)pack.Source,
                "boss");
        }

        private void TrySpawnShimmeringCarrier(
            EnemyDefinitionId preferredDefinition)
        {
            if (phase != RunPhase.Combat ||
                activeShimmeringLineageId >= 0 ||
                HasUnopenedShimmeringPack() ||
                nextCardPackThresholdIndex >=
                    run.CardPackProgressThresholdsInternal.Length ||
                cardPackProgress <
                    run.CardPackProgressThresholdsInternal[
                        nextCardPackThresholdIndex])
            {
                return;
            }

            EnemyDefinitionId definitionId =
                ResolveCarrierDefinition(preferredDefinition);
            if (!definitionId.IsValid)
            {
                return;
            }

            // 임계값은 출현 기회를 예약한 시점에 소비한다. 운반 몬스터가
            // 탈출하면 같은 임계값으로 재생성하지 않는다.
            nextCardPackThresholdIndex++;
            EnemyState carrier = SpawnEnemy(
                definitionId,
                EnemySpawnOrigin.ShimmeringCarrier);
            activeShimmeringLineageId =
                carrier.LineageId.Value;
            AddPresentation(
                PresentationEventType.ShimmeringCarrierSpawned,
                carrier.Id.Value,
                -1,
                nextCardPackThresholdIndex,
                content.GetEnemy(definitionId).StableId);
        }

        private EnemyDefinitionId ResolveCarrierDefinition(
            EnemyDefinitionId preferredDefinition)
        {
            if (preferredDefinition.IsValid &&
                content.GetEnemy(preferredDefinition).Rank ==
                    EnemyRank.Normal)
            {
                return preferredDefinition;
            }

            if (currentWaveIndex < 0 ||
                currentWaveIndex >= content.WaveCount)
            {
                return EnemyDefinitionId.Invalid;
            }

            CompiledWaveSpawn[] spawns =
                content.GetWave(currentWaveIndex).SpawnsInternal;
            for (int i = 0; i < spawns.Length; i++)
            {
                CompiledEnemyDefinition candidate =
                    content.GetEnemy(spawns[i].EnemyId);
                if (candidate.Rank == EnemyRank.Normal)
                {
                    return candidate.Id;
                }
            }

            return EnemyDefinitionId.Invalid;
        }

        private bool HasUnopenedShimmeringPack()
        {
            for (int i = 0; i < cardPacks.Count; i++)
            {
                if (cardPacks[i].Source ==
                    CardPackSource.ShimmeringCarrier)
                {
                    return true;
                }
            }
            return false;
        }

        private void ResolveCompletedLineage(
            LineageState lineage)
        {
            if (!lineage.IsShimmering)
            {
                return;
            }

            activeShimmeringLineageId = -1;
            if (lineage.ShimmeringFailed)
            {
                AddPresentation(
                    PresentationEventType.CardPackLost,
                    lineage.Id.Value,
                    -1,
                    0,
                    "shimmering");
                return;
            }

            var pack = new CardPackState
            {
                Id = nextCardPackId++,
                Source =
                    CardPackSource.ShimmeringCarrier,
                Position = lineage.LastResolvedPosition,
                WorldDrop = true
            };
            cardPacks.Add(pack);
            AddPresentation(
                PresentationEventType.CardPackDropped,
                pack.Id,
                lineage.Id.Value,
                (int)pack.Source,
                "shimmering");
        }

        private CommandResult OpenCardPack(int cardPackId)
        {
            if (phase != RunPhase.Combat)
            {
                return CommandResult.Reject(
                    CommandError.InvalidPhase,
                    "A world card pack can only be opened during combat.");
            }

            CardPackState pack = FindCardPack(cardPackId);
            if (pack == null || !pack.WorldDrop)
            {
                return CommandResult.Reject(
                    CommandError.InvalidTarget,
                    "The requested world card pack does not exist.");
            }

            BeginCardPackChoice(pack, RunPhase.Combat);
            return CommandResult.Success();
        }

        private CommandResult SelectCardPack(int offerIndex)
        {
            if (phase != RunPhase.CardPackChoice)
            {
                return CommandResult.Reject(
                    CommandError.InvalidPhase,
                    "There is no active card-pack choice.");
            }
            if (offerIndex < 0 ||
                offerIndex >= cardPackOffers.Count)
            {
                return CommandResult.Reject(
                    CommandError.InvalidCardPackChoice,
                    "Card-pack offer is out of range.");
            }

            CardInstanceState card =
                AddOwnedCard(cardPackOffers[offerIndex]);
            pendingCardInstanceId = card.InstanceId;
            phase = RunPhase.CardPackLoadout;
            AddPresentation(
                PresentationEventType.CardPackOpened,
                activeCardPackId,
                card.InstanceId,
                card.DefinitionId.Value,
                content.GetCard(card.DefinitionId).StableId);
            return CommandResult.Success();
        }

        private CommandResult ResumeCardPackCombat()
        {
            if (phase != RunPhase.CardPackLoadout)
            {
                return CommandResult.Reject(
                    CommandError.InvalidPhase,
                    "There is no card-pack loadout to complete.");
            }

            CardInstanceState pending =
                FindCardInstance(pendingCardInstanceId);
            if (phaseAfterCardPack == RunPhase.Combat &&
                (pending == null || !pending.Equipped))
            {
                return CommandResult.Reject(
                    CommandError.CardPackRequiresEquippedCard,
                    "The newly selected card must be equipped before combat resumes.");
            }

            RemoveCardPack(activeCardPackId);
            activeCardPackId = -1;
            pendingCardInstanceId = -1;
            cardPackOffers.Clear();

            if (phaseAfterCardPack == RunPhase.Combat)
            {
                for (int i = 0; i < towers.Count; i++)
                {
                    CompileTowerProgram(towers[i]);
                }
                phase = RunPhase.Combat;
            }
            else
            {
                BeginNextWaveReward();
            }

            return CommandResult.Success();
        }

        private void BeginCardPackChoice(
            CardPackState pack,
            RunPhase returnPhase)
        {
            activeCardPackId = pack.Id;
            phaseAfterCardPack = returnPhase;
            pendingCardInstanceId = -1;
            GenerateCardPackOffers();
            phase = RunPhase.CardPackChoice;
        }

        private void GenerateCardPackOffers()
        {
            cardPackOffers.Clear();
            AddCardPackOffer(PickCardPackCompatible());
            AddCardPackOffer(PickCardPackSynergy());
            AddCardPackOffer(PickCardPackTierTwoPlus());

            while (cardPackOffers.Count <
                       run.DraftOfferCount &&
                   cardPackOffers.Count < content.CardCount)
            {
                AddCardPackOffer(
                    PickCardPackFallback());
            }
        }

        private CardId PickCardPackCompatible()
        {
            var unowned = new List<CardId>();
            var owned = new List<CardId>();
            CollectCardPackCandidates(
                unowned,
                owned,
                requireTierTwo: false,
                synergyOnly: false);
            return PickPreferred(unowned, owned);
        }

        private CardId PickCardPackSynergy()
        {
            int bestUnowned = int.MinValue;
            int bestOwned = int.MinValue;
            var unowned = new List<CardId>();
            var owned = new List<CardId>();
            for (int i = 0; i < content.CardCount; i++)
            {
                CardId id = new CardId(i);
                if (IsCardPackOffered(id) ||
                    !CanEverFitAnyTower(
                        content.GetCard(id)))
                {
                    continue;
                }

                int score = CountTagSynergy(
                    content.GetCard(id));
                bool alreadyOwned =
                    IsOwnedCardDefinition(id);
                int best = alreadyOwned
                    ? bestOwned
                    : bestUnowned;
                List<CardId> list = alreadyOwned
                    ? owned
                    : unowned;
                if (score > best)
                {
                    list.Clear();
                    list.Add(id);
                    if (alreadyOwned)
                    {
                        bestOwned = score;
                    }
                    else
                    {
                        bestUnowned = score;
                    }
                }
                else if (score == best)
                {
                    list.Add(id);
                }
            }
            return PickPreferred(unowned, owned);
        }

        private CardId PickCardPackTierTwoPlus()
        {
            var unowned = new List<CardId>();
            var owned = new List<CardId>();
            CollectCardPackCandidates(
                unowned,
                owned,
                requireTierTwo: true,
                synergyOnly: false);
            CardId result = PickWeightedPreferred(
                unowned,
                owned);
            return result.IsValid
                ? result
                : PickCardPackFallback();
        }

        private CardId PickCardPackFallback()
        {
            var unowned = new List<CardId>();
            var owned = new List<CardId>();
            CollectCardPackCandidates(
                unowned,
                owned,
                requireTierTwo: false,
                synergyOnly: false);
            return PickWeightedPreferred(
                unowned,
                owned);
        }

        private void CollectCardPackCandidates(
            List<CardId> unowned,
            List<CardId> owned,
            bool requireTierTwo,
            bool synergyOnly)
        {
            for (int i = 0; i < content.CardCount; i++)
            {
                CardId id = new CardId(i);
                CompiledCardDefinition definition =
                    content.GetCard(id);
                if (IsCardPackOffered(id) ||
                    (requireTierTwo &&
                     definition.Tier < CardTier.Uncommon) ||
                    !CanEverFitAnyTower(definition))
                {
                    continue;
                }

                if (IsOwnedCardDefinition(id))
                {
                    owned.Add(id);
                }
                else
                {
                    unowned.Add(id);
                }
            }
        }

        private CardId PickPreferred(
            List<CardId> unowned,
            List<CardId> owned)
        {
            return unowned.Count > 0
                ? PickFromCandidates(unowned)
                : PickFromCandidates(owned);
        }

        private CardId PickWeightedPreferred(
            List<CardId> unowned,
            List<CardId> owned)
        {
            List<CardId> candidates =
                unowned.Count > 0 ? unowned : owned;
            if (candidates.Count == 0)
            {
                return CardId.Invalid;
            }

            int total = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                int tierIndex =
                    (int)content.GetCard(
                        candidates[i]).Tier - 1;
                total += Math.Max(
                    0,
                    run.TierWeightsInternal[tierIndex]);
            }
            if (total <= 0)
            {
                return PickFromCandidates(candidates);
            }

            int roll = draftRandom.NextInt(total);
            int cumulative = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                int tierIndex =
                    (int)content.GetCard(
                        candidates[i]).Tier - 1;
                cumulative += Math.Max(
                    0,
                    run.TierWeightsInternal[tierIndex]);
                if (roll < cumulative)
                {
                    return candidates[i];
                }
            }
            return candidates[candidates.Count - 1];
        }

        private bool CanEverFitAnyTower(
            CompiledCardDefinition card)
        {
            for (int i = 0; i < towers.Count; i++)
            {
                CompiledTowerDefinition tower =
                    content.GetTower(towers[i].DefinitionId);
                CompiledTowerLevelBalance maximumLevel =
                    tower.GetLevel(7);
                if (card.SlotCost <= tower.SlotCount &&
                    card.ComputeCost <=
                        (maximumLevel == null
                            ? tower.ComputeCapacity
                            : maximumLevel.ComputeCapacity))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsOwnedCardDefinition(CardId id)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].DefinitionId == id)
                {
                    return true;
                }
            }
            return false;
        }

        private void AddCardPackOffer(CardId id)
        {
            if (id.IsValid && !IsCardPackOffered(id))
            {
                cardPackOffers.Add(id);
            }
        }

        private bool IsCardPackOffered(CardId id)
        {
            for (int i = 0; i < cardPackOffers.Count; i++)
            {
                if (cardPackOffers[i] == id)
                {
                    return true;
                }
            }
            return false;
        }

        private CardPackState FindCardPack(int id)
        {
            for (int i = 0; i < cardPacks.Count; i++)
            {
                if (cardPacks[i].Id == id)
                {
                    return cardPacks[i];
                }
            }
            return null;
        }

        private void RemoveCardPack(int id)
        {
            for (int i = 0; i < cardPacks.Count; i++)
            {
                if (cardPacks[i].Id == id)
                {
                    cardPacks.RemoveAt(i);
                    return;
                }
            }
        }

        private void BeginWaveEndRewards(bool finalWave)
        {
            waveRewardsPending = true;
            victoryPending = finalWave;
            regularDraftPending =
                !finalWave &&
                ContainsWaveIndex(
                    run.RegularDraftWaveIndicesInternal,
                    currentWaveIndex);
            BeginNextWaveReward();
        }

        private void BeginNextWaveReward()
        {
            if (waveRewardsPending)
            {
                CardPackState nextPack =
                    FindNextWaveRewardPack();
                if (nextPack != null)
                {
                    BeginCardPackChoice(
                        nextPack,
                        RunPhase.Planning);
                    return;
                }

                if (regularDraftPending)
                {
                    regularDraftPending = false;
                    GenerateDraft();
                    phase = RunPhase.Draft;
                    waveRewardsPending = false;
                    return;
                }

                waveRewardsPending = false;
                if (victoryPending)
                {
                    victoryPending = false;
                    phase = RunPhase.Victory;
                    AddPresentation(
                        PresentationEventType.RunWon,
                        currentWaveIndex);
                    return;
                }
            }

            phase = RunPhase.Planning;
        }

        private CardPackState FindNextWaveRewardPack()
        {
            // 운반 몬스터팩이 항상 보스팩보다 먼저 처리된다.
            for (int i = 0; i < cardPacks.Count; i++)
            {
                if (cardPacks[i].Source ==
                    CardPackSource.ShimmeringCarrier)
                {
                    return cardPacks[i];
                }
            }
            for (int i = 0; i < cardPacks.Count; i++)
            {
                if (cardPacks[i].Source ==
                    CardPackSource.Boss)
                {
                    return cardPacks[i];
                }
            }
            return null;
        }

        private static bool ContainsWaveIndex(
            int[] sortedIndices,
            int waveIndex)
        {
            return Array.BinarySearch(
                sortedIndices,
                waveIndex) >= 0;
        }

        private void ProcessBossAbilities()
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState boss = enemies[i];
                if (!boss.Alive ||
                    boss.DeathQueued ||
                    boss.SpawnOrigin ==
                        EnemySpawnOrigin.BossSummon)
                {
                    continue;
                }

                CompiledEnemyDefinition definition =
                    content.GetEnemy(boss.DefinitionId);
                if (definition.Rank != EnemyRank.Boss ||
                    definition.BossAbility ==
                        BossAbilityType.None)
                {
                    continue;
                }

                bool enraged =
                    boss.HealthMilli * 10_000L <=
                    boss.MaxHealthMilli *
                    definition.BossPhaseHealthBps;
                if (enraged && !boss.BossPhaseAnnounced)
                {
                    boss.BossEnraged = true;
                    boss.BossPhaseAnnounced = true;
                    boss.BossAbilityCooldownTicks = Math.Min(
                        boss.BossAbilityCooldownTicks,
                        definition
                            .BossEnragedAbilityIntervalTicks);
                    AddPresentation(
                        PresentationEventType.BossPhaseChanged,
                        boss.Id.Value,
                        -1,
                        2,
                        definition.StableId);
                }

                bool interrupted =
                    HasActiveStatus(boss, StatusType.Stun);
                if (interrupted)
                {
                    if (boss.BossCastRemainingTicks > 0)
                    {
                        boss.BossCastRemainingTicks = 0;
                        boss.BossAbilityCooldownTicks =
                            GetBossAbilityInterval(
                                boss,
                                definition);
                    }
                    continue;
                }

                // 지연은 캐스트와 쿨다운 진행을 잠시 멈추고, 봉인은 특수 능력
                // 전체를 차단한다. 기절과 달리 진행 값을 취소하지 않아 상태가
                // 끝난 뒤 남은 시간부터 결정적으로 재개한다.
                if (IsEnemyDelayed(boss) ||
                    IsEnemySpecialAbilitySealed(boss))
                {
                    continue;
                }

                if (boss.BossCastRemainingTicks > 0)
                {
                    boss.BossCastRemainingTicks--;
                    if (boss.BossCastRemainingTicks == 0)
                    {
                        ActivateBossTeleport(
                            boss,
                            definition);
                    }
                    continue;
                }

                if (boss.BossAbilityCooldownTicks > 0)
                {
                    boss.BossAbilityCooldownTicks--;
                    continue;
                }

                switch (definition.BossAbility)
                {
                    case BossAbilityType.Shield:
                        boss.ShieldMilli =
                            DeterministicMath.MultiplyBasisPoints(
                                boss.MaxHealthMilli,
                                definition.BossShieldBps);
                        AnnounceBossAbility(
                            boss,
                            definition,
                            boss.ShieldMilli);
                        break;
                    case BossAbilityType.Summon:
                        ActivateBossSummon(
                            boss,
                            definition);
                        break;
                    case BossAbilityType.Teleport:
                        boss.BossCastRemainingTicks =
                            definition.BossCastTicks;
                        AddPresentation(
                            PresentationEventType.BossAbilityTelegraphed,
                            boss.Id.Value,
                            -1,
                            definition.BossCastTicks,
                            definition.StableId);
                        break;
                }

                boss.BossAbilityCooldownTicks =
                    GetBossAbilityInterval(
                        boss,
                        definition);
            }
        }

        private int GetBossAbilityInterval(
            EnemyState boss,
            CompiledEnemyDefinition definition)
        {
            return boss.BossEnraged
                ? definition.BossEnragedAbilityIntervalTicks
                : definition.BossAbilityIntervalTicks;
        }

        private void ActivateBossSummon(
            EnemyState boss,
            CompiledEnemyDefinition definition)
        {
            int activeSummons = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyState enemy = enemies[i];
                if (enemy.Alive &&
                    enemy.SpawnOrigin ==
                        EnemySpawnOrigin.BossSummon &&
                    enemy.SummonerId == boss.Id)
                {
                    activeSummons++;
                }
            }

            int requested = boss.BossEnraged
                ? definition.BossEnragedSummonCount
                : definition.BossSummonCount;
            int allowed = Math.Min(
                requested,
                Math.Max(
                    0,
                    definition.BossMaxActiveSummons -
                    activeSummons));
            for (int i = 0; i < allowed; i++)
            {
                SpawnEnemy(
                    definition.BossSummonEnemyId,
                    EnemySpawnOrigin.BossSummon,
                    boss.Id.Value,
                    definition.BossSummonHealthBps);
            }
            AnnounceBossAbility(
                boss,
                definition,
                allowed);
        }

        private void ActivateBossTeleport(
            EnemyState boss,
            CompiledEnemyDefinition definition)
        {
            int distanceBps = boss.BossEnraged
                ? definition.BossEnragedTeleportDistanceBps
                : definition.BossTeleportDistanceBps;
            long distance =
                DeterministicMath.MultiplyBasisPoints(
                    path.TotalLengthMilli,
                    distanceBps);
            boss.PathProgressMilli = Math.Min(
                path.TotalLengthMilli,
                boss.PathProgressMilli + distance);
            RefreshEnemyPosition(boss);
            AnnounceBossAbility(
                boss,
                definition,
                distanceBps);
            if (boss.PathProgressMilli >=
                path.TotalLengthMilli)
            {
                LeakEnemy(boss);
            }
        }

        private void AnnounceBossAbility(
            EnemyState boss,
            CompiledEnemyDefinition definition,
            long value)
        {
            AddPresentation(
                PresentationEventType.BossAbilityActivated,
                boss.Id.Value,
                -1,
                (int)Math.Min(int.MaxValue, value),
                definition.StableId);
        }
    }
}
