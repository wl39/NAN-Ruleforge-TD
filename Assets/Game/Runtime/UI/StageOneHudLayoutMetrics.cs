namespace RuleforgeTD.UI
{
    /// <summary>
    /// Shared layout contract for Stage 01 overlays that must coexist with
    /// the persistent top HUD. Keeping these measurements in one place
    /// prevents independently-created canvases from drifting into overlap.
    /// </summary>
    public static class StageOneHudLayoutMetrics
    {
        public const float DesktopTopBarHeight = 52f;
        public const float DesktopStatusPanelHeight = 42f;
        public const float DesktopStatusPanelTopOffset = 56f;
        public const float DesktopTopOccupiedHeight =
            DesktopStatusPanelTopOffset +
            DesktopStatusPanelHeight;

        public const float PortraitTopBarHeight = 118f;
        public const float PortraitStatusPanelHeight = 54f;
        public const float PortraitStatusPanelTopOffset = 124f;
        public const float PortraitTopOccupiedHeight =
            PortraitStatusPanelTopOffset +
            PortraitStatusPanelHeight;

        public const float OverlaySeparation = 16f;
        public const float CompactBottomSheetHeightRatio = 0.6f;

        public static float GetTopOccupiedHeight(
            bool portrait)
        {
            return portrait
                ? PortraitTopOccupiedHeight
                : DesktopTopOccupiedHeight;
        }
    }
}
