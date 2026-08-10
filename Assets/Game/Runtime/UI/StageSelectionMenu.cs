using System;
using System.Collections;
using RuleforgeTD.GameLogic.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Main-title and campaign-map flow. The campaign presents fifteen nodes,
    /// while only the first three are playable in the demo build.
    /// </summary>
    public sealed class StageSelectionMenu : MonoBehaviour
    {
        private const float CampaignTransitionDuration = 0.26f;

        private static readonly Color Ink =
            new Color32(20, 26, 27, 255);
        private static readonly Color DeepInk =
            new Color32(12, 17, 19, 255);
        private static readonly Color Ivory =
            new Color32(244, 236, 210, 255);
        private static readonly Color Gold =
            new Color32(244, 194, 78, 255);
        private static readonly Color Cyan =
            new Color32(112, 220, 217, 255);
        private static readonly Color Muted =
            new Color32(188, 192, 174, 255);
        private static readonly Color Panel =
            new Color32(19, 27, 28, 235);
        private static readonly Color PanelSoft =
            new Color32(27, 37, 37, 217);

        private static readonly Vector2[] StagePositions =
        {
            new Vector2(0.090f, 0.180f),
            new Vector2(0.105f, 0.295f),
            new Vector2(0.195f, 0.255f),
            new Vector2(0.275f, 0.265f),
            new Vector2(0.355f, 0.340f),
            new Vector2(0.395f, 0.455f),
            new Vector2(0.495f, 0.470f),
            new Vector2(0.580f, 0.420f),
            new Vector2(0.630f, 0.265f),
            new Vector2(0.725f, 0.235f),
            new Vector2(0.820f, 0.255f),
            new Vector2(0.875f, 0.360f),
            new Vector2(0.880f, 0.510f),
            new Vector2(0.820f, 0.630f),
            new Vector2(0.885f, 0.720f)
        };

        private static bool openMapOnNextLoad;

        [SerializeField]
        private TextAsset textData;

        [SerializeField]
        private TextAsset battleTextData;

        [SerializeField]
        private TextAsset stageOneContent;

        [SerializeField]
        private TextAsset stageTwoContent;

        [SerializeField]
        private TextAsset stageThreeContent;

        [SerializeField]
        private Font uiFont;

        [SerializeField]
        private Sprite worldMapBackground;

        [SerializeField]
        private string stageOneSceneName = "Stage01";

        [SerializeField]
        private string stageTwoSceneName = "Stage02";

        [SerializeField]
        private string stageThreeSceneName = "Stage03";

        private MainMenuTextDto copy;
        private GameObject titlePage;
        private GameObject campaignPage;
        private StageOneBlueprintGridGraphic campaignRevealGraphic;
        private CanvasGroup campaignInterfaceGroup;
        private GameObject stageLaunchWipe;
        private StageOneBlueprintGridGraphic stageLaunchWipeGraphic;
        private StageMapRouteGraphic routeGraphic;
        private StageNodeRuntime[] stageNodes =
            Array.Empty<StageNodeRuntime>();
        private Text progressText;
        private Text selectedNumberText;
        private Text selectedTitleText;
        private Text selectedDescriptionText;
        private Text selectedStartingCardsText;
        private Text selectedStateText;
        private Text actionText;
        private Text loadingText;
        private Button actionButton;
        private Button gameGuideButton;
        private GameGuideModal gameGuide;
        private int selectedStageNumber = 1;
        private bool loading;
        private bool campaignTransitionRunning;
        private Coroutine campaignTransitionRoutine;
        private StageOneUiTextCatalog battleTextCatalog;
        private string[][] stageStartingCardIds =
            Array.Empty<string[]>();

        public TextAsset TextData => textData;
        public TextAsset BattleTextData => battleTextData;
        public TextAsset StageOneContent => stageOneContent;
        public TextAsset StageTwoContent => stageTwoContent;
        public TextAsset StageThreeContent => stageThreeContent;
        public Font UiFont => uiFont;
        public Sprite WorldMapBackground => worldMapBackground;
        public string StageOneSceneName => stageOneSceneName;
        public string StageTwoSceneName => stageTwoSceneName;
        public string StageThreeSceneName => stageThreeSceneName;
        public int DisplayedStageCount =>
            CampaignStageProgress.DisplayedStageCount;
        public Button GameGuideButton => gameGuideButton;
        public GameGuideModal GameGuide => gameGuide;

        public static void RequestMapOnNextLoad()
        {
            openMapOnNextLoad = true;
        }

        public void ConfigureAuthoring(
            TextAsset sourceTextData,
            TextAsset sourceBattleTextData,
            TextAsset firstStageContent,
            TextAsset secondStageContent,
            TextAsset thirdStageContent,
            Font sourceFont,
            Sprite sourceWorldMapBackground,
            string firstStageScene,
            string secondStageScene,
            string thirdStageScene)
        {
            textData = sourceTextData;
            battleTextData = sourceBattleTextData;
            stageOneContent = firstStageContent;
            stageTwoContent = secondStageContent;
            stageThreeContent = thirdStageContent;
            uiFont = sourceFont;
            worldMapBackground = sourceWorldMapBackground;
            stageOneSceneName = firstStageScene ?? string.Empty;
            stageTwoSceneName = secondStageScene ?? string.Empty;
            stageThreeSceneName = thirdStageScene ?? string.Empty;
        }

        private void Awake()
        {
            Time.timeScale = 1f;
            copy = LoadCopy(textData);
            if (battleTextData == null)
            {
                throw new InvalidOperationException(
                    "Battle localization is required by the campaign menu.");
            }
            battleTextCatalog =
                StageOneUiTextCatalog.Load(battleTextData);
            stageStartingCardIds = new[]
            {
                LoadStartingCards(stageOneContent, "Stage01"),
                LoadStartingCards(stageTwoContent, "Stage02"),
                LoadStartingCards(stageThreeContent, "Stage03")
            };
            ValidateDistinctStarterLoadouts(stageStartingCardIds);
            if (uiFont == null)
            {
                uiFont = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            }

            BuildInterface();
            bool shouldOpenMap = openMapOnNextLoad;
            openMapOnNextLoad = false;
            if (shouldOpenMap)
            {
                ShowCampaignMap();
            }
            else
            {
                ShowTitlePage();
            }
        }

        private void Update()
        {
            if (loading || campaignTransitionRunning)
            {
                return;
            }

            if (gameGuide != null && gameGuide.IsOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    gameGuide.Close();
                }

                return;
            }

            if (titlePage.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.Return) ||
                    Input.GetKeyDown(KeyCode.Space))
                {
                    ShowCampaignMap();
                }

                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ShowTitlePage();
                return;
            }

            for (int stage = 1;
                 stage <= CampaignStageProgress.DemoStageCount;
                 stage++)
            {
                KeyCode topRow = (KeyCode)((int)KeyCode.Alpha0 + stage);
                KeyCode keypad = (KeyCode)((int)KeyCode.Keypad0 + stage);
                if (Input.GetKeyDown(topRow) ||
                    Input.GetKeyDown(keypad))
                {
                    SelectStage(stage);
                    if (CampaignStageProgress.IsUnlocked(stage))
                    {
                        BeginLoadSelectedStage();
                    }

                    return;
                }
            }
        }

        private void BuildInterface()
        {
            EnsureEventSystem();
            gameGuide = GetComponent<GameGuideModal>();
            if (gameGuide == null)
            {
                gameGuide = gameObject.AddComponent<GameGuideModal>();
            }
            gameGuide.Initialize(uiFont);

            GameObject canvasObject = new GameObject(
                "Campaign Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.sortingOrder = 100;
            CanvasScaler scaler =
                canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            titlePage = BuildTitlePage(canvasObject.transform);
            campaignPage = BuildCampaignPage(canvasObject.transform);
            stageLaunchWipe = BuildStageLaunchWipe(
                canvasObject.transform);
            SetCampaignTransitionState(0f, false);
            campaignPage.SetActive(false);
            titlePage.SetActive(true);
        }

        private GameObject BuildTitlePage(Transform parent)
        {
            RectTransform root = CreateImagePanel(
                "Main Title Page",
                parent,
                worldMapBackground,
                Color.white);
            Stretch(root, 0f, 0f, 0f, 0f);

            RectTransform shade = CreatePanel(
                "Title Shade",
                root,
                new Color32(8, 14, 16, 194));
            Stretch(shade, 0f, 0f, 0f, 0f);

            RectTransform upperShade = CreatePanel(
                "Upper Vignette",
                shade,
                new Color32(6, 12, 14, 82));
            upperShade.anchorMin = new Vector2(0f, 0.56f);
            upperShade.anchorMax = Vector2.one;
            upperShade.offsetMin = Vector2.zero;
            upperShade.offsetMax = Vector2.zero;

            Text eyebrow = CreateText(
                "Title Eyebrow",
                shade,
                copy.eyebrow,
                19,
                FontStyle.Bold,
                Gold,
                TextAnchor.MiddleCenter);
            SetRect(
                eyebrow.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 168f),
                new Vector2(1240f, 42f));

            Text title = CreateText(
                "Game Title",
                shade,
                string.IsNullOrWhiteSpace(copy.title)
                    ? GameMetadata.DefaultGameTitle
                    : copy.title,
                72,
                FontStyle.Bold,
                Ivory,
                TextAnchor.MiddleCenter);
            SetRect(
                title.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 90f),
                new Vector2(1120f, 112f));

            Text tagline = CreateText(
                "Tagline",
                shade,
                copy.tagline,
                22,
                FontStyle.Normal,
                Ivory,
                TextAnchor.MiddleCenter);
            SetRect(
                tagline.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 15f),
                new Vector2(1320f, 52f));

            Button startButton = CreateButton(
                "Open Campaign Map",
                shade,
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -92f),
                new Vector2(330f, 72f),
                Gold,
                Ink);
            startButton.onClick.AddListener(ShowCampaignMap);
            Text buttonLabel = CreateText(
                "Label",
                startButton.transform,
                copy.mainAction,
                24,
                FontStyle.Bold,
                RuleforgePixelUi.ParchmentText,
                TextAnchor.MiddleCenter);
            Stretch(buttonLabel.rectTransform, 16f, 5f, 16f, 5f);

            gameGuideButton = CreateButton(
                "Open Game Guide",
                shade,
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -166f),
                new Vector2(252f, 48f),
                new Color32(50, 64, 62, 255),
                Ivory);
            gameGuideButton.onClick.AddListener(gameGuide.Open);
            Text guideLabel = CreateText(
                "Label",
                gameGuideButton.transform,
                gameGuide.Catalog.Title,
                18,
                FontStyle.Bold,
                Ivory,
                TextAnchor.MiddleCenter);
            Stretch(guideLabel.rectTransform, 12f, 4f, 12f, 4f);

            Text footer = CreateText(
                "Title Footer",
                shade,
                copy.mainHint,
                16,
                FontStyle.Normal,
                Muted,
                TextAnchor.MiddleCenter);
            SetRect(
                footer.rectTransform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 38f),
                new Vector2(920f, 30f));
            return root.gameObject;
        }

        private GameObject BuildCampaignPage(Transform parent)
        {
            var rootObject = new GameObject(
                "Campaign Map Page",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(StageOneBlueprintGridGraphic),
                typeof(Mask));
            rootObject.transform.SetParent(parent, false);
            RectTransform root =
                rootObject.GetComponent<RectTransform>();
            Stretch(root, 0f, 0f, 0f, 0f);

            campaignRevealGraphic =
                rootObject.GetComponent<StageOneBlueprintGridGraphic>();
            campaignRevealGraphic.Configure(
                Color.white,
                Color.white,
                Color.white,
                24f);
            campaignRevealGraphic.raycastTarget = false;
            campaignRevealGraphic.SetRevealProgress(0f);
            rootObject.GetComponent<Mask>().showMaskGraphic = false;

            RectTransform content = CreateImagePanel(
                "Campaign Map Content",
                root,
                worldMapBackground,
                Color.white);
            Stretch(content, 0f, 0f, 0f, 0f);

            RectTransform wash = CreatePanel(
                "Map Color Wash",
                content,
                new Color32(15, 23, 27, 34));
            Stretch(wash, 0f, 0f, 0f, 0f);

            var interfaceObject = new GameObject(
                "Campaign Interface",
                typeof(RectTransform),
                typeof(CanvasGroup));
            interfaceObject.transform.SetParent(content, false);
            RectTransform interfaceRoot =
                interfaceObject.GetComponent<RectTransform>();
            Stretch(interfaceRoot, 0f, 0f, 0f, 0f);
            campaignInterfaceGroup =
                interfaceObject.GetComponent<CanvasGroup>();
            campaignInterfaceGroup.alpha = 0f;
            campaignInterfaceGroup.interactable = false;
            campaignInterfaceGroup.blocksRaycasts = false;

            RectTransform routeObject = new GameObject(
                "Campaign Route",
                typeof(RectTransform),
                typeof(StageMapRouteGraphic))
                .GetComponent<RectTransform>();
            routeObject.SetParent(interfaceRoot, false);
            Stretch(routeObject, 0f, 0f, 0f, 0f);
            routeGraphic =
                routeObject.GetComponent<StageMapRouteGraphic>();
            routeGraphic.raycastTarget = false;
            routeGraphic.Configure(
                StagePositions,
                CampaignStageProgress.HighestUnlockedStage);

            BuildStageNodes(interfaceRoot);
            BuildCampaignHeader(interfaceRoot);
            BuildStageDetails(interfaceRoot);
            return rootObject;
        }

        private GameObject BuildStageLaunchWipe(Transform parent)
        {
            var wipeObject = new GameObject(
                "Stage Launch Wipe",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(StageOneBlueprintGridGraphic));
            wipeObject.transform.SetParent(parent, false);
            RectTransform wipeRoot =
                wipeObject.GetComponent<RectTransform>();
            Stretch(wipeRoot, 0f, 0f, 0f, 0f);
            stageLaunchWipeGraphic =
                wipeObject.GetComponent<StageOneBlueprintGridGraphic>();
            stageLaunchWipeGraphic.Configure(
                DeepInk,
                new Color32(45, 58, 54, 105),
                new Color32(77, 91, 78, 142),
                24f);
            stageLaunchWipeGraphic.raycastTarget = true;
            stageLaunchWipeGraphic.SetRevealProgress(0f);
            wipeObject.SetActive(false);
            return wipeObject;
        }

        private void BuildCampaignHeader(Transform parent)
        {
            RectTransform header = CreatePanel(
                "Campaign Header",
                parent,
                new Color32(13, 19, 21, 226));
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.anchoredPosition = Vector2.zero;
            header.sizeDelta = new Vector2(0f, 84f);

            Button backButton = CreateButton(
                "Back To Title",
                header,
                new Vector2(0f, 0.5f),
                new Vector2(24f, 0f),
                new Vector2(172f, 44f),
                new Color32(50, 64, 62, 255),
                Ivory,
                new Vector2(0f, 0.5f));
            backButton.onClick.AddListener(ShowTitlePage);
            Text backLabel = CreateText(
                "Label",
                backButton.transform,
                copy.backToTitle,
                17,
                FontStyle.Bold,
                Ivory,
                TextAnchor.MiddleCenter);
            Stretch(backLabel.rectTransform, 8f, 3f, 8f, 3f);

            Text heading = CreateText(
                "Campaign Title",
                header,
                copy.campaignTitle,
                27,
                FontStyle.Bold,
                Ivory,
                TextAnchor.MiddleLeft);
            SetRect(
                heading.rectTransform,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(218f, 8f),
                new Vector2(500f, 42f),
                new Vector2(0f, 0.5f));

            Text subtitle = CreateText(
                "Campaign Subtitle",
                header,
                copy.campaignSubtitle,
                14,
                FontStyle.Normal,
                Muted,
                TextAnchor.MiddleLeft);
            SetRect(
                subtitle.rectTransform,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(218f, -24f),
                new Vector2(720f, 28f),
                new Vector2(0f, 0.5f));

            progressText = CreateText(
                "Campaign Progress",
                header,
                string.Empty,
                17,
                FontStyle.Bold,
                Gold,
                TextAnchor.MiddleRight);
            SetRect(
                progressText.rectTransform,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-36f, 0f),
                new Vector2(430f, 44f),
                new Vector2(1f, 0.5f));
        }

        private void BuildStageNodes(Transform parent)
        {
            stageNodes = new StageNodeRuntime[
                CampaignStageProgress.DisplayedStageCount];
            for (int index = 0; index < stageNodes.Length; index++)
            {
                int stageNumber = index + 1;
                var nodeObject = new GameObject(
                    "Stage Node " + stageNumber,
                    typeof(RectTransform),
                    typeof(StageMapNodeGraphic),
                    typeof(Button));
                nodeObject.transform.SetParent(parent, false);
                RectTransform rect =
                    nodeObject.GetComponent<RectTransform>();
                SetRect(
                    rect,
                    StagePositions[index],
                    StagePositions[index],
                    Vector2.zero,
                    new Vector2(66f, 66f));

                StageMapNodeGraphic graphic =
                    nodeObject.GetComponent<StageMapNodeGraphic>();
                Button button = nodeObject.GetComponent<Button>();
                button.targetGraphic = graphic;
                button.transition = Selectable.Transition.ColorTint;
                ColorBlock colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.16f, 1.16f, 1.16f, 1f);
                colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
                colors.selectedColor = Color.white;
                colors.colorMultiplier = 1f;
                button.colors = colors;
                button.onClick.AddListener(() => SelectStage(stageNumber));

                Text number = CreateText(
                    "Number",
                    nodeObject.transform,
                    stageNumber.ToString("00"),
                    21,
                    FontStyle.Bold,
                    Ivory,
                    TextAnchor.MiddleCenter);
                Stretch(number.rectTransform, 5f, 5f, 5f, 5f);

                Text stars = CreateText(
                    "Stars",
                    nodeObject.transform,
                    string.Empty,
                    15,
                    FontStyle.Bold,
                    Gold,
                    TextAnchor.MiddleCenter);
                SetRect(
                    stars.rectTransform,
                    new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f),
                    new Vector2(0f, 16f),
                    new Vector2(100f, 22f));

                Text label = CreateText(
                    "State Label",
                    nodeObject.transform,
                    string.Empty,
                    12,
                    FontStyle.Bold,
                    Ivory,
                    TextAnchor.MiddleCenter);
                SetRect(
                    label.rectTransform,
                    new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0f),
                    new Vector2(0f, -14f),
                    new Vector2(116f, 20f));

                stageNodes[index] = new StageNodeRuntime(
                    graphic,
                    number,
                    stars,
                    label,
                    button);
            }
        }

        private void BuildStageDetails(Transform parent)
        {
            RectTransform details = CreatePanel(
                "Selected Stage Details",
                parent,
                Panel);
            SetRect(
                details,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 24f),
                new Vector2(760f, 176f),
                new Vector2(0.5f, 0f));

            RectTransform accent = CreatePanel(
                "Accent",
                details,
                Gold);
            accent.anchorMin = new Vector2(0f, 0f);
            accent.anchorMax = new Vector2(0f, 1f);
            accent.pivot = new Vector2(0f, 0.5f);
            accent.sizeDelta = new Vector2(6f, 0f);
            accent.anchoredPosition = Vector2.zero;

            selectedNumberText = CreateText(
                "Selected Number",
                details,
                string.Empty,
                14,
                FontStyle.Bold,
                Cyan,
                TextAnchor.MiddleLeft);
            SetRectFromTopLeft(
                selectedNumberText.rectTransform,
                new Vector2(28f, -18f),
                new Vector2(410f, 24f));

            selectedTitleText = CreateText(
                "Selected Title",
                details,
                string.Empty,
                27,
                FontStyle.Bold,
                Ivory,
                TextAnchor.MiddleLeft);
            SetRectFromTopLeft(
                selectedTitleText.rectTransform,
                new Vector2(28f, -43f),
                new Vector2(450f, 38f));

            selectedDescriptionText = CreateText(
                "Selected Description",
                details,
                string.Empty,
                14,
                FontStyle.Normal,
                Muted,
                TextAnchor.UpperLeft);
            selectedDescriptionText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            SetRectFromTopLeft(
                selectedDescriptionText.rectTransform,
                new Vector2(28f, -87f),
                new Vector2(455f, 44f));

            selectedStartingCardsText = CreateText(
                "Selected Starting Cards",
                details,
                string.Empty,
                14,
                FontStyle.Bold,
                Gold,
                TextAnchor.MiddleLeft);
            selectedStartingCardsText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            SetRectFromTopLeft(
                selectedStartingCardsText.rectTransform,
                new Vector2(28f, -137f),
                new Vector2(455f, 28f));

            selectedStateText = CreateText(
                "Selected State",
                details,
                string.Empty,
                13,
                FontStyle.Bold,
                Gold,
                TextAnchor.MiddleCenter);
            SetRect(
                selectedStateText.rectTransform,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-127f, 105f),
                new Vector2(220f, 24f),
                new Vector2(1f, 0f));

            actionButton = CreateButton(
                "Launch Selected Stage",
                details,
                new Vector2(1f, 0f),
                new Vector2(-26f, 28f),
                new Vector2(204f, 60f),
                Gold,
                Ink,
                new Vector2(1f, 0f));
            actionButton.onClick.AddListener(BeginLoadSelectedStage);
            actionText = CreateText(
                "Label",
                actionButton.transform,
                string.Empty,
                19,
                FontStyle.Bold,
                RuleforgePixelUi.ParchmentText,
                TextAnchor.MiddleCenter);
            Stretch(actionText.rectTransform, 10f, 4f, 10f, 4f);

            loadingText = CreateText(
                "Loading Status",
                parent,
                string.Empty,
                17,
                FontStyle.Bold,
                Ivory,
                TextAnchor.MiddleCenter);
            SetRect(
                loadingText.rectTransform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 174f),
                new Vector2(720f, 30f),
                new Vector2(0.5f, 0f));
        }

        private void ShowTitlePage()
        {
            if (loading || campaignTransitionRunning)
            {
                return;
            }

            if (campaignPage.activeSelf)
            {
                BeginCampaignTransition(false);
                return;
            }

            titlePage.SetActive(true);
            campaignPage.SetActive(false);
            SetCampaignTransitionState(0f, false);
        }

        private void ShowCampaignMap()
        {
            if (loading || campaignTransitionRunning)
            {
                return;
            }

            selectedStageNumber = Mathf.Clamp(
                CampaignStageProgress.HighestUnlockedStage,
                1,
                CampaignStageProgress.DemoStageCount);
            RefreshCampaignState();
            BeginCampaignTransition(true);
        }

        private void BeginCampaignTransition(bool showing)
        {
            if (campaignTransitionRoutine != null)
            {
                StopCoroutine(campaignTransitionRoutine);
                campaignTransitionRoutine = null;
            }

            titlePage.SetActive(true);
            campaignPage.SetActive(true);
            campaignTransitionRoutine = StartCoroutine(
                RunCampaignTransition(showing));
        }

        private IEnumerator RunCampaignTransition(bool showing)
        {
            campaignTransitionRunning = true;
            float start = campaignRevealGraphic.RevealProgress;
            float end = showing ? 1f : 0f;
            float duration = Mathf.Max(
                0.04f,
                CampaignTransitionDuration * Mathf.Abs(end - start));
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float eased = normalized * normalized *
                              (3f - 2f * normalized);
                SetCampaignTransitionState(
                    Mathf.Lerp(start, end, eased),
                    false);
                yield return null;
            }

            SetCampaignTransitionState(end, showing);
            campaignTransitionRunning = false;
            campaignTransitionRoutine = null;
            if (showing)
            {
                titlePage.SetActive(false);
            }
            else
            {
                campaignPage.SetActive(false);
            }
        }

        private void SetCampaignTransitionState(
            float reveal,
            bool fullyInteractive)
        {
            float progress = Mathf.Clamp01(reveal);
            campaignRevealGraphic.SetRevealProgress(progress);
            float interfaceProgress = Mathf.InverseLerp(
                0.36f,
                0.9f,
                progress);
            campaignInterfaceGroup.alpha =
                interfaceProgress * interfaceProgress *
                (3f - 2f * interfaceProgress);
            bool interactive =
                fullyInteractive && progress >= 0.999f;
            campaignInterfaceGroup.interactable = interactive;
            campaignInterfaceGroup.blocksRaycasts = interactive;
        }

        private void SelectStage(int stageNumber)
        {
            if (loading ||
                stageNumber < 1 ||
                stageNumber > CampaignStageProgress.DisplayedStageCount)
            {
                return;
            }

            selectedStageNumber = stageNumber;
            RefreshCampaignState();
        }

        private void RefreshCampaignState()
        {
            int highestUnlocked =
                CampaignStageProgress.HighestUnlockedStage;
            routeGraphic.Configure(StagePositions, highestUnlocked);
            int clearedCount = 0;
            for (int index = 0; index < stageNodes.Length; index++)
            {
                int stageNumber = index + 1;
                StageMapNodeState state = ResolveState(stageNumber);
                if (state == StageMapNodeState.Cleared)
                {
                    clearedCount++;
                }

                StageNodeRuntime node = stageNodes[index];
                node.Graphic.SetState(
                    state,
                    stageNumber == selectedStageNumber);
                node.Number.color =
                    state == StageMapNodeState.ComingSoon ||
                    state == StageMapNodeState.Locked
                        ? new Color32(154, 150, 145, 255)
                        : Ivory;
                node.Stars.text = string.Empty;
                node.StateLabel.text = ResolveNodeLabel(state);
                node.Button.interactable = !loading;
            }

            progressText.text = string.Format(
                copy.progressFormat,
                highestUnlocked,
                CampaignStageProgress.DisplayedStageCount,
                clearedCount,
                CampaignStageProgress.DemoStageCount);

            StageTextDto selected =
                copy.stages[selectedStageNumber - 1];
            StageMapNodeState selectedState =
                ResolveState(selectedStageNumber);
            selectedNumberText.text =
                "STAGE " + selectedStageNumber.ToString("00");
            selectedTitleText.text = selected.title;
            selectedDescriptionText.text = selected.description;
            selectedStartingCardsText.text =
                BuildStartingCardSummary(selectedStageNumber);
            selectedStateText.text = ResolveDetailState(selectedState);
            bool playable =
                selectedState == StageMapNodeState.Unlocked ||
                selectedState == StageMapNodeState.Cleared;
            actionButton.interactable = playable && !loading;
            actionText.text = playable
                ? copy.playAction
                : selectedState == StageMapNodeState.ComingSoon
                    ? copy.comingSoonAction
                    : copy.lockedAction;
        }

        private StageMapNodeState ResolveState(int stageNumber)
        {
            if (stageNumber > CampaignStageProgress.DemoStageCount)
            {
                return StageMapNodeState.ComingSoon;
            }

            if (CampaignStageProgress.IsCleared(stageNumber))
            {
                return StageMapNodeState.Cleared;
            }

            return CampaignStageProgress.IsUnlocked(stageNumber)
                ? StageMapNodeState.Unlocked
                : StageMapNodeState.Locked;
        }

        private string ResolveNodeLabel(StageMapNodeState state)
        {
            switch (state)
            {
                case StageMapNodeState.Cleared:
                    return copy.clearedNode;
                case StageMapNodeState.Unlocked:
                    return copy.unlockedNode;
                case StageMapNodeState.ComingSoon:
                    return copy.comingSoonNode;
                default:
                    return copy.lockedNode;
            }
        }

        private string ResolveDetailState(StageMapNodeState state)
        {
            switch (state)
            {
                case StageMapNodeState.Cleared:
                    return copy.clearedDetail;
                case StageMapNodeState.Unlocked:
                    return copy.unlockedDetail;
                case StageMapNodeState.ComingSoon:
                    return copy.comingSoonDetail;
                default:
                    return copy.lockedDetail;
            }
        }

        private void BeginLoadSelectedStage()
        {
            if (loading ||
                !CampaignStageProgress.IsUnlocked(selectedStageNumber))
            {
                return;
            }

            string sceneName = GetSceneName(selectedStageNumber);
            if (string.IsNullOrWhiteSpace(sceneName) ||
                !Application.CanStreamedLevelBeLoaded(sceneName))
            {
                return;
            }

            loading = true;
            loadingText.text = copy.loading;
            for (int index = 0; index < stageNodes.Length; index++)
            {
                stageNodes[index].Button.interactable = false;
            }

            actionButton.interactable = false;
            StartCoroutine(RunStageLaunchTransition(sceneName));
        }

        private IEnumerator RunStageLaunchTransition(string sceneName)
        {
            stageLaunchWipe.SetActive(true);
            stageLaunchWipeGraphic.SetRevealProgress(0f);
            float elapsed = 0f;
            while (elapsed < CampaignTransitionDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(
                    elapsed / CampaignTransitionDuration);
                float eased = normalized * normalized *
                              (3f - 2f * normalized);
                stageLaunchWipeGraphic.SetRevealProgress(eased);
                yield return null;
            }

            stageLaunchWipeGraphic.SetRevealProgress(1f);
            yield return LoadScene(sceneName);
        }

        private string GetSceneName(int stageNumber)
        {
            switch (stageNumber)
            {
                case 1:
                    return stageOneSceneName;
                case 2:
                    return stageTwoSceneName;
                case 3:
                    return stageThreeSceneName;
                default:
                    return string.Empty;
            }
        }

        private string BuildStartingCardSummary(int stageNumber)
        {
            int index = stageNumber - 1;
            if (index < 0 || index >= stageStartingCardIds.Length)
            {
                return string.Empty;
            }

            string[] ids = stageStartingCardIds[index];
            var names = new string[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                names[i] = battleTextCatalog.GetCardName(ids[i]);
            }

            return string.Format(
                copy.startingCardsFormat,
                string.Join(" · ", names));
        }

        private static IEnumerator LoadScene(string sceneName)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
            while (operation != null && !operation.isDone)
            {
                yield return null;
            }
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            string value,
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
            text.text = value ?? string.Empty;
            RuleforgeUiTypography.Configure(
                text,
                uiFont,
                fontSize,
                color,
                alignment,
                RuleforgeUiTypography.IsLight(color));
            return text;
        }

        private static RectTransform CreateImagePanel(
            string objectName,
            Transform parent,
            Sprite sprite,
            Color color)
        {
            var panelObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image));
            panelObject.transform.SetParent(parent, false);
            Image image = panelObject.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = false;
            return panelObject.GetComponent<RectTransform>();
        }

        private static RectTransform CreatePanel(
            string objectName,
            Transform parent,
            Color color)
        {
            return CreateImagePanel(
                objectName,
                parent,
                null,
                color);
        }

        private static Button CreateButton(
            string objectName,
            Transform parent,
            Vector2 anchor,
            Vector2 position,
            Vector2 size,
            Color normal,
            Color textColor,
            Vector2? pivot = null)
        {
            var buttonObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect =
                buttonObject.GetComponent<RectTransform>();
            SetRect(
                rect,
                anchor,
                anchor,
                position,
                size,
                pivot ?? new Vector2(0.5f, 0.5f));
            Image image = buttonObject.GetComponent<Image>();
            image.color = normal;
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color32(84, 86, 82, 190);
            colors.colorMultiplier = 1f;
            button.colors = colors;
            RuleforgePixelUi.ApplyLegacyColor(button, normal);
            return button;
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 size,
            Vector2? pivot = null)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
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
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void SetRectFromTopLeft(
            RectTransform rect,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var eventSystemObject = new GameObject(
                "Campaign Event System",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            eventSystemObject.transform.SetParent(transform, false);
        }

        private static MainMenuTextDto LoadCopy(TextAsset source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.text))
            {
                throw new InvalidOperationException(
                    "Main menu localization data is required.");
            }

            MainMenuTextDto result =
                JsonUtility.FromJson<MainMenuTextDto>(source.text);
            if (result == null ||
                result.stages == null ||
                result.stages.Length !=
                    CampaignStageProgress.DisplayedStageCount ||
                string.IsNullOrWhiteSpace(result.startingCardsFormat))
            {
                throw new InvalidOperationException(
                    "Campaign localization must define exactly fifteen stages.");
            }

            for (int index = 0; index < result.stages.Length; index++)
            {
                if (result.stages[index] == null ||
                    string.IsNullOrWhiteSpace(result.stages[index].title))
                {
                    throw new InvalidOperationException(
                        "Campaign localization has an incomplete stage entry.");
                }
            }

            return result;
        }

        private static string[] LoadStartingCards(
            TextAsset content,
            string stageLabel)
        {
            if (content == null || string.IsNullOrWhiteSpace(content.text))
            {
                throw new InvalidOperationException(
                    stageLabel + " content is required by the campaign menu.");
            }

            StageContentDto dto =
                JsonUtility.FromJson<StageContentDto>(content.text);
            if (dto == null || dto.run == null ||
                dto.run.startingCards == null ||
                dto.run.startingCards.Length == 0)
            {
                throw new InvalidOperationException(
                    stageLabel + " must define starting cards.");
            }

            for (int i = 0; i < dto.run.startingCards.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(dto.run.startingCards[i]))
                {
                    throw new InvalidOperationException(
                        stageLabel + " has an empty starting-card id.");
                }
            }

            return (string[])dto.run.startingCards.Clone();
        }

        private static void ValidateDistinctStarterLoadouts(
            string[][] loadouts)
        {
            if (loadouts == null ||
                loadouts.Length != CampaignStageProgress.DemoStageCount)
            {
                throw new InvalidOperationException(
                    "The campaign menu needs one starter loadout per demo stage.");
            }

            for (int left = 0; left < loadouts.Length; left++)
            {
                for (int right = left + 1;
                     right < loadouts.Length;
                     right++)
                {
                    if (ArraysEqual(loadouts[left], loadouts[right]))
                    {
                        throw new InvalidOperationException(
                            "Demo stages " +
                            (left + 1) +
                            " and " +
                            (right + 1) +
                            " cannot use the same starter loadout.");
                    }
                }
            }
        }

        private static bool ArraysEqual(string[] left, string[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (!string.Equals(
                        left[i],
                        right[i],
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class StageNodeRuntime
        {
            public StageNodeRuntime(
                StageMapNodeGraphic graphic,
                Text number,
                Text stars,
                Text stateLabel,
                Button button)
            {
                Graphic = graphic;
                Number = number;
                Stars = stars;
                StateLabel = stateLabel;
                Button = button;
            }

            public StageMapNodeGraphic Graphic { get; }
            public Text Number { get; }
            public Text Stars { get; }
            public Text StateLabel { get; }
            public Button Button { get; }
        }

        [Serializable]
        private sealed class MainMenuTextDto
        {
            public string title;
            public string eyebrow;
            public string tagline;
            public string mainAction;
            public string mainHint;
            public string campaignTitle;
            public string campaignSubtitle;
            public string backToTitle;
            public string progressFormat;
            public string playAction;
            public string lockedAction;
            public string comingSoonAction;
            public string clearedNode;
            public string unlockedNode;
            public string lockedNode;
            public string comingSoonNode;
            public string clearedDetail;
            public string unlockedDetail;
            public string lockedDetail;
            public string comingSoonDetail;
            public string loading;
            public string returnToSelection;
            public string startingCardsFormat;
            public StageTextDto[] stages;
        }

        [Serializable]
        private sealed class StageTextDto
        {
            public string title;
            public string description;
        }

        [Serializable]
        private sealed class StageContentDto
        {
            public StageRunDto run;
        }

        [Serializable]
        private sealed class StageRunDto
        {
            public string[] startingCards;
        }
    }
}
