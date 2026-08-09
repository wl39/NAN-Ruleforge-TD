#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using RuleforgeTD.Editor.AssetImport;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.Maps;
using RuleforgeTD.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleforgeTD.Tests.EditMode
{
    public sealed class StageThreeFieldMapTests
    {
        [Test]
        public void StageThree_IsWideAndOwnsItsRouteAndStarterCards()
        {
            StageThreeFieldMapBuilder.ValidateStageThreeFromCommandLine();

            TextAsset contentAsset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    StageThreeFieldMapBuilder.StageThreeContentPath);
            Assert.That(contentAsset, Is.Not.Null);
            CompiledContent content =
                LogicContentJsonLoader.Load(contentAsset);
            Assert.That(content.Run.PathPoints, Has.Length.EqualTo(10));
            Assert.That(content.Run.BuildSpots, Has.Length.EqualTo(12));
            Assert.That(
                content.Run.StartingCards
                    .Select(cardId => content.GetCard(cardId).StableId),
                Is.EqualTo(new[]
                {
                    "ricochet",
                    "bleed",
                    "knockback",
                    "shock"
                }));
            AssertTunedCombatBalance(content);
            Assert.That(content.GetWave(0).TotalSpawnCount, Is.EqualTo(35));
            Assert.That(
                content.GetWave(0).Spawns[0].IntervalTicks,
                Is.EqualTo(30));
            AssertStageThreeWaveComposition(content);

            TextAsset stageTwoAsset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    StageTwoFieldMapBuilder.StageTwoContentPath);
            Assert.That(stageTwoAsset, Is.Not.Null);
            CompiledContent stageTwoContent =
                LogicContentJsonLoader.Load(stageTwoAsset);
            Assert.That(
                content.Run.PathPoints,
                Is.Not.EqualTo(stageTwoContent.Run.PathPoints));
            Assert.That(
                content.Run.StartingCards,
                Is.Not.EqualTo(stageTwoContent.Run.StartingCards));
            Assert.That(
                CountAllSpawns(content),
                Is.GreaterThan(CountAllSpawns(stageTwoContent)));

            Scene scene = EditorSceneManager.OpenScene(
                StageThreeFieldMapBuilder.StageThreeScenePath,
                OpenSceneMode.Additive);
            try
            {
                FieldStageMap stage = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    stage = root.GetComponentInChildren<FieldStageMap>(true);
                    if (stage != null)
                    {
                        break;
                    }
                }

                Assert.That(stage, Is.Not.Null);
                Assert.That(stage.BuildSiteCount, Is.EqualTo(12));
                Assert.That(stage.Path.WaypointCount, Is.EqualTo(10));
                Bounds bounds = stage.Terrain
                    .GetComponent<UnityEngine.Tilemaps.TilemapRenderer>()
                    .bounds;
                Assert.That(
                    bounds.size.x / bounds.size.y,
                    Is.GreaterThanOrEqualTo(1.75f));

                Vector2[] scenePath =
                    stage.Path.GetLocalWaypointsCopy();
                for (int i = 0; i < scenePath.Length; i++)
                {
                    Assert.That(
                        scenePath[i].x,
                        Is.EqualTo(
                            content.Run.PathPoints[i].X.MilliUnits / 1000f)
                            .Within(0.001f));
                    Assert.That(
                        scenePath[i].y,
                        Is.EqualTo(
                            content.Run.PathPoints[i].Y.MilliUnits / 1000f)
                            .Within(0.001f));
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void AssertTunedCombatBalance(
            CompiledContent content)
        {
            Assert.That(
                content.TryGetEnemyId("raider", out var raiderId),
                Is.True);
            Assert.That(
                content.GetEnemy(raiderId).MaxHealthMilli,
                Is.EqualTo(15028));

            Assert.That(
                content.TryGetCardId("burn", out var burnId),
                Is.True);
            CompiledCardDefinition burn = content.GetCard(burnId);
            Assert.That(
                burn.ProjectileEffects[0].Amount,
                Is.EqualTo(450));
            Assert.That(
                burn.EnemyEffects[0].Amount,
                Is.EqualTo(450));

            Assert.That(
                content.TryGetCardId("explode", out var explodeId),
                Is.True);
            CompiledCardDefinition explode = content.GetCard(explodeId);
            Assert.That(
                explode.ProjectileEffects[0].Amount,
                Is.EqualTo(6300));
            Assert.That(
                explode.EnemyEffects[0].Amount,
                Is.EqualTo(2250));
            Assert.That(
                explode.EnemyEffects[0].Amount2,
                Is.EqualTo(21600));
        }

        private static void AssertStageThreeWaveComposition(
            CompiledContent content)
        {
            Assert.That(
                Enumerable.Range(0, content.WaveCount)
                    .Select(index =>
                        content.GetWave(index).TotalSpawnCount),
                Is.EqualTo(new[]
                {
                    35, 62, 20, 60, 72, 21, 73, 86, 23
                }));
            Assert.That(CountSpawns(content, "raider"), Is.EqualTo(215));
            Assert.That(CountSpawns(content, "runner"), Is.EqualTo(191));
            Assert.That(
                CountSpawns(content, "armored_knight"),
                Is.EqualTo(34));
            Assert.That(
                CountSpawns(content, "elite_golem"),
                Is.EqualTo(9));
            Assert.That(
                CountAllSpawns(content),
                Is.EqualTo(452));
        }

        private static int CountSpawns(
            CompiledContent content,
            string enemyStableId)
        {
            int total = 0;
            for (int waveIndex = 0;
                 waveIndex < content.WaveCount;
                 waveIndex++)
            {
                foreach (CompiledWaveSpawn spawn in
                         content.GetWave(waveIndex).Spawns)
                {
                    if (content.GetEnemy(spawn.EnemyId).StableId ==
                        enemyStableId)
                    {
                        total += spawn.Count;
                    }
                }
            }
            return total;
        }

        private static int CountAllSpawns(CompiledContent content)
        {
            return Enumerable.Range(0, content.WaveCount)
                .Sum(index => content.GetWave(index).TotalSpawnCount);
        }
    }
}
#endif
