using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Generic uGUI drag source carrying one owned card instance ID.
    /// It works with mouse and touch PointerEventData and creates a
    /// raycast-transparent visual copy on the root canvas while dragging.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class StageOneCardDragSource :
        MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler
    {
        [SerializeField]
        private int cardInstanceId = -1;

        [SerializeField]
        private bool draggable = true;

        [SerializeField, Range(0.1f, 1f)]
        private float ghostAlpha = 0.88f;

        [SerializeField]
        private Canvas dragCanvas;

        private RectTransform sourceRect;
        private RectTransform dragGhost;
        private bool dragging;
        private Vector2 pointerOffset;
        private float dragPlaneLocalZ;

        public event Action<int> DragStarted;
        public event Action<int> DragEnded;

        public int CardInstanceId => cardInstanceId;
        public bool Draggable => draggable;
        public bool IsDragging => dragging;
        public bool IsForwardingScrollDrag => false;
        public RectTransform DragGhost => dragGhost;
        public Canvas DragCanvas => ResolveDragCanvas();

        private void Awake()
        {
            sourceRect = GetComponent<RectTransform>();
        }

        private void OnDisable()
        {
            CancelDrag();
        }

        public void Configure(
            int instanceId,
            Canvas rootCanvas = null,
            bool canDrag = true)
        {
            cardInstanceId = instanceId;
            dragCanvas = rootCanvas;
            draggable = canDrag;
            if (!draggable)
            {
                CancelDrag();
            }
        }

        public void SetCardInstanceId(int instanceId)
        {
            cardInstanceId = instanceId;
            if (cardInstanceId < 0)
            {
                CancelDrag();
            }
        }

        public void SetDragCanvas(Canvas rootCanvas)
        {
            dragCanvas = rootCanvas;
        }

        public void SetDraggable(bool canDrag)
        {
            draggable = canDrag;
            if (!draggable)
            {
                CancelDrag();
            }
        }

        public void SetGhostAlpha(float alpha)
        {
            ghostAlpha = Mathf.Clamp(alpha, 0.1f, 1f);
            if (dragGhost != null)
            {
                CanvasGroup group =
                    dragGhost.GetComponent<CanvasGroup>();
                if (group != null)
                {
                    group.alpha = ghostAlpha;
                }
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!CanStartDrag(eventData))
            {
                return;
            }

            CancelDrag();
            // 카드 위에서 시작한 드래그는 처음 프레임부터 카드가 소유한다.
            // 예전에는 ScrollRect가 먼저 동작해 뷰포트를 벗어난 뒤에야
            // 고스트가 생겼고, 그 시점의 큰 포인터 오프셋 때문에 카드가
            // 마우스를 뒤늦게 엉뚱한 위치에서 따라오는 것처럼 보였다.
            BeginCardDrag(eventData);
        }

        private void BeginCardDrag(PointerEventData eventData)
        {
            Canvas canvas = ResolveDragCanvas();
            if (canvas == null)
            {
                return;
            }

            CreateDragGhost(canvas);
            if (dragGhost == null)
            {
                return;
            }

            if (!TryGetCanvasLocalPointer(
                    canvas,
                    eventData,
                    out Vector2 pointerLocal))
            {
                CancelDrag();
                return;
            }

            RectTransform canvasRect =
                (RectTransform)canvas.transform;
            Vector3 sourceLocal =
                canvasRect.InverseTransformPoint(
                    sourceRect.position);
            pointerOffset =
                (Vector2)sourceLocal - pointerLocal;
            dragPlaneLocalZ = sourceLocal.z;
            dragging = true;
            SetGhostPosition(pointerLocal);
            DragStarted?.Invoke(cardInstanceId);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (dragging)
            {
                UpdateGhostPosition(eventData);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!dragging)
            {
                return;
            }

            int endedCardInstanceId = cardInstanceId;
            CancelDrag();
            DragEnded?.Invoke(endedCardInstanceId);
        }

        public void CancelDrag()
        {
            dragging = false;
            pointerOffset = Vector2.zero;
            dragPlaneLocalZ = 0f;
            if (dragGhost == null)
            {
                return;
            }

            GameObject ghostObject = dragGhost.gameObject;
            dragGhost = null;
            if (Application.isPlaying)
            {
                Destroy(ghostObject);
            }
            else
            {
                DestroyImmediate(ghostObject);
            }
        }

        private bool CanStartDrag(PointerEventData eventData)
        {
            if (!isActiveAndEnabled ||
                !draggable ||
                cardInstanceId < 0 ||
                eventData == null ||
                eventData.button != PointerEventData.InputButton.Left)
            {
                return false;
            }

            Selectable selectable = GetComponent<Selectable>();
            return selectable == null || selectable.IsInteractable();
        }

        private Canvas ResolveDragCanvas()
        {
            Canvas canvas = dragCanvas != null
                ? dragCanvas
                : GetComponentInParent<Canvas>();
            return canvas == null ? null : canvas.rootCanvas;
        }

        private void CreateDragGhost(Canvas canvas)
        {
            GameObject ghostObject = Instantiate(gameObject);
            ghostObject.name = gameObject.name + " Drag Ghost";
            ghostObject.transform.SetParent(canvas.transform, false);
            ghostObject.transform.SetAsLastSibling();

            StageOneCardDragSource[] clonedDragSources =
                ghostObject.GetComponentsInChildren<
                    StageOneCardDragSource>(true);
            for (int i = 0; i < clonedDragSources.Length; i++)
            {
                clonedDragSources[i].enabled = false;
            }

            Selectable[] clonedSelectables =
                ghostObject.GetComponentsInChildren<Selectable>(true);
            for (int i = 0; i < clonedSelectables.Length; i++)
            {
                clonedSelectables[i].interactable = false;
            }

            CanvasGroup group =
                ghostObject.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = ghostObject.AddComponent<CanvasGroup>();
            }

            group.alpha = ghostAlpha;
            group.interactable = false;
            group.blocksRaycasts = false;
            group.ignoreParentGroups = true;

            dragGhost =
                ghostObject.GetComponent<RectTransform>();
            if (sourceRect == null)
            {
                sourceRect = GetComponent<RectTransform>();
            }

            dragGhost.anchorMin = Vector2.zero;
            dragGhost.anchorMax = Vector2.zero;
            dragGhost.pivot = sourceRect.pivot;
            dragGhost.sizeDelta = sourceRect.rect.size;
            dragGhost.localScale = Vector3.one;
            dragGhost.localRotation = Quaternion.identity;
        }

        private void UpdateGhostPosition(
            PointerEventData eventData)
        {
            if (dragGhost == null || eventData == null)
            {
                return;
            }

            Canvas canvas = ResolveDragCanvas();
            if (canvas == null ||
                !TryGetCanvasLocalPointer(
                    canvas,
                    eventData,
                    out Vector2 pointerLocal))
            {
                return;
            }

            SetGhostPosition(pointerLocal);
        }

        private bool TryGetCanvasLocalPointer(
            Canvas canvas,
            PointerEventData eventData,
            out Vector2 pointerLocal)
        {
            pointerLocal = default;
            if (canvas == null ||
                eventData == null ||
                !(canvas.transform is RectTransform canvasRect))
            {
                return false;
            }

            Camera eventCamera =
                canvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null
                    : eventData.pressEventCamera != null
                        ? eventData.pressEventCamera
                        : canvas.worldCamera;
            return RectTransformUtility
                .ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    eventData.position,
                    eventCamera,
                    out pointerLocal);
        }

        private void SetGhostPosition(Vector2 pointerLocal)
        {
            if (dragGhost == null)
            {
                return;
            }

            Vector2 ghostLocal = pointerLocal + pointerOffset;
            dragGhost.localPosition = new Vector3(
                ghostLocal.x,
                ghostLocal.y,
                dragPlaneLocalZ);
        }
    }
}
