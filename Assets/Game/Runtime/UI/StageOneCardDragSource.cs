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
        private ScrollRect parentScrollRect;
        private bool dragging;
        private bool forwardingScrollDrag;
        private Vector2 lastScrollPointerPosition;
        private bool hasScrollPointerPosition;
        private Vector2 pointerOffset;
        private float dragPlaneLocalZ;

        public event Action<int> DragStarted;
        public event Action<int> DragEnded;

        public int CardInstanceId => cardInstanceId;
        public bool Draggable => draggable;
        public bool IsDragging => dragging;
        public bool IsForwardingScrollDrag =>
            forwardingScrollDrag;
        public RectTransform DragGhost => dragGhost;
        public Canvas DragCanvas => ResolveDragCanvas();

        private void Awake()
        {
            sourceRect = GetComponent<RectTransform>();
            parentScrollRect = GetComponentInParent<ScrollRect>();
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
            parentScrollRect = GetComponentInParent<ScrollRect>();
            if (CanForwardToParentScrollRect(eventData))
            {
                forwardingScrollDrag = true;
                lastScrollPointerPosition =
                    eventData.position;
                hasScrollPointerPosition = true;
                parentScrollRect.StopMovement();
                return;
            }

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
            if (forwardingScrollDrag)
            {
                if (IsPointerInsideScrollViewport(eventData))
                {
                    ScrollParentByPointer(eventData);
                    return;
                }

                forwardingScrollDrag = false;
                hasScrollPointerPosition = false;
                BeginCardDrag(eventData);
            }

            if (dragging)
            {
                UpdateGhostPosition(eventData);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (forwardingScrollDrag)
            {
                forwardingScrollDrag = false;
                hasScrollPointerPosition = false;
                return;
            }

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
            forwardingScrollDrag = false;
            lastScrollPointerPosition = Vector2.zero;
            hasScrollPointerPosition = false;
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

        private bool CanForwardToParentScrollRect(
            PointerEventData eventData)
        {
            return parentScrollRect != null &&
                parentScrollRect.isActiveAndEnabled &&
                parentScrollRect.vertical &&
                parentScrollRect.content != null &&
                parentScrollRect.viewport != null &&
                IsPointerInsideScrollViewport(eventData);
        }

        private bool IsPointerInsideScrollViewport(
            PointerEventData eventData)
        {
            if (parentScrollRect == null ||
                parentScrollRect.viewport == null ||
                eventData == null)
            {
                return false;
            }

            Canvas canvas = ResolveDragCanvas();
            Camera eventCamera =
                canvas == null ||
                canvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null
                    : eventData.pressEventCamera != null
                        ? eventData.pressEventCamera
                        : canvas.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint(
                parentScrollRect.viewport,
                eventData.position,
                eventCamera);
        }

        /// <summary>
        /// 카드가 포인터 드래그를 소유한 상태에서도 보유 카드 목록을
        /// 확실하게 움직인다. ScrollRect.OnDrag를 외부에서 대신 호출하면
        /// EventSystem의 내부 드래그 대상과 ScrollRect의 private 시작 상태가
        /// 어긋날 수 있으므로, 실제 포인터 이동량을 스크롤 가능한 높이에
        /// 대한 정규화 값으로 변환해 직접 적용한다.
        /// </summary>
        private void ScrollParentByPointer(
            PointerEventData eventData)
        {
            if (parentScrollRect == null ||
                parentScrollRect.content == null ||
                parentScrollRect.viewport == null ||
                eventData == null)
            {
                return;
            }

            if (!hasScrollPointerPosition)
            {
                lastScrollPointerPosition =
                    eventData.position;
                hasScrollPointerPosition = true;
                return;
            }

            Vector2 pointerDelta =
                eventData.position -
                lastScrollPointerPosition;
            lastScrollPointerPosition =
                eventData.position;
            float scrollableHeight = Mathf.Max(
                0f,
                parentScrollRect.content.rect.height -
                parentScrollRect.viewport.rect.height);
            if (scrollableHeight <= 0.01f ||
                Mathf.Abs(pointerDelta.y) <= 0.001f)
            {
                return;
            }

            Canvas canvas = ResolveDragCanvas();
            float scaleFactor = canvas != null
                ? Mathf.Max(0.01f, canvas.scaleFactor)
                : 1f;
            float normalizedDelta =
                pointerDelta.y /
                scaleFactor /
                scrollableHeight;
            parentScrollRect.verticalNormalizedPosition =
                Mathf.Clamp01(
                    parentScrollRect.verticalNormalizedPosition -
                    normalizedDelta);
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
