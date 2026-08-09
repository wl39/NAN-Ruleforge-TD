using RuleforgeTD.Rendering;
using UnityEngine;

namespace RuleforgeTD.Battle
{
    /// <summary>
    /// Reusable four-corner world-space selection frame. Input components
    /// provide bounds and style; this view owns only the presentation objects.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldSelectionCornerView : MonoBehaviour
    {
        private const int CornerCount = 4;

        private static Material sharedMarkerMaterial;
        private static int sharedMarkerMaterialUsers;

        private Transform markerRoot;
        private LineRenderer[] corners;
        private Material markerMaterial;
        private bool hasSharedMarkerMaterial;
        private Color color = Color.white;
        private float lineWidth = 0.075f;
        private float margin = 0.13f;
        private float cornerRatio = 0.28f;
        private float minimumCornerLength = 0.22f;
        private float maximumCornerLength = 0.5f;
        private int sortingOrder = 250;
        private string rootName = "Selection Corners";
        private string cornerNamePrefix = "Corner";
        private bool configured;

        public Transform MarkerRoot => markerRoot;
        public bool IsVisible =>
            markerRoot != null &&
            markerRoot.gameObject.activeSelf;
        public Color Color => color;

        public void Configure(
            Color frameColor,
            string selectionRootName,
            string childNamePrefix,
            float width = 0.075f,
            int order = 250,
            float boundsMargin = 0.13f,
            float lengthRatio = 0.28f,
            float minimumLength = 0.22f,
            float maximumLength = 0.5f)
        {
            color = frameColor;
            rootName = string.IsNullOrWhiteSpace(selectionRootName)
                ? "Selection Corners"
                : selectionRootName;
            cornerNamePrefix =
                string.IsNullOrWhiteSpace(childNamePrefix)
                    ? "Corner"
                    : childNamePrefix;
            lineWidth = Mathf.Max(0.001f, width);
            sortingOrder = order;
            margin = Mathf.Max(0f, boundsMargin);
            cornerRatio = Mathf.Max(0.01f, lengthRatio);
            minimumCornerLength = Mathf.Max(
                0.01f,
                minimumLength);
            maximumCornerLength = Mathf.Max(
                minimumCornerLength,
                maximumLength);
            configured = true;
            EnsureVisuals();
            ApplyStyle();
        }

        public void SetVisible(bool visible)
        {
            if (visible)
            {
                EnsureVisuals();
            }

            if (markerRoot != null)
            {
                markerRoot.gameObject.SetActive(visible);
            }
        }

        public void Refresh(Bounds worldBounds)
        {
            EnsureVisuals();
            if (corners == null ||
                corners.Length != CornerCount)
            {
                return;
            }

            float left = worldBounds.min.x - margin;
            float right = worldBounds.max.x + margin;
            float bottom = worldBounds.min.y - margin;
            float top = worldBounds.max.y + margin;
            float shortestSide = Mathf.Min(
                Mathf.Max(0.01f, worldBounds.size.x),
                Mathf.Max(0.01f, worldBounds.size.y));
            float cornerLength = Mathf.Clamp(
                shortestSide * cornerRatio,
                minimumCornerLength,
                maximumCornerLength);
            float z = worldBounds.center.z - 0.02f;

            SetCorner(
                0,
                new Vector3(left + cornerLength, top, z),
                new Vector3(left, top, z),
                new Vector3(left, top - cornerLength, z));
            SetCorner(
                1,
                new Vector3(right - cornerLength, top, z),
                new Vector3(right, top, z),
                new Vector3(right, top - cornerLength, z));
            SetCorner(
                2,
                new Vector3(left + cornerLength, bottom, z),
                new Vector3(left, bottom, z),
                new Vector3(left, bottom + cornerLength, z));
            SetCorner(
                3,
                new Vector3(right - cornerLength, bottom, z),
                new Vector3(right, bottom, z),
                new Vector3(right, bottom + cornerLength, z));
        }

        private void EnsureVisuals()
        {
            if (!configured)
            {
                configured = true;
            }

            if (markerRoot == null)
            {
                var root = new GameObject(rootName);
                root.transform.SetParent(transform, false);
                markerRoot = root.transform;
            }

            if (corners != null &&
                corners.Length == CornerCount)
            {
                return;
            }

            EnsureMaterial();
            corners = new LineRenderer[CornerCount];
            for (int i = 0; i < CornerCount; i++)
            {
                var corner = new GameObject(
                    cornerNamePrefix + " " + (i + 1));
                corner.transform.SetParent(markerRoot, false);
                LineRenderer line =
                    corner.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 3;
                line.numCapVertices = 0;
                line.numCornerVertices = 0;
                line.alignment = LineAlignment.TransformZ;
                if (markerMaterial != null)
                {
                    line.sharedMaterial = markerMaterial;
                }

                corners[i] = line;
            }

            ApplyStyle();
        }

        private void EnsureMaterial()
        {
            if (hasSharedMarkerMaterial &&
                markerMaterial != null)
            {
                return;
            }

            if (sharedMarkerMaterial == null)
            {
                Shader shader =
                    Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    shader = Shader.Find("UI/Default");
                }

                if (shader != null)
                {
                    sharedMarkerMaterial =
                        new Material(shader)
                        {
                            name =
                                "Ruleforge World Selection Corners",
                            hideFlags =
                                HideFlags.HideAndDontSave
                        };
                }
            }

            markerMaterial = sharedMarkerMaterial;
            if (markerMaterial != null)
            {
                hasSharedMarkerMaterial = true;
                sharedMarkerMaterialUsers++;
            }
        }

        private void ApplyStyle()
        {
            if (corners == null)
            {
                return;
            }

            for (int i = 0; i < corners.Length; i++)
            {
                LineRenderer line = corners[i];
                if (line == null)
                {
                    continue;
                }

                line.startWidth = lineWidth;
                line.endWidth = lineWidth;
                line.startColor = color;
                line.endColor = color;
                WorldSortingLayers.Apply(
                    line,
                    WorldSortingLayers.Effects);
                line.sortingOrder = sortingOrder;
                if (markerMaterial != null)
                {
                    line.sharedMaterial = markerMaterial;
                }
            }
        }

        private void SetCorner(
            int index,
            Vector3 first,
            Vector3 middle,
            Vector3 last)
        {
            LineRenderer line =
                corners != null &&
                index >= 0 &&
                index < corners.Length
                    ? corners[index]
                    : null;
            if (line == null)
            {
                return;
            }

            line.SetPosition(0, first);
            line.SetPosition(1, middle);
            line.SetPosition(2, last);
        }

        private void OnDestroy()
        {
            if (!hasSharedMarkerMaterial)
            {
                return;
            }

            hasSharedMarkerMaterial = false;
            markerMaterial = null;
            sharedMarkerMaterialUsers =
                Mathf.Max(0, sharedMarkerMaterialUsers - 1);
            if (sharedMarkerMaterialUsers != 0 ||
                sharedMarkerMaterial == null)
            {
                return;
            }

            Destroy(sharedMarkerMaterial);
            sharedMarkerMaterial = null;
        }
    }
}
