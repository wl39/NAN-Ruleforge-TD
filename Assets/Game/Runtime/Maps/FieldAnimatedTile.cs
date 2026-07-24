using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleforgeTD.Maps
{
    /// <summary>
    /// Deterministic presentation-only sprite sequence for animated Tilemaps.
    /// The animation never consumes gameplay random state.
    /// </summary>
    [CreateAssetMenu(
        fileName = "FieldAnimatedTile",
        menuName = "Ruleforge TD/Maps/Field Animated Tile")]
    public sealed class FieldAnimatedTile : TileBase
    {
        [SerializeField]
        private Sprite[] frames = Array.Empty<Sprite>();

        [SerializeField]
        private bool flipX;

        [SerializeField]
        [Min(0.01f)]
        private float animationSpeed = 1f;

        public int FrameCount => frames == null ? 0 : frames.Length;
        public bool FlipX => flipX;
        public float AnimationSpeed => animationSpeed;

        public Sprite GetFrame(int index)
        {
            if (frames == null ||
                index < 0 ||
                index >= frames.Length)
            {
                return null;
            }

            return frames[index];
        }

        public void ConfigureAuthoring(
            Sprite[] animationFrames,
            bool shouldFlipX,
            float speed = 1f)
        {
            if (animationFrames == null || animationFrames.Length == 0)
            {
                throw new ArgumentException(
                    "An animated tile requires at least one frame.",
                    nameof(animationFrames));
            }

            for (int i = 0; i < animationFrames.Length; i++)
            {
                if (animationFrames[i] == null)
                {
                    throw new ArgumentException(
                        "Animated tile frames cannot contain null.",
                        nameof(animationFrames));
                }
            }

            frames = (Sprite[])animationFrames.Clone();
            flipX = shouldFlipX;
            animationSpeed = Mathf.Max(0.01f, speed);
        }

        public override void GetTileData(
            Vector3Int position,
            ITilemap tilemap,
            ref TileData tileData)
        {
            tileData.sprite = FrameCount == 0 ? null : frames[0];
            tileData.color = Color.white;
            tileData.transform = flipX
                ? Matrix4x4.Scale(new Vector3(-1f, 1f, 1f))
                : Matrix4x4.identity;
            tileData.gameObject = null;
            tileData.flags =
                TileFlags.LockColor | TileFlags.LockTransform;
            tileData.colliderType = Tile.ColliderType.None;
        }

        public override bool GetTileAnimationData(
            Vector3Int position,
            ITilemap tilemap,
            ref TileAnimationData tileAnimationData)
        {
            if (FrameCount == 0)
            {
                return false;
            }

            tileAnimationData.animatedSprites = frames;
            tileAnimationData.animationSpeed = animationSpeed;
            tileAnimationData.animationStartTime = 0f;
            return true;
        }
    }
}
