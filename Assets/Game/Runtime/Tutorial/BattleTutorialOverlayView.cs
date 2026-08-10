using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuleforgeTD.Tutorial
{
    /// <summary>
    /// Immutable-by-convention presentation model consumed by the tutorial
    /// overlay. Controllers may reuse the same view for guided interactions
    /// (ShowNextButton=false) and explanatory pages (ShowNextButton=true).
    /// </summary>
    [Serializable]
    public sealed class TutorialOverlayContent
    {
        public TutorialOverlayContent(
            string anchorId,
            string title,
            string body,
            string progressLabel = null)
        {
            AnchorId = anchorId;
            Title = title;
            Body = body;
            ProgressLabel = progressLabel;
        }

        public string AnchorId { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string ProgressLabel { get; set; }
        public string NextLabel { get; set; } = "다음";
        public string SkipLabel { get; set; } = "건너뛰기";
        public bool ShowNextButton { get; set; } = true;
        public bool BlockOutsideHole { get; set; } = true;
        public Vector2 HolePadding { get; set; } =
            new Vector2(18f, 14f);
    }

    /// <summary>
    /// Runtime uGUI tutorial overlay. Four independent dim graphics block
    /// input only outside the highlighted rectangle, leaving the target hole
    /// free for pointer clicks and drags. The overlay owns presentation only;
    /// step progression and battle pausing stay with its controller.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleTutorialOverlayView : MonoBehaviour
    {
        public const int DefaultSortingOrder = 5000;

        private const float CalloutGap = 24f;
        private const float CalloutEdgeInset = 16f;
        private const float PreferredCalloutWidth = 600f;
        private const float PreferredCalloutHeight = 300f;
        private const float MinimumCalloutWidth = 300f;
        private const float MinimumCalloutHeight = 180f;
        private const float ArrowLongSize = 30f;
        private const float ArrowShortSize = 22f;

        private static readonly Color DimColor =
            new Color(0.015f, 0.012f, 0.01f, 0.74f);
        private static readonly Color CalloutColor =
            new Color32(52, 37, 27, 252);
        private static readonly Color CalloutOutlineColor =
            new Color32(205, 160, 88, 255);
        private static readonly Color TitleColor =
            new Color32(255, 231, 177, 255);
        private static readonly Color BodyColor =
            new Color32(244, 235, 211, 255);
        private static readonly Color ProgressColor =
            new Color32(206, 177, 123, 255);
        private static readonly Color PrimaryButtonColor =
            new Color32(205, 143, 65, 255);
        private static readonly Color SecondaryButtonColor =
            new Color32(102, 79, 58, 255);

        private enum Placement
        {
            Center,
            Below,
            Above,
            Right,
            Left
        }

        private readonly Image[] dimPanels = new Image[4];

        private TutorialAnchorRegistry anchorRegistry;
        private TutorialOverlayContent currentContent;
        private Font font;
        private Canvas canvas;
        private RectTransform overlayRoot;
        private RectTransform calloutRect;
        private TutorialArrowGraphic arrow;
        private Text titleText;
        private Text bodyText;
        private Text progressText;
        private Button nextButton;
        private Button skipButton;
        private bool built;
        private bool visible;
        private bool hasResolvedAnchor;
        private bool nextInteractable = true;
        private Rect lastHoleScreenRect;

        public event Action NextRequested;
        public event Action SkipRequested;
        public event Action<bool> AnchorAvailabilityChanged;

        public TutorialAnchorRegistry AnchorRegistry
        {
            get => anchorRegistry;
            set
            {
                if (anchorRegistry == value)
                {
                    return;
                }

                anchorRegistry = value;
                if (visible)
                {
                    RefreshNow();
                }
            }
        }

        public TutorialOverlayContent CurrentContent => currentContent;
        public Canvas Canvas => canvas;
        public RectTransform CalloutRect => calloutRect;
        public Button NextButton => nextButton;
        public Button SkipButton => skipButton;
        public Text TitleText => titleText;
        public Text BodyText => bodyText;
        public Text ProgressText => progressText;
        public bool IsVisible => visible && canvas != null && canvas.enabled;
        public bool HasResolvedAnchor => hasResolvedAnchor;
        public Rect LastHoleScreenRect => lastHoleScreenRect;
        public int DimPanelCount => dimPanels.Length;

        public static BattleTutorialOverlayView CreateRuntime(
            Font uiFont,
            Transform parent = null)
        {
            var host = new GameObject("Battle Tutorial Overlay");
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            BattleTutorialOverlayView view =
                host.AddComponent<BattleTutorialOverlayView>();
            view.SetFont(uiFont);
            return view;
        }

        private void Awake()
        {
            font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            BuildInterface();
            Hide();
        }

        private void LateUpdate()
        {
            if (visible)
            {
                RefreshNow();
            }
        }

        private void OnDestroy()
        {
            if (nextButton != null)
            {
                nextButton.onClick.RemoveListener(HandleNextClicked);
            }

            if (skipButton != null)
            {
                skipButton.onClick.RemoveListener(HandleSkipClicked);
            }
        }

        public void Show(TutorialOverlayContent content)
        {
            if (content == null)
            {
                Hide();
                return;
            }

            BuildInterface();
            currentContent = content;
            visible = true;
            canvas.enabled = true;
            titleText.text = content.Title ?? string.Empty;
            bodyText.text = content.Body ?? string.Empty;
            progressText.text = content.ProgressLabel ?? string.Empty;
            progressText.gameObject.SetActive(
                !string.IsNullOrEmpty(content.ProgressLabel));
            SetButtonLabel(
                nextButton,
                string.IsNullOrEmpty(content.NextLabel)
                    ? "다음"
                    : content.NextLabel);
            SetButtonLabel(
                skipButton,
                string.IsNullOrEmpty(content.SkipLabel)
                    ? "건너뛰기"
                    : content.SkipLabel);
            nextButton.gameObject.SetActive(content.ShowNextButton);
            nextButton.interactable = nextInteractable;
            skipButton.gameObject.SetActive(true);
            RefreshNow();
        }

        public void Hide()
        {
            visible = false;
            currentContent = null;
            hasResolvedAnchor = false;
            lastHoleScreenRect = default(Rect);
            if (canvas != null)
            {
                canvas.enabled = false;
            }
        }

        /// <summary>
        /// Replaces every overlay label font. Pass the bundled Korean-capable
        /// font from the stage presentation catalog in production.
        /// </summary>
        public void SetFont(Font uiFont)
        {
            font = uiFont != null
                ? uiFont
                : Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            if (!built)
            {
                return;
            }

            Text[] labels = GetComponentsInChildren<Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].font = font;
            }
        }

        public void SetNextInteractable(bool interactable)
        {
            nextInteractable = interactable;
            if (nextButton != null)
            {
                nextButton.interactable = interactable;
            }
        }

        public Image GetDimPanel(int index)
        {
            if (index < 0 || index >= dimPanels.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return dimPanels[index];
        }

        public bool IsScreenPointInsideHole(Vector2 screenPoint)
        {
            return hasResolvedAnchor &&
                lastHoleScreenRect.Contains(screenPoint);
        }

        /// <summary>
        /// Resolves the current target and updates all screen-space geometry.
        /// It is public so tests and controllers can force a refresh directly
        /// after opening or closing another runtime panel.
        /// </summary>
        public void RefreshNow()
        {
            if (!visible || !built || currentContent == null)
            {
                return;
            }

            Rect rootRect = overlayRoot.rect;
            if (rootRect.width <= 0f || rootRect.height <= 0f)
            {
                Canvas.ForceUpdateCanvases();
                rootRect = overlayRoot.rect;
            }

            if (rootRect.width <= 0f || rootRect.height <= 0f)
            {
                return;
            }

            bool resolved = TryResolveHole(
                currentContent,
                rootRect,
                out Rect holeLocalRect,
                out Rect holeScreenRect);
            SetAnchorAvailability(resolved);
            lastHoleScreenRect = resolved
                ? holeScreenRect
                : default(Rect);

            if (resolved)
            {
                LayoutDimPanelsAroundHole(rootRect, holeLocalRect);
            }
            else
            {
                LayoutMissingAnchorFallback(rootRect);
            }

            bool blockOutside = resolved &&
                currentContent.BlockOutsideHole;
            for (int i = 0; i < dimPanels.Length; i++)
            {
                dimPanels[i].raycastTarget = blockOutside;
            }

            Rect safeLocalRect = ResolveSafeAreaLocal(rootRect);
            LayoutCallout(
                safeLocalRect,
                resolved,
                holeLocalRect);
        }

        private void BuildInterface()
        {
            if (built)
            {
                return;
            }

            built = true;
            EnsureEventSystem();

            GameObject canvasObject = new GameObject(
                "Tutorial Overlay Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            canvas.sortingOrder = DefaultSortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            overlayRoot = CreateRect("Overlay Root", canvasObject.transform);
            Stretch(overlayRoot);

            string[] panelNames =
            {
                "Dim Top",
                "Dim Bottom",
                "Dim Left",
                "Dim Right"
            };
            for (int i = 0; i < dimPanels.Length; i++)
            {
                dimPanels[i] = CreateImage(
                    panelNames[i],
                    overlayRoot,
                    DimColor,
                    true);
            }

            Image calloutImage = CreateImage(
                "Tutorial Callout",
                overlayRoot,
                CalloutColor,
                true);
            calloutRect = calloutImage.rectTransform;
            Outline calloutOutline =
                calloutImage.gameObject.AddComponent<Outline>();
            calloutOutline.effectColor = CalloutOutlineColor;
            calloutOutline.effectDistance = new Vector2(2f, -2f);
            calloutOutline.useGraphicAlpha = true;

            progressText = CreateText(
                "Progress",
                calloutRect,
                16,
                ProgressColor,
                TextAnchor.MiddleLeft);
            Anchor(
                progressText.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(22f, -42f),
                new Vector2(-22f, -12f));

            titleText = CreateText(
                "Title",
                calloutRect,
                28,
                TitleColor,
                TextAnchor.MiddleLeft);
            Anchor(
                titleText.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(22f, -88f),
                new Vector2(-22f, -42f));

            bodyText = CreateText(
                "Body",
                calloutRect,
                20,
                BodyColor,
                TextAnchor.UpperLeft);
            bodyText.lineSpacing = 1.12f;
            Anchor(
                bodyText.rectTransform,
                Vector2.zero,
                Vector2.one,
                new Vector2(22f, 80f),
                new Vector2(-22f, -92f));

            skipButton = CreateButton(
                "Skip Tutorial",
                calloutRect,
                SecondaryButtonColor,
                "건너뛰기");
            SetBottomButtonRect(
                skipButton.GetComponent<RectTransform>(),
                false);

            nextButton = CreateButton(
                "Next Tutorial Step",
                calloutRect,
                PrimaryButtonColor,
                "다음");
            SetBottomButtonRect(
                nextButton.GetComponent<RectTransform>(),
                true);

            nextButton.onClick.AddListener(HandleNextClicked);
            skipButton.onClick.AddListener(HandleSkipClicked);

            arrow = new GameObject(
                "Tutorial Arrow",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TutorialArrowGraphic))
                .GetComponent<TutorialArrowGraphic>();
            arrow.transform.SetParent(overlayRoot, false);
            arrow.color = CalloutOutlineColor;
            arrow.raycastTarget = false;
        }

        private bool TryResolveHole(
            TutorialOverlayContent content,
            Rect rootRect,
            out Rect holeLocalRect,
            out Rect holeScreenRect)
        {
            holeLocalRect = default(Rect);
            holeScreenRect = default(Rect);
            if (anchorRegistry == null ||
                string.IsNullOrEmpty(content.AnchorId) ||
                !anchorRegistry.TryGetScreenRect(
                    content.AnchorId,
                    out Rect targetScreenRect))
            {
                return false;
            }

            float scale = canvas == null
                ? 1f
                : Mathf.Max(0.01f, canvas.scaleFactor);
            Vector2 paddingPixels = new Vector2(
                Mathf.Max(0f, content.HolePadding.x) * scale,
                Mathf.Max(0f, content.HolePadding.y) * scale);
            targetScreenRect = Rect.MinMaxRect(
                Mathf.Clamp(
                    targetScreenRect.xMin - paddingPixels.x,
                    0f,
                    Mathf.Max(1f, Screen.width)),
                Mathf.Clamp(
                    targetScreenRect.yMin - paddingPixels.y,
                    0f,
                    Mathf.Max(1f, Screen.height)),
                Mathf.Clamp(
                    targetScreenRect.xMax + paddingPixels.x,
                    0f,
                    Mathf.Max(1f, Screen.width)),
                Mathf.Clamp(
                    targetScreenRect.yMax + paddingPixels.y,
                    0f,
                    Mathf.Max(1f, Screen.height)));
            if (targetScreenRect.width <= 0f ||
                targetScreenRect.height <= 0f ||
                !TryScreenRectToLocal(targetScreenRect, out holeLocalRect))
            {
                return false;
            }

            holeLocalRect = Intersect(holeLocalRect, rootRect);
            if (holeLocalRect.width <= 0f || holeLocalRect.height <= 0f)
            {
                return false;
            }

            holeScreenRect = targetScreenRect;
            return true;
        }

        private void LayoutDimPanelsAroundHole(
            Rect rootRect,
            Rect holeRect)
        {
            SetPanelRect(
                dimPanels[0],
                Rect.MinMaxRect(
                    rootRect.xMin,
                    holeRect.yMax,
                    rootRect.xMax,
                    rootRect.yMax));
            SetPanelRect(
                dimPanels[1],
                Rect.MinMaxRect(
                    rootRect.xMin,
                    rootRect.yMin,
                    rootRect.xMax,
                    holeRect.yMin));
            SetPanelRect(
                dimPanels[2],
                Rect.MinMaxRect(
                    rootRect.xMin,
                    holeRect.yMin,
                    holeRect.xMin,
                    holeRect.yMax));
            SetPanelRect(
                dimPanels[3],
                Rect.MinMaxRect(
                    holeRect.xMax,
                    holeRect.yMin,
                    rootRect.xMax,
                    holeRect.yMax));
        }

        private void LayoutMissingAnchorFallback(Rect rootRect)
        {
            SetPanelRect(dimPanels[0], rootRect);
            for (int i = 1; i < dimPanels.Length; i++)
            {
                SetPanelRect(dimPanels[i], default(Rect));
            }
        }

        private void LayoutCallout(
            Rect safeRect,
            bool hasAnchor,
            Rect holeRect)
        {
            Rect usableSafeRect = Inset(
                safeRect,
                CalloutEdgeInset,
                CalloutEdgeInset);
            if (usableSafeRect.width < 1f || usableSafeRect.height < 1f)
            {
                usableSafeRect = safeRect;
            }

            float desiredWidth = Mathf.Min(
                PreferredCalloutWidth,
                usableSafeRect.width);
            float desiredHeight = Mathf.Min(
                PreferredCalloutHeight,
                usableSafeRect.height);
            Placement placement = Placement.Center;
            Rect panelRect;

            if (!hasAnchor ||
                !TryPlaceOutsideHole(
                    usableSafeRect,
                    holeRect,
                    desiredWidth,
                    desiredHeight,
                    out panelRect,
                    out placement))
            {
                panelRect = CenteredRect(
                    usableSafeRect.center,
                    new Vector2(desiredWidth, desiredHeight));
                panelRect = ClampInside(panelRect, usableSafeRect);
                placement = Placement.Center;
            }

            SetLocalRect(calloutRect, panelRect);
            LayoutArrow(placement, panelRect, holeRect);
        }

        private static bool TryPlaceOutsideHole(
            Rect safeRect,
            Rect holeRect,
            float desiredWidth,
            float desiredHeight,
            out Rect panelRect,
            out Placement placement)
        {
            float belowSpace = holeRect.yMin - CalloutGap - safeRect.yMin;
            if (belowSpace >= MinimumCalloutHeight)
            {
                float height = Mathf.Min(desiredHeight, belowSpace);
                float centerX = Mathf.Clamp(
                    holeRect.center.x,
                    safeRect.xMin + desiredWidth * 0.5f,
                    safeRect.xMax - desiredWidth * 0.5f);
                panelRect = Rect.MinMaxRect(
                    centerX - desiredWidth * 0.5f,
                    holeRect.yMin - CalloutGap - height,
                    centerX + desiredWidth * 0.5f,
                    holeRect.yMin - CalloutGap);
                placement = Placement.Below;
                return true;
            }

            float aboveSpace = safeRect.yMax - holeRect.yMax - CalloutGap;
            if (aboveSpace >= MinimumCalloutHeight)
            {
                float height = Mathf.Min(desiredHeight, aboveSpace);
                float centerX = Mathf.Clamp(
                    holeRect.center.x,
                    safeRect.xMin + desiredWidth * 0.5f,
                    safeRect.xMax - desiredWidth * 0.5f);
                panelRect = Rect.MinMaxRect(
                    centerX - desiredWidth * 0.5f,
                    holeRect.yMax + CalloutGap,
                    centerX + desiredWidth * 0.5f,
                    holeRect.yMax + CalloutGap + height);
                placement = Placement.Above;
                return true;
            }

            float rightSpace = safeRect.xMax - holeRect.xMax - CalloutGap;
            if (rightSpace >= MinimumCalloutWidth)
            {
                float width = Mathf.Min(desiredWidth, rightSpace);
                float centerY = Mathf.Clamp(
                    holeRect.center.y,
                    safeRect.yMin + desiredHeight * 0.5f,
                    safeRect.yMax - desiredHeight * 0.5f);
                panelRect = Rect.MinMaxRect(
                    holeRect.xMax + CalloutGap,
                    centerY - desiredHeight * 0.5f,
                    holeRect.xMax + CalloutGap + width,
                    centerY + desiredHeight * 0.5f);
                placement = Placement.Right;
                return true;
            }

            float leftSpace = holeRect.xMin - CalloutGap - safeRect.xMin;
            if (leftSpace >= MinimumCalloutWidth)
            {
                float width = Mathf.Min(desiredWidth, leftSpace);
                float centerY = Mathf.Clamp(
                    holeRect.center.y,
                    safeRect.yMin + desiredHeight * 0.5f,
                    safeRect.yMax - desiredHeight * 0.5f);
                panelRect = Rect.MinMaxRect(
                    holeRect.xMin - CalloutGap - width,
                    centerY - desiredHeight * 0.5f,
                    holeRect.xMin - CalloutGap,
                    centerY + desiredHeight * 0.5f);
                placement = Placement.Left;
                return true;
            }

            panelRect = default(Rect);
            placement = Placement.Center;
            return false;
        }

        private void LayoutArrow(
            Placement placement,
            Rect panelRect,
            Rect holeRect)
        {
            if (placement == Placement.Center)
            {
                arrow.gameObject.SetActive(false);
                return;
            }

            arrow.gameObject.SetActive(true);
            Vector2 size;
            Vector2 center;
            switch (placement)
            {
                case Placement.Below:
                    size = new Vector2(ArrowLongSize, ArrowShortSize);
                    center = new Vector2(
                        Mathf.Clamp(
                            holeRect.center.x,
                            panelRect.xMin + ArrowLongSize,
                            panelRect.xMax - ArrowLongSize),
                        panelRect.yMax + ArrowShortSize * 0.5f);
                    arrow.SetDirection(TutorialArrowDirection.Up);
                    break;
                case Placement.Above:
                    size = new Vector2(ArrowLongSize, ArrowShortSize);
                    center = new Vector2(
                        Mathf.Clamp(
                            holeRect.center.x,
                            panelRect.xMin + ArrowLongSize,
                            panelRect.xMax - ArrowLongSize),
                        panelRect.yMin - ArrowShortSize * 0.5f);
                    arrow.SetDirection(TutorialArrowDirection.Down);
                    break;
                case Placement.Right:
                    size = new Vector2(ArrowShortSize, ArrowLongSize);
                    center = new Vector2(
                        panelRect.xMin - ArrowShortSize * 0.5f,
                        Mathf.Clamp(
                            holeRect.center.y,
                            panelRect.yMin + ArrowLongSize,
                            panelRect.yMax - ArrowLongSize));
                    arrow.SetDirection(TutorialArrowDirection.Left);
                    break;
                default:
                    size = new Vector2(ArrowShortSize, ArrowLongSize);
                    center = new Vector2(
                        panelRect.xMax + ArrowShortSize * 0.5f,
                        Mathf.Clamp(
                            holeRect.center.y,
                            panelRect.yMin + ArrowLongSize,
                            panelRect.yMax - ArrowLongSize));
                    arrow.SetDirection(TutorialArrowDirection.Right);
                    break;
            }

            SetLocalRect(arrow.rectTransform, CenteredRect(center, size));
        }

        private Rect ResolveSafeAreaLocal(Rect fallback)
        {
            Rect screenSafeArea = Screen.safeArea;
            if (screenSafeArea.width <= 0f || screenSafeArea.height <= 0f)
            {
                return fallback;
            }

            return TryScreenRectToLocal(screenSafeArea, out Rect safeLocal)
                ? Intersect(safeLocal, fallback)
                : fallback;
        }

        private bool TryScreenRectToLocal(
            Rect screenRect,
            out Rect localRect)
        {
            localRect = default(Rect);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    overlayRoot,
                    new Vector2(screenRect.xMin, screenRect.yMin),
                    null,
                    out Vector2 minimum) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    overlayRoot,
                    new Vector2(screenRect.xMax, screenRect.yMax),
                    null,
                    out Vector2 maximum))
            {
                return false;
            }

            localRect = Rect.MinMaxRect(
                Mathf.Min(minimum.x, maximum.x),
                Mathf.Min(minimum.y, maximum.y),
                Mathf.Max(minimum.x, maximum.x),
                Mathf.Max(minimum.y, maximum.y));
            return true;
        }

        private void SetAnchorAvailability(bool available)
        {
            if (hasResolvedAnchor == available)
            {
                return;
            }

            hasResolvedAnchor = available;
            AnchorAvailabilityChanged?.Invoke(available);
        }

        private void HandleNextClicked()
        {
            NextRequested?.Invoke();
        }

        private void HandleSkipClicked()
        {
            SkipRequested?.Invoke();
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            int fontSize,
            Color color,
            TextAnchor alignment)
        {
            Text label = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text))
                .GetComponent<Text>();
            label.transform.SetParent(parent, false);
            label.font = font;
            label.fontSize = fontSize;
            label.fontStyle = FontStyle.Normal;
            label.color = color;
            label.alignment = alignment;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private Button CreateButton(
            string objectName,
            Transform parent,
            Color color,
            string label)
        {
            Image image = CreateImage(
                objectName,
                parent,
                color,
                true);
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.disabledColor = new Color(0.52f, 0.52f, 0.52f, 0.72f);
            colors.colorMultiplier = 1f;
            button.colors = colors;

            Text text = CreateText(
                "Label",
                button.transform,
                19,
                BodyColor,
                TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 8f, 4f);
            text.text = label;
            return button;
        }

        private static void SetButtonLabel(Button button, string value)
        {
            if (button == null)
            {
                return;
            }

            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.text = value ?? string.Empty;
            }
        }

        private static RectTransform CreateRect(
            string objectName,
            Transform parent)
        {
            RectTransform rect = new GameObject(
                objectName,
                typeof(RectTransform))
                .GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image CreateImage(
            string objectName,
            Transform parent,
            Color color,
            bool raycastTarget)
        {
            Image image = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image))
                .GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        private static void SetBottomButtonRect(
            RectTransform rect,
            bool alignRight)
        {
            rect.anchorMin = alignRight
                ? new Vector2(1f, 0f)
                : Vector2.zero;
            rect.anchorMax = rect.anchorMin;
            rect.pivot = alignRight
                ? new Vector2(1f, 0f)
                : Vector2.zero;
            rect.anchoredPosition = alignRight
                ? new Vector2(-20f, 18f)
                : new Vector2(20f, 18f);
            rect.sizeDelta = new Vector2(170f, 48f);
        }

        private static void SetPanelRect(Image panel, Rect rect)
        {
            bool valid = rect.width > 0.01f && rect.height > 0.01f;
            panel.gameObject.SetActive(valid);
            if (valid)
            {
                SetLocalRect(panel.rectTransform, rect);
            }
        }

        private static void SetLocalRect(RectTransform target, Rect rect)
        {
            target.anchorMin = new Vector2(0.5f, 0.5f);
            target.anchorMax = new Vector2(0.5f, 0.5f);
            target.pivot = new Vector2(0.5f, 0.5f);
            target.anchoredPosition = rect.center;
            target.sizeDelta = rect.size;
        }

        private static void Stretch(
            RectTransform target,
            float horizontalInset = 0f,
            float verticalInset = 0f)
        {
            target.anchorMin = Vector2.zero;
            target.anchorMax = Vector2.one;
            target.offsetMin = new Vector2(
                horizontalInset,
                verticalInset);
            target.offsetMax = new Vector2(
                -horizontalInset,
                -verticalInset);
        }

        private static void Anchor(
            RectTransform target,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            target.anchorMin = anchorMin;
            target.anchorMax = anchorMax;
            target.offsetMin = offsetMin;
            target.offsetMax = offsetMax;
        }

        private static Rect Intersect(Rect first, Rect second)
        {
            float minX = Mathf.Max(first.xMin, second.xMin);
            float minY = Mathf.Max(first.yMin, second.yMin);
            float maxX = Mathf.Min(first.xMax, second.xMax);
            float maxY = Mathf.Min(first.yMax, second.yMax);
            return maxX > minX && maxY > minY
                ? Rect.MinMaxRect(minX, minY, maxX, maxY)
                : default(Rect);
        }

        private static Rect Inset(
            Rect source,
            float horizontal,
            float vertical)
        {
            return Rect.MinMaxRect(
                source.xMin + horizontal,
                source.yMin + vertical,
                source.xMax - horizontal,
                source.yMax - vertical);
        }

        private static Rect CenteredRect(Vector2 center, Vector2 size)
        {
            Vector2 half = size * 0.5f;
            return Rect.MinMaxRect(
                center.x - half.x,
                center.y - half.y,
                center.x + half.x,
                center.y + half.y);
        }

        private static Rect ClampInside(Rect source, Rect bounds)
        {
            if (source.width >= bounds.width || source.height >= bounds.height)
            {
                return CenteredRect(
                    bounds.center,
                    new Vector2(
                        Mathf.Min(source.width, bounds.width),
                        Mathf.Min(source.height, bounds.height)));
            }

            Vector2 center = new Vector2(
                Mathf.Clamp(
                    source.center.x,
                    bounds.xMin + source.width * 0.5f,
                    bounds.xMax - source.width * 0.5f),
                Mathf.Clamp(
                    source.center.y,
                    bounds.yMin + source.height * 0.5f,
                    bounds.yMax - source.height * 0.5f));
            return CenteredRect(center, source.size);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            new GameObject(
                "Tutorial EventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
        }
    }

    internal enum TutorialArrowDirection
    {
        Up,
        Down,
        Left,
        Right
    }

    /// <summary>
    /// Texture-free triangle so the tutorial pointer works in WebGL without
    /// adding an asset dependency or relying on font arrow glyph coverage.
    /// </summary>
    internal sealed class TutorialArrowGraphic : MaskableGraphic
    {
        private TutorialArrowDirection direction =
            TutorialArrowDirection.Up;

        public void SetDirection(TutorialArrowDirection value)
        {
            if (direction == value)
            {
                return;
            }

            direction = value;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = GetPixelAdjustedRect();
            Vector2 first;
            Vector2 second;
            Vector2 point;
            switch (direction)
            {
                case TutorialArrowDirection.Down:
                    first = new Vector2(rect.xMin, rect.yMax);
                    second = new Vector2(rect.xMax, rect.yMax);
                    point = new Vector2(rect.center.x, rect.yMin);
                    break;
                case TutorialArrowDirection.Left:
                    first = new Vector2(rect.xMax, rect.yMin);
                    second = new Vector2(rect.xMax, rect.yMax);
                    point = new Vector2(rect.xMin, rect.center.y);
                    break;
                case TutorialArrowDirection.Right:
                    first = new Vector2(rect.xMin, rect.yMax);
                    second = new Vector2(rect.xMin, rect.yMin);
                    point = new Vector2(rect.xMax, rect.center.y);
                    break;
                default:
                    first = new Vector2(rect.xMin, rect.yMin);
                    second = new Vector2(rect.xMax, rect.yMin);
                    point = new Vector2(rect.center.x, rect.yMax);
                    break;
            }

            Color32 vertexColor = color;
            vertexHelper.AddVert(first, vertexColor, Vector2.zero);
            vertexHelper.AddVert(second, vertexColor, Vector2.right);
            vertexHelper.AddVert(point, vertexColor, Vector2.up);
            vertexHelper.AddTriangle(0, 1, 2);
        }
    }
}
