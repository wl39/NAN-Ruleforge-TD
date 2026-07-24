using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleforgeTD.Maps
{
    /// <summary>
    /// A paintable Fields terrain tile with the semantic navigation mask baked
    /// from FieldsTilesetTest.png.
    /// </summary>
    [CreateAssetMenu(
        fileName = "FieldTerrainTile",
        menuName = "Ruleforge TD/Maps/Field Terrain Tile")]
    public sealed class FieldTerrainTile : Tile
    {
        public const int MaskResolution = 32;

        [SerializeField]
        private int tileNumber;

        [SerializeField]
        private uint[] blockedRows = new uint[MaskResolution];

        [SerializeField]
        private int blockedPixelCount;

        public int TileNumber => tileNumber;
        public int BlockedPixelCount => blockedPixelCount;
        public bool HasBlockedArea => blockedPixelCount > 0;
        public bool IsFullyBlocked =>
            blockedPixelCount == MaskResolution * MaskResolution;

        /// <summary>
        /// Configures generated authoring data. Runtime systems only read the
        /// resulting immutable asset.
        /// </summary>
        public void ConfigureAuthoring(
            int number,
            Sprite visualSprite,
            uint[] rows)
        {
            if (number < 1 || number > 64)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(number),
                    "Fields tile numbers must be in the range 1..64.");
            }

            if (visualSprite == null)
            {
                throw new ArgumentNullException(nameof(visualSprite));
            }

            if (rows == null || rows.Length != MaskResolution)
            {
                throw new ArgumentException(
                    "A Fields tile requires exactly 32 blocked-mask rows.",
                    nameof(rows));
            }

            tileNumber = number;
            sprite = visualSprite;
            color = Color.white;
            transform = Matrix4x4.identity;
            gameObject = null;
            flags = TileFlags.LockColor | TileFlags.LockTransform;
            blockedRows = (uint[])rows.Clone();
            blockedPixelCount = CountBlockedPixels(blockedRows);
            colliderType = blockedPixelCount == 0
                ? ColliderType.None
                : ColliderType.Sprite;
        }

        public bool IsBlockedPixel(int x, int y)
        {
            if (x < 0 ||
                x >= MaskResolution ||
                y < 0 ||
                y >= MaskResolution)
            {
                return true;
            }

            return (blockedRows[y] & (1u << x)) != 0;
        }

        public bool IsBlockedNormalized(Vector2 normalizedCellPosition)
        {
            int x = Mathf.Clamp(
                Mathf.FloorToInt(
                    normalizedCellPosition.x * MaskResolution),
                0,
                MaskResolution - 1);
            int y = Mathf.Clamp(
                Mathf.FloorToInt(
                    normalizedCellPosition.y * MaskResolution),
                0,
                MaskResolution - 1);
            return IsBlockedPixel(x, y);
        }

        public uint[] GetBlockedRowsCopy()
        {
            return blockedRows == null
                ? Array.Empty<uint>()
                : (uint[])blockedRows.Clone();
        }

        private static int CountBlockedPixels(uint[] rows)
        {
            int count = 0;
            for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                uint row = rows[rowIndex];
                while (row != 0)
                {
                    row &= row - 1;
                    count++;
                }
            }

            return count;
        }
    }
}
