using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleforgeTD.Maps
{
    /// <summary>
    /// Queries the baked semantic green mask painted on the terrain Tilemap.
    /// Physics colliders are presentation safeguards; deterministic enemy
    /// movement remains authoritative in GameLogic.PathModel.
    /// </summary>
    public sealed class StageNavigationMask : MonoBehaviour
    {
        [SerializeField]
        private Tilemap terrain;

        public Tilemap Terrain => terrain;

        public void ConfigureAuthoring(Tilemap terrainTilemap)
        {
            terrain = terrainTilemap;
        }

        public bool IsBlocked(Vector2 worldPosition)
        {
            if (terrain == null)
            {
                return true;
            }

            Vector3Int cell = terrain.WorldToCell(worldPosition);
            FieldTerrainTile tile =
                terrain.GetTile<FieldTerrainTile>(cell);
            if (tile == null)
            {
                return true;
            }

            GridLayout layout = terrain.layoutGrid;
            Vector3 localPoint =
                layout.transform.InverseTransformPoint(worldPosition);
            Vector3 cellSize = layout.cellSize;
            Vector3 cellGap = layout.cellGap;
            var cellStride = new Vector3(
                cellSize.x + cellGap.x,
                cellSize.y + cellGap.y,
                cellSize.z + cellGap.z);
            if (Mathf.Approximately(cellSize.x, 0f) ||
                Mathf.Approximately(cellSize.y, 0f))
            {
                return true;
            }

            var normalized = new Vector2(
                (localPoint.x - cell.x * cellStride.x) / cellSize.x,
                (localPoint.y - cell.y * cellStride.y) / cellSize.y);
            return tile.IsBlockedNormalized(normalized);
        }
    }
}
