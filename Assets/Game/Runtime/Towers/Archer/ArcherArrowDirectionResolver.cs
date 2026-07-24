using UnityEngine;

namespace RuleforgeTD.Towers.Archer
{
    public readonly struct ArcherArrowVisual
    {
        public ArcherArrowVisual(int spriteIndex, bool flipX, bool flipY)
        {
            SpriteIndex = spriteIndex;
            FlipX = flipX;
            FlipY = flipY;
        }

        public int SpriteIndex { get; }
        public bool FlipX { get; }
        public bool FlipY { get; }
    }

    public static class ArcherArrowDirectionResolver
    {
        public static ArcherArrowVisual Resolve(Vector2 direction, int bankSize)
        {
            if (bankSize <= 0)
            {
                return new ArcherArrowVisual(0, false, false);
            }

            float absoluteX = Mathf.Abs(direction.x);
            float absoluteY = Mathf.Abs(direction.y);
            float angleFromUp = absoluteX <= 0.000001f && absoluteY <= 0.000001f
                ? 0f
                : Mathf.Atan2(absoluteX, absoluteY);
            float normalizedAngle = angleFromUp / (Mathf.PI * 0.5f);
            int spriteIndex = Mathf.Clamp(
                Mathf.RoundToInt(normalizedAngle * (bankSize - 1)),
                0,
                bankSize - 1);

            return new ArcherArrowVisual(
                spriteIndex,
                direction.x < 0f,
                direction.y < 0f);
        }
    }
}
