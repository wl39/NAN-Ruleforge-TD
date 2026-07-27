using RuleforgeTD.GameLogic.Simulation;
using UnityEngine;

namespace RuleforgeTD.Battle
{
    /// <summary>
    /// Snapshot-driven projectile tint, bounded trail, and view-only airborne
    /// arc. It has no Update callback; StageOne's existing reconciliation loop
    /// refreshes it after the authoritative projectile view is positioned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneProjectileCardEffectVisual :
        MonoBehaviour
    {
        private const string OverlayName =
            "Projectile Card Effect Overlay";
        private const string TrailName =
            "Projectile Card Effect Trail";

        [SerializeField]
        private SpriteRenderer targetRenderer;

        private StageOneProjectileView projectileView;
        private SpriteRenderer overlay;
        private TrailRenderer trail;
        private ProjectileEffectVisualFlags activeFlags;
        private Vector3 appliedPresentationOffset;
        private string dominantEffectId = string.Empty;
        private Color dominantColor = Color.white;

        public ProjectileEffectVisualFlags ActiveFlags =>
            activeFlags;
        public string DominantEffectId =>
            dominantEffectId;
        public Color DominantColor =>
            dominantColor;
        public float AirborneLift =>
            appliedPresentationOffset.y;
        public bool OverlayVisible =>
            overlay != null && overlay.enabled;
        public bool TrailEmitting =>
            trail != null && trail.emitting;

        private void Awake()
        {
            CacheComponents();
            EnsureRenderers();
            ResetVisuals();
        }

        private void OnDisable()
        {
            ResetVisuals();
        }

        public void Configure(
            StageOneProjectileView sourceView,
            SpriteRenderer sourceRenderer = null)
        {
            RestorePresentationOffset();
            projectileView = sourceView != null
                ? sourceView
                : GetComponent<StageOneProjectileView>();
            targetRenderer = sourceRenderer != null
                ? sourceRenderer
                : GetComponent<SpriteRenderer>();
            EnsureRenderers();
            RefreshPresentation();
        }

        public void ApplySnapshot(in ProjectileSnapshot snapshot)
        {
            SetVisualFlags(snapshot.VisualFlags);
            RefreshPresentation();
        }

        /// <summary>
        /// Removes the previous frame's view-only arc before the projectile
        /// view writes its next authoritative snapshot position.
        /// </summary>
        public void PrepareForAuthoritativeSnapshot()
        {
            RestorePresentationOffset();
        }

        public void SetVisualFlags(
            ProjectileEffectVisualFlags flags)
        {
            if (flags == activeFlags)
            {
                return;
            }

            activeFlags = flags;
            RefreshStyle();
        }

        public void RefreshPresentation()
        {
            RestorePresentationOffset();
            EnsureRenderers();
            SynchronizeOverlay();
            ApplyAirborneOffset();
            AnimateOverlay();
        }

        public void ResetVisuals()
        {
            activeFlags = ProjectileEffectVisualFlags.None;
            dominantEffectId = string.Empty;
            dominantColor = Color.white;
            RestorePresentationOffset();
            if (overlay != null)
            {
                overlay.enabled = false;
            }

            if (trail != null)
            {
                trail.emitting = false;
                trail.Clear();
            }
        }

        private void CacheComponents()
        {
            if (projectileView == null)
            {
                projectileView =
                    GetComponent<StageOneProjectileView>();
            }

            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<SpriteRenderer>();
            }
        }

        private void EnsureRenderers()
        {
            CacheComponents();
            if (targetRenderer == null)
            {
                return;
            }

            if (overlay == null)
            {
                Transform existing =
                    targetRenderer.transform.Find(OverlayName);
                GameObject overlayObject;
                if (existing != null)
                {
                    overlayObject = existing.gameObject;
                }
                else
                {
                    overlayObject = new GameObject(OverlayName);
                    overlayObject.transform.SetParent(
                        targetRenderer.transform,
                        false);
                }

                overlay =
                    overlayObject.GetComponent<SpriteRenderer>();
                if (overlay == null)
                {
                    overlay =
                        overlayObject.AddComponent<SpriteRenderer>();
                }

                overlayObject.transform.localPosition = Vector3.zero;
                overlayObject.transform.localRotation =
                    Quaternion.identity;
                overlayObject.transform.localScale = Vector3.one;
                overlay.enabled = false;
            }

            if (trail == null)
            {
                Transform existing =
                    transform.Find(TrailName);
                GameObject trailObject;
                if (existing != null)
                {
                    trailObject = existing.gameObject;
                }
                else
                {
                    trailObject = new GameObject(TrailName);
                    trailObject.transform.SetParent(
                        transform,
                        false);
                }

                trail =
                    trailObject.GetComponent<TrailRenderer>();
                if (trail == null)
                {
                    trail =
                        trailObject.AddComponent<TrailRenderer>();
                }

                trailObject.transform.localPosition = Vector3.zero;
                trailObject.transform.localRotation =
                    Quaternion.identity;
                trailObject.transform.localScale = Vector3.one;
                trail.sharedMaterial = SharedResources.LineMaterial;
                trail.textureMode =
                    LineTextureMode.Stretch;
                trail.alignment =
                    LineAlignment.View;
                trail.minVertexDistance = 0.055f;
                trail.numCapVertices = 0;
                trail.numCornerVertices = 0;
                trail.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.emitting = false;
            }
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
                if (overlay != null)
                {
                    overlay.enabled = false;
                }

                if (trail != null)
                {
                    trail.emitting = false;
                    trail.Clear();
                }

                return;
            }

            dominantColor = style.Primary;
            if (overlay != null)
            {
                Color overlayColor = style.Primary;
                overlayColor.a =
                    (activeFlags &
                     ProjectileEffectVisualFlags.Curse) != 0
                        ? 0.7f
                        : 0.55f;
                overlay.color = overlayColor;
                overlay.enabled = true;
            }

            if (trail != null)
            {
                Color start = style.Secondary;
                start.a = 0.82f;
                Color end = style.Primary;
                end.a = 0f;
                trail.startColor = start;
                trail.endColor = end;
                trail.startWidth = style.Width * 1.35f;
                trail.endWidth = style.Width * 0.2f;
                trail.time =
                    (activeFlags &
                     ProjectileEffectVisualFlags.Afterimage) != 0
                        ? 0.38f
                        : (activeFlags &
                           ProjectileEffectVisualFlags.Accelerate) != 0
                            ? 0.18f
                            : 0.24f;
                trail.emitting = isActiveAndEnabled;
            }
        }

        private void SynchronizeOverlay()
        {
            if (overlay == null ||
                targetRenderer == null)
            {
                return;
            }

            overlay.sprite = targetRenderer.sprite;
            overlay.flipX = targetRenderer.flipX;
            overlay.flipY = targetRenderer.flipY;
            overlay.sortingLayerID =
                targetRenderer.sortingLayerID;
            overlay.sortingOrder =
                targetRenderer.sortingOrder + 1;
            overlay.enabled =
                activeFlags !=
                    ProjectileEffectVisualFlags.None &&
                targetRenderer.enabled &&
                targetRenderer.sprite != null;

            if (trail != null)
            {
                trail.sortingLayerID =
                    targetRenderer.sortingLayerID;
                trail.sortingOrder =
                    targetRenderer.sortingOrder - 1;
                trail.emitting =
                    activeFlags !=
                        ProjectileEffectVisualFlags.None &&
                    gameObject.activeInHierarchy;
            }
        }

        private void AnimateOverlay()
        {
            if (overlay == null || !overlay.enabled)
            {
                return;
            }

            float time = Time.unscaledTime;
            float pulse =
                1f + Mathf.Sin(time * 12f) * 0.09f;
            overlay.transform.localScale =
                Vector3.one * pulse;

            if ((activeFlags &
                 ProjectileEffectVisualFlags.Delay) != 0)
            {
                overlay.transform.localRotation =
                    Quaternion.Euler(
                        0f,
                        0f,
                        -time * 80f);
            }
            else if ((activeFlags &
                      ProjectileEffectVisualFlags.Shock) != 0)
            {
                overlay.transform.localRotation =
                    Quaternion.Euler(
                        0f,
                        0f,
                        Mathf.Sin(time * 45f) * 8f);
            }
            else
            {
                overlay.transform.localRotation =
                    Quaternion.identity;
            }
        }

        private void ApplyAirborneOffset()
        {
            if ((activeFlags &
                 ProjectileEffectVisualFlags.Airborne) == 0 ||
                projectileView == null)
            {
                return;
            }

            float distance = Vector3.Distance(
                projectileView.PresentationLaunchPosition,
                projectileView.PresentationAimPoint);
            float progress;
            if (projectileView.IsUsingAimLine &&
                distance > 0.0001f)
            {
                progress = Mathf.Clamp01(
                    projectileView.PresentationDistanceTravelled /
                    distance);
            }
            else
            {
                progress =
                    0.5f +
                    Mathf.Sin(Time.unscaledTime * 4f) * 0.12f;
            }

            StageOneCardEffectPalette.TryGetStyle(
                "airborne",
                out StageOneCardEffectStyle airborneStyle);
            float lift =
                Mathf.Sin(progress * Mathf.PI) *
                airborneStyle.MotionHeight;
            appliedPresentationOffset =
                new Vector3(0f, Mathf.Max(0.08f, lift), 0f);
            transform.position += appliedPresentationOffset;
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

        private static string ResolveDominantEffectId(
            ProjectileEffectVisualFlags flags)
        {
            if ((flags & ProjectileEffectVisualFlags.Airborne) != 0)
            {
                return "airborne";
            }

            if ((flags & ProjectileEffectVisualFlags.Stun) != 0)
            {
                return "stun";
            }

            if ((flags & ProjectileEffectVisualFlags.Explode) != 0)
            {
                return "explode";
            }

            if ((flags & ProjectileEffectVisualFlags.Reflect) != 0)
            {
                return "reflect";
            }

            if ((flags & ProjectileEffectVisualFlags.Afterimage) != 0)
            {
                return "afterimage";
            }

            if ((flags & ProjectileEffectVisualFlags.Shock) != 0)
            {
                return "shock";
            }

            if ((flags & ProjectileEffectVisualFlags.Freeze) != 0)
            {
                return "freeze";
            }

            if ((flags & ProjectileEffectVisualFlags.Curse) != 0)
            {
                return "curse";
            }

            if ((flags & ProjectileEffectVisualFlags.Bind) != 0)
            {
                return "bind";
            }

            if ((flags & ProjectileEffectVisualFlags.Magnet) != 0)
            {
                return "magnet";
            }

            if ((flags & ProjectileEffectVisualFlags.Contagion) != 0)
            {
                return "contagion";
            }

            if ((flags & ProjectileEffectVisualFlags.Seal) != 0)
            {
                return "seal";
            }

            if ((flags & ProjectileEffectVisualFlags.Corrosion) != 0)
            {
                return "corrosion";
            }

            if ((flags & ProjectileEffectVisualFlags.Orbit) != 0)
            {
                return "orbit";
            }

            if ((flags & ProjectileEffectVisualFlags.Lifesteal) != 0)
            {
                return "lifesteal";
            }

            if ((flags & ProjectileEffectVisualFlags.Fear) != 0)
            {
                return "fear";
            }

            if ((flags & ProjectileEffectVisualFlags.Pulse) != 0)
            {
                return "pulse";
            }

            if ((flags & ProjectileEffectVisualFlags.Delay) != 0)
            {
                return "delay";
            }

            if ((flags & ProjectileEffectVisualFlags.Homing) != 0)
            {
                return "homing";
            }

            if ((flags & ProjectileEffectVisualFlags.Accelerate) != 0)
            {
                return "accelerate";
            }

            if ((flags & ProjectileEffectVisualFlags.Ricochet) != 0)
            {
                return "ricochet";
            }

            if ((flags & ProjectileEffectVisualFlags.Bleed) != 0)
            {
                return "bleed";
            }

            if ((flags & ProjectileEffectVisualFlags.Burn) != 0)
            {
                return "burn";
            }

            if ((flags & ProjectileEffectVisualFlags.Poison) != 0)
            {
                return "poison";
            }

            if ((flags & ProjectileEffectVisualFlags.Mark) != 0)
            {
                return "mark";
            }

            if ((flags & ProjectileEffectVisualFlags.Pierce) != 0)
            {
                return "pierce";
            }

            if ((flags & ProjectileEffectVisualFlags.Knockback) != 0)
            {
                return "knockback";
            }

            if ((flags & ProjectileEffectVisualFlags.GoldBounty) != 0)
            {
                return "gold_bounty";
            }

            if ((flags & ProjectileEffectVisualFlags.Enlarge) != 0)
            {
                return "enlarge";
            }

            if ((flags & ProjectileEffectVisualFlags.Shrink) != 0)
            {
                return "shrink";
            }

            if ((flags & ProjectileEffectVisualFlags.Slow) != 0)
            {
                return "slow";
            }

            if ((flags & ProjectileEffectVisualFlags.Split) != 0)
            {
                return "split";
            }

            return string.Empty;
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
                            "Ruleforge Projectile Effect Trail Material",
                        hideFlags =
                            HideFlags.HideAndDontSave
                    };
                    return lineMaterial;
                }
            }
        }
    }
}
