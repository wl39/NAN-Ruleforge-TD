using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Lightweight, asset-free blueprint paper used by the tower workbench.
    /// It keeps the visual usable until the final UI texture is supplied.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneBlueprintGridGraphic : MaskableGraphic
    {
        [SerializeField]
        private Color backgroundColor =
            new Color32(13, 37, 65, 248);

        [SerializeField]
        private Color minorGridColor =
            new Color32(47, 98, 132, 82);

        [SerializeField]
        private Color majorGridColor =
            new Color32(79, 139, 174, 128);

        [SerializeField, Min(8f)]
        private float cellSize = 24f;

        [SerializeField, Range(2, 8)]
        private int majorLineInterval = 4;

        [SerializeField, Range(0.5f, 4f)]
        private float minorLineWidth = 1f;

        [SerializeField, Range(0.5f, 6f)]
        private float majorLineWidth = 2f;

        [SerializeField, Range(0f, 1f)]
        private float revealProgress = 1f;

        private readonly List<Vector2> clipInput =
            new List<Vector2>(6);
        private readonly List<Vector2> clipOutput =
            new List<Vector2>(6);

        public Color BackgroundColor => backgroundColor;
        public float CellSize => cellSize;
        public float RevealProgress => revealProgress;

        public void Configure(
            Color background,
            Color minorGrid,
            Color majorGrid,
            float gridCellSize = 24f)
        {
            backgroundColor = background;
            minorGridColor = minorGrid;
            majorGridColor = majorGrid;
            cellSize = Mathf.Max(8f, gridCellSize);
            SetVerticesDirty();
        }

        /// <summary>
        /// Reveals the blueprint along the x+y diagonal, starting at the
        /// lower-left corner and finishing at the upper-right corner.
        /// </summary>
        public void SetRevealProgress(float progress)
        {
            float next = Mathf.Clamp01(progress);
            if (Mathf.Approximately(revealProgress, next))
            {
                return;
            }

            revealProgress = next;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (revealProgress <= 0f)
            {
                return;
            }

            Rect rect = GetPixelAdjustedRect();
            AddQuad(
                vertexHelper,
                rect,
                backgroundColor,
                rect);

            int verticalCount = Mathf.CeilToInt(
                rect.width / cellSize);
            for (int i = 0; i <= verticalCount; i++)
            {
                bool major = i % majorLineInterval == 0;
                float width = major
                    ? majorLineWidth
                    : minorLineWidth;
                float x = rect.xMin + i * cellSize;
                AddQuad(
                    vertexHelper,
                    new Rect(
                        x - width * 0.5f,
                        rect.yMin,
                        width,
                        rect.height),
                    major ? majorGridColor : minorGridColor,
                    rect);
            }

            int horizontalCount = Mathf.CeilToInt(
                rect.height / cellSize);
            for (int i = 0; i <= horizontalCount; i++)
            {
                bool major = i % majorLineInterval == 0;
                float width = major
                    ? majorLineWidth
                    : minorLineWidth;
                float y = rect.yMin + i * cellSize;
                AddQuad(
                    vertexHelper,
                    new Rect(
                        rect.xMin,
                        y - width * 0.5f,
                        rect.width,
                        width),
                    major ? majorGridColor : minorGridColor,
                    rect);
            }
        }

        private void AddQuad(
            VertexHelper vertexHelper,
            Rect rect,
            Color color,
            Rect revealRect)
        {
            if (revealProgress < 0.9999f)
            {
                AddClippedQuad(
                    vertexHelper,
                    rect,
                    color,
                    revealRect);
                return;
            }

            int start = vertexHelper.currentVertCount;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;

            vertex.position = new Vector3(rect.xMin, rect.yMin);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector3(rect.xMin, rect.yMax);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector3(rect.xMax, rect.yMax);
            vertexHelper.AddVert(vertex);
            vertex.position = new Vector3(rect.xMax, rect.yMin);
            vertexHelper.AddVert(vertex);

            vertexHelper.AddTriangle(start, start + 1, start + 2);
            vertexHelper.AddTriangle(start + 2, start + 3, start);
        }

        private void AddClippedQuad(
            VertexHelper vertexHelper,
            Rect rect,
            Color color,
            Rect revealRect)
        {
            clipInput.Clear();
            clipOutput.Clear();
            clipInput.Add(new Vector2(rect.xMin, rect.yMin));
            clipInput.Add(new Vector2(rect.xMin, rect.yMax));
            clipInput.Add(new Vector2(rect.xMax, rect.yMax));
            clipInput.Add(new Vector2(rect.xMax, rect.yMin));

            float threshold =
                Mathf.Lerp(-0.002f, 2.002f, revealProgress);
            Vector2 previous =
                clipInput[clipInput.Count - 1];
            float previousDistance =
                GetDiagonalDistance(
                    previous,
                    revealRect) -
                threshold;
            bool previousInside = previousDistance <= 0f;
            for (int i = 0; i < clipInput.Count; i++)
            {
                Vector2 current = clipInput[i];
                float currentDistance =
                    GetDiagonalDistance(
                        current,
                        revealRect) -
                    threshold;
                bool currentInside = currentDistance <= 0f;
                if (currentInside != previousInside)
                {
                    float denominator =
                        previousDistance -
                        currentDistance;
                    float interpolation =
                        Mathf.Abs(denominator) < 0.00001f
                            ? 0f
                            : previousDistance /
                              denominator;
                    clipOutput.Add(
                        Vector2.LerpUnclamped(
                            previous,
                            current,
                            interpolation));
                }

                if (currentInside)
                {
                    clipOutput.Add(current);
                }

                previous = current;
                previousDistance = currentDistance;
                previousInside = currentInside;
            }

            if (clipOutput.Count < 3)
            {
                return;
            }

            int start = vertexHelper.currentVertCount;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            for (int i = 0; i < clipOutput.Count; i++)
            {
                vertex.position = clipOutput[i];
                vertexHelper.AddVert(vertex);
            }

            for (int i = 1; i < clipOutput.Count - 1; i++)
            {
                vertexHelper.AddTriangle(
                    start,
                    start + i,
                    start + i + 1);
            }
        }

        private static float GetDiagonalDistance(
            Vector2 point,
            Rect revealRect)
        {
            return
                (point.x - revealRect.xMin) /
                    Mathf.Max(0.001f, revealRect.width) +
                (point.y - revealRect.yMin) /
                    Mathf.Max(0.001f, revealRect.height);
        }
    }
}
