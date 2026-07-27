using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Small pointer relay kept separate from the slot Button so a regular
    /// click can continue selecting the slot while a double click requests
    /// unequip.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneSlotDoubleClickRelay :
        MonoBehaviour,
        IPointerClickHandler
    {
        private const float DoubleClickWindowSeconds = 0.65f;
        private float lastClickTime = float.NegativeInfinity;

        public event Action<int> DoubleClicked;

        public int SlotIndex { get; private set; } = -1;

        public void Configure(int slotIndex)
        {
            SlotIndex = slotIndex;
            lastClickTime = float.NegativeInfinity;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!isActiveAndEnabled ||
                SlotIndex < 0 ||
                eventData == null ||
                eventData.button !=
                    PointerEventData.InputButton.Left)
            {
                return;
            }

            float now = Time.unscaledTime;
            bool isDoubleClick =
                eventData.clickCount >= 2 ||
                now - lastClickTime <=
                    DoubleClickWindowSeconds;
            if (!isDoubleClick)
            {
                lastClickTime = now;
                return;
            }

            lastClickTime = float.NegativeInfinity;
            DoubleClicked?.Invoke(SlotIndex);
        }

        public void RequestDoubleClick()
        {
            if (isActiveAndEnabled && SlotIndex >= 0)
            {
                DoubleClicked?.Invoke(SlotIndex);
            }
        }
    }
}
