using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Simulation;
using UnityEngine;

namespace RuleforgeTD.StatusEffects
{
    /// <summary>
    /// Renders status information owned by <see cref="GameSimulation"/>.
    /// This component never advances durations or applies damage; callers feed
    /// it immutable simulation snapshots during enemy-view reconciliation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyStatusVisualView : MonoBehaviour
    {
        public const int BurnParticleLimit = 8;
        public const int PoisonParticleLimit = 6;
        public const float TintStrength = 0.72f;

        public static readonly Color BurnTint =
            new Color32(255, 116, 38, 255);
        public static readonly Color PoisonTint =
            new Color32(86, 220, 72, 255);

        private const string BurnEmitterName = "Burn Status Embers";
        private const string PoisonEmitterName = "Poison Status Bubbles";
        private const string ImpactFlashOverlayName =
            "Enemy Impact Flash";

        [SerializeField]
        private SpriteRenderer targetRenderer;

        private ParticleSystem burnEmitter;
        private ParticleSystem poisonEmitter;
        private ParticleSystemRenderer burnEmitterRenderer;
        private ParticleSystemRenderer poisonEmitterRenderer;
        private SpriteRenderer impactFlashOverlay;
        private Color baseColor = Color.white;
        private float impactFlashStrength;
        private int burnStacks;
        private int poisonStacks;
        private int cachedSortingLayerId = int.MinValue;
        private int cachedSortingOrder = int.MinValue;
        private bool configured;

        public int BurnStacks => burnStacks;
        public int PoisonStacks => poisonStacks;
        public bool IsBurning => burnStacks > 0;
        public bool IsPoisoned => poisonStacks > 0;
        public bool BurnEmitterPlaying =>
            burnEmitter != null && burnEmitter.isEmitting;
        public bool PoisonEmitterPlaying =>
            poisonEmitter != null && poisonEmitter.isEmitting;
        public int BurnParticleCount =>
            burnEmitter == null ? 0 : burnEmitter.particleCount;
        public int PoisonParticleCount =>
            poisonEmitter == null ? 0 : poisonEmitter.particleCount;
        public Color CurrentTint =>
            targetRenderer == null ? baseColor : targetRenderer.color;
        public float ImpactFlashStrength => impactFlashStrength;
        public bool IsImpactFlashVisible =>
            impactFlashOverlay != null &&
            impactFlashOverlay.enabled;
        public float ImpactFlashOverlayAlpha =>
            impactFlashOverlay == null
                ? 0f
                : impactFlashOverlay.color.a;

        private void Awake()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<SpriteRenderer>();
            }

            if (targetRenderer != null)
            {
                baseColor = targetRenderer.color;
                configured = true;
            }

            EnsureEmitters();
            EnsureImpactFlashOverlay();
            RefreshVisuals(0, 0);
        }

        private void OnEnable()
        {
            EnsureEmitters();
            EnsureImpactFlashOverlay();
            RefreshVisuals(0, 0);
        }

        private void LateUpdate()
        {
            SynchronizeSorting();
            SynchronizeImpactFlashOverlay();
        }

        private void OnDisable()
        {
            ResetVisuals();
        }

        /// <summary>
        /// Binds this presentation to the enemy's body renderer.
        /// Rebinding restores the previous renderer before taking ownership.
        /// </summary>
        public void Configure(SpriteRenderer enemyRenderer)
        {
            SpriteRenderer resolvedRenderer =
                enemyRenderer != null
                    ? enemyRenderer
                    : GetComponent<SpriteRenderer>();

            if (configured && targetRenderer == resolvedRenderer)
            {
                if (!IsBurning && !IsPoisoned &&
                    targetRenderer != null)
                {
                    baseColor = targetRenderer.color;
                }

                EnsureEmitters();
                EnsureImpactFlashOverlay();
                RefreshVisuals(burnStacks, poisonStacks);
                return;
            }

            if (configured && targetRenderer != null)
            {
                targetRenderer.color = baseColor;
            }

            targetRenderer = resolvedRenderer;
            configured = targetRenderer != null;
            baseColor = configured
                ? targetRenderer.color
                : Color.white;
            cachedSortingLayerId = int.MinValue;
            cachedSortingOrder = int.MinValue;

            EnsureEmitters();
            EnsureImpactFlashOverlay();
            RefreshVisuals(burnStacks, poisonStacks);
        }

        /// <summary>
        /// Applies the current visual state from an enemy simulation snapshot.
        /// Dead snapshots immediately clear presentation state for pool reuse.
        /// </summary>
        public void ApplySnapshot(in EnemySnapshot snapshot)
        {
            if (!snapshot.Alive)
            {
                ResetVisuals();
                return;
            }

            ApplySnapshot(snapshot.StatusDetails);
        }

        /// <summary>
        /// Scans detailed status snapshots without allocation or LINQ.
        /// Multiple source instances of the same status contribute their
        /// stacks to the presentation intensity.
        /// </summary>
        public void ApplySnapshot(StatusSnapshot[] statuses)
        {
            int nextBurnStacks = 0;
            int nextPoisonStacks = 0;

            if (statuses != null)
            {
                for (int i = 0; i < statuses.Length; i++)
                {
                    StatusSnapshot status = statuses[i];
                    if (status.RemainingTicks <= 0 || status.Stacks <= 0)
                    {
                        continue;
                    }

                    if (status.Type == StatusType.Burn)
                    {
                        nextBurnStacks = SaturatingAdd(
                            nextBurnStacks,
                            status.Stacks);
                    }
                    else if (status.Type == StatusType.Poison)
                    {
                        nextPoisonStacks = SaturatingAdd(
                            nextPoisonStacks,
                            status.Stacks);
                    }
                }
            }

            SetStatusStacks(nextBurnStacks, nextPoisonStacks);
        }

        /// <summary>
        /// Direct presentation seam for view tests and non-simulation previews.
        /// It does not create or mutate gameplay status state.
        /// </summary>
        public void SetStatusStacks(
            int activeBurnStacks,
            int activePoisonStacks)
        {
            int nextBurnStacks = Mathf.Max(0, activeBurnStacks);
            int nextPoisonStacks = Mathf.Max(0, activePoisonStacks);
            if (nextBurnStacks == burnStacks &&
                nextPoisonStacks == poisonStacks)
            {
                SynchronizeSorting();
                return;
            }

            int previousBurnStacks = burnStacks;
            int previousPoisonStacks = poisonStacks;
            burnStacks = nextBurnStacks;
            poisonStacks = nextPoisonStacks;
            RefreshVisuals(
                previousBurnStacks,
                previousPoisonStacks);
        }

        /// <summary>
        /// Overlays a white impact flash without discarding the underlying
        /// burn/poison tint. The enemy view owns the short presentation timer;
        /// status state remains sourced exclusively from simulation snapshots.
        /// </summary>
        public void SetImpactFlashStrength(float strength)
        {
            float nextStrength = Mathf.Clamp01(strength);
            if (Mathf.Approximately(
                    impactFlashStrength,
                    nextStrength))
            {
                return;
            }

            impactFlashStrength = nextStrength;
            if (configured && targetRenderer != null)
            {
                targetRenderer.color = ResolveTint();
            }

            SynchronizeImpactFlashOverlay();
        }

        /// <summary>
        /// Clears tint and particles so a pooled enemy cannot inherit visuals.
        /// </summary>
        public void ResetVisuals()
        {
            int previousBurnStacks = burnStacks;
            int previousPoisonStacks = poisonStacks;
            burnStacks = 0;
            poisonStacks = 0;
            impactFlashStrength = 0f;
            RefreshVisuals(
                previousBurnStacks,
                previousPoisonStacks);
        }

        private void RefreshVisuals(
            int previousBurnStacks,
            int previousPoisonStacks)
        {
            EnsureEmitters();
            EnsureImpactFlashOverlay();

            if (configured && targetRenderer != null)
            {
                targetRenderer.color = ResolveTint();
            }

            UpdateEmitter(
                burnEmitter,
                burnStacks,
                previousBurnStacks,
                BurnParticleLimit);
            UpdateEmitter(
                poisonEmitter,
                poisonStacks,
                previousPoisonStacks,
                PoisonParticleLimit);
            SynchronizeSorting();
            SynchronizeImpactFlashOverlay();
        }

        private void EnsureImpactFlashOverlay()
        {
            if (targetRenderer == null)
            {
                return;
            }

            if (impactFlashOverlay == null)
            {
                Transform existing =
                    targetRenderer.transform.Find(
                        ImpactFlashOverlayName);
                GameObject overlayObject;
                if (existing != null)
                {
                    overlayObject = existing.gameObject;
                    impactFlashOverlay =
                        overlayObject.GetComponent<SpriteRenderer>();
                }
                else
                {
                    overlayObject =
                        new GameObject(ImpactFlashOverlayName);
                    overlayObject.transform.SetParent(
                        targetRenderer.transform,
                        false);
                    impactFlashOverlay =
                        overlayObject.AddComponent<SpriteRenderer>();
                }

                if (impactFlashOverlay == null)
                {
                    return;
                }

                Material flashMaterial =
                    SharedResources.ImpactFlashMaterial;
                if (flashMaterial != null)
                {
                    impactFlashOverlay.sharedMaterial =
                        flashMaterial;
                }
            }

            Transform overlayTransform =
                impactFlashOverlay.transform;
            if (overlayTransform.parent !=
                targetRenderer.transform)
            {
                overlayTransform.SetParent(
                    targetRenderer.transform,
                    false);
            }

            overlayTransform.localPosition = Vector3.zero;
            overlayTransform.localRotation = Quaternion.identity;
            overlayTransform.localScale = Vector3.one;
            impactFlashOverlay.enabled = false;
        }

        private void SynchronizeImpactFlashOverlay()
        {
            if (targetRenderer == null)
            {
                if (impactFlashOverlay != null)
                {
                    impactFlashOverlay.enabled = false;
                }

                return;
            }

            EnsureImpactFlashOverlay();
            if (impactFlashOverlay == null)
            {
                return;
            }

            impactFlashOverlay.sprite = targetRenderer.sprite;
            impactFlashOverlay.flipX = targetRenderer.flipX;
            impactFlashOverlay.flipY = targetRenderer.flipY;
            impactFlashOverlay.drawMode = targetRenderer.drawMode;
            impactFlashOverlay.size = targetRenderer.size;
            impactFlashOverlay.tileMode = targetRenderer.tileMode;
            impactFlashOverlay.maskInteraction =
                targetRenderer.maskInteraction;
            impactFlashOverlay.spriteSortPoint =
                targetRenderer.spriteSortPoint;
            impactFlashOverlay.sortingLayerID =
                targetRenderer.sortingLayerID;
            impactFlashOverlay.sortingOrder =
                targetRenderer.sortingOrder + 2;

            float visibleStrength = Mathf.SmoothStep(
                0f,
                1f,
                impactFlashStrength);
            impactFlashOverlay.color =
                new Color(1f, 1f, 1f, visibleStrength);
            impactFlashOverlay.enabled =
                visibleStrength > 0.001f &&
                targetRenderer.enabled &&
                targetRenderer.sprite != null;
        }

        private Color ResolveTint()
        {
            Color statusColor;
            if (!IsBurning && !IsPoisoned)
            {
                statusColor = baseColor;
            }
            else
            {
                Color statusTint;
                if (IsBurning && IsPoisoned)
                {
                    float poisonWeight =
                        poisonStacks /
                        ((float)burnStacks + poisonStacks);
                    statusTint = Color.Lerp(
                        BurnTint,
                        PoisonTint,
                        poisonWeight);
                }
                else
                {
                    statusTint =
                        IsBurning ? BurnTint : PoisonTint;
                }

                Color multiplied = new Color(
                    baseColor.r * statusTint.r,
                    baseColor.g * statusTint.g,
                    baseColor.b * statusTint.b,
                    baseColor.a);
                statusColor = Color.Lerp(
                    baseColor,
                    multiplied,
                    TintStrength);
            }

            Color result = Color.Lerp(
                statusColor,
                Color.white,
                impactFlashStrength);
            result.a = baseColor.a;
            return result;
        }

        private void EnsureEmitters()
        {
            if (burnEmitter == null)
            {
                CreateEmitter(
                    BurnEmitterName,
                    true,
                    BurnParticleLimit,
                    SharedResources.BurnMaterial,
                    out burnEmitter,
                    out burnEmitterRenderer);
            }

            if (poisonEmitter == null)
            {
                CreateEmitter(
                    PoisonEmitterName,
                    false,
                    PoisonParticleLimit,
                    SharedResources.PoisonMaterial,
                    out poisonEmitter,
                    out poisonEmitterRenderer);
            }
        }

        private void CreateEmitter(
            string emitterName,
            bool isBurn,
            int particleLimit,
            Material sharedMaterial,
            out ParticleSystem emitter,
            out ParticleSystemRenderer emitterRenderer)
        {
            Transform existing = transform.Find(emitterName);
            GameObject emitterObject;
            if (existing != null)
            {
                emitterObject = existing.gameObject;
                emitter = emitterObject.GetComponent<ParticleSystem>();
            }
            else
            {
                emitterObject = new GameObject(emitterName);
                emitterObject.transform.SetParent(transform, false);
                emitter = emitterObject.AddComponent<ParticleSystem>();
            }

            emitter.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            emitterObject.transform.localPosition =
                isBurn
                    ? new Vector3(0f, 0.18f, -0.01f)
                    : new Vector3(0f, 0.12f, -0.01f);
            emitterObject.transform.localRotation = Quaternion.identity;
            emitterObject.transform.localScale = Vector3.one;

            ConfigureParticleSystem(
                emitter,
                isBurn,
                particleLimit);
            emitterRenderer =
                emitterObject.GetComponent<ParticleSystemRenderer>();
            emitterRenderer.renderMode =
                ParticleSystemRenderMode.Billboard;
            emitterRenderer.sortMode =
                ParticleSystemSortMode.OldestInFront;
            emitterRenderer.sharedMaterial = sharedMaterial;
            emitter.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void ConfigureParticleSystem(
            ParticleSystem emitter,
            bool isBurn,
            int particleLimit)
        {
            emitter.useAutoRandomSeed = false;
            emitter.randomSeed = unchecked(
                (uint)(
                    GetInstanceID() * 397 +
                    (isBurn ? 17 : 31)));

            ParticleSystem.MainModule main = emitter.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.simulationSpace =
                ParticleSystemSimulationSpace.Local;
            main.scalingMode =
                ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = particleLimit;
            main.startSpeed = 0f;
            main.startLifetime = isBurn
                ? new ParticleSystem.MinMaxCurve(0.36f, 0.72f)
                : new ParticleSystem.MinMaxCurve(0.65f, 1.15f);
            main.startSize = isBurn
                ? new ParticleSystem.MinMaxCurve(0.045f, 0.095f)
                : new ParticleSystem.MinMaxCurve(0.075f, 0.145f);
            main.startRotation =
                new ParticleSystem.MinMaxCurve(
                    0f,
                    Mathf.PI * 2f);
            main.startColor = isBurn
                ? new ParticleSystem.MinMaxGradient(
                    new Color32(255, 232, 84, 255),
                    new Color32(255, 108, 28, 255))
                : new ParticleSystem.MinMaxGradient(
                    new Color32(168, 255, 95, 235),
                    new Color32(49, 190, 74, 220));

            ParticleSystem.EmissionModule emission =
                emitter.emission;
            emission.enabled = true;
            emission.rateOverTime = isBurn ? 7f : 3.5f;

            ParticleSystem.ShapeModule shape = emitter.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = isBurn
                ? new Vector3(0.54f, 0.2f, 0.01f)
                : new Vector3(0.5f, 0.12f, 0.01f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                emitter.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space =
                ParticleSystemSimulationSpace.Local;
            velocity.x = isBurn
                ? new ParticleSystem.MinMaxCurve(-0.16f, 0.16f)
                : new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            velocity.y = isBurn
                ? new ParticleSystem.MinMaxCurve(0.48f, 0.78f)
                : new ParticleSystem.MinMaxCurve(0.2f, 0.38f);
            velocity.z =
                new ParticleSystem.MinMaxCurve(0f, 0f);

            ParticleSystem.ColorOverLifetimeModule color =
                emitter.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(
                CreateLifetimeGradient(isBurn));

            ParticleSystem.CollisionModule collision =
                emitter.collision;
            collision.enabled = false;
            ParticleSystem.NoiseModule noise = emitter.noise;
            noise.enabled = false;
            ParticleSystem.TrailModule trails = emitter.trails;
            trails.enabled = false;
            ParticleSystem.LightsModule lights = emitter.lights;
            lights.enabled = false;
        }

        private static Gradient CreateLifetimeGradient(bool isBurn)
        {
            var gradient = new Gradient();
            if (isBurn)
            {
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(
                            new Color32(255, 240, 100, 255),
                            0f),
                        new GradientColorKey(
                            new Color32(255, 118, 34, 255),
                            0.62f),
                        new GradientColorKey(
                            new Color32(180, 45, 18, 255),
                            1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0.86f, 0.55f),
                        new GradientAlphaKey(0f, 1f)
                    });
            }
            else
            {
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(
                            new Color32(190, 255, 105, 255),
                            0f),
                        new GradientColorKey(
                            new Color32(65, 205, 78, 255),
                            0.7f),
                        new GradientColorKey(
                            new Color32(28, 125, 54, 255),
                            1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(0.92f, 0f),
                        new GradientAlphaKey(0.72f, 0.72f),
                        new GradientAlphaKey(0f, 1f)
                    });
            }

            return gradient;
        }

        private void UpdateEmitter(
            ParticleSystem emitter,
            int stacks,
            int previousStacks,
            int particleLimit)
        {
            if (emitter == null)
            {
                return;
            }

            if (stacks <= 0 || !isActiveAndEnabled)
            {
                emitter.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
                return;
            }

            if (!emitter.isPlaying)
            {
                emitter.Play(true);
            }

            int newlyAddedStacks =
                Mathf.Max(0, stacks - previousStacks);
            if (previousStacks <= 0)
            {
                newlyAddedStacks = Mathf.Max(2, stacks);
            }

            if (newlyAddedStacks > 0)
            {
                emitter.Emit(
                    Mathf.Clamp(
                        newlyAddedStacks,
                        1,
                        particleLimit));
            }
        }

        private void SynchronizeSorting()
        {
            if (targetRenderer == null)
            {
                return;
            }

            int sortingLayerId = targetRenderer.sortingLayerID;
            int sortingOrder = targetRenderer.sortingOrder + 1;
            if (sortingLayerId == cachedSortingLayerId &&
                sortingOrder == cachedSortingOrder)
            {
                return;
            }

            cachedSortingLayerId = sortingLayerId;
            cachedSortingOrder = sortingOrder;
            if (burnEmitterRenderer != null)
            {
                burnEmitterRenderer.sortingLayerID = sortingLayerId;
                burnEmitterRenderer.sortingOrder = sortingOrder;
            }

            if (poisonEmitterRenderer != null)
            {
                poisonEmitterRenderer.sortingLayerID = sortingLayerId;
                poisonEmitterRenderer.sortingOrder = sortingOrder;
            }

            if (impactFlashOverlay != null)
            {
                impactFlashOverlay.sortingLayerID = sortingLayerId;
                impactFlashOverlay.sortingOrder =
                    targetRenderer.sortingOrder + 2;
            }
        }

        private static int SaturatingAdd(int left, int right)
        {
            long result = (long)left + right;
            return result >= int.MaxValue
                ? int.MaxValue
                : (int)result;
        }

        private static class SharedResources
        {
            private static Texture2D burnTexture;
            private static Texture2D poisonTexture;
            private static Material burnMaterial;
            private static Material poisonMaterial;
            private static Material impactFlashMaterial;

            public static Material BurnMaterial
            {
                get
                {
                    EnsureCreated();
                    return burnMaterial;
                }
            }

            public static Material PoisonMaterial
            {
                get
                {
                    EnsureCreated();
                    return poisonMaterial;
                }
            }

            public static Material ImpactFlashMaterial
            {
                get
                {
                    if (impactFlashMaterial != null)
                    {
                        return impactFlashMaterial;
                    }

                    Shader flashShader =
                        Resources.Load<Shader>(
                            "RuleforgeTD/EnemyHitFlash");
                    if (flashShader == null)
                    {
                        flashShader = Shader.Find(
                            "RuleforgeTD/EnemyHitFlash");
                    }

                    if (flashShader != null)
                    {
                        impactFlashMaterial =
                            new Material(flashShader)
                            {
                                name =
                                    "Ruleforge Enemy Hit Flash Material",
                                hideFlags =
                                    HideFlags.HideAndDontSave
                            };
                    }

                    return impactFlashMaterial;
                }
            }

            private static void EnsureCreated()
            {
                if (burnMaterial != null && poisonMaterial != null)
                {
                    return;
                }

                Shader spriteShader = Shader.Find("Sprites/Default");
                if (spriteShader == null)
                {
                    return;
                }

                if (burnTexture == null)
                {
                    burnTexture = CreateEmberTexture();
                }

                if (poisonTexture == null)
                {
                    poisonTexture = CreateBubbleTexture();
                }

                if (burnMaterial == null)
                {
                    burnMaterial = CreateMaterial(
                        "Ruleforge Burn Particle Material",
                        spriteShader,
                        burnTexture);
                }

                if (poisonMaterial == null)
                {
                    poisonMaterial = CreateMaterial(
                        "Ruleforge Poison Particle Material",
                        spriteShader,
                        poisonTexture);
                }
            }

            private static Material CreateMaterial(
                string materialName,
                Shader shader,
                Texture2D texture)
            {
                var material = new Material(shader)
                {
                    name = materialName,
                    hideFlags = HideFlags.HideAndDontSave,
                    mainTexture = texture
                };
                return material;
            }

            private static Texture2D CreateEmberTexture()
            {
                const int size = 5;
                var pixels = new Color32[size * size];
                Color32 white = new Color32(255, 255, 255, 255);
                SetPixel(pixels, size, 2, 0, white);
                SetPixel(pixels, size, 1, 1, white);
                SetPixel(pixels, size, 2, 1, white);
                SetPixel(pixels, size, 3, 1, white);
                SetPixel(pixels, size, 1, 2, white);
                SetPixel(pixels, size, 2, 2, white);
                SetPixel(pixels, size, 3, 2, white);
                SetPixel(pixels, size, 2, 3, white);
                SetPixel(pixels, size, 2, 4, white);
                return CreateTexture(
                    "Ruleforge Ember Particle Texture",
                    size,
                    pixels);
            }

            private static Texture2D CreateBubbleTexture()
            {
                const int size = 7;
                var pixels = new Color32[size * size];
                Color32 white = new Color32(255, 255, 255, 255);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int deltaX = x - 3;
                        int deltaY = y - 3;
                        int distanceSquared =
                            deltaX * deltaX + deltaY * deltaY;
                        if (distanceSquared >= 6 &&
                            distanceSquared <= 11)
                        {
                            SetPixel(
                                pixels,
                                size,
                                x,
                                y,
                                white);
                        }
                    }
                }

                SetPixel(
                    pixels,
                    size,
                    2,
                    5,
                    new Color32(255, 255, 255, 180));
                return CreateTexture(
                    "Ruleforge Bubble Particle Texture",
                    size,
                    pixels);
            }

            private static Texture2D CreateTexture(
                string textureName,
                int size,
                Color32[] pixels)
            {
                var texture = new Texture2D(
                    size,
                    size,
                    TextureFormat.RGBA32,
                    false)
                {
                    name = textureName,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                return texture;
            }

            private static void SetPixel(
                Color32[] pixels,
                int width,
                int x,
                int y,
                Color32 color)
            {
                pixels[y * width + x] = color;
            }
        }
    }
}
