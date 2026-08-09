#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using RuleforgeTD.Editor.AssetImport;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.Maps;
using RuleforgeTD.Rendering;
using RuleforgeTD.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleforgeTD.Tests.EditMode
{
    public sealed class StageTwoFieldMapTests
    {
        [Test]
        public void StageTwo_IsTallAndUsesTheSimulationRoute()
        {
            StageTwoFieldMapBuilder.ValidateStageTwoFromCommandLine();

            TextAsset contentAsset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    StageTwoFieldMapBuilder.StageTwoContentPath);
            Assert.That(contentAsset, Is.Not.Null);
            CompiledContent content =
                LogicContentJsonLoader.Load(contentAsset);
            Assert.That(content.Run.PathPoints, Has.Length.EqualTo(14));
            Assert.That(content.Run.BuildSpots, Has.Length.EqualTo(12));
            Assert.That(
                content.Run.StartingCards
                    .Select(cardId => content.GetCard(cardId).StableId),
                Is.EqualTo(new[]
                {
                    "pierce",
                    "mark",
                    "poison",
                    "corrosion"
                }));
            AssertTunedCombatBalance(content);
            Assert.That(content.GetWave(0).TotalSpawnCount, Is.EqualTo(30));
            Assert.That(
                content.GetWave(0).Spawns[0].IntervalTicks,
                Is.EqualTo(12));

            Scene scene = EditorSceneManager.OpenScene(
                StageTwoFieldMapBuilder.StageTwoScenePath,
                OpenSceneMode.Additive);
            try
            {
                FieldStageMap stage = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    stage = root.GetComponentInChildren<
                        FieldStageMap>(true);
                    if (stage != null)
                    {
                        break;
                    }
                }

                Assert.That(stage, Is.Not.Null);
                Assert.That(stage.BuildSiteCount, Is.EqualTo(12));
                Assert.That(stage.Path.WaypointCount, Is.EqualTo(14));
                Assert.That(
                    stage.Terrain.GetComponent<
                        UnityEngine.Tilemaps.TilemapRenderer>()
                        .sortingLayerName,
                    Is.EqualTo(WorldSortingLayers.Route));
                Assert.That(
                    stage.GroundDecals.GetComponent<
                        UnityEngine.Tilemaps.TilemapRenderer>()
                        .sortingLayerName,
                    Is.EqualTo(WorldSortingLayers.Route));
                for (int siteIndex = 0;
                     siteIndex < stage.BuildSiteCount;
                     siteIndex++)
                {
                    Assert.That(
                        stage.GetBuildSite(siteIndex)
                            .GetComponent<SpriteRenderer>()
                            .sortingLayerName,
                        Is.EqualTo(WorldSortingLayers.Route));
                }

                Assert.That(
                    stage.DecorationRoot.GetComponentsInChildren<
                            SpriteRenderer>(true)
                        .All(renderer =>
                            renderer.sortingLayerName ==
                            WorldSortingLayers.Object),
                    Is.True);
                Assert.That(
                    stage.DecorationRoot.GetComponentsInChildren<
                        FieldDecorationView>(true).Length,
                    Is.GreaterThanOrEqualTo(270));
                Assert.That(
                    stage.DecorationRoot.GetComponentsInChildren<
                        FieldDecorationCluster>(true).Length,
                    Is.EqualTo(18));
                Assert.That(
                    stage.DecorationRoot.GetComponentsInChildren<
                            FieldDecorationCluster>(true)
                        .Count(cluster =>
                            cluster.Profile == "Wildflower Meadow"),
                    Is.EqualTo(5));
                Assert.That(
                    stage.DecorationRoot.GetComponentsInChildren<
                            FieldDecorationView>(true)
                        .Count(decoration =>
                            decoration.IsRoadsideMarker),
                    Is.EqualTo(12));
                Bounds bounds = stage.Terrain
                    .GetComponent<UnityEngine.Tilemaps.TilemapRenderer>()
                    .bounds;
                Assert.That(
                    bounds.size.y / bounds.size.x,
                    Is.GreaterThanOrEqualTo(1.75f));

                Vector2[] scenePath =
                    stage.Path.GetLocalWaypointsCopy();
                float longestStraight = 0f;
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
                    if (i > 0)
                    {
                        longestStraight = Mathf.Max(
                            longestStraight,
                            Vector2.Distance(
                                scenePath[i - 1],
                                scenePath[i]));
                    }
                }

                Assert.That(
                    longestStraight,
                    Is.GreaterThanOrEqualTo(14f));
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
    }
}
#endif
