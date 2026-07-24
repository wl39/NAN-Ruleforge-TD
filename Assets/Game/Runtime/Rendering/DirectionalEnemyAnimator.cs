using UnityEngine;

namespace RuleforgeTD.Rendering
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator), typeof(SpriteRenderer))]
    public sealed class DirectionalEnemyAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private bool sideFramesFaceLeft = true;
        [SerializeField] private EnemyAnimationBehaviour[] availableBehaviours =
        {
            EnemyAnimationBehaviour.Walk
        };

        private EnemyFacingDirection currentDirection = EnemyFacingDirection.Down;
        private EnemyAnimationBehaviour currentBehaviour = EnemyAnimationBehaviour.Walk;
        private string currentStateName;

        public EnemyFacingDirection CurrentDirection => currentDirection;
        public EnemyAnimationBehaviour CurrentBehaviour => currentBehaviour;
        public int AvailableBehaviourCount => availableBehaviours == null ? 0 : availableBehaviours.Length;

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            ApplyState(currentBehaviour, currentDirection, true);
        }

        public void SetMovement(Vector2 movement)
        {
            EnemyFacingDirection nextDirection = EnemyDirectionResolver.Resolve(movement, currentDirection);
            currentDirection = nextDirection;
            ApplyHorizontalFlip(nextDirection);

            if (currentBehaviour == EnemyAnimationBehaviour.Walk ||
                currentBehaviour == EnemyAnimationBehaviour.Walk2)
            {
                ApplyState(currentBehaviour, nextDirection, false);
            }
        }

        public bool Supports(EnemyAnimationBehaviour behaviour)
        {
            if (availableBehaviours == null)
            {
                return false;
            }

            for (int i = 0; i < availableBehaviours.Length; i++)
            {
                if (availableBehaviours[i] == behaviour)
                {
                    return true;
                }
            }

            return false;
        }

        public bool PlayBehaviour(EnemyAnimationBehaviour behaviour, bool restart = true)
        {
            if (!Supports(behaviour))
            {
                return false;
            }

            ApplyState(behaviour, currentDirection, restart);
            return true;
        }

        public void Configure(
            Animator targetAnimator,
            SpriteRenderer targetRenderer,
            bool horizontalFramesFaceLeft,
            EnemyAnimationBehaviour[] supportedBehaviours)
        {
            animator = targetAnimator;
            spriteRenderer = targetRenderer;
            sideFramesFaceLeft = horizontalFramesFaceLeft;
            availableBehaviours = supportedBehaviours;
        }

        private void ApplyState(
            EnemyAnimationBehaviour behaviour,
            EnemyFacingDirection direction,
            bool restart)
        {
            if (animator == null || spriteRenderer == null)
            {
                return;
            }

            string nextStateName = behaviour + GetDirectionSuffix(direction);
            currentDirection = direction;
            currentBehaviour = behaviour;
            ApplyHorizontalFlip(direction);

            if (!restart && currentStateName == nextStateName)
            {
                return;
            }

            currentStateName = nextStateName;
            animator.Play(nextStateName, 0, 0f);
        }

        private void ApplyHorizontalFlip(EnemyFacingDirection direction)
        {
            switch (direction)
            {
                case EnemyFacingDirection.SideLeft:
                    spriteRenderer.flipX = !sideFramesFaceLeft;
                    break;
                case EnemyFacingDirection.SideRight:
                    spriteRenderer.flipX = sideFramesFaceLeft;
                    break;
                default:
                    spriteRenderer.flipX = false;
                    break;
            }
        }

        private static string GetDirectionSuffix(EnemyFacingDirection direction)
        {
            switch (direction)
            {
                case EnemyFacingDirection.Up:
                    return "Up";
                case EnemyFacingDirection.SideLeft:
                case EnemyFacingDirection.SideRight:
                    return "Side";
                default:
                    return "Down";
            }
        }
    }
}
