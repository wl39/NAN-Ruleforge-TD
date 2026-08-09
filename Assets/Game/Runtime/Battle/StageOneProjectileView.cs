using RuleforgeTD.GameLogic.Simulation;
using RuleforgeTD.Rendering;
using RuleforgeTD.Towers.Archer;
using UnityEngine;

namespace RuleforgeTD.Battle
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class StageOneProjectileView : MonoBehaviour
    {
        public const float VisualScaleMultiplier = 1.65f;
        public const float ImpactTravelSpeed = 11f;
        public const float MinimumImpactTravelSeconds = 0.045f;
        public const float MaximumImpactTravelSeconds = 0.11f;
        public const float ImpactCenterHoldSeconds = 0.035f;
        public const float AimCorrectionSpeed = 20f;

        [SerializeField]
        private SpriteRenderer targetRenderer;

        private StageOnePresentationCatalog catalog;
        private int projectileId = -1;
        private Vector3 launchOffset;
        private Vector3 lastLaunchOrigin;
        private Vector3 lastLogicalPosition;
        private Vector3 presentationLaunchPosition;
        private Vector3 presentationAimPoint;
        private Vector3 impactStartPosition;
        private Vector3 impactTargetPosition;
        private Vector3 preparedImpactPosition;
        private StageOneEnemyView trackedAimTarget;
        private StageOneEnemyView preparedImpactTarget;
        private int trackedAimTargetId = -1;
        private float presentationDistanceTravelled;
        private float impactTravelDuration;
        private float impactElapsed;
        private Color rendererBaseColor = Color.white;
        private bool rendererColorCaptured;
        private bool impactPresentationActive;
        private bool hasLaunchOrigin;
        private bool aimLineActive;

        public int ProjectileId => projectileId;
        public bool HasLaunchOrigin => hasLaunchOrigin;
        public Vector3 LastLaunchOrigin => lastLaunchOrigin;
        public bool IsImpactPresentationActive =>
            impactPresentationActive;
        public Vector3 ImpactStartPosition => impactStartPosition;
        public Vector3 ImpactTargetPosition => impactTargetPosition;
        public Vector3 PresentationLaunchPosition =>
            presentationLaunchPosition;
        public Vector3 PresentationAimPoint => presentationAimPoint;
        public float PresentationDistanceTravelled =>
            presentationDistanceTravelled;
        public bool IsUsingAimLine => aimLineActive;

        private void Awake()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<SpriteRenderer>();
            }

            CaptureRendererColor();
        }

        private void Update()
        {
            if (!impactPresentationActive)
            {
                return;
            }

            impactElapsed += Time.deltaTime;
            if (impactElapsed < impactTravelDuration)
            {
                float progress = Mathf.Clamp01(
                    impactElapsed /
                    Mathf.Max(
                        0.0001f,
                        impactTravelDuration));
                transform.position = Vector3.Lerp(
                    impactStartPosition,
                    impactTargetPosition,
                    progress);
                return;
            }

            transform.position = impactTargetPosition;
            float holdElapsed =
                impactElapsed - impactTravelDuration;
            if (holdElapsed < ImpactCenterHoldSeconds)
            {
                if (targetRenderer != null)
                {
                    float alpha =
                        1f -
                        Mathf.Clamp01(
                            holdElapsed /
                            ImpactCenterHoldSeconds);
                    Color faded = rendererBaseColor;
                    faded.a *= alpha;
                    targetRenderer.color = faded;
                }

                return;
            }

            CompletePoolReturn();
        }

        public void Configure(StageOnePresentationCatalog sourceCatalog)
        {
            catalog = sourceCatalog;
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<SpriteRenderer>();
            }

            WorldSortingLayers.Apply(
                targetRenderer,
                WorldSortingLayers.Effects);
            targetRenderer.sortingOrder = 40;
            CaptureRendererColor();
        }

        public void ApplySnapshot(
            in ProjectileSnapshot snapshot,
            Vector3? initialWorldPosition = null,
            StageOneEnemyView aimTarget = null)
        {
            bool isNewProjectile = projectileId != snapshot.Id;
            bool survivedPreparedImpact =
                !isNewProjectile &&
                impactPresentationActive;
            Vector3 continuationPosition =
                impactTargetPosition;
            if (impactPresentationActive)
            {
                CancelImpactPresentation();
            }

            projectileId = snapshot.Id;
            Vector3 logicalPosition = ToWorld(snapshot.Position);
            Vector2 simulationDirection = new Vector2(
                snapshot.DirectionXBps,
                snapshot.DirectionYBps);
            if (simulationDirection.sqrMagnitude <= 0.000001f)
            {
                simulationDirection = Vector2.up;
            }

            if (isNewProjectile)
            {
                hasLaunchOrigin = initialWorldPosition.HasValue;
                lastLaunchOrigin =
                    initialWorldPosition ?? logicalPosition;
                lastLaunchOrigin.z = logicalPosition.z;
                launchOffset =
                    lastLaunchOrigin - logicalPosition;
                lastLogicalPosition = logicalPosition;
                presentationLaunchPosition =
                    lastLaunchOrigin;
                presentationDistanceTravelled = 0f;
                trackedAimTarget =
                    aimTarget != null &&
                    aimTarget.EntityId == snapshot.TargetId
                        ? aimTarget
                        : null;
                trackedAimTargetId =
                    trackedAimTarget != null
                        ? snapshot.TargetId
                        : -1;
                aimLineActive = trackedAimTarget != null;
                presentationAimPoint = aimLineActive
                    ? trackedAimTarget.LogicalImpactCenter
                    : Vector3.zero;
                transform.position =
                    presentationLaunchPosition;
            }
            else
            {
                float travelledThisFrame =
                    Vector3.Distance(
                        lastLogicalPosition,
                        logicalPosition);
                lastLogicalPosition = logicalPosition;

                if (survivedPreparedImpact)
                {
                    // A pierced arrow resumes from the exact center it just
                    // crossed. The simulation continues to own direction and
                    // speed from this snapshot onward.
                    aimLineActive = false;
                    trackedAimTarget = null;
                    trackedAimTargetId = -1;
                    presentationDistanceTravelled = 0f;
                    launchOffset =
                        continuationPosition -
                        logicalPosition;
                    transform.position =
                        continuationPosition;
                }
                else if (aimLineActive)
                {
                    presentationDistanceTravelled +=
                        travelledThisFrame;
                    if (aimTarget != null &&
                        aimTarget.EntityId == snapshot.TargetId &&
                        (trackedAimTarget == null ||
                         snapshot.Homing ||
                         snapshot.TargetId ==
                         trackedAimTargetId))
                    {
                        trackedAimTarget = aimTarget;
                        trackedAimTargetId =
                            snapshot.TargetId;
                    }

                    UpdateTrackedAimPoint();
                    transform.position =
                        ResolveAimLinePosition();
                }
                else
                {
                    transform.position =
                        logicalPosition + launchOffset;
                }
            }

            Vector2 visualDirection = aimLineActive
                ? (Vector2)(
                    presentationAimPoint -
                    presentationLaunchPosition)
                : simulationDirection;
            if (visualDirection.sqrMagnitude <= 0.000001f)
            {
                visualDirection = simulationDirection;
            }

            if (catalog != null &&
                catalog.ProjectileDirectionCount > 0 &&
                targetRenderer != null)
            {
                ArcherArrowVisual visual =
                    ArcherArrowDirectionResolver.Resolve(
                        visualDirection,
                        catalog.ProjectileDirectionCount);
                targetRenderer.sprite =
                    catalog.GetProjectileDirectionSprite(
                        visual.SpriteIndex);
                targetRenderer.flipX = visual.FlipX;
                targetRenderer.flipY = visual.FlipY;
            }

            float radiusScale = Mathf.Clamp(
                snapshot.RadiusMilli / 150f,
                0.65f,
                2.5f);
            transform.localScale =
                Vector3.one *
                radiusScale *
                VisualScaleMultiplier;
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Arms an exact view-only impact using the target resolved by the
        /// simulation's ProjectileHit presentation event. If the projectile
        /// survives through piercing, its next snapshot cancels this terminal
        /// animation and resumes the authoritative trajectory.
        /// </summary>
        public bool PrepareImpact(StageOneEnemyView target)
        {
            if (target == null ||
                projectileId < 0 ||
                !gameObject.activeInHierarchy)
            {
                return false;
            }

            preparedImpactTarget = target;
            bool isTrackedTarget =
                aimLineActive &&
                trackedAimTargetId == target.EntityId;
            preparedImpactPosition = isTrackedTarget
                ? presentationAimPoint
                : target.LogicalImpactCenter;
            preparedImpactPosition.z =
                transform.position.z;
            BeginImpactPresentation(
                preparedImpactPosition);
            target.PlayHitFeedback(transform.position);
            return true;
        }

        public void ReturnToPool()
        {
            bool canFinishAtImpact =
                impactPresentationActive &&
                preparedImpactTarget != null &&
                preparedImpactTarget.isActiveAndEnabled;

            projectileId = -1;
            launchOffset = Vector3.zero;
            lastLaunchOrigin = Vector3.zero;
            hasLaunchOrigin = false;

            if (!canFinishAtImpact)
            {
                CompletePoolReturn();
                return;
            }

            BeginImpactPresentation(
                preparedImpactPosition);
        }

        private void BeginImpactPresentation(
            Vector3 targetPosition)
        {
            impactPresentationActive = true;
            impactStartPosition = transform.position;
            impactTargetPosition = targetPosition;
            impactTargetPosition.z = transform.position.z;
            impactElapsed = 0f;
            float distance = Vector3.Distance(
                impactStartPosition,
                impactTargetPosition);
            impactTravelDuration = Mathf.Clamp(
                distance / ImpactTravelSpeed,
                MinimumImpactTravelSeconds,
                MaximumImpactTravelSeconds);
            if (targetRenderer != null)
            {
                targetRenderer.color = rendererBaseColor;
            }

            gameObject.SetActive(true);
        }

        private void CancelImpactPresentation()
        {
            impactPresentationActive = false;
            impactElapsed = 0f;
            impactTravelDuration = 0f;
            impactStartPosition = Vector3.zero;
            impactTargetPosition = Vector3.zero;
            preparedImpactPosition = Vector3.zero;
            trackedAimTarget = null;
            trackedAimTargetId = -1;
            preparedImpactTarget = null;
            if (targetRenderer != null)
            {
                targetRenderer.color = rendererBaseColor;
            }
        }

        private void CompletePoolReturn()
        {
            CancelImpactPresentation();
            lastLogicalPosition = Vector3.zero;
            presentationLaunchPosition = Vector3.zero;
            presentationAimPoint = Vector3.zero;
            presentationDistanceTravelled = 0f;
            aimLineActive = false;
            gameObject.SetActive(false);
        }

        private void UpdateTrackedAimPoint()
        {
            if (trackedAimTarget == null)
            {
                return;
            }

            if (trackedAimTarget.EntityId !=
                trackedAimTargetId)
            {
                trackedAimTarget = null;
                return;
            }

            Vector3 desiredAim =
                trackedAimTarget.LogicalImpactCenter;
            desiredAim.z = presentationLaunchPosition.z;
            float maximumCorrection =
                AimCorrectionSpeed * Time.deltaTime;
            presentationAimPoint = Vector3.MoveTowards(
                presentationAimPoint,
                desiredAim,
                maximumCorrection);
        }

        private Vector3 ResolveAimLinePosition()
        {
            Vector3 ray =
                presentationAimPoint -
                presentationLaunchPosition;
            float aimDistance = ray.magnitude;
            if (aimDistance <= 0.0001f)
            {
                return presentationLaunchPosition;
            }

            float distance = Mathf.Min(
                presentationDistanceTravelled,
                aimDistance);
            return presentationLaunchPosition +
                   ray / aimDistance * distance;
        }

        private void CaptureRendererColor()
        {
            if (rendererColorCaptured || targetRenderer == null)
            {
                return;
            }

            rendererBaseColor = targetRenderer.color;
            rendererColorCaptured = true;
        }

        private static Vector3 ToWorld(
            RuleforgeTD.GameLogic.Core.SimPosition position)
        {
            return new Vector3(
                position.X.MilliUnits / 1000f,
                position.Y.MilliUnits / 1000f,
                -0.08f);
        }
    }
}
