using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Small reusable pointer bridge for runtime-built uGUI. The owning view
    /// keeps tooltip content and placement in one place while buttons and
    /// cards only report pointer entry/exit.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneHoverRelay :
        MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        private bool hoverEnabled = true;
        private bool pointerInside;

        public event Action<RectTransform> Entered;
        public event Action<RectTransform> Exited;

        public bool HoverEnabled => hoverEnabled;
        public bool PointerInside => pointerInside;

        public void SetHoverEnabled(bool enabled)
        {
            hoverEnabled = enabled;
            if (!hoverEnabled && pointerInside)
            {
                pointerInside = false;
                Exited?.Invoke(transform as RectTransform);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!hoverEnabled)
            {
                return;
            }

            pointerInside = true;
            Entered?.Invoke(transform as RectTransform);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!pointerInside)
            {
                return;
            }

            pointerInside = false;
            Exited?.Invoke(transform as RectTransform);
        }

        private void OnDisable()
        {
            if (!pointerInside)
            {
                return;
            }

            pointerInside = false;
            Exited?.Invoke(transform as RectTransform);
        }
    }
}
