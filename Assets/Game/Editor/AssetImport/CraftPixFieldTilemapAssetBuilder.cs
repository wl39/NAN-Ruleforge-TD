#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuleforgeTD.Battle;
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
        private const string WebGLStagingRootPath =
            "Builds/WebGL/.Stage01-staging";
        private const string WebGLStagingBuildPath =
            WebGLStagingRootPath + "/Stage01";
        private const string WebGLPreviousBuildPath =
            "Builds/WebGL/.Stage01-previous";

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
        private const float GroundCoverPathClearance = 1.8f;
        private const float GroundCoverBuildSiteClearance = 2.1f;
        private const float SmallPropPathClearance = 1.9f;
        private const float SmallPropBuildSiteClearance = 2.1f;
        private const float BushPathClearance = 2.15f;
        private const float BushBuildSiteClearance = 2.3f;
        private const float TreePathClearance = 2.9f;
        private const float TreeBuildSiteClearance = 4.1f;
        private const float TentPathClearance = 2.4f;
        private const float TentBuildSiteClearance = 2.8f;
        private const float MarkerPathClearance = 1.9f;
        private const float MeadowDetailPathClearance = 1.9f;
        private const float MeadowDetailBuildSiteClearance = 2.1f;
        private const float FlowerMinimumSpacing = 0.2f;
        private const float FlowerPatchRadius = 0.72f;
        private const int TerrainSortingOrder = -3000;
        private const int GroundDecalSortingOrder = -2500;
        private const int DecorationSortingBase = -1000;
        private const int MinimumDecorationInstanceCount = 130;
        private const int MinimumBiomeClusterCount = 11;
        private const int MinimumWildflowerCount = 37;
        private const int ExpectedMeadowStoneCount = 3;
        private const float PixelWorldSize = 1f / PixelsPerUnit;

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
            if (!EditorSceneManager
                    .SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

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

            if (Directory.Exists(WebGLStagingRootPath))
            {
                Directory.Delete(WebGLStagingRootPath, true);
            }

            Directory.CreateDirectory(WebGLStagingBuildPath);
            PlayerSettings.WebGL.compressionFormat =
                WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.template =
                "PROJECT:RuleforgeFullscreen";

            var options = new BuildPlayerOptions
            {
                scenes = new[] { StageOneScenePath },
                locationPathName = WebGLStagingBuildPath,
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

            PublishWebGLBuild();
            Debug.Log(
                "RULEFORGE_FIELDS_WEBGL_BUILD_OK path=" +
                WebGLBuildPath +
                " size=" +
                summary.totalSize +
                " duration=" +
                summary.totalTime);
        }

        private static void PublishWebGLBuild()
        {
            if (!Directory.Exists(WebGLStagingBuildPath))
            {
                throw new BuildFailedException(
                    "Stage 01 WebGL staging build is missing: " +
                    WebGLStagingBuildPath);
            }

            if (Directory.Exists(WebGLPreviousBuildPath))
            {
                Directory.Delete(WebGLPreviousBuildPath, true);
            }

            bool previousBuildMoved = false;
            try
            {
                if (Directory.Exists(WebGLBuildPath))
                {
                    Directory.Move(
                        WebGLBuildPath,
                        WebGLPreviousBuildPath);
                    previousBuildMoved = true;
                }

                Directory.Move(
                    WebGLStagingBuildPath,
                    WebGLBuildPath);
                if (Directory.Exists(WebGLStagingRootPath))
                {
                    Directory.Delete(
                        WebGLStagingRootPath,
                        true);
                }

                if (previousBuildMoved &&
                    Directory.Exists(WebGLPreviousBuildPath))
                {
                    Directory.Delete(
                        WebGLPreviousBuildPath,
                        true);
                }
            }
            catch
            {
                if (!Directory.Exists(WebGLBuildPath) &&
                    previousBuildMoved &&
                    Directory.Exists(WebGLPreviousBuildPath))
                {
                    Directory.Move(
                        WebGLPreviousBuildPath,
                        WebGLBuildPath);
                }

                throw;
            }
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
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            bool usesCenteredPivot =
                Mathf.Approximately(pivot.x, 0.5f) &&
                Mathf.Approximately(pivot.y, 0.5f);
            settings.spriteAlignment = (int)(
                usesCenteredPivot
                    ? SpriteAlignment.Center
                    : SpriteAlignment.Custom);
            settings.spritePivot = pivot;
            importer.SetTextureSettings(settings);
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
                root.transform.localScale =
                    Vector3.one *
                    TowerBuildSiteView.AuthoredVisualScale;
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
                StageOneGameplaySceneInstaller
                    .InstallFromCommandLine();
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
                TerrainSortingOrder,
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
                GroundDecalSortingOrder,
                TilemapRenderer.Mode.Individual,
                new Vector3(0.5f, 0.5f, 0f));

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
            BiomeDefinition[] biomes = GetStageOneBiomes();
            MeadowDefinition[] meadows = GetStageOneMeadows();
            PaintBiomeGroundCover(
                decals,
                propTiles,
                run,
                biomes);
            Transform decorationRoot =
                CreateStageDecorations(
                    stageRoot.transform,
                    propTiles,
                    animatedTiles,
                    run,
                    biomes,
                    meadows);

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
                decorationRoot,
                navigationMask,
                path,
                sites);

            CreateCamera(stageRoot.transform);
            StageOneGameplaySceneInstaller.EnsureInstalled(
                scene,
                stageMap);
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

        private static BiomeDefinition[] GetStageOneBiomes()
        {
            return new[]
            {
                new BiomeDefinition(
                    "forest_west",
                    "Dense Forest",
                    new Vector2(0.5f, 12f),
                    new Vector2(4.2f, 3.8f),
                    1103,
                    11,
                    16,
                    5,
                    7,
                    0.72f,
                    190f,
                    350f),
                new BiomeDefinition(
                    "forest_north",
                    "Dense Forest",
                    new Vector2(6.6f, 14.1f),
                    new Vector2(4.6f, 2.3f),
                    2207,
                    10,
                    14,
                    4,
                    6,
                    0.68f,
                    205f,
                    340f),
                new BiomeDefinition(
                    "forest_ridge",
                    "Forest Edge",
                    new Vector2(12.2f, 14.8f),
                    new Vector2(3.6f, 1.8f),
                    3319,
                    6,
                    9,
                    4,
                    5,
                    0.62f,
                    205f,
                    335f),
                new BiomeDefinition(
                    "forest_east",
                    "Woodland",
                    new Vector2(25f, 4.9f),
                    new Vector2(2.7f, 3.6f),
                    4421,
                    7,
                    12,
                    5,
                    5,
                    0.65f,
                    105f,
                    255f),
                new BiomeDefinition(
                    "camp_northeast",
                    "Ranger Camp",
                    new Vector2(25.1f, 15f),
                    new Vector2(2.5f, 1.7f),
                    5527,
                    0,
                    0,
                    0,
                    0,
                    0.5f,
                    0f,
                    0f),
                new BiomeDefinition(
                    "camp_southwest",
                    "Abandoned Camp",
                    new Vector2(0f, -2.85f),
                    new Vector2(2.7f, 1.05f),
                    6637,
                    0,
                    0,
                    0,
                    0,
                    0.42f,
                    0f,
                    0f),
                new BiomeDefinition(
                    "scrub_south",
                    "Rocky Scrub",
                    new Vector2(20.8f, -1.5f),
                    new Vector2(7f, 1.9f),
                    7753,
                    0,
                    3,
                    18,
                    5,
                    0.3f,
                    0f,
                    0f)
            };
        }

        private static MeadowDefinition[] GetStageOneMeadows()
        {
            return new[]
            {
                new MeadowDefinition(
                    "meadow_west",
                    new Vector2(2.25f, 5.9f),
                    new Vector2(4.5f, 2.7f),
                    9109,
                    3,
                    15,
                    5,
                    1,
                    1,
                    2,
                    4),
                new MeadowDefinition(
                    "meadow_central",
                    new Vector2(11.7f, 10f),
                    new Vector2(3f, 1.8f),
                    10111,
                    2,
                    10,
                    3,
                    1,
                    3,
                    4,
                    5),
                new MeadowDefinition(
                    "meadow_east",
                    new Vector2(20.1f, 4.7f),
                    new Vector2(3.1f, 2.8f),
                    11213,
                    2,
                    12,
                    4,
                    1,
                    1,
                    5,
                    6)
            };
        }

        private static void PaintBiomeGroundCover(
            Tilemap decals,
            Dictionary<string, Tile> tiles,
            RunMapSource run,
            BiomeDefinition[] biomes)
        {
            for (int y = MapMinY; y <= MapMaxY; y++)
            {
                for (int x = MapMinX; x <= MapMaxX; x++)
                {
                    var point = new Vector2(
                        x + 0.5f,
                        y + 0.5f);
                    if (!HasWorldClearance(
                            point,
                            run,
                            GroundCoverPathClearance,
                            GroundCoverBuildSiteClearance))
                    {
                        continue;
                    }

                    int selectedIndex = -1;
                    float selectedInfluence = 0f;
                    for (int biomeIndex = 0;
                         biomeIndex < biomes.Length;
                         biomeIndex++)
                    {
                        float influence =
                            CalculateBiomeInfluence(
                                point,
                                biomes[biomeIndex]);
                        if (influence <= selectedInfluence)
                        {
                            continue;
                        }

                        selectedIndex = biomeIndex;
                        selectedInfluence = influence;
                    }

                    if (selectedIndex < 0)
                    {
                        continue;
                    }

                    BiomeDefinition biome = biomes[selectedIndex];
                    float density =
                        biome.GroundDensity *
                        Mathf.Lerp(0.3f, 1f, selectedInfluence);
                    if (Hash01(x, y, biome.Seed, 17) >= density)
                    {
                        continue;
                    }

                    string key = SelectGroundCoverKey(
                        biome.Profile,
                        x,
                        y,
                        biome.Seed);
                    SetGroundTile(decals, tiles, key, x, y);
                }
            }

            decals.CompressBounds();
        }

        private static Transform CreateStageDecorations(
            Transform parent,
            Dictionary<string, Tile> propTiles,
            Dictionary<string, FieldAnimatedTile> animatedTiles,
            RunMapSource run,
            BiomeDefinition[] biomes,
            MeadowDefinition[] meadows)
        {
            var rootObject = new GameObject("Decorative Biomes");
            rootObject.transform.SetParent(parent);
            int sequence = 0;

            for (int i = 0; i < biomes.Length; i++)
            {
                BiomeDefinition biome = biomes[i];
                Transform cluster = CreateDecorationCluster(
                    rootObject.transform,
                    biome);
                if (biome.Profile == "Dense Forest" ||
                    biome.Profile == "Forest Edge" ||
                    biome.Profile == "Woodland")
                {
                    CreateForestDecorations(
                        cluster,
                        biome,
                        propTiles,
                        run,
                        ref sequence);
                }
                else if (biome.Profile == "Rocky Scrub")
                {
                    CreateRockyScrubDecorations(
                        cluster,
                        biome,
                        propTiles,
                        run,
                        ref sequence);
                }
                else if (biome.Profile == "Ranger Camp" ||
                         biome.Profile == "Abandoned Camp")
                {
                    CreateCampDecorations(
                        cluster,
                        biome,
                        propTiles,
                        animatedTiles,
                        run,
                        ref sequence);
                }
                else
                {
                    throw new InvalidOperationException(
                        "Unsupported Stage 01 biome profile: " +
                        biome.Profile);
                }
            }

            for (int i = 0; i < meadows.Length; i++)
            {
                MeadowDefinition meadow = meadows[i];
                Transform cluster = CreateDecorationCluster(
                    rootObject.transform,
                    meadow.Id,
                    "Wildflower Meadow",
                    meadow.Center,
                    meadow.Radius,
                    meadow.Seed);
                CreateFlowerMeadowDecorations(
                    cluster,
                    meadow,
                    biomes,
                    propTiles,
                    run,
                    ref sequence);
            }

            CreateRoadsideDecorations(
                rootObject.transform,
                propTiles,
                animatedTiles,
                run,
                ref sequence);
            return rootObject.transform;
        }

        private static Transform CreateDecorationCluster(
            Transform parent,
            BiomeDefinition biome)
        {
            return CreateDecorationCluster(
                parent,
                biome.Id,
                biome.Profile,
                biome.Center,
                biome.Radius,
                biome.Seed);
        }

        private static Transform CreateDecorationCluster(
            Transform parent,
            string id,
            string profile,
            Vector2 center,
            Vector2 radius,
            int seed)
        {
            var clusterObject = new GameObject(id);
            clusterObject.transform.SetParent(parent);
            clusterObject.transform.position = center;
            FieldDecorationCluster cluster =
                clusterObject.AddComponent<FieldDecorationCluster>();
            cluster.ConfigureAuthoring(
                id,
                profile,
                radius,
                seed);
            return clusterObject.transform;
        }

        private static void CreateForestDecorations(
            Transform cluster,
            BiomeDefinition biome,
            Dictionary<string, Tile> tiles,
            RunMapSource run,
            ref int sequence)
        {
            var random = new DeterministicRandom(
                unchecked((uint)biome.Seed));
            List<Vector2> treePoints = GenerateBiomePoints(
                biome,
                biome.TreeCount,
                1.15f,
                TreePathClearance,
                TreeBuildSiteClearance,
                1.4f,
                run,
                ref random);
            for (int i = 0; i < treePoints.Count; i++)
            {
                CreateStaticDecoration(
                    cluster,
                    biome.Id,
                    "7 Decor/Tree1",
                    treePoints[i],
                    random.NextBool(),
                    "1 Shadow/6",
                    new Vector2(0f, -2f * PixelWorldSize),
                    false,
                    TreePathClearance,
                    TreeBuildSiteClearance,
                    tiles,
                    run,
                    ref sequence);
            }

            List<Vector2> bushPoints = GenerateBiomePoints(
                biome,
                biome.BushCount,
                0.52f,
                BushPathClearance,
                BushBuildSiteClearance,
                0.35f,
                run,
                ref random);
            for (int i = 0; i < bushPoints.Count; i++)
            {
                int variant = 1 + (int)(random.NextUInt() % 6u);
                CreateStaticDecoration(
                    cluster,
                    biome.Id,
                    "9 Bush/" + variant,
                    bushPoints[i],
                    random.NextBool(),
                    variant <= 2
                        ? "1 Shadow/3"
                        : "1 Shadow/4",
                    new Vector2(0f, -PixelWorldSize),
                    false,
                    BushPathClearance,
                    BushBuildSiteClearance,
                    tiles,
                    run,
                    ref sequence);
            }

            string[] accentKeys =
            {
                "7 Decor/Tree2",
                "7 Decor/Log1",
                "7 Decor/Log2",
                "4 Stone/2",
                "4 Stone/6",
                "4 Stone/11"
            };
            List<Vector2> accentPoints = GenerateBiomePoints(
                biome,
                biome.AccentCount,
                0.65f,
                SmallPropPathClearance,
                SmallPropBuildSiteClearance,
                0.25f,
                run,
                ref random);
            for (int i = 0; i < accentPoints.Count; i++)
            {
                string key = accentKeys[
                    (int)(random.NextUInt() %
                          (uint)accentKeys.Length)];
                bool isStump = key == "7 Decor/Tree2";
                CreateStaticDecoration(
                    cluster,
                    biome.Id,
                    key,
                    accentPoints[i],
                    random.NextBool(),
                    isStump ? "1 Shadow/3" : null,
                    isStump
                        ? new Vector2(0f, -PixelWorldSize)
                        : Vector2.zero,
                    false,
                    isStump
                        ? BushPathClearance
                        : SmallPropPathClearance,
                    isStump
                        ? BushBuildSiteClearance
                        : SmallPropBuildSiteClearance,
                    tiles,
                    run,
                    ref sequence);
            }

            CreateFenceContour(
                cluster,
                biome,
                tiles,
                run,
                ref random,
                ref sequence);
        }

        private static void CreateFenceContour(
            Transform cluster,
            BiomeDefinition biome,
            Dictionary<string, Tile> tiles,
            RunMapSource run,
            ref DeterministicRandom random,
            ref int sequence)
        {
            string[] fenceKeys =
            {
                "2 Fence/1",
                "2 Fence/2",
                "2 Fence/3",
                "2 Fence/4",
                "2 Fence/8",
                "2 Fence/10"
            };
            for (int i = 0; i < biome.FenceCount; i++)
            {
                float t = biome.FenceCount <= 1
                    ? 0.5f
                    : i / (float)(biome.FenceCount - 1);
                float angle = Mathf.Lerp(
                        biome.FenceStartDegrees,
                        biome.FenceEndDegrees,
                        t)
                    * Mathf.Deg2Rad;
                Vector2 position = biome.Center +
                    new Vector2(
                        Mathf.Cos(angle) * biome.Radius.x * 0.88f,
                        Mathf.Sin(angle) * biome.Radius.y * 0.88f);
                position = SnapToPixel(position);
                if (!HasWorldClearance(
                        position,
                        run,
                        SmallPropPathClearance,
                        SmallPropBuildSiteClearance) ||
                    !IsInsideDecorationBounds(position, 0.25f))
                {
                    continue;
                }

                string key = fenceKeys[
                    (i + biome.Seed) % fenceKeys.Length];
                CreateStaticDecoration(
                    cluster,
                    biome.Id,
                    key,
                    position,
                    (i & 1) == 0
                        ? random.NextBool()
                        : !random.NextBool(),
                    null,
                    Vector2.zero,
                    false,
                    SmallPropPathClearance,
                    SmallPropBuildSiteClearance,
                    tiles,
                    run,
                    ref sequence);
            }
        }

        private static void CreateRockyScrubDecorations(
            Transform cluster,
            BiomeDefinition biome,
            Dictionary<string, Tile> tiles,
            RunMapSource run,
            ref int sequence)
        {
            var random = new DeterministicRandom(
                unchecked((uint)biome.Seed));
            Vector2[] subCenters =
            {
                biome.Center + new Vector2(-3.4f, -0.35f),
                biome.Center + new Vector2(0f, -0.2f),
                biome.Center + new Vector2(3.8f, 0.8f)
            };
            string[] rubbleKeys =
            {
                "4 Stone/1",
                "4 Stone/3",
                "4 Stone/5",
                "4 Stone/7",
                "4 Stone/9",
                "4 Stone/12",
                "4 Stone/14",
                "4 Stone/16",
                "7 Decor/Log2",
                "7 Decor/Log3",
                "7 Decor/Box2",
                "2 Fence/5",
                "2 Fence/6"
            };
            int perCluster = Mathf.Max(
                1,
                biome.AccentCount / subCenters.Length);
            for (int clusterIndex = 0;
                 clusterIndex < subCenters.Length;
                 clusterIndex++)
            {
                var localBiome = new BiomeDefinition(
                    biome.Id,
                    biome.Profile,
                    subCenters[clusterIndex],
                    new Vector2(1.8f, 0.85f),
                    biome.Seed + clusterIndex * 97,
                    0,
                    0,
                    perCluster,
                    0,
                    biome.GroundDensity,
                    0f,
                    0f);
                List<Vector2> points = GenerateBiomePoints(
                    localBiome,
                    perCluster,
                    0.58f,
                    SmallPropPathClearance,
                    SmallPropBuildSiteClearance,
                    0.2f,
                    run,
                    ref random);
                for (int i = 0; i < points.Count; i++)
                {
                    string key = rubbleKeys[
                        (int)(random.NextUInt() %
                              (uint)rubbleKeys.Length)];
                    CreateStaticDecoration(
                        cluster,
                        biome.Id,
                        key,
                        points[i],
                        random.NextBool(),
                        null,
                        Vector2.zero,
                        false,
                        SmallPropPathClearance,
                        SmallPropBuildSiteClearance,
                        tiles,
                        run,
                        ref sequence);
                }
            }

            List<Vector2> bushes = GenerateBiomePoints(
                biome,
                biome.BushCount,
                0.8f,
                BushPathClearance,
                BushBuildSiteClearance,
                0.35f,
                run,
                ref random);
            for (int i = 0; i < bushes.Count; i++)
            {
                int variant = 4 + i % 3;
                CreateStaticDecoration(
                    cluster,
                    biome.Id,
                    "9 Bush/" + variant,
                    bushes[i],
                    (i & 1) == 0,
                    "1 Shadow/4",
                    new Vector2(0f, -PixelWorldSize),
                    false,
                    BushPathClearance,
                    BushBuildSiteClearance,
                    tiles,
                    run,
                    ref sequence);
            }
        }

        private static void CreateFlowerMeadowDecorations(
            Transform cluster,
            MeadowDefinition meadow,
            BiomeDefinition[] structuralBiomes,
            Dictionary<string, Tile> tiles,
            RunMapSource run,
            ref int sequence)
        {
            var patchRandom = new DeterministicRandom(
                unchecked((uint)meadow.Seed) ^ 0xA341316Cu);
            List<Vector2> patchCenters =
                GenerateMeadowDetailPoints(
                    meadow,
                    meadow.PatchCount,
                    1.45f,
                    0.08f,
                    0.76f,
                    MeadowDetailPathClearance + 0.45f,
                    MeadowDetailBuildSiteClearance + 0.45f,
                    FlowerPatchRadius + 0.15f,
                    structuralBiomes,
                    run,
                    null,
                    0f,
                    ref patchRandom);

            var flowerPositionRandom = new DeterministicRandom(
                unchecked((uint)meadow.Seed) ^ 0xC8013EA4u);
            var flowerVariantRandom = new DeterministicRandom(
                unchecked((uint)meadow.Seed) ^ 0xAD90777Du);
            var flowerFlipRandom = new DeterministicRandom(
                unchecked((uint)meadow.Seed) ^ 0x7E95761Eu);
            var flowerPositions =
                new List<Vector2>(meadow.FlowerCount);
            int remainingFlowers = meadow.FlowerCount;
            for (int patchIndex = 0;
                 patchIndex < patchCenters.Count;
                 patchIndex++)
            {
                int remainingPatches =
                    patchCenters.Count - patchIndex;
                int quota =
                    remainingFlowers / remainingPatches;
                int placedInPatch = 0;
                int attempts = 0;
                while (placedInPatch < quota &&
                       attempts < quota * 240)
                {
                    attempts++;
                    Vector2 candidate;
                    if (placedInPatch == 0)
                    {
                        candidate = patchCenters[patchIndex];
                    }
                    else
                    {
                        float angle =
                            flowerPositionRandom.Next01() *
                            Mathf.PI *
                            2f;
                        float distance =
                            Mathf.Sqrt(
                                flowerPositionRandom.Next01()) *
                            FlowerPatchRadius;
                        candidate = patchCenters[patchIndex] +
                            new Vector2(
                                Mathf.Cos(angle) * distance,
                                Mathf.Sin(angle) *
                                distance *
                                0.68f);
                    }

                    candidate = SnapToPixel(candidate);
                    if (!IsValidMeadowDetailPoint(
                            candidate,
                            meadow,
                            structuralBiomes,
                            MeadowDetailPathClearance,
                            MeadowDetailBuildSiteClearance,
                            0.12f,
                            run) ||
                        HasPointWithin(
                            flowerPositions,
                            candidate,
                            FlowerMinimumSpacing))
                    {
                        continue;
                    }

                    flowerPositions.Add(candidate);
                    placedInPatch++;
                }

                if (placedInPatch != quota)
                {
                    throw new InvalidOperationException(
                        meadow.Id +
                        " could only place " +
                        placedInPatch +
                        " of " +
                        quota +
                        " requested flowers in patch " +
                        patchIndex +
                        ".");
                }

                remainingFlowers -= quota;
            }

            for (int i = 0; i < flowerPositions.Count; i++)
            {
                uint selector =
                    flowerVariantRandom.NextUInt() % 100u;
                int variant = selector < 52u
                    ? meadow.PrimaryFlower
                    : selector < 78u
                        ? meadow.SecondaryFlower
                        : meadow.TertiaryFlower;
                CreateStaticDecoration(
                    cluster,
                    meadow.Id,
                    "6 Flower/" + variant,
                    flowerPositions[i],
                    flowerFlipRandom.NextBool(),
                    null,
                    Vector2.zero,
                    false,
                    MeadowDetailPathClearance,
                    MeadowDetailBuildSiteClearance,
                    tiles,
                    run,
                    ref sequence);
            }

            var grassRandom = new DeterministicRandom(
                unchecked((uint)meadow.Seed) ^ 0x4CF5AD43u);
            List<Vector2> grassPositions =
                GenerateMeadowDetailPoints(
                    meadow,
                    meadow.GrassCount,
                    0.72f,
                    0.08f,
                    0.94f,
                    MeadowDetailPathClearance,
                    MeadowDetailBuildSiteClearance,
                    0.08f,
                    structuralBiomes,
                    run,
                    flowerPositions,
                    0.12f,
                    ref grassRandom);
            for (int i = 0; i < grassPositions.Count; i++)
            {
                int variant =
                    4 + (int)(grassRandom.NextUInt() % 3u);
                CreateStaticDecoration(
                    cluster,
                    meadow.Id,
                    "5 Grass/" + variant,
                    grassPositions[i],
                    grassRandom.NextBool(),
                    null,
                    Vector2.zero,
                    false,
                    MeadowDetailPathClearance,
                    MeadowDetailBuildSiteClearance,
                    tiles,
                    run,
                    ref sequence);
            }

            var stoneRandom = new DeterministicRandom(
                unchecked((uint)meadow.Seed) ^ 0x9E3779B9u);
            List<Vector2> stonePositions =
                GenerateMeadowDetailPoints(
                    meadow,
                    meadow.StoneCount,
                    2.2f,
                    0.68f,
                    0.96f,
                    SmallPropPathClearance,
                    SmallPropBuildSiteClearance,
                    0.12f,
                    structuralBiomes,
                    run,
                    flowerPositions,
                    0.3f,
                    ref stoneRandom);
            int[] stoneVariants = { 1, 3, 4, 6 };
            for (int i = 0; i < stonePositions.Count; i++)
            {
                int variant = stoneVariants[
                    (int)(stoneRandom.NextUInt() %
                          (uint)stoneVariants.Length)];
                CreateStaticDecoration(
                    cluster,
                    meadow.Id,
                    "4 Stone/" + variant,
                    stonePositions[i],
                    stoneRandom.NextBool(),
                    null,
                    Vector2.zero,
                    false,
                    SmallPropPathClearance,
                    SmallPropBuildSiteClearance,
                    tiles,
                    run,
                    ref sequence);
            }
        }

        private static List<Vector2> GenerateMeadowDetailPoints(
            MeadowDefinition meadow,
            int count,
            float minimumSpacing,
            float minimumNormalizedRadius,
            float maximumNormalizedRadius,
            float pathClearance,
            float buildSiteClearance,
            float structuralPadding,
            BiomeDefinition[] structuralBiomes,
            RunMapSource run,
            IReadOnlyList<Vector2> exclusions,
            float exclusionSpacing,
            ref DeterministicRandom random)
        {
            var result = new List<Vector2>(count);
            int attempts = 0;
            int maximumAttempts = Mathf.Max(500, count * 350);
            while (result.Count < count &&
                   attempts < maximumAttempts)
            {
                attempts++;
                float angle = random.Next01() * Mathf.PI * 2f;
                float minimumRadiusSquared =
                    minimumNormalizedRadius *
                    minimumNormalizedRadius;
                float maximumRadiusSquared =
                    maximumNormalizedRadius *
                    maximumNormalizedRadius;
                float normalizedRadius = Mathf.Sqrt(
                    Mathf.Lerp(
                        minimumRadiusSquared,
                        maximumRadiusSquared,
                        random.Next01()));
                Vector2 candidate = meadow.Center +
                    new Vector2(
                        Mathf.Cos(angle) *
                        meadow.Radius.x *
                        normalizedRadius,
                        Mathf.Sin(angle) *
                        meadow.Radius.y *
                        normalizedRadius);
                candidate = SnapToPixel(candidate);
                if (!IsValidMeadowDetailPoint(
                        candidate,
                        meadow,
                        structuralBiomes,
                        pathClearance,
                        buildSiteClearance,
                        structuralPadding,
                        run) ||
                    HasPointWithin(
                        result,
                        candidate,
                        minimumSpacing) ||
                    HasPointWithin(
                        exclusions,
                        candidate,
                        exclusionSpacing))
                {
                    continue;
                }

                result.Add(candidate);
            }

            if (result.Count != count)
            {
                throw new InvalidOperationException(
                    meadow.Id +
                    " could only place " +
                    result.Count +
                    " of " +
                    count +
                    " requested meadow details.");
            }

            return result;
        }

        private static bool IsValidMeadowDetailPoint(
            Vector2 point,
            MeadowDefinition meadow,
            BiomeDefinition[] structuralBiomes,
            float pathClearance,
            float buildSiteClearance,
            float structuralPadding,
            RunMapSource run)
        {
            return IsInsideDecorationBounds(point, 0.35f) &&
                IsInsideEllipse(
                    point,
                    meadow.Center,
                    meadow.Radius,
                    0.98f) &&
                IsOutsideStructuralBiomes(
                    point,
                    structuralBiomes,
                    structuralPadding) &&
                HasWorldClearance(
                    point,
                    run,
                    pathClearance,
                    buildSiteClearance);
        }

        private static bool IsOutsideStructuralBiomes(
            Vector2 point,
            BiomeDefinition[] biomes,
            float padding)
        {
            for (int i = 0; i < biomes.Length; i++)
            {
                Vector2 radius = biomes[i].Radius +
                    Vector2.one * padding;
                if (IsInsideEllipse(
                        point,
                        biomes[i].Center,
                        radius,
                        1f))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsInsideEllipse(
            Vector2 point,
            Vector2 center,
            Vector2 radius,
            float normalizedLimit)
        {
            if (radius.x <= Mathf.Epsilon ||
                radius.y <= Mathf.Epsilon)
            {
                return false;
            }

            Vector2 offset = point - center;
            float normalizedSquared =
                offset.x * offset.x / (radius.x * radius.x) +
                offset.y * offset.y / (radius.y * radius.y);
            return normalizedSquared <=
                normalizedLimit * normalizedLimit;
        }

        private static bool HasPointWithin(
            IReadOnlyList<Vector2> points,
            Vector2 candidate,
            float distance)
        {
            if (points == null || distance <= 0f)
            {
                return false;
            }

            float distanceSquared = distance * distance;
            for (int i = 0; i < points.Count; i++)
            {
                if ((points[i] - candidate).sqrMagnitude <
                    distanceSquared)
                {
                    return true;
                }
            }

            return false;
        }

        private static void CreateCampDecorations(
            Transform cluster,
            BiomeDefinition biome,
            Dictionary<string, Tile> propTiles,
            Dictionary<string, FieldAnimatedTile> animatedTiles,
            RunMapSource run,
            ref int sequence)
        {
            DecorationPlacement[] placements =
                biome.Id == "camp_northeast"
                    ? new[]
                    {
                        new DecorationPlacement(
                            "8 Camp/1",
                            new Vector2(-0.65f, 0.75f),
                            false,
                            "1 Shadow/5"),
                        new DecorationPlacement(
                            "8 Camp/3",
                            new Vector2(0.7f, 0.68f),
                            true,
                            "1 Shadow/5"),
                        new DecorationPlacement(
                            "8 Camp/2",
                            new Vector2(1.55f, -0.05f),
                            false,
                            "1 Shadow/3"),
                        new DecorationPlacement(
                            "7 Decor/Box1",
                            new Vector2(-1.45f, -0.15f),
                            false,
                            null),
                        new DecorationPlacement(
                            "7 Decor/Log3",
                            new Vector2(1.15f, -0.65f),
                            true,
                            null),
                        new DecorationPlacement(
                            "9 Bush/2",
                            new Vector2(-1.25f, 1.05f),
                            true,
                            "1 Shadow/3"),
                        new DecorationPlacement(
                            "2 Fence/7",
                            new Vector2(1.85f, 0.7f),
                            false,
                            null),
                        new DecorationPlacement(
                            "2 Fence/8",
                            new Vector2(1.9f, 0.05f),
                            true,
                            null),
                        new DecorationPlacement(
                            "2 Fence/3",
                            new Vector2(-1.55f, 1.1f),
                            true,
                            null)
                    }
                    : new[]
                    {
                        new DecorationPlacement(
                            "8 Camp/2",
                            new Vector2(-1.15f, 0.3f),
                            true,
                            "1 Shadow/3"),
                        new DecorationPlacement(
                            "8 Camp/4",
                            new Vector2(1f, 0.15f),
                            false,
                            "1 Shadow/5"),
                        new DecorationPlacement(
                            "7 Decor/Box2",
                            new Vector2(-1.8f, -0.2f),
                            true,
                            null),
                        new DecorationPlacement(
                            "7 Decor/Log1",
                            new Vector2(1.85f, -0.25f),
                            false,
                            null),
                        new DecorationPlacement(
                            "2 Fence/1",
                            new Vector2(-0.7f, 0.9f),
                            false,
                            null),
                        new DecorationPlacement(
                            "2 Fence/3",
                            new Vector2(0.05f, 0.9f),
                            true,
                            null),
                        new DecorationPlacement(
                            "2 Fence/4",
                            new Vector2(0.8f, 0.85f),
                            false,
                            null)
                    };

            for (int i = 0; i < placements.Length; i++)
            {
                DecorationPlacement placement = placements[i];
                bool isTent = placement.Key.StartsWith(
                    "8 Camp/",
                    StringComparison.Ordinal);
                bool isBush = placement.Key.StartsWith(
                    "9 Bush/",
                    StringComparison.Ordinal);
                CreateStaticDecoration(
                    cluster,
                    biome.Id,
                    placement.Key,
                    biome.Center + placement.Offset,
                    placement.FlipX,
                    placement.GroundBaseKey,
                    placement.GroundBaseKey == null
                        ? Vector2.zero
                        : new Vector2(
                            0f,
                            isBush
                                ? -PixelWorldSize
                                : -2f * PixelWorldSize),
                    false,
                    isTent
                        ? TentPathClearance
                        : isBush
                            ? BushPathClearance
                            : SmallPropPathClearance,
                    isTent
                        ? TentBuildSiteClearance
                        : isBush
                            ? BushBuildSiteClearance
                            : SmallPropBuildSiteClearance,
                    propTiles,
                    run,
                    ref sequence);
            }

            string animationKey =
                biome.Id == "camp_northeast"
                    ? "Campfire_Lit"
                    : "Campfire_Unlit";
            Vector2 fireOffset =
                biome.Id == "camp_northeast"
                    ? new Vector2(0f, -0.65f)
                    : new Vector2(0f, -0.35f);
            CreateAnimatedDecoration(
                cluster,
                biome.Id,
                animationKey,
                biome.Center + fireOffset,
                false,
                false,
                SmallPropPathClearance,
                SmallPropBuildSiteClearance,
                animatedTiles,
                run,
                ref sequence);
        }

        private static void CreateRoadsideDecorations(
            Transform parent,
            Dictionary<string, Tile> propTiles,
            Dictionary<string, FieldAnimatedTile> animatedTiles,
            RunMapSource run,
            ref int sequence)
        {
            var biome = new BiomeDefinition(
                "roadside_verges",
                "Roadside Verges",
                new Vector2(12f, 6f),
                new Vector2(14f, 9f),
                8867,
                0,
                0,
                0,
                0,
                0f,
                0f,
                0f);
            Transform cluster = CreateDecorationCluster(parent, biome);
            var placements = new[]
            {
                new RoadMarkerPlacement(
                    0,
                    0.2f,
                    -1f,
                    "3 Pointer/1",
                    false,
                    false),
                new RoadMarkerPlacement(
                    0,
                    0.7f,
                    1f,
                    "Flag_DownRight",
                    true,
                    false),
                new RoadMarkerPlacement(
                    1,
                    0.45f,
                    1f,
                    "3 Pointer/2",
                    false,
                    true),
                new RoadMarkerPlacement(
                    2,
                    0.35f,
                    1f,
                    "Flag_Down",
                    true,
                    false),
                new RoadMarkerPlacement(
                    3,
                    0.8f,
                    1f,
                    "3 Pointer/4",
                    false,
                    true),
                new RoadMarkerPlacement(
                    4,
                    0.85f,
                    1f,
                    "Flag_UpRight",
                    true,
                    false)
            };
            float vergeDistance = RoadHalfWidth + 0.65f;
            for (int i = 0; i < placements.Length; i++)
            {
                RoadMarkerPlacement placement = placements[i];
                Vector2 from =
                    run.PathPoints[placement.SegmentIndex];
                Vector2 to =
                    run.PathPoints[placement.SegmentIndex + 1];
                Vector2 direction = (to - from).normalized;
                Vector2 normal =
                    new Vector2(-direction.y, direction.x);
                Vector2 position =
                    Vector2.Lerp(from, to, placement.SegmentT) +
                    normal * placement.Side * vergeDistance;
                if (placement.Animated)
                {
                    CreateAnimatedDecoration(
                        cluster,
                        biome.Id,
                        placement.Key,
                        position,
                        placement.FlipX,
                        true,
                        MarkerPathClearance,
                        SmallPropBuildSiteClearance,
                        animatedTiles,
                        run,
                        ref sequence);
                }
                else
                {
                    CreateStaticDecoration(
                        cluster,
                        biome.Id,
                        placement.Key,
                        position,
                        placement.FlipX,
                        null,
                        Vector2.zero,
                        true,
                        MarkerPathClearance,
                        SmallPropBuildSiteClearance,
                        propTiles,
                        run,
                        ref sequence);
                }
            }
        }

        private static List<Vector2> GenerateBiomePoints(
            BiomeDefinition biome,
            int count,
            float minimumSpacing,
            float pathClearance,
            float buildSiteClearance,
            float boundsPadding,
            RunMapSource run,
            ref DeterministicRandom random)
        {
            var result = new List<Vector2>(count);
            int attempts = 0;
            int maximumAttempts = Mathf.Max(400, count * 300);
            while (result.Count < count &&
                   attempts < maximumAttempts)
            {
                attempts++;
                float angle = random.Next01() * Mathf.PI * 2f;
                float distance = Mathf.Sqrt(random.Next01()) * 0.92f;
                Vector2 candidate = biome.Center +
                    new Vector2(
                        Mathf.Cos(angle) *
                        biome.Radius.x *
                        distance,
                        Mathf.Sin(angle) *
                        biome.Radius.y *
                        distance);
                candidate = SnapToPixel(candidate);
                if (!IsInsideDecorationBounds(
                        candidate,
                        boundsPadding) ||
                    !HasWorldClearance(
                        candidate,
                        run,
                        pathClearance,
                        buildSiteClearance))
                {
                    continue;
                }

                bool overlapsBase = false;
                for (int i = 0; i < result.Count; i++)
                {
                    if (Vector2.Distance(
                            candidate,
                            result[i]) <
                        minimumSpacing)
                    {
                        overlapsBase = true;
                        break;
                    }
                }

                if (!overlapsBase)
                {
                    result.Add(candidate);
                }
            }

            if (result.Count != count)
            {
                throw new InvalidOperationException(
                    biome.Id +
                    " could only place " +
                    result.Count +
                    " of " +
                    count +
                    " requested decorations.");
            }

            return result;
        }

        private static FieldDecorationView CreateStaticDecoration(
            Transform cluster,
            string clusterId,
            string key,
            Vector2 worldPosition,
            bool flipX,
            string groundBaseKey,
            Vector2 groundBaseOffset,
            bool roadsideMarker,
            float pathClearance,
            float buildSiteClearance,
            Dictionary<string, Tile> tiles,
            RunMapSource run,
            ref int sequence)
        {
            if (!tiles.TryGetValue(key, out Tile tile) ||
                tile == null ||
                tile.sprite == null)
            {
                throw new InvalidOperationException(
                    "Missing generated prop sprite: " + key);
            }

            Vector2 position = SnapToPixel(worldPosition);
            ValidateWorldClearance(
                key,
                position,
                run,
                pathClearance,
                buildSiteClearance);

            var propObject = new GameObject(
                SanitizeAssetName(key) +
                "_" +
                sequence.ToString("000"));
            propObject.transform.SetParent(cluster);
            propObject.transform.position = position;
            SpriteRenderer body =
                propObject.AddComponent<SpriteRenderer>();
            body.sprite = tile.sprite;
            body.flipX = flipX;
            body.spriteSortPoint = SpriteSortPoint.Pivot;
            body.sortingOrder =
                GetDecorationSortingOrder(position, sequence);

            SpriteRenderer groundBase = null;
            if (!string.IsNullOrEmpty(groundBaseKey))
            {
                if (!tiles.TryGetValue(
                        groundBaseKey,
                        out Tile baseTile) ||
                    baseTile == null ||
                    baseTile.sprite == null)
                {
                    throw new InvalidOperationException(
                        "Missing ground base sprite: " +
                        groundBaseKey);
                }

                var baseObject = new GameObject("Ground Base");
                baseObject.transform.SetParent(propObject.transform);
                baseObject.transform.localPosition =
                    SnapToPixel(groundBaseOffset);
                groundBase =
                    baseObject.AddComponent<SpriteRenderer>();
                groundBase.sprite = baseTile.sprite;
                groundBase.spriteSortPoint = SpriteSortPoint.Pivot;
                groundBase.sortingOrder = body.sortingOrder - 1;
            }

            FieldDecorationView view =
                propObject.AddComponent<FieldDecorationView>();
            view.ConfigureAuthoring(
                key,
                clusterId,
                roadsideMarker,
                body,
                groundBase);
            ValidateBuildSiteVisualBounds(view, run);
            sequence++;
            return view;
        }

        private static FieldDecorationView CreateAnimatedDecoration(
            Transform cluster,
            string clusterId,
            string key,
            Vector2 worldPosition,
            bool flipX,
            bool roadsideMarker,
            float pathClearance,
            float buildSiteClearance,
            Dictionary<string, FieldAnimatedTile> tiles,
            RunMapSource run,
            ref int sequence)
        {
            if (!tiles.TryGetValue(
                    key,
                    out FieldAnimatedTile tile) ||
                tile == null ||
                tile.FrameCount == 0)
            {
                throw new InvalidOperationException(
                    "Missing animated prop: " + key);
            }

            Vector2 position = SnapToPixel(worldPosition);
            ValidateWorldClearance(
                key,
                position,
                run,
                pathClearance,
                buildSiteClearance);
            var propObject = new GameObject(
                SanitizeAssetName(key) +
                "_" +
                sequence.ToString("000"));
            propObject.transform.SetParent(cluster);
            propObject.transform.position = position;
            SpriteRenderer body =
                propObject.AddComponent<SpriteRenderer>();
            body.sprite = tile.GetFrame(0);
            body.flipX = tile.FlipX ^ flipX;
            body.spriteSortPoint = SpriteSortPoint.Pivot;
            body.sortingOrder =
                GetDecorationSortingOrder(position, sequence);

            var frames = new Sprite[tile.FrameCount];
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] = tile.GetFrame(i);
            }

            FieldSpriteAnimator animator =
                propObject.AddComponent<FieldSpriteAnimator>();
            animator.ConfigureAuthoring(
                body,
                frames,
                AnimationFrameDuration / tile.AnimationSpeed);
            FieldDecorationView view =
                propObject.AddComponent<FieldDecorationView>();
            view.ConfigureAuthoring(
                key,
                clusterId,
                roadsideMarker,
                body,
                null);
            ValidateBuildSiteVisualBounds(view, run);
            sequence++;
            return view;
        }

        private static void ValidateBuildSiteVisualBounds(
            FieldDecorationView view,
            RunMapSource run)
        {
            Bounds bounds = view.Body.bounds;
            if (view.HasGroundBase)
            {
                bounds.Encapsulate(view.GroundBase.bounds);
            }

            for (int i = 0; i < run.BuildSpots.Length; i++)
            {
                var buildBounds = new Bounds(
                    run.BuildSpots[i],
                    new Vector3(2.2f, 2.2f, 1f));
                if (bounds.Intersects(buildBounds))
                {
                    throw new InvalidOperationException(
                        view.AssetKey +
                        " visual bounds overlap build site " +
                        i +
                        ".");
                }
            }
        }

        private static float CalculateBiomeInfluence(
            Vector2 point,
            BiomeDefinition biome)
        {
            if (biome.GroundDensity <= 0f ||
                biome.Radius.x <= Mathf.Epsilon ||
                biome.Radius.y <= Mathf.Epsilon)
            {
                return 0f;
            }

            Vector2 offset = point - biome.Center;
            float normalized = Mathf.Sqrt(
                offset.x * offset.x /
                (biome.Radius.x * biome.Radius.x) +
                offset.y * offset.y /
                (biome.Radius.y * biome.Radius.y));
            return normalized >= 1f
                ? 0f
                : 1f - normalized;
        }

        private static string SelectGroundCoverKey(
            string profile,
            int x,
            int y,
            int seed)
        {
            int selector = Mathf.FloorToInt(
                Hash01(x, y, seed, 31) * 100f);
            int variant = 1 +
                Mathf.FloorToInt(
                    Hash01(x, y, seed, 47) * 6f);
            if (profile == "Dense Forest" ||
                profile == "Forest Edge" ||
                profile == "Woodland")
            {
                if (selector < 78)
                {
                    return "5 Grass/" + variant;
                }

                int flower = 1 +
                    Mathf.FloorToInt(
                        Hash01(x, y, seed, 59) * 12f);
                return "6 Flower/" + flower;
            }

            if (profile == "Ranger Camp" ||
                profile == "Abandoned Camp")
            {
                return selector < 72
                    ? "7 Decor/Dirt" + variant
                    : "5 Grass/" + variant;
            }

            if (selector < 62)
            {
                return "7 Decor/Dirt" + variant;
            }

            return selector < 92
                ? "5 Grass/" + variant
                : "6 Flower/" + (1 + variant);
        }

        private static float Hash01(
            int x,
            int y,
            int seed,
            int salt)
        {
            uint value = unchecked(
                (uint)(x * 73856093) ^
                (uint)(y * 19349663) ^
                (uint)(seed * 83492791) ^
                (uint)salt * 2654435761u);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777216f;
        }

        private static void SetGroundTile(
            Tilemap tilemap,
            Dictionary<string, Tile> tiles,
            string key,
            int x,
            int y)
        {
            if (!tiles.TryGetValue(key, out Tile tile))
            {
                throw new InvalidOperationException(
                    "Missing generated ground tile: " + key);
            }

            tilemap.SetTile(new Vector3Int(x, y, 0), tile);
        }

        private static int GetDecorationSortingOrder(
            Vector2 position,
            int sequence)
        {
            return DecorationSortingBase -
                Mathf.RoundToInt(position.y * 64f) +
                (sequence & 1);
        }

        private static Vector2 SnapToPixel(Vector2 value)
        {
            return new Vector2(
                Mathf.Round(value.x / PixelWorldSize) *
                PixelWorldSize,
                Mathf.Round(value.y / PixelWorldSize) *
                PixelWorldSize);
        }

        private static bool IsInsideDecorationBounds(
            Vector2 point,
            float padding)
        {
            return point.x >= MapMinX + padding &&
                point.x <= MapMaxX - padding &&
                point.y >= MapMinY + padding &&
                point.y <= MapMaxY - padding;
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
                    stage.DecorationRoot == null ||
                    stage.NavigationMask == null ||
                    stage.Path == null)
                {
                    throw new InvalidOperationException(
                        "Stage 01 map hierarchy is incomplete.");
                }

                if (stage.BuildSiteCount != 8)
                {
                    throw new InvalidOperationException(
                        "Stage 01 must expose eight build sites.");
                }

                StageOneBattleController controller = scene
                    .GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<
                            StageOneBattleController>(true))
                    .SingleOrDefault();
                if (controller == null ||
                    controller.StageMap != stage ||
                    controller.PresentationCatalog == null ||
                    controller.Seed !=
                    StageOneGameplaySceneInstaller.AuthoredSeed)
                {
                    throw new InvalidOperationException(
                        "Stage 01 gameplay controller is not configured.");
                }

                StageOnePresentationCatalog catalog =
                    controller.PresentationCatalog;
                if (catalog.TowerBindingCount != 7 ||
                    catalog.EnemyBindingCount != 7 ||
                    catalog.ProjectileDirectionCount != 5 ||
                    catalog.DefaultCardProgramCount != 3 ||
                    catalog.UiFont == null)
                {
                    throw new InvalidOperationException(
                        "Stage 01 presentation catalog is incomplete.");
                }

                if (!catalog.TryGetTower(
                        "ballista",
                        out GameObject towerPrefab,
                        out float towerScale) ||
                    towerPrefab == null ||
                    towerScale <= 0f)
                {
                    throw new InvalidOperationException(
                        "Stage 01 ballista presentation is missing.");
                }

                string[] expectedEnemies =
                {
                    "raider",
                    "runner",
                    "armored_knight",
                    "elite_golem",
                    "boss_guardian",
                    "boss_summoner",
                    "boss_time_walker"
                };
                for (int i = 0; i < expectedEnemies.Length; i++)
                {
                    if (!catalog.TryGetEnemy(
                            expectedEnemies[i],
                            out GameObject enemyPrefab,
                            out float enemyScale) ||
                        enemyPrefab == null ||
                        enemyScale <= 0f)
                    {
                        throw new InvalidOperationException(
                            "Stage 01 enemy presentation is missing: " +
                            expectedEnemies[i] +
                            ".");
                    }
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

                ValidateDecorationComposition(stage);
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

        private static void ValidateDecorationComposition(
            FieldStageMap stage)
        {
            FieldDecorationView[] decorations =
                stage.DecorationRoot
                    .GetComponentsInChildren<FieldDecorationView>(
                        true);
            FieldDecorationCluster[] clusters =
                stage.DecorationRoot
                    .GetComponentsInChildren<FieldDecorationCluster>(
                        true);
            FieldSpriteAnimator[] animators =
                stage.DecorationRoot
                    .GetComponentsInChildren<FieldSpriteAnimator>(
                        true);
            if (decorations.Length <
                MinimumDecorationInstanceCount)
            {
                throw new InvalidOperationException(
                    "Stage 01 has too few decoration instances. Expected " +
                    "at least " +
                    MinimumDecorationInstanceCount +
                    " but found " +
                    decorations.Length +
                    ".");
            }

            if (clusters.Length < MinimumBiomeClusterCount)
            {
                throw new InvalidOperationException(
                    "Stage 01 must contain semantic biome clusters.");
            }

            if (animators.Length < 5)
            {
                throw new InvalidOperationException(
                    "Stage 01 must animate camps and roadside flags.");
            }

            for (int i = 0; i < decorations.Length; i++)
            {
                FieldDecorationView decoration = decorations[i];
                if (decoration.Body == null ||
                    decoration.Body.sprite == null ||
                    string.IsNullOrEmpty(decoration.ClusterId))
                {
                    throw new InvalidOperationException(
                        "Decoration instance metadata is incomplete.");
                }

                Vector3 position = decoration.transform.position;
                if (!Mathf.Approximately(
                        position.x / PixelWorldSize,
                        Mathf.Round(position.x / PixelWorldSize)) ||
                    !Mathf.Approximately(
                        position.y / PixelWorldSize,
                        Mathf.Round(position.y / PixelWorldSize)))
                {
                    throw new InvalidOperationException(
                        decoration.AssetKey +
                        " is not snapped to the pixel grid.");
                }

                if (!decoration.HasGroundBase)
                {
                    continue;
                }

                if (decoration.GroundBase.transform.parent !=
                    decoration.transform ||
                    decoration.GroundBase.transform.localPosition
                        .magnitude > 0.125f ||
                    decoration.GroundBase.sortingOrder !=
                    decoration.Body.sortingOrder - 1)
                {
                    throw new InvalidOperationException(
                        decoration.AssetKey +
                        " has a detached ground base.");
                }
            }

            ValidateMirroredDecorationFamily(
                decorations,
                "9 Bush/");
            ValidateMirroredDecorationFamily(
                decorations,
                "2 Fence/");
            ValidateMirroredDecorationFamily(
                decorations,
                "7 Decor/Tree1");
            ValidateMirroredDecorationFamily(
                decorations,
                "8 Camp/");
            ValidateForestOverlap(clusters);
            ValidateWildflowerMeadows(
                decorations,
                clusters);
        }

        private static void ValidateMirroredDecorationFamily(
            FieldDecorationView[] decorations,
            string keyPrefix)
        {
            FieldDecorationView[] family = decorations
                .Where(decoration =>
                    decoration.AssetKey.StartsWith(
                        keyPrefix,
                        StringComparison.Ordinal))
                .ToArray();
            if (!family.Any(decoration => decoration.FlipX) ||
                !family.Any(decoration => !decoration.FlipX))
            {
                throw new InvalidOperationException(
                    keyPrefix +
                    " must include mirrored and unmirrored instances.");
            }
        }

        private static void ValidateForestOverlap(
            FieldDecorationCluster[] clusters)
        {
            FieldDecorationCluster[] forests = clusters
                .Where(cluster =>
                    cluster.Profile == "Dense Forest" ||
                    cluster.Profile == "Forest Edge" ||
                    cluster.Profile == "Woodland")
                .ToArray();
            for (int forestIndex = 0;
                 forestIndex < forests.Length;
                 forestIndex++)
            {
                FieldDecorationView[] trees = forests[forestIndex]
                    .GetComponentsInChildren<FieldDecorationView>(true)
                    .Where(decoration =>
                        decoration.AssetKey ==
                        "7 Decor/Tree1")
                    .ToArray();
                bool hasOverlap = false;
                for (int left = 0;
                     left < trees.Length && !hasOverlap;
                     left++)
                {
                    for (int right = left + 1;
                         right < trees.Length;
                         right++)
                    {
                        if (trees[left].Body.bounds.Intersects(
                                trees[right].Body.bounds))
                        {
                            hasOverlap = true;
                            break;
                        }
                    }
                }

                if (!hasOverlap)
                {
                    throw new InvalidOperationException(
                        forests[forestIndex].ClusterId +
                        " must contain overlapping tree canopies.");
                }
            }
        }

        private static void ValidateWildflowerMeadows(
            FieldDecorationView[] decorations,
            FieldDecorationCluster[] clusters)
        {
            FieldDecorationCluster[] meadows = clusters
                .Where(cluster =>
                    cluster.Profile == "Wildflower Meadow")
                .ToArray();
            if (meadows.Length != 3)
            {
                throw new InvalidOperationException(
                    "Stage 01 must contain exactly three wildflower meadows.");
            }

            int totalFlowers = 0;
            int totalGrass = 0;
            int totalStones = 0;
            for (int meadowIndex = 0;
                 meadowIndex < meadows.Length;
                 meadowIndex++)
            {
                FieldDecorationCluster meadow =
                    meadows[meadowIndex];
                FieldDecorationView[] details = meadow
                    .GetComponentsInChildren<FieldDecorationView>(
                        true);
                FieldDecorationView[] flowers = details
                    .Where(detail =>
                        detail.AssetKey.StartsWith(
                            "6 Flower/",
                            StringComparison.Ordinal))
                    .ToArray();
                FieldDecorationView[] stones = details
                    .Where(detail =>
                        detail.AssetKey.StartsWith(
                            "4 Stone/",
                            StringComparison.Ordinal))
                    .ToArray();
                totalFlowers += flowers.Length;
                totalGrass += details.Count(detail =>
                    detail.AssetKey.StartsWith(
                        "5 Grass/",
                        StringComparison.Ordinal));
                totalStones += stones.Length;

                if (flowers.Length < 10 || stones.Length > 1)
                {
                    throw new InvalidOperationException(
                        meadow.ClusterId +
                        " must remain flower-dominant with sparse stones.");
                }

                int flowerVariants = flowers
                    .Select(flower => flower.AssetKey)
                    .Distinct()
                    .Count();
                if (flowerVariants > 3)
                {
                    throw new InvalidOperationException(
                        meadow.ClusterId +
                        " uses too many flower colors.");
                }

                for (int detailIndex = 0;
                     detailIndex < details.Length;
                     detailIndex++)
                {
                    if (!IsInsideEllipse(
                            details[detailIndex].transform.position,
                            meadow.Center,
                            meadow.Radius,
                            1.01f))
                    {
                        throw new InvalidOperationException(
                            details[detailIndex].AssetKey +
                            " escaped " +
                            meadow.ClusterId +
                            ".");
                    }
                }

                for (int flowerIndex = 0;
                     flowerIndex < flowers.Length;
                     flowerIndex++)
                {
                    float nearest = float.MaxValue;
                    for (int other = 0;
                         other < flowers.Length;
                         other++)
                    {
                        if (flowerIndex == other)
                        {
                            continue;
                        }

                        nearest = Mathf.Min(
                            nearest,
                            Vector2.Distance(
                                flowers[flowerIndex]
                                    .transform.position,
                                flowers[other]
                                    .transform.position));
                    }

                    if (nearest > 1f)
                    {
                        throw new InvalidOperationException(
                            meadow.ClusterId +
                            " contains an isolated flower.");
                    }
                }
            }

            if (totalFlowers < MinimumWildflowerCount ||
                totalGrass < 12 ||
                totalStones != ExpectedMeadowStoneCount ||
                totalFlowers < totalStones * 8)
            {
                throw new InvalidOperationException(
                    "Wildflower meadow composition is outside its density contract.");
            }
        }

        private static void ValidateWorldClearance(
            string key,
            Vector2 point,
            RunMapSource run,
            float pathClearance,
            float buildSiteClearance)
        {
            if (HasWorldClearance(
                    point,
                    run,
                    pathClearance,
                    buildSiteClearance))
            {
                return;
            }

            throw new InvalidOperationException(
                key +
                " at (" +
                point.x +
                ", " +
                point.y +
                ") overlaps the road or a tower build site.");
        }

        private static bool HasWorldClearance(
            Vector2 point,
            RunMapSource run,
            float pathClearance,
            float buildSiteClearance)
        {
            for (int segment = 0;
                 segment < run.PathPoints.Length - 1;
                 segment++)
            {
                if (DistanceToSegment(
                        point,
                        run.PathPoints[segment],
                        run.PathPoints[segment + 1]) <
                    pathClearance)
                {
                    return false;
                }
            }

            for (int index = 0;
                 index < run.BuildSpots.Length;
                 index++)
            {
                if (Vector2.Distance(
                        point,
                        run.BuildSpots[index]) <
                    buildSiteClearance)
                {
                    return false;
                }
            }

            return true;
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

        private readonly struct BiomeDefinition
        {
            public BiomeDefinition(
                string id,
                string profile,
                Vector2 center,
                Vector2 radius,
                int seed,
                int treeCount,
                int bushCount,
                int accentCount,
                int fenceCount,
                float groundDensity,
                float fenceStartDegrees,
                float fenceEndDegrees)
            {
                Id = id;
                Profile = profile;
                Center = center;
                Radius = radius;
                Seed = seed;
                TreeCount = treeCount;
                BushCount = bushCount;
                AccentCount = accentCount;
                FenceCount = fenceCount;
                GroundDensity = groundDensity;
                FenceStartDegrees = fenceStartDegrees;
                FenceEndDegrees = fenceEndDegrees;
            }

            public string Id { get; }
            public string Profile { get; }
            public Vector2 Center { get; }
            public Vector2 Radius { get; }
            public int Seed { get; }
            public int TreeCount { get; }
            public int BushCount { get; }
            public int AccentCount { get; }
            public int FenceCount { get; }
            public float GroundDensity { get; }
            public float FenceStartDegrees { get; }
            public float FenceEndDegrees { get; }
        }

        private readonly struct MeadowDefinition
        {
            public MeadowDefinition(
                string id,
                Vector2 center,
                Vector2 radius,
                int seed,
                int patchCount,
                int flowerCount,
                int grassCount,
                int stoneCount,
                int primaryFlower,
                int secondaryFlower,
                int tertiaryFlower)
            {
                Id = id;
                Center = center;
                Radius = radius;
                Seed = seed;
                PatchCount = patchCount;
                FlowerCount = flowerCount;
                GrassCount = grassCount;
                StoneCount = stoneCount;
                PrimaryFlower = primaryFlower;
                SecondaryFlower = secondaryFlower;
                TertiaryFlower = tertiaryFlower;
            }

            public string Id { get; }
            public Vector2 Center { get; }
            public Vector2 Radius { get; }
            public int Seed { get; }
            public int PatchCount { get; }
            public int FlowerCount { get; }
            public int GrassCount { get; }
            public int StoneCount { get; }
            public int PrimaryFlower { get; }
            public int SecondaryFlower { get; }
            public int TertiaryFlower { get; }
        }

        private readonly struct DecorationPlacement
        {
            public DecorationPlacement(
                string key,
                Vector2 offset,
                bool flipX,
                string groundBaseKey)
            {
                Key = key;
                Offset = offset;
                FlipX = flipX;
                GroundBaseKey = groundBaseKey;
            }

            public string Key { get; }
            public Vector2 Offset { get; }
            public bool FlipX { get; }
            public string GroundBaseKey { get; }
        }

        private readonly struct RoadMarkerPlacement
        {
            public RoadMarkerPlacement(
                int segmentIndex,
                float segmentT,
                float side,
                string key,
                bool animated,
                bool flipX)
            {
                SegmentIndex = segmentIndex;
                SegmentT = segmentT;
                Side = side;
                Key = key;
                Animated = animated;
                FlipX = flipX;
            }

            public int SegmentIndex { get; }
            public float SegmentT { get; }
            public float Side { get; }
            public string Key { get; }
            public bool Animated { get; }
            public bool FlipX { get; }
        }

        private struct DeterministicRandom
        {
            private uint state;

            public DeterministicRandom(uint seed)
            {
                state = seed == 0u ? 0x6D2B79F5u : seed;
            }

            public uint NextUInt()
            {
                uint value = state;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                state = value;
                return value;
            }

            public float Next01()
            {
                return (NextUInt() & 0x00FFFFFFu) / 16777216f;
            }

            public bool NextBool()
            {
                return (NextUInt() & 1u) != 0u;
            }
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
