#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using RuleforgeTD.Battle;
using RuleforgeTD.Maps;
using RuleforgeTD.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleforgeTD.Editor.AssetImport
{
    /// <summary>
    /// Connects the generated Stage01 map to its playable presentation layer.
    /// This installer is intentionally idempotent so map regeneration and
    /// command-line authoring both produce the same scene wiring.
    /// </summary>
    public static class StageOneGameplaySceneInstaller
    {
        public const string CatalogPath =
            "Assets/Game/Data/AssetCatalog/" +
            "StageOnePresentationCatalog.asset";

        public const ulong AuthoredSeed = 12345UL;

        private const string ContentPath =
            "Assets/Game/Data/Logic/phase1-content.json";
        private const string LocalizationPath =
            "Assets/Game/Data/Localization/stage01-ko.json";
        private const string UiFontPath =
            "Assets/Game/Runtime/UI/Fonts/" +
            "RuleforgeStageOne.ttf";
        private const string TowerPrefabPathFormat =
            "Assets/Game/Prefabs/Towers/Archer/" +
            "ArcherTower_Level{0:00}.prefab";
        private const string EnemyPrefabRoot =
            "Assets/Game/Prefabs/Enemies";
        private const string ArrowRoot =
            "Assets/ThirdParty/CraftPix/Raw/Towers/Archer/" +
            "Units/Arrow";
        private const string GameplayRootName = "Gameplay";

        // Canonical Stage 01 presentation scales. These are final authored
        // defaults rather than chained adjustment factors. Build-site scale
        // and all world-space anchors remain independent.
        // Both the build site and the tower body have a 62 px opaque
        // footprint. Match their world size across 32 PPU and 48 PPU:
        // 1.1 * 48 / 32 = 1.65.
        private const float TowerVisualScale = 1.65f;
        private const float RaiderVisualScale = 1.638f;
        private const float RunnerVisualScale = 1.5561f;
        private const float ArmoredKnightVisualScale = 2.0475f;
        private const float EliteGolemVisualScale = 2.7027f;
        private const float BossGuardianVisualScale = 2.9484f;
        private const float BossSummonerVisualScale = 2.6208f;
        private const float BossTimeWalkerVisualScale = 3.1941f;

        private static readonly string[] DefaultCardProgram =
        {
            "split",
            "burn",
            "poison"
        };

        [MenuItem(
            "Ruleforge TD/Scenes/Install Stage 01 Gameplay")]
        public static void InstallFromMenu()
        {
            if (!EditorSceneManager
                    .SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            InstallFromCommandLine();
        }

        /// <summary>
        /// Batch-mode entry point for installing gameplay into the existing
        /// Stage01 scene without regenerating its map art.
        /// </summary>
        public static void InstallFromCommandLine()
        {
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport);

            SceneAsset sceneAsset =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    CraftPixFieldTilemapAssetBuilder.StageOneScenePath);
            if (sceneAsset == null)
            {
                throw new FileNotFoundException(
                    "Stage01 scene was not found.",
                    CraftPixFieldTilemapAssetBuilder.StageOneScenePath);
            }

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(
                CraftPixFieldTilemapAssetBuilder.StageOneScenePath);
            bool wasAlreadyLoaded = scene.IsValid() && scene.isLoaded;
            bool openedInSingleMode = false;

            if (!wasAlreadyLoaded)
            {
                openedInSingleMode =
                    CanReplaceUntitledScene(previousActiveScene);
                scene = EditorSceneManager.OpenScene(
                    CraftPixFieldTilemapAssetBuilder.StageOneScenePath,
                    openedInSingleMode
                        ? OpenSceneMode.Single
                        : OpenSceneMode.Additive);
            }

            try
            {
                FieldStageMap stageMap = FindSingleStageMap(scene);
                EnsureInstalled(scene, stageMap);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(
                        scene,
                        CraftPixFieldTilemapAssetBuilder
                            .StageOneScenePath))
                {
                    throw new InvalidOperationException(
                        "Unity could not save the installed Stage01 scene.");
                }

                AssetDatabase.SaveAssets();
                Debug.Log(
                    "RULEFORGE_STAGE01_GAMEPLAY_INSTALL_OK scene=" +
                    CraftPixFieldTilemapAssetBuilder.StageOneScenePath +
                    " catalog=" +
                    CatalogPath);
            }
            finally
            {
                RestoreEditorSceneState(
                    previousActiveScene,
                    scene,
                    wasAlreadyLoaded || openedInSingleMode);
            }
        }

        /// <summary>
        /// Adds or refreshes the gameplay root in an already loaded Stage01
        /// scene. Map generators call this before their first scene save.
        /// </summary>
        public static StageOneBattleController EnsureInstalled(
            Scene scene,
            FieldStageMap stageMap)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new ArgumentException(
                    "Stage01 must be a valid, loaded scene.",
                    nameof(scene));
            }

            if (stageMap == null ||
                stageMap.gameObject.scene != scene)
            {
                throw new ArgumentException(
                    "The FieldStageMap must belong to the target scene.",
                    nameof(stageMap));
            }

            StageOnePresentationCatalog catalog =
                EnsurePresentationCatalog();
            GameObject gameplayRoot = FindGameplayRoot(scene);
            StageOneBattleController controller =
                FindSingleController(scene);

            if (gameplayRoot == null)
            {
                if (controller != null &&
                    controller.transform.parent == null)
                {
                    gameplayRoot = controller.gameObject;
                    gameplayRoot.name = GameplayRootName;
                }
                else
                {
                    gameplayRoot =
                        new GameObject(GameplayRootName);
                    SceneManager.MoveGameObjectToScene(
                        gameplayRoot,
                        scene);
                }
            }

            if (controller == null)
            {
                controller =
                    gameplayRoot.AddComponent<
                        StageOneBattleController>();
            }
            else if (controller.gameObject != gameplayRoot &&
                     controller.transform.parent == null)
            {
                controller.transform.SetParent(
                    gameplayRoot.transform,
                    false);
            }

            controller.ConfigureAuthoring(
                stageMap,
                catalog,
                AuthoredSeed);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(gameplayRoot);
            return controller;
        }

        public static StageOnePresentationCatalog
            EnsurePresentationCatalog()
        {
            EnsureAssetFolder(
                Path.GetDirectoryName(CatalogPath)
                    ?.Replace('\\', '/'));

            TextAsset content =
                LoadRequiredAsset<TextAsset>(ContentPath);
            TextAsset localization =
                LoadRequiredAsset<TextAsset>(LocalizationPath);
            Font uiFont =
                LoadRequiredAsset<Font>(UiFontPath);
            ValidateLocalizationFontCoverage(
                uiFont,
                localization.text);
            GameObject goblin = LoadEnemyPrefab("Goblin");
            GameObject dog = LoadEnemyPrefab("Dog");
            GameObject slime = LoadEnemyPrefab("Slime");
            GameObject bee = LoadEnemyPrefab("Bee");

            var towerBindings =
                new StageOnePrefabBinding[7];
            for (int level = 1; level <= 7; level++)
            {
                GameObject tower =
                    LoadRequiredAsset<GameObject>(
                        string.Format(
                            TowerPrefabPathFormat,
                            level));
                towerBindings[level - 1] =
                    new StageOnePrefabBinding(
                        "ballista",
                        tower,
                        TowerVisualScale,
                        level);
            }
            var enemyBindings =
                new[]
                {
                    new StageOnePrefabBinding(
                        "raider",
                        goblin,
                        RaiderVisualScale),
                    new StageOnePrefabBinding(
                        "runner",
                        dog,
                        RunnerVisualScale),
                    new StageOnePrefabBinding(
                        "armored_knight",
                        goblin,
                        ArmoredKnightVisualScale),
                    new StageOnePrefabBinding(
                        "elite_golem",
                        slime,
                        EliteGolemVisualScale),
                    new StageOnePrefabBinding(
                        "boss_guardian",
                        goblin,
                        BossGuardianVisualScale),
                    new StageOnePrefabBinding(
                        "boss_summoner",
                        bee,
                        BossSummonerVisualScale),
                    new StageOnePrefabBinding(
                        "boss_time_walker",
                        slime,
                        BossTimeWalkerVisualScale)
                };

            var projectileSprites = new Sprite[5];
            for (int i = 0; i < projectileSprites.Length; i++)
            {
                int numericName = 23 + i;
                projectileSprites[i] =
                    LoadRequiredAsset<Sprite>(
                        ArrowRoot +
                        "/" +
                        numericName +
                        ".png");
            }

            StageOnePresentationCatalog catalog =
                AssetDatabase.LoadAssetAtPath<
                    StageOnePresentationCatalog>(
                    CatalogPath);
            if (catalog == null)
            {
                catalog =
                    ScriptableObject.CreateInstance<
                        StageOnePresentationCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.ConfigureAuthoring(
                content,
                localization,
                uiFont,
                towerBindings,
                enemyBindings,
                projectileSprites,
                DefaultCardProgram);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        private static void ValidateLocalizationFontCoverage(
            Font font,
            string localizationJson)
        {
            if (font == null ||
                string.IsNullOrEmpty(localizationJson))
            {
                return;
            }

            string missing =
                StageOneUiFontCoverage.FindMissingCharacters(
                    font,
                    localizationJson);

            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    "Stage 01 UI font is missing required glyphs: " +
                    missing);
            }
        }

        private static FieldStageMap FindSingleStageMap(
            Scene scene)
        {
            FieldStageMap[] stageMaps = scene
                .GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<FieldStageMap>(
                        true))
                .ToArray();
            if (stageMaps.Length != 1)
            {
                throw new InvalidOperationException(
                    "Stage01 must contain exactly one FieldStageMap, " +
                    "but found " +
                    stageMaps.Length +
                    ".");
            }

            return stageMaps[0];
        }

        private static GameObject FindGameplayRoot(Scene scene)
        {
            GameObject[] matches = scene
                .GetRootGameObjects()
                .Where(root =>
                    string.Equals(
                        root.name,
                        GameplayRootName,
                        StringComparison.Ordinal))
                .ToArray();
            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    "Stage01 contains more than one Gameplay root.");
            }

            return matches.Length == 1 ? matches[0] : null;
        }

        private static StageOneBattleController
            FindSingleController(Scene scene)
        {
            StageOneBattleController[] controllers = scene
                .GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<
                        StageOneBattleController>(true))
                .ToArray();
            if (controllers.Length > 1)
            {
                throw new InvalidOperationException(
                    "Stage01 contains more than one " +
                    nameof(StageOneBattleController) +
                    ".");
            }

            return controllers.Length == 1
                ? controllers[0]
                : null;
        }

        private static GameObject LoadEnemyPrefab(string name)
        {
            return LoadRequiredAsset<GameObject>(
                EnemyPrefabRoot + "/" + name + ".prefab");
        }

        private static T LoadRequiredAsset<T>(string path)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException(
                    "Required Stage01 asset was not found.",
                    path);
            }

            return asset;
        }

        private static void EnsureAssetFolder(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent =
                Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureAssetFolder(parent);
            string folderName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) ||
                string.IsNullOrEmpty(folderName))
            {
                throw new InvalidOperationException(
                    "Invalid Unity asset folder path: " + path);
            }

            AssetDatabase.CreateFolder(parent, folderName);
        }

        private static bool CanReplaceUntitledScene(
            Scene activeScene)
        {
            if (!activeScene.IsValid() ||
                !string.IsNullOrEmpty(activeScene.path) ||
                SceneManager.sceneCount != 1)
            {
                return false;
            }

            return Application.isBatchMode || !activeScene.isDirty;
        }

        private static void RestoreEditorSceneState(
            Scene previousActiveScene,
            Scene targetScene,
            bool keepTargetSceneLoaded)
        {
            if (previousActiveScene.IsValid() &&
                previousActiveScene.isLoaded &&
                previousActiveScene != targetScene)
            {
                EditorSceneManager.SetActiveScene(
                    previousActiveScene);
            }

            if (!keepTargetSceneLoaded &&
                targetScene.IsValid() &&
                targetScene.isLoaded)
            {
                EditorSceneManager.CloseScene(
                    targetScene,
                    true);
            }
        }
    }
}
#endif
