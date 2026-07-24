using System;
using System.Collections.Generic;
using System.IO;
using RuleforgeTD.Simulation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleforgeTD.Editor.BuildTools
{
    public static class HeadlessLogicWebGLBuilder
    {
        public const string ScenePath =
            "Assets/Game/Scenes/Test/HeadlessLogicTest.unity";

        public const string ContentPath =
            "Assets/Game/Data/Logic/phase1-content.json";

        public const string OutputPath =
            "Builds/WebGL/HeadlessLogicTest";

        private const string HarnessObjectName = "HeadlessSimulationHarness";
        private const ulong ReplaySeed = 12345UL;
        private const int TicksPerFrame = 30;
        private const int MaximumTicks = 60000;

        [MenuItem("Ruleforge TD/Build/Rebuild Headless Logic Test Scene")]
        public static void RebuildHeadlessLogicTestScene()
        {
            TextAsset content = AssetDatabase.LoadAssetAtPath<TextAsset>(ContentPath);
            if (content == null)
            {
                throw new FileNotFoundException(
                    "Phase 1 content TextAsset was not found.",
                    ContentPath);
            }

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene targetScene = SceneManager.GetSceneByPath(ScenePath);
            bool wasAlreadyLoaded = targetScene.IsValid() && targetScene.isLoaded;
            bool openedInSingleMode = false;

            if (!wasAlreadyLoaded)
            {
                openedInSingleMode = CanReplaceUntitledScene(previousActiveScene);
                SceneAsset existingAsset =
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
                targetScene = existingAsset == null
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
                if (SceneManager.GetActiveScene() != targetScene &&
                    !EditorSceneManager.SetActiveScene(targetScene))
                {
                    throw new InvalidOperationException(
                        "Could not make the headless test scene active.");
                }

                ClearScene(targetScene);

                GameObject harnessObject = new GameObject(HarnessObjectName);
                HeadlessSimulationHarness harness =
                    harnessObject.AddComponent<HeadlessSimulationHarness>();
                ulong expectedHash =
                    HeadlessReplayDriver.ComputeVictoryHash(
                        LogicContentJsonLoader.Load(content),
                        ReplaySeed,
                        MaximumTicks,
                        out int editorTicks);
                harness.Configure(
                    content,
                    ReplaySeed,
                    TicksPerFrame,
                    MaximumTicks,
                    expectedHash);

                EditorUtility.SetDirty(harness);

                if (!EditorSceneManager.SaveScene(targetScene, ScenePath))
                {
                    throw new InvalidOperationException(
                        "Unity could not save the headless test scene.");
                }

                UpsertBuildSettingsScene();
                AssetDatabase.SaveAssets();

                Debug.Log(
                    "RULEFORGE_HEADLESS_SCENE_OK scene=" + ScenePath +
                    " content=" + ContentPath +
                    " editorTick=" + editorTicks +
                    " editorHash=" + expectedHash.ToString("X16"));
            }
            finally
            {
                RestoreEditorSceneState(
                    previousActiveScene,
                    targetScene,
                    wasAlreadyLoaded || openedInSingleMode);
            }
        }

        [MenuItem("Ruleforge TD/Build/Build Headless Logic Test (WebGL)")]
        public static void BuildWebGL()
        {
            RebuildHeadlessLogicTestScene();

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    "Headless WebGL build failed with result " +
                    summary.result +
                    " and " +
                    summary.totalErrors +
                    " error(s).");
            }

            Debug.Log(
                "RULEFORGE_HEADLESS_WEBGL_BUILD_OK path=" + OutputPath +
                " size=" + summary.totalSize +
                " duration=" + summary.totalTime);
        }

        private static void ClearScene(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                UnityEngine.Object.DestroyImmediate(roots[i]);
            }
        }

        private static bool CanReplaceUntitledScene(Scene activeScene)
        {
            if (!activeScene.IsValid() ||
                !string.IsNullOrEmpty(activeScene.path) ||
                SceneManager.sceneCount != 1)
            {
                return false;
            }

            return Application.isBatchMode || !activeScene.isDirty;
        }

        private static void UpsertBuildSettingsScene()
        {
            EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
            var updated = new List<EditorBuildSettingsScene>(current.Length + 1);
            bool found = false;

            for (int i = 0; i < current.Length; i++)
            {
                EditorBuildSettingsScene scene = current[i];
                if (string.Equals(
                    scene.path,
                    ScenePath,
                    StringComparison.Ordinal))
                {
                    if (!found)
                    {
                        updated.Add(
                            new EditorBuildSettingsScene(ScenePath, true));
                        found = true;
                    }

                    continue;
                }

                updated.Add(scene);
            }

            if (!found)
            {
                updated.Add(new EditorBuildSettingsScene(ScenePath, true));
            }

            EditorBuildSettings.scenes = updated.ToArray();
        }

        private static void RestoreEditorSceneState(
            Scene previousActiveScene,
            Scene targetScene,
            bool wasAlreadyLoaded)
        {
            if (previousActiveScene.IsValid() &&
                previousActiveScene.isLoaded &&
                previousActiveScene != targetScene)
            {
                EditorSceneManager.SetActiveScene(previousActiveScene);
            }

            if (!wasAlreadyLoaded &&
                targetScene.IsValid() &&
                targetScene.isLoaded)
            {
                EditorSceneManager.CloseScene(targetScene, true);
            }
        }
    }
}
