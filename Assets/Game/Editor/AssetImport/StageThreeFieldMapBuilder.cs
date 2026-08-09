#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using RuleforgeTD.Battle;
using RuleforgeTD.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace RuleforgeTD.Editor.AssetImport
{
    /// <summary>
    /// Authors Stage03 as a wide bridge-march battlefield. It reuses shared
    /// Fields assets and battle presentation, but owns its map, content,
    /// starter cards, catalog and deterministic seed.
    /// </summary>
    public static class StageThreeFieldMapBuilder
    {
        internal const int FirstWaveEnemyCount = 24;
        internal const int FirstWaveIntervalTicks = 30;

        public const string StageThreeScenePath =
            "Assets/Game/Scenes/Battle/Stage03.unity";
        public const string StageThreeContentPath =
            "Assets/Game/Data/Logic/stage03-content.json";
        public const string StageThreeCatalogPath =
            "Assets/Game/Data/AssetCatalog/" +
            "StageThreePresentationCatalog.asset";

        internal static readonly string[] StartingCardIds =
        {
            "ricochet",
            "bleed",
            "knockback",
            "shock"
        };

        private const int MapMinX = -4;
        private const int MapMaxX = 43;
        private const int MapMinY = -4;
        private const int MapMaxY = 20;
        private const float RoadHalfWidth = 1.35f;
        private const int TerrainSalt = 30720809;
        private const ulong StageThreeSeed = 32345UL;

        private static readonly Vector2[] PathPoints =
        {
            new Vector2(-2f, 8f),
            new Vector2(6f, 8f),
            new Vector2(6f, 2f),
            new Vector2(16f, 2f),
            new Vector2(16f, 15f),
            new Vector2(25f, 15f),
            new Vector2(25f, 5f),
            new Vector2(34f, 5f),
            new Vector2(34f, 17f),
            new Vector2(41f, 17f)
        };

        private static readonly Vector2[] BuildSpots =
        {
            new Vector2(1f, 11f),
            new Vector2(2f, 4f),
            new Vector2(10f, 5f),
            new Vector2(11f, 11f),
            new Vector2(19f, 11f),
            new Vector2(20f, 18f),
            new Vector2(28f, 11f),
            new Vector2(29f, 2f),
            new Vector2(37f, 11f),
            new Vector2(38f, 14f),
            new Vector2(14f, 18f),
            new Vector2(23f, 2f)
        };

        private static readonly int[] BuildSpotUnlockCosts =
        {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
        };

        [MenuItem("Ruleforge TD/Scenes/Rebuild Stage 03 (Overwrite)")]
        public static void RebuildStageThreeFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Rebuild Stage 03",
                    "This replaces Stage03 with the wide bridge-march map.",
                    "Rebuild",
                    "Cancel"))
            {
                return;
            }

            RebuildStageThreeFromCommandLine();
        }

        public static void RebuildStageThreeFromCommandLine()
        {
            StageTwoFieldMapBuilder
                .EnsureSharedFieldAssetsForStageBuilders();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    StageTwoFieldMapBuilder.StageTwoScenePath) == null)
            {
                StageTwoFieldMapBuilder.RebuildStageTwoFromCommandLine();
            }

            StageContentAuthoring.Create(
                StageThreeContentPath,
                PathPoints,
                BuildSpots,
                BuildSpotUnlockCosts,
                StartingCardIds,
                FirstWaveEnemyCount,
                FirstWaveIntervalTicks);
            StageOnePresentationCatalog catalog =
                StageTwoFieldMapBuilder.CreateStageCatalog(
                    StageThreeCatalogPath,
                    StageThreeContentPath,
                    "Stage 03");
            CreateStageThreeScene(catalog);
            EnsureStageInBuildSettings();
            ValidateStageThree();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                "RULEFORGE_STAGE03_BUILD_OK waypoints=" +
                PathPoints.Length +
                " turns=" +
                CountTurns(PathPoints) +
                " buildSites=" +
                BuildSpots.Length +
                " scene=" +
                StageThreeScenePath +
                " seed=" +
                StageThreeSeed);
        }

        public static void ValidateStageThreeFromCommandLine()
        {
            ValidateStageThree();
        }

        private static void CreateStageThreeScene(
            StageOnePresentationCatalog catalog)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    StageThreeScenePath) == null)
            {
                if (!AssetDatabase.CopyAsset(
                        StageTwoFieldMapBuilder.StageTwoScenePath,
                        StageThreeScenePath))
                {
                    throw new InvalidOperationException(
                        "Unity could not create the Stage03 scene baseline.");
                }
            }
            else
            {
                // Keep the existing Stage03 GUID stable across generated builds.
                File.Copy(
                    StageTwoFieldMapBuilder.StageTwoScenePath,
                    StageThreeScenePath,
                    true);
            }

            AssetDatabase.ImportAsset(
                StageThreeScenePath,
                ImportAssetOptions.ForceSynchronousImport);
            Scene scene = EditorSceneManager.OpenScene(
                StageThreeScenePath,
                OpenSceneMode.Single);
            FieldStageMap stageMap = FindSingle<FieldStageMap>(scene);
            stageMap.gameObject.name = "Stage 03 - Bridge March";

            Tilemap terrain = stageMap.Terrain;
            Tilemap decals = stageMap.GroundDecals;
            FieldStageTerrainPipeline.Paint(
                terrain,
                MapMinX,
                MapMaxX,
                MapMinY,
                MapMaxY,
                RoadHalfWidth,
                PathPoints,
                TerrainSalt,
                "Stage 03");

            Transform oldDecorations = stageMap.DecorationRoot;
            FieldStageDecorationSpec decorationSpec =
                FieldStageDecorationSpecs.CreateStageThree(
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
            TowerBuildSiteView[] sites =
                StageTwoFieldMapBuilder.ReplaceBuildSites(
                    scene,
                    stageMap.transform,
                    BuildSpots,
                    BuildSpotUnlockCosts,
                    "Stage 03");
            stageMap.ConfigureAuthoring(
                terrain,
                decals,
                decorationRoot,
                navigation,
                path,
                sites);

            Camera camera = FindSingle<Camera>(scene);
            camera.transform.position =
                new Vector3(PathPoints[0].x, PathPoints[0].y, -10f);
            camera.orthographicSize = 7f;

            StageOneBattleController controller =
                StageOneGameplaySceneInstaller.EnsureInstalled(
                    scene,
                    stageMap);
            controller.ConfigureAuthoring(
                stageMap,
                catalog,
                StageThreeSeed);

            EditorUtility.SetDirty(stageMap);
            EditorUtility.SetDirty(path);
            EditorUtility.SetDirty(navigation);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, StageThreeScenePath))
            {
                throw new InvalidOperationException(
                    "Unity could not save Stage03.");
            }
        }

        private static void EnsureStageInBuildSettings()
        {
            EditorBuildSettingsScene[] scenes =
                EditorBuildSettings.scenes;
            if (scenes.Any(entry =>
                    string.Equals(
                        entry.path,
                        StageThreeScenePath,
                        StringComparison.Ordinal)))
            {
                return;
            }

            var updated = new EditorBuildSettingsScene[scenes.Length + 1];
            Array.Copy(scenes, updated, scenes.Length);
            updated[scenes.Length] =
                new EditorBuildSettingsScene(
                    StageThreeScenePath,
                    true);
            EditorBuildSettings.scenes = updated;
        }

        private static void ValidateStageThree()
        {
            SceneAsset sceneAsset =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    StageThreeScenePath);
            TextAsset content =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    StageThreeContentPath);
            StageOnePresentationCatalog catalog =
                AssetDatabase.LoadAssetAtPath<
                    StageOnePresentationCatalog>(
                    StageThreeCatalogPath);
            if (sceneAsset == null || content == null || catalog == null)
            {
                throw new InvalidOperationException(
                    "Stage03 scene, content or catalog is missing.");
            }

            Scene activeBefore = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(StageThreeScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened)
            {
                scene = EditorSceneManager.OpenScene(
                    StageThreeScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                FieldStageMap stageMap = FindSingle<FieldStageMap>(scene);
                StageOneBattleController controller =
                    FindSingle<StageOneBattleController>(scene);
                Vector2[] authoredPath =
                    stageMap.Path.GetLocalWaypointsCopy();
                if (stageMap.BuildSiteCount != BuildSpots.Length ||
                    authoredPath.Length != PathPoints.Length ||
                    CountTurns(authoredPath) != 8)
                {
                    throw new InvalidOperationException(
                        "Stage03 route or build-site count is incomplete.");
                }

                FieldStageDecorationPipeline.Validate(
                    stageMap.DecorationRoot,
                    FieldStageDecorationSpecs.CreateStageThree(
                        PathPoints,
                        BuildSpots));

                Bounds bounds = stageMap.Terrain
                    .GetComponent<TilemapRenderer>().bounds;
                if (bounds.size.x < bounds.size.y * 1.75f)
                {
                    throw new InvalidOperationException(
                        "Stage03 must remain horizontally elongated.");
                }

                for (int i = 0; i < PathPoints.Length; i++)
                {
                    if ((authoredPath[i] - PathPoints[i]).sqrMagnitude >
                            0.0001f ||
                        stageMap.NavigationMask.IsBlocked(PathPoints[i]))
                    {
                        throw new InvalidOperationException(
                            "Stage03 path mismatch at waypoint " + i + ".");
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
                            "Stage03 build site mismatch at index " + i + ".");
                    }
                }

                if (controller.StageMap != stageMap ||
                    controller.PresentationCatalog != catalog ||
                    catalog.ContentJson != content)
                {
                    throw new InvalidOperationException(
                        "Stage03 gameplay wiring is incomplete.");
                }
                if (!StartingCardIds.SequenceEqual(
                        StageContentAuthoring.ReadStartingCards(content)))
                {
                    throw new InvalidOperationException(
                        "Stage03 starting cards do not match its authored role.");
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

        private static T FindSingle<T>(Scene scene)
            where T : Component
        {
            T[] matches = scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<T>(true))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    "Stage03 expected exactly one " +
                    typeof(T).Name +
                    ", found " +
                    matches.Length +
                    ".");
            }

            return matches[0];
        }
    }
}
#endif
