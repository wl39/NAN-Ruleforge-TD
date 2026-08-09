using System;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.Simulation;
using UnityEngine;

namespace RuleforgeTD.Battle
{
    /// <summary>
    /// Runtime driver for the browser-facing VFX review page. It deliberately
    /// uses the exact Stage 01 palette and renderer so the gallery cannot drift
    /// into a second, showcase-only visual implementation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CardEffectVfxGallery : MonoBehaviour
    {
        public const int ColumnCount = 5;
        public const int ReviewFramesPerSecond = 30;
        public const float HorizontalSpacing = 2.24f;
        public const float VerticalSpacing = 1.82f;
        public const float DefaultReplayInterval = 1.15f;
        public const float DesktopAspectThreshold = 1.3f;
        public const float TabletAspectThreshold = 0.8f;
        public const float NarrowPhoneAspectThreshold = 0.42f;

        private const float DesktopOrthographicSize = 3.7f;
        private const float TabletOrthographicSize = 4.15f;
        private const float PhoneOrthographicSize = 4.85f;
        private const float ScrollWheelStep = 0.7f;

        [SerializeField]
        private StageOneCardEffectVfxView vfxView;

        [SerializeField]
        private StageOnePresentationCatalog presentationCatalog;

        [SerializeField]
        private Camera galleryCamera;

        [SerializeField]
        private Transform headerRoot;

        [SerializeField]
        private Transform[] cardRoots = Array.Empty<Transform>();

        [SerializeField]
        [Min(0.75f)]
        private float replayInterval = DefaultReplayInterval;

        private bool isPlaying = true;
        private float playbackElapsedTime;
        private float maximumEffectDuration;
        private int currentFrame;
        private int totalFrameCount;
        private GUIStyle panelStyle;
        private GUIStyle buttonStyle;
        private GUIStyle primaryButtonStyle;
        private GUIStyle frameLabelStyle;
        private GUIStyle hintLabelStyle;
        private float scrollNormalized;
        private float scrollTopY;
        private float scrollBottomY;
        private int activeColumnCount = ColumnCount;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private bool isPointerDragging;
        private Vector2 previousPointerPosition;
        private StageOneCardEffectStyle[] effectStyles =
            Array.Empty<StageOneCardEffectStyle>();

        public int EffectCount =>
            effectStyles.Length;
        public float ReplayInterval =>
            replayInterval;
        public bool IsPlaying =>
            isPlaying;
        public int CurrentFrame =>
            currentFrame;
        public int TotalFrameCount =>
            totalFrameCount;
        public float MaximumEffectDuration =>
            maximumEffectDuration;
        public float ScrollNormalized =>
            scrollNormalized;
        public int ActiveColumnCount =>
            activeColumnCount;
        public bool CanScroll =>
            scrollTopY > scrollBottomY + 0.001f;

        private void Awake()
        {
            InitializeEffectStyles();
            if (vfxView == null)
            {
                vfxView =
                    StageOneCardEffectVfxView.CreateRuntime(
                        transform);
            }

            vfxView.InitializeNow(
                Mathf.Max(
                    StageOneCardEffectVfxView.DefaultPoolCapacity,
                    effectStyles.Length));
            RecalculateTimeline();
        }

        private void Start()
        {
            ApplyResponsiveLayout(true);
            PlayAllNow();
        }

        private void Update()
        {
            if (lastScreenWidth != Screen.width ||
                lastScreenHeight != Screen.height)
            {
                ApplyResponsiveLayout(false);
                PlayAllNow();
            }

            HandleViewportInput();
            HandleKeyboard();
            if (!isPlaying)
            {
                return;
            }

            playbackElapsedTime += Time.unscaledDeltaTime;
            if (playbackElapsedTime >= replayInterval)
            {
                PlayAllNow();
                return;
            }

            SetPreviewTime(playbackElapsedTime);
        }

        public void Configure(
            StageOneCardEffectVfxView effectView,
            float interval = DefaultReplayInterval)
        {
            vfxView = effectView;
            replayInterval = Mathf.Max(0.75f, interval);
            RecalculateTimeline();
        }

        public void Configure(
            StageOneCardEffectVfxView effectView,
            StageOnePresentationCatalog catalog,
            Camera camera,
            Transform galleryHeader,
            Transform[] galleryCardRoots,
            float interval = DefaultReplayInterval)
        {
            vfxView = effectView;
            presentationCatalog = catalog;
            galleryCamera = camera;
            headerRoot = galleryHeader;
            cardRoots = galleryCardRoots ?? Array.Empty<Transform>();
            replayInterval = Mathf.Max(0.75f, interval);
            effectStyles = Array.Empty<StageOneCardEffectStyle>();
            InitializeEffectStyles();
            RecalculateTimeline();
        }

        public void PlayAllNow()
        {
            if (vfxView == null)
            {
                return;
            }

            vfxView.StopAll();
            for (int i = 0; i < effectStyles.Length; i++)
            {
                StageOneCardEffectStyle style =
                    effectStyles[i];
                Vector3 center =
                    GetSlotPosition(
                        i,
                        effectStyles.Length,
                        activeColumnCount);
                if (UsesDirectionalLink(style.Shape))
                {
                    Vector3 linkOffset =
                        Vector3.right * 0.54f;
                    vfxView.PlayLink(
                        style.Id,
                        center - linkOffset,
                        center + linkOffset);
                }
                else
                {
                    vfxView.Play(style.Id, center);
                }
            }

            playbackElapsedTime = 0f;
            currentFrame = 0;
            vfxView.SetManualPreviewTime(0f);
        }

        public void TogglePlayback()
        {
            isPlaying = !isPlaying;
            if (!isPlaying)
            {
                SetFrame(currentFrame);
            }
        }

        public void SetFrame(int frame)
        {
            isPlaying = false;
            currentFrame = Mathf.Clamp(
                frame,
                0,
                Mathf.Max(0, totalFrameCount - 1));
            playbackElapsedTime =
                currentFrame /
                (float)ReviewFramesPerSecond;
            SetPreviewTime(playbackElapsedTime);
        }

        public void StepFrame(int frameDelta)
        {
            SetFrame(currentFrame + frameDelta);
        }

        public void Replay()
        {
            isPlaying = true;
            PlayAllNow();
        }

        public static Vector3 GetSlotPosition(int index)
        {
            return GetSlotPosition(
                index,
                StageOneCardEffectPalette.StyleCount);
        }

        public static Vector3 GetSlotPosition(
            int index,
            int effectCount)
        {
            return GetSlotPosition(
                index,
                effectCount,
                ColumnCount);
        }

        public static Vector3 GetSlotPosition(
            int index,
            int effectCount,
            int columnCount)
        {
            int safeCount = Mathf.Max(1, effectCount);
            int safeColumnCount = Mathf.Min(
                Mathf.Max(1, columnCount),
                safeCount);
            int column = Mathf.Max(0, index) % safeColumnCount;
            int row = Mathf.Max(0, index) / safeColumnCount;
            int rowCount = GetRowCount(
                safeCount,
                safeColumnCount);
            float firstColumnX =
                -(safeColumnCount - 1) * HorizontalSpacing * 0.5f;
            float firstRowY =
                (rowCount - 1) * VerticalSpacing * 0.5f;
            return new Vector3(
                firstColumnX +
                column * HorizontalSpacing,
                firstRowY -
                row * VerticalSpacing +
                0.18f,
                0f);
        }

        public static int GetRowCount(int effectCount)
        {
            return GetRowCount(effectCount, ColumnCount);
        }

        public static int GetRowCount(
            int effectCount,
            int columnCount)
        {
            int safeColumnCount = Mathf.Max(1, columnCount);
            return Mathf.Max(
                1,
                (Mathf.Max(0, effectCount) + safeColumnCount - 1) /
                safeColumnCount);
        }

        public static int GetColumnCountForAspect(float aspect)
        {
            if (aspect >= DesktopAspectThreshold)
            {
                return ColumnCount;
            }

            if (aspect >= TabletAspectThreshold)
            {
                return 3;
            }

            return aspect < NarrowPhoneAspectThreshold ? 1 : 2;
        }

        private static bool UsesDirectionalLink(
            StageOneCardEffectShape shape)
        {
            return shape == StageOneCardEffectShape.Arc ||
                   shape == StageOneCardEffectShape.Chain ||
                   shape == StageOneCardEffectShape.Lightning ||
                   shape == StageOneCardEffectShape.Transfer ||
                   shape == StageOneCardEffectShape.Lance ||
                   shape == StageOneCardEffectShape.Streak ||
                   shape == StageOneCardEffectShape.Return ||
                   shape == StageOneCardEffectShape.Rewind ||
                   shape == StageOneCardEffectShape.Relay ||
                   shape == StageOneCardEffectShape.Recursion ||
                   shape == StageOneCardEffectShape.LastCommand;
        }

        private void RecalculateTimeline()
        {
            InitializeEffectStyles();
            maximumEffectDuration = 0f;
            for (int i = 0; i < effectStyles.Length; i++)
            {
                maximumEffectDuration = Mathf.Max(
                    maximumEffectDuration,
                    effectStyles[i].Duration);
            }

            totalFrameCount = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    maximumEffectDuration *
                    ReviewFramesPerSecond));
            replayInterval = Mathf.Max(
                replayInterval,
                maximumEffectDuration + 0.1f);
            currentFrame = Mathf.Clamp(
                currentFrame,
                0,
                totalFrameCount - 1);
        }

        private void InitializeEffectStyles()
        {
            if (effectStyles.Length > 0)
            {
                return;
            }

            if (presentationCatalog != null &&
                presentationCatalog.ContentJson != null)
            {
                CompiledContent content =
                    LogicContentJsonLoader.Load(
                        presentationCatalog.ContentJson,
                        presentationCatalog.CardContentModules);
                effectStyles = StageOneCardEffectPalette
                    .CreateCardGalleryStyles(content);
                return;
            }

            int authoredCount =
                StageOneCardEffectPalette.StyleCount;
            effectStyles =
                new StageOneCardEffectStyle[authoredCount];
            for (int i = 0; i < authoredCount; i++)
            {
                effectStyles[i] =
                    StageOneCardEffectPalette.GetStyle(i);
            }
        }

        private void SetPreviewTime(float elapsedTime)
        {
            if (vfxView == null)
            {
                return;
            }

            float maximumSampleTime =
                (totalFrameCount - 1) /
                (float)ReviewFramesPerSecond;
            float previewTime = Mathf.Clamp(
                elapsedTime,
                0f,
                maximumSampleTime);
            currentFrame = Mathf.Clamp(
                Mathf.FloorToInt(
                    previewTime *
                    ReviewFramesPerSecond),
                0,
                totalFrameCount - 1);
            vfxView.SetManualPreviewTime(previewTime);
        }

        public void SetScrollNormalized(float value)
        {
            scrollNormalized = Mathf.Clamp01(value);
            if (galleryCamera == null)
            {
                return;
            }

            Vector3 position = galleryCamera.transform.position;
            position.y = Mathf.Lerp(
                scrollTopY,
                scrollBottomY,
                scrollNormalized);
            galleryCamera.transform.position = position;
        }

        private void ApplyResponsiveLayout(bool resetScroll)
        {
            if (galleryCamera == null)
            {
                galleryCamera = Camera.main;
            }

            if (galleryCamera == null)
            {
                return;
            }

            float preservedScroll = scrollNormalized;
            float aspect = Screen.height > 0
                ? Screen.width / (float)Screen.height
                : 16f / 9f;
            activeColumnCount =
                GetColumnCountForAspect(aspect);

            int layoutCount = Mathf.Min(
                effectStyles.Length,
                cardRoots != null ? cardRoots.Length : 0);
            for (int i = 0; i < layoutCount; i++)
            {
                if (cardRoots[i] == null)
                {
                    continue;
                }

                cardRoots[i].position =
                    GetSlotPosition(
                        i,
                        effectStyles.Length,
                        activeColumnCount);
            }

            int rowCount = GetRowCount(
                effectStyles.Length,
                activeColumnCount);
            float gridHalfHeight =
                (rowCount - 1) * VerticalSpacing * 0.5f;
            if (headerRoot != null)
            {
                headerRoot.position =
                    new Vector3(
                        0f,
                        gridHalfHeight + 1.93f,
                        0f);
            }

            galleryCamera.orthographic = true;
            galleryCamera.orthographicSize =
                GetOrthographicSizeForAspect(aspect);

            float halfViewHeight =
                galleryCamera.orthographicSize;
            float contentTop = gridHalfHeight + 2.72f;
            float contentBottom = -gridHalfHeight - 1.05f;
            float controlsWorldHeight =
                halfViewHeight * 2f *
                GetControlPanelHeight() /
                Mathf.Max(1f, Screen.height);
            scrollTopY = contentTop - halfViewHeight;
            scrollBottomY =
                contentBottom +
                halfViewHeight +
                controlsWorldHeight;
            if (scrollTopY < scrollBottomY)
            {
                float centeredY =
                    (contentTop + contentBottom) * 0.5f;
                scrollTopY = centeredY;
                scrollBottomY = centeredY;
            }

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            SetScrollNormalized(
                resetScroll ? 0f : preservedScroll);
        }

        private static float GetOrthographicSizeForAspect(float aspect)
        {
            if (aspect >= DesktopAspectThreshold)
            {
                return DesktopOrthographicSize;
            }

            if (aspect >= TabletAspectThreshold)
            {
                return TabletOrthographicSize;
            }

            return PhoneOrthographicSize;
        }

        private static float GetControlPanelHeight()
        {
            return Screen.width < 700 ? 104f : 86f;
        }

        private void HandleViewportInput()
        {
            if (!CanScroll || galleryCamera == null)
            {
                isPointerDragging = false;
                return;
            }

            float wheelDelta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheelDelta) > 0.001f)
            {
                MoveCamera(wheelDelta * ScrollWheelStep);
            }

            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began)
                {
                    isPointerDragging =
                        touch.position.y > GetControlPanelHeight();
                    previousPointerPosition = touch.position;
                }
                else if (isPointerDragging &&
                         touch.phase == TouchPhase.Moved)
                {
                    MoveCamera(
                        -touch.deltaPosition.y /
                        Mathf.Max(1f, Screen.height) *
                        galleryCamera.orthographicSize * 2f);
                    previousPointerPosition = touch.position;
                }
                else if (touch.phase == TouchPhase.Ended ||
                         touch.phase == TouchPhase.Canceled)
                {
                    isPointerDragging = false;
                }

                return;
            }

            Vector2 pointerPosition = Input.mousePosition;
            if (Input.GetMouseButtonDown(0))
            {
                isPointerDragging =
                    pointerPosition.y > GetControlPanelHeight() &&
                    pointerPosition.x < Screen.width - 28f;
                previousPointerPosition = pointerPosition;
            }
            else if (isPointerDragging &&
                     Input.GetMouseButton(0))
            {
                float pointerDeltaY =
                    pointerPosition.y -
                    previousPointerPosition.y;
                MoveCamera(
                    -pointerDeltaY /
                    Mathf.Max(1f, Screen.height) *
                    galleryCamera.orthographicSize * 2f);
                previousPointerPosition = pointerPosition;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                isPointerDragging = false;
            }
        }

        private void MoveCamera(float worldDeltaY)
        {
            Vector3 position = galleryCamera.transform.position;
            position.y = Mathf.Clamp(
                position.y + worldDeltaY,
                scrollBottomY,
                scrollTopY);
            galleryCamera.transform.position = position;
            float range = scrollTopY - scrollBottomY;
            scrollNormalized = range > 0.001f
                ? Mathf.Clamp01(
                    (scrollTopY - position.y) / range)
                : 0f;
        }

        private void HandleKeyboard()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                TogglePlayback();
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow) ||
                     Input.GetKeyDown(KeyCode.Comma))
            {
                StepFrame(-1);
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow) ||
                     Input.GetKeyDown(KeyCode.Period))
            {
                StepFrame(1);
            }
            else if (Input.GetKeyDown(KeyCode.Home))
            {
                SetFrame(0);
            }
            else if (Input.GetKeyDown(KeyCode.End))
            {
                SetFrame(totalFrameCount - 1);
            }
            else if (Input.GetKeyDown(KeyCode.R))
            {
                Replay();
            }
            else if (Input.GetKeyDown(KeyCode.PageUp))
            {
                MoveCamera(galleryCamera.orthographicSize * 1.4f);
            }
            else if (Input.GetKeyDown(KeyCode.PageDown))
            {
                MoveCamera(-galleryCamera.orthographicSize * 1.4f);
            }
        }

        private void OnGUI()
        {
            EnsureGuiStyles();

            float panelHeight = GetControlPanelHeight();
            float panelY = Screen.height - panelHeight;
            Rect panelRect = new Rect(
                0f,
                panelY,
                Screen.width,
                panelHeight);
            Color previousGuiColor = GUI.color;
            GUI.color =
                new Color(0.025f, 0.045f, 0.06f, 0.98f);
            GUI.Box(panelRect, GUIContent.none, panelStyle);
            GUI.color = previousGuiColor;

            DrawScrollBar(panelY);
            if (Screen.width < 700)
            {
                DrawCompactControls(panelY);
                return;
            }

            DrawDesktopControls(panelY);
        }

        private void DrawScrollBar(float panelY)
        {
            if (!CanScroll)
            {
                return;
            }

            const float margin = 10f;
            float trackHeight = Mathf.Max(
                80f,
                panelY - margin * 2f);
            float contentHeight =
                Mathf.Max(
                    galleryCamera.orthographicSize * 2f,
                    scrollTopY - scrollBottomY +
                    galleryCamera.orthographicSize * 2f);
            float thumbSize = Mathf.Clamp(
                galleryCamera.orthographicSize * 2f /
                contentHeight,
                0.08f,
                0.85f);
            float selectedScroll = GUI.VerticalScrollbar(
                new Rect(
                    Screen.width - 18f,
                    margin,
                    12f,
                    trackHeight),
                scrollNormalized,
                thumbSize,
                0f,
                1f);
            if (!Mathf.Approximately(
                    selectedScroll,
                    scrollNormalized))
            {
                SetScrollNormalized(selectedScroll);
            }

            GUI.Label(
                new Rect(
                    Mathf.Max(0f, Screen.width - 132f),
                    10f,
                    104f,
                    22f),
                "SCROLL · DRAG",
                hintLabelStyle);

        }

        private void DrawDesktopControls(float panelY)
        {
            const float margin = 18f;
            const float buttonHeight = 32f;
            float controlsY = panelY + 10f;
            float x = margin;
            if (GUI.Button(
                    new Rect(x, controlsY, 82f, buttonHeight),
                    isPlaying ? "PAUSE" : "PLAY",
                    primaryButtonStyle))
            {
                TogglePlayback();
            }

            x += 90f;
            if (GUI.Button(
                    new Rect(x, controlsY, 38f, buttonHeight),
                    "|<",
                    buttonStyle))
            {
                SetFrame(0);
            }

            x += 42f;
            if (GUI.Button(
                    new Rect(x, controlsY, 38f, buttonHeight),
                    "<",
                    buttonStyle))
            {
                StepFrame(-1);
            }

            x += 42f;
            if (GUI.Button(
                    new Rect(x, controlsY, 38f, buttonHeight),
                    ">",
                    buttonStyle))
            {
                StepFrame(1);
            }

            x += 42f;
            if (GUI.Button(
                    new Rect(x, controlsY, 38f, buttonHeight),
                    ">|",
                    buttonStyle))
            {
                SetFrame(totalFrameCount - 1);
            }

            x += 50f;
            float labelWidth =
                Mathf.Clamp(Screen.width * 0.24f, 250f, 390f);
            float sliderWidth = Mathf.Max(
                120f,
                Screen.width - x - labelWidth - margin);
            int selectedFrame = Mathf.RoundToInt(
                GUI.HorizontalSlider(
                    new Rect(
                        x,
                        controlsY + 7f,
                        sliderWidth,
                        buttonHeight),
                    currentFrame,
                    0f,
                    Mathf.Max(0, totalFrameCount - 1)));
            if (selectedFrame != currentFrame)
            {
                SetFrame(selectedFrame);
            }

            x += sliderWidth + 12f;
            string playbackState =
                isPlaying ? "PLAYING" : "PAUSED";
            string frameText = string.Format(
                "FRAME {0:00} / {1:00}   {2:0.000}s   {3} FPS   {4}",
                currentFrame + 1,
                totalFrameCount,
                currentFrame /
                (float)ReviewFramesPerSecond,
                ReviewFramesPerSecond,
                playbackState);
            GUI.Label(
                new Rect(
                    x,
                    controlsY,
                    labelWidth,
                    buttonHeight),
                frameText,
                frameLabelStyle);

            GUI.Label(
                new Rect(
                    margin,
                    panelY + 50f,
                    Screen.width - margin * 2f,
                    24f),
                "SCROLL / DRAG  BROWSE     SPACE  PLAY / PAUSE     LEFT · RIGHT  FRAME STEP     R  REPLAY",
                hintLabelStyle);
        }

        private void DrawCompactControls(float panelY)
        {
            const float margin = 10f;
            const float gap = 5f;
            const float buttonHeight = 36f;
            float controlsY = panelY + 8f;
            float compactButtonWidth = Mathf.Clamp(
                (Screen.width - margin * 2f - gap * 4f) / 5f,
                44f,
                72f);
            float x = margin;
            if (GUI.Button(
                    new Rect(
                        x,
                        controlsY,
                        compactButtonWidth,
                        buttonHeight),
                    isPlaying ? "PAUSE" : "PLAY",
                    primaryButtonStyle))
            {
                TogglePlayback();
            }

            x += compactButtonWidth + gap;
            if (GUI.Button(
                    new Rect(
                        x,
                        controlsY,
                        compactButtonWidth,
                        buttonHeight),
                    "|<",
                    buttonStyle))
            {
                SetFrame(0);
            }

            x += compactButtonWidth + gap;
            if (GUI.Button(
                    new Rect(
                        x,
                        controlsY,
                        compactButtonWidth,
                        buttonHeight),
                    "<",
                    buttonStyle))
            {
                StepFrame(-1);
            }

            x += compactButtonWidth + gap;
            if (GUI.Button(
                    new Rect(
                        x,
                        controlsY,
                        compactButtonWidth,
                        buttonHeight),
                    ">",
                    buttonStyle))
            {
                StepFrame(1);
            }

            x += compactButtonWidth + gap;
            if (GUI.Button(
                    new Rect(
                        x,
                        controlsY,
                        compactButtonWidth,
                        buttonHeight),
                    "R",
                    buttonStyle))
            {
                Replay();
            }

            float sliderY = controlsY + buttonHeight + 12f;
            int selectedFrame = Mathf.RoundToInt(
                GUI.HorizontalSlider(
                    new Rect(
                        margin,
                        sliderY + 5f,
                        Screen.width - margin * 2f - 76f,
                        24f),
                    currentFrame,
                    0f,
                    Mathf.Max(0, totalFrameCount - 1)));
            if (selectedFrame != currentFrame)
            {
                SetFrame(selectedFrame);
            }

            GUI.Label(
                new Rect(
                    Screen.width - margin - 68f,
                    sliderY - 4f,
                    68f,
                    28f),
                string.Format(
                    "{0:00}/{1:00}",
                    currentFrame + 1,
                    totalFrameCount),
                frameLabelStyle);
        }

        private void EnsureGuiStyles()
        {
            if (panelStyle != null)
            {
                return;
            }

            panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background =
                Texture2D.whiteTexture;
            panelStyle.normal.textColor = Color.white;

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            buttonStyle.normal.textColor =
                new Color(0.78f, 0.88f, 0.92f, 1f);
            buttonStyle.hover.textColor = Color.white;

            primaryButtonStyle = new GUIStyle(buttonStyle);
            primaryButtonStyle.normal.textColor =
                new Color(0.42f, 0.9f, 1f, 1f);

            frameLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            };
            frameLabelStyle.normal.textColor =
                new Color(0.82f, 0.91f, 0.95f, 1f);

            hintLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter
            };
            hintLabelStyle.normal.textColor =
                new Color(0.49f, 0.64f, 0.7f, 1f);
        }
    }
}
