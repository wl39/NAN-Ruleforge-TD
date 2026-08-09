using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Simulation;
using UnityEngine;

namespace RuleforgeTD.Battle
{
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
        private const string ShadowName =
            "Airborne Ground Shadow";

        [SerializeField]
        private SpriteRenderer targetRenderer;

        private StageOneEnemyView enemyView;
        private SpriteRenderer tintOverlay;
        private SpriteRenderer airborneShadow;
        private CardEffectVisualFlags activeFlags;
        private Vector3 appliedPresentationOffset;
        private int airborneInstanceId = -1;
        private int airborneRemainingTicks;
        private int airbornePeakTicks;
        private int cachedSortingLayerId = int.MinValue;
        private int cachedSortingOrder = int.MinValue;
        private string dominantEffectId = string.Empty;
        private Color dominantColor = Color.white;

        public CardEffectVisualFlags ActiveFlags =>
            activeFlags;
        public bool IsCursed =>
            HasFlag(CardEffectVisualFlags.Curse);
        public bool IsBound =>
            HasFlag(CardEffectVisualFlags.Bind);
        public bool IsAirborne =>
            HasFlag(CardEffectVisualFlags.Airborne);
        public float AirborneLift =>
            appliedPresentationOffset.y;
        public string DominantEffectId =>
            dominantEffectId;
        public Color DominantColor =>
            dominantColor;
        public bool TintOverlayVisible =>
            tintOverlay != null && tintOverlay.enabled;
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
                    CardEffectVisualFlags.None)
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
            CardEffectVisualFlags nextFlags =
                CardEffectVisualFlags.None;
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
            CardEffectVisualFlags flags,
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
                 CardEffectVisualFlags.Airborne) == 0)
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
            activeFlags = CardEffectVisualFlags.None;
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
            airborneShadow = EnsureSpriteRenderer(
                airborneShadow,
                ShadowName,
                transform);

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
                    CardEffectVisualFlags.None &&
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

        }

        private void ApplyPresentationOffset()
        {
            RestorePresentationOffset();
            if (activeFlags ==
                CardEffectVisualFlags.None)
            {
                return;
            }

            float horizontalOffset = 0f;
            if (HasFlag(CardEffectVisualFlags.Fear))
            {
                int stableId =
                    enemyView == null
                        ? GetInstanceID()
                        : enemyView.EntityId;
                horizontalOffset =
                    Mathf.Sin(
                        Time.time * 34f +
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
            CardEffectVisualFlags flag)
        {
            return (activeFlags & flag) != 0;
        }

        private static CardEffectVisualFlags ToVisualFlag(
            StatusType type)
        {
            return StageOneStatusEffectVisualCatalog
                .ToVisualFlag(type);
        }

        private static string ResolveDominantEffectId(
            CardEffectVisualFlags flags)
        {
            string highTierEffect =
                StageOneCardEffectPalette
                    .ResolveHighestSetEffectId(
                        flags,
                        44);
            if (!string.IsNullOrEmpty(highTierEffect))
            {
                return highTierEffect;
            }

            if ((flags & CardEffectVisualFlags.Rebirth) != 0)
            {
                return "rebirth";
            }

            if ((flags & CardEffectVisualFlags.Execute) != 0)
            {
                return "execute";
            }

            if ((flags & CardEffectVisualFlags.TimeStop) != 0)
            {
                return "time_stop";
            }

            if ((flags & CardEffectVisualFlags.Mutation) != 0)
            {
                return "mutation";
            }

            if ((flags & CardEffectVisualFlags.Parasite) != 0)
            {
                return "parasite";
            }

            if ((flags & CardEffectVisualFlags.Absorb) != 0)
            {
                return "absorb";
            }

            if ((flags & CardEffectVisualFlags.Resonance) != 0)
            {
                return "resonance";
            }

            if ((flags & CardEffectVisualFlags.Chain) != 0)
            {
                return "chain";
            }

            if ((flags & CardEffectVisualFlags.Retrograde) != 0)
            {
                return "retrograde";
            }

            if ((flags & CardEffectVisualFlags.Return) != 0)
            {
                return "return";
            }

            if ((flags & CardEffectVisualFlags.Sacrifice) != 0)
            {
                return "sacrifice";
            }

            if ((flags & CardEffectVisualFlags.Duplicate) != 0)
            {
                return "duplicate";
            }

            if ((flags & CardEffectVisualFlags.Airborne) != 0)
            {
                return "airborne";
            }

            if ((flags & CardEffectVisualFlags.Stun) != 0)
            {
                return "stun";
            }

            if ((flags & CardEffectVisualFlags.Explode) != 0)
            {
                return "explode";
            }

            if ((flags & CardEffectVisualFlags.Bind) != 0)
            {
                return "bind";
            }

            if ((flags & CardEffectVisualFlags.Freeze) != 0)
            {
                return "freeze";
            }

            if ((flags & CardEffectVisualFlags.Curse) != 0)
            {
                return "curse";
            }

            if ((flags & CardEffectVisualFlags.Shock) != 0)
            {
                return "shock";
            }

            if ((flags & CardEffectVisualFlags.Corrosion) != 0)
            {
                return "corrosion";
            }

            if ((flags & CardEffectVisualFlags.Burn) != 0)
            {
                return "burn";
            }

            if ((flags & CardEffectVisualFlags.Poison) != 0)
            {
                return "poison";
            }

            if ((flags & CardEffectVisualFlags.Fear) != 0)
            {
                return "fear";
            }

            if ((flags & CardEffectVisualFlags.Bleed) != 0)
            {
                return "bleed";
            }

            if ((flags & CardEffectVisualFlags.Seal) != 0)
            {
                return "seal";
            }

            if ((flags & CardEffectVisualFlags.Magnet) != 0)
            {
                return "magnet";
            }

            if ((flags & CardEffectVisualFlags.Reflect) != 0)
            {
                return "reflect";
            }

            if ((flags & CardEffectVisualFlags.Contagion) != 0)
            {
                return "contagion";
            }

            if ((flags & CardEffectVisualFlags.Orbit) != 0)
            {
                return "orbit";
            }

            if ((flags & CardEffectVisualFlags.Lifesteal) != 0)
            {
                return "lifesteal";
            }

            if ((flags & CardEffectVisualFlags.Pulse) != 0)
            {
                return "pulse";
            }

            if ((flags & CardEffectVisualFlags.Afterimage) != 0)
            {
                return "afterimage";
            }

            if ((flags & CardEffectVisualFlags.Homing) != 0)
            {
                return "homing";
            }

            if ((flags & CardEffectVisualFlags.Delay) != 0)
            {
                return "delay";
            }

            if ((flags & CardEffectVisualFlags.Ricochet) != 0)
            {
                return "ricochet";
            }

            if ((flags & CardEffectVisualFlags.Accelerate) != 0)
            {
                return "accelerate";
            }

            if ((flags & CardEffectVisualFlags.Mark) != 0)
            {
                return "mark";
            }

            if ((flags & CardEffectVisualFlags.Pierce) != 0)
            {
                return "pierce";
            }

            if ((flags & CardEffectVisualFlags.Slow) != 0)
            {
                return "slow";
            }

            if ((flags & CardEffectVisualFlags.Knockback) != 0)
            {
                return "knockback";
            }

            if ((flags & CardEffectVisualFlags.GoldBounty) != 0)
            {
                return "gold_bounty";
            }

            if ((flags & CardEffectVisualFlags.Enlarge) != 0)
            {
                return "enlarge";
            }

            if ((flags & CardEffectVisualFlags.Shrink) != 0)
            {
                return "shrink";
            }

            if ((flags & CardEffectVisualFlags.Split) != 0)
            {
                return "split";
            }

            return string.Empty;
        }

        private static float ResolveTintAlpha(
            CardEffectVisualFlags flags)
        {
            if ((flags & CardEffectVisualFlags.Curse) != 0)
            {
                return 0.42f;
            }

            if ((flags & CardEffectVisualFlags.Bind) != 0)
            {
                return 0.36f;
            }

            if ((flags & CardEffectVisualFlags.Freeze) != 0)
            {
                return 0.44f;
            }

            return 0.27f;
        }

        private static class SharedResources
        {
            private static Sprite shadowSprite;

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
