using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using RuleforgeTD.Audio;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;
using RuleforgeTD.Maps;
using RuleforgeTD.Simulation;
using RuleforgeTD.Towers.Archer;
using RuleforgeTD.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace RuleforgeTD.Battle
{
    /// <summary>
    /// 모든 전투 스테이지가 공유하는 런타임 bridge다.
    /// 타입 이름은 기존 씬 직렬화 호환을 위해 유지한다. GameSimulation이
    /// 권위 상태를 소유하고 이 컴포넌트는 입력과 snapshot 표현만 조정한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneBattleController : MonoBehaviour
    {
        /// <summary>
        /// 한 타워 인스턴스의 루트, 선택 UI, 선택적 전용 애니메이터와
        /// 이를 만든 정의/레벨을 한 수명 단위로 보관한다.
        /// 전용 프리팹이 ArcherTowerView를 갖는지와 무관하게 재사용 여부를 판정한다.
        /// </summary>
        private sealed class TowerPresentationHandle
        {
            public TowerPresentationHandle(
                string definitionId,
                int level,
                GameObject root,
                ArcherTowerView archer,
                TowerSelectionView selection)
            {
                DefinitionId = definitionId ?? string.Empty;
                Level = level;
                Root = root;
                Archer = archer;
                Selection = selection;
            }

            public string DefinitionId { get; }
            public int Level { get; }
            public GameObject Root { get; }
            public ArcherTowerView Archer { get; }
            public TowerSelectionView Selection { get; }

            public bool Matches(in TowerSnapshot tower)
            {
                return Root != null &&
                       Level == tower.Level &&
                       string.Equals(
                           DefinitionId,
                           tower.DefinitionId,
                           StringComparison.Ordinal);
            }
        }

        private const int MaximumTicksPerFrame = 8;
        private const int KonamiGoldReward = 1000;
        private static readonly KeyCode[] KonamiSequence =
        {
            KeyCode.UpArrow,
            KeyCode.UpArrow,
            KeyCode.DownArrow,
            KeyCode.DownArrow,
            KeyCode.LeftArrow,
            KeyCode.RightArrow,
            KeyCode.LeftArrow,
            KeyCode.RightArrow,
            KeyCode.B,
            KeyCode.A
        };
        private static readonly KeyCode[] KonamiInputKeys =
        {
            KeyCode.UpArrow,
            KeyCode.DownArrow,
            KeyCode.LeftArrow,
            KeyCode.RightArrow,
            KeyCode.B,
            KeyCode.A
        };
        private static readonly float[] SupportedSpeedMultipliers =
        {
            0.5f,
            1f,
            2f,
            3f
        };

        [SerializeField]
        private FieldStageMap stageMap;

        [SerializeField]
        private StageOnePresentationCatalog presentationCatalog;

        [SerializeField]
        private ulong seed = 12345UL;

        private readonly Dictionary<int, TowerPresentationHandle>
            towerPresentations =
                new Dictionary<int, TowerPresentationHandle>();
        private readonly Dictionary<int, StageOneEnemyView> enemyViews =
            new Dictionary<int, StageOneEnemyView>();
        private readonly Dictionary<int, StageOneProjectileView>
            projectileViews =
                new Dictionary<int, StageOneProjectileView>();
        private readonly Dictionary<int, Vector3>
            pendingProjectileLaunchOrigins =
                new Dictionary<int, Vector3>();
        private readonly Dictionary<int, int>
            pendingTowerAttackTargets =
                new Dictionary<int, int>();
        private readonly Dictionary<string, Queue<StageOneEnemyView>>
            enemyPools =
                new Dictionary<string, Queue<StageOneEnemyView>>(
                    StringComparer.Ordinal);
        private readonly Queue<StageOneProjectileView> projectilePool =
            new Queue<StageOneProjectileView>();
        private readonly List<int> removalIds = new List<int>(128);
        private readonly List<StageOneCardDisplay> cardDisplays =
            new List<StageOneCardDisplay>(4);
        private readonly List<StageOneLoadoutCard> loadoutCards =
            new List<StageOneLoadoutCard>(16);
        private readonly List<Vector2> loadoutMapPathPoints =
            new List<Vector2>(16);
        private readonly List<StageOneLoadoutMapSite> loadoutMapSites =
            new List<StageOneLoadoutMapSite>(12);
        private readonly List<StageOneTowerBuildOption>
            towerBuildOptions =
                new List<StageOneTowerBuildOption>(4);
        private readonly Vector3[] towerAimPositions =
            new Vector3[3];
        private readonly int[] towerAimEnemyIds =
            new int[3];
        private GameSimulation simulation;
        private CompiledContent content;
        private SimulationSnapshot snapshot;
        private StageOneUiTextCatalog textCatalog;
        private StageOneHudView hud;
        private WavePreviewPresenter wavePreviewPresenter;
        private StageOneTowerLoadoutView loadoutView;
        private StageOneTowerActionView towerActionView;
        private StageOneTowerBuildPickerView towerBuildPickerView;
        private StageOneEnemyInspectionController
            enemyInspectionController;
        private StageOneCameraController cameraController;
        private Transform towerRoot;
        private Transform enemyRoot;
        private Transform projectileRoot;
        private Transform effectRoot;
        private StageOneFireTrailRenderer fireTrailRenderer;
        private StageOneCardEffectVfxView cardEffectVfx;
        private float tickAccumulator;
        private float speedMultiplier = 1f;
        private bool paused;
        private bool towerBlueprintOpen;
        private bool pausedBeforeTowerBlueprint;
        private bool resumeCombatAfterTowerBlueprint;
        private Coroutine towerBlueprintCloseRoutine;
        private bool isInitialized;
        private RunPhase lastPresentedPhase = (RunPhase)(-1);
        private int selectedTowerId = -1;
        private int selectedTowerSlot;
        private int pendingBuildPointIndex = -1;
        private int konamiSequenceIndex;
        private int effectsPresentedThisFrame;

        public FieldStageMap StageMap => stageMap;
        public StageOnePresentationCatalog PresentationCatalog =>
            presentationCatalog;
        public ulong Seed => seed;
        public bool IsInitialized => isInitialized;
        public bool IsPaused => paused;
        public bool IsTowerBlueprintOpen => towerBlueprintOpen;
        public float SpeedMultiplier => speedMultiplier;
        public StageOneHudView Hud => hud;
        public WavePreviewView WavePreviewView =>
            wavePreviewPresenter == null
                ? null
                : wavePreviewPresenter.View;
        public StageOneTowerLoadoutView LoadoutView => loadoutView;
        public StageOneTowerActionView TowerActionView =>
            towerActionView;
        public StageOneTowerBuildPickerView TowerBuildPickerView =>
            towerBuildPickerView;
        public StageOneEnemyInspectionController
            EnemyInspectionController =>
                enemyInspectionController;
        public StageOneEnemyInspectionView EnemyInspectionView =>
            enemyInspectionController == null
                ? null
                : enemyInspectionController.InspectionView;
        public int SelectedEnemyId =>
            enemyInspectionController == null
                ? -1
                : enemyInspectionController.SelectedEnemyId;
        public StageOneCameraController CameraController =>
            cameraController;
        public int SelectedTowerId => selectedTowerId;
        public int PendingBuildPointIndex =>
            pendingBuildPointIndex;
        public TowerSelectionView SelectedTowerSelectionView =>
            towerPresentations.TryGetValue(
                selectedTowerId,
                out TowerPresentationHandle presentation)
                ? presentation.Selection
                : null;
        public SimulationSnapshot CurrentSnapshot => snapshot;
        public RunPhase CurrentPhase =>
            snapshot == null
                ? RunPhase.AwaitingStartingTower
                : snapshot.Phase;
        public int TowerViewCount => towerPresentations.Count;
        public int EnemyViewCount => enemyViews.Count;
        public int ProjectileViewCount => projectileViews.Count;
        public int FireTrailSegmentCount =>
            fireTrailRenderer == null
                ? 0
                : fireTrailRenderer.VisibleHazardCount;
        public StageOneFireTrailRenderer FireTrailRenderer =>
            fireTrailRenderer;
        public StageOneCardEffectVfxView CardEffectVfx =>
            cardEffectVfx;
        public int KonamiSequenceProgress =>
            konamiSequenceIndex;
        /// <summary>
        /// 같은 Runtime assembly의 명시적 외부 제어 어댑터가 권위 시뮬레이션에
        /// 연결할 때만 사용하는 좁은 seam이다. 일반 HUD는 이 속성을 사용하지 않는다.
        /// </summary>
        internal GameSimulation AuthoritativeSimulation =>
            simulation;

        internal CompiledContent LoadedContent =>
            content;

        internal StageOneUiTextCatalog LoadedTextCatalog =>
            textCatalog;

        private void Start()
        {
            InitializeNow();
        }

        private void Update()
        {
            if (!isInitialized)
            {
                return;
            }

            effectsPresentedThisFrame = 0;
            PollKonamiKeyboardInput();
            AdvanceCombatClock();
            ReconcilePresentation();
            ApplyWorldTimeScale();
        }

        private void OnDestroy()
        {
            UnhookInput();
            if (Time.timeScale != 1f)
            {
                Time.timeScale = 1f;
            }
        }

        public void ConfigureAuthoring(
            FieldStageMap sourceStageMap,
            StageOnePresentationCatalog sourceCatalog,
            ulong deterministicSeed)
        {
            stageMap = sourceStageMap;
            presentationCatalog = sourceCatalog;
            seed = deterministicSeed;
        }

        /// <summary>
        /// Explicit entry point used by PlayMode tests and runtime Start.
        /// Safe to call more than once.
        /// </summary>
        public void InitializeNow()
        {
            if (isInitialized)
            {
                return;
            }

            if (stageMap == null)
            {
                stageMap = FindObjectOfType<FieldStageMap>();
            }

            if (stageMap == null)
            {
                throw new InvalidOperationException(
                    "A battle stage requires a FieldStageMap.");
            }

            if (presentationCatalog == null ||
                presentationCatalog.ContentJson == null ||
                presentationCatalog.LocalizationJson == null)
            {
                throw new InvalidOperationException(
                    "A battle stage requires a presentation catalog with " +
                    "base content and localization JSON.");
            }

            TextAsset[] cardContentModules =
                presentationCatalog.CardContentModules;
            content = LogicContentJsonLoader.Load(
                presentationCatalog.ContentJson,
                cardContentModules);
            StageOneCardEffectPalette.RegisterContent(content);
            textCatalog = StageOneUiTextCatalog.Load(
                presentationCatalog.LocalizationJson,
                cardContentModules);
            textCatalog.ValidateCardDefinitions(content);
            textCatalog.ValidateWavePreviewDefinitions(content);
            simulation = new GameSimulation();
            simulation.Initialize(content, seed);

            CreatePresentationRoots();
            hud = StageOneHudView.CreateRuntime(
                textCatalog,
                transform);
            hud.SetFont(presentationCatalog.UiFont);
            WavePreviewView wavePreviewView =
                WavePreviewView.CreateRuntime(
                    textCatalog,
                    presentationCatalog.UiFont,
                    transform);
            wavePreviewPresenter = new WavePreviewPresenter(
                wavePreviewView,
                content,
                textCatalog,
                presentationCatalog);
            loadoutView =
                StageOneTowerLoadoutView.CreateRuntime(
                    textCatalog,
                    presentationCatalog.UiFont,
                    transform);
            towerActionView =
                StageOneTowerActionView.CreateRuntime(
                    textCatalog,
                    presentationCatalog.UiFont,
                    transform);
            towerBuildPickerView =
                StageOneTowerBuildPickerView.CreateRuntime(
                    textCatalog,
                    presentationCatalog.UiFont,
                    transform);
            ConfigureStageCamera();
            enemyInspectionController =
                GetComponent<
                    StageOneEnemyInspectionController>();
            if (enemyInspectionController == null)
            {
                enemyInspectionController =
                    gameObject.AddComponent<
                        StageOneEnemyInspectionController>();
            }

            enemyInspectionController.Configure(
                content,
                textCatalog,
                presentationCatalog.UiFont,
                cameraController,
                transform);
            HookInput();

            isInitialized = true;
            snapshot = simulation.GetSnapshot();
            hud.SetSpeed(speedMultiplier);
            hud.SetStatus("status.ready");
            ReconcilePresentation();
            ApplyWorldTimeScale();
        }

        /// <summary>
        /// 권위 시뮬레이션이 일반 GameCommand 경로 밖의 명시적 도구 포트로 변경된 뒤,
        /// 누적 표현 이벤트와 캐시 스냅샷을 같은 프레임에 동기화한다.
        /// 일시정지 상태에서도 수동 스폰·배치·제거 결과가 즉시 보이게 하는
        /// Runtime assembly 내부 훅이다.
        /// </summary>
        internal void SynchronizeAuthoritativeState()
        {
            if (!isInitialized || simulation == null)
            {
                return;
            }

            snapshot = simulation.GetSnapshot();
            ProcessPresentationEvents();
            ReconcilePresentation();
            ApplyWorldTimeScale();
        }

        public bool ProcessKonamiKey(KeyCode key)
        {
            if (!isInitialized)
            {
                return false;
            }

            if (key == KonamiSequence[konamiSequenceIndex])
            {
                konamiSequenceIndex++;
            }
            else
            {
                konamiSequenceIndex =
                    key == KonamiSequence[0]
                        ? 1
                        : 0;
            }

            if (konamiSequenceIndex <
                KonamiSequence.Length)
            {
                return false;
            }

            konamiSequenceIndex = 0;
            CommandResult result = simulation.Submit(
                GameCommand.GrantDebugGold(
                    KonamiGoldReward));
            if (!result.Accepted)
            {
                return false;
            }

            snapshot = simulation.GetSnapshot();
            ProcessPresentationEvents();
            RefreshHud();
            hud.SetStatus(
                "status.konami_gold_format",
                KonamiGoldReward);
            return true;
        }

        private void PollKonamiKeyboardInput()
        {
            for (int i = 0;
                 i < KonamiInputKeys.Length;
                 i++)
            {
                KeyCode key = KonamiInputKeys[i];
                if (Input.GetKeyDown(key))
                {
                    ProcessKonamiKey(key);
                    return;
                }
            }
        }

        /// <summary>
        /// Opens the tower chooser for an available fixed build point.
        /// No tower is placed until the player explicitly selects an option.
        /// </summary>
        public bool TryBuildAt(int buildPointIndex)
        {
            if (!isInitialized ||
                snapshot == null ||
                (snapshot.Phase !=
                    RunPhase.AwaitingStartingTower &&
                 snapshot.Phase != RunPhase.Planning &&
                 snapshot.Phase != RunPhase.Combat))
            {
                return false;
            }

            TowerBuildSiteView site = FindBuildSite(
                buildPointIndex);
            if (site == null || !site.CanBuild)
            {
                return false;
            }

            return ShowTowerBuildPicker(site);
        }

        /// <summary>
        /// Places the explicitly selected tower at the pending build point.
        /// For the first tower this also commits the starting-tower choice.
        /// </summary>
        public bool TryBuildAt(
            string towerDefinitionId,
            int buildPointIndex)
        {
            if (!isInitialized ||
                snapshot == null ||
                string.IsNullOrWhiteSpace(towerDefinitionId))
            {
                return false;
            }

            if (snapshot.Phase ==
                RunPhase.AwaitingStartingTower)
            {
                CommandResult choiceResult = simulation.Submit(
                    GameCommand.ChooseStartingTower(
                        towerDefinitionId));
                if (!choiceResult.Accepted)
                {
                    ShowBuildFailure(choiceResult);
                    snapshot = simulation.GetSnapshot();
                    ReconcilePresentation();
                    return false;
                }

                snapshot = simulation.GetSnapshot();
            }

            if (snapshot.Phase != RunPhase.Planning &&
                snapshot.Phase != RunPhase.Combat)
            {
                return false;
            }

            CommandResult result = simulation.Submit(
                GameCommand.PlaceTower(
                    towerDefinitionId,
                    buildPointIndex));
            if (!result.Accepted)
            {
                ShowBuildFailure(result);
                snapshot = simulation.GetSnapshot();
                ReconcilePresentation();
                return false;
            }

            snapshot = simulation.GetSnapshot();
            TowerSnapshot placedTower = FindTowerAt(buildPointIndex);
            ProcessPresentationEvents();
            hud.SetStatus("status.tower_placed");
            ReconcilePresentation();
            SelectTowerContext(placedTower.Id);
            return true;
        }

        /// <summary>
        /// Starts the next wave, pauses combat, or resumes combat depending
        /// on the current run phase.
        /// </summary>
        public bool TogglePlay()
        {
            if (!isInitialized || snapshot == null)
            {
                return false;
            }

            if (towerBlueprintOpen)
            {
                return false;
            }

            if (snapshot.Phase == RunPhase.Combat)
            {
                paused = !paused;
                hud.SetStatus(
                    paused
                        ? "status.paused"
                        : "status.resumed");
                RefreshHud();
                ApplyWorldTimeScale();
                return true;
            }

            if (snapshot.Phase == RunPhase.Victory)
            {
                CommandResult continueResult = simulation.Submit(
                    GameCommand.ContinueStage());
                if (!continueResult.Accepted)
                {
                    return false;
                }

                paused = false;
                tickAccumulator = 0f;
                snapshot = simulation.GetSnapshot();
                hud.SetStatus(
                    "status.endless_stage_started_format",
                    snapshot.StageNumber);
                ReconcilePresentation();
                ApplyWorldTimeScale();
                return true;
            }

            if (snapshot.Phase != RunPhase.Planning)
            {
                return false;
            }

            CommandResult result = simulation.Submit(
                GameCommand.StartWave());
            if (!result.Accepted)
            {
                hud.SetStatus(
                    result.Error == CommandError.InvalidTarget
                        ? "status.no_tower"
                        : "status.tower_place_failed_format",
                    result.Error.ToString());
                RefreshHud();
                return false;
            }

            paused = false;
            tickAccumulator = 0f;
            snapshot = simulation.GetSnapshot();
            ProcessPresentationEvents();
            hud.SetStatus(
                "status.wave_started_format",
                snapshot.WaveIndex + 1);
            ReconcilePresentation();
            ApplyWorldTimeScale();
            return true;
        }

        /// <summary>
        /// Compatibility cycle used by keyboard/test callers. The HUD exposes
        /// each speed directly, so players do not need to cycle through them.
        /// </summary>
        public float ToggleSpeed()
        {
            int currentIndex = FindSupportedSpeedIndex(
                speedMultiplier);
            int nextIndex =
                (currentIndex + 1) %
                SupportedSpeedMultipliers.Length;
            return SetSpeed(
                SupportedSpeedMultipliers[nextIndex]);
        }

        public float SetSpeed(float multiplier)
        {
            speedMultiplier =
                SupportedSpeedMultipliers[
                    FindSupportedSpeedIndex(multiplier)];
            if (hud != null)
            {
                hud.SetSpeed(speedMultiplier);
            }

            ApplyWorldTimeScale();
            return speedMultiplier;
        }

        private void AdvanceCombatClock()
        {
            if (snapshot == null ||
                snapshot.Phase != RunPhase.Combat ||
                paused)
            {
                tickAccumulator = 0f;
                return;
            }

            float secondsPerTick = 1f / content.Run.TickRate;
            tickAccumulator +=
                Time.unscaledDeltaTime * speedMultiplier;
            int ticks = 0;
            while (tickAccumulator >= secondsPerTick &&
                   ticks < MaximumTicksPerFrame &&
                   snapshot.Phase == RunPhase.Combat)
            {
                tickAccumulator -= secondsPerTick;
                simulation.Step();
                snapshot = simulation.GetSnapshot();
                ProcessPresentationEvents();
                ticks++;
            }

            if (ticks == MaximumTicksPerFrame &&
                tickAccumulator >= secondsPerTick)
            {
                tickAccumulator = secondsPerTick;
            }
        }

        private void HandleBuildSiteClicked(TowerBuildSiteView site)
        {
            if (site != null)
            {
                TryBuildAt(site.BuildPointIndex);
            }
        }

        private void HandleTowerBuildRequested(
            string towerDefinitionId)
        {
            int buildPointIndex = pendingBuildPointIndex;
            if (towerBuildPickerView != null)
            {
                towerBuildPickerView.Hide();
            }

            pendingBuildPointIndex = -1;
            TryBuildAt(towerDefinitionId, buildPointIndex);
        }

        private void HandleTowerBuildPickerClosed()
        {
            pendingBuildPointIndex = -1;
        }

        private void HandleBackgroundClicked(Vector2 screenPosition)
        {
            if (towerBlueprintOpen ||
                hud == null ||
                hud.IsRewardVisible)
            {
                return;
            }

            bool hadTowerContext = selectedTowerId >= 0;
            bool hadBuildPicker =
                pendingBuildPointIndex >= 0 ||
                (towerBuildPickerView != null &&
                 towerBuildPickerView.IsVisible);
            if (!hadTowerContext && !hadBuildPicker)
            {
                return;
            }

            HideTowerBuildPicker();
            selectedTowerId = -1;
            selectedTowerSlot = 0;
            RefreshTowerSelectionIndicators();
            RefreshTowerActionView();
        }

        private void HandleTowerClicked(TowerSelectionView selection)
        {
            if (selection != null)
            {
                HideTowerBuildPicker();
                SelectTowerContext(selection.TowerId);
            }
        }

        private void HandleEnemySelected(int enemyId)
        {
            HideTowerBuildPicker();
            selectedTowerId = -1;
            selectedTowerSlot = 0;
            RefreshTowerSelectionIndicators();
            RefreshTowerActionView();
            hud?.SetStatus("status.enemy_selected");
        }

        private void HandleRewardVisibilityChanged(bool visible)
        {
            // The reward chooser is a modal boundary even though it lives on
            // the HUD canvas. Do not let a tower context from the previous
            // combat remain alive behind it: that separate canvas can keep
            // blocking the first world click after the reward closes.
            if (visible)
            {
                HideTowerBuildPicker();
                if (enemyInspectionController != null &&
                    !enemyInspectionController
                        .IsShowingDefeatedEnemy)
                {
                    enemyInspectionController.ClearSelection();
                }

                selectedTowerId = -1;
                selectedTowerSlot = 0;
            }

            foreach (TowerPresentationHandle presentation in
                     towerPresentations.Values)
            {
                TowerSelectionView selection =
                    presentation.Selection;
                if (selection != null)
                {
                    selection.ResetPointerClickSequence();
                }
            }

            RefreshTowerSelectionIndicators();
            RefreshTowerActionView();
        }

        private void HandleTowerDoubleClicked(
            TowerSelectionView selection)
        {
            if (selection != null)
            {
                HideTowerBuildPicker();
                SelectTower(selection.TowerId);
            }
        }

        private bool ShowTowerBuildPicker(
            TowerBuildSiteView site)
        {
            towerBuildOptions.Clear();
            if (snapshot.Phase ==
                RunPhase.AwaitingStartingTower)
            {
                TowerDefinitionId[] startingChoices =
                    content.Run.StartingTowerChoices;
                for (int i = 0;
                     i < startingChoices.Length;
                     i++)
                {
                    AddTowerBuildOption(
                        content.GetTower(
                            startingChoices[i]));
                }
            }
            else
            {
                string[] unlockedTowerIds =
                    snapshot.UnlockedTowerIds;
                for (int i = 0;
                     i < unlockedTowerIds.Length;
                     i++)
                {
                    if (content.TryGetTowerId(
                            unlockedTowerIds[i],
                            out TowerDefinitionId towerId))
                    {
                        AddTowerBuildOption(
                            content.GetTower(towerId));
                    }
                }
            }

            if (towerBuildOptions.Count == 0 ||
                towerBuildPickerView == null)
            {
                return false;
            }

            enemyInspectionController?.ClearSelection();
            selectedTowerId = -1;
            RefreshTowerSelectionIndicators();
            RefreshTowerActionView();
            pendingBuildPointIndex =
                site.BuildPointIndex;
            towerBuildPickerView.Show(
                site,
                towerBuildOptions,
                snapshot.Gold);
            hud.SetStatus("status.choose_tower");
            return true;
        }

        private void AddTowerBuildOption(
            CompiledTowerDefinition definition)
        {
            if (simulation == null)
            {
                return;
            }

            TowerConstructionQuote constructionQuote =
                simulation.GetTowerConstructionQuote(
                    definition.StableId);
            if (!constructionQuote.IsKnown)
            {
                return;
            }

            towerBuildOptions.Add(
                new StageOneTowerBuildOption(
                    definition.StableId,
                    textCatalog.GetTowerName(
                        definition.StableId),
                    textCatalog.GetTowerDescription(
                        definition.StableId),
                    definition.SubjectTypeMode ==
                        SubjectTypeMode.Enemy,
                    constructionQuote.Cost,
                    constructionQuote.CanAfford));
        }

        private void HideTowerBuildPicker()
        {
            pendingBuildPointIndex = -1;
            if (towerBuildPickerView != null)
            {
                towerBuildPickerView.Hide();
            }
        }

        /// <summary>
        /// Selects a tower in the field without pausing combat or opening the
        /// modal blueprint. This exposes its range and compact actions.
        /// </summary>
        public bool SelectTowerContext(int towerId)
        {
            TowerSnapshot tower = FindTowerById(towerId);
            if (tower.Id < 0)
            {
                return false;
            }

            enemyInspectionController?.ClearSelection();
            HideTowerBuildPicker();
            selectedTowerId = towerId;
            selectedTowerSlot = 0;
            RefreshTowerSelectionIndicators();
            hud.SetStatus("status.tower_selected");
            RefreshTowerLoadout();
            RefreshTowerActionView();
            return true;
        }

        /// <summary>
        /// Opens the selected tower directly in the modal card blueprint.
        /// Kept as the explicit API for build flow, double-clicks, and tests.
        /// </summary>
        public bool SelectTower(int towerId)
        {
            if (!SelectTowerContext(towerId))
            {
                return false;
            }

            EnterTowerBlueprint();
            RefreshTowerSelectionIndicators();
            RefreshTowerActionView();
            RefreshTowerLoadout();
            return true;
        }

        private void HandleTowerSlotRequested(int slotIndex)
        {
            TowerSnapshot tower =
                FindTowerById(selectedTowerId);
            if (tower.Id < 0)
            {
                return;
            }

            int unlocked =
                ResolveTowerUnlockedSlotCount(tower);
            if (slotIndex < 0 || slotIndex >= unlocked)
            {
                return;
            }

            selectedTowerSlot = slotIndex;
            RefreshTowerLoadout();
        }

        private void HandleTowerSlotUnequipRequested(
            int slotIndex)
        {
            TowerSnapshot tower =
                FindTowerById(selectedTowerId);
            int unlockedSlotCount = tower.Id < 0
                ? 0
                : ResolveTowerUnlockedSlotCount(tower);
            if (slotIndex < 0 ||
                slotIndex >= unlockedSlotCount)
            {
                return;
            }

            int cardInstanceId =
                tower.CardInstanceIds[slotIndex];
            if (cardInstanceId < 0)
            {
                return;
            }

            CardInstanceSnapshot card =
                FindCardById(cardInstanceId);
            if (!card.Equipped ||
                card.TowerId != tower.Id ||
                card.Slot != slotIndex)
            {
                return;
            }

            selectedTowerSlot = slotIndex;
            CompiledCardDefinition definition =
                content.GetCard(card.DefinitionId);
            CompleteLoadoutCommand(
                simulation.Submit(
                    GameCommand.UnequipCard(card.Id)),
                "status.card_unequipped_format",
                new object[]
                {
                    textCatalog.ResolveDisplayName(
                        definition)
                });
        }

        private void HandleTowerCardRequested(int cardInstanceId)
        {
            if (!IsLoadoutEditable())
            {
                hud.SetStatus("status.loadout_locked");
                return;
            }

            TowerSnapshot tower =
                FindTowerById(selectedTowerId);
            CardInstanceSnapshot card =
                FindCardById(cardInstanceId);
            if (tower.Id < 0 || card.Id < 0)
            {
                return;
            }

            string cardName = textCatalog.ResolveDisplayName(
                content.GetCard(card.DefinitionId));
            CommandResult result;
            string successStatus;
            object[] successArguments;
            if (card.Equipped &&
                card.TowerId == tower.Id)
            {
                result = simulation.Submit(
                    GameCommand.UnequipCard(card.Id));
                successStatus =
                    "status.card_unequipped_format";
                successArguments =
                    new object[] { cardName };
            }
            else
            {
                int slotValue =
                    tower.CardInstanceIds[selectedTowerSlot];
                if (slotValue != -1)
                {
                    hud.SetStatus("status.slot_occupied");
                    return;
                }

                result = simulation.Submit(
                    card.Equipped
                        ? GameCommand.MoveCard(
                            card.Id,
                            tower.Id,
                            selectedTowerSlot)
                        : GameCommand.EquipCard(
                            card.Id,
                            tower.Id,
                            selectedTowerSlot));
                successStatus =
                    "status.card_equipped_format";
                successArguments =
                    new object[]
                    {
                        cardName,
                        selectedTowerSlot + 1
                    };
            }

            CompleteLoadoutCommand(
                result,
                successStatus,
                successArguments);
        }

        private void HandleTowerCardDoubleClickRequested(
            int cardInstanceId,
            int startedSlotIndex)
        {
            if (!IsLoadoutEditable())
            {
                hud.SetStatus("status.loadout_locked");
                return;
            }

            TowerSnapshot tower =
                FindTowerById(selectedTowerId);
            CardInstanceSnapshot card =
                FindCardById(cardInstanceId);
            if (tower.Id < 0 || card.Id < 0)
            {
                return;
            }

            int unlockedSlotCount =
                ResolveTowerUnlockedSlotCount(tower);
            CompiledCardDefinition definition =
                content.GetCard(card.DefinitionId);

            // A regular first click unequips a card that was already on this
            // tower. A second click is the automatic-equip gesture, so put
            // that card back instead of unexpectedly moving another card.
            if (startedSlotIndex >= 0 &&
                startedSlotIndex < unlockedSlotCount)
            {
                if (card.Equipped &&
                    card.TowerId == tower.Id &&
                    card.Slot == startedSlotIndex)
                {
                    selectedTowerSlot = startedSlotIndex;
                    RefreshTowerLoadout();
                    return;
                }

                selectedTowerSlot = startedSlotIndex;
                CompleteLoadoutCommand(
                    simulation.Submit(
                        card.Equipped
                            ? GameCommand.MoveCard(
                                card.Id,
                                tower.Id,
                                startedSlotIndex)
                            : GameCommand.EquipCard(
                                card.Id,
                                tower.Id,
                                startedSlotIndex)),
                    "status.card_equipped_format",
                    new object[]
                    {
                        textCatalog.ResolveDisplayName(
                            definition),
                        startedSlotIndex + 1
                    });
                return;
            }

            // Empty slots always win from top to bottom. If the ordinary
            // first click already placed this card, its current slot is
            // treated as the same pre-click empty slot.
            int firstCandidateSlot = -1;
            int automaticSlot = -1;
            for (int slot = 0; slot < unlockedSlotCount; slot++)
            {
                int slotCardInstanceId =
                    tower.CardInstanceIds[slot];
                bool available = slotCardInstanceId == -1 ||
                    slotCardInstanceId == card.Id;
                if (!available)
                {
                    continue;
                }

                if (firstCandidateSlot < 0)
                {
                    firstCandidateSlot = slot;
                }

                CardPlacementQuote quote =
                    simulation.GetCardPlacementQuote(
                        card.Id,
                        tower.Id,
                        slot);
                if (quote.CanPlace)
                {
                    automaticSlot = slot;
                    break;
                }
            }

            if (automaticSlot < 0)
            {
                automaticSlot = firstCandidateSlot;
            }

            if (automaticSlot >= 0)
            {
                selectedTowerSlot = automaticSlot;
                if (card.Equipped &&
                    card.TowerId == tower.Id &&
                    card.Slot == automaticSlot)
                {
                    RefreshTowerLoadout();
                    return;
                }

                CompleteLoadoutCommand(
                    simulation.Submit(
                        card.Equipped &&
                        card.TowerId == tower.Id
                            ? GameCommand.ReorderCard(
                                tower.Id,
                                card.Slot,
                                automaticSlot)
                            : card.Equipped
                                ? GameCommand.MoveCard(
                                    card.Id,
                                    tower.Id,
                                    automaticSlot)
                                : GameCommand.EquipCard(
                                    card.Id,
                                    tower.Id,
                                    automaticSlot)),
                    "status.card_equipped_format",
                    new object[]
                    {
                        textCatalog.ResolveDisplayName(
                            definition),
                        automaticSlot + 1
                    });
                return;
            }

            // No empty slot remains. Replace the bottom-most primary card;
            // continuation cells of multi-slot cards are skipped.
            int replacementSlot = -1;
            for (int slot = unlockedSlotCount - 1;
                 slot >= 0;
                 slot--)
            {
                if (tower.CardInstanceIds[slot] >= 0 &&
                    tower.CardInstanceIds[slot] != card.Id)
                {
                    replacementSlot = slot;
                    break;
                }
            }

            if (replacementSlot < 0)
            {
                return;
            }

            selectedTowerSlot = replacementSlot;
            CompleteLoadoutCommand(
                simulation.Submit(
                    GameCommand.ReplaceCard(
                        card.Id,
                        tower.Id,
                        replacementSlot)),
                "status.card_equipped_format",
                new object[]
                {
                    textCatalog.ResolveDisplayName(definition),
                    replacementSlot + 1
                });
        }

        private void HandleTowerCardDropped(
            int cardInstanceId,
            int slotIndex)
        {
            if (!IsLoadoutEditable())
            {
                hud.SetStatus("status.loadout_locked");
                return;
            }

            TowerSnapshot tower =
                FindTowerById(selectedTowerId);
            CardInstanceSnapshot card =
                FindCardById(cardInstanceId);
            int unlockedSlotCount = tower.Id < 0
                ? 0
                : ResolveTowerUnlockedSlotCount(tower);
            if (tower.Id < 0 ||
                card.Id < 0 ||
                slotIndex < 0 ||
                slotIndex >= unlockedSlotCount)
            {
                return;
            }

            selectedTowerSlot = slotIndex;
            if (card.Equipped &&
                card.TowerId == tower.Id &&
                card.Slot == slotIndex)
            {
                RefreshTowerLoadout();
                return;
            }

            CommandResult result;
            if (card.Equipped &&
                card.TowerId == tower.Id)
            {
                result = simulation.Submit(
                    GameCommand.ReorderCard(
                        tower.Id,
                        card.Slot,
                        slotIndex));
            }
            else
            {
                int slotValue =
                    tower.CardInstanceIds[slotIndex];
                if (slotValue >= 0)
                {
                    result = simulation.Submit(
                        GameCommand.ReplaceCard(
                            card.Id,
                            tower.Id,
                            slotIndex));
                }
                else if (slotValue != -1)
                {
                    hud.SetStatus("status.slot_occupied");
                    RefreshTowerLoadout();
                    return;
                }
                else
                {
                    result = simulation.Submit(
                        card.Equipped
                            ? GameCommand.MoveCard(
                                card.Id,
                                tower.Id,
                                slotIndex)
                            : GameCommand.EquipCard(
                                card.Id,
                                tower.Id,
                                slotIndex));
                }
            }

            CompiledCardDefinition definition =
                content.GetCard(card.DefinitionId);
            CompleteLoadoutCommand(
                result,
                "status.card_equipped_format",
                new object[]
                {
                    textCatalog.ResolveDisplayName(
                        definition),
                    slotIndex + 1
                });
        }

        private void HandleTowerUpgradeRequested()
        {
            if (!IsLoadoutEditable())
            {
                hud.SetStatus("status.loadout_locked");
                return;
            }

            CommandResult result = simulation.Submit(
                GameCommand.UpgradeTower(selectedTowerId));
            snapshot = simulation.GetSnapshot();
            TowerSnapshot tower =
                FindTowerById(selectedTowerId);
            CompleteLoadoutCommand(
                result,
                "status.tower_upgraded_format",
                new object[] { tower.Level });
        }

        private void HandleTowerCardsRequested()
        {
            if (selectedTowerId >= 0)
            {
                SelectTower(selectedTowerId);
            }
        }

        private void HandleTowerSubjectRequested(
            SubjectType subjectType)
        {
            if (!IsLoadoutEditable())
            {
                hud.SetStatus("status.loadout_locked");
                return;
            }

            CommandResult result = simulation.Submit(
                GameCommand.SetTowerSubjectType(
                    selectedTowerId,
                    subjectType));
            CompleteLoadoutCommand(
                result,
                subjectType == SubjectType.Projectile
                    ? "status.subject_projectile"
                    : "status.subject_enemy",
                Array.Empty<object>());
        }

        private void HandleTowerSlotSubjectRequested(
            int slotIndex,
            SubjectType subjectType)
        {
            if (!IsLoadoutEditable())
            {
                hud.SetStatus("status.loadout_locked");
                return;
            }

            selectedTowerSlot = slotIndex;
            CommandResult result = simulation.Submit(
                GameCommand.SetTowerSlotSubjectType(
                    selectedTowerId,
                    slotIndex,
                    subjectType));
            CompleteLoadoutCommand(
                result,
                subjectType == SubjectType.Projectile
                    ? "status.subject_projectile"
                    : "status.subject_enemy",
                Array.Empty<object>());
        }

        private void HandleTowerPanelClosed()
        {
            selectedTowerId = -1;
            RefreshTowerSelectionIndicators();
            RefreshTowerActionView();
            if (towerBlueprintCloseRoutine != null)
            {
                StopCoroutine(towerBlueprintCloseRoutine);
            }

            towerBlueprintCloseRoutine = StartCoroutine(
                ExitTowerBlueprintAfterTransition());
        }

        private IEnumerator ExitTowerBlueprintAfterTransition()
        {
            while (loadoutView != null &&
                   (loadoutView.IsVisible ||
                    loadoutView.IsTransitionRunning))
            {
                yield return null;
            }

            towerBlueprintCloseRoutine = null;
            ExitTowerBlueprint();
        }

        private void EnterTowerBlueprint()
        {
            HideTowerBuildPicker();
            if (towerBlueprintCloseRoutine != null)
            {
                StopCoroutine(towerBlueprintCloseRoutine);
                towerBlueprintCloseRoutine = null;
            }

            if (towerBlueprintOpen)
            {
                if (hud != null)
                {
                    hud.SetVisible(false);
                }
                wavePreviewPresenter?.Hide();

                ApplyWorldTimeScale();
                return;
            }

            towerBlueprintOpen = true;
            pausedBeforeTowerBlueprint = paused;
            resumeCombatAfterTowerBlueprint =
                snapshot != null &&
                snapshot.Phase == RunPhase.Combat &&
                !pausedBeforeTowerBlueprint;
            paused = true;
            tickAccumulator = 0f;
            if (hud != null)
            {
                hud.SetVisible(false);
            }
            wavePreviewPresenter?.Hide();

            if (towerActionView != null)
            {
                towerActionView.Hide();
            }

            RefreshTowerSelectionIndicators();
            ApplyWorldTimeScale();
        }

        private void ExitTowerBlueprint()
        {
            if (!towerBlueprintOpen)
            {
                if (hud != null)
                {
                    hud.SetVisible(true);
                }

                RefreshHud();

                return;
            }

            towerBlueprintOpen = false;
            if (snapshot != null &&
                snapshot.Phase == RunPhase.Combat)
            {
                paused = resumeCombatAfterTowerBlueprint
                    ? false
                    : pausedBeforeTowerBlueprint;
            }
            else
            {
                paused = pausedBeforeTowerBlueprint;
            }

            pausedBeforeTowerBlueprint = false;
            resumeCombatAfterTowerBlueprint = false;
            if (hud != null)
            {
                hud.SetVisible(true);
            }

            RestoreWorldInteractionAfterBlueprint();
            RefreshHud();
            ApplyWorldTimeScale();
        }

        private void RestoreWorldInteractionAfterBlueprint()
        {
            EventSystem eventSystem = EventSystem.current;
            GameObject focusedObject =
                eventSystem == null
                    ? null
                    : eventSystem.currentSelectedGameObject;
            if (focusedObject != null &&
                loadoutView != null &&
                focusedObject.transform.IsChildOf(
                    loadoutView.transform))
            {
                eventSystem.SetSelectedGameObject(null);
            }

            foreach (TowerPresentationHandle presentation in
                     towerPresentations.Values)
            {
                TowerSelectionView selection =
                    presentation.Selection;
                if (selection != null)
                {
                    selection.ResetPointerClickSequence();
                }
            }

            RefreshTowerSelectionIndicators();
            RefreshTowerActionView();
        }

        private void CompleteLoadoutCommand(
            CommandResult result,
            string successStatus,
            object[] successArguments)
        {
            if (!result.Accepted)
            {
                hud.SetStatus(
                    result.Error ==
                        CommandError.CombatLoadoutLocked
                        ? "status.loadout_locked"
                        : result.Error ==
                            CommandError.InsufficientGold
                            ? "status.insufficient_gold"
                        : result.Error ==
                            CommandError.SlotOccupied
                            ? "status.slot_occupied"
                            : "status.loadout_failed_format",
                    result.Error.ToString());
                RefreshTowerLoadout();
                return;
            }

            snapshot = simulation.GetSnapshot();
            ProcessPresentationEvents();
            hud.SetStatus(
                successStatus,
                successArguments ??
                Array.Empty<object>());
            ReconcilePresentation();
        }

        private bool IsLoadoutEditable()
        {
            return simulation != null &&
                simulation.CanEditLoadout;
        }

        private void HandleRewardChoice(int offerIndex)
        {
            if (snapshot == null)
            {
                return;
            }

            string selectedCardName = string.Empty;
            CommandResult result;
            if (snapshot.Phase == RunPhase.Draft)
            {
                if (offerIndex < 0 ||
                    offerIndex >= snapshot.DraftOffers.Length)
                {
                    return;
                }

                selectedCardName = textCatalog.ResolveDisplayName(
                    content.GetCard(
                        snapshot.DraftOffers[offerIndex]));
                result = simulation.Submit(
                    GameCommand.SelectDraft(offerIndex));
            }
            else if (snapshot.Phase == RunPhase.CardPackChoice)
            {
                if (offerIndex < 0 ||
                    offerIndex >= snapshot.CardPackOffers.Length)
                {
                    return;
                }

                selectedCardName = textCatalog.ResolveDisplayName(
                    content.GetCard(
                        snapshot.CardPackOffers[offerIndex]));
                result = simulation.Submit(
                    GameCommand.SelectCardPack(offerIndex));
            }
            else
            {
                return;
            }

            if (!result.Accepted)
            {
                hud.SetStatus(
                    "status.tower_place_failed_format",
                    result.Error.ToString());
                return;
            }

            snapshot = simulation.GetSnapshot();
            if (snapshot.Phase == RunPhase.CardPackLoadout)
            {
                CommandResult resumeResult = simulation.Submit(
                    GameCommand.ResumeCardPackCombat());
                if (!resumeResult.Accepted)
                {
                    hud.SetStatus(
                        "status.tower_place_failed_format",
                        resumeResult.Error.ToString());
                    RefreshHud();
                    return;
                }
            }

            snapshot = simulation.GetSnapshot();
            ProcessPresentationEvents();
            hud.SetStatus(
                "status.reward_selected_format",
                selectedCardName);
            ReconcilePresentation();
        }

        private TowerBuildSiteView FindBuildSite(
            int buildPointIndex)
        {
            if (stageMap == null)
            {
                return null;
            }

            for (int i = 0;
                 i < stageMap.BuildSiteCount;
                 i++)
            {
                TowerBuildSiteView site =
                    stageMap.GetBuildSite(i);
                if (site != null &&
                    site.BuildPointIndex == buildPointIndex)
                {
                    return site;
                }
            }

            return null;
        }

        private TowerSnapshot FindTowerAt(int buildPointIndex)
        {
            for (int i = 0; i < snapshot.Towers.Length; i++)
            {
                if (snapshot.Towers[i].BuildPointIndex ==
                    buildPointIndex)
                {
                    return snapshot.Towers[i];
                }
            }

            return new TowerSnapshot(
                -1,
                string.Empty,
                -1,
                SimPosition.Origin,
                Array.Empty<int>(),
                1,
                SubjectType.Projectile);
        }

        private TowerSnapshot FindTowerById(int towerId)
        {
            if (snapshot != null)
            {
                for (int i = 0;
                     i < snapshot.Towers.Length;
                     i++)
                {
                    if (snapshot.Towers[i].Id == towerId)
                    {
                        return snapshot.Towers[i];
                    }
                }
            }

            return new TowerSnapshot(
                -1,
                string.Empty,
                -1,
                SimPosition.Origin,
                Array.Empty<int>(),
                1,
                SubjectType.Projectile);
        }

        private CardInstanceSnapshot FindCardById(
            int cardInstanceId)
        {
            if (snapshot != null)
            {
                for (int i = 0;
                     i < snapshot.Cards.Length;
                     i++)
                {
                    if (snapshot.Cards[i].Id ==
                        cardInstanceId)
                    {
                        return snapshot.Cards[i];
                    }
                }
            }

            return new CardInstanceSnapshot(
                -1,
                CardId.Invalid,
                1,
                false,
                -1,
                -1);
        }

        private void ProcessPresentationEvents()
        {
            SimulationEventBuffer events =
                simulation.ReadPresentationEvents();
            if (events.Count == 0)
            {
                return;
            }

            for (int i = 0; i < events.Count; i++)
            {
                SimulationPresentationEvent item = events[i];
                RuleforgeAudioService.PlayPresentationEvent(
                    item.Type);
                if (item.Type == PresentationEventType.EnemyDied &&
                    enemyViews.TryGetValue(
                        item.SubjectId,
                        out StageOneEnemyView enemy))
                {
                    if (TryConsumeEffectPresentationBudget())
                    {
                        cardEffectVfx?.PlayFlagSet(
                            item.EffectVisualFlags,
                            enemy);
                    }
                    enemyInspectionController?.NotifyEnemyDied(
                        item.SubjectId);
                    enemy.BeginDeath();
                }
                else if (
                    item.Type ==
                        PresentationEventType.ProjectileHit &&
                    projectileViews.TryGetValue(
                        item.SourceId,
                        out StageOneProjectileView projectile) &&
                    enemyViews.TryGetValue(
                        item.SubjectId,
                        out StageOneEnemyView hitEnemy))
                {
                    projectile.PrepareImpact(hitEnemy);
                    if (TryConsumeEffectPresentationBudget())
                    {
                        cardEffectVfx?.PlayFlagSet(
                            item.EffectVisualFlags,
                            hitEnemy);
                    }
                }
                else if (
                    item.Type ==
                        PresentationEventType.AreaEffectTriggered &&
                    TryConsumeEffectPresentationBudget())
                {
                    PlayAreaEffectIndicator(item);
                }
                else if (
                    item.Type ==
                        PresentationEventType.TowerAttackStarted)
                {
                    pendingTowerAttackTargets[item.SourceId] =
                        item.SubjectId;
                }
                else if (
                    item.Type ==
                        PresentationEventType.ProjectileSpawned)
                {
                    if (string.IsNullOrEmpty(item.ContentId))
                    {
                        if (TryGetTowerProjectileLaunchOrigin(
                                item.SourceId,
                                out Vector3 launchOrigin))
                        {
                            pendingProjectileLaunchOrigins[
                                item.SubjectId] = launchOrigin;
                        }
                    }
                    else if (
                        string.Equals(
                            item.ContentId,
                            "split",
                            StringComparison.Ordinal))
                    {
                        if (pendingProjectileLaunchOrigins.TryGetValue(
                                item.SourceId,
                                out Vector3 inheritedOrigin))
                        {
                            pendingProjectileLaunchOrigins[
                                item.SubjectId] =
                                inheritedOrigin;
                        }
                        else if (projectileViews.TryGetValue(
                                     item.SourceId,
                                     out StageOneProjectileView
                                         sourceProjectile) &&
                                 sourceProjectile != null)
                        {
                            pendingProjectileLaunchOrigins[
                                item.SubjectId] =
                                sourceProjectile.transform.position;
                        }
                    }
                }
            }
        }

        private bool TryConsumeEffectPresentationBudget()
        {
            if (content == null ||
                effectsPresentedThisFrame >=
                content.Safety.MaxEffectsPerFrame)
            {
                return false;
            }

            effectsPresentedThisFrame++;
            return true;
        }

        private void PlayAreaEffectIndicator(
            in SimulationPresentationEvent item)
        {
            if (cardEffectVfx == null ||
                item.Value <= 0 ||
                string.IsNullOrWhiteSpace(item.ContentId))
            {
                return;
            }

            // Enemy-rooted areas follow the visible body center. Simulation
            // positions are bottom-center path anchors, so blindly preferring
            // EffectPosition draws the range ring below tall sprites.
            Vector3 position;
            if (!TryResolveAreaEffectPosition(
                    item.SubjectId,
                    out position) &&
                !TryResolveAreaEffectPosition(
                    item.SourceId,
                    out position))
            {
                if (!item.HasEffectPosition)
                {
                    return;
                }

                position = ToWorld(
                    item.EffectPosition,
                    -0.06f);
            }

            cardEffectVfx.PlayAreaIndicator(
                item.ContentId,
                position,
                item.Value / 1000f);
        }

        private bool TryResolveAreaEffectPosition(
            int entityId,
            out Vector3 position)
        {
            if (enemyViews.TryGetValue(
                    entityId,
                    out StageOneEnemyView enemy) &&
                enemy != null)
            {
                // Recoil is presentation-only. Keep the ring locked to the
                // logical sprite-body center so its origin is stable while
                // the enemy flashes or recoils from the same impact.
                position = enemy.LogicalImpactCenter;
                return true;
            }

            if (projectileViews.TryGetValue(
                    entityId,
                    out StageOneProjectileView projectile) &&
                projectile != null)
            {
                position = projectile.transform.position;
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        private void ReconcilePresentation()
        {
            if (snapshot == null)
            {
                return;
            }

            ReconcileBuildSites();
            ReconcileTowers();
            ReconcileEnemies();
            PresentPendingTowerAttacks();
            ReconcileProjectiles();
            ReconcileHazards();
            AimTowers();
            RefreshHud();
        }

        private void ReconcileHazards()
        {
            if (fireTrailRenderer == null)
            {
                return;
            }

            fireTrailRenderer.ApplySnapshot(
                snapshot.Hazards,
                snapshot.Tick);
        }

        private void ReconcileBuildSites()
        {
            for (int siteIndex = 0;
                 siteIndex < stageMap.BuildSiteCount;
                 siteIndex++)
            {
                TowerBuildSiteView site =
                    stageMap.GetBuildSite(siteIndex);
                bool unlocked = false;
                for (int spotIndex = 0;
                     spotIndex < snapshot.BuildSpots.Length;
                     spotIndex++)
                {
                    if (snapshot.BuildSpots[spotIndex].Index ==
                        site.BuildPointIndex)
                    {
                        unlocked =
                            snapshot.BuildSpots[spotIndex].Unlocked;
                        break;
                    }
                }

                bool occupied = false;
                for (int towerIndex = 0;
                     towerIndex < snapshot.Towers.Length;
                     towerIndex++)
                {
                    if (snapshot.Towers[towerIndex].BuildPointIndex ==
                        site.BuildPointIndex)
                    {
                        occupied = true;
                        break;
                    }
                }

                site.ApplySimulationState(unlocked, occupied);
            }

            if (towerBuildPickerView != null &&
                towerBuildPickerView.IsVisible &&
                (towerBuildPickerView.Target == null ||
                 !towerBuildPickerView.Target.CanBuild ||
                 (snapshot.Phase !=
                    RunPhase.AwaitingStartingTower &&
                  snapshot.Phase != RunPhase.Planning &&
                  snapshot.Phase != RunPhase.Combat)))
            {
                HideTowerBuildPicker();
            }
        }

        private void ReconcileTowers()
        {
            for (int i = 0; i < snapshot.Towers.Length; i++)
            {
                TowerSnapshot tower = snapshot.Towers[i];
                if (towerPresentations.TryGetValue(
                        tower.Id,
                        out TowerPresentationHandle existing) &&
                    existing.Matches(tower))
                {
                    continue;
                }

                RemoveTowerPresentation(tower.Id);
                GameObject instance =
                    StageOneTowerPresentationFactory.Create(
                        presentationCatalog,
                        tower.DefinitionId,
                        tower.Level,
                        towerRoot);

                instance.name =
                    "Tower " + tower.Id + " (" +
                    tower.DefinitionId + ")";
                instance.transform.position =
                    ToWorld(tower.Position, 0f);

                ArcherTowerView view =
                    instance.GetComponent<ArcherTowerView>();
                if (view != null)
                {
                    view.EnableVisibleBaseAlignment();
                    // A freshly presented tower must show its authored
                    // construction sequence before the crew returns to Idle.
                    // PlayUpgrade already transitions to Idle and performs the
                    // archer landing once its final frame completes.
                    if (!view.PlayUpgrade())
                    {
                        view.RestartIdle();
                    }
                }

                TowerSelectionView selection =
                    instance.GetComponent<TowerSelectionView>();
                if (selection == null)
                {
                    selection =
                        instance.AddComponent<TowerSelectionView>();
                }

                selection.Configure(
                    tower.Id,
                    ResolveTowerAttackRangeWorld(tower));
                selection.SetSelected(
                    tower.Id == selectedTowerId);
                selection.Clicked += HandleTowerClicked;
                selection.DoubleClicked +=
                    HandleTowerDoubleClicked;
                towerPresentations.Add(
                    tower.Id,
                    new TowerPresentationHandle(
                        tower.DefinitionId,
                        tower.Level,
                        instance,
                        view,
                        selection));
            }

            RefreshTowerSelectionIndicators();
        }

        private void RemoveTowerPresentation(int towerId)
        {
            if (!towerPresentations.TryGetValue(
                    towerId,
                    out TowerPresentationHandle presentation))
            {
                return;
            }

            towerPresentations.Remove(towerId);
            TowerSelectionView selection =
                presentation.Selection;
            if (selection != null)
            {
                selection.Clicked -= HandleTowerClicked;
                selection.DoubleClicked -=
                    HandleTowerDoubleClicked;
            }

            if (presentation.Root != null)
            {
                Destroy(presentation.Root);
            }
        }

        private void ReconcileEnemies()
        {
            foreach (StageOneEnemyView view in enemyViews.Values)
            {
                view.BeginSnapshotFrame();
            }

            for (int i = 0; i < snapshot.Enemies.Length; i++)
            {
                EnemySnapshot enemy = snapshot.Enemies[i];
                if (!enemyViews.TryGetValue(
                        enemy.Id,
                        out StageOneEnemyView view))
                {
                    view = GetEnemyView(
                        enemy.DefinitionId,
                        enemy.Id,
                        enemy.EliteTraitIds);
                    enemyViews.Add(enemy.Id, view);
                    enemyInspectionController?.Register(view);
                }

                if (cardEffectVfx != null)
                {
                    cardEffectVfx.PrepareEnemySnapshot(view);
                }
                view.ApplySnapshot(enemy);
                if (cardEffectVfx != null)
                {
                    cardEffectVfx.ApplyEnemySnapshot(
                        view,
                        enemy);
                }
            }

            enemyInspectionController?.ApplySnapshot(snapshot);
            removalIds.Clear();
            foreach (KeyValuePair<int, StageOneEnemyView> pair in
                     enemyViews)
            {
                StageOneEnemyView view = pair.Value;
                if (!view.SeenThisFrame &&
                    !view.IsDeathPresentationActive)
                {
                    removalIds.Add(pair.Key);
                }
            }

            for (int i = 0; i < removalIds.Count; i++)
            {
                int id = removalIds[i];
                StageOneEnemyView view = enemyViews[id];
                enemyViews.Remove(id);
                ReturnEnemyView(view);
            }
        }

        private StageOneEnemyView GetEnemyView(
            string definitionId,
            int entityId,
            string[] eliteTraitStableIds)
        {
            if (!enemyPools.TryGetValue(
                    definitionId,
                    out Queue<StageOneEnemyView> pool))
            {
                pool = new Queue<StageOneEnemyView>();
                enemyPools.Add(definitionId, pool);
            }

            StageOneEnemyView view = null;
            while (pool.Count > 0 && view == null)
            {
                view = pool.Dequeue();
            }

            float scale = 1f;
            if (view == null)
            {
                if (!presentationCatalog.TryGetEnemy(
                        definitionId,
                        out GameObject prefab,
                        out scale))
                {
                    throw new InvalidOperationException(
                        "No battle-stage enemy prefab for '" +
                        definitionId + "'.");
                }

                GameObject instance =
                    Instantiate(prefab, enemyRoot);
                view =
                    instance.GetComponent<StageOneEnemyView>();
                if (view == null)
                {
                    view =
                        instance.AddComponent<StageOneEnemyView>();
                }
            }
            else
            {
                presentationCatalog.TryGetEnemy(
                    definitionId,
                    out _,
                    out scale);
            }

            view.name =
                "Enemy " + entityId + " (" + definitionId + ")";
            CompiledEliteTraitDefinition eliteTrait = null;
            if (eliteTraitStableIds != null &&
                eliteTraitStableIds.Length > 0 &&
                content.TryGetEliteTraitId(
                    eliteTraitStableIds[0],
                    out EliteTraitId eliteTraitId))
            {
                eliteTrait = content.GetEliteTrait(eliteTraitId);
                view.name =
                    "Enemy " + entityId + " (" +
                    eliteTrait.StableId + " " + definitionId + ")";
            }
            view.Configure(
                entityId,
                definitionId,
                scale,
                eliteTrait,
                presentationCatalog.UiFont);
            return view;
        }

        private void ReturnEnemyView(StageOneEnemyView view)
        {
            string definitionId = view.DefinitionId;
            enemyInspectionController?.Unregister(view);
            view.ReturnToPool();
            if (!enemyPools.TryGetValue(
                    definitionId,
                    out Queue<StageOneEnemyView> pool))
            {
                pool = new Queue<StageOneEnemyView>();
                enemyPools.Add(definitionId, pool);
            }

            pool.Enqueue(view);
        }

        private void ReconcileProjectiles()
        {
            removalIds.Clear();
            foreach (int id in projectileViews.Keys)
            {
                removalIds.Add(id);
            }

            for (int i = 0;
                 i < snapshot.Projectiles.Length;
                 i++)
            {
                ProjectileSnapshot projectile =
                    snapshot.Projectiles[i];
                enemyViews.TryGetValue(
                    projectile.TargetId,
                    out StageOneEnemyView aimTarget);
                if (!projectileViews.TryGetValue(
                        projectile.Id,
                        out StageOneProjectileView view))
                {
                    view = GetProjectileView();
                    projectileViews.Add(projectile.Id, view);
                    if (cardEffectVfx != null)
                    {
                        cardEffectVfx.PrepareProjectileSnapshot(
                            view);
                    }
                    if (pendingProjectileLaunchOrigins.TryGetValue(
                            projectile.Id,
                            out Vector3 launchOrigin))
                    {
                        view.ApplySnapshot(
                            projectile,
                            launchOrigin,
                            aimTarget);
                        pendingProjectileLaunchOrigins.Remove(
                            projectile.Id);
                    }
                    else
                    {
                        view.ApplySnapshot(
                            projectile,
                            null,
                            aimTarget);
                    }
                }
                else
                {
                    if (cardEffectVfx != null)
                    {
                        cardEffectVfx.PrepareProjectileSnapshot(
                            view);
                    }
                    view.ApplySnapshot(
                        projectile,
                        null,
                        aimTarget);
                }

                if (cardEffectVfx != null)
                {
                    cardEffectVfx.ApplyProjectileSnapshot(
                        view,
                        projectile);
                }
                removalIds.Remove(projectile.Id);
            }

            pendingProjectileLaunchOrigins.Clear();

            for (int i = 0; i < removalIds.Count; i++)
            {
                int id = removalIds[i];
                StageOneProjectileView view = projectileViews[id];
                projectileViews.Remove(id);
                view.ReturnToPool();
                projectilePool.Enqueue(view);
            }
        }

        private bool TryGetTowerProjectileLaunchOrigin(
            int towerId,
            out Vector3 origin)
        {
            if (!towerPresentations.TryGetValue(
                    towerId,
                    out TowerPresentationHandle presentation) ||
                presentation.Root == null)
            {
                origin = Vector3.zero;
                return false;
            }

            if (presentation.Archer != null)
            {
                origin =
                    presentation.Archer
                        .GetNextProjectileLaunchOrigin();
                return true;
            }

            SpriteRenderer[] renderers =
                presentation.Root.GetComponentsInChildren<
                    SpriteRenderer>(true);
            bool found = false;
            Bounds bounds = default(Bounds);
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null ||
                    !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy ||
                    renderer.sprite == null)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            origin = found
                ? bounds.center
                : presentation.Root.transform.position;
            return true;
        }

        private StageOneProjectileView GetProjectileView()
        {
            StageOneProjectileView view =
                projectilePool.Count > 0
                    ? projectilePool.Dequeue()
                    : null;
            if (view == null)
            {
                var host = new GameObject(
                    "Projectile",
                    typeof(SpriteRenderer),
                    typeof(StageOneProjectileView));
                host.transform.SetParent(projectileRoot, false);
                view = host.GetComponent<StageOneProjectileView>();
                view.Configure(presentationCatalog);
            }

            view.gameObject.SetActive(true);
            return view;
        }

        private void AimTowers()
        {
            if (snapshot.Enemies.Length == 0)
            {
                return;
            }

            foreach (
                KeyValuePair<int, TowerPresentationHandle> pair in
                towerPresentations)
            {
                TowerPresentationHandle presentation =
                    pair.Value;
                if (presentation.Archer == null ||
                    presentation.Root == null)
                {
                    continue;
                }

                Vector3 origin =
                    presentation.Root.transform.position;
                AimTowerAtDistinctEnemies(
                    pair.Key,
                    presentation.Archer,
                    origin);
            }
        }

        private int AimTowerAtDistinctEnemies(
            int towerId,
            ArcherTowerView tower,
            Vector3 origin)
        {
            if (tower == null || snapshot == null)
            {
                return 0;
            }

            for (int i = 0; i < towerAimEnemyIds.Length; i++)
            {
                towerAimEnemyIds[i] = -1;
            }

            int limit = Mathf.Min(
                tower.ArcherCount,
                towerAimPositions.Length);
            float range = ResolveTowerAttackRangeWorld(
                FindTowerById(towerId));
            float rangeSquared = range * range;
            int selectedCount = 0;
            while (selectedCount < limit)
            {
                int bestIndex = -1;
                for (int enemyIndex = 0;
                     enemyIndex < snapshot.Enemies.Length;
                     enemyIndex++)
                {
                    EnemySnapshot candidate =
                        snapshot.Enemies[enemyIndex];
                    Vector3 candidatePosition =
                        ToWorld(candidate.Position, 0f);
                    if (!candidate.Alive ||
                        (candidatePosition - origin)
                            .sqrMagnitude > rangeSquared ||
                        ContainsAimEnemyId(
                            candidate.Id,
                            selectedCount))
                    {
                        continue;
                    }

                    if (bestIndex < 0 ||
                        CompareAimPriority(
                            origin,
                            candidate,
                            snapshot.Enemies[bestIndex]) < 0)
                    {
                        bestIndex = enemyIndex;
                    }
                }

                if (bestIndex < 0)
                {
                    break;
                }

                EnemySnapshot selected =
                    snapshot.Enemies[bestIndex];
                towerAimEnemyIds[selectedCount] =
                    selected.Id;
                towerAimPositions[selectedCount] =
                    ToWorld(selected.Position, 0f);
                selectedCount++;
            }

            tower.AimAtDistinctTargets(
                towerAimPositions,
                selectedCount);
            return selectedCount;
        }

        private bool ContainsAimEnemyId(
            int enemyId,
            int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (towerAimEnemyIds[i] == enemyId)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareAimPriority(
            Vector3 origin,
            in EnemySnapshot left,
            in EnemySnapshot right)
        {
            bool leftMarked =
                left.Statuses != null &&
                Array.IndexOf(
                    left.Statuses,
                    StatusType.Mark) >= 0;
            bool rightMarked =
                right.Statuses != null &&
                Array.IndexOf(
                    right.Statuses,
                    StatusType.Mark) >= 0;
            if (leftMarked != rightMarked)
            {
                return leftMarked ? -1 : 1;
            }

            float leftDistance =
                (ToWorld(left.Position, 0f) - origin)
                .sqrMagnitude;
            float rightDistance =
                (ToWorld(right.Position, 0f) - origin)
                .sqrMagnitude;
            int distanceComparison =
                leftDistance.CompareTo(rightDistance);
            if (distanceComparison != 0)
            {
                return distanceComparison;
            }

            int progressComparison =
                right.PathProgressMilli.CompareTo(
                    left.PathProgressMilli);
            return progressComparison != 0
                ? progressComparison
                : left.Id.CompareTo(right.Id);
        }

        /// <summary>
        /// Starts the authored draw animation from the deterministic
        /// TowerAttackStarted event. The simulation waits the configured
        /// windup before emitting ProjectileSpawned, so the actual projectile
        /// and the final release pose occur together at every combat speed.
        /// </summary>
        private void PresentPendingTowerAttacks()
        {
            if (pendingTowerAttackTargets.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<int, int> attack in
                     pendingTowerAttackTargets)
            {
                if (!towerPresentations.TryGetValue(
                        attack.Key,
                        out TowerPresentationHandle presentation) ||
                    presentation.Archer == null)
                {
                    continue;
                }

                ArcherTowerView tower =
                    presentation.Archer;
                if (enemyViews.TryGetValue(
                        attack.Value,
                        out StageOneEnemyView target) &&
                    target != null &&
                    presentation.Root != null)
                {
                    AimTowerAtDistinctEnemies(
                        attack.Key,
                        tower,
                        presentation.Root.transform.position);
                }

                tower.PlayVolley();
            }

            pendingTowerAttackTargets.Clear();
        }

        private void RefreshHud()
        {
            if (hud == null || snapshot == null)
            {
                return;
            }

            hud.SetHud(
                snapshot.WaveIndex < 0
                    ? 0
                    : snapshot.WaveIndex + 1,
                content.WaveCount,
                snapshot.BaseHealth,
                snapshot.Gold,
                PhaseToId(snapshot.Phase),
                snapshot.StageNumber);
            hud.SetSpeed(speedMultiplier);
            RefreshCombatDetails();

            switch (snapshot.Phase)
            {
                case RunPhase.Planning:
                    hud.SetPlayState(StageOnePlayState.Ready);
                    break;
                case RunPhase.Combat:
                    hud.SetPlayState(
                        paused
                            ? StageOnePlayState.Paused
                            : StageOnePlayState.Playing);
                    break;
                case RunPhase.Victory:
                    hud.SetPlayState(
                        StageOnePlayState.Continue);
                    break;
                default:
                    hud.SetPlayState(StageOnePlayState.Disabled);
                    break;
            }

            RefreshEquippedCardDisplay();
            RefreshRewardDisplay();
            RefreshTowerLoadout();
            RefreshTowerActionView();
            RefreshPhaseStatus();
            RefreshWavePreview();
        }

        private void RefreshWavePreview()
        {
            wavePreviewPresenter?.Refresh(
                simulation,
                snapshot,
                towerBlueprintOpen);
        }

        private void RefreshCombatDetails()
        {
            if (hud == null || simulation == null || snapshot == null)
            {
                return;
            }

            if (snapshot.Phase == RunPhase.Planning)
            {
                int nextWaveIndex = snapshot.WaveIndex + 1;
                hud.SetCombatDetails(
                    nextWaveIndex >= 0 &&
                    nextWaveIndex < content.WaveCount
                        ? FormatWaveForecast(
                            simulation.GetWaveForecast(
                                nextWaveIndex))
                        : string.Empty);
                return;
            }

            CombatTelemetrySnapshot telemetry =
                simulation.GetCombatTelemetrySnapshot();
            if (snapshot.Phase == RunPhase.Combat)
            {
                hud.SetCombatDetails(textCatalog.Format(
                    "combat_density.live_format",
                    telemetry.CurrentKillStreak,
                    telemetry.LargestChainKillCount,
                    telemetry.ExplosionTriggerCount,
                    telemetry.ShockChainHitCount,
                    telemetry.RicochetCount,
                    telemetry.StatusSpreadCount,
                    telemetry.RecentGold,
                    telemetry.CardBountyGold,
                    telemetry.CardBountyRemaining));
                return;
            }

            var summary = new StringBuilder(160);
            summary.Append(textCatalog.Format(
                "combat_density.wave_summary_format",
                telemetry.WaveGold,
                telemetry.HighestKillStreak));
            int cardCount = Mathf.Min(2, telemetry.CardStats.Length);
            for (int i = 0; i < cardCount; i++)
            {
                CardCombatStatSnapshot stat = telemetry.CardStats[i];
                string cardName = stat.CardId;
                if (content.TryGetCardId(
                        stat.CardId,
                        out CardId cardId))
                {
                    cardName = textCatalog.ResolveDisplayName(
                        content.GetCard(cardId));
                }
                summary.Append(textCatalog.Format(
                    "combat_density.card_summary_format",
                    cardName,
                    stat.DamageMilli / 1000L,
                    stat.ActivationCount));
            }
            hud.SetCombatDetails(summary.ToString());
        }

        private string FormatWaveForecast(
            WaveForecastSnapshot forecast)
        {
            var result = new StringBuilder(192);
            result.Append(textCatalog.Format(
                "combat_density.forecast_format",
                forecast.WaveIndex + 1,
                GetWaveArchetypeName(forecast.Archetype),
                forecast.TotalCount,
                forecast.NormalCount,
                forecast.EliteCount,
                forecast.BossCount));
            for (int i = 0; i < forecast.Spawns.Length; i++)
            {
                WaveForecastSpawn spawn = forecast.Spawns[i];
                string enemyName = spawn.EnemyId;
                if (content.TryGetEnemyId(
                        spawn.EnemyId,
                        out EnemyDefinitionId enemyId))
                {
                    enemyName = textCatalog.Get(
                        content.GetEnemy(enemyId).DisplayNameKey);
                }
                result.Append(textCatalog.Format(
                    "combat_density.forecast_spawn_format",
                    enemyName,
                    spawn.Count));
            }
            if (forecast.BossActiveSummonLimit > 0)
            {
                result.Append(textCatalog.Format(
                    "combat_density.forecast_summon_format",
                    forecast.BossActiveSummonLimit));
            }
            return result.ToString();
        }

        private string GetWaveArchetypeName(
            WaveArchetype archetype)
        {
            switch (archetype)
            {
                case WaveArchetype.Swarm:
                    return textCatalog.Get(
                        "combat_density.archetype.swarm");
                case WaveArchetype.EliteMix:
                    return textCatalog.Get(
                        "combat_density.archetype.elite_mix");
                case WaveArchetype.Rush:
                    return textCatalog.Get(
                        "combat_density.archetype.rush");
                case WaveArchetype.HeavyEscort:
                    return textCatalog.Get(
                        "combat_density.archetype.heavy_escort");
                case WaveArchetype.Boss:
                    return textCatalog.Get(
                        "combat_density.archetype.boss");
                case WaveArchetype.Containment:
                    return textCatalog.Get(
                        "combat_density.archetype.containment");
                default:
                    return textCatalog.Get(
                        "combat_density.archetype.standard");
            }
        }

        private void RefreshEquippedCardDisplay()
        {
            cardDisplays.Clear();
            if (snapshot.Towers.Length == 0)
            {
                hud.SetEquippedCards(cardDisplays);
                return;
            }

            TowerSnapshot tower =
                FindTowerById(selectedTowerId);
            if (tower.Id < 0)
            {
                tower = snapshot.Towers[0];
            }
            var addedInstances = new HashSet<int>();
            for (int slot = 0;
                 slot < tower.CardInstanceIds.Length;
                 slot++)
            {
                int cardInstanceId = tower.CardInstanceIds[slot];
                if (cardInstanceId < 0 ||
                    !addedInstances.Add(cardInstanceId))
                {
                    continue;
                }

                for (int cardIndex = 0;
                     cardIndex < snapshot.Cards.Length;
                     cardIndex++)
                {
                    CardInstanceSnapshot card =
                        snapshot.Cards[cardIndex];
                    if (card.Id != cardInstanceId)
                    {
                        continue;
                    }

                    CompiledCardDefinition definition =
                        content.GetCard(card.DefinitionId);
                    cardDisplays.Add(
                        textCatalog.GetCardDisplay(
                            definition,
                            GetTowerSlotSubjectType(
                                tower,
                                slot)));
                    break;
                }
            }

            hud.SetEquippedCards(cardDisplays);
        }

        private void RefreshTowerLoadout()
        {
            if (loadoutView == null)
            {
                return;
            }

            TowerSnapshot tower =
                FindTowerById(selectedTowerId);
            if (tower.Id < 0 || !towerBlueprintOpen)
            {
                loadoutView.SetTowerPreview(null);
                loadoutView.Hide();
                return;
            }

            int unlockedSlotCount =
                ResolveTowerUnlockedSlotCount(tower);
            selectedTowerSlot = Mathf.Clamp(
                selectedTowerSlot,
                0,
                Mathf.Max(0, unlockedSlotCount - 1));
            SubjectType selectedSubjectType =
                GetTowerSlotSubjectType(
                    tower,
                    selectedTowerSlot);

            loadoutCards.Clear();
            for (int i = 0; i < snapshot.Cards.Length; i++)
            {
                CardInstanceSnapshot card =
                    snapshot.Cards[i];
                CompiledCardDefinition definition =
                    content.GetCard(card.DefinitionId);
                SubjectType cardSubjectType =
                    card.Equipped &&
                    card.TowerId == tower.Id
                        ? GetTowerSlotSubjectType(
                            tower,
                            card.Slot)
                        : selectedSubjectType;
                loadoutCards.Add(
                    new StageOneLoadoutCard(
                        card.Id,
                        textCatalog.GetCardDisplay(
                            definition,
                            cardSubjectType),
                        card.Equipped,
                        card.Equipped &&
                        card.TowerId == tower.Id,
                        card.Equipped
                            ? card.TowerId
                            : -1));
            }

            TowerUpgradeQuote upgradeQuote =
                simulation.GetTowerUpgradeQuote(tower.Id);
            RefreshLoadoutMapOverview();
            loadoutView.SetMapOverview(
                loadoutMapPathPoints,
                loadoutMapSites);
            loadoutView.Show(
                textCatalog.GetTowerName(
                    tower.DefinitionId),
                tower.Level,
                tower.CardSubjectTypes,
                unlockedSlotCount,
                tower.CardInstanceIds,
                loadoutCards,
                selectedTowerSlot,
                IsLoadoutEditable(),
                upgradeQuote);
            loadoutView.SetTowerPreview(
                towerPresentations.TryGetValue(
                    tower.Id,
                    out TowerPresentationHandle presentation) &&
                presentation.Root != null
                    ? presentation.Root.transform
                    : null);
        }

        private void RefreshLoadoutMapOverview()
        {
            loadoutMapPathPoints.Clear();
            if (stageMap != null && stageMap.Path != null)
            {
                for (int index = 0;
                     index < stageMap.Path.WaypointCount;
                     index++)
                {
                    loadoutMapPathPoints.Add(
                        stageMap.Path.GetWorldWaypoint(index));
                }
            }

            loadoutMapSites.Clear();
            if (stageMap == null)
            {
                return;
            }

            for (int siteIndex = 0;
                 siteIndex < stageMap.BuildSiteCount;
                 siteIndex++)
            {
                TowerBuildSiteView buildSite =
                    stageMap.GetBuildSite(siteIndex);
                int towerId = -1;
                for (int towerIndex = 0;
                     towerIndex < snapshot.Towers.Length;
                     towerIndex++)
                {
                    TowerSnapshot candidate =
                        snapshot.Towers[towerIndex];
                    if (candidate.BuildPointIndex ==
                        buildSite.BuildPointIndex)
                    {
                        towerId = candidate.Id;
                        break;
                    }
                }

                loadoutMapSites.Add(
                    new StageOneLoadoutMapSite(
                        towerId,
                        buildSite.transform.position,
                        towerId >= 0));
            }
        }

        private void RefreshTowerActionView()
        {
            if (towerActionView == null)
            {
                return;
            }

            TowerSnapshot tower =
                FindTowerById(selectedTowerId);
            if (towerBlueprintOpen ||
                tower.Id < 0 ||
                !towerPresentations.TryGetValue(
                    tower.Id,
                    out TowerPresentationHandle presentation) ||
                presentation.Selection == null)
            {
                towerActionView.Hide();
                return;
            }

            TowerUpgradeQuote upgradeQuote =
                simulation.GetTowerUpgradeQuote(tower.Id);
            towerActionView.Show(
                presentation.Selection,
                upgradeQuote);
        }

        private int ResolveTowerUnlockedSlotCount(
            in TowerSnapshot tower)
        {
            if (tower.Id < 0)
            {
                return 0;
            }

            int unlocked;
            if (simulation != null)
            {
                unlocked =
                    simulation.GetTowerUnlockedSlotCount(
                        tower.Id);
            }
            else if (content != null &&
                     content.TryGetTowerId(
                         tower.DefinitionId,
                         out TowerDefinitionId definitionId) &&
                     content.GetTower(definitionId)
                         .TryGetLevel(
                             tower.Level,
                             out CompiledTowerLevelBalance level))
            {
                unlocked = level.UnlockedSlots;
            }
            else
            {
                unlocked = 0;
            }
            return Math.Min(
                tower.CardInstanceIds.Length,
                unlocked);
        }

        private float ResolveTowerAttackRangeWorld(
            in TowerSnapshot tower)
        {
            if (simulation != null && tower.Id >= 0)
            {
                return Mathf.Max(
                    0f,
                    simulation.GetTowerRangeMilli(tower.Id) /
                    1000f);
            }

            if (content == null ||
                !content.TryGetTowerId(
                    tower.DefinitionId,
                    out TowerDefinitionId definitionId))
            {
                return 0f;
            }

            return Mathf.Max(
                0f,
                content.GetTower(definitionId).RangeMilli /
                1000f);
        }

        private static SubjectType GetTowerSlotSubjectType(
            in TowerSnapshot tower,
            int slotIndex)
        {
            return tower.CardSubjectTypes != null &&
                   slotIndex >= 0 &&
                   slotIndex < tower.CardSubjectTypes.Length
                ? tower.CardSubjectTypes[slotIndex]
                : tower.SubjectType;
        }

        private void RefreshRewardDisplay()
        {
            cardDisplays.Clear();
            CardId[] offers;
            if (snapshot.Phase == RunPhase.Draft)
            {
                offers = snapshot.DraftOffers;
            }
            else if (snapshot.Phase ==
                     RunPhase.CardPackChoice)
            {
                offers = snapshot.CardPackOffers;
            }
            else
            {
                hud.HideRewardChoices();
                return;
            }

            for (int i = 0; i < offers.Length; i++)
            {
                CompiledCardDefinition definition =
                    content.GetCard(offers[i]);
                cardDisplays.Add(
                    textCatalog.GetCardDisplay(
                        definition,
                        SubjectType.Projectile));
            }

            hud.ShowRewardChoices(cardDisplays);
        }

        private void RefreshPhaseStatus()
        {
            if (lastPresentedPhase == snapshot.Phase)
            {
                return;
            }

            lastPresentedPhase = snapshot.Phase;
            switch (snapshot.Phase)
            {
                case RunPhase.AwaitingStartingTower:
                    hud.SetStatus("status.ready");
                    break;
                case RunPhase.Planning:
                    if (snapshot.Towers.Length == 0)
                    {
                        hud.SetStatus("status.ready");
                    }
                    break;
                case RunPhase.Combat:
                    hud.SetStatus("status.combat");
                    break;
                case RunPhase.Draft:
                case RunPhase.CardPackChoice:
                    hud.SetStatus("status.reward");
                    break;
                case RunPhase.Victory:
                    paused = false;
                    CampaignStageProgress.MarkSceneCompleted(
                        SceneManager.GetActiveScene().name);
                    hud.SetStatus("status.victory");
                    break;
                case RunPhase.Defeat:
                    paused = false;
                    hud.SetStatus("status.defeat");
                    break;
            }
        }

        private void ShowBuildFailure(CommandResult result)
        {
            if (result.Error == CommandError.InsufficientGold)
            {
                hud.SetStatus("status.insufficient_gold");
            }
            else
            {
                hud.SetStatus(
                    "status.tower_place_failed_format",
                    result.Error.ToString());
            }
        }

        private void CreatePresentationRoots()
        {
            towerRoot = CreateChild("Tower Views");
            enemyRoot = CreateChild("Enemy Views");
            projectileRoot = CreateChild("Projectile Views");
            effectRoot = CreateChild("Effect Views");

            Transform existing =
                effectRoot.Find("Ground Fire Trail");
            GameObject host;
            if (existing == null)
            {
                host = new GameObject(
                    "Ground Fire Trail",
                    typeof(MeshFilter),
                    typeof(MeshRenderer),
                    typeof(StageOneFireTrailRenderer));
                host.transform.SetParent(effectRoot, false);
            }
            else
            {
                host = existing.gameObject;
                fireTrailRenderer =
                    host.GetComponent<
                        StageOneFireTrailRenderer>();
                if (fireTrailRenderer == null)
                {
                    fireTrailRenderer =
                        host.AddComponent<
                            StageOneFireTrailRenderer>();
                }
            }

            if (fireTrailRenderer == null)
            {
                fireTrailRenderer =
                    host.GetComponent<
                        StageOneFireTrailRenderer>();
            }

            Transform existingCardVfx =
                effectRoot.Find("Card Effect VFX");
            if (existingCardVfx == null)
            {
                cardEffectVfx =
                    StageOneCardEffectVfxView.CreateRuntime(
                        effectRoot);
            }
            else
            {
                cardEffectVfx =
                    existingCardVfx.GetComponent<
                        StageOneCardEffectVfxView>();
                if (cardEffectVfx == null)
                {
                    cardEffectVfx =
                        existingCardVfx.gameObject.AddComponent<
                            StageOneCardEffectVfxView>();
                }
            }
        }

        private Transform CreateChild(string childName)
        {
            Transform existing = transform.Find(childName);
            if (existing != null)
            {
                return existing;
            }

            var child = new GameObject(childName);
            child.transform.SetParent(transform, false);
            return child.transform;
        }

        private void HookInput()
        {
            hud.PlayRequested += HandlePlayRequested;
            hud.SpeedSelected += HandleSpeedSelected;
            hud.RewardChoiceRequested += HandleRewardChoice;
            hud.RewardVisibilityChanged +=
                HandleRewardVisibilityChanged;
            loadoutView.SlotRequested +=
                HandleTowerSlotRequested;
            loadoutView.SlotUnequipRequested +=
                HandleTowerSlotUnequipRequested;
            loadoutView.CardRequested +=
                HandleTowerCardRequested;
            loadoutView.CardDoubleClickRequested +=
                HandleTowerCardDoubleClickRequested;
            loadoutView.CardDropped +=
                HandleTowerCardDropped;
            loadoutView.UpgradeRequested +=
                HandleTowerUpgradeRequested;
            loadoutView.SlotSubjectTypeRequested +=
                HandleTowerSlotSubjectRequested;
            loadoutView.CloseRequested +=
                HandleTowerPanelClosed;
            towerActionView.UpgradeRequested +=
                HandleTowerUpgradeRequested;
            towerActionView.CardsRequested +=
                HandleTowerCardsRequested;
            towerBuildPickerView.TowerRequested +=
                HandleTowerBuildRequested;
            towerBuildPickerView.CloseRequested +=
                HandleTowerBuildPickerClosed;
            if (enemyInspectionController != null)
            {
                enemyInspectionController.EnemySelected +=
                    HandleEnemySelected;
            }
            if (cameraController != null)
            {
                cameraController.BackgroundClicked +=
                    HandleBackgroundClicked;
            }

            for (int i = 0; i < stageMap.BuildSiteCount; i++)
            {
                stageMap.GetBuildSite(i).Clicked +=
                    HandleBuildSiteClicked;
            }
        }

        private void UnhookInput()
        {
            if (hud != null)
            {
                hud.PlayRequested -= HandlePlayRequested;
                hud.SpeedSelected -= HandleSpeedSelected;
                hud.RewardChoiceRequested -= HandleRewardChoice;
                hud.RewardVisibilityChanged -=
                    HandleRewardVisibilityChanged;
            }

            if (loadoutView != null)
            {
                loadoutView.SlotRequested -=
                    HandleTowerSlotRequested;
                loadoutView.SlotUnequipRequested -=
                    HandleTowerSlotUnequipRequested;
                loadoutView.CardRequested -=
                    HandleTowerCardRequested;
                loadoutView.CardDoubleClickRequested -=
                    HandleTowerCardDoubleClickRequested;
                loadoutView.CardDropped -=
                    HandleTowerCardDropped;
                loadoutView.UpgradeRequested -=
                    HandleTowerUpgradeRequested;
                loadoutView.SlotSubjectTypeRequested -=
                    HandleTowerSlotSubjectRequested;
                loadoutView.CloseRequested -=
                    HandleTowerPanelClosed;
            }

            if (towerActionView != null)
            {
                towerActionView.UpgradeRequested -=
                    HandleTowerUpgradeRequested;
                towerActionView.CardsRequested -=
                    HandleTowerCardsRequested;
            }

            if (towerBuildPickerView != null)
            {
                towerBuildPickerView.TowerRequested -=
                    HandleTowerBuildRequested;
                towerBuildPickerView.CloseRequested -=
                    HandleTowerBuildPickerClosed;
            }

            if (enemyInspectionController != null)
            {
                enemyInspectionController.EnemySelected -=
                    HandleEnemySelected;
            }

            if (cameraController != null)
            {
                cameraController.BackgroundClicked -=
                    HandleBackgroundClicked;
            }

            foreach (TowerPresentationHandle presentation in
                     towerPresentations.Values)
            {
                TowerSelectionView selection =
                    presentation.Selection;
                if (selection != null)
                {
                    selection.Clicked -= HandleTowerClicked;
                    selection.DoubleClicked -=
                        HandleTowerDoubleClicked;
                }
            }

            if (stageMap != null)
            {
                for (int i = 0;
                     i < stageMap.BuildSiteCount;
                     i++)
                {
                    TowerBuildSiteView site =
                        stageMap.GetBuildSite(i);
                    if (site != null)
                    {
                        site.Clicked -= HandleBuildSiteClicked;
                    }
                }
            }
        }

        private void ConfigureStageCamera()
        {
            Camera stageCamera = Camera.main;
            if (stageCamera == null)
            {
                return;
            }

            cameraController =
                stageCamera.GetComponent<
                    StageOneCameraController>();
            if (cameraController == null)
            {
                cameraController =
                    stageCamera.gameObject.AddComponent<
                        StageOneCameraController>();
            }

            cameraController.Configure(stageMap);
        }

        private void HandlePlayRequested()
        {
            TogglePlay();
        }

        private void HandleSpeedSelected(float multiplier)
        {
            float selectedSpeed = SetSpeed(multiplier);
            hud.SetStatus(
                "status.speed_format",
                selectedSpeed.ToString("0.#"));
        }

        private void ApplyWorldTimeScale()
        {
            float target = towerBlueprintOpen
                ? 0f
                : snapshot != null &&
                           snapshot.Phase == RunPhase.Combat
                    ? paused
                        ? 0f
                        : speedMultiplier
                    : 1f;
            if (!Mathf.Approximately(Time.timeScale, target))
            {
                Time.timeScale = target;
            }
        }

        private static string PhaseToId(RunPhase phase)
        {
            switch (phase)
            {
                case RunPhase.AwaitingStartingTower:
                    return "awaiting_starting_tower";
                case RunPhase.Planning:
                    return "planning";
                case RunPhase.Combat:
                    return "combat";
                case RunPhase.Draft:
                    return "draft";
                case RunPhase.Victory:
                    return "victory";
                case RunPhase.Defeat:
                    return "defeat";
                case RunPhase.CardPackChoice:
                    return "card_pack_choice";
                case RunPhase.CardPackLoadout:
                    return "card_pack_loadout";
                default:
                    return "unknown";
            }
        }

        private static Vector3 ToWorld(
            SimPosition position,
            float z)
        {
            return new Vector3(
                position.X.MilliUnits / 1000f,
                position.Y.MilliUnits / 1000f,
                z);
        }

        private void RefreshTowerSelectionIndicators()
        {
            foreach (
                KeyValuePair<int, TowerPresentationHandle> pair in
                towerPresentations)
            {
                TowerSelectionView selection =
                    pair.Value.Selection;
                if (selection != null)
                {
                    selection.SetSelected(
                        pair.Key == selectedTowerId);
                    selection.SetContextVisible(
                        pair.Key == selectedTowerId &&
                        !towerBlueprintOpen);
                }
            }
        }

        private static int FindSupportedSpeedIndex(
            float multiplier)
        {
            int closestIndex = 0;
            float closestDistance = float.MaxValue;
            for (int i = 0;
                 i < SupportedSpeedMultipliers.Length;
                 i++)
            {
                float distance = Mathf.Abs(
                    SupportedSpeedMultipliers[i] -
                    multiplier);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = i;
                }
            }

            return closestIndex;
        }
    }
}
