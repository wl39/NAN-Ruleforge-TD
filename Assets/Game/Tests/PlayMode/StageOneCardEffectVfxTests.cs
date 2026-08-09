using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RuleforgeTD.Battle;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace RuleforgeTD.Tests.PlayMode
{
    public sealed class StageOneCardEffectVfxTests
    {
        private static readonly string[] ExpectedCardIds =
        {
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
            "fear",
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
            "duplicate",
            "sacrifice",
            "return",
            "retrograde",
            "resonance",
            "absorb",
            "time_stop",
            "mutation",
            "execute",
            "parasite",
            "rebirth",
            "chain",
            "recursion",
            "reverse_order",
            "dual_interpretation",
            "infinite_orbit",
            "overclone",
            "forbidden_deal",
            "last_command",
            "fate_lock",
            "overload",
            "singularity",
            "phoenix_core",
            "time_rift",
            "mirror_world",
            "ouroboros"
        };

        [Test]
        public void Palette_PreservesDistinctLegacyAuthoredStyles()
        {
            Assert.That(
                StageOneCardEffectPalette.StyleCount,
                Is.EqualTo(ExpectedCardIds.Length));
            var shapes =
                new HashSet<StageOneCardEffectShape>();

            for (int i = 0; i < ExpectedCardIds.Length; i++)
            {
                Assert.That(
                    StageOneCardEffectPalette.GetStyle(i).Id,
                    Is.EqualTo(ExpectedCardIds[i]),
                    "The serialized visualStyleIndex contract uses " +
                    "the stable palette order. Reordering either side " +
                    "silently assigns another card's VFX.");
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
                "The authored phase-one palette must preserve its visual ABI; " +
                "module cards may use deterministic generated fallbacks.");
        }

        [Test]
        public void CardVisualFlags_AreSingleBitsWithoutCollisions()
        {
            var flags = new HashSet<ulong>();
            Array values =
                Enum.GetValues(
                    typeof(CardEffectVisualFlags));
            for (int i = 0; i < values.Length; i++)
            {
                CardEffectVisualFlags flag =
                    (CardEffectVisualFlags)
                        values.GetValue(i);
                if (flag == CardEffectVisualFlags.None)
                {
                    continue;
                }

                ulong raw = (ulong)flag;
                Assert.That(
                    raw & (raw - 1UL),
                    Is.EqualTo(0UL),
                    flag + " must occupy one bit.");
                Assert.That(
                    flags.Add(raw),
                    Is.True,
                    "Duplicate card VFX flag: " + flag);
            }

            Assert.That(
                flags.Count,
                Is.EqualTo(ExpectedCardIds.Length));
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

        [Test]
        public void Palette_GeneratesDeterministicFallbackForModuleCard()
        {
            StageOneCardEffectStyle first =
                StageOneCardEffectPalette.CreateGeneratedCardStyle(
                    "module_arcane_echo",
                    CardTier.Rare);
            StageOneCardEffectStyle second =
                StageOneCardEffectPalette.CreateGeneratedCardStyle(
                    "module_arcane_echo",
                    CardTier.Rare);
            StageOneCardEffectStyle other =
                StageOneCardEffectPalette.CreateGeneratedCardStyle(
                    "module_void_echo",
                    CardTier.Rare);

            Assert.That(first.Id, Is.EqualTo("module_arcane_echo"));
            Assert.That(first.Primary, Is.EqualTo(second.Primary));
            Assert.That(first.Secondary, Is.EqualTo(second.Secondary));
            Assert.That(first.Shape, Is.EqualTo(second.Shape));
            Assert.That(first.Duration, Is.GreaterThan(0f));
            Assert.That(first.Radius, Is.GreaterThan(0f));
            Assert.That(
                first.Primary != other.Primary ||
                first.Shape != other.Shape,
                Is.True,
                "Different stable IDs should not collapse to one fallback.");
            Assert.That(
                StageOneCardEffectPalette.TryGetStyle(
                    "module_arcane_event_without_alias",
                    out StageOneCardEffectStyle resolved),
                Is.True);
            Assert.That(
                resolved.Id,
                Is.EqualTo("module_arcane_event_without_alias"));
            Assert.That(
                StageOneCardEffectPalette.TryGetCardStyle(
                    "unregistered_module_card",
                    out _),
                Is.False,
                "Card lookup must not silently use the semantic-event " +
                "fallback before a merged catalog is registered.");
            Assert.That(
                StageOneCardEffectPalette.TryGetEventStyle(
                    "unregistered_module_card",
                    out StageOneCardEffectStyle eventStyle),
                Is.True);
            Assert.That(
                eventStyle.Id,
                Is.EqualTo("unregistered_module_card"));
            Assert.That(
                StageOneCardEffectPalette.TryGetStyle(
                    string.Empty,
                    out _),
                Is.False);
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
                        (ulong)CardEffectVisualFlags.Bleed,
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
            AreaIndicator_UsesGameplayRadiusAndMatchingVfxColor()
        {
            var host = new GameObject(
                "Area Indicator VFX Test",
                typeof(StageOneCardEffectVfxView));
            try
            {
                StageOneCardEffectVfxView view =
                    host.GetComponent<
                        StageOneCardEffectVfxView>();
                view.InitializeNow(16);
                const float radius = 1.5f;

                Assert.That(
                    view.PlayAreaIndicator(
                        "explode",
                        new Vector3(2f, 3f, 0f),
                        radius),
                    Is.True);
                view.SetManualPreviewTime(0.2f);

                SpriteRenderer indicator = null;
                SpriteRenderer[] renderers =
                    host.GetComponentsInChildren<
                        SpriteRenderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i].enabled &&
                        renderers[i].gameObject.name ==
                        "Pixel Area Indicator")
                    {
                        indicator = renderers[i];
                        break;
                    }
                }

                Assert.That(indicator, Is.Not.Null);
                Assert.That(
                    indicator.transform.lossyScale.x,
                    Is.EqualTo(radius * 2f).Within(0.001f));
                Assert.That(
                    view.LastPlayedAreaRadius,
                    Is.EqualTo(radius).Within(0.001f));
                Assert.That(
                    StageOneCardEffectPalette.TryGetStyle(
                        "explode",
                        out StageOneCardEffectStyle style),
                    Is.True);
                Assert.That(
                    indicator.color.r,
                    Is.EqualTo(style.Primary.r).Within(0.001f));
                Assert.That(
                    indicator.color.g,
                    Is.EqualTo(style.Primary.g).Within(0.001f));
                Assert.That(
                    indicator.color.b,
                    Is.EqualTo(style.Primary.b).Within(0.001f));
                Assert.That(
                    indicator.color.a,
                    Is.GreaterThanOrEqualTo(0.97f));
                Assert.That(
                    indicator.sprite.texture.filterMode,
                    Is.EqualTo(FilterMode.Point));
                Assert.That(
                    StageOneCardEffectVfxView
                        .AreaIndicatorResolutionMultiplier,
                    Is.EqualTo(1));
                Assert.That(
                    StageOneCardEffectVfxView
                        .AreaIndicatorTextureSize,
                    Is.EqualTo(129),
                    "Every area effect must share the very thin ring.");
                Assert.That(
                    indicator.sprite.texture.width,
                    Is.EqualTo(
                        StageOneCardEffectVfxView
                            .AreaIndicatorTextureSize));
                Assert.That(
                    indicator.sprite.texture.height,
                    Is.EqualTo(
                        StageOneCardEffectVfxView
                            .AreaIndicatorTextureSize));
                Assert.That(
                    StageOneCardEffectVfxView
                        .AreaIndicatorBorderPixels,
                    Is.EqualTo(1),
                    "The gameplay boundary must remain a thin outline.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator
            TransientVfx_FreezesAndResumesWithWorldTimeScale()
        {
            float previousTimeScale = Time.timeScale;
            var host = new GameObject(
                "Paused Card VFX Test",
                typeof(StageOneCardEffectVfxView));
            try
            {
                Time.timeScale = 1f;
                StageOneCardEffectVfxView view =
                    host.GetComponent<StageOneCardEffectVfxView>();
                view.InitializeNow(16);
                Assert.That(
                    view.PlayAreaIndicator(
                        "explode",
                        Vector3.zero,
                        1.5f),
                    Is.True);
                yield return null;

                Vector3[] beforePause =
                    GetActivePixelMesh(host).vertices;
                Time.timeScale = 0f;
                yield return null;
                yield return null;
                Vector3[] whilePaused =
                    GetActivePixelMesh(host).vertices;
                Assert.That(
                    HasDifferentVertex(beforePause, whilePaused),
                    Is.False,
                    "Card VFX must not advance while gameplay is paused.");

                Time.timeScale = 1f;
                yield return new WaitForSeconds(0.08f);
                Vector3[] afterResume =
                    GetActivePixelMesh(host).vertices;
                Assert.That(
                    HasDifferentVertex(whilePaused, afterResume),
                    Is.True,
                    "Card VFX must resume from the frozen frame.");
            }
            finally
            {
                Time.timeScale = previousTimeScale;
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void
            AreaAnimations_StayInsideGameplayRadiusAndPixelBudget()
        {
            var host = new GameObject(
                "Semantic Area VFX Test",
                typeof(StageOneCardEffectVfxView));
            try
            {
                StageOneCardEffectVfxView view =
                    host.GetComponent<
                        StageOneCardEffectVfxView>();
                view.InitializeNow(16);
                const float radius = 1.5f;
                string[] areaEffectIds =
                {
                    "explode",
                    "poison",
                    "burn",
                    "pulse",
                    "airborne_land",
                    "freeze_shard",
                    "bind_pulse",
                    "legendary_overload",
                    "mythic_singularity",
                    "legendary_last_command",
                    "rare_sacrifice_enemy"
                };

                for (int i = 0; i < areaEffectIds.Length; i++)
                {
                    string effectId = areaEffectIds[i];
                    view.StopAll();
                    Assert.That(
                        view.PlayAreaIndicator(
                            effectId,
                            Vector3.zero,
                            radius),
                        Is.True,
                        effectId);
                    view.SetManualPreviewTime(0.28f);

                    Assert.That(
                        view.ActivePixelBlockCount,
                        Is.GreaterThan(0),
                        effectId +
                        " must draw a semantic animation inside its ring.");
                    Assert.That(
                        view.ActivePixelBlockCount,
                        Is.LessThanOrEqualTo(
                            StageOneCardEffectVfxView
                                .MaximumPixelBlocks),
                        effectId +
                        " exceeded the bounded WebGL pixel budget.");
                    Bounds bounds = GetActivePixelBounds(host);
                    Assert.That(
                        Mathf.Max(
                            bounds.extents.x,
                            bounds.extents.y),
                        Is.LessThanOrEqualTo(
                            radius +
                            StageOneCardEffectVfxView.PixelWorldSize),
                        effectId +
                        " rendered outside the authoritative radius.");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void
            ExplosionMushroom_GrowsCloseToAuthoritativeAreaRing()
        {
            var host = new GameObject(
                "Large Explosion Mushroom VFX Test",
                typeof(StageOneCardEffectVfxView));
            try
            {
                StageOneCardEffectVfxView view =
                    host.GetComponent<StageOneCardEffectVfxView>();
                view.InitializeNow(16);
                const float radius = 1.5f;
                Assert.That(
                    view.PlayAreaIndicator(
                        "explode",
                        Vector3.zero,
                        radius),
                    Is.True);

                view.SetManualPreviewTime(0.46f);
                Bounds bounds = GetActivePixelBounds(host);
                Assert.That(
                    bounds.size.x,
                    Is.GreaterThanOrEqualTo(radius * 1.68f),
                    "The rolled mushroom cap should fill most of the ring.");
                Assert.That(
                    bounds.size.y,
                    Is.GreaterThanOrEqualTo(radius * 1.16f),
                    "The stem and cap should use the ring's vertical space.");
                Assert.That(
                    Mathf.Max(bounds.extents.x, bounds.extents.y),
                    Is.LessThanOrEqualTo(
                        radius +
                        StageOneCardEffectVfxView.PixelWorldSize),
                    "The larger cloud must remain inside its gameplay ring.");
                Assert.That(
                    view.ActivePixelBlockCount,
                    Is.LessThanOrEqualTo(
                        StageOneCardEffectVfxView.MaximumPixelBlocks));
            }
            finally
            {
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
            const ulong expectedFlags =
                (ulong)(
                    CardEffectVisualFlags.Burn |
                    CardEffectVisualFlags.Poison);
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

        [Test]
        public void
            PresentationEvent_PreservesAuthoritativeAreaCenter()
        {
            SimPosition center =
                SimPosition.FromMilliUnits(1250, -500);
            var item = new SimulationPresentationEvent(
                13,
                PresentationEventType.AreaEffectTriggered,
                7,
                4,
                1500,
                "explode",
                effectPosition: center,
                hasEffectPosition: true);

            Assert.That(item.HasEffectPosition, Is.True);
            Assert.That(item.EffectPosition, Is.EqualTo(center));
            Assert.That(item.Value, Is.EqualTo(1500));
        }

        private static bool HasDifferentVertex(
            Vector3[] first,
            Vector3[] second)
        {
            if (first.Length != second.Length)
            {
                return true;
            }

            for (int i = 0; i < first.Length; i++)
            {
                if ((first[i] - second[i]).sqrMagnitude > 0.000001f)
                {
                    return true;
                }
            }

            return false;
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
                    CardEffectVisualFlags.Curse);
                yield return null;

                Assert.That(visual.IsCursed, Is.True);
                Assert.That(
                    visual.DominantEffectId,
                    Is.EqualTo("curse"));
                Assert.That(
                    visual.DominantColor.b,
                    Is.GreaterThan(visual.DominantColor.g));
                Assert.That(visual.TintOverlayVisible, Is.True);
                Assert.That(
                    enemyObject.transform.Find(
                        "Card Effect Aura"),
                    Is.Null,
                    "Persistent status presentation must not recreate " +
                    "the floating ring.");
                Assert.That(
                    enemyObject.transform.Find(
                        "Card Effect Glyph"),
                    Is.Null,
                    "Persistent status presentation must not recreate " +
                    "the rotating rune/star.");

                visual.SetVisualFlags(
                    CardEffectVisualFlags.Bind);
                yield return null;

                Assert.That(visual.IsBound, Is.True);
                Assert.That(
                    visual.DominantEffectId,
                    Is.EqualTo("bind"));

                Vector3 groundPosition =
                    enemyObject.transform.position;
                visual.SetVisualFlags(
                    CardEffectVisualFlags.Airborne,
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
                    CardEffectVisualFlags.Airborne,
                    29,
                    77);
                yield return null;

                Assert.That(
                    enemyObject.transform.position.y,
                    Is.GreaterThan(
                        nextGroundPosition.y + 0.1f));

                visual.SetVisualFlags(
                    CardEffectVisualFlags.None);
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
                    CardEffectVisualFlags.Airborne |
                    CardEffectVisualFlags.Curse);
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
                    CardEffectVisualFlags.None);
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

        [Test]
        public void EnemyStatusIcons_PrioritizeThreeAndUsePaletteColors()
        {
            var enemyObject = new GameObject(
                "Status Icon Stack Enemy",
                typeof(SpriteRenderer));
            Texture2D texture = null;
            Sprite sprite = null;
            try
            {
                SpriteRenderer renderer =
                    enemyObject.GetComponent<SpriteRenderer>();
                renderer.sortingOrder = 20;
                texture = new Texture2D(
                    4,
                    4,
                    TextureFormat.RGBA32,
                    false);
                var pixels = new Color[16];
                for (int index = 0;
                     index < pixels.Length;
                     index++)
                {
                    pixels[index] = Color.white;
                }

                texture.SetPixels(pixels);
                texture.Apply();
                sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, 4f, 4f),
                    new Vector2(0.5f, 0.5f),
                    4f);
                renderer.sprite = sprite;

                StageOneEnemyStatusIconStackView icons =
                    enemyObject.AddComponent<
                        StageOneEnemyStatusIconStackView>();
                const float healthBarTopLocalY = 1.25f;
                icons.Configure(
                    renderer,
                    healthBarTopLocalY);
                icons.ApplyStatuses(
                    new[]
                    {
                        Status(1, StatusType.Burn, 1),
                        Status(2, StatusType.Poison, 2),
                        Status(3, StatusType.Slow, 3),
                        Status(4, StatusType.Mark),
                        Status(5, StatusType.Curse),
                        Status(6, StatusType.Bind),
                        Status(7, StatusType.Shock)
                    });

                Assert.That(
                    icons.ActiveIconCount,
                    Is.EqualTo(3));
                Assert.That(
                    icons.ActiveRowCount,
                    Is.EqualTo(1));
                Assert.That(
                    icons.GetIconLocalPosition(0).y,
                    Is.EqualTo(
                        healthBarTopLocalY +
                        StageOneEnemyStatusIconStackView
                            .HealthBarClearance +
                        StageOneEnemyStatusIconStackView
                            .IconSize * 0.5f)
                        .Within(0.001f));
                Assert.That(
                    icons.GetIconLocalPosition(0).y,
                    Is.EqualTo(
                        icons.GetIconLocalPosition(2).y)
                        .Within(0.001f));
                Assert.That(
                    icons.GetIconLocalPosition(0).x,
                    Is.LessThan(
                        icons.GetIconLocalPosition(1).x));
                Assert.That(
                    icons.GetIconLocalPosition(1).x,
                    Is.LessThan(
                        icons.GetIconLocalPosition(2).x));
                Assert.That(
                    icons.GetIconStackLabel(0),
                    Is.Empty);
                Assert.That(
                    icons.GetIconStackLabel(1),
                    Is.Empty);
                Assert.That(
                    icons.GetIconStackLabel(2),
                    Is.Empty);
                Assert.That(
                    icons.GetIconEffectId(0),
                    Is.EqualTo("bind"));
                Assert.That(
                    icons.GetIconEffectId(1),
                    Is.EqualTo("mark"));
                Assert.That(
                    icons.GetIconEffectId(2),
                    Is.EqualTo("curse"));

                Assert.That(
                    StageOneCardEffectPalette.TryGetStyle(
                        "bind",
                        out StageOneCardEffectStyle bindStyle),
                    Is.True);
                AssertColorRgb(
                    icons.GetIconPrimaryColor(0),
                    bindStyle.Primary);
                AssertColorRgb(
                    icons.GetIconSecondaryColor(0),
                    bindStyle.Secondary);
                Transform fill =
                    enemyObject.transform.Find(
                        "Enemy Status Icon Stack/" +
                        "Status Icon 0/Fill");
                Assert.That(fill, Is.Not.Null);
                Assert.That(
                    fill.localScale.x,
                    Is.EqualTo(fill.localScale.y)
                        .Within(0.001f),
                    "Status icons must be square.");
                Transform accent =
                    enemyObject.transform.Find(
                        "Enemy Status Icon Stack/" +
                        "Status Icon 0/Accent");
                Assert.That(
                    accent,
                    Is.Null,
                    "Status icons must not render the old " +
                    "underscore-like accent bar.");
                Transform stackTextTransform =
                    enemyObject.transform.Find(
                        "Enemy Status Icon Stack/" +
                        "Status Icon 1/Stack Count");
                Assert.That(
                    stackTextTransform,
                    Is.Not.Null);
                TextMesh stackText =
                    stackTextTransform
                        .GetComponent<TextMesh>();
                Assert.That(stackText, Is.Not.Null);
                Assert.That(
                    stackText.characterSize,
                    Is.EqualTo(
                        StageOneEnemyStatusIconStackView
                            .StackLabelCharacterSize)
                        .Within(0.001f));
                Assert.That(
                    stackText.fontSize,
                    Is.EqualTo(
                        StageOneEnemyStatusIconStackView
                            .StackLabelFontSize));
                Assert.That(
                    stackText.fontStyle,
                    Is.EqualTo(FontStyle.Normal));
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
        public void EnemyStatusIcons_DeduplicateVisualsAndHideRecoveryBuffs()
        {
            var enemyObject = new GameObject(
                "Status Icon Deduplication Enemy",
                typeof(SpriteRenderer));
            try
            {
                StageOneEnemyStatusIconStackView icons =
                    enemyObject.AddComponent<
                        StageOneEnemyStatusIconStackView>();
                icons.Configure(
                    enemyObject.GetComponent<SpriteRenderer>());
                icons.ApplyStatuses(
                    new[]
                    {
                        Status(1, StatusType.Burn, 2),
                        Status(2, StatusType.Burn),
                        Status(3, StatusType.Chill, 4),
                        Status(4, StatusType.Frozen, 5),
                        Status(5, StatusType.FreezeImmunity),
                        Status(6, StatusType.FearHaste)
                    });

                Assert.That(
                    icons.ActiveIconCount,
                    Is.EqualTo(2));
                Assert.That(
                    icons.GetIconEffectId(0),
                    Is.EqualTo("freeze"));
                Assert.That(
                    icons.GetIconEffectId(1),
                    Is.EqualTo("burn"));
                Assert.That(
                    icons.GetIconStackLabel(0),
                    Is.EqualTo("9+"));
                Assert.That(
                    icons.GetIconDisplayedStackCount(1),
                    Is.EqualTo(3));
                Assert.That(
                    icons.GetIconStackLabel(1),
                    Is.EqualTo("x3"));
            }
            finally
            {
                Object.DestroyImmediate(enemyObject);
            }
        }

        private static StatusSnapshot Status(
            int instanceId,
            StatusType type,
            int stacks = 1)
        {
            return new StatusSnapshot(
                instanceId,
                type,
                -1,
                -1,
                CardId.Invalid,
                stacks,
                1,
                120,
                1,
                0,
                0);
        }

        private static void AssertColorRgb(
            Color actual,
            Color expected)
        {
            Assert.That(
                actual.r,
                Is.EqualTo(expected.r).Within(0.001f));
            Assert.That(
                actual.g,
                Is.EqualTo(expected.g).Within(0.001f));
            Assert.That(
                actual.b,
                Is.EqualTo(expected.b).Within(0.001f));
        }
    }
}
