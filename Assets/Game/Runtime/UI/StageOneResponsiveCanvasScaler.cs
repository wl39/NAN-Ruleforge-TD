using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Keeps runtime-created UI readable on phones without changing the
    /// desktop layout scale. Portrait browsers use a phone-sized design
    /// surface; handheld landscape displays use a wider equivalent.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class StageOneResponsiveCanvasScaler : MonoBehaviour
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

        private void OnEnable()
        {
            ApplyScale();
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
