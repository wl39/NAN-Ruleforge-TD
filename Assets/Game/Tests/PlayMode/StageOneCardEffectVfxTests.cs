using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RuleforgeTD.Battle;
using RuleforgeTD.GameLogic.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuleforgeTD.Tests.PlayMode
{
    public sealed class StageOneCardEffectVfxTests
    {
        private static readonly string[] ExpectedCardIds =
        {
            "split",
            "pierce",
            "burn",
            "slow",
            "explode",
            "knockback",
            "mark",
            "gold_bounty",
            "poison",
            "enlarge",
            "shrink",
            "stun",
            "ricochet",
            "bleed",
            "accelerate",
            "homing",
            "delay",
            "curse",
            "bind",
            "airborne",
            "shock",
            "freeze",
            "afterimage",
            "pulse",
            "magnet",
            "reflect",
            "contagion",
            "seal",
            "corrosion",
            "orbit",
            "lifesteal",
            "fear"
        };

        [Test]
        public void Palette_DefinesDistinctShapeForAllThirtyTwoCards()
        {
            Assert.That(
                StageOneCardEffectPalette.StyleCount,
                Is.EqualTo(ExpectedCardIds.Length));
            var shapes =
                new HashSet<StageOneCardEffectShape>();

            for (int i = 0; i < ExpectedCardIds.Length; i++)
            {
                Assert.That(
                    StageOneCardEffectPalette.TryGetStyle(
                        ExpectedCardIds[i],
                        out StageOneCardEffectStyle style),
                    Is.True,
                    ExpectedCardIds[i]);
                Assert.That(style.Id, Is.EqualTo(ExpectedCardIds[i]));
                Assert.That(style.Duration, Is.GreaterThan(0f));
                Assert.That(
                    style.Duration,
                    Is.EqualTo(
                        StageOneCardEffectPalette
                            .StandardEffectDuration)
                        .Within(0.0001f),
                    ExpectedCardIds[i] +
                    " must use the shared VFX duration.");
                Assert.That(style.Radius, Is.GreaterThan(0f));
                Assert.That(style.Width, Is.GreaterThan(0f));
                shapes.Add(style.Shape);
            }

            Assert.That(
                shapes.Count,
                Is.EqualTo(ExpectedCardIds.Length),
                "Each new card needs a visually distinct motion shape.");
        }

        [Test]
        public void ProjectileFlags_CoverEveryCardWithoutCollisions()
        {
            var flags = new HashSet<uint>();
            for (int i = 0; i < ExpectedCardIds.Length; i++)
            {
                ProjectileEffectVisualFlags flag =
                    GameSimulation.GetCardVisualFlag(
                        ExpectedCardIds[i]);
                Assert.That(
                    flag,
                    Is.Not.EqualTo(
                        ProjectileEffectVisualFlags.None),
                    ExpectedCardIds[i]);
                Assert.That(
                    flags.Add((uint)flag),
                    Is.True,
                    "Duplicate projectile VFX flag: " +
                    ExpectedCardIds[i]);
            }

            Assert.That(flags.Count, Is.EqualTo(32));
        }

        [Test]
        public void Palette_CurseIsPurpleAndBindIsNeutralGray()
        {
            Assert.That(
                StageOneCardEffectPalette.TryGetStyle(
                    "curse",
                    out StageOneCardEffectStyle curse),
                Is.True);
            Assert.That(
                curse.Primary.b,
                Is.GreaterThan(curse.Primary.g));
            Assert.That(
                curse.Primary.b,
                Is.GreaterThan(curse.Primary.r));

            Assert.That(
                StageOneCardEffectPalette.TryGetStyle(
                    "bind",
                    out StageOneCardEffectStyle bind),
                Is.True);
            float minimum = Mathf.Min(
                bind.Primary.r,
                Mathf.Min(bind.Primary.g, bind.Primary.b));
            float maximum = Mathf.Max(
                bind.Primary.r,
                Mathf.Max(bind.Primary.g, bind.Primary.b));
            Assert.That(
                maximum - minimum,
                Is.LessThan(0.08f),
                "Bind should read as neutral gray, not a coloured debuff.");

            Assert.That(
                StageOneCardEffectPalette.TryGetStyle(
                    "blind",
                    out StageOneCardEffectStyle blind),
                Is.True);
            Assert.That(blind.Id, Is.EqualTo("bind"));
        }

        [UnityTest]
        public IEnumerator
            ProceduralPool_RicochetLinksPositionsAndRemainsBounded()
        {
            var host = new GameObject(
                "Card VFX Pool Test",
                typeof(StageOneCardEffectVfxView));
            try
            {
                StageOneCardEffectVfxView view =
                    host.GetComponent<
                        StageOneCardEffectVfxView>();
                view.InitializeNow();

                Vector3 source = new Vector3(1f, 2f, 0f);
                Vector3 target = new Vector3(4f, 3f, 0f);
                var ricochetEvent =
                    new SimulationPresentationEvent(
                        10,
                        PresentationEventType.ProjectileRicochet,
                        3,
                        7,
                        1,
                        "ricochet");

                Assert.That(
                    view.PlayEvent(
                        ricochetEvent,
                        target,
                        true,
                        source,
                        true),
                    Is.True);
                Assert.That(
                    view.LastPlayedEffectId,
                    Is.EqualTo("ricochet"));
                Assert.That(
                    view.LastPlayedShape,
                    Is.EqualTo(
                        StageOneCardEffectShape.Arc));
                Assert.That(
                    view.LastStartPosition.x,
                    Is.EqualTo(source.x).Within(0.001f));
                Assert.That(
                    view.LastEndPosition.x,
                    Is.EqualTo(target.x).Within(0.001f));

                for (int i = 0; i < 100; i++)
                {
                    view.Play(
                        ExpectedCardIds[
                            i % ExpectedCardIds.Length],
                        new Vector3(i * 0.1f, 0f, 0f));
                }

                Assert.That(
                    view.PoolCapacity,
                    Is.EqualTo(
                        StageOneCardEffectVfxView
                            .DefaultPoolCapacity));
                Assert.That(
                    view.ActiveEffectCount,
                    Is.EqualTo(
                        StageOneCardEffectVfxView
                            .DefaultPoolCapacity),
                    "Overflow must reuse a bounded slot.");

                yield return new WaitForSecondsRealtime(1.05f);

                Assert.That(view.ActiveEffectCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator
            ProceduralPool_SplitUsesSixPixelSparksAndNoBranchLine()
        {
            var host = new GameObject(
                "Split VFX Pool Test",
                typeof(StageOneCardEffectVfxView));
            try
            {
                StageOneCardEffectVfxView view =
                    host.GetComponent<
                        StageOneCardEffectVfxView>();
                view.InitializeNow(16);

                Assert.That(
                    view.Play(
                        "split",
                        new Vector3(2f, 3f, 0f)),
                    Is.True);
                yield return null;

                SpriteRenderer[] sparks =
                    host.GetComponentsInChildren<
                        SpriteRenderer>(true);
                int visibleSparks = 0;
                for (int i = 0; i < sparks.Length; i++)
                {
                    if (sparks[i].enabled)
                    {
                        visibleSparks++;
                    }
                }

                Assert.That(
                    visibleSparks,
                    Is.EqualTo(6),
                    "Split must match the six-spark Archer showcase burst.");

                LineRenderer[] lines =
                    host.GetComponentsInChildren<
                        LineRenderer>(true);
                for (int i = 0; i < lines.Length; i++)
                {
                    Assert.That(
                        lines[i].enabled,
                        Is.False,
                        "The old connected Y-branch must not render behind " +
                        "the split sparks.");
                }

                yield return new WaitForSecondsRealtime(0.60f);
                Assert.That(view.ActiveEffectCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator
            ProceduralPool_NonSplitEffectsUseBoundedPixelMeshes()
        {
            var host = new GameObject(
                "Pixel VFX Pool Test",
                typeof(StageOneCardEffectVfxView));
            try
            {
                StageOneCardEffectVfxView view =
                    host.GetComponent<
                        StageOneCardEffectVfxView>();
                view.InitializeNow(16);

                Assert.That(
                    view.Play(
                        "explode",
                        new Vector3(1.13f, 2.29f, 0f)),
                    Is.True);
                yield return null;

                Assert.That(
                    view.ActivePixelBlockCount,
                    Is.GreaterThan(8));
                Assert.That(
                    view.ActivePixelBlockCount,
                    Is.LessThanOrEqualTo(
                        StageOneCardEffectVfxView
                            .MaximumPixelBlocks));

                LineRenderer[] lines =
                    host.GetComponentsInChildren<
                        LineRenderer>(true);
                for (int i = 0; i < lines.Length; i++)
                {
                    Assert.That(
                        lines[i].enabled,
                        Is.False,
                        "Smooth lines must stay disabled in pixel VFX mode.");
                }

                MeshFilter[] meshes =
                    host.GetComponentsInChildren<
                        MeshFilter>(true);
                int visiblePixelMeshes = 0;
                for (int i = 0; i < meshes.Length; i++)
                {
                    if (meshes[i].sharedMesh != null &&
                        meshes[i].sharedMesh.vertexCount > 0)
                    {
                        visiblePixelMeshes++;
                        Assert.That(
                            meshes[i].sharedMesh.vertexCount % 4,
                            Is.Zero,
                            "Every visible pixel must be one quad.");
                    }
                }

                Assert.That(visiblePixelMeshes, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void
            ProceduralPool_SemanticCommonEffectsBuildDetailedPixelIcons()
        {
            var host = new GameObject(
                "Semantic Common VFX Test",
                typeof(StageOneCardEffectVfxView));
            try
            {
                StageOneCardEffectVfxView view =
                    host.GetComponent<
                        StageOneCardEffectVfxView>();
                view.InitializeNow(16);

                AssertSemanticPixelEffect(
                    view,
                    "pierce",
                    0.22f,
                    12);
                AssertSemanticPixelEffect(
                    view,
                    "burn",
                    0.31f,
                    24);
                AssertSemanticPixelEffect(
                    view,
                    "slow",
                    0.34f,
                    28);
                AssertSemanticPixelEffect(
                    view,
                    "poison",
                    0.35f,
                    28);
                AssertSemanticPixelEffect(
                    view,
                    "explode",
                    0.31f,
                    36);
                AssertSemanticPixelEffect(
                    view,
                    "bleed",
                    0.28f,
                    24);
                AssertSemanticPixelEffect(
                    view,
                    "stun",
                    0.28f,
                    20);
                AssertSemanticPixelEffect(
                    view,
                    "blind",
                    0.30f,
                    32);

                LineRenderer[] lines =
                    host.GetComponentsInChildren<
                        LineRenderer>(true);
                for (int i = 0; i < lines.Length; i++)
                {
                    Assert.That(
                        lines[i].enabled,
                        Is.False,
                        "Semantic pixel icons must not fall back to " +
                        "smooth line rendering.");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void
            ProceduralPool_SemanticMotionConvergesBuildsAndContracts()
        {
            var host = new GameObject(
                "Semantic Motion Direction Test",
                typeof(StageOneCardEffectVfxView));
            try
            {
                StageOneCardEffectVfxView view =
                    host.GetComponent<
                        StageOneCardEffectVfxView>();
                view.InitializeNow(16);

                Assert.That(
                    view.Play("magnet", Vector3.zero),
                    Is.True);
                view.SetManualPreviewTime(0.08f);
                Bounds magnetOuterBounds =
                    GetActivePixelBounds(host);
                view.SetManualPreviewTime(0.46f);
                Bounds magnetCenterBounds =
                    GetActivePixelBounds(host);
                Assert.That(
                    magnetCenterBounds.size.x,
                    Is.LessThan(
                        magnetOuterBounds.size.x -
                        StageOneCardEffectVfxView.PixelWorldSize * 4f),
                    "Magnet circles must converge toward one centre.");

                view.StopAll();
                Assert.That(
                    view.Play("airborne", Vector3.zero),
                    Is.True);
                view.SetManualPreviewTime(0.08f);
                Bounds airborneBaseBounds =
                    GetActivePixelBounds(host);
                view.SetManualPreviewTime(0.42f);
                Bounds airborneWhirlwindBounds =
                    GetActivePixelBounds(host);
                Assert.That(
                    airborneWhirlwindBounds.max.y,
                    Is.GreaterThan(
                        airborneBaseBounds.max.y +
                        StageOneCardEffectVfxView.PixelWorldSize * 4f),
                    "Airborne must build a whirlwind from bottom to top.");

                view.StopAll();
                Assert.That(
                    view.Play("corrosion", Vector3.zero),
                    Is.True);
                view.SetManualPreviewTime(0.04f);
                Bounds corrosionOuterBounds =
                    GetActivePixelBounds(host);
                view.SetManualPreviewTime(0.46f);
                Bounds corrosionInnerBounds =
                    GetActivePixelBounds(host);
                Assert.That(
                    corrosionInnerBounds.size.x,
                    Is.LessThan(
                        corrosionOuterBounds.size.x -
                        StageOneCardEffectVfxView.PixelWorldSize * 4f),
                    "Corrosion must contract instead of expanding.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void
            ProceduralPool_BleedIsDiagonalAndBlindCompletesRedCross()
        {
            var host = new GameObject(
                "Diagonal Wound And Blind Cross Test",
                typeof(StageOneCardEffectVfxView));
            try
            {
                StageOneCardEffectVfxView view =
                    host.GetComponent<
                        StageOneCardEffectVfxView>();
                view.InitializeNow(16);

                Assert.That(
                    view.Play("bleed", Vector3.zero),
                    Is.True);
                view.SetManualPreviewTime(0.34f);
                Mesh bleedMesh = GetActivePixelMesh(host);
                Assert.That(
                    HasPixelCenter(
                        bleedMesh,
                        position =>
                            position.x < -0.18f &&
                            position.y > 0.18f),
                    Is.True,
                    "Bleed must begin in the upper-left quadrant.");
                Assert.That(
                    HasPixelCenter(
                        bleedMesh,
                        position =>
                            position.x > 0.18f &&
                            position.y < -0.18f),
                    Is.True,
                    "Bleed must end in the lower-right quadrant.");
                Vector3[] bleedVertices = bleedMesh.vertices;
                Vector3 bleedDirection =
                    new Vector3(1f, -1f, 0f).normalized;
                Vector3 bleedNormal =
                    new Vector3(1f, 1f, 0f).normalized;
                float minimumAlong = float.PositiveInfinity;
                float maximumAlong = float.NegativeInfinity;
                float minimumAcross = float.PositiveInfinity;
                float maximumAcross = float.NegativeInfinity;
                for (int vertex = 0;
                     vertex + 3 < bleedVertices.Length;
                     vertex += 4)
                {
                    Vector3 center =
                        (bleedVertices[vertex] +
                         bleedVertices[vertex + 1] +
                         bleedVertices[vertex + 2] +
                         bleedVertices[vertex + 3]) *
                        0.25f;
                    float along =
                        Vector3.Dot(center, bleedDirection);
                    float across =
                        Vector3.Dot(center, bleedNormal);
                    minimumAlong = Mathf.Min(minimumAlong, along);
                    maximumAlong = Mathf.Max(maximumAlong, along);
                    minimumAcross = Mathf.Min(minimumAcross, across);
                    maximumAcross = Mathf.Max(maximumAcross, across);
                }

                Assert.That(
                    maximumAlong - minimumAlong,
                    Is.GreaterThan(
                        (maximumAcross - minimumAcross) * 1.8f),
                    "Bleed must read as a long diagonal cut, not a vertical opening.");

                view.StopAll();
                Assert.That(
                    view.Play("blind", Vector3.zero),
                    Is.True);
                view.SetManualPreviewTime(0.36f);
                Mesh blindMesh = GetActivePixelMesh(host);
                Vector3[] vertices = blindMesh.vertices;
                Color32[] colors = blindMesh.colors32;
                int upperRightRedCrossPixels = 0;
                for (int vertex = 0;
                     vertex + 3 < vertices.Length;
                     vertex += 4)
                {
                    Vector3 center =
                        (vertices[vertex] +
                         vertices[vertex + 1] +
                         vertices[vertex + 2] +
                         vertices[vertex + 3]) *
                        0.25f;
                    Color32 color = colors[vertex];
                    if (center.x > 0.16f &&
                        center.y > 0.10f &&
                        color.r > color.g + 60)
                    {
                        upperRightRedCrossPixels++;
                    }
                }

                Assert.That(
                    upperRightRedCrossPixels,
                    Is.GreaterThanOrEqualTo(3),
                    "Blind must retain the full red upper-right arm.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void
            FlagPlayback_FollowsHitEnemyAndIgnoresEmptyFlags()
        {
            var host = new GameObject(
                "Attached Flag VFX Test",
                typeof(StageOneCardEffectVfxView));
            var enemyObject = new GameObject(
                "Attached VFX Enemy",
                typeof(SpriteRenderer),
                typeof(StageOneEnemyView));
            try
            {
                StageOneCardEffectVfxView view =
                    host.GetComponent<
                        StageOneCardEffectVfxView>();
                view.InitializeNow(16);
                StageOneEnemyView enemy =
                    enemyObject.GetComponent<StageOneEnemyView>();
                enemyObject.transform.position =
                    new Vector3(2f, 3f, 0f);
                enemy.Configure(27, "raider", 1f);

                Assert.That(
                    view.PlayFlagSet(
                        (uint)ProjectileEffectVisualFlags.Bleed,
                        enemy),
                    Is.EqualTo(1));
                view.SetManualPreviewTime(0.28f);
                Bounds initialBounds =
                    GetActivePixelBounds(host);

                enemyObject.transform.position +=
                    new Vector3(3f, 2f, 0f);
                view.SetManualPreviewTime(0.28f);
                Bounds movedBounds =
                    GetActivePixelBounds(host);

                Assert.That(
                    movedBounds.center.x - initialBounds.center.x,
                    Is.EqualTo(3f).Within(0.001f));
                Assert.That(
                    movedBounds.center.y - initialBounds.center.y,
                    Is.EqualTo(2f).Within(0.001f));
                int activeBeforeEmptyFlags =
                    view.ActiveEffectCount;
                Assert.That(
                    view.PlayFlagSet(0u, enemy),
                    Is.Zero);
                Assert.That(
                    view.ActiveEffectCount,
                    Is.EqualTo(activeBeforeEmptyFlags));
            }
            finally
            {
                Object.DestroyImmediate(enemyObject);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void
            Playback_NormalizesSizeExceptSplitAndLifesteal()
        {
            var host = new GameObject(
                "Normalized VFX Size Test",
                typeof(StageOneCardEffectVfxView));
            try
            {
                StageOneCardEffectVfxView view =
                    host.GetComponent<
                        StageOneCardEffectVfxView>();
                view.InitializeNow(16);

                for (int i = 0; i < ExpectedCardIds.Length; i++)
                {
                    string effectId = ExpectedCardIds[i];
                    view.StopAll();
                    Assert.That(
                        view.Play(effectId, Vector3.zero),
                        Is.True,
                        effectId);
                    Assert.That(
                        StageOneCardEffectPalette.TryGetStyle(
                            effectId,
                            out StageOneCardEffectStyle sourceStyle),
                        Is.True);
                    float expectedRadius =
                        effectId == "split" ||
                        effectId == "lifesteal"
                            ? sourceStyle.Radius
                            : StageOneCardEffectVfxView
                                .StandardPlaybackRadius;
                    Assert.That(
                        view.LastPlayedRadius,
                        Is.EqualTo(expectedRadius)
                            .Within(0.0001f),
                        effectId);
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void
            PresentationEvent_PreservesImpactAndDeathFlagBits()
        {
            const uint expectedFlags =
                (uint)(
                    ProjectileEffectVisualFlags.Burn |
                    ProjectileEffectVisualFlags.Poison);
            var item = new SimulationPresentationEvent(
                12,
                PresentationEventType.ProjectileHit,
                7,
                4,
                100,
                string.Empty,
                expectedFlags);

            Assert.That(
                item.EffectVisualFlags,
                Is.EqualTo(expectedFlags));
        }

        private static bool HasPixelCenter(
            Mesh mesh,
            System.Predicate<Vector3> predicate)
        {
            Vector3[] vertices = mesh.vertices;
            for (int vertex = 0;
                 vertex + 3 < vertices.Length;
                 vertex += 4)
            {
                Vector3 center =
                    (vertices[vertex] +
                     vertices[vertex + 1] +
                     vertices[vertex + 2] +
                     vertices[vertex + 3]) *
                    0.25f;
                if (predicate(center))
                {
                    return true;
                }
            }

            return false;
        }

        private static Mesh GetActivePixelMesh(GameObject host)
        {
            MeshFilter[] meshes =
                host.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshes.Length; i++)
            {
                Mesh mesh = meshes[i].sharedMesh;
                if (mesh != null && mesh.vertexCount > 0)
                {
                    return mesh;
                }
            }

            Assert.Fail("Expected an active pixel mesh.");
            return null;
        }

        private static Bounds GetActivePixelBounds(
            GameObject host)
        {
            return GetActivePixelMesh(host).bounds;
        }

        private static void AssertSemanticPixelEffect(
            StageOneCardEffectVfxView view,
            string effectId,
            float previewTime,
            int minimumPixelBlockCount)
        {
            view.StopAll();
            Assert.That(
                view.Play(effectId, Vector3.zero),
                Is.True,
                effectId);
            view.SetManualPreviewTime(previewTime);
            Assert.That(
                view.ActivePixelBlockCount,
                Is.GreaterThanOrEqualTo(minimumPixelBlockCount),
                effectId + " should render a readable pixel silhouette.");
            Assert.That(
                view.ActivePixelBlockCount,
                Is.LessThanOrEqualTo(
                    StageOneCardEffectVfxView.MaximumPixelBlocks),
                effectId + " must remain inside the WebGL block budget.");
        }

        [UnityTest]
        public IEnumerator
            EnemyVisual_CurseBindAndAirborneRemainReadable()
        {
            var enemyObject = new GameObject(
                "Expanded Status Visual Enemy",
                typeof(SpriteRenderer),
                typeof(StageOneEnemyView));
            Texture2D texture = null;
            Sprite sprite = null;
            try
            {
                SpriteRenderer renderer =
                    enemyObject.GetComponent<SpriteRenderer>();
                texture = new Texture2D(
                    2,
                    2,
                    TextureFormat.RGBA32,
                    false);
                texture.SetPixels(
                    new[]
                    {
                        Color.white,
                        Color.white,
                        Color.white,
                        Color.white
                    });
                texture.Apply();
                sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, 2f, 2f),
                    new Vector2(0.5f, 0.5f),
                    2f);
                renderer.sprite = sprite;

                StageOneEnemyView enemy =
                    enemyObject.GetComponent<StageOneEnemyView>();
                enemy.Configure(41, "raider", 1f);
                StageOneEnemyCardEffectVisual visual =
                    enemyObject.AddComponent<
                        StageOneEnemyCardEffectVisual>();
                visual.Configure(enemy, renderer);

                visual.SetVisualFlags(
                    StageOneEnemyEffectVisualFlags.Curse);
                yield return null;

                Assert.That(visual.IsCursed, Is.True);
                Assert.That(
                    visual.DominantEffectId,
                    Is.EqualTo("curse"));
                Assert.That(
                    visual.DominantColor.b,
                    Is.GreaterThan(visual.DominantColor.g));
                Assert.That(visual.TintOverlayVisible, Is.True);

                visual.SetVisualFlags(
                    StageOneEnemyEffectVisualFlags.Bind);
                yield return null;

                Assert.That(visual.IsBound, Is.True);
                Assert.That(
                    visual.DominantEffectId,
                    Is.EqualTo("bind"));

                Vector3 groundPosition =
                    enemyObject.transform.position;
                visual.SetVisualFlags(
                    StageOneEnemyEffectVisualFlags.Airborne,
                    30,
                    77);
                yield return null;

                Assert.That(visual.IsAirborne, Is.True);
                Assert.That(
                    visual.AirborneLift,
                    Is.GreaterThan(0.1f));
                Assert.That(
                    enemyObject.transform.position.y,
                    Is.GreaterThan(groundPosition.y));
                Assert.That(
                    visual.AirborneShadowVisible,
                    Is.True);

                // The owning view overwrites the root from each authoritative
                // snapshot. The previous visual lift must be removed before
                // that write so subsequent frames apply the full new lift,
                // rather than only the difference between two arc samples.
                visual.PrepareForAuthoritativeSnapshot();
                Vector3 nextGroundPosition =
                    new Vector3(2f, 3f, 0f);
                enemyObject.transform.position =
                    nextGroundPosition;
                visual.SetVisualFlags(
                    StageOneEnemyEffectVisualFlags.Airborne,
                    29,
                    77);
                yield return null;

                Assert.That(
                    enemyObject.transform.position.y,
                    Is.GreaterThan(
                        nextGroundPosition.y + 0.1f));

                visual.SetVisualFlags(
                    StageOneEnemyEffectVisualFlags.None);
                yield return null;

                Assert.That(
                    enemyObject.transform.position.y,
                    Is.EqualTo(
                        nextGroundPosition.y).Within(0.001f));
                Assert.That(
                    visual.AirborneShadowVisible,
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(enemyObject);
                if (sprite != null)
                {
                    Object.DestroyImmediate(sprite);
                }

                if (texture != null)
                {
                    Object.DestroyImmediate(texture);
                }
            }
        }

        [Test]
        public void ProjectileVisual_UsesSnapshotFlagsWithoutGameplayGuessing()
        {
            var projectileObject = new GameObject(
                "Expanded Projectile Visual",
                typeof(SpriteRenderer),
                typeof(StageOneProjectileView));
            Texture2D texture = null;
            Sprite sprite = null;
            try
            {
                SpriteRenderer renderer =
                    projectileObject.GetComponent<SpriteRenderer>();
                texture = new Texture2D(
                    2,
                    2,
                    TextureFormat.RGBA32,
                    false);
                texture.SetPixels(
                    new[]
                    {
                        Color.white,
                        Color.white,
                        Color.white,
                        Color.white
                    });
                texture.Apply();
                sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, 2f, 2f),
                    new Vector2(0.5f, 0.5f),
                    2f);
                renderer.sprite = sprite;

                StageOneProjectileView projectile =
                    projectileObject.GetComponent<
                        StageOneProjectileView>();
                projectile.Configure(null);
                StageOneProjectileCardEffectVisual visual =
                    projectileObject.AddComponent<
                        StageOneProjectileCardEffectVisual>();
                visual.Configure(projectile, renderer);
                visual.SetVisualFlags(
                    ProjectileEffectVisualFlags.Airborne |
                    ProjectileEffectVisualFlags.Curse);
                visual.RefreshPresentation();

                Assert.That(
                    visual.DominantEffectId,
                    Is.EqualTo("airborne"));
                Assert.That(visual.OverlayVisible, Is.True);
                Assert.That(visual.TrailEmitting, Is.True);
                Assert.That(visual.AirborneLift, Is.GreaterThan(0f));

                visual.PrepareForAuthoritativeSnapshot();
                projectileObject.transform.position =
                    new Vector3(4f, 5f, 0f);
                visual.RefreshPresentation();
                Assert.That(
                    projectileObject.transform.position.y,
                    Is.GreaterThan(5f));

                visual.SetVisualFlags(
                    ProjectileEffectVisualFlags.None);
                visual.RefreshPresentation();

                Assert.That(visual.OverlayVisible, Is.False);
                Assert.That(visual.TrailEmitting, Is.False);
                Assert.That(visual.AirborneLift, Is.Zero);
                Assert.That(
                    projectileObject.transform.position.y,
                    Is.EqualTo(5f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(projectileObject);
                if (sprite != null)
                {
                    Object.DestroyImmediate(sprite);
                }

                if (texture != null)
                {
                    Object.DestroyImmediate(texture);
                }
            }
        }
    }
}
