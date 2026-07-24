using System;
using UnityEngine;

namespace RuleforgeTD.Maps
{
    /// <summary>
    /// Deterministic presentation-only sprite animation driven by the global
    /// unscaled clock. It never consumes gameplay random state.
    /// </summary>
    public sealed class FieldSpriteAnimator : MonoBehaviour
    {
        private const float MinimumFrameDuration = 0.01f;

        [SerializeField]
        private SpriteRenderer target;

        [SerializeField]
        private Sprite[] frames = Array.Empty<Sprite>();

        [SerializeField]
        [Min(MinimumFrameDuration)]
        private float frameDuration = 0.12f;

        public SpriteRenderer TargetRenderer => target;
        public int FrameCount => frames == null ? 0 : frames.Length;
        public float FrameDuration => frameDuration;

        public void ConfigureAuthoring(
            SpriteRenderer targetRenderer,
            Sprite[] animationFrames,
            float duration)
        {
            if (targetRenderer == null)
            {
                throw new ArgumentNullException(nameof(targetRenderer));
            }

            if (animationFrames == null || animationFrames.Length == 0)
            {
                throw new ArgumentException(
                    "A field sprite animation requires at least one frame.",
                    nameof(animationFrames));
            }

            for (int i = 0; i < animationFrames.Length; i++)
            {
                if (animationFrames[i] == null)
                {
                    throw new ArgumentException(
                        "Field sprite animation frames cannot contain null.",
                        nameof(animationFrames));
                }
            }

            target = targetRenderer;
            frames = (Sprite[])animationFrames.Clone();
            frameDuration = Mathf.Max(MinimumFrameDuration, duration);
            ShowFrame(0);
        }

        private void OnEnable()
        {
            ShowFrame(0);
        }

        private void OnValidate()
        {
            frameDuration = Mathf.Max(
                MinimumFrameDuration,
                frameDuration);
        }

        private void Update()
        {
            if (target == null || FrameCount == 0)
            {
                return;
            }

            int frameIndex =
                Mathf.FloorToInt(Time.unscaledTime / frameDuration) %
                FrameCount;
            ShowFrame(frameIndex);
        }

        private void ShowFrame(int frameIndex)
        {
            if (target == null ||
                frames == null ||
                frameIndex < 0 ||
                frameIndex >= frames.Length)
            {
                return;
            }

            target.sprite = frames[frameIndex];
        }
    }
}
