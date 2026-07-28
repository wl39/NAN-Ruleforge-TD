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
        public const int ColumnCount = 8;
        public const int RowCount = 4;
        public const int ReviewFramesPerSecond = 30;
        public const float HorizontalSpacing = 2.24f;
        public const float VerticalSpacing = 1.82f;
        public const float FirstColumnX = -7.84f;
        public const float FirstRowY = 3.05f;
        public const float DefaultReplayInterval = 1.15f;

        [SerializeField]
        private StageOneCardEffectVfxView vfxView;

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

        public int EffectCount =>
            StageOneCardEffectPalette.StyleCount;
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

        private void Awake()
        {
            if (vfxView == null)
            {
                vfxView =
                    StageOneCardEffectVfxView.CreateRuntime(
                        transform);
            }

            vfxView.InitializeNow();
            RecalculateTimeline();
        }

        private void Start()
        {
            PlayAllNow();
        }

        private void Update()
        {
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

        public void PlayAllNow()
        {
            if (vfxView == null)
            {
                return;
            }

            vfxView.StopAll();
            for (int i = 0;
                 i < StageOneCardEffectPalette.StyleCount;
                 i++)
            {
                StageOneCardEffectStyle style =
                    StageOneCardEffectPalette.GetStyle(i);
                Vector3 center = GetSlotPosition(i);
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
            int column = Mathf.Clamp(
                index % ColumnCount,
                0,
                ColumnCount - 1);
            int row = Mathf.Clamp(
                index / ColumnCount,
                0,
                RowCount - 1);
            return new Vector3(
                FirstColumnX +
                column * HorizontalSpacing,
                FirstRowY -
                row * VerticalSpacing +
                0.18f,
                0f);
        }

        private static bool UsesDirectionalLink(
            StageOneCardEffectShape shape)
        {
            return shape == StageOneCardEffectShape.Arc ||
                   shape == StageOneCardEffectShape.Chain ||
                   shape == StageOneCardEffectShape.Lightning ||
                   shape == StageOneCardEffectShape.Transfer ||
                   shape == StageOneCardEffectShape.Lance ||
                   shape == StageOneCardEffectShape.Streak;
        }

        private void RecalculateTimeline()
        {
            maximumEffectDuration = 0f;
            for (int i = 0;
                 i < StageOneCardEffectPalette.StyleCount;
                 i++)
            {
                maximumEffectDuration = Mathf.Max(
                    maximumEffectDuration,
                    StageOneCardEffectPalette
                        .GetStyle(i)
                        .Duration);
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
        }

        private void OnGUI()
        {
            EnsureGuiStyles();

            const float panelHeight = 86f;
            const float margin = 18f;
            const float buttonHeight = 32f;
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
                "SPACE  PLAY / PAUSE     LEFT · RIGHT  FRAME STEP     HOME · END  FIRST / LAST     R  REPLAY",
                hintLabelStyle);
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
