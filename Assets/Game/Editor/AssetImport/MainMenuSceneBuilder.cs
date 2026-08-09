#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using RuleforgeTD.Battle;
using RuleforgeTD.UI;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleforgeTD.Editor.AssetImport
{
    /// <summary>
    /// Generates the stage-selection entry scene and publishes a multi-scene
    /// WebGL build containing the menu plus both authored battle stages.
    /// </summary>
    public static class MainMenuSceneBuilder
    {
        public const string MainMenuScenePath =
            "Assets/Game/Scenes/MainMenu/MainMenu.unity";
        public const string WebGLBuildPath =
            "Builds/WebGL/RuleforgeTD";

        private const string LocalizationPath =
            "Assets/Game/Resources/RuleforgeTD/MainMenuKo.json";
        private const string BattleLocalizationPath =
            "Assets/Game/Data/Localization/stage01-ko.json";
        private const string StageOneContentPath =
            "Assets/Game/Data/Logic/phase1-content.json";
        private const string WorldMapBackgroundPath =
            "Assets/Game/Art/UI/CampaignWorldMap.png";
        private const string CampaignFontPath =
            "Assets/Game/Runtime/UI/Fonts/RuleforgeCampaign.ttf";
        private const string StagingRootPath =
            "Builds/WebGL/.RuleforgeTD-staging";
        private const string StagingBuildPath =
            StagingRootPath + "/RuleforgeTD";
        private const string PreviousBuildPath =
            "Builds/WebGL/.RuleforgeTD-previous";

        private static readonly string[] PlayableScenes =
        {
            MainMenuScenePath,
            CraftPixFieldTilemapAssetBuilder.StageOneScenePath,
            StageTwoFieldMapBuilder.StageTwoScenePath,
            StageThreeFieldMapBuilder.StageThreeScenePath
        };

        [MenuItem("Ruleforge TD/Scenes/Open Main Menu")]
        public static void OpenMainMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    MainMenuScenePath) == null)
            {
                RebuildMainMenuFromCommandLine();
            }

            EditorSceneManager.OpenScene(
                MainMenuScenePath,
                OpenSceneMode.Single);
        }

        [MenuItem("Ruleforge TD/Scenes/Rebuild Main Menu (Overwrite)")]
        public static void RebuildMainMenuFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Rebuild Main Menu",
                    "This replaces the generated stage-selection scene.",
                    "Rebuild",
                    "Cancel"))
            {
                return;
            }

            RebuildMainMenuFromCommandLine();
        }

        public static void RebuildMainMenuFromCommandLine()
        {
            EnsureFolder("Assets/Game/Scenes/MainMenu");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    StageTwoFieldMapBuilder.StageTwoScenePath) == null ||
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    StageTwoFieldMapBuilder.StageTwoContentPath) == null)
            {
                StageTwoFieldMapBuilder.RebuildStageTwoFromCommandLine();
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    StageThreeFieldMapBuilder.StageThreeScenePath) == null ||
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    StageThreeFieldMapBuilder.StageThreeContentPath) == null)
            {
                StageThreeFieldMapBuilder
                    .RebuildStageThreeFromCommandLine();
            }

            CreateMainMenuScene();
            EnsurePlayableScenesInBuildSettings();
            ValidateMainMenu();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                "RULEFORGE_MAIN_MENU_BUILD_OK scene=" +
                MainMenuScenePath +
                " displayedStages=15 playableStages=3");
        }

        [MenuItem("Ruleforge TD/Build/Build Complete Game (WebGL)")]
        public static void BuildWebGLFromCommandLine()
        {
            StageTwoFieldMapBuilder.RebuildStageTwoFromCommandLine();
            StageThreeFieldMapBuilder
                .RebuildStageThreeFromCommandLine();
            RebuildMainMenuFromCommandLine();
            if (Directory.Exists(StagingRootPath))
            {
                Directory.Delete(StagingRootPath, true);
            }

            Directory.CreateDirectory(StagingBuildPath);
            PlayerSettings.WebGL.compressionFormat =
                WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.template =
                "PROJECT:RuleforgeFullscreen";

            var options = new BuildPlayerOptions
            {
                scenes = PlayableScenes,
                locationPathName = StagingBuildPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    "Complete game WebGL build failed with result " +
                    summary.result +
                    " and " +
                    summary.totalErrors +
                    " error(s).");
            }

            PublishBuild();
            Debug.Log(
                "RULEFORGE_COMPLETE_WEBGL_BUILD_OK path=" +
                WebGLBuildPath +
                " scenes=" +
                PlayableScenes.Length +
                " bytes=" +
                summary.totalSize);
        }

        public static void ValidateMainMenuFromCommandLine()
        {
            ValidateMainMenu();
        }

        private static void CreateMainMenuScene()
        {
            Sprite worldMapBackground =
                EnsureWorldMapBackgroundSprite();
            TextAsset localization =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    LocalizationPath);
            TextAsset battleLocalization =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    BattleLocalizationPath);
            TextAsset stageOneContent =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    StageOneContentPath);
            TextAsset stageTwoContent =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    StageTwoFieldMapBuilder.StageTwoContentPath);
            TextAsset stageThreeContent =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    StageThreeFieldMapBuilder.StageThreeContentPath);
            Font campaignFont =
                AssetDatabase.LoadAssetAtPath<Font>(
                    CampaignFontPath);
            if (localization == null ||
                battleLocalization == null ||
                stageOneContent == null ||
                stageTwoContent == null ||
                stageThreeContent == null ||
                campaignFont == null ||
                worldMapBackground == null)
            {
                throw new InvalidOperationException(
                    "Main menu localization, campaign font, or world map " +
                    "background is missing.");
            }

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            scene.name = "MainMenu";
            var root = new GameObject("Main Menu");
            StageSelectionMenu menu =
                root.AddComponent<StageSelectionMenu>();
            menu.ConfigureAuthoring(
                localization,
                battleLocalization,
                stageOneContent,
                stageTwoContent,
                stageThreeContent,
                campaignFont,
                worldMapBackground,
                "Stage01",
                "Stage02",
                "Stage03");
            EditorUtility.SetDirty(menu);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(
                    scene,
                    MainMenuScenePath))
            {
                throw new InvalidOperationException(
                    "Unity could not save the main menu scene.");
            }
        }

        private static void EnsurePlayableScenesInBuildSettings()
        {
            EditorBuildSettingsScene[] existing =
                EditorBuildSettings.scenes;
            EditorBuildSettingsScene[] updated =
                PlayableScenes
                    .Select(path =>
                        new EditorBuildSettingsScene(path, true))
                    .Concat(existing.Where(scene =>
                        !PlayableScenes.Contains(
                            scene.path,
                            StringComparer.Ordinal)))
                    .ToArray();
            EditorBuildSettings.scenes = updated;
        }

        private static void ValidateMainMenu()
        {
            for (int i = 0; i < PlayableScenes.Length; i++)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                        PlayableScenes[i]) == null)
                {
                    throw new InvalidOperationException(
                        "Playable scene is missing: " +
                        PlayableScenes[i]);
                }
            }

            Scene activeBefore = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(MainMenuScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened)
            {
                scene = EditorSceneManager.OpenScene(
                    MainMenuScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                StageSelectionMenu[] menus = scene
                    .GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<
                            StageSelectionMenu>(true))
                    .ToArray();
                if (menus.Length != 1 ||
                    menus[0].TextData == null ||
                    menus[0].BattleTextData == null ||
                    menus[0].StageOneContent == null ||
                    menus[0].StageTwoContent == null ||
                    menus[0].StageThreeContent == null ||
                    menus[0].UiFont == null ||
                    menus[0].WorldMapBackground == null ||
                    menus[0].StageOneSceneName != "Stage01" ||
                    menus[0].StageTwoSceneName != "Stage02" ||
                    menus[0].StageThreeSceneName != "Stage03" ||
                    menus[0].DisplayedStageCount != 15)
                {
                    throw new InvalidOperationException(
                        "Main menu scene wiring is incomplete.");
                }

                string[] stageOneCards =
                    StageContentAuthoring.ReadStartingCards(
                        menus[0].StageOneContent);
                string[] stageTwoCards =
                    StageContentAuthoring.ReadStartingCards(
                        menus[0].StageTwoContent);
                string[] stageThreeCards =
                    StageContentAuthoring.ReadStartingCards(
                        menus[0].StageThreeContent);
                if (stageOneCards.SequenceEqual(stageTwoCards) ||
                    stageOneCards.SequenceEqual(stageThreeCards) ||
                    stageTwoCards.SequenceEqual(stageThreeCards))
                {
                    throw new InvalidOperationException(
                        "Each playable stage needs a distinct starter loadout.");
                }

                EditorBuildSettingsScene[] settings =
                    EditorBuildSettings.scenes;
                for (int i = 0; i < PlayableScenes.Length; i++)
                {
                    if (i >= settings.Length ||
                        !settings[i].enabled ||
                        !string.Equals(
                            settings[i].path,
                            PlayableScenes[i],
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Playable scenes must lead build settings in " +
                            "menu, Stage01, Stage02, Stage03 order.");
                    }
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

        private static void PublishBuild()
        {
            if (!Directory.Exists(StagingBuildPath))
            {
                throw new DirectoryNotFoundException(
                    "Complete game WebGL staging build is missing.");
            }

            if (Directory.Exists(PreviousBuildPath))
            {
                Directory.Delete(PreviousBuildPath, true);
            }

            bool movedPrevious = false;
            try
            {
                if (Directory.Exists(WebGLBuildPath))
                {
                    Directory.Move(
                        WebGLBuildPath,
                        PreviousBuildPath);
                    movedPrevious = true;
                }

                Directory.Move(StagingBuildPath, WebGLBuildPath);
                if (Directory.Exists(StagingRootPath))
                {
                    Directory.Delete(StagingRootPath, true);
                }

                if (movedPrevious &&
                    Directory.Exists(PreviousBuildPath))
                {
                    Directory.Delete(PreviousBuildPath, true);
                }
            }
            catch
            {
                if (!Directory.Exists(WebGLBuildPath) &&
                    movedPrevious &&
                    Directory.Exists(PreviousBuildPath))
                {
                    Directory.Move(
                        PreviousBuildPath,
                        WebGLBuildPath);
                }

                throw;
            }
        }

        private static Sprite EnsureWorldMapBackgroundSprite()
        {
            if (!File.Exists(WorldMapBackgroundPath))
            {
                throw new FileNotFoundException(
                    "Campaign world-map background is missing.",
                    WorldMapBackgroundPath);
            }

            AssetDatabase.ImportAsset(
                WorldMapBackgroundPath,
                ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer =
                AssetImporter.GetAtPath(WorldMapBackgroundPath)
                    as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    "Campaign world-map background is not a texture.");
            }

            bool changed =
                importer.textureType != TextureImporterType.Sprite ||
                importer.mipmapEnabled ||
                importer.alphaSource !=
                    TextureImporterAlphaSource.None ||
                importer.maxTextureSize != 2048 ||
                importer.filterMode != FilterMode.Bilinear;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.maxTextureSize = 2048;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression =
                TextureImporterCompression.Compressed;
            if (changed)
            {
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(
                WorldMapBackgroundPath);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)
                ?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(parent) ||
                string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException(
                    "Invalid asset folder path: " + path);
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
