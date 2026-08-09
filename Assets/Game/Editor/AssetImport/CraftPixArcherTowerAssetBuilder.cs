#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuleforgeTD.Enemies;
using RuleforgeTD.Enemies.Testing;
using RuleforgeTD.Rendering;
using RuleforgeTD.Towers.Archer;
using RuleforgeTD.Towers.Testing;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleforgeTD.Editor.AssetImport
{
    public static class CraftPixArcherTowerAssetBuilder
    {
        public const string TestScenePath =
            "Assets/Game/Scenes/Test/ArcherTowerAnimationTest.unity";
        public const string WebGLBuildPath =
            "Builds/WebGL/ArcherTowerShowcase";

        private const string RawRoot =
            "Assets/ThirdParty/CraftPix/Raw/Towers/Archer";
        private const string IdleRoot = RawRoot + "/Idle";
        private const string UpgradeRoot = RawRoot + "/Upgrade";
        private const string UnitsRoot = RawRoot + "/Units";
        private const string ArrowRoot = UnitsRoot + "/Arrow";
        private const string DataRoot = "Assets/Game/Data/Towers/Archer";
        private const string TowerAnimationRoot = DataRoot + "/Animations/Tower";
        private const string UnitAnimationRoot = DataRoot + "/Animations/Units";
        private const string PrefabRoot = "Assets/Game/Prefabs/Towers/Archer";
        private const string EnemyPrefabRoot = "Assets/Game/Prefabs/Enemies";
        private const string ShowcasePixelPath = DataRoot + "/ArcherShowcasePixel.asset";
        private const string LogicContentPath =
            "Assets/Game/Data/Logic/phase1-content.json";

        private const int TowerFrameWidth = 70;
        private const int TowerFrameHeight = 130;
        private const int UnitFrameSize = 48;
        private const float PixelsPerUnit = 48f;
        private const int ExpectedTowerLevels = 7;
        private const int ExpectedUnitSheets = 27;
        private const int ExpectedArrowSprites = 27;
        private const int ExpectedGeneratedClips = 41;
        private const int InitialSplitChildrenPerRoot = 2;
        private const int ShowcaseEnemyMaximumHealth = 1000;
        private const float TowerIdleFrameDuration = 0.12f;
        private const float EnemyRespawnDelay = 1.8f;
        private const int TowerBodySortingOrder = 20;
        private const int ArcherSortingOrder = 21;

        private static readonly int[] ExpectedIdleFrameCounts =
        {
            0,
            1,
            4,
            4,
            6,
            6,
            6,
            6
        };

        private static readonly string[] UnitDirections = { "D", "U", "S" };
        private static readonly string[] CombatEnemyNames =
        {
            "Slime",
            "Bee",
            "Dog",
            "Goblin"
        };
        private static readonly ArcherUnitAnimationBehaviour[] UnitBehaviours =
        {
            ArcherUnitAnimationBehaviour.Idle,
            ArcherUnitAnimationBehaviour.Preattack,
            ArcherUnitAnimationBehaviour.Attack
        };

        [MenuItem("Ruleforge TD/Assets/Rebuild Archer Tower Showcase")]
        public static void BuildFromMenu()
        {
            BuildAll();
        }

        [MenuItem("Ruleforge TD/Assets/Validate Archer Tower Showcase")]
        public static void ValidateFromMenu()
        {
            ValidateGeneratedAssets();
        }

        [MenuItem("Ruleforge TD/Scenes/Open Archer Tower Showcase")]
        public static void OpenShowcaseScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) == null)
            {
                BuildAll();
            }

            EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Single);
        }

        public static void BuildFromCommandLine()
        {
            BuildAll();
        }

        public static void ValidateFromCommandLine()
        {
            ValidateGeneratedAssets();
        }

        [MenuItem("Ruleforge TD/Build/Build Archer Tower Showcase (WebGL)")]
        public static void BuildWebGLFromCommandLine()
        {
            BuildAll();

            if (Directory.Exists(WebGLBuildPath))
            {
                Directory.Delete(WebGLBuildPath, true);
            }

            Directory.CreateDirectory(WebGLBuildPath);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { TestScenePath },
                locationPathName = WebGLBuildPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    "Archer tower WebGL build failed with result " +
                    summary.result +
                    " and " +
                    summary.totalErrors +
                    " error(s).");
            }

            Debug.Log(
                "RULEFORGE_ARCHER_WEBGL_BUILD_OK path=" +
                WebGLBuildPath +
                " size=" +
                summary.totalSize +
                " duration=" +
                summary.totalTime);
        }

        private static void BuildAll()
        {
            EnsureFolder(TowerAnimationRoot);
            EnsureFolder(UnitAnimationRoot);
            EnsureFolder(PrefabRoot);
            EnsureFolder("Assets/Game/Scenes/Test");

            TowerSheetDescriptor[] idleDescriptors =
                GetTowerDescriptors(IdleRoot, TowerSheetKind.Idle);
            TowerSheetDescriptor[] upgradeDescriptors =
                GetTowerDescriptors(UpgradeRoot, TowerSheetKind.Upgrade);
            UnitSheetDescriptor[] unitDescriptors = GetUnitDescriptors();
            string[] arrowPaths = GetArrowPaths();

            ValidateSourceDescriptors(
                idleDescriptors,
                upgradeDescriptors,
                unitDescriptors,
                arrowPaths);

            for (int i = 0; i < idleDescriptors.Length; i++)
            {
                ConfigureAndSliceTowerTexture(idleDescriptors[i]);
            }

            for (int i = 0; i < upgradeDescriptors.Length; i++)
            {
                ConfigureAndSliceTowerTexture(upgradeDescriptors[i]);
            }

            for (int i = 0; i < unitDescriptors.Length; i++)
            {
                ConfigureAndSliceUnitTexture(unitDescriptors[i]);
            }

            for (int i = 0; i < arrowPaths.Length; i++)
            {
                ConfigureArrowTexture(arrowPaths[i]);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var towerIdleFrames = new Dictionary<int, Sprite[]>();
            var towerUpgradeFrames = new Dictionary<int, Sprite[]>();
            for (int i = 0; i < idleDescriptors.Length; i++)
            {
                TowerSheetDescriptor descriptor = idleDescriptors[i];
                Sprite[] sprites = LoadSprites(descriptor.AssetPath);
                towerIdleFrames.Add(descriptor.Level, sprites);
                CreateTowerIdleClip(descriptor.Level, sprites);
            }

            for (int i = 0; i < upgradeDescriptors.Length; i++)
            {
                TowerSheetDescriptor descriptor = upgradeDescriptors[i];
                Sprite[] sprites = LoadSprites(descriptor.AssetPath);
                towerUpgradeFrames.Add(descriptor.Level, sprites);
                CreateRegularClip(
                    descriptor.ClipPath,
                    descriptor.ClipName,
                    sprites,
                    0.16f,
                    false,
                    true);
            }

            var unitFrames = new Dictionary<string, Sprite[]>(StringComparer.Ordinal);
            for (int i = 0; i < unitDescriptors.Length; i++)
            {
                UnitSheetDescriptor descriptor = unitDescriptors[i];
                Sprite[] sprites = LoadSprites(descriptor.AssetPath);
                unitFrames.Add(descriptor.Key, sprites);
                float frameDuration =
                    descriptor.Behaviour == ArcherUnitAnimationBehaviour.Idle
                        ? 0.18f
                        : descriptor.Behaviour == ArcherUnitAnimationBehaviour.Preattack
                            ? 0.16f
                            : 0.09f;
                CreateRegularClip(
                    descriptor.ClipPath,
                    descriptor.ClipName,
                    sprites,
                    frameDuration,
                    descriptor.Behaviour == ArcherUnitAnimationBehaviour.Idle,
                    true);
            }

            Sprite[][] arrowBanks = LoadArrowBanks(arrowPaths);
            Sprite showcasePixel = CreateShowcasePixel();
            var prefabs = new Dictionary<int, GameObject>();
            for (int level = 1; level <= ExpectedTowerLevels; level++)
            {
                prefabs.Add(
                    level,
                    CreateTowerPrefab(
                        level,
                        towerIdleFrames[level],
                        towerUpgradeFrames[level],
                        unitFrames));
            }

            CreateTestScene(prefabs, arrowBanks, showcasePixel);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateGeneratedAssets();

            Debug.Log(
                "RULEFORGE_ARCHER_BUILD_OK towerSheets=14 unitSheets=27 " +
                "arrows=27 cards=3 cardSubject=enemy roots=4 seedPooled=8 " +
                "seedTargets=12 enemyHealth=1000 splitStop=health<1 " +
                "statusCopy=all scalePerGeneration=0.9 " +
                "pool=dynamic-high-water clips=41 prefabs=7 scene=" +
                TestScenePath);
        }

        private static TowerSheetDescriptor[] GetTowerDescriptors(
            string root,
            TowerSheetKind kind)
        {
            return AssetDatabase.FindAssets("t:Texture2D", new[] { root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsPng)
                .Select(path =>
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (!int.TryParse(name, out int level))
                    {
                        throw new InvalidOperationException(
                            "Tower sheet must use a numeric level filename: " + path);
                    }

                    return new TowerSheetDescriptor(path, level, kind);
                })
                .OrderBy(descriptor => descriptor.Level)
                .ToArray();
        }

        private static UnitSheetDescriptor[] GetUnitDescriptors()
        {
            return AssetDatabase.FindAssets("t:Texture2D", new[] { UnitsRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                    IsPng(path) &&
                    !path.StartsWith(ArrowRoot + "/", StringComparison.Ordinal))
                .Select(ParseUnitDescriptor)
                .OrderBy(descriptor => descriptor.Tier)
                .ThenBy(descriptor => descriptor.Behaviour)
                .ThenBy(descriptor => DirectionOrder(descriptor.Direction))
                .ToArray();
        }

        private static string[] GetArrowPaths()
        {
            return AssetDatabase.FindAssets("t:Texture2D", new[] { ArrowRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsPng)
                .OrderBy(ParseNumericFileName)
                .ToArray();
        }

        private static UnitSheetDescriptor ParseUnitDescriptor(string assetPath)
        {
            var directory = new DirectoryInfo(
                Path.GetDirectoryName(assetPath) ?? string.Empty);
            if (!int.TryParse(directory.Name, out int tier) ||
                tier < 1 ||
                tier > 3)
            {
                throw new InvalidOperationException(
                    "Archer unit folder must be 1, 2, or 3: " + assetPath);
            }

            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            string[] parts = fileName.Split(new[] { '_' }, 2);
            if (parts.Length != 2 ||
                !UnitDirections.Contains(parts[0], StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Archer unit sheet must match <D|U|S>_<Behaviour>: " +
                    assetPath);
            }

            if (!Enum.TryParse(
                    parts[1],
                    true,
                    out ArcherUnitAnimationBehaviour behaviour))
            {
                throw new InvalidOperationException(
                    "Unsupported archer unit behaviour: " + assetPath);
            }

            return new UnitSheetDescriptor(
                assetPath,
                tier,
                parts[0],
                behaviour);
        }

        private static void ValidateSourceDescriptors(
            TowerSheetDescriptor[] idleDescriptors,
            TowerSheetDescriptor[] upgradeDescriptors,
            UnitSheetDescriptor[] unitDescriptors,
            string[] arrowPaths)
        {
            ValidateLevelSequence(idleDescriptors, "Idle");
            ValidateLevelSequence(upgradeDescriptors, "Upgrade");

            if (unitDescriptors.Length != ExpectedUnitSheets)
            {
                throw new InvalidOperationException(
                    "Expected 27 archer unit sheets, found " +
                    unitDescriptors.Length +
                    ".");
            }

            for (int tier = 1; tier <= 3; tier++)
            {
                for (int directionIndex = 0;
                     directionIndex < UnitDirections.Length;
                     directionIndex++)
                {
                    for (int behaviourIndex = 0;
                         behaviourIndex < UnitBehaviours.Length;
                         behaviourIndex++)
                    {
                        string direction = UnitDirections[directionIndex];
                        ArcherUnitAnimationBehaviour behaviour =
                            UnitBehaviours[behaviourIndex];
                        int matches = unitDescriptors.Count(descriptor =>
                            descriptor.Tier == tier &&
                            descriptor.Direction == direction &&
                            descriptor.Behaviour == behaviour);
                        if (matches != 1)
                        {
                            throw new InvalidOperationException(
                                "Archer unit matrix is incomplete: tier=" +
                                tier +
                                " direction=" +
                                direction +
                                " behaviour=" +
                                behaviour +
                                ".");
                        }
                    }
                }
            }

            if (arrowPaths.Length != ExpectedArrowSprites)
            {
                throw new InvalidOperationException(
                    "Expected 27 archer arrow sprites, found " +
                    arrowPaths.Length +
                    ".");
            }

            for (int i = 0; i < arrowPaths.Length; i++)
            {
                int expectedNumber = i + 1;
                int actualNumber = ParseNumericFileName(arrowPaths[i]);
                if (actualNumber != expectedNumber)
                {
                    throw new InvalidOperationException(
                        "Archer arrow sequence is incomplete at " +
                        expectedNumber +
                        ".");
                }
            }
        }

        private static void ValidateLevelSequence(
            TowerSheetDescriptor[] descriptors,
            string label)
        {
            if (descriptors.Length != ExpectedTowerLevels)
            {
                throw new InvalidOperationException(
                    "Expected seven " +
                    label +
                    " tower sheets, found " +
                    descriptors.Length +
                    ".");
            }

            for (int i = 0; i < descriptors.Length; i++)
            {
                if (descriptors[i].Level != i + 1)
                {
                    throw new InvalidOperationException(
                        label + " tower levels must be exactly 1 through 7.");
                }
            }
        }

        private static void ConfigureAndSliceTowerTexture(
            TowerSheetDescriptor descriptor)
        {
            var importer = AssetImporter.GetAtPath(descriptor.AssetPath) as TextureImporter;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(descriptor.AssetPath);
            if (importer == null || texture == null)
            {
                throw new InvalidOperationException(
                    "Could not load tower texture: " + descriptor.AssetPath);
            }

            int expectedFrames = descriptor.Kind == TowerSheetKind.Idle
                ? ExpectedIdleFrameCounts[descriptor.Level]
                : 4;
            if (texture.height != TowerFrameHeight ||
                texture.width != TowerFrameWidth * expectedFrames)
            {
                throw new InvalidOperationException(
                    descriptor.AssetPath +
                    " must be " +
                    expectedFrames +
                    " cells of 70x130, but is " +
                    texture.width +
                    "x" +
                    texture.height +
                    ".");
            }

            ConfigureCommonPixelTexture(importer, SpriteImportMode.Multiple);
            importer.spritePixelsPerUnit = PixelsPerUnit;
            var metadata = new SpriteMetaData[expectedFrames];
            for (int frame = 0; frame < expectedFrames; frame++)
            {
                metadata[frame] = new SpriteMetaData
                {
                    name = descriptor.SpritePrefix + "_" + frame.ToString("00"),
                    rect = new Rect(
                        frame * TowerFrameWidth,
                        0,
                        TowerFrameWidth,
                        TowerFrameHeight),
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

        private static void ConfigureAndSliceUnitTexture(
            UnitSheetDescriptor descriptor)
        {
            var importer = AssetImporter.GetAtPath(descriptor.AssetPath) as TextureImporter;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(descriptor.AssetPath);
            if (importer == null || texture == null)
            {
                throw new InvalidOperationException(
                    "Could not load archer unit texture: " + descriptor.AssetPath);
            }

            int expectedFrames = GetExpectedUnitFrameCount(descriptor.Behaviour);
            if (texture.height != UnitFrameSize ||
                texture.width != UnitFrameSize * expectedFrames)
            {
                throw new InvalidOperationException(
                    descriptor.AssetPath +
                    " must be " +
                    expectedFrames +
                    " cells of 48x48, but is " +
                    texture.width +
                    "x" +
                    texture.height +
                    ".");
            }

            ConfigureCommonPixelTexture(importer, SpriteImportMode.Multiple);
            importer.spritePixelsPerUnit = PixelsPerUnit;
            var metadata = new SpriteMetaData[expectedFrames];
            for (int frame = 0; frame < expectedFrames; frame++)
            {
                metadata[frame] = new SpriteMetaData
                {
                    name = descriptor.SpritePrefix + "_" + frame.ToString("00"),
                    rect = new Rect(
                        frame * UnitFrameSize,
                        0,
                        UnitFrameSize,
                        UnitFrameSize),
                    alignment = (int)SpriteAlignment.Custom,
                    pivot = new Vector2(0.5f, 15f / UnitFrameSize),
                    border = Vector4.zero
                };
            }

#pragma warning disable CS0618
            importer.spritesheet = metadata;
#pragma warning restore CS0618
            importer.SaveAndReimport();
        }

        private static void ConfigureArrowTexture(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    "Could not load archer arrow texture: " + assetPath);
            }

            ConfigureCommonPixelTexture(importer, SpriteImportMode.Single);
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.spritePivot = new Vector2(0.5f, 0.5f);
            importer.SaveAndReimport();
        }

        private static void ConfigureCommonPixelTexture(
            TextureImporter importer,
            SpriteImportMode importMode)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = importMode;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
        }

        private static void CreateTowerIdleClip(int level, Sprite[] sprites)
        {
            string clipName = "ArcherTower_Level" + level.ToString("00") + "_Idle";
            string clipPath = TowerAnimationRoot + "/" + clipName + ".anim";
            CreateRegularClip(
                clipPath,
                clipName,
                sprites,
                TowerIdleFrameDuration,
                true,
                true);
        }

        private static void CreateRegularClip(
            string clipPath,
            string clipName,
            Sprite[] sprites,
            float frameDuration,
            bool loop,
            bool addTerminalFrame)
        {
            DeleteAssetIfPresent(clipPath);
            var frames = new List<ObjectReferenceKeyframe>(sprites.Length + 1);
            for (int i = 0; i < sprites.Length; i++)
            {
                frames.Add(new ObjectReferenceKeyframe
                {
                    time = i * frameDuration,
                    value = sprites[i]
                });
            }

            if (addTerminalFrame && sprites.Length > 0)
            {
                frames.Add(new ObjectReferenceKeyframe
                {
                    time = sprites.Length * frameDuration,
                    value = loop ? sprites[0] : sprites[sprites.Length - 1]
                });
            }

            CreateClipAsset(clipPath, clipName, frames.ToArray(), loop);
        }

        private static void CreateClipAsset(
            string clipPath,
            string clipName,
            ObjectReferenceKeyframe[] frames,
            bool loop)
        {
            var clip = new AnimationClip
            {
                name = clipName,
                frameRate = 60f
            };
            var binding = new EditorCurveBinding
            {
                path = string.Empty,
                type = typeof(SpriteRenderer),
                propertyName = "m_Sprite"
            };
            AnimationUtility.SetObjectReferenceCurve(clip, binding, frames);
            AnimationClipSettings settings =
                AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, clipPath);
        }

        private static Sprite[][] LoadArrowBanks(string[] numericArrowPaths)
        {
            Sprite[] all = numericArrowPaths
                .Select(LoadSingleSprite)
                .ToArray();

            // Chosen presentation default: stronger unit tiers receive a larger
            // arrow and a finer direction bank.
            return new[]
            {
                all.Skip(22).Take(5).ToArray(),
                all.Skip(13).Take(9).ToArray(),
                all.Take(13).ToArray()
            };
        }

        private static GameObject CreateTowerPrefab(
            int level,
            Sprite[] idleFrames,
            Sprite[] upgradeFrames,
            IReadOnlyDictionary<string, Sprite[]> unitFrames)
        {
            string prefabPath =
                PrefabRoot + "/ArcherTower_Level" + level.ToString("00") + ".prefab";
            DeleteAssetIfPresent(prefabPath);

            var root = new GameObject(
                "Archer Tower Level " + level.ToString("00"));
            try
            {
                var bodyObject = new GameObject("Tower Body");
                bodyObject.transform.SetParent(root.transform, false);
                SpriteRenderer bodyRenderer = bodyObject.AddComponent<SpriteRenderer>();
                bodyRenderer.sprite = idleFrames[0];
                WorldSortingLayers.Apply(
                    bodyRenderer,
                    WorldSortingLayers.Tower);
                bodyRenderer.sortingOrder = TowerBodySortingOrder;

                var unitsObject = new GameObject("Archers");
                unitsObject.transform.SetParent(root.transform, false);

                bool hasOpenRoof = ArcherTowerView.LevelHasOpenRoof(level);
                int unitTier = ArcherTowerView.GetDefaultUnitTier(level);
                int archerCount = ArcherTowerView.GetDefaultArcherCount(level);
                DirectionalArcherAnimator[] archers = archerCount > 0
                    ? CreateArchers(
                        level,
                        unitTier,
                        archerCount,
                        unitsObject.transform,
                        unitFrames)
                    : Array.Empty<DirectionalArcherAnimator>();

                ArcherTowerView towerView = root.AddComponent<ArcherTowerView>();
                towerView.Configure(
                    level,
                    unitTier,
                    hasOpenRoof,
                    bodyRenderer,
                    unitsObject,
                    archers,
                    idleFrames,
                    upgradeFrames);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        "Failed to create archer tower prefab: " + prefabPath);
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static DirectionalArcherAnimator[] CreateArchers(
            int level,
            int unitTier,
            int archerCount,
            Transform parent,
            IReadOnlyDictionary<string, Sprite[]> unitFrames)
        {
            float seatHeight = GetArcherSeatHeight(level);
            Vector2[] positions = CreateArcherSeatPositions(
                level,
                archerCount,
                seatHeight);

            var result = new DirectionalArcherAnimator[archerCount];
            for (int i = 0; i < result.Length; i++)
            {
                var archerObject = new GameObject("Archer " + (i + 1));
                archerObject.transform.SetParent(parent, false);
                archerObject.transform.localPosition = positions[i];
                archerObject.transform.localScale = Vector3.one * 0.8f;

                var originObject = new GameObject(
                    "Arrow Origin " + (i + 1));
                originObject.transform.SetParent(parent, false);
                originObject.transform.localPosition =
                    GetArcherProjectileOriginPosition(
                        level,
                        i,
                        positions[i]);

                SpriteRenderer renderer = archerObject.AddComponent<SpriteRenderer>();
                WorldSortingLayers.Apply(
                    renderer,
                    WorldSortingLayers.Tower);
                renderer.sortingOrder = ArcherSortingOrder;
                DirectionalArcherAnimator animator =
                    archerObject.AddComponent<DirectionalArcherAnimator>();
                animator.Configure(
                    renderer,
                    originObject.transform,
                    true,
                    GetArcherAimDirection(positions[i].x),
                    level * 101 + i * 37,
                    GetUnitFrames(
                        unitFrames,
                        unitTier,
                        "D",
                        ArcherUnitAnimationBehaviour.Idle),
                    GetUnitFrames(
                        unitFrames,
                        unitTier,
                        "U",
                        ArcherUnitAnimationBehaviour.Idle),
                    GetUnitFrames(
                        unitFrames,
                        unitTier,
                        "S",
                        ArcherUnitAnimationBehaviour.Idle),
                    GetUnitFrames(
                        unitFrames,
                        unitTier,
                        "D",
                        ArcherUnitAnimationBehaviour.Preattack),
                    GetUnitFrames(
                        unitFrames,
                        unitTier,
                        "U",
                        ArcherUnitAnimationBehaviour.Preattack),
                    GetUnitFrames(
                        unitFrames,
                        unitTier,
                        "S",
                        ArcherUnitAnimationBehaviour.Preattack),
                    GetUnitFrames(
                        unitFrames,
                        unitTier,
                        "D",
                        ArcherUnitAnimationBehaviour.Attack),
                    GetUnitFrames(
                        unitFrames,
                        unitTier,
                        "U",
                        ArcherUnitAnimationBehaviour.Attack),
                    GetUnitFrames(
                        unitFrames,
                        unitTier,
                        "S",
                        ArcherUnitAnimationBehaviour.Attack));
                result[i] = animator;
            }

            return result;
        }

        private static Vector2[] CreateArcherSeatPositions(
            int level,
            int archerCount,
            float seatHeight)
        {
            if (level == 4)
            {
                return new[]
                {
                    new Vector2(-0.2f, 0.4f),
                    new Vector2(0.2f, 0.4f)
                };
            }

            if (level == 7)
            {
                return new[]
                {
                    new Vector2(-0.28f, 0.44f),
                    new Vector2(0f, 0.44f),
                    new Vector2(0.28f, 0.44f)
                };
            }

            switch (archerCount)
            {
                case 1:
                    return new[]
                    {
                        new Vector2(0f, seatHeight + 0.05f)
                    };
                case 2:
                    return new[]
                    {
                        new Vector2(-0.16f, seatHeight + 0.03f),
                        new Vector2(0.16f, seatHeight + 0.03f)
                    };
                case 3:
                    return new[]
                    {
                        new Vector2(-0.23f, seatHeight),
                        new Vector2(0f, seatHeight + 0.06f),
                        new Vector2(0.23f, seatHeight)
                    };
                default:
                    return Array.Empty<Vector2>();
            }
        }

        private static Vector2 GetArcherProjectileOriginPosition(
            int level,
            int archerIndex,
            Vector2 archerPosition)
        {
            if (level == 4)
            {
                return new Vector2(
                    archerIndex == 0 ? -0.2f : 0.2f,
                    0.61f);
            }

            if (level == 7)
            {
                float horizontalPosition = archerIndex == 0
                    ? -0.28f
                    : archerIndex == 1
                        ? 0f
                        : 0.28f;
                return new Vector2(horizontalPosition, 0.72f);
            }

            return archerPosition + Vector2.up * 0.24f;
        }

        private static Vector2 GetArcherAimDirection(float horizontalPosition)
        {
            if (horizontalPosition < -0.001f)
            {
                return new Vector2(-0.9f, 0.45f).normalized;
            }

            if (horizontalPosition > 0.001f)
            {
                return new Vector2(0.9f, 0.45f).normalized;
            }

            return Vector2.down;
        }

        private static Sprite[] GetUnitFrames(
            IReadOnlyDictionary<string, Sprite[]> unitFrames,
            int tier,
            string direction,
            ArcherUnitAnimationBehaviour behaviour)
        {
            string key = UnitSheetDescriptor.BuildKey(tier, direction, behaviour);
            if (!unitFrames.TryGetValue(key, out Sprite[] sprites))
            {
                throw new InvalidOperationException(
                    "Missing generated archer unit frames: " + key);
            }

            return sprites;
        }

        private static float GetArcherSeatHeight(int level)
        {
            switch (level)
            {
                case 1:
                    return 0.78f;
                case 2:
                    return 0.94f;
                case 3:
                    return 1.14f;
                case 4:
                    return 0.4f;
                case 5:
                    return 1.23f;
                case 6:
                    return 1.18f;
                case 7:
                    return 0.44f;
                default:
                    return 0f;
            }
        }

        private static Sprite CreateShowcasePixel()
        {
            DeleteAssetIfPresent(ShowcasePixelPath);
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "ArcherShowcasePixelTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            AssetDatabase.CreateAsset(texture, ShowcasePixelPath);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            sprite.name = "ArcherShowcasePixel";
            AssetDatabase.AddObjectToAsset(sprite, texture);
            EditorUtility.SetDirty(texture);
            return sprite;
        }

        private static void CreateTestScene(
            IReadOnlyDictionary<int, GameObject> prefabs,
            Sprite[][] arrowBanks,
            Sprite showcasePixel)
        {
            TextAsset logicContent =
                AssetDatabase.LoadAssetAtPath<TextAsset>(LogicContentPath);
            if (logicContent == null)
            {
                throw new InvalidOperationException(
                    "Missing compiled card content JSON: " + LogicContentPath);
            }

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene targetScene = SceneManager.GetSceneByPath(TestScenePath);
            bool wasAlreadyLoaded = targetScene.IsValid() && targetScene.isLoaded;
            bool openedInSingleMode = false;

            if (!wasAlreadyLoaded)
            {
                openedInSingleMode = CanReplaceUntitledScene(previousActiveScene);
                SceneAsset existingAsset =
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath);
                targetScene = existingAsset == null
                    ? EditorSceneManager.NewScene(
                        NewSceneSetup.EmptyScene,
                        openedInSingleMode
                            ? NewSceneMode.Single
                            : NewSceneMode.Additive)
                    : EditorSceneManager.OpenScene(
                        TestScenePath,
                        openedInSingleMode
                            ? OpenSceneMode.Single
                            : OpenSceneMode.Additive);
            }

            try
            {
                if (SceneManager.GetActiveScene() != targetScene &&
                    !EditorSceneManager.SetActiveScene(targetScene))
                {
                    throw new InvalidOperationException(
                        "Could not make the archer showcase scene active.");
                }

                ClearScene(targetScene);
                CreateShowcaseCamera();
                CreateShowcaseBackground(showcasePixel);
                CreateShowcaseText(showcasePixel);

                Vector2[] positions =
                {
                    new Vector2(-5.25f, 1.35f),
                    new Vector2(-1.75f, 1.35f),
                    new Vector2(1.75f, 1.35f),
                    new Vector2(5.25f, 1.35f),
                    new Vector2(-3.5f, -2.75f),
                    new Vector2(0f, -2.75f),
                    new Vector2(3.5f, -2.75f)
                };
                var towerActors =
                    new List<ArcherTowerShowcaseActor>(ExpectedTowerLevels);
                TextAsset[] cardContentModules =
                    CardContentModuleCatalogDiscovery
                        .DiscoverTextAssets();

                for (int level = 1; level <= ExpectedTowerLevels; level++)
                {
                    GameObject instance =
                        PrefabUtility.InstantiatePrefab(prefabs[level], targetScene)
                        as GameObject;
                    if (instance == null)
                    {
                        throw new InvalidOperationException(
                            "Failed to instantiate archer tower level " + level + ".");
                    }

                    instance.name = "Archer Tower Level " + level.ToString("00");
                    instance.transform.position = positions[level - 1];
                    instance.transform.localScale = Vector3.one * 1.45f;

                    ArcherTowerView view = instance.GetComponent<ArcherTowerView>();
                    int unitTier = ArcherTowerView.GetDefaultUnitTier(level);
                    ArcherShowcaseCardProgram cardProgram =
                        instance.AddComponent<ArcherShowcaseCardProgram>();
                    cardProgram.Configure(
                        logicContent,
                        showcasePixel,
                        cardContentModules);
                    ArcherTowerShowcaseActor actor =
                        instance.AddComponent<ArcherTowerShowcaseActor>();
                    actor.Configure(
                        view,
                        arrowBanks[unitTier - 1],
                        0.35f + (level - 1) * 0.32f,
                        0.7f + (level - 1) * 0.09f,
                        12.5f,
                        ArcherTowerShowcaseActor.GetDefaultVolleyInterval(level),
                        ArcherTowerShowcaseActor.DefaultProjectileSpeed,
                        cardProgram);
                    towerActors.Add(actor);

                    int archerCount =
                        ArcherTowerView.GetDefaultArcherCount(level);
                    string archerLabel = archerCount == 1
                        ? "1 ARCHER"
                        : archerCount + " ARCHERS";
                    string roofLabel = view.HasOpenRoof
                        ? "OPEN • " + archerLabel + " • UNIT T" + unitTier
                        : "CLOSED • " + archerLabel +
                          " INSIDE • UNIT T" + unitTier;
                    CreateText(
                        "Level " + level + " Label",
                        "LEVEL " + level + "\n" + roofLabel,
                        new Vector3(
                            positions[level - 1].x,
                            positions[level - 1].y - 0.75f,
                            0f),
                        0.048f,
                        32,
                        new Color(0.96f, 0.92f, 0.72f, 1f),
                        60);
                }

                EnemyHealth[] combatTargets =
                    CreateCombatEnemies(
                        targetScene,
                        showcasePixel,
                        out ArcherEnemyCombatSystem combatSystem);
                for (int i = 0; i < towerActors.Count; i++)
                {
                    towerActors[i].SetTargets(combatTargets);
                    towerActors[i].SetEnemyCombatSystem(combatSystem);
                }

                EditorSceneManager.MarkSceneDirty(targetScene);
                if (!EditorSceneManager.SaveScene(targetScene, TestScenePath))
                {
                    throw new InvalidOperationException(
                        "Failed to save archer showcase scene: " + TestScenePath);
                }

                UpsertBuildSettingsScene();
            }
            finally
            {
                RestoreEditorSceneState(
                    previousActiveScene,
                    targetScene,
                    wasAlreadyLoaded || openedInSingleMode);
            }
        }

        private static EnemyHealth[] CreateCombatEnemies(
            Scene targetScene,
            Sprite showcasePixel,
            out ArcherEnemyCombatSystem combatSystem)
        {
            Vector3[] routeCenters =
            {
                new Vector3(-6.3f, -0.7f, 0f),
                new Vector3(-2.1f, -0.7f, 0f),
                new Vector3(2.1f, -0.7f, 0f),
                new Vector3(6.3f, -0.7f, 0f)
            };
            float[] movementSpeeds = { 0.86f, 1.08f, 0.92f, 0.78f };
            var rootEnemies =
                new List<EnemyHealth>(CombatEnemyNames.Length);
            var pooledEnemies = new List<EnemyHealth>(
                CombatEnemyNames.Length * InitialSplitChildrenPerRoot);
            var allActors = new List<EnemyTestActor>(
                CombatEnemyNames.Length *
                (InitialSplitChildrenPerRoot + 1));

            for (int i = 0; i < CombatEnemyNames.Length; i++)
            {
                string enemyName = CombatEnemyNames[i];
                string prefabPath =
                    EnemyPrefabRoot + "/" + enemyName + ".prefab";
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        "Missing combat enemy prefab: " + prefabPath);
                }

                EnemyHealth rootHealth = CreateCombatEnemy(
                    prefab,
                    targetScene,
                    "Live Target " + enemyName,
                    routeCenters[i],
                    movementSpeeds[i],
                    showcasePixel,
                    out EnemyTestActor rootActor);
                rootEnemies.Add(rootHealth);
                allActors.Add(rootActor);

                for (int childIndex = 0;
                     childIndex < InitialSplitChildrenPerRoot;
                     childIndex++)
                {
                    EnemyHealth childHealth = CreateCombatEnemy(
                        prefab,
                        targetScene,
                        "Split Pool " +
                        enemyName +
                        " " +
                        (childIndex + 1).ToString("00"),
                        routeCenters[i],
                        movementSpeeds[i],
                        showcasePixel,
                        out EnemyTestActor childActor);
                    childHealth.gameObject.SetActive(false);
                    pooledEnemies.Add(childHealth);
                    allActors.Add(childActor);
                }
            }

            EnemyTestMovementSystem movementSystem =
                new GameObject("Live Enemy Movement System")
                    .AddComponent<EnemyTestMovementSystem>();
            movementSystem.Configure(allActors.ToArray());

            combatSystem =
                new GameObject("Archer Enemy Combat System")
                    .AddComponent<ArcherEnemyCombatSystem>();
            EnemyHealth[] roots = rootEnemies.ToArray();
            EnemyHealth[] pool = pooledEnemies.ToArray();
            combatSystem.Configure(
                roots,
                pool,
                movementSystem,
                EnemyRespawnDelay);

            var allTargets =
                new EnemyHealth[roots.Length + pool.Length];
            Array.Copy(roots, 0, allTargets, 0, roots.Length);
            Array.Copy(pool, 0, allTargets, roots.Length, pool.Length);
            return allTargets;
        }

        private static EnemyHealth CreateCombatEnemy(
            GameObject prefab,
            Scene targetScene,
            string instanceName,
            Vector3 routeCenter,
            float movementSpeed,
            Sprite showcasePixel,
            out EnemyTestActor testActor)
        {
            GameObject instance =
                PrefabUtility.InstantiatePrefab(prefab, targetScene)
                as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Failed to instantiate combat enemy: " + instanceName);
            }

            instance.name = instanceName;
            instance.transform.position = routeCenter;
            instance.transform.localScale = Vector3.one * 0.84f;

            DirectionalEnemyAnimator directionalAnimator =
                instance.GetComponent<DirectionalEnemyAnimator>();
            testActor = instance.GetComponent<EnemyTestActor>();
            EnemyHealth health = instance.GetComponent<EnemyHealth>();
            SpriteRenderer enemyRenderer =
                instance.GetComponent<SpriteRenderer>();
            if (directionalAnimator == null ||
                testActor == null ||
                health == null ||
                enemyRenderer == null)
            {
                throw new InvalidOperationException(
                    "Combat enemy prefab is missing runtime components: " +
                    AssetDatabase.GetAssetPath(prefab));
            }

            testActor.Configure(
                directionalAnimator,
                1.05f,
                0.18f,
                movementSpeed);
            health.Configure(
                ShowcaseEnemyMaximumHealth,
                ShowcaseEnemyMaximumHealth,
                directionalAnimator);
            ArcherEnemyCardStatusView cardStatus =
                instance.GetComponent<ArcherEnemyCardStatusView>();
            if (cardStatus == null)
            {
                cardStatus =
                    instance.AddComponent<ArcherEnemyCardStatusView>();
            }

            cardStatus.Configure(
                health,
                enemyRenderer,
                showcasePixel);
            return health;
        }

        private static void CreateShowcaseCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            cameraObject.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.orthographic = true;
            camera.orthographicSize = 6.2f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.12f, 0.11f, 1f);
        }

        private static void CreateShowcaseBackground(Sprite pixel)
        {
            CreateColoredSprite(
                "Grass Field",
                pixel,
                Vector3.zero,
                new Vector2(18f, 12.4f),
                new Color(0.35f, 0.48f, 0.23f, 1f),
                -100);
            CreateColoredSprite(
                "Upper Road",
                pixel,
                new Vector3(0f, 1.45f, 0f),
                new Vector2(18f, 3.75f),
                new Color(0.72f, 0.49f, 0.28f, 1f),
                -90);
            CreateColoredSprite(
                "Lower Road",
                pixel,
                new Vector3(0f, -2.65f, 0f),
                new Vector2(14f, 3.35f),
                new Color(0.68f, 0.45f, 0.25f, 1f),
                -90);
            CreateColoredSprite(
                "Live Enemy Lane",
                pixel,
                new Vector3(0f, -0.7f, 0f),
                new Vector2(16f, 1f),
                new Color(0.24f, 0.34f, 0.18f, 1f),
                -85);
            CreateColoredSprite(
                "Title Backdrop",
                pixel,
                new Vector3(0f, 5.45f, 0f),
                new Vector2(18f, 1.5f),
                new Color(0.055f, 0.075f, 0.12f, 0.98f),
                -80);
        }

        private static void CreateShowcaseText(Sprite showcasePixel)
        {
            CreateText(
                "Title",
                "RULEFORGE TD • ARCHER TOWER",
                new Vector3(0f, 5.72f, 0f),
                0.105f,
                64,
                new Color(1f, 0.94f, 0.66f, 1f),
                70);
            CreateCardChip(
                "Split Card Chip",
                "ENEMY SPLIT",
                new Vector3(-3f, 5.12f, 0f),
                new Color(0.18f, 0.52f, 0.68f, 0.98f),
                showcasePixel,
                2.25f);
            CreateText(
                "First Card Arrow",
                "→",
                new Vector3(-1.45f, 5.12f, 0f),
                0.07f,
                44,
                new Color(0.82f, 0.9f, 0.88f, 1f),
                70);
            CreateCardChip(
                "Burn Card Chip",
                "BURN",
                Vector3.up * 5.12f,
                new Color(0.76f, 0.29f, 0.08f, 0.98f),
                showcasePixel);
            CreateText(
                "Second Card Arrow",
                "→",
                new Vector3(1.35f, 5.12f, 0f),
                0.07f,
                44,
                new Color(0.82f, 0.9f, 0.88f, 1f),
                70);
            CreateCardChip(
                "Poison Card Chip",
                "POISON",
                new Vector3(2.7f, 5.12f, 0f),
                new Color(0.43f, 0.19f, 0.58f, 0.98f),
                showcasePixel);
            CreateText(
                "Guide",
                "1 ARROW HIT → ENEMY SPLIT 2 × 45% HP → BURN → POISON\n" +
                "HEALTH < 1 STOPS • COPY ALL STATUS • -10% SCALE • DYNAMIC POOL",
                new Vector3(0f, -5.42f, 0f),
                0.043f,
                32,
                new Color(0.92f, 0.88f, 0.74f, 1f),
                70);
        }

        private static void CreateCardChip(
            string objectName,
            string label,
            Vector3 position,
            Color backgroundColor,
            Sprite showcasePixel,
            float width = 1.65f)
        {
            CreateColoredSprite(
                objectName + " Backdrop",
                showcasePixel,
                position,
                new Vector2(width, 0.42f),
                backgroundColor,
                69);
            CreateText(
                objectName + " Label",
                label,
                position,
                0.065f,
                44,
                Color.white,
                70);
        }

        private static void CreateColoredSprite(
            string name,
            Sprite sprite,
            Vector3 position,
            Vector2 size,
            Color color,
            int sortingOrder)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.position = position;
            gameObject.transform.localScale = new Vector3(size.x, size.y, 1f);
            SpriteRenderer renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
        }

        private static TextMesh CreateText(
            string objectName,
            string content,
            Vector3 position,
            float characterSize,
            int fontSize,
            Color color,
            int sortingOrder)
        {
            var textObject = new GameObject(objectName);
            textObject.transform.position = position;
            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = content;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = characterSize;
            text.fontSize = fontSize;
            text.color = color;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            MeshRenderer renderer = text.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.sortingOrder = sortingOrder;
            return text;
        }

        private static void ValidateGeneratedAssets()
        {
            var errors = new List<string>();
            TowerSheetDescriptor[] idleDescriptors =
                GetTowerDescriptors(IdleRoot, TowerSheetKind.Idle);
            TowerSheetDescriptor[] upgradeDescriptors =
                GetTowerDescriptors(UpgradeRoot, TowerSheetKind.Upgrade);
            UnitSheetDescriptor[] unitDescriptors = GetUnitDescriptors();
            string[] arrowPaths = GetArrowPaths();

            try
            {
                ValidateSourceDescriptors(
                    idleDescriptors,
                    upgradeDescriptors,
                    unitDescriptors,
                    arrowPaths);
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
            }

            for (int i = 0; i < idleDescriptors.Length; i++)
            {
                TowerSheetDescriptor descriptor = idleDescriptors[i];
                Sprite[] sprites = LoadSprites(descriptor.AssetPath);
                int expected = ExpectedIdleFrameCounts[descriptor.Level];
                if (sprites.Length != expected)
                {
                    errors.Add(
                        descriptor.AssetPath +
                        " has " +
                        sprites.Length +
                        " slices, expected " +
                        expected +
                        ".");
                }
            }

            for (int i = 0; i < upgradeDescriptors.Length; i++)
            {
                TowerSheetDescriptor descriptor = upgradeDescriptors[i];
                if (LoadSprites(descriptor.AssetPath).Length != 4)
                {
                    errors.Add(descriptor.AssetPath + " must have four slices.");
                }
            }

            for (int i = 0; i < unitDescriptors.Length; i++)
            {
                UnitSheetDescriptor descriptor = unitDescriptors[i];
                int expected = GetExpectedUnitFrameCount(descriptor.Behaviour);
                if (LoadSprites(descriptor.AssetPath).Length != expected)
                {
                    errors.Add(
                        descriptor.AssetPath +
                        " has the wrong unit frame count.");
                }
            }

            int clipCount = AssetDatabase.FindAssets(
                    "t:AnimationClip",
                    new[] { DataRoot + "/Animations" })
                .Length;
            if (clipCount != ExpectedGeneratedClips)
            {
                errors.Add(
                    "Expected 41 generated animation clips, found " +
                    clipCount +
                    ".");
            }

            for (int level = 1; level <= ExpectedTowerLevels; level++)
            {
                string prefabPath =
                    PrefabRoot +
                    "/ArcherTower_Level" +
                    level.ToString("00") +
                    ".prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    errors.Add("Missing archer tower prefab: " + prefabPath);
                    continue;
                }

                ArcherTowerView view = prefab.GetComponent<ArcherTowerView>();
                if (view == null)
                {
                    errors.Add("Archer tower prefab has no view: " + prefabPath);
                    continue;
                }

                bool expectedOpen = ArcherTowerView.LevelHasOpenRoof(level);
                int expectedArchers =
                    ArcherTowerView.GetDefaultArcherCount(level);
                if (view.Level != level ||
                    view.HasOpenRoof != expectedOpen ||
                    view.ArcherCount != expectedArchers ||
                    view.UpgradeFrameCount != 4 ||
                    view.IdleFrameCount != ExpectedIdleFrameCounts[level])
                {
                    errors.Add("Archer tower prefab configuration mismatch: " + prefabPath);
                }
            }

            for (int i = 0; i < CombatEnemyNames.Length; i++)
            {
                string prefabPath =
                    EnemyPrefabRoot + "/" + CombatEnemyNames[i] + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                {
                    errors.Add("Missing combat enemy prefab: " + prefabPath);
                }
            }

            if (AssetDatabase.LoadAssetAtPath<TextAsset>(LogicContentPath) == null)
            {
                errors.Add("Missing compiled card content JSON: " + LogicContentPath);
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) == null)
            {
                errors.Add("Missing archer tower showcase scene: " + TestScenePath);
            }

            if (!EditorBuildSettings.scenes.Any(scene =>
                    string.Equals(
                        scene.path,
                        TestScenePath,
                        StringComparison.Ordinal) &&
                    scene.enabled))
            {
                errors.Add("Archer tower showcase scene is not enabled in Build Settings.");
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    string.Join(Environment.NewLine, errors));
            }

            Debug.Log(
                "RULEFORGE_ARCHER_VALIDATION_OK levels=7 archers=15 " +
                "roots=4 seedPooled=8 seedTargets=12 enemyHealth=1000 " +
                "cards=3 cardSubject=enemy splitStop=health<1 " +
                "statusCopy=all scalePerGeneration=0.9 " +
                "pool=dynamic-high-water " +
                "towerClips=14 unitClips=27 arrows=27 scene=1");
        }

        private static Sprite[] LoadSprites(string assetPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.name, StringComparer.Ordinal)
                .ToArray();
        }

        private static Sprite LoadSingleSprite(string assetPath)
        {
            Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Sprite>()
                .FirstOrDefault();
            if (sprite == null)
            {
                throw new InvalidOperationException(
                    "Missing imported arrow sprite: " + assetPath);
            }

            return sprite;
        }

        private static int GetExpectedUnitFrameCount(
            ArcherUnitAnimationBehaviour behaviour)
        {
            switch (behaviour)
            {
                case ArcherUnitAnimationBehaviour.Idle:
                    return 4;
                case ArcherUnitAnimationBehaviour.Preattack:
                    return 1;
                default:
                    return 6;
            }
        }

        private static int DirectionOrder(string direction)
        {
            switch (direction)
            {
                case "D":
                    return 0;
                case "U":
                    return 1;
                default:
                    return 2;
            }
        }

        private static string DirectionSuffix(string direction)
        {
            switch (direction)
            {
                case "D":
                    return "Down";
                case "U":
                    return "Up";
                default:
                    return "Side";
            }
        }

        private static int ParseNumericFileName(string assetPath)
        {
            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            if (!int.TryParse(fileName, out int value))
            {
                throw new InvalidOperationException(
                    "Expected numeric asset filename: " + assetPath);
            }

            return value;
        }

        private static bool IsPng(string assetPath)
        {
            return assetPath.EndsWith(
                ".png",
                StringComparison.OrdinalIgnoreCase);
        }

        private static void DeleteAssetIfPresent(string assetPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
            string currentPath = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string nextPath = currentPath + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, segments[i]);
                }

                currentPath = nextPath;
            }
        }

        private static void ClearScene(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                UnityEngine.Object.DestroyImmediate(roots[i]);
            }
        }

        private static bool CanReplaceUntitledScene(Scene activeScene)
        {
            if (!activeScene.IsValid() ||
                !string.IsNullOrEmpty(activeScene.path) ||
                SceneManager.sceneCount != 1)
            {
                return false;
            }

            return Application.isBatchMode || !activeScene.isDirty;
        }

        private static void UpsertBuildSettingsScene()
        {
            EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
            var updated = new List<EditorBuildSettingsScene>(current.Length + 1);
            bool found = false;
            for (int i = 0; i < current.Length; i++)
            {
                EditorBuildSettingsScene scene = current[i];
                if (string.Equals(
                        scene.path,
                        TestScenePath,
                        StringComparison.Ordinal))
                {
                    if (!found)
                    {
                        updated.Add(new EditorBuildSettingsScene(TestScenePath, true));
                        found = true;
                    }

                    continue;
                }

                updated.Add(scene);
            }

            if (!found)
            {
                updated.Add(new EditorBuildSettingsScene(TestScenePath, true));
            }

            EditorBuildSettings.scenes = updated.ToArray();
        }

        private static void RestoreEditorSceneState(
            Scene previousActiveScene,
            Scene targetScene,
            bool wasAlreadyLoaded)
        {
            if (previousActiveScene.IsValid() &&
                previousActiveScene.isLoaded &&
                previousActiveScene != targetScene)
            {
                EditorSceneManager.SetActiveScene(previousActiveScene);
            }

            if (!wasAlreadyLoaded &&
                targetScene.IsValid() &&
                targetScene.isLoaded)
            {
                EditorSceneManager.CloseScene(targetScene, true);
            }
        }

        private enum TowerSheetKind
        {
            Idle,
            Upgrade
        }

        private sealed class TowerSheetDescriptor
        {
            public TowerSheetDescriptor(
                string assetPath,
                int level,
                TowerSheetKind kind)
            {
                AssetPath = assetPath;
                Level = level;
                Kind = kind;
            }

            public string AssetPath { get; }
            public int Level { get; }
            public TowerSheetKind Kind { get; }
            public string SpritePrefix =>
                "ArcherTower_L" +
                Level.ToString("00") +
                "_" +
                Kind;
            public string ClipName =>
                "ArcherTower_Level" +
                Level.ToString("00") +
                "_" +
                Kind;
            public string ClipPath =>
                TowerAnimationRoot + "/" + ClipName + ".anim";
        }

        private sealed class UnitSheetDescriptor
        {
            public UnitSheetDescriptor(
                string assetPath,
                int tier,
                string direction,
                ArcherUnitAnimationBehaviour behaviour)
            {
                AssetPath = assetPath;
                Tier = tier;
                Direction = direction;
                Behaviour = behaviour;
            }

            public string AssetPath { get; }
            public int Tier { get; }
            public string Direction { get; }
            public ArcherUnitAnimationBehaviour Behaviour { get; }
            public string Key => BuildKey(Tier, Direction, Behaviour);
            public string SpritePrefix =>
                "ArcherUnit_T" +
                Tier +
                "_" +
                Behaviour +
                "_" +
                Direction;
            public string ClipName =>
                "ArcherUnit_Tier" +
                Tier +
                "_" +
                Behaviour +
                DirectionSuffix(Direction);
            public string ClipPath =>
                UnitAnimationRoot + "/" + ClipName + ".anim";

            public static string BuildKey(
                int tier,
                string direction,
                ArcherUnitAnimationBehaviour behaviour)
            {
                return tier + "|" + direction + "|" + behaviour;
            }
        }
    }
}
#endif
