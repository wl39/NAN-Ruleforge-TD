using System;
using RuleforgeTD.Audio;
using RuleforgeTD.Battle;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// 전투 화면의 우측 상단 설정 UI를 만든다. 설정 진입은 톱니바퀴
    /// 아이콘으로 표현하며 음량과 현재 런 이탈처럼 전역 동작만 제공한다.
    /// </summary>
    public sealed class StageSelectionReturnButton : MonoBehaviour
    {
        private const string MainMenuSceneName = "MainMenu";
        private const string LocalizationResourcePath =
            "RuleforgeTD/MainMenuKo";

        private static readonly Color PanelColor =
            new Color32(238, 225, 184, 255);
        private static readonly Color TextColor =
            new Color32(238, 232, 205, 255);
        private static readonly Color InkColor =
            new Color32(45, 35, 25, 255);
        private static readonly Color MutedInkColor =
            new Color32(91, 75, 57, 255);
        private static readonly Color TrackColor =
            new Color32(45, 29, 20, 255);
        private static readonly Color VolumeColor =
            new Color32(255, 197, 62, 255);
        private static readonly Color HandleColor =
            new Color32(255, 232, 145, 255);
        private static readonly Color HandleBorderColor =
            new Color32(38, 24, 16, 255);

        private SettingsTextDto copy;
        private GameObject settingsPanel;
        private GameObject confirmationOverlay;
        private RectTransform confirmationDialog;
        private Button settingsButton;
        private Button stageSelectionButton;
        private Button speakerButton;
        private Button cancelButton;
        private Button confirmButton;
        private Slider volumeSlider;
        private RuleforgeSettingsIconGraphic settingsIcon;
        private RuleforgeSettingsIconGraphic speakerIcon;
        private int lastLayoutWidth = -1;
        private int lastLayoutHeight = -1;

        public Button SettingsButton => settingsButton;
        public Button StageSelectionButton => stageSelectionButton;
        public Button SpeakerButton => speakerButton;
        public Button CancelButton => cancelButton;
        public Button ConfirmButton => confirmButton;
        public Slider VolumeSlider => volumeSlider;
        public RuleforgeSettingsIconGraphic SettingsIcon => settingsIcon;
        public RuleforgeSettingsIconGraphic SpeakerIcon => speakerIcon;
        public bool IsMenuOpen =>
            settingsPanel != null && settingsPanel.activeSelf;
        public bool IsConfirmationOpen =>
            confirmationOverlay != null &&
            confirmationOverlay.activeSelf;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneCallback()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(
            Scene scene,
            LoadSceneMode mode)
        {
            if (!IsBattleScene(scene.name) ||
                FindObjectOfType<StageSelectionReturnButton>() != null)
            {
                return;
            }

            var root = new GameObject("Battle Settings Navigation");
            root.AddComponent<StageSelectionReturnButton>();
        }

        private void Awake()
        {
            copy = LoadCopy();
            BuildInterface();
        }

        private void OnDestroy()
        {
            RemoveListeners();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (IsConfirmationOpen)
                {
                    CancelStageSelection();
                }
                else
                {
                    ToggleSettingsMenu();
                }
            }

            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            if (width != lastLayoutWidth || height != lastLayoutHeight)
            {
                ApplyResponsiveLayout();
            }
        }

        public void SetMenuOpen(bool open)
        {
            if (settingsPanel == null)
            {
                return;
            }

            settingsPanel.SetActive(open);
            if (open)
            {
                SetConfirmationOpen(false);
                RefreshVolumeControls();
                SelectControl(speakerButton);
            }
        }

        private void ToggleSettingsMenu()
        {
            SetMenuOpen(!IsMenuOpen);
        }

        private void ShowStageSelectionConfirmation()
        {
            settingsPanel.SetActive(false);
            SetConfirmationOpen(true);
            SelectControl(cancelButton);
        }

        private void CancelStageSelection()
        {
            SetConfirmationOpen(false);
            settingsPanel.SetActive(true);
            RefreshVolumeControls();
            SelectControl(stageSelectionButton);
        }

        private void SetConfirmationOpen(bool open)
        {
            if (confirmationOverlay != null)
            {
                confirmationOverlay.SetActive(open);
            }
        }

        private void ToggleMute()
        {
            RuleforgeAudioService.ToggleMuted();
            RefreshVolumeControls();
        }

        private void HandleVolumeChanged(float value)
        {
            RuleforgeAudioService.SetVolume(value);
            RefreshVolumeVisuals();
        }

        private void RefreshVolumeControls()
        {
            if (volumeSlider != null)
            {
                volumeSlider.SetValueWithoutNotify(
                    RuleforgeAudioService.IsMuted
                        ? 0f
                        : RuleforgeAudioService.GameVolume);
            }

            RefreshVolumeVisuals();
        }

        private void RefreshVolumeVisuals()
        {
            bool muted = RuleforgeAudioService.IsMuted;
            float volume = RuleforgeAudioService.GameVolume;
            if (speakerIcon != null)
            {
                speakerIcon.SetMode(
                    muted || volume <= 0.0001f
                        ? RuleforgeSettingsIconMode.SpeakerMuted
                        : RuleforgeSettingsIconMode.Speaker);
            }
        }

        private void BuildInterface()
        {
            Font font = LoadFont();
            RectTransform safeArea = CreateCanvasAndSafeArea();

            settingsButton = CreateIconButton(
                "Settings Close Button",
                safeArea,
                new Vector2(-16f, -58f),
                new Vector2(44f, 44f),
                RuleforgeExactButtonAsset.Square44,
                out settingsIcon);
            settingsIcon.SetMode(RuleforgeSettingsIconMode.Gear);
            settingsButton.onClick.AddListener(ToggleSettingsMenu);

            BuildSettingsPanel(safeArea, font);
            BuildConfirmation(safeArea, font);

            settingsPanel.SetActive(false);
            confirmationOverlay.SetActive(false);
            RefreshVolumeControls();
            ApplyResponsiveLayout(true);
            EnsureEventSystem(transform);
        }

        private RectTransform CreateCanvasAndSafeArea()
        {
            GameObject canvasObject = new GameObject(
                "Battle Settings Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.sortingOrder = 850;

            CanvasScaler scaler =
                canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<StageOneResponsiveCanvasScaler>();

            RectTransform safeArea = new GameObject(
                "Safe Area",
                typeof(RectTransform),
                typeof(StageOneSafeAreaFitter))
                .GetComponent<RectTransform>();
            safeArea.SetParent(canvasObject.transform, false);
            Stretch(safeArea);
            return safeArea;
        }

        private void BuildSettingsPanel(RectTransform safeArea, Font font)
        {
            settingsPanel = CreatePanel(
                "Settings Menu",
                safeArea,
                new Vector2(280f, 184f));
            RectTransform panelRect =
                settingsPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.one;
            panelRect.anchorMax = Vector2.one;
            panelRect.pivot = Vector2.one;
            panelRect.anchoredPosition = new Vector2(-16f, -110f);

            CreateText(
                "Settings Title",
                settingsPanel.transform,
                copy.settings,
                new Vector2(20f, -10f),
                new Vector2(240f, 26f),
                font,
                17,
                InkColor,
                TextAnchor.MiddleCenter);

            CreateText(
                "Game Volume Label",
                settingsPanel.transform,
                copy.gameVolume,
                new Vector2(20f, -40f),
                new Vector2(240f, 20f),
                font,
                13,
                MutedInkColor,
                TextAnchor.MiddleLeft);

            speakerButton = CreateIconButton(
                "Settings Speaker Button",
                settingsPanel.transform,
                new Vector2(18f, -67f),
                new Vector2(30f, 30f),
                RuleforgeExactButtonAsset.Square36,
                out speakerIcon,
                false);
            speakerButton.onClick.AddListener(ToggleMute);

            volumeSlider = CreateVolumeSlider(
                settingsPanel.transform,
                new Vector2(58f, -70f),
                new Vector2(160f, 24f));
            volumeSlider.onValueChanged.AddListener(
                HandleVolumeChanged);

            stageSelectionButton = CreateButton(
                "Settings Stage Selection Button",
                settingsPanel.transform,
                copy.returnToSelection,
                new Vector2(54f, -126f),
                new Vector2(172f, 44f),
                font,
                RuleforgePixelButtonRole.Secondary,
                TextColor,
                false,
                RuleforgeExactButtonAsset.Back172x44);
            stageSelectionButton.onClick.AddListener(
                ShowStageSelectionConfirmation);
            ConfigureNavigation();
        }

        private void BuildConfirmation(RectTransform safeArea, Font font)
        {
            confirmationOverlay = new GameObject(
                "Stage Selection Confirmation Overlay",
                typeof(RectTransform),
                typeof(Image));
            confirmationOverlay.transform.SetParent(safeArea, false);
            RectTransform overlayRect =
                confirmationOverlay.GetComponent<RectTransform>();
            Stretch(overlayRect);
            Image overlayImage =
                confirmationOverlay.GetComponent<Image>();
            overlayImage.color = new Color(0.035f, 0.04f, 0.035f, 0.76f);
            overlayImage.raycastTarget = true;

            GameObject dialog = CreatePanel(
                "Stage Selection Confirmation Dialog",
                confirmationOverlay.transform,
                new Vector2(450f, 220f));
            confirmationDialog = dialog.GetComponent<RectTransform>();
            confirmationDialog.anchorMin = new Vector2(0.5f, 0.5f);
            confirmationDialog.anchorMax = new Vector2(0.5f, 0.5f);
            confirmationDialog.pivot = new Vector2(0.5f, 0.5f);
            confirmationDialog.anchoredPosition = Vector2.zero;

            CreateText(
                "Confirmation Title",
                dialog.transform,
                copy.confirmTitle,
                new Vector2(30f, -20f),
                new Vector2(390f, 30f),
                font,
                18,
                InkColor,
                TextAnchor.MiddleCenter);
            CreateText(
                "Confirmation Message",
                dialog.transform,
                copy.confirmMessage,
                new Vector2(45f, -62f),
                new Vector2(360f, 65f),
                font,
                14,
                MutedInkColor,
                TextAnchor.MiddleCenter);

            cancelButton = CreateButton(
                "Cancel Stage Selection Button",
                dialog.transform,
                copy.cancel,
                new Vector2(45f, -158f),
                new Vector2(160f, 42f),
                font,
                RuleforgePixelButtonRole.Secondary,
                TextColor,
                false,
                RuleforgeExactButtonAsset.Back172x44);
            cancelButton.onClick.AddListener(CancelStageSelection);

            confirmButton = CreateButton(
                "Confirm Stage Selection Button",
                dialog.transform,
                copy.leaveStage,
                new Vector2(245f, -158f),
                new Vector2(160f, 42f),
                font,
                RuleforgePixelButtonRole.Danger,
                TextColor,
                false,
                RuleforgeExactButtonAsset.Back172x44);
            RuleforgePixelUi.ApplyExact(
                confirmButton,
                RuleforgeExactButtonAsset.Back172x44,
                RuleforgePixelButtonRole.Danger,
                new Color(1f, 0.82f, 0.76f, 1f));
            confirmButton.onClick.AddListener(ReturnToSelection);
        }

        private Slider CreateVolumeSlider(
            Transform parent,
            Vector2 position,
            Vector2 size)
        {
            GameObject sliderObject = new GameObject(
                "Game Volume Slider",
                typeof(RectTransform),
                typeof(Image),
                typeof(Slider));
            sliderObject.transform.SetParent(parent, false);
            RectTransform rect =
                sliderObject.GetComponent<RectTransform>();
            SetTopLeft(rect, position, size);
            Image hitArea = sliderObject.GetComponent<Image>();
            hitArea.color = new Color(1f, 1f, 1f, 0f);
            hitArea.raycastTarget = true;

            Image background = CreateImage(
                "Background",
                sliderObject.transform,
                TrackColor);
            RectTransform backgroundRect =
                background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(1f, 0.5f);
            backgroundRect.pivot = new Vector2(0.5f, 0.5f);
            backgroundRect.offsetMin = new Vector2(0f, -3.5f);
            backgroundRect.offsetMax = new Vector2(0f, 3.5f);

            RectTransform fillArea = new GameObject(
                "Fill Area",
                typeof(RectTransform))
                .GetComponent<RectTransform>();
            fillArea.SetParent(sliderObject.transform, false);
            fillArea.anchorMin = Vector2.zero;
            fillArea.anchorMax = Vector2.one;
            fillArea.offsetMin = new Vector2(3f, 10.5f);
            fillArea.offsetMax = new Vector2(-3f, -10.5f);

            Image fill = CreateImage(
                "Fill",
                fillArea,
                VolumeColor);
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            Stretch(fillRect);

            RectTransform handleArea = new GameObject(
                "Handle Slide Area",
                typeof(RectTransform))
                .GetComponent<RectTransform>();
            handleArea.SetParent(sliderObject.transform, false);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(5f, 0f);
            handleArea.offsetMax = new Vector2(-5f, 0f);

            GameObject handleObject = new GameObject(
                "Handle",
                typeof(RectTransform),
                typeof(RuleforgeSliderKnobGraphic));
            handleObject.transform.SetParent(handleArea, false);
            RectTransform handleRect =
                handleObject.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(10f, 10f);
            RuleforgeSliderKnobGraphic handle =
                handleObject.GetComponent<
                    RuleforgeSliderKnobGraphic>();
            handle.Configure(HandleBorderColor, HandleColor);
            handle.raycastTarget = false;

            Slider slider = sliderObject.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.direction = Slider.Direction.LeftToRight;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.value = RuleforgeAudioService.IsMuted
                ? 0f
                : RuleforgeAudioService.GameVolume;

            ColorBlock colors = slider.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color32(255, 216, 112, 255);
            colors.pressedColor = new Color32(181, 124, 44, 255);
            colors.disabledColor = new Color32(130, 124, 103, 180);
            slider.colors = colors;
            return slider;
        }

        private Button CreateIconButton(
            string objectName,
            Transform parent,
            Vector2 position,
            Vector2 size,
            RuleforgeExactButtonAsset exactAsset,
            out RuleforgeSettingsIconGraphic icon,
            bool topRight = true)
        {
            GameObject buttonObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect =
                buttonObject.GetComponent<RectTransform>();
            if (topRight)
            {
                rect.anchorMin = Vector2.one;
                rect.anchorMax = Vector2.one;
                rect.pivot = Vector2.one;
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
            }
            else
            {
                SetTopLeft(rect, position, size);
            }

            Image image = buttonObject.GetComponent<Image>();
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            RuleforgePixelUi.ApplyExact(
                button,
                exactAsset,
                RuleforgePixelButtonRole.Utility,
                Color.white);

            GameObject iconObject = new GameObject(
                "Icon",
                typeof(RectTransform),
                typeof(RuleforgeSettingsIconGraphic));
            iconObject.transform.SetParent(buttonObject.transform, false);
            RectTransform iconRect =
                iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = size.x >= 44f
                ? new Vector2(24f, 24f)
                : new Vector2(20f, 20f);
            icon = iconObject.GetComponent<
                RuleforgeSettingsIconGraphic>();
            icon.color = TextColor;
            icon.raycastTarget = false;
            return button;
        }

        private Button CreateButton(
            string objectName,
            Transform parent,
            string labelValue,
            Vector2 position,
            Vector2 size,
            Font font,
            RuleforgePixelButtonRole role,
            Color labelColor,
            bool topRight,
            RuleforgeExactButtonAsset exactAsset =
                RuleforgeExactButtonAsset.None)
        {
            GameObject buttonObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect =
                buttonObject.GetComponent<RectTransform>();
            if (topRight)
            {
                rect.anchorMin = Vector2.one;
                rect.anchorMax = Vector2.one;
                rect.pivot = Vector2.one;
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
            }
            else
            {
                SetTopLeft(rect, position, size);
            }

            Image image = buttonObject.GetComponent<Image>();
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            Text label = CreateText(
                "Label",
                buttonObject.transform,
                labelValue,
                new Vector2(12f, -3f),
                new Vector2(size.x - 24f, size.y - 6f),
                font,
                16,
                labelColor,
                TextAnchor.MiddleCenter);
            if (exactAsset == RuleforgeExactButtonAsset.None)
            {
                RuleforgePixelUi.Apply(button, role);
            }
            else
            {
                RuleforgePixelUi.ApplyExact(
                    button,
                    exactAsset,
                    role,
                    Color.white);
            }
            RuleforgeUiTypography.RestyleButtonLabel(
                label,
                labelColor);
            return button;
        }

        private static Text CreateText(
            string objectName,
            Transform parent,
            string value,
            Vector2 position,
            Vector2 size,
            Font font,
            int fontSize,
            Color color,
            TextAnchor alignment)
        {
            GameObject textObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform rect =
                textObject.GetComponent<RectTransform>();
            SetTopLeft(rect, position, size);
            Text text = textObject.GetComponent<Text>();
            text.text = value ?? string.Empty;
            RuleforgeUiTypography.Configure(
                text,
                font,
                fontSize,
                color,
                alignment,
                false);
            return text;
        }

        private static Image CreateImage(
            string objectName,
            Transform parent,
            Color color)
        {
            GameObject imageObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static GameObject CreatePanel(
            string objectName,
            Transform parent,
            Vector2 size)
        {
            GameObject panel = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image));
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            Image image = panel.GetComponent<Image>();
            RuleforgePixelUi.ApplyPanel(
                image,
                RuleforgePixelPanelRole.Parchment,
                PanelColor);
            return panel;
        }

        private void ApplyResponsiveLayout(bool force = false)
        {
            if (settingsButton == null || settingsPanel == null)
            {
                return;
            }

            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            if (!force &&
                width == lastLayoutWidth &&
                height == lastLayoutHeight)
            {
                return;
            }

            bool portrait = height > width;
            RectTransform buttonRect =
                settingsButton.GetComponent<RectTransform>();
            RectTransform panelRect =
                settingsPanel.GetComponent<RectTransform>();
            if (portrait)
            {
                float top = StageOneHudLayoutMetrics
                    .PortraitTopOccupiedHeight;
                buttonRect.anchoredPosition =
                    new Vector2(-14f, -top - 10f);
                panelRect.anchoredPosition =
                    new Vector2(-14f, -top - 62f);
                if (confirmationDialog != null)
                {
                    confirmationDialog.sizeDelta =
                        new Vector2(430f, 230f);
                }
            }
            else
            {
                buttonRect.anchoredPosition =
                    new Vector2(-16f, -58f);
                panelRect.anchoredPosition =
                    new Vector2(-16f, -110f);
                if (confirmationDialog != null)
                {
                    confirmationDialog.sizeDelta =
                        new Vector2(450f, 220f);
                }
            }

            lastLayoutWidth = width;
            lastLayoutHeight = height;
        }

        private void RemoveListeners()
        {
            if (settingsButton != null)
            {
                settingsButton.onClick.RemoveListener(
                    ToggleSettingsMenu);
            }

            if (stageSelectionButton != null)
            {
                stageSelectionButton.onClick.RemoveListener(
                    ShowStageSelectionConfirmation);
            }

            if (speakerButton != null)
            {
                speakerButton.onClick.RemoveListener(ToggleMute);
            }

            if (volumeSlider != null)
            {
                volumeSlider.onValueChanged.RemoveListener(
                    HandleVolumeChanged);
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveListener(
                    CancelStageSelection);
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(ReturnToSelection);
            }
        }

        private static void EnsureEventSystem(Transform parent)
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var eventSystem = new GameObject(
                "Battle Settings Event System",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            eventSystem.transform.SetParent(parent, false);
        }

        private static void SelectControl(Selectable selectable)
        {
            if (selectable == null || EventSystem.current == null)
            {
                return;
            }

            EventSystem.current.SetSelectedGameObject(
                selectable.gameObject);
        }

        private void ConfigureNavigation()
        {
            Navigation settingsNavigation = settingsButton.navigation;
            settingsNavigation.mode = Navigation.Mode.Explicit;
            settingsNavigation.selectOnDown = speakerButton;
            settingsButton.navigation = settingsNavigation;

            Navigation speakerNavigation = speakerButton.navigation;
            speakerNavigation.mode = Navigation.Mode.Explicit;
            speakerNavigation.selectOnRight = volumeSlider;
            speakerNavigation.selectOnDown = stageSelectionButton;
            speakerButton.navigation = speakerNavigation;

            Navigation sliderNavigation = volumeSlider.navigation;
            sliderNavigation.mode = Navigation.Mode.Explicit;
            sliderNavigation.selectOnUp = speakerButton;
            sliderNavigation.selectOnDown = stageSelectionButton;
            volumeSlider.navigation = sliderNavigation;

            Navigation stageNavigation =
                stageSelectionButton.navigation;
            stageNavigation.mode = Navigation.Mode.Explicit;
            stageNavigation.selectOnUp = volumeSlider;
            stageSelectionButton.navigation = stageNavigation;
        }

        private static void SetTopLeft(
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

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Font LoadFont()
        {
            StageOneBattleController controller =
                FindObjectOfType<StageOneBattleController>();
            Font font = controller == null ||
                controller.PresentationCatalog == null
                    ? null
                    : controller.PresentationCatalog.UiFont;
            return font != null
                ? font
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
        }

        private static SettingsTextDto LoadCopy()
        {
            TextAsset localization = Resources.Load<TextAsset>(
                LocalizationResourcePath);
            SettingsTextDto result = localization == null
                ? null
                : JsonUtility.FromJson<SettingsTextDto>(
                    localization.text);
            result = result ?? new SettingsTextDto();
            result.settings = ResolveCopy(result.settings, "설정");
            result.returnToSelection = ResolveCopy(
                result.returnToSelection,
                "스테이지 선택");
            result.gameVolume = ResolveCopy(
                result.gameVolume,
                "게임 음량");
            result.confirmTitle = ResolveCopy(
                result.confirmTitle,
                "스테이지 선택으로 이동");
            result.confirmMessage = ResolveCopy(
                result.confirmMessage,
                "현재 스테이지의 진행 상황이 초기화됩니다.\n" +
                "정말 스테이지 선택으로 나가시겠습니까?");
            result.cancel = ResolveCopy(result.cancel, "취소");
            result.leaveStage = ResolveCopy(
                result.leaveStage,
                "나가기");
            return result;
        }

        private static string ResolveCopy(
            string localized,
            string fallback)
        {
            return string.IsNullOrWhiteSpace(localized)
                ? fallback
                : localized.Trim();
        }

        private static void ReturnToSelection()
        {
            if (!Application.CanStreamedLevelBeLoaded(
                    MainMenuSceneName))
            {
                return;
            }

            Time.timeScale = 1f;
            StageSelectionMenu.RequestMapOnNextLoad();
            SceneManager.LoadScene(MainMenuSceneName);
        }

        private static bool IsBattleScene(string sceneName)
        {
            return string.Equals(
                    sceneName,
                    "Stage01",
                    StringComparison.Ordinal) ||
                string.Equals(
                    sceneName,
                    "Stage02",
                    StringComparison.Ordinal) ||
                string.Equals(
                    sceneName,
                    "Stage03",
                    StringComparison.Ordinal);
        }

        [Serializable]
        private sealed class SettingsTextDto
        {
            public string settings;
            public string returnToSelection;
            public string gameVolume;
            public string confirmTitle;
            public string confirmMessage;
            public string cancel;
            public string leaveStage;
        }
    }

    public enum RuleforgeSettingsIconMode
    {
        Gear,
        Speaker,
        SpeakerMuted
    }

    /// <summary>
    /// 폰트 글리프에 의존하지 않는 작은 벡터 아이콘이다. WebGL과 한글
    /// 폰트 교체 환경에서도 톱니바퀴와 스피커가 동일하게 보인다.
    /// </summary>
    public sealed class RuleforgeSettingsIconGraphic : MaskableGraphic
    {
        private RuleforgeSettingsIconMode mode;

        public RuleforgeSettingsIconMode Mode => mode;

        public void SetMode(RuleforgeSettingsIconMode value)
        {
            if (mode == value)
            {
                return;
            }

            mode = value;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            float scale = Mathf.Min(
                rectTransform.rect.width,
                rectTransform.rect.height) / 32f;
            switch (mode)
            {
                case RuleforgeSettingsIconMode.Gear:
                    DrawGear(helper, scale);
                    break;
                case RuleforgeSettingsIconMode.SpeakerMuted:
                    DrawSpeaker(helper, scale, true);
                    break;
                default:
                    DrawSpeaker(helper, scale, false);
                    break;
            }
        }

        private void DrawGear(VertexHelper helper, float scale)
        {
            const int segments = 32;
            const float innerRadius = 5.1f;
            for (int i = 0; i < segments; i++)
            {
                float angle0 = Mathf.PI * 2f * i / segments;
                float angle1 = Mathf.PI * 2f * (i + 1) / segments;
                float radius0 = GearRadius(i);
                float radius1 = GearRadius(i + 1);
                AddQuad(
                    helper,
                    Polar(innerRadius, angle0, scale),
                    Polar(radius0, angle0, scale),
                    Polar(radius1, angle1, scale),
                    Polar(innerRadius, angle1, scale));
            }
        }

        private static float GearRadius(int index)
        {
            int toothPhase = index % 4;
            return toothPhase == 0 || toothPhase == 1
                ? 14.2f
                : 11.2f;
        }

        private void DrawSpeaker(
            VertexHelper helper,
            float scale,
            bool muted)
        {
            AddQuad(
                helper,
                Point(-12f, -4.2f, scale),
                Point(-6.5f, -4.2f, scale),
                Point(-6.5f, 4.2f, scale),
                Point(-12f, 4.2f, scale));
            AddTriangle(
                helper,
                Point(-7f, -4.5f, scale),
                Point(1.8f, -10.5f, scale),
                Point(1.8f, 10.5f, scale));

            if (muted)
            {
                AddLine(
                    helper,
                    Point(5f, -7f, scale),
                    Point(13f, 7f, scale),
                    2.6f * scale);
                AddLine(
                    helper,
                    Point(5f, 7f, scale),
                    Point(13f, -7f, scale),
                    2.6f * scale);
                return;
            }

            AddArc(helper, 4.5f, 1.5f, scale);
            AddArc(helper, 8.5f, 1.5f, scale);
        }

        private void AddArc(
            VertexHelper helper,
            float radius,
            float xOffset,
            float scale)
        {
            const int segments = 6;
            Vector2 previous = Vector2.zero;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(
                    -Mathf.PI * 0.37f,
                    Mathf.PI * 0.37f,
                    i / (float)segments);
                Vector2 current = Point(
                    xOffset + Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    scale);
                if (i > 0)
                {
                    AddLine(
                        helper,
                        previous,
                        current,
                        1.9f * scale);
                }

                previous = current;
            }
        }

        private static Vector2 Polar(
            float radius,
            float angle,
            float scale)
        {
            return new Vector2(
                Mathf.Cos(angle) * radius * scale,
                Mathf.Sin(angle) * radius * scale);
        }

        private static Vector2 Point(
            float x,
            float y,
            float scale)
        {
            return new Vector2(x * scale, y * scale);
        }

        private void AddLine(
            VertexHelper helper,
            Vector2 from,
            Vector2 to,
            float thickness)
        {
            Vector2 direction = (to - from).normalized;
            Vector2 normal = new Vector2(
                -direction.y,
                direction.x) * thickness * 0.5f;
            AddQuad(
                helper,
                from - normal,
                from + normal,
                to + normal,
                to - normal);
        }

        private void AddQuad(
            VertexHelper helper,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d)
        {
            int index = helper.currentVertCount;
            helper.AddVert(a, color, Vector2.zero);
            helper.AddVert(b, color, Vector2.zero);
            helper.AddVert(c, color, Vector2.zero);
            helper.AddVert(d, color, Vector2.zero);
            helper.AddTriangle(index, index + 1, index + 2);
            helper.AddTriangle(index, index + 2, index + 3);
        }

        private void AddTriangle(
            VertexHelper helper,
            Vector2 a,
            Vector2 b,
            Vector2 c)
        {
            int index = helper.currentVertCount;
            helper.AddVert(a, color, Vector2.zero);
            helper.AddVert(b, color, Vector2.zero);
            helper.AddVert(c, color, Vector2.zero);
            helper.AddTriangle(index, index + 1, index + 2);
        }
    }

    /// <summary>
    /// 볼륨 트랙 위에 표시하는 작은 원형 손잡이. 폰트나 외부 텍스처에
    /// 의존하지 않아 WebGL에서도 항상 같은 크기와 실루엣을 유지한다.
    /// </summary>
    public sealed class RuleforgeSliderKnobGraphic : MaskableGraphic
    {
        private const int CircleSegments = 20;
        private Color fillColor = Color.white;

        public void Configure(Color border, Color fill)
        {
            color = border;
            fillColor = fill;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            Rect rect = rectTransform.rect;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            AddDisc(helper, rect.center, radius, color);
            AddDisc(helper, rect.center, radius * 0.68f, fillColor);
        }

        private static void AddDisc(
            VertexHelper helper,
            Vector2 center,
            float radius,
            Color discColor)
        {
            int centerIndex = helper.currentVertCount;
            helper.AddVert(center, discColor, Vector2.zero);
            for (int i = 0; i <= CircleSegments; i++)
            {
                float angle = Mathf.PI * 2f * i / CircleSegments;
                Vector2 point = center + new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)) * radius;
                helper.AddVert(point, discColor, Vector2.zero);
            }

            for (int i = 0; i < CircleSegments; i++)
            {
                helper.AddTriangle(
                    centerIndex,
                    centerIndex + i + 1,
                    centerIndex + i + 2);
            }
        }
    }
}
