using UnityEngine;

namespace RuleforgeTD.Maps
{
    /// <summary>
    /// Semantic grouping for decorations that form one authored terrain
    /// feature, such as a grove, campsite, or roadside enclosure.
    /// </summary>
    public sealed class FieldDecorationCluster : MonoBehaviour
    {
        [SerializeField]
        private string clusterId = string.Empty;

        [SerializeField]
        private string profile = string.Empty;

        [SerializeField]
        private Vector2 radius;

        [SerializeField]
        private int seed;

        public string ClusterId => clusterId;
        public string Profile => profile;
        public Vector2 Radius => radius;
        public int Seed => seed;
        public Vector2 Center => transform.position;

        public void ConfigureAuthoring(
            string id,
            string clusterProfile,
            Vector2 clusterRadius,
            int clusterSeed)
        {
            clusterId = id ?? string.Empty;
            profile = clusterProfile ?? string.Empty;
            radius = new Vector2(
                Mathf.Max(0f, clusterRadius.x),
                Mathf.Max(0f, clusterRadius.y));
            seed = clusterSeed;
        }
    }
}
