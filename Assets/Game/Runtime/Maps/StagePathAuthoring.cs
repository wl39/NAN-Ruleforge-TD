using System;
using UnityEngine;

namespace RuleforgeTD.Maps
{
    /// <summary>
    /// Editable scene representation of the deterministic path stored in run
    /// content. It exists for scene alignment, validation, and gizmos.
    /// </summary>
    public sealed class StagePathAuthoring : MonoBehaviour
    {
        [SerializeField]
        private Vector2[] localWaypoints = Array.Empty<Vector2>();

        [SerializeField]
        private Color gizmoColor = new Color(1f, 0.78f, 0.18f, 0.9f);

        public int WaypointCount =>
            localWaypoints == null ? 0 : localWaypoints.Length;

        public void ConfigureAuthoring(Vector2[] waypoints)
        {
            if (waypoints == null || waypoints.Length < 2)
            {
                throw new ArgumentException(
                    "A stage path requires at least two waypoints.",
                    nameof(waypoints));
            }

            localWaypoints = (Vector2[])waypoints.Clone();
        }

        public Vector2 GetWorldWaypoint(int index)
        {
            if (index < 0 || index >= WaypointCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return transform.TransformPoint(localWaypoints[index]);
        }

        public Vector2[] GetLocalWaypointsCopy()
        {
            return localWaypoints == null
                ? Array.Empty<Vector2>()
                : (Vector2[])localWaypoints.Clone();
        }

        private void OnDrawGizmos()
        {
            if (WaypointCount < 2)
            {
                return;
            }

            Gizmos.color = gizmoColor;
            for (int i = 0; i < WaypointCount - 1; i++)
            {
                Vector3 from = transform.TransformPoint(localWaypoints[i]);
                Vector3 to = transform.TransformPoint(localWaypoints[i + 1]);
                Gizmos.DrawLine(from, to);
                Gizmos.DrawWireSphere(from, 0.18f);
            }

            Gizmos.DrawWireSphere(
                transform.TransformPoint(
                    localWaypoints[WaypointCount - 1]),
                0.18f);
        }
    }
}
