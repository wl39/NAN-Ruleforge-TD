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
            Assert.That(content.Cards, Has.Length.EqualTo(12));
            Assert.That(content.Towers, Has.Length.EqualTo(3));
            Assert.That(content.Waves, Has.Length.EqualTo(5));

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
                SubjectSelector.PrimaryProjectile);
            AssertTower(
                "mutation_obelisk",
                TowerTrigger.EnemyEnteredRange,
                SubjectTypeMode.Enemy,
                SubjectSelector.EnteringEnemy);
            AssertTower(
                "death_engine",
                TowerTrigger.EnemyDied,
                SubjectTypeMode.Enemy,
                SubjectSelector.EnemiesNearEvent);

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
            Assert.That(content.Safety.MaxEventsPerTick, Is.EqualTo(4096));
            Assert.That(content.Safety.MaxActiveHazards, Is.EqualTo(2048));
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
        public void MutationSplit_PreservesRewardAndContinuesWithoutPriorBindings()
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
            EnemySnapshot nonInheritedChild = FindNewestEnemy(inheritanceEnemies);
            CollectionAssert.DoesNotContain(nonInheritedChild.Statuses, StatusType.Burn);
            Assert.That(nonInheritedChild.DeathBindingCount, Is.EqualTo(1),
                "The split child must continue at explode without inheriting burn.");
        }

        [Test]
        public void EnemySplit_LimitsTheWholeLineageToTwoSplitOperations()
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
                source.run.buildSpotXMilli[0] = 1000;
                source.run.buildSpotYMilli[0] = 0;
                source.run.buildSpotXMilli[1] = 5000;
                source.run.buildSpotYMilli[1] = 0;
                FindTowerDto(source, "mutation_obelisk")
                    .rangeMilli = 500;
                FindEnemyDto(source, "raider")
                    .speedMilliPerTick = 1000;
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
                simulation.Diagnostics[0].Code,
                Is.EqualTo(
                    DiagnosticCode.EnemyLineageLimitReached));
        }

        [Test]
        public void LineageBudget_CountsSiblingSplitsAcrossTheWholeLineage()
        {
            var budget = new LineageBudget(
                new LineageId(1),
                content.Safety);

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
                Is.False);
            Assert.That(
                thirdFailure,
                Is.EqualTo(BudgetFailure.EnemySplitLimit));
            Assert.That(budget.SplitsUsed, Is.EqualTo(2));
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

            Assert.That(content.Cards[0], Is.Not.Null);
            Assert.That(
                content.GetCard(new CardId(0)).Tags[0],
                Is.EqualTo(originalTag));
            Assert.That(
                content.Run.PathPoints[0],
                Is.Not.EqualTo(path[0]));
            Assert.That(content.ContentHash, Is.EqualTo(fingerprint));
        }

        [Test]
        public void RunConfigFingerprint_DistinguishesDeterministicOverrides()
        {
            ContentCatalogDto changedSource = LoadPhaseOneDto();
            changedSource.run.pathPointXMilli[1]++;
            CompiledContent changed = Compile(changedSource);

            var first = new GameSimulation();
            first.Initialize(content, new RunConfig(content.Run), 41UL);
            var second = new GameSimulation();
            second.Initialize(content, new RunConfig(changed.Run), 41UL);

            Assert.That(
                first.ComputeStateHash(),
                Is.Not.EqualTo(second.ComputeStateHash()));
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
            const int maximumSteps = 30000;
            int steps = 0;

            while (steps < maximumSteps &&
                   simulation.Phase != RunPhase.Victory &&
                   simulation.Phase != RunPhase.Defeat)
            {
                if (simulation.Phase == RunPhase.Draft)
                {
                    AssertAccepted(
                        simulation.Submit(GameCommand.SelectDraft(0)),
                        "select the first draft offer");
                }

                if (simulation.Phase == RunPhase.Planning)
                {
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
                "The headless run did not terminate within {0} steps.",
                maximumSteps);
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
            SubjectSelector expectedSelector)
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
            Assert.That(tower.SlotCount, Is.GreaterThan(0));
            Assert.That(tower.ComputeCapacity, Is.GreaterThan(0));
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

            int[] buildPoints = { 0, 2, 3, 5 };
            for (int i = 0; i < buildPoints.Length; i++)
            {
                AssertAccepted(
                    simulation.Submit(
                        GameCommand.PlaceTower(
                            "ballista",
                            buildPoints[i])),
                    "place headless ballista");
            }

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
            simulation.Step();
            ProjectileSnapshot[] projectiles =
                simulation.GetSnapshot().Projectiles;
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
