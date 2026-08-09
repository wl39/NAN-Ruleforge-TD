using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// 스테이지와 무관하게 런타임 생성 UI의 기준 해상도를 화면 형태에 맞춘다.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasScaler))]
    public class RuntimeResponsiveCanvasScaler : MonoBehaviour
    {
        public static readonly Vector2 DesktopReferenceResolution =
            new Vector2(1600f, 900f);
        public static readonly Vector2 CompactPortraitReferenceResolution =
            new Vector2(540f, 960f);
        public static readonly Vector2 CompactLandscapeReferenceResolution =
            new Vector2(960f, 540f);

        private CanvasScaler canvasScaler;
        private int lastScreenWidth = -1;
        private int lastScreenHeight = -1;
        private bool lastHandheld;

        public bool IsCompactLayout { get; private set; }
        public bool IsPortraitLayout { get; private set; }

        protected virtual void OnEnable()
        {
            ApplyScale();
        }

        protected virtual void Update()
        {
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            bool handheld = IsHandheldDevice();
            if (width != lastScreenWidth ||
                height != lastScreenHeight ||
                handheld != lastHandheld)
            {
                ApplyScale(width, height, handheld);
            }
        }

        public void ApplyScale()
        {
            ApplyScale(
                Mathf.Max(1, Screen.width),
                Mathf.Max(1, Screen.height),
                IsHandheldDevice());
        }

        public void ApplyScale(
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
            canvasScaler.matchWidthOrHeight = 0.5f;
            canvasScaler.referenceResolution = IsCompactLayout
                ? IsPortraitLayout
                    ? CompactPortraitReferenceResolution
                    : CompactLandscapeReferenceResolution
                : DesktopReferenceResolution;

            lastScreenWidth = width;
            lastScreenHeight = height;
            lastHandheld = handheld;
        }

        public static bool ShouldUseCompactLayout(
            int screenWidth,
            int screenHeight,
            bool handheld)
        {
            return handheld || screenHeight > screenWidth;
        }

        private static bool IsHandheldDevice()
        {
            return Application.isMobilePlatform ||
                SystemInfo.deviceType == DeviceType.Handheld;
        }
    }
}
