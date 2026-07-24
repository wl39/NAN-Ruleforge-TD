using UnityEngine;

namespace RuleforgeTD.Maps
{
    /// <summary>
    /// Scene-authored decoration made from an independently sorted body and
    /// optional ground contact sprite.
    /// </summary>
    public sealed class FieldDecorationView : MonoBehaviour
    {
        [SerializeField]
        private string assetKey = string.Empty;

        [SerializeField]
        private string clusterId = string.Empty;

        [SerializeField]
        private bool isRoadsideMarker;

        [SerializeField]
        private SpriteRenderer body;

        [SerializeField]
        private SpriteRenderer groundBase;

        public string AssetKey => assetKey;
        public string ClusterId => clusterId;
        public bool IsRoadsideMarker => isRoadsideMarker;
        public SpriteRenderer Body => body;
        public SpriteRenderer GroundBase => groundBase;
        public bool HasGroundBase => groundBase != null;

        public bool FlipX
        {
            get => body != null && body.flipX;
            set
            {
                if (body != null)
                {
                    body.flipX = value;
                }
            }
        }

        public void ConfigureAuthoring(
            string decorationAssetKey,
            string decorationClusterId,
            bool roadsideMarker,
            SpriteRenderer bodyRenderer,
            SpriteRenderer groundBaseRenderer)
        {
            assetKey = decorationAssetKey ?? string.Empty;
            clusterId = decorationClusterId ?? string.Empty;
            isRoadsideMarker = roadsideMarker;
            body = bodyRenderer;
            groundBase = groundBaseRenderer;
        }
    }
}
