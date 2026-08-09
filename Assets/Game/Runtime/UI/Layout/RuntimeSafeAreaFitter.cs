using UnityEngine;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// 스테이지와 무관하게 런타임 생성 UI를 모바일 안전 영역 안에 맞춘다.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class RuntimeSafeAreaFitter : MonoBehaviour
    {
        private RectTransform target;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;

        protected virtual void OnEnable()
        {
            ApplySafeArea();
        }

        protected virtual void Update()
        {
            var screenSize =
                new Vector2Int(Screen.width, Screen.height);
            if (Screen.safeArea != lastSafeArea ||
                screenSize != lastScreenSize)
            {
                ApplySafeArea();
            }
        }

        public void ApplySafeArea()
        {
            if (target == null)
            {
                target = GetComponent<RectTransform>();
            }

            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            Rect safeArea = Screen.safeArea;
            Vector2 minimum = new Vector2(
                Mathf.Clamp01(safeArea.xMin / width),
                Mathf.Clamp01(safeArea.yMin / height));
            Vector2 maximum = new Vector2(
                Mathf.Clamp01(safeArea.xMax / width),
                Mathf.Clamp01(safeArea.yMax / height));

            target.anchorMin = minimum;
            target.anchorMax = maximum;
            target.offsetMin = Vector2.zero;
            target.offsetMax = Vector2.zero;
            lastSafeArea = safeArea;
            lastScreenSize = new Vector2Int(width, height);
        }
    }
}
