#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuleforgeTD.Enemies;
using RuleforgeTD.Enemies.Testing;
using RuleforgeTD.Rendering;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleforgeTD.Editor.AssetImport
{
    public static class CraftPixEnemyAssetBuilder
    {
        private const string RawEnemyRoot = "Assets/ThirdParty/CraftPix/Raw/Enemies";
        private const string EnemyDataRoot = "Assets/Game/Data/Enemies";
        private const string AnimationRoot = EnemyDataRoot + "/Animations";
        private const string ControllerRoot = EnemyDataRoot + "/AnimatorControllers";
        private const string PrefabRoot = "Assets/Game/Prefabs/Enemies";
        private const string TestSceneRoot = "Assets/Game/Scenes/Test";
        private const string TestScenePath = TestSceneRoot + "/EnemyAnimationTest.unity";
        private const string RouteMaterialPath = TestSceneRoot + "/EnemyRouteLine.mat";
        private const string HealthBarSpritePath = EnemyDataRoot + "/EnemyHealthBarSprite.asset";
        private const string HealthBarVisualSettingsPath =
            EnemyDataRoot +
            "/EnemyHealthBarVisualSettings.asset";
        private const int FrameSize = 48;
        private const float PixelsPerUnit = 48f;
        private const float AnimationFrameRate = 10f;
        private const int ExpectedSheetCount = 39;

        private static readonly string[] EnemyIds = { "bee", "dog", "goblin", "slime" };

        [MenuItem("Ruleforge TD/Assets/Rebuild Enemy Test Content")]
        public static void BuildFromMenu()
        {
            BuildAll();
        }

        public static void BuildFromCommandLine()
        {
            BuildAll();
        }

        public static void ValidateFromCommandLine()
        {
            ValidateGeneratedAssets();
        }

        [MenuItem("Ruleforge TD/Build/Build Enemy Test WebGL")]
        public static void BuildWebGLFromCommandLine()
        {
            ValidateGeneratedAssets();

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;

            const string outputPath = "Builds/WebGL/EnemyAnimationTest";
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }

            Directory.CreateDirectory(outputPath);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { TestScenePath },
                locationPathName = outputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"WebGL build failed: result={report.summary.result}, " +
                    $"errors={report.summary.totalErrors}");
            }

            Debug.Log(
                $"RULEFORGE_WEBGL_BUILD_OK output={outputPath} " +
                $"bytes={report.summary.totalSize}");
        }

        private static void BuildAll()
        {
            EnsureFolder(AnimationRoot);
            EnsureFolder(ControllerRoot);
            EnsureFolder(PrefabRoot);
            EnsureFolder(TestSceneRoot);

            string[] texturePaths = GetTexturePaths();
            if (texturePaths.Length != ExpectedSheetCount)
            {
                throw new InvalidOperationException(
                    $"Expected {ExpectedSheetCount} enemy sheets below {RawEnemyRoot}, " +
                    $"but found {texturePaths.Length}.");
            }

            for (int i = 0; i < texturePaths.Length; i++)
            {
                ConfigureAndSliceTexture(texturePaths[i]);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            AnimationDescriptor[] descriptors = texturePaths
                .Select(ParseDescriptor)
                .OrderBy(descriptor => descriptor.EnemyId, StringComparer.Ordinal)
                .ThenBy(descriptor => descriptor.Behaviour)
                .ThenBy(descriptor => DirectionOrder(descriptor.Direction))
                .ToArray();
            ValidateSourceDescriptors(descriptors);

            var clipsByPath = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            for (int i = 0; i < descriptors.Length; i++)
            {
                AnimationDescriptor descriptor = descriptors[i];
                clipsByPath.Add(descriptor.AssetPath, CreateAnimationClip(descriptor));
            }

            Sprite healthBarSprite = CreateHealthBarSprite();
            EnemyHealthBarVisualSettings
                healthBarVisualSettings =
                    AssetDatabase.LoadAssetAtPath<
                        EnemyHealthBarVisualSettings>(
                        HealthBarVisualSettingsPath);
            if (healthBarVisualSettings == null)
            {
                throw new InvalidOperationException(
                    "Missing enemy health-bar visual settings: " +
                    HealthBarVisualSettingsPath);
            }

            var prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            for (int i = 0; i < EnemyIds.Length; i++)
            {
                string enemyId = EnemyIds[i];
                AnimationDescriptor[] enemyDescriptors = descriptors
                    .Where(descriptor => descriptor.EnemyId == enemyId)
                    .ToArray();
                AnimatorController controller = CreateController(
                    enemyId,
                    enemyDescriptors,
                    clipsByPath);
                prefabs.Add(
                    enemyId,
                    CreatePrefab(
                        enemyId,
                        controller,
                        enemyDescriptors,
                        clipsByPath,
                        healthBarSprite,
                        healthBarVisualSettings));
            }

            CreateTestScene(prefabs);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateGeneratedAssets();

            Debug.Log(
                $"RULEFORGE_ENEMY_BUILD_OK sheets={texturePaths.Length} " +
                $"clips={descriptors.Length} prefabs={prefabs.Count} scene={TestScenePath}");
        }

        private static string[] GetTexturePaths()
        {
            return AssetDatabase.FindAssets("t:Texture2D", new[] { RawEnemyRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        private static void ConfigureAndSliceTexture(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (importer == null || texture == null)
            {
                throw new InvalidOperationException($"Unable to load texture importer for {assetPath}.");
            }

            if (texture.height != FrameSize || texture.width % FrameSize != 0)
            {
                throw new InvalidOperationException(
                    $"Enemy sheet must be one {FrameSize}px row: {assetPath} is " +
                    $"{texture.width}x{texture.height}.");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;

            string sheetName = Path.GetFileNameWithoutExtension(assetPath);
            int frameCount = texture.width / FrameSize;
            var metadata = new SpriteMetaData[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                metadata[i] = new SpriteMetaData
                {
                    name = $"{sheetName}_{i:00}",
                    rect = new Rect(i * FrameSize, 0, FrameSize, FrameSize),
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

        private static AnimationDescriptor ParseDescriptor(string assetPath)
        {
            string enemyId = new DirectoryInfo(Path.GetDirectoryName(assetPath) ?? string.Empty).Name;
            if (!EnemyIds.Contains(enemyId))
            {
                throw new InvalidOperationException($"Unknown enemy folder for {assetPath}.");
            }

            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            string[] parts = fileName.Split(new[] { '_' }, 2);
            if (parts.Length != 2 || (parts[0] != "D" && parts[0] != "U" && parts[0] != "S"))
            {
                throw new InvalidOperationException(
                    $"Enemy sheet name must match <D|U|S>_<Behaviour>: {assetPath}.");
            }

            if (!Enum.TryParse(parts[1], true, out EnemyAnimationBehaviour behaviour))
            {
                throw new InvalidOperationException($"Unsupported enemy behaviour in {assetPath}.");
            }

            return new AnimationDescriptor(assetPath, enemyId, parts[0], behaviour);
        }

        private static void ValidateSourceDescriptors(AnimationDescriptor[] descriptors)
        {
            for (int enemyIndex = 0; enemyIndex < EnemyIds.Length; enemyIndex++)
            {
                string enemyId = EnemyIds[enemyIndex];
                AnimationDescriptor[] enemyDescriptors = descriptors
                    .Where(descriptor => descriptor.EnemyId == enemyId)
                    .ToArray();

                if (!enemyDescriptors.Any(descriptor =>
                        descriptor.Behaviour == EnemyAnimationBehaviour.Walk))
                {
                    throw new InvalidOperationException($"{enemyId} is missing Walk animations.");
                }

                foreach (IGrouping<EnemyAnimationBehaviour, AnimationDescriptor> group in
                         enemyDescriptors.GroupBy(descriptor => descriptor.Behaviour))
                {
                    string[] directions = group
                        .Select(descriptor => descriptor.Direction)
                        .Distinct()
                        .OrderBy(direction => direction, StringComparer.Ordinal)
                        .ToArray();
                    if (directions.Length != 3 ||
                        !directions.Contains("D") ||
                        !directions.Contains("U") ||
                        !directions.Contains("S"))
                    {
                        throw new InvalidOperationException(
                            $"{enemyId} {group.Key} must provide D, U, and S sheets.");
                    }
                }
            }
        }

        private static AnimationClip CreateAnimationClip(AnimationDescriptor descriptor)
        {
            Sprite[] sprites = LoadSprites(descriptor.AssetPath);
            if (sprites.Length == 0)
            {
                throw new InvalidOperationException(
                    $"No sliced sprites found at {descriptor.AssetPath}.");
            }

            string clipPath = descriptor.ClipPath;
            DeleteAssetIfPresent(clipPath);

            var clip = new AnimationClip
            {
                name = descriptor.ClipName,
                frameRate = AnimationFrameRate
            };

            var binding = new EditorCurveBinding
            {
                path = string.Empty,
                type = typeof(SpriteRenderer),
                propertyName = "m_Sprite"
            };

            var keyframes = new ObjectReferenceKeyframe[sprites.Length];
            for (int i = 0; i < sprites.Length; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i / AnimationFrameRate,
                    value = sprites[i]
                };
            }

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = descriptor.Behaviour == EnemyAnimationBehaviour.Walk ||
                                descriptor.Behaviour == EnemyAnimationBehaviour.Walk2;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, clipPath);
            return clip;
        }

        private static AnimatorController CreateController(
            string enemyId,
            AnimationDescriptor[] descriptors,
            IReadOnlyDictionary<string, AnimationClip> clipsByPath)
        {
            string enemyName = ToDisplayName(enemyId);
            string controllerPath = $"{ControllerRoot}/{enemyName}.controller";
            DeleteAssetIfPresent(controllerPath);

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimatorState defaultState = null;

            for (int i = 0; i < descriptors.Length; i++)
            {
                AnimationDescriptor descriptor = descriptors[i];
                AnimatorState state = stateMachine.AddState(
                    descriptor.StateName,
                    new Vector3(
                        220f + DirectionOrder(descriptor.Direction) * 190f,
                        40f + (int)descriptor.Behaviour * 80f));
                state.motion = clipsByPath[descriptor.AssetPath];

                if (descriptor.Behaviour == EnemyAnimationBehaviour.Walk &&
                    descriptor.Direction == "D")
                {
                    defaultState = state;
                }
            }

            if (defaultState == null)
            {
                throw new InvalidOperationException($"{enemyName} controller has no WalkDown state.");
            }

            stateMachine.defaultState = defaultState;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static Sprite CreateHealthBarSprite()
        {
            DeleteAssetIfPresent(HealthBarSpritePath);

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "EnemyHealthBarTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            AssetDatabase.CreateAsset(texture, HealthBarSpritePath);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            sprite.name = "EnemyHealthBarSprite";
            AssetDatabase.AddObjectToAsset(sprite, texture);
            EditorUtility.SetDirty(texture);
            return sprite;
        }

        private static GameObject CreatePrefab(
            string enemyId,
            RuntimeAnimatorController controller,
            AnimationDescriptor[] descriptors,
            IReadOnlyDictionary<string, AnimationClip> clipsByPath,
            Sprite healthBarSprite,
            EnemyHealthBarVisualSettings
                healthBarVisualSettings)
        {
            string enemyName = ToDisplayName(enemyId);
            string prefabPath = $"{PrefabRoot}/{enemyName}.prefab";
            DeleteAssetIfPresent(prefabPath);

            AnimationDescriptor walkDownDescriptor = descriptors.First(descriptor =>
                descriptor.Behaviour == EnemyAnimationBehaviour.Walk &&
                descriptor.Direction == "D");
            EnemyAnimationBehaviour[] availableBehaviours = descriptors
                .Select(descriptor => descriptor.Behaviour)
                .Distinct()
                .OrderBy(behaviour => behaviour)
                .ToArray();

            var root = new GameObject(enemyName);
            try
            {
                SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
                renderer.sprite = GetFirstSpriteFromClip(clipsByPath[walkDownDescriptor.AssetPath]);
                WorldSortingLayers.Apply(
                    renderer,
                    WorldSortingLayers.Enemy);
                renderer.sortingOrder = 10;

                Animator animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                DirectionalEnemyAnimator directionalAnimator =
                    root.AddComponent<DirectionalEnemyAnimator>();
                directionalAnimator.Configure(animator, renderer, true, availableBehaviours);

                EnemyHealth health = root.AddComponent<EnemyHealth>();
                health.Configure(GetMaxHealth(enemyId), directionalAnimator);
                CreateHealthBar(
                    root,
                    health,
                    healthBarSprite,
                    healthBarVisualSettings);

                EnemyTestActor testActor = root.AddComponent<EnemyTestActor>();
                testActor.Configure(directionalAnimator, 1.8f, 0.7f, GetMovementSpeed(enemyId));

                CapsuleCollider2D collider = root.AddComponent<CapsuleCollider2D>();
                collider.isTrigger = true;
                collider.size = new Vector2(0.55f, 0.7f);
                collider.offset = new Vector2(0f, 0.35f);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException($"Failed to create prefab at {prefabPath}.");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CreateHealthBar(
            GameObject enemyRoot,
            EnemyHealth health,
            Sprite healthBarSprite,
            EnemyHealthBarVisualSettings visualSettings)
        {
            var barRoot = new GameObject("Health Bar");
            barRoot.transform.SetParent(enemyRoot.transform, false);
            barRoot.transform.localPosition = new Vector3(
                0f,
                visualSettings.LocalY,
                0f);

            var background = new GameObject("Background");
            background.transform.SetParent(barRoot.transform, false);
            background.transform.localScale = new Vector3(
                visualSettings.BackgroundWidth,
                visualSettings.BackgroundHeight,
                1f);
            SpriteRenderer backgroundRenderer = background.AddComponent<SpriteRenderer>();
            backgroundRenderer.sprite = healthBarSprite;
            backgroundRenderer.color = new Color(0.035f, 0.055f, 0.075f, 0.95f);
            WorldSortingLayers.Apply(
                backgroundRenderer,
                WorldSortingLayers.Enemy);
            backgroundRenderer.sortingOrder = 30;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(barRoot.transform, false);
            fill.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            fill.transform.localScale = new Vector3(
                visualSettings.FillWidth,
                visualSettings.FillHeight,
                1f);
            SpriteRenderer fillRenderer = fill.AddComponent<SpriteRenderer>();
            fillRenderer.sprite = healthBarSprite;
            fillRenderer.color = new Color(0.25f, 0.9f, 0.38f, 1f);
            WorldSortingLayers.Apply(
                fillRenderer,
                WorldSortingLayers.Enemy);
            fillRenderer.sortingOrder = 31;

            EnemyHealthBarView healthBarView = enemyRoot.AddComponent<EnemyHealthBarView>();
            healthBarView.Configure(
                health,
                fill.transform,
                fillRenderer,
                null,
                visualSettings);
        }

        private static void CreateTestScene(IReadOnlyDictionary<string, GameObject> prefabs)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "EnemyAnimationTest";

            var cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            cameraObject.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.orthographic = true;
            camera.orthographicSize = 6.2f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.075f, 0.11f, 1f);

            new GameObject("Enemy Test Movement System").AddComponent<EnemyTestMovementSystem>();

            Material routeMaterial = CreateRouteMaterial();
            CreateText(
                "Title",
                "RULEFORGE TD - ENEMY ANIMATION & HEALTH TEST",
                new Vector3(0f, 5.45f, 0f),
                0.105f,
                64);
            TextMesh statusText = CreateText(
                "Animation Status",
                "NOW: WALK",
                new Vector3(0f, 4.92f, 0f),
                0.075f,
                48);
            CreateText(
                "Guide",
                "WALK > ATTACK > SPECIAL > WALK2 > DEATH > DEATH2  |  SIDE USES FLIP X",
                new Vector3(0f, 4.48f, 0f),
                0.055f,
                38);

            Vector2[] centers =
            {
                new Vector2(-4.25f, 1.7f),
                new Vector2(4.25f, 1.7f),
                new Vector2(-4.25f, -2.15f),
                new Vector2(4.25f, -2.15f)
            };

            for (int i = 0; i < EnemyIds.Length; i++)
            {
                string id = EnemyIds[i];
                Vector2 center = centers[i];
                GameObject instance = PrefabUtility.InstantiatePrefab(prefabs[id], scene) as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException($"Failed to instantiate {id} prefab.");
                }

                instance.name = ToDisplayName(id);
                instance.transform.position = center;
                instance.transform.localScale = Vector3.one * 1.55f;
                CreateRouteLine(ToDisplayName(id) + " Route", center, 1.8f, 0.7f, routeMaterial);
                CreateText(
                    ToDisplayName(id) + " Label",
                    ToDisplayName(id).ToUpperInvariant() + "  HP " + GetMaxHealth(id),
                    new Vector3(center.x, center.y - 1.25f, 0f),
                    0.075f,
                    44);
            }

            EnemyAnimationShowcaseSystem showcase =
                new GameObject("Enemy Animation Showcase System")
                    .AddComponent<EnemyAnimationShowcaseSystem>();
            showcase.Configure(1.25f, statusText);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, TestScenePath))
            {
                throw new InvalidOperationException($"Failed to save scene at {TestScenePath}.");
            }

            EnsureTestSceneInBuildSettings();
        }

        private static void EnsureTestSceneInBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes =
                EditorBuildSettings.scenes.ToList();
            int existingIndex = scenes.FindIndex(
                scene => string.Equals(
                    scene.path,
                    TestScenePath,
                    StringComparison.Ordinal));
            var testScene =
                new EditorBuildSettingsScene(
                    TestScenePath,
                    true);
            if (existingIndex >= 0)
            {
                scenes[existingIndex] = testScene;
            }
            else
            {
                scenes.Insert(0, testScene);
            }

            EditorBuildSettings.scenes =
                scenes.ToArray();
        }

        private static Material CreateRouteMaterial()
        {
            DeleteAssetIfPresent(RouteMaterialPath);
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                throw new InvalidOperationException("Sprites/Default shader was not found.");
            }

            var material = new Material(shader)
            {
                name = "Enemy Route Line",
                color = new Color(0.28f, 0.42f, 0.58f, 0.6f)
            };
            AssetDatabase.CreateAsset(material, RouteMaterialPath);
            return material;
        }

        private static void CreateRouteLine(
            string objectName,
            Vector2 center,
            float horizontalHalfRange,
            float verticalHalfRange,
            Material material)
        {
            var routeObject = new GameObject(objectName);
            LineRenderer line = routeObject.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = 4;
            line.startWidth = 0.035f;
            line.endWidth = 0.035f;
            line.startColor = material.color;
            line.endColor = material.color;
            line.sortingOrder = 1;
            line.SetPositions(new[]
            {
                new Vector3(center.x - horizontalHalfRange, center.y - verticalHalfRange, 0f),
                new Vector3(center.x + horizontalHalfRange, center.y - verticalHalfRange, 0f),
                new Vector3(center.x + horizontalHalfRange, center.y + verticalHalfRange, 0f),
                new Vector3(center.x - horizontalHalfRange, center.y + verticalHalfRange, 0f)
            });
        }

        private static TextMesh CreateText(
            string objectName,
            string content,
            Vector3 position,
            float characterSize,
            int fontSize)
        {
            var textObject = new GameObject(objectName);
            textObject.transform.position = position;
            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = content;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = characterSize;
            text.fontSize = fontSize;
            text.color = new Color(0.82f, 0.9f, 1f, 1f);
            AssignLegacyFont(text, 20);
            return text;
        }

        private static void AssignLegacyFont(TextMesh text, int sortingOrder)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            MeshRenderer renderer = text.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.sortingOrder = sortingOrder;
        }

        private static void ValidateGeneratedAssets()
        {
            var errors = new List<string>();
            string[] texturePaths = GetTexturePaths();
            AnimationDescriptor[] descriptors = texturePaths
                .Select(ParseDescriptor)
                .ToArray();

            if (descriptors.Length != ExpectedSheetCount)
            {
                errors.Add(
                    $"Expected {ExpectedSheetCount} source sheets but found {descriptors.Length}.");
            }

            EnemyHealthBarVisualSettings visualSettings =
                AssetDatabase.LoadAssetAtPath<
                    EnemyHealthBarVisualSettings>(
                    HealthBarVisualSettingsPath);
            if (visualSettings == null)
            {
                errors.Add(
                    "Missing health-bar visual settings: " +
                    HealthBarVisualSettingsPath);
            }

            for (int descriptorIndex = 0; descriptorIndex < descriptors.Length; descriptorIndex++)
            {
                AnimationDescriptor descriptor = descriptors[descriptorIndex];
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(descriptor.ClipPath) == null)
                {
                    errors.Add($"Missing animation clip: {descriptor.ClipPath}");
                }
            }

            for (int i = 0; i < EnemyIds.Length; i++)
            {
                string enemyId = EnemyIds[i];
                string enemyName = ToDisplayName(enemyId);
                string prefabPath = $"{PrefabRoot}/{enemyName}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    errors.Add($"Missing prefab: {prefabPath}");
                    continue;
                }

                DirectionalEnemyAnimator directionalAnimator =
                    prefab.GetComponent<DirectionalEnemyAnimator>();
                EnemyHealth health = prefab.GetComponent<EnemyHealth>();
                EnemyHealthBarView healthBar = prefab.GetComponent<EnemyHealthBarView>();
                if (prefab.GetComponent<Animator>() == null ||
                    directionalAnimator == null ||
                    prefab.GetComponent<EnemyTestActor>() == null ||
                    health == null ||
                    healthBar == null)
                {
                    errors.Add($"Prefab is missing required components: {prefabPath}");
                }
                else
                {
                    int expectedBehaviourCount = descriptors
                        .Where(descriptor => descriptor.EnemyId == enemyId)
                        .Select(descriptor => descriptor.Behaviour)
                        .Distinct()
                        .Count();
                    if (directionalAnimator.AvailableBehaviourCount != expectedBehaviourCount)
                    {
                        errors.Add($"Prefab behaviour catalog is incomplete: {prefabPath}");
                    }

                    if (health.MaxHealth != GetMaxHealth(enemyId))
                    {
                        errors.Add(
                            $"Prefab health mismatch: {prefabPath} has {health.MaxHealth}, " +
                            $"expected {GetMaxHealth(enemyId)}.");
                    }

                    if (healthBar.VisualSettings !=
                        visualSettings)
                    {
                        errors.Add(
                            "Prefab health-bar settings mismatch: " +
                            prefabPath);
                    }
                }

                string controllerPath = $"{ControllerRoot}/{enemyName}.controller";
                AnimatorController controller =
                    AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (controller == null)
                {
                    errors.Add($"Missing animator controller: {controllerPath}");
                }
                else
                {
                    int expectedStateCount = descriptors.Count(descriptor =>
                        descriptor.EnemyId == enemyId);
                    int actualStateCount = controller.layers[0].stateMachine.states.Length;
                    if (actualStateCount != expectedStateCount)
                    {
                        errors.Add(
                            $"Controller state mismatch: {controllerPath} has " +
                            $"{actualStateCount}, expected {expectedStateCount}.");
                    }
                }
            }

            if (AssetDatabase.LoadAllAssetsAtPath(HealthBarSpritePath).OfType<Sprite>().FirstOrDefault() == null)
            {
                errors.Add($"Missing health bar sprite: {HealthBarSpritePath}");
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TestScenePath) == null)
            {
                errors.Add($"Missing test scene: {TestScenePath}");
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            }

            Debug.Log("RULEFORGE_ENEMY_VALIDATION_OK prefabs=4 clips=39 healthBars=4 scene=1");
        }

        private static Sprite[] LoadSprites(string assetPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.name, StringComparer.Ordinal)
                .ToArray();
        }

        private static Sprite GetFirstSpriteFromClip(AnimationClip clip)
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            if (bindings.Length == 0)
            {
                return null;
            }

            ObjectReferenceKeyframe[] frames =
                AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]);
            return frames.Length == 0 ? null : frames[0].value as Sprite;
        }

        private static float GetMovementSpeed(string enemyId)
        {
            switch (enemyId)
            {
                case "bee":
                    return 1.7f;
                case "dog":
                    return 1.5f;
                case "goblin":
                    return 1.25f;
                default:
                    return 1.1f;
            }
        }

        private static int GetMaxHealth(string enemyId)
        {
            switch (enemyId)
            {
                case "bee":
                    return 5;
                case "dog":
                    return 8;
                case "goblin":
                    return 30;
                case "slime":
                    return 10;
                default:
                    throw new ArgumentOutOfRangeException(nameof(enemyId), enemyId, null);
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

        private static string ToDisplayName(string enemyId)
        {
            return char.ToUpperInvariant(enemyId[0]) + enemyId.Substring(1);
        }

        private static void DeleteAssetIfPresent(string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
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

        private sealed class AnimationDescriptor
        {
            public AnimationDescriptor(
                string assetPath,
                string enemyId,
                string direction,
                EnemyAnimationBehaviour behaviour)
            {
                AssetPath = assetPath;
                EnemyId = enemyId;
                Direction = direction;
                Behaviour = behaviour;
            }

            public string AssetPath { get; }
            public string EnemyId { get; }
            public string Direction { get; }
            public EnemyAnimationBehaviour Behaviour { get; }
            public string StateName => Behaviour + DirectionSuffix(Direction);
            public string ClipName => ToDisplayName(EnemyId) + "_" + StateName;
            public string ClipPath => AnimationRoot + "/" + ClipName + ".anim";
        }
    }
}
#endif
