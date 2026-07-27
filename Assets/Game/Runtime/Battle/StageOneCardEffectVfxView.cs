using RuleforgeTD.GameLogic.Simulation;
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
        public const int MaximumLinePoints = 24;

        [SerializeField]
        [Range(16, 96)]
        private int poolCapacity = DefaultPoolCapacity;

        private EffectInstance[] pool;
        private int nextPoolIndex;
        private uint playSequence;
        private string lastPlayedEffectId = string.Empty;
        private StageOneCardEffectShape lastPlayedShape;
        private Color lastPlayedColor = Color.white;
        private Vector3 lastStartPosition;
        private Vector3 lastEndPosition;

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
        public string LastPlayedEffectId =>
            lastPlayedEffectId;
        public StageOneCardEffectShape LastPlayedShape =>
            lastPlayedShape;
        public Color LastPlayedColor =>
            lastPlayedColor;
        public Vector3 LastStartPosition =>
            lastStartPosition;
        public Vector3 LastEndPosition =>
            lastEndPosition;

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

            float now = Time.unscaledTime;
            for (int i = 0; i < pool.Length; i++)
            {
                EffectInstance instance = pool[i];
                if (!instance.Active)
                {
                    continue;
                }

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
                    96);
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
            bool hasEffect =
                StageOneEnemyCardEffectVisual.HasSupportedEffect(
                    snapshot.StatusDetails);
            if (visual == null && !hasEffect)
            {
                return null;
            }

            if (visual == null)
            {
                visual =
                    enemy.gameObject.AddComponent<
                        StageOneEnemyCardEffectVisual>();
                visual.Configure(enemy);
            }

            visual.enabled = true;
            visual.ApplySnapshot(snapshot);
            if (!hasEffect)
            {
                visual.enabled = false;
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
                ProjectileEffectVisualFlags.None;
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

            if (!StageOneCardEffectPalette.TryGetStyle(
                    item.ContentId,
                    out StageOneCardEffectStyle style))
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

            if (hasLink)
            {
                return PlayLink(
                    style.Id,
                    sourcePosition,
                    subjectPosition);
            }

            if (hasSubjectPosition)
            {
                return Play(style.Id, subjectPosition);
            }

            if (hasSourcePosition)
            {
                return Play(style.Id, sourcePosition);
            }

            return false;
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
            InitializePool();
            if (!StageOneCardEffectPalette.TryGetStyle(
                    effectId,
                    out StageOneCardEffectStyle style))
            {
                return false;
            }

            EffectInstance instance = AcquireInstance();
            sourcePosition.z = -0.18f;
            targetPosition.z = -0.18f;
            instance.Play(
                style,
                sourcePosition,
                targetPosition,
                Time.unscaledTime,
                ++playSequence);
            RenderInstance(instance, 0f);

            lastPlayedEffectId = style.Id;
            lastPlayedShape = style.Shape;
            lastPlayedColor = style.Primary;
            lastStartPosition = sourcePosition;
            lastEndPosition = targetPosition;
            return true;
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
                    typeof(LineRenderer));
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
                line.sortingOrder = 60;
                line.enabled = false;
                pool[i] = new EffectInstance(child, line);
            }
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
            float eased =
                1f - (1f - progress) * (1f - progress);
            float alpha =
                1f - Mathf.SmoothStep(0.58f, 1f, progress);
            float widthPulse =
                1f + Mathf.Sin(progress * Mathf.PI) * 0.35f;
            instance.Line.startWidth =
                instance.Style.Width * widthPulse;
            instance.Line.endWidth =
                instance.Style.Width * 0.35f * alpha;

            Color startColor = Color.Lerp(
                instance.Style.Secondary,
                instance.Style.Primary,
                progress * 0.6f);
            Color endColor = instance.Style.Primary;
            startColor.a = alpha;
            endColor.a = alpha * 0.22f;
            instance.Line.startColor = startColor;
            instance.Line.endColor = endColor;

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
                case StageOneCardEffectShape.Slash:
                    count = BuildSlash(instance, eased);
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
                case StageOneCardEffectShape.Chain:
                    count = BuildChain(instance, eased);
                    break;
                case StageOneCardEffectShape.Launch:
                    count = BuildLaunch(instance, eased);
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
                case StageOneCardEffectShape.Vortex:
                    count = BuildVortex(instance, eased);
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
                case StageOneCardEffectShape.Lance:
                    count = BuildLance(instance, eased);
                    break;
                case StageOneCardEffectShape.Flame:
                    count = BuildFlame(instance, eased);
                    break;
                case StageOneCardEffectShape.Hourglass:
                    count = BuildHourglass(instance, eased);
                    break;
                case StageOneCardEffectShape.Blast:
                    count = BuildStar(instance, eased, 10);
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
                case StageOneCardEffectShape.Toxic:
                    count = BuildToxic(instance, eased);
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
                case StageOneCardEffectShape.StunBurst:
                    count = BuildStunBurst(instance, eased);
                    break;
                default:
                    count = BuildRing(
                        instance,
                        eased,
                        0.4f,
                        1.4f);
                    break;
            }

            instance.Line.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                instance.Line.SetPosition(
                    i,
                    instance.Points[i]);
            }
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
                Mathf.Lerp(0.35f, 1.15f, progress);
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
                LineRenderer line)
            {
                Host = host;
                Line = line;
                Points =
                    new Vector3[MaximumLinePoints];
            }

            public GameObject Host { get; }
            public LineRenderer Line { get; }
            public Vector3[] Points { get; }
            public StageOneCardEffectStyle Style { get; private set; }
            public Vector3 Start { get; set; }
            public Vector3 End { get; set; }
            public float StartTime { get; private set; }
            public uint Sequence { get; private set; }
            public bool Active { get; private set; }

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
                Host.SetActive(true);
                Line.enabled = true;
            }

            public void Stop()
            {
                Active = false;
                Line.positionCount = 0;
                Line.enabled = false;
                Host.SetActive(false);
            }
        }

        private static class SharedResources
        {
            private static Material lineMaterial;

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
        }
    }
}
