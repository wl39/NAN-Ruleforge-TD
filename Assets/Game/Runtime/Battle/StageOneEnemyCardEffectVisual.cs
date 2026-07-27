using System;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Simulation;
using UnityEngine;

namespace RuleforgeTD.Battle
{
    [Flags]
    public enum StageOneEnemyEffectVisualFlags : uint
    {
        None = 0,
        Ricochet = 1u << 0,
        Bleed = 1u << 1,
        Accelerate = 1u << 2,
        Homing = 1u << 3,
        Delay = 1u << 4,
        Curse = 1u << 5,
        Bind = 1u << 6,
        Airborne = 1u << 7,
        Shock = 1u << 8,
        Freeze = 1u << 9,
        Afterimage = 1u << 10,
        Pulse = 1u << 11,
        Magnet = 1u << 12,
        Reflect = 1u << 13,
        Contagion = 1u << 14,
        Seal = 1u << 15,
        Corrosion = 1u << 16,
        Orbit = 1u << 17,
        Lifesteal = 1u << 18,
        Fear = 1u << 19,
        Split = 1u << 20,
        Pierce = 1u << 21,
        Burn = 1u << 22,
        Slow = 1u << 23,
        Explode = 1u << 24,
        Knockback = 1u << 25,
        Mark = 1u << 26,
        GoldBounty = 1u << 27,
        Poison = 1u << 28,
        Enlarge = 1u << 29,
        Shrink = 1u << 30,
        Stun = 1u << 31
    }

    /// <summary>
    /// Persistent, view-only status presentation for the Common/Uncommon
    /// cards. It is intentionally separate from EnemyStatusVisualView so burn
    /// and poison remain untouched while the expanded card set is introduced.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneEnemyCardEffectVisual : MonoBehaviour
    {
        public const float MaximumAirborneLift = 0.92f;
        public const float FearJitterDistance = 0.045f;

        private const string TintOverlayName =
            "Card Effect Tint Overlay";
        private const string AuraName =
            "Card Effect Aura";
        private const string GlyphName =
            "Card Effect Glyph";
        private const string ShadowName =
            "Airborne Ground Shadow";

        [SerializeField]
        private SpriteRenderer targetRenderer;

        private StageOneEnemyView enemyView;
        private SpriteRenderer tintOverlay;
        private SpriteRenderer aura;
        private SpriteRenderer glyph;
        private SpriteRenderer airborneShadow;
        private StageOneEnemyEffectVisualFlags activeFlags;
        private Vector3 appliedPresentationOffset;
        private int airborneInstanceId = -1;
        private int airborneRemainingTicks;
        private int airbornePeakTicks;
        private int cachedSortingLayerId = int.MinValue;
        private int cachedSortingOrder = int.MinValue;
        private string dominantEffectId = string.Empty;
        private Color dominantColor = Color.white;

        public StageOneEnemyEffectVisualFlags ActiveFlags =>
            activeFlags;
        public bool IsCursed =>
            HasFlag(StageOneEnemyEffectVisualFlags.Curse);
        public bool IsBound =>
            HasFlag(StageOneEnemyEffectVisualFlags.Bind);
        public bool IsAirborne =>
            HasFlag(StageOneEnemyEffectVisualFlags.Airborne);
        public float AirborneLift =>
            appliedPresentationOffset.y;
        public string DominantEffectId =>
            dominantEffectId;
        public Color DominantColor =>
            dominantColor;
        public bool TintOverlayVisible =>
            tintOverlay != null && tintOverlay.enabled;
        public bool AuraVisible =>
            aura != null && aura.enabled;
        public bool AirborneShadowVisible =>
            airborneShadow != null && airborneShadow.enabled;

        private void Awake()
        {
            CacheComponents();
            EnsureRenderers();
            ResetVisuals();
        }

        private void OnEnable()
        {
            CacheComponents();
            EnsureRenderers();
        }

        private void LateUpdate()
        {
            SynchronizeSourceSprite();
            SynchronizeSorting();
            AnimatePersistentVisuals();
            ApplyPresentationOffset();
        }

        private void OnDisable()
        {
            ResetVisuals();
        }

        public void Configure(
            StageOneEnemyView sourceEnemyView,
            SpriteRenderer sourceRenderer = null)
        {
            RestorePresentationOffset();
            enemyView = sourceEnemyView != null
                ? sourceEnemyView
                : GetComponent<StageOneEnemyView>();
            targetRenderer = sourceRenderer != null
                ? sourceRenderer
                : GetComponent<SpriteRenderer>();
            cachedSortingLayerId = int.MinValue;
            cachedSortingOrder = int.MinValue;
            EnsureRenderers();
            SynchronizeSourceSprite();
            SynchronizeSorting();
            RefreshStyle();
        }

        public void ApplySnapshot(in EnemySnapshot snapshot)
        {
            if (!snapshot.Alive)
            {
                ResetVisuals();
                return;
            }

            ApplyStatuses(snapshot.StatusDetails);
        }

        /// <summary>
        /// Removes the previous frame's view-only lift before the owning enemy
        /// view writes its next authoritative snapshot position.
        /// </summary>
        public void PrepareForAuthoritativeSnapshot()
        {
            RestorePresentationOffset();
        }

        public static bool HasSupportedEffect(
            StatusSnapshot[] statuses)
        {
            if (statuses == null)
            {
                return false;
            }

            for (int i = 0; i < statuses.Length; i++)
            {
                StatusSnapshot status = statuses[i];
                if (status.Stacks > 0 &&
                    status.RemainingTicks > 0 &&
                    ToVisualFlag(status.Type) !=
                    StageOneEnemyEffectVisualFlags.None)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Converts immutable simulation status snapshots into presentation
        /// flags without allocations, LINQ, or status-name string conversion.
        /// </summary>
        public void ApplyStatuses(StatusSnapshot[] statuses)
        {
            StageOneEnemyEffectVisualFlags nextFlags =
                StageOneEnemyEffectVisualFlags.None;
            int nextAirborneInstanceId = -1;
            int nextAirborneRemainingTicks = 0;

            if (statuses != null)
            {
                for (int i = 0; i < statuses.Length; i++)
                {
                    StatusSnapshot status = statuses[i];
                    if (status.Stacks <= 0 ||
                        status.RemainingTicks <= 0)
                    {
                        continue;
                    }

                    nextFlags |= ToVisualFlag(status.Type);
                    if (status.Type == StatusType.Airborne &&
                        status.RemainingTicks >
                        nextAirborneRemainingTicks)
                    {
                        nextAirborneInstanceId = status.InstanceId;
                        nextAirborneRemainingTicks =
                            status.RemainingTicks;
                    }
                }
            }

            SetVisualFlags(
                nextFlags,
                nextAirborneRemainingTicks,
                nextAirborneInstanceId);
        }

        /// <summary>
        /// Direct presentation seam used by tests and non-simulation previews.
        /// </summary>
        public void SetVisualFlags(
            StageOneEnemyEffectVisualFlags flags,
            int remainingAirborneTicks = 0,
            int airborneStatusInstanceId = -1)
        {
            if (airborneStatusInstanceId != airborneInstanceId)
            {
                airborneInstanceId = airborneStatusInstanceId;
                airbornePeakTicks =
                    Mathf.Max(1, remainingAirborneTicks);
            }
            else if (remainingAirborneTicks >
                     airbornePeakTicks)
            {
                airbornePeakTicks =
                    remainingAirborneTicks;
            }

            airborneRemainingTicks =
                Mathf.Max(0, remainingAirborneTicks);
            if ((flags &
                 StageOneEnemyEffectVisualFlags.Airborne) == 0)
            {
                airborneInstanceId = -1;
                airbornePeakTicks = 0;
                airborneRemainingTicks = 0;
            }

            if (flags == activeFlags)
            {
                return;
            }

            activeFlags = flags;
            RefreshStyle();
        }

        public void ResetVisuals()
        {
            activeFlags = StageOneEnemyEffectVisualFlags.None;
            airborneInstanceId = -1;
            airborneRemainingTicks = 0;
            airbornePeakTicks = 0;
            dominantEffectId = string.Empty;
            dominantColor = Color.white;
            RestorePresentationOffset();

            if (tintOverlay != null)
            {
                tintOverlay.enabled = false;
            }

            if (aura != null)
            {
                aura.enabled = false;
            }

            if (glyph != null)
            {
                glyph.enabled = false;
            }

            if (airborneShadow != null)
            {
                airborneShadow.enabled = false;
            }
        }

        private void CacheComponents()
        {
            if (enemyView == null)
            {
                enemyView = GetComponent<StageOneEnemyView>();
            }

            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<SpriteRenderer>();
            }
        }

        private void EnsureRenderers()
        {
            if (targetRenderer == null)
            {
                return;
            }

            tintOverlay = EnsureSpriteRenderer(
                tintOverlay,
                TintOverlayName,
                targetRenderer.transform);
            aura = EnsureSpriteRenderer(
                aura,
                AuraName,
                transform);
            glyph = EnsureSpriteRenderer(
                glyph,
                GlyphName,
                transform);
            airborneShadow = EnsureSpriteRenderer(
                airborneShadow,
                ShadowName,
                transform);

            if (aura != null)
            {
                aura.sprite = SharedResources.RingSprite;
            }

            if (glyph != null)
            {
                glyph.sprite = SharedResources.RuneSprite;
            }

            if (airborneShadow != null)
            {
                airborneShadow.sprite =
                    SharedResources.ShadowSprite;
                airborneShadow.color =
                    new Color(0.06f, 0.08f, 0.12f, 0.38f);
            }
        }

        private static SpriteRenderer EnsureSpriteRenderer(
            SpriteRenderer current,
            string objectName,
            Transform parent)
        {
            if (current != null)
            {
                return current;
            }

            Transform existing = parent.Find(objectName);
            GameObject child;
            if (existing != null)
            {
                child = existing.gameObject;
            }
            else
            {
                child = new GameObject(objectName);
                child.transform.SetParent(parent, false);
            }

            SpriteRenderer renderer =
                child.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                renderer = child.AddComponent<SpriteRenderer>();
            }

            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;
            renderer.enabled = false;
            return renderer;
        }

        private void RefreshStyle()
        {
            EnsureRenderers();
            dominantEffectId =
                ResolveDominantEffectId(activeFlags);
            if (string.IsNullOrEmpty(dominantEffectId) ||
                !StageOneCardEffectPalette.TryGetStyle(
                    dominantEffectId,
                    out StageOneCardEffectStyle style))
            {
                dominantColor = Color.white;
                if (tintOverlay != null)
                {
                    tintOverlay.enabled = false;
                }

                if (aura != null)
                {
                    aura.enabled = false;
                }

                if (glyph != null)
                {
                    glyph.enabled = false;
                }

                if (airborneShadow != null)
                {
                    airborneShadow.enabled = false;
                }

                return;
            }

            dominantColor = style.Primary;
            if (tintOverlay != null)
            {
                Color tint = style.Primary;
                tint.a = ResolveTintAlpha(activeFlags);
                tintOverlay.color = tint;
                tintOverlay.enabled = true;
            }

            if (aura != null)
            {
                Color auraColor = style.Primary;
                auraColor.a = 0.74f;
                aura.color = auraColor;
                aura.enabled = true;
            }

            if (glyph != null)
            {
                glyph.sprite = ResolveGlyph(style.Shape);
                Color glyphColor = style.Secondary;
                glyphColor.a = 0.88f;
                glyph.color = glyphColor;
                glyph.enabled = true;
            }

            if (airborneShadow != null)
            {
                airborneShadow.enabled = IsAirborne;
            }
        }

        private void SynchronizeSourceSprite()
        {
            if (targetRenderer == null ||
                tintOverlay == null)
            {
                return;
            }

            tintOverlay.sprite = targetRenderer.sprite;
            tintOverlay.flipX = targetRenderer.flipX;
            tintOverlay.flipY = targetRenderer.flipY;
            tintOverlay.enabled =
                activeFlags !=
                    StageOneEnemyEffectVisualFlags.None &&
                targetRenderer.enabled &&
                targetRenderer.sprite != null;
        }

        private void SynchronizeSorting()
        {
            if (targetRenderer == null)
            {
                return;
            }

            int layer = targetRenderer.sortingLayerID;
            int order = targetRenderer.sortingOrder;
            if (layer == cachedSortingLayerId &&
                order == cachedSortingOrder)
            {
                return;
            }

            cachedSortingLayerId = layer;
            cachedSortingOrder = order;
            if (airborneShadow != null)
            {
                airborneShadow.sortingLayerID = layer;
                airborneShadow.sortingOrder = order - 1;
            }

            if (tintOverlay != null)
            {
                tintOverlay.sortingLayerID = layer;
                tintOverlay.sortingOrder = order + 1;
            }

            if (aura != null)
            {
                aura.sortingLayerID = layer;
                aura.sortingOrder = order + 2;
            }

            if (glyph != null)
            {
                glyph.sortingLayerID = layer;
                glyph.sortingOrder = order + 3;
            }
        }

        private void AnimatePersistentVisuals()
        {
            if (activeFlags ==
                StageOneEnemyEffectVisualFlags.None)
            {
                return;
            }

            float time = Time.unscaledTime;
            float pulse = 0.92f +
                          Mathf.Sin(time * 5.8f) * 0.08f;
            if (aura != null)
            {
                aura.transform.localPosition =
                    new Vector3(0f, 0.12f, -0.015f);
                aura.transform.localScale =
                    new Vector3(
                        pulse,
                        pulse * 0.58f,
                        1f);
                aura.transform.localRotation =
                    Quaternion.Euler(
                        0f,
                        0f,
                        time *
                        (HasFlag(
                             StageOneEnemyEffectVisualFlags.Orbit)
                            ? 150f
                            : 42f));
            }

            if (glyph != null)
            {
                float glyphHeight = IsAirborne ? 0.5f : 0.36f;
                glyph.transform.localPosition =
                    new Vector3(
                        0f,
                        glyphHeight +
                        Mathf.Sin(time * 4.4f) * 0.035f,
                        -0.02f);
                glyph.transform.localScale =
                    Vector3.one * (0.42f + pulse * 0.08f);
                glyph.transform.localRotation =
                    Quaternion.Euler(
                        0f,
                        0f,
                        HasFlag(
                            StageOneEnemyEffectVisualFlags.Fear)
                            ? Mathf.Sin(time * 31f) * 9f
                            : -time * 28f);
            }
        }

        private void ApplyPresentationOffset()
        {
            RestorePresentationOffset();
            if (activeFlags ==
                StageOneEnemyEffectVisualFlags.None)
            {
                return;
            }

            float horizontalOffset = 0f;
            if (HasFlag(StageOneEnemyEffectVisualFlags.Fear))
            {
                int stableId =
                    enemyView == null
                        ? GetInstanceID()
                        : enemyView.EntityId;
                horizontalOffset =
                    Mathf.Sin(
                        Time.unscaledTime * 34f +
                        stableId * 0.73f) *
                    FearJitterDistance;
            }

            float lift = ResolveAirborneLift();
            appliedPresentationOffset =
                new Vector3(horizontalOffset, lift, 0f);
            transform.position += appliedPresentationOffset;

            if (airborneShadow != null)
            {
                airborneShadow.enabled = IsAirborne;
                airborneShadow.transform.localPosition =
                    new Vector3(
                        -horizontalOffset,
                        -lift - 0.03f,
                        0.02f);
                float shadowScale =
                    Mathf.Lerp(
                        0.68f,
                        0.38f,
                        Mathf.Clamp01(
                            lift / MaximumAirborneLift));
                airborneShadow.transform.localScale =
                    new Vector3(
                        shadowScale,
                        shadowScale * 0.48f,
                        1f);
            }
        }

        private float ResolveAirborneLift()
        {
            if (!IsAirborne)
            {
                return 0f;
            }

            if (airbornePeakTicks <= 1)
            {
                return MaximumAirborneLift * 0.72f;
            }

            float progress =
                1f -
                Mathf.Clamp01(
                    airborneRemainingTicks /
                    (float)airbornePeakTicks);
            float arc = Mathf.Sin(progress * Mathf.PI);
            return Mathf.Max(
                0.16f,
                arc * MaximumAirborneLift);
        }

        private void RestorePresentationOffset()
        {
            if (appliedPresentationOffset.sqrMagnitude <=
                0.0000001f)
            {
                return;
            }

            transform.position -= appliedPresentationOffset;
            appliedPresentationOffset = Vector3.zero;
        }

        private bool HasFlag(
            StageOneEnemyEffectVisualFlags flag)
        {
            return (activeFlags & flag) != 0;
        }

        private static StageOneEnemyEffectVisualFlags ToVisualFlag(
            StatusType type)
        {
            switch (type)
            {
                case StatusType.Burn:
                    return StageOneEnemyEffectVisualFlags.Burn;
                case StatusType.Poison:
                    return StageOneEnemyEffectVisualFlags.Poison;
                case StatusType.Slow:
                    return StageOneEnemyEffectVisualFlags.Slow;
                case StatusType.Mark:
                    return StageOneEnemyEffectVisualFlags.Mark;
                case StatusType.Pierced:
                    return StageOneEnemyEffectVisualFlags.Pierce;
                case StatusType.Stun:
                    return StageOneEnemyEffectVisualFlags.Stun;
                case StatusType.Ricochet:
                    return StageOneEnemyEffectVisualFlags.Ricochet;
                case StatusType.Bleed:
                    return StageOneEnemyEffectVisualFlags.Bleed;
                case StatusType.HomingPriority:
                    return StageOneEnemyEffectVisualFlags.Homing;
                case StatusType.Delay:
                    return StageOneEnemyEffectVisualFlags.Delay;
                case StatusType.Curse:
                    return StageOneEnemyEffectVisualFlags.Curse;
                case StatusType.Bind:
                    return StageOneEnemyEffectVisualFlags.Bind;
                case StatusType.Airborne:
                    return StageOneEnemyEffectVisualFlags.Airborne;
                case StatusType.Shock:
                    return StageOneEnemyEffectVisualFlags.Shock;
                case StatusType.Chill:
                case StatusType.Frozen:
                case StatusType.FreezeImmunity:
                    return StageOneEnemyEffectVisualFlags.Freeze;
                case StatusType.Afterimage:
                    return StageOneEnemyEffectVisualFlags.Afterimage;
                case StatusType.Pulse:
                    return StageOneEnemyEffectVisualFlags.Pulse;
                case StatusType.Magnet:
                    return StageOneEnemyEffectVisualFlags.Magnet;
                case StatusType.Reflect:
                    return StageOneEnemyEffectVisualFlags.Reflect;
                case StatusType.Contagion:
                    return StageOneEnemyEffectVisualFlags.Contagion;
                case StatusType.Seal:
                    return StageOneEnemyEffectVisualFlags.Seal;
                case StatusType.Corrosion:
                    return StageOneEnemyEffectVisualFlags.Corrosion;
                case StatusType.Orbit:
                    return StageOneEnemyEffectVisualFlags.Orbit;
                case StatusType.Lifesteal:
                    return StageOneEnemyEffectVisualFlags.Lifesteal;
                case StatusType.Fear:
                case StatusType.FearHaste:
                    return StageOneEnemyEffectVisualFlags.Fear;
                default:
                    return StageOneEnemyEffectVisualFlags.None;
            }
        }

        private static string ResolveDominantEffectId(
            StageOneEnemyEffectVisualFlags flags)
        {
            if ((flags & StageOneEnemyEffectVisualFlags.Airborne) != 0)
            {
                return "airborne";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Stun) != 0)
            {
                return "stun";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Explode) != 0)
            {
                return "explode";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Bind) != 0)
            {
                return "bind";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Freeze) != 0)
            {
                return "freeze";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Curse) != 0)
            {
                return "curse";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Shock) != 0)
            {
                return "shock";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Corrosion) != 0)
            {
                return "corrosion";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Burn) != 0)
            {
                return "burn";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Poison) != 0)
            {
                return "poison";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Fear) != 0)
            {
                return "fear";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Bleed) != 0)
            {
                return "bleed";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Seal) != 0)
            {
                return "seal";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Magnet) != 0)
            {
                return "magnet";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Reflect) != 0)
            {
                return "reflect";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Contagion) != 0)
            {
                return "contagion";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Orbit) != 0)
            {
                return "orbit";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Lifesteal) != 0)
            {
                return "lifesteal";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Pulse) != 0)
            {
                return "pulse";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Afterimage) != 0)
            {
                return "afterimage";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Homing) != 0)
            {
                return "homing";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Delay) != 0)
            {
                return "delay";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Ricochet) != 0)
            {
                return "ricochet";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Accelerate) != 0)
            {
                return "accelerate";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Mark) != 0)
            {
                return "mark";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Pierce) != 0)
            {
                return "pierce";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Slow) != 0)
            {
                return "slow";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Knockback) != 0)
            {
                return "knockback";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.GoldBounty) != 0)
            {
                return "gold_bounty";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Enlarge) != 0)
            {
                return "enlarge";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Shrink) != 0)
            {
                return "shrink";
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Split) != 0)
            {
                return "split";
            }

            return string.Empty;
        }

        private static float ResolveTintAlpha(
            StageOneEnemyEffectVisualFlags flags)
        {
            if ((flags & StageOneEnemyEffectVisualFlags.Curse) != 0)
            {
                return 0.42f;
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Bind) != 0)
            {
                return 0.36f;
            }

            if ((flags & StageOneEnemyEffectVisualFlags.Freeze) != 0)
            {
                return 0.44f;
            }

            return 0.27f;
        }

        private static Sprite ResolveGlyph(
            StageOneCardEffectShape shape)
        {
            switch (shape)
            {
                case StageOneCardEffectShape.Chain:
                    return SharedResources.ChainSprite;
                case StageOneCardEffectShape.IceBurst:
                    return SharedResources.SnowflakeSprite;
                case StageOneCardEffectShape.Reticle:
                    return SharedResources.ReticleSprite;
                default:
                    return SharedResources.RuneSprite;
            }
        }

        private static class SharedResources
        {
            private static Sprite ringSprite;
            private static Sprite runeSprite;
            private static Sprite chainSprite;
            private static Sprite snowflakeSprite;
            private static Sprite reticleSprite;
            private static Sprite shadowSprite;

            public static Sprite RingSprite =>
                ringSprite ?? (ringSprite =
                    CreateSprite(
                        "Ruleforge Card Aura Ring",
                        24,
                        DrawRing));
            public static Sprite RuneSprite =>
                runeSprite ?? (runeSprite =
                    CreateSprite(
                        "Ruleforge Card Rune",
                        20,
                        DrawRune));
            public static Sprite ChainSprite =>
                chainSprite ?? (chainSprite =
                    CreateSprite(
                        "Ruleforge Bind Chain",
                        20,
                        DrawChain));
            public static Sprite SnowflakeSprite =>
                snowflakeSprite ?? (snowflakeSprite =
                    CreateSprite(
                        "Ruleforge Freeze Glyph",
                        20,
                        DrawSnowflake));
            public static Sprite ReticleSprite =>
                reticleSprite ?? (reticleSprite =
                    CreateSprite(
                        "Ruleforge Homing Reticle",
                        20,
                        DrawReticle));
            public static Sprite ShadowSprite =>
                shadowSprite ?? (shadowSprite =
                    CreateSprite(
                        "Ruleforge Airborne Shadow",
                        20,
                        DrawShadow));

            private delegate void PixelDrawer(
                Color32[] pixels,
                int size);

            private static Sprite CreateSprite(
                string resourceName,
                int size,
                PixelDrawer drawer)
            {
                var pixels = new Color32[size * size];
                drawer(pixels, size);
                var texture = new Texture2D(
                    size,
                    size,
                    TextureFormat.RGBA32,
                    false)
                {
                    name = resourceName + " Texture",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, size, size),
                    new Vector2(0.5f, 0.5f),
                    size);
                sprite.name = resourceName;
                sprite.hideFlags = HideFlags.HideAndDontSave;
                return sprite;
            }

            private static void DrawRing(
                Color32[] pixels,
                int size)
            {
                float center = (size - 1) * 0.5f;
                float outer = center - 1f;
                float inner = outer - 2f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x - center;
                        float dy = y - center;
                        float distance = Mathf.Sqrt(
                            dx * dx + dy * dy);
                        if (distance >= inner &&
                            distance <= outer)
                        {
                            Set(pixels, size, x, y);
                        }
                    }
                }
            }

            private static void DrawRune(
                Color32[] pixels,
                int size)
            {
                int center = size / 2;
                for (int i = 2; i < size - 2; i++)
                {
                    int delta = Mathf.Abs(center - i);
                    Set(pixels, size, i, center - delta);
                    Set(pixels, size, i, center + delta);
                    Set(pixels, size, center, i);
                }
            }

            private static void DrawChain(
                Color32[] pixels,
                int size)
            {
                DrawBox(pixels, size, 2, 5, 10, 14);
                DrawBox(pixels, size, 9, 5, 17, 14);
                for (int x = 7; x <= 12; x++)
                {
                    Set(pixels, size, x, 9);
                    Set(pixels, size, x, 10);
                }
            }

            private static void DrawSnowflake(
                Color32[] pixels,
                int size)
            {
                int center = size / 2;
                for (int i = 2; i < size - 2; i++)
                {
                    Set(pixels, size, center, i);
                    Set(pixels, size, i, center);
                    Set(pixels, size, i, i);
                    Set(pixels, size, i, size - 1 - i);
                }
            }

            private static void DrawReticle(
                Color32[] pixels,
                int size)
            {
                DrawRing(pixels, size);
                int center = size / 2;
                for (int i = 0; i < 6; i++)
                {
                    Set(pixels, size, center, i);
                    Set(pixels, size, center, size - 1 - i);
                    Set(pixels, size, i, center);
                    Set(pixels, size, size - 1 - i, center);
                }
            }

            private static void DrawShadow(
                Color32[] pixels,
                int size)
            {
                float center = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x - center) / center;
                        float dy =
                            (y - center) /
                            Mathf.Max(1f, center * 0.48f);
                        if (dx * dx + dy * dy <= 1f)
                        {
                            Set(pixels, size, x, y);
                        }
                    }
                }
            }

            private static void DrawBox(
                Color32[] pixels,
                int size,
                int minX,
                int minY,
                int maxX,
                int maxY)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Set(pixels, size, x, minY);
                    Set(pixels, size, x, maxY);
                }

                for (int y = minY; y <= maxY; y++)
                {
                    Set(pixels, size, minX, y);
                    Set(pixels, size, maxX, y);
                }
            }

            private static void Set(
                Color32[] pixels,
                int size,
                int x,
                int y)
            {
                if (x < 0 || y < 0 ||
                    x >= size || y >= size)
                {
                    return;
                }

                pixels[y * size + x] =
                    new Color32(255, 255, 255, 255);
            }
        }
    }
}
