using NUnit.Framework;
using RuleforgeTD.StatusEffects;
using UnityEngine;

namespace RuleforgeTD.Tests.PlayMode
{
    public sealed class EnemyStatusVisualViewTests
    {
        private GameObject enemyObject;
        private SpriteRenderer enemyRenderer;
        private EnemyStatusVisualView view;
        private Texture2D enemyTexture;
        private Sprite enemySprite;

        [SetUp]
        public void SetUp()
        {
            enemyObject = new GameObject("Status Visual Test Enemy");
            enemyRenderer =
                enemyObject.AddComponent<SpriteRenderer>();
            enemyTexture =
                new Texture2D(
                    2,
                    2,
                    TextureFormat.RGBA32,
                    false);
            enemyTexture.SetPixels(
                new[]
                {
                    Color.red,
                    Color.blue,
                    Color.green,
                    Color.yellow
                });
            enemyTexture.Apply();
            enemySprite = Sprite.Create(
                enemyTexture,
                new Rect(0f, 0f, 2f, 2f),
                new Vector2(0.5f, 0.5f),
                2f);
            enemyRenderer.sprite = enemySprite;
            enemyRenderer.color = Color.white;
            enemyRenderer.sortingOrder = 20;
            view =
                enemyObject.AddComponent<EnemyStatusVisualView>();
            view.Configure(enemyRenderer);
        }

        [TearDown]
        public void TearDown()
        {
            if (enemyObject != null)
            {
                Object.DestroyImmediate(enemyObject);
            }

            if (enemySprite != null)
            {
                Object.DestroyImmediate(enemySprite);
            }

            if (enemyTexture != null)
            {
                Object.DestroyImmediate(enemyTexture);
            }
        }

        [Test]
        public void Burn_UsesOrangeTintAndBoundedEmbers()
        {
            view.SetStatusStacks(3, 0);

            Assert.That(view.IsBurning, Is.True);
            Assert.That(view.IsPoisoned, Is.False);
            Assert.That(view.BurnStacks, Is.EqualTo(3));
            Assert.That(view.BurnEmitterPlaying, Is.True);
            Assert.That(view.PoisonEmitterPlaying, Is.False);
            AssertColor(
                view.CurrentTint,
                ExpectedTint(
                    Color.white,
                    EnemyStatusVisualView.BurnTint));

            ParticleSystem burnEmitter =
                FindEmitter("Burn Status Embers");
            burnEmitter.Simulate(
                5f,
                true,
                true,
                true);

            Assert.That(
                burnEmitter.particleCount,
                Is.InRange(
                    1,
                    EnemyStatusVisualView.BurnParticleLimit));
            Assert.That(
                burnEmitter.main.maxParticles,
                Is.EqualTo(
                    EnemyStatusVisualView.BurnParticleLimit));
        }

        [Test]
        public void Poison_UsesGreenTintAndBoundedBubbles()
        {
            view.SetStatusStacks(0, 4);

            Assert.That(view.IsBurning, Is.False);
            Assert.That(view.IsPoisoned, Is.True);
            Assert.That(view.PoisonStacks, Is.EqualTo(4));
            Assert.That(view.BurnEmitterPlaying, Is.False);
            Assert.That(view.PoisonEmitterPlaying, Is.True);
            AssertColor(
                view.CurrentTint,
                ExpectedTint(
                    Color.white,
                    EnemyStatusVisualView.PoisonTint));

            ParticleSystem poisonEmitter =
                FindEmitter("Poison Status Bubbles");
            poisonEmitter.Simulate(
                5f,
                true,
                true,
                true);

            Assert.That(
                poisonEmitter.particleCount,
                Is.InRange(
                    1,
                    EnemyStatusVisualView.PoisonParticleLimit));
            Assert.That(
                poisonEmitter.main.maxParticles,
                Is.EqualTo(
                    EnemyStatusVisualView.PoisonParticleLimit));
        }

        [Test]
        public void BurnAndPoison_BlendTintAndPlayBothEmitters()
        {
            const int burnStacks = 2;
            const int poisonStacks = 5;
            view.SetStatusStacks(
                burnStacks,
                poisonStacks);

            float poisonWeight =
                poisonStacks /
                (float)(burnStacks + poisonStacks);
            Color combinedStatusTint = Color.Lerp(
                EnemyStatusVisualView.BurnTint,
                EnemyStatusVisualView.PoisonTint,
                poisonWeight);

            Assert.That(view.IsBurning, Is.True);
            Assert.That(view.IsPoisoned, Is.True);
            Assert.That(view.BurnEmitterPlaying, Is.True);
            Assert.That(view.PoisonEmitterPlaying, Is.True);
            AssertColor(
                view.CurrentTint,
                ExpectedTint(
                    Color.white,
                    combinedStatusTint));

            ParticleSystem burnEmitter =
                FindEmitter("Burn Status Embers");
            ParticleSystem poisonEmitter =
                FindEmitter("Poison Status Bubbles");
            ParticleSystemRenderer burnRenderer =
                burnEmitter.GetComponent<ParticleSystemRenderer>();
            ParticleSystemRenderer poisonRenderer =
                poisonEmitter.GetComponent<ParticleSystemRenderer>();

            Assert.That(
                burnRenderer.sharedMaterial,
                Is.Not.Null);
            Assert.That(
                poisonRenderer.sharedMaterial,
                Is.Not.Null);
            Assert.That(
                burnRenderer.sortingOrder,
                Is.EqualTo(enemyRenderer.sortingOrder + 1));
            Assert.That(
                poisonRenderer.sortingOrder,
                Is.EqualTo(enemyRenderer.sortingOrder + 1));
        }

        [Test]
        public void ImpactFlash_PreservesAndRestoresStatusTint()
        {
            view.SetStatusStacks(3, 0);
            Color burnTint = view.CurrentTint;

            view.SetImpactFlashStrength(1f);

            Assert.That(view.ImpactFlashStrength, Is.EqualTo(1f));
            AssertColor(view.CurrentTint, Color.white);
            Assert.That(view.IsBurning, Is.True);
            Assert.That(view.IsImpactFlashVisible, Is.True);
            Assert.That(
                view.ImpactFlashOverlayAlpha,
                Is.EqualTo(1f).Within(0.001f));
            SpriteRenderer flashRenderer =
                enemyObject.transform
                    .Find("Enemy Impact Flash")
                    .GetComponent<SpriteRenderer>();
            Assert.That(
                flashRenderer.sharedMaterial.shader.name,
                Is.EqualTo("RuleforgeTD/EnemyHitFlash"));
            Assert.That(
                flashRenderer.sortingOrder,
                Is.EqualTo(enemyRenderer.sortingOrder + 2));

            view.SetImpactFlashStrength(0f);

            Assert.That(view.ImpactFlashStrength, Is.Zero);
            AssertColor(view.CurrentTint, burnTint);
            Assert.That(view.IsBurning, Is.True);
            Assert.That(view.IsImpactFlashVisible, Is.False);
        }

        [Test]
        public void ResetVisuals_ClearsTintParticlesAndPooledState()
        {
            Color baseColor =
                new Color(0.9f, 0.85f, 0.8f, 0.75f);
            view.ResetVisuals();
            enemyRenderer.color = baseColor;
            view.Configure(enemyRenderer);
            view.SetStatusStacks(6, 7);

            Assert.That(view.BurnParticleCount, Is.GreaterThan(0));
            Assert.That(view.PoisonParticleCount, Is.GreaterThan(0));

            view.ResetVisuals();

            Assert.That(view.BurnStacks, Is.Zero);
            Assert.That(view.PoisonStacks, Is.Zero);
            Assert.That(view.IsBurning, Is.False);
            Assert.That(view.IsPoisoned, Is.False);
            Assert.That(view.BurnEmitterPlaying, Is.False);
            Assert.That(view.PoisonEmitterPlaying, Is.False);
            Assert.That(view.BurnParticleCount, Is.Zero);
            Assert.That(view.PoisonParticleCount, Is.Zero);
            Assert.That(view.ImpactFlashStrength, Is.Zero);
            AssertColor(view.CurrentTint, baseColor);

            view.SetStatusStacks(0, 1);
            Assert.That(view.IsPoisoned, Is.True);
            Assert.That(view.IsBurning, Is.False);
            Assert.That(view.BurnParticleCount, Is.Zero);
        }

        private ParticleSystem FindEmitter(string objectName)
        {
            ParticleSystem[] emitters =
                enemyObject.GetComponentsInChildren<ParticleSystem>(
                    true);
            for (int i = 0; i < emitters.Length; i++)
            {
                if (emitters[i].name == objectName)
                {
                    return emitters[i];
                }
            }

            Assert.Fail("Missing particle emitter: " + objectName);
            return null;
        }

        private static Color ExpectedTint(
            Color baseColor,
            Color statusTint)
        {
            Color multiplied = new Color(
                baseColor.r * statusTint.r,
                baseColor.g * statusTint.g,
                baseColor.b * statusTint.b,
                baseColor.a);
            Color result = Color.Lerp(
                baseColor,
                multiplied,
                EnemyStatusVisualView.TintStrength);
            result.a = baseColor.a;
            return result;
        }

        private static void AssertColor(
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
            Assert.That(
                actual.a,
                Is.EqualTo(expected.a).Within(0.001f));
        }
    }
}
