using UnityEngine;

namespace RuleforgeTD.Enemies
{
    [CreateAssetMenu(
        fileName = "EnemyHealthBarVisualSettings",
        menuName = "Ruleforge TD/Enemies/Health Bar Visual Settings")]
    public sealed class EnemyHealthBarVisualSettings :
        ScriptableObject
    {
        [SerializeField, Min(0.01f)]
        private float localY;
        [SerializeField, Min(0.001f)]
        private float backgroundWidth;
        [SerializeField, Min(0.001f)]
        private float backgroundHeight;
        [SerializeField, Min(0.001f)]
        private float fillWidth;
        [SerializeField, Min(0.001f)]
        private float fillHeight;

        public float LocalY => localY;
        public float BackgroundWidth => backgroundWidth;
        public float BackgroundHeight => backgroundHeight;
        public float FillWidth => fillWidth;
        public float FillHeight => fillHeight;

        private void OnValidate()
        {
            localY = Mathf.Max(0.01f, localY);
            backgroundWidth =
                Mathf.Max(0.001f, backgroundWidth);
            backgroundHeight =
                Mathf.Max(0.001f, backgroundHeight);
            fillWidth = Mathf.Clamp(
                fillWidth,
                0.001f,
                backgroundWidth);
            fillHeight = Mathf.Clamp(
                fillHeight,
                0.001f,
                backgroundHeight);
        }
    }
}
