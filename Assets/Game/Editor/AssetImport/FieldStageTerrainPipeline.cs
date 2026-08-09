#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using RuleforgeTD.Maps;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleforgeTD.Editor.AssetImport
{
    /// <summary>
    /// Shared Fields terrain authoring for stage-specific paths and map
    /// aspect ratios. The generated navigation tiles remain deterministic.
    /// </summary>
    internal static class FieldStageTerrainPipeline
    {
        private const string TerrainTileRoot =
            "Assets/Game/Data/Maps/Fields/Tiles/Terrain";
        private const string CollisionGuidePath =
            "Assets/ThirdParty/CraftPix/Raw/Maps/Fields/" +
            "Tiles/FieldsTilesetTest.png";

        public static void Paint(
            Tilemap terrain,
            int mapMinX,
            int mapMaxX,
            int mapMinY,
            int mapMaxY,
            float roadHalfWidth,
            Vector2[] pathPoints,
            int terrainSalt,
            string stageId)
        {
            if (terrain == null)
            {
                throw new ArgumentNullException(nameof(terrain));
            }
            if (mapMaxX < mapMinX || mapMaxY < mapMinY ||
                roadHalfWidth <= 0f ||
                pathPoints == null || pathPoints.Length < 2)
            {
                throw new ArgumentException(
                    "Stage terrain bounds or path are invalid.");
            }

            FieldTerrainTile[] tiles =
                AssetDatabase.FindAssets(
                        "t:FieldTerrainTile",
                        new[] { TerrainTileRoot })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<
                        FieldTerrainTile>)
                    .Where(tile => tile != null)
                    .OrderBy(tile => tile.TileNumber)
                    .ToArray();
            if (tiles.Length != 64)
            {
                throw new InvalidOperationException(
                    stageId + " requires all 64 generated terrain tiles.");
            }

            var byNumber = new Dictionary<int, FieldTerrainTile>(64);
            for (int i = 0; i < tiles.Length; i++)
            {
                byNumber.Add(tiles[i].TileNumber, tiles[i]);
            }

            uint[][] masks =
                FieldTilesetMaskUtility.LoadTileMasks(
                    CollisionGuidePath);
            bool[,] walkable = BuildWalkableLayout(
                mapMinX,
                mapMaxX,
                mapMinY,
                mapMaxY,
                roadHalfWidth,
                pathPoints);

            terrain.ClearAllTiles();
            int width = walkable.GetLength(0);
            int height = walkable.GetLength(1);
            for (int localY = 0; localY < height; localY++)
            {
                for (int localX = 0; localX < width; localX++)
                {
                    int tileNumber = walkable[localX, localY]
                        ? FieldTilesetMaskUtility.ResolveWalkableTile(
                            walkable,
                            localX,
                            localY,
                            masks,
                            terrainSalt)
                        : 38;
                    terrain.SetTile(
                        new Vector3Int(
                            mapMinX + localX,
                            mapMinY + localY,
                            0),
                        byNumber[tileNumber]);
                }
            }

            terrain.CompressBounds();
        }

        private static bool[,] BuildWalkableLayout(
            int mapMinX,
            int mapMaxX,
            int mapMinY,
            int mapMaxY,
            float roadHalfWidth,
            Vector2[] pathPoints)
        {
            int width = mapMaxX - mapMinX + 1;
            int height = mapMaxY - mapMinY + 1;
            var walkable = new bool[width, height];
            for (int localY = 0; localY < height; localY++)
            {
                for (int localX = 0; localX < width; localX++)
                {
                    var point = new Vector2(
                        mapMinX + localX,
                        mapMinY + localY);
                    float distance = float.MaxValue;
                    for (int segment = 0;
                         segment < pathPoints.Length - 1;
                         segment++)
                    {
                        distance = Mathf.Min(
                            distance,
                            DistanceToSegment(
                                point,
                                pathPoints[segment],
                                pathPoints[segment + 1]));
                    }

                    walkable[localX, localY] =
                        distance <= roadHalfWidth;
                }
            }

            return walkable;
        }

        private static float DistanceToSegment(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
            {
                return Vector2.Distance(point, start);
            }

            float amount = Mathf.Clamp01(
                Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * amount);
        }
    }
}
#endif
