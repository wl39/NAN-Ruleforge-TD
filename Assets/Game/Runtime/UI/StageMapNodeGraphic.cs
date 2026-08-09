using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    public enum StageMapNodeState
    {
        Locked = 0,
        Unlocked = 1,
        Cleared = 2,
        ComingSoon = 3
    }

    /// <summary>
    /// Lightweight marker used by every stage button.
    /// </summary>
    public sealed class StageMapNodeGraphic : MaskableGraphic
    {
        private StageMapNodeState state;
        private bool selected;

        public void SetState(StageMapNodeState value, bool isSelected)
        {
            state = value;
            selected = isSelected;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = GetPixelAdjustedRect();
            Vector2 center = rect.center;
            float radius = Mathf.Min(rect.width, rect.height) * 0.47f;

            Color32 shadow = new Color32(21, 18, 18, 210);
            Color32 border;
            Color32 fill;
            switch (state)
            {
                case StageMapNodeState.Cleared:
                    border = new Color32(255, 215, 92, 255);
                    fill = new Color32(50, 98, 76, 255);
                    break;
                case StageMapNodeState.Unlocked:
                    border = new Color32(117, 230, 229, 255);
                    fill = new Color32(38, 79, 85, 255);
                    break;
                case StageMapNodeState.ComingSoon:
                    border = new Color32(102, 91, 96, 230);
                    fill = new Color32(42, 38, 45, 245);
                    break;
                default:
                    border = new Color32(117, 105, 101, 240);
                    fill = new Color32(48, 45, 45, 245);
                    break;
            }

            AddDisc(vh, center + new Vector2(2f, -5f), radius, shadow, 10);
            if (selected)
            {
                AddDisc(
                    vh,
                    center,
                    radius + 7f,
                    new Color32(255, 227, 128, 150),
                    10);
            }

            AddDisc(vh, center, radius, border, 10);
            AddDisc(vh, center, radius - 6f, fill, 10);
            AddDisc(
                vh,
                center,
                Mathf.Max(2f, radius - 14f),
                new Color32(20, 29, 31, 150),
                10);
        }

        private static void AddDisc(
            VertexHelper vh,
            Vector2 center,
            float radius,
            Color32 color,
            int sides)
        {
            int centerIndex = vh.currentVertCount;
            vh.AddVert(center, color, new Vector2(0.5f, 0.5f));
            for (int side = 0; side <= sides; side++)
            {
                float angle = side / (float)sides * Mathf.PI * 2f +
                              Mathf.PI * 0.5f;
                Vector2 direction = new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle));
                vh.AddVert(
                    center + direction * radius,
                    color,
                    (direction + Vector2.one) * 0.5f);
                if (side > 0)
                {
                    vh.AddTriangle(
                        centerIndex,
                        centerIndex + side,
                        centerIndex + side + 1);
                }
            }
        }
    }
}
