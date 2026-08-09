#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using RuleforgeTD.Battle;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.Maps;
using RuleforgeTD.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace RuleforgeTD.Editor.AssetImport
{
    /// <summary>
    /// Authors Stage 02 as a tall, camera-scrollable switchback map while
    /// reusing the Fields asset library and Stage 01 gameplay presentation.
    /// The simulation content receives the same route and build-site data as
    /// the scene, so the visible road never diverges from authoritative logic.
    /// </summary>
    public static class StageTwoFieldMapBuilder
    {
        internal const int FirstWaveEnemyCount = 35;
        internal const int FirstWaveIntervalTicks = 12;

        public const string StageTwoScenePath =
            "Assets/Game/Scenes/Battle/Stage02.unity";
        public const string StageTwoContentPath =
            "Assets/Game/Data/Logic/stage02-content.json";
        public const string StageTwoCatalogPath =
            "Assets/Game/Data/AssetCatalog/" +
            "StageTwoPresentationCatalog.asset";

        internal static readonly string[] StartingCardIds =
        {
            "pierce",
            "mark",
            "poison",
            "corrosion"
        };

        private static readonly WaveDefinitionDto[] WaveOverrides =
        {
            Wave(2,
                Spawn("raider", 26, 0, 12),
                Spawn("armored_knight", 3, 20, 68, "ironclad"),
                Spawn("runner", 14, 24, 14),
                Spawn("armored_knight", 12, 60, 43)),
            Wave(3,
                Spawn("raider", 5, 0, 38),
                Spawn("runner", 5, 24, 36),
                Spawn("armored_knight", 5, 60, 77),
                Spawn("boss_guardian", 1, 180, 1)),
            Wave(4,
                Spawn("armored_knight", 9, 0, 37),
                Spawn("raider", 26, 12, 12),
                Spawn("raider", 3, 70, 85, "giant"),
                Spawn("runner", 14, 45, 14)),
            Wave(5,
                Spawn("runner", 29, 0, 8),
                Spawn("runner", 5, 36, 51, "rusher"),
                Spawn("raider", 15, 30, 15),
                Spawn("armored_knight", 7, 55, 48),
                Spawn("elite_golem", 6, 190, 60)),
            Wave(6,
                Spawn("raider", 6, 0, 41),
                Spawn("runner", 6, 24, 37),
                Spawn("armored_knight", 5, 60, 81),
                Spawn("boss_summoner", 1, 210, 1)),
            Wave(7,
                Spawn("elite_golem", 5, 0, 115),
                Spawn("runner", 25, 24, 9),
                Spawn("raider", 21, 35, 14),
                Spawn("armored_knight", 8, 60, 41),
                Spawn("armored_knight", 5, 85, 85, "barrier")),
            Wave(8,
                Spawn("raider", 31, 0, 8),
                Spawn("runner", 30, 8, 8),
                Spawn("armored_knight", 9, 45, 41),
                Spawn("elite_golem", 6, 180, 68)),
            Wave(9,
                Spawn("raider", 6, 0, 44),
                Spawn("runner", 7, 24, 39),
                Spawn("armored_knight", 6, 75, 89),
                Spawn("boss_time_walker", 1, 230, 1))
        };

        private const string TerrainTileRoot =
            "Assets/Game/Data/Maps/Fields/Tiles/Terrain";
        private const string PropTileRoot =
            "Assets/Game/Data/Maps/Fields/Tiles/Props";
        private const string CollisionGuidePath =
            "Assets/ThirdParty/CraftPix/Raw/Maps/Fields/" +
            "Tiles/FieldsTilesetTest.png";
        private const string BuildSitePrefabPath =
            "Assets/Game/Prefabs/Maps/Fields/TowerBuildSite.prefab";
        private const string BuildSiteSpriteRoot =
            "Assets/ThirdParty/CraftPix/Raw/Maps/Fields/Objects";

        private const int MapMinX = -3;
        private const int MapMaxX = 21;
        private const int MapMinY = -4;
        private const int MapMaxY = 42;
        private const float RoadHalfWidth = 1.35f;
        private const int TerrainSalt = 20620803;
        private const int DecorationSortingBase = -1000;

        private static readonly Vector2[] PathPoints =
        {
            new Vector2(8f, -2f),
            new Vector2(8f, 5f),
            new Vector2(17f, 5f),
            new Vector2(17f, 11f),
            new Vector2(3f, 11f),
            new Vector2(3f, 18f),
            new Vector2(14f, 18f),
            new Vector2(14f, 27f),
            new Vector2(2f, 27f),
            new Vector2(2f, 34f),
            new Vector2(16f, 34f),
            new Vector2(16f, 39f),
            new Vector2(8f, 39f),
            new Vector2(8f, 40f)
        };

        private static readonly Vector2[] BuildSpots =
        {
            new Vector2(11f, 1f),
            new Vector2(10f, 8f),
            new Vector2(14f, 8f),
            new Vector2(7f, 14f),
            new Vector2(11f, 15f),
            new Vector2(9f, 22f),
            new Vector2(17f, 23f),
            new Vector2(7f, 30f),
            new Vector2(11f, 31f),
            new Vector2(8f, 36f),
            new Vector2(19f, 25f),
            new Vector2(0f, 21f)
        };

        private static readonly int[] BuildSpotUnlockCosts =
        {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
        };

        [MenuItem("Ruleforge TD/Scenes/Open Stage 02")]
        public static void OpenStageTwo()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    StageTwoScenePath) == null)
            {
                RebuildStageTwoFromCommandLine();
            }

            EditorSceneManager.OpenScene(
                StageTwoScenePath,
                OpenSceneMode.Single);
        }

        [MenuItem("Ruleforge TD/Scenes/Rebuild Stage 02 (Overwrite)")]
        public static void RebuildStageTwoFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Rebuild Stage 02",
                    "This replaces the generated Stage02 map and gameplay " +
                    "wiring with the tall switchback baseline.",
                    "Rebuild",
                    "Cancel"))
            {
                return;
            }

            RebuildStageTwoFromCommandLine();
        }

        public static void RebuildStageTwoFromCommandLine()
        {
            EnsureSharedFieldAssets();
            CreateStageTwoContent();
            CreateStageTwoCatalog();
            CreateStageTwoScene();
            EnsureStageInBuildSettings();
            ValidateStageTwo();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                "RULEFORGE_STAGE02_BUILD_OK waypoints=" +
                PathPoints.Length +
                " turns=" +
                CountTurns(PathPoints) +
                " buildSites=" +
                BuildSpots.Length +
                " scene=" +
                StageTwoScenePath);
        }

        public static void ValidateStageTwoFromCommandLine()
        {
            ValidateStageTwo();
        }

        private static void EnsureSharedFieldAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<FieldTerrainTile>(
                    TerrainTileRoot + "/FieldTile_38.asset") != null &&
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    BuildSitePrefabPath) != null &&
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    CraftPixFieldTilemapAssetBuilder.StageOneScenePath) != null)
            {
                StageOneGameplaySceneInstaller.EnsurePresentationCatalog();
                return;
            }

            CraftPixFieldTilemapAssetBuilder.BuildFromCommandLine();
        }

        internal static void EnsureSharedFieldAssetsForStageBuilders()
        {
            EnsureSharedFieldAssets();
        }

        private static void CreateStageTwoContent()
        {
            StageContentAuthoring.Create(
                StageTwoContentPath,
                PathPoints,
                BuildSpots,
                BuildSpotUnlockCosts,
                StartingCardIds,
                FirstWaveEnemyCount,
                FirstWaveIntervalTicks,
                WaveOverrides);
        }

        private static WaveDefinitionDto Wave(
            int waveNumber,
            params WaveSpawnDto[] spawns)
        {
            return new WaveDefinitionDto
            {
                id = "wave_" + waveNumber,
                spawns = spawns
            };
        }

        private static WaveSpawnDto Spawn(
            string enemyId,
            int count,
            int firstSpawnTick,
            int intervalTicks,
            params string[] eliteTraitIds)
        {
            return new WaveSpawnDto
            {
                enemyId = enemyId,
                count = count,
                firstSpawnTick = firstSpawnTick,
                intervalTicks = intervalTicks,
                eliteTraitIds = eliteTraitIds.Length > 0
                    ? eliteTraitIds
                    : null
            };
        }

        private static void CreateStageTwoCatalog()
        {
            CreateStageCatalog(
                StageTwoCatalogPath,
                StageTwoContentPath,
                "Stage 02");
        }

        internal static StageOnePresentationCatalog CreateStageCatalog(
            string catalogPath,
            string contentPath,
            string stageLabel)
        {
            StageOnePresentationCatalog source =
                StageOneGameplaySceneInstaller
                    .EnsurePresentationCatalog();
            StageOnePresentationCatalog catalog =
                AssetDatabase.LoadAssetAtPath<
                    StageOnePresentationCatalog>(
                    catalogPath);
            if (catalog == null)
            {
                if (!AssetDatabase.CopyAsset(
                        StageOneGameplaySceneInstaller.CatalogPath,
                        catalogPath))
                {
                    throw new InvalidOperationException(
                        "Could not create the " + stageLabel +
                        " presentation catalog.");
                }

                AssetDatabase.ImportAsset(
                    catalogPath,
                    ImportAssetOptions.ForceSynchronousImport);
                catalog = AssetDatabase.LoadAssetAtPath<
                    StageOnePresentationCatalog>(
                    catalogPath);
            }

            TextAsset content =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    contentPath);
            if (catalog == null || content == null || source == null)
            {
                throw new InvalidOperationException(
                    stageLabel +
                    " catalog dependencies are incomplete.");
            }

            var serialized = new SerializedObject(catalog);
            SerializedProperty contentProperty =
                serialized.FindProperty("contentJson");
            if (contentProperty == null)
            {
                throw new InvalidOperationException(
                    "Stage presentation catalog content field is missing.");
            }

            contentProperty.objectReferenceValue = content;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            StageOneGameplaySceneInstaller
                .ValidatePresentationCatalog(catalog);
            return catalog;
        }

        private static void CreateStageTwoScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    StageTwoScenePath) != null &&
                !AssetDatabase.DeleteAsset(StageTwoScenePath))
            {
                throw new InvalidOperationException(
                    "Could not replace the existing Stage 02 scene.");
            }

            if (!AssetDatabase.CopyAsset(
                    CraftPixFieldTilemapAssetBuilder.StageOneScenePath,
                    StageTwoScenePath))
            {
                throw new InvalidOperationException(
                    "Could not copy the Stage 01 scene baseline.");
            }

            AssetDatabase.ImportAsset(
                StageTwoScenePath,
                ImportAssetOptions.ForceSynchronousImport);
            Scene scene = EditorSceneManager.OpenScene(
                StageTwoScenePath,
                OpenSceneMode.Single);
            FieldStageMap stageMap = FindSingle<FieldStageMap>(scene);
            stageMap.gameObject.name =
                "Stage 02 - Serpentine Ascent";

            Tilemap terrain = stageMap.Terrain;
            Tilemap decals = stageMap.GroundDecals;
            PaintTerrain(terrain);

            Transform oldDecorations = stageMap.DecorationRoot;
            FieldStageDecorationSpec decorationSpec =
                FieldStageDecorationSpecs.CreateStageTwo(
                    PathPoints,
                    BuildSpots);
            Transform decorationRoot =
                FieldStageDecorationPipeline.Generate(
                    decals,
                    stageMap.transform,
                    decorationSpec);
            if (oldDecorations != null)
            {
                Object.DestroyImmediate(oldDecorations.gameObject);
            }

            StagePathAuthoring path = stageMap.Path;
            path.ConfigureAuthoring(PathPoints);
            StageNavigationMask navigation =
                stageMap.NavigationMask;
            navigation.ConfigureAuthoring(terrain);

            TowerBuildSiteView[] sites = ReplaceBuildSites(
                scene,
                stageMap.transform,
                BuildSpots,
                BuildSpotUnlockCosts,
                "Stage 02");
            stageMap.ConfigureAuthoring(
                terrain,
                decals,
                decorationRoot,
                navigation,
                path,
                sites);

            Camera camera = FindSingle<Camera>(scene);
            camera.transform.position =
                new Vector3(9f, 4f, -10f);
            camera.orthographicSize = 7f;

            StageOnePresentationCatalog catalog =
                AssetDatabase.LoadAssetAtPath<
                    StageOnePresentationCatalog>(
                    StageTwoCatalogPath);
            StageOneBattleController controller =
                StageOneGameplaySceneInstaller.EnsureInstalled(
                    scene,
                    stageMap);
            controller.ConfigureAuthoring(
                stageMap,
                catalog,
                22345UL);

            EditorUtility.SetDirty(stageMap);
            EditorUtility.SetDirty(path);
            EditorUtility.SetDirty(navigation);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, StageTwoScenePath))
            {
                throw new InvalidOperationException(
                    "Unity could not save Stage 02.");
            }
        }

        private static void PaintTerrain(Tilemap terrain)
        {
            FieldStageTerrainPipeline.Paint(
                terrain,
                MapMinX,
                MapMaxX,
                MapMinY,
                MapMaxY,
                RoadHalfWidth,
                PathPoints,
                TerrainSalt,
                "Stage 02");
        }

        private static bool[,] BuildWalkableLayout()
        {
            int width = MapMaxX - MapMinX + 1;
            int height = MapMaxY - MapMinY + 1;
            var walkable = new bool[width, height];
            for (int localY = 0; localY < height; localY++)
            {
                for (int localX = 0; localX < width; localX++)
                {
                    var point = new Vector2(
                        MapMinX + localX,
                        MapMinY + localY);
                    walkable[localX, localY] =
                        DistanceToPath(point) <= RoadHalfWidth;
                }
            }

            return walkable;
        }

        private static void PaintGroundDecals(Tilemap decals)
        {
            decals.ClearAllTiles();
            TileBase[] grass = LoadPropTiles("5_Grass", 6);
            TileBase[] flowers = LoadPropTiles("6_Flower", 12);
            TileBase[] dirt = LoadNamedPropTiles(
                "7_Decor",
                new[] { "Dirt1", "Dirt2", "Dirt3", "Dirt4" });

            for (int y = MapMinY; y <= MapMaxY; y++)
            {
                for (int x = MapMinX; x <= MapMaxX; x++)
                {
                    var point = new Vector2(x, y);
                    if (DistanceToPath(point) < 1.9f ||
                        DistanceToBuildSpot(point) < 1.8f)
                    {
                        continue;
                    }

                    uint hash = Hash(x, y, 7741);
                    TileBase selected = null;
                    if (hash % 100u < 20u)
                    {
                        selected = grass[
                            (int)(hash % (uint)grass.Length)];
                    }
                    else if (hash % 100u < 28u)
                    {
                        selected = flowers[
                            (int)(hash % (uint)flowers.Length)];
                    }
                    else if (hash % 100u < 34u)
                    {
                        selected = dirt[
                            (int)(hash % (uint)dirt.Length)];
                    }

                    if (selected != null)
                    {
                        decals.SetTile(
                            new Vector3Int(x, y, 0),
                            selected);
                    }
                }
            }

            decals.CompressBounds();
        }

        private static Transform CreateDecorations(Transform parent)
        {
            var root = new GameObject("Decorative Biomes");
            root.transform.SetParent(parent);
            CreateDecorationBand(
                root.transform,
                "lower_grove",
                "Switchback Grove",
                new Vector2(9f, 5f),
                -2,
                11,
                913);
            CreateDecorationBand(
                root.transform,
                "middle_grove",
                "Switchback Grove",
                new Vector2(9f, 20f),
                11,
                27,
                1823);
            CreateDecorationBand(
                root.transform,
                "upper_grove",
                "Highland Grove",
                new Vector2(9f, 34f),
                27,
                41,
                2719);
            return root.transform;
        }

        private static void CreateDecorationBand(
            Transform parent,
            string id,
            string profile,
            Vector2 center,
            int minY,
            int maxY,
            int seed)
        {
            var clusterObject = new GameObject(id);
            clusterObject.transform.SetParent(parent);
            clusterObject.transform.position = center;
            FieldDecorationCluster cluster =
                clusterObject.AddComponent<FieldDecorationCluster>();
            cluster.ConfigureAuthoring(
                id,
                profile,
                new Vector2(11f, (maxY - minY) * 0.5f),
                seed);

            Tile[] trees =
            {
                LoadPropTile("7_Decor", "Tree1"),
                LoadPropTile("7_Decor", "Tree2")
            };
            Tile[] bushes = Enumerable.Range(1, 6)
                .Select(index =>
                    LoadPropTile("9_Bush", index.ToString()))
                .ToArray();
            Tile[] stones = Enumerable.Range(1, 16)
                .Select(index =>
                    LoadPropTile("4_Stone", index.ToString()))
                .ToArray();
            Tile[] flowers = Enumerable.Range(1, 12)
                .Select(index =>
                    LoadPropTile("6_Flower", index.ToString()))
                .ToArray();
            Tile[] logs =
            {
                LoadPropTile("7_Decor", "Log1"),
                LoadPropTile("7_Decor", "Log2"),
                LoadPropTile("7_Decor", "Log3"),
                LoadPropTile("7_Decor", "Log4")
            };
            int sequence = 0;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = MapMinX + 1; x <= MapMaxX - 1; x++)
                {
                    uint hash = Hash(x, y, seed);
                    Tile selected;
                    float scale;
                    float pathClearance;
                    float buildSpotClearance;
                    if (hash % 100u < 10u)
                    {
                        selected = trees[
                            (int)(hash % (uint)trees.Length)];
                        scale = selected.name == "Tree2"
                            ? 0.9f
                            : 1f;
                        pathClearance = 2.65f;
                        buildSpotClearance = 2.55f;
                    }
                    else if (hash % 100u < 23u)
                    {
                        selected = bushes[
                            (int)(hash % (uint)bushes.Length)];
                        scale = 0.88f;
                        pathClearance = 2.05f;
                        buildSpotClearance = 2f;
                    }
                    else if (hash % 100u < 31u)
                    {
                        selected = stones[
                            (int)(hash % (uint)stones.Length)];
                        scale = 0.84f;
                        pathClearance = 1.9f;
                        buildSpotClearance = 1.95f;
                    }
                    else if (hash % 100u < 41u)
                    {
                        selected = flowers[
                            (int)(hash % (uint)flowers.Length)];
                        scale = 0.78f;
                        pathClearance = 1.72f;
                        buildSpotClearance = 1.8f;
                    }
                    else if (hash % 100u < 47u)
                    {
                        selected = logs[
                            (int)(hash % (uint)logs.Length)];
                        scale = 0.88f;
                        pathClearance = 1.95f;
                        buildSpotClearance = 2f;
                    }
                    else
                    {
                        continue;
                    }

                    Vector2 position = SnapToPixel(
                        new Vector2(
                            x + (((hash >> 8) & 1u) == 0u
                                ? -0.25f
                                : 0.25f),
                            y + (((hash >> 9) & 1u) == 0u
                                ? -0.125f
                                : 0.125f)));
                    if (DistanceToPath(position) < pathClearance ||
                        DistanceToBuildSpot(position) <
                        buildSpotClearance)
                    {
                        continue;
                    }

                    CreateDecoration(
                        clusterObject.transform,
                        id + "_" + sequence.ToString("000"),
                        selected.sprite,
                        position,
                        scale,
                        (hash & 2u) != 0u);
                    sequence++;
                }
            }
        }

        private static void CreateDecoration(
            Transform parent,
            string name,
            Sprite sprite,
            Vector2 position,
            float scale,
            bool flipX)
        {
            var decoration = new GameObject(name);
            decoration.transform.SetParent(parent);
            decoration.transform.position = position;
            decoration.transform.localScale = Vector3.one * scale;
            SpriteRenderer renderer =
                decoration.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.flipX = flipX;
            WorldSortingLayers.Apply(
                renderer,
                WorldSortingLayers.Object);
            renderer.sortingOrder =
                DecorationSortingBase -
                Mathf.RoundToInt(position.y * 100f);
        }

        internal static TowerBuildSiteView[] ReplaceBuildSites(
            Scene scene,
            Transform stageRoot,
            Vector2[] buildSpots,
            int[] buildSpotUnlockCosts,
            string stageLabel)
        {
            if (buildSpots == null || buildSpotUnlockCosts == null ||
                buildSpots.Length == 0 ||
                buildSpots.Length != buildSpotUnlockCosts.Length)
            {
                throw new ArgumentException(
                    stageLabel + " build-site data is invalid.");
            }

            Transform existing = stageRoot.Find("Tower Build Sites");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var root = new GameObject("Tower Build Sites");
            root.transform.SetParent(stageRoot);
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    BuildSitePrefabPath);
            Sprite available =
                AssetDatabase.LoadAssetAtPath<Sprite>(
                    BuildSiteSpriteRoot + "/PlaceForTower1.png");
            Sprite locked =
                AssetDatabase.LoadAssetAtPath<Sprite>(
                    BuildSiteSpriteRoot + "/PlaceForTower2.png");
            if (prefab == null || available == null || locked == null)
            {
                throw new InvalidOperationException(
                    stageLabel + " build-site assets are missing.");
            }

            var sites = new TowerBuildSiteView[buildSpots.Length];
            for (int i = 0; i < buildSpots.Length; i++)
            {
                var instance = (GameObject)PrefabUtility
                    .InstantiatePrefab(prefab, scene);
                instance.name = "Build Site " + i.ToString("00");
                instance.transform.SetParent(root.transform);
                instance.transform.position = buildSpots[i];
                TowerBuildSiteView view =
                    instance.GetComponent<TowerBuildSiteView>();
                view.ConfigureAuthoring(
                    i,
                    buildSpotUnlockCosts[i],
                    available,
                    locked,
                    buildSpotUnlockCosts[i] == 0);
                sites[i] = view;
            }

            return sites;
        }

        private static TileBase[] LoadPropTiles(
            string group,
            int count)
        {
            var result = new TileBase[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = LoadPropTile(
                    group,
                    (i + 1).ToString());
            }

            return result;
        }

        private static TileBase[] LoadNamedPropTiles(
            string group,
            string[] names)
        {
            var result = new TileBase[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                result[i] = LoadPropTile(group, names[i]);
            }

            return result;
        }

        private static Tile LoadPropTile(
            string group,
            string name)
        {
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(
                PropTileRoot + "/" + group + "/" + name + ".asset");
            if (tile == null || tile.sprite == null)
            {
                throw new InvalidOperationException(
                    "Missing generated prop tile: " +
                    group +
                    "/" +
                    name);
            }

            return tile;
        }

        private static void EnsureStageInBuildSettings()
        {
            EditorBuildSettingsScene[] scenes =
                EditorBuildSettings.scenes;
            if (scenes.Any(entry =>
                    string.Equals(
                        entry.path,
                        StageTwoScenePath,
                        StringComparison.Ordinal)))
            {
                return;
            }

            var updated = new EditorBuildSettingsScene[scenes.Length + 1];
            Array.Copy(scenes, updated, scenes.Length);
            updated[scenes.Length] =
                new EditorBuildSettingsScene(
                    StageTwoScenePath,
                    true);
            EditorBuildSettings.scenes = updated;
        }

        private static void ValidateStageTwo()
        {
            SceneAsset sceneAsset =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    StageTwoScenePath);
            TextAsset content =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    StageTwoContentPath);
            if (sceneAsset == null || content == null)
            {
                throw new InvalidOperationException(
                    "Stage 02 scene or content is missing.");
            }

            Scene activeBefore = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(StageTwoScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened)
            {
                scene = EditorSceneManager.OpenScene(
                    StageTwoScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                FieldStageMap stageMap =
                    FindSingle<FieldStageMap>(scene);
                StageOneBattleController controller =
                    FindSingle<StageOneBattleController>(scene);
                Vector2[] authoredPath =
                    stageMap.Path.GetLocalWaypointsCopy();
                if (stageMap.BuildSiteCount != BuildSpots.Length ||
                    authoredPath.Length != PathPoints.Length ||
                    CountTurns(authoredPath) != 12)
                {
                    throw new InvalidOperationException(
                        "Stage 02 route or build-site count is incomplete.");
                }

                FieldStageDecorationPipeline.Validate(
                    stageMap.DecorationRoot,
                    FieldStageDecorationSpecs.CreateStageTwo(
                        PathPoints,
                        BuildSpots));

                Bounds bounds = stageMap.Terrain
                    .GetComponent<TilemapRenderer>().bounds;
                if (bounds.size.y < bounds.size.x * 1.75f)
                {
                    throw new InvalidOperationException(
                        "Stage 02 must remain vertically elongated.");
                }

                for (int i = 0; i < PathPoints.Length; i++)
                {
                    if ((authoredPath[i] - PathPoints[i]).sqrMagnitude >
                        0.0001f ||
                        stageMap.NavigationMask.IsBlocked(PathPoints[i]))
                    {
                        throw new InvalidOperationException(
                            "Stage 02 path mismatch at waypoint " + i + ".");
                    }
                }

                for (int i = 0; i < BuildSpots.Length; i++)
                {
                    TowerBuildSiteView site = stageMap.GetBuildSite(i);
                    if (site.BuildPointIndex != i ||
                        (site.transform.position -
                         (Vector3)BuildSpots[i]).sqrMagnitude > 0.0001f)
                    {
                        throw new InvalidOperationException(
                            "Stage 02 build site mismatch at index " + i +
                            ".");
                    }
                }

                if (controller.StageMap != stageMap ||
                    controller.PresentationCatalog == null ||
                    controller.PresentationCatalog.ContentJson != content)
                {
                    throw new InvalidOperationException(
                        "Stage 02 gameplay wiring is incomplete.");
                }

                if (!StartingCardIds.SequenceEqual(
                        StageContentAuthoring.ReadStartingCards(content)))
                {
                    throw new InvalidOperationException(
                        "Stage 02 starting cards do not match its authored role.");
                }
            }
            finally
            {
                if (opened && scene.IsValid())
                {
                    EditorSceneManager.CloseScene(scene, true);
                }

                if (activeBefore.IsValid() && activeBefore.isLoaded)
                {
                    SceneManager.SetActiveScene(activeBefore);
                }
            }
        }

        private static int CountTurns(Vector2[] points)
        {
            int turns = 0;
            for (int i = 1; i < points.Length - 1; i++)
            {
                Vector2 before =
                    (points[i] - points[i - 1]).normalized;
                Vector2 after =
                    (points[i + 1] - points[i]).normalized;
                if (Mathf.Abs(Vector2.Dot(before, after)) < 0.999f)
                {
                    turns++;
                }
            }

            return turns;
        }

        private static float DistanceToPath(Vector2 point)
        {
            float result = float.MaxValue;
            for (int i = 0; i < PathPoints.Length - 1; i++)
            {
                result = Mathf.Min(
                    result,
                    DistanceToSegment(
                        point,
                        PathPoints[i],
                        PathPoints[i + 1]));
            }

            return result;
        }

        private static float DistanceToBuildSpot(Vector2 point)
        {
            float result = float.MaxValue;
            for (int i = 0; i < BuildSpots.Length; i++)
            {
                result = Mathf.Min(
                    result,
                    Vector2.Distance(point, BuildSpots[i]));
            }

            return result;
        }

        private static float DistanceToSegment(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
            {
                return Vector2.Distance(point, start);
            }

            float t = Mathf.Clamp01(
                Vector2.Dot(point - start, segment) /
                lengthSquared);
            return Vector2.Distance(
                point,
                start + segment * t);
        }

        private static uint Hash(int x, int y, int seed)
        {
            return unchecked(
                (uint)(x * 73856093) ^
                (uint)(y * 19349663) ^
                (uint)(seed * 83492791));
        }

        private static Vector2 SnapToPixel(Vector2 point)
        {
            return new Vector2(
                Mathf.Round(point.x * 32f) / 32f,
                Mathf.Round(point.y * 32f) / 32f);
        }

        private static T FindSingle<T>(Scene scene)
            where T : Component
        {
            T[] found = scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<T>(true))
                .ToArray();
            if (found.Length != 1)
            {
                throw new InvalidOperationException(
                    StageTwoScenePath +
                    " expected exactly one " +
                    typeof(T).Name +
                    " but found " +
                    found.Length +
                    ".");
            }

            return found[0];
        }
    }
}
#endif
