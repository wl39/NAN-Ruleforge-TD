using System;
using System.Collections.Generic;
using RuleforgeTD.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Shared button roles for the medieval workshop UI language.
    /// Saturated magic colors are accents; the button body stays grounded in
    /// iron, walnut, moss, brass, and parchment taken from the field artwork.
    /// </summary>
    public enum RuleforgePixelButtonRole
    {
        Primary,
        Secondary,
        Utility,
        Selected,
        Danger
    }

    public enum RuleforgeExactButtonAsset
    {
        None,
        Main330x72,
        Launch204x60,
        Back172x44,
        TowerAction188x58,
        TowerOption356x88,
        Upgrade132x44,
        UpgradePortrait200x90,
        HudPlay118x36,
        HudSpeed50x36,
        SpeedSlow50x36,
        SpeedNormal50x36,
        SpeedFast50x36,
        SpeedUltra50x36,
        StageReturn184x38,
        Square80,
        Square54,
        Square36,
        Square44,
        Square94,
        Square90
    }

    public enum RuleforgeExactPanelAsset
    {
        Effect876x120,
        Slot670x80,
        Inventory862x208,
        EffectPortrait666x170,
        SlotPortrait448x94,
        InventoryPortrait646x252,
        InventoryLandscapeSide400x660,
        TowerPreviewLandscape280x660,
        EffectLandscapeMiddle670x120,
        SlotLandscapeMiddle480x80,
        InventoryLandscapeSide380x660,
        TowerPicker1_380x176,
        TowerPicker2_380x272,
        TowerPicker3_380x368,
        TowerPicker4_380x464
    }

    public enum RuleforgePixelPanelRole
    {
        Parchment,
        Workbench
    }

    /// <summary>
    /// Shared legacy-uGUI typography contract. The bundled Galmuri font is
    /// already drawn on a pixel grid, so synthetic bold plus a four-direction
    /// outline makes Korean glyphs close up. Visual hierarchy is expressed by
    /// size and color while every non-scrollable label remains clipped to its
    /// own rect.
    /// </summary>
    public static class RuleforgeUiTypography
    {
        public static void Configure(
            Text text,
            Font font,
            int fontSize,
            Color color,
            TextAnchor alignment,
            bool useSubtleShadow = false)
        {
            if (text == null)
            {
                return;
            }

            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Normal;
            text.color = color;
            text.alignment = alignment;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.lineSpacing = 1f;
            ConfigureEffects(text, useSubtleShadow);
        }

        public static void RestyleButtonLabel(
            Text text,
            Color color)
        {
            if (text == null)
            {
                return;
            }

            text.fontStyle = FontStyle.Normal;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            ConfigureEffects(text, true);
        }

        public static bool IsLight(Color color)
        {
            return color.r * 0.2126f +
                color.g * 0.7152f +
                color.b * 0.0722f >= 0.58f;
        }

        private static void ConfigureEffects(
            Text text,
            bool useSubtleShadow)
        {
            Shadow subtleShadow = null;
            Shadow[] effects = text.GetComponents<Shadow>();
            for (int i = 0; i < effects.Length; i++)
            {
                Shadow effect = effects[i];
                if (effect is Outline)
                {
                    effect.enabled = false;
                    continue;
                }

                if (effect.GetType() == typeof(Shadow))
                {
                    subtleShadow = effect;
                    effect.enabled = useSubtleShadow;
                }
            }

            if (!useSubtleShadow)
            {
                return;
            }

            if (subtleShadow == null)
            {
                subtleShadow = text.gameObject.AddComponent<Shadow>();
            }

            subtleShadow.enabled = true;
            subtleShadow.effectColor = new Color(0f, 0f, 0f, 0.42f);
            subtleShadow.effectDistance = new Vector2(1f, -1f);
            subtleShadow.useGraphicAlpha = true;
        }
    }

    /// <summary>
    /// Authored UI textures are exported at multiple times their logical
    /// size and then rendered through a responsive CanvasScaler. Bilinear
    /// sampling avoids the uneven texel loss produced by point filtering at
    /// non-integer mobile scale factors. World sprites retain point filtering.
    /// </summary>
    internal static class RuleforgeUiTextureSampling
    {
        public static Sprite ConfigureResponsive(Sprite sprite)
        {
            Texture2D texture = sprite == null
                ? null
                : sprite.texture;
            if (texture == null)
            {
                return sprite;
            }

            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;

            // Older imports used 3/4 PPU instead of 300/400 PPU.
            // uGUI compares this with Canvas.referencePixelsPerUnit (100),
            // so the old value inflated sliced borders by a factor of 100.
            if (sprite.pixelsPerUnit >= 100f)
            {
                return sprite;
            }

            Rect rect = sprite.rect;
            Vector2 normalizedPivot = new Vector2(
                sprite.pivot.x / rect.width,
                sprite.pivot.y / rect.height);
            Sprite corrected = Sprite.Create(
                texture,
                rect,
                normalizedPivot,
                sprite.pixelsPerUnit * 100f,
                0u,
                SpriteMeshType.FullRect,
                sprite.border);
            corrected.name = sprite.name + " Responsive";
            corrected.hideFlags = HideFlags.HideAndDontSave;
            return corrected;
        }
    }

    /// <summary>
    /// Runtime marker used by tests and by views that need to restyle an
    /// existing button without rebuilding it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuleforgePixelButtonSkin : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler
    {
        [SerializeField]
        private RuleforgePixelButtonRole role;

        private Button button;
        private bool acceptedPointerDown;

        public RuleforgePixelButtonRole Role => role;

        internal void SetRole(RuleforgePixelButtonRole value)
        {
            role = value;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            acceptedPointerDown =
                IsPrimaryPointer(eventData) &&
                IsButtonInteractable();
            if (acceptedPointerDown)
            {
                RuleforgeAudioService.PlayUiPress();
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            bool shouldPlay =
                acceptedPointerDown &&
                IsPrimaryPointer(eventData) &&
                IsButtonInteractable() &&
                IsPointerStillOverButton(eventData);
            acceptedPointerDown = false;
            if (shouldPlay)
            {
                RuleforgeAudioService.PlayUiRelease();
            }
        }

        private bool IsButtonInteractable()
        {
            if (button == null)
            {
                button = GetComponent<Button>();
            }

            return button != null &&
                button.IsActive() &&
                button.IsInteractable();
        }

        private bool IsPointerStillOverButton(
            PointerEventData eventData)
        {
            GameObject releaseTarget =
                eventData.pointerCurrentRaycast.gameObject;
            return releaseTarget != null &&
                releaseTarget.transform.IsChildOf(transform);
        }

        private static bool IsPrimaryPointer(
            PointerEventData eventData)
        {
            return eventData != null &&
                eventData.button ==
                    PointerEventData.InputButton.Left;
        }
    }

    /// <summary>
    /// Applies authored pixel-art button frames with a shared, restrained
    /// material palette. Large shapes survive at gameplay scale without
    /// miniature grain, bevel, or lighting detail.
    /// </summary>
    public static class RuleforgePixelUi
    {
        private const int TextureSize = 16;
        private const float PixelsPerUnit = 100f;
        private static readonly Vector4 SpriteBorder =
            new Vector4(5f, 5f, 5f, 5f);

        private enum VisualState
        {
            Normal,
            Hovered,
            Pressed,
            Selected,
            Disabled
        }

        private readonly struct SpriteKey
        {
            public SpriteKey(
                RuleforgePixelButtonRole role,
                VisualState state)
            {
                Role = role;
                State = state;
            }

            public RuleforgePixelButtonRole Role { get; }
            public VisualState State { get; }

            public override int GetHashCode()
            {
                return ((int)Role * 397) ^ (int)State;
            }

            public override bool Equals(object obj)
            {
                return obj is SpriteKey other &&
                    other.Role == Role &&
                    other.State == State;
            }
        }

        private readonly struct Palette
        {
            public Palette(
                Color32 outline,
                Color32 lowerRim,
                Color32 rim,
                Color32 upperRim,
                Color32 surface,
                Color32 surfaceDetail,
                Color32 rivet)
            {
                Outline = outline;
                LowerRim = lowerRim;
                Rim = rim;
                UpperRim = upperRim;
                Surface = surface;
                SurfaceDetail = surfaceDetail;
                Rivet = rivet;
            }

            public Color32 Outline { get; }
            public Color32 LowerRim { get; }
            public Color32 Rim { get; }
            public Color32 UpperRim { get; }
            public Color32 Surface { get; }
            public Color32 SurfaceDetail { get; }
            public Color32 Rivet { get; }
        }

        private static readonly Dictionary<SpriteKey, Sprite> Sprites =
            new Dictionary<SpriteKey, Sprite>();
        private static readonly Dictionary<string, Sprite> AuthoredSprites =
            new Dictionary<string, Sprite>();

        private const string PrimarySpritePath =
            "RuleforgeTD/UI/Panels/RuleforgeActionButtonCompact";
        private const string SecondarySpritePath =
            "RuleforgeTD/UI/Buttons/RuleforgeButtonSecondary";
        private const string SquareSpritePath =
            "RuleforgeTD/UI/Buttons/RuleforgeButtonSquare";
        private const string ExactRoot =
            "RuleforgeTD/UI/Exact/";
        private const string PanelRoot =
            "RuleforgeTD/UI/Panels/";

        public static readonly Color ParchmentText =
            new Color32(244, 232, 197, 255);
        public static readonly Color MutedParchmentText =
            new Color32(189, 181, 158, 255);
        public static readonly Color InkText =
            new Color32(35, 28, 22, 255);

        public static void Apply(
            Button button,
            RuleforgePixelButtonRole role)
        {
            Apply(button, role, Color.white);
        }

        public static void Apply(
            Button button,
            RuleforgePixelButtonRole role,
            Color tint)
        {
            if (button == null)
            {
                return;
            }

            Image image = button.targetGraphic as Image;
            if (image == null)
            {
                image = button.GetComponent<Image>();
            }

            if (image == null)
            {
                return;
            }

            RuleforgePixelButtonSkin skin =
                button.GetComponent<RuleforgePixelButtonSkin>();
            if (skin == null)
            {
                skin = button.gameObject.AddComponent<
                    RuleforgePixelButtonSkin>();
            }

            skin.SetRole(role);
            RuleforgeExactButtonAsset exactAsset =
                ResolveExactButtonAsset(button);
            image.sprite = exactAsset ==
                    RuleforgeExactButtonAsset.None
                ? GetAuthoredSprite(
                    role,
                    IsSquareButton(button))
                : LoadAuthoredSprite(
                    ExactRoot + GetExactButtonFilename(
                        exactAsset));
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = tint;
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.spriteState = new SpriteState();

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor =
                new Color(1f, 0.98f, 0.86f, 1f);
            colors.pressedColor =
                new Color(0.74f, 0.70f, 0.62f, 1f);
            colors.selectedColor = role ==
                    RuleforgePixelButtonRole.Selected
                ? new Color(0.78f, 1f, 0.94f, 1f)
                : new Color(1f, 0.94f, 0.76f, 1f);
            colors.disabledColor =
                new Color(0.76f, 0.74f, 0.68f, 0.92f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.055f;
            button.colors = colors;

            StyleLabels(button);
        }

        public static void ApplyExact(
            Button button,
            RuleforgeExactButtonAsset asset,
            RuleforgePixelButtonRole role,
            Color tint)
        {
            if (button == null ||
                asset == RuleforgeExactButtonAsset.None)
            {
                return;
            }

            Image image = button.targetGraphic as Image ??
                button.GetComponent<Image>();
            if (image == null)
            {
                return;
            }

            RuleforgePixelButtonSkin skin =
                button.GetComponent<RuleforgePixelButtonSkin>();
            if (skin == null)
            {
                skin = button.gameObject.AddComponent<
                    RuleforgePixelButtonSkin>();
            }

            skin.SetRole(role);
            image.sprite = LoadAuthoredSprite(
                ExactRoot + GetExactButtonFilename(asset));
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = tint;
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.spriteState = new SpriteState();

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor =
                new Color(1f, 0.98f, 0.86f, 1f);
            colors.pressedColor =
                new Color(0.74f, 0.70f, 0.62f, 1f);
            colors.selectedColor = role ==
                    RuleforgePixelButtonRole.Selected
                ? new Color(0.78f, 1f, 0.94f, 1f)
                : new Color(1f, 0.94f, 0.76f, 1f);
            colors.disabledColor =
                new Color(0.76f, 0.74f, 0.68f, 0.92f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.055f;
            button.colors = colors;
            StyleLabels(button);
        }

        /// <summary>
        /// Compatibility bridge for existing views that used raw colors as
        /// their semantic state. It retains a restrained hint of that color
        /// without turning the whole control into a modern flat rectangle.
        /// </summary>
        public static void ApplyLegacyColor(
            Button button,
            Color legacyColor)
        {
            RuleforgePixelButtonRole role = ResolveRole(legacyColor);
            ApplyTint(button, role, legacyColor);
        }

        public static void ApplyTint(
            Button button,
            RuleforgePixelButtonRole role,
            Color accentColor)
        {
            Color tint = Color.Lerp(
                Color.white,
                new Color(
                    Mathf.Clamp01(accentColor.r + 0.18f),
                    Mathf.Clamp01(accentColor.g + 0.18f),
                    Mathf.Clamp01(accentColor.b + 0.18f),
                    1f),
                0.14f);
            Apply(button, role, tint);
        }

        public static void ApplyExactPanel(
            Image image,
            RuleforgeExactPanelAsset asset,
            Color tint)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = LoadAuthoredSprite(
                ExactRoot + GetExactPanelFilename(asset));
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = tint;
        }

        public static void ApplyPanel(
            Image image,
            RuleforgePixelPanelRole role,
            Color tint)
        {
            if (image == null)
            {
                return;
            }

            string filename = role == RuleforgePixelPanelRole.Parchment
                ? "RuleforgeInfoPanel"
                : "RuleforgeWorkbenchPanel";
            image.sprite = LoadAuthoredSprite(PanelRoot + filename);
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
            image.color = tint;
        }

        private static RuleforgeExactButtonAsset
            ResolveExactButtonAsset(Button button)
        {
            if (button == null)
            {
                return RuleforgeExactButtonAsset.None;
            }

            string objectName = button.gameObject.name;
            if (objectName.Equals(
                    "Open Campaign Map",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RuleforgeExactButtonAsset.Main330x72;
            }

            if (objectName.Equals(
                    "Launch Selected Stage",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RuleforgeExactButtonAsset.Launch204x60;
            }

            if (objectName.Equals(
                    "Back To Title",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RuleforgeExactButtonAsset.Back172x44;
            }

            if (objectName.Equals(
                    "Play Button",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RuleforgeExactButtonAsset.HudPlay118x36;
            }

            if (objectName.Equals(
                    "Speed 0.5x Button",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RuleforgeExactButtonAsset.SpeedSlow50x36;
            }

            if (objectName.Equals(
                    "Speed 1x Button",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RuleforgeExactButtonAsset.SpeedNormal50x36;
            }

            if (objectName.Equals(
                    "Speed 2x Button",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RuleforgeExactButtonAsset.SpeedFast50x36;
            }

            if (objectName.Equals(
                    "Speed 3x Button",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RuleforgeExactButtonAsset.SpeedUltra50x36;
            }

            if (objectName.StartsWith(
                    "Speed ",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RuleforgeExactButtonAsset.HudSpeed50x36;
            }

            if (objectName.Equals(
                    "Return To Stage Selection",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RuleforgeExactButtonAsset.StageReturn184x38;
            }

            if (objectName.StartsWith(
                    "Build ",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RuleforgeExactButtonAsset.TowerOption356x88;
            }

            if (objectName.Equals(
                    "Upgrade Tower Button",
                    StringComparison.OrdinalIgnoreCase) ||
                objectName.Equals(
                    "Equip Cards Button",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RuleforgeExactButtonAsset.TowerAction188x58;
            }

            RectTransform rect =
                button.GetComponent<RectTransform>();
            float width = rect == null ? 0f : rect.rect.width;
            float height = rect == null ? 0f : rect.rect.height;

            if (objectName.Equals(
                    "Upgrade Tower",
                    StringComparison.OrdinalIgnoreCase))
            {
                return height >= 70f
                    ? RuleforgeExactButtonAsset.UpgradePortrait200x90
                    : RuleforgeExactButtonAsset.Upgrade132x44;
            }

            if (objectName.IndexOf(
                    "tower slot",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf(
                    "subject toggle",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return width >= 90f
                    ? RuleforgeExactButtonAsset.Square94
                    : RuleforgeExactButtonAsset.Square80;
            }

            if (objectName.IndexOf(
                    "close",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (width <= 42f)
                {
                    return RuleforgeExactButtonAsset.Square36;
                }

                if (width <= 48f)
                {
                    return RuleforgeExactButtonAsset.Square44;
                }

                if (width <= 64f)
                {
                    return RuleforgeExactButtonAsset.Square54;
                }

                return RuleforgeExactButtonAsset.Square90;
            }

            return RuleforgeExactButtonAsset.None;
        }

        private static string GetExactButtonFilename(
            RuleforgeExactButtonAsset asset)
        {
            switch (asset)
            {
                case RuleforgeExactButtonAsset.Main330x72:
                    return "RuleforgeButton_Main_330x72";
                case RuleforgeExactButtonAsset.Launch204x60:
                    return "RuleforgeButton_Launch_204x60";
                case RuleforgeExactButtonAsset.Back172x44:
                    return "RuleforgeButton_Back_172x44";
                case RuleforgeExactButtonAsset.TowerAction188x58:
                    return "RuleforgeButton_TowerAction_188x58";
                case RuleforgeExactButtonAsset.TowerOption356x88:
                    return "RuleforgeButton_TowerOption_356x88";
                case RuleforgeExactButtonAsset.Upgrade132x44:
                    return "RuleforgeButton_Upgrade_132x44";
                case RuleforgeExactButtonAsset.UpgradePortrait200x90:
                    return "RuleforgeButton_UpgradePortrait_200x90";
                case RuleforgeExactButtonAsset.HudPlay118x36:
                    return "RuleforgeButton_HudPlay_118x36";
                case RuleforgeExactButtonAsset.HudSpeed50x36:
                    return "RuleforgeButton_HudSpeed_50x36";
                case RuleforgeExactButtonAsset.SpeedSlow50x36:
                    return "RuleforgeButton_SpeedSlow_50x36";
                case RuleforgeExactButtonAsset.SpeedNormal50x36:
                    return "RuleforgeButton_SpeedNormal_50x36";
                case RuleforgeExactButtonAsset.SpeedFast50x36:
                    return "RuleforgeButton_SpeedFast_50x36";
                case RuleforgeExactButtonAsset.SpeedUltra50x36:
                    return "RuleforgeButton_SpeedUltra_50x36";
                case RuleforgeExactButtonAsset.StageReturn184x38:
                    return "RuleforgeButton_StageReturn_184x38";
                case RuleforgeExactButtonAsset.Square80:
                    return "RuleforgeButton_Square80";
                case RuleforgeExactButtonAsset.Square54:
                    return "RuleforgeButton_Square54";
                case RuleforgeExactButtonAsset.Square36:
                    return "RuleforgeButton_Square36";
                case RuleforgeExactButtonAsset.Square44:
                    return "RuleforgeButton_Square44";
                case RuleforgeExactButtonAsset.Square94:
                    return "RuleforgeButton_Square94";
                case RuleforgeExactButtonAsset.Square90:
                    return "RuleforgeButton_Square90";
                default:
                    return string.Empty;
            }
        }

        private static string GetExactPanelFilename(
            RuleforgeExactPanelAsset asset)
        {
            switch (asset)
            {
                case RuleforgeExactPanelAsset.Effect876x120:
                    return "RuleforgePanel_Effect_876x120";
                case RuleforgeExactPanelAsset.Slot670x80:
                    return "RuleforgePanel_Slot_670x80";
                case RuleforgeExactPanelAsset.Inventory862x208:
                    return "RuleforgePanel_Inventory_862x208";
                case RuleforgeExactPanelAsset.EffectPortrait666x170:
                    return "RuleforgePanel_EffectPortrait_666x170";
                case RuleforgeExactPanelAsset.SlotPortrait448x94:
                    return "RuleforgePanel_SlotPortrait_448x94";
                case RuleforgeExactPanelAsset.InventoryPortrait646x252:
                    return "RuleforgePanel_InventoryPortrait_646x252";
                case RuleforgeExactPanelAsset.InventoryLandscapeSide400x660:
                    return "RuleforgePanel_InventoryLandscapeSide_400x660";
                case RuleforgeExactPanelAsset.TowerPreviewLandscape280x660:
                    return "RuleforgePanel_TowerPreviewLandscape_280x660";
                case RuleforgeExactPanelAsset.EffectLandscapeMiddle670x120:
                    return "RuleforgePanel_EffectLandscapeMiddle_670x120";
                case RuleforgeExactPanelAsset.SlotLandscapeMiddle480x80:
                    return "RuleforgePanel_SlotLandscapeMiddle_480x80";
                case RuleforgeExactPanelAsset.InventoryLandscapeSide380x660:
                    return "RuleforgePanel_InventoryLandscapeSide_380x660";
                case RuleforgeExactPanelAsset.TowerPicker1_380x176:
                    return "RuleforgePanel_TowerPicker1_380x176";
                case RuleforgeExactPanelAsset.TowerPicker2_380x272:
                    return "RuleforgePanel_TowerPicker2_380x272";
                case RuleforgeExactPanelAsset.TowerPicker3_380x368:
                    return "RuleforgePanel_TowerPicker3_380x368";
                case RuleforgeExactPanelAsset.TowerPicker4_380x464:
                    return "RuleforgePanel_TowerPicker4_380x464";
                default:
                    return string.Empty;
            }
        }

        private static RuleforgePixelButtonRole ResolveRole(Color color)
        {
            if (color.r > color.g * 1.45f &&
                color.r > color.b * 1.35f &&
                color.g < 0.42f)
            {
                return RuleforgePixelButtonRole.Danger;
            }

            if (color.r > color.b * 1.2f && color.g > 0.36f)
            {
                return RuleforgePixelButtonRole.Primary;
            }

            if (color.g > color.r * 1.08f &&
                color.g > color.b * 0.8f)
            {
                return RuleforgePixelButtonRole.Utility;
            }

            if (color.b > color.r * 1.15f &&
                color.g > color.r * 1.1f)
            {
                return RuleforgePixelButtonRole.Utility;
            }

            return RuleforgePixelButtonRole.Secondary;
        }

        private static Sprite GetAuthoredSprite(
            RuleforgePixelButtonRole role,
            bool square)
        {
            string path = square
                ? SquareSpritePath
                : role == RuleforgePixelButtonRole.Primary
                    ? PrimarySpritePath
                    : SecondarySpritePath;
            Sprite sprite = LoadAuthoredSprite(path);
            if (sprite == null)
            {
                sprite = GetSprite(role, VisualState.Normal);
            }
            return sprite;
        }

        private static Sprite LoadAuthoredSprite(string path)
        {
            if (AuthoredSprites.TryGetValue(path, out Sprite cached) &&
                cached != null)
            {
                return cached;
            }

            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite != null)
            {
                sprite = RuleforgeUiTextureSampling
                    .ConfigureResponsive(sprite);
                AuthoredSprites[path] = sprite;
            }

            return sprite;
        }

        private static bool IsSquareButton(Button button)
        {
            string objectName = button == null
                ? string.Empty
                : button.gameObject.name;
            return objectName.IndexOf(
                       "close",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf(
                    "tower slot",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf(
                    "subject toggle",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void StyleLabels(Button button)
        {
            Text[] labels = button.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                RuleforgeUiTypography.RestyleButtonLabel(
                    labels[i],
                    ParchmentText);
            }
        }

        private static Sprite GetSprite(
            RuleforgePixelButtonRole role,
            VisualState state)
        {
            var key = new SpriteKey(role, state);
            if (Sprites.TryGetValue(key, out Sprite sprite) &&
                sprite != null)
            {
                return sprite;
            }

            Texture2D texture = BuildTexture(role, state);
            sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit,
                0u,
                SpriteMeshType.FullRect,
                SpriteBorder);
            sprite.name = "Ruleforge Pixel Button " +
                role + " " + state;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            Sprites[key] = sprite;
            return sprite;
        }

        private static Texture2D BuildTexture(
            RuleforgePixelButtonRole role,
            VisualState state)
        {
            Palette palette = GetPalette(role, state);
            var texture = new Texture2D(
                TextureSize,
                TextureSize,
                TextureFormat.RGBA32,
                false)
            {
                name = "Ruleforge Pixel Button Texture " +
                    role + " " + state,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    pixels[y * TextureSize + x] =
                        EvaluatePixel(x, y, palette, state);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Color32 EvaluatePixel(
            int x,
            int y,
            Palette palette,
            VisualState state)
        {
            if (IsCutCorner(x, y))
            {
                return new Color32(0, 0, 0, 0);
            }

            if (IsOuterEdge(x, y))
            {
                return palette.Outline;
            }

            bool pressed = state == VisualState.Pressed;
            if (y <= (pressed ? 3 : 2))
            {
                return palette.LowerRim;
            }

            if (y >= (pressed ? 12 : 13))
            {
                return palette.UpperRim;
            }

            if (x <= 2 || x >= TextureSize - 3)
            {
                return palette.Rim;
            }

            if (IsRivet(x, y))
            {
                return palette.Rivet;
            }

            return palette.Surface;
        }

        private static bool IsCutCorner(int x, int y)
        {
            bool extremeX = x <= 1 || x >= TextureSize - 2;
            bool extremeY = y <= 1 || y >= TextureSize - 2;
            return extremeX && extremeY;
        }

        private static bool IsOuterEdge(int x, int y)
        {
            if (x == 0 || x == TextureSize - 1 ||
                y == 0 || y == TextureSize - 1)
            {
                return true;
            }

            bool firstStepX = x == 1 || x == TextureSize - 2;
            bool firstStepY = y == 2 || y == TextureSize - 3;
            bool secondStepX = x == 2 || x == TextureSize - 3;
            bool secondStepY = y == 1 || y == TextureSize - 2;
            return (firstStepX && firstStepY) ||
                (secondStepX && secondStepY);
        }

        private static bool IsRivet(int x, int y)
        {
            bool rivetX = x == 2 || x == TextureSize - 3;
            bool rivetY = y == 3 || y == TextureSize - 4;
            return rivetX && rivetY;
        }

        private static Palette GetPalette(
            RuleforgePixelButtonRole role,
            VisualState state)
        {
            if (state == VisualState.Disabled)
            {
                return new Palette(
                    new Color32(28, 27, 25, 255),
                    new Color32(49, 47, 43, 255),
                    new Color32(75, 72, 65, 255),
                    new Color32(111, 106, 94, 255),
                    new Color32(70, 68, 63, 255),
                    new Color32(62, 60, 56, 255),
                    new Color32(103, 99, 89, 255));
            }

            Color32 surface;
            Color32 detail;
            Color32 rivet;
            switch (role)
            {
                case RuleforgePixelButtonRole.Primary:
                    surface = new Color32(104, 58, 31, 255);
                    detail = new Color32(85, 45, 27, 255);
                    rivet = new Color32(238, 187, 75, 255);
                    break;
                case RuleforgePixelButtonRole.Utility:
                    surface = new Color32(62, 76, 43, 255);
                    detail = new Color32(50, 63, 38, 255);
                    rivet = new Color32(120, 177, 139, 255);
                    break;
                case RuleforgePixelButtonRole.Selected:
                    surface = new Color32(76, 66, 32, 255);
                    detail = new Color32(61, 52, 27, 255);
                    rivet = new Color32(102, 218, 203, 255);
                    break;
                case RuleforgePixelButtonRole.Danger:
                    surface = new Color32(111, 44, 38, 255);
                    detail = new Color32(88, 32, 30, 255);
                    rivet = new Color32(203, 75, 53, 255);
                    break;
                default:
                    surface = new Color32(58, 53, 44, 255);
                    detail = new Color32(47, 43, 37, 255);
                    rivet = new Color32(163, 143, 99, 255);
                    break;
            }

            if (state == VisualState.Hovered ||
                state == VisualState.Selected)
            {
                surface = Lighten(surface, 14);
                detail = Lighten(detail, 10);
                rivet = role == RuleforgePixelButtonRole.Selected
                    ? new Color32(119, 234, 218, 255)
                    : new Color32(244, 198, 87, 255);
            }
            else if (state == VisualState.Pressed)
            {
                surface = Darken(surface, 17);
                detail = Darken(detail, 14);
            }

            return new Palette(
                new Color32(24, 19, 16, 255),
                new Color32(55, 35, 24, 255),
                new Color32(126, 81, 41, 255),
                new Color32(210, 154, 65, 255),
                surface,
                detail,
                rivet);
        }

        private static Color32 Lighten(Color32 color, byte amount)
        {
            return new Color32(
                (byte)Mathf.Min(255, color.r + amount),
                (byte)Mathf.Min(255, color.g + amount),
                (byte)Mathf.Min(255, color.b + amount),
                color.a);
        }

        private static Color32 Darken(Color32 color, byte amount)
        {
            return new Color32(
                (byte)Mathf.Max(0, color.r - amount),
                (byte)Mathf.Max(0, color.g - amount),
                (byte)Mathf.Max(0, color.b - amount),
                color.a);
        }
    }
}
