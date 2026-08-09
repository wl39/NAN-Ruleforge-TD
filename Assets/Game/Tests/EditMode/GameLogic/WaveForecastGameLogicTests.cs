using System;
using NUnit.Framework;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Effects;
using RuleforgeTD.GameLogic.Simulation;
using UnityEditor;
using UnityEngine;

namespace RuleforgeTD.Tests.EditMode.GameLogic
{
    public sealed class WaveForecastGameLogicTests
    {
        private const string ContentAssetPath =
            "Assets/Game/Data/Logic/phase1-content.json";

        [Test]
        public void InitialForecastGroupsExactScheduledComposition()
        {
            CompiledContent content = Compile(LoadSource());
            var simulation = new GameSimulation();
            simulation.Initialize(content, 0xF0AEC45UL);

            WaveForecastSnapshot forecast =
                simulation.GetUpcomingWaveForecast();

            Assert.That(forecast.IsAvailable, Is.True);
            Assert.That(forecast.WaveIndex, Is.EqualTo(0));
            Assert.That(forecast.TotalCount, Is.EqualTo(35));
            Assert.That(forecast.Spawns, Has.Length.EqualTo(1));
            Assert.That(
                forecast.Spawns[0].EnemyId,
                Is.EqualTo("raider"));
            Assert.That(forecast.Spawns[0].Count, Is.EqualTo(35));
            Assert.That(
                forecast.Spawns[0].Stats.MaxHealthMilli,
                Is.EqualTo(15028));
            Assert.That(forecast.Spawns[0].Stats.Armor, Is.Zero);
            Assert.That(
                forecast.Spawns[0].Stats.SpeedMilliPerTick,
                Is.EqualTo(33));
        }

        [Test]
        public void EliteForecastStatsAndCountMatchActualSpawns()
        {
            ContentCatalogDto source = LoadSource();
            source.waves = new[]
            {
                new WaveDefinitionDto
                {
                    id = "forecast_match",
                    archetype = "EliteMix",
                    spawns = new[]
                    {
                        new WaveSpawnDto
                        {
                            enemyId = "raider",
                            count = 3,
                            firstSpawnTick = 0,
                            intervalTicks = 1,
                            eliteTraitIds = new[] { "barrier" }
                        }
                    }
                }
            };
            source.run.regularDraftWaveNumbers = Array.Empty<int>();
            source.run.bossCardPackWaveNumbers = Array.Empty<int>();
            source.run.cardPackProgressThresholds = new[] { 10000 };
            CompiledContent content = Compile(source);
            var simulation = new GameSimulation();
            simulation.Initialize(content, 0xE11EUL);
            WaveForecastSnapshot forecast =
                simulation.GetUpcomingWaveForecast();
            WaveForecastSpawn group = forecast.Spawns[0];

            AssertAccepted(simulation.Submit(
                GameCommand.ChooseStartingTower("ballista")));
            AssertAccepted(simulation.Submit(
                GameCommand.PlaceTower("ballista", 0)));
            AssertAccepted(simulation.Submit(GameCommand.StartWave()));

            int spawned = 0;
            EnemySnapshot firstSpawn = default(EnemySnapshot);
            for (int tick = 0; tick < 4; tick++)
            {
                simulation.Step();
                SimulationEventBuffer events =
                    simulation.ReadPresentationEvents();
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Type ==
                        PresentationEventType.EnemySpawned)
                    {
                        spawned++;
                    }
                }

                SimulationSnapshot snapshot =
                    simulation.GetSnapshot();
                if (snapshot.Enemies.Length > 0 &&
                    firstSpawn.Id == 0)
                {
                    firstSpawn = snapshot.Enemies[0];
                }
            }

            Assert.That(forecast.TotalCount, Is.EqualTo(3));
            Assert.That(group.Count, Is.EqualTo(3));
            Assert.That(spawned, Is.EqualTo(3));
            Assert.That(
                firstSpawn.MaxHealthMilli,
                Is.EqualTo(group.Stats.MaxHealthMilli));
            Assert.That(
                firstSpawn.Armor,
                Is.EqualTo(group.Stats.Armor));
            Assert.That(
                firstSpawn.ShieldMilli,
                Is.EqualTo(group.Stats.ShieldMilli));
            Assert.That(
                firstSpawn.EliteTraitIds,
                Is.EqualTo(new[] { "barrier" }));
        }

        [Test]
        public void EndlessStageResolver_DoublesThreatAndRootsEnemyGold()
        {
            CompiledContent content = Compile(LoadSource());
            CompiledWaveSpawn armoredSpawn =
                content.GetWave(1).Spawns[3];

            ResolvedWaveEnemyStats firstStage =
                WaveEnemyStatResolver.Resolve(
                    content,
                    armoredSpawn,
                    1);
            ResolvedWaveEnemyStats secondStage =
                WaveEnemyStatResolver.Resolve(
                    content,
                    armoredSpawn,
                    2);
            ResolvedWaveEnemyStats thirdStage =
                WaveEnemyStatResolver.Resolve(
                    content,
                    armoredSpawn,
                    3);

            Assert.That(
                WaveEnemyStatResolver.ResolveSpawnCount(7, 1),
                Is.EqualTo(7));
            Assert.That(
                WaveEnemyStatResolver.ResolveSpawnCount(7, 2),
                Is.EqualTo(14));
            Assert.That(
                WaveEnemyStatResolver.ResolveSpawnCount(7, 3),
                Is.EqualTo(28));
            Assert.That(
                secondStage.MaxHealthMilli,
                Is.EqualTo(firstStage.MaxHealthMilli * 2L));
            Assert.That(
                secondStage.Armor,
                Is.EqualTo(firstStage.Armor * 2));
            Assert.That(secondStage.RewardBudget, Is.EqualTo(2));
            Assert.That(
                thirdStage.MaxHealthMilli,
                Is.EqualTo(firstStage.MaxHealthMilli * 4L));
            Assert.That(
                thirdStage.Armor,
                Is.EqualTo(firstStage.Armor * 4));
            Assert.That(thirdStage.RewardBudget, Is.EqualTo(1));
            Assert.That(
                WaveEnemyStatResolver.ApplyRewardRoots(10, 2),
                Is.EqualTo(3));
        }

        [Test]
        public void VictoryContinue_PreservesBuildAndStartsScaledStage()
        {
            ContentCatalogDto source = LoadSource();
            source.waves = new[]
            {
                new WaveDefinitionDto
                {
                    id = "endless_test",
                    archetype = "Standard",
                    spawns = new[]
                    {
                        new WaveSpawnDto
                        {
                            enemyId = "armored_knight",
                            count = 1,
                            firstSpawnTick = 0,
                            intervalTicks = 1
                        }
                    }
                }
            };
            source.run.regularDraftWaveNumbers = Array.Empty<int>();
            source.run.bossCardPackWaveNumbers = Array.Empty<int>();
            source.run.startingTowerChoices = new[] { "ballista" };
            for (int i = 0; i < source.towers.Length; i++)
            {
                if (source.towers[i].id != "ballista")
                {
                    continue;
                }

                source.towers[i].baseDamageMilli = 2_000_000;
                source.towers[i].cooldownTicks = 1;
                for (int level = 0;
                     level < source.towers[i].levels.Length;
                     level++)
                {
                    source.towers[i].levels[level].cooldownTicks = 1;
                }
            }
            for (int i = 0; i < source.enemies.Length; i++)
            {
                if (source.enemies[i].id == "armored_knight")
                {
                    source.enemies[i].speedMilliPerTick = 1;
                }
            }

            CompiledContent content = Compile(source);
            var simulation = new GameSimulation();
            simulation.Initialize(content, 0xE0D1E55UL);
            AssertAccepted(simulation.Submit(
                GameCommand.ChooseStartingTower("ballista")));
            AssertAccepted(simulation.Submit(
                GameCommand.PlaceTower("ballista", 0)));
            AssertAccepted(simulation.Submit(
                GameCommand.StartWave()));
            StepUntilVictory(simulation, 200);

            SimulationSnapshot cleared = simulation.GetSnapshot();
            int retainedGold = cleared.Gold;
            int retainedTowerId = cleared.Towers[0].Id;
            int retainedCardCount = cleared.Cards.Length;
            Assert.That(cleared.StageNumber, Is.EqualTo(1));

            AssertAccepted(simulation.Submit(
                GameCommand.ContinueStage()));
            SimulationSnapshot continued = simulation.GetSnapshot();
            Assert.That(continued.Phase, Is.EqualTo(RunPhase.Planning));
            Assert.That(continued.StageNumber, Is.EqualTo(2));
            Assert.That(continued.WaveIndex, Is.EqualTo(-1));
            Assert.That(continued.Gold, Is.EqualTo(retainedGold));
            Assert.That(continued.BaseHealth, Is.EqualTo(cleared.BaseHealth));
            Assert.That(continued.Towers, Has.Length.EqualTo(1));
            Assert.That(continued.Towers[0].Id, Is.EqualTo(retainedTowerId));
            Assert.That(
                continued.Cards,
                Has.Length.EqualTo(retainedCardCount));
            Assert.That(continued.Enemies, Is.Empty);
            Assert.That(continued.Lineages, Is.Empty);

            WaveForecastSnapshot forecast =
                simulation.GetUpcomingWaveForecast();
            Assert.That(forecast.StageNumber, Is.EqualTo(2));
            Assert.That(forecast.TotalCount, Is.EqualTo(2));
            Assert.That(forecast.Spawns[0].Count, Is.EqualTo(2));
            Assert.That(forecast.Spawns[0].Stats.MaxHealthMilli,
                Is.EqualTo(69_360));
            Assert.That(forecast.Spawns[0].Stats.Armor,
                Is.EqualTo(84));
            Assert.That(forecast.Spawns[0].Stats.RewardBudget,
                Is.EqualTo(2));

            CommandResult duplicateContinue = simulation.Submit(
                GameCommand.ContinueStage());
            Assert.That(duplicateContinue.Accepted, Is.False);
            Assert.That(
                duplicateContinue.Error,
                Is.EqualTo(CommandError.InvalidPhase));

            AssertAccepted(simulation.Submit(
                GameCommand.StartWave()));
            int spawned = CountSpawnsUntilVictory(
                simulation,
                300);
            Assert.That(spawned, Is.EqualTo(2));
            Assert.That(
                simulation.GetSnapshot().Lineages,
                Has.Length.EqualTo(2));
        }

        [Test]
        public void UnknownRecommendedCardFailsContentCompilation()
        {
            ContentCatalogDto source = LoadSource();
            source.enemies[0].recommendedCardIds =
                new[] { "missing_card" };

            ContentValidationException exception = Assert.Throws<
                ContentValidationException>(
                delegate { Compile(source); });
            Assert.That(
                exception.Message,
                Does.Contain("unknown recommended card"));
        }

        private static ContentCatalogDto LoadSource()
        {
            TextAsset asset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    ContentAssetPath);
            Assert.That(asset, Is.Not.Null);
            ContentCatalogDto source =
                JsonUtility.FromJson<ContentCatalogDto>(asset.text);
            Assert.That(source, Is.Not.Null);
            return source;
        }

        private static CompiledContent Compile(
            ContentCatalogDto source)
        {
            return EffectContentCompiler.Compile(
                source,
                GameSimulation.IsEffectOperationSupported);
        }

        private static void AssertAccepted(CommandResult result)
        {
            Assert.That(
                result.Accepted,
                Is.True,
                result.Error + ": " + result.Message);
        }

        private static void StepUntilVictory(
            GameSimulation simulation,
            int maximumSteps)
        {
            CountSpawnsUntilVictory(simulation, maximumSteps);
            Assert.That(
                simulation.Phase,
                Is.EqualTo(RunPhase.Victory));
        }

        private static int CountSpawnsUntilVictory(
            GameSimulation simulation,
            int maximumSteps)
        {
            int spawned = 0;
            for (int step = 0;
                 step < maximumSteps &&
                 simulation.Phase != RunPhase.Victory &&
                 simulation.Phase != RunPhase.Defeat;
                 step++)
            {
                simulation.Step();
                SimulationEventBuffer events =
                    simulation.ReadPresentationEvents();
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i].Type ==
                        PresentationEventType.EnemySpawned)
                    {
                        spawned++;
                    }
                }
            }

            Assert.That(
                simulation.Phase,
                Is.EqualTo(RunPhase.Victory),
                "The endless-stage fixture did not reach victory.");
            return spawned;
        }
    }
}
