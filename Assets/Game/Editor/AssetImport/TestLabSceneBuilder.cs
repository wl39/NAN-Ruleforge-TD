#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuleforgeTD.Battle;
using RuleforgeTD.Maps;
using RuleforgeTD.UnityView.TestLab;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleforgeTD.Editor.AssetImport
{
    /// <summary>
    /// Generates the small TestLab bootstrap scene and its standalone WebGL
    /// build. Stage01 remains the single source of truth for map authoring;
    /// TestLab loads that scene additively instead of copying its hierarchy.
    /// </summary>
    public static class TestLabSceneBuilder
    {
        public const string ScenePath =
            "Assets/Game/Scenes/Test/BattleTestLab.unity";
        public const string SourceStageScenePath =
            CraftPixFieldTilemapAssetBuilder.StageOneScenePath;
        public const string WebGLBuildPath =
            "Builds/WebGL/TestLab";
        public const string BootstrapRootName =
            "TestLab Bootstrap";

        private const string WebGLStagingRootPath =
            "Builds/WebGL/.TestLab-staging";
        private const string WebGLStagingBuildPath =
            WebGLStagingRootPath + "/TestLab";
        private const string WebGLPreviousBuildPath =
            "Builds/WebGL/.TestLab-previous";

        [MenuItem(
            "Ruleforge TD/Scenes/Rebuild Test Lab")]
        public static void RebuildFromMenu()
        {
            if (!EditorSceneManager
                    .SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            RebuildFromCommandLine();
        }

        [MenuItem(
            "Ruleforge TD/Scenes/Open Test Lab")]
        public static void OpenFromMenu()
        {
            if (!EditorSceneManager
                    .SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    ScenePath) == null)
            {
                RebuildFromCommandLine();
            }

            EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
        }

        [MenuItem(
            "Ruleforge TD/Scenes/Validate Test Lab")]
        public static void ValidateFromMenu()
        {
            ValidateFromCommandLine();
        }

        public static void RebuildFromCommandLine()
        {
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport);
            EnsureSourceAssets();
            EnsureAssetFolder(
                Path.GetDirectoryName(ScenePath)
                    ?.Replace('\\', '/'));

            Scene previousActiveScene =
                SceneManager.GetActiveScene();
            Scene scene =
                SceneManager.GetSceneByPath(ScenePath);
            bool wasAlreadyLoaded =
                scene.IsValid() && scene.isLoaded;
            bool openedInSingleMode = false;

            if (!wasAlreadyLoaded)
            {
                openedInSingleMode =
                    CanReplaceUntitledScene(
                        previousActiveScene);
                SceneAsset existing =
                    AssetDatabase.LoadAssetAtPath<
                        SceneAsset>(ScenePath);
                scene = existing == null
                    ? EditorSceneManager.NewScene(
                        NewSceneSetup.EmptyScene,
                        openedInSingleMode
                            ? NewSceneMode.Single
                            : NewSceneMode.Additive)
                    : EditorSceneManager.OpenScene(
                        ScenePath,
                        openedInSingleMode
                            ? OpenSceneMode.Single
                            : OpenSceneMode.Additive);
            }

            try
            {
                if (SceneManager.GetActiveScene() != scene &&
                    !EditorSceneManager.SetActiveScene(scene))
                {
                    throw new InvalidOperationException(
                        "Could not make TestLab the active scene.");
                }

                ClearScene(scene);

                var root =
                    new GameObject(BootstrapRootName);
                SceneManager.MoveGameObjectToScene(
                    root,
                    scene);
                TestLabSceneBootstrap bootstrap =
                    root.AddComponent<
                        TestLabSceneBootstrap>();
                bootstrap.ConfigureAuthoring(
                    SourceStageScenePath);
                EditorUtility.SetDirty(bootstrap);
                EditorUtility.SetDirty(root);

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(
                        scene,
                        ScenePath))
                {
                    throw new InvalidOperationException(
                        "Unity could not save the TestLab scene.");
                }

                EnsureScenesInBuildSettings();
                AssetDatabase.SaveAssets();
                Debug.Log(
                    "RULEFORGE_TESTLAB_SCENE_OK scene=" +
                    ScenePath +
                    " source=" +
                    SourceStageScenePath);
            }
            finally
            {
                RestoreEditorSceneState(
                    previousActiveScene,
                    scene,
                    wasAlreadyLoaded ||
                    openedInSingleMode);
            }
        }

        public static void ValidateFromCommandLine()
        {
            EnsureSourceAssets();
            SceneAsset sceneAsset =
                AssetDatabase.LoadAssetAtPath<
                    SceneAsset>(ScenePath);
            if (sceneAsset == null)
            {
                throw new FileNotFoundException(
                    "The generated TestLab scene was not found.",
                    ScenePath);
            }

            Scene previousActiveScene =
                SceneManager.GetActiveScene();
            Scene scene =
                SceneManager.GetSceneByPath(ScenePath);
            bool wasAlreadyLoaded =
                scene.IsValid() && scene.isLoaded;
            bool openedInSingleMode = false;
            if (!wasAlreadyLoaded)
            {
                openedInSingleMode =
                    CanReplaceUntitledScene(
                        previousActiveScene);
                scene = EditorSceneManager.OpenScene(
                    ScenePath,
                    openedInSingleMode
                        ? OpenSceneMode.Single
                        : OpenSceneMode.Additive);
            }

            try
            {
                ValidateSceneContents(scene);
                EnsureScenesInBuildSettings();
                Debug.Log(
                    "RULEFORGE_TESTLAB_VALIDATE_OK scene=" +
                    ScenePath +
                    " source=" +
                    SourceStageScenePath);
            }
            finally
            {
                RestoreEditorSceneState(
                    previousActiveScene,
                    scene,
                    wasAlreadyLoaded ||
                    openedInSingleMode);
            }
        }

        [MenuItem(
            "Ruleforge TD/Build/Build Test Lab (WebGL)")]
        public static void BuildWebGLFromCommandLine()
        {
            // Refresh the canonical map/catalog first. Existing Stage01 art
            // is preserved by its idempotent builder.
            CraftPixFieldTilemapAssetBuilder
                .BuildFromCommandLine();
            RebuildFromCommandLine();
            ValidateFromCommandLine();

            if (Directory.Exists(
                    WebGLStagingRootPath))
            {
                Directory.Delete(
                    WebGLStagingRootPath,
                    true);
            }

            Directory.CreateDirectory(
                WebGLStagingBuildPath);
            PlayerSettings.WebGL.compressionFormat =
                WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback =
                false;
            PlayerSettings.WebGL.template =
                "PROJECT:RuleforgeFullscreen";

            var options = new BuildPlayerOptions
            {
                scenes = new[]
                {
                    ScenePath,
                    SourceStageScenePath
                },
                locationPathName =
                    WebGLStagingBuildPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };
            BuildReport report =
                BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result !=
                BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    "TestLab WebGL build failed with result " +
                    summary.result +
                    " and " +
                    summary.totalErrors +
                    " error(s).");
            }

            PublishWebGLBuild();
            Debug.Log(
                "RULEFORGE_TESTLAB_WEBGL_BUILD_OK path=" +
                WebGLBuildPath +
                " size=" +
                summary.totalSize +
                " duration=" +
                summary.totalTime);
        }

        private static void ValidateSceneContents(
            Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException(
                    "TestLab validation requires a loaded scene.");
            }

            GameObject[] roots =
                scene.GetRootGameObjects();
            TestLabSceneBootstrap[] bootstraps = roots
                .SelectMany(root =>
                    root.GetComponentsInChildren<
                        TestLabSceneBootstrap>(true))
                .ToArray();
            if (roots.Length != 1 ||
                bootstraps.Length != 1 ||
                !string.Equals(
                    roots[0].name,
                    BootstrapRootName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "TestLab must contain only its generated " +
                    "bootstrap root.");
            }

            if (!string.Equals(
                    bootstraps[0].StageScenePath,
                    SourceStageScenePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "TestLab references the wrong source stage.");
            }

            int copiedMapCount = roots
                .SelectMany(root =>
                    root.GetComponentsInChildren<
                        FieldStageMap>(true))
                .Count();
            int copiedControllerCount = roots
                .SelectMany(root =>
                    root.GetComponentsInChildren<
                        StageOneBattleController>(true))
                .Count();
            if (copiedMapCount != 0 ||
                copiedControllerCount != 0)
            {
                throw new InvalidOperationException(
                    "TestLab must load canonical Stage01 " +
                    "additively instead of copying gameplay roots.");
            }

            ValidateBuildSetting(ScenePath);
            ValidateBuildSetting(
                SourceStageScenePath);
        }

        private static void EnsureSourceAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    SourceStageScenePath) == null)
            {
                throw new FileNotFoundException(
                    "TestLab requires the generated Stage01 scene.",
                    SourceStageScenePath);
            }

            if (AssetDatabase.LoadAssetAtPath<
                    StageOnePresentationCatalog>(
                    StageOneGameplaySceneInstaller.CatalogPath) ==
                null)
            {
                throw new FileNotFoundException(
                    "TestLab requires the Stage01 presentation " +
                    "catalog.",
                    StageOneGameplaySceneInstaller.CatalogPath);
            }
        }

        private static void EnsureScenesInBuildSettings()
        {
            EditorBuildSettingsScene[] current =
                EditorBuildSettings.scenes;
            var updated =
                new List<EditorBuildSettingsScene>(
                    current.Length + 2);
            var required =
                new HashSet<string>(
                    new[]
                    {
                        ScenePath,
                        SourceStageScenePath
                    },
                    StringComparer.Ordinal);
            var found =
                new HashSet<string>(
                    StringComparer.Ordinal);

            for (int i = 0;
                 i < current.Length;
                 i++)
            {
                EditorBuildSettingsScene entry =
                    current[i];
                if (required.Contains(entry.path))
                {
                    if (found.Add(entry.path))
                    {
                        updated.Add(
                            new EditorBuildSettingsScene(
                                entry.path,
                                true));
                    }

                    continue;
                }

                updated.Add(entry);
            }

            foreach (string requiredPath in
                     new[]
                     {
                         SourceStageScenePath,
                         ScenePath
                     })
            {
                if (found.Add(requiredPath))
                {
                    updated.Add(
                        new EditorBuildSettingsScene(
                            requiredPath,
                            true));
                }
            }

            EditorBuildSettings.scenes =
                updated.ToArray();
        }

        private static void ValidateBuildSetting(
            string requiredPath)
        {
            EditorBuildSettingsScene[] matches =
                EditorBuildSettings.scenes
                    .Where(entry =>
                        string.Equals(
                            entry.path,
                            requiredPath,
                            StringComparison.Ordinal))
                    .ToArray();
            if (matches.Length != 1 ||
                !matches[0].enabled)
            {
                throw new InvalidOperationException(
                    "Build Settings must contain one enabled " +
                    "entry for " +
                    requiredPath +
                    ".");
            }
        }

        private static void ClearScene(Scene scene)
        {
            GameObject[] roots =
                scene.GetRootGameObjects();
            for (int i = 0;
                 i < roots.Length;
                 i++)
            {
                UnityEngine.Object.DestroyImmediate(
                    roots[i]);
            }
        }

        private static void PublishWebGLBuild()
        {
            if (!Directory.Exists(
                    WebGLStagingBuildPath))
            {
                throw new BuildFailedException(
                    "TestLab WebGL staging build is missing: " +
                    WebGLStagingBuildPath);
            }

            if (Directory.Exists(
                    WebGLPreviousBuildPath))
            {
                Directory.Delete(
                    WebGLPreviousBuildPath,
                    true);
            }

            bool previousBuildMoved = false;
            try
            {
                if (Directory.Exists(WebGLBuildPath))
                {
                    Directory.Move(
                        WebGLBuildPath,
                        WebGLPreviousBuildPath);
                    previousBuildMoved = true;
                }

                Directory.Move(
                    WebGLStagingBuildPath,
                    WebGLBuildPath);
                if (Directory.Exists(
                        WebGLStagingRootPath))
                {
                    Directory.Delete(
                        WebGLStagingRootPath,
                        true);
                }

                if (previousBuildMoved &&
                    Directory.Exists(
                        WebGLPreviousBuildPath))
                {
                    Directory.Delete(
                        WebGLPreviousBuildPath,
                        true);
                }
            }
            catch
            {
                if (!Directory.Exists(
                        WebGLBuildPath) &&
                    previousBuildMoved &&
                    Directory.Exists(
                        WebGLPreviousBuildPath))
                {
                    Directory.Move(
                        WebGLPreviousBuildPath,
                        WebGLBuildPath);
                }

                throw;
            }
        }

        private static void EnsureAssetFolder(
            string path)
        {
            if (string.IsNullOrEmpty(path) ||
                AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent =
                Path.GetDirectoryName(path)
                    ?.Replace('\\', '/');
            EnsureAssetFolder(parent);
            string folderName =
                Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) ||
                string.IsNullOrEmpty(folderName))
            {
                throw new InvalidOperationException(
                    "Invalid Unity asset folder path: " +
                    path);
            }

            AssetDatabase.CreateFolder(
                parent,
                folderName);
        }

        private static bool CanReplaceUntitledScene(
            Scene activeScene)
        {
            if (!activeScene.IsValid() ||
                !string.IsNullOrEmpty(
                    activeScene.path) ||
                SceneManager.sceneCount != 1)
            {
                return false;
            }

            return Application.isBatchMode ||
                !activeScene.isDirty;
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
