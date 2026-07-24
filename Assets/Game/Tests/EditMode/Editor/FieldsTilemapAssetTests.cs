using System.IO;
using System.Linq;
using NUnit.Framework;
using RuleforgeTD.Maps;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleforgeTD.Tests.EditMode
{
    public sealed class FieldsTilemapAssetTests
    {
        private const string TerrainRoot =
            "Assets/Game/Data/Maps/Fields/Tiles/Terrain";
        private const string PropsRoot =
            "Assets/Game/Data/Maps/Fields/Tiles/Props";
        private const string AnimatedRoot =
            "Assets/Game/Data/Maps/Fields/Tiles/Animated";
        private const string ObjectsRoot =
            "Assets/ThirdParty/CraftPix/Raw/Maps/Fields/Objects";
        private const string AtlasPath =
            "Assets/ThirdParty/CraftPix/Raw/Maps/Fields/Tiles/" +
            "FieldsTileset.png";

        [Test]
        public void TerrainLibrary_UsesTopLeftRowMajorNumbersAndBakedMasks()
        {
            string[] terrainGuids = AssetDatabase.FindAssets(
                "t:FieldTerrainTile",
                new[] { TerrainRoot });
            Assert.That(terrainGuids, Has.Length.EqualTo(64));

            for (int tileNumber = 1; tileNumber <= 64; tileNumber++)
            {
                FieldTerrainTile tile = LoadTerrainTile(tileNumber);
                Assert.That(tile, Is.Not.Null);
                Assert.That(tile.TileNumber, Is.EqualTo(tileNumber));
                Assert.That(tile.sprite, Is.Not.Null);

                int column = (tileNumber - 1) % 8;
                int topRow = (tileNumber - 1) / 8;
                var expectedRect = new Rect(
                    column * 32,
                    (7 - topRow) * 32,
                    32,
                    32);
                Assert.That(
                    tile.sprite.rect,
                    Is.EqualTo(expectedRect),
                    "Unexpected atlas rect for tile " + tileNumber + ".");
                Assert.That(
                    tile.colliderType == Tile.ColliderType.None,
                    Is.EqualTo(tile.BlockedPixelCount == 0));
            }

            FieldTerrainTile fullyBlocked = LoadTerrainTile(38);
            Assert.That(fullyBlocked.IsFullyBlocked, Is.True);
            Assert.That(fullyBlocked.BlockedPixelCount, Is.EqualTo(1024));
            Assert.That(
                fullyBlocked.sprite.GetPhysicsShapeCount(),
                Is.GreaterThan(0));

            int[] fullyWalkable = { 11, 18, 20, 27 };
            foreach (int tileNumber in fullyWalkable)
            {
                FieldTerrainTile tile = LoadTerrainTile(tileNumber);
                Assert.That(tile.BlockedPixelCount, Is.Zero);
                Assert.That(tile.colliderType, Is.EqualTo(
                    Tile.ColliderType.None));
            }
        }

        [Test]
        public void AtlasImporter_PreservesPixelArtSettingsAndSixtyFourSlices()
        {
            var importer =
                AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.spriteImportMode,
                Is.EqualTo(SpriteImportMode.Multiple));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(32f));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));

            Sprite[] sprites = AssetDatabase
                .LoadAllAssetRepresentationsAtPath(AtlasPath)
                .OfType<Sprite>()
                .ToArray();
            Assert.That(sprites, Has.Length.EqualTo(64));
        }

        [Test]
        public void ObjectAndAnimationLibraries_ExposeAllEditableVariants()
        {
            string[] propGuids = AssetDatabase.FindAssets(
                "t:Tile",
                new[] { PropsRoot });
            Assert.That(propGuids, Has.Length.EqualTo(90));

            string[] animatedGuids = AssetDatabase.FindAssets(
                "t:FieldAnimatedTile",
                new[] { AnimatedRoot });
            Assert.That(animatedGuids, Has.Length.EqualTo(10));

            string[] keys =
            {
                "Flag_Down",
                "Flag_DownLeft",
                "Flag_DownRight",
                "Flag_Up",
                "Flag_UpLeft",
                "Flag_UpRight",
                "Flag_Left",
                "Flag_Right",
                "Campfire_Unlit",
                "Campfire_Lit"
            };
            foreach (string key in keys)
            {
                FieldAnimatedTile tile = LoadAnimatedTile(key);
                Assert.That(tile, Is.Not.Null);
                Assert.That(tile.FrameCount, Is.EqualTo(6));
                for (int frame = 0; frame < tile.FrameCount; frame++)
                {
                    Assert.That(tile.GetFrame(frame), Is.Not.Null);
                }
            }

            AssertMirroredPair("Flag_DownLeft", "Flag_DownRight");
            AssertMirroredPair("Flag_UpLeft", "Flag_UpRight");
            AssertMirroredPair("Flag_Left", "Flag_Right");
            Assert.That(LoadAnimatedTile("Flag_Down").FlipX, Is.False);
            Assert.That(LoadAnimatedTile("Flag_Up").FlipX, Is.False);
            Assert.That(LoadAnimatedTile("Campfire_Unlit").FlipX, Is.False);
            Assert.That(LoadAnimatedTile("Campfire_Lit").FlipX, Is.False);
        }

        [Test]
        public void ObjectImporters_UseSemanticPivots()
        {
            AssertNormalizedPivot(
                ObjectsRoot + "/7 Decor/Tree1.png",
                new Vector2(0.5f, 0f));
            AssertNormalizedPivot(
                ObjectsRoot + "/9 Bush/2.png",
                new Vector2(0.5f, 0f));
            AssertNormalizedPivot(
                ObjectsRoot + "/8 Camp/1.png",
                new Vector2(0.5f, 0f));
            AssertNormalizedPivot(
                ObjectsRoot + "/1 Shadow/6.png",
                new Vector2(0.5f, 0.5f));
            AssertNormalizedPivot(
                ObjectsRoot + "/6 Flower/1.png",
                new Vector2(0.5f, 0.5f));
            AssertNormalizedPivot(
                ObjectsRoot + "/5 Grass/4.png",
                new Vector2(0.5f, 0.5f));
            AssertNormalizedPivot(
                ObjectsRoot + "/4 Stone/1.png",
                new Vector2(0.5f, 0f));
        }

        [Test]
        public void RequiredPalettesPrefabAndScene_AreGenerated()
        {
            string[] paths =
            {
                "Assets/Game/Data/Maps/Fields/Palettes/" +
                "Fields Terrain Palette.prefab",
                "Assets/Game/Data/Maps/Fields/Palettes/" +
                "Fields Objects Palette.prefab",
                "Assets/Game/Data/Maps/Fields/Palettes/" +
                "Fields Animated Objects Palette.prefab",
                "Assets/Game/Prefabs/Maps/Fields/TowerBuildSite.prefab",
                "Assets/Game/Scenes/Battle/Stage01.unity"
            };

            foreach (string path in paths)
            {
                Assert.That(
                    File.Exists(path),
                    Is.True,
                    "Missing generated authoring asset: " + path);
            }
        }

        private static FieldTerrainTile LoadTerrainTile(int tileNumber)
        {
            return AssetDatabase.LoadAssetAtPath<FieldTerrainTile>(
                TerrainRoot +
                "/FieldTile_" +
                tileNumber.ToString("00") +
                ".asset");
        }

        private static FieldAnimatedTile LoadAnimatedTile(string key)
        {
            return AssetDatabase.LoadAssetAtPath<FieldAnimatedTile>(
                AnimatedRoot + "/" + key + ".asset");
        }

        private static void AssertMirroredPair(
            string sourceKey,
            string mirroredKey)
        {
            FieldAnimatedTile source = LoadAnimatedTile(sourceKey);
            FieldAnimatedTile mirrored = LoadAnimatedTile(mirroredKey);
            Assert.That(source.FlipX, Is.False);
            Assert.That(mirrored.FlipX, Is.True);
            for (int frame = 0; frame < source.FrameCount; frame++)
            {
                Assert.That(
                    mirrored.GetFrame(frame),
                    Is.SameAs(source.GetFrame(frame)));
            }
        }

        private static void AssertNormalizedPivot(
            string path,
            Vector2 expected)
        {
            var importer =
                AssetImporter.GetAtPath(path) as TextureImporter;
            Sprite sprite =
                AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.That(importer, Is.Not.Null);
            Assert.That(sprite, Is.Not.Null);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            bool expectsCenteredPivot =
                Mathf.Approximately(expected.x, 0.5f) &&
                Mathf.Approximately(expected.y, 0.5f);
            Assert.That(
                settings.spriteAlignment,
                Is.EqualTo((int)(
                    expectsCenteredPivot
                        ? SpriteAlignment.Center
                        : SpriteAlignment.Custom)));

            var actual = new Vector2(
                sprite.pivot.x / sprite.rect.width,
                sprite.pivot.y / sprite.rect.height);
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f));
        }
    }
}
