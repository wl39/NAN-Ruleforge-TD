using System.IO;
using NUnit.Framework;
using RuleforgeTD.GameLogic.Content;
using UnityEditor;
using UnityEngine;

namespace RuleforgeTD.Tests.EditMode
{
    public sealed class CardArtworkAssetTests
    {
        private const string ContentPath =
            "Assets/Game/Data/Logic/phase1-content.json";
        private const string ArtworkRoot =
            "Assets/Game/Resources/RuleforgeTD/UI/Cards/Artwork/";

        [Test]
        public void EveryAuthoredCardHasPixelArtwork()
        {
            TextAsset contentAsset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ContentPath);
            Assert.That(contentAsset, Is.Not.Null);
            ContentCatalogDto catalog =
                JsonUtility.FromJson<ContentCatalogDto>(
                    contentAsset.text);
            Assert.That(catalog.cards, Has.Length.EqualTo(58));

            for (int i = 0; i < catalog.cards.Length; i++)
            {
                string stableId = catalog.cards[i].id;
                string path = ArtworkRoot + stableId + ".png";
                Sprite artwork =
                    AssetDatabase.LoadAssetAtPath<Sprite>(path);
                Assert.That(
                    artwork,
                    Is.Not.Null,
                    "Card '{0}' is missing artwork at {1}.",
                    stableId,
                    path);
                Assert.That(
                    artwork.texture.width,
                    Is.EqualTo(192),
                    stableId);
                Assert.That(
                    artwork.texture.height,
                    Is.EqualTo(112),
                    stableId);

                TextureImporter importer =
                    AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, stableId);
                Assert.That(
                    importer.filterMode,
                    Is.EqualTo(FilterMode.Point),
                    stableId);
                AssertTransparentCorners(path, stableId);
            }
        }

        private static void AssertTransparentCorners(
            string assetPath,
            string stableId)
        {
            byte[] pngBytes = File.ReadAllBytes(assetPath);
            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false);
            try
            {
                Assert.That(
                    ImageConversion.LoadImage(
                        texture,
                        pngBytes,
                        false),
                    Is.True,
                    stableId);
                Color32[] pixels = texture.GetPixels32();
                int width = texture.width;
                int height = texture.height;
                Assert.That(pixels[0].a, Is.Zero, stableId);
                Assert.That(pixels[width - 1].a, Is.Zero, stableId);
                Assert.That(
                    pixels[(height - 1) * width].a,
                    Is.Zero,
                    stableId);
                Assert.That(
                    pixels[height * width - 1].a,
                    Is.Zero,
                    stableId);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
