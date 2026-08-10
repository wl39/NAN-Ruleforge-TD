using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    public enum StageOnePlayState
    {
        Disabled = 0,
        Ready = 1,
        Playing = 2,
        Paused = 3,
        Continue = 4
    }

    /// <summary>
    /// Asset-free Stage 01 prototype HUD. It creates a uGUI hierarchy at
    /// runtime and only emits intent events; a battle controller remains the
    /// authority for game phase, speed, rewards, and card loadout.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneHudView : MonoBehaviour
    {
        public const int EquippedCardCapacity = 3;
        public const int RewardChoiceCapacity = 3;

        private static readonly Color BarColor =
            new Color32(48, 31, 21, 0);
        private static readonly Color MobileBarColor =
            new Color32(48, 31, 21, 232);
        private static readonly Color PanelColor =
            new Color32(64, 41, 27, 0);
        private static readonly Color MobilePanelColor =
            Color.white;
        private static readonly Color CardColor =
            new Color32(73, 48, 31, 0);
        private static readonly Color RewardDialogColor =
            Color.white;
        private static readonly Color PrimaryButtonColor =
            new Color32(203, 118, 44, 255);
        private static readonly Color SpeedButtonColor =
            new Color32(76, 92, 50, 255);
        private static readonly Color RewardButtonColor =
            new Color32(82, 58, 38, 255);
        private static readonly Color OverlayColor =
            new Color32(8, 12, 20, 224);
        private static readonly Color TitleColor =
            new Color32(255, 226, 162, 255);
        private static readonly Color BodyColor =
            new Color32(246, 235, 205, 255);
        private static readonly Color MutedColor =
            new Color32(205, 188, 151, 255);

        private const float PlayPulsePeriodSeconds = 1.15f;
        private const float PlayPulseScale = 0.045f;

        private static readonly float[] SupportedSpeedMultipliers =
        {
            0.5f,
            1f,
            2f,
            3f
        };

        private static readonly string[] SpeedLocalizationKeys =
        {
            "speed.0_5x",
            "speed.1x",
            "speed.2x",
            "speed.3x"
        };

        private static readonly string[] SpeedButtonNames =
        {
            "Speed 0.5x Button",
            "Speed 1x Button",
            "Speed 2x Button",
            "Speed 3x Button"
        };

        [SerializeField]
        private TextAsset localizationJson;

        private StageOneUiTextCatalog textCatalog;
        private Font legacyFont;
        private Canvas hudCanvas;
        private Text hudText;
        private Text combatDetailsText;
        private Text statusText;
        private Text equippedCardsTitleText;
        private Text playButtonLabel;
        private Text speedButtonLabel;
        private Button playButton;
        private Button speedButton;
        private Button[] speedButtons = Array.Empty<Button>();
        private Text[] speedButtonLabels = Array.Empty<Text>();
        private Text[] equippedCardTexts = Array.Empty<Text>();
        private GameObject rewardOverlay;
        private Text rewardTitleText;
        private Text rewardInstructionText;
        private Button[] rewardButtons = Array.Empty<Button>();
        private Text[] rewardChoiceTexts = Array.Empty<Text>();
        private StageOneCardView[] rewardCardViews =
            Array.Empty<StageOneCardView>();
        private StageOneResponsiveCanvasScaler responsiveScaler;
        private RectTransform topBar;
        private RectTransform statusPanel;
        private RectTransform rewardDialog;
        private int lastLayoutScreenWidth = -1;
        private int lastLayoutScreenHeight = -1;
        private bool usesPortraitLayout;

        private readonly string[] equippedCardIds =
            new string[EquippedCardCapacity];
        private readonly string[] rewardCardIds =
            new string[RewardChoiceCapacity];
        private readonly StageOneCardDisplay[] equippedCardDisplays =
            new StageOneCardDisplay[EquippedCardCapacity];
        private readonly StageOneCardDisplay[] rewardCardDisplays =
            new StageOneCardDisplay[RewardChoiceCapacity];
        private StageOnePlayState playState = StageOnePlayState.Disabled;
        private float speedMultiplier = 1f;
        private int waveNumber;
        private int totalWaveCount = 1;
        private int stageNumber = 1;
        private int baseHealth;
        private int gold;
        private string phaseId = "awaiting_starting_tower";
        private string statusKey = "status.ready";
        private string combatDetails = string.Empty;
        private object[] statusArguments = Array.Empty<object>();
        private bool equippedDisplaysAreExplicit;
        private bool rewardDisplaysAreExplicit;
        private bool equippedCardsUseEnemyInterpretation;
        private int visibleRewardChoiceCount;
        private bool hudVisible = true;
        private bool built;

        public event Action PlayRequested;
        public event Action<float> SpeedSelected;
        public event Action SpeedRequested;
        public event Action<int> RewardChoiceRequested;
        public event Action<bool> RewardVisibilityChanged;

        public Canvas HudCanvas => hudCanvas;
        public Text HudText => hudText;
        public Text CombatDetailsText => combatDetailsText;
        public Text StatusText => statusText;
        public Button PlayButton => playButton;
        public Button SpeedButton => speedButton;
        public Text PlayButtonLabel => playButtonLabel;
        public Text SpeedButtonLabel => speedButtonLabel;
        public StageOnePlayState PlayState => playState;
        public float SpeedMultiplier => speedMultiplier;
        public int SpeedButtonCount => speedButtons.Length;
        public int EquippedCardPanelCount => equippedCardTexts.Length;
        public int RewardButtonCount => rewardButtons.Length;
        public int VisibleRewardChoiceCount => visibleRewardChoiceCount;
        public bool IsRewardVisible =>
            rewardOverlay != null && rewardOverlay.activeSelf;
        public bool IsVisible =>
            hudCanvas != null &&
            hudCanvas.enabled &&
            hudVisible;
        public bool IsBuilt => built;
        public bool IsPlayButtonPulsing =>
            (playState == StageOnePlayState.Ready ||
             playState == StageOnePlayState.Continue) &&
            playButton != null &&
            playButton.interactable;

        public static StageOneHudView CreateRuntime(
            StageOneUiTextCatalog catalog,
            Transform parent = null)
        {
            var host = new GameObject("Stage One HUD");
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            StageOneHudView view =
                host.AddComponent<StageOneHudView>();
            view.SetTextCatalog(catalog);
            return view;
        }

        private void Awake()
        {
            textCatalog = StageOneUiTextCatalog.Load(localizationJson);
            BuildInterface();
            RefreshAllLocalizedText();
        }

        private void OnDestroy()
        {
            if (playButton != null)
            {
                playButton.onClick.RemoveListener(HandlePlayClicked);
            }

            RemoveSpeedButtonListeners();
        }

        private void Update()
        {
            if (!built)
            {
                return;
            }

            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            if (width != lastLayoutScreenWidth ||
                height != lastLayoutScreenHeight)
            {
                ApplyResponsiveLayout();
            }

            UpdatePlayButtonPulse();
        }

        public void Configure(TextAsset stageLocalizationJson)
        {
            localizationJson = stageLocalizationJson;
            SetTextCatalog(StageOneUiTextCatalog.Load(localizationJson));
        }

        public void SetTextCatalog(StageOneUiTextCatalog catalog)
        {
            textCatalog = catalog ??
                StageOneUiTextCatalog.FromJson(null);
            RefreshCatalogDerivedCardDisplays();
            BuildInterface();
            RefreshAllLocalizedText();
        }

        public void SetFont(Font font)
        {
            legacyFont = font != null
                ? font
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            if (!built)
            {
                return;
            }

            Text[] texts = GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                texts[i].font = legacyFont;
            }
        }

        /// <summary>
        /// Hides every Stage 01 HUD element and disables HUD raycasts while a
        /// modal tower blueprint owns the screen. Cached HUD/reward state is
        /// retained so closing the blueprint restores it immediately.
        /// </summary>
        public void SetVisible(bool visible)
        {
            hudVisible = visible;
            if (hudCanvas == null)
            {
                return;
            }

            hudCanvas.enabled = visible;
            GraphicRaycaster raycaster =
                hudCanvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
            {
                raycaster.enabled = visible;
            }
        }

        public void SetHud(
            int currentWaveNumber,
            int waveCount,
            int currentBaseHealth,
            int currentGold,
            string currentPhaseId,
            int currentStageNumber = 1)
        {
            waveNumber = Mathf.Max(0, currentWaveNumber);
            totalWaveCount = Mathf.Max(1, waveCount);
            stageNumber = Mathf.Max(1, currentStageNumber);
            baseHealth = Mathf.Max(0, currentBaseHealth);
            gold = Mathf.Max(0, currentGold);
            phaseId = string.IsNullOrWhiteSpace(currentPhaseId)
                ? "unknown"
                : currentPhaseId.Trim();
            RefreshHudText();
            RefreshPlayState();
        }

        /// <summary>
        /// 개별 피해/골드 팝업 대신 합산한 고밀도 전투 지표 또는 다음 웨이브
        /// 구성을 상단 중앙에 표시한다.
        /// </summary>
        public void SetCombatDetails(string details)
        {
            combatDetails = details ?? string.Empty;
            RefreshCombatDetails();
        }

        public void SetPlayState(StageOnePlayState state)
        {
            playState = state;
            RefreshPlayState();
        }

        public void SetPlayState(
            bool isPlaying,
            bool isPaused,
            bool interactable = true)
        {
            if (!interactable)
            {
                SetPlayState(StageOnePlayState.Disabled);
                return;
            }

            SetPlayState(
                isPaused
                    ? StageOnePlayState.Paused
                    : isPlaying
                        ? StageOnePlayState.Playing
                        : StageOnePlayState.Ready);
        }

        public void SetSpeed(float multiplier)
        {
            speedMultiplier = NormalizeSpeedMultiplier(multiplier);
            RefreshSpeedButtons();
        }

        public Button GetSpeedButton(float multiplier)
        {
            int index = FindSupportedSpeedIndex(multiplier);
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(multiplier),
                    multiplier,
                    "Supported Stage 01 speeds are 0.5x, 1x, 2x, and 3x.");
            }

            return index < speedButtons.Length
                ? speedButtons[index]
                : null;
        }

        public void SetStatus(
            string localizationKey,
            params object[] arguments)
        {
            statusKey = string.IsNullOrWhiteSpace(localizationKey)
                ? "status.empty"
                : localizationKey.Trim();
            statusArguments = arguments == null
                ? Array.Empty<object>()
                : (object[])arguments.Clone();
            RefreshStatusText();
        }

        public void SetEquippedCards(
            IReadOnlyList<string> cardIds,
            bool useEnemyInterpretation = false)
        {
            equippedDisplaysAreExplicit = false;
            equippedCardsUseEnemyInterpretation =
                useEnemyInterpretation;
            for (int i = 0; i < equippedCardIds.Length; i++)
            {
                equippedCardIds[i] =
                    cardIds != null && i < cardIds.Count
                        ? cardIds[i]
                        : null;
                equippedCardDisplays[i] =
                    string.IsNullOrWhiteSpace(equippedCardIds[i])
                        ? default(StageOneCardDisplay)
                        : GetCatalog().GetCardDisplay(
                            equippedCardIds[i],
                            useEnemyInterpretation);
            }

            RefreshEquippedCards();
        }

        public void SetEquippedCards(
            IReadOnlyList<StageOneCardDisplay> cards)
        {
            equippedDisplaysAreExplicit = true;
            for (int i = 0; i < equippedCardDisplays.Length; i++)
            {
                StageOneCardDisplay display =
                    cards != null && i < cards.Count
                        ? cards[i]
                        : default(StageOneCardDisplay);
                equippedCardDisplays[i] = display;
                equippedCardIds[i] = display.StableId;
            }

            RefreshEquippedCards();
        }

        public void ShowRewardChoices(IReadOnlyList<string> cardIds)
        {
            bool wasVisible = IsRewardVisible;
            rewardDisplaysAreExplicit = false;
            visibleRewardChoiceCount = Mathf.Min(
                RewardChoiceCapacity,
                cardIds == null ? 0 : cardIds.Count);
            for (int i = 0; i < rewardCardIds.Length; i++)
            {
                rewardCardIds[i] =
                    i < visibleRewardChoiceCount
                        ? cardIds[i]
                        : null;
                rewardCardDisplays[i] =
                    string.IsNullOrWhiteSpace(rewardCardIds[i])
                        ? default(StageOneCardDisplay)
                        : GetCatalog().GetCardDisplay(rewardCardIds[i]);
            }

            RefreshRewardChoices();
            if (rewardOverlay != null)
            {
                rewardOverlay.SetActive(true);
            }

            if (!wasVisible && IsRewardVisible)
            {
                RewardVisibilityChanged?.Invoke(true);
            }
        }

        public void ShowRewardChoices(
            IReadOnlyList<StageOneCardDisplay> cards)
        {
            bool wasVisible = IsRewardVisible;
            rewardDisplaysAreExplicit = true;
            visibleRewardChoiceCount = Mathf.Min(
                RewardChoiceCapacity,
                cards == null ? 0 : cards.Count);
            for (int i = 0; i < rewardCardDisplays.Length; i++)
            {
                StageOneCardDisplay display =
                    i < visibleRewardChoiceCount
                        ? cards[i]
                        : default(StageOneCardDisplay);
                rewardCardDisplays[i] = display;
                rewardCardIds[i] = display.StableId;
            }

            RefreshRewardChoices();
            if (rewardOverlay != null)
            {
                rewardOverlay.SetActive(true);
            }

            if (!wasVisible && IsRewardVisible)
            {
                RewardVisibilityChanged?.Invoke(true);
            }
        }

        public void HideRewardChoices()
        {
            bool wasVisible = IsRewardVisible;
            visibleRewardChoiceCount = 0;
            Array.Clear(rewardCardIds, 0, rewardCardIds.Length);
            Array.Clear(
                rewardCardDisplays,
                0,
                rewardCardDisplays.Length);
            ClearRewardInputFocus();
            if (rewardOverlay != null)
            {
                rewardOverlay.SetActive(false);
            }

            if (wasVisible && !IsRewardVisible)
            {
                RewardVisibilityChanged?.Invoke(false);
            }
        }

        private void ClearRewardInputFocus()
        {
            if (rewardOverlay == null ||
                EventSystem.current == null)
            {
                return;
            }

            GameObject selected =
                EventSystem.current.currentSelectedGameObject;
            if (selected != null &&
                selected.transform.IsChildOf(
                    rewardOverlay.transform))
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        public Text GetEquippedCardText(int index)
        {
            ValidateIndex(
                index,
                equippedCardTexts.Length,
                nameof(index));
            return equippedCardTexts[index];
        }

        public Button GetRewardChoiceButton(int index)
        {
            ValidateIndex(index, rewardButtons.Length, nameof(index));
            return rewardButtons[index];
        }

        public Text GetRewardChoiceText(int index)
        {
            ValidateIndex(
                index,
                rewardChoiceTexts.Length,
                nameof(index));
            return rewardChoiceTexts[index];
        }

        public StageOneCardView GetRewardChoiceCard(int index)
        {
            ValidateIndex(
                index,
                rewardCardViews.Length,
                nameof(index));
            return rewardCardViews[index];
        }

        private void BuildInterface()
        {
            if (built)
            {
                return;
            }

            legacyFont = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");

            GameObject canvasObject = new GameObject(
                "Stage One HUD Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            hudCanvas = canvasObject.GetComponent<Canvas>();
            hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hudCanvas.pixelPerfect = true;
            hudCanvas.sortingOrder = 100;

            CanvasScaler scaler =
                canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            responsiveScaler = canvasObject.AddComponent<
                StageOneResponsiveCanvasScaler>();

            RectTransform safeArea = new GameObject(
                "Safe Area",
                typeof(RectTransform),
                typeof(StageOneSafeAreaFitter))
                .GetComponent<RectTransform>();
            safeArea.SetParent(canvasObject.transform, false);
            safeArea.anchorMin = Vector2.zero;
            safeArea.anchorMax = Vector2.one;
            safeArea.offsetMin = Vector2.zero;
            safeArea.offsetMax = Vector2.zero;

            EnsureEventSystem();
            CreateTopBar(safeArea);
            CreateStatusPanel(safeArea);
            CreateCardStrip(safeArea);
            CreateRewardOverlay(safeArea);
            built = true;
            ApplyResponsiveLayout(true);
            SetVisible(hudVisible);
        }

        private void CreateTopBar(Transform parent)
        {
            topBar = CreatePanel(
                "Top HUD",
                parent,
                BarColor);
            AnchorAtTop(
                topBar,
                StageOneHudLayoutMetrics.DesktopTopBarHeight,
                Vector2.zero);

            hudText = CreateText(
                "HUD Summary",
                topBar,
                19,
                FontStyle.Bold,
                BodyColor,
                TextAnchor.MiddleLeft);
            Stretch(
                hudText.rectTransform,
                18f,
                6f,
                374f,
                6f);

            combatDetailsText = CreateText(
                "Combat Density Details",
                topBar,
                13,
                FontStyle.Bold,
                TitleColor,
                TextAnchor.MiddleCenter);
            combatDetailsText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            combatDetailsText.verticalOverflow =
                VerticalWrapMode.Truncate;
            Stretch(
                combatDetailsText.rectTransform,
                390f,
                4f,
                470f,
                4f);

            playButton = CreateButton(
                "Play Button",
                topBar,
                PrimaryButtonColor,
                out playButtonLabel,
                16);
            AnchorAtRight(
                playButton.GetComponent<RectTransform>(),
                new Vector2(-236f, 0f),
                new Vector2(148f, 45f));
            playButton.onClick.AddListener(HandlePlayClicked);

            speedButtons =
                new Button[SupportedSpeedMultipliers.Length];
            speedButtonLabels =
                new Text[SupportedSpeedMultipliers.Length];
            for (int i = 0; i < speedButtons.Length; i++)
            {
                Button button = CreateButton(
                    SpeedButtonNames[i],
                    topBar,
                    SpeedButtonColor,
                    out Text label,
                    14);
                float rightOffset =
                    -12f -
                    (speedButtons.Length - 1 - i) * 54f;
                AnchorAtRight(
                    button.GetComponent<RectTransform>(),
                    new Vector2(rightOffset, 0f),
                    new Vector2(50f, 36f));
                speedButtons[i] = button;
                speedButtonLabels[i] = label;
            }

            speedButtons[0].onClick.AddListener(
                HandleHalfSpeedClicked);
            speedButtons[1].onClick.AddListener(
                HandleNormalSpeedClicked);
            speedButtons[2].onClick.AddListener(
                HandleDoubleSpeedClicked);
            speedButtons[3].onClick.AddListener(
                HandleTripleSpeedClicked);

            // Compatibility aliases for callers that used the old single
            // speed button. The 2x option preserves its most common action.
            speedButton = speedButtons[2];
            speedButtonLabel = speedButtonLabels[2];
            RefreshSpeedButtons();
        }

        private void CreateStatusPanel(Transform parent)
        {
            statusPanel = CreatePanel(
                "Instruction And Status",
                parent,
                PanelColor);
            AnchorAtTop(
                statusPanel,
                StageOneHudLayoutMetrics
                    .DesktopStatusPanelHeight,
                new Vector2(
                    0f,
                    -StageOneHudLayoutMetrics
                        .DesktopStatusPanelTopOffset));
            statusPanel.anchorMin = new Vector2(0.15f, 1f);
            statusPanel.anchorMax = new Vector2(0.85f, 1f);

            statusText = CreateText(
                "Status Text",
                statusPanel,
                15,
                FontStyle.Normal,
                MutedColor,
                TextAnchor.MiddleCenter);
            statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            statusText.verticalOverflow = VerticalWrapMode.Truncate;
            Stretch(
                statusText.rectTransform,
                18f,
                8f,
                18f,
                8f);
        }

        private void CreateCardStrip(Transform parent)
        {
            RectTransform strip = CreatePanel(
                "Equipped Cards",
                parent,
                PanelColor);
            AnchorAtBottomCenter(
                strip,
                new Vector2(0f, 14f),
                new Vector2(970f, 154f));

            equippedCardsTitleText = CreateText(
                "Equipped Cards Title",
                strip,
                20,
                FontStyle.Bold,
                TitleColor,
                TextAnchor.MiddleLeft);
            RectTransform titleRect =
                equippedCardsTitleText.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -6f);
            titleRect.sizeDelta = new Vector2(-28f, 32f);

            equippedCardTexts = new Text[EquippedCardCapacity];
            for (int i = 0; i < equippedCardTexts.Length; i++)
            {
                RectTransform cardPanel = CreatePanel(
                    "Equipped Card " + (i + 1),
                    strip,
                    CardColor);
                AnchorAtCenter(
                    cardPanel,
                    new Vector2((i - 1) * 310f, -16f),
                    new Vector2(294f, 104f));

                Text cardText = CreateText(
                    "Card Text",
                    cardPanel,
                    16,
                    FontStyle.Normal,
                    BodyColor,
                    TextAnchor.MiddleCenter);
                cardText.horizontalOverflow =
                    HorizontalWrapMode.Wrap;
                cardText.verticalOverflow =
                    VerticalWrapMode.Truncate;
                Stretch(
                    cardText.rectTransform,
                    10f,
                    8f,
                    10f,
                    8f);
                equippedCardTexts[i] = cardText;
            }

            // 카드 장착은 선택한 타워의 전용 패널에서 처리한다.
            // 기존 참조는 테스트와 보상 표시 호환성을 위해 유지한다.
            strip.gameObject.SetActive(false);
        }

        private void CreateRewardOverlay(Transform parent)
        {
            rewardOverlay = new GameObject(
                "Reward Choice Overlay",
                typeof(RectTransform),
                typeof(Image));
            rewardOverlay.transform.SetParent(parent, false);
            RectTransform overlayRect =
                rewardOverlay.GetComponent<RectTransform>();
            Stretch(overlayRect, 0f, 0f, 0f, 0f);
            rewardOverlay.GetComponent<Image>().color = OverlayColor;

            rewardDialog = CreatePanel(
                "Reward Choice Dialog",
                rewardOverlay.transform,
                RewardDialogColor);
            RuleforgePixelUi.ApplyPanel(
                rewardDialog.GetComponent<Image>(),
                RuleforgePixelPanelRole.Workbench,
                RewardDialogColor);
            AnchorAtCenter(
                rewardDialog,
                Vector2.zero,
                new Vector2(1020f, 570f));

            rewardTitleText = CreateText(
                "Reward Title",
                rewardDialog,
                31,
                FontStyle.Bold,
                TitleColor,
                TextAnchor.MiddleCenter);
            AnchorAtTop(
                rewardTitleText.rectTransform,
                54f,
                new Vector2(0f, -20f));
            rewardTitleText.rectTransform.anchorMin =
                new Vector2(0.1f, 1f);
            rewardTitleText.rectTransform.anchorMax =
                new Vector2(0.9f, 1f);

            rewardInstructionText = CreateText(
                "Reward Instruction",
                rewardDialog,
                18,
                FontStyle.Normal,
                MutedColor,
                TextAnchor.MiddleCenter);
            AnchorAtTop(
                rewardInstructionText.rectTransform,
                36f,
                new Vector2(0f, -76f));
            rewardInstructionText.rectTransform.anchorMin =
                new Vector2(0.1f, 1f);
            rewardInstructionText.rectTransform.anchorMax =
                new Vector2(0.9f, 1f);

            rewardButtons = new Button[RewardChoiceCapacity];
            rewardChoiceTexts = new Text[RewardChoiceCapacity];
            rewardCardViews =
                new StageOneCardView[RewardChoiceCapacity];
            for (int i = 0; i < rewardButtons.Length; i++)
            {
                StageOneCardView cardView =
                    StageOneCardView.CreateRuntime(
                    "Reward Choice " + (i + 1),
                    rewardDialog,
                    legacyFont);
                cardView.SetExpandedPresentation(true);
                Button button = cardView.Button;
                AnchorAtCenter(
                    cardView.GetComponent<RectTransform>(),
                    new Vector2((i - 1) * 320f, -48f),
                    new Vector2(272f, 380f));

                int choiceIndex = i;
                button.onClick.AddListener(
                    () => HandleRewardClicked(choiceIndex));
                rewardButtons[i] = button;
                rewardChoiceTexts[i] = cardView.NameText;
                rewardCardViews[i] = cardView;
            }

            rewardOverlay.SetActive(false);
        }

        private void ApplyResponsiveLayout(bool force = false)
        {
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            bool portrait = height > width;
            if (!force &&
                width == lastLayoutScreenWidth &&
                height == lastLayoutScreenHeight &&
                portrait == usesPortraitLayout)
            {
                return;
            }

            if (responsiveScaler != null)
            {
                responsiveScaler.ApplyScale();
            }

            usesPortraitLayout = portrait;
            lastLayoutScreenWidth = width;
            lastLayoutScreenHeight = height;

            if (portrait)
            {
                ApplyPortraitTopLayout();
                ApplyPortraitRewardLayout();
            }
            else
            {
                ApplyLandscapeTopLayout();
                ApplyLandscapeRewardLayout();
            }
        }

        private void ApplyPortraitTopLayout()
        {
            AnchorAtTop(
                topBar,
                StageOneHudLayoutMetrics.PortraitTopBarHeight,
                Vector2.zero);
            topBar.GetComponent<Image>().color = MobileBarColor;

            RectTransform summaryRect = hudText.rectTransform;
            summaryRect.anchorMin = new Vector2(0f, 1f);
            summaryRect.anchorMax = new Vector2(1f, 1f);
            summaryRect.pivot = new Vector2(0.5f, 1f);
            summaryRect.anchoredPosition = new Vector2(0f, -4f);
            summaryRect.sizeDelta = new Vector2(-24f, 42f);
            hudText.fontSize = 21;
            hudText.alignment = TextAnchor.MiddleLeft;
            hudText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hudText.verticalOverflow = VerticalWrapMode.Truncate;
            if (combatDetailsText != null)
            {
                combatDetailsText.gameObject.SetActive(false);
            }

            SetBottomAnchoredRange(
                playButton.GetComponent<RectTransform>(),
                0.02f,
                0.3f,
                10f,
                54f,
                2f,
                2f);
            playButtonLabel.fontSize = 18;

            const float speedsStart = 0.32f;
            const float speedsEnd = 0.99f;
            float speedWidth =
                (speedsEnd - speedsStart) / speedButtons.Length;
            for (int i = 0; i < speedButtons.Length; i++)
            {
                SetBottomAnchoredRange(
                    speedButtons[i].GetComponent<RectTransform>(),
                    speedsStart + speedWidth * i,
                    speedsStart + speedWidth * (i + 1),
                    10f,
                    54f,
                    3f,
                    3f);
                speedButtonLabels[i].fontSize = 18;
            }

            AnchorAtTop(
                statusPanel,
                StageOneHudLayoutMetrics
                    .PortraitStatusPanelHeight,
                new Vector2(
                    0f,
                    -StageOneHudLayoutMetrics
                        .PortraitStatusPanelTopOffset));
            statusPanel.anchorMin = new Vector2(0.025f, 1f);
            statusPanel.anchorMax = new Vector2(0.975f, 1f);
            Image statusImage = statusPanel.GetComponent<Image>();
            RuleforgePixelUi.ApplyPanel(
                statusImage,
                RuleforgePixelPanelRole.Parchment,
                MobilePanelColor);
            statusText.color = RuleforgePixelUi.InkText;
            RuleforgeUiTypography.RestyleButtonLabel(
                statusText,
                RuleforgePixelUi.InkText);
            Shadow statusShadow = statusText.GetComponent<Shadow>();
            if (statusShadow != null)
            {
                statusShadow.enabled = false;
            }
            statusText.fontSize = 17;
            Stretch(
                statusText.rectTransform,
                12f,
                7f,
                12f,
                7f);
        }

        private void ApplyLandscapeTopLayout()
        {
            if (combatDetailsText != null)
            {
                combatDetailsText.gameObject.SetActive(true);
                Stretch(
                    combatDetailsText.rectTransform,
                    390f,
                    4f,
                    470f,
                    4f);
            }

            AnchorAtTop(
                topBar,
                StageOneHudLayoutMetrics.DesktopTopBarHeight,
                Vector2.zero);
            topBar.GetComponent<Image>().color = BarColor;
            hudText.fontSize = 19;
            hudText.alignment = TextAnchor.MiddleLeft;
            hudText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hudText.verticalOverflow = VerticalWrapMode.Truncate;
            Stretch(
                hudText.rectTransform,
                18f,
                6f,
                374f,
                6f);

            AnchorAtRight(
                playButton.GetComponent<RectTransform>(),
                new Vector2(-236f, 0f),
                new Vector2(148f, 45f));
            playButtonLabel.fontSize = 16;

            for (int i = 0; i < speedButtons.Length; i++)
            {
                float rightOffset =
                    -12f -
                    (speedButtons.Length - 1 - i) * 54f;
                AnchorAtRight(
                    speedButtons[i].GetComponent<RectTransform>(),
                    new Vector2(rightOffset, 0f),
                    new Vector2(50f, 36f));
                speedButtonLabels[i].fontSize = 14;
            }

            AnchorAtTop(
                statusPanel,
                StageOneHudLayoutMetrics
                    .DesktopStatusPanelHeight,
                new Vector2(
                    0f,
                    -StageOneHudLayoutMetrics
                        .DesktopStatusPanelTopOffset));
            statusPanel.anchorMin = new Vector2(0.15f, 1f);
            statusPanel.anchorMax = new Vector2(0.85f, 1f);
            Image statusImage = statusPanel.GetComponent<Image>();
            statusImage.sprite = null;
            statusImage.type = Image.Type.Simple;
            statusImage.preserveAspect = false;
            statusImage.color = PanelColor;
            statusText.color = MutedColor;
            RuleforgeUiTypography.RestyleButtonLabel(
                statusText,
                MutedColor);
            statusText.fontSize = 15;
            Stretch(
                statusText.rectTransform,
                18f,
                8f,
                18f,
                8f);
        }

        private void ApplyPortraitRewardLayout()
        {
            AnchorAtCenter(
                rewardDialog,
                Vector2.zero,
                new Vector2(504f, 888f));

            rewardTitleText.fontSize = 29;
            AnchorAtTop(
                rewardTitleText.rectTransform,
                50f,
                new Vector2(0f, -16f));
            rewardTitleText.rectTransform.anchorMin =
                new Vector2(0.04f, 1f);
            rewardTitleText.rectTransform.anchorMax =
                new Vector2(0.96f, 1f);

            rewardInstructionText.fontSize = 18;
            AnchorAtTop(
                rewardInstructionText.rectTransform,
                34f,
                new Vector2(0f, -66f));
            rewardInstructionText.rectTransform.anchorMin =
                new Vector2(0.04f, 1f);
            rewardInstructionText.rectTransform.anchorMax =
                new Vector2(0.96f, 1f);

            for (int i = 0; i < rewardCardViews.Length; i++)
            {
                AnchorAtCenter(
                    rewardCardViews[i].GetComponent<RectTransform>(),
                    new Vector2(0f, 220f - i * 240f),
                    new Vector2(444f, 220f));
            }
        }

        private void ApplyLandscapeRewardLayout()
        {
            AnchorAtCenter(
                rewardDialog,
                Vector2.zero,
                new Vector2(1020f, 570f));

            rewardTitleText.fontSize = 31;
            AnchorAtTop(
                rewardTitleText.rectTransform,
                54f,
                new Vector2(0f, -20f));
            rewardTitleText.rectTransform.anchorMin =
                new Vector2(0.1f, 1f);
            rewardTitleText.rectTransform.anchorMax =
                new Vector2(0.9f, 1f);

            rewardInstructionText.fontSize = 18;
            AnchorAtTop(
                rewardInstructionText.rectTransform,
                36f,
                new Vector2(0f, -76f));
            rewardInstructionText.rectTransform.anchorMin =
                new Vector2(0.1f, 1f);
            rewardInstructionText.rectTransform.anchorMax =
                new Vector2(0.9f, 1f);

            for (int i = 0; i < rewardCardViews.Length; i++)
            {
                AnchorAtCenter(
                    rewardCardViews[i].GetComponent<RectTransform>(),
                    new Vector2((i - 1) * 320f, -48f),
                    new Vector2(252f, 396f));
            }
        }

        private void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var eventSystemObject = new GameObject(
                "Stage One Event System",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            eventSystemObject.transform.SetParent(transform, false);
        }

        private void RefreshAllLocalizedText()
        {
            if (!built)
            {
                return;
            }

            RefreshHudText();
            RefreshCombatDetails();
            RefreshPlayState();
            SetSpeed(speedMultiplier);
            RefreshStatusText();
            if (equippedCardsTitleText != null)
            {
                equippedCardsTitleText.text =
                    GetText("hud.cards_title");
            }
            RefreshEquippedCards();
            RefreshRewardChoices();
        }

        private void RefreshHudText()
        {
            if (hudText == null)
            {
                return;
            }

            hudText.text = FormatText(
                "hud.summary_format",
                waveNumber,
                totalWaveCount,
                baseHealth,
                gold,
                GetPhaseText(phaseId),
                stageNumber);
        }

        private void RefreshCombatDetails()
        {
            if (combatDetailsText != null)
            {
                combatDetailsText.text = combatDetails;
            }
        }

        private void RefreshPlayState()
        {
            if (playButton == null || playButtonLabel == null)
            {
                return;
            }

            playButton.interactable =
                playState != StageOnePlayState.Disabled;
            switch (playState)
            {
                case StageOnePlayState.Playing:
                    playButtonLabel.text = GetText("hud.pause");
                    break;
                case StageOnePlayState.Paused:
                    playButtonLabel.text = GetText("hud.resume");
                    break;
                case StageOnePlayState.Continue:
                    playButtonLabel.text = GetText(
                        "hud.continue_stage");
                    break;
                default:
                    playButtonLabel.text = FormatText(
                        "hud.play_format",
                        Mathf.Max(1, waveNumber));
                    break;
            }

            if (!IsPlayButtonPulsing)
            {
                playButton.transform.localScale = Vector3.one;
            }
        }

        private void UpdatePlayButtonPulse()
        {
            if (playButton == null)
            {
                return;
            }

            if (!IsPlayButtonPulsing)
            {
                playButton.transform.localScale = Vector3.one;
                return;
            }

            float cycle = Mathf.Repeat(
                Time.unscaledTime,
                PlayPulsePeriodSeconds) /
                PlayPulsePeriodSeconds;
            float wave = (Mathf.Sin(
                cycle * Mathf.PI * 2f - Mathf.PI * 0.5f) + 1f) *
                0.5f;
            float eased = wave * wave * (3f - 2f * wave);
            float scale = 1f + PlayPulseScale * eased;
            playButton.transform.localScale =
                new Vector3(scale, scale, 1f);
        }

        private void RefreshStatusText()
        {
            if (statusText != null)
            {
                statusText.text = FormatText(
                    statusKey,
                    statusArguments);
            }
        }

        private void RefreshEquippedCards()
        {
            for (int i = 0; i < equippedCardTexts.Length; i++)
            {
                StageOneCardDisplay card = equippedCardDisplays[i];
                if (!card.IsValid)
                {
                    equippedCardTexts[i].text =
                        GetText("hud.empty_card");
                    continue;
                }

                equippedCardTexts[i].text = FormatText(
                    "hud.card_format",
                    card.Name,
                    card.Description);
            }
        }

        private void RefreshRewardChoices()
        {
            if (rewardTitleText != null)
            {
                rewardTitleText.text = GetText("reward.title");
            }

            if (rewardInstructionText != null)
            {
                rewardInstructionText.text =
                    GetText("reward.instruction");
            }

            for (int i = 0; i < rewardButtons.Length; i++)
            {
                bool visible = i < visibleRewardChoiceCount &&
                    rewardCardDisplays[i].IsValid;
                rewardButtons[i].gameObject.SetActive(visible);
                rewardButtons[i].interactable = visible;
                if (!visible)
                {
                    rewardChoiceTexts[i].text = string.Empty;
                    continue;
                }

                StageOneCardDisplay card = rewardCardDisplays[i];
                rewardCardViews[i].Configure(
                    card,
                    (CardTier)Mathf.Clamp(card.Tier, 1, 5),
                    SubjectType.Projectile,
                    null,
                    false,
                    null,
                    true);
            }
        }

        private string GetText(string key)
        {
            return GetCatalog().Get(key);
        }

        private string FormatText(
            string key,
            params object[] arguments)
        {
            return GetCatalog().Format(
                    key,
                    arguments);
        }

        private string GetPhaseText(string id)
        {
            return GetCatalog().GetPhase(id);
        }

        private StageOneUiTextCatalog GetCatalog()
        {
            if (textCatalog == null)
            {
                textCatalog = StageOneUiTextCatalog.FromJson(null);
            }

            return textCatalog;
        }

        private void RefreshCatalogDerivedCardDisplays()
        {
            StageOneUiTextCatalog catalog = GetCatalog();
            if (!equippedDisplaysAreExplicit)
            {
                for (int i = 0; i < equippedCardDisplays.Length; i++)
                {
                    equippedCardDisplays[i] =
                        string.IsNullOrWhiteSpace(equippedCardIds[i])
                            ? default(StageOneCardDisplay)
                            : catalog.GetCardDisplay(
                                equippedCardIds[i],
                                equippedCardsUseEnemyInterpretation);
                }
            }

            if (!rewardDisplaysAreExplicit)
            {
                for (int i = 0; i < rewardCardDisplays.Length; i++)
                {
                    rewardCardDisplays[i] =
                        string.IsNullOrWhiteSpace(rewardCardIds[i])
                            ? default(StageOneCardDisplay)
                            : catalog.GetCardDisplay(rewardCardIds[i]);
                }
            }
        }

        private void HandlePlayClicked()
        {
            PlayRequested?.Invoke();
        }

        private void HandleHalfSpeedClicked()
        {
            HandleSpeedSelected(0.5f);
        }

        private void HandleNormalSpeedClicked()
        {
            HandleSpeedSelected(1f);
        }

        private void HandleDoubleSpeedClicked()
        {
            HandleSpeedSelected(2f);
        }

        private void HandleTripleSpeedClicked()
        {
            HandleSpeedSelected(3f);
        }

        private void HandleSpeedSelected(float multiplier)
        {
            SetSpeed(multiplier);
            SpeedSelected?.Invoke(speedMultiplier);
            SpeedRequested?.Invoke();
        }

        private void RefreshSpeedButtons()
        {
            for (int i = 0; i < speedButtons.Length; i++)
            {
                Button button = speedButtons[i];
                Text label =
                    i < speedButtonLabels.Length
                        ? speedButtonLabels[i]
                        : null;
                if (label != null)
                {
                    label.text = GetText(SpeedLocalizationKeys[i]);
                }

                if (button == null)
                {
                    continue;
                }

                bool selected = Mathf.Approximately(
                    SupportedSpeedMultipliers[i],
                    speedMultiplier);
                RuleforgePixelUi.Apply(
                    button,
                    selected
                        ? RuleforgePixelButtonRole.Selected
                        : RuleforgePixelButtonRole.Utility);

                if (label != null)
                {
                    label.color = RuleforgePixelUi.ParchmentText;
                }
            }
        }

        private void RemoveSpeedButtonListeners()
        {
            if (speedButtons.Length > 0 && speedButtons[0] != null)
            {
                speedButtons[0].onClick.RemoveListener(
                    HandleHalfSpeedClicked);
            }

            if (speedButtons.Length > 1 && speedButtons[1] != null)
            {
                speedButtons[1].onClick.RemoveListener(
                    HandleNormalSpeedClicked);
            }

            if (speedButtons.Length > 2 && speedButtons[2] != null)
            {
                speedButtons[2].onClick.RemoveListener(
                    HandleDoubleSpeedClicked);
            }

            if (speedButtons.Length > 3 && speedButtons[3] != null)
            {
                speedButtons[3].onClick.RemoveListener(
                    HandleTripleSpeedClicked);
            }
        }

        private static float NormalizeSpeedMultiplier(float multiplier)
        {
            int exactIndex = FindSupportedSpeedIndex(multiplier);
            if (exactIndex >= 0)
            {
                return SupportedSpeedMultipliers[exactIndex];
            }

            if (float.IsNaN(multiplier) ||
                float.IsInfinity(multiplier))
            {
                return 1f;
            }

            int closestIndex = 0;
            float closestDistance = float.MaxValue;
            for (int i = 0;
                 i < SupportedSpeedMultipliers.Length;
                 i++)
            {
                float distance = Mathf.Abs(
                    SupportedSpeedMultipliers[i] - multiplier);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = i;
                }
            }

            return SupportedSpeedMultipliers[closestIndex];
        }

        private static int FindSupportedSpeedIndex(float multiplier)
        {
            for (int i = 0;
                 i < SupportedSpeedMultipliers.Length;
                 i++)
            {
                if (Mathf.Approximately(
                        SupportedSpeedMultipliers[i],
                        multiplier))
                {
                    return i;
                }
            }

            return -1;
        }

        private void HandleRewardClicked(int choiceIndex)
        {
            if (choiceIndex < 0 ||
                choiceIndex >= visibleRewardChoiceCount)
            {
                return;
            }

            RewardChoiceRequested?.Invoke(choiceIndex);
        }

        private Button CreateButton(
            string objectName,
            Transform parent,
            Color color,
            out Text label,
            int fontSize = 20)
        {
            var buttonObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            Image image = buttonObject.GetComponent<Image>();
            image.color = color;

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f);
            colors.disabledColor = new Color(0.42f, 0.42f, 0.42f, 0.7f);
            colors.colorMultiplier = 1f;
            button.colors = colors;

            label = CreateText(
                "Label",
                buttonObject.transform,
                fontSize,
                FontStyle.Bold,
                BodyColor,
                TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 8f, 4f, 8f, 4f);
            RuleforgePixelButtonRole role =
                color == PrimaryButtonColor
                    ? RuleforgePixelButtonRole.Primary
                    : color == SpeedButtonColor
                        ? RuleforgePixelButtonRole.Utility
                        : RuleforgePixelButtonRole.Secondary;
            RuleforgePixelUi.ApplyTint(button, role, color);
            return button;
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            int fontSize,
            FontStyle style,
            Color color,
            TextAnchor alignment)
        {
            var textObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            RuleforgeUiTypography.Configure(
                text,
                legacyFont,
                fontSize,
                color,
                alignment,
                RuleforgeUiTypography.IsLight(color));
            return text;
        }

        private static RectTransform CreatePanel(
            string objectName,
            Transform parent,
            Color color)
        {
            var panelObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image));
            panelObject.transform.SetParent(parent, false);
            Image image = panelObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return panelObject.GetComponent<RectTransform>();
        }

        private static void Stretch(
            RectTransform rect,
            float left,
            float bottom,
            float right,
            float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void AnchorAtTop(
            RectTransform rect,
            float height,
            Vector2 position)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(0f, height);
        }

        private static void AnchorAtRight(
            RectTransform rect,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetBottomAnchoredRange(
            RectTransform rect,
            float anchorMinX,
            float anchorMaxX,
            float bottom,
            float height,
            float leftInset,
            float rightInset)
        {
            rect.anchorMin = new Vector2(anchorMinX, 0f);
            rect.anchorMax = new Vector2(anchorMaxX, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(
                (leftInset - rightInset) * 0.5f,
                bottom);
            rect.sizeDelta = new Vector2(
                -leftInset - rightInset,
                height);
        }

        private static void AnchorAtCenter(
            RectTransform rect,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void AnchorAtBottomCenter(
            RectTransform rect,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void ValidateIndex(
            int index,
            int count,
            string parameterName)
        {
            if (index < 0 || index >= count)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
