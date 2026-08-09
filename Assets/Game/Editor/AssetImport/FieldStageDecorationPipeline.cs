#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using RuleforgeTD.Maps;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleforgeTD.Editor.AssetImport
{
    /// <summary>
    /// Stage-agnostic authoring data consumed by the shared field decoration
    /// pipeline. New stages provide only layout data; asset loading,
    /// deterministic placement, metadata and validation stay centralized.
    /// </summary>
    internal sealed class FieldStageDecorationSpec
    {
        public FieldStageDecorationSpec(
            string stageId,
            int mapMinX,
            int mapMaxX,
            int mapMinY,
            int mapMaxY,
            float roadHalfWidth,
            Vector2[] pathPoints,
            Vector2[] buildSpots,
            FieldStageBiomeSpec[] biomes,
            FieldStageMeadowSpec[] meadows,
            FieldStageRoadMarkerSpec[] roadsideMarkers,
            int minimumDecorationCount,
            int minimumAnimatedCount)
        {
            StageId = stageId ?? string.Empty;
            MapMinX = mapMinX;
            MapMaxX = mapMaxX;
            MapMinY = mapMinY;
            MapMaxY = mapMaxY;
            RoadHalfWidth = roadHalfWidth;
            PathPoints = pathPoints ?? Array.Empty<Vector2>();
            BuildSpots = buildSpots ?? Array.Empty<Vector2>();
            Biomes = biomes ?? Array.Empty<FieldStageBiomeSpec>();
            Meadows = meadows ?? Array.Empty<FieldStageMeadowSpec>();
            RoadsideMarkers = roadsideMarkers ??
                Array.Empty<FieldStageRoadMarkerSpec>();
            MinimumDecorationCount = minimumDecorationCount;
            MinimumAnimatedCount = minimumAnimatedCount;
        }

        public string StageId { get; }
        public int MapMinX { get; }
        public int MapMaxX { get; }
        public int MapMinY { get; }
        public int MapMaxY { get; }
        public float RoadHalfWidth { get; }
        public Vector2[] PathPoints { get; }
        public Vector2[] BuildSpots { get; }
        public FieldStageBiomeSpec[] Biomes { get; }
        public FieldStageMeadowSpec[] Meadows { get; }
        public FieldStageRoadMarkerSpec[] RoadsideMarkers { get; }
        public int MinimumDecorationCount { get; }
        public int MinimumAnimatedCount { get; }
    }

    internal readonly struct FieldStageBiomeSpec
    {
        public FieldStageBiomeSpec(
            string id,
            string profile,
            Vector2 center,
            Vector2 radius,
            int seed,
            int treeCount,
            int bushCount,
            int accentCount,
            int fenceCount,
            float groundDensity,
            float fenceStartDegrees = 0f,
            float fenceEndDegrees = 0f)
        {
            Id = id;
            Profile = profile;
            Center = center;
            Radius = radius;
            Seed = seed;
            TreeCount = treeCount;
            BushCount = bushCount;
            AccentCount = accentCount;
            FenceCount = fenceCount;
            GroundDensity = groundDensity;
            FenceStartDegrees = fenceStartDegrees;
            FenceEndDegrees = fenceEndDegrees;
        }

        public string Id { get; }
        public string Profile { get; }
        public Vector2 Center { get; }
        public Vector2 Radius { get; }
        public int Seed { get; }
        public int TreeCount { get; }
        public int BushCount { get; }
        public int AccentCount { get; }
        public int FenceCount { get; }
        public float GroundDensity { get; }
        public float FenceStartDegrees { get; }
        public float FenceEndDegrees { get; }
    }

    internal readonly struct FieldStageMeadowSpec
    {
        public FieldStageMeadowSpec(
            string id,
            Vector2 center,
            Vector2 radius,
            int seed,
            int patchCount,
            int flowerCount,
            int grassCount,
            int stoneCount,
            int primaryFlower,
            int secondaryFlower,
            int tertiaryFlower)
        {
            Id = id;
            Center = center;
            Radius = radius;
            Seed = seed;
            PatchCount = patchCount;
            FlowerCount = flowerCount;
            GrassCount = grassCount;
            StoneCount = stoneCount;
            PrimaryFlower = primaryFlower;
            SecondaryFlower = secondaryFlower;
            TertiaryFlower = tertiaryFlower;
        }

        public string Id { get; }
        public Vector2 Center { get; }
        public Vector2 Radius { get; }
        public int Seed { get; }
        public int PatchCount { get; }
        public int FlowerCount { get; }
        public int GrassCount { get; }
        public int StoneCount { get; }
        public int PrimaryFlower { get; }
        public int SecondaryFlower { get; }
        public int TertiaryFlower { get; }
    }

    internal readonly struct FieldStageRoadMarkerSpec
    {
        public FieldStageRoadMarkerSpec(
            int segmentIndex,
            float segmentT,
            float side,
            string key,
            bool animated,
            bool flipX)
        {
            SegmentIndex = segmentIndex;
            SegmentT = segmentT;
            Side = side;
            Key = key;
            Animated = animated;
            FlipX = flipX;
        }

        public int SegmentIndex { get; }
        public float SegmentT { get; }
        public float Side { get; }
        public string Key { get; }
        public bool Animated { get; }
        public bool FlipX { get; }
    }

    /// <summary>
    /// Shared entry point for every Fields-based stage. It deliberately owns
    /// both generation and the decoration contract so a stage cannot opt into
    /// placement without also opting into the same QA rules.
    /// </summary>
    internal static class FieldStageDecorationPipeline
    {
        private const float PixelWorldSize = 1f / 32f;

        public static Transform Generate(
            Tilemap groundDecals,
            Transform stageRoot,
            FieldStageDecorationSpec spec)
        {
            ValidateSpec(spec);
            if (groundDecals == null)
            {
                throw new ArgumentNullException(nameof(groundDecals));
            }

            if (stageRoot == null)
            {
                throw new ArgumentNullException(nameof(stageRoot));
            }

            Dictionary<string, Tile> propTiles =
                CraftPixFieldTilemapAssetBuilder.LoadPropTiles();
            Dictionary<string, FieldAnimatedTile> animatedTiles =
                CraftPixFieldTilemapAssetBuilder.LoadAnimatedTiles();
            CraftPixFieldTilemapAssetBuilder.RunMapSource run =
                CreateRun(spec);
            CraftPixFieldTilemapAssetBuilder.BiomeDefinition[] biomes =
                spec.Biomes.Select(ConvertBiome).ToArray();
            CraftPixFieldTilemapAssetBuilder.MeadowDefinition[] meadows =
                spec.Meadows.Select(ConvertMeadow).ToArray();
            CraftPixFieldTilemapAssetBuilder.RoadMarkerPlacement[] markers =
                spec.RoadsideMarkers.Select(ConvertMarker).ToArray();

            groundDecals.ClearAllTiles();
            CraftPixFieldTilemapAssetBuilder.PaintBiomeGroundCover(
                groundDecals,
                propTiles,
                run,
                biomes);
            Transform root =
                CraftPixFieldTilemapAssetBuilder.CreateStageDecorations(
                    stageRoot,
                    propTiles,
                    animatedTiles,
                    run,
                    biomes,
                    meadows,
                    markers);
            Validate(root, spec);
            return root;
        }

        public static void Validate(
            Transform decorationRoot,
            FieldStageDecorationSpec spec)
        {
            ValidateSpec(spec);
            if (decorationRoot == null)
            {
                throw new InvalidOperationException(
                    spec.StageId + " has no decoration root.");
            }

            FieldDecorationView[] decorations = decorationRoot
                .GetComponentsInChildren<FieldDecorationView>(true);
            FieldDecorationCluster[] clusters = decorationRoot
                .GetComponentsInChildren<FieldDecorationCluster>(true);
            FieldSpriteAnimator[] animators = decorationRoot
                .GetComponentsInChildren<FieldSpriteAnimator>(true);
            int expectedClusterCount =
                spec.Biomes.Length + spec.Meadows.Length + 1;
            if (decorations.Length < spec.MinimumDecorationCount)
            {
                throw new InvalidOperationException(
                    spec.StageId + " requires at least " +
                    spec.MinimumDecorationCount +
                    " decoration instances, but found " +
                    decorations.Length + ".");
            }

            if (clusters.Length != expectedClusterCount)
            {
                throw new InvalidOperationException(
                    spec.StageId + " expected " + expectedClusterCount +
                    " semantic decoration clusters, but found " +
                    clusters.Length + ".");
            }

            if (animators.Length < spec.MinimumAnimatedCount)
            {
                throw new InvalidOperationException(
                    spec.StageId + " has too few animated field props.");
            }

            var bodyRenderers = new HashSet<SpriteRenderer>();
            var baseRenderers = new HashSet<SpriteRenderer>();
            for (int i = 0; i < decorations.Length; i++)
            {
                FieldDecorationView decoration = decorations[i];
                ValidateDecorationMetadata(decoration, spec, i);
                bodyRenderers.Add(decoration.Body);
                if (decoration.HasGroundBase)
                {
                    baseRenderers.Add(decoration.GroundBase);
                }
            }

            SpriteRenderer[] allRenderers = decorationRoot
                .GetComponentsInChildren<SpriteRenderer>(true);
            if (allRenderers.Any(renderer =>
                    !bodyRenderers.Contains(renderer) &&
                    !baseRenderers.Contains(renderer)))
            {
                throw new InvalidOperationException(
                    spec.StageId +
                    " contains a decoration renderer outside the shared view contract.");
            }

            ValidateRoadsideMarkers(decorations, spec);
            ValidateShadowBases(decorations, spec.StageId);
            ValidateFlipDiversity(decorations, spec.StageId);
            ValidateForestCanopies(clusters, spec.StageId);
            ValidateMeadows(clusters, spec);
        }

        private static void ValidateDecorationMetadata(
            FieldDecorationView decoration,
            FieldStageDecorationSpec spec,
            int index)
        {
            if (decoration.Body == null ||
                decoration.Body.sprite == null ||
                string.IsNullOrEmpty(decoration.AssetKey) ||
                string.IsNullOrEmpty(decoration.ClusterId))
            {
                throw new InvalidOperationException(
                    spec.StageId + " decoration " + index +
                    " has incomplete metadata.");
            }

            Vector3 position = decoration.transform.position;
            if (!IsPixelSnapped(position.x) ||
                !IsPixelSnapped(position.y))
            {
                throw new InvalidOperationException(
                    decoration.AssetKey + " is not snapped to 1/32 units.");
            }

            Bounds visualBounds = decoration.Body.bounds;
            if (decoration.HasGroundBase)
            {
                if (decoration.GroundBase.transform.parent !=
                        decoration.transform ||
                    decoration.GroundBase.transform.localPosition.magnitude >
                        0.125f ||
                    decoration.GroundBase.sortingOrder !=
                        decoration.Body.sortingOrder - 1)
                {
                    throw new InvalidOperationException(
                        decoration.AssetKey +
                        " has a detached or incorrectly sorted ground base.");
                }

                visualBounds.Encapsulate(decoration.GroundBase.bounds);
            }

            for (int siteIndex = 0;
                 siteIndex < spec.BuildSpots.Length;
                 siteIndex++)
            {
                var siteBounds = new Bounds(
                    spec.BuildSpots[siteIndex],
                    new Vector3(2.2f, 2.2f, 1f));
                if (visualBounds.Intersects(siteBounds))
                {
                    throw new InvalidOperationException(
                        decoration.AssetKey +
                        " visual bounds overlap build site " +
                        siteIndex + ".");
                }
            }
        }

        private static void ValidateRoadsideMarkers(
            FieldDecorationView[] decorations,
            FieldStageDecorationSpec spec)
        {
            FieldDecorationView[] markers = decorations
                .Where(decoration => decoration.IsRoadsideMarker)
                .ToArray();
            if (markers.Length != spec.RoadsideMarkers.Length)
            {
                throw new InvalidOperationException(
                    spec.StageId + " roadside marker count drifted.");
            }

            for (int i = 0; i < markers.Length; i++)
            {
                float distance = DistanceToPath(
                    markers[i].transform.position,
                    spec.PathPoints);
                float expected = spec.RoadHalfWidth + 0.65f;
                if (Mathf.Abs(distance - expected) > 0.1f)
                {
                    throw new InvalidOperationException(
                        markers[i].AssetKey +
                        " is not aligned to a path segment normal.");
                }
            }
        }

        private static void ValidateShadowBases(
            FieldDecorationView[] decorations,
            string stageId)
        {
            FieldDecorationView[] grounded = decorations
                .Where(decoration =>
                    decoration.AssetKey.StartsWith(
                        "7 Decor/Tree",
                        StringComparison.Ordinal) ||
                    decoration.AssetKey.StartsWith(
                        "9 Bush/",
                        StringComparison.Ordinal) ||
                    decoration.AssetKey.StartsWith(
                        "8 Camp/",
                        StringComparison.Ordinal))
                .ToArray();
            if (grounded.Any(decoration => !decoration.HasGroundBase))
            {
                throw new InvalidOperationException(
                    stageId +
                    " trees, bushes and tents must own a 1 Shadow ground base.");
            }
        }

        private static void ValidateFlipDiversity(
            FieldDecorationView[] decorations,
            string stageId)
        {
            string[] families =
            {
                "9 Bush/",
                "2 Fence/",
                "7 Decor/Tree1",
                "8 Camp/"
            };
            for (int i = 0; i < families.Length; i++)
            {
                FieldDecorationView[] family = decorations
                    .Where(decoration =>
                        decoration.AssetKey.StartsWith(
                            families[i],
                            StringComparison.Ordinal))
                    .ToArray();
                if (family.Length < 2)
                {
                    continue;
                }

                if (!family.Any(decoration => decoration.FlipX) ||
                    !family.Any(decoration => !decoration.FlipX))
                {
                    throw new InvalidOperationException(
                        stageId + " " + families[i] +
                        " lacks deterministic flip diversity.");
                }
            }
        }

        private static void ValidateForestCanopies(
            FieldDecorationCluster[] clusters,
            string stageId)
        {
            FieldDecorationCluster[] forests = clusters
                .Where(cluster => IsForestProfile(cluster.Profile))
                .ToArray();
            for (int forestIndex = 0;
                 forestIndex < forests.Length;
                 forestIndex++)
            {
                FieldDecorationView[] trees = forests[forestIndex]
                    .GetComponentsInChildren<FieldDecorationView>(true)
                    .Where(decoration =>
                        decoration.AssetKey == "7 Decor/Tree1")
                    .ToArray();
                bool overlap = false;
                for (int left = 0;
                     left < trees.Length && !overlap;
                     left++)
                {
                    for (int right = left + 1;
                         right < trees.Length;
                         right++)
                    {
                        if (trees[left].Body.bounds.Intersects(
                                trees[right].Body.bounds))
                        {
                            overlap = true;
                            break;
                        }
                    }
                }

                if (!overlap)
                {
                    throw new InvalidOperationException(
                        stageId + " " + forests[forestIndex].ClusterId +
                        " has no intentional canopy overlap.");
                }
            }
        }

        private static void ValidateMeadows(
            FieldDecorationCluster[] clusters,
            FieldStageDecorationSpec spec)
        {
            FieldDecorationCluster[] meadows = clusters
                .Where(cluster => cluster.Profile == "Wildflower Meadow")
                .ToArray();
            if (meadows.Length != spec.Meadows.Length)
            {
                throw new InvalidOperationException(
                    spec.StageId + " wildflower meadow count drifted.");
            }

            for (int i = 0; i < spec.Meadows.Length; i++)
            {
                FieldStageMeadowSpec meadowSpec = spec.Meadows[i];
                FieldDecorationCluster meadow = meadows.Single(cluster =>
                    cluster.ClusterId == meadowSpec.Id);
                FieldDecorationView[] details = meadow
                    .GetComponentsInChildren<FieldDecorationView>(true);
                FieldDecorationView[] flowers = details
                    .Where(detail => detail.AssetKey.StartsWith(
                        "6 Flower/",
                        StringComparison.Ordinal))
                    .ToArray();
                int stoneCount = details.Count(detail =>
                    detail.AssetKey.StartsWith(
                        "4 Stone/",
                        StringComparison.Ordinal));
                if (flowers.Length != meadowSpec.FlowerCount ||
                    stoneCount != meadowSpec.StoneCount ||
                    stoneCount > 1 ||
                    (stoneCount > 0 && flowers.Length < stoneCount * 8) ||
                    flowers.Select(flower => flower.AssetKey)
                        .Distinct()
                        .Count() > 3)
                {
                    throw new InvalidOperationException(
                        spec.StageId + " " + meadowSpec.Id +
                        " violates the wildflower composition contract.");
                }

                for (int flowerIndex = 0;
                     flowerIndex < flowers.Length;
                     flowerIndex++)
                {
                    float nearest = float.MaxValue;
                    for (int other = 0;
                         other < flowers.Length;
                         other++)
                    {
                        if (flowerIndex == other)
                        {
                            continue;
                        }

                        nearest = Mathf.Min(
                            nearest,
                            Vector2.Distance(
                                flowers[flowerIndex].transform.position,
                                flowers[other].transform.position));
                    }

                    if (nearest > 1f)
                    {
                        throw new InvalidOperationException(
                            spec.StageId + " " + meadowSpec.Id +
                            " contains an isolated flower.");
                    }
                }
            }
        }

        private static void ValidateSpec(FieldStageDecorationSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            if (string.IsNullOrWhiteSpace(spec.StageId) ||
                spec.MapMinX >= spec.MapMaxX ||
                spec.MapMinY >= spec.MapMaxY ||
                spec.RoadHalfWidth <= 0f ||
                spec.PathPoints.Length < 2 ||
                spec.Biomes.Length == 0 ||
                spec.Meadows.Length < 3 ||
                spec.RoadsideMarkers.Length == 0)
            {
                throw new InvalidOperationException(
                    "Field stage decoration specification is incomplete.");
            }

            if (spec.Biomes.Select(biome => biome.Id)
                    .Concat(spec.Meadows.Select(meadow => meadow.Id))
                    .Append("roadside_verges")
                    .Distinct(StringComparer.Ordinal)
                    .Count() !=
                spec.Biomes.Length + spec.Meadows.Length + 1)
            {
                throw new InvalidOperationException(
                    spec.StageId + " contains duplicate cluster ids.");
            }

            for (int i = 0; i < spec.RoadsideMarkers.Length; i++)
            {
                FieldStageRoadMarkerSpec marker =
                    spec.RoadsideMarkers[i];
                if (marker.SegmentIndex < 0 ||
                    marker.SegmentIndex >= spec.PathPoints.Length - 1 ||
                    marker.SegmentT < 0f ||
                    marker.SegmentT > 1f ||
                    Mathf.Approximately(marker.Side, 0f) ||
                    string.IsNullOrWhiteSpace(marker.Key))
                {
                    throw new InvalidOperationException(
                        spec.StageId + " has an invalid roadside marker.");
                }
            }
        }

        private static CraftPixFieldTilemapAssetBuilder.RunMapSource
            CreateRun(FieldStageDecorationSpec spec)
        {
            return new CraftPixFieldTilemapAssetBuilder.RunMapSource(
                spec.PathPoints,
                spec.BuildSpots,
                new int[spec.BuildSpots.Length],
                spec.MapMinX,
                spec.MapMaxX,
                spec.MapMinY,
                spec.MapMaxY,
                spec.RoadHalfWidth);
        }

        private static CraftPixFieldTilemapAssetBuilder.BiomeDefinition
            ConvertBiome(FieldStageBiomeSpec biome)
        {
            return new CraftPixFieldTilemapAssetBuilder.BiomeDefinition(
                biome.Id,
                biome.Profile,
                biome.Center,
                biome.Radius,
                biome.Seed,
                biome.TreeCount,
                biome.BushCount,
                biome.AccentCount,
                biome.FenceCount,
                biome.GroundDensity,
                biome.FenceStartDegrees,
                biome.FenceEndDegrees);
        }

        private static CraftPixFieldTilemapAssetBuilder.MeadowDefinition
            ConvertMeadow(FieldStageMeadowSpec meadow)
        {
            return new CraftPixFieldTilemapAssetBuilder.MeadowDefinition(
                meadow.Id,
                meadow.Center,
                meadow.Radius,
                meadow.Seed,
                meadow.PatchCount,
                meadow.FlowerCount,
                meadow.GrassCount,
                meadow.StoneCount,
                meadow.PrimaryFlower,
                meadow.SecondaryFlower,
                meadow.TertiaryFlower);
        }

        private static CraftPixFieldTilemapAssetBuilder.RoadMarkerPlacement
            ConvertMarker(FieldStageRoadMarkerSpec marker)
        {
            return new CraftPixFieldTilemapAssetBuilder.RoadMarkerPlacement(
                marker.SegmentIndex,
                marker.SegmentT,
                marker.Side,
                marker.Key,
                marker.Animated,
                marker.FlipX);
        }

        private static bool IsPixelSnapped(float value)
        {
            float units = value / PixelWorldSize;
            return Mathf.Abs(units - Mathf.Round(units)) < 0.001f;
        }

        private static bool IsForestProfile(string profile)
        {
            return profile == "Dense Forest" ||
                profile == "Forest Edge" ||
                profile == "Woodland";
        }

        private static float DistanceToPath(
            Vector2 point,
            Vector2[] path)
        {
            float result = float.MaxValue;
            for (int i = 0; i < path.Length - 1; i++)
            {
                Vector2 segment = path[i + 1] - path[i];
                float lengthSquared = segment.sqrMagnitude;
                float t = lengthSquared <= Mathf.Epsilon
                    ? 0f
                    : Mathf.Clamp01(
                        Vector2.Dot(point - path[i], segment) /
                        lengthSquared);
                result = Mathf.Min(
                    result,
                    Vector2.Distance(
                        point,
                        path[i] + segment * t));
            }

            return result;
        }
    }
}
#endif
