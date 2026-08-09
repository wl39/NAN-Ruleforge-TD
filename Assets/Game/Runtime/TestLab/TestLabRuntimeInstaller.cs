using System;
using RuleforgeTD.Battle;
using RuleforgeTD.Maps;
using UnityEngine;

namespace RuleforgeTD.UnityView.TestLab
{
    /// <summary>
    /// TestLab bootstrap과 테스트 패널을 연결하는 작은 composition root.
    /// Awake/OnEnable에서는 씬 참조를 찾거나 오류를 내지 않고,
    /// bootstrap의 명시적 ConfigureAuthoring 이후에만 설치한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TestLabRuntimeInstaller : MonoBehaviour
    {
        [SerializeField]
        private StageOneBattleController battleController;

        private TestLabBattleControlTarget controlTarget;
        private TestLabControlPanel controlPanel;
        private bool installAttempted;

        public StageOneBattleController BattleController =>
            battleController;
        public TestLabControlPanel ControlPanel =>
            controlPanel;
        public bool IsInstalled =>
            controlPanel != null &&
            controlPanel.IsBuilt;
        public string FailureMessage { get; private set; }

        private void Start()
        {
            if (battleController != null && !IsInstalled)
            {
                Install();
            }
        }

        private void OnDestroy()
        {
            if (controlTarget != null)
            {
                controlTarget.Dispose();
                controlTarget = null;
            }
        }

        /// <summary>
        /// additive로 로드·초기화된 정규 Stage01 표현 호스트를 재사용한다.
        /// TestLabSceneBootstrap의 기본 연결 경로다.
        /// </summary>
        public void ConfigureAuthoring(
            StageOneBattleController sourceBattleController)
        {
            if (sourceBattleController == null)
            {
                throw new ArgumentNullException(
                    nameof(sourceBattleController));
            }

            if (IsInstalled &&
                battleController != sourceBattleController)
            {
                throw new InvalidOperationException(
                    "TestLab is already installed on another battle.");
            }

            battleController = sourceBattleController;
            Install();
        }

        /// <summary>
        /// PlayMode 테스트나 독립 authoring 환경에서 필요한 StageOne bridge를
        /// 같은 호스트에 만들어 연결하는 편의 경로다.
        /// </summary>
        public void ConfigureAuthoring(
            FieldStageMap stageMap,
            StageOnePresentationCatalog presentationCatalog,
            ulong seed)
        {
            if (stageMap == null)
            {
                throw new ArgumentNullException(nameof(stageMap));
            }
            if (presentationCatalog == null)
            {
                throw new ArgumentNullException(
                    nameof(presentationCatalog));
            }

            StageOneBattleController source =
                GetComponent<StageOneBattleController>();
            if (source == null)
            {
                source =
                    gameObject.AddComponent<
                        StageOneBattleController>();
            }

            source.ConfigureAuthoring(
                stageMap,
                presentationCatalog,
                seed);
            source.InitializeNow();
            ConfigureAuthoring(source);
        }

        private void Install()
        {
            if (IsInstalled || installAttempted)
            {
                return;
            }

            installAttempted = true;
            try
            {
                if (!battleController.IsInitialized)
                {
                    battleController.InitializeNow();
                }

                controlTarget =
                    new TestLabBattleControlTarget(
                        battleController);
                Font font =
                    battleController.PresentationCatalog == null
                        ? null
                        : battleController
                            .PresentationCatalog
                            .UiFont;
                controlPanel =
                    TestLabControlPanel.CreateRuntime(
                        controlTarget,
                        font,
                        transform);
                FailureMessage = string.Empty;
            }
            catch (Exception exception)
            {
                FailureMessage = exception.ToString();
                installAttempted = false;
                if (controlTarget != null)
                {
                    controlTarget.Dispose();
                    controlTarget = null;
                }

                Debug.LogError(
                    "RULEFORGE_TESTLAB_INSTALL_FAILED " +
                    FailureMessage,
                    this);
                throw;
            }
        }
    }
}
