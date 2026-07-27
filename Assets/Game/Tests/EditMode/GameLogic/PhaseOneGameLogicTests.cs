using System;
using System.Collections.Generic;
using NUnit.Framework;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;
using UnityEditor;
using UnityEngine;

namespace RuleforgeTD.Tests.EditMode.GameLogic
{
    public sealed class PhaseOneGameLogicTests
    {
        private const string ContentAssetPath =
            "Assets/Game/Data/Logic/phase1-content.json";

        private CompiledContent content;

        [SetUp]
        public void SetUp()
        {
            content = LoadPhaseOneContent();
        }

        [Test]
        public void PhaseOneContent_CompilesAllCardsTowersAndWaves()
        {
            Assert.That(content.Cards, Has.Length.EqualTo(32));
            Assert.That(content.Towers, Has.Length.EqualTo(3));
            Assert.That(content.Waves, Has.Length.EqualTo(9));

            var cardStableIds = new HashSet<string>(StringComparer.Ordinal);
            for (int cardIndex = 0; cardIndex < content.Cards.Length; cardIndex++)
            {
                CompiledCardDefinition card = content.Cards[cardIndex];
                Assert.That(card, Is.Not.Null, "Card {0} was not compiled.", cardIndex);
                Assert.That(card.Id.Value, Is.EqualTo(cardIndex));
                Assert.That(cardStableIds.Add(card.StableId), Is.True,
                    "Duplicate compiled card id '{0}'.", card.StableId);
                Assert.That(card.Tags, Is.Not.Null.And.Not.Empty,
                    "Card '{0}' has no tags.", card.StableId);
                Assert.That(card.ComputeCost, Is.GreaterThan(0),
                    "Card '{0}' has no compute cost.", card.StableId);
                Assert.That(card.SlotCost, Is.GreaterThan(0),
                    "Card '{0}' has no slot cost.", card.StableId);

                AssertInterpretationIsExecutable(
                    card.StableId,
                    "projectile",
                    card.ProjectileEffects);
                AssertInterpretationIsExecutable(
                    card.StableId,
                    "enemy",
                    card.EnemyEffects);

                Assert.That(
                    content.TryGetCardId(card.StableId, out CardId resolvedCardId),
                    Is.True);
                Assert.That(resolvedCardId, Is.EqualTo(card.Id));
            }

            AssertTower(
                "ballista",
                TowerTrigger.Attack,
                SubjectTypeMode.Projectile,
                SubjectSelector.PrimaryProjectile,
                18);
            AssertTower(
                "mutation_obelisk",
                TowerTrigger.EnemyEnteredRange,
                SubjectTypeMode.Enemy,
                SubjectSelector.EnteringEnemy,
                0);
            AssertTower(
                "death_engine",
                TowerTrigger.EnemyDied,
                SubjectTypeMode.Enemy,
                SubjectSelector.EnemiesNearEvent,
                0);

            for (int waveIndex = 0; waveIndex < content.Waves.Length; waveIndex++)
            {
                CompiledWaveDefinition wave = content.Waves[waveIndex];
                Assert.That(wave.StableId, Is.EqualTo("wave_" + (waveIndex + 1)));
                Assert.That(wave.Spawns, Is.Not.Null.And.Not.Empty,
                    "Wave '{0}' has no spawns.", wave.StableId);
                for (int spawnIndex = 0; spawnIndex < wave.Spawns.Length; spawnIndex++)
                {
                    CompiledWaveSpawn spawn = wave.Spawns[spawnIndex];
                    Assert.That(spawn.Count, Is.GreaterThan(0));
                    Assert.That(spawn.IntervalTicks, Is.GreaterThan(0));
                    Assert.That(spawn.EnemyId.IsValid, Is.True);
                }
            }

            Assert.That(content.Run.TickRate, Is.EqualTo(30));
            Assert.That(content.Run.StartingTowerChoices, Has.Length.EqualTo(2));
            Assert.That(content.Run.InitiallyUnlockedTowers, Has.Length.EqualTo(1));
            Assert.That(content.Run.BuildSpots, Has.Length.EqualTo(8));
            CollectionAssert.AreEqual(
                new[] { 0, 0, 0, 0, 0, 0, 0, 0 },
                content.Run.BuildSpotUnlockCosts);
            Assert.That(content.Run.TowerConstructionCost, Is.EqualTo(100));
            CollectionAssert.AreEqual(
                new[] { 1, 4, 7 },
                content.Run.RegularDraftWaveIndices);
            CollectionAssert.AreEqual(
                new[] { 2, 5 },
                content.Run.BossCardPackWaveIndices);
            CollectionAssert.AreEqual(
                new[] { 150000, 500000, 1050000, 1800000 },
                content.Run.CardPackProgressThresholds);
            Assert.That(content.Safety.MaxEventsPerTick, Is.EqualTo(4096));
            Assert.That(content.Safety.MaxActiveHazards, Is.EqualTo(2048));
            Assert.That(
                content.Safety.MaxEnemiesPerLineage,
                Is.EqualTo(256));
        }

        [Test]
        public void DebugGoldCommand_GrantsGoldAndRejectsUnsafeAmounts()
        {
            var simulation = new GameSimulation();
            simulation.Initialize(content, 0xC0DEUL);
            int initialGold =
                simulation.GetSnapshot().Gold;
            ulong initialHash =
                simulation.ComputeStateHash();

            AssertAccepted(
                simulation.Submit(
                    GameCommand.GrantDebugGold(1000)),
                "grant Konami gold");
            Assert.That(
                simulation.GetSnapshot().Gold,
                Is.EqualTo(initialGold + 1000));
            Assert.That(
                simulation.ComputeStateHash(),
                Is.Not.EqualTo(initialHash));

            int goldAfterGrant =
                simulation.GetSnapshot().Gold;
            CommandResult invalid = simulation.Submit(
                GameCommand.GrantDebugGold(-1));
            Assert.That(invalid.Accepted, Is.False);
            Assert.That(
                invalid.Error,
                Is.EqualTo(CommandError.InvalidTarget));
            Assert.That(
                simulation.GetSnapshot().Gold,
                Is.EqualTo(goldAfterGrant));
        }

        [Test]
        public void BallistaWindup_StartsThenReleasesAfterEighteenTicks()
        {
            CompiledContent windupContent =
                CompileCustomized(source =>
                {
                    ConfigureSingleWave(source, "raider", 1, 1);
                    source.run.startingTowerChoices =
                        new[] { "ballista" };
                    source.run.initiallyUnlockedTowers =
                        Array.Empty<string>();
                    source.run.pathPointXMilli =
                        new[] { 0, 100000 };
                    source.run.pathPointYMilli =
                        new[] { 0, 0 };
                    source.run.buildSpotXMilli[0] = -5000;
                    source.run.buildSpotYMilli[0] = 0;

                    TowerDefinitionDto ballista =
                        FindTowerDto(source, "ballista");
                    ballista.rangeMilli = 30000;
                    ballista.cooldownTicks = 30;
                    ballista.attackWindupTicks = 18;
                    ballista.baseDamageMilli = 1;
                    ballista.projectileSpeedMilliPerTick = 10;
                    ballista.projectileLifetimeTicks = 1000;

                    EnemyDefinitionDto raider =
                        FindEnemyDto(source, "raider");
                    raider.maxHealthMilli = 1000000;
                    raider.speedMilliPerTick = 1;
                });
            GameSimulation simulation =
                CreateCombatSimulation(
                    windupContent,
                    "ballista",
                    Array.Empty<string>(),
                    0xA771CUL);
            simulation.ReadPresentationEvents();

            var startedTicks = new List<long>();
            var projectileTicks = new List<long>();
            for (int step = 0; step < 55; step++)
            {
                simulation.Step();
                SimulationEventBuffer events =
                    simulation.ReadPresentationEvents();
                for (int eventIndex = 0;
                     eventIndex < events.Count;
                     eventIndex++)
                {
                    SimulationPresentationEvent item =
                        events[eventIndex];
                    if (item.Type ==
                        PresentationEventType.TowerAttackStarted)
                    {
                        startedTicks.Add(item.Tick);
                        Assert.That(item.SourceId, Is.Zero);
                        Assert.That(item.SubjectId, Is.GreaterThanOrEqualTo(0));
                        Assert.That(item.Value, Is.EqualTo(18));
                        Assert.That(item.ContentId, Is.EqualTo("ballista"));
                    }
                    else if (
                        item.Type ==
                        PresentationEventType.ProjectileSpawned)
                    {
                        projectileTicks.Add(item.Tick);
                    }
                }
            }

            CollectionAssert.AreEqual(
                new long[] { 0, 31 },
                startedTicks);
            CollectionAssert.AreEqual(
                new long[] { 18, 49 },
                projectileTicks);
            Assert.That(
                projectileTicks[0] - startedTicks[0],
                Is.EqualTo(18));
            Assert.That(
                projectileTicks[1] - projectileTicks[0],
                Is.EqualTo(31),
                "The windup must not lengthen the authored cooldown cadence.");
        }

        [Test]
        public void ZeroWindup_FiresOnTheStartEventTick()
        {
            CompiledContent immediateContent =
                CompileCustomized(source =>
                {
                    ConfigureSingleWave(source, "raider", 1, 1);
                    source.run.startingTowerChoices =
                        new[] { "ballista" };
                    source.run.initiallyUnlockedTowers =
                        Array.Empty<string>();
                    FindTowerDto(source, "ballista")
                        .attackWindupTicks = 0;
                });
            GameSimulation simulation =
                CreateCombatSimulation(
                    immediateContent,
                    "ballista",
                    Array.Empty<string>(),
                    0xA771DUL);
            simulation.ReadPresentationEvents();

            simulation.Step();
            SimulationEventBuffer events =
                simulation.ReadPresentationEvents();
            int startIndex = -1;
            int projectileIndex = -1;
            long startTick = -1;
            long projectileTick = -1;
            for (int eventIndex = 0;
                 eventIndex < events.Count;
                 eventIndex++)
            {
                if (events[eventIndex].Type ==
                    PresentationEventType.TowerAttackStarted)
                {
                    startIndex = eventIndex;
                    startTick = events[eventIndex].Tick;
                    Assert.That(events[eventIndex].Value, Is.Zero);
                }
                else if (
                    events[eventIndex].Type ==
                    PresentationEventType.ProjectileSpawned)
                {
                    projectileIndex = eventIndex;
                    projectileTick = events[eventIndex].Tick;
                }
            }

            Assert.That(startIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(projectileIndex, Is.GreaterThan(startIndex));
            Assert.That(projectileTick, Is.EqualTo(startTick));
        }

        [Test]
        public void LevelSixArchers_FireTogetherAtThreeDistinctEnemies()
        {
            CompiledContent volleyContent =
                CompileCustomized(source =>
                {
                    ConfigureSingleWave(
                        source,
                        "raider",
                        3,
                        1);
                    source.run.startingTowerChoices =
                        new[] { "ballista" };
                    source.run.initiallyUnlockedTowers =
                        Array.Empty<string>();
                    source.run.pathPointXMilli =
                        new[] { 0, 100000 };
                    source.run.pathPointYMilli =
                        new[] { 0, 0 };
                    source.run.buildSpotXMilli[0] = -5000;
                    source.run.buildSpotYMilli[0] = 0;

                    TowerDefinitionDto ballista =
                        FindTowerDto(source, "ballista");
                    ballista.rangeMilli = 30000;
                    ballista.cooldownTicks = 100;
                    ballista.attackWindupTicks = 18;
                    ballista.baseDamageMilli = 1;
                    ballista.projectileSpeedMilliPerTick = 10;
                    ballista.projectileLifetimeTicks = 1000;

                    EnemyDefinitionDto raider =
                        FindEnemyDto(source, "raider");
                    raider.maxHealthMilli = 1000000;
                    raider.speedMilliPerTick = 1;
                });

            var simulation = new GameSimulation();
            simulation.Initialize(volleyContent, 0xA771FUL);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower("ballista")),
                "choose starting tower");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower("ballista", 0)),
                "place starting tower");

            int towerId =
                simulation.GetSnapshot().Towers[0].Id;
            for (int level = 2; level <= 6; level++)
            {
                AssertAccepted(
                    simulation.Submit(
                        GameCommand.UpgradeTower(towerId)),
                    "upgrade tower to level " + level);
            }

            AssertAccepted(
                simulation.Submit(GameCommand.StartWave()),
                "start multi-archer wave");

            ProjectileSnapshot[] projectiles =
                Array.Empty<ProjectileSnapshot>();
            for (int step = 0;
                 step < 30 &&
                 projectiles.Length == 0;
                 step++)
            {
                simulation.Step();
                projectiles =
                    simulation.GetSnapshot().Projectiles;
            }

            Assert.That(
                projectiles,
                Has.Length.EqualTo(3),
                "All three archers should release in the same volley.");
            var distinctTargetIds = new HashSet<int>();
            for (int projectileIndex = 0;
                 projectileIndex < projectiles.Length;
                 projectileIndex++)
            {
                distinctTargetIds.Add(
                    projectiles[projectileIndex].TargetId);
            }

            Assert.That(
                distinctTargetIds.Count,
                Is.EqualTo(3),
                "Archers must not duplicate a target while three enemies are available.");
        }

        [Test]
        public void AttackWindup_IsValidatedHashedAndStoredInState()
        {
            ContentCatalogDto negativeSource =
                LoadPhaseOneDto();
            FindTowerDto(negativeSource, "ballista")
                .attackWindupTicks = -1;
            Assert.Throws<ContentValidationException>(
                () => Compile(negativeSource));

            ContentCatalogDto changedSource =
                LoadPhaseOneDto();
            FindTowerDto(changedSource, "ballista")
                .attackWindupTicks = 17;
            CompiledContent changed =
                Compile(changedSource);
            Assert.That(
                changed.ContentHash,
                Is.Not.EqualTo(content.ContentHash));

            GameSimulation simulation =
                CreateCombatSimulation(
                    "ballista",
                    Array.Empty<string>(),
                    0xA771EUL);
            simulation.Step();
            TowerState tower =
                FindTowerState(simulation, 0);
            Assert.That(tower.AttackWindupRemaining, Is.EqualTo(18));
            Assert.That(tower.PendingAttackTargetId.IsValid, Is.True);

            ulong baselineHash =
                simulation.ComputeStateHash();
            tower.AttackWindupRemaining--;
            Assert.That(
                simulation.ComputeStateHash(),
                Is.Not.EqualTo(baselineHash));

            tower.AttackWindupRemaining++;
            tower.PendingAttackTargetId =
                EntityId.Invalid;
            Assert.That(
                simulation.ComputeStateHash(),
                Is.Not.EqualTo(baselineHash));
        }

        [Test]
        public void NineWaveEconomyAndBossContent_MatchesTheBalancePlan()
        {
            int totalGold = 0;
            int goldThroughWaveTwo = 0;
            int weightedKillProgress = 0;
            for (int waveIndex = 0;
                 waveIndex < content.WaveCount;
                 waveIndex++)
            {
                CompiledWaveSpawn[] spawns =
                    content.GetWave(waveIndex).Spawns;
                for (int spawnIndex = 0;
                     spawnIndex < spawns.Length;
                     spawnIndex++)
                {
                    CompiledWaveSpawn spawn =
                        spawns[spawnIndex];
                    CompiledEnemyDefinition enemy =
                        content.GetEnemy(spawn.EnemyId);
                    int gold =
                        enemy.RewardBudget * spawn.Count;
                    totalGold += gold;
                    if (waveIndex <= 1)
                    {
                        goldThroughWaveTwo += gold;
                    }

                    if (enemy.Rank == EnemyRank.Normal)
                    {
                        weightedKillProgress +=
                            spawn.Count;
                    }
                    else if (enemy.Rank == EnemyRank.Elite)
                    {
                        weightedKillProgress +=
                            spawn.Count * 3;
                    }
                }
            }

            Assert.That(totalGold, Is.EqualTo(924));
            Assert.That(goldThroughWaveTwo, Is.EqualTo(106));
            Assert.That(weightedKillProgress, Is.EqualTo(160));
            Assert.That(
                weightedKillProgress * 10_000L * 10_302L /
                10_000L,
                Is.LessThan(1_800_000),
                "Maximum split gain must not unlock a fourth carrier.");

            CompiledEnemyDefinition guardian =
                GetEnemyDefinition("boss_guardian");
            Assert.That(guardian.MaxHealthMilli, Is.EqualTo(300000));
            Assert.That(guardian.Armor, Is.EqualTo(40));
            Assert.That(guardian.LeakDamage, Is.EqualTo(5));
            Assert.That(
                guardian.BossAbility,
                Is.EqualTo(BossAbilityType.Shield));
            Assert.That(guardian.BossShieldBps, Is.EqualTo(400));
            Assert.That(
                guardian.BossAbilityIntervalTicks,
                Is.EqualTo(240));
            Assert.That(
                guardian.BossEnragedAbilityIntervalTicks,
                Is.EqualTo(180));

            CompiledEnemyDefinition summoner =
                GetEnemyDefinition("boss_summoner");
            Assert.That(summoner.MaxHealthMilli, Is.EqualTo(600000));
            Assert.That(
                summoner.BossAbility,
                Is.EqualTo(BossAbilityType.Summon));
            Assert.That(summoner.BossSummonCount, Is.EqualTo(2));
            Assert.That(
                summoner.BossEnragedSummonCount,
                Is.EqualTo(3));
            Assert.That(
                summoner.BossMaxActiveSummons,
                Is.EqualTo(6));
            Assert.That(
                summoner.BossSummonHealthBps,
                Is.EqualTo(5000));

            CompiledEnemyDefinition finalBoss =
                GetEnemyDefinition("boss_time_walker");
            Assert.That(finalBoss.MaxHealthMilli, Is.EqualTo(1100000));
            Assert.That(finalBoss.Armor, Is.EqualTo(50));
            Assert.That(finalBoss.LeakDamage, Is.EqualTo(20));
            Assert.That(
                finalBoss.BossAbility,
                Is.EqualTo(BossAbilityType.Teleport));
            Assert.That(finalBoss.BossCastTicks, Is.EqualTo(45));
            Assert.That(
                finalBoss.BossTeleportDistanceBps,
                Is.EqualTo(600));
            Assert.That(
                finalBoss.BossEnragedTeleportDistanceBps,
                Is.EqualTo(800));
        }

        [Test]
        public void BossShieldAndSummonAbilities_RunOnFixedTickData()
        {
            CompiledContent shieldContent =
                CompileCustomized(source =>
                {
                    ConfigureSingleWave(
                        source,
                        "boss_guardian",
                        1,
                        1);
                    source.run.startingTowerChoices =
                        new[] { "ballista" };
                    source.run.initiallyUnlockedTowers =
                        Array.Empty<string>();
                    source.run.startingCards =
                        new[] { "slow" };
                    source.run.buildSpotXMilli[0] = 0;
                    source.run.buildSpotYMilli[0] = 0;
                    TowerDefinitionDto ballista =
                        FindTowerDto(source, "ballista");
                    ballista.rangeMilli = 6000;
                    ballista.baseDamageMilli = 220000;
                    ballista.cooldownTicks = 1000;
                    ballista.projectileSpeedMilliPerTick = 1000;
                    FindEnemyDto(source, "boss_guardian")
                        .speedMilliPerTick = 1;
                });
            GameSimulation shieldSimulation =
                CreateCombatSimulation(
                    shieldContent,
                    "ballista",
                    Array.Empty<string>(),
                    0xB05501UL);
            for (int step = 0; step < 300; step++)
            {
                shieldSimulation.Step();
                SimulationSnapshot snapshot =
                    shieldSimulation.GetSnapshot();
                if (snapshot.Enemies.Length > 0 &&
                    snapshot.Enemies[0].ShieldMilli > 0)
                {
                    break;
                }
            }
            Assert.That(
                shieldSimulation.GetSnapshot()
                    .Enemies[0].ShieldMilli,
                Is.EqualTo(12000));
            Assert.That(
                shieldSimulation.GetSnapshot()
                    .Enemies[0].HealthMilli,
                Is.LessThanOrEqualTo(150000));
            int phaseEvents = 0;
            SimulationEventBuffer shieldEvents =
                shieldSimulation.ReadPresentationEvents();
            for (int eventIndex = 0;
                 eventIndex < shieldEvents.Count;
                 eventIndex++)
            {
                if (shieldEvents[eventIndex].Type ==
                    PresentationEventType.BossPhaseChanged)
                {
                    phaseEvents++;
                }
            }
            Assert.That(phaseEvents, Is.EqualTo(1));

            CompiledContent summonContent =
                CompileCustomized(source =>
                {
                    ConfigureSingleWave(
                        source,
                        "boss_summoner",
                        1,
                        1);
                    source.run.startingTowerChoices =
                        new[] { "ballista" };
                    source.run.initiallyUnlockedTowers =
                        Array.Empty<string>();
                    source.run.startingCards =
                        new[] { "slow" };
                    source.run.buildSpotXMilli[0] = -100000;
                    source.run.buildSpotYMilli[0] = 0;
                    FindTowerDto(source, "ballista")
                        .rangeMilli = 1;
                    FindEnemyDto(source, "boss_summoner")
                        .speedMilliPerTick = 1;
                    EnemyDefinitionDto runner =
                        FindEnemyDto(source, "runner");
                    runner.speedMilliPerTick = 1;
                    runner.leakDamage = 0;
                });
            GameSimulation summonSimulation =
                CreateCombatSimulation(
                    summonContent,
                    "ballista",
                    Array.Empty<string>(),
                    0xB05502UL);
            for (int step = 0; step < 800; step++)
            {
                summonSimulation.Step();
            }

            EnemySnapshot[] summonedEnemies =
                summonSimulation.GetSnapshot().Enemies;
            Assert.That(summonedEnemies, Has.Length.EqualTo(7));
            int zeroProgressSummons = 0;
            for (int enemyIndex = 0;
                 enemyIndex < summonedEnemies.Length;
                 enemyIndex++)
            {
                if (summonedEnemies[enemyIndex].DefinitionId ==
                    "runner")
                {
                    zeroProgressSummons++;
                    Assert.That(
                        summonedEnemies[enemyIndex]
                            .RewardBudget,
                        Is.Zero);
                    Assert.That(
                        summonedEnemies[enemyIndex]
                            .WaveProgressBudget,
                        Is.Zero);
                    Assert.That(
                        summonedEnemies[enemyIndex]
                            .CardPackProgressBudget,
                        Is.Zero);
                    Assert.That(
                        summonedEnemies[enemyIndex]
                            .MaxHealthMilli,
                        Is.EqualTo(9000));
                }
            }
            Assert.That(zeroProgressSummons, Is.EqualTo(6));
        }

        [Test]
        public void FinalBossTeleportCast_IsCancelledByControlInterrupt()
        {
            CompiledContent testContent =
                CompileCustomized(source =>
                {
                    ConfigureSingleWave(
                        source,
                        "boss_time_walker",
                        1,
                        1);
                    source.run.startingTowerChoices =
                        new[] { "mutation_obelisk" };
                    source.run.initiallyUnlockedTowers =
                        Array.Empty<string>();
                    source.run.startingCards =
                        new[] { "stun", "stun", "stun", "stun" };
                    source.run.startingGold = 300;
                    source.run.pathPointXMilli =
                        new[] { 0, 100000 };
                    source.run.pathPointYMilli =
                        new[] { 0, 0 };
                    for (int spot = 0; spot < 4; spot++)
                    {
                        source.run.buildSpotXMilli[spot] = 2600;
                        source.run.buildSpotYMilli[spot] = 0;
                    }
                    FindTowerDto(source, "mutation_obelisk")
                        .rangeMilli = 100;
                    FindEnemyDto(source, "boss_time_walker")
                        .speedMilliPerTick = 10;
                });

            var simulation = new GameSimulation();
            simulation.Initialize(testContent, 0xB05503UL);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower(
                        "mutation_obelisk")),
                "choose mutation obelisk");
            for (int tower = 0; tower < 4; tower++)
            {
                AssertAccepted(
                    simulation.Submit(
                        GameCommand.PlaceTower(
                            "mutation_obelisk",
                            tower)),
                    "place control tower");
                AssertAccepted(
                    simulation.Submit(
                        GameCommand.EquipCard(
                            tower,
                            tower,
                            0)),
                    "equip control card");
            }
            AssertAccepted(
                simulation.Submit(GameCommand.StartWave()),
                "start final boss interrupt test");

            int telegraphs = 0;
            int activations = 0;
            for (int step = 0; step < 400; step++)
            {
                simulation.Step();
                SimulationEventBuffer events =
                    simulation.ReadPresentationEvents();
                for (int eventIndex = 0;
                     eventIndex < events.Count;
                     eventIndex++)
                {
                    if (events[eventIndex].Type ==
                        PresentationEventType
                            .BossAbilityTelegraphed)
                    {
                        telegraphs++;
                    }
                    else if (events[eventIndex].Type ==
                        PresentationEventType
                            .BossAbilityActivated)
                    {
                        activations++;
                    }
                }
            }

            Assert.That(telegraphs, Is.EqualTo(1));
            Assert.That(activations, Is.Zero);
            Assert.That(
                simulation.GetSnapshot()
                    .Enemies[0].PathProgressMilli,
                Is.LessThan(10000),
                "A cancelled cast must not apply the teleport distance.");
        }

        [Test]
        public void FinalBossLeak_ImmediatelyDefeatsTheRun()
        {
            CompiledContent testContent =
                CompileCustomized(source =>
                {
                    ConfigureSingleWave(
                        source,
                        "boss_time_walker",
                        1,
                        1);
                    source.run.startingTowerChoices =
                        new[] { "ballista" };
                    source.run.initiallyUnlockedTowers =
                        Array.Empty<string>();
                    source.run.startingCards =
                        new[] { "slow" };
                    source.run.pathPointXMilli =
                        new[] { 0, 1 };
                    source.run.pathPointYMilli =
                        new[] { 0, 0 };
                    source.run.buildSpotXMilli[0] = -100000;
                    source.run.buildSpotYMilli[0] = 0;
                    FindTowerDto(source, "ballista")
                        .rangeMilli = 1;
                    FindEnemyDto(source, "boss_time_walker")
                        .speedMilliPerTick = 1;
                });
            GameSimulation simulation = CreateCombatSimulation(
                testContent,
                "ballista",
                Array.Empty<string>(),
                0xB05504UL);

            simulation.Step();

            Assert.That(simulation.Phase, Is.EqualTo(RunPhase.Defeat));
            Assert.That(simulation.GetSnapshot().BaseHealth, Is.Zero);
        }

        [Test]
        public void AllBuildSpots_AreOpenAndFirstTowerIsFree()
        {
            var simulation = new GameSimulation();
            simulation.Initialize(content, 51UL);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower("ballista")),
                "choose starting tower");

            SimulationSnapshot initial = simulation.GetSnapshot();
            Assert.That(initial.BuildSpots, Has.Length.EqualTo(8));
            for (int i = 0; i < initial.BuildSpots.Length; i++)
            {
                Assert.That(initial.BuildSpots[i].UnlockCost, Is.Zero);
                Assert.That(initial.BuildSpots[i].Unlocked, Is.True);
            }

            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower("ballista", 5)),
                "place free starting tower");
            Assert.That(simulation.GetSnapshot().Gold, Is.Zero);
            Assert.That(simulation.GetSnapshot().Towers, Has.Length.EqualTo(1));
        }

        [Test]
        public void AdditionalTower_CostsExactlyOneHundredGold()
        {
            CompiledContent fundedContent = CompileCustomized(
                source => source.run.startingGold = 100);
            var first = new GameSimulation();
            first.Initialize(fundedContent, 52UL);
            AssertAccepted(
                first.Submit(
                    GameCommand.ChooseStartingTower("ballista")),
                "choose first starting tower");
            AssertAccepted(
                first.Submit(
                    GameCommand.PlaceTower("ballista", 0)),
                "place free starting tower");
            Assert.That(first.GetSnapshot().Gold, Is.EqualTo(100));
            AssertAccepted(
                first.Submit(
                    GameCommand.PlaceTower("ballista", 5)),
                "place paid tower");
            Assert.That(first.GetSnapshot().Gold, Is.Zero);
            Assert.That(first.GetSnapshot().Towers, Has.Length.EqualTo(2));
            Assert.That(
                first.GetSnapshot().Towers[1].BuildPointIndex,
                Is.EqualTo(5));

            ulong beforeFailure = first.ComputeStateHash();
            CommandResult third = first.Submit(
                GameCommand.PlaceTower("ballista", 6));
            Assert.That(third.Accepted, Is.False);
            Assert.That(third.Error, Is.EqualTo(CommandError.InsufficientGold));
            Assert.That(first.ComputeStateHash(), Is.EqualTo(beforeFailure));
        }

        [Test]
        public void TowerLevels_UnlockOneTwoAndThreeCardSlots()
        {
            var simulation = new GameSimulation();
            simulation.Initialize(content, 53UL);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower("ballista")),
                "choose starting tower");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower("ballista", 0)),
                "place starting tower");

            TowerSnapshot tower =
                simulation.GetSnapshot().Towers[0];
            Assert.That(tower.Level, Is.EqualTo(1));
            Assert.That(
                GameSimulation.GetTowerCardCapacityForLevel(
                    tower.Level),
                Is.EqualTo(1));

            int split = FindCardInstanceId(simulation, "split");
            int burn = FindCardInstanceId(simulation, "burn");
            int poison = FindCardInstanceId(simulation, "poison");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.EquipCard(
                        split,
                        tower.Id,
                        0)),
                "equip the level-one slot");
            CommandResult lockedSecondSlot = simulation.Submit(
                GameCommand.EquipCard(
                    burn,
                    tower.Id,
                    1));
            Assert.That(lockedSecondSlot.Accepted, Is.False);
            Assert.That(
                lockedSecondSlot.Error,
                Is.EqualTo(CommandError.SlotOutOfRange));

            for (int level = 2; level <= 4; level++)
            {
                AssertAccepted(
                    simulation.Submit(
                        GameCommand.UpgradeTower(tower.Id)),
                    "upgrade to level " + level);
            }

            tower = simulation.GetSnapshot().Towers[0];
            Assert.That(tower.Level, Is.EqualTo(4));
            Assert.That(
                GameSimulation.GetTowerCardCapacityForLevel(
                    tower.Level),
                Is.EqualTo(2));
            AssertAccepted(
                simulation.Submit(
                    GameCommand.EquipCard(
                        burn,
                        tower.Id,
                        1)),
                "equip the level-four slot");
            CommandResult lockedThirdSlot = simulation.Submit(
                GameCommand.EquipCard(
                    poison,
                    tower.Id,
                    2));
            Assert.That(lockedThirdSlot.Accepted, Is.False);
            Assert.That(
                lockedThirdSlot.Error,
                Is.EqualTo(CommandError.SlotOutOfRange));

            AssertAccepted(
                simulation.Submit(
                    GameCommand.UpgradeTower(tower.Id)),
                "upgrade to level five");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.UpgradeTower(tower.Id)),
                "upgrade to level six");
            tower = simulation.GetSnapshot().Towers[0];
            Assert.That(tower.Level, Is.EqualTo(6));
            Assert.That(
                GameSimulation.GetTowerCardCapacityForLevel(
                    tower.Level),
                Is.EqualTo(3));
            AssertAccepted(
                simulation.Submit(
                    GameCommand.EquipCard(
                        poison,
                        tower.Id,
                        2)),
                "equip the level-six slot");
        }

        [Test]
        public void Combat_AllowsAffordableTowerConstruction()
        {
            CompiledContent fundedContent = CompileCustomized(
                source => source.run.startingGold = 100);
            var simulation = new GameSimulation();
            simulation.Initialize(fundedContent, 54UL);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower("ballista")),
                "choose starting tower");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower("ballista", 0)),
                "place starting tower");
            AssertAccepted(
                simulation.Submit(GameCommand.StartWave()),
                "start wave");
            Assert.That(simulation.Phase, Is.EqualTo(RunPhase.Combat));

            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower("ballista", 1)),
                "place tower during combat");
            SimulationSnapshot snapshot =
                simulation.GetSnapshot();
            Assert.That(snapshot.Towers, Has.Length.EqualTo(2));
            Assert.That(snapshot.Towers[1].Level, Is.EqualTo(1));
            CollectionAssert.AreEqual(
                new[] { -1, -1, -1 },
                snapshot.Towers[1].CardInstanceIds);
            Assert.That(snapshot.Gold, Is.Zero);
        }

        [Test]
        public void WorldCardPack_PausesUntilTheNewCardIsEquipped()
        {
            CompiledContent testContent = CompileCustomized(source =>
            {
                ConfigureSingleWave(source, "raider", 2, 300);
                source.run.cardPackProgressThresholds =
                    new[] { 10000, 1000000 };
                source.run.startingTowerChoices =
                    new[] { "ballista" };
                source.run.initiallyUnlockedTowers =
                    Array.Empty<string>();
                source.run.startingCards =
                    new[] { "explode" };

                TowerDefinitionDto ballista =
                    FindTowerDto(source, "ballista");
                ballista.baseDamageMilli = 100000;
                ballista.cooldownTicks = 1;
                FindEnemyDto(source, "raider")
                    .speedMilliPerTick = 1;
            });

            GameSimulation simulation = CreateCombatSimulation(
                testContent,
                "ballista",
                new[] { "explode" },
                0xCA4DUL);

            CardPackSnapshot worldPack = default(CardPackSnapshot);
            bool foundPack = false;
            for (int step = 0; step < 200 && !foundPack; step++)
            {
                simulation.Step();
                SimulationSnapshot snapshot =
                    simulation.GetSnapshot();
                for (int packIndex = 0;
                     packIndex < snapshot.CardPacks.Length;
                     packIndex++)
                {
                    if (snapshot.CardPacks[packIndex].WorldDrop)
                    {
                        worldPack =
                            snapshot.CardPacks[packIndex];
                        foundPack = true;
                        break;
                    }
                }
            }

            Assert.That(foundPack, Is.True);
            Assert.That(simulation.Phase, Is.EqualTo(RunPhase.Combat));
            AssertAccepted(
                simulation.Submit(
                    GameCommand.OpenCardPack(worldPack.Id)),
                "open world card pack");

            SimulationSnapshot choice =
                simulation.GetSnapshot();
            Assert.That(
                choice.Phase,
                Is.EqualTo(RunPhase.CardPackChoice));
            Assert.That(choice.CardPackOffers, Has.Length.EqualTo(3));
            Assert.That(
                new HashSet<CardId>(choice.CardPackOffers).Count,
                Is.EqualTo(3));
            long pausedTick = choice.Tick;
            for (int step = 0; step < 5; step++)
            {
                simulation.Step();
            }
            Assert.That(simulation.Tick, Is.EqualTo(pausedTick));

            AssertAccepted(
                simulation.Submit(
                    GameCommand.SelectCardPack(0)),
                "select card-pack card");
            SimulationSnapshot loadout =
                simulation.GetSnapshot();
            Assert.That(
                loadout.Phase,
                Is.EqualTo(RunPhase.CardPackLoadout));
            Assert.That(
                loadout.PendingCardInstanceId,
                Is.GreaterThanOrEqualTo(0));

            CommandResult prematureResume = simulation.Submit(
                GameCommand.ResumeCardPackCombat());
            Assert.That(prematureResume.Accepted, Is.False);
            Assert.That(
                prematureResume.Error,
                Is.EqualTo(
                    CommandError.CardPackRequiresEquippedCard));

            for (int level = 2; level <= 4; level++)
            {
                AssertAccepted(
                    simulation.Submit(
                        GameCommand.UpgradeTower(
                            loadout.Towers[0].Id)),
                    "unlock the second card slot at level " +
                    level);
            }

            AssertAccepted(
                simulation.Submit(
                    GameCommand.EquipCard(
                        loadout.PendingCardInstanceId,
                        loadout.Towers[0].Id,
                        1)),
                "equip newly selected card");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ResumeCardPackCombat()),
                "resume card-pack combat");
            Assert.That(simulation.Phase, Is.EqualTo(RunPhase.Combat));
            Assert.That(simulation.Tick, Is.EqualTo(pausedTick));
        }

        [Test]
        public void EscapedShimmeringCarrier_DealsNoDamageAndLosesItsPack()
        {
            CompiledContent testContent = CompileCustomized(source =>
            {
                ConfigureSingleWave(source, "raider", 2, 3000);
                source.run.cardPackProgressThresholds =
                    new[] { 10000, 1000000 };
                source.run.startingTowerChoices =
                    new[] { "ballista" };
                source.run.initiallyUnlockedTowers =
                    Array.Empty<string>();
                source.run.startingCards =
                    new[] { "explode" };
                source.run.pathPointXMilli =
                    new[] { 0, 1000 };
                source.run.pathPointYMilli =
                    new[] { 0, 0 };
                source.run.buildSpotXMilli[0] = 0;
                source.run.buildSpotYMilli[0] = 0;

                TowerDefinitionDto ballista =
                    FindTowerDto(source, "ballista");
                ballista.baseDamageMilli = 100000;
                ballista.cooldownTicks = 2000;
                ballista.projectileSpeedMilliPerTick = 1000;
                FindEnemyDto(source, "raider")
                    .speedMilliPerTick = 1;
            });

            GameSimulation simulation = CreateCombatSimulation(
                testContent,
                "ballista",
                Array.Empty<string>(),
                0x1057UL);
            for (int step = 0; step < 1500; step++)
            {
                simulation.Step();
            }

            SimulationSnapshot snapshot =
                simulation.GetSnapshot();
            Assert.That(snapshot.BaseHealth, Is.EqualTo(20));
            Assert.That(snapshot.CardPacks, Is.Empty);
            Assert.That(snapshot.CardPackProgress, Is.EqualTo(10000));
            Assert.That(snapshot.NextCardPackThreshold, Is.EqualTo(1000000));

            int lostEvents = 0;
            SimulationEventBuffer events =
                simulation.ReadPresentationEvents();
            for (int eventIndex = 0;
                 eventIndex < events.Count;
                 eventIndex++)
            {
                if (events[eventIndex].Type ==
                    PresentationEventType.CardPackLost)
                {
                    lostEvents++;
                }
            }
            Assert.That(lostEvents, Is.EqualTo(1));
        }

        [Test]
        public void Combat_RejectsCardEquipAndReorderCommands()
        {
            GameSimulation simulation = CreateCombatSimulation(
                "ballista",
                new[] { "split", "burn", "explode" },
                0xC0FFEEUL);
            SimulationSnapshot snapshot = simulation.GetSnapshot();
            int towerId = snapshot.Towers[0].Id;
            int splitCardId = FindCardInstanceId(simulation, "split");

            CommandResult equipResult = simulation.Submit(
                GameCommand.EquipCard(splitCardId, towerId, 0));
            CommandResult reorderResult = simulation.Submit(
                GameCommand.ReorderCard(towerId, 0, 1));
            CommandResult moveResult = simulation.Submit(
                GameCommand.MoveCard(splitCardId, towerId, 1));
            CommandResult unequipResult = simulation.Submit(
                GameCommand.UnequipCard(splitCardId));

            Assert.That(equipResult.Accepted, Is.False);
            Assert.That(
                equipResult.Error,
                Is.EqualTo(CommandError.CombatLoadoutLocked));
            StringAssert.Contains("combat", equipResult.Message.ToLowerInvariant());
            Assert.That(reorderResult.Accepted, Is.False);
            Assert.That(
                reorderResult.Error,
                Is.EqualTo(CommandError.CombatLoadoutLocked));
            StringAssert.Contains("combat", reorderResult.Message.ToLowerInvariant());
            Assert.That(moveResult.Accepted, Is.False);
            Assert.That(
                moveResult.Error,
                Is.EqualTo(CommandError.CombatLoadoutLocked));
            StringAssert.Contains("combat", moveResult.Message.ToLowerInvariant());
            Assert.That(unequipResult.Accepted, Is.False);
            Assert.That(
                unequipResult.Error,
                Is.EqualTo(CommandError.CombatLoadoutLocked));
        }

        [Test]
        public void EnemySubjectMode_AppliesCardProgramOnProjectileHit()
        {
            CompiledContent testContent = CompileCustomized(source =>
            {
                ConfigureSingleWave(source, "raider", 1, 1);
                source.run.startingTowerChoices =
                    new[] { "ballista" };
                source.run.initiallyUnlockedTowers =
                    Array.Empty<string>();
                source.run.startingCards =
                    new[] { "burn" };
                source.run.pathPointXMilli =
                    new[] { 0, 20000 };
                source.run.pathPointYMilli =
                    new[] { 0, 0 };
                source.run.buildSpotXMilli[0] = -5000;
                source.run.buildSpotYMilli[0] = 0;

                TowerDefinitionDto ballista =
                    FindTowerDto(source, "ballista");
                ballista.rangeMilli = 30000;
                ballista.cooldownTicks = 100;
                ballista.baseDamageMilli = 1000;
                ballista.projectileSpeedMilliPerTick = 1000;
                ballista.projectileLifetimeTicks = 100;

                EnemyDefinitionDto raider =
                    FindEnemyDto(source, "raider");
                raider.maxHealthMilli = 100000;
                raider.speedMilliPerTick = 1;
            });

            var simulation = new GameSimulation();
            simulation.Initialize(testContent, 55UL);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower("ballista")),
                "choose ballista");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower("ballista", 0)),
                "place ballista");
            TowerSnapshot tower =
                simulation.GetSnapshot().Towers[0];
            EquipProgram(
                simulation,
                testContent,
                tower.Id,
                new[] { "burn" });
            AssertAccepted(
                simulation.Submit(
                    GameCommand.SetTowerSubjectType(
                        tower.Id,
                        SubjectType.Enemy)),
                "choose enemy interpretation");
            AssertAccepted(
                simulation.Submit(GameCommand.StartWave()),
                "start enemy-interpretation wave");

            bool sawProjectileBeforeStatus = false;
            bool sawBurnAfterHit = false;
            for (int step = 0; step < 30; step++)
            {
                simulation.Step();
                SimulationSnapshot snapshot =
                    simulation.GetSnapshot();
                if (snapshot.Enemies.Length == 0)
                {
                    continue;
                }

                bool burning = Array.IndexOf(
                    snapshot.Enemies[0].Statuses,
                    StatusType.Burn) >= 0;
                if (snapshot.Projectiles.Length > 0 &&
                    !burning)
                {
                    sawProjectileBeforeStatus = true;
                    Assert.That(
                        snapshot.Projectiles[0]
                            .ApplyEnemyProgramOnHit,
                        Is.True);
                }

                if (burning)
                {
                    sawBurnAfterHit = true;
                    break;
                }
            }

            Assert.That(sawProjectileBeforeStatus, Is.True);
            Assert.That(sawBurnAfterHit, Is.True);
        }

        [Test]
        public void SlotSubjectModes_AreIndependentAndExecuteAtTheirSubjects()
        {
            CompiledContent testContent = CompileCustomized(source =>
            {
                ConfigureSingleWave(source, "raider", 1, 1);
                source.run.startingTowerChoices =
                    new[] { "ballista" };
                source.run.initiallyUnlockedTowers =
                    Array.Empty<string>();
                source.run.startingCards =
                    new[] { "split", "burn" };
                source.run.pathPointXMilli =
                    new[] { 0, 20000 };
                source.run.pathPointYMilli =
                    new[] { 0, 0 };
                source.run.buildSpotXMilli[0] = -5000;
                source.run.buildSpotYMilli[0] = 0;

                TowerDefinitionDto ballista =
                    FindTowerDto(source, "ballista");
                ballista.rangeMilli = 30000;
                ballista.cooldownTicks = 100;
                ballista.baseDamageMilli = 1000;
                ballista.projectileSpeedMilliPerTick = 1000;
                ballista.projectileLifetimeTicks = 100;

                EnemyDefinitionDto raider =
                    FindEnemyDto(source, "raider");
                raider.maxHealthMilli = 100000;
                raider.speedMilliPerTick = 1;
            });

            var simulation = new GameSimulation();
            simulation.Initialize(testContent, 56UL);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower("ballista")),
                "choose ballista");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower("ballista", 0)),
                "place ballista");
            TowerSnapshot tower =
                simulation.GetSnapshot().Towers[0];
            EquipProgram(
                simulation,
                testContent,
                tower.Id,
                new[] { "split", "burn" });
            AssertAccepted(
                simulation.Submit(
                    GameCommand.SetTowerSlotSubjectType(
                        tower.Id,
                        1,
                        SubjectType.Enemy)),
                "set the second slot to enemy");

            tower = simulation.GetSnapshot().Towers[0];
            Assert.That(
                tower.CardSubjectTypes[0],
                Is.EqualTo(SubjectType.Projectile));
            Assert.That(
                tower.CardSubjectTypes[1],
                Is.EqualTo(SubjectType.Enemy));

            AssertAccepted(
                simulation.Submit(GameCommand.StartWave()),
                "start mixed-interpretation wave");

            bool sawSplitProjectiles = false;
            bool sawBurnAfterHit = false;
            for (int step = 0; step < 30; step++)
            {
                simulation.Step();
                SimulationSnapshot current =
                    simulation.GetSnapshot();
                if (current.Projectiles.Length >= 2)
                {
                    sawSplitProjectiles = true;
                    for (int projectileIndex = 0;
                         projectileIndex < current.Projectiles.Length;
                         projectileIndex++)
                    {
                        Assert.That(
                            current.Projectiles[projectileIndex]
                                .ApplyEnemyProgramOnHit,
                            Is.True,
                            "Split projectile {0} lost the enemy-on-hit program.",
                            current.Projectiles[projectileIndex].Id);
                    }
                }

                if (current.Enemies.Length > 0 &&
                    Array.IndexOf(
                        current.Enemies[0].Statuses,
                        StatusType.Burn) >= 0)
                {
                    sawBurnAfterHit = true;
                    break;
                }
            }

            Assert.That(sawSplitProjectiles, Is.True);
            Assert.That(sawBurnAfterHit, Is.True);
        }

        [Test]
        public void SameSeedAndCommands_ProduceTheSameHashOnEveryTick()
        {
            const ulong seed = 0x0123456789ABCDEFUL;
            string[] program = { "split", "burn", "explode" };
            GameSimulation first = CreateCombatSimulation("ballista", program, seed);
            GameSimulation second = CreateCombatSimulation("ballista", program, seed);

            Assert.That(first.ComputeStateHash(), Is.EqualTo(second.ComputeStateHash()));
            for (int step = 0; step < 1500; step++)
            {
                first.Step();
                second.Step();

                Assert.That(
                    first.ComputeStateHash(),
                    Is.EqualTo(second.ComputeStateHash()),
                    "State diverged after simulation step {0}.",
                    step + 1);
            }
        }

        [Test]
        public void Ballista_CardOrderChangesProjectileBindingDistribution()
        {
            int[] splitThenBurn = GetFirstVolleyBindingCounts(
                new[] { "split", "burn", "explode" });
            int[] burnThenSplit = GetFirstVolleyBindingCounts(
                new[] { "burn", "split", "explode" });

            CollectionAssert.AreEqual(new[] { 2, 2 }, splitThenBurn);
            CollectionAssert.AreEqual(new[] { 1, 2 }, burnThenSplit);
            CollectionAssert.AreNotEqual(splitThenBurn, burnThenSplit);

            GameSimulation first = CreateCombatSimulation(
                "ballista",
                new[] { "split", "burn", "explode" },
                23UL);
            GameSimulation second = CreateCombatSimulation(
                "ballista",
                new[] { "burn", "split", "explode" },
                23UL);
            first.Step();
            second.Step();
            Assert.That(
                first.ComputeStateHash(),
                Is.Not.EqualTo(second.ComputeStateHash()));
        }

        [Test]
        public void MutationSplit_PreservesRewardDeepCopiesStatusesAndSkipsDeathBindings()
        {
            GameSimulation continuationSimulation = CreateCombatSimulation(
                "mutation_obelisk",
                new[] { "split", "burn", "explode" },
                17UL);
            continuationSimulation.Step();
            EnemySnapshot[] continuationEnemies =
                continuationSimulation.GetSnapshot().Enemies;

            Assert.That(continuationEnemies, Has.Length.EqualTo(2));
            AssertSplitRewardIsPreserved(continuationEnemies, "raider");
            Assert.That(
                continuationEnemies[0].Position.X.MilliUnits,
                Is.EqualTo(
                    continuationEnemies[1].Position.X.MilliUnits)
                    .Within(2),
                "A horizontal path split must keep both branches at the same progress.");
            Assert.That(
                Math.Abs(
                    continuationEnemies[0].Position.Y.MilliUnits -
                    continuationEnemies[1].Position.Y.MilliUnits),
                Is.GreaterThanOrEqualTo(650),
                "The two split branches must visibly separate to the path's left and right.");
            Assert.That(
                continuationEnemies[0].Position.Y.MilliUnits *
                continuationEnemies[1].Position.Y.MilliUnits,
                Is.LessThan(0),
                "The original and child must occupy opposite sides of the route.");
            EnemySnapshot continuedChild = FindNewestEnemy(continuationEnemies);
            CollectionAssert.Contains(continuedChild.Statuses, StatusType.Burn);
            Assert.That(continuedChild.DeathBindingCount, Is.EqualTo(1),
                "The split child must execute both cards after split.");

            GameSimulation inheritanceSimulation = CreateCombatSimulation(
                "mutation_obelisk",
                new[] { "burn", "split", "explode" },
                17UL);
            inheritanceSimulation.Step();
            EnemySnapshot[] inheritanceEnemies =
                inheritanceSimulation.GetSnapshot().Enemies;

            Assert.That(inheritanceEnemies, Has.Length.EqualTo(2));
            AssertSplitRewardIsPreserved(inheritanceEnemies, "raider");
            EnemySnapshot inheritedChild = FindNewestEnemy(inheritanceEnemies);
            EnemySnapshot inheritedOriginal =
                inheritanceEnemies[0].Id == inheritedChild.Id
                    ? inheritanceEnemies[1]
                    : inheritanceEnemies[0];
            CollectionAssert.Contains(inheritedChild.Statuses, StatusType.Burn);
            Assert.That(inheritedChild.DeathBindingCount, Is.EqualTo(1),
                "The split child must continue at explode after inheriting burn.");

            EnemyState originalState = FindEnemyState(
                inheritanceSimulation,
                inheritedOriginal.Id);
            EnemyState childState = FindEnemyState(
                inheritanceSimulation,
                inheritedChild.Id);
            Assert.That(originalState.Statuses, Has.Count.EqualTo(1));
            Assert.That(childState.Statuses, Has.Count.EqualTo(1));
            AssertStatusWasDeepCopied(
                originalState.Statuses[0],
                childState.Statuses[0]);

            GameSimulation bindingSimulation = CreateCombatSimulation(
                "mutation_obelisk",
                new[] { "explode", "split" },
                17UL);
            bindingSimulation.Step();
            EnemySnapshot[] bindingEnemies =
                bindingSimulation.GetSnapshot().Enemies;
            Assert.That(bindingEnemies, Has.Length.EqualTo(2));
            EnemySnapshot bindingChild = FindNewestEnemy(bindingEnemies);
            Assert.That(bindingChild.DeathBindingCount, Is.Zero,
                "Death bindings created before split must not be inherited.");
        }

        [Test]
        public void EnemySplit_UsesPointFiveOneBudgetAndStopsAtEmergencyMemberCap()
        {
            CompiledContent splitContent = CompileCustomized(source =>
            {
                ConfigureSingleWave(source, "raider", 1, 1);
                source.run.startingTowerChoices =
                    new[] { "mutation_obelisk" };
                source.run.initiallyUnlockedTowers =
                    Array.Empty<string>();
                source.run.startingCards =
                    new[] { "split", "split" };
                source.run.startingGold = 100;
                source.run.buildSpotXMilli[0] = 1000;
                source.run.buildSpotYMilli[0] = 0;
                source.run.buildSpotXMilli[1] = 5000;
                source.run.buildSpotYMilli[1] = 0;
                FindTowerDto(source, "mutation_obelisk")
                    .rangeMilli = 500;
                EnemyDefinitionDto raider = FindEnemyDto(source, "raider");
                raider.maxHealthMilli = 1_000_000;
                raider.speedMilliPerTick = 1000;
                source.safety.maxEnemySplitCount = 1;
                source.safety.maxEnemyLineageMembers = 3;
            });

            var simulation = new GameSimulation();
            simulation.Initialize(splitContent, 0x5A17UL);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower(
                        "mutation_obelisk")),
                "choose mutation obelisk");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower(
                        "mutation_obelisk",
                        0)),
                "place first mutation obelisk");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower(
                        "mutation_obelisk",
                        1)),
                "place second mutation obelisk");

            TowerSnapshot[] towers =
                simulation.GetSnapshot().Towers;
            EquipProgram(
                simulation,
                splitContent,
                towers[0].Id,
                new[] { "split" });
            EquipProgram(
                simulation,
                splitContent,
                towers[1].Id,
                new[] { "split" });
            AssertAccepted(
                simulation.Submit(GameCommand.StartWave()),
                "start lineage split scenario");

            for (int step = 0; step < 5; step++)
            {
                simulation.Step();
            }

            SimulationSnapshot snapshot =
                simulation.GetSnapshot();
            Assert.That(snapshot.Enemies, Has.Length.EqualTo(3));
            Assert.That(snapshot.Lineages, Has.Length.EqualTo(1));
            Assert.That(snapshot.Lineages[0].SplitCount, Is.EqualTo(2));
            Assert.That(
                snapshot.Lineages[0].SpawnedEntityCount,
                Is.EqualTo(3));
            Assert.That(
                snapshot.Lineages[0].BaseCardPackProgress,
                Is.EqualTo(10000));

            var progressBudgets = new int[snapshot.Enemies.Length];
            var generations = new int[snapshot.Enemies.Length];
            var sizeMultipliers = new int[snapshot.Enemies.Length];
            for (int enemyIndex = 0;
                 enemyIndex < snapshot.Enemies.Length;
                 enemyIndex++)
            {
                progressBudgets[enemyIndex] =
                    snapshot.Enemies[enemyIndex]
                        .CardPackProgressBudget;
                generations[enemyIndex] =
                    snapshot.Enemies[enemyIndex].Generation;
                sizeMultipliers[enemyIndex] =
                    snapshot.Enemies[enemyIndex]
                        .SizeMultiplierBps;
            }
            Array.Sort(progressBudgets);
            Array.Sort(generations);
            Array.Sort(sizeMultipliers);
            CollectionAssert.AreEqual(
                new[] { 2601, 2601, 5100 },
                progressBudgets);
            CollectionAssert.AreEqual(
                new[] { 1, 2, 2 },
                generations);
            CollectionAssert.AreEqual(
                new[] { 8100, 8100, 9000 },
                sizeMultipliers);
            Assert.That(
                progressBudgets[0] +
                progressBudgets[1] +
                progressBudgets[2],
                Is.EqualTo(10302));
            Assert.That(
                simulation.Diagnostics.Count,
                Is.EqualTo(1));
            Assert.That(
                simulation.Diagnostics[0].Code,
                Is.EqualTo(
                    DiagnosticCode.EnemyLineageLimitReached));
        }

        [Test]
        public void EnemySplit_ThreeCardsReachEightMembersDespiteLegacySplitHint()
        {
            CompiledContent splitContent = CompileCustomized(source =>
            {
                ConfigureSingleWave(source, "raider", 1, 1);
                source.run.startingTowerChoices =
                    new[] { "mutation_obelisk" };
                source.run.initiallyUnlockedTowers =
                    Array.Empty<string>();
                source.run.startingCards =
                    new[] { "split", "split", "split" };
                EnemyDefinitionDto raider =
                    FindEnemyDto(source, "raider");
                raider.maxHealthMilli = 1_000_000;
                raider.speedMilliPerTick = 1;
                source.safety.maxEnemySplitCount = 1;
                source.safety.maxEnemyLineageMembers = 8;
            });
            GameSimulation simulation =
                CreateCombatSimulation(
                    splitContent,
                    "mutation_obelisk",
                    new[] { "split", "split", "split" },
                    0x5A18UL);

            simulation.Step();

            SimulationSnapshot snapshot =
                simulation.GetSnapshot();
            Assert.That(snapshot.Enemies, Has.Length.EqualTo(8));
            Assert.That(snapshot.Lineages, Has.Length.EqualTo(1));
            LineageSnapshot lineage = snapshot.Lineages[0];
            Assert.That(lineage.HighestGeneration, Is.EqualTo(3));
            Assert.That(lineage.SplitCount, Is.EqualTo(7));
            Assert.That(lineage.SpawnedEntityCount, Is.EqualTo(8));
            Assert.That(lineage.LiveMembers, Is.EqualTo(8));
            Assert.That(lineage.BaseRewardBudget, Is.EqualTo(5));
            Assert.That(lineage.ProgressBudget, Is.EqualTo(1));
            Assert.That(
                lineage.BaseCardPackProgress,
                Is.EqualTo(10000));

            int rewardTotal = 0;
            int waveProgressTotal = 0;
            int cardPackProgressTotal = 0;
            for (int enemyIndex = 0;
                 enemyIndex < snapshot.Enemies.Length;
                 enemyIndex++)
            {
                EnemySnapshot enemy = snapshot.Enemies[enemyIndex];
                Assert.That(enemy.Generation, Is.EqualTo(3));
                Assert.That(enemy.MaxHealthMilli, Is.EqualTo(91125));
                Assert.That(enemy.HealthMilli, Is.EqualTo(91125));
                Assert.That(enemy.SizeMultiplierBps, Is.EqualTo(7290));
                rewardTotal += enemy.RewardBudget;
                waveProgressTotal += enemy.WaveProgressBudget;
                cardPackProgressTotal +=
                    enemy.CardPackProgressBudget;
            }

            Assert.That(rewardTotal, Is.EqualTo(5));
            Assert.That(waveProgressTotal, Is.EqualTo(1));
            Assert.That(cardPackProgressTotal, Is.EqualTo(10608));
            Assert.That(simulation.Diagnostics.Count, Is.Zero);
        }

        [TestCase(2222, false)]
        [TestCase(2223, true)]
        public void EnemySplit_RequiresAtLeastOneHealthForBothResults(
            int startingHealthMilli,
            bool shouldSplit)
        {
            CompiledContent splitContent = CompileCustomized(source =>
            {
                ConfigureSingleWave(source, "raider", 1, 1);
                source.run.startingTowerChoices =
                    new[] { "mutation_obelisk" };
                source.run.initiallyUnlockedTowers =
                    Array.Empty<string>();
                source.run.startingCards =
                    new[] { "split" };
                EnemyDefinitionDto raider = FindEnemyDto(source, "raider");
                raider.maxHealthMilli = startingHealthMilli;
                raider.speedMilliPerTick = 1;
            });
            GameSimulation simulation = CreateCombatSimulation(
                splitContent,
                "mutation_obelisk",
                new[] { "split" },
                0x51A7UL);

            simulation.Step();

            SimulationSnapshot snapshot = simulation.GetSnapshot();
            Assert.That(
                snapshot.Enemies,
                Has.Length.EqualTo(shouldSplit ? 2 : 1));
            Assert.That(snapshot.Lineages, Has.Length.EqualTo(1));
            Assert.That(
                snapshot.Lineages[0].SplitCount,
                Is.EqualTo(shouldSplit ? 1 : 0));
            Assert.That(
                snapshot.Lineages[0].SpawnedEntityCount,
                Is.EqualTo(shouldSplit ? 2 : 1));

            for (int enemyIndex = 0;
                 enemyIndex < snapshot.Enemies.Length;
                 enemyIndex++)
            {
                EnemySnapshot enemy = snapshot.Enemies[enemyIndex];
                Assert.That(
                    enemy.MaxHealthMilli,
                    Is.EqualTo(shouldSplit ? 1000 : startingHealthMilli));
                Assert.That(
                    enemy.HealthMilli,
                    Is.EqualTo(shouldSplit ? 1000 : startingHealthMilli));
                Assert.That(
                    enemy.Generation,
                    Is.EqualTo(shouldSplit ? 1 : 0));
                Assert.That(
                    enemy.SizeMultiplierBps,
                    Is.EqualTo(shouldSplit ? 9000 : 10000));
            }
        }

        [Test]
        public void LineageBudget_IgnoresLegacySplitHintAndStopsAtMemberCap()
        {
            CompiledContent memberLimitedContent =
                CompileCustomized(source =>
                {
                    source.safety.maxEnemySplitCount = 2;
                    source.safety.maxEnemyLineageMembers = 4;
                });
            var budget = new LineageBudget(
                new LineageId(1),
                memberLimitedContent.Safety);

            Assert.That(
                budget.TryReserveSplit(
                    1,
                    1,
                    out BudgetFailure firstFailure),
                Is.True);
            Assert.That(firstFailure, Is.EqualTo(BudgetFailure.None));
            Assert.That(
                budget.TryReserveSplit(
                    1,
                    1,
                    out BudgetFailure secondFailure),
                Is.True);
            Assert.That(secondFailure, Is.EqualTo(BudgetFailure.None));
            Assert.That(
                budget.TryReserveSplit(
                    1,
                    1,
                    out BudgetFailure thirdFailure),
                Is.True);
            Assert.That(thirdFailure, Is.EqualTo(BudgetFailure.None));
            Assert.That(
                budget.TryReserveSplit(
                    1,
                    1,
                    out BudgetFailure fourthFailure),
                Is.False);
            Assert.That(
                fourthFailure,
                Is.EqualTo(
                    BudgetFailure.EnemyLineageEntityLimit));
            Assert.That(budget.SplitsUsed, Is.EqualTo(3));
            Assert.That(budget.EntitiesCreated, Is.EqualTo(4));
        }

        [Test]
        public void ProjectileKnockback_UsesFlightDirectionAndSweptCollision()
        {
            CompiledContent knockbackContent =
                CompileCustomized(source =>
                {
                    ConfigureSingleWave(source, "raider", 2, 1);
                    source.run.startingTowerChoices =
                        new[] { "ballista" };
                    source.run.initiallyUnlockedTowers =
                        Array.Empty<string>();
                    source.run.startingCards =
                        new[] { "knockback" };
                    source.run.pathPointXMilli =
                        new[] { 0, 20000 };
                    source.run.pathPointYMilli =
                        new[] { 0, 0 };
                    source.run.buildSpotXMilli[0] = -5000;
                    source.run.buildSpotYMilli[0] = 0;

                    TowerDefinitionDto ballista =
                        FindTowerDto(source, "ballista");
                    ballista.rangeMilli = 30000;
                    ballista.cooldownTicks = 100;
                    ballista.attackWindupTicks = 0;
                    ballista.baseDamageMilli = 1000;
                    ballista.projectileSpeedMilliPerTick = 1000;
                    ballista.projectileLifetimeTicks = 100;

                    EnemyDefinitionDto raider =
                        FindEnemyDto(source, "raider");
                    raider.maxHealthMilli = 100000;
                    raider.speedMilliPerTick = 500;

                    CardDefinitionDto knockback =
                        FindCardDto(source, "knockback");
                    knockback.projectileEffects[0].amount = 2000;
                    knockback.projectileEffects[0].amount2 = 3000;
                    knockback.projectileEffects[0].radiusMilli = 350;
                });

            var simulation = new GameSimulation();
            simulation.Initialize(knockbackContent, 0xB00BUL);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower("ballista")),
                "choose ballista");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower("ballista", 0)),
                "place ballista behind the path");
            EquipProgram(
                simulation,
                knockbackContent,
                simulation.GetSnapshot().Towers[0].Id,
                new[] { "knockback" });
            AssertAccepted(
                simulation.Submit(GameCommand.StartWave()),
                "start knockback scenario");

            int hitEnemyId = -1;
            for (int step = 0;
                 step < 30 && hitEnemyId < 0;
                 step++)
            {
                simulation.Step();
                SimulationEventBuffer events =
                    simulation.ReadPresentationEvents();
                for (int eventIndex = 0;
                     eventIndex < events.Count;
                     eventIndex++)
                {
                    if (events[eventIndex].Type ==
                        PresentationEventType.ProjectileHit)
                    {
                        hitEnemyId = events[eventIndex].SubjectId;
                        break;
                    }
                }
            }

            Assert.That(hitEnemyId, Is.GreaterThanOrEqualTo(0));
            EnemySnapshot[] enemies =
                simulation.GetSnapshot().Enemies;
            Assert.That(enemies, Has.Length.EqualTo(2));
            Array.Sort(
                enemies,
                (left, right) => left.Id.CompareTo(right.Id));
            EnemySnapshot firstSpawned = enemies[0];
            EnemySnapshot rearTarget = enemies[1];

            Assert.That(hitEnemyId, Is.EqualTo(rearTarget.Id),
                "The projectile must encounter the rear enemy first.");
            Assert.That(
                rearTarget.PathProgressMilli,
                Is.GreaterThan(firstSpawned.PathProgressMilli),
                "Forward projectile knockback must project along the path.");
            Assert.That(
                firstSpawned.HealthMilli,
                Is.LessThan(firstSpawned.MaxHealthMilli),
                "The enemy crossed before the final position must receive collision damage.");
        }

        [Test]
        public void KnockbackSweep_DetectsSelfIntersectingPathContact()
        {
            var selfIntersectingPath = new PathModel(
                new[]
                {
                    new SimPosition(0, 0),
                    new SimPosition(5000, 0),
                    new SimPosition(0, 0),
                    new SimPosition(0, 5000)
                });
            SimPosition enemyOnDistantBranch =
                selfIntersectingPath.GetPosition(10000);

            Assert.That(
                selfIntersectingPath.TryGetSweepContactDistance(
                    enemyOnDistantBranch,
                    1000,
                    0,
                    100,
                    out long travelDistance),
                Is.True);
            Assert.That(
                travelDistance,
                Is.EqualTo(900),
                "Contact distance must stop at the radius boundary, not the center.");
        }

        [Test]
        public void KnockbackSweep_OrdersCandidatesByFirstRadiusContact()
        {
            var straightPath = new PathModel(
                new[]
                {
                    new SimPosition(0, 0),
                    new SimPosition(10000, 0)
                });

            Assert.That(
                straightPath.TryGetSweepContactDistance(
                    new SimPosition(5000, 0),
                    0,
                    10000,
                    100,
                    out long nearCenterContact),
                Is.True);
            Assert.That(
                straightPath.TryGetSweepContactDistance(
                    new SimPosition(5200, 0),
                    0,
                    10000,
                    500,
                    out long enlargedContact),
                Is.True);

            Assert.That(nearCenterContact, Is.EqualTo(4900));
            Assert.That(enlargedContact, Is.EqualTo(4700));
            Assert.That(
                enlargedContact,
                Is.LessThan(nearCenterContact),
                "The larger farther enemy must be contacted first.");
        }

        [Test]
        public void CompiledContent_PublicArraysCannotMutateTheSimulationSource()
        {
            ulong fingerprint = content.ContentHash;
            CompiledCardDefinition[] cards = content.Cards;
            cards[0] = null;

            string[] tags = content.GetCard(new CardId(0)).Tags;
            string originalTag = tags[0];
            tags[0] = "mutated";

            SimPosition[] path = content.Run.PathPoints;
            path[0] = SimPosition.FromMilliUnits(999999, 999999);

            int[] unlockCosts = content.Run.BuildSpotUnlockCosts;
            unlockCosts[5] = 999999;

            Assert.That(content.Cards[0], Is.Not.Null);
            Assert.That(
                content.GetCard(new CardId(0)).Tags[0],
                Is.EqualTo(originalTag));
            Assert.That(
                content.Run.PathPoints[0],
                Is.Not.EqualTo(path[0]));
            Assert.That(content.Run.BuildSpotUnlockCosts[5], Is.Zero);
            Assert.That(content.ContentHash, Is.EqualTo(fingerprint));
        }

        [Test]
        public void BuildSpotUnlockCosts_AffectContentAndRunFingerprints()
        {
            ContentCatalogDto changedSource = LoadPhaseOneDto();
            changedSource.run.buildSpotUnlockCosts[5]++;
            CompiledContent changed = Compile(changedSource);

            Assert.That(changed.ContentHash, Is.Not.EqualTo(content.ContentHash));

            var first = new GameSimulation();
            first.Initialize(content, new RunConfig(content.Run), 41UL);
            var second = new GameSimulation();
            second.Initialize(content, new RunConfig(changed.Run), 41UL);

            Assert.That(
                first.ComputeStateHash(),
                Is.Not.EqualTo(second.ComputeStateHash()));
        }

        [Test]
        public void ContentCompiler_RejectsInvalidBuildSpotUnlockCosts()
        {
            ContentCatalogDto wrongLength = LoadPhaseOneDto();
            wrongLength.run.buildSpotUnlockCosts = new int[7];
            Assert.Throws<ContentValidationException>(
                () => Compile(wrongLength));

            ContentCatalogDto negativeCost = LoadPhaseOneDto();
            negativeCost.run.buildSpotUnlockCosts[5] = -1;
            Assert.Throws<ContentValidationException>(
                () => Compile(negativeCost));
        }

        [Test]
        public void ContentCompiler_RejectsUnsafeSpatialValues()
        {
            ContentCatalogDto source = LoadPhaseOneDto();
            source.cards[0].projectileEffects[0].radiusMilli = int.MaxValue;

            Assert.Throws<ContentValidationException>(
                () => Compile(source));
        }

        [Test]
        public void BurnTicksAtTheConfiguredInterval()
        {
            CompiledContent testContent = CompileCustomized(source =>
            {
                ConfigureSingleWave(source, "raider", 1, 1);
                source.run.startingTowerChoices =
                    new[] { "mutation_obelisk" };
                source.run.initiallyUnlockedTowers =
                    Array.Empty<string>();
                source.run.startingCards = new[] { "burn" };
            });
            GameSimulation simulation = CreateCombatSimulation(
                testContent,
                "mutation_obelisk",
                new[] { "burn" },
                101UL);

            simulation.Step();
            EnemySnapshot applied = simulation.GetSnapshot().Enemies[0];
            Assert.That(applied.HealthMilli, Is.EqualTo(30000));
            CollectionAssert.Contains(applied.Statuses, StatusType.Burn);

            for (int step = 0; step < 15; step++)
            {
                simulation.Step();
            }

            EnemySnapshot ticked = simulation.GetSnapshot().Enemies[0];
            Assert.That(ticked.HealthMilli, Is.EqualTo(29500));
        }

        [Test]
        public void SlowStacksMultiplicativelyAndStopsAtSixtyPercent()
        {
            CompiledContent testContent = CompileCustomized(source =>
            {
                ConfigureSingleWave(source, "raider", 1, 1);
                source.run.startingTowerChoices =
                    new[] { "mutation_obelisk" };
                source.run.initiallyUnlockedTowers =
                    Array.Empty<string>();
                source.run.startingCards =
                    new[] { "slow", "slow", "slow" };
                FindCardDto(source, "slow")
                    .enemyEffects[0].amount = 5000;
            });
            GameSimulation simulation = CreateCombatSimulation(
                testContent,
                "mutation_obelisk",
                new[] { "slow", "slow", "slow" },
                102UL);

            simulation.Step();
            EnemySnapshot enemy = simulation.GetSnapshot().Enemies[0];
            Assert.That(enemy.StatusDetails, Has.Length.EqualTo(1));
            Assert.That(enemy.StatusDetails[0].Stacks, Is.EqualTo(3));
            Assert.That(enemy.SlowBps, Is.EqualTo(6000));
        }

        [Test]
        public void EliteStunBuildsControlGaugeAndRaisesResistanceThreshold()
        {
            CompiledContent testContent = CompileCustomized(source =>
            {
                ConfigureSingleWave(source, "elite_golem", 1, 1);
                source.run.startingTowerChoices =
                    new[] { "mutation_obelisk" };
                source.run.initiallyUnlockedTowers =
                    Array.Empty<string>();
                source.run.startingCards = new[] { "stun" };
                FindCardDto(source, "stun")
                    .enemyEffects[0].amount = 100;
            });
            GameSimulation simulation = CreateCombatSimulation(
                testContent,
                "mutation_obelisk",
                new[] { "stun" },
                103UL);

            simulation.Step();
            EnemySnapshot enemy = simulation.GetSnapshot().Enemies[0];
            Assert.That(enemy.ControlGauge, Is.EqualTo(0));
            Assert.That(enemy.ControlThreshold, Is.EqualTo(125));
            CollectionAssert.Contains(enemy.Statuses, StatusType.Stun);
        }

        [Test]
        public void PiercedStatusIncreasesDamageAgainstArmor()
        {
            int baselineDamage =
                GetFirstArmoredDamageWithMutationCard("slow");
            int piercedDamage =
                GetFirstArmoredDamageWithMutationCard("pierce");

            Assert.That(piercedDamage, Is.GreaterThan(baselineDamage));
        }

        [Test]
        public void ProjectilePiercingConsumesTheGlobalLimit()
        {
            CompiledContent testContent = CompileCustomized(source =>
            {
                ConfigureSingleWave(source, "raider", 6, 1);
                source.safety.maxProjectilePierces = 2;
                source.run.startingTowerChoices =
                    new[] { "ballista" };
                source.run.initiallyUnlockedTowers =
                    Array.Empty<string>();
                source.run.startingCards = new[] { "pierce" };
                source.run.buildSpotXMilli[0] = -1000;
                source.run.buildSpotYMilli[0] = 0;
                TowerDefinitionDto ballista =
                    FindTowerDto(source, "ballista");
                ballista.cooldownTicks = 1000;
                ballista.baseDamageMilli = 1000;
                EnemyDefinitionDto raider =
                    FindEnemyDto(source, "raider");
                raider.maxHealthMilli = 1000000;
                raider.speedMilliPerTick = 1;
                FindCardDto(source, "pierce")
                    .projectileEffects[0].amount = 100;
            });
            GameSimulation simulation = CreateCombatSimulation(
                testContent,
                "ballista",
                new[] { "pierce" },
                104UL);

            int hitCount = 0;
            for (int step = 0; step < 30; step++)
            {
                simulation.Step();
                SimulationEventBuffer events =
                    simulation.ReadPresentationEvents();
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Type ==
                        PresentationEventType.ProjectileHit)
                    {
                        hitCount++;
                    }
                }
            }

            Assert.That(hitCount, Is.EqualTo(3),
                "One initial hit plus two globally capped pierces are expected.");
        }

        [Test]
        public void DeathEngine_ExecutesTheSameOrderedCardsOnNearbyEnemies()
        {
            CompiledContent testContent = CompileCustomized(source =>
            {
                ConfigureSingleWave(source, "raider", 4, 1);
                source.run.startingTowerChoices =
                    new[] { "ballista" };
                source.run.initiallyUnlockedTowers =
                    new[] { "death_engine" };
                source.run.startingCards =
                    new[] { "split", "burn", "explode" };
                source.run.startingGold = 100;
                CoLocateFirstTwoBuildSpots(source);
                TowerDefinitionDto ballista =
                    FindTowerDto(source, "ballista");
                ballista.baseDamageMilli = 100000;
                ballista.cooldownTicks = 30;
                FindEnemyDto(source, "raider")
                    .speedMilliPerTick = 1;
            });

            var simulation = new GameSimulation();
            simulation.Initialize(testContent, 105UL);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower("ballista")),
                "choose ballista");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower("ballista", 0)),
                "place ballista");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower("death_engine", 1)),
                "place death engine");
            TowerSnapshot deathEngine = FindTowerSnapshot(
                simulation.GetSnapshot(),
                "death_engine");
            EquipProgram(
                simulation,
                testContent,
                deathEngine.Id,
                new[] { "split", "burn", "explode" });
            AssertAccepted(
                simulation.Submit(GameCommand.StartWave()),
                "start death engine scenario");

            var executed = new HashSet<string>(StringComparer.Ordinal);
            for (int step = 0; step < 120; step++)
            {
                simulation.Step();
                SimulationEventBuffer events =
                    simulation.ReadPresentationEvents();
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Type ==
                            PresentationEventType.CardExecuted &&
                        events[i].SourceId == deathEngine.Id)
                    {
                        executed.Add(events[i].ContentId);
                    }
                }
            }

            CollectionAssert.IsSubsetOf(
                new[] { "split", "burn", "explode" },
                executed);
        }

        [Test]
        public void SplitLineage_PreservesAndDeduplicatesRewardBudget()
        {
            CompiledContent testContent = CompileCustomized(source =>
            {
                ConfigureSingleWave(source, "raider", 1, 1);
                source.run.startingTowerChoices =
                    new[] { "mutation_obelisk" };
                source.run.initiallyUnlockedTowers =
                    new[] { "ballista" };
                source.run.startingCards =
                    new[] { "split", "gold_bounty", "explode" };
                source.run.startingGold = 100;
                CoLocateFirstTwoBuildSpots(source);
                TowerDefinitionDto ballista =
                    FindTowerDto(source, "ballista");
                ballista.baseDamageMilli = 100000;
                ballista.cooldownTicks = 1;
                FindEnemyDto(source, "raider")
                    .speedMilliPerTick = 1;
            });

            var simulation = new GameSimulation();
            simulation.Initialize(testContent, 106UL);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower(
                        "mutation_obelisk")),
                "choose mutation obelisk");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower(
                        "mutation_obelisk",
                        0)),
                "place mutation obelisk");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower("ballista", 1)),
                "place supporting ballista");
            TowerSnapshot mutation = FindTowerSnapshot(
                simulation.GetSnapshot(),
                "mutation_obelisk");
            EquipProgram(
                simulation,
                testContent,
                mutation.Id,
                new[] { "split", "gold_bounty", "explode" });
            AssertAccepted(
                simulation.Submit(GameCommand.StartWave()),
                "start reward scenario");

            StepUntilTerminal(simulation, 1000);
            Assert.That(simulation.Phase, Is.EqualTo(RunPhase.Victory));
            LineageSnapshot[] lineages =
                simulation.GetSnapshot().Lineages;
            Assert.That(lineages, Has.Length.EqualTo(1));
            LineageSnapshot lineage = lineages[0];
            Assert.That(lineage.SpawnedEntityCount, Is.EqualTo(2));
            Assert.That(lineage.HighestGeneration, Is.EqualTo(1));
            Assert.That(lineage.RewardAugmentCount, Is.EqualTo(1));
            Assert.That(lineage.BaseRewardBudget, Is.EqualTo(5));
            Assert.That(lineage.MaxRewardBudget, Is.EqualTo(6));
            Assert.That(lineage.PaidReward, Is.EqualTo(6));
            Assert.That(lineage.ForfeitedReward, Is.EqualTo(0));
            Assert.That(lineage.LiveMembers, Is.EqualTo(0));
        }

        [Test]
        public void CardBountyRewardKeepsItsNonTriggeringOrigin()
        {
            CompiledContent testContent = CompileCustomized(source =>
            {
                ConfigureSingleWave(source, "raider", 1, 1);
                source.run.startingTowerChoices =
                    new[] { "ballista" };
                source.run.initiallyUnlockedTowers =
                    Array.Empty<string>();
                source.run.startingCards =
                    new[] { "gold_bounty" };
                source.run.buildSpotXMilli[0] = -1000;
                source.run.buildSpotYMilli[0] = 0;
                TowerDefinitionDto ballista =
                    FindTowerDto(source, "ballista");
                ballista.baseDamageMilli = 1000;
                ballista.cooldownTicks = 1000;
                EnemyDefinitionDto raider =
                    FindEnemyDto(source, "raider");
                raider.maxHealthMilli = 1000000;
                raider.speedMilliPerTick = 1;
            });
            GameSimulation simulation = CreateCombatSimulation(
                testContent,
                "ballista",
                new[] { "gold_bounty" },
                107UL);

            int bountyEvents = 0;
            for (int step = 0; step < 30; step++)
            {
                simulation.Step();
                SimulationEventBuffer events =
                    simulation.ReadPresentationEvents();
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Type ==
                            PresentationEventType.RewardGranted &&
                        events[i].ContentId == "CardBounty")
                    {
                        bountyEvents++;
                    }
                }
            }

            Assert.That(bountyEvents, Is.EqualTo(1));
            Assert.That(simulation.Gold, Is.EqualTo(1));
        }

        [Test]
        public void SplitRejectsAtomicallyWhenContinuationBudgetCannotFit()
        {
            CompiledContent testContent = CompileCustomized(source =>
            {
                ConfigureSingleWave(source, "raider", 1, 1);
                source.run.startingTowerChoices =
                    new[] { "mutation_obelisk" };
                source.run.initiallyUnlockedTowers =
                    Array.Empty<string>();
                source.run.startingCards =
                    new[] { "split", "burn", "explode" };
                source.safety.maxEventsPerChain = 3;
            });
            GameSimulation simulation = CreateCombatSimulation(
                testContent,
                "mutation_obelisk",
                new[] { "split", "burn", "explode" },
                108UL);

            simulation.Step();
            EnemySnapshot[] enemies =
                simulation.GetSnapshot().Enemies;
            Assert.That(enemies, Has.Length.EqualTo(1));
            CollectionAssert.Contains(
                enemies[0].Statuses,
                StatusType.Burn);
            Assert.That(enemies[0].DeathBindingCount, Is.EqualTo(1));
            Assert.That(simulation.Diagnostics.Count, Is.EqualTo(1));
            Assert.That(
                simulation.Diagnostics[0].Code,
                Is.EqualTo(
                    DiagnosticCode.ChainEventBudgetExceeded));
        }

        [Test]
        [Timeout(30000)]
        public void LegalCardPrograms_FuzzWithoutOverflowOrDuplicateDeath()
        {
            string[] allCardIds =
            {
                "split", "pierce", "burn", "slow",
                "explode", "knockback", "mark", "gold_bounty",
                "poison", "enlarge", "shrink", "stun"
            };
            CompiledContent fuzzContent = CompileCustomized(source =>
            {
                source.run.startingTowerChoices =
                    new[] { "ballista" };
                source.run.initiallyUnlockedTowers =
                    Array.Empty<string>();
                source.run.startingCards = allCardIds;
            });
            var random = new System.Random(0x51A7);
            int totalExecutedSteps = 0;

            for (int runIndex = 0; runIndex < 40; runIndex++)
            {
                string[] program = PickLegalProgram(
                    fuzzContent,
                    allCardIds,
                    random);
                GameSimulation simulation = CreateCombatSimulation(
                    fuzzContent,
                    "ballista",
                    program,
                    (ulong)(5000 + runIndex));
                var deadEntityIds = new HashSet<int>();

                for (int step = 0; step < 750; step++)
                {
                    if (simulation.Phase == RunPhase.Draft)
                    {
                        AssertAccepted(
                            simulation.Submit(
                                GameCommand.SelectDraft(0)),
                            "select fuzz draft");
                    }
                    if (simulation.Phase == RunPhase.CardPackChoice)
                    {
                        AssertAccepted(
                            simulation.Submit(
                                GameCommand.SelectCardPack(0)),
                            "select fuzz card pack");
                    }
                    if (simulation.Phase == RunPhase.CardPackLoadout)
                    {
                        AssertAccepted(
                            simulation.Submit(
                                GameCommand.ResumeCardPackCombat()),
                            "finish fuzz card pack");
                    }
                    if (simulation.Phase == RunPhase.Planning)
                    {
                        AssertAccepted(
                            simulation.Submit(
                                GameCommand.StartWave()),
                            "start fuzz wave");
                    }
                    if (simulation.Phase == RunPhase.Victory ||
                        simulation.Phase == RunPhase.Defeat)
                    {
                        break;
                    }

                    simulation.Step();
                    totalExecutedSteps++;
                    SimulationEventBuffer events =
                        simulation.ReadPresentationEvents();
                    for (int eventIndex = 0;
                         eventIndex < events.Count;
                         eventIndex++)
                    {
                        if (events[eventIndex].Type ==
                            PresentationEventType.EnemyDied)
                        {
                            Assert.That(
                                deadEntityIds.Add(
                                    events[eventIndex].SubjectId),
                                Is.True,
                                "An enemy died twice in fuzz run {0}.",
                                runIndex);
                        }
                    }

                    SimulationSnapshot snapshot =
                        simulation.GetSnapshot();
                    Assert.That(snapshot.Gold, Is.GreaterThanOrEqualTo(0));
                    Assert.That(
                        snapshot.BaseHealth,
                        Is.GreaterThanOrEqualTo(0));
                    for (int enemyIndex = 0;
                         enemyIndex < snapshot.Enemies.Length;
                         enemyIndex++)
                    {
                        Assert.That(
                            snapshot.Enemies[enemyIndex].HealthMilli,
                            Is.GreaterThanOrEqualTo(0));
                        Assert.That(
                            snapshot.Enemies[enemyIndex].RewardBudget,
                            Is.GreaterThanOrEqualTo(0));
                    }
                    for (int lineageIndex = 0;
                         lineageIndex < snapshot.Lineages.Length;
                         lineageIndex++)
                    {
                        LineageSnapshot lineage =
                            snapshot.Lineages[lineageIndex];
                        Assert.That(
                            lineage.PaidReward,
                            Is.GreaterThanOrEqualTo(0));
                        Assert.That(
                            lineage.ForfeitedReward,
                            Is.GreaterThanOrEqualTo(0));
                        Assert.That(
                            lineage.PaidReward +
                            lineage.ForfeitedReward,
                            Is.LessThanOrEqualTo(
                                lineage.MaxRewardBudget));
                    }
                }
            }

            Assert.That(
                totalExecutedSteps,
                Is.GreaterThanOrEqualTo(20000),
                "Fuzz coverage must execute at least twenty thousand ticks.");
        }

        [Test]
        [Timeout(30000)]
        public void PhaseOneHeadlessRun_TerminatesWithinBudget()
        {
            GameSimulation simulation =
                CreatePhaseOneHeadlessSimulation(0x5EEDUL);
            const int maximumSteps = 60000;
            int steps = 0;

            while (steps < maximumSteps &&
                   simulation.Phase != RunPhase.Victory &&
                   simulation.Phase != RunPhase.Defeat)
            {
                if (simulation.Phase == RunPhase.Draft)
                {
                    SimulationSnapshot offerSnapshot =
                        simulation.GetSnapshot();
                    AssertAccepted(
                        simulation.Submit(
                            GameCommand.SelectDraft(
                                SelectHeadlessOffer(
                                    content,
                                    offerSnapshot.DraftOffers))),
                        "select a damaging draft offer");
                }

                if (simulation.Phase == RunPhase.CardPackChoice)
                {
                    SimulationSnapshot offerSnapshot =
                        simulation.GetSnapshot();
                    AssertAccepted(
                        simulation.Submit(
                            GameCommand.SelectCardPack(
                                SelectHeadlessOffer(
                                    content,
                                    offerSnapshot.CardPackOffers))),
                        "select a damaging card-pack offer");
                }

                if (simulation.Phase == RunPhase.CardPackLoadout)
                {
                    AssertAccepted(
                        simulation.Submit(
                            GameCommand.ResumeCardPackCombat()),
                        "finish the wave-end card pack");
                }

                if (simulation.Phase == RunPhase.Planning)
                {
                    TryBuildAffordableHeadlessTower(simulation);
                    EquipAvailableHeadlessCards(simulation);
                    AssertAccepted(
                        simulation.Submit(GameCommand.StartWave()),
                        "start the next wave");
                }

                simulation.Step();
                steps++;

                SimulationEventBuffer events = simulation.ReadPresentationEvents();
                Assert.That(
                    events.Count,
                    Is.LessThanOrEqualTo(content.Safety.MaxEventsPerTick),
                    "Presentation event count exceeded the per-tick safety budget.");
                for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
                {
                    Assert.That(
                        events[eventIndex].Type,
                        Is.Not.EqualTo(PresentationEventType.SafetyLimitReached),
                        "A safety limit was reached at tick {0}: {1}.",
                        events[eventIndex].Tick,
                        events[eventIndex].ContentId);
                }
            }

            Assert.That(
                simulation.Phase,
                Is.EqualTo(RunPhase.Victory),
                "The headless run failed within {0} steps at wave {1} with base health {2}, gold {3}, {4} towers, and cards [{5}].",
                maximumSteps,
                simulation.GetSnapshot().WaveIndex + 1,
                simulation.GetSnapshot().BaseHealth,
                simulation.GetSnapshot().Gold,
                simulation.GetSnapshot().Towers.Length,
                DescribeHeadlessCards(simulation, content));
            SimulationSnapshot finalSnapshot =
                simulation.GetSnapshot();
            Assert.That(
                finalSnapshot.Cards,
                Has.Length.EqualTo(12),
                "4 starting + 3 drafts + 2 boss packs + 3 carrier packs are expected.");
            Assert.That(
                finalSnapshot.NextCardPackThreshold,
                Is.EqualTo(1800000),
                "The fourth carrier threshold must remain unreached.");
            Assert.That(simulation.Diagnostics.Count, Is.EqualTo(0),
                "A normal Phase 1 run should not consume a safety budget.");
        }

        private static CompiledContent LoadPhaseOneContent()
        {
            return Compile(LoadPhaseOneDto());
        }

        private static ContentCatalogDto LoadPhaseOneDto()
        {
            TextAsset contentAsset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ContentAssetPath);
            Assert.That(contentAsset, Is.Not.Null,
                "Missing Phase 1 content asset at '{0}'.", ContentAssetPath);

            ContentCatalogDto source =
                JsonUtility.FromJson<ContentCatalogDto>(contentAsset.text);
            Assert.That(source, Is.Not.Null, "Phase 1 JSON could not be deserialized.");
            return source;
        }

        private static CompiledContent Compile(
            ContentCatalogDto source)
        {
            return ContentCompiler.Compile(
                source,
                GameSimulation.IsEffectOperationSupported);
        }

        private static CompiledContent CompileCustomized(
            Action<ContentCatalogDto> configure)
        {
            ContentCatalogDto source = LoadPhaseOneDto();
            configure(source);
            return Compile(source);
        }

        private void AssertTower(
            string stableId,
            TowerTrigger expectedTrigger,
            SubjectTypeMode expectedSubjectType,
            SubjectSelector expectedSelector,
            int expectedAttackWindupTicks)
        {
            Assert.That(
                content.TryGetTowerId(stableId, out TowerDefinitionId towerId),
                Is.True,
                "Missing tower '{0}'.",
                stableId);
            CompiledTowerDefinition tower = content.GetTower(towerId);
            Assert.That(tower.Trigger, Is.EqualTo(expectedTrigger));
            Assert.That(tower.SubjectTypeMode, Is.EqualTo(expectedSubjectType));
            Assert.That(tower.Selector, Is.EqualTo(expectedSelector));
            Assert.That(
                tower.AttackWindupTicks,
                Is.EqualTo(expectedAttackWindupTicks));
            Assert.That(tower.SlotCount, Is.GreaterThan(0));
            Assert.That(tower.ComputeCapacity, Is.GreaterThan(0));
        }

        private CompiledEnemyDefinition GetEnemyDefinition(
            string stableId)
        {
            Assert.That(
                content.TryGetEnemyId(
                    stableId,
                    out EnemyDefinitionId enemyId),
                Is.True,
                "Missing enemy '{0}'.",
                stableId);
            return content.GetEnemy(enemyId);
        }

        private static void AssertInterpretationIsExecutable(
            string cardId,
            string interpretation,
            CompiledEffectNode[] effects)
        {
            Assert.That(effects, Is.Not.Null.And.Not.Empty,
                "Card '{0}' has no {1} interpretation.", cardId, interpretation);
            for (int effectIndex = 0; effectIndex < effects.Length; effectIndex++)
            {
                Assert.That(
                    GameSimulation.IsEffectOperationSupported(
                        effects[effectIndex].Operation),
                    Is.True,
                    "Card '{0}' has an unregistered {1} operation '{2}'.",
                    cardId,
                    interpretation,
                    effects[effectIndex].Operation);
            }
        }

        private GameSimulation CreateCombatSimulation(
            string towerStableId,
            string[] orderedCardStableIds,
            ulong seed)
        {
            return CreateCombatSimulation(
                content,
                towerStableId,
                orderedCardStableIds,
                seed);
        }

        private static GameSimulation CreateCombatSimulation(
            CompiledContent simulationContent,
            string towerStableId,
            string[] orderedCardStableIds,
            ulong seed)
        {
            var simulation = new GameSimulation();
            simulation.Initialize(simulationContent, seed);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower(towerStableId)),
                "choose starting tower");
            AssertAccepted(
                simulation.Submit(GameCommand.PlaceTower(towerStableId, 0)),
                "place starting tower");

            SimulationSnapshot snapshot = simulation.GetSnapshot();
            Assert.That(snapshot.Towers, Has.Length.EqualTo(1));
            int towerInstanceId = snapshot.Towers[0].Id;
            EquipProgram(
                simulation,
                simulationContent,
                towerInstanceId,
                orderedCardStableIds);

            AssertAccepted(
                simulation.Submit(GameCommand.StartWave()),
                "start first wave");
            Assert.That(simulation.Phase, Is.EqualTo(RunPhase.Combat));
            return simulation;
        }

        private GameSimulation CreatePhaseOneHeadlessSimulation(
            ulong seed)
        {
            var simulation = new GameSimulation();
            simulation.Initialize(content, seed);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower("ballista")),
                "choose headless ballista");

            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower("ballista", 0)),
                "place free headless ballista");

            EquipProgram(
                simulation,
                content,
                simulation.GetSnapshot().Towers[0].Id,
                new[] { "split", "burn", "explode" });
            AssertAccepted(
                simulation.Submit(GameCommand.StartWave()),
                "start headless wave");
            return simulation;
        }

        private static void TryBuildAffordableHeadlessTower(
            GameSimulation simulation)
        {
            while (true)
            {
                SimulationSnapshot snapshot =
                    simulation.GetSnapshot();
                if (snapshot.Gold <
                        snapshot.TowerConstructionCost ||
                    snapshot.Towers.Length >=
                        snapshot.BuildSpots.Length)
                {
                    return;
                }

                bool placed = false;
                for (int buildPointIndex = 0;
                     buildPointIndex < snapshot.BuildSpots.Length;
                     buildPointIndex++)
                {
                    bool occupied = false;
                    for (int towerIndex = 0;
                         towerIndex < snapshot.Towers.Length;
                         towerIndex++)
                    {
                        if (snapshot.Towers[towerIndex].
                            BuildPointIndex == buildPointIndex)
                        {
                            occupied = true;
                            break;
                        }
                    }

                    if (!occupied)
                    {
                        AssertAccepted(
                            simulation.Submit(
                                GameCommand.PlaceTower(
                                    "ballista",
                                    buildPointIndex)),
                            "place funded headless ballista");
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                {
                    return;
                }
            }
        }

        private static void EquipAvailableHeadlessCards(
            GameSimulation simulation)
        {
            while (true)
            {
                SimulationSnapshot snapshot =
                    simulation.GetSnapshot();
                bool equippedOne = false;
                for (int cardIndex = 0;
                     cardIndex < snapshot.Cards.Length &&
                     !equippedOne;
                     cardIndex++)
                {
                    CardInstanceSnapshot card =
                        snapshot.Cards[cardIndex];
                    if (card.Equipped)
                    {
                        continue;
                    }

                    for (int towerIndex = 0;
                         towerIndex < snapshot.Towers.Length &&
                         !equippedOne;
                         towerIndex++)
                    {
                        TowerSnapshot tower =
                            snapshot.Towers[towerIndex];
                        for (int slot = 0;
                             slot < tower.CardInstanceIds.Length;
                             slot++)
                        {
                            if (tower.CardInstanceIds[slot] >= 0)
                            {
                                continue;
                            }

                            CommandResult result =
                                simulation.Submit(
                                    GameCommand.EquipCard(
                                        card.Id,
                                        tower.Id,
                                        slot));
                            if (result.Accepted)
                            {
                                equippedOne = true;
                                break;
                            }
                        }
                    }
                }

                if (!equippedOne)
                {
                    return;
                }
            }
        }

        private static string DescribeHeadlessCards(
            GameSimulation simulation,
            CompiledContent content)
        {
            SimulationSnapshot snapshot =
                simulation.GetSnapshot();
            var descriptions =
                new string[snapshot.Cards.Length];
            for (int i = 0; i < snapshot.Cards.Length; i++)
            {
                CardInstanceSnapshot card = snapshot.Cards[i];
                descriptions[i] =
                    content.GetCard(card.DefinitionId).StableId +
                    (card.Equipped
                        ? "@" + card.TowerId +
                          ":" + card.Slot
                        : "@inventory");
            }
            return string.Join(", ", descriptions);
        }

        private static int SelectHeadlessOffer(
            CompiledContent content,
            CardId[] offers)
        {
            int selectedIndex = 0;
            int selectedScore = int.MinValue;
            for (int i = 0; i < offers.Length; i++)
            {
                string stableId =
                    content.GetCard(offers[i]).StableId;
                int score;
                switch (stableId)
                {
                    case "split":
                        score = 120;
                        break;
                    case "burn":
                    case "poison":
                        score = 110;
                        break;
                    case "explode":
                    case "pulse":
                    case "shock":
                        score = 100;
                        break;
                    case "ricochet":
                    case "afterimage":
                    case "orbit":
                        score = 90;
                        break;
                    case "bleed":
                    case "accelerate":
                    case "corrosion":
                        score = 80;
                        break;
                    case "pierce":
                    case "enlarge":
                    case "shrink":
                        score = 70;
                        break;
                    default:
                        score = 0;
                        break;
                }

                if (score > selectedScore)
                {
                    selectedScore = score;
                    selectedIndex = i;
                }
            }
            return selectedIndex;
        }

        private int FindCardInstanceId(
            GameSimulation simulation,
            string stableId)
        {
            SimulationSnapshot snapshot = simulation.GetSnapshot();
            for (int cardIndex = 0; cardIndex < snapshot.Cards.Length; cardIndex++)
            {
                CardInstanceSnapshot card = snapshot.Cards[cardIndex];
                if (content.GetCard(card.DefinitionId).StableId == stableId)
                {
                    return card.Id;
                }
            }

            Assert.Fail("No owned card instance exists for '{0}'.", stableId);
            return -1;
        }

        private static int FindUnequippedCardInstanceId(
            GameSimulation simulation,
            CompiledContent simulationContent,
            string stableId)
        {
            SimulationSnapshot snapshot = simulation.GetSnapshot();
            for (int cardIndex = 0;
                 cardIndex < snapshot.Cards.Length;
                 cardIndex++)
            {
                CardInstanceSnapshot card = snapshot.Cards[cardIndex];
                if (!card.Equipped &&
                    simulationContent.GetCard(
                        card.DefinitionId).StableId == stableId)
                {
                    return card.Id;
                }
            }

            Assert.Fail(
                "No unequipped card instance exists for '{0}'.",
                stableId);
            return -1;
        }

        private static void EquipProgram(
            GameSimulation simulation,
            CompiledContent simulationContent,
            int towerInstanceId,
            string[] orderedCardStableIds)
        {
            while (true)
            {
                TowerSnapshot tower = FindTowerSnapshot(
                    simulation.GetSnapshot(),
                    towerInstanceId);
                int capacity =
                    GameSimulation
                        .GetTowerCardCapacityForLevel(
                            tower.Level);
                if (capacity >=
                    orderedCardStableIds.Length)
                {
                    break;
                }

                AssertAccepted(
                    simulation.Submit(
                        GameCommand.UpgradeTower(
                            towerInstanceId)),
                    "upgrade tower for requested program");
            }

            for (int slot = 0;
                 slot < orderedCardStableIds.Length;
                 slot++)
            {
                int cardInstanceId =
                    FindUnequippedCardInstanceId(
                        simulation,
                        simulationContent,
                        orderedCardStableIds[slot]);
                AssertAccepted(
                    simulation.Submit(
                        GameCommand.EquipCard(
                            cardInstanceId,
                            towerInstanceId,
                            slot)),
                    "equip card '" +
                    orderedCardStableIds[slot] + "'");
            }
        }

        private int[] GetFirstVolleyBindingCounts(string[] program)
        {
            GameSimulation simulation =
                CreateCombatSimulation("ballista", program, 23UL);
            ProjectileSnapshot[] projectiles =
                Array.Empty<ProjectileSnapshot>();
            for (int step = 0;
                 step <= 18 &&
                 projectiles.Length == 0;
                 step++)
            {
                simulation.Step();
                projectiles =
                    simulation.GetSnapshot().Projectiles;
            }

            Assert.That(projectiles, Has.Length.EqualTo(2));

            var result = new int[projectiles.Length];
            for (int projectileIndex = 0;
                 projectileIndex < projectiles.Length;
                 projectileIndex++)
            {
                result[projectileIndex] = projectiles[projectileIndex].BindingCount;
            }

            Array.Sort(result);
            return result;
        }

        private void AssertSplitRewardIsPreserved(
            EnemySnapshot[] splitEnemies,
            string enemyStableId)
        {
            Assert.That(
                content.TryGetEnemyId(
                    enemyStableId,
                    out EnemyDefinitionId enemyDefinitionId),
                Is.True);
            int rewardTotal = 0;
            int lineageId = splitEnemies[0].LineageId;
            for (int enemyIndex = 0; enemyIndex < splitEnemies.Length; enemyIndex++)
            {
                rewardTotal += splitEnemies[enemyIndex].RewardBudget;
                Assert.That(splitEnemies[enemyIndex].LineageId, Is.EqualTo(lineageId));
            }

            Assert.That(
                rewardTotal,
                Is.EqualTo(content.GetEnemy(enemyDefinitionId).RewardBudget));
        }

        private static EnemySnapshot FindNewestEnemy(EnemySnapshot[] enemies)
        {
            EnemySnapshot newest = enemies[0];
            for (int enemyIndex = 1; enemyIndex < enemies.Length; enemyIndex++)
            {
                if (enemies[enemyIndex].Id > newest.Id)
                {
                    newest = enemies[enemyIndex];
                }
            }

            return newest;
        }

        private static EnemyState FindEnemyState(
            GameSimulation simulation,
            int enemyId)
        {
            var field = typeof(GameSimulation).GetField(
                "enemies",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var states = (List<EnemyState>)field.GetValue(simulation);
            for (int index = 0; index < states.Count; index++)
            {
                if (states[index].Id.Value == enemyId)
                {
                    return states[index];
                }
            }

            Assert.Fail("Missing internal enemy state {0}.", enemyId);
            return null;
        }

        private static TowerState FindTowerState(
            GameSimulation simulation,
            int towerId)
        {
            var field = typeof(GameSimulation).GetField(
                "towers",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var states =
                (List<TowerState>)field.GetValue(simulation);
            for (int index = 0;
                 index < states.Count;
                 index++)
            {
                if (states[index].Id.Value == towerId)
                {
                    return states[index];
                }
            }

            Assert.Fail(
                "Missing internal tower state {0}.",
                towerId);
            return null;
        }

        private static void AssertStatusWasDeepCopied(
            StatusInstance original,
            StatusInstance clone)
        {
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.InstanceId, Is.Not.EqualTo(original.InstanceId));
            Assert.That(clone.Type, Is.EqualTo(original.Type));
            Assert.That(clone.SourceEntityId, Is.EqualTo(original.SourceEntityId));
            Assert.That(clone.SourceTowerId, Is.EqualTo(original.SourceTowerId));
            Assert.That(clone.SourceCardId, Is.EqualTo(original.SourceCardId));
            Assert.That(
                clone.SourceCardInstanceId,
                Is.EqualTo(original.SourceCardInstanceId));
            Assert.That(clone.Stacks, Is.EqualTo(original.Stacks));
            Assert.That(clone.Intensity, Is.EqualTo(original.Intensity));
            Assert.That(
                clone.RemainingTicks,
                Is.EqualTo(original.RemainingTicks));
            Assert.That(clone.MaxStacks, Is.EqualTo(original.MaxStacks));
            Assert.That(clone.TickInterval, Is.EqualTo(original.TickInterval));
            Assert.That(clone.NextTick, Is.EqualTo(original.NextTick));
            Assert.That(clone.Inherited, Is.EqualTo(original.Inherited));
            Assert.That(clone.Dispellable, Is.EqualTo(original.Dispellable));
            Assert.That(clone.Limit, Is.EqualTo(original.Limit));
            Assert.That(clone.RadiusMilli, Is.EqualTo(original.RadiusMilli));
            Assert.That(
                clone.ArmorIgnoreBps,
                Is.EqualTo(original.ArmorIgnoreBps));

            int originalStacks = original.Stacks;
            clone.Stacks++;
            Assert.That(original.Stacks, Is.EqualTo(originalStacks),
                "Changing the split child's status must not mutate the original.");
        }

        private int GetFirstArmoredDamageWithMutationCard(
            string cardStableId)
        {
            CompiledContent testContent = CompileCustomized(source =>
            {
                ConfigureSingleWave(
                    source,
                    "armored_knight",
                    1,
                    1);
                source.run.startingTowerChoices =
                    new[] { "mutation_obelisk" };
                source.run.initiallyUnlockedTowers =
                    new[] { "ballista" };
                source.run.startingCards =
                    new[] { cardStableId };
                source.run.startingGold = 100;
                CoLocateFirstTwoBuildSpots(source);
                FindEnemyDto(source, "armored_knight")
                    .speedMilliPerTick = 1;
            });

            var simulation = new GameSimulation();
            simulation.Initialize(testContent, 301UL);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower(
                        "mutation_obelisk")),
                "choose mutation obelisk");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower(
                        "mutation_obelisk",
                        0)),
                "place mutation obelisk");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower("ballista", 1)),
                "place ballista");
            TowerSnapshot mutation = FindTowerSnapshot(
                simulation.GetSnapshot(),
                "mutation_obelisk");
            EquipProgram(
                simulation,
                testContent,
                mutation.Id,
                new[] { cardStableId });
            AssertAccepted(
                simulation.Submit(GameCommand.StartWave()),
                "start armor scenario");

            for (int step = 0; step < 120; step++)
            {
                simulation.Step();
                SimulationEventBuffer events =
                    simulation.ReadPresentationEvents();
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Type ==
                        PresentationEventType.EnemyDamaged)
                    {
                        return events[i].Value;
                    }
                }
            }

            Assert.Fail(
                "No armored damage event was produced for '{0}'.",
                cardStableId);
            return 0;
        }

        private static void ConfigureSingleWave(
            ContentCatalogDto source,
            string enemyId,
            int count,
            int intervalTicks)
        {
            source.waves = new[]
            {
                new WaveDefinitionDto
                {
                    id = "wave_test",
                    spawns = new[]
                    {
                        new WaveSpawnDto
                        {
                            enemyId = enemyId,
                            count = count,
                            firstSpawnTick = 0,
                            intervalTicks = intervalTicks
                        }
                    }
                }
            };
            source.run.regularDraftWaveNumbers =
                Array.Empty<int>();
            source.run.bossCardPackWaveNumbers =
                Array.Empty<int>();
        }

        private static void CoLocateFirstTwoBuildSpots(
            ContentCatalogDto source)
        {
            source.run.buildSpotXMilli[1] =
                source.run.buildSpotXMilli[0];
            source.run.buildSpotYMilli[1] =
                source.run.buildSpotYMilli[0];
        }

        private static CardDefinitionDto FindCardDto(
            ContentCatalogDto source,
            string stableId)
        {
            for (int i = 0; i < source.cards.Length; i++)
            {
                if (source.cards[i].id == stableId)
                {
                    return source.cards[i];
                }
            }

            Assert.Fail("Missing card DTO '{0}'.", stableId);
            return null;
        }

        private static TowerDefinitionDto FindTowerDto(
            ContentCatalogDto source,
            string stableId)
        {
            for (int i = 0; i < source.towers.Length; i++)
            {
                if (source.towers[i].id == stableId)
                {
                    return source.towers[i];
                }
            }

            Assert.Fail("Missing tower DTO '{0}'.", stableId);
            return null;
        }

        private static EnemyDefinitionDto FindEnemyDto(
            ContentCatalogDto source,
            string stableId)
        {
            for (int i = 0; i < source.enemies.Length; i++)
            {
                if (source.enemies[i].id == stableId)
                {
                    return source.enemies[i];
                }
            }

            Assert.Fail("Missing enemy DTO '{0}'.", stableId);
            return null;
        }

        private static TowerSnapshot FindTowerSnapshot(
            SimulationSnapshot snapshot,
            string stableId)
        {
            for (int i = 0; i < snapshot.Towers.Length; i++)
            {
                if (snapshot.Towers[i].DefinitionId == stableId)
                {
                    return snapshot.Towers[i];
                }
            }

            Assert.Fail("Missing placed tower '{0}'.", stableId);
            return default(TowerSnapshot);
        }

        private static TowerSnapshot FindTowerSnapshot(
            SimulationSnapshot snapshot,
            int towerInstanceId)
        {
            for (int i = 0; i < snapshot.Towers.Length; i++)
            {
                if (snapshot.Towers[i].Id == towerInstanceId)
                {
                    return snapshot.Towers[i];
                }
            }

            Assert.Fail(
                "Missing placed tower instance {0}.",
                towerInstanceId);
            return default(TowerSnapshot);
        }

        private static void StepUntilTerminal(
            GameSimulation simulation,
            int maximumSteps)
        {
            for (int step = 0;
                 step < maximumSteps &&
                 simulation.Phase != RunPhase.Victory &&
                 simulation.Phase != RunPhase.Defeat;
                 step++)
            {
                simulation.Step();
            }
        }

        private static string[] PickLegalProgram(
            CompiledContent simulationContent,
            string[] cardIds,
            System.Random random)
        {
            var candidates = new List<string>(cardIds);
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int swapIndex = random.Next(i + 1);
                string swap = candidates[i];
                candidates[i] = candidates[swapIndex];
                candidates[swapIndex] = swap;
            }

            var selected = new List<string>(3);
            int compute = 0;
            for (int i = 0;
                 i < candidates.Count && selected.Count < 3;
                 i++)
            {
                simulationContent.TryGetCardId(
                    candidates[i],
                    out CardId cardId);
                int cardCompute =
                    simulationContent.GetCard(cardId).ComputeCost;
                if (compute + cardCompute > 5)
                {
                    continue;
                }

                selected.Add(candidates[i]);
                compute += cardCompute;
            }

            Assert.That(selected, Is.Not.Empty);
            return selected.ToArray();
        }

        private static void AssertAccepted(
            CommandResult result,
            string operation)
        {
            Assert.That(
                result.Accepted,
                Is.True,
                "Could not {0}: {1} ({2}).",
                operation,
                result.Message,
                result.Error);
        }
    }
}
