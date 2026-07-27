using System;
using RuleforgeTD.Rendering;
using UnityEngine;

namespace RuleforgeTD.Towers.Archer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class DirectionalArcherAnimator : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Transform projectileOrigin;
        [SerializeField] private bool sideFramesFaceLeft = true;
        [SerializeField] private Vector2 initialAim = Vector2.down;
        [SerializeField] private int idlePhaseSeed;
        [SerializeField, Min(0.05f)] private float idleFrameDuration = 0.18f;
        [SerializeField, Min(0.05f)] private float preattackDuration = 0.15f;
        [SerializeField, Min(0.04f)] private float attackFrameDuration = 0.09f;

        [SerializeField] private Sprite[] idleDown = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] idleUp = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] idleSide = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] preattackDown = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] preattackUp = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] preattackSide = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] attackDown = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] attackUp = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] attackSide = Array.Empty<Sprite>();

        private EnemyFacingDirection currentDirection = EnemyFacingDirection.Down;
        private ArcherUnitAnimationBehaviour currentBehaviour =
            ArcherUnitAnimationBehaviour.Idle;
        private Vector2 aimDirection = Vector2.down;
        private int currentFrameIndex;
        private float elapsed;
        private bool blueprintPreviewAnimationEnabled;

        public event Action<DirectionalArcherAnimator> ArrowReleased;

        public EnemyFacingDirection CurrentDirection => currentDirection;
        public ArcherUnitAnimationBehaviour CurrentBehaviour => currentBehaviour;
        public Vector2 AimDirection => aimDirection;
        public Vector3 ProjectileOrigin => projectileOrigin == null
            ? transform.position + Vector3.up * 0.34f
            : projectileOrigin.position;
        public int CurrentFrameIndex => currentFrameIndex;
        public bool IsConfigured => spriteRenderer != null && idleDown.Length > 0;
        public float ArrowReleaseDelay =>
            preattackDuration +
            Mathf.Max(
                0,
                GetFrames(
                    ArcherUnitAnimationBehaviour.Attack,
                    currentDirection).Length - 1) *
            attackFrameDuration;
        public bool IsBlueprintPreviewAnimationEnabled =>
            blueprintPreviewAnimationEnabled;

        private void Awake()
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            SetAim(initialAim);
            ResetToIdle(true);
        }

        private void OnEnable()
        {
            if (spriteRenderer != null && idleDown.Length > 0)
            {
                SetAim(initialAim);
                ResetToIdle(true);
            }
        }

        private void Update()
        {
            if (!IsConfigured)
            {
                return;
            }

            elapsed += blueprintPreviewAnimationEnabled
                ? Time.unscaledDeltaTime
                : Time.deltaTime;
            switch (currentBehaviour)
            {
                case ArcherUnitAnimationBehaviour.Preattack:
                    UpdatePreattack();
                    break;
                case ArcherUnitAnimationBehaviour.Attack:
                    UpdateAttack();
                    break;
                default:
                    UpdateIdle();
                    break;
            }
        }

        public void Configure(
            SpriteRenderer targetRenderer,
            Transform arrowOrigin,
            bool horizontalFramesFaceLeft,
            Vector2 defaultAim,
            int phaseSeed,
            Sprite[] downIdle,
            Sprite[] upIdle,
            Sprite[] sideIdle,
            Sprite[] downPreattack,
            Sprite[] upPreattack,
            Sprite[] sidePreattack,
            Sprite[] downAttack,
            Sprite[] upAttack,
            Sprite[] sideAttack)
        {
            spriteRenderer = targetRenderer;
            projectileOrigin = arrowOrigin;
            sideFramesFaceLeft = horizontalFramesFaceLeft;
            initialAim = defaultAim.sqrMagnitude <= 0.000001f
                ? Vector2.down
                : defaultAim.normalized;
            idlePhaseSeed = phaseSeed;
            idleDown = downIdle ?? Array.Empty<Sprite>();
            idleUp = upIdle ?? Array.Empty<Sprite>();
            idleSide = sideIdle ?? Array.Empty<Sprite>();
            preattackDown = downPreattack ?? Array.Empty<Sprite>();
            preattackUp = upPreattack ?? Array.Empty<Sprite>();
            preattackSide = sidePreattack ?? Array.Empty<Sprite>();
            attackDown = downAttack ?? Array.Empty<Sprite>();
            attackUp = upAttack ?? Array.Empty<Sprite>();
            attackSide = sideAttack ?? Array.Empty<Sprite>();

            SetAim(initialAim);
            ResetToIdle(true);
        }

        public void SetAim(Vector2 direction)
        {
            if (direction.sqrMagnitude > 0.000001f)
            {
                aimDirection = direction.normalized;
            }

            currentDirection = EnemyDirectionResolver.Resolve(
                aimDirection,
                currentDirection);
            ApplyHorizontalFlip();
            ApplyCurrentSprite();
        }

        public bool PlayAttack()
        {
            if (blueprintPreviewAnimationEnabled)
            {
                return false;
            }

            Sprite[] attackFrames = GetFrames(
                ArcherUnitAnimationBehaviour.Attack,
                currentDirection);
            if (attackFrames.Length == 0)
            {
                return false;
            }

            Sprite[] preattackFrames = GetFrames(
                ArcherUnitAnimationBehaviour.Preattack,
                currentDirection);
            currentBehaviour = preattackFrames.Length > 0
                ? ArcherUnitAnimationBehaviour.Preattack
                : ArcherUnitAnimationBehaviour.Attack;
            currentFrameIndex = 0;
            elapsed = 0f;
            ApplyCurrentSprite();
            return true;
        }

        public void ResetToIdle(bool randomizedPhase)
        {
            currentBehaviour = ArcherUnitAnimationBehaviour.Idle;
            Sprite[] frames = GetFrames(currentBehaviour, currentDirection);
            currentFrameIndex = randomizedPhase && frames.Length > 0
                ? PositiveModulo(idlePhaseSeed, frames.Length)
                : 0;
            elapsed = randomizedPhase
                ? idleFrameDuration * HashToUnitInterval((uint)(idlePhaseSeed + 1))
                : 0f;
            ApplyCurrentSprite();
        }

        /// <summary>
        /// Keeps only this selected tower's idle presentation moving while the
        /// blueprint pauses world time. Entering preview cancels any attack so
        /// no ArrowReleased event can leak out of the paused workbench.
        /// </summary>
        public void SetBlueprintPreviewAnimation(bool enabled)
        {
            if (blueprintPreviewAnimationEnabled == enabled)
            {
                return;
            }

            blueprintPreviewAnimationEnabled = enabled;
            if (enabled)
            {
                ResetToIdle(true);
            }
        }

        private void UpdateIdle()
        {
            Sprite[] frames = GetFrames(currentBehaviour, currentDirection);
            if (frames.Length <= 1 || elapsed < idleFrameDuration)
            {
                return;
            }

            elapsed -= idleFrameDuration;
            currentFrameIndex = (currentFrameIndex + 1) % frames.Length;
            ApplyCurrentSprite();
        }

        private void UpdatePreattack()
        {
            if (elapsed < preattackDuration)
            {
                return;
            }

            elapsed -= preattackDuration;
            currentBehaviour = ArcherUnitAnimationBehaviour.Attack;
            currentFrameIndex = 0;
            ApplyCurrentSprite();
            UpdateAttack();
        }

        private void UpdateAttack()
        {
            Sprite[] frames = GetFrames(currentBehaviour, currentDirection);
            while (currentBehaviour ==
                       ArcherUnitAnimationBehaviour.Attack &&
                   elapsed >= attackFrameDuration)
            {
                elapsed -= attackFrameDuration;
                currentFrameIndex++;
                if (currentFrameIndex >= frames.Length)
                {
                    ResetToIdle(false);
                    return;
                }

                ApplyCurrentSprite();
                if (currentFrameIndex == frames.Length - 1)
                {
                    // The authored attack sheets keep the nocked arrow visible
                    // through the penultimate frame. Emit on the first frame
                    // where it has left the bow so the projectile and release
                    // pose begin together.
                    ArrowReleased?.Invoke(this);
                }
            }
        }

        private void ApplyCurrentSprite()
        {
            if (spriteRenderer == null)
            {
                return;
            }

            Sprite[] frames = GetFrames(currentBehaviour, currentDirection);
            if (frames.Length == 0)
            {
                return;
            }

            currentFrameIndex = Mathf.Clamp(currentFrameIndex, 0, frames.Length - 1);
            spriteRenderer.sprite = frames[currentFrameIndex];
            ApplyHorizontalFlip();
        }

        private void ApplyHorizontalFlip()
        {
            if (spriteRenderer == null)
            {
                return;
            }

            switch (currentDirection)
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

            spriteRenderer.flipY = false;
        }

        private Sprite[] GetFrames(
            ArcherUnitAnimationBehaviour behaviour,
            EnemyFacingDirection direction)
        {
            bool isUp = direction == EnemyFacingDirection.Up;
            bool isSide = direction == EnemyFacingDirection.SideLeft ||
                          direction == EnemyFacingDirection.SideRight;

            switch (behaviour)
            {
                case ArcherUnitAnimationBehaviour.Preattack:
                    return isSide
                        ? preattackSide
                        : isUp
                            ? preattackUp
                            : preattackDown;
                case ArcherUnitAnimationBehaviour.Attack:
                    return isSide
                        ? attackSide
                        : isUp
                            ? attackUp
                            : attackDown;
                default:
                    return isSide
                        ? idleSide
                        : isUp
                            ? idleUp
                            : idleDown;
            }
        }

        private static int PositiveModulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private static float HashToUnitInterval(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return (value & 0x00ffffffu) / 16777215f;
        }
    }
}
