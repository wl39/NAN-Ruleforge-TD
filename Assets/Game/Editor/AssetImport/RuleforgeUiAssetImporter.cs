using UnityEditor;
using UnityEngine;

namespace RuleforgeTD.EditorTools
{
    /// <summary>
    /// Keeps the authored Ruleforge UI PNGs uncompressed in WebGL and stable
    /// under responsive canvas scaling. These textures are authored at 3x or
    /// 4x logical resolution, so point sampling at a fractional mobile scale
    /// drops uneven texel rows and makes frames look crushed. Bilinear
    /// sampling preserves the authored silhouette without changing gameplay
    /// pixel-art import settings.
    /// </summary>
    public sealed class RuleforgeUiAssetImporter : AssetPostprocessor
    {
        private const string UiRoot =
            "Assets/Game/Resources/RuleforgeTD/UI/";
        private const string CardArtworkRoot =
            UiRoot + "Cards/Artwork/";
        private const string LoadoutIconRoot =
            UiRoot + "Loadout/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(UiRoot))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            bool exactSizeAsset = assetPath.Contains("/Exact/");
            importer.spritePixelsPerUnit =
                assetPath.Contains("/Cards/") || exactSizeAsset
                    ? 300f
                    : 400f;
            importer.spriteBorder = exactSizeAsset
                ? Vector4.zero
                : ResolveBorder();
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode =
                assetPath.StartsWith(CardArtworkRoot) ||
                assetPath.StartsWith(LoadoutIconRoot)
                    ? FilterMode.Point
                    : FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.anisoLevel = 0;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression =
                TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = exactSizeAsset
                ? 4096
                : 2048;
            importer.sRGBTexture = true;
        }

        private Vector4 ResolveBorder()
        {
            if (assetPath.EndsWith("RuleforgeButtonPrimary.png"))
            {
                return new Vector4(72f, 56f, 72f, 56f);
            }

            if (assetPath.EndsWith("RuleforgeButtonSecondary.png"))
            {
                return new Vector4(62f, 52f, 62f, 52f);
            }

            if (assetPath.EndsWith("RuleforgeButtonSquare.png"))
            {
                return new Vector4(52f, 52f, 52f, 52f);
            }

            if (assetPath.EndsWith(
                    "RuleforgeActionButtonCompact.png"))
            {
                return new Vector4(52f, 52f, 52f, 52f);
            }

            if (assetPath.EndsWith("RuleforgeInfoPanel.png"))
            {
                return new Vector4(52f, 52f, 52f, 52f);
            }

            if (assetPath.EndsWith("RuleforgeWorkbenchPanel.png"))
            {
                return new Vector4(52f, 52f, 52f, 52f);
            }

            return Vector4.zero;
        }
    }
}
