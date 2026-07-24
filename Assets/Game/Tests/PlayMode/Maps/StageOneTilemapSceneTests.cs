using System.Collections;
using NUnit.Framework;
using RuleforgeTD.Maps;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace RuleforgeTD.Tests.PlayMode
{
    public sealed class StageOneTilemapSceneTests
    {
        private static readonly Vector2[] ExpectedPath =
        {
            new Vector2(0f, 0f),
            new Vector2(8f, 0f),
            new Vector2(8f, 6f),
            new Vector2(16f, 6f),
            new Vector2(16f, 12f),
            new Vector2(24f, 12f)
        };

        [UnityTest]
        public IEnumerator Stage01_IsEditableAndMatchesSimulationMapData()
        {
            SceneManager.LoadScene("Stage01", LoadSceneMode.Single);
            yield return null;

            FieldStageMap stage =
                Object.FindObjectOfType<FieldStageMap>();
            Assert.That(stage, Is.Not.Null);
            Assert.That(stage.Terrain, Is.Not.Null);
            Assert.That(stage.GroundDecals, Is.Not.Null);
            Assert.That(stage.Props, Is.Not.Null);
            Assert.That(stage.AnimatedObjects, Is.Not.Null);
            Assert.That(stage.NavigationMask, Is.Not.Null);
            Assert.That(stage.Path, Is.Not.Null);

            Assert.That(stage.Path.WaypointCount, Is.EqualTo(
                ExpectedPath.Length));
            for (int i = 0; i < ExpectedPath.Length; i++)
            {
                Vector2 actual = stage.Path.GetWorldWaypoint(i);
                Assert.That(
                    actual.x,
                    Is.EqualTo(ExpectedPath[i].x).Within(0.001f));
                Assert.That(
                    actual.y,
                    Is.EqualTo(ExpectedPath[i].y).Within(0.001f));
            }

            AssertPathClear(stage.NavigationMask);
            AssertBuildSites(stage);

            Assert.That(
                stage.Props.GetUsedTilesCount(),
                Is.GreaterThan(0));
            Assert.That(
                stage.AnimatedObjects.GetUsedTilesCount(),
                Is.GreaterThan(0));
            Assert.That(
                stage.AnimatedObjects.animationFrameRate,
                Is.EqualTo(1f / 0.12f).Within(0.001f));

            TilemapCollider2D tilemapCollider =
                stage.Terrain.GetComponent<TilemapCollider2D>();
            CompositeCollider2D compositeCollider =
                stage.Terrain.GetComponent<CompositeCollider2D>();
            Rigidbody2D terrainBody =
                stage.Terrain.GetComponent<Rigidbody2D>();
            Assert.That(tilemapCollider, Is.Not.Null);
            Assert.That(compositeCollider, Is.Not.Null);
            Assert.That(terrainBody, Is.Not.Null);
            Assert.That(
                terrainBody.bodyType,
                Is.EqualTo(RigidbodyType2D.Static));

            AssertFullyBlockedTileIsBlocked(stage);
        }

        private static void AssertPathClear(
            StageNavigationMask navigation)
        {
            Vector2[] offsets =
            {
                Vector2.zero,
                Vector2.up * 0.25f,
                Vector2.down * 0.25f,
                Vector2.left * 0.25f,
                Vector2.right * 0.25f
            };

            for (int segment = 0;
                 segment < ExpectedPath.Length - 1;
                 segment++)
            {
                Vector2 from = ExpectedPath[segment];
                Vector2 to = ExpectedPath[segment + 1];
                int steps = Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        Vector2.Distance(from, to) / 0.125f));
                for (int step = 0; step <= steps; step++)
                {
                    Vector2 point = Vector2.Lerp(
                        from,
                        to,
                        step / (float)steps);
                    foreach (Vector2 offset in offsets)
                    {
                        Assert.That(
                            navigation.IsBlocked(point + offset),
                            Is.False,
                            "Path blocked at " + (point + offset) + ".");
                    }
                }
            }
        }

        private static void AssertBuildSites(FieldStageMap stage)
        {
            Assert.That(stage.BuildSiteCount, Is.EqualTo(8));
            int[] expectedCosts = { 0, 0, 0, 0, 0, 75, 75, 0 };
            for (int i = 0; i < stage.BuildSiteCount; i++)
            {
                TowerBuildSiteView site = stage.GetBuildSite(i);
                Assert.That(site.BuildPointIndex, Is.EqualTo(i));
                Assert.That(site.UnlockCost, Is.EqualTo(expectedCosts[i]));
                bool shouldBeLocked = i == 5 || i == 6;
                Assert.That(
                    site.State,
                    Is.EqualTo(
                        shouldBeLocked
                            ? TowerBuildSiteVisualState.Locked
                            : TowerBuildSiteVisualState.Available));
                Assert.That(site.CanBuild, Is.EqualTo(!shouldBeLocked));

                SpriteRenderer renderer =
                    site.GetComponent<SpriteRenderer>();
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.sprite, Is.Not.Null);
                Assert.That(
                    renderer.sprite.texture.name,
                    Is.EqualTo(
                        shouldBeLocked
                            ? "PlaceForTower2"
                            : "PlaceForTower1"));

                if (shouldBeLocked)
                {
                    site.ApplySimulationState(true, false);
                    Assert.That(
                        site.State,
                        Is.EqualTo(
                            TowerBuildSiteVisualState.Available));
                    Assert.That(site.CanBuild, Is.True);
                    Assert.That(
                        renderer.sprite.texture.name,
                        Is.EqualTo("PlaceForTower1"));
                    site.ApplySimulationState(false, false);
                }
            }
        }

        private static void AssertFullyBlockedTileIsBlocked(
            FieldStageMap stage)
        {
            BoundsInt bounds = stage.Terrain.cellBounds;
            foreach (Vector3Int cell in bounds.allPositionsWithin)
            {
                FieldTerrainTile tile =
                    stage.Terrain.GetTile<FieldTerrainTile>(cell);
                if (tile == null || tile.TileNumber != 38)
                {
                    continue;
                }

                Vector3 worldCenter =
                    stage.Terrain.GetCellCenterWorld(cell);
                Assert.That(
                    stage.NavigationMask.IsBlocked(worldCenter),
                    Is.True);
                return;
            }

            Assert.Fail("Stage01 must paint at least one tile 38.");
        }
    }
}
