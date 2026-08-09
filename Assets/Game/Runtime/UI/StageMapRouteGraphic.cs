using System;
using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Draws the campaign's dashed route in one UI mesh, avoiding hundreds of
    /// decorative GameObjects on WebGL.
    /// </summary>
    public sealed class StageMapRouteGraphic : MaskableGraphic
    {
        private static readonly Color ReachedColor =
            new Color32(246, 201, 93, 220);
        private static readonly Color LockedColor =
            new Color32(45, 37, 31, 185);

        private Vector2[] normalizedPoints = Array.Empty<Vector2>();
        private int highestUnlockedStage = 1;

        public void Configure(
            Vector2[] points,
            int unlockedStage)
        {
            normalizedPoints = points == null
                ? Array.Empty<Vector2>()
                : (Vector2[])points.Clone();
            highestUnlockedStage = Mathf.Max(1, unlockedStage);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (normalizedPoints.Length < 2)
            {
                return;
            }

            Rect area = GetPixelAdjustedRect();
            for (int segment = 0;
                 segment < normalizedPoints.Length - 1;
                 segment++)
            {
                Vector2 start = ToLocal(area, normalizedPoints[segment]);
                Vector2 end = ToLocal(area, normalizedPoints[segment + 1]);
                float distance = Vector2.Distance(start, end);
                int dashCount = Mathf.Max(3, Mathf.RoundToInt(distance / 22f));
                Color32 dashColor = segment < highestUnlockedStage - 1
                    ? ReachedColor
                    : LockedColor;
                for (int dash = 1; dash < dashCount; dash += 2)
                {
                    float fromT = dash / (float)dashCount;
                    float toT = Mathf.Min(
                        1f,
                        (dash + 0.82f) / dashCount);
                    AddDash(
                        vh,
                        Vector2.Lerp(start, end, fromT),
                        Vector2.Lerp(start, end, toT),
                        5f,
                        dashColor);
                }
            }
        }

        private static Vector2 ToLocal(Rect area, Vector2 normalized)
        {
            return new Vector2(
                area.xMin + normalized.x * area.width,
                area.yMin + normalized.y * area.height);
        }

        private static void AddDash(
            VertexHelper vh,
            Vector2 start,
            Vector2 end,
            float width,
            Color32 color)
        {
            Vector2 direction = (end - start).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) *
                             (width * 0.5f);
            int baseIndex = vh.currentVertCount;
            vh.AddVert(start - normal, color, Vector2.zero);
            vh.AddVert(start + normal, color, Vector2.up);
            vh.AddVert(end + normal, color, Vector2.one);
            vh.AddVert(end - normal, color, Vector2.right);
            vh.AddTriangle(baseIndex, baseIndex + 1, baseIndex + 2);
            vh.AddTriangle(baseIndex, baseIndex + 2, baseIndex + 3);
        }
    }
}
