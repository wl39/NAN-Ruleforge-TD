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

                yield return new WaitForSecondsRealtime(0.75f);

                Assert.That(view.ActiveEffectCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
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
