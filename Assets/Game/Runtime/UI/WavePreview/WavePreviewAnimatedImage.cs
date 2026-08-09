using UnityEngine;
using UnityEngine.UI;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// 전투용 SpriteRenderer 애니메이터의 현재 프레임을 uGUI Image에 복사한다.
    /// 별도 UI용 스프라이트 시트를 만들지 않아 전투 외형과 예고 외형이 항상 같다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class WavePreviewAnimatedImage : MonoBehaviour
    {
        private Image targetImage;
        private SpriteRenderer animationSource;
        private Animator animator;
        private Sprite fallbackSprite;

        public bool IsAnimating =>
            animator != null &&
            animator.enabled &&
            animator.runtimeAnimatorController != null;

        public void Configure(
            Sprite fallback,
            RuntimeAnimatorController controller)
        {
            fallbackSprite = fallback;
            if (targetImage == null)
            {
                targetImage = GetComponent<Image>();
            }

            targetImage.type = Image.Type.Simple;
            targetImage.preserveAspect = true;
            targetImage.useSpriteMesh = false;
            targetImage.material = null;
            targetImage.raycastTarget = false;

            EnsureAnimationSource();
            animator.runtimeAnimatorController = controller;
            animator.enabled = controller != null;
            targetImage.sprite = fallbackSprite;
            targetImage.enabled = fallbackSprite != null || controller != null;

            if (controller == null)
            {
                return;
            }

            if (isActiveAndEnabled && gameObject.activeInHierarchy)
            {
                StartAnimator();
            }
        }

        private void OnEnable()
        {
            if (IsAnimating)
            {
                StartAnimator();
            }
        }

        private void LateUpdate()
        {
            if (IsAnimating)
            {
                CopyCurrentFrame();
            }
        }

        private void EnsureAnimationSource()
        {
            if (animationSource == null)
            {
                animationSource = GetComponent<SpriteRenderer>();
                if (animationSource == null)
                {
                    animationSource = gameObject.AddComponent<SpriteRenderer>();
                }
                animationSource.enabled = false;
            }

            if (animator == null)
            {
                animator = GetComponent<Animator>();
                if (animator == null)
                {
                    animator = gameObject.AddComponent<Animator>();
                }
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            }
        }

        private void CopyCurrentFrame()
        {
            Sprite frame = animationSource == null
                ? null
                : animationSource.sprite;
            targetImage.sprite = frame != null ? frame : fallbackSprite;
        }

        private void StartAnimator()
        {
            animator.Rebind();
            animator.Update(0f);
            CopyCurrentFrame();
        }
    }
}
