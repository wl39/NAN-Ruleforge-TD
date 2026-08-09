using RuleforgeTD.Enemies;
using RuleforgeTD.GameLogic.Simulation;
using RuleforgeTD.Rendering;
using UnityEngine;

namespace RuleforgeTD.Battle
{
    /// <summary>
    /// Central, bounded procedural VFX pool for Stage 01 card effects.
    /// Gameplay events and snapshots are inputs only; this view cannot mutate
    /// combat state. One LateUpdate advances every transient instead of
    /// allocating a Coroutine or Update behaviour per effect.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneCardEffectVfxView : MonoBehaviour
    {
        public const int DefaultPoolCapacity = 64;
        public const int MaximumPoolCapacity = 256;
        public const int MaximumLinePoints = 24;
        public const int MaximumPixelBlocks = 112;
        public const float PixelWorldSize = 0.0625f;
        public const float StandardPlaybackRadius = 0.36f;
        public const float AreaIndicatorPeakOpacity = 0.98f;
        public const int AreaIndicatorResolutionMultiplier = 1;
        public const int AreaIndicatorTextureSize = 129;
        public const int AreaIndicatorBorderPixels = 1;

        private static readonly float[] PoisonBubbleXOffsets =
        {
            -2.5f,
            -0.7f,
            1.2f,
            2.8f
        };
        private static readonly float[] PoisonBubblePhaseOffsets =
        {
            0.08f,
            0.36f,
            0.61f,
            0.83f
        };

        [SerializeField]
        [Range(16, MaximumPoolCapacity)]
        private int poolCapacity = DefaultPoolCapacity;

        private EffectInstance[] pool;
        private int nextPoolIndex;
        private uint playSequence;
        private string lastPlayedEffectId = string.Empty;
        private StageOneCardEffectShape lastPlayedShape;
        private Color lastPlayedColor = Color.white;
        private float lastPlayedRadius;
        private Vector3 lastStartPosition;
        private Vector3 lastEndPosition;
        private float lastPlayedAreaRadius;
        private int semanticEventPlayCount;
        private bool manualPreviewEnabled;
        private float manualPreviewElapsedTime;

        public int PoolCapacity =>
            pool == null ? 0 : pool.Length;
        public int ActiveEffectCount
        {
            get
            {
                if (pool == null)
                {
                    return 0;
                }

                int count = 0;
                for (int i = 0; i < pool.Length; i++)
                {
                    if (pool[i].Active)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
        public int ActivePixelBlockCount
        {
            get
            {
                if (pool == null)
                {
                    return 0;
                }

                int count = 0;
                for (int i = 0; i < pool.Length; i++)
                {
                    if (pool[i].Active)
                    {
                        count += pool[i].PixelBlockCount;
                    }
                }

                return count;
            }
        }
        public string LastPlayedEffectId =>
            lastPlayedEffectId;
        public StageOneCardEffectShape LastPlayedShape =>
            lastPlayedShape;
        public Color LastPlayedColor =>
            lastPlayedColor;
        public float LastPlayedRadius =>
            lastPlayedRadius;
        public Vector3 LastStartPosition =>
            lastStartPosition;
        public Vector3 LastEndPosition =>
            lastEndPosition;
        public float LastPlayedAreaRadius =>
            lastPlayedAreaRadius;
        public int SemanticEventPlayCount =>
            semanticEventPlayCount;
        public bool IsManualPreviewEnabled =>
            manualPreviewEnabled;
        public float ManualPreviewElapsedTime =>
            manualPreviewElapsedTime;

        private void Awake()
        {
            InitializePool();
        }

        private void LateUpdate()
        {
            if (pool == null)
            {
                return;
            }

            if (manualPreviewEnabled)
            {
                RenderManualPreview();
                return;
            }

            float now = Time.time;
            for (int i = 0; i < pool.Length; i++)
            {
                EffectInstance instance = pool[i];
                if (!instance.Active)
                {
                    continue;
                }

                instance.RefreshFollowTarget();
                float elapsed = now - instance.StartTime;
                float progress =
                    elapsed /
                    Mathf.Max(0.001f, instance.Style.Duration);
                if (progress >= 1f)
                {
                    instance.Stop();
                    continue;
                }

                RenderInstance(
                    instance,
                    Mathf.Clamp01(progress));
            }
        }

        /// <summary>
        /// Freezes active effects at an exact elapsed time. This is used only
        /// by review tools; Stage 01 continues to advance from scaled game
        /// time so pause and combat speed affect every transient together.
        /// Instances remain active while hidden so the reviewer can scrub
        /// backwards without reallocating the bounded pool.
        /// </summary>
        public void SetManualPreviewTime(float elapsedTime)
        {
            InitializePool();
            manualPreviewEnabled = true;
            manualPreviewElapsedTime =
                Mathf.Max(0f, elapsedTime);
            RenderManualPreview();
        }

        public void ClearManualPreview()
        {
            manualPreviewEnabled = false;
        }

        private void OnDisable()
        {
            StopAll();
        }

        public static StageOneCardEffectVfxView CreateRuntime(
            Transform parent)
        {
            var host = new GameObject(
                "Card Effect VFX",
                typeof(StageOneCardEffectVfxView));
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            return host.GetComponent<
                StageOneCardEffectVfxView>();
        }

        public void InitializeNow(int requestedCapacity = -1)
        {
            if (requestedCapacity > 0)
            {
                poolCapacity = Mathf.Clamp(
                    requestedCapacity,
                    16,
                    MaximumPoolCapacity);
            }

            InitializePool();
        }

        /// <summary>
        /// Adds the persistent status layer on demand and immediately applies
        /// the current authoritative enemy snapshot.
        /// </summary>
        public StageOneEnemyCardEffectVisual ApplyEnemySnapshot(
            StageOneEnemyView enemy,
            in EnemySnapshot snapshot)
        {
            if (enemy == null)
            {
                return null;
            }

            StageOneEnemyCardEffectVisual visual =
                enemy.GetComponent<
                    StageOneEnemyCardEffectVisual>();
            StageOneEnemyStatusIconStackView iconStack =
                enemy.GetComponent<
                    StageOneEnemyStatusIconStackView>();
            bool hasEffect =
                StageOneEnemyCardEffectVisual.HasSupportedEffect(
                    snapshot.StatusDetails);
            bool hasVisibleDebuff =
                StageOneEnemyStatusIconStackView
                    .HasVisibleDebuff(
                        snapshot.StatusDetails);
            if (visual == null &&
                iconStack == null &&
                !hasEffect &&
                !hasVisibleDebuff)
            {
                return null;
            }

            if (visual == null &&
                hasEffect)
            {
                visual =
                    enemy.gameObject.AddComponent<
                        StageOneEnemyCardEffectVisual>();
                visual.Configure(enemy);
            }

            if (iconStack == null &&
                hasVisibleDebuff)
            {
                iconStack =
                    enemy.gameObject.AddComponent<
                        StageOneEnemyStatusIconStackView>();
                float healthBarTopLocalY = float.NaN;
                EnemyHealthBarView healthBar =
                    enemy.GetComponent<EnemyHealthBarView>();
                if (healthBar != null &&
                    healthBar.TryGetVisualTopLocalY(
                        out float resolvedHealthBarTop))
                {
                    healthBarTopLocalY =
                        resolvedHealthBarTop;
                }

                iconStack.Configure(
                    enemy.GetComponent<SpriteRenderer>(),
                    healthBarTopLocalY);
            }

            if (visual != null)
            {
                visual.enabled = true;
                visual.ApplySnapshot(snapshot);
                if (!hasEffect)
                {
                    visual.enabled = false;
                }
            }
            if (iconStack != null)
            {
                iconStack.enabled = true;
                iconStack.ApplySnapshot(snapshot);
                if (!hasVisibleDebuff)
                {
                    iconStack.enabled = false;
                }
            }

            return visual;
        }

        public void PrepareEnemySnapshot(StageOneEnemyView enemy)
        {
            if (enemy == null)
            {
                return;
            }

            StageOneEnemyCardEffectVisual visual =
                enemy.GetComponent<
                    StageOneEnemyCardEffectVisual>();
            if (visual != null)
            {
                visual.PrepareForAuthoritativeSnapshot();
            }
        }

        /// <summary>
        /// Adds the projectile overlay on demand. VisualFlags are authored by
        /// simulation runtime state, so the renderer never guesses which cards
        /// are bound to a projectile.
        /// </summary>
        public StageOneProjectileCardEffectVisual
            ApplyProjectileSnapshot(
                StageOneProjectileView projectile,
                in ProjectileSnapshot snapshot)
        {
            if (projectile == null)
            {
                return null;
            }

            StageOneProjectileCardEffectVisual visual =
                projectile.GetComponent<
                    StageOneProjectileCardEffectVisual>();
            bool hasEffect =
                snapshot.VisualFlags !=
                CardEffectVisualFlags.None;
            if (visual == null && !hasEffect)
            {
                return null;
            }

            if (visual == null)
            {
                visual =
                    projectile.gameObject.AddComponent<
                        StageOneProjectileCardEffectVisual>();
                visual.Configure(projectile);
            }

            visual.enabled = true;
            visual.ApplySnapshot(snapshot);
            if (!hasEffect)
            {
                visual.enabled = false;
            }
            return visual;
        }

        public void PrepareProjectileSnapshot(
            StageOneProjectileView projectile)
        {
            if (projectile == null)
            {
                return;
            }

            StageOneProjectileCardEffectVisual visual =
                projectile.GetComponent<
                    StageOneProjectileCardEffectVisual>();
            if (visual != null)
            {
                visual.PrepareForAuthoritativeSnapshot();
            }
        }

        /// <summary>
        /// Converts a simulation presentation event into a local burst or link.
        /// Position resolution stays in StageOneBattleController because it
        /// already owns the entity-to-view dictionaries.
        /// </summary>
        public bool PlayEvent(
            in SimulationPresentationEvent item,
            Vector3 subjectPosition,
            bool hasSubjectPosition,
            Vector3 sourcePosition,
            bool hasSourcePosition)
        {
            if (item.Type ==
                PresentationEventType.StatusRemoved)
            {
                return false;
            }

            StageOneCardEffectStyle style;
            bool hasStyle =
                item.Type == PresentationEventType.CardExecuted
                    ? StageOneCardEffectPalette.TryGetCardStyle(
                        item.ContentId,
                        out style)
                    : StageOneCardEffectPalette.TryGetEventStyle(
                        item.ContentId,
                        out style);
            if (!hasStyle)
            {
                return false;
            }

            bool localOnly =
                item.Type == PresentationEventType.CardExecuted ||
                item.Type == PresentationEventType.StatusApplied;
            bool hasLink =
                !localOnly &&
                hasSourcePosition &&
                hasSubjectPosition &&
                Vector3.SqrMagnitude(
                    subjectPosition - sourcePosition) >
                0.0001f;

            bool played;
            if (hasLink)
            {
                played = PlayInternal(
                    style,
                    sourcePosition,
                    subjectPosition,
                    null);
            }
            else if (hasSubjectPosition)
            {
                played = PlayInternal(
                    style,
                    subjectPosition,
                    subjectPosition,
                    null);
            }
            else if (hasSourcePosition)
            {
                played = PlayInternal(
                    style,
                    sourcePosition,
                    sourcePosition,
                    null);
            }
            else
            {
                played = false;
            }

            if (played)
            {
                semanticEventPlayCount++;
            }
            return played;
        }

        public bool Play(
            string effectId,
            Vector3 worldPosition)
        {
            return PlayLink(
                effectId,
                worldPosition,
                worldPosition);
        }

        /// <summary>
        /// 시뮬레이션이 확정한 실제 범위를 카드 VFX 색상의
        /// 높은 가시성 픽셀 원으로 표시한다. 반경은 표현 계층에서
        /// 추측하지 않고 범위 판정 이벤트의 값을 그대로 사용한다.
        /// </summary>
        public bool PlayAreaIndicator(
            string effectId,
            Vector3 worldPosition,
            float radius)
        {
            if (radius <= 0f ||
                !StageOneCardEffectPalette.TryGetEventStyle(
                    effectId,
                    out StageOneCardEffectStyle style))
            {
                return false;
            }

            InitializePool();
            EffectInstance instance = AcquireInstance();
            worldPosition.z = -0.06f;
            instance.Play(
                style,
                worldPosition,
                worldPosition,
                Time.time,
                ++playSequence);
            instance.SetAreaIndicator(radius);
            RenderInstance(instance, 0f);

            lastPlayedEffectId = style.Id;
            lastPlayedShape = style.Shape;
            lastPlayedColor = style.Primary;
            lastPlayedRadius = style.Radius;
            lastPlayedAreaRadius = radius;
            lastStartPosition = worldPosition;
            lastEndPosition = worldPosition;
            return true;
        }

        /// <summary>
        /// Plays every card VFX bit at the enemy impact center. The effect
        /// remains attached to the same enemy entity for its full lifetime,
        /// including hit recoil and death animation movement.
        /// </summary>
        public int PlayFlagSet(
            ulong effectVisualFlags,
            StageOneEnemyView target)
        {
            if (effectVisualFlags == 0UL || target == null)
            {
                return 0;
            }

            int played = 0;
            for (int bit = 0;
                 bit < StageOneCardEffectPalette.StyleCount;
                 bit++)
            {
                ulong mask = 1UL << bit;
                if ((effectVisualFlags & mask) == 0UL)
                {
                    continue;
                }

                if (PlayAttached(
                        StageOneCardEffectPalette
                            .GetStyle(bit)
                            .Id,
                        target))
                {
                    played++;
                }
            }

            return played;
        }

        public bool PlayAttached(
            string effectId,
            StageOneEnemyView target)
        {
            if (target == null)
            {
                return false;
            }

            Vector3 position = target.WorldImpactCenter;
            return PlayInternal(
                effectId,
                position,
                position,
                target);
        }

        /// <summary>
        /// Plays a bounded link. For ricochet the source is the current
        /// projectile and the target is the newly selected enemy, making the
        /// real direction change readable while the simulation snapshot drives
        /// the projectile itself toward that target.
        /// </summary>
        public bool PlayLink(
            string effectId,
            Vector3 sourcePosition,
            Vector3 targetPosition)
        {
            return PlayInternal(
                effectId,
                sourcePosition,
                targetPosition,
                null);
        }

        private bool PlayInternal(
            string effectId,
            Vector3 sourcePosition,
            Vector3 targetPosition,
            StageOneEnemyView followTarget)
        {
            if (!StageOneCardEffectPalette.TryGetStyle(
                    effectId,
                    out StageOneCardEffectStyle style))
            {
                return false;
            }

            return PlayInternal(
                style,
                sourcePosition,
                targetPosition,
                followTarget);
        }

        private bool PlayInternal(
            in StageOneCardEffectStyle style,
            Vector3 sourcePosition,
            Vector3 targetPosition,
            StageOneEnemyView followTarget)
        {
            InitializePool();

            EffectInstance instance = AcquireInstance();
            StageOneCardEffectStyle playbackStyle =
                ResolvePlaybackStyle(style);
            sourcePosition.z = -0.18f;
            targetPosition.z = -0.18f;
            instance.Play(
                playbackStyle,
                sourcePosition,
                targetPosition,
                Time.time,
                ++playSequence);
            if (followTarget != null)
            {
                instance.AttachToEnemy(
                    followTarget,
                    Vector3.Lerp(
                        sourcePosition,
                        targetPosition,
                        0.5f));
            }
            RenderInstance(instance, 0f);

            lastPlayedEffectId = playbackStyle.Id;
            lastPlayedShape = playbackStyle.Shape;
            lastPlayedColor = playbackStyle.Primary;
            lastPlayedRadius = playbackStyle.Radius;
            lastPlayedAreaRadius = 0f;
            lastStartPosition = sourcePosition;
            lastEndPosition = targetPosition;
            return true;
        }

        private static StageOneCardEffectStyle ResolvePlaybackStyle(
            StageOneCardEffectStyle style)
        {
            if (string.Equals(
                    style.Id,
                    "split",
                    System.StringComparison.Ordinal) ||
                string.Equals(
                    style.Id,
                    "lifesteal",
                    System.StringComparison.Ordinal))
            {
                return style;
            }

            float scale =
                StandardPlaybackRadius /
                Mathf.Max(0.05f, style.Radius);
            return new StageOneCardEffectStyle(
                style.Id,
                style.Primary,
                style.Secondary,
                style.Shape,
                style.Duration,
                StandardPlaybackRadius,
                style.Width * scale,
                style.MotionHeight * scale);
        }

        public void StopAll()
        {
            if (pool == null)
            {
                return;
            }

            for (int i = 0; i < pool.Length; i++)
            {
                pool[i].Stop();
            }
        }

        private void InitializePool()
        {
            if (pool != null)
            {
                return;
            }

            int capacity = Mathf.Clamp(
                poolCapacity,
                16,
                96);
            pool = new EffectInstance[capacity];
            Material material = SharedResources.LineMaterial;
            for (int i = 0; i < pool.Length; i++)
            {
                var child = new GameObject(
                    "Card Effect " + i.ToString("00"),
                    typeof(LineRenderer),
                    typeof(MeshFilter),
                    typeof(MeshRenderer));
                child.transform.SetParent(transform, false);
                LineRenderer line =
                    child.GetComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.loop = false;
                line.alignment = LineAlignment.View;
                line.textureMode = LineTextureMode.Stretch;
                line.numCapVertices = 0;
                line.numCornerVertices = 0;
                line.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.sharedMaterial = material;
                WorldSortingLayers.Apply(
                    line,
                    WorldSortingLayers.Effects);
                line.sortingOrder = 60;
                line.enabled = false;

                MeshFilter meshFilter =
                    child.GetComponent<MeshFilter>();
                MeshRenderer meshRenderer =
                    child.GetComponent<MeshRenderer>();
                var pixelMesh = new Mesh
                {
                    name =
                        "Card Effect Pixel Mesh " +
                        i.ToString("00"),
                    hideFlags = HideFlags.DontSave
                };
                pixelMesh.MarkDynamic();
                meshFilter.sharedMesh = pixelMesh;
                meshRenderer.sharedMaterial =
                    SharedResources.PixelMaterial;
                WorldSortingLayers.Apply(
                    meshRenderer,
                    WorldSortingLayers.Effects);
                meshRenderer.sortingOrder = 60;
                meshRenderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                meshRenderer.enabled = false;
                SpriteRenderer areaIndicator =
                    CreateAreaIndicator(child.transform);
                pool[i] = new EffectInstance(
                    child,
                    line,
                    meshRenderer,
                    pixelMesh,
                    CreateSplitSparks(child.transform),
                    areaIndicator);
            }
        }

        private static SpriteRenderer CreateAreaIndicator(
            Transform parent)
        {
            var areaObject = new GameObject(
                "Pixel Area Indicator",
                typeof(SpriteRenderer));
            areaObject.transform.SetParent(parent, false);
            SpriteRenderer renderer =
                areaObject.GetComponent<SpriteRenderer>();
            renderer.sprite = SharedResources.PixelCircleSprite;
            WorldSortingLayers.Apply(
                renderer,
                WorldSortingLayers.Effects);
            renderer.sortingOrder = 15;
            renderer.enabled = false;
            return renderer;
        }

        private static SpriteRenderer[] CreateSplitSparks(
            Transform parent)
        {
            const int sparkCount = 6;
            var sparks = new SpriteRenderer[sparkCount];
            for (int i = 0; i < sparks.Length; i++)
            {
                var sparkObject = new GameObject(
                    "Split Spark " + (i + 1).ToString("00"));
                sparkObject.transform.SetParent(parent, false);
                SpriteRenderer renderer =
                    sparkObject.AddComponent<SpriteRenderer>();
                renderer.sprite = SharedResources.PixelSprite;
                WorldSortingLayers.Apply(
                    renderer,
                    WorldSortingLayers.Effects);
                renderer.sortingOrder = 61;
                renderer.enabled = false;
                sparks[i] = renderer;
            }

            return sparks;
        }

        private EffectInstance AcquireInstance()
        {
            for (int offset = 0;
                 offset < pool.Length;
                 offset++)
            {
                int index =
                    (nextPoolIndex + offset) % pool.Length;
                if (!pool[index].Active)
                {
                    nextPoolIndex =
                        (index + 1) % pool.Length;
                    return pool[index];
                }
            }

            // Reuse the oldest slot when the browser-facing cap is reached.
            EffectInstance result = pool[nextPoolIndex];
            nextPoolIndex =
                (nextPoolIndex + 1) % pool.Length;
            result.Stop();
            return result;
        }

        private static void RenderInstance(
            EffectInstance instance,
            float progress)
        {
            instance.Show();
            if (instance.AreaRadius > 0f)
            {
                RenderAreaIndicator(instance, progress);
                return;
            }

            instance.AreaIndicator.enabled = false;
            if (instance.Style.Shape ==
                StageOneCardEffectShape.Branch)
            {
                RenderSplitBurst(instance, progress);
                return;
            }

            instance.SetSplitSparksVisible(false);
            instance.Line.positionCount = 0;
            instance.Line.enabled = false;
            instance.PixelRenderer.enabled = true;
            float eased =
                1f - (1f - progress) * (1f - progress);
            float alpha = ResolveVisibleAlpha(progress);
            float widthPulse =
                1f + Mathf.Sin(progress * Mathf.PI) * 0.35f;
            Color startColor = Color.Lerp(
                instance.Style.Secondary,
                instance.Style.Primary,
                progress * 0.6f);
            Color endColor = instance.Style.Primary;
            startColor.a = alpha;
            endColor.a = alpha * 0.22f;

            // These semantic effects rely on a recognisable silhouette,
            // not an abstract polyline. Drawing them directly into the same
            // bounded pixel mesh keeps the WebGL cost predictable while
            // allowing layered colours and disconnected icon parts.
            switch (instance.Style.Shape)
            {
                case StageOneCardEffectShape.Lance:
                    RenderPierceArrow(instance, eased, alpha);
                    return;
                case StageOneCardEffectShape.Flame:
                    RenderBurnFlame(instance, progress, alpha);
                    return;
                case StageOneCardEffectShape.Hourglass:
                    RenderSlowSnail(instance, eased, alpha);
                    return;
                case StageOneCardEffectShape.Toxic:
                    RenderPoisonBubbles(instance, progress, alpha);
                    return;
                case StageOneCardEffectShape.Blast:
                    RenderExplosionCloud(instance, progress, alpha);
                    return;
                case StageOneCardEffectShape.Vortex:
                    RenderMagnetConvergence(
                        instance,
                        progress);
                    return;
                case StageOneCardEffectShape.Launch:
                    RenderAirborneWhirlwind(
                        instance,
                        progress);
                    return;
                case StageOneCardEffectShape.Slash:
                    RenderBleedWound(
                        instance,
                        progress);
                    return;
                case StageOneCardEffectShape.StunBurst:
                    RenderStunStars(
                        instance,
                        progress);
                    return;
                case StageOneCardEffectShape.Chain:
                    RenderBindBlindEye(
                        instance,
                        progress);
                    return;
            }

            int count;
            switch (instance.Style.Shape)
            {
                case StageOneCardEffectShape.Arc:
                    count = BuildArc(
                        instance,
                        progress,
                        12,
                        1f);
                    break;
                case StageOneCardEffectShape.Streak:
                    count = BuildStreak(instance, eased);
                    break;
                case StageOneCardEffectShape.Reticle:
                    count = BuildRing(
                        instance,
                        eased,
                        0.78f,
                        1.5f);
                    break;
                case StageOneCardEffectShape.Clock:
                    count = BuildClock(instance, eased);
                    break;
                case StageOneCardEffectShape.Rune:
                    count = BuildRune(instance, eased);
                    break;
                case StageOneCardEffectShape.Lightning:
                    count = BuildLightning(instance, eased);
                    break;
                case StageOneCardEffectShape.IceBurst:
                    count = BuildStar(instance, eased, 8);
                    break;
                case StageOneCardEffectShape.Echo:
                    count = BuildEcho(instance, eased);
                    break;
                case StageOneCardEffectShape.Pulse:
                    count = BuildRing(
                        instance,
                        eased,
                        0.25f,
                        1.9f);
                    break;
                case StageOneCardEffectShape.Mirror:
                    count = BuildMirror(instance, eased);
                    break;
                case StageOneCardEffectShape.Transfer:
                    count = BuildArc(
                        instance,
                        progress,
                        14,
                        0.62f);
                    break;
                case StageOneCardEffectShape.Seal:
                    count = BuildPolygon(instance, eased, 6);
                    break;
                case StageOneCardEffectShape.Corrosion:
                    count = BuildCorrosion(instance, eased);
                    break;
                case StageOneCardEffectShape.Orbit:
                    count = BuildOrbit(instance, eased);
                    break;
                case StageOneCardEffectShape.Heal:
                    count = BuildHeart(instance, eased);
                    break;
                case StageOneCardEffectShape.Fear:
                    count = BuildFear(instance, eased);
                    break;
                case StageOneCardEffectShape.Branch:
                    count = BuildBranch(instance, eased);
                    break;
                case StageOneCardEffectShape.Impact:
                    count = BuildImpact(instance, eased);
                    break;
                case StageOneCardEffectShape.Target:
                    count = BuildTarget(instance, eased);
                    break;
                case StageOneCardEffectShape.Coin:
                    count = BuildPolygon(instance, eased, 12);
                    break;
                case StageOneCardEffectShape.Grow:
                    count = BuildRing(
                        instance,
                        eased,
                        0.18f,
                        1.75f);
                    break;
                case StageOneCardEffectShape.Contract:
                    count = BuildRing(
                        instance,
                        eased,
                        1.75f,
                        0.18f);
                    break;
                case StageOneCardEffectShape.Twin:
                    count = BuildEcho(instance, eased);
                    break;
                case StageOneCardEffectShape.Sacrifice:
                    count = BuildHeart(instance, 1f - eased);
                    break;
                case StageOneCardEffectShape.Return:
                    count = BuildArc(
                        instance,
                        1f - progress,
                        16,
                        0.82f);
                    break;
                case StageOneCardEffectShape.Rewind:
                    count = BuildClock(instance, 1f - eased);
                    break;
                case StageOneCardEffectShape.Resonance:
                    count = BuildStar(instance, eased, 6);
                    break;
                case StageOneCardEffectShape.Absorb:
                    count = BuildRing(
                        instance,
                        eased,
                        1.9f,
                        0.08f);
                    break;
                case StageOneCardEffectShape.TimeStop:
                    count = BuildClock(instance, Mathf.Min(0.35f, eased));
                    break;
                case StageOneCardEffectShape.Mutate:
                    count = BuildRune(
                        instance,
                        Mathf.Repeat(eased * 1.5f, 1f));
                    break;
                case StageOneCardEffectShape.Execute:
                    count = BuildTarget(instance, eased);
                    break;
                case StageOneCardEffectShape.Parasite:
                    count = BuildCorrosion(instance, eased);
                    break;
                case StageOneCardEffectShape.Rebirth:
                    count = BuildStar(instance, eased, 10);
                    break;
                case StageOneCardEffectShape.Relay:
                    count = BuildLightning(instance, eased);
                    break;
                case StageOneCardEffectShape.Recursion:
                    count = BuildLightning(
                        instance,
                        Mathf.Repeat(eased * 1.5f, 1f));
                    break;
                case StageOneCardEffectShape.ReverseOrder:
                    count = BuildClock(instance, 1f - eased);
                    break;
                case StageOneCardEffectShape.DualInterpretation:
                    count = BuildMirror(instance, eased);
                    break;
                case StageOneCardEffectShape.InfiniteOrbit:
                    count = BuildOrbit(
                        instance,
                        Mathf.Repeat(eased * 2f, 1f));
                    break;
                case StageOneCardEffectShape.Overclone:
                    count = BuildEcho(
                        instance,
                        Mathf.Repeat(eased * 1.35f, 1f));
                    break;
                case StageOneCardEffectShape.ForbiddenDeal:
                    count = BuildPolygon(instance, eased, 8);
                    break;
                case StageOneCardEffectShape.LastCommand:
                    count = BuildLightning(instance, 1f - eased);
                    break;
                case StageOneCardEffectShape.FateLock:
                    count = BuildTarget(
                        instance,
                        Mathf.Min(0.72f, eased));
                    break;
                case StageOneCardEffectShape.Overload:
                    count = BuildStar(instance, eased, 12);
                    break;
                case StageOneCardEffectShape.Singularity:
                    count = BuildRing(
                        instance,
                        eased,
                        2.1f,
                        0.03f);
                    break;
                case StageOneCardEffectShape.PhoenixCore:
                    count = BuildStar(instance, eased, 11);
                    break;
                case StageOneCardEffectShape.TimeRift:
                    count = BuildEcho(instance, 1f - eased);
                    break;
                case StageOneCardEffectShape.MirrorWorld:
                    count = BuildMirror(instance, 1f - eased);
                    break;
                case StageOneCardEffectShape.Ouroboros:
                    count = BuildOrbit(
                        instance,
                        Mathf.Repeat(eased * 1.5f, 1f));
                    break;
                default:
                    count = BuildRing(
                        instance,
                        eased,
                        0.4f,
                        1.4f);
                    break;
            }

            RenderPixelPolyline(
                instance,
                count,
                startColor,
                endColor,
                alpha,
                widthPulse);
        }

        private static void RenderAreaIndicator(
            EffectInstance instance,
            float progress)
        {
            instance.Line.positionCount = 0;
            instance.Line.enabled = false;
            instance.SetSplitSparksVisible(false);

            SpriteRenderer renderer = instance.AreaIndicator;
            renderer.enabled = true;
            renderer.transform.position = ResolveCenter(instance);
            float diameter = instance.AreaRadius * 2f;
            renderer.transform.localScale =
                new Vector3(
                    diameter,
                    diameter,
                    1f);
            float fade = progress < 0.84f
                ? 1f
                : Mathf.Clamp01(
                    (1f - progress) / 0.16f);
            Color color = instance.Style.Primary;
            color.a = AreaIndicatorPeakOpacity * fade;
            renderer.color = color;

            RenderAreaAnimation(instance, progress);
        }

        /// <summary>
        /// Draws the semantic animation inside the authoritative gameplay
        /// radius. The thin SpriteRenderer ring above is kept independent so
        /// even a visually dense burst never obscures the exact hit boundary.
        /// Every authored point is clamped inside AreaRadius and shares the
        /// bounded WebGL pixel-mesh budget.
        /// </summary>
        private static void RenderAreaAnimation(
            EffectInstance instance,
            float progress)
        {
            switch (instance.Style.Shape)
            {
                case StageOneCardEffectShape.Blast:
                    RenderAreaExplosionMushroom(instance, progress);
                    break;
                case StageOneCardEffectShape.Toxic:
                    RenderAreaPoisonBubbles(instance, progress);
                    break;
                case StageOneCardEffectShape.Flame:
                    RenderAreaFlameBurst(instance, progress);
                    break;
                case StageOneCardEffectShape.Pulse:
                    RenderAreaPulse(instance, progress);
                    break;
                case StageOneCardEffectShape.Launch:
                    RenderAreaWhirlwind(instance, progress);
                    break;
                case StageOneCardEffectShape.IceBurst:
                    RenderAreaIceShards(instance, progress);
                    break;
                case StageOneCardEffectShape.Chain:
                    RenderAreaBindChains(instance, progress);
                    break;
                case StageOneCardEffectShape.Lightning:
                    RenderAreaLightning(instance, progress);
                    break;
                case StageOneCardEffectShape.Overload:
                    RenderAreaOverload(instance, progress);
                    break;
                case StageOneCardEffectShape.Singularity:
                    RenderAreaSingularity(instance, progress);
                    break;
                case StageOneCardEffectShape.LastCommand:
                    RenderAreaLastCommand(instance, progress);
                    break;
                case StageOneCardEffectShape.Sacrifice:
                    RenderAreaSacrifice(instance, progress);
                    break;
                default:
                    RenderAreaPulse(instance, progress);
                    break;
            }
        }

        private static void RenderAreaExplosionMushroom(
            EffectInstance instance,
            float progress)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixel = ResolveAreaPixelSize(instance.AreaRadius);
            float radius = ResolveContainedRadius(
                instance.AreaRadius,
                pixel);
            float alpha = ResolveVisibleAlpha(progress);
            RenderPixelMushroomExplosion(
                instance,
                center,
                radius,
                pixel,
                progress,
                alpha);
            instance.EndPixelMesh();
        }

        private static void RenderAreaPoisonBubbles(
            EffectInstance instance,
            float progress)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixel = ResolveAreaPixelSize(instance.AreaRadius);
            float radius = ResolveContainedRadius(
                instance.AreaRadius,
                pixel);
            float alpha = ResolveVisibleAlpha(progress);
            Color pool = instance.Style.Primary;
            Color bubble = instance.Style.Secondary;
            pool.a = QuantizeAlpha(alpha * 0.68f);
            bubble.a = QuantizeAlpha(alpha);
            AddPixelCircle(
                instance,
                center,
                radius * 0.86f,
                pixel,
                pool,
                28);

            const int bubbleCount = 7;
            for (int i = 0; i < bubbleCount; i++)
            {
                float phase = Mathf.Repeat(
                    progress + i * 0.137f,
                    1f);
                float rise = Mathf.SmoothStep(0f, 1f, phase);
                float x = HashSigned(instance.Sequence, i) *
                    radius * 0.56f;
                float y = Mathf.Lerp(
                    -radius * 0.42f,
                    radius * 0.58f,
                    rise);
                float bubbleRadius = pixel *
                    Mathf.Lerp(1.1f, 2.2f, rise);
                AddPixelCircle(
                    instance,
                    center + new Vector3(x, y, 0f),
                    bubbleRadius,
                    pixel,
                    bubble,
                    8);
            }
            instance.EndPixelMesh();
        }

        private static void RenderAreaFlameBurst(
            EffectInstance instance,
            float progress)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixel = ResolveAreaPixelSize(instance.AreaRadius);
            float radius = ResolveContainedRadius(instance.AreaRadius, pixel);
            float alpha = ResolveVisibleAlpha(progress);
            Color outer = instance.Style.Primary;
            Color core = instance.Style.Secondary;
            outer.a = QuantizeAlpha(alpha);
            core.a = QuantizeAlpha(alpha);
            const int flameCount = 12;
            for (int i = 0; i < flameCount; i++)
            {
                float angle = Mathf.PI * 2f * i / flameCount;
                float distance = radius * Mathf.Lerp(
                    0.12f,
                    0.82f,
                    Mathf.SmoothStep(0f, 1f, progress));
                Vector3 point = center + new Vector3(
                    Mathf.Cos(angle) * distance,
                    Mathf.Sin(angle) * distance,
                    0f);
                instance.AddPixelBlock(
                    SnapToPixelGrid(point),
                    pixel * 1.8f,
                    i % 2 == 0 ? core : outer);
            }
            instance.EndPixelMesh();
        }

        private static void RenderAreaPulse(
            EffectInstance instance,
            float progress)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixel = ResolveAreaPixelSize(instance.AreaRadius);
            float radius = ResolveContainedRadius(instance.AreaRadius, pixel);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            float alpha = ResolveVisibleAlpha(progress);
            Color primary = instance.Style.Primary;
            Color secondary = instance.Style.Secondary;
            primary.a = QuantizeAlpha(alpha);
            secondary.a = QuantizeAlpha(alpha * 0.75f);
            AddPixelCircle(
                instance,
                center,
                radius * Mathf.Lerp(0.04f, 0.96f, eased),
                pixel,
                primary,
                32);
            AddPixelCircle(
                instance,
                center,
                radius * Mathf.Lerp(0.02f, 0.72f, eased),
                pixel,
                secondary,
                24);
            instance.EndPixelMesh();
        }

        private static void RenderAreaWhirlwind(
            EffectInstance instance,
            float progress)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixel = ResolveAreaPixelSize(instance.AreaRadius);
            float radius = ResolveContainedRadius(instance.AreaRadius, pixel);
            float alpha = ResolveVisibleAlpha(progress);
            Color wind = instance.Style.Primary;
            Color highlight = instance.Style.Secondary;
            wind.a = QuantizeAlpha(alpha * 0.84f);
            highlight.a = QuantizeAlpha(alpha);
            const int arms = 3;
            const int pointsPerArm = 12;
            for (int arm = 0; arm < arms; arm++)
            {
                for (int point = 0; point < pointsPerArm; point++)
                {
                    float t = point / (float)(pointsPerArm - 1);
                    float angle =
                        arm * Mathf.PI * 2f / arms +
                        t * Mathf.PI * 2.6f -
                        progress * Mathf.PI * 5f;
                    float distance = radius * t * 0.9f;
                    Vector3 position = center + new Vector3(
                        Mathf.Cos(angle) * distance,
                        Mathf.Sin(angle) * distance * 0.72f,
                        0f);
                    instance.AddPixelBlock(
                        SnapToPixelGrid(position),
                        pixel,
                        point > 7 ? highlight : wind);
                }
            }
            instance.EndPixelMesh();
        }

        private static void RenderAreaIceShards(
            EffectInstance instance,
            float progress)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixel = ResolveAreaPixelSize(instance.AreaRadius);
            float radius = ResolveContainedRadius(instance.AreaRadius, pixel);
            float expansion = Mathf.SmoothStep(0f, 1f, progress);
            float alpha = ResolveVisibleAlpha(progress);
            Color ice = instance.Style.Primary;
            Color snow = instance.Style.Secondary;
            ice.a = QuantizeAlpha(alpha);
            snow.a = QuantizeAlpha(alpha * 0.88f);
            const int shardCount = 8;
            const int shardSteps = 8;
            for (int shard = 0; shard < shardCount; shard++)
            {
                float angle = Mathf.PI * 2f * shard / shardCount;
                Vector3 direction = new Vector3(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle),
                    0f);
                for (int step = 1; step <= shardSteps; step++)
                {
                    float t = step / (float)shardSteps;
                    instance.AddPixelBlock(
                        SnapToPixelGrid(
                            center + direction * radius * t * expansion),
                        pixel * Mathf.Lerp(1.45f, 0.7f, t),
                        ice);
                }
            }

            const int snowCount = 18;
            for (int i = 0; i < snowCount; i++)
            {
                float angle = Mathf.PI * 2f * i / snowCount +
                    HashSigned(instance.Sequence, i + 40) * 0.2f;
                float distance = radius *
                    (0.22f + 0.68f * Mathf.Repeat(
                        progress * 0.72f + i * 0.173f,
                        1f));
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        center + new Vector3(
                            Mathf.Cos(angle) * distance,
                            Mathf.Sin(angle) * distance,
                            0f)),
                    pixel * 0.75f,
                    snow);
            }
            instance.EndPixelMesh();
        }

        private static void RenderAreaBindChains(
            EffectInstance instance,
            float progress)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixel = ResolveAreaPixelSize(instance.AreaRadius);
            float radius = ResolveContainedRadius(instance.AreaRadius, pixel);
            float pull = Mathf.SmoothStep(0f, 1f, progress);
            float alpha = ResolveVisibleAlpha(progress);
            Color gold = new Color32(245, 187, 48, 255);
            Color highlight = new Color32(255, 241, 150, 255);
            gold.a = QuantizeAlpha(alpha);
            highlight.a = QuantizeAlpha(alpha);
            const int chains = 6;
            const int links = 8;
            for (int chain = 0; chain < chains; chain++)
            {
                float angle = Mathf.PI * 2f * chain / chains;
                Vector3 anchor = center + new Vector3(
                    Mathf.Cos(angle) * radius * 0.88f,
                    Mathf.Sin(angle) * radius * 0.88f,
                    0f);
                Vector3 target = Vector3.Lerp(
                    anchor,
                    center,
                    pull);
                for (int link = 0; link <= links; link++)
                {
                    float t = link / (float)links;
                    Vector3 point = Vector3.Lerp(anchor, target, t);
                    Vector3 tangent = (target - anchor).normalized;
                    Vector3 normal = new Vector3(
                        -tangent.y,
                        tangent.x,
                        0f);
                    point += normal *
                        (link % 2 == 0 ? pixel * 0.38f : -pixel * 0.38f);
                    instance.AddPixelBlock(
                        SnapToPixelGrid(point),
                        pixel * (link % 2 == 0 ? 1.35f : 0.9f),
                        link % 2 == 0 ? gold : highlight);
                }
                instance.AddPixelBlock(
                    SnapToPixelGrid(anchor),
                    pixel * 2f,
                    highlight);
            }
            instance.EndPixelMesh();
        }

        private static void RenderAreaLightning(
            EffectInstance instance,
            float progress)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixel = ResolveAreaPixelSize(instance.AreaRadius);
            float radius = ResolveContainedRadius(instance.AreaRadius, pixel);
            float alpha = ResolveVisibleAlpha(progress);
            Color bolt = instance.Style.Primary;
            Color core = instance.Style.Secondary;
            bolt.a = QuantizeAlpha(alpha);
            core.a = QuantizeAlpha(alpha);
            const int bolts = 7;
            const int steps = 8;
            for (int boltIndex = 0; boltIndex < bolts; boltIndex++)
            {
                float angle = Mathf.PI * 2f * boltIndex / bolts;
                Vector3 direction = new Vector3(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle),
                    0f);
                Vector3 normal = new Vector3(-direction.y, direction.x, 0f);
                for (int step = 1; step <= steps; step++)
                {
                    float t = step / (float)steps;
                    float jitter = HashSigned(
                        instance.Sequence + (uint)(progress * 12f),
                        boltIndex * steps + step) * pixel * 0.75f;
                    instance.AddPixelBlock(
                        SnapToPixelGrid(
                            center + direction * radius * t * 0.92f +
                            normal * jitter),
                        pixel,
                        step % 3 == 0 ? core : bolt);
                }
            }
            instance.EndPixelMesh();
        }

        private static void RenderAreaOverload(
            EffectInstance instance,
            float progress)
        {
            RenderAreaRadialGlyph(
                instance,
                progress,
                12,
                0.94f,
                true);
        }

        private static void RenderAreaSingularity(
            EffectInstance instance,
            float progress)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixel = ResolveAreaPixelSize(instance.AreaRadius);
            float radius = ResolveContainedRadius(instance.AreaRadius, pixel);
            float alpha = ResolveVisibleAlpha(progress);
            Color voidColor = instance.Style.Primary;
            Color energy = instance.Style.Secondary;
            voidColor.a = QuantizeAlpha(alpha);
            energy.a = QuantizeAlpha(alpha);
            float collapse = 1f - Mathf.SmoothStep(0f, 1f, progress);
            AddPixelCircle(
                instance,
                center,
                radius * Mathf.Lerp(0.14f, 0.86f, collapse),
                pixel,
                energy,
                30);
            const int particleCount = 24;
            for (int i = 0; i < particleCount; i++)
            {
                float phase = Mathf.Repeat(
                    collapse + i * 0.113f,
                    1f);
                float angle = i * 2.399963f + progress * 5f;
                float distance = radius * (0.12f + 0.8f * phase);
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        center + new Vector3(
                            Mathf.Cos(angle) * distance,
                            Mathf.Sin(angle) * distance,
                            0f)),
                    pixel,
                    i % 3 == 0 ? energy : voidColor);
            }
            instance.AddPixelBlock(
                SnapToPixelGrid(center),
                pixel * 3f,
                voidColor);
            instance.EndPixelMesh();
        }

        private static void RenderAreaLastCommand(
            EffectInstance instance,
            float progress)
        {
            RenderAreaRadialGlyph(
                instance,
                1f - progress,
                8,
                0.9f,
                false);
        }

        private static void RenderAreaSacrifice(
            EffectInstance instance,
            float progress)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixel = ResolveAreaPixelSize(instance.AreaRadius);
            float radius = ResolveContainedRadius(instance.AreaRadius, pixel);
            float alpha = ResolveVisibleAlpha(progress);
            Color blood = instance.Style.Primary;
            Color gift = instance.Style.Secondary;
            blood.a = QuantizeAlpha(alpha);
            gift.a = QuantizeAlpha(alpha);
            const int packets = 10;
            for (int i = 0; i < packets; i++)
            {
                float angle = Mathf.PI * 2f * i / packets;
                float distance = radius * 0.88f *
                    Mathf.SmoothStep(0f, 1f, progress);
                Vector3 direction = new Vector3(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle),
                    0f);
                for (int trail = 0; trail < 4; trail++)
                {
                    float trailDistance = Mathf.Max(
                        0f,
                        distance - trail * pixel * 1.5f);
                    instance.AddPixelBlock(
                        SnapToPixelGrid(center + direction * trailDistance),
                        pixel * (trail == 0 ? 1.6f : 0.8f),
                        trail == 0 ? gift : blood);
                }
            }
            instance.EndPixelMesh();
        }

        private static void RenderAreaRadialGlyph(
            EffectInstance instance,
            float progress,
            int spokeCount,
            float radiusScale,
            bool rotate)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixel = ResolveAreaPixelSize(instance.AreaRadius);
            float radius = ResolveContainedRadius(instance.AreaRadius, pixel);
            float alpha = ResolveVisibleAlpha(
                rotate ? progress : 1f - progress);
            Color primary = instance.Style.Primary;
            Color secondary = instance.Style.Secondary;
            primary.a = QuantizeAlpha(alpha);
            secondary.a = QuantizeAlpha(alpha);
            const int steps = 7;
            for (int spoke = 0; spoke < spokeCount; spoke++)
            {
                float angle = Mathf.PI * 2f * spoke / spokeCount +
                    (rotate ? progress * Mathf.PI * 1.5f : 0f);
                Vector3 direction = new Vector3(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle),
                    0f);
                for (int step = 1; step <= steps; step++)
                {
                    float t = step / (float)steps;
                    instance.AddPixelBlock(
                        SnapToPixelGrid(
                            center + direction * radius * radiusScale * t),
                        pixel,
                        step == steps ? secondary : primary);
                }
            }
            AddPixelCircle(
                instance,
                center,
                radius * radiusScale * 0.55f,
                pixel,
                secondary,
                24);
            instance.EndPixelMesh();
        }

        private static float ResolveAreaPixelSize(float radius)
        {
            return QuantizeSize(
                Mathf.Clamp(
                    radius / 22f,
                    PixelWorldSize,
                    PixelWorldSize * 2f));
        }

        private static float ResolveContainedRadius(
            float radius,
            float pixelSize)
        {
            return Mathf.Max(
                PixelWorldSize,
                radius - pixelSize * 1.5f);
        }

        private void RenderManualPreview()
        {
            if (pool == null)
            {
                return;
            }

            for (int i = 0; i < pool.Length; i++)
            {
                EffectInstance instance = pool[i];
                if (!instance.Active)
                {
                    continue;
                }

                instance.RefreshFollowTarget();
                float duration =
                    Mathf.Max(0.001f, instance.Style.Duration);
                if (manualPreviewElapsedTime >= duration)
                {
                    instance.Hide();
                    continue;
                }

                RenderInstance(
                    instance,
                    Mathf.Clamp01(
                        manualPreviewElapsedTime / duration));
            }
        }

        /// <summary>
        /// Rasterises the procedural polyline into axis-aligned square blocks.
        /// Positions, width, colour interpolation, and opacity are quantised so
        /// the effect reads like a low-resolution Point-filtered render texture
        /// without reducing the resolution of the underlying CraftPix scene.
        /// </summary>
        private static void RenderPixelPolyline(
            EffectInstance instance,
            int pointCount,
            Color startColor,
            Color endColor,
            float alpha,
            float widthPulse)
        {
            instance.BeginPixelMesh();
            if (pointCount <= 0)
            {
                instance.EndPixelMesh();
                return;
            }

            float blockSize = QuantizeSize(
                instance.Style.Width * widthPulse);
            Vector3 previous = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            int estimatedSteps = 0;
            for (int i = 1; i < pointCount; i++)
            {
                estimatedSteps += Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        Vector3.Distance(
                            instance.Points[i - 1],
                            instance.Points[i]) /
                        PixelWorldSize));
            }

            estimatedSteps = Mathf.Max(1, estimatedSteps);
            int written = 0;
            for (int segment = 1;
                 segment < pointCount &&
                 written < MaximumPixelBlocks;
                 segment++)
            {
                Vector3 first =
                    instance.Points[segment - 1];
                Vector3 second =
                    instance.Points[segment];
                int steps = Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        Vector3.Distance(first, second) /
                        PixelWorldSize));
                for (int step = 0;
                     step <= steps &&
                     written < MaximumPixelBlocks;
                     step++)
                {
                    Vector3 position = SnapToPixelGrid(
                        Vector3.Lerp(
                            first,
                            second,
                            step / (float)steps));
                    if ((position - previous).sqrMagnitude <
                        0.000001f)
                    {
                        continue;
                    }

                    float colorProgress =
                        Mathf.Clamp01(
                            written /
                            (float)estimatedSteps);
                    colorProgress =
                        Mathf.Round(colorProgress * 3f) / 3f;
                    Color color = Color.Lerp(
                        startColor,
                        endColor,
                        colorProgress);
                    color.a = QuantizeAlpha(alpha);
                    instance.AddPixelBlock(
                        position,
                        blockSize,
                        color);
                    previous = position;
                    written++;
                }
            }

            // A one-point shape still needs a visible pixel.
            if (written == 0)
            {
                startColor.a = QuantizeAlpha(alpha);
                instance.AddPixelBlock(
                    SnapToPixelGrid(instance.Points[0]),
                    blockSize,
                    startColor);
            }

            instance.EndPixelMesh();
        }

        private static void RenderBleedWound(
            EffectInstance instance,
            float progress)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixelSize = PixelWorldSize;
            float opening =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        0.06f,
                        0.64f,
                        progress));
            float visibleAlpha =
                ResolveVisibleAlpha(progress);
            Color edge = instance.Style.Primary;
            Color freshBlood = instance.Style.Secondary;
            edge.a = QuantizeAlpha(visibleAlpha);
            freshBlood.a = QuantizeAlpha(visibleAlpha);
            Vector3 woundDirection =
                new Vector3(1f, -1f, 0f).normalized;
            Vector3 woundNormal =
                new Vector3(1f, 1f, 0f).normalized;

            const int edgePointCount = 8;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 previous = Vector3.zero;
                for (int i = 0; i < edgePointCount; i++)
                {
                    float t =
                        i / (float)(edgePointCount - 1);
                    float along =
                        Mathf.Lerp(
                            -instance.Style.Radius * 0.92f,
                            instance.Style.Radius * 0.92f,
                            t);
                    float middleBulge =
                        Mathf.Sin(t * Mathf.PI) *
                        instance.Style.Radius *
                        0.16f *
                        opening;
                    float jagged =
                        (i % 2 == 0 ? -1f : 1f) *
                        pixelSize *
                        0.28f;
                    Vector3 point =
                        center +
                        woundDirection * along +
                        woundNormal *
                        side *
                        (pixelSize * 0.16f +
                         middleBulge +
                         jagged);
                    if (i == 0)
                    {
                        instance.AddPixelBlock(
                            SnapToPixelGrid(point),
                            pixelSize,
                            edge);
                    }
                    else
                    {
                        AddPixelLine(
                            instance,
                            previous,
                            point,
                            pixelSize,
                            i >= 2 && i <= 5
                                ? freshBlood
                                : edge);
                    }

                    previous = point;
                }
            }

            if (progress > 0.52f)
            {
                float drop =
                    Mathf.InverseLerp(
                        0.52f,
                        1f,
                        progress);
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        center +
                        woundDirection *
                        instance.Style.Radius *
                        0.68f +
                        Vector3.down *
                        Mathf.Lerp(
                            pixelSize,
                            instance.Style.Radius * 0.38f,
                            drop)),
                    pixelSize,
                    freshBlood);
            }

            instance.EndPixelMesh();
        }

        private static void RenderStunStars(
            EffectInstance instance,
            float progress)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixelSize = PixelWorldSize;
            float visibleAlpha =
                ResolveVisibleAlpha(progress);
            Color star = instance.Style.Primary;
            Color glint = instance.Style.Secondary;
            star.a = QuantizeAlpha(visibleAlpha);
            glint.a = QuantizeAlpha(visibleAlpha);

            const int starCount = 4;
            for (int i = 0; i < starCount; i++)
            {
                float angle =
                    progress * Mathf.PI * 2f +
                    i * Mathf.PI * 2f / starCount;
                Vector3 starCenter =
                    center +
                    new Vector3(
                        Mathf.Cos(angle) *
                        instance.Style.Radius * 0.56f,
                        instance.Style.Radius * 0.52f +
                        Mathf.Sin(angle) *
                        instance.Style.Radius * 0.16f,
                        0f);
                AddPixelStar(
                    instance,
                    starCenter,
                    pixelSize,
                    i % 2 == 0 ? glint : star,
                    i % 2 == 0 ? 2 : 1);
            }

            instance.EndPixelMesh();
        }

        private static void RenderBindBlindEye(
            EffectInstance instance,
            float progress)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixelSize = PixelWorldSize;
            float visibleAlpha =
                ResolveVisibleAlpha(progress);
            Color eye = instance.Style.Primary;
            Color cross = Color.Lerp(
                instance.Style.Secondary,
                new Color(1f, 0.18f, 0.26f, 1f),
                0.72f);
            eye.a = QuantizeAlpha(visibleAlpha);
            cross.a = QuantizeAlpha(visibleAlpha);
            const int curvePointCount = 5;

            Vector3 previousUpper = Vector3.zero;
            Vector3 previousLower = Vector3.zero;
            for (int i = 0; i < curvePointCount; i++)
            {
                float t =
                    i / (float)(curvePointCount - 1);
                float x =
                    Mathf.Lerp(
                        -instance.Style.Radius,
                        instance.Style.Radius,
                        t);
                float arc =
                    Mathf.Sin(t * Mathf.PI) *
                    instance.Style.Radius *
                    0.48f;
                Vector3 upper =
                    center + new Vector3(x, arc, 0f);
                Vector3 lower =
                    center + new Vector3(x, -arc, 0f);
                if (i > 0)
                {
                    AddPixelLine(
                        instance,
                        previousUpper,
                        upper,
                        pixelSize,
                        eye);
                    AddPixelLine(
                        instance,
                        previousLower,
                        lower,
                        pixelSize,
                        eye);
                }

                previousUpper = upper;
                previousLower = lower;
            }

            float crossReveal =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        0.28f,
                        0.62f,
                        progress));
            float crossRadius =
                instance.Style.Radius *
                0.88f *
                crossReveal;
            AddPixelLine(
                instance,
                center +
                new Vector3(
                    -crossRadius,
                    -crossRadius * 0.68f,
                    0f),
                center +
                new Vector3(
                    crossRadius,
                    crossRadius * 0.68f,
                    0f),
                pixelSize,
                cross);
            AddPixelLine(
                instance,
                center +
                new Vector3(
                    -crossRadius,
                    crossRadius * 0.68f,
                    0f),
                center +
                new Vector3(
                    crossRadius,
                    -crossRadius * 0.68f,
                    0f),
                pixelSize,
                cross);

            // Reserve explicit endpoint pixels so the last arm cannot be
            // clipped by the bounded mesh budget after the eye outline.
            instance.AddPixelBlock(
                SnapToPixelGrid(
                    center +
                    new Vector3(
                        crossRadius,
                        crossRadius * 0.68f,
                        0f)),
                pixelSize,
                cross);
            instance.AddPixelBlock(
                SnapToPixelGrid(
                    center +
                    new Vector3(
                        -crossRadius,
                        -crossRadius * 0.68f,
                        0f)),
                pixelSize,
                cross);
            instance.AddPixelBlock(
                SnapToPixelGrid(center),
                pixelSize * 1.5f,
                cross);

            instance.EndPixelMesh();
        }

        private static void RenderExplosionCloud(
            EffectInstance instance,
            float progress,
            float alpha)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixelSize = PixelWorldSize;
            float containedRadius = Mathf.Max(
                pixelSize * 2f,
                instance.Style.Radius - pixelSize);
            RenderPixelMushroomExplosion(
                instance,
                center,
                containedRadius,
                pixelSize,
                progress,
                alpha);
            instance.EndPixelMesh();
        }

        /// <summary>
        /// Three readable pixel-art phases shared by impact and area blasts:
        /// a hot flash with a shock ring, a rising fire column, then a broad
        /// rolling cap. Phase-specific geometry keeps the silhouette detailed
        /// without exceeding the fixed WebGL block budget.
        /// </summary>
        private static void RenderPixelMushroomExplosion(
            EffectInstance instance,
            Vector3 center,
            float radius,
            float pixelSize,
            float progress,
            float alpha)
        {
            Color hot = instance.Style.Secondary;
            Color flame = instance.Style.Primary;
            Color ember = Color.Lerp(hot, flame, 0.58f);
            Color smoke = Color.Lerp(
                flame,
                new Color(0.22f, 0.17f, 0.16f, 1f),
                0.72f);
            hot.a = QuantizeAlpha(alpha);
            flame.a = QuantizeAlpha(alpha);
            ember.a = QuantizeAlpha(alpha * 0.94f);
            smoke.a = QuantizeAlpha(alpha * 0.84f);

            float flash = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0f, 0.20f, progress));
            float rise = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.12f, 0.66f, progress));
            float roll = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.22f, 0.82f, progress));

            if (progress < 0.22f)
            {
                float coreRadius = Mathf.Min(
                    radius * Mathf.Lerp(0.12f, 0.30f, flash),
                    pixelSize * 3.25f);
                AddFilledPixelCircle(
                    instance,
                    center,
                    coreRadius,
                    pixelSize,
                    hot);
                AddPixelCircle(
                    instance,
                    center,
                    radius * Mathf.Lerp(0.18f, 0.74f, flash),
                    pixelSize,
                    flame,
                    28);

                const int rayCount = 10;
                for (int ray = 0; ray < rayCount; ray++)
                {
                    float angle = Mathf.PI * 2f * ray / rayCount;
                    Vector3 direction = new Vector3(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle),
                        0f);
                    for (int step = 1; step <= 3; step++)
                    {
                        float distance = radius *
                            Mathf.Lerp(0.24f, 0.82f, step / 3f) *
                            flash;
                        instance.AddPixelBlock(
                            SnapToPixelGrid(center + direction * distance),
                            pixelSize * (step == 1 ? 1.5f : 1f),
                            step == 3 ? ember : hot);
                    }
                }
                return;
            }

            if (progress < 0.54f)
            {
                float shock = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.16f, 0.54f, progress));
                AddPixelCircle(
                    instance,
                    center,
                    radius * Mathf.Lerp(0.48f, 0.94f, shock),
                    pixelSize,
                    ember,
                    24);
            }

            Vector3 capCenter = center + Vector3.up *
                radius * Mathf.Lerp(0.12f, 0.43f, rise);
            float stemBottom = -radius * 0.38f;
            float stemTop = radius * Mathf.Lerp(0.16f, 0.41f, rise);
            const int stemRows = 15;
            for (int row = 0; row < stemRows; row++)
            {
                float t = row / (float)(stemRows - 1);
                float y = Mathf.Lerp(stemBottom, stemTop, t);
                int halfWidth = row < 2 || row > 12 ? 1 : 0;
                int sway = row > 7 && ((row + (int)(progress * 18f)) & 1) == 0
                    ? 1
                    : 0;
                for (int x = -halfWidth; x <= halfWidth; x++)
                {
                    instance.AddPixelBlock(
                        SnapToPixelGrid(
                            center + new Vector3(
                                (x + sway) * pixelSize,
                                y,
                                0f)),
                        pixelSize,
                        row < 5 && x == 0 ? hot : flame);
                }
            }

            // The fully rolled cloud reaches almost the authoritative ring.
            // Its lobes still retain a pixel margin, so the visual reads at
            // the same scale as the gameplay area without crossing it.
            float capWidth = radius * Mathf.Lerp(0.50f, 0.96f, roll);
            float capHeight = radius * Mathf.Lerp(0.20f, 0.36f, roll);
            AddPixelEllipse(
                instance,
                capCenter,
                capWidth,
                capHeight,
                pixelSize,
                smoke,
                28);

            const int lobeCount = 5;
            for (int lobe = 0; lobe < lobeCount; lobe++)
            {
                float normalized = (lobe - 2f) / 2f;
                Vector3 lobeCenter = capCenter + new Vector3(
                    normalized * capWidth * 0.72f,
                    (1f - normalized * normalized) * capHeight * 0.42f,
                    0f);
                AddPixelCircle(
                    instance,
                    lobeCenter,
                    Mathf.Lerp(
                        capHeight * 0.62f,
                        capHeight,
                        1f - Mathf.Abs(normalized)),
                    pixelSize,
                    lobe == 2 && progress < 0.62f ? hot : smoke,
                    7);
            }

            if (progress >= 0.54f)
            {
                AddPixelEllipse(
                    instance,
                    capCenter + Vector3.down * capHeight * 0.12f,
                    capWidth * 0.66f,
                    capHeight * 0.46f,
                    pixelSize,
                    ember,
                    16);
                for (int i = 0; i < 9; i++)
                {
                    float angle = Mathf.PI * 2f * i / 9f;
                    float drift = radius * Mathf.Lerp(
                        0.30f,
                        0.76f,
                        Mathf.Repeat(progress + i * 0.13f, 1f));
                    instance.AddPixelBlock(
                        SnapToPixelGrid(
                            center + new Vector3(
                                Mathf.Cos(angle) * drift,
                                Mathf.Sin(angle) * drift * 0.46f,
                                0f)),
                        pixelSize,
                        i % 3 == 0 ? hot : flame);
                }
            }
        }

        private static void RenderMagnetConvergence(
            EffectInstance instance,
            float progress)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixelSize = PixelWorldSize;
            float convergence =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        0.04f,
                        0.88f,
                        progress));
            Color outer = instance.Style.Primary;
            Color inner = instance.Style.Secondary;
            float visibleAlpha =
                ResolveVisibleAlpha(progress);
            outer.a = QuantizeAlpha(visibleAlpha);
            inner.a = QuantizeAlpha(visibleAlpha);

            const int circleCount = 6;
            for (int i = 0; i < circleCount; i++)
            {
                float angle =
                    Mathf.PI * 2f * i / circleCount +
                    progress *
                    (i % 2 == 0 ? 0.48f : -0.38f);
                float startDistance =
                    instance.Style.Radius *
                    (i % 2 == 0 ? 0.86f : 0.72f);
                float distance =
                    Mathf.Lerp(
                        startDistance,
                        0f,
                        convergence);
                float circleRadius =
                    instance.Style.Radius *
                    Mathf.Lerp(
                        i % 2 == 0 ? 0.16f : 0.12f,
                        0.075f,
                        convergence);
                Vector3 circleCenter =
                    center +
                    new Vector3(
                        Mathf.Cos(angle) * distance,
                        Mathf.Sin(angle) * distance * 0.76f,
                        0f);
                AddPixelCircle(
                    instance,
                    circleCenter,
                    circleRadius,
                    pixelSize,
                    i % 2 == 0 ? inner : outer,
                    8);
            }

            if (progress > 0.52f)
            {
                float coreRadius =
                    instance.Style.Radius *
                    Mathf.Lerp(
                        0.04f,
                        0.18f,
                        Mathf.InverseLerp(
                            0.52f,
                            1f,
                            progress));
                AddPixelCircle(
                    instance,
                    center,
                    coreRadius,
                    pixelSize,
                    inner,
                    12);
            }

            instance.EndPixelMesh();
        }

        private static void RenderAirborneWhirlwind(
            EffectInstance instance,
            float progress)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixelSize = PixelWorldSize;
            float build =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(
                        0f,
                        0.58f,
                        progress));
            Color lower = instance.Style.Primary;
            Color upper = instance.Style.Secondary;
            float visibleAlpha =
                ResolveVisibleAlpha(progress);
            lower.a = QuantizeAlpha(visibleAlpha);
            upper.a = QuantizeAlpha(visibleAlpha);

            const int maximumSpiralPoints = 24;
            int visiblePointCount =
                Mathf.Clamp(
                    2 +
                    Mathf.FloorToInt(
                        build *
                        (maximumSpiralPoints - 2)),
                    2,
                    maximumSpiralPoints);
            float rotation =
                progress * Mathf.PI * 4.5f;
            Vector3 previous = Vector3.zero;
            for (int i = 0;
                 i < visiblePointCount;
                 i++)
            {
                float heightT =
                    i /
                    (float)(maximumSpiralPoints - 1);
                float horizontalAmplitude =
                    instance.Style.Radius *
                    Mathf.Lerp(
                        0.08f,
                        0.68f,
                        heightT);
                float y =
                    Mathf.Lerp(
                        -instance.Style.Radius * 0.48f,
                        instance.Style.Radius * 0.72f,
                        heightT);
                float spiralAngle =
                    heightT * Mathf.PI * 6f +
                    rotation;
                Vector3 point =
                    center +
                    new Vector3(
                        Mathf.Sin(spiralAngle) *
                        horizontalAmplitude,
                        y,
                        0f);
                Color color =
                    Color.Lerp(lower, upper, heightT);
                if (i == 0)
                {
                    instance.AddPixelBlock(
                        SnapToPixelGrid(point),
                        pixelSize,
                        color);
                }
                else
                {
                    AddPixelLine(
                        instance,
                        previous,
                        point,
                        pixelSize,
                        color);
                }

                previous = point;
            }

            // Two loose motes orbit the completed upper funnel without
            // appearing before its lower half has formed.
            if (build > 0.58f)
            {
                for (int i = 0; i < 2; i++)
                {
                    float lift =
                        Mathf.Repeat(
                            progress * 1.45f +
                            i * 0.5f,
                            1f);
                    Vector3 mote =
                        center +
                        new Vector3(
                            Mathf.Sin(
                                lift * Mathf.PI * 4f + i) *
                            instance.Style.Radius * 0.18f,
                            Mathf.Lerp(
                                -instance.Style.Radius * 0.38f,
                                instance.Style.Radius * 0.72f,
                                lift),
                            0f);
                    instance.AddPixelBlock(
                        SnapToPixelGrid(mote),
                        pixelSize,
                        upper);
                }
            }

            instance.EndPixelMesh();
        }

        private static void AddFilledPixelCircle(
            EffectInstance instance,
            Vector3 center,
            float radius,
            float pixelSize,
            Color color)
        {
            int gridRadius = Mathf.Max(
                1,
                Mathf.CeilToInt(radius / pixelSize));
            float squaredRadius =
                radius * radius +
                pixelSize * pixelSize * 0.35f;
            for (int y = -gridRadius;
                 y <= gridRadius;
                 y++)
            {
                for (int x = -gridRadius;
                     x <= gridRadius;
                     x++)
                {
                    Vector3 offset =
                        new Vector3(
                            x * pixelSize,
                            y * pixelSize,
                            0f);
                    if (offset.sqrMagnitude > squaredRadius)
                    {
                        continue;
                    }

                    instance.AddPixelBlock(
                        SnapToPixelGrid(center + offset),
                        pixelSize,
                        color);
                }
            }
        }

        private static void RenderPierceArrow(
            EffectInstance instance,
            float progress,
            float alpha)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float radius = instance.Style.Radius;
            float reveal = Mathf.SmoothStep(0f, 1f, progress);
            Vector3 tail =
                center + Vector3.left * radius;
            Vector3 tip =
                Vector3.Lerp(
                    tail,
                    center + Vector3.right * radius,
                    reveal);
            float pixelSize = QuantizeSize(
                instance.Style.Width);
            Color shaftColor = instance.Style.Primary;
            Color tipColor = instance.Style.Secondary;
            shaftColor.a = QuantizeAlpha(alpha);
            tipColor.a = QuantizeAlpha(alpha);

            AddPixelLine(
                instance,
                tail,
                tip,
                pixelSize,
                shaftColor);

            // The head grows only after the shaft has visibly travelled from
            // left to right. This avoids the old centred "pop" impression.
            float headReveal =
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                    0.35f,
                    0.72f,
                    progress));
            float headLength =
                radius * 0.38f * headReveal;
            float headHeight =
                radius * 0.32f * headReveal;
            AddPixelLine(
                instance,
                tip,
                tip + new Vector3(-headLength, headHeight, 0f),
                pixelSize,
                tipColor);
            AddPixelLine(
                instance,
                tip,
                tip + new Vector3(-headLength, -headHeight, 0f),
                pixelSize,
                tipColor);

            // A small cold glint travels just behind the arrow head.
            if (progress > 0.28f)
            {
                Vector3 glint =
                    tip + Vector3.left * pixelSize * 2f;
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        glint + Vector3.up * pixelSize),
                    pixelSize,
                    tipColor);
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        glint + Vector3.down * pixelSize),
                    pixelSize,
                    tipColor);
            }

            instance.EndPixelMesh();
        }

        private static void RenderBurnFlame(
            EffectInstance instance,
            float progress,
            float alpha)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixelSize = QuantizeSize(
                instance.Style.Radius / 5.5f);
            float growth =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0f, 0.20f, progress));
            float phase = progress * Mathf.PI * 8f;
            int tipSway = Mathf.RoundToInt(
                Mathf.Sin(phase) * 1.15f);
            int middleSway = Mathf.RoundToInt(
                Mathf.Sin(phase * 0.72f + 1.3f) * 0.75f);
            Vector3 basePosition =
                center + Vector3.down * pixelSize * 2.5f;
            Color outer = instance.Style.Primary;
            Color inner = instance.Style.Secondary;
            outer.a = QuantizeAlpha(alpha);
            inner.a = QuantizeAlpha(alpha);

            // Wide base, broken side tongues, and a swaying tapered crown
            // create a flame silhouette instead of a closed droplet outline.
            for (int row = 0; row <= 7; row++)
            {
                if (row / 7f > growth)
                {
                    continue;
                }

                int halfWidth;
                int rowShift;
                if (row <= 1)
                {
                    halfWidth = 3;
                    rowShift = 0;
                }
                else if (row <= 3)
                {
                    halfWidth = 2;
                    rowShift = middleSway;
                }
                else if (row <= 5)
                {
                    halfWidth = 1;
                    rowShift = tipSway;
                }
                else
                {
                    halfWidth = 0;
                    rowShift = tipSway;
                }

                for (int column = -halfWidth;
                     column <= halfWidth;
                     column++)
                {
                    bool hotCore =
                        row <= 4 &&
                        Mathf.Abs(column) <=
                        (row <= 1 ? 1 : 0);
                    Color color = hotCore ? inner : outer;
                    instance.AddPixelBlock(
                        SnapToPixelGrid(
                            basePosition +
                            new Vector3(
                                (column + rowShift) * pixelSize,
                                row * pixelSize,
                                0f)),
                        pixelSize,
                        color);
                }
            }

            // Alternating side licks make the outline visibly flicker.
            int lickSide = Mathf.Sin(phase * 1.17f) >= 0f ? 1 : -1;
            for (int row = 0; row < 3; row++)
            {
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        basePosition +
                        new Vector3(
                            lickSide * (4 - row) * pixelSize,
                            (row + 1) * pixelSize,
                            0f)),
                    pixelSize,
                    outer);
            }

            instance.EndPixelMesh();
        }

        private static void RenderSlowSnail(
            EffectInstance instance,
            float progress,
            float alpha)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixelSize = QuantizeSize(
                instance.Style.Radius / 5.25f);
            float reveal =
                Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0f, 0.22f, progress));
            float crawl =
                Mathf.Lerp(-pixelSize, pixelSize, progress);
            Vector3 origin =
                center +
                Vector3.right * crawl +
                Vector3.down * pixelSize;
            Color body = instance.Style.Primary;
            Color shell = Color.Lerp(
                instance.Style.Primary,
                instance.Style.Secondary,
                0.42f);
            Color highlight = instance.Style.Secondary;
            body.a = QuantizeAlpha(alpha);
            shell.a = QuantizeAlpha(alpha);
            highlight.a = QuantizeAlpha(alpha);

            int bodyLength =
                Mathf.Max(2, Mathf.RoundToInt(9f * reveal));
            for (int i = 0; i < bodyLength; i++)
            {
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        origin +
                        Vector3.right * (i - 4) * pixelSize),
                    pixelSize,
                    body);
            }

            if (reveal > 0.35f)
            {
                Vector3 shellCenter =
                    origin +
                    new Vector3(-pixelSize, pixelSize * 2f, 0f);
                AddPixelCircle(
                    instance,
                    shellCenter,
                    pixelSize * 2.55f * reveal,
                    pixelSize,
                    shell,
                    16);
                AddPixelLine(
                    instance,
                    shellCenter + Vector3.right * pixelSize,
                    shellCenter + Vector3.up * pixelSize,
                    pixelSize,
                    highlight);
                AddPixelLine(
                    instance,
                    shellCenter + Vector3.up * pixelSize,
                    shellCenter + Vector3.left * pixelSize,
                    pixelSize,
                    highlight);
                instance.AddPixelBlock(
                    SnapToPixelGrid(shellCenter),
                    pixelSize,
                    highlight);
            }

            if (reveal > 0.58f)
            {
                Vector3 head =
                    origin + Vector3.right * pixelSize * 4f;
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        head + Vector3.up * pixelSize),
                    pixelSize,
                    body);
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        head + Vector3.up * pixelSize * 2f),
                    pixelSize,
                    body);

                Vector3 leftEye =
                    head +
                    new Vector3(
                        -pixelSize * 0.35f,
                        pixelSize * 3.55f,
                        0f);
                Vector3 rightEye =
                    head +
                    new Vector3(
                        pixelSize * 1.35f,
                        pixelSize * 3.55f,
                        0f);
                AddPixelLine(
                    instance,
                    head + Vector3.up * pixelSize * 2f,
                    leftEye,
                    pixelSize,
                    body);
                AddPixelLine(
                    instance,
                    head + Vector3.up * pixelSize * 2f,
                    rightEye,
                    pixelSize,
                    body);
                instance.AddPixelBlock(
                    SnapToPixelGrid(leftEye),
                    pixelSize,
                    highlight);
                instance.AddPixelBlock(
                    SnapToPixelGrid(rightEye),
                    pixelSize,
                    highlight);
            }

            instance.EndPixelMesh();
        }

        private static void RenderPoisonBubbles(
            EffectInstance instance,
            float progress,
            float alpha)
        {
            instance.BeginPixelMesh();
            Vector3 center = ResolveCenter(instance);
            float pixelSize = QuantizeSize(
                instance.Style.Radius / 7f);
            Color poolColor = instance.Style.Primary;
            Color bubbleColor = instance.Style.Secondary;
            poolColor.a = QuantizeAlpha(alpha);
            bubbleColor.a = QuantizeAlpha(alpha);

            // The pool stays grounded while bubbles loop upward at staggered
            // phases, so the effect reads as bubbling poison at every frame.
            for (int i = -4; i <= 4; i++)
            {
                int lift =
                    Mathf.Abs(i) == 4 ? 1 : 0;
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        center +
                        new Vector3(
                            i * pixelSize,
                            (-2 + lift) * pixelSize,
                            0f)),
                    pixelSize,
                    poolColor);
            }

            for (int i = 0;
                 i < PoisonBubbleXOffsets.Length;
                 i++)
            {
                float bubbleProgress =
                    Mathf.Repeat(
                        progress +
                        PoisonBubblePhaseOffsets[i],
                        1f);
                float rise =
                    Mathf.SmoothStep(0f, 1f, bubbleProgress);
                Vector3 bubbleCenter =
                    center +
                    new Vector3(
                        PoisonBubbleXOffsets[i] * pixelSize +
                        Mathf.Sin(
                            bubbleProgress * Mathf.PI * 2f + i) *
                        pixelSize * 0.45f,
                        Mathf.Lerp(
                            -pixelSize,
                            instance.Style.Radius * 1.05f,
                            rise),
                        0f);
                float bubbleRadius =
                    pixelSize *
                    Mathf.Lerp(
                        i % 2 == 0 ? 1.25f : 1.7f,
                        i % 2 == 0 ? 1.75f : 2.2f,
                        rise);
                if (bubbleProgress < 0.84f)
                {
                    AddPixelCircle(
                        instance,
                        bubbleCenter,
                        bubbleRadius,
                        pixelSize,
                        bubbleColor,
                        i % 2 == 0 ? 8 : 12);
                }
                else
                {
                    float popDistance =
                        pixelSize *
                        Mathf.InverseLerp(
                            0.84f,
                            1f,
                            bubbleProgress) *
                        2.5f;
                    instance.AddPixelBlock(
                        SnapToPixelGrid(
                            bubbleCenter + Vector3.left * popDistance),
                        pixelSize,
                        bubbleColor);
                    instance.AddPixelBlock(
                        SnapToPixelGrid(
                            bubbleCenter + Vector3.right * popDistance),
                        pixelSize,
                        bubbleColor);
                    instance.AddPixelBlock(
                        SnapToPixelGrid(
                            bubbleCenter + Vector3.up * popDistance),
                        pixelSize,
                        bubbleColor);
                }
            }

            instance.EndPixelMesh();
        }

        private static void AddPixelLine(
            EffectInstance instance,
            Vector3 start,
            Vector3 end,
            float pixelSize,
            Color color)
        {
            int steps = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    Vector3.Distance(start, end) /
                    PixelWorldSize));
            for (int i = 0; i <= steps; i++)
            {
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        Vector3.Lerp(
                            start,
                            end,
                            i / (float)steps)),
                    pixelSize,
                    color);
            }
        }

        private static void AddPixelCircle(
            EffectInstance instance,
            Vector3 center,
            float radius,
            float pixelSize,
            Color color,
            int pointCount)
        {
            int count = Mathf.Max(4, pointCount);
            for (int i = 0; i < count; i++)
            {
                float angle =
                    Mathf.PI * 2f * i / count;
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        center +
                        new Vector3(
                            Mathf.Cos(angle) * radius,
                            Mathf.Sin(angle) * radius,
                            0f)),
                    pixelSize,
                    color);
            }
        }

        private static void AddPixelEllipse(
            EffectInstance instance,
            Vector3 center,
            float horizontalRadius,
            float verticalRadius,
            float pixelSize,
            Color color,
            int pointCount)
        {
            int count = Mathf.Max(4, pointCount);
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * 2f * i / count;
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        center + new Vector3(
                            Mathf.Cos(angle) * horizontalRadius,
                            Mathf.Sin(angle) * verticalRadius,
                            0f)),
                    pixelSize,
                    color);
            }
        }

        private static void AddPixelStar(
            EffectInstance instance,
            Vector3 center,
            float pixelSize,
            Color color,
            int armLength)
        {
            int length = Mathf.Max(1, armLength);
            instance.AddPixelBlock(
                SnapToPixelGrid(center),
                pixelSize,
                color);
            for (int step = 1; step <= length; step++)
            {
                float distance = pixelSize * step;
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        center + Vector3.left * distance),
                    pixelSize,
                    color);
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        center + Vector3.right * distance),
                    pixelSize,
                    color);
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        center + Vector3.up * distance),
                    pixelSize,
                    color);
                instance.AddPixelBlock(
                    SnapToPixelGrid(
                        center + Vector3.down * distance),
                    pixelSize,
                    color);
            }
        }

        /// <summary>
        /// Matches the original Archer showcase split read: six crisp pixels
        /// fan away from the split point, shrink, and fade. It stays inside the
        /// shared bounded pool, so Stage 01 does not add per-burst behaviours
        /// or coroutines.
        /// </summary>
        private static void RenderSplitBurst(
            EffectInstance instance,
            float progress)
        {
            instance.Line.positionCount = 0;
            instance.Line.enabled = false;
            instance.ClearPixelMesh();
            instance.PixelRenderer.enabled = false;
            instance.SetSplitSparksVisible(true);

            Vector3 center =
                SnapToPixelGrid(ResolveCenter(instance));
            float alpha = QuantizeAlpha(
                ResolveVisibleAlpha(progress));
            float distance = QuantizeSize(Mathf.Lerp(
                0.04f,
                instance.Style.Radius * 0.58f,
                progress));
            float size = QuantizeSize(Mathf.Lerp(
                0.09f,
                0.025f,
                progress));
            for (int i = 0; i < instance.SplitSparks.Length; i++)
            {
                float angle =
                    Mathf.PI * 2f * i /
                    instance.SplitSparks.Length +
                    progress * 0.35f;
                SpriteRenderer spark = instance.SplitSparks[i];
                spark.transform.position =
                    SnapToPixelGrid(
                        center +
                    new Vector3(
                        Mathf.Cos(angle) * distance,
                        Mathf.Sin(angle) * distance,
                            0f));
                spark.transform.localScale =
                    new Vector3(size, size, 1f);
                Color color = instance.Style.Primary;
                color.a = alpha;
                spark.color = color;
            }
        }

        private static Vector3 SnapToPixelGrid(
            Vector3 position)
        {
            position.x =
                Mathf.Round(position.x / PixelWorldSize) *
                PixelWorldSize;
            position.y =
                Mathf.Round(position.y / PixelWorldSize) *
                PixelWorldSize;
            position.z = -0.18f;
            return position;
        }

        private static float QuantizeSize(float size)
        {
            return Mathf.Max(
                PixelWorldSize,
                Mathf.Round(size / PixelWorldSize) *
                PixelWorldSize);
        }

        private static float QuantizeAlpha(float alpha)
        {
            return Mathf.Clamp01(
                Mathf.Round(alpha * 4f) / 4f);
        }

        private static float ResolveVisibleAlpha(float progress)
        {
            return 1f -
                   Mathf.SmoothStep(
                       0f,
                       1f,
                       Mathf.InverseLerp(
                           0.78f,
                           1f,
                           progress));
        }

        private static int BuildArc(
            EffectInstance instance,
            float progress,
            int count,
            float heightScale)
        {
            Vector3 delta =
                instance.End - instance.Start;
            bool local =
                delta.sqrMagnitude < 0.0001f;
            if (local)
            {
                delta = new Vector3(
                    instance.Style.Radius * 1.2f,
                    0f,
                    0f);
            }

            Vector3 start = local
                ? instance.Start - delta * 0.5f
                : instance.Start;
            float visible = Mathf.Max(0.08f, progress);
            for (int i = 0; i < count; i++)
            {
                float t =
                    i / (float)(count - 1) * visible;
                Vector3 point = start + delta * t;
                point.y +=
                    Mathf.Sin(t * Mathf.PI) *
                    instance.Style.MotionHeight *
                    heightScale;
                instance.Points[i] = point;
            }

            return count;
        }

        private static int BuildSlash(
            EffectInstance instance,
            float progress)
        {
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(0.45f, 1.15f, progress);
            Vector3 center = ResolveCenter(instance);
            instance.Points[0] =
                center + new Vector3(-radius, radius * 0.62f);
            instance.Points[1] =
                center + new Vector3(radius, -radius * 0.62f);
            instance.Points[2] =
                center + new Vector3(radius * 0.36f, radius * 0.25f);
            instance.Points[3] =
                center + new Vector3(-radius * 0.42f, -radius * 0.3f);
            return 4;
        }

        private static int BuildStreak(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            Vector3 direction =
                instance.End - instance.Start;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Vector3.right;
            }

            direction.Normalize();
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(0.35f, 1.4f, progress);
            Vector3 normal =
                new Vector3(-direction.y, direction.x);
            instance.Points[0] =
                center - direction * radius + normal * 0.12f;
            instance.Points[1] =
                center + direction * radius;
            instance.Points[2] =
                center - direction * radius - normal * 0.12f;
            return 3;
        }

        private static int BuildRing(
            EffectInstance instance,
            float progress,
            float minimumScale,
            float maximumScale)
        {
            Vector3 center = ResolveCenter(instance);
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(
                    minimumScale,
                    maximumScale,
                    progress);
            const int count = MaximumLinePoints;
            for (int i = 0; i < count; i++)
            {
                float angle =
                    i /
                    (float)(count - 1) *
                    Mathf.PI *
                    2f;
                instance.Points[i] =
                    center +
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius * 0.62f,
                        0f);
            }

            return count;
        }

        private static int BuildClock(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(0.72f, 1.15f, progress);
            const int ringCount = 18;
            for (int i = 0; i < ringCount; i++)
            {
                float angle =
                    i /
                    (float)(ringCount - 1) *
                    Mathf.PI *
                    2f;
                instance.Points[i] =
                    center +
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius,
                        0f);
            }

            float handAngle =
                -Mathf.PI * 0.5f +
                progress * Mathf.PI * 3f;
            instance.Points[ringCount] = center;
            instance.Points[ringCount + 1] =
                center +
                new Vector3(
                    Mathf.Cos(handAngle) * radius * 0.72f,
                    Mathf.Sin(handAngle) * radius * 0.72f,
                    0f);
            return ringCount + 2;
        }

        private static int BuildRune(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(0.5f, 1.05f, progress);
            float rotation =
                progress * Mathf.PI * 0.85f;
            const int count = 9;
            for (int i = 0; i < count; i++)
            {
                float angle =
                    rotation +
                    i / 8f * Mathf.PI * 2f;
                float scale = (i & 1) == 0 ? 1f : 0.42f;
                instance.Points[i] =
                    center +
                    new Vector3(
                        Mathf.Cos(angle) * radius * scale,
                        Mathf.Sin(angle) * radius * scale,
                        0f);
            }

            return count;
        }

        private static int BuildChain(
            EffectInstance instance,
            float progress)
        {
            Vector3 start = instance.Start;
            Vector3 end = instance.End;
            if ((end - start).sqrMagnitude < 0.0001f)
            {
                float radius = instance.Style.Radius;
                start += new Vector3(-radius, 0f);
                end += new Vector3(radius, 0f);
            }

            Vector3 delta = end - start;
            Vector3 normal =
                new Vector3(-delta.y, delta.x).normalized;
            const int count = 14;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float wave =
                    Mathf.Sin(
                        t * Mathf.PI * 6f +
                        progress * Mathf.PI * 2f);
                instance.Points[i] =
                    start +
                    delta * t +
                    normal * wave * 0.1f;
            }

            return count;
        }

        private static int BuildLaunch(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            float height =
                instance.Style.MotionHeight *
                Mathf.Lerp(0.25f, 1.15f, progress);
            float radius = instance.Style.Radius * 0.42f;
            instance.Points[0] =
                center + new Vector3(-radius, 0f);
            instance.Points[1] =
                center + new Vector3(0f, height);
            instance.Points[2] =
                center + new Vector3(radius, 0f);
            instance.Points[3] =
                center + new Vector3(0f, height * 0.55f);
            instance.Points[4] =
                center + new Vector3(-radius * 0.42f, height * 0.72f);
            instance.Points[5] =
                center + new Vector3(0f, height);
            instance.Points[6] =
                center + new Vector3(radius * 0.42f, height * 0.72f);
            return 7;
        }

        private static int BuildLightning(
            EffectInstance instance,
            float progress)
        {
            Vector3 start = instance.Start;
            Vector3 end = instance.End;
            if ((end - start).sqrMagnitude < 0.0001f)
            {
                float radius = instance.Style.Radius;
                start += new Vector3(-radius, radius * 0.35f);
                end += new Vector3(radius, -radius * 0.35f);
            }

            Vector3 delta = end - start;
            Vector3 normal =
                new Vector3(-delta.y, delta.x).normalized;
            const int count = 12;
            for (int i = 0; i < count; i++)
            {
                float t =
                    i / (float)(count - 1) *
                    Mathf.Max(0.08f, progress);
                float jitter =
                    i == 0 || i == count - 1
                        ? 0f
                        : HashSigned(
                              instance.Sequence,
                              i) *
                          instance.Style.Radius *
                          0.22f *
                          (1f - progress * 0.35f);
                instance.Points[i] =
                    start + delta * t + normal * jitter;
            }

            return count;
        }

        private static int BuildStar(
            EffectInstance instance,
            float progress,
            int arms)
        {
            Vector3 center = ResolveCenter(instance);
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(0.35f, 1.2f, progress);
            int count = Mathf.Min(
                MaximumLinePoints,
                arms * 2 + 1);
            for (int i = 0; i < count; i++)
            {
                float angle =
                    i /
                    (float)(count - 1) *
                    Mathf.PI *
                    2f;
                float scale = (i & 1) == 0 ? 1f : 0.3f;
                instance.Points[i] =
                    center +
                    new Vector3(
                        Mathf.Cos(angle) * radius * scale,
                        Mathf.Sin(angle) * radius * scale,
                        0f);
            }

            return count;
        }

        private static int BuildEcho(
            EffectInstance instance,
            float progress)
        {
            Vector3 original = instance.Start;
            Vector3 direction =
                instance.End - instance.Start;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Vector3.right;
            }

            Vector3 offset =
                -direction.normalized *
                instance.Style.Radius *
                progress;
            instance.Start = original + offset;
            instance.End = instance.Start;
            int count = BuildRing(
                instance,
                progress,
                0.45f,
                1.15f);
            instance.Start = original;
            instance.End =
                original + direction;
            return count;
        }

        private static int BuildVortex(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            const int count = 20;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float angle =
                    t * Mathf.PI * 4.5f -
                    progress * Mathf.PI * 2f;
                float radius =
                    instance.Style.Radius *
                    Mathf.Lerp(1.15f, 0.08f, t);
                instance.Points[i] =
                    center +
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius * 0.72f,
                        0f);
            }

            return count;
        }

        private static int BuildMirror(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(0.5f, 1.1f, progress);
            instance.Points[0] =
                center + new Vector3(-radius, 0f);
            instance.Points[1] =
                center + new Vector3(0f, radius * 0.75f);
            instance.Points[2] =
                center + new Vector3(radius, 0f);
            instance.Points[3] =
                center + new Vector3(0f, -radius * 0.75f);
            instance.Points[4] =
                center + new Vector3(-radius, 0f);
            instance.Points[5] =
                center + new Vector3(radius, 0f);
            return 6;
        }

        private static int BuildPolygon(
            EffectInstance instance,
            float progress,
            int sides)
        {
            Vector3 center = ResolveCenter(instance);
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(0.5f, 1.05f, progress);
            int count = sides + 1;
            for (int i = 0; i < count; i++)
            {
                float angle =
                    -Mathf.PI * 0.5f +
                    i /
                    (float)sides *
                    Mathf.PI *
                    2f;
                instance.Points[i] =
                    center +
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius,
                        0f);
            }

            return count;
        }

        private static int BuildCorrosion(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(1.15f, 0.24f, progress);
            const int count = 16;
            for (int i = 0; i < count; i++)
            {
                float angle =
                    i /
                    (float)(count - 1) *
                    Mathf.PI *
                    2f;
                float noise =
                    0.72f +
                    (HashSigned(instance.Sequence, i) + 1f) *
                    0.18f;
                float drip =
                    Mathf.Sin(angle) < -0.45f
                        ? 1.25f
                        : 1f;
                instance.Points[i] =
                    center +
                    new Vector3(
                        Mathf.Cos(angle) * radius * noise,
                        Mathf.Sin(angle) * radius * noise * drip,
                        0f);
            }

            return count;
        }

        private static int BuildOrbit(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            float radius = instance.Style.Radius;
            const int count = 20;
            float rotation =
                progress * Mathf.PI * 2.5f;
            for (int i = 0; i < count; i++)
            {
                float angle =
                    rotation +
                    i /
                    (float)(count - 1) *
                    Mathf.PI *
                    2f;
                instance.Points[i] =
                    center +
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius * 0.45f,
                        0f);
            }

            return count;
        }

        private static int BuildHeart(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            float scale =
                instance.Style.Radius *
                Mathf.Lerp(0.38f, 0.075f, progress);
            center.y +=
                instance.Style.MotionHeight *
                progress;
            const int count = 20;
            for (int i = 0; i < count; i++)
            {
                float t =
                    i /
                    (float)(count - 1) *
                    Mathf.PI *
                    2f;
                float x =
                    16f * Mathf.Pow(Mathf.Sin(t), 3f);
                float y =
                    13f * Mathf.Cos(t) -
                    5f * Mathf.Cos(2f * t) -
                    2f * Mathf.Cos(3f * t) -
                    Mathf.Cos(4f * t);
                instance.Points[i] =
                    center +
                    new Vector3(
                        x * scale * 0.06f,
                        y * scale * 0.06f,
                        0f);
            }

            return count;
        }

        private static int BuildFear(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(0.4f, 1.12f, progress);
            const int count = 13;
            for (int i = 0; i < count; i++)
            {
                float t = i / 12f;
                float x = Mathf.Lerp(-radius, radius, t);
                float y =
                    Mathf.Sin(t * Mathf.PI * 5f) *
                    radius *
                    (0.28f +
                     Mathf.Abs(0.5f - t) * 0.5f);
                instance.Points[i] =
                    center + new Vector3(x, y, 0f);
            }

            return count;
        }

        private static int BuildBranch(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(0.3f, 1.1f, progress);
            float height =
                radius +
                instance.Style.MotionHeight * progress;
            instance.Points[0] =
                center + new Vector3(0f, -radius * 0.65f);
            instance.Points[1] =
                center + new Vector3(0f, 0f);
            instance.Points[2] =
                center + new Vector3(-radius, height * 0.72f);
            instance.Points[3] =
                center + new Vector3(0f, 0f);
            instance.Points[4] =
                center + new Vector3(radius, height * 0.72f);
            instance.Points[5] =
                center + new Vector3(0f, 0f);
            instance.Points[6] =
                center + new Vector3(0f, height);
            return 7;
        }

        private static int BuildLance(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(0.3f, 1.35f, progress);
            Vector3 direction = instance.End - instance.Start;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Vector3.right;
            }

            direction.Normalize();
            Vector3 normal =
                new Vector3(-direction.y, direction.x);
            Vector3 tip = center + direction * radius;
            instance.Points[0] = center - direction * radius;
            instance.Points[1] = tip;
            instance.Points[2] =
                tip - direction * radius * 0.34f +
                normal * radius * 0.28f;
            instance.Points[3] = tip;
            instance.Points[4] =
                tip - direction * radius * 0.34f -
                normal * radius * 0.28f;
            return 5;
        }

        private static int BuildFlame(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(0.36f, 1.08f, progress);
            center.y +=
                instance.Style.MotionHeight *
                progress * 0.45f;
            const int count = 18;
            for (int i = 0; i < count; i++)
            {
                float angle =
                    i /
                    (float)(count - 1) *
                    Mathf.PI *
                    2f;
                float taper =
                    0.52f +
                    0.48f *
                    Mathf.Max(0f, -Mathf.Sin(angle));
                instance.Points[i] =
                    center +
                    new Vector3(
                        Mathf.Cos(angle) * radius * taper,
                        Mathf.Sin(angle) * radius +
                        Mathf.Max(0f, Mathf.Sin(angle)) *
                        radius * 0.72f,
                        0f);
            }

            return count;
        }

        private static int BuildHourglass(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(0.55f, 1.05f, progress);
            instance.Points[0] =
                center + new Vector3(-radius, radius);
            instance.Points[1] =
                center + new Vector3(radius, radius);
            instance.Points[2] = center;
            instance.Points[3] =
                center + new Vector3(-radius, -radius);
            instance.Points[4] =
                center + new Vector3(radius, -radius);
            return 5;
        }

        private static int BuildImpact(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(0.4f, 1.25f, progress);
            instance.Points[0] =
                center + new Vector3(-radius, radius * 0.55f);
            instance.Points[1] = center;
            instance.Points[2] =
                center + new Vector3(-radius, -radius * 0.55f);
            instance.Points[3] = center;
            instance.Points[4] =
                center + new Vector3(radius, 0f);
            instance.Points[5] =
                center + new Vector3(radius * 0.52f, radius * 0.4f);
            instance.Points[6] =
                center + new Vector3(radius, 0f);
            return 7;
        }

        private static int BuildTarget(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(0.55f, 1.1f, progress);
            const int ringCount = 16;
            for (int i = 0; i < ringCount; i++)
            {
                float angle =
                    i /
                    (float)(ringCount - 1) *
                    Mathf.PI *
                    2f;
                instance.Points[i] =
                    center +
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius,
                        0f);
            }

            instance.Points[ringCount] =
                center + new Vector3(-radius * 1.25f, 0f);
            instance.Points[ringCount + 1] =
                center + new Vector3(radius * 1.25f, 0f);
            instance.Points[ringCount + 2] = center;
            instance.Points[ringCount + 3] =
                center + new Vector3(0f, radius * 1.25f);
            instance.Points[ringCount + 4] =
                center + new Vector3(0f, -radius * 1.25f);
            return ringCount + 5;
        }

        private static int BuildToxic(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            float maximumRadius =
                instance.Style.Radius *
                Mathf.Lerp(0.4f, 1.15f, progress);
            const int count = 20;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float angle =
                    t * Mathf.PI * 4f +
                    progress * Mathf.PI;
                float radius =
                    maximumRadius *
                    Mathf.Lerp(0.18f, 1f, t);
                instance.Points[i] =
                    center +
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius * 0.72f +
                        t * instance.Style.MotionHeight,
                        0f);
            }

            return count;
        }

        private static int BuildStunBurst(
            EffectInstance instance,
            float progress)
        {
            Vector3 center = ResolveCenter(instance);
            float radius =
                instance.Style.Radius *
                Mathf.Lerp(0.35f, 1.18f, progress);
            instance.Points[0] =
                center + new Vector3(-radius, radius * 0.4f);
            instance.Points[1] =
                center + new Vector3(-radius * 0.25f, 0f);
            instance.Points[2] =
                center + new Vector3(-radius * 0.5f, -radius);
            instance.Points[3] =
                center + new Vector3(radius * 0.12f, -radius * 0.25f);
            instance.Points[4] =
                center + new Vector3(radius * 0.38f, radius);
            instance.Points[5] =
                center + new Vector3(radius, -radius * 0.38f);
            return 6;
        }

        private static Vector3 ResolveCenter(
            EffectInstance instance)
        {
            return Vector3.Lerp(
                instance.Start,
                instance.End,
                0.5f);
        }

        private static float HashSigned(
            uint sequence,
            int index)
        {
            uint value =
                sequence * 747796405u +
                (uint)index * 2891336453u +
                277803737u;
            value ^= value >> 16;
            value *= 2246822519u;
            value ^= value >> 13;
            return (value & 0xffffu) /
                   32767.5f -
                   1f;
        }

        private sealed class EffectInstance
        {
            public EffectInstance(
                GameObject host,
                LineRenderer line,
                MeshRenderer pixelRenderer,
                Mesh pixelMesh,
                SpriteRenderer[] splitSparks,
                SpriteRenderer areaIndicator)
            {
                Host = host;
                Line = line;
                PixelRenderer = pixelRenderer;
                PixelMesh = pixelMesh;
                SplitSparks =
                    splitSparks ?? new SpriteRenderer[0];
                AreaIndicator = areaIndicator;
                Points =
                    new Vector3[MaximumLinePoints];
                PixelVertices =
                    new Vector3[MaximumPixelBlocks * 4];
                PixelColors =
                    new Color32[MaximumPixelBlocks * 4];
                PixelUvs =
                    new Vector2[MaximumPixelBlocks * 4];
                PixelTriangles =
                    new int[MaximumPixelBlocks * 6];
            }

            public GameObject Host { get; }
            public LineRenderer Line { get; }
            public MeshRenderer PixelRenderer { get; }
            public Mesh PixelMesh { get; }
            public SpriteRenderer[] SplitSparks { get; }
            public SpriteRenderer AreaIndicator { get; }
            public Vector3[] Points { get; }
            public Vector3[] PixelVertices { get; }
            public Color32[] PixelColors { get; }
            public Vector2[] PixelUvs { get; }
            public int[] PixelTriangles { get; }
            public StageOneCardEffectStyle Style { get; private set; }
            public Vector3 Start { get; set; }
            public Vector3 End { get; set; }
            public float StartTime { get; private set; }
            public uint Sequence { get; private set; }
            public bool Active { get; private set; }
            public int PixelBlockCount { get; private set; }
            public float AreaRadius { get; private set; }
            private StageOneEnemyView followEnemy;
            private int followEnemyId = -1;
            private Vector3 followOffset;
            private Vector3 lastFollowAnchor;

            public void Play(
                StageOneCardEffectStyle style,
                Vector3 start,
                Vector3 end,
                float startTime,
                uint sequence)
            {
                Style = style;
                Start = start;
                End = end;
                StartTime = startTime;
                Sequence = sequence;
                Active = true;
                AreaRadius = 0f;
                followEnemy = null;
                followEnemyId = -1;
                followOffset = Vector3.zero;
                lastFollowAnchor = Vector3.zero;
                Host.SetActive(true);
                bool isSplitBurst =
                    style.Shape ==
                    StageOneCardEffectShape.Branch;
                Line.enabled = false;
                PixelRenderer.enabled = !isSplitBurst;
                SetSplitSparksVisible(isSplitBurst);
                AreaIndicator.enabled = false;
            }

            public void SetAreaIndicator(float radius)
            {
                AreaRadius = Mathf.Max(PixelWorldSize, radius);
                Line.enabled = false;
                PixelRenderer.enabled = false;
                SetSplitSparksVisible(false);
                AreaIndicator.enabled = true;
            }

            public void AttachToEnemy(
                StageOneEnemyView enemy,
                Vector3 currentAnchor)
            {
                followEnemy = enemy;
                followEnemyId =
                    enemy == null
                        ? -1
                        : enemy.EntityId;
                lastFollowAnchor = currentAnchor;
                followOffset =
                    enemy == null
                        ? Vector3.zero
                        : currentAnchor -
                          enemy.WorldImpactCenter;
            }

            public void RefreshFollowTarget()
            {
                if (followEnemy == null)
                {
                    return;
                }

                if (!followEnemy.gameObject.activeInHierarchy ||
                    followEnemy.EntityId != followEnemyId)
                {
                    followEnemy = null;
                    followEnemyId = -1;
                    return;
                }

                Vector3 nextAnchor =
                    followEnemy.WorldImpactCenter +
                    followOffset;
                Vector3 delta =
                    nextAnchor -
                    lastFollowAnchor;
                Start += delta;
                End += delta;
                lastFollowAnchor = nextAnchor;
            }

            public void BeginPixelMesh()
            {
                PixelBlockCount = 0;
            }

            public void AddPixelBlock(
                Vector3 worldPosition,
                float size,
                Color color)
            {
                if (PixelBlockCount >= MaximumPixelBlocks)
                {
                    return;
                }

                Vector3 center =
                    Host.transform.InverseTransformPoint(
                        worldPosition);
                float half = size * 0.5f;
                int vertex = PixelBlockCount * 4;
                PixelVertices[vertex] =
                    center + new Vector3(-half, -half, 0f);
                PixelVertices[vertex + 1] =
                    center + new Vector3(-half, half, 0f);
                PixelVertices[vertex + 2] =
                    center + new Vector3(half, half, 0f);
                PixelVertices[vertex + 3] =
                    center + new Vector3(half, -half, 0f);
                Color32 pixelColor = color;
                for (int i = 0; i < 4; i++)
                {
                    PixelColors[vertex + i] = pixelColor;
                    PixelUvs[vertex + i] =
                        new Vector2(0.5f, 0.5f);
                }

                int triangle = PixelBlockCount * 6;
                PixelTriangles[triangle] = vertex;
                PixelTriangles[triangle + 1] = vertex + 1;
                PixelTriangles[triangle + 2] = vertex + 2;
                PixelTriangles[triangle + 3] = vertex;
                PixelTriangles[triangle + 4] = vertex + 2;
                PixelTriangles[triangle + 5] = vertex + 3;
                PixelBlockCount++;
            }

            public void EndPixelMesh()
            {
                PixelMesh.Clear(false);
                int vertexCount = PixelBlockCount * 4;
                int triangleCount = PixelBlockCount * 6;
                if (vertexCount <= 0)
                {
                    PixelRenderer.enabled = false;
                    return;
                }

                PixelMesh.SetVertices(
                    PixelVertices,
                    0,
                    vertexCount);
                PixelMesh.SetColors(
                    PixelColors,
                    0,
                    vertexCount);
                PixelMesh.SetUVs(
                    0,
                    PixelUvs,
                    0,
                    vertexCount);
                PixelMesh.SetTriangles(
                    PixelTriangles,
                    0,
                    triangleCount,
                    0,
                    false);
                PixelMesh.RecalculateBounds();
                PixelRenderer.enabled = true;
            }

            public void ClearPixelMesh()
            {
                PixelBlockCount = 0;
                PixelMesh.Clear(false);
            }

            public void SetSplitSparksVisible(bool visible)
            {
                for (int i = 0; i < SplitSparks.Length; i++)
                {
                    if (SplitSparks[i] != null)
                    {
                        SplitSparks[i].enabled = visible;
                    }
                }
            }

            public void Show()
            {
                Host.SetActive(true);
            }

            public void Hide()
            {
                Line.positionCount = 0;
                Line.enabled = false;
                ClearPixelMesh();
                PixelRenderer.enabled = false;
                SetSplitSparksVisible(false);
                AreaIndicator.enabled = false;
                Host.SetActive(false);
            }

            public void Stop()
            {
                Active = false;
                AreaRadius = 0f;
                followEnemy = null;
                followEnemyId = -1;
                Hide();
            }
        }

        private static class SharedResources
        {
            private static Material lineMaterial;
            private static Material pixelMaterial;
            private static Sprite pixelSprite;
            private static Sprite pixelCircleSprite;

            public static Material LineMaterial
            {
                get
                {
                    if (lineMaterial != null)
                    {
                        return lineMaterial;
                    }

                    Shader shader = Shader.Find("Sprites/Default");
                    if (shader == null)
                    {
                        return null;
                    }

                    lineMaterial = new Material(shader)
                    {
                        name =
                            "Ruleforge Card Effect Line Material",
                        hideFlags =
                            HideFlags.HideAndDontSave
                    };
                    return lineMaterial;
                }
            }

            public static Material PixelMaterial
            {
                get
                {
                    if (pixelMaterial != null)
                    {
                        return pixelMaterial;
                    }

                    Shader shader = Shader.Find("Sprites/Default");
                    if (shader == null)
                    {
                        return null;
                    }

                    pixelMaterial = new Material(shader)
                    {
                        name =
                            "Ruleforge Pixel VFX Material",
                        hideFlags =
                            HideFlags.HideAndDontSave,
                        mainTexture = Texture2D.whiteTexture
                    };
                    return pixelMaterial;
                }
            }

            public static Sprite PixelSprite
            {
                get
                {
                    if (pixelSprite != null)
                    {
                        return pixelSprite;
                    }

                    var texture = new Texture2D(
                        1,
                        1,
                        TextureFormat.RGBA32,
                        false)
                    {
                        name =
                            "Ruleforge Card Effect Pixel Texture",
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    texture.SetPixel(0, 0, Color.white);
                    texture.Apply(false, true);
                    pixelSprite = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, 1f, 1f),
                        new Vector2(0.5f, 0.5f),
                        1f);
                    pixelSprite.name =
                        "Ruleforge Card Effect Pixel";
                    pixelSprite.hideFlags =
                        HideFlags.HideAndDontSave;
                    return pixelSprite;
                }
            }

            public static Sprite PixelCircleSprite
            {
                get
                {
                    if (pixelCircleSprite != null)
                    {
                        return pixelCircleSprite;
                    }

                    const int size = AreaIndicatorTextureSize;
                    // The authoritative boundary is deliberately a crisp,
                    // high-opacity outline with no translucent fill. A 129px
                    // one-pixel rim keeps the line thin while making the
                    // stair-stepped pixel silhouette visible in gameplay.
                    const float borderPixels =
                        AreaIndicatorBorderPixels;
                    const float center = (size - 1) * 0.5f;
                    var texture = new Texture2D(
                        size,
                        size,
                        TextureFormat.RGBA32,
                        false)
                    {
                        name = "Ruleforge Pixel Area Circle Texture",
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    var pixels = new Color32[size * size];
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float dx = x - center;
                            float dy = y - center;
                            float distance = Mathf.Sqrt(
                                dx * dx + dy * dy);
                            byte alpha =
                                distance >= center - borderPixels &&
                                distance <= center
                                    ? (byte)255
                                    : (byte)0;
                            pixels[y * size + x] =
                                new Color32(255, 255, 255, alpha);
                        }
                    }

                    texture.SetPixels32(pixels);
                    texture.Apply(false, true);
                    pixelCircleSprite = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, size, size),
                        new Vector2(0.5f, 0.5f),
                        size);
                    pixelCircleSprite.name =
                        "Ruleforge Pixel Area Circle";
                    pixelCircleSprite.hideFlags =
                        HideFlags.HideAndDontSave;
                    return pixelCircleSprite;
                }
            }
        }
    }
}
