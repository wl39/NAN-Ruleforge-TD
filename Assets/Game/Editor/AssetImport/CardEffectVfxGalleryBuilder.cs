#if UNITY_EDITOR
using System;
using System.IO;
using RuleforgeTD.Battle;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.Simulation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleforgeTD.Editor.AssetImport
{
    /// <summary>
    /// Builds the review-only VFX gallery served on port 8766. The page uses
    /// StageOneCardEffectVfxView directly; it is not a parallel VFX mock.
    /// </summary>
    public static class CardEffectVfxGalleryBuilder
    {
        public const string ScenePath =
            "Assets/Game/Scenes/Test/CardEffectVfxGallery.unity";
        public const string WebGLBuildPath =
            "Builds/WebGL/ArcherTowerShowcase";

        private const string StagingRootPath =
            "Builds/WebGL/.CardEffectVfxGallery-staging";
        private const string StagingBuildPath =
            StagingRootPath + "/CardEffectVfxGallery";
        private const string PreviousBuildPath =
            "Builds/WebGL/.CardEffectVfxGallery-previous";
        private const string GalleryPixelPath =
            "Assets/Game/Data/Vfx/CardEffectVfxGalleryPixel.asset";

        [MenuItem("Ruleforge TD/Scenes/Rebuild Card Effect VFX Gallery")]
        public static void BuildSceneFromMenu()
        {
            BuildScene();
        }

        [MenuItem("Ruleforge TD/Build/Build Card Effect VFX Gallery (WebGL)")]
        public static void BuildWebGLFromCommandLine()
        {
            BuildScene();

            if (Directory.Exists(StagingRootPath))
            {
                Directory.Delete(StagingRootPath, true);
            }

            Directory.CreateDirectory(StagingBuildPath);
            PlayerSettings.WebGL.compressionFormat =
                WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.template =
                "PROJECT:RuleforgeFullscreen";

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = StagingBuildPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };
            BuildReport report =
                BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    "Card VFX gallery WebGL build failed with result " +
                    summary.result +
                    " and " +
                    summary.totalErrors +
                    " error(s).");
            }

            PublishBuild();
            Debug.Log(
                "RULEFORGE_CARD_VFX_GALLERY_WEBGL_BUILD_OK path=" +
                WebGLBuildPath +
                " effects=" +
                GetGalleryCardCount() +
                " size=" +
                summary.totalSize +
                " duration=" +
                summary.totalTime);
        }

        public static void BuildScene()
        {
            CardContentModuleCatalogDiscovery
                .SynchronizeCatalogNow();
            EnsureFolder("Assets/Game/Scenes/Test");
            EnsureFolder("Assets/Game/Data/Vfx");
            Sprite pixel = EnsureGalleryPixel();
            StageOnePresentationCatalog catalog =
                LoadGalleryCatalog();
            CompiledContent content =
                LogicContentJsonLoader.Load(
                    catalog.ContentJson,
                    catalog.CardContentModules);
            StageOneCardEffectStyle[] styles =
                StageOneCardEffectPalette
                    .CreateCardGalleryStyles(content);

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            scene.name = "CardEffectVfxGallery";
            Camera galleryCamera = CreateCamera();
            CreateBackdrop(pixel, styles.Length);
            Transform headerRoot =
                CreateHeader(pixel, styles.Length);
            Transform[] cardRoots = CreateEffectGrid(
                pixel,
                styles,
                content.Cards);

            var host = new GameObject("Card Effect VFX Gallery");
            StageOneCardEffectVfxView vfx =
                StageOneCardEffectVfxView.CreateRuntime(
                    host.transform);
            CardEffectVfxGallery gallery =
                host.AddComponent<CardEffectVfxGallery>();
            gallery.Configure(
                vfx,
                catalog,
                galleryCamera,
                headerRoot,
                cardRoots);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException(
                    "Could not save card VFX gallery scene: " +
                    ScenePath);
            }

            Debug.Log(
                "RULEFORGE_CARD_VFX_GALLERY_SCENE_OK effects=" +
                styles.Length +
                " scene=" +
                ScenePath);
        }

        private static Camera CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            cameraObject.tag = "MainCamera";
            camera.transform.position =
                new Vector3(0f, 0f, -10f);
            camera.orthographic = true;
            camera.orthographicSize = 3.7f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor =
                new Color(0.025f, 0.035f, 0.055f, 1f);
            return camera;
        }

        private static Sprite EnsureGalleryPixel()
        {
            UnityEngine.Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath(
                    GalleryPixelPath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite existing)
                {
                    return existing;
                }
            }

            var texture = new Texture2D(
                1,
                1,
                TextureFormat.RGBA32,
                false)
            {
                name = "CardEffectVfxGalleryPixelTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            AssetDatabase.CreateAsset(
                texture,
                GalleryPixelPath);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            sprite.name = "CardEffectVfxGalleryPixel";
            AssetDatabase.AddObjectToAsset(
                sprite,
                texture);
            EditorUtility.SetDirty(texture);
            AssetDatabase.SaveAssets();
            return sprite;
        }

        private static void CreateBackdrop(
            Sprite pixel,
            int effectCount)
        {
            float maximumGalleryHalfHeight =
                GetGridHalfHeight(effectCount, 1) + 3f;
            CreateColoredSprite(
                "Gallery Backdrop",
                pixel,
                Vector3.zero,
                new Vector2(
                    24f,
                    maximumGalleryHalfHeight * 2f),
                new Color(0.035f, 0.055f, 0.075f, 1f),
                -100);
        }

        private static Transform CreateHeader(
            Sprite pixel,
            int effectCount)
        {
            var headerRoot = new GameObject("Gallery Header");
            headerRoot.transform.position = new Vector3(
                0f,
                GetGridHalfHeight(
                    effectCount,
                    CardEffectVfxGallery.ColumnCount) +
                1.93f,
                0f);
            CreateColoredSprite(
                "Header Backdrop",
                pixel,
                Vector3.zero,
                new Vector2(24f, 1.4f),
                new Color(0.055f, 0.08f, 0.11f, 1f),
                -90,
                headerRoot.transform);
            CreateText(
                "Gallery Title",
                "RULEFORGE TD  •  CARD EFFECT VFX LAB",
                new Vector3(0f, 0.25f, 0f),
                0.085f,
                56,
                new Color(1f, 0.93f, 0.68f, 1f),
                80,
                headerRoot.transform);
            CreateText(
                "Gallery Subtitle",
                effectCount +
                " CARDS  •  SCROLL / DRAG  •  SAME RENDERER AS STAGE 01",
                new Vector3(0f, -0.23f, 0f),
                0.046f,
                34,
                new Color(0.66f, 0.78f, 0.84f, 1f),
                80,
                headerRoot.transform);
            return headerRoot.transform;
        }

        private static Transform[] CreateEffectGrid(
            Sprite pixel,
            StageOneCardEffectStyle[] styles,
            CompiledCardDefinition[] cards)
        {
            if (styles == null || cards == null ||
                styles.Length != cards.Length)
            {
                throw new ArgumentException(
                    "Gallery styles and cards must have matching lengths.");
            }

            var cardRoots = new Transform[styles.Length];
            for (int i = 0; i < styles.Length; i++)
            {
                StageOneCardEffectStyle style =
                    styles[i];
                var cardRoot = new GameObject(
                    "VFX Card " + i.ToString("000") + " " + style.Id);
                cardRoot.transform.position =
                    CardEffectVfxGallery.GetSlotPosition(
                        i,
                        styles.Length);
                cardRoots[i] = cardRoot.transform;
                Vector3 panelCenter =
                    Vector3.down * 0.18f;
                CreateColoredSprite(
                    "VFX Panel " + style.Id,
                    pixel,
                    panelCenter,
                    new Vector2(2.08f, 1.62f),
                    new Color(0.07f, 0.10f, 0.13f, 0.96f),
                    -70,
                    cardRoot.transform);

                Color accent = style.Primary;
                accent.a = 0.9f;
                CreateColoredSprite(
                    "VFX Accent " + style.Id,
                    pixel,
                    panelCenter + Vector3.up * 0.75f,
                    new Vector2(2.08f, 0.08f),
                    accent,
                    -60,
                    cardRoot.transform);
                CreateText(
                    "VFX Label " + style.Id,
                    style.Id.ToUpperInvariant(),
                    panelCenter + Vector3.down * 0.53f,
                    0.044f,
                    34,
                    new Color(0.94f, 0.96f, 0.98f, 1f),
                    80,
                    cardRoot.transform);
                CreateText(
                    "VFX Shape " + style.Id,
                    "T" +
                    (int)cards[i].Tier +
                    "  •  " +
                    style.Shape.ToString().ToUpperInvariant(),
                    panelCenter + Vector3.down * 0.73f,
                    0.025f,
                    26,
                    new Color(0.45f, 0.56f, 0.62f, 1f),
                    80,
                    cardRoot.transform);
            }

            return cardRoots;
        }

        private static float GetGridHalfHeight(
            int effectCount,
            int columnCount)
        {
            return (
                CardEffectVfxGallery.GetRowCount(
                    effectCount,
                    columnCount) - 1) *
                CardEffectVfxGallery.VerticalSpacing *
                0.5f;
        }

        private static StageOnePresentationCatalog LoadGalleryCatalog()
        {
            StageOnePresentationCatalog catalog =
                AssetDatabase.LoadAssetAtPath<
                    StageOnePresentationCatalog>(
                    StageOneGameplaySceneInstaller.CatalogPath);
            if (catalog == null || catalog.ContentJson == null)
            {
                throw new InvalidOperationException(
                    "The card VFX gallery requires the Stage 01 " +
                    "presentation catalog.");
            }

            return catalog;
        }

        private static int GetGalleryCardCount()
        {
            StageOnePresentationCatalog catalog =
                LoadGalleryCatalog();
            return LogicContentJsonLoader.Load(
                    catalog.ContentJson,
                    catalog.CardContentModules)
                .Cards
                .Length;
        }

        private static void CreateColoredSprite(
            string objectName,
            Sprite sprite,
            Vector3 position,
            Vector2 size,
            Color color,
            int sortingOrder,
            Transform parent = null)
        {
            var target = new GameObject(objectName);
            target.transform.SetParent(parent, false);
            target.transform.localPosition = position;
            target.transform.localScale =
                new Vector3(size.x, size.y, 1f);
            SpriteRenderer renderer =
                target.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
        }

        private static void CreateText(
            string objectName,
            string content,
            Vector3 position,
            float characterSize,
            int fontSize,
            Color color,
            int sortingOrder,
            Transform parent = null)
        {
            var target = new GameObject(objectName);
            target.transform.SetParent(parent, false);
            target.transform.localPosition = position;
            TextMesh text = target.AddComponent<TextMesh>();
            text.text = content;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = characterSize;
            text.fontSize = fontSize;
            text.color = color;

            Font font =
                Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            text.font = font;
            MeshRenderer renderer =
                text.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.sortingOrder = sortingOrder;
        }

        private static void PublishBuild()
        {
            if (Directory.Exists(PreviousBuildPath))
            {
                Directory.Delete(PreviousBuildPath, true);
            }

            bool movedPrevious = false;
            try
            {
                if (Directory.Exists(WebGLBuildPath))
                {
                    Directory.Move(
                        WebGLBuildPath,
                        PreviousBuildPath);
                    movedPrevious = true;
                }

                Directory.Move(
                    StagingBuildPath,
                    WebGLBuildPath);
                if (Directory.Exists(StagingRootPath))
                {
                    Directory.Delete(StagingRootPath, true);
                }

                if (movedPrevious &&
                    Directory.Exists(PreviousBuildPath))
                {
                    Directory.Delete(PreviousBuildPath, true);
                }
            }
            catch
            {
                if (!Directory.Exists(WebGLBuildPath) &&
                    movedPrevious &&
                    Directory.Exists(PreviousBuildPath))
                {
                    Directory.Move(
                        PreviousBuildPath,
                        WebGLBuildPath);
                }

                throw;
            }
        }

        private static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(
                        current,
                        segments[i]);
                }

                current = next;
            }
        }
    }
}
#endif
