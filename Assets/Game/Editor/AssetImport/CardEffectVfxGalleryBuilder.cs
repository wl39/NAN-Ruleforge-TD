#if UNITY_EDITOR
using System;
using System.IO;
using RuleforgeTD.Battle;
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
                StageOneCardEffectPalette.StyleCount +
                " size=" +
                summary.totalSize +
                " duration=" +
                summary.totalTime);
        }

        public static void BuildScene()
        {
            EnsureFolder("Assets/Game/Scenes/Test");
            EnsureFolder("Assets/Game/Data/Vfx");
            Sprite pixel = EnsureGalleryPixel();

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            scene.name = "CardEffectVfxGallery";
            CreateCamera();
            CreateBackdrop(pixel);
            CreateHeader();
            CreateEffectGrid(pixel);

            var host = new GameObject("Card Effect VFX Gallery");
            StageOneCardEffectVfxView vfx =
                StageOneCardEffectVfxView.CreateRuntime(
                    host.transform);
            CardEffectVfxGallery gallery =
                host.AddComponent<CardEffectVfxGallery>();
            gallery.Configure(vfx);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException(
                    "Could not save card VFX gallery scene: " +
                    ScenePath);
            }

            Debug.Log(
                "RULEFORGE_CARD_VFX_GALLERY_SCENE_OK effects=" +
                StageOneCardEffectPalette.StyleCount +
                " scene=" +
                ScenePath);
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            cameraObject.tag = "MainCamera";
            camera.transform.position =
                new Vector3(0f, 0f, -10f);
            camera.orthographic = true;
            camera.orthographicSize = 5.25f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor =
                new Color(0.025f, 0.035f, 0.055f, 1f);
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

        private static void CreateBackdrop(Sprite pixel)
        {
            CreateColoredSprite(
                "Gallery Backdrop",
                pixel,
                Vector3.zero,
                new Vector2(19f, 10.5f),
                new Color(0.035f, 0.055f, 0.075f, 1f),
                -100);
            CreateColoredSprite(
                "Header Backdrop",
                pixel,
                new Vector3(0f, 4.55f, 0f),
                new Vector2(19f, 1.4f),
                new Color(0.055f, 0.08f, 0.11f, 1f),
                -90);
            CreateColoredSprite(
                "Footer Backdrop",
                pixel,
                new Vector3(0f, -4.72f, 0f),
                new Vector2(19f, 0.7f),
                new Color(0.025f, 0.04f, 0.06f, 1f),
                -90);
        }

        private static void CreateHeader()
        {
            CreateText(
                "Gallery Title",
                "RULEFORGE TD  •  CARD EFFECT VFX LAB",
                new Vector3(0f, 4.8f, 0f),
                0.085f,
                56,
                new Color(1f, 0.93f, 0.68f, 1f),
                80);
            CreateText(
                "Gallery Subtitle",
                "32 EFFECTS  •  30 FPS FRAME STEP  •  SAME RENDERER AS STAGE 01",
                new Vector3(0f, 4.32f, 0f),
                0.046f,
                34,
                new Color(0.66f, 0.78f, 0.84f, 1f),
                80);
        }

        private static void CreateEffectGrid(Sprite pixel)
        {
            for (int i = 0;
                 i < StageOneCardEffectPalette.StyleCount;
                 i++)
            {
                StageOneCardEffectStyle style =
                    StageOneCardEffectPalette.GetStyle(i);
                Vector3 effectCenter =
                    CardEffectVfxGallery.GetSlotPosition(i);
                Vector3 panelCenter =
                    effectCenter + Vector3.down * 0.18f;
                CreateColoredSprite(
                    "VFX Panel " + style.Id,
                    pixel,
                    panelCenter,
                    new Vector2(2.08f, 1.62f),
                    new Color(0.07f, 0.10f, 0.13f, 0.96f),
                    -70);

                Color accent = style.Primary;
                accent.a = 0.9f;
                CreateColoredSprite(
                    "VFX Accent " + style.Id,
                    pixel,
                    panelCenter + Vector3.up * 0.75f,
                    new Vector2(2.08f, 0.08f),
                    accent,
                    -60);
                CreateText(
                    "VFX Label " + style.Id,
                    style.Id.ToUpperInvariant(),
                    panelCenter + Vector3.down * 0.53f,
                    0.044f,
                    34,
                    new Color(0.94f, 0.96f, 0.98f, 1f),
                    80);
                CreateText(
                    "VFX Shape " + style.Id,
                    style.Shape.ToString().ToUpperInvariant(),
                    panelCenter + Vector3.down * 0.73f,
                    0.025f,
                    26,
                    new Color(0.45f, 0.56f, 0.62f, 1f),
                    80);
            }
        }

        private static void CreateColoredSprite(
            string objectName,
            Sprite sprite,
            Vector3 position,
            Vector2 size,
            Color color,
            int sortingOrder)
        {
            var target = new GameObject(objectName);
            target.transform.position = position;
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
            int sortingOrder)
        {
            var target = new GameObject(objectName);
            target.transform.position = position;
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
