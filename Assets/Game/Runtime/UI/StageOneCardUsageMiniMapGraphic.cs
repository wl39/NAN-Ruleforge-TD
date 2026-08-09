using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// One build-site marker used by the inventory-card usage minimap.
    /// Coordinates are presentation-only world positions supplied by the
    /// battle controller; combat and placement authority remain in GameLogic.
    /// </summary>
    public readonly struct StageOneLoadoutMapSite
    {
        public StageOneLoadoutMapSite(
            int towerId,
            Vector2 worldPosition,
            bool occupied)
        {
            TowerId = towerId;
            WorldPosition = worldPosition;
            Occupied = occupied;
        }

        public int TowerId { get; }
        public Vector2 WorldPosition { get; }
        public bool Occupied { get; }
    }

    /// <summary>
    /// Deliberately abstract map: blocked ground, road, empty build pads,
    /// placed towers, and the tower using the hovered card. It avoids scene
    /// textures and labels so a glance answers only “where is this used?”.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneCardUsageMiniMapGraphic :
        MaskableGraphic
    {
        public static readonly Color BlockedGroundColor =
            new Color32(92, 126, 72, 255);
        public static readonly Color RoadColor =
            new Color32(235, 150, 97, 255);
        public static readonly Color EmptyTowerColor =
            new Color32(119, 123, 126, 255);
        public static readonly Color OccupiedTowerColor =
            new Color32(240, 196, 66, 255);
        public static readonly Color FocusedTowerColor =
            new Color32(76, 224, 222, 255);

        private static readonly Color SiteOutlineColor =
            new Color32(47, 36, 27, 255);

        private readonly List<Vector2> pathPoints =
            new List<Vector2>(16);
        private readonly List<StageOneLoadoutMapSite> sites =
            new List<StageOneLoadoutMapSite>(12);
        private int focusedTowerId = -1;

        public int PathPointCount => pathPoints.Count;
        public int SiteCount => sites.Count;
        public int FocusedTowerId => focusedTowerId;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            color = Color.white;
        }

        public void SetMap(
            IReadOnlyList<Vector2> waypoints,
            IReadOnlyList<StageOneLoadoutMapSite> buildSites)
        {
            pathPoints.Clear();
            if (waypoints != null)
            {
                for (int index = 0;
                     index < waypoints.Count;
                     index++)
                {
                    pathPoints.Add(waypoints[index]);
                }
            }

            sites.Clear();
            if (buildSites != null)
            {
                for (int index = 0;
                     index < buildSites.Count;
                     index++)
                {
                    sites.Add(buildSites[index]);
                }
            }

            SetVerticesDirty();
        }

        public void SetFocusedTower(int towerId)
        {
            if (focusedTowerId == towerId)
            {
                return;
            }

            focusedTowerId = towerId;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            AddQuad(helper, rect, BlockedGroundColor);
            CalculateWorldBounds(
                out Vector2 minimum,
                out Vector2 maximum);
            float padding = Mathf.Clamp(
                Mathf.Min(rect.width, rect.height) * 0.08f,
                7f,
                16f);
            Rect contentRect = new Rect(
                rect.xMin + padding,
                rect.yMin + padding,
                Mathf.Max(1f, rect.width - padding * 2f),
                Mathf.Max(1f, rect.height - padding * 2f));
            Vector2 range = new Vector2(
                Mathf.Max(1f, maximum.x - minimum.x),
                Mathf.Max(1f, maximum.y - minimum.y));
            float scale = Mathf.Min(
                contentRect.width / range.x,
                contentRect.height / range.y);
            Vector2 worldCenter = (minimum + maximum) * 0.5f;
            Vector2 localCenter = contentRect.center;

            Vector2 Map(Vector2 world)
            {
                return localCenter +
                    (world - worldCenter) * scale;
            }

            float roadWidth = Mathf.Clamp(
                Mathf.Min(rect.width, rect.height) * 0.095f,
                9f,
                18f);
            for (int index = 0;
                 index + 1 < pathPoints.Count;
                 index++)
            {
                AddLine(
                    helper,
                    Map(pathPoints[index]),
                    Map(pathPoints[index + 1]),
                    roadWidth,
                    RoadColor);
            }

            float siteSize = Mathf.Clamp(
                Mathf.Min(rect.width, rect.height) * 0.09f,
                10f,
                16f);
            for (int index = 0; index < sites.Count; index++)
            {
                StageOneLoadoutMapSite site = sites[index];
                Vector2 center = Map(site.WorldPosition);
                AddCenteredSquare(
                    helper,
                    center,
                    siteSize + 4f,
                    SiteOutlineColor);
                Color siteColor = !site.Occupied
                    ? EmptyTowerColor
                    : site.TowerId == focusedTowerId
                        ? FocusedTowerColor
                        : OccupiedTowerColor;
                AddCenteredSquare(
                    helper,
                    center,
                    siteSize,
                    siteColor);
            }
        }

        private void CalculateWorldBounds(
            out Vector2 minimum,
            out Vector2 maximum)
        {
            bool hasPoint = false;
            minimum = Vector2.zero;
            maximum = Vector2.one;

            for (int index = 0;
                 index < pathPoints.Count;
                 index++)
            {
                Encapsulate(
                    pathPoints[index],
                    ref hasPoint,
                    ref minimum,
                    ref maximum);
            }

            for (int index = 0; index < sites.Count; index++)
            {
                Encapsulate(
                    sites[index].WorldPosition,
                    ref hasPoint,
                    ref minimum,
                    ref maximum);
            }

            if (!hasPoint)
            {
                minimum = Vector2.zero;
                maximum = Vector2.one;
            }
        }

        private static void Encapsulate(
            Vector2 point,
            ref bool hasPoint,
            ref Vector2 minimum,
            ref Vector2 maximum)
        {
            if (!hasPoint)
            {
                minimum = point;
                maximum = point;
                hasPoint = true;
                return;
            }

            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }

        private static void AddLine(
            VertexHelper helper,
            Vector2 from,
            Vector2 to,
            float width,
            Color color)
        {
            Vector2 direction = to - from;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                AddCenteredSquare(helper, from, width, color);
                return;
            }

            direction.Normalize();
            Vector2 normal =
                new Vector2(-direction.y, direction.x) *
                (width * 0.5f);
            int start = helper.currentVertCount;
            helper.AddVert(from - normal, color, Vector2.zero);
            helper.AddVert(from + normal, color, Vector2.up);
            helper.AddVert(to + normal, color, Vector2.one);
            helper.AddVert(to - normal, color, Vector2.right);
            helper.AddTriangle(start, start + 1, start + 2);
            helper.AddTriangle(start, start + 2, start + 3);
            AddCenteredSquare(helper, from, width, color);
            AddCenteredSquare(helper, to, width, color);
        }

        private static void AddCenteredSquare(
            VertexHelper helper,
            Vector2 center,
            float size,
            Color color)
        {
            float half = size * 0.5f;
            AddQuad(
                helper,
                Rect.MinMaxRect(
                    center.x - half,
                    center.y - half,
                    center.x + half,
                    center.y + half),
                color);
        }

        private static void AddQuad(
            VertexHelper helper,
            Rect rect,
            Color color)
        {
            int start = helper.currentVertCount;
            helper.AddVert(
                new Vector2(rect.xMin, rect.yMin),
                color,
                Vector2.zero);
            helper.AddVert(
                new Vector2(rect.xMin, rect.yMax),
                color,
                Vector2.up);
            helper.AddVert(
                new Vector2(rect.xMax, rect.yMax),
                color,
                Vector2.one);
            helper.AddVert(
                new Vector2(rect.xMax, rect.yMin),
                color,
                Vector2.right);
            helper.AddTriangle(start, start + 1, start + 2);
            helper.AddTriangle(start, start + 2, start + 3);
        }
    }
}
