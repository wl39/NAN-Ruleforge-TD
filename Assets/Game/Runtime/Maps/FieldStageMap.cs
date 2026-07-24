using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleforgeTD.Maps
{
    /// <summary>
    /// Scene-level references for an editable Fields Tilemap stage.
    /// </summary>
    public sealed class FieldStageMap : MonoBehaviour
    {
        [SerializeField]
        private Tilemap terrain;

        [SerializeField]
        private Tilemap groundDecals;

        [SerializeField]
        private Tilemap props;

        [SerializeField]
        private Tilemap animatedObjects;

        [SerializeField]
        private StageNavigationMask navigationMask;

        [SerializeField]
        private StagePathAuthoring path;

        [SerializeField]
        private TowerBuildSiteView[] buildSites =
            Array.Empty<TowerBuildSiteView>();

        public Tilemap Terrain => terrain;
        public Tilemap GroundDecals => groundDecals;
        public Tilemap Props => props;
        public Tilemap AnimatedObjects => animatedObjects;
        public StageNavigationMask NavigationMask => navigationMask;
        public StagePathAuthoring Path => path;
        public int BuildSiteCount =>
            buildSites == null ? 0 : buildSites.Length;

        public void ConfigureAuthoring(
            Tilemap terrainTilemap,
            Tilemap decalTilemap,
            Tilemap propTilemap,
            Tilemap animatedTilemap,
            StageNavigationMask mask,
            StagePathAuthoring pathAuthoring,
            TowerBuildSiteView[] sites)
        {
            terrain = terrainTilemap;
            groundDecals = decalTilemap;
            props = propTilemap;
            animatedObjects = animatedTilemap;
            navigationMask = mask;
            path = pathAuthoring;
            buildSites = sites == null
                ? Array.Empty<TowerBuildSiteView>()
                : (TowerBuildSiteView[])sites.Clone();
        }

        public TowerBuildSiteView GetBuildSite(int index)
        {
            if (index < 0 || index >= BuildSiteCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return buildSites[index];
        }
    }
}
