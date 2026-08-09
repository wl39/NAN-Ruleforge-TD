using System;
using UnityEngine;

namespace RuleforgeTD.Battle
{
    /// <summary>
    /// Pool-safe input and selection presentation for one enemy view. It
    /// raises intent only; simulation state and inspection content remain
    /// owned by the battle presentation coordinator.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(StageOneEnemyView))]
    public sealed class EnemySelectionView :
        MonoBehaviour,
        IStageOneCameraFocusTarget
    {
        private static readonly Color SelectionColor =
            new Color32(239, 61, 54, 255);

        [SerializeField]
        private StageOneEnemyView enemyView;

        [SerializeField]
        private SpriteRenderer targetRenderer;

        private Collider2D hitArea;
        private WorldSelectionCornerView selectionCorners;
        private int entityId = -1;
        private bool selected;

        public event Action<EnemySelectionView> Clicked;

        public int EntityId => entityId;
        public bool IsSelected => selected;
        public Transform SelectionMarkerRoot =>
            selectionCorners == null
                ? null
                : selectionCorners.MarkerRoot;
        public Bounds WorldBounds
        {
            get
            {
                CacheComponents();
                if (targetRenderer != null &&
                    targetRenderer.sprite != null)
                {
                    return targetRenderer.bounds;
                }

                return hitArea != null
                    ? hitArea.bounds
                    : new Bounds(
                        transform.position,
                        Vector3.one);
            }
        }
        public bool IsCameraFocusValid =>
            entityId >= 0 &&
            enemyView != null &&
            gameObject.activeInHierarchy;
        public Vector3 CameraFocusPosition =>
            enemyView == null
                ? transform.position
                : enemyView.LogicalPosition;

        private void Awake()
        {
            CacheComponents();
        }

        private void LateUpdate()
        {
            if (selected && selectionCorners != null)
            {
                selectionCorners.Refresh(WorldBounds);
            }
        }

        public void Configure(int id)
        {
            CacheComponents();
            entityId = id;
            selected = false;
            EnsureClickTargets();
            if (selectionCorners != null)
            {
                selectionCorners.SetVisible(false);
            }
        }

        public void ResetForPool()
        {
            entityId = -1;
            selected = false;
            if (selectionCorners != null)
            {
                selectionCorners.SetVisible(false);
            }
        }

        public void SetSelected(bool value)
        {
            selected = value && entityId >= 0;
            if (selected)
            {
                EnsureSelectionCorners();
                selectionCorners.Refresh(WorldBounds);
                selectionCorners.SetVisible(true);
            }
            else if (selectionCorners != null)
            {
                selectionCorners.SetVisible(false);
            }
        }

        public bool RequestSelection()
        {
            if (!Application.isPlaying ||
                entityId < 0 ||
                !gameObject.activeInHierarchy)
            {
                return false;
            }

            Clicked?.Invoke(this);
            return true;
        }

        private void CacheComponents()
        {
            if (enemyView == null)
            {
                enemyView = GetComponent<StageOneEnemyView>();
            }

            if (targetRenderer == null)
            {
                targetRenderer =
                    GetComponentInChildren<SpriteRenderer>(true);
            }

            if (hitArea == null)
            {
                hitArea =
                    GetComponentInChildren<Collider2D>(true);
            }
        }

        private void EnsureClickTargets()
        {
            Collider2D[] colliders =
                GetComponentsInChildren<Collider2D>(true);
            if (colliders.Length == 0)
            {
                GameObject colliderHost =
                    targetRenderer == null
                        ? gameObject
                        : targetRenderer.gameObject;
                BoxCollider2D generated =
                    colliderHost.AddComponent<BoxCollider2D>();
                generated.isTrigger = true;
                if (targetRenderer != null &&
                    targetRenderer.sprite != null)
                {
                    Bounds spriteBounds =
                        targetRenderer.sprite.bounds;
                    generated.offset =
                        spriteBounds.center;
                    generated.size =
                        spriteBounds.size;
                }

                hitArea = generated;
                colliders = new Collider2D[] { generated };
            }
            else
            {
                hitArea = colliders[0];
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null)
                {
                    continue;
                }

                EnemySelectionClickProxy proxy =
                    collider.GetComponent<
                        EnemySelectionClickProxy>();
                if (proxy == null)
                {
                    proxy =
                        collider.gameObject.AddComponent<
                            EnemySelectionClickProxy>();
                }

                proxy.Configure(this);
            }
        }

        private void EnsureSelectionCorners()
        {
            if (selectionCorners != null)
            {
                return;
            }

            selectionCorners =
                GetComponent<WorldSelectionCornerView>();
            if (selectionCorners == null)
            {
                selectionCorners =
                    gameObject.AddComponent<
                        WorldSelectionCornerView>();
            }

            selectionCorners.Configure(
                SelectionColor,
                "Selected Enemy Corners",
                "Red Corner",
                width: 0.07f,
                order: 255,
                boundsMargin: 0.1f,
                lengthRatio: 0.3f,
                minimumLength: 0.16f,
                maximumLength: 0.48f);
        }
    }
}
