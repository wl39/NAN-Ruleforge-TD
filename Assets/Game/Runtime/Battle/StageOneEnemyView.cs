using RuleforgeTD.Enemies;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;
using RuleforgeTD.Rendering;
using RuleforgeTD.StatusEffects;
using UnityEngine;

namespace RuleforgeTD.Battle
{
    [DisallowMultipleComponent]
    public sealed class StageOneEnemyView : MonoBehaviour
    {
        private const float DeathPresentationSeconds = 0.55f;
        public const float HitStaggerSeconds = 0.22f;
        public const float HitFlashHoldSeconds = 0.07f;
        public const float HitRecoilDistance = 0.16f;
        public const float HitSquashStrength = 0.1f;

        [SerializeField]
        private SpriteRenderer targetRenderer;

        [SerializeField]
        private DirectionalEnemyAnimator directionalAnimator;

        [SerializeField]
        private EnemyHealth health;

        [SerializeField]
        private EnemyStatusVisualView statusVisual;

        private int entityId = -1;
        private string definitionId = string.Empty;
        private Vector3 prefabScale = Vector3.one;
        private Vector3 authoredScale = Vector3.one;
        private Vector3 logicalScale = Vector3.one;
        private Vector3 logicalPosition;
        private Vector3 previousPosition;
        private Vector2 latestMovement;
        private Vector2 hitRecoilDirection = Vector2.left;
        private long lastHealthMilli = -1L;
        private int displayedHealth = -1;
        private int displayedMaxHealth = -1;
        private float deathPresentationRemaining;
        private float hitFeedbackRemaining;
        private bool seenThisFrame;
        private bool scaleCaptured;

        public int EntityId => entityId;
        public string DefinitionId => definitionId;
        public bool SeenThisFrame => seenThisFrame;
        public bool IsDeathPresentationActive =>
            deathPresentationRemaining > 0f;
        public bool IsHitFeedbackActive =>
            hitFeedbackRemaining > 0f;
        public float HitFeedbackRemaining => hitFeedbackRemaining;
        public Vector3 LogicalPosition => logicalPosition;
        public Vector3 LogicalImpactCenter
        {
            get
            {
                Vector3 visualOffset =
                    targetRenderer != null &&
                    targetRenderer.sprite != null
                        ? targetRenderer.bounds.center -
                          transform.position
                        : Vector3.zero;
                Vector3 center =
                    logicalPosition + visualOffset;
                center.z = -0.08f;
                return center;
            }
        }
        public Vector3 WorldImpactCenter
        {
            get
            {
                Vector3 center = targetRenderer != null &&
                                 targetRenderer.sprite != null
                    ? targetRenderer.bounds.center
                    : transform.position;
                center.z = -0.08f;
                return center;
            }
        }
        public EnemyStatusVisualView StatusVisual => statusVisual;

        private void Awake()
        {
            CacheComponents();
        }

        private void Update()
        {
            if (deathPresentationRemaining > 0f)
            {
                deathPresentationRemaining = Mathf.Max(
                    0f,
                    deathPresentationRemaining -
                    Time.deltaTime);
            }

            if (hitFeedbackRemaining > 0f)
            {
                hitFeedbackRemaining = Mathf.Max(
                    0f,
                    hitFeedbackRemaining -
                    Time.unscaledDeltaTime);
                ApplyHitPresentation();
            }
        }

        public void Configure(
            int id,
            string stableDefinitionId,
            float scaleMultiplier)
        {
            CacheComponents();
            entityId = id;
            definitionId = stableDefinitionId ?? string.Empty;
            if (!scaleCaptured)
            {
                prefabScale = transform.localScale;
                scaleCaptured = true;
            }

            authoredScale =
                prefabScale *
                Mathf.Max(0.1f, scaleMultiplier);
            logicalScale = authoredScale;
            logicalPosition = transform.position;
            previousPosition = logicalPosition;
            latestMovement = Vector2.zero;
            hitRecoilDirection =
                (id & 1) == 0
                    ? Vector2.left
                    : Vector2.right;
            lastHealthMilli = -1L;
            displayedHealth = -1;
            displayedMaxHealth = -1;
            deathPresentationRemaining = 0f;
            hitFeedbackRemaining = 0f;
            seenThisFrame = true;
            transform.localScale = logicalScale;

            if (statusVisual == null)
            {
                statusVisual =
                    gameObject.AddComponent<EnemyStatusVisualView>();
            }

            statusVisual.Configure(targetRenderer);
            statusVisual.ResetVisuals();
            gameObject.SetActive(true);
        }

        public void BeginSnapshotFrame()
        {
            seenThisFrame = false;
        }

        public void ApplySnapshot(in EnemySnapshot snapshot)
        {
            seenThisFrame = true;
            Vector3 position = ToWorld(snapshot.Position);
            Vector2 movement = position - previousPosition;
            logicalPosition = position;
            previousPosition = position;
            if (movement.sqrMagnitude > 0.000001f)
            {
                latestMovement = movement.normalized;
            }

            if (directionalAnimator != null &&
                movement.sqrMagnitude > 0.000001f)
            {
                directionalAnimator.SetMovement(movement);
            }

            float sizeMultiplier =
                Mathf.Max(0.1f, snapshot.SizeMultiplierBps / 10000f);
            logicalScale =
                authoredScale * sizeMultiplier;
            transform.localScale = logicalScale;

            if (snapshot.Alive)
            {
                bool tookDamage =
                    lastHealthMilli >= 0L &&
                    snapshot.HealthMilli < lastHealthMilli;
                lastHealthMilli = snapshot.HealthMilli;
                if (tookDamage)
                {
                    PlayHitFeedback();
                }

                ApplyHealth(snapshot);
                if (statusVisual != null)
                {
                    statusVisual.ApplySnapshot(
                        snapshot.StatusDetails);
                }

                ApplyHitPresentation();
            }
            else
            {
                BeginDeath();
            }
        }

        /// <summary>
        /// Plays view-only hit feedback. Supplying an impact origin makes the
        /// recoil move away from the incoming arrow; snapshot movement remains
        /// authoritative and is never modified.
        /// </summary>
        public void PlayHitFeedback(Vector3? impactOrigin = null)
        {
            Vector2 recoil = Vector2.zero;
            if (impactOrigin.HasValue)
            {
                recoil =
                    (Vector2)(
                        WorldImpactCenter -
                        impactOrigin.Value);
            }

            if (recoil.sqrMagnitude <= 0.000001f)
            {
                recoil = latestMovement.sqrMagnitude > 0.000001f
                    ? -latestMovement
                    : (entityId & 1) == 0
                        ? Vector2.left
                        : Vector2.right;
            }

            hitRecoilDirection = recoil.normalized;
            hitFeedbackRemaining = HitStaggerSeconds;
            ApplyHitPresentation();
        }

        public void BeginDeath()
        {
            if (deathPresentationRemaining > 0f)
            {
                return;
            }

            deathPresentationRemaining =
                DeathPresentationSeconds;
            hitFeedbackRemaining = 0f;
            transform.position = logicalPosition;
            transform.localScale = logicalScale;
            if (statusVisual != null)
            {
                statusVisual.ResetVisuals();
            }

            if (health != null && !health.IsDead)
            {
                health.Kill();
            }
            else if (directionalAnimator != null)
            {
                directionalAnimator.PlayBehaviour(
                    EnemyAnimationBehaviour.Death);
            }
        }

        public void ReturnToPool()
        {
            if (statusVisual != null)
            {
                statusVisual.ResetVisuals();
            }

            entityId = -1;
            definitionId = string.Empty;
            displayedHealth = -1;
            displayedMaxHealth = -1;
            lastHealthMilli = -1L;
            deathPresentationRemaining = 0f;
            hitFeedbackRemaining = 0f;
            seenThisFrame = false;
            transform.localScale = authoredScale;
            logicalScale = authoredScale;
            transform.position = logicalPosition;
            gameObject.SetActive(false);
        }

        private void ApplyHealth(in EnemySnapshot snapshot)
        {
            if (health == null)
            {
                return;
            }

            int maximum = ToDisplayedHealth(
                snapshot.MaxHealthMilli);
            int current = snapshot.HealthMilli <= 0
                ? 0
                : ToDisplayedHealth(snapshot.HealthMilli);
            current = Mathf.Clamp(current, 0, maximum);
            if (maximum == displayedMaxHealth &&
                current == displayedHealth &&
                !health.IsDead)
            {
                return;
            }

            displayedMaxHealth = maximum;
            displayedHealth = current;
            health.Configure(
                maximum,
                current,
                directionalAnimator);
        }

        private void CacheComponents()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<SpriteRenderer>();
            }

            if (directionalAnimator == null)
            {
                directionalAnimator =
                    GetComponent<DirectionalEnemyAnimator>();
            }

            if (health == null)
            {
                health = GetComponent<EnemyHealth>();
            }

            if (statusVisual == null)
            {
                statusVisual =
                    GetComponent<EnemyStatusVisualView>();
            }
        }

        private void ApplyHitPresentation()
        {
            float remainingStrength = hitFeedbackRemaining <= 0f
                ? 0f
                : Mathf.Clamp01(
                    hitFeedbackRemaining /
                    HitStaggerSeconds);
            float recoilStrength =
                remainingStrength * remainingStrength;
            Vector3 offset =
                (Vector3)(
                    hitRecoilDirection *
                    (HitRecoilDistance * recoilStrength));
            transform.position = logicalPosition + offset;

            float progress = 1f - remainingStrength;
            float squash = hitFeedbackRemaining <= 0f
                ? 0f
                : Mathf.Sin(progress * Mathf.PI) *
                  HitSquashStrength;
            transform.localScale = new Vector3(
                logicalScale.x * (1f + squash),
                logicalScale.y * (1f - squash * 0.72f),
                logicalScale.z);

            if (statusVisual != null)
            {
                float elapsed =
                    HitStaggerSeconds - hitFeedbackRemaining;
                float flashStrength;
                if (hitFeedbackRemaining <= 0f)
                {
                    flashStrength = 0f;
                }
                else if (elapsed <= HitFlashHoldSeconds)
                {
                    flashStrength = 1f;
                }
                else
                {
                    flashStrength = 1f - Mathf.InverseLerp(
                        HitFlashHoldSeconds,
                        HitStaggerSeconds,
                        elapsed);
                }

                statusVisual.SetImpactFlashStrength(
                    flashStrength);
            }
        }

        private static int ToDisplayedHealth(long milliHealth)
        {
            if (milliHealth <= 0)
            {
                return 1;
            }

            long result = (milliHealth + 999L) / 1000L;
            return result >= int.MaxValue
                ? int.MaxValue
                : (int)result;
        }

        private static Vector3 ToWorld(SimPosition position)
        {
            return new Vector3(
                position.X.MilliUnits / 1000f,
                position.Y.MilliUnits / 1000f,
                -0.05f);
        }
    }
}
