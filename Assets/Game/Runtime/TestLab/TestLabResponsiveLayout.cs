using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.UnityView.TestLab
{
    /// <summary>
    /// TestLab 캔버스의 화면 배율과 루트 배치만 담당한다.
    /// 전투·샌드박스 명령에는 관여하지 않으며 화면 회전에도 즉시 대응한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class TestLabResponsiveLayout : MonoBehaviour
    {
        public static readonly Vector2 DesktopReferenceResolution =
            new Vector2(1920f, 1080f);
        public static readonly Vector2 MobilePortraitReferenceResolution =
            new Vector2(390f, 844f);
        public static readonly Vector2 MobileLandscapeReferenceResolution =
            new Vector2(844f, 390f);

        public const float DesktopPanelWidth = 500f;
        public const float MobileLandscapePanelWidth = 380f;
        public const float MobileMargin = 8f;
        public const float MobileHudInset = 96f;
        public const float MobileToolbarHeight = 68f;
        public const float MobilePanelGap = 8f;
        public const float MobileReopenHeight = 48f;

        private CanvasScaler canvasScaler;
        private RectTransform panelRoot;
        private RectTransform debuffToolbarRoot;
        private RectTransform reopenButtonRoot;
        private int lastScreenWidth = -1;
        private int lastScreenHeight = -1;
        private bool lastHandheld;

        public bool IsCompactLayout { get; private set; }
        public bool IsPortraitLayout { get; private set; }

        private void OnEnable()
        {
            ApplyLayout();
        }

        private void Update()
        {
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            bool handheld = IsHandheldDevice();
            if (width != lastScreenWidth ||
                height != lastScreenHeight ||
                handheld != lastHandheld)
            {
                ApplyLayout(width, height, handheld);
            }
        }

        public void Configure(
            RectTransform panel,
            RectTransform debuffToolbar,
            RectTransform reopenButton)
        {
            panelRoot = panel;
            debuffToolbarRoot = debuffToolbar;
            reopenButtonRoot = reopenButton;
            ApplyLayout();
        }

        public void ApplyLayout()
        {
            ApplyLayout(
                Mathf.Max(1, Screen.width),
                Mathf.Max(1, Screen.height),
                IsHandheldDevice());
        }

        public void ApplyLayout(
            int screenWidth,
            int screenHeight,
            bool handheld)
        {
            if (canvasScaler == null)
            {
                canvasScaler = GetComponent<CanvasScaler>();
            }

            int width = Mathf.Max(1, screenWidth);
            int height = Mathf.Max(1, screenHeight);
            IsPortraitLayout = height > width;
            IsCompactLayout = ShouldUseCompactLayout(
                width,
                height,
                handheld);

            canvasScaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            canvasScaler.matchWidthOrHeight =
                IsCompactLayout ? 0.5f : 1f;
            canvasScaler.referenceResolution =
                IsCompactLayout
                    ? IsPortraitLayout
                        ? MobilePortraitReferenceResolution
                        : MobileLandscapeReferenceResolution
                    : DesktopReferenceResolution;

            if (panelRoot != null &&
                debuffToolbarRoot != null &&
                reopenButtonRoot != null)
            {
                if (!IsCompactLayout)
                {
                    ApplyDesktopLayout();
                }
                else if (IsPortraitLayout)
                {
                    ApplyMobilePortraitLayout();
                }
                else
                {
                    ApplyMobileLandscapeLayout();
                }
            }

            lastScreenWidth = width;
            lastScreenHeight = height;
            lastHandheld = handheld;
        }

        public static bool ShouldUseCompactLayout(
            int screenWidth,
            int screenHeight,
            bool handheld)
        {
            int width = Mathf.Max(1, screenWidth);
            int height = Mathf.Max(1, screenHeight);
            int shortest = Mathf.Min(width, height);
            int longest = Mathf.Max(width, height);
            return handheld ||
                   height > width ||
                   (shortest <= 820 &&
                    longest <= 1400);
        }

        private void ApplyDesktopLayout()
        {
            panelRoot.anchorMin = new Vector2(1f, 0f);
            panelRoot.anchorMax = new Vector2(1f, 1f);
            panelRoot.pivot = new Vector2(1f, 0.5f);
            panelRoot.sizeDelta =
                new Vector2(DesktopPanelWidth, -136f);
            panelRoot.anchoredPosition =
                new Vector2(-12f, -56f);

            StretchTop(
                debuffToolbarRoot,
                12f,
                524f,
                112f,
                180f);
            AnchorReopenButton(
                new Vector2(112f, 42f),
                new Vector2(-12f, -112f));
        }

        private void ApplyMobileLandscapeLayout()
        {
            panelRoot.anchorMin = new Vector2(1f, 0f);
            panelRoot.anchorMax = new Vector2(1f, 1f);
            panelRoot.pivot = new Vector2(1f, 0.5f);
            panelRoot.sizeDelta =
                new Vector2(
                    MobileLandscapePanelWidth,
                    -(MobileHudInset +
                      MobileMargin));
            panelRoot.anchoredPosition =
                new Vector2(
                    -MobileMargin,
                    -(MobileHudInset -
                      MobileMargin) * 0.5f);

            StretchTop(
                debuffToolbarRoot,
                MobileMargin,
                MobileLandscapePanelWidth +
                MobileMargin * 2f,
                MobileHudInset,
                MobileHudInset +
                MobileToolbarHeight);
            AnchorMobileReopenButton();
        }

        private void ApplyMobilePortraitLayout()
        {
            panelRoot.anchorMin = Vector2.zero;
            panelRoot.anchorMax = Vector2.one;
            panelRoot.pivot = new Vector2(0.5f, 0.5f);
            panelRoot.offsetMin =
                new Vector2(
                    MobileMargin,
                    MobileMargin);
            panelRoot.offsetMax =
                new Vector2(
                    -MobileMargin,
                    -(MobileHudInset +
                      MobileToolbarHeight +
                      MobilePanelGap));

            StretchTop(
                debuffToolbarRoot,
                MobileMargin,
                MobileMargin,
                MobileHudInset,
                MobileHudInset +
                MobileToolbarHeight);
            AnchorMobileReopenButton();
        }

        private void AnchorMobileReopenButton()
        {
            AnchorReopenButton(
                new Vector2(116f, MobileReopenHeight),
                new Vector2(
                    -MobileMargin,
                    -MobileHudInset));
        }

        private void AnchorReopenButton(
            Vector2 size,
            Vector2 position)
        {
            reopenButtonRoot.anchorMin =
                new Vector2(1f, 1f);
            reopenButtonRoot.anchorMax =
                new Vector2(1f, 1f);
            reopenButtonRoot.pivot =
                new Vector2(1f, 1f);
            reopenButtonRoot.sizeDelta = size;
            reopenButtonRoot.anchoredPosition =
                position;
        }

        private static void StretchTop(
            RectTransform target,
            float left,
            float right,
            float top,
            float bottom)
        {
            target.anchorMin = new Vector2(0f, 1f);
            target.anchorMax = new Vector2(1f, 1f);
            target.pivot = new Vector2(0.5f, 1f);
            target.offsetMin =
                new Vector2(left, -bottom);
            target.offsetMax =
                new Vector2(-right, -top);
        }

        private static bool IsHandheldDevice()
        {
            return Application.isMobilePlatform ||
                   SystemInfo.deviceType ==
                   DeviceType.Handheld;
        }
    }
}
