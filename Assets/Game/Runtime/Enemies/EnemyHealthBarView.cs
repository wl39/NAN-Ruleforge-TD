using UnityEngine;

namespace RuleforgeTD.Enemies
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class EnemyHealthBarView : MonoBehaviour
    {
        [SerializeField] private EnemyHealth health;
        [SerializeField] private Transform fillTransform;
        [SerializeField] private SpriteRenderer fillRenderer;
        [SerializeField] private TextMesh valueText;
        [SerializeField, Min(0.01f)] private float fullWidth = 0.74f;

        public string DisplayedValue => valueText == null ? string.Empty : valueText.text;

        private void Awake()
        {
            if (health == null)
            {
                health = GetComponent<EnemyHealth>();
            }
        }

        private void OnEnable()
        {
            health.HealthChanged += Refresh;
            Refresh(health.CurrentHealth, health.MaxHealth);
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.HealthChanged -= Refresh;
            }
        }

        public void Configure(
            EnemyHealth targetHealth,
            Transform targetFill,
            SpriteRenderer targetFillRenderer,
            TextMesh targetValueText,
            float width)
        {
            health = targetHealth;
            fillTransform = targetFill;
            fillRenderer = targetFillRenderer;
            valueText = targetValueText;
            fullWidth = width;
            Refresh(health.CurrentHealth, health.MaxHealth);
        }

        private void Refresh(int currentHealth, int maxHealth)
        {
            float ratio = maxHealth <= 0 ? 0f : Mathf.Clamp01((float)currentHealth / maxHealth);
            float fillWidth = fullWidth * ratio;

            if (fillTransform != null)
            {
                Vector3 scale = fillTransform.localScale;
                scale.x = fillWidth;
                fillTransform.localScale = scale;

                Vector3 position = fillTransform.localPosition;
                position.x = -fullWidth * 0.5f + fillWidth * 0.5f;
                fillTransform.localPosition = position;
            }

            if (fillRenderer != null)
            {
                fillRenderer.color = Color.Lerp(
                    new Color(0.92f, 0.2f, 0.18f, 1f),
                    new Color(0.25f, 0.9f, 0.38f, 1f),
                    ratio);
            }

            if (valueText != null)
            {
                valueText.text = currentHealth + " / " + maxHealth;
            }
        }
    }
}
