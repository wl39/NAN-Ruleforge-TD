using System.Collections;
using System.Linq;
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
            Assert.That(stage.DecorationRoot, Is.Not.Null);
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
            AssertDecorationLayout(stage);

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
            for (int i = 0; i < stage.BuildSiteCount; i++)
            {
                TowerBuildSiteView site = stage.GetBuildSite(i);
                Assert.That(site.BuildPointIndex, Is.EqualTo(i));
                Assert.That(site.UnlockCost, Is.Zero);
                Assert.That(
                    site.State,
                    Is.EqualTo(TowerBuildSiteVisualState.Available));
                Assert.That(site.CanBuild, Is.True);

                SpriteRenderer renderer =
                    site.GetComponent<SpriteRenderer>();
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.sprite, Is.Not.Null);
                Assert.That(
                    renderer.sprite.texture.name,
                    Is.EqualTo("PlaceForTower1"));
            }
        }

        private static void AssertDecorationLayout(FieldStageMap stage)
        {
            TilemapRenderer terrainRenderer =
                stage.Terrain.GetComponent<TilemapRenderer>();
            TilemapRenderer decalsRenderer =
                stage.GroundDecals.GetComponent<TilemapRenderer>();
            Assert.That(terrainRenderer, Is.Not.Null);
            Assert.That(decalsRenderer, Is.Not.Null);
            Assert.That(
                terrainRenderer.sortingOrder,
                Is.EqualTo(-3000));
            Assert.That(
                decalsRenderer.sortingOrder,
                Is.EqualTo(-2500));

            AssertGroundDecalClearance(
                stage,
                stage.GroundDecals,
                1.6f,
                1.45f);
            Assert.That(
                stage.GroundDecals.GetUsedTilesCount(),
                Is.GreaterThanOrEqualTo(16),
                "Biome ground cover should support the clustered scenery without returning to uniform map-wide scatter.");

            FieldDecorationView[] decorations =
                stage.DecorationRoot
                    .GetComponentsInChildren<FieldDecorationView>(
                        true);
            FieldDecorationCluster[] clusters =
                stage.DecorationRoot
                    .GetComponentsInChildren<FieldDecorationCluster>(
                        true);
            FieldSpriteAnimator[] animators =
                stage.DecorationRoot
                    .GetComponentsInChildren<FieldSpriteAnimator>(
                        true);
            Assert.That(decorations.Length, Is.GreaterThanOrEqualTo(130));
            Assert.That(clusters.Length, Is.GreaterThanOrEqualTo(11));
            Assert.That(animators.Length, Is.GreaterThanOrEqualTo(5));

            for (int i = 0; i < animators.Length; i++)
            {
                Assert.That(animators[i].TargetRenderer, Is.Not.Null);
                Assert.That(animators[i].FrameCount, Is.EqualTo(6));
                Assert.That(
                    animators[i].FrameDuration,
                    Is.EqualTo(0.12f).Within(0.001f));
            }

            bool hasFractionalPosition = false;
            for (int i = 0; i < decorations.Length; i++)
            {
                FieldDecorationView decoration = decorations[i];
                AssertDecorationBody(stage, decoration);
                hasFractionalPosition |=
                    !Mathf.Approximately(
                        decoration.transform.position.x,
                        Mathf.Round(
                            decoration.transform.position.x)) ||
                    !Mathf.Approximately(
                        decoration.transform.position.y,
                        Mathf.Round(
                            decoration.transform.position.y));
            }

            Assert.That(
                hasFractionalPosition,
                Is.True,
                "Decorations should not be constrained to whole-unit cells.");
            AssertMirroredFamily(decorations, "9 Bush/");
            AssertMirroredFamily(decorations, "2 Fence/");
            AssertMirroredFamily(decorations, "8 Camp/");
            AssertForestOverlap(clusters);
            AssertWildflowerMeadows(clusters);
        }

        private static void AssertDecorationBody(
            FieldStageMap stage,
            FieldDecorationView decoration)
        {
            Assert.That(decoration, Is.Not.Null);
            Assert.That(decoration.Body, Is.Not.Null);
            Assert.That(decoration.Body.sprite, Is.Not.Null);
            Assert.That(
                decoration.Body.spriteSortPoint,
                Is.EqualTo(SpriteSortPoint.Pivot));

            Vector3 position = decoration.transform.position;
            const float pixelSize = 1f / 32f;
            Assert.That(
                position.x / pixelSize,
                Is.EqualTo(
                    Mathf.Round(position.x / pixelSize))
                    .Within(0.001f),
                decoration.AssetKey + " is not pixel-snapped on X.");
            Assert.That(
                position.y / pixelSize,
                Is.EqualTo(
                    Mathf.Round(position.y / pixelSize))
                    .Within(0.001f),
                decoration.AssetKey + " is not pixel-snapped on Y.");

            int expectedOrder =
                -1000 - Mathf.RoundToInt(position.y * 64f);
            Assert.That(
                decoration.Body.sortingOrder,
                Is.InRange(expectedOrder, expectedOrder + 1),
                decoration.AssetKey +
                " does not follow deterministic Y sorting.");

            if (decoration.IsRoadsideMarker)
            {
                Assert.That(
                    DistanceToPath(position),
                    Is.GreaterThanOrEqualTo(1.9f),
                    decoration.AssetKey +
                    " must stay on the road verge.");
            }

            if (decoration.ClusterId.StartsWith("meadow_"))
            {
                Assert.That(
                    DistanceToPath(position),
                    Is.GreaterThanOrEqualTo(1.9f),
                    decoration.AssetKey +
                    " must stay out of the road while filling the meadow.");
            }

            Bounds visualBounds = decoration.Body.bounds;
            if (decoration.HasGroundBase)
            {
                AssertGroundBaseConnection(decoration);
                visualBounds.Encapsulate(
                    decoration.GroundBase.bounds);
            }

            for (int i = 0; i < stage.BuildSiteCount; i++)
            {
                var siteBounds = new Bounds(
                    stage.GetBuildSite(i).transform.position,
                    new Vector3(2.2f, 2.2f, 1f));
                Assert.That(
                    visualBounds.Intersects(siteBounds),
                    Is.False,
                    decoration.AssetKey +
                    " visual bounds overlap build site " +
                    i +
                    ".");
            }

            if (IsGroundedFamily(decoration.AssetKey))
            {
                Assert.That(
                    decoration.HasGroundBase,
                    Is.True,
                    decoration.AssetKey +
                    " must share a ground base at its foot.");
            }
        }

        private static void AssertGroundBaseConnection(
            FieldDecorationView decoration)
        {
            SpriteRenderer groundBase = decoration.GroundBase;
            Assert.That(
                groundBase.transform.parent,
                Is.EqualTo(decoration.transform));
            Assert.That(
                groundBase.transform.localPosition.magnitude,
                Is.LessThanOrEqualTo(0.125f));
            Assert.That(
                groundBase.sortingOrder,
                Is.EqualTo(decoration.Body.sortingOrder - 1));

            Vector3 foot = decoration.transform.position;
            Vector3 closest = groundBase.bounds.ClosestPoint(foot);
            Assert.That(
                Vector2.Distance(closest, foot),
                Is.LessThanOrEqualTo(1f / 32f),
                decoration.AssetKey +
                " ground base is detached from the body foot.");
        }

        private static bool IsGroundedFamily(string assetKey)
        {
            return assetKey == "7 Decor/Tree1" ||
                assetKey.StartsWith("9 Bush/") ||
                assetKey.StartsWith("8 Camp/");
        }

        private static void AssertMirroredFamily(
            FieldDecorationView[] decorations,
            string assetPrefix)
        {
            bool hasFlipped = false;
            bool hasUnflipped = false;
            for (int i = 0; i < decorations.Length; i++)
            {
                if (!decorations[i].AssetKey.StartsWith(
                        assetPrefix))
                {
                    continue;
                }

                hasFlipped |= decorations[i].FlipX;
                hasUnflipped |= !decorations[i].FlipX;
            }

            Assert.That(
                hasFlipped,
                Is.True,
                assetPrefix + " requires at least one mirrored instance.");
            Assert.That(
                hasUnflipped,
                Is.True,
                assetPrefix + " requires at least one normal instance.");
        }

        private static void AssertForestOverlap(
            FieldDecorationCluster[] clusters)
        {
            int forestCount = 0;
            for (int clusterIndex = 0;
                 clusterIndex < clusters.Length;
                 clusterIndex++)
            {
                FieldDecorationCluster cluster =
                    clusters[clusterIndex];
                if (cluster.Profile != "Dense Forest" &&
                    cluster.Profile != "Forest Edge" &&
                    cluster.Profile != "Woodland")
                {
                    continue;
                }

                forestCount++;
                FieldDecorationView[] decorations =
                    cluster.GetComponentsInChildren<
                        FieldDecorationView>(true);
                bool hasOverlap = false;
                for (int left = 0;
                     left < decorations.Length && !hasOverlap;
                     left++)
                {
                    if (decorations[left].AssetKey !=
                        "7 Decor/Tree1")
                    {
                        continue;
                    }

                    for (int right = left + 1;
                         right < decorations.Length;
                         right++)
                    {
                        if (decorations[right].AssetKey ==
                                "7 Decor/Tree1" &&
                            decorations[left].Body.bounds.Intersects(
                                decorations[right].Body.bounds))
                        {
                            hasOverlap = true;
                            break;
                        }
                    }
                }

                Assert.That(
                    hasOverlap,
                    Is.True,
                    cluster.ClusterId +
                    " requires overlapping Tree1 canopies.");
            }

            Assert.That(
                forestCount,
                Is.GreaterThan(0),
                "Stage01 needs at least one forest cluster.");
        }

        private static void AssertWildflowerMeadows(
            FieldDecorationCluster[] clusters)
        {
            FieldDecorationCluster[] meadows = clusters
                .Where(cluster =>
                    cluster.Profile == "Wildflower Meadow")
                .ToArray();
            FieldDecorationCluster[] structural = clusters
                .Where(cluster =>
                    cluster.Profile != "Wildflower Meadow" &&
                    cluster.Profile != "Roadside Verges")
                .ToArray();
            Assert.That(meadows.Length, Is.EqualTo(3));

            int totalFlowers = 0;
            int totalGrass = 0;
            int totalStones = 0;
            for (int meadowIndex = 0;
                 meadowIndex < meadows.Length;
                 meadowIndex++)
            {
                FieldDecorationCluster meadow =
                    meadows[meadowIndex];
                FieldDecorationView[] details = meadow
                    .GetComponentsInChildren<FieldDecorationView>(
                        true);
                FieldDecorationView[] flowers = details
                    .Where(detail =>
                        detail.AssetKey.StartsWith("6 Flower/"))
                    .ToArray();
                FieldDecorationView[] stones = details
                    .Where(detail =>
                        detail.AssetKey.StartsWith("4 Stone/"))
                    .ToArray();
                totalFlowers += flowers.Length;
                totalGrass += details.Count(detail =>
                    detail.AssetKey.StartsWith("5 Grass/"));
                totalStones += stones.Length;

                Assert.That(flowers.Length, Is.GreaterThanOrEqualTo(10));
                Assert.That(stones.Length, Is.LessThanOrEqualTo(1));
                Assert.That(
                    flowers.Select(flower => flower.AssetKey)
                        .Distinct()
                        .Count(),
                    Is.LessThanOrEqualTo(3),
                    meadow.ClusterId +
                    " should use a restrained flower palette.");

                for (int detailIndex = 0;
                     detailIndex < details.Length;
                     detailIndex++)
                {
                    Vector2 position =
                        details[detailIndex].transform.position;
                    Assert.That(
                        IsInsideEllipse(
                            position,
                            meadow.Center,
                            meadow.Radius,
                            1.01f),
                        Is.True,
                        details[detailIndex].AssetKey +
                        " escaped " +
                        meadow.ClusterId +
                        ".");

                    for (int structuralIndex = 0;
                         structuralIndex < structural.Length;
                         structuralIndex++)
                    {
                        Vector2 expandedRadius =
                            structural[structuralIndex].Radius +
                            Vector2.one * 0.1f;
                        Assert.That(
                            IsInsideEllipse(
                                position,
                                structural[structuralIndex].Center,
                                expandedRadius,
                                1f),
                            Is.False,
                            details[detailIndex].AssetKey +
                            " intrudes into " +
                            structural[structuralIndex].ClusterId +
                            ".");
                    }
                }

                for (int flowerIndex = 0;
                     flowerIndex < flowers.Length;
                     flowerIndex++)
                {
                    float nearest = flowers
                        .Where((flower, index) =>
                            index != flowerIndex)
                        .Min(flower =>
                            Vector2.Distance(
                                flowers[flowerIndex]
                                    .transform.position,
                                flower.transform.position));
                    Assert.That(
                        nearest,
                        Is.LessThanOrEqualTo(1f),
                        meadow.ClusterId +
                        " contains an isolated flower.");
                }
            }

            for (int left = 0; left < meadows.Length; left++)
            {
                for (int right = left + 1;
                     right < meadows.Length;
                     right++)
                {
                    Assert.That(
                        Vector2.Distance(
                            meadows[left].Center,
                            meadows[right].Center),
                        Is.GreaterThanOrEqualTo(4f),
                        "Wildflower meadow centers must read as separate terrain patches.");
                }
            }

            Assert.That(totalFlowers, Is.GreaterThanOrEqualTo(37));
            Assert.That(totalGrass, Is.GreaterThanOrEqualTo(12));
            Assert.That(totalStones, Is.EqualTo(3));
            Assert.That(
                totalFlowers,
                Is.GreaterThanOrEqualTo(totalStones * 8));
        }

        private static bool IsInsideEllipse(
            Vector2 point,
            Vector2 center,
            Vector2 radius,
            float normalizedLimit)
        {
            Vector2 offset = point - center;
            float normalizedSquared =
                offset.x * offset.x / (radius.x * radius.x) +
                offset.y * offset.y / (radius.y * radius.y);
            return normalizedSquared <=
                normalizedLimit * normalizedLimit;
        }

        private static void AssertGroundDecalClearance(
            FieldStageMap stage,
            Tilemap tilemap,
            float pathClearance,
            float buildSiteClearance)
        {
            foreach (Vector3Int cell in
                     tilemap.cellBounds.allPositionsWithin)
            {
                if (!tilemap.HasTile(cell))
                {
                    continue;
                }

                Vector2 world = tilemap.GetCellCenterWorld(cell);
                float pathDistance = DistanceToPath(world);

                Assert.That(
                    pathDistance,
                    Is.GreaterThanOrEqualTo(pathClearance),
                    tilemap.name +
                    " decoration is too close to the road at " +
                    cell +
                    " / " +
                    world +
                    ".");

                for (int i = 0; i < stage.BuildSiteCount; i++)
                {
                    float siteDistance = Vector2.Distance(
                        world,
                        stage.GetBuildSite(i).transform.position);
                    Assert.That(
                        siteDistance,
                        Is.GreaterThanOrEqualTo(buildSiteClearance),
                        tilemap.name +
                        " decoration overlaps build site " +
                        i +
                        " at " +
                        cell +
                        " / " +
                        world +
                        ".");
                }
            }
        }

        private static float DistanceToPath(Vector2 point)
        {
            float distance = float.MaxValue;
            for (int segment = 0;
                 segment < ExpectedPath.Length - 1;
                 segment++)
            {
                distance = Mathf.Min(
                    distance,
                    DistanceToSegment(
                        point,
                        ExpectedPath[segment],
                        ExpectedPath[segment + 1]));
            }

            return distance;
        }

        private static float DistanceToSegment(
            Vector2 point,
            Vector2 from,
            Vector2 to)
        {
            Vector2 segment = to - from;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
            {
                return Vector2.Distance(point, from);
            }

            float t = Mathf.Clamp01(
                Vector2.Dot(point - from, segment) /
                lengthSquared);
            return Vector2.Distance(
                point,
                from + segment * t);
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
