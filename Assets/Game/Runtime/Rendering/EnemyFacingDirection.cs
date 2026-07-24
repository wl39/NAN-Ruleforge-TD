using UnityEngine;

namespace RuleforgeTD.Rendering
{
    public enum EnemyFacingDirection
    {
        Down,
        Up,
        SideLeft,
        SideRight
    }

    public static class EnemyDirectionResolver
    {
        public static EnemyFacingDirection Resolve(Vector2 movement, EnemyFacingDirection fallback)
        {
            if (movement.sqrMagnitude <= 0.000001f)
            {
                return fallback;
            }

            if (Mathf.Abs(movement.x) > Mathf.Abs(movement.y))
            {
                return movement.x < 0f
                    ? EnemyFacingDirection.SideLeft
                    : EnemyFacingDirection.SideRight;
            }

            return movement.y < 0f
                ? EnemyFacingDirection.Down
                : EnemyFacingDirection.Up;
        }
    }
}
