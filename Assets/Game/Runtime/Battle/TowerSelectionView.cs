using System;
using UnityEngine;

namespace RuleforgeTD.Battle
{
    /// <summary>
    /// Adds a presentation-only click target to generated tower prefabs.
    /// Selection never mutates combat state directly; the battle controller
    /// translates the intent into deterministic game commands.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TowerSelectionView : MonoBehaviour
    {
        private const float DoubleClickInterval = 0.55f;
        private const int AttackRangeDashCount = 48;
        private const int AttackRangeDashSegmentCount = 3;
        private const float AttackRangeDashFill = 0.68f;
        private const float AttackRangeLineWidth = 0.02f;
        private const float AttackRangeRotationSpeedDegrees = 16f;

        private static readonly Color SelectionColor =
            new Color32(255, 218, 44, 255);
        private static readonly Color AttackRangeColor =
            new Color32(239, 61, 54, 196);

        private BoxCollider2D hitArea;
        private Transform selectionMarkerRoot;
        private Material selectionMarkerMaterial;
        private Transform attackRangeRoot;
        private LineRenderer[] attackRangeDashes;
        private Material attackRangeMaterial;
        private float attackRangeWorld;
        private float attackRangeRotationRadians;
        private float lastPointerClickTime =
            float.NegativeInfinity;
        private bool selected;
        private bool contextVisible;

        public event Action<TowerSelectionView> Clicked;
        public event Action<TowerSelectionView> DoubleClicked;

        public int TowerId { get; private set; } = -1;
        public bool IsSelected => selected;
        public bool IsContextVisible => contextVisible;
        public float AttackRangeWorld => attackRangeWorld;
        public Transform SelectionMarkerRoot =>
            selectionMarkerRoot;
        public Transform AttackRangeRoot => attackRangeRoot;
        public Bounds WorldHitBounds
        {
            get
            {
                EnsureHitArea();
                return hitArea.bounds;
            }
        }

        public void Configure(int towerId)
        {
            Configure(towerId, 0f);
        }

        public void Configure(
            int towerId,
            float rangeWorld)
        {
            TowerId = towerId;
            attackRangeWorld = Mathf.Max(0f, rangeWorld);
            EnsureHitArea();
            RefreshHitArea();
            if (attackRangeRoot != null)
            {
                RefreshAttackRange();
            }
        }

        public void SetSelected(bool value)
        {
            selected = value;
            if (value)
            {
                EnsureSelectionMarker();
                RefreshSelectionMarker();
            }

            if (selectionMarkerRoot != null)
            {
                selectionMarkerRoot.gameObject.SetActive(value);
            }

            if (!value)
            {
                SetContextVisible(false);
            }
        }

        /// <summary>
        /// Separates ordinary tower selection from the modal blueprint state.
        /// The red combat radius belongs to the compact action context only,
        /// while the yellow corner marker continues to identify the tower.
        /// </summary>
        public void SetContextVisible(bool value)
        {
            contextVisible = value && selected;
            if (!contextVisible)
            {
                // A tap sequence must not survive a modal blueprint
                // round-trip. Otherwise the first tower tap after closing
                // the blueprint can be mistaken for the second half of the
                // tap that originally opened its compact action panel.
                ResetPointerClickSequence();
            }

            if (contextVisible)
            {
                EnsureAttackRange();
                RefreshAttackRange();
            }

            if (attackRangeRoot != null)
            {
                attackRangeRoot.gameObject.SetActive(
                    contextVisible &&
                    attackRangeWorld > 0.001f);
            }
        }

        public void ResetPointerClickSequence()
        {
            lastPointerClickTime = float.NegativeInfinity;
        }

        public bool RequestSelection()
        {
            if (!Application.isPlaying || TowerId < 0)
            {
                return false;
            }

            Clicked?.Invoke(this);
            return true;
        }

        public bool RequestBlueprint()
        {
            if (!Application.isPlaying || TowerId < 0)
            {
                return false;
            }

            DoubleClicked?.Invoke(this);
            return true;
        }

        /// <summary>
        /// Processes a mouse click or touch tap. The first tap selects the
        /// tower immediately; a second tap within the platform-independent
        /// unscaled interval opens its blueprint.
        /// </summary>
        public bool RequestPointerClick()
        {
            if (!Application.isPlaying || TowerId < 0)
            {
                return false;
            }

            float now = Time.unscaledTime;
            bool isDoubleClick =
                now - lastPointerClickTime <=
                DoubleClickInterval;
            lastPointerClickTime = isDoubleClick
                ? float.NegativeInfinity
                : now;
            return isDoubleClick
                ? RequestBlueprint()
                : RequestSelection();
        }

        private void OnMouseUpAsButton()
        {
            if (StageOneCameraController.ShouldSuppressWorldClick)
            {
                return;
            }

            RequestPointerClick();
        }

        private void Update()
        {
            if (!contextVisible ||
                !selected ||
                attackRangeRoot == null ||
                !attackRangeRoot.gameObject.activeSelf ||
                attackRangeWorld <= 0.001f)
            {
                return;
            }

            attackRangeRotationRadians -=
                AttackRangeRotationSpeedDegrees *
                Mathf.Deg2Rad *
                Time.unscaledDeltaTime;
            if (attackRangeRotationRadians <= -Mathf.PI * 2f)
            {
                attackRangeRotationRadians += Mathf.PI * 2f;
            }

            RefreshAttackRange();
        }

        private void EnsureHitArea()
        {
            hitArea = GetComponent<BoxCollider2D>();
            if (hitArea == null)
            {
                hitArea = gameObject.AddComponent<BoxCollider2D>();
            }

            hitArea.isTrigger = true;
        }

        private void RefreshHitArea()
        {
            SpriteRenderer[] renderers =
                GetComponentsInChildren<SpriteRenderer>(true);
            if (renderers.Length == 0)
            {
                hitArea.offset = new Vector2(0f, 0.7f);
                hitArea.size = new Vector2(1.2f, 1.8f);
                return;
            }

            Bounds worldBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                worldBounds.Encapsulate(renderers[i].bounds);
            }

            Vector3 localMin =
                transform.InverseTransformPoint(worldBounds.min);
            Vector3 localMax =
                transform.InverseTransformPoint(worldBounds.max);
            Vector2 size = new Vector2(
                Mathf.Max(0.8f, localMax.x - localMin.x),
                Mathf.Max(1.2f, localMax.y - localMin.y));
            hitArea.offset = new Vector2(
                (localMin.x + localMax.x) * 0.5f,
                (localMin.y + localMax.y) * 0.5f);
            hitArea.size = size;
        }

        private void EnsureSelectionMarker()
        {
            if (selectionMarkerRoot != null)
            {
                return;
            }

            var root = new GameObject("Selected Tower Corners");
            root.transform.SetParent(transform, false);
            selectionMarkerRoot = root.transform;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("UI/Default");
            }

            if (shader != null)
            {
                selectionMarkerMaterial =
                    new Material(shader)
                    {
                        name = "Stage One Tower Selection"
                    };
            }

            for (int i = 0; i < 4; i++)
            {
                var corner = new GameObject(
                    "Yellow Corner " + (i + 1));
                corner.transform.SetParent(
                    selectionMarkerRoot,
                    false);
                LineRenderer line =
                    corner.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.positionCount = 3;
                line.startWidth = 0.075f;
                line.endWidth = 0.075f;
                line.startColor = SelectionColor;
                line.endColor = SelectionColor;
                line.numCapVertices = 0;
                line.numCornerVertices = 0;
                line.alignment =
                    LineAlignment.TransformZ;
                line.sortingOrder = 250;
                if (selectionMarkerMaterial != null)
                {
                    line.sharedMaterial =
                        selectionMarkerMaterial;
                }
            }
        }

        private void RefreshSelectionMarker()
        {
            if (selectionMarkerRoot == null)
            {
                return;
            }

            EnsureHitArea();
            Vector2 halfSize = hitArea.size * 0.5f;
            const float margin = 0.13f;
            float left =
                hitArea.offset.x - halfSize.x - margin;
            float right =
                hitArea.offset.x + halfSize.x + margin;
            float bottom =
                hitArea.offset.y - halfSize.y - margin;
            float top =
                hitArea.offset.y + halfSize.y + margin;
            float cornerLength = Mathf.Clamp(
                Mathf.Min(hitArea.size.x, hitArea.size.y) *
                0.28f,
                0.22f,
                0.5f);

            SetCorner(
                0,
                new Vector3(left + cornerLength, top, 0f),
                new Vector3(left, top, 0f),
                new Vector3(left, top - cornerLength, 0f));
            SetCorner(
                1,
                new Vector3(right - cornerLength, top, 0f),
                new Vector3(right, top, 0f),
                new Vector3(right, top - cornerLength, 0f));
            SetCorner(
                2,
                new Vector3(left + cornerLength, bottom, 0f),
                new Vector3(left, bottom, 0f),
                new Vector3(left, bottom + cornerLength, 0f));
            SetCorner(
                3,
                new Vector3(right - cornerLength, bottom, 0f),
                new Vector3(right, bottom, 0f),
                new Vector3(right, bottom + cornerLength, 0f));
        }

        private void EnsureAttackRange()
        {
            if (attackRangeRoot != null)
            {
                return;
            }

            var root = new GameObject("Tower Attack Range");
            root.transform.SetParent(transform, false);
            attackRangeRoot = root.transform;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("UI/Default");
            }

            if (shader != null)
            {
                attackRangeMaterial =
                    new Material(shader)
                    {
                        name = "Stage One Tower Attack Range"
                    };
            }

            attackRangeDashes =
                new LineRenderer[AttackRangeDashCount];
            for (int i = 0; i < AttackRangeDashCount; i++)
            {
                var dashObject = new GameObject(
                    "Range Dash " + (i + 1));
                dashObject.transform.SetParent(
                    attackRangeRoot,
                    false);
                LineRenderer dash =
                    dashObject.AddComponent<LineRenderer>();
                dash.useWorldSpace = true;
                dash.loop = false;
                dash.positionCount =
                    AttackRangeDashSegmentCount + 1;
                dash.startWidth = AttackRangeLineWidth;
                dash.endWidth = AttackRangeLineWidth;
                dash.startColor = AttackRangeColor;
                dash.endColor = AttackRangeColor;
                dash.numCapVertices = 0;
                dash.numCornerVertices = 0;
                dash.alignment =
                    LineAlignment.TransformZ;
                dash.sortingOrder = 230;
                if (attackRangeMaterial != null)
                {
                    dash.sharedMaterial =
                        attackRangeMaterial;
                }

                attackRangeDashes[i] = dash;
            }
        }

        private void RefreshAttackRange()
        {
            if (attackRangeDashes == null)
            {
                return;
            }

            Vector3 center = transform.position;
            center.z = 0f;
            float dashStride =
                Mathf.PI * 2f / AttackRangeDashCount;
            float dashArc = dashStride * AttackRangeDashFill;
            for (int dashIndex = 0;
                 dashIndex < attackRangeDashes.Length;
                 dashIndex++)
            {
                LineRenderer dash =
                    attackRangeDashes[dashIndex];
                if (dash == null)
                {
                    continue;
                }

                float dashStart =
                    attackRangeRotationRadians +
                    dashStride * dashIndex;
                for (int pointIndex = 0;
                     pointIndex <= AttackRangeDashSegmentCount;
                     pointIndex++)
                {
                    float radians =
                        dashStart +
                        dashArc *
                        pointIndex /
                        AttackRangeDashSegmentCount;
                    dash.SetPosition(
                        pointIndex,
                        center +
                        new Vector3(
                            Mathf.Cos(radians) *
                            attackRangeWorld,
                            Mathf.Sin(radians) *
                            attackRangeWorld,
                            0f));
                }
            }
        }

        private void SetCorner(
            int index,
            Vector3 first,
            Vector3 middle,
            Vector3 last)
        {
            if (selectionMarkerRoot == null ||
                index < 0 ||
                index >= selectionMarkerRoot.childCount)
            {
                return;
            }

            LineRenderer line =
                selectionMarkerRoot.GetChild(index)
                    .GetComponent<LineRenderer>();
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
            if (selectionMarkerMaterial != null)
            {
                Destroy(selectionMarkerMaterial);
            }

            if (attackRangeMaterial != null)
            {
                Destroy(attackRangeMaterial);
            }
        }
    }
}
