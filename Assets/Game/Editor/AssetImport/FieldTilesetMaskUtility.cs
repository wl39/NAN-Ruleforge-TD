#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RuleforgeTD.Editor.AssetImport
{
    internal static class FieldTilesetMaskUtility
    {
        internal const int TileSize = 32;
        internal const int AtlasColumns = 8;
        internal const int AtlasRows = 8;
        internal const int TileCount = AtlasColumns * AtlasRows;
        internal static readonly Color32 BlockedColor =
            new Color32(166, 176, 79, 255);

        internal static uint[][] LoadTileMasks(string projectRelativePath)
        {
            Texture2D texture = LoadPng(projectRelativePath);
            try
            {
                if (texture.width != TileSize * AtlasColumns ||
                    texture.height != TileSize * AtlasRows)
                {
                    throw new InvalidOperationException(
                        "Fields collision guide must be 256x256: " +
                        projectRelativePath);
                }

                Color32[] pixels = texture.GetPixels32();
                var result = new uint[TileCount][];
                for (int tileNumber = 1;
                     tileNumber <= TileCount;
                     tileNumber++)
                {
                    int column = (tileNumber - 1) % AtlasColumns;
                    int topRow = (tileNumber - 1) / AtlasColumns;
                    int bottomRow = AtlasRows - topRow - 1;
                    var rows = new uint[TileSize];
                    for (int localY = 0;
                         localY < TileSize;
                         localY++)
                    {
                        uint rowBits = 0;
                        int sourceY = bottomRow * TileSize + localY;
                        for (int localX = 0;
                             localX < TileSize;
                             localX++)
                        {
                            int sourceX = column * TileSize + localX;
                            Color32 color =
                                pixels[sourceY * texture.width + sourceX];
                            if (ColorsEqual(color, BlockedColor))
                            {
                                rowBits |= 1u << localX;
                            }
                        }

                        rows[localY] = rowBits;
                    }

                    result[tileNumber - 1] = rows;
                }

                return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        internal static void ValidateAtlasAgainstIndividualTiles(
            string atlasPath,
            string individualTileFolder)
        {
            Texture2D atlas = LoadPng(atlasPath);
            try
            {
                if (atlas.width != TileSize * AtlasColumns ||
                    atlas.height != TileSize * AtlasRows)
                {
                    throw new InvalidOperationException(
                        "Fields visual atlas must be 256x256.");
                }

                Color32[] atlasPixels = atlas.GetPixels32();
                for (int tileNumber = 1;
                     tileNumber <= TileCount;
                     tileNumber++)
                {
                    string tilePath = individualTileFolder +
                        "/FieldsTile_" +
                        tileNumber.ToString("00") +
                        ".png";
                    if (!File.Exists(tilePath))
                    {
                        throw new FileNotFoundException(
                            "Missing individual Fields tile.",
                            tilePath);
                    }

                    Texture2D tile = LoadPng(tilePath);
                    try
                    {
                        if (tile.width != TileSize ||
                            tile.height != TileSize)
                        {
                            throw new InvalidOperationException(
                                tilePath + " must be 32x32.");
                        }

                        Color32[] tilePixels = tile.GetPixels32();
                        int column =
                            (tileNumber - 1) % AtlasColumns;
                        int topRow =
                            (tileNumber - 1) / AtlasColumns;
                        int bottomRow =
                            AtlasRows - topRow - 1;
                        for (int y = 0; y < TileSize; y++)
                        {
                            for (int x = 0; x < TileSize; x++)
                            {
                                Color32 fromAtlas = atlasPixels[
                                    (bottomRow * TileSize + y) *
                                    atlas.width +
                                    column * TileSize +
                                    x];
                                Color32 fromTile =
                                    tilePixels[y * TileSize + x];
                                if (!ColorsEqual(
                                        fromAtlas,
                                        fromTile))
                                {
                                    throw new InvalidOperationException(
                                        "Fields atlas does not match tile " +
                                        tileNumber +
                                        " at local pixel (" +
                                        x +
                                        "," +
                                        y +
                                        ").");
                                }
                            }
                        }
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(tile);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(atlas);
            }
        }

        internal static int CountBlockedPixels(uint[] rows)
        {
            int count = 0;
            for (int rowIndex = 0;
                 rowIndex < rows.Length;
                 rowIndex++)
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

        internal static List<Vector2[]> CreateRectanglePhysicsShapes(
            uint[] rows)
        {
            var used = new bool[TileSize, TileSize];
            var shapes = new List<Vector2[]>();
            for (int y = 0; y < TileSize; y++)
            {
                for (int x = 0; x < TileSize; x++)
                {
                    if (used[x, y] || !IsBlocked(rows, x, y))
                    {
                        continue;
                    }

                    int width = 1;
                    while (x + width < TileSize &&
                           !used[x + width, y] &&
                           IsBlocked(rows, x + width, y))
                    {
                        width++;
                    }

                    int height = 1;
                    bool canGrow = true;
                    while (y + height < TileSize && canGrow)
                    {
                        for (int checkX = x;
                             checkX < x + width;
                             checkX++)
                        {
                            if (used[checkX, y + height] ||
                                !IsBlocked(
                                    rows,
                                    checkX,
                                    y + height))
                            {
                                canGrow = false;
                                break;
                            }
                        }

                        if (canGrow)
                        {
                            height++;
                        }
                    }

                    for (int markY = y;
                         markY < y + height;
                         markY++)
                    {
                        for (int markX = x;
                             markX < x + width;
                             markX++)
                        {
                            used[markX, markY] = true;
                        }
                    }

                    float left = x - TileSize * 0.5f;
                    float bottom = y - TileSize * 0.5f;
                    float right = left + width;
                    float top = bottom + height;
                    shapes.Add(new[]
                    {
                        new Vector2(left, bottom),
                        new Vector2(right, bottom),
                        new Vector2(right, top),
                        new Vector2(left, top)
                    });
                }
            }

            return shapes;
        }

        internal static int ResolveWalkableTile(
            bool[,] walkable,
            int x,
            int y,
            uint[][] tileMasks,
            int deterministicSalt)
        {
            uint[] ideal = CreateIdealBoundaryMask(
                walkable,
                x,
                y);
            int bestDistance = int.MaxValue;
            var candidates = new List<int>();
            for (int tileNumber = 1;
                 tileNumber <= TileCount;
                 tileNumber++)
            {
                if (tileNumber == 38)
                {
                    continue;
                }

                int distance = MaskDistance(
                    ideal,
                    tileMasks[tileNumber - 1]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    candidates.Clear();
                    candidates.Add(tileNumber);
                }
                else if (distance == bestDistance)
                {
                    candidates.Add(tileNumber);
                }
            }

            if (candidates.Count == 0)
            {
                throw new InvalidOperationException(
                    "No Fields auto-tile candidate was found.");
            }

            int hash = unchecked(
                x * 73856093 ^
                y * 19349663 ^
                deterministicSalt * 83492791);
            int candidateIndex =
                (hash & int.MaxValue) % candidates.Count;
            return candidates[candidateIndex];
        }

        private static uint[] CreateIdealBoundaryMask(
            bool[,] walkable,
            int x,
            int y)
        {
            var rows = new uint[TileSize];
            bool northBlocked = !IsWalkable(walkable, x, y + 1);
            bool southBlocked = !IsWalkable(walkable, x, y - 1);
            bool westBlocked = !IsWalkable(walkable, x - 1, y);
            bool eastBlocked = !IsWalkable(walkable, x + 1, y);

            if (southBlocked)
            {
                AddHorizontalBand(rows, 0, 5);
            }

            if (northBlocked)
            {
                AddHorizontalBand(rows, TileSize - 5, 5);
            }

            if (westBlocked)
            {
                AddVerticalBand(rows, 0, 5);
            }

            if (eastBlocked)
            {
                AddVerticalBand(rows, TileSize - 5, 5);
            }

            if (!IsWalkable(walkable, x - 1, y - 1))
            {
                AddCorner(rows, 0, 0);
            }

            if (!IsWalkable(walkable, x + 1, y - 1))
            {
                AddCorner(rows, TileSize - 5, 0);
            }

            if (!IsWalkable(walkable, x - 1, y + 1))
            {
                AddCorner(rows, 0, TileSize - 5);
            }

            if (!IsWalkable(walkable, x + 1, y + 1))
            {
                AddCorner(
                    rows,
                    TileSize - 5,
                    TileSize - 5);
            }

            return rows;
        }

        private static void AddHorizontalBand(
            uint[] rows,
            int startY,
            int height)
        {
            for (int y = startY; y < startY + height; y++)
            {
                rows[y] = uint.MaxValue;
            }
        }

        private static void AddVerticalBand(
            uint[] rows,
            int startX,
            int width)
        {
            uint bits = ((1u << width) - 1u) << startX;
            for (int y = 0; y < TileSize; y++)
            {
                rows[y] |= bits;
            }
        }

        private static void AddCorner(
            uint[] rows,
            int startX,
            int startY)
        {
            uint bits = 0x1Fu << startX;
            for (int y = startY; y < startY + 5; y++)
            {
                rows[y] |= bits;
            }
        }

        private static int MaskDistance(uint[] left, uint[] right)
        {
            int distance = 0;
            for (int row = 0; row < TileSize; row++)
            {
                uint difference = left[row] ^ right[row];
                while (difference != 0)
                {
                    difference &= difference - 1;
                    distance++;
                }
            }

            return distance;
        }

        private static bool IsWalkable(
            bool[,] walkable,
            int x,
            int y)
        {
            return x >= 0 &&
                   x < walkable.GetLength(0) &&
                   y >= 0 &&
                   y < walkable.GetLength(1) &&
                   walkable[x, y];
        }

        private static bool IsBlocked(uint[] rows, int x, int y)
        {
            return (rows[y] & (1u << x)) != 0;
        }

        private static Texture2D LoadPng(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Required Fields source image was not found.",
                    path);
            }

            byte[] bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false,
                false);
            if (!texture.LoadImage(bytes, false))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw new InvalidOperationException(
                    "Unable to decode PNG: " + path);
            }

            return texture;
        }

        private static bool ColorsEqual(Color32 left, Color32 right)
        {
            return left.r == right.r &&
                   left.g == right.g &&
                   left.b == right.b &&
                   left.a == right.a;
        }
    }
}
#endif
