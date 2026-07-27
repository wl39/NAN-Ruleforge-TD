using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Generic uGUI card drop destination. The owner supplies a slot index,
    /// optional validation predicate, and subscribes to DropRequested.
    /// No battle-controller or loadout-view dependency is required.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneCardDropSlot :
        MonoBehaviour,
        IDropHandler,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        [SerializeField]
        private int slotIndex = -1;

        [SerializeField]
        private bool dropEnabled = true;

        [SerializeField]
        private Graphic highlightGraphic;

        [SerializeField]
        private Color validHoverColor =
            new Color32(255, 218, 76, 255);

        private Color restingColor;
        private bool hasRestingColor;
        private Func<int, int, bool> acceptanceFilter;

        public event Action<int, int> DropRequested;

        public int SlotIndex => slotIndex;
        public bool DropEnabled => dropEnabled;
        public Graphic HighlightGraphic => highlightGraphic;

        private void Awake()
        {
            CaptureRestingColor();
        }

        private void OnDisable()
        {
            RestoreRestingColor();
        }

        public void Configure(
            int targetSlotIndex,
            bool acceptsDrops = true)
        {
            slotIndex = targetSlotIndex;
            dropEnabled = acceptsDrops;
            if (!dropEnabled)
            {
                RestoreRestingColor();
            }
        }

        public void SetDropEnabled(bool acceptsDrops)
        {
            dropEnabled = acceptsDrops;
            if (!dropEnabled)
            {
                RestoreRestingColor();
            }
        }

        public void SetAcceptanceFilter(
            Func<int, int, bool> filter)
        {
            acceptanceFilter = filter;
        }

        public void SetHighlight(
            Graphic graphic,
            Color hoverColor)
        {
            RestoreRestingColor();
            highlightGraphic = graphic;
            validHoverColor = hoverColor;
            hasRestingColor = false;
            CaptureRestingColor();
        }

        public void RefreshRestingColor()
        {
            hasRestingColor = false;
            CaptureRestingColor();
        }

        public bool CanAccept(int cardInstanceId)
        {
            return isActiveAndEnabled &&
                dropEnabled &&
                slotIndex >= 0 &&
                cardInstanceId >= 0 &&
                (acceptanceFilter == null ||
                 acceptanceFilter(cardInstanceId, slotIndex));
        }

        public bool TryAccept(
            StageOneCardDragSource dragSource)
        {
            return dragSource != null &&
                TryAccept(dragSource.CardInstanceId);
        }

        public bool TryAccept(int cardInstanceId)
        {
            if (!CanAccept(cardInstanceId))
            {
                return false;
            }

            DropRequested?.Invoke(cardInstanceId, slotIndex);
            return true;
        }

        public void OnDrop(PointerEventData eventData)
        {
            RestoreRestingColor();
            TryAccept(ResolveDragSource(eventData));
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            StageOneCardDragSource source =
                ResolveDragSource(eventData);
            if (source == null ||
                !CanAccept(source.CardInstanceId) ||
                highlightGraphic == null)
            {
                return;
            }

            restingColor = highlightGraphic.color;
            hasRestingColor = true;
            highlightGraphic.color = validHoverColor;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            RestoreRestingColor();
        }

        private static StageOneCardDragSource ResolveDragSource(
            PointerEventData eventData)
        {
            if (eventData == null ||
                eventData.pointerDrag == null)
            {
                return null;
            }

            StageOneCardDragSource source =
                eventData.pointerDrag.GetComponent<
                    StageOneCardDragSource>();
            return source != null
                ? source
                : eventData.pointerDrag.GetComponentInParent<
                    StageOneCardDragSource>();
        }

        private void CaptureRestingColor()
        {
            if (highlightGraphic == null || hasRestingColor)
            {
                return;
            }

            restingColor = highlightGraphic.color;
            hasRestingColor = true;
        }

        private void RestoreRestingColor()
        {
            if (highlightGraphic != null && hasRestingColor)
            {
                highlightGraphic.color = restingColor;
            }
        }
    }
}
