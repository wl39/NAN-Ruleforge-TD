using System;
using RuleforgeTD.Tutorial;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Decoupled signal used by the guide's "tutorial replay" action. The
    /// pending flag survives a scene transition in the current player session;
    /// the tutorial bootstrap owns consuming it and starting a fresh Stage 01
    /// run without resetting campaign progress.
    /// </summary>
    public static class GameGuideRuntime
    {
        public static event Action TutorialReplayRequested;

        public static bool HasPendingTutorialReplayRequest =>
            TutorialProgressStore.CreateCurrent()
                .IsManualReplayRequested;

        public static void RequestTutorialReplay()
        {
            TutorialProgressStore.CreateCurrent().RequestManualReplay();
            TutorialReplayRequested?.Invoke();
        }

        public static bool ConsumeTutorialReplayRequest()
        {
            return TutorialProgressStore.CreateCurrent()
                .ConsumeManualReplayRequest();
        }
    }

    /// <summary>
    /// Runtime-built, always-available game guide shared by the main menu and
    /// battle settings. It deliberately observes no GameSimulation state.
    /// </summary>
    public sealed class GameGuideModal : MonoBehaviour
    {
        private const string TutorialSceneName = "Stage01";
        private static readonly Color BackdropColor =
            new Color32(5, 9, 10, 224);
        private static readonly Color WorkbenchColor =
            new Color32(20, 29, 30, 255);
        private static readonly Color SidebarColor =
            new Color32(31, 40, 39, 255);
        private static readonly Color ContentColor =
            new Color32(239, 225, 183, 255);
        private static readonly Color InkColor =
            new Color32(48, 38, 27, 255);
        private static readonly Color MutedInkColor =
            new Color32(91, 75, 57, 255);
        private static readonly Color IvoryColor =
            new Color32(244, 236, 210, 255);
        private static readonly Color GoldColor =
            new Color32(244, 194, 78, 255);
        private static readonly Color MutedColor =
            new Color32(184, 190, 176, 255);

        private static int openGuideCount;

        private GameGuideCatalog catalog;
        private Font uiFont;
        private Func<IDisposable> pauseLeaseFactory;
        private IDisposable pauseLease;
        private GameObject modalRoot;
        private RectTransform viewportRect;
        private RectTransform scrollContentRect;
        private ScrollRect scrollRect;
        private Text sectionTitle;
        private Text contentText;
        private Button closeButton;
        private Button tutorialReplayButton;
        private Button[] tabButtons = Array.Empty<Button>();
        private int selectedTabIndex;
        private bool initialized;
        private bool isOpen;

        public static event Action AnyGuideOpened;
        public static event Action AnyGuideClosed;

        public event Action Opened;
        public event Action Closed;

        public static bool IsAnyGuideOpen => openGuideCount > 0;
        public bool IsInitialized => initialized;
        public bool IsOpen => isOpen;
        public int SelectedTabIndex => selectedTabIndex;
        public int TabCount => tabButtons.Length;
        public GameGuideCatalog Catalog => catalog;
        public Text ContentText => contentText;
        public Button CloseButton => closeButton;
        public Button TutorialReplayButton => tutorialReplayButton;

        /// <summary>
        /// Builds the modal once. A custom pause lease can be supplied by a
        /// host; otherwise an exact Time.timeScale lease is used. Battle code
        /// should additionally observe IsAnyGuideOpen because it normally
        /// reapplies its preferred speed every frame.
        /// </summary>
        public void Initialize(
            Font font,
            Func<IDisposable> sourcePauseLeaseFactory = null,
            TextAsset localization = null)
        {
            if (initialized)
            {
                return;
            }

            catalog = localization == null
                ? GameGuideCatalog.LoadDefault()
                : GameGuideCatalog.Load(localization);
            uiFont = font != null
                ? font
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            pauseLeaseFactory = sourcePauseLeaseFactory ??
                GameGuideTimeScaleLease.Acquire;
            BuildInterface();
            initialized = true;
        }

        public Button GetTabButton(int index)
        {
            if (index < 0 || index >= tabButtons.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return tabButtons[index];
        }

        public void Open()
        {
            if (!initialized)
            {
                Initialize(null);
            }

            if (isOpen)
            {
                return;
            }

            pauseLease = pauseLeaseFactory == null
                ? null
                : pauseLeaseFactory();
            isOpen = true;
            openGuideCount++;
            modalRoot.SetActive(true);
            SelectTab(selectedTabIndex);
            SelectControl(tabButtons[selectedTabIndex]);
            Opened?.Invoke();
            AnyGuideOpened?.Invoke();
        }

        public void Close()
        {
            if (!isOpen)
            {
                return;
            }

            isOpen = false;
            openGuideCount = Mathf.Max(0, openGuideCount - 1);
            if (modalRoot != null)
            {
                modalRoot.SetActive(false);
            }

            IDisposable lease = pauseLease;
            pauseLease = null;
            lease?.Dispose();
            Closed?.Invoke();
            AnyGuideClosed?.Invoke();
        }

        public void SelectTab(int index)
        {
            if (!initialized && catalog == null)
            {
                throw new InvalidOperationException(
                    "Initialize the game guide before selecting a tab.");
            }

            if (index < 0 || index >= catalog.TabCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            selectedTabIndex = index;
            GameGuideTab tab = catalog.GetTab(index);
            sectionTitle.text = tab.Title;
            contentText.text = catalog.BuildTabBody(index);
            RefreshTabStyles();
            RefreshScrollLayout();
        }

        private void OnDestroy()
        {
            Close();
        }

        private void BuildInterface()
        {
            EnsureEventSystem();

            GameObject canvasObject = new GameObject(
                "Game Guide Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.sortingOrder = 6000;

            CanvasScaler scaler =
                canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            modalRoot = new GameObject(
                "Game Guide Modal",
                typeof(RectTransform),
                typeof(Image));
            modalRoot.transform.SetParent(canvasObject.transform, false);
            RectTransform modalRect =
                modalRoot.GetComponent<RectTransform>();
            Stretch(modalRect);
            Image backdrop = modalRoot.GetComponent<Image>();
            backdrop.color = BackdropColor;
            backdrop.raycastTarget = true;

            GameObject panel = CreatePanel(
                "Game Guide Panel",
                modalRoot.transform,
                WorkbenchColor,
                RuleforgePixelPanelRole.Workbench);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.055f, 0.055f);
            panelRect.anchorMax = new Vector2(0.945f, 0.945f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            BuildHeader(panel.transform);
            BuildTabs(panel.transform);
            BuildContent(panel.transform);
            BuildFooter(panel.transform);

            modalRoot.SetActive(false);
        }

        private void BuildHeader(Transform parent)
        {
            GameObject header = CreatePanel(
                "Game Guide Header",
                parent,
                SidebarColor,
                RuleforgePixelPanelRole.Workbench);
            RectTransform headerRect =
                header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = Vector2.one;
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0f, 86f);

            Text title = CreateText(
                "Game Guide Title",
                header.transform,
                catalog.Title,
                30,
                IvoryColor,
                TextAnchor.MiddleLeft);
            SetAnchoredRect(
                title.rectTransform,
                new Vector2(0f, 0.34f),
                new Vector2(1f, 1f),
                new Vector2(28f, 0f),
                new Vector2(-240f, -4f));

            Text subtitle = CreateText(
                "Game Guide Subtitle",
                header.transform,
                catalog.Subtitle,
                14,
                MutedColor,
                TextAnchor.LowerLeft);
            SetAnchoredRect(
                subtitle.rectTransform,
                new Vector2(0f, 0f),
                new Vector2(1f, 0.42f),
                new Vector2(30f, 4f),
                new Vector2(-250f, -1f));

            closeButton = CreateButton(
                "Close Game Guide Button",
                header.transform,
                catalog.CloseLabel,
                RuleforgePixelButtonRole.Secondary,
                IvoryColor);
            RectTransform closeRect =
                closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.anchoredPosition = new Vector2(-22f, 0f);
            closeRect.sizeDelta = new Vector2(154f, 44f);
            closeButton.onClick.AddListener(Close);
        }

        private void BuildTabs(Transform parent)
        {
            GameObject sidebar = CreatePanel(
                "Game Guide Tabs",
                parent,
                SidebarColor,
                RuleforgePixelPanelRole.Workbench);
            RectTransform sidebarRect =
                sidebar.GetComponent<RectTransform>();
            sidebarRect.anchorMin = Vector2.zero;
            sidebarRect.anchorMax = new Vector2(0f, 1f);
            sidebarRect.pivot = new Vector2(0f, 0.5f);
            sidebarRect.anchoredPosition = new Vector2(0f, -3f);
            sidebarRect.sizeDelta = new Vector2(244f, -172f);
            sidebarRect.offsetMin = new Vector2(0f, 86f);
            sidebarRect.offsetMax = new Vector2(244f, -86f);

            tabButtons = new Button[catalog.TabCount];
            for (int i = 0; i < tabButtons.Length; i++)
            {
                int tabIndex = i;
                Button button = CreateButton(
                    "Game Guide Tab " + catalog.GetTab(i).Id,
                    sidebar.transform,
                    catalog.GetTab(i).Title,
                    RuleforgePixelButtonRole.Secondary,
                    IvoryColor);
                RectTransform rect =
                    button.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -16f - i * 59f);
                rect.sizeDelta = new Vector2(-28f, 46f);
                button.onClick.AddListener(() => SelectTab(tabIndex));
                tabButtons[i] = button;
            }
        }

        private void BuildContent(Transform parent)
        {
            GameObject contentPanel = CreatePanel(
                "Game Guide Content Panel",
                parent,
                ContentColor,
                RuleforgePixelPanelRole.Parchment);
            RectTransform panelRect =
                contentPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = new Vector2(256f, 86f);
            panelRect.offsetMax = new Vector2(-18f, -98f);

            sectionTitle = CreateText(
                "Game Guide Section Title",
                contentPanel.transform,
                string.Empty,
                27,
                InkColor,
                TextAnchor.MiddleLeft);
            SetAnchoredRect(
                sectionTitle.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(24f, -58f),
                new Vector2(-48f, -14f));

            GameObject viewport = new GameObject(
                "Game Guide Scroll View",
                typeof(RectTransform),
                typeof(Image),
                typeof(Mask),
                typeof(ScrollRect));
            viewport.transform.SetParent(contentPanel.transform, false);
            viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(18f, 18f);
            viewportRect.offsetMax = new Vector2(-36f, -74f);
            Image viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.012f);
            viewportImage.raycastTarget = true;
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            scrollContentRect = new GameObject(
                "Game Guide Scroll Content",
                typeof(RectTransform))
                .GetComponent<RectTransform>();
            scrollContentRect.SetParent(viewport.transform, false);
            scrollContentRect.anchorMin = new Vector2(0f, 1f);
            scrollContentRect.anchorMax = new Vector2(1f, 1f);
            scrollContentRect.pivot = new Vector2(0.5f, 1f);
            scrollContentRect.anchoredPosition = Vector2.zero;
            scrollContentRect.sizeDelta = Vector2.zero;

            contentText = CreateText(
                "Game Guide Body",
                scrollContentRect,
                string.Empty,
                17,
                InkColor,
                TextAnchor.UpperLeft);
            contentText.lineSpacing = 1.18f;
            contentText.verticalOverflow = VerticalWrapMode.Overflow;
            SetAnchoredRect(
                contentText.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(12f, 12f),
                new Vector2(-12f, -12f));

            scrollRect = viewport.GetComponent<ScrollRect>();
            scrollRect.content = scrollContentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.decelerationRate = 0.12f;
            scrollRect.scrollSensitivity = 38f;

            Scrollbar scrollbar = CreateScrollbar(
                "Game Guide Scrollbar",
                contentPanel.transform);
            RectTransform scrollbarRect =
                scrollbar.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.offsetMin = new Vector2(-27f, 20f);
            scrollbarRect.offsetMax = new Vector2(-15f, -76f);
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scrollRect.verticalScrollbarSpacing = 7f;
        }

        private void BuildFooter(Transform parent)
        {
            GameObject footer = CreatePanel(
                "Game Guide Footer",
                parent,
                SidebarColor,
                RuleforgePixelPanelRole.Workbench);
            RectTransform footerRect =
                footer.GetComponent<RectTransform>();
            footerRect.anchorMin = Vector2.zero;
            footerRect.anchorMax = new Vector2(1f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.anchoredPosition = Vector2.zero;
            footerRect.sizeDelta = new Vector2(0f, 86f);

            tutorialReplayButton = CreateButton(
                "Replay Tutorial Button",
                footer.transform,
                catalog.TutorialReplayLabel,
                RuleforgePixelButtonRole.Primary,
                IvoryColor);
            RectTransform replayRect =
                tutorialReplayButton.GetComponent<RectTransform>();
            replayRect.anchorMin = new Vector2(0f, 0.5f);
            replayRect.anchorMax = new Vector2(0f, 0.5f);
            replayRect.pivot = new Vector2(0f, 0.5f);
            replayRect.anchoredPosition = new Vector2(22f, 0f);
            replayRect.sizeDelta = new Vector2(230f, 46f);
            tutorialReplayButton.onClick.AddListener(
                HandleTutorialReplayRequested);

            Text hint = CreateText(
                "Tutorial Replay Hint",
                footer.transform,
                catalog.TutorialReplayHint,
                13,
                MutedColor,
                TextAnchor.MiddleLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            SetAnchoredRect(
                hint.rectTransform,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(274f, 12f),
                new Vector2(-30f, -12f));
        }

        private void HandleTutorialReplayRequested()
        {
            Close();
            GameGuideRuntime.RequestTutorialReplay();
            if (!Application.isBatchMode &&
                Application.CanStreamedLevelBeLoaded(
                    TutorialSceneName))
            {
                SceneManager.LoadScene(TutorialSceneName);
            }
        }

        private void RefreshTabStyles()
        {
            for (int i = 0; i < tabButtons.Length; i++)
            {
                RuleforgePixelUi.Apply(
                    tabButtons[i],
                    i == selectedTabIndex
                        ? RuleforgePixelButtonRole.Selected
                        : RuleforgePixelButtonRole.Secondary);
            }
        }

        private void RefreshScrollLayout()
        {
            if (contentText == null || scrollContentRect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            float viewportHeight = viewportRect == null
                ? 480f
                : Mathf.Max(1f, viewportRect.rect.height);
            float preferredHeight = Mathf.Max(
                viewportHeight,
                contentText.preferredHeight + 32f);
            scrollContentRect.sizeDelta =
                new Vector2(0f, preferredHeight);
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                scrollContentRect);
            if (scrollRect != null)
            {
                scrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private Button CreateButton(
            string objectName,
            Transform parent,
            string label,
            RuleforgePixelButtonRole role,
            Color labelColor)
        {
            GameObject buttonObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            RuleforgePixelUi.Apply(button, role);

            Text text = CreateText(
                "Label",
                buttonObject.transform,
                label,
                16,
                labelColor,
                TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 10f, 5f, 10f, 5f);
            RuleforgeUiTypography.RestyleButtonLabel(text, labelColor);
            return button;
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            string value,
            int fontSize,
            Color color,
            TextAnchor alignment)
        {
            GameObject textObject = new GameObject(
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

        private static GameObject CreatePanel(
            string objectName,
            Transform parent,
            Color color,
            RuleforgePixelPanelRole role)
        {
            GameObject panel = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image));
            panel.transform.SetParent(parent, false);
            Image image = panel.GetComponent<Image>();
            RuleforgePixelUi.ApplyPanel(image, role, color);
            return panel;
        }

        private static Scrollbar CreateScrollbar(
            string objectName,
            Transform parent)
        {
            GameObject root = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Image),
                typeof(Scrollbar));
            root.transform.SetParent(parent, false);
            Image background = root.GetComponent<Image>();
            background.color = new Color32(82, 67, 48, 150);

            GameObject handleObject = new GameObject(
                "Handle",
                typeof(RectTransform),
                typeof(Image));
            handleObject.transform.SetParent(root.transform, false);
            RectTransform handleRect =
                handleObject.GetComponent<RectTransform>();
            Stretch(handleRect, 2f, 2f, 2f, 2f);
            Image handle = handleObject.GetComponent<Image>();
            handle.color = GoldColor;

            Scrollbar scrollbar = root.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            return scrollbar;
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var eventSystem = new GameObject(
                "Game Guide Event System",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            eventSystem.transform.SetParent(transform, false);
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

        private static void SetAnchoredRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void Stretch(RectTransform rect)
        {
            Stretch(rect, 0f, 0f, 0f, 0f);
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

        private static class GameGuideTimeScaleLease
        {
            private static int leaseCount;
            private static float restoreTimeScale = 1f;

            public static IDisposable Acquire()
            {
                if (leaseCount == 0)
                {
                    restoreTimeScale = Time.timeScale;
                }

                leaseCount++;
                Time.timeScale = 0f;
                return new Lease();
            }

            private sealed class Lease : IDisposable
            {
                private bool disposed;

                public void Dispose()
                {
                    if (disposed)
                    {
                        return;
                    }

                    disposed = true;
                    leaseCount = Mathf.Max(0, leaseCount - 1);
                    if (leaseCount == 0)
                    {
                        Time.timeScale = restoreTimeScale;
                    }
                }
            }
        }
    }
}
