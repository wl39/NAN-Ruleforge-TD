using RuleforgeTD.Rendering;
using UnityEngine;

namespace RuleforgeTD.Enemies.Testing
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DirectionalEnemyAnimator))]
    public sealed class EnemyTestActor : MonoBehaviour
    {
        [SerializeField] private DirectionalEnemyAnimator directionalAnimator;
        [SerializeField, Min(0.1f)] private float horizontalHalfRange = 1.8f;
        [SerializeField, Min(0.1f)] private float verticalHalfRange = 0.7f;
        [SerializeField, Min(0.1f)] private float movementSpeed = 1.4f;

        private Vector2 routeCenter;
        private int targetWaypointIndex;
        private bool initialized;
        private bool movementEnabled = true;

        public bool MovementEnabled => movementEnabled;

        public void Configure(
            DirectionalEnemyAnimator targetAnimator,
            float horizontalRange,
            float verticalRange,
            float speed)
        {
            directionalAnimator = targetAnimator;
            horizontalHalfRange = horizontalRange;
            verticalHalfRange = verticalRange;
            movementSpeed = speed;
        }

        public void InitializeRoute()
        {
            if (directionalAnimator == null)
            {
                directionalAnimator = GetComponent<DirectionalEnemyAnimator>();
            }

            routeCenter = transform.position;
            transform.position = GetWaypoint(0);
            targetWaypointIndex = 1;
            initialized = true;
        }

        public void CopyRuntimeRouteFrom(
            EnemyTestActor source,
            Vector2 positionOffset)
        {
            if (source == null ||
                !source.gameObject.activeInHierarchy)
            {
                return;
            }

            if (!source.initialized)
            {
                source.InitializeRoute();
            }

            if (directionalAnimator == null)
            {
                directionalAnimator = GetComponent<DirectionalEnemyAnimator>();
            }

            horizontalHalfRange = source.horizontalHalfRange;
            verticalHalfRange = source.verticalHalfRange;
            movementSpeed = source.movementSpeed;
            routeCenter = source.routeCenter;
            targetWaypointIndex = source.targetWaypointIndex;
            transform.position =
                source.transform.position + (Vector3)positionOffset;
            initialized = true;
            movementEnabled = source.movementEnabled;
        }

        public void SetMovementEnabled(bool enabled)
        {
            movementEnabled = enabled;
        }

        public void Simulate(float deltaTime)
        {
            if (!initialized)
            {
                InitializeRoute();
            }

            if (!movementEnabled)
            {
                return;
            }

            Vector2 currentPosition = transform.position;
            Vector2 targetPosition = GetWaypoint(targetWaypointIndex);
            Vector2 displacement = targetPosition - currentPosition;

            if (displacement.sqrMagnitude <= 0.0001f)
            {
                transform.position = targetPosition;
                targetWaypointIndex = (targetWaypointIndex + 1) % 4;
                targetPosition = GetWaypoint(targetWaypointIndex);
                displacement = targetPosition - (Vector2)transform.position;
            }

            Vector2 velocity = displacement.normalized * movementSpeed;
            transform.position = Vector2.MoveTowards(
                transform.position,
                targetPosition,
                movementSpeed * deltaTime);
            directionalAnimator.SetMovement(velocity);
        }

        private Vector2 GetWaypoint(int index)
        {
            switch (index)
            {
                case 0:
                    return routeCenter + new Vector2(-horizontalHalfRange, -verticalHalfRange);
                case 1:
                    return routeCenter + new Vector2(horizontalHalfRange, -verticalHalfRange);
                case 2:
                    return routeCenter + new Vector2(horizontalHalfRange, verticalHalfRange);
                default:
                    return routeCenter + new Vector2(-horizontalHalfRange, verticalHalfRange);
            }
        }
    }
}
