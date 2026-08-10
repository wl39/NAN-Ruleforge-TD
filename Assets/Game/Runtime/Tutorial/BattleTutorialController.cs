using System;
using System.Collections.Generic;
using RuleforgeTD.Battle;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;
using RuleforgeTD.Maps;
using RuleforgeTD.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RuleforgeTD.Tutorial
{
    /// <summary>
    /// Presentation-only coordinator for the first-run lesson and one-shot
    /// contextual help. It observes snapshots and accepted UI actions, but it
    /// never submits commands to the deterministic simulation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleTutorialController : MonoBehaviour
    {
        private const string CurrentAnchorId = "tutorial.current";

        private StageOneBattleController battle;
        private TutorialDefinition definition;
        private TutorialProgressStore progress;
        private TutorialAnchorRegistry anchors;
        private BattleTutorialOverlayView overlay;
        private TutorialStepDefinition currentStep;
        private readonly Queue<string> contextualQueue =
            new Queue<string>();
        private readonly HashSet<string> queuedContextualTips =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Transform> pointAnchors =
            new List<Transform>(3);
        private readonly RectTransform[] rewardChoiceAnchors =
            new RectTransform[StageOneHudView.RewardChoiceCapacity];
        private readonly RectTransform[] cardDragAnchors =
            new RectTransform[2];
        private string activeContextualTipId = string.Empty;
        private int stepIndex = -1;
        private int objectivePage;
        private int cardTargetCycle;
        private int firstWaveState;
        private int enemyInspectionState;
        private int draftRewardState;
        private int anchoredEnemyId = -1;
        private bool enemyInspectionCompleted;
        private bool upgradeCompleted;
        private bool upgradeDeferred;
        private bool coreActive;
        private bool contextualTipsEnabled;
        private bool configured;
        private bool requestsWorldPause;
        private RunPhase previousPhase = (RunPhase)(-1);
        private int previousTowerCount;
        private int previousMaximumComputeCapacity;
        private int previousTowerStateFingerprint = int.MinValue;
        private int previousForecastWaveIndex = int.MinValue;
        private int previousSelectedEnemyId = -1;
        private bool hasObservedComputeCapacity;

        public bool IsCoreActive => coreActive;
        public bool IsShowingContextualTip =>
            !string.IsNullOrEmpty(activeContextualTipId);
        public string CurrentStepId =>
            currentStep == null ? string.Empty : currentStep.Id;
        public bool RequestsWorldPause => requestsWorldPause;
        public BattleTutorialOverlayView Overlay => overlay;
        public TutorialAnchorRegistry AnchorRegistry => anchors;

        public void Configure(
            StageOneBattleController battleController,
            Font uiFont)
        {
            if (configured)
            {
                return;
            }

            battle = battleController ??
                throw new ArgumentNullException(nameof(battleController));
            definition = TutorialDefinitionLoader.LoadKorean();
            progress = TutorialProgressStore.FromDefinition(definition);
            anchors = TutorialAnchorRegistry.CreateRuntime(transform);
            overlay = BattleTutorialOverlayView.CreateRuntime(
                uiFont,
                transform);
            overlay.AnchorRegistry = anchors;
            overlay.NextRequested += HandleNextRequested;
            overlay.SkipRequested += HandleSkipRequested;
            if (battle.WavePreviewView != null &&
                battle.WavePreviewView.SummaryButton != null)
            {
                battle.WavePreviewView.SummaryButton.onClick.AddListener(
                    HandleWavePreviewOpened);
            }

            configured = true;
            contextualTipsEnabled = !Application.isBatchMode;
            SimulationSnapshot snapshot = battle.CurrentSnapshot;
            previousPhase = snapshot == null
                ? RunPhase.AwaitingStartingTower
                : snapshot.Phase;
            previousTowerCount = snapshot == null
                ? 0
                : snapshot.Towers.Length;

            bool isStageOne = string.Equals(
                SceneManager.GetActiveScene().name,
                "Stage01",
                StringComparison.OrdinalIgnoreCase);
            // Keep the replay request until completion or skip. If the player
            // leaves the scene mid-lesson, the next Stage 01 entry must begin
            // again from the first step.
            bool manualReplay = isStageOne &&
                progress.IsManualReplayRequested;
            if (isStageOne &&
                (manualReplay ||
                 (!Application.isBatchMode && progress.ShouldAutoStart)))
            {
                StartCoreTutorial();
            }
            else if (contextualTipsEnabled)
            {
                QueueStageIdentityTip();
            }
        }

        public void StartCoreTutorial()
        {
            if (!configured || definition.Steps.Count == 0)
            {
                return;
            }

            coreActive = true;
            activeContextualTipId = string.Empty;
            contextualQueue.Clear();
            queuedContextualTips.Clear();
            objectivePage = 0;
            cardTargetCycle = 0;
            firstWaveState = 0;
            enemyInspectionState = 0;
            draftRewardState = 0;
            anchoredEnemyId = -1;
            enemyInspectionCompleted = false;
            upgradeCompleted = false;
            upgradeDeferred = false;
            requestsWorldPause = false;
            stepIndex = 0;
            currentStep = definition.Steps[stepIndex];
            ShowCurrentStep();
        }

        public void Tick()
        {
            if (!configured || battle == null)
            {
                return;
            }

            SimulationSnapshot snapshot = battle.CurrentSnapshot;
            if (snapshot == null)
            {
                return;
            }

            if (coreActive)
            {
                // Capture one-shot events while the core lesson owns the
                // screen. They remain queued and are only presented after
                // the lesson ends, so no contextual overlay can stack on it.
                if (contextualTipsEnabled)
                {
                    DetectContextualTips(snapshot, true);
                }

                if (snapshot.Phase == RunPhase.Defeat)
                {
                    CancelCoreWithoutResolving();
                    ClearQueuedContextualTips();
                    QueueContextualTip(TutorialIds.ContextualTips.Defeat);
                }
                else
                {
                    TickCore(snapshot);
                }
            }
            else if (contextualTipsEnabled)
            {
                DetectContextualTips(snapshot, false);
                TryShowNextContextualTip(snapshot);
            }

            previousPhase = snapshot.Phase;
            previousTowerCount = snapshot.Towers.Length;
            previousSelectedEnemyId = battle.SelectedEnemyId;
        }

        public bool Allows(
            TutorialAction action,
            int targetId = -1)
        {
            if (!coreActive || currentStep == null)
            {
                return true;
            }

            if (action == TutorialAction.SkipTutorial ||
                action == TutorialAction.OpenGuide ||
                action == TutorialAction.CloseGuide)
            {
                return true;
            }

            string id = currentStep.Id;
            if (id == TutorialIds.Steps.Objective ||
                id == TutorialIds.Steps.CardOrder ||
                id == TutorialIds.Steps.Complete)
            {
                return action == TutorialAction.Continue;
            }

            if (id == TutorialIds.Steps.WavePreview)
            {
                return action == TutorialAction.OpenWavePreview ||
                    (firstWaveState > 0 &&
                     action == TutorialAction.Continue);
            }

            if (id == TutorialIds.Steps.TowerBuild)
            {
                return action == TutorialAction.SelectBuildSite ||
                    action == TutorialAction.BuildTower;
            }

            if (id == TutorialIds.Steps.TowerSelect)
            {
                return action == TutorialAction.SelectTower;
            }

            if (id == TutorialIds.Steps.Loadout)
            {
                return action == TutorialAction.OpenTowerLoadout;
            }

            if (id == TutorialIds.Steps.CardDrag)
            {
                return action == TutorialAction.DragCardToSlot;
            }

            if (id == TutorialIds.Steps.CardTarget)
            {
                return cardTargetCycle == 0
                    ? action == TutorialAction.SetCardTargetEnemy
                    : action == TutorialAction.SetCardTargetProjectile;
            }

            if (id == TutorialIds.Steps.FirstWave)
            {
                if (firstWaveState == 0)
                {
                    return action == TutorialAction.CloseTowerLoadout;
                }

                return action == TutorialAction.StartWave;
            }

            if (id == TutorialIds.Steps.EnemyInspection)
            {
                if (enemyInspectionState == 0)
                {
                    return action == TutorialAction.Continue ||
                        action == TutorialAction.TogglePause ||
                        action == TutorialAction.ChangeBattleSpeed;
                }

                if (enemyInspectionState == 1)
                {
                    return action == TutorialAction.SelectEnemy ||
                        action == TutorialAction.TogglePause ||
                        action == TutorialAction.ChangeBattleSpeed;
                }

                if (enemyInspectionState == 2)
                {
                    return action == TutorialAction.Continue;
                }

                if (enemyInspectionState == 3)
                {
                    return action == TutorialAction.TogglePause ||
                        action == TutorialAction.ChangeBattleSpeed;
                }

                return true;
            }

            if (id == TutorialIds.Steps.TowerUpgrade)
            {
                if (battle.CurrentPhase == RunPhase.Combat)
                {
                    return true;
                }

                if (!upgradeCompleted &&
                    battle.SelectedTowerId >= 0 &&
                    !CanAffordSelectedTowerUpgrade())
                {
                    return true;
                }

                return upgradeCompleted
                    ? action == TutorialAction.StartWave
                    : action == TutorialAction.SelectTower ||
                      action == TutorialAction.UpgradeTower;
            }

            if (id == TutorialIds.Steps.DraftReward)
            {
                if (battle.CurrentPhase == RunPhase.CardPackLoadout)
                {
                    return action == TutorialAction.SelectTower ||
                        action == TutorialAction.OpenTowerLoadout ||
                        action == TutorialAction.DragCardToSlot ||
                        action == TutorialAction.AutoEquipCard ||
                        action == TutorialAction.UnequipCard ||
                        action == TutorialAction.ReorderCard ||
                        action == TutorialAction.CloseTowerLoadout;
                }

                return !IsRewardChoicePhase(battle.CurrentPhase) ||
                    action == TutorialAction.ChooseDraftReward;
            }

            return currentStep.Allows(
                action,
                targetId < 0 ? null : targetId.ToString());
        }

        public void ReportAction(
            TutorialAction action,
            int targetId = -1)
        {
            if (!coreActive)
            {
                HandleContextualAction(action);
                return;
            }

            if (currentStep == null)
            {
                return;
            }

            string id = currentStep.Id;
            if (id == TutorialIds.Steps.TowerBuild &&
                action == TutorialAction.SelectBuildSite)
            {
                // The accepted world click creates the picker. Move the
                // spotlight to its playable option before the next input.
                ShowCurrentStep();
            }
            else if (id == TutorialIds.Steps.TowerBuild &&
                     action == TutorialAction.BuildTower)
            {
                AdvanceStep();
            }
            else if (id == TutorialIds.Steps.TowerSelect &&
                     action == TutorialAction.SelectTower)
            {
                AdvanceStep();
            }
            else if (id == TutorialIds.Steps.Loadout &&
                     action == TutorialAction.OpenTowerLoadout)
            {
                AdvanceStep();
            }
            else if (id == TutorialIds.Steps.CardDrag &&
                     action == TutorialAction.DragCardToSlot)
            {
                AdvanceStep();
            }
            else if (id == TutorialIds.Steps.CardTarget)
            {
                if (action == TutorialAction.SetCardTargetEnemy &&
                    cardTargetCycle == 0)
                {
                    cardTargetCycle = 1;
                    ShowCurrentStep();
                }
                else if (action ==
                         TutorialAction.SetCardTargetProjectile &&
                         cardTargetCycle == 1)
                {
                    AdvanceStep();
                }
            }
            else if (id == TutorialIds.Steps.FirstWave)
            {
                if (action == TutorialAction.CloseTowerLoadout)
                {
                    firstWaveState = 1;
                    ShowCurrentStep();
                }
                else if (action == TutorialAction.StartWave &&
                         firstWaveState == 1)
                {
                    AdvanceStep();
                }
            }
            else if (id == TutorialIds.Steps.EnemyInspection &&
                     action == TutorialAction.SelectEnemy &&
                     enemyInspectionState == 1)
            {
                enemyInspectionCompleted = true;
                enemyInspectionState = 2;
                ShowCurrentStep();
            }
            else if (id == TutorialIds.Steps.EnemyInspection &&
                     action == TutorialAction.TogglePause)
            {
                if (enemyInspectionState == 1 && battle.IsPaused)
                {
                    enemyInspectionState = 3;
                    ShowCurrentStep();
                }
                else if (enemyInspectionState == 3 &&
                         !battle.IsPaused)
                {
                    enemyInspectionState = 1;
                    requestsWorldPause = false;
                    overlay.Hide();
                }
            }
            else if (id == TutorialIds.Steps.TowerUpgrade)
            {
                if (action == TutorialAction.SelectTower)
                {
                    ShowCurrentStep();
                }
                else if (action == TutorialAction.UpgradeTower)
                {
                    upgradeCompleted = true;
                    upgradeDeferred = false;
                    ShowCurrentStep();
                }
                else if (action == TutorialAction.StartWave &&
                         !upgradeCompleted &&
                         !CanAffordSelectedTowerUpgrade())
                {
                    upgradeDeferred = true;
                    overlay.Hide();
                }
                else if (action == TutorialAction.StartWave &&
                         upgradeCompleted)
                {
                    AdvanceStep();
                }
            }
            else if (id == TutorialIds.Steps.DraftReward &&
                     action == TutorialAction.ChooseDraftReward)
            {
                if (battle.CurrentPhase == RunPhase.CardPackLoadout)
                {
                    draftRewardState = 1;
                    ShowCurrentStep();
                }
                else
                {
                    if (draftRewardState > 0)
                    {
                        QueueContextualTip(
                            TutorialIds.ContextualTips.CardPack);
                    }
                    AdvanceStep();
                }
            }
            else if (id == TutorialIds.Steps.DraftReward &&
                     draftRewardState > 0 &&
                     action == TutorialAction.SelectTower)
            {
                ShowCurrentStep();
            }
            else if (id == TutorialIds.Steps.DraftReward &&
                     draftRewardState > 0 &&
                     action == TutorialAction.OpenTowerLoadout)
            {
                draftRewardState = 2;
                ShowCurrentStep();
            }
        }

        public void Shutdown()
        {
            requestsWorldPause = false;
            if (battle != null &&
                battle.WavePreviewView != null &&
                battle.WavePreviewView.SummaryButton != null)
            {
                battle.WavePreviewView.SummaryButton.onClick.RemoveListener(
                    HandleWavePreviewOpened);
            }

            if (overlay != null)
            {
                overlay.NextRequested -= HandleNextRequested;
                overlay.SkipRequested -= HandleSkipRequested;
                overlay.Hide();
            }

            anchors?.Clear();
            for (int i = 0; i < pointAnchors.Count; i++)
            {
                if (pointAnchors[i] != null)
                {
                    Destroy(pointAnchors[i].gameObject);
                }
            }
            pointAnchors.Clear();
            configured = false;
        }

        private void TickCore(SimulationSnapshot snapshot)
        {
            if (currentStep == null)
            {
                return;
            }

            if (currentStep.Id == TutorialIds.Steps.EnemyInspection &&
                enemyInspectionState == 1 &&
                !enemyInspectionCompleted)
            {
                if (battle.IsPaused)
                {
                    enemyInspectionState = 3;
                    ShowCurrentStep();
                    return;
                }

                if (anchoredEnemyId >= 0 && overlay.IsVisible)
                {
                    requestsWorldPause = true;
                    return;
                }

                StageOneEnemyView enemy = FindFirstLiveEnemy();
                if (enemy == null)
                {
                    anchoredEnemyId = -1;
                    if (overlay.IsVisible)
                    {
                        overlay.Hide();
                    }
                    requestsWorldPause = false;
                    return;
                }

                anchoredEnemyId = enemy.EntityId;
                requestsWorldPause = true;
                RegisterWorldAnchor(enemy.transform, new Vector2(112f, 112f));
                ShowOverlay(currentStep, false, true,
                    "몬스터를 클릭해 상세 정보를 확인하세요.");
            }
            else if (currentStep.Id == TutorialIds.Steps.TowerUpgrade)
            {
                if (snapshot.Phase == RunPhase.Combat ||
                    snapshot.WaveIndex < 0)
                {
                    if (overlay.IsVisible)
                    {
                        overlay.Hide();
                    }
                    return;
                }

                if (!overlay.IsVisible)
                {
                    ShowCurrentStep();
                }
            }
            else if (currentStep.Id == TutorialIds.Steps.DraftReward)
            {
                bool waitingForCardPackLoadout =
                    draftRewardState > 0 &&
                    snapshot.Phase == RunPhase.CardPackLoadout;
                if (!IsRewardChoicePhase(snapshot.Phase) &&
                    !waitingForCardPackLoadout)
                {
                    if (overlay.IsVisible)
                    {
                        overlay.Hide();
                    }
                    return;
                }

                if (!overlay.IsVisible)
                {
                    ShowCurrentStep();
                }
            }
        }

        private void ShowCurrentStep()
        {
            if (!coreActive || currentStep == null || overlay == null)
            {
                return;
            }

            requestsWorldPause = currentStep.PauseBattle;
            anchors.Clear();
            string id = currentStep.Id;
            bool showNext = currentStep.Completion ==
                TutorialCompletion.Acknowledged;
            bool blockOutside = currentStep.RestrictInput;
            string bodyOverride = null;

            if (id == TutorialIds.Steps.Objective)
            {
                RegisterObjectiveAnchor();
                showNext = true;
                string[] pages =
                {
                    "몬스터는 출현 지점에서 길을 따라 진입합니다. 상단에서 웨이브와 골드를 확인하세요.",
                    "적은 표시된 경로를 따라 이동합니다. 길목의 건설 지점에 타워를 배치해 막아야 합니다.",
                    "몬스터가 종착점에 도달하면 본진 체력이 감소하고, 체력이 0이 되면 패배합니다."
                };
                bodyOverride = pages[Mathf.Clamp(
                    objectivePage, 0, pages.Length - 1)];
            }
            else if (id == TutorialIds.Steps.WavePreview)
            {
                RectTransform target = firstWaveState == 0
                    ? AsRect(battle.WavePreviewView?.SummaryButton)
                    : battle.WavePreviewView == null
                        ? null
                        : battle.WavePreviewView.DetailPanel == null
                            ? null
                            : battle.WavePreviewView.DetailPanel.transform
                                as RectTransform;
                RegisterUiAnchor(target);
                showNext = firstWaveState > 0;
            }
            else if (id == TutorialIds.Steps.TowerBuild)
            {
                if (battle.TowerBuildPickerView != null &&
                    battle.TowerBuildPickerView.IsVisible &&
                    battle.TowerBuildPickerView.OptionCount > 0)
                {
                    RegisterUiAnchor(AsRect(
                        battle.TowerBuildPickerView.GetOptionButton(0)));
                    bodyOverride =
                        "궁수 타워를 선택하세요. 첫 타워는 무료이며 이후 타워는 골드를 사용합니다.";
                }
                else
                {
                    TowerBuildSiteView site = FindFirstBuildSite();
                    if (site != null)
                    {
                        RegisterWorldAnchor(
                            site.transform,
                            new Vector2(118f, 118f));
                    }
                }
                showNext = false;
            }
            else if (id == TutorialIds.Steps.TowerSelect)
            {
                RegisterWorldAnchor(
                    FindFirstTowerTransform(),
                    new Vector2(132f, 164f));
                showNext = false;
            }
            else if (id == TutorialIds.Steps.Loadout)
            {
                RegisterUiAnchor(AsRect(
                    battle.TowerActionView?.CardsButton));
                showNext = false;
            }
            else if (id == TutorialIds.Steps.CardDrag)
            {
                RegisterCardDragAnchor();
                showNext = false;
            }
            else if (id == TutorialIds.Steps.CardTarget)
            {
                Button target = cardTargetCycle == 0
                    ? battle.LoadoutView?.GetSlotEnemyButton(0)
                    : battle.LoadoutView?.GetSlotProjectileButton(0);
                RegisterUiAnchor(AsRect(target));
                showNext = false;
                bodyOverride = cardTargetCycle == 0
                    ? "같은 카드의 적 효과를 확인하려면 '적'을 누르세요. 설명과 그림이 적 해석으로 바뀝니다."
                    : "차이를 확인했습니다. 첫 웨이브를 안정적으로 진행하도록 다시 '탄환'을 선택하세요.";
            }
            else if (id == TutorialIds.Steps.CardOrder)
            {
                RegisterUiAnchor(AsRect(
                    battle.LoadoutView?.GetSlotButton(0)));
                showNext = true;
            }
            else if (id == TutorialIds.Steps.FirstWave)
            {
                if (firstWaveState == 0)
                {
                    RegisterUiAnchor(AsRect(
                        battle.LoadoutView?.CloseButton));
                    bodyOverride =
                        "전투 중에는 카드 편집이 잠깁니다. 설계도를 닫아 준비를 마치세요.";
                }
                else
                {
                    RegisterUiAnchor(AsRect(battle.Hud?.PlayButton));
                    bodyOverride =
                        "웨이브 시작 버튼을 누르면 타워가 자동으로 공격합니다.";
                }
                showNext = false;
            }
            else if (id == TutorialIds.Steps.EnemyInspection)
            {
                if (enemyInspectionState == 0)
                {
                    RegisterUiAnchor(AsRect(battle.Hud?.SpeedButton));
                    showNext = true;
                    blockOutside = false;
                    bodyOverride =
                        "전투 버튼으로 일시정지·재개하고 0.5·1·2·3배속을 선택할 수 있습니다. 원하는 속도로 관찰하세요.";
                }
                else if (enemyInspectionState == 2)
                {
                    RegisterUiAnchor(
                        battle.EnemyInspectionView?.PanelRoot);
                    showNext = true;
                    bodyOverride =
                        "정보창에서 체력, 방어력, 속도, 저항, 처치 골드와 디버프를 확인합니다. 정예와 보스는 강한 제어를 저항 게이지로 받습니다.";
                }
                else if (enemyInspectionState == 3)
                {
                    RegisterUiAnchor(AsRect(battle.Hud?.PlayButton));
                    requestsWorldPause = false;
                    showNext = false;
                    bodyOverride =
                        "전투가 일시정지되어 몬스터가 아직 등장하지 않았습니다. 재생 버튼을 눌러 전투를 재개하세요.";
                }
                else
                {
                    overlay.Hide();
                    return;
                }
            }
            else if (id == TutorialIds.Steps.TowerUpgrade)
            {
                if (!upgradeCompleted && battle.SelectedTowerId < 0)
                {
                    RegisterWorldAnchor(
                        FindFirstTowerTransform(),
                        new Vector2(132f, 164f));
                    bodyOverride =
                        "첫 웨이브 보상으로 타워를 강화할 수 있습니다. 타워를 선택하세요.";
                }
                else if (!upgradeCompleted)
                {
                    if (CanAffordSelectedTowerUpgrade())
                    {
                        RegisterUiAnchor(AsRect(
                            battle.TowerActionView?.UpgradeButton));
                        bodyOverride =
                            "업그레이드는 발사 수·속도·범위·슬롯·연산력을 성장시킵니다. 레벨 2로 강화하세요.";
                    }
                    else
                    {
                        RegisterUiAnchor(AsRect(battle.Hud?.PlayButton));
                        showNext = !upgradeDeferred;
                        blockOutside = false;
                        bodyOverride = upgradeDeferred
                            ? "아직 강화 골드가 부족합니다. 다음 웨이브를 진행하세요. 웨이브가 끝나 골드가 충분해지면 강화 안내가 다시 나타납니다."
                            : "아직 강화 골드가 부족합니다. 이번 실습을 미루고 다음 웨이브를 진행할 수 있습니다.";
                    }
                }
                else
                {
                    RegisterUiAnchor(AsRect(battle.Hud?.PlayButton));
                    bodyOverride = upgradeDeferred
                        ? "강화 실습을 미뤘습니다. 다음 웨이브를 시작하고, 골드가 모이면 타워의 업그레이드 버튼을 다시 확인하세요."
                        : "강화를 마쳤습니다. 다음 웨이브들을 진행해 첫 카드 보상을 획득하세요.";
                }
                if (upgradeCompleted ||
                    battle.SelectedTowerId < 0 ||
                    CanAffordSelectedTowerUpgrade())
                {
                    showNext = false;
                }
            }
            else if (id == TutorialIds.Steps.DraftReward)
            {
                if (draftRewardState == 0)
                {
                    RegisterRewardChoiceAnchor();
                    bodyOverride =
                        "첫 3장 카드 보상에서 한 장을 선택하세요. 티어, 연산 비용, 두 대상 효과와 현재 조합의 시너지를 비교할 수 있습니다.";
                }
                else if (!battle.IsTowerBlueprintOpen)
                {
                    if (battle.SelectedTowerId >= 0)
                    {
                        RegisterUiAnchor(AsRect(
                            battle.TowerActionView?.CardsButton));
                        bodyOverride =
                            "카드팩에서 고른 새 카드는 전투를 재개하기 전에 반드시 장착해야 합니다. 카드 장착하기를 누르세요.";
                    }
                    else
                    {
                        RegisterWorldAnchor(
                            FindFirstTowerTransform(),
                            new Vector2(132f, 164f));
                        bodyOverride =
                            "카드팩에서 고른 새 카드는 전투를 재개하기 전에 반드시 장착해야 합니다. 장착할 타워를 선택하세요.";
                    }
                }
                else
                {
                    RegisterUiAnchor(AsRect(
                        battle.LoadoutView?.GetSlotButton(0)));
                    blockOutside = false;
                    bodyOverride =
                        "방금 고른 카드를 보유 카드에서 빈 슬롯으로 드래그하세요. 빈 슬롯이 없으면 기존 카드를 교체할 수 있습니다. 장착이 끝나면 전투가 자동으로 재개됩니다.";
                }
                showNext = false;
            }
            else if (id == TutorialIds.Steps.Complete)
            {
                showNext = true;
                blockOutside = false;
            }

            ShowOverlay(
                currentStep,
                showNext,
                blockOutside,
                bodyOverride);
        }

        private void ShowOverlay(
            TutorialStepDefinition step,
            bool showNext,
            bool blockOutside,
            string bodyOverride = null)
        {
            int visibleChapter = Mathf.Clamp(
                step.Chapter,
                1,
                TutorialIds.CoreChapterCount);
            var content = new TutorialOverlayContent(
                anchors.Contains(CurrentAnchorId)
                    ? CurrentAnchorId
                    : string.Empty,
                step.Title,
                string.IsNullOrEmpty(bodyOverride)
                    ? step.Body
                    : bodyOverride,
                visibleChapter + " / " +
                TutorialIds.CoreChapterCount);
            content.ShowNextButton = showNext;
            content.BlockOutsideHole = blockOutside;
            content.NextLabel = step.Id == TutorialIds.Steps.Complete
                ? "완료"
                : "다음";
            content.SkipLabel = "건너뛰기";
            overlay.Show(content);
        }

        private void HandleNextRequested()
        {
            if (!coreActive)
            {
                AcknowledgeContextualTip();
                return;
            }

            if (currentStep == null)
            {
                return;
            }

            if (currentStep.Id == TutorialIds.Steps.Objective)
            {
                if (objectivePage < 2)
                {
                    objectivePage++;
                    ShowCurrentStep();
                }
                else
                {
                    AdvanceStep();
                }
            }
            else if (currentStep.Id == TutorialIds.Steps.WavePreview &&
                     firstWaveState > 0)
            {
                battle.WavePreviewView?.CloseExpandedDetail();
                firstWaveState = 0;
                AdvanceStep();
            }
            else if (currentStep.Id == TutorialIds.Steps.CardOrder)
            {
                AdvanceStep();
            }
            else if (currentStep.Id ==
                     TutorialIds.Steps.EnemyInspection)
            {
                if (enemyInspectionState == 0)
                {
                    requestsWorldPause = false;
                    if (battle.IsPaused)
                    {
                        enemyInspectionState = 3;
                        ShowCurrentStep();
                    }
                    else
                    {
                        enemyInspectionState = 1;
                        overlay.Hide();
                    }
                }
                else if (enemyInspectionState == 2)
                {
                    requestsWorldPause = false;
                    AdvanceStep();
                }
            }
            else if (currentStep.Id == TutorialIds.Steps.Complete)
            {
                CompleteCoreTutorial();
            }
            else if (currentStep.Id ==
                     TutorialIds.Steps.TowerUpgrade &&
                     !upgradeCompleted &&
                     !CanAffordSelectedTowerUpgrade())
            {
                upgradeDeferred = true;
                ShowCurrentStep();
            }
            else if (currentStep.Completion ==
                     TutorialCompletion.Acknowledged)
            {
                AdvanceStep();
            }
        }

        private void HandleSkipRequested()
        {
            if (coreActive)
            {
                // The target-cycle exercise deliberately puts slot 1 into
                // Enemy mode midway through the lesson. Skipping at that
                // exact moment must not leave the player's first-wave card
                // in the temporary demonstration state.
                if (currentStep != null &&
                    currentStep.Id == TutorialIds.Steps.CardTarget &&
                    cardTargetCycle == 1)
                {
                    Button projectileButton = battle.LoadoutView?
                        .GetSlotProjectileButton(0);
                    if (projectileButton != null &&
                        projectileButton.interactable)
                    {
                        projectileButton.onClick.Invoke();
                    }
                }

                battle.WavePreviewView?.CloseExpandedDetail();
                progress.MarkSkipped();
                EndCorePresentation();
            }
            else
            {
                AcknowledgeContextualTip();
            }
        }

        private void HandleWavePreviewOpened()
        {
            if (!coreActive || currentStep == null ||
                currentStep.Id != TutorialIds.Steps.WavePreview)
            {
                return;
            }

            firstWaveState = 1;
            ShowCurrentStep();
        }

        private void AdvanceStep()
        {
            stepIndex++;
            if (stepIndex < 0 || stepIndex >= definition.Steps.Count)
            {
                CompleteCoreTutorial();
                return;
            }

            currentStep = definition.Steps[stepIndex];
            if (currentStep.Id == TutorialIds.Steps.TowerSelect &&
                battle.SelectedTowerId >= 0)
            {
                // Placement selects the created tower as part of the normal
                // UI command. Do not ask for a redundant second click when
                // the required range/action context is already visible.
                AdvanceStep();
                return;
            }
            if (currentStep.Id == TutorialIds.Steps.FirstWave)
            {
                firstWaveState = 0;
            }
            else if (currentStep.Id ==
                     TutorialIds.Steps.EnemyInspection)
            {
                enemyInspectionState = 0;
                anchoredEnemyId = -1;
            }

            ShowCurrentStep();
        }

        private void CompleteCoreTutorial()
        {
            progress.MarkCompleted();
            EndCorePresentation();
            QueueStageIdentityTip();
        }

        private void CancelCoreWithoutResolving()
        {
            EndCorePresentation();
        }

        private void EndCorePresentation()
        {
            coreActive = false;
            currentStep = null;
            stepIndex = -1;
            requestsWorldPause = false;
            anchors.Clear();
            overlay.Hide();
        }

        private void DetectContextualTips(
            SimulationSnapshot snapshot,
            bool duringCore)
        {
            if (snapshot.Towers.Length >= 2 && previousTowerCount < 2)
            {
                QueueContextualTip(TutorialIds.ContextualTips.SecondTower);
            }

            int towerStateFingerprint = 17;
            for (int i = 0; i < snapshot.Towers.Length; i++)
            {
                TowerSnapshot tower = snapshot.Towers[i];
                unchecked
                {
                    towerStateFingerprint =
                        towerStateFingerprint * 31 + tower.Id;
                    towerStateFingerprint =
                        towerStateFingerprint * 31 + tower.Level;
                }
            }

            if (towerStateFingerprint != previousTowerStateFingerprint)
            {
                previousTowerStateFingerprint = towerStateFingerprint;
                int maximumUnlockedSlots = 0;
                int maximumComputeCapacity = 0;
                for (int i = 0; i < snapshot.Towers.Length; i++)
                {
                    TowerSnapshot tower = snapshot.Towers[i];
                    maximumUnlockedSlots = Mathf.Max(
                        maximumUnlockedSlots,
                        battle.AuthoritativeSimulation
                            .GetTowerUnlockedSlotCount(tower.Id));
                    if (battle.LoadedContent.TryGetTowerId(
                            tower.DefinitionId,
                            out TowerDefinitionId definitionId))
                    {
                        CompiledTowerLevelBalance level = battle
                            .LoadedContent
                            .GetTower(definitionId)
                            .GetLevel(tower.Level);
                        if (level != null)
                        {
                            maximumComputeCapacity = Mathf.Max(
                                maximumComputeCapacity,
                                level.ComputeCapacity);
                        }
                    }
                }

                if (maximumUnlockedSlots >= 2)
                {
                    QueueContextualTip(
                        TutorialIds.ContextualTips.SecondSlot);
                }
                bool computeCapacityIncreased =
                    hasObservedComputeCapacity &&
                    maximumComputeCapacity >
                    previousMaximumComputeCapacity;
                if (maximumUnlockedSlots >= 3 ||
                    computeCapacityIncreased)
                {
                    QueueContextualTip(
                        TutorialIds.ContextualTips.ThirdSlotAndCompute);
                }
                if (snapshot.Towers.Length > 0)
                {
                    hasObservedComputeCapacity = true;
                    previousMaximumComputeCapacity =
                        maximumComputeCapacity;
                }
            }

            if (!duringCore &&
                (snapshot.Phase == RunPhase.CardPackChoice ||
                 snapshot.Phase == RunPhase.CardPackLoadout))
            {
                QueueContextualTip(TutorialIds.ContextualTips.CardPack);
            }
            if (snapshot.Phase == RunPhase.Victory &&
                previousPhase != RunPhase.Victory)
            {
                ClearQueuedContextualTips();
                QueueContextualTip(TutorialIds.ContextualTips.Victory);
            }
            if (snapshot.Phase == RunPhase.Defeat &&
                previousPhase != RunPhase.Defeat)
            {
                ClearQueuedContextualTips();
                QueueContextualTip(TutorialIds.ContextualTips.Defeat);
            }
            if (snapshot.Phase == RunPhase.Combat &&
                battle.IsTowerBlueprintOpen)
            {
                QueueContextualTip(
                    TutorialIds.ContextualTips.CombatEditLocked);
            }
            if (!duringCore &&
                battle.SelectedEnemyId >= 0 &&
                battle.SelectedEnemyId != previousSelectedEnemyId)
            {
                for (int i = 0; i < snapshot.Enemies.Length; i++)
                {
                    if (snapshot.Enemies[i].Id == battle.SelectedEnemyId &&
                        snapshot.Enemies[i].Statuses.Length > 0)
                    {
                        QueueContextualTip(
                            TutorialIds.ContextualTips.StatusEffect);
                        break;
                    }
                }
            }

            bool forecastPhase = snapshot.Phase == RunPhase.Planning ||
                snapshot.Phase == RunPhase.AwaitingStartingTower;
            if (forecastPhase &&
                (snapshot.WaveIndex != previousForecastWaveIndex ||
                 snapshot.Phase != previousPhase))
            {
                previousForecastWaveIndex = snapshot.WaveIndex;
                WaveForecastSnapshot forecast = battle
                    .AuthoritativeSimulation
                    .GetUpcomingWaveForecast();
                if (forecast != null && forecast.IsAvailable)
                {
                    WaveForecastSpawn[] spawns = forecast.Spawns ??
                        Array.Empty<WaveForecastSpawn>();
                    for (int i = 0; i < spawns.Length; i++)
                    {
                        QueueContextualTip(
                            TutorialIds.ContextualTips.NewEnemyType);
                        if (spawns[i].IsBoss)
                        {
                            QueueContextualTip(
                                TutorialIds.ContextualTips.BossEnemy);
                        }
                        else if (spawns[i].IsElite)
                        {
                            QueueContextualTip(
                                TutorialIds.ContextualTips.EliteEnemy);
                        }
                    }
                }
            }
        }

        private void QueueStageIdentityTip()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            if (string.Equals(sceneName, "Stage02",
                    StringComparison.OrdinalIgnoreCase))
            {
                QueueContextualTip(TutorialIds.ContextualTips.StageTwo);
            }
            else if (string.Equals(sceneName, "Stage03",
                         StringComparison.OrdinalIgnoreCase))
            {
                QueueContextualTip(TutorialIds.ContextualTips.StageThree);
            }
        }

        private void QueueContextualTip(string id)
        {
            if (string.IsNullOrWhiteSpace(id) ||
                progress.HasSeenContextualTip(id) ||
                queuedContextualTips.Contains(id) ||
                string.Equals(activeContextualTipId, id,
                    StringComparison.Ordinal) ||
                !definition.TryFindContextualTip(
                    id,
                    out TutorialContextualTipDefinition _))
            {
                return;
            }

            queuedContextualTips.Add(id);
            contextualQueue.Enqueue(id);
        }

        private void ClearQueuedContextualTips()
        {
            contextualQueue.Clear();
            queuedContextualTips.Clear();
        }

        private void TryShowNextContextualTip(
            SimulationSnapshot snapshot)
        {
            if (!string.IsNullOrEmpty(activeContextualTipId) ||
                contextualQueue.Count == 0 ||
                GameGuideModal.IsAnyGuideOpen)
            {
                return;
            }

            bool safe = snapshot.Phase != RunPhase.Combat ||
                battle.IsPaused || battle.IsTowerBlueprintOpen;
            if (!safe)
            {
                return;
            }

            string id = contextualQueue.Dequeue();
            queuedContextualTips.Remove(id);
            if (!definition.TryFindContextualTip(
                    id,
                    out TutorialContextualTipDefinition tip))
            {
                return;
            }

            activeContextualTipId = id;
            requestsWorldPause = tip.PauseBattle;
            anchors.Clear();
            RegisterContextualAnchor(id);
            var content = new TutorialOverlayContent(
                anchors.Contains(CurrentAnchorId)
                    ? CurrentAnchorId
                    : string.Empty,
                tip.Title,
                tip.Body,
                "TIP");
            content.ShowNextButton = true;
            content.BlockOutsideHole = false;
            content.NextLabel = "확인";
            content.SkipLabel = "닫기";
            overlay.Show(content);
        }

        private void AcknowledgeContextualTip()
        {
            if (string.IsNullOrEmpty(activeContextualTipId))
            {
                requestsWorldPause = false;
                overlay.Hide();
                return;
            }

            progress.MarkContextualTipSeen(activeContextualTipId);
            activeContextualTipId = string.Empty;
            requestsWorldPause = false;
            anchors.Clear();
            overlay.Hide();
        }

        private void HandleContextualAction(TutorialAction action)
        {
            if (string.IsNullOrEmpty(activeContextualTipId))
            {
                return;
            }

            if (activeContextualTipId ==
                    TutorialIds.ContextualTips.SecondSlot &&
                action == TutorialAction.ReorderCard)
            {
                AcknowledgeContextualTip();
            }
            else if (activeContextualTipId ==
                         TutorialIds.ContextualTips.CardPack &&
                     (action == TutorialAction.ChooseDraftReward ||
                      action == TutorialAction.DragCardToSlot ||
                      action == TutorialAction.AutoEquipCard) &&
                     battle.CurrentPhase != RunPhase.CardPackLoadout)
            {
                AcknowledgeContextualTip();
            }
            else if (action == TutorialAction.OpenTowerLoadout &&
                     (activeContextualTipId ==
                          TutorialIds.ContextualTips.SecondSlot ||
                      activeContextualTipId ==
                          TutorialIds.ContextualTips.ThirdSlotAndCompute ||
                      activeContextualTipId ==
                          TutorialIds.ContextualTips.CardPack))
            {
                anchors.Clear();
                RegisterContextualAnchor(activeContextualTipId);
                overlay.RefreshNow();
            }
        }

        private void RegisterContextualAnchor(string id)
        {
            if (id == TutorialIds.ContextualTips.SecondTower)
            {
                RegisterWorldAnchor(
                    FindFirstTowerTransform(),
                    new Vector2(132f, 164f));
            }
            else if (id == TutorialIds.ContextualTips.SecondSlot ||
                     id == TutorialIds.ContextualTips.ThirdSlotAndCompute ||
                     id == TutorialIds.ContextualTips.CombatEditLocked)
            {
                if (battle.IsTowerBlueprintOpen)
                {
                    RegisterUiAnchor(AsRect(
                        battle.LoadoutView?.GetSlotButton(0)));
                }
                else
                {
                    RegisterWorldAnchor(
                        FindFirstTowerTransform(),
                        new Vector2(132f, 164f));
                }
            }
            else if (id == TutorialIds.ContextualTips.CardPack)
            {
                if (battle.CurrentPhase == RunPhase.CardPackChoice)
                {
                    RegisterRewardChoiceAnchor();
                }
                else if (battle.IsTowerBlueprintOpen)
                {
                    RegisterUiAnchor(AsRect(
                        battle.LoadoutView?.GetSlotButton(0)));
                }
                else
                {
                    RegisterWorldAnchor(
                        FindFirstTowerTransform(),
                        new Vector2(132f, 164f));
                }
            }
            else if (id == TutorialIds.ContextualTips.NewEnemyType ||
                     id == TutorialIds.ContextualTips.EliteEnemy ||
                     id == TutorialIds.ContextualTips.BossEnemy)
            {
                RegisterUiAnchor(AsRect(
                    battle.WavePreviewView?.SummaryButton));
            }
            else if (id == TutorialIds.ContextualTips.StatusEffect)
            {
                RegisterUiAnchor(
                    battle.EnemyInspectionView?.PanelRoot);
            }
            else
            {
                RegisterUiAnchor(AsRect(battle.Hud?.PlayButton));
            }
        }

        private void RegisterObjectiveAnchor()
        {
            StagePathAuthoring path = battle.StageMap?.Path;
            if (path == null || path.WaypointCount == 0)
            {
                return;
            }

            int waypointIndex = objectivePage == 0
                ? 0
                : objectivePage == 1
                    ? path.WaypointCount / 2
                    : path.WaypointCount - 1;
            EnsurePointAnchors();
            Transform anchor = pointAnchors[Mathf.Clamp(
                objectivePage, 0, pointAnchors.Count - 1)];
            anchor.position = path.GetWorldWaypoint(waypointIndex);
            RegisterWorldAnchor(anchor, new Vector2(128f, 128f));
        }

        private void EnsurePointAnchors()
        {
            while (pointAnchors.Count < 3)
            {
                var host = new GameObject(
                    "Tutorial World Point " + pointAnchors.Count);
                host.transform.SetParent(transform, false);
                host.hideFlags = HideFlags.DontSave;
                pointAnchors.Add(host.transform);
            }
        }

        private void RegisterUiAnchor(RectTransform target)
        {
            if (target != null)
            {
                anchors.RegisterUi(CurrentAnchorId, target);
            }
        }

        private void RegisterRewardChoiceAnchor()
        {
            if (battle.Hud == null)
            {
                return;
            }

            for (int i = 0; i < rewardChoiceAnchors.Length; i++)
            {
                Button choice = battle.Hud.GetRewardChoiceButton(i);
                rewardChoiceAnchors[i] = choice == null
                    ? null
                    : choice.transform as RectTransform;
            }
            anchors.RegisterUiGroup(
                CurrentAnchorId,
                rewardChoiceAnchors);
        }

        private void RegisterCardDragAnchor()
        {
            StageOneTowerLoadoutView loadout = battle.LoadoutView;
            if (loadout == null)
            {
                return;
            }

            cardDragAnchors[0] = loadout.VisibleCardCount > 0
                ? AsRect(loadout.GetCardButton(0))
                : null;
            Image dropSurface = loadout.GetSlotDropSurface(0);
            cardDragAnchors[1] = dropSurface == null
                ? null
                : dropSurface.rectTransform;
            anchors.RegisterUiGroup(
                CurrentAnchorId,
                cardDragAnchors);
        }

        private void RegisterWorldAnchor(
            Transform target,
            Vector2 screenSize)
        {
            if (target != null)
            {
                Camera camera = battle.CameraController == null
                    ? Camera.main
                    : battle.CameraController.GetComponent<Camera>();
                anchors.RegisterWorld(
                    CurrentAnchorId,
                    target,
                    camera,
                    screenSize);
            }
        }

        private TowerBuildSiteView FindFirstBuildSite()
        {
            FieldStageMap map = battle.StageMap;
            if (map == null)
            {
                return null;
            }

            for (int i = 0; i < map.BuildSiteCount; i++)
            {
                TowerBuildSiteView site = map.GetBuildSite(i);
                if (site != null && site.CanBuild)
                {
                    return site;
                }
            }
            return null;
        }

        private Transform FindFirstTowerTransform()
        {
            TowerSelectionView[] views =
                FindObjectsOfType<TowerSelectionView>();
            TowerSelectionView selected = null;
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] == null || !views[i].isActiveAndEnabled)
                {
                    continue;
                }
                if (selected == null ||
                    views[i].TowerId < selected.TowerId)
                {
                    selected = views[i];
                }
            }
            return selected == null ? null : selected.transform;
        }

        private StageOneEnemyView FindFirstLiveEnemy()
        {
            StageOneEnemyView[] views =
                FindObjectsOfType<StageOneEnemyView>();
            StageOneEnemyView selected = null;
            for (int i = 0; i < views.Length; i++)
            {
                StageOneEnemyView candidate = views[i];
                if (candidate == null || candidate.EntityId < 0 ||
                    !candidate.gameObject.activeInHierarchy ||
                    candidate.IsDeathPresentationActive)
                {
                    continue;
                }
                if (selected == null ||
                    candidate.EntityId < selected.EntityId)
                {
                    selected = candidate;
                }
            }
            return selected;
        }

        private bool CanAffordSelectedTowerUpgrade()
        {
            if (battle == null ||
                battle.AuthoritativeSimulation == null ||
                battle.SelectedTowerId < 0)
            {
                return false;
            }

            TowerUpgradeQuote quote = battle.AuthoritativeSimulation
                .GetTowerUpgradeQuote(battle.SelectedTowerId);
            return quote.Exists && quote.HasNextLevel &&
                quote.IsEligible && quote.CanAfford;
        }

        private static RectTransform AsRect(Button button)
        {
            return button == null
                ? null
                : button.transform as RectTransform;
        }

        private static bool IsRewardChoicePhase(RunPhase phase)
        {
            return phase == RunPhase.Draft ||
                phase == RunPhase.CardPackChoice;
        }
    }
}
