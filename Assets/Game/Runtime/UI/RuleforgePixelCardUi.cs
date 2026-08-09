using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    public enum RuleforgePixelCardPanel
    {
        Body,
        Name,
        Artwork,
        Description,
        Badge
    }

    /// <summary>
    /// Shared card chrome. Tier frames keep their native layout but use the
    /// same short wood, metal, parchment, and accent ramps as the field art.
    /// </summary>
    public static class RuleforgePixelCardUi
    {
        private const int FrameWidth = 24;
        private const int FrameHeight = 32;
        private const int PanelSize = 16;
        private static readonly Vector4 FrameBorder =
            new Vector4(6f, 6f, 6f, 6f);
        private static readonly Vector4 PanelBorder =
            new Vector4(4f, 4f, 4f, 4f);

        private enum FrameState
        {
            Normal,
            Hovered,
            Pressed,
            Selected,
            Disabled
        }

        private static readonly Dictionary<int, Sprite> FrameSprites =
            new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite>
            AuthoredFrameSprites = new Dictionary<int, Sprite>();
        private static readonly Dictionary<RuleforgePixelCardPanel, Sprite>
            PanelSprites =
                new Dictionary<RuleforgePixelCardPanel, Sprite>();

        public static void ApplyFrame(Button button, bool equipped)
        {
            ApplyFrame(button, CardTier.Common, equipped);
        }

        public static void ApplyFrame(
            Button button,
            CardTier tier,
            bool equipped)
        {
            if (button == null ||
                !(button.targetGraphic is Image image))
            {
                return;
            }

            image.sprite = GetFrameSprite(tier);
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = Color.white;
            button.transition = Selectable.Transition.ColorTint;
            button.spriteState = new SpriteState();

            ColorBlock colors = button.colors;
            colors.normalColor = equipped
                ? new Color(0.92f, 1f, 0.96f, 1f)
                : Color.white;
            colors.highlightedColor =
                new Color(1f, 0.97f, 0.82f, 1f);
            colors.pressedColor =
                new Color(0.74f, 0.70f, 0.62f, 1f);
            colors.selectedColor =
                new Color(0.83f, 1f, 0.96f, 1f);
            colors.disabledColor =
                new Color(0.78f, 0.76f, 0.70f, 0.92f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.055f;
            button.colors = colors;
        }

        public static void ApplyPanel(
            Image image,
            RuleforgePixelCardPanel panel,
            Color tint)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = GetPanelSprite(panel);
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
            image.color = tint;
        }

        private static Sprite GetFrameSprite(FrameState state)
        {
            int key = (int)state;
            if (FrameSprites.TryGetValue(key, out Sprite sprite) &&
                sprite != null)
            {
                return sprite;
            }

            var texture = new Texture2D(
                FrameWidth,
                FrameHeight,
                TextureFormat.RGBA32,
                false)
            {
                name = "Ruleforge Pixel Card Frame " + state,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[FrameWidth * FrameHeight];
            for (int y = 0; y < FrameHeight; y++)
            {
                for (int x = 0; x < FrameWidth; x++)
                {
                    pixels[y * FrameWidth + x] =
                        EvaluateFramePixel(x, y, state);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, FrameWidth, FrameHeight),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect,
                FrameBorder);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            FrameSprites[key] = sprite;
            return sprite;
        }

        private static Sprite GetFrameSprite(CardTier tier)
        {
            int tierValue = Mathf.Clamp((int)tier, 1, 5);
            if (AuthoredFrameSprites.TryGetValue(
                    tierValue,
                    out Sprite sprite) &&
                sprite != null)
            {
                return sprite;
            }

            sprite = Resources.Load<Sprite>(
                "RuleforgeTD/UI/Cards/RuleforgeCardFrame_T" +
                tierValue);
            if (sprite == null)
            {
                sprite = GetFrameSprite(FrameState.Normal);
            }
            else
            {
                sprite = RuleforgeUiTextureSampling
                    .ConfigureResponsive(sprite);
            }

            AuthoredFrameSprites[tierValue] = sprite;
            return sprite;
        }

        private static Color32 EvaluateFramePixel(
            int x,
            int y,
            FrameState state)
        {
            bool farX = x <= 1 || x >= FrameWidth - 2;
            bool farY = y <= 1 || y >= FrameHeight - 2;
            if (farX && farY)
            {
                return new Color32(0, 0, 0, 0);
            }

            if (IsFrameOuterEdge(x, y))
            {
                return new Color32(24, 18, 15, 255);
            }

            if (state == FrameState.Disabled)
            {
                if (x <= 4 || x >= FrameWidth - 5 ||
                    y <= 4 || y >= FrameHeight - 5)
                {
                    return new Color32(85, 80, 70, 255);
                }

                return new Color32(55, 52, 47, 255);
            }

            bool pressed = state == FrameState.Pressed;
            if (y <= (pressed ? 5 : 4))
            {
                return new Color32(58, 34, 23, 255);
            }

            if (y >= (pressed ? FrameHeight - 6 : FrameHeight - 5))
            {
                return new Color32(211, 154, 66, 255);
            }

            if (x <= 4 || x >= FrameWidth - 5)
            {
                return new Color32(132, 83, 40, 255);
            }

            if (IsFrameRivet(x, y))
            {
                return state == FrameState.Selected
                    ? new Color32(103, 222, 207, 255)
                    : new Color32(227, 181, 78, 255);
            }

            Color32 surface = new Color32(73, 40, 25, 255);
            if (state == FrameState.Hovered ||
                state == FrameState.Selected)
            {
                surface = new Color32(91, 52, 29, 255);
            }
            else if (pressed)
            {
                surface = new Color32(55, 31, 22, 255);
            }

            return surface;
        }

        private static bool IsFrameOuterEdge(int x, int y)
        {
            if (x == 0 || x == FrameWidth - 1 ||
                y == 0 || y == FrameHeight - 1)
            {
                return true;
            }

            bool xStepOne = x == 1 || x == FrameWidth - 2;
            bool yStepTwo = y == 2 || y == FrameHeight - 3;
            bool xStepTwo = x == 2 || x == FrameWidth - 3;
            bool yStepOne = y == 1 || y == FrameHeight - 2;
            return (xStepOne && yStepTwo) ||
                (xStepTwo && yStepOne);
        }

        private static bool IsFrameRivet(int x, int y)
        {
            bool rivetX = x == 3 || x == FrameWidth - 4;
            bool rivetY = y == 5 || y == FrameHeight - 6;
            return rivetX && rivetY;
        }

        private static Sprite GetPanelSprite(
            RuleforgePixelCardPanel panel)
        {
            if (PanelSprites.TryGetValue(panel, out Sprite sprite) &&
                sprite != null)
            {
                return sprite;
            }

            var texture = new Texture2D(
                PanelSize,
                PanelSize,
                TextureFormat.RGBA32,
                false)
            {
                name = "Ruleforge Pixel Card Panel " + panel,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[PanelSize * PanelSize];
            for (int y = 0; y < PanelSize; y++)
            {
                for (int x = 0; x < PanelSize; x++)
                {
                    pixels[y * PanelSize + x] =
                        EvaluatePanelPixel(x, y, panel);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, PanelSize, PanelSize),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect,
                PanelBorder);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            PanelSprites[panel] = sprite;
            return sprite;
        }

        private static Color32 EvaluatePanelPixel(
            int x,
            int y,
            RuleforgePixelCardPanel panel)
        {
            bool farX = x <= 1 || x >= PanelSize - 2;
            bool farY = y <= 1 || y >= PanelSize - 2;
            if (farX && farY)
            {
                return new Color32(0, 0, 0, 0);
            }

            if (x == 0 || x == PanelSize - 1 ||
                y == 0 || y == PanelSize - 1)
            {
                return new Color32(28, 21, 17, 255);
            }

            if (x <= 2 || x >= PanelSize - 3 ||
                y <= 2 || y >= PanelSize - 3)
            {
                return panel == RuleforgePixelCardPanel.Body
                    ? new Color32(184, 151, 109, 255)
                    : new Color32(126, 82, 43, 255);
            }

            switch (panel)
            {
                case RuleforgePixelCardPanel.Body:
                    return new Color32(226, 211, 185, 255);
                case RuleforgePixelCardPanel.Artwork:
                    return new Color32(36, 33, 30, 255);
                case RuleforgePixelCardPanel.Badge:
                    return new Color32(69, 43, 25, 255);
                case RuleforgePixelCardPanel.Name:
                    return new Color32(55, 35, 25, 255);
                default:
                    return new Color32(46, 37, 30, 255);
            }
        }
    }
}
