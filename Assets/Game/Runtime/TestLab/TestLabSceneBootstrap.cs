using System;
using System.Collections;
using System.Linq;
using RuleforgeTD.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleforgeTD.UnityView.TestLab
{
    /// <summary>
    /// Keeps TestLab as a small, drift-free bootstrap scene. The canonical
    /// Stage01 scene is loaded additively, then its initialized battle bridge
    /// is handed to the test-only runtime installer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TestLabSceneBootstrap : MonoBehaviour
    {
        public const string DefaultStageScenePath =
            "Assets/Game/Scenes/Battle/Stage01.unity";

        [SerializeField]
        private string stageScenePath = DefaultStageScenePath;

        private TestLabRuntimeInstaller installer;
        private StageOneBattleController battleController;

        public string StageScenePath => stageScenePath;
        public bool IsReady { get; private set; }
        public bool HasFailed =>
            !string.IsNullOrEmpty(FailureMessage);
        public string FailureMessage { get; private set; }
        public TestLabRuntimeInstaller Installer => installer;
        public StageOneBattleController BattleController =>
            battleController;
        public Scene LoadedStageScene { get; private set; }

        public void ConfigureAuthoring(string sourceStageScenePath)
        {
            if (string.IsNullOrWhiteSpace(sourceStageScenePath))
            {
                throw new ArgumentException(
                    "A source stage scene path is required.",
                    nameof(sourceStageScenePath));
            }

            stageScenePath = sourceStageScenePath.Trim();
        }

        private IEnumerator Start()
        {
            Scene stageScene =
                SceneManager.GetSceneByPath(stageScenePath);
            if (!stageScene.IsValid() || !stageScene.isLoaded)
            {
                int buildIndex =
                    SceneUtility.GetBuildIndexByScenePath(
                        stageScenePath);
                if (buildIndex < 0)
                {
                    Fail(
                        "The TestLab source scene is not enabled in " +
                        "Build Settings: " +
                        stageScenePath);
                    yield break;
                }

                AsyncOperation loadOperation;
                try
                {
                    loadOperation = SceneManager.LoadSceneAsync(
                        buildIndex,
                        LoadSceneMode.Additive);
                }
                catch (Exception exception)
                {
                    Fail(
                        "Could not begin loading the TestLab source " +
                        "scene.",
                        exception);
                    yield break;
                }

                if (loadOperation == null)
                {
                    Fail(
                        "Unity did not create a load operation for " +
                        stageScenePath +
                        ".");
                    yield break;
                }

                while (!loadOperation.isDone)
                {
                    yield return null;
                }

                stageScene =
                    SceneManager.GetSceneByPath(stageScenePath);
            }

            try
            {
                InstallInto(stageScene);
            }
            catch (Exception exception)
            {
                Fail(
                    "TestLab could not attach to the loaded stage.",
                    exception);
            }
        }

        private void InstallInto(Scene stageScene)
        {
            if (!stageScene.IsValid() || !stageScene.isLoaded)
            {
                throw new InvalidOperationException(
                    "The TestLab source stage is not loaded: " +
                    stageScenePath);
            }

            StageOneBattleController[] controllers = stageScene
                .GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<
                        StageOneBattleController>(true))
                .ToArray();
            if (controllers.Length != 1)
            {
                throw new InvalidOperationException(
                    "TestLab requires exactly one " +
                    nameof(StageOneBattleController) +
                    " in the source stage, but found " +
                    controllers.Length +
                    ".");
            }

            LoadedStageScene = stageScene;
            battleController = controllers[0];
            SceneManager.SetActiveScene(stageScene);
            battleController.InitializeNow();

            installer =
                GetComponent<TestLabRuntimeInstaller>();
            if (installer == null)
            {
                installer =
                    gameObject.AddComponent<
                        TestLabRuntimeInstaller>();
            }

            installer.ConfigureAuthoring(battleController);
            IsReady = true;
            Debug.Log(
                "RULEFORGE_TESTLAB_READY scene=" +
                stageScene.path);
        }

        private void Fail(
            string message,
            Exception exception = null)
        {
            FailureMessage = exception == null
                ? message
                : message + "\n" + exception;
            Debug.LogError(
                "RULEFORGE_TESTLAB_FAILED " +
                FailureMessage);
        }
    }
}
