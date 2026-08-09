using System;
using NUnit.Framework;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;
using RuleforgeTD.GameLogic.Simulation;
using UnityEditor;
using UnityEngine;

namespace RuleforgeTD.Tests.EditMode.GameLogic
{
    public sealed class CombatDensityGameLogicTests
    {
        private const string ContentAssetPath =
            "Assets/Game/Data/Logic/phase1-content.json";

        private CompiledContent content;

        [SetUp]
        public void SetUp()
        {
            TextAsset asset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    ContentAssetPath);
            Assert.That(asset, Is.Not.Null);
            ContentCatalogDto source =
                JsonUtility.FromJson<ContentCatalogDto>(asset.text);
            content = EffectContentCompiler.Compile(
                source,
                GameSimulation.IsEffectOperationSupported);
        }

        [Test]
        public void WavePlan_AlternatesDenseAndContainmentCompositions()
        {
            int[] expectedCounts =
                { 35, 55, 16, 52, 62, 18, 64, 76, 20 };
            WaveArchetype[] expectedArchetypes =
            {
                WaveArchetype.Swarm,
                WaveArchetype.EliteMix,
                WaveArchetype.Containment,
                WaveArchetype.HeavyEscort,
                WaveArchetype.Rush,
                WaveArchetype.Containment,
                WaveArchetype.HeavyEscort,
                WaveArchetype.Swarm,
                WaveArchetype.Containment
            };

            int total = 0;
            int elite = 0;
            int boss = 0;
            int containmentWaveCount = 0;
            for (int waveIndex = 0;
                 waveIndex < content.WaveCount;
                 waveIndex++)
            {
                CompiledWaveDefinition wave =
                    content.GetWave(waveIndex);
                Assert.That(
                    wave.TotalSpawnCount,
                    Is.EqualTo(expectedCounts[waveIndex]));
                Assert.That(
                    wave.Archetype,
                    Is.EqualTo(expectedArchetypes[waveIndex]));
                Assert.That(
                    wave.NormalSpawnCount +
                    wave.EliteSpawnCount +
                    wave.BossSpawnCount,
                    Is.EqualTo(wave.TotalSpawnCount));
                total += wave.TotalSpawnCount;
                elite += wave.EliteSpawnCount;
                boss += wave.BossSpawnCount;
                if (wave.Archetype == WaveArchetype.Containment)
                {
                    containmentWaveCount++;
                    Assert.That(wave.TotalSpawnCount, Is.LessThanOrEqualTo(20));
                }
                else
                {
                    Assert.That(wave.TotalSpawnCount, Is.GreaterThanOrEqualTo(30));
                }
            }

            Assert.That(total, Is.EqualTo(398));
            Assert.That(total, Is.GreaterThan(334));
            Assert.That(containmentWaveCount, Is.EqualTo(3));
            Assert.That(
                total,
                Is.InRange(334 * 115 / 100, 334 * 125 / 100));
            int eliteShareBps = elite * 10_000 / (total - boss);
            Assert.That(eliteShareBps, Is.InRange(1000, 2000));
        }

        [TestCase("raider", 15028, 0)]
        [TestCase("runner", 9248, 0)]
        [TestCase("armored_knight", 34680, 42)]
        [TestCase("elite_golem", 130050, 50)]
        [TestCase("boss_guardian", 156060, 34)]
        [TestCase("boss_summoner", 312120, 30)]
        [TestCase("boss_time_walker", 572220, 42)]
        public void EnemyDurability_UsesLowerHealthAndArmorBaseline(
            string stableId,
            int expectedHealthMilli,
            int expectedArmor)
        {
            Assert.That(
                content.TryGetEnemyId(
                    stableId,
                    out EnemyDefinitionId enemyId),
                Is.True);
            CompiledEnemyDefinition enemy =
                content.GetEnemy(enemyId);
            Assert.That(
                enemy.MaxHealthMilli,
                Is.EqualTo(expectedHealthMilli));
            Assert.That(enemy.Armor, Is.EqualTo(expectedArmor));
        }

        [Test]
        public void WaveForecast_ExactlyMatchesCompiledSpawnPlan()
        {
            var simulation = new GameSimulation();
            simulation.Initialize(content, 0xD311517UL);

            for (int waveIndex = 0;
                 waveIndex < content.WaveCount;
                 waveIndex++)
            {
                CompiledWaveDefinition wave =
                    content.GetWave(waveIndex);
                WaveForecastSnapshot forecast =
                    simulation.GetWaveForecast(waveIndex);
                Assert.That(
                    forecast.TotalCount,
                    Is.EqualTo(wave.TotalSpawnCount));
                Assert.That(
                    forecast.NormalCount,
                    Is.EqualTo(wave.NormalSpawnCount));
                Assert.That(
                    forecast.EliteCount,
                    Is.EqualTo(wave.EliteSpawnCount));
                Assert.That(
                    forecast.BossCount,
                    Is.EqualTo(wave.BossSpawnCount));
                Assert.That(
                    forecast.Spawns,
                    Has.Length.EqualTo(wave.Spawns.Length));

                int forecastTotal = 0;
                for (int spawnIndex = 0;
                     spawnIndex < forecast.Spawns.Length;
                     spawnIndex++)
                {
                    forecastTotal += forecast.Spawns[spawnIndex].Count;
                    Assert.That(
                        forecast.Spawns[spawnIndex].Count,
                        Is.EqualTo(wave.Spawns[spawnIndex].Count));
                    Assert.That(
                        forecast.Spawns[spawnIndex].FirstSpawnTick,
                        Is.EqualTo(
                            wave.Spawns[spawnIndex].FirstSpawnTick));
                    Assert.That(
                        forecast.Spawns[spawnIndex].IntervalTicks,
                        Is.EqualTo(
                            wave.Spawns[spawnIndex].IntervalTicks));
                }
                Assert.That(forecastTotal, Is.EqualTo(forecast.TotalCount));
            }
        }

        [Test]
        public void DensityEconomyAndSafety_AreDataDriven()
        {
            Assert.That(content.Run.ArmorMitigationScale, Is.EqualTo(100));
            Assert.That(
                content.Run.AreaArmorSensitivityBps,
                Is.EqualTo(50000));
            Assert.That(
                content.Run.BurnArmorSensitivityBps,
                Is.EqualTo(40000));
            Assert.That(content.Run.KillStreakWindowTicks, Is.EqualTo(45));
            Assert.That(content.Run.KillStreakBonusInterval, Is.EqualTo(5));
            Assert.That(content.Run.KillStreakBonusGold, Is.EqualTo(1));
            Assert.That(content.Run.EliteKillBonusGold, Is.EqualTo(3));
            Assert.That(content.Run.WaveCompletionBaseGold, Is.EqualTo(8));
            Assert.That(content.Run.WaveCompletionGoldPerWave, Is.EqualTo(2));

            Assert.That(content.Safety.MaxActiveEnemies, Is.EqualTo(220));
            Assert.That(content.Safety.MaxActiveProjectiles, Is.EqualTo(500));
            Assert.That(content.Safety.MaxEffectsPerFrame, Is.EqualTo(32));
            Assert.That(content.Safety.MaxEntitySpawnsPerChain, Is.EqualTo(96));
            Assert.That(content.Safety.MaxEnemySplitGeneration, Is.EqualTo(8));
            Assert.That(content.Safety.MaxProjectileCloneGeneration, Is.EqualTo(4));
            Assert.That(content.Safety.MaxCombatPopups, Is.EqualTo(8));
            Assert.That(content.Safety.PopupAggregateTicks, Is.EqualTo(12));
        }

        [Test]
        public void EntitySpawnBudget_RejectsWholeOverflowReservation()
        {
            var budget = new ChainBudget(
                new ChainId(1),
                SafetyLimits.CreateDefault());
            var accepted = new ChainReservation(
                depth: 1,
                eventCount: 2,
                projectileSpawnCount: 64,
                enemySpawnCount: 32);
            Assert.That(
                budget.TryReserve(in accepted, out BudgetFailure firstFailure),
                Is.True);
            Assert.That(firstFailure, Is.EqualTo(BudgetFailure.None));

            var overflow = new ChainReservation(
                depth: 1,
                eventCount: 1,
                enemySpawnCount: 1);
            Assert.That(
                budget.TryReserve(in overflow, out BudgetFailure failure),
                Is.False);
            Assert.That(failure, Is.EqualTo(BudgetFailure.EntitySpawnLimit));
            Assert.That(budget.EventsUsed, Is.EqualTo(2));
            Assert.That(budget.EntitySpawnsUsed, Is.EqualTo(96));
        }
    }
}
