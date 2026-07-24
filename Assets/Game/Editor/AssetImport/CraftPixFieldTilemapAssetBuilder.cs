#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuleforgeTD.Maps;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.Tilemaps;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace RuleforgeTD.Editor.AssetImport
{
    public static class CraftPixFieldTilemapAssetBuilder
    {
        public const string StageOneScenePath =
            "Assets/Game/Scenes/Battle/Stage01.unity";
        public const string WebGLBuildPath =
            "Builds/WebGL/Stage01";

        private const string RawRoot =
            "Assets/ThirdParty/CraftPix/Raw/Maps/Fields";
        private const string TilesRoot = RawRoot + "/Tiles";
        private const string VisualAtlasPath =
            TilesRoot + "/FieldsTileset.png";
        private const string CollisionGuidePath =
            TilesRoot + "/FieldsTilesetTest.png";
        private const string ObjectsRoot = RawRoot + "/Objects";
        private const string AnimatedRoot =
            RawRoot + "/Animated Objects";
        private const string FlagRoot = AnimatedRoot + "/1 Flag";
        private const string CampfireRoot =
            AnimatedRoot + "/2 Campfire";
        private const string LogicContentPath =
            "Assets/Game/Data/Logic/phase1-content.json";

        private const string DataRoot =
            "Assets/Game/Data/Maps/Fields";
        private const string TerrainTileRoot =
            DataRoot + "/Tiles/Terrain";
        private const string PropTileRoot =
            DataRoot + "/Tiles/Props";
        private const string AnimatedTileRoot =
            DataRoot + "/Tiles/Animated";
        private const string PaletteRoot =
            DataRoot + "/Palettes";
        private const string PrefabRoot =
            "Assets/Game/Prefabs/Maps/Fields";
        private const string BuildSitePrefabPath =
            PrefabRoot + "/TowerBuildSite.prefab";

        private const string TerrainPaletteName =
            "Fields Terrain Palette";
        private const string PropPaletteName =
            "Fields Objects Palette";
        private const string AnimatedPaletteName =
            "Fields Animated Objects Palette";
        private const float PixelsPerUnit = 32f;
        private const float AnimationFrameDuration = 0.12f;
        private const int MapMinX = -3;
        private const int MapMaxX = 27;
        private const int MapMinY = -4;
        private const int MapMaxY = 17;
        private const float RoadHalfWidth = 1.35f;

        private static readonly string[] ExpectedObjectGroups =
        {
            "1 Shadow",
            "2 Fence",
            "3 Pointer",
            "4 Stone",
            "5 Grass",
            "6 Flower",
            "7 Decor",
            "8 Camp",
            "9 Bush"
        };

        private static readonly int[] ExpectedObjectCounts =
        {
            6,
            10,
            6,
            16,
            6,
            12,
            22,
            6,
            6
        };

        private static readonly string[] FlagDirectionKeys =
        {
            "Down",
            "DownLeft",
            "DownRight",
            "Up",
            "UpLeft",
            "UpRight",
            "Left",
            "Right"
        };

        [MenuItem(
            "Ruleforge TD/Assets/Rebuild Fields Tilemap Content")]
        public static void BuildFromMenu()
        {
            BuildAll();
        }

        [MenuItem(
            "Ruleforge TD/Assets/Validate Fields Tilemap Content")]
        public static void ValidateFromMenu()
        {
            ValidateGeneratedAssets();
        }

        [MenuItem("Ruleforge TD/Scenes/Open Stage 01")]
        public static void OpenStageOne()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    StageOneScenePath) == null)
            {
                BuildAll();
            }

            EditorSceneManager.OpenScene(
                StageOneScenePath,
                OpenSceneMode.Single);
        }

        [MenuItem(
            "Ruleforge TD/Scenes/Rebuild Stage 01 (Overwrite)")]
        public static void RebuildStageOneFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Rebuild Stage 01",
                "This replaces the current Stage01 Tilemap. " +
                "Use this only when you intentionally want the generated " +
                "baseline again.",
                "Rebuild",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            BuildAssetLibrary();
            CreateStageOneScene(true);
            ValidateGeneratedAssets();
        }

        public static void BuildFromCommandLine()
        {
            BuildAll();
        }

        public static void ValidateFromCommandLine()
        {
            ValidateGeneratedAssets();
        }

        public static void RebuildStageOneFromCommandLine()
        {
            BuildAssetLibrary();
            CreateStageOneScene(true);
            ValidateGeneratedAssets();
        }

        [MenuItem("Ruleforge TD/Build/Build Stage 01 (WebGL)")]
        public static void BuildWebGLFromCommandLine()
        {
            BuildAll();
            ValidateGeneratedAssets();

            if (Directory.Exists(WebGLBuildPath))
            {
                Directory.Delete(WebGLBuildPath, true);
            }

            Directory.CreateDirectory(WebGLBuildPath);
            PlayerSettings.WebGL.compressionFormat =
                WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { StageOneScenePath },
                locationPathName = WebGLBuildPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    "Stage 01 WebGL build failed with result " +
                    summary.result +
                    " and " +
                    summary.totalErrors +
                    " error(s).");
            }

            Debug.Log(
                "RULEFORGE_FIELDS_WEBGL_BUILD_OK path=" +
                WebGLBuildPath +
                " size=" +
                summary.totalSize +
                " duration=" +
                summary.totalTime);
        }

        private static void BuildAll()
        {
            BuildAssetLibrary();
            CreateStageOneScene(false);
            EnsureStageInBuildSettings();
            ValidateGeneratedAssets();
            Debug.Log(
                "RULEFORGE_FIELDS_BUILD_OK tiles=64 props=90 " +
                "animated=10 scene=" +
                StageOneScenePath);
        }

        private static void BuildAssetLibrary()
        {
            EnsureFolder(TerrainTileRoot);
            EnsureFolder(PropTileRoot);
            EnsureFolder(AnimatedTileRoot);
            EnsureFolder(PaletteRoot);
            EnsureFolder(PrefabRoot);
            EnsureFolder("Assets/Game/Scenes/Battle");

            ValidateSourceAssets();
            uint[][] masks =
                FieldTilesetMaskUtility.LoadTileMasks(
                    CollisionGuidePath);

            ConfigureVisualAtlas(masks);
            ConfigureSingleTexture(
                CollisionGuidePath,
                new Vector2(0.5f, 0.5f));
            ConfigureIndividualTiles();
            ConfigureObjectTextures();
            ConfigureAnimatedSheets();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport);

            Dictionary<int, FieldTerrainTile> terrainTiles =
                CreateTerrainTiles(masks);
            Dictionary<string, Tile> propTiles =
                CreatePropTiles();
            Dictionary<string, FieldAnimatedTile> animatedTiles =
                CreateAnimatedTiles();
            CreateTerrainPalette(terrainTiles);
            CreatePropPalette(propTiles);
            CreateAnimatedPalette(animatedTiles);
            CreateBuildSitePrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport);
        }

        private static void ValidateSourceAssets()
        {
            FieldTilesetMaskUtility
                .ValidateAtlasAgainstIndividualTiles(
                    VisualAtlasPath,
                    TilesRoot);
            uint[][] masks =
                FieldTilesetMaskUtility.LoadTileMasks(
                    CollisionGuidePath);
            if (FieldTilesetMaskUtility.CountBlockedPixels(
                    masks[37]) != 1024)
            {
                throw new InvalidOperationException(
                    "Fields tile 38 must be fully blocked.");
            }

            int[] openTiles = { 11, 18, 20, 27 };
            for (int i = 0; i < openTiles.Length; i++)
            {
                if (FieldTilesetMaskUtility.CountBlockedPixels(
                        masks[openTiles[i] - 1]) != 0)
                {
                    throw new InvalidOperationException(
                        "Expected fully walkable tile " +
                        openTiles[i] +
                        ".");
                }
            }

            int totalObjectCount = 0;
            for (int groupIndex = 0;
                 groupIndex < ExpectedObjectGroups.Length;
                 groupIndex++)
            {
                string groupPath =
                    ObjectsRoot +
                    "/" +
                    ExpectedObjectGroups[groupIndex];
                int count = Directory.GetFiles(
                        groupPath,
                        "*.png",
                        SearchOption.TopDirectoryOnly)
                    .Length;
                if (count != ExpectedObjectCounts[groupIndex])
                {
                    throw new InvalidOperationException(
                        groupPath +
                        " expected " +
                        ExpectedObjectCounts[groupIndex] +
                        " PNGs but found " +
                        count +
                        ".");
                }

                totalObjectCount += count;
            }

            if (totalObjectCount != 90)
            {
                throw new InvalidOperationException(
                    "Fields object variant count must be 90.");
            }

            ValidatePngSize(
                ObjectsRoot + "/PlaceForTower1.png",
                62,
                61);
            ValidatePngSize(
                ObjectsRoot + "/PlaceForTower2.png",
                63,
                64);
            for (int flag = 1; flag <= 5; flag++)
            {
                ValidatePngSize(
                    FlagRoot + "/" + flag + ".png",
                    192,
                    64);
            }

            ValidatePngSize(CampfireRoot + "/1.png", 192, 64);
            ValidatePngSize(CampfireRoot + "/2.png", 192, 32);
        }

        private static void ConfigureVisualAtlas(uint[][] masks)
        {
            var importer =
                AssetImporter.GetAtPath(VisualAtlasPath)
                    as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    "Unable to load Fields visual atlas importer.");
            }

            ApplyPixelTextureSettings(
                importer,
                SpriteImportMode.Multiple);

            bool needsSlice = true;
#pragma warning disable CS0618
            SpriteMetaData[] existing = importer.spritesheet;
#pragma warning restore CS0618
            if (existing != null &&
                existing.Length == FieldTilesetMaskUtility.TileCount)
            {
                needsSlice = false;
                for (int i = 0; i < existing.Length; i++)
                {
                    Rect expected = GetAtlasRect(i + 1);
                    if (existing[i].name !=
                            GetTerrainSpriteName(i + 1) ||
                        existing[i].rect != expected)
                    {
                        needsSlice = true;
                        break;
                    }
                }
            }

            if (needsSlice)
            {
                var metadata = new SpriteMetaData[
                    FieldTilesetMaskUtility.TileCount];
                for (int tileNumber = 1;
                     tileNumber <=
                     FieldTilesetMaskUtility.TileCount;
                     tileNumber++)
                {
                    metadata[tileNumber - 1] = new SpriteMetaData
                    {
                        name = GetTerrainSpriteName(tileNumber),
                        rect = GetAtlasRect(tileNumber),
                        alignment = (int)SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                        border = Vector4.zero
                    };
                }

#pragma warning disable CS0618
                importer.spritesheet = metadata;
#pragma warning restore CS0618
            }

            importer.SaveAndReimport();
            ApplyTerrainPhysicsShapes(importer, masks);
        }

        private static void ApplyTerrainPhysicsShapes(
            TextureImporter importer,
            uint[][] masks)
        {
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            ISpriteEditorDataProvider dataProvider =
                factories.GetSpriteEditorDataProviderFromObject(importer);
            if (dataProvider == null)
            {
                throw new InvalidOperationException(
                    "Fields atlas has no Sprite data provider.");
            }

            dataProvider.InitSpriteEditorDataProvider();
            ISpritePhysicsOutlineDataProvider physicsProvider =
                dataProvider
                    .GetDataProvider<
                        ISpritePhysicsOutlineDataProvider>();
            if (physicsProvider == null)
            {
                throw new InvalidOperationException(
                    "Fields atlas has no physics-outline provider.");
            }

            SpriteRect[] rects = dataProvider.GetSpriteRects();
            if (rects.Length !=
                FieldTilesetMaskUtility.TileCount)
            {
                throw new InvalidOperationException(
                    "Fields atlas must expose 64 Sprite rects.");
            }

            for (int i = 0; i < rects.Length; i++)
            {
                int tileNumber =
                    ParseTrailingNumber(rects[i].name);
                List<Vector2[]> shapes =
                    FieldTilesetMaskUtility
                        .CreateRectanglePhysicsShapes(
                            masks[tileNumber - 1]);
                physicsProvider.SetOutlines(
                    rects[i].spriteID,
                    shapes);
                physicsProvider.SetTessellationDetail(
                    rects[i].spriteID,
                    1f);
            }

            dataProvider.Apply();
            AssetDatabase.ImportAsset(
                VisualAtlasPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
        }

        private static void ConfigureIndividualTiles()
        {
            for (int tileNumber = 1;
                 tileNumber <=
                 FieldTilesetMaskUtility.TileCount;
                 tileNumber++)
            {
                ConfigureSingleTexture(
                    TilesRoot +
                    "/FieldsTile_" +
                    tileNumber.ToString("00") +
                    ".png",
                    new Vector2(0.5f, 0.5f));
            }
        }

        private static void ConfigureObjectTextures()
        {
            string[] paths = Directory.GetFiles(
                    ObjectsRoot,
                    "*.png",
                    SearchOption.AllDirectories)
                .Select(NormalizePath)
                .OrderBy(path => path, NaturalPathComparer.Instance)
                .ToArray();
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                Vector2 pivot = IsGroundDecal(path)
                    ? new Vector2(0.5f, 0.5f)
                    : path.EndsWith(
                        "PlaceForTower1.png",
                        StringComparison.Ordinal) ||
                      path.EndsWith(
                        "PlaceForTower2.png",
                        StringComparison.Ordinal)
                        ? new Vector2(0.5f, 0.5f)
                        : new Vector2(0.5f, 0f);
                ConfigureSingleTexture(path, pivot);
            }
        }

        private static void ConfigureAnimatedSheets()
        {
            for (int flag = 1; flag <= 5; flag++)
            {
                ConfigureHorizontalSheet(
                    FlagRoot + "/" + flag + ".png",
                    "Flag_" + flag,
                    32,
                    64);
            }

            ConfigureHorizontalSheet(
                CampfireRoot + "/1.png",
                "Campfire_Unlit",
                32,
                64);
            ConfigureHorizontalSheet(
                CampfireRoot + "/2.png",
                "Campfire_Lit",
                32,
                32);
        }

        private static void ConfigureSingleTexture(
            string path,
            Vector2 pivot)
        {
            var importer =
                AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    "Unable to load texture importer: " + path);
            }

            ApplyPixelTextureSettings(
                importer,
                SpriteImportMode.Single);
            importer.spritePivot = pivot;
            importer.SaveAndReimport();
        }

        private static void ConfigureHorizontalSheet(
            string path,
            string framePrefix,
            int frameWidth,
            int frameHeight)
        {
            var importer =
                AssetImporter.GetAtPath(path) as TextureImporter;
            Texture2D texture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (importer == null || texture == null)
            {
                throw new InvalidOperationException(
                    "Unable to load animated sheet: " + path);
            }

            if (texture.height != frameHeight ||
                texture.width % frameWidth != 0)
            {
                throw new InvalidOperationException(
                    path +
                    " does not match the expected frame grid.");
            }

            ApplyPixelTextureSettings(
                importer,
                SpriteImportMode.Multiple);
            int frameCount = texture.width / frameWidth;
            var metadata = new SpriteMetaData[frameCount];
            for (int frame = 0; frame < frameCount; frame++)
            {
                metadata[frame] = new SpriteMetaData
                {
                    name = framePrefix +
                        "_" +
                        frame.ToString("00"),
                    rect = new Rect(
                        frame * frameWidth,
                        0,
                        frameWidth,
                        frameHeight),
                    alignment = (int)SpriteAlignment.Custom,
                    pivot = new Vector2(0.5f, 0f),
                    border = Vector4.zero
                };
            }

#pragma warning disable CS0618
            importer.spritesheet = metadata;
#pragma warning restore CS0618
            importer.SaveAndReimport();
        }

        private static void ApplyPixelTextureSettings(
            TextureImporter importer,
            SpriteImportMode mode)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = mode;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression =
                TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;
        }

        private static Dictionary<int, FieldTerrainTile>
            CreateTerrainTiles(uint[][] masks)
        {
            Dictionary<string, Sprite> sprites =
                LoadSprites(VisualAtlasPath)
                    .ToDictionary(
                        sprite => sprite.name,
                        StringComparer.Ordinal);
            var result =
                new Dictionary<int, FieldTerrainTile>();
            for (int tileNumber = 1;
                 tileNumber <=
                 FieldTilesetMaskUtility.TileCount;
                 tileNumber++)
            {
                string assetPath =
                    TerrainTileRoot +
                    "/FieldTile_" +
                    tileNumber.ToString("00") +
                    ".asset";
                FieldTerrainTile tile =
                    LoadOrCreateAsset<FieldTerrainTile>(assetPath);
                tile.name = Path.GetFileNameWithoutExtension(assetPath);
                tile.ConfigureAuthoring(
                    tileNumber,
                    sprites[GetTerrainSpriteName(tileNumber)],
                    masks[tileNumber - 1]);
                EditorUtility.SetDirty(tile);
                result.Add(tileNumber, tile);
            }

            return result;
        }

        private static Dictionary<string, Tile> CreatePropTiles()
        {
            var result = new Dictionary<string, Tile>(
                StringComparer.Ordinal);
            for (int groupIndex = 0;
                 groupIndex < ExpectedObjectGroups.Length;
                 groupIndex++)
            {
                string groupName = ExpectedObjectGroups[groupIndex];
                string sourceGroupPath =
                    ObjectsRoot + "/" + groupName;
                string generatedGroupPath =
                    PropTileRoot +
                    "/" +
                    SanitizeAssetName(groupName);
                EnsureFolder(generatedGroupPath);
                string[] sourcePaths = Directory.GetFiles(
                        sourceGroupPath,
                        "*.png",
                        SearchOption.TopDirectoryOnly)
                    .Select(NormalizePath)
                    .OrderBy(
                        path => path,
                        NaturalPathComparer.Instance)
                    .ToArray();
                for (int fileIndex = 0;
                     fileIndex < sourcePaths.Length;
                     fileIndex++)
                {
                    string sourcePath = sourcePaths[fileIndex];
                    string baseName =
                        Path.GetFileNameWithoutExtension(sourcePath);
                    string key = groupName + "/" + baseName;
                    string assetPath =
                        generatedGroupPath +
                        "/" +
                        SanitizeAssetName(baseName) +
                        ".asset";
                    Tile tile = LoadOrCreateAsset<Tile>(assetPath);
                    tile.name = Path.GetFileNameWithoutExtension(assetPath);
                    tile.sprite =
                        AssetDatabase.LoadAssetAtPath<Sprite>(
                            sourcePath);
                    tile.color = Color.white;
                    tile.transform = Matrix4x4.identity;
                    tile.gameObject = null;
                    tile.flags =
                        TileFlags.LockColor |
                        TileFlags.LockTransform;
                    tile.colliderType = Tile.ColliderType.None;
                    EditorUtility.SetDirty(tile);
                    result.Add(key, tile);
                }
            }

            return result;
        }

        private static Dictionary<string, FieldAnimatedTile>
            CreateAnimatedTiles()
        {
            var sheetFrames = new Dictionary<string, Sprite[]>(
                StringComparer.Ordinal);
            for (int flag = 1; flag <= 5; flag++)
            {
                sheetFrames.Add(
                    "Flag" + flag,
                    LoadSprites(FlagRoot + "/" + flag + ".png"));
            }

            sheetFrames.Add(
                "CampfireUnlit",
                LoadSprites(CampfireRoot + "/1.png"));
            sheetFrames.Add(
                "CampfireLit",
                LoadSprites(CampfireRoot + "/2.png"));

            var definitions = new[]
            {
                new AnimatedTileDefinition(
                    "Flag_Down",
                    "Flag1",
                    false),
                new AnimatedTileDefinition(
                    "Flag_DownLeft",
                    "Flag2",
                    false),
                new AnimatedTileDefinition(
                    "Flag_DownRight",
                    "Flag2",
                    true),
                new AnimatedTileDefinition(
                    "Flag_Up",
                    "Flag3",
                    false),
                new AnimatedTileDefinition(
                    "Flag_UpLeft",
                    "Flag4",
                    false),
                new AnimatedTileDefinition(
                    "Flag_UpRight",
                    "Flag4",
                    true),
                new AnimatedTileDefinition(
                    "Flag_Left",
                    "Flag5",
                    false),
                new AnimatedTileDefinition(
                    "Flag_Right",
                    "Flag5",
                    true),
                new AnimatedTileDefinition(
                    "Campfire_Unlit",
                    "CampfireUnlit",
                    false),
                new AnimatedTileDefinition(
                    "Campfire_Lit",
                    "CampfireLit",
                    false)
            };

            var result =
                new Dictionary<string, FieldAnimatedTile>(
                    StringComparer.Ordinal);
            for (int i = 0; i < definitions.Length; i++)
            {
                AnimatedTileDefinition definition = definitions[i];
                string assetPath =
                    AnimatedTileRoot +
                    "/" +
                    definition.Key +
                    ".asset";
                FieldAnimatedTile tile =
                    LoadOrCreateAsset<FieldAnimatedTile>(assetPath);
                tile.name = Path.GetFileNameWithoutExtension(assetPath);
                tile.ConfigureAuthoring(
                    sheetFrames[definition.FrameBank],
                    definition.FlipX);
                EditorUtility.SetDirty(tile);
                result.Add(definition.Key, tile);
            }

            return result;
        }

        private static void CreateTerrainPalette(
            Dictionary<int, FieldTerrainTile> tiles)
        {
            GameObject contents =
                LoadOrCreatePalette(TerrainPaletteName);
            try
            {
                Tilemap tilemap =
                    contents.GetComponentInChildren<Tilemap>(true);
                tilemap.ClearAllTiles();
                for (int tileNumber = 1;
                     tileNumber <=
                     FieldTilesetMaskUtility.TileCount;
                     tileNumber++)
                {
                    int column =
                        (tileNumber - 1) %
                        FieldTilesetMaskUtility.AtlasColumns;
                    int topRow =
                        (tileNumber - 1) /
                        FieldTilesetMaskUtility.AtlasColumns;
                    tilemap.SetTile(
                        new Vector3Int(
                            column,
                            FieldTilesetMaskUtility.AtlasRows -
                            topRow -
                            1,
                            0),
                        tiles[tileNumber]);
                }

                SavePaletteContents(contents, TerrainPaletteName);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void CreatePropPalette(
            Dictionary<string, Tile> tiles)
        {
            GameObject contents =
                LoadOrCreatePalette(PropPaletteName);
            try
            {
                Tilemap tilemap =
                    contents.GetComponentInChildren<Tilemap>(true);
                tilemap.ClearAllTiles();
                int row = 0;
                for (int groupIndex = 0;
                     groupIndex < ExpectedObjectGroups.Length;
                     groupIndex++)
                {
                    string prefix =
                        ExpectedObjectGroups[groupIndex] + "/";
                    Tile[] groupTiles = tiles
                        .Where(pair => pair.Key.StartsWith(
                            prefix,
                            StringComparison.Ordinal))
                        .OrderBy(
                            pair => pair.Key,
                            NaturalPathComparer.Instance)
                        .Select(pair => pair.Value)
                        .ToArray();
                    for (int column = 0;
                         column < groupTiles.Length;
                         column++)
                    {
                        tilemap.SetTile(
                            new Vector3Int(column, -row, 0),
                            groupTiles[column]);
                    }

                    row += 3;
                }

                SavePaletteContents(contents, PropPaletteName);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void CreateAnimatedPalette(
            Dictionary<string, FieldAnimatedTile> tiles)
        {
            GameObject contents =
                LoadOrCreatePalette(AnimatedPaletteName);
            try
            {
                Tilemap tilemap =
                    contents.GetComponentInChildren<Tilemap>(true);
                tilemap.ClearAllTiles();
                tilemap.tileAnchor = new Vector3(0.5f, 0f, 0f);
                tilemap.animationFrameRate =
                    1f / AnimationFrameDuration;
                for (int i = 0;
                     i < FlagDirectionKeys.Length;
                     i++)
                {
                    tilemap.SetTile(
                        new Vector3Int(i, 0, 0),
                        tiles["Flag_" + FlagDirectionKeys[i]]);
                }

                tilemap.SetTile(
                    new Vector3Int(0, -3, 0),
                    tiles["Campfire_Unlit"]);
                tilemap.SetTile(
                    new Vector3Int(1, -3, 0),
                    tiles["Campfire_Lit"]);
                SavePaletteContents(contents, AnimatedPaletteName);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static GameObject LoadOrCreatePalette(string name)
        {
            string path = PaletteRoot + "/" + name + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                GridPaletteUtility.CreateNewPalette(
                    PaletteRoot,
                    name,
                    GridLayout.CellLayout.Rectangle,
                    GridPalette.CellSizing.Manual,
                    Vector3.one,
                    GridLayout.CellSwizzle.XYZ);
            }

            return PrefabUtility.LoadPrefabContents(path);
        }

        private static void SavePaletteContents(
            GameObject contents,
            string name)
        {
            string path = PaletteRoot + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(contents, path);
        }

        private static void CreateBuildSitePrefab()
        {
            Sprite available =
                AssetDatabase.LoadAssetAtPath<Sprite>(
                    ObjectsRoot + "/PlaceForTower1.png");
            Sprite locked =
                AssetDatabase.LoadAssetAtPath<Sprite>(
                    ObjectsRoot + "/PlaceForTower2.png");
            var root = new GameObject("Tower Build Site");
            try
            {
                SpriteRenderer renderer =
                    root.AddComponent<SpriteRenderer>();
                renderer.sprite = available;
                renderer.sortingOrder = 30;
                var collider = root.AddComponent<BoxCollider2D>();
                collider.size = new Vector2(1.9f, 1.9f);
                collider.isTrigger = true;
                TowerBuildSiteView view =
                    root.AddComponent<TowerBuildSiteView>();
                view.ConfigureAuthoring(
                    -1,
                    0,
                    available,
                    locked,
                    true);
                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    BuildSitePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void CreateStageOneScene(bool overwrite)
        {
            bool exists =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    StageOneScenePath) != null;
            if (exists && !overwrite)
            {
                return;
            }

            RunMapSource run = LoadRunMapSource();
            Scene previousActiveScene =
                SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            scene.name = "Stage01";

            var stageRoot = new GameObject(
                "Stage 01 - Verdant Switchbacks");
            var gridObject = new GameObject("Grid");
            gridObject.transform.SetParent(stageRoot.transform);
            gridObject.transform.position =
                new Vector3(-0.5f, -0.5f, 0f);
            Grid grid = gridObject.AddComponent<Grid>();
            grid.cellSize = Vector3.one;

            Tilemap terrain = CreateTilemap(
                gridObject.transform,
                "Terrain",
                -100,
                TilemapRenderer.Mode.Chunk,
                new Vector3(0.5f, 0.5f, 0f));
            Rigidbody2D terrainBody =
                terrain.gameObject.AddComponent<Rigidbody2D>();
            terrainBody.bodyType = RigidbodyType2D.Static;
            CompositeCollider2D composite =
                terrain.gameObject.AddComponent<CompositeCollider2D>();
            composite.geometryType =
                CompositeCollider2D.GeometryType.Polygons;
            TilemapCollider2D tilemapCollider =
                terrain.gameObject.AddComponent<TilemapCollider2D>();
            tilemapCollider.usedByComposite = true;

            Tilemap decals = CreateTilemap(
                gridObject.transform,
                "Ground Decals",
                -50,
                TilemapRenderer.Mode.Individual,
                new Vector3(0.5f, 0.5f, 0f));
            Tilemap props = CreateTilemap(
                gridObject.transform,
                "Objects",
                0,
                TilemapRenderer.Mode.Individual,
                new Vector3(0.5f, 0f, 0f));
            Tilemap animated = CreateTilemap(
                gridObject.transform,
                "Animated Objects",
                10,
                TilemapRenderer.Mode.Individual,
                new Vector3(0.5f, 0f, 0f));
            animated.animationFrameRate =
                1f / AnimationFrameDuration;

            Dictionary<int, FieldTerrainTile> terrainTiles =
                LoadTerrainTiles();
            uint[][] masks =
                FieldTilesetMaskUtility.LoadTileMasks(
                    CollisionGuidePath);
            bool[,] walkable =
                BuildWalkableLayout(run.PathPoints);
            PaintTerrain(
                terrain,
                terrainTiles,
                masks,
                walkable);

            Dictionary<string, Tile> propTiles =
                LoadPropTiles();
            Dictionary<string, FieldAnimatedTile> animatedTiles =
                LoadAnimatedTiles();
            PaintStageProps(decals, props, propTiles);
            PaintAnimatedProps(animated, animatedTiles);

            var navigationObject = new GameObject("Navigation");
            navigationObject.transform.SetParent(stageRoot.transform);
            StageNavigationMask navigationMask =
                navigationObject.AddComponent<StageNavigationMask>();
            navigationMask.ConfigureAuthoring(terrain);
            StagePathAuthoring path =
                navigationObject.AddComponent<StagePathAuthoring>();
            path.ConfigureAuthoring(run.PathPoints);

            var sitesRoot = new GameObject("Tower Build Sites");
            sitesRoot.transform.SetParent(stageRoot.transform);
            TowerBuildSiteView[] sites = CreateBuildSites(
                scene,
                sitesRoot.transform,
                run);

            FieldStageMap stageMap =
                stageRoot.AddComponent<FieldStageMap>();
            stageMap.ConfigureAuthoring(
                terrain,
                decals,
                props,
                animated,
                navigationMask,
                path,
                sites);

            CreateCamera(stageRoot.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, StageOneScenePath);

            if (previousActiveScene.IsValid() &&
                previousActiveScene.path != StageOneScenePath)
            {
                // The generated scene remains active in batch mode. In an
                // interactive editor this method is only reached after an
                // explicit overwrite confirmation.
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport);
        }

        private static Tilemap CreateTilemap(
            Transform parent,
            string name,
            int sortingOrder,
            TilemapRenderer.Mode mode,
            Vector3 tileAnchor)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent);
            Tilemap tilemap = gameObject.AddComponent<Tilemap>();
            tilemap.tileAnchor = tileAnchor;
            TilemapRenderer renderer =
                gameObject.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = sortingOrder;
            renderer.mode = mode;
            return tilemap;
        }

        private static bool[,] BuildWalkableLayout(
            Vector2[] pathPoints)
        {
            int width = MapMaxX - MapMinX + 1;
            int height = MapMaxY - MapMinY + 1;
            var walkable = new bool[width, height];
            for (int localY = 0; localY < height; localY++)
            {
                for (int localX = 0; localX < width; localX++)
                {
                    var worldCenter = new Vector2(
                        MapMinX + localX,
                        MapMinY + localY);
                    float distance = float.MaxValue;
                    for (int segment = 0;
                         segment < pathPoints.Length - 1;
                         segment++)
                    {
                        distance = Mathf.Min(
                            distance,
                            DistanceToSegment(
                                worldCenter,
                                pathPoints[segment],
                                pathPoints[segment + 1]));
                    }

                    walkable[localX, localY] =
                        distance <= RoadHalfWidth;
                }
            }

            return walkable;
        }

        private static void PaintTerrain(
            Tilemap terrain,
            Dictionary<int, FieldTerrainTile> tiles,
            uint[][] masks,
            bool[,] walkable)
        {
            int width = walkable.GetLength(0);
            int height = walkable.GetLength(1);
            for (int localY = 0; localY < height; localY++)
            {
                for (int localX = 0; localX < width; localX++)
                {
                    int tileNumber = walkable[localX, localY]
                        ? FieldTilesetMaskUtility.ResolveWalkableTile(
                            walkable,
                            localX,
                            localY,
                            masks,
                            104729)
                        : 38;
                    terrain.SetTile(
                        new Vector3Int(
                            MapMinX + localX,
                            MapMinY + localY,
                            0),
                        tiles[tileNumber]);
                }
            }

            terrain.CompressBounds();
        }

        private static void PaintStageProps(
            Tilemap decals,
            Tilemap props,
            Dictionary<string, Tile> tiles)
        {
            var decalPlacements = new[]
            {
                new TilePlacement("1 Shadow/6", -1, 15),
                new TilePlacement("1 Shadow/5", 21, 15),
                new TilePlacement("1 Shadow/4", 24, 8),
                new TilePlacement("5 Grass/2", 2, 14),
                new TilePlacement("5 Grass/5", 13, 14),
                new TilePlacement("5 Grass/3", 19, 9),
                new TilePlacement("6 Flower/1", 1, 3),
                new TilePlacement("6 Flower/7", 12, 10),
                new TilePlacement("6 Flower/10", 19, 15),
                new TilePlacement("7 Decor/Dirt2", 20, 15),
                new TilePlacement("7 Decor/Dirt6", 23, 8),
                new TilePlacement("3 Pointer/1", 3, 0),
                new TilePlacement("3 Pointer/4", 8, 3),
                new TilePlacement("3 Pointer/2", 12, 6),
                new TilePlacement("3 Pointer/3", 16, 9),
                new TilePlacement("3 Pointer/5", 20, 12)
            };
            for (int i = 0; i < decalPlacements.Length; i++)
            {
                SetTile(decals, tiles, decalPlacements[i]);
            }

            var propPlacements = new[]
            {
                new TilePlacement("7 Decor/Tree1", -1, 15),
                new TilePlacement("7 Decor/Tree2", 26, 16),
                new TilePlacement("9 Bush/4", 3, 15),
                new TilePlacement("9 Bush/6", 13, 14),
                new TilePlacement("9 Bush/2", 24, 8),
                new TilePlacement("8 Camp/1", 21, 15),
                new TilePlacement("8 Camp/3", 23, 15),
                new TilePlacement("7 Decor/Box1", 20, 14),
                new TilePlacement("7 Decor/Box3", 22, 14),
                new TilePlacement("7 Decor/Log3", 24, 14),
                new TilePlacement("7 Decor/Lamp1", 5, 3),
                new TilePlacement("7 Decor/Lamp3", 15, 5),
                new TilePlacement("4 Stone/11", -2, 5),
                new TilePlacement("4 Stone/8", 10, 15),
                new TilePlacement("4 Stone/15", 19, 3),
                new TilePlacement("2 Fence/1", 0, 14),
                new TilePlacement("2 Fence/2", 1, 14),
                new TilePlacement("2 Fence/7", 25, 15),
                new TilePlacement("2 Fence/8", 25, 14)
            };
            for (int i = 0; i < propPlacements.Length; i++)
            {
                SetTile(props, tiles, propPlacements[i]);
            }
        }

        private static void PaintAnimatedProps(
            Tilemap animated,
            Dictionary<string, FieldAnimatedTile> tiles)
        {
            var placements = new[]
            {
                new AnimatedTilePlacement("Flag_Right", -2, 1),
                new AnimatedTilePlacement("Flag_Down", 7, 4),
                new AnimatedTilePlacement("Flag_UpRight", 14, 8),
                new AnimatedTilePlacement("Flag_Up", 22, 14),
                new AnimatedTilePlacement("Flag_Left", 26, 11),
                new AnimatedTilePlacement("Campfire_Unlit", 4, 15),
                new AnimatedTilePlacement("Campfire_Lit", 21, 14)
            };
            for (int i = 0; i < placements.Length; i++)
            {
                AnimatedTilePlacement placement = placements[i];
                animated.SetTile(
                    new Vector3Int(
                        placement.X,
                        placement.Y,
                        0),
                    tiles[placement.Key]);
            }
        }

        private static TowerBuildSiteView[] CreateBuildSites(
            Scene scene,
            Transform parent,
            RunMapSource run)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    BuildSitePrefabPath);
            Sprite available =
                AssetDatabase.LoadAssetAtPath<Sprite>(
                    ObjectsRoot + "/PlaceForTower1.png");
            Sprite locked =
                AssetDatabase.LoadAssetAtPath<Sprite>(
                    ObjectsRoot + "/PlaceForTower2.png");
            var result =
                new TowerBuildSiteView[run.BuildSpots.Length];
            for (int index = 0;
                 index < run.BuildSpots.Length;
                 index++)
            {
                var instance = (GameObject)PrefabUtility
                    .InstantiatePrefab(prefab, scene);
                instance.name =
                    "Build Site " + index.ToString("00");
                instance.transform.SetParent(parent);
                instance.transform.position =
                    run.BuildSpots[index];
                TowerBuildSiteView view =
                    instance.GetComponent<TowerBuildSiteView>();
                int cost = run.BuildSpotUnlockCosts[index];
                view.ConfigureAuthoring(
                    index,
                    cost,
                    available,
                    locked,
                    cost == 0);
                result[index] = view;
            }

            return result;
        }

        private static void CreateCamera(Transform parent)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(parent);
            cameraObject.transform.position =
                new Vector3(12f, 6.5f, -10f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 11.25f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor =
                new Color32(66, 76, 45, 255);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            cameraObject.AddComponent<AudioListener>();
            PixelPerfectCamera pixelPerfect =
                cameraObject.AddComponent<PixelPerfectCamera>();
            pixelPerfect.assetsPPU = 32;
            pixelPerfect.refResolutionX = 1280;
            pixelPerfect.refResolutionY = 720;
        }

        private static void ValidateGeneratedAssets()
        {
            uint[][] masks =
                FieldTilesetMaskUtility.LoadTileMasks(
                    CollisionGuidePath);
            Dictionary<int, FieldTerrainTile> tiles =
                LoadTerrainTiles();
            if (tiles.Count != 64)
            {
                throw new InvalidOperationException(
                    "Generated terrain tile count must be 64.");
            }

            for (int tileNumber = 1;
                 tileNumber <= 64;
                 tileNumber++)
            {
                FieldTerrainTile tile = tiles[tileNumber];
                int expectedBlocked =
                    FieldTilesetMaskUtility.CountBlockedPixels(
                        masks[tileNumber - 1]);
                if (tile.TileNumber != tileNumber ||
                    tile.BlockedPixelCount != expectedBlocked)
                {
                    throw new InvalidOperationException(
                        "Generated tile mask mismatch for tile " +
                        tileNumber +
                        ".");
                }

                if ((expectedBlocked == 0) !=
                    (tile.colliderType ==
                     Tile.ColliderType.None))
                {
                    throw new InvalidOperationException(
                        "Generated collider mode mismatch for tile " +
                        tileNumber +
                        ".");
                }
            }

            if (!tiles[38].IsFullyBlocked)
            {
                throw new InvalidOperationException(
                    "Generated tile 38 must be fully blocked.");
            }

            if (LoadPropTiles().Count != 90)
            {
                throw new InvalidOperationException(
                    "Generated prop tile count must be 90.");
            }

            Dictionary<string, FieldAnimatedTile> animated =
                LoadAnimatedTiles();
            if (animated.Count != 10)
            {
                throw new InvalidOperationException(
                    "Generated animated tile count must be 10.");
            }

            foreach (KeyValuePair<string, FieldAnimatedTile> pair
                     in animated)
            {
                if (pair.Value.FrameCount != 6)
                {
                    throw new InvalidOperationException(
                        pair.Key +
                        " must contain exactly six frames.");
                }
            }

            ValidateFlagDirections(animated);
            ValidateStageScene();
            Debug.Log(
                "RULEFORGE_FIELDS_VALIDATE_OK tiles=64 props=90 " +
                "animated=10 buildSites=8");
        }

        private static void ValidateFlagDirections(
            Dictionary<string, FieldAnimatedTile> animated)
        {
            string[] mirrored =
            {
                "Flag_DownRight",
                "Flag_UpRight",
                "Flag_Right"
            };
            for (int i = 0; i < FlagDirectionKeys.Length; i++)
            {
                string key = "Flag_" + FlagDirectionKeys[i];
                bool expectedFlip =
                    Array.IndexOf(mirrored, key) >= 0;
                if (animated[key].FlipX != expectedFlip)
                {
                    throw new InvalidOperationException(
                        key + " has the wrong flipX setting.");
                }
            }
        }

        private static void ValidateStageScene()
        {
            SceneAsset sceneAsset =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    StageOneScenePath);
            if (sceneAsset == null)
            {
                throw new InvalidOperationException(
                    "Stage 01 scene is missing.");
            }

            Scene activeBefore = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(StageOneScenePath);
            bool openedForValidation = !scene.IsValid() ||
                !scene.isLoaded;
            if (openedForValidation)
            {
                scene = EditorSceneManager.OpenScene(
                    StageOneScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                FieldStageMap stage = scene
                    .GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<FieldStageMap>(
                            true))
                    .SingleOrDefault();
                if (stage == null ||
                    stage.Terrain == null ||
                    stage.GroundDecals == null ||
                    stage.Props == null ||
                    stage.AnimatedObjects == null ||
                    stage.NavigationMask == null ||
                    stage.Path == null)
                {
                    throw new InvalidOperationException(
                        "Stage 01 Tilemap hierarchy is incomplete.");
                }

                if (stage.BuildSiteCount != 8)
                {
                    throw new InvalidOperationException(
                        "Stage 01 must expose eight build sites.");
                }

                RunMapSource run = LoadRunMapSource();
                for (int i = 0; i < stage.BuildSiteCount; i++)
                {
                    TowerBuildSiteView site =
                        stage.GetBuildSite(i);
                    if (site.BuildPointIndex != i ||
                        site.UnlockCost !=
                        run.BuildSpotUnlockCosts[i])
                    {
                        throw new InvalidOperationException(
                            "Build site " +
                            i +
                            " does not match run content.");
                    }
                }

                ValidatePathClearance(
                    stage.NavigationMask,
                    run.PathPoints,
                    0.25f);
                TilemapCollider2D tileCollider =
                    stage.Terrain
                        .GetComponent<TilemapCollider2D>();
                CompositeCollider2D composite =
                    stage.Terrain
                        .GetComponent<CompositeCollider2D>();
                Rigidbody2D rigidbody =
                    stage.Terrain.GetComponent<Rigidbody2D>();
                if (tileCollider == null ||
                    composite == null ||
                    rigidbody == null ||
                    rigidbody.bodyType != RigidbodyType2D.Static)
                {
                    throw new InvalidOperationException(
                        "Stage terrain collision components are incomplete.");
                }

                if (stage.AnimatedObjects.GetUsedTilesCount() == 0 ||
                    stage.Props.GetUsedTilesCount() == 0)
                {
                    throw new InvalidOperationException(
                        "Stage 01 must contain props and animated objects.");
                }
            }
            finally
            {
                if (openedForValidation && scene.IsValid())
                {
                    EditorSceneManager.CloseScene(scene, true);
                }

                if (activeBefore.IsValid() &&
                    activeBefore.isLoaded)
                {
                    SceneManager.SetActiveScene(activeBefore);
                }
            }
        }

        private static void ValidatePathClearance(
            StageNavigationMask navigation,
            Vector2[] pathPoints,
            float radius)
        {
            Vector2[] offsets =
            {
                Vector2.zero,
                Vector2.up * radius,
                Vector2.down * radius,
                Vector2.left * radius,
                Vector2.right * radius
            };
            for (int segment = 0;
                 segment < pathPoints.Length - 1;
                 segment++)
            {
                Vector2 from = pathPoints[segment];
                Vector2 to = pathPoints[segment + 1];
                float length = Vector2.Distance(from, to);
                int steps = Mathf.Max(
                    1,
                    Mathf.CeilToInt(length / 0.125f));
                for (int step = 0; step <= steps; step++)
                {
                    Vector2 point = Vector2.Lerp(
                        from,
                        to,
                        step / (float)steps);
                    for (int offsetIndex = 0;
                         offsetIndex < offsets.Length;
                         offsetIndex++)
                    {
                        if (navigation.IsBlocked(
                                point + offsets[offsetIndex]))
                        {
                            throw new InvalidOperationException(
                                "Stage path intersects blocked terrain at " +
                                (point + offsets[offsetIndex]) +
                                ".");
                        }
                    }
                }
            }
        }

        private static Dictionary<int, FieldTerrainTile>
            LoadTerrainTiles()
        {
            var result =
                new Dictionary<int, FieldTerrainTile>();
            for (int tileNumber = 1;
                 tileNumber <= 64;
                 tileNumber++)
            {
                string path =
                    TerrainTileRoot +
                    "/FieldTile_" +
                    tileNumber.ToString("00") +
                    ".asset";
                FieldTerrainTile tile =
                    AssetDatabase.LoadAssetAtPath<FieldTerrainTile>(
                        path);
                if (tile != null)
                {
                    result.Add(tileNumber, tile);
                }
            }

            return result;
        }

        private static Dictionary<string, Tile> LoadPropTiles()
        {
            var result = new Dictionary<string, Tile>(
                StringComparer.Ordinal);
            for (int groupIndex = 0;
                 groupIndex < ExpectedObjectGroups.Length;
                 groupIndex++)
            {
                string groupName = ExpectedObjectGroups[groupIndex];
                string generatedGroupPath =
                    PropTileRoot +
                    "/" +
                    SanitizeAssetName(groupName);
                string[] paths = AssetDatabase.FindAssets(
                        "t:Tile",
                        new[] { generatedGroupPath })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .OrderBy(
                        path => path,
                        NaturalPathComparer.Instance)
                    .ToArray();
                for (int i = 0; i < paths.Length; i++)
                {
                    string baseName =
                        Path.GetFileNameWithoutExtension(paths[i]);
                    result.Add(
                        groupName + "/" + baseName,
                        AssetDatabase.LoadAssetAtPath<Tile>(paths[i]));
                }
            }

            return result;
        }

        private static Dictionary<string, FieldAnimatedTile>
            LoadAnimatedTiles()
        {
            var result =
                new Dictionary<string, FieldAnimatedTile>(
                    StringComparer.Ordinal);
            string[] paths = AssetDatabase.FindAssets(
                    "t:FieldAnimatedTile",
                    new[] { AnimatedTileRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            for (int i = 0; i < paths.Length; i++)
            {
                string key =
                    Path.GetFileNameWithoutExtension(paths[i]);
                result.Add(
                    key,
                    AssetDatabase.LoadAssetAtPath<FieldAnimatedTile>(
                        paths[i]));
            }

            return result;
        }

        private static void SetTile(
            Tilemap tilemap,
            Dictionary<string, Tile> tiles,
            TilePlacement placement)
        {
            if (!tiles.TryGetValue(placement.Key, out Tile tile))
            {
                throw new InvalidOperationException(
                    "Missing generated prop tile: " +
                    placement.Key);
            }

            tilemap.SetTile(
                new Vector3Int(
                    placement.X,
                    placement.Y,
                    0),
                tile);
        }

        private static RunMapSource LoadRunMapSource()
        {
            string json = File.ReadAllText(LogicContentPath);
            RunMapRoot root =
                JsonUtility.FromJson<RunMapRoot>(json);
            if (root == null || root.run == null)
            {
                throw new InvalidOperationException(
                    "phase1-content.json has no run map data.");
            }

            RunMapDto dto = root.run;
            ValidateParallelArrays(
                dto.pathPointXMilli,
                dto.pathPointYMilli,
                "path points");
            ValidateParallelArrays(
                dto.buildSpotXMilli,
                dto.buildSpotYMilli,
                "build spots");
            if (dto.buildSpotUnlockCosts == null ||
                dto.buildSpotUnlockCosts.Length !=
                dto.buildSpotXMilli.Length)
            {
                throw new InvalidOperationException(
                    "buildSpotUnlockCosts must match build spots.");
            }

            return new RunMapSource(
                ToWorldPositions(
                    dto.pathPointXMilli,
                    dto.pathPointYMilli),
                ToWorldPositions(
                    dto.buildSpotXMilli,
                    dto.buildSpotYMilli),
                (int[])dto.buildSpotUnlockCosts.Clone());
        }

        private static Vector2[] ToWorldPositions(
            int[] xMilli,
            int[] yMilli)
        {
            var result = new Vector2[xMilli.Length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new Vector2(
                    xMilli[i] / 1000f,
                    yMilli[i] / 1000f);
            }

            return result;
        }

        private static void ValidateParallelArrays(
            int[] x,
            int[] y,
            string label)
        {
            if (x == null ||
                y == null ||
                x.Length != y.Length ||
                x.Length == 0)
            {
                throw new InvalidOperationException(
                    "Invalid " + label + " in run content.");
            }
        }

        private static void EnsureStageInBuildSettings()
        {
            EditorBuildSettingsScene[] existing =
                EditorBuildSettings.scenes;
            bool alreadyIncluded = existing.Any(
                scene =>
                    string.Equals(
                        scene.path,
                        StageOneScenePath,
                        StringComparison.Ordinal));
            if (alreadyIncluded)
            {
                return;
            }

            var updated = new EditorBuildSettingsScene[
                existing.Length + 1];
            Array.Copy(existing, updated, existing.Length);
            updated[existing.Length] =
                new EditorBuildSettingsScene(
                    StageOneScenePath,
                    true);
            EditorBuildSettings.scenes = updated;
        }

        private static T LoadOrCreateAsset<T>(string path)
            where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static Sprite[] LoadSprites(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<Sprite>()
                .OrderBy(
                    sprite => sprite.name,
                    NaturalPathComparer.Instance)
                .ToArray();
        }

        private static Rect GetAtlasRect(int tileNumber)
        {
            int column =
                (tileNumber - 1) %
                FieldTilesetMaskUtility.AtlasColumns;
            int topRow =
                (tileNumber - 1) /
                FieldTilesetMaskUtility.AtlasColumns;
            int bottomRow =
                FieldTilesetMaskUtility.AtlasRows -
                topRow -
                1;
            return new Rect(
                column * FieldTilesetMaskUtility.TileSize,
                bottomRow * FieldTilesetMaskUtility.TileSize,
                FieldTilesetMaskUtility.TileSize,
                FieldTilesetMaskUtility.TileSize);
        }

        private static string GetTerrainSpriteName(int tileNumber)
        {
            return "FieldTile_" + tileNumber.ToString("00");
        }

        private static int ParseTrailingNumber(string value)
        {
            int underscore = value.LastIndexOf('_');
            if (underscore < 0 ||
                !int.TryParse(
                    value.Substring(underscore + 1),
                    out int number) ||
                number < 1 ||
                number > 64)
            {
                throw new InvalidOperationException(
                    "Unable to parse Fields tile number from " +
                    value +
                    ".");
            }

            return number;
        }

        private static bool IsGroundDecal(string path)
        {
            return path.Contains("/1 Shadow/") ||
                   path.Contains("/5 Grass/") ||
                   path.Contains("/6 Flower/") ||
                   path.Contains("/7 Decor/Dirt");
        }

        private static float DistanceToSegment(
            Vector2 point,
            Vector2 from,
            Vector2 to)
        {
            Vector2 segment = to - from;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
            {
                return Vector2.Distance(point, from);
            }

            float t = Mathf.Clamp01(
                Vector2.Dot(point - from, segment) /
                lengthSquared);
            return Vector2.Distance(
                point,
                from + segment * t);
        }

        private static string SanitizeAssetName(string value)
        {
            string sanitized = value.Replace(' ', '_');
            foreach (char invalid in
                     Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(invalid, '_');
            }

            return sanitized;
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        private static void ValidatePngSize(
            string path,
            int width,
            int height)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Required Fields source image is missing.",
                    path);
            }

            byte[] bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2);
            try
            {
                if (!texture.LoadImage(bytes, false) ||
                    texture.width != width ||
                    texture.height != height)
                {
                    throw new InvalidOperationException(
                        path +
                        " must be " +
                        width +
                        "x" +
                        height +
                        ".");
                }
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static void EnsureFolder(string path)
        {
            string normalized = path.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            string parent =
                NormalizePath(Path.GetDirectoryName(normalized));
            string name = Path.GetFileName(normalized);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, name);
        }

        [Serializable]
        private sealed class RunMapRoot
        {
            public RunMapDto run;
        }

        [Serializable]
        private sealed class RunMapDto
        {
            public int[] buildSpotXMilli;
            public int[] buildSpotYMilli;
            public int[] buildSpotUnlockCosts;
            public int[] pathPointXMilli;
            public int[] pathPointYMilli;
        }

        private readonly struct RunMapSource
        {
            public RunMapSource(
                Vector2[] pathPoints,
                Vector2[] buildSpots,
                int[] buildSpotUnlockCosts)
            {
                PathPoints = pathPoints;
                BuildSpots = buildSpots;
                BuildSpotUnlockCosts = buildSpotUnlockCosts;
            }

            public Vector2[] PathPoints { get; }
            public Vector2[] BuildSpots { get; }
            public int[] BuildSpotUnlockCosts { get; }
        }

        private readonly struct AnimatedTileDefinition
        {
            public AnimatedTileDefinition(
                string key,
                string frameBank,
                bool flipX)
            {
                Key = key;
                FrameBank = frameBank;
                FlipX = flipX;
            }

            public string Key { get; }
            public string FrameBank { get; }
            public bool FlipX { get; }
        }

        private readonly struct TilePlacement
        {
            public TilePlacement(string key, int x, int y)
            {
                Key = key;
                X = x;
                Y = y;
            }

            public string Key { get; }
            public int X { get; }
            public int Y { get; }
        }

        private readonly struct AnimatedTilePlacement
        {
            public AnimatedTilePlacement(
                string key,
                int x,
                int y)
            {
                Key = key;
                X = x;
                Y = y;
            }

            public string Key { get; }
            public int X { get; }
            public int Y { get; }
        }

        private sealed class NaturalPathComparer :
            IComparer<string>
        {
            public static readonly NaturalPathComparer Instance =
                new NaturalPathComparer();

            public int Compare(string left, string right)
            {
                if (ReferenceEquals(left, right))
                {
                    return 0;
                }

                if (left == null)
                {
                    return -1;
                }

                if (right == null)
                {
                    return 1;
                }

                int leftIndex = 0;
                int rightIndex = 0;
                while (leftIndex < left.Length &&
                       rightIndex < right.Length)
                {
                    char leftChar = left[leftIndex];
                    char rightChar = right[rightIndex];
                    if (char.IsDigit(leftChar) &&
                        char.IsDigit(rightChar))
                    {
                        long leftNumber = 0;
                        long rightNumber = 0;
                        while (leftIndex < left.Length &&
                               char.IsDigit(left[leftIndex]))
                        {
                            leftNumber =
                                leftNumber * 10 +
                                left[leftIndex] -
                                '0';
                            leftIndex++;
                        }

                        while (rightIndex < right.Length &&
                               char.IsDigit(right[rightIndex]))
                        {
                            rightNumber =
                                rightNumber * 10 +
                                right[rightIndex] -
                                '0';
                            rightIndex++;
                        }

                        int numberComparison =
                            leftNumber.CompareTo(rightNumber);
                        if (numberComparison != 0)
                        {
                            return numberComparison;
                        }

                        continue;
                    }

                    int characterComparison =
                        char.ToUpperInvariant(leftChar)
                            .CompareTo(
                                char.ToUpperInvariant(rightChar));
                    if (characterComparison != 0)
                    {
                        return characterComparison;
                    }

                    leftIndex++;
                    rightIndex++;
                }

                return left.Length.CompareTo(right.Length);
            }
        }
    }
}
#endif
