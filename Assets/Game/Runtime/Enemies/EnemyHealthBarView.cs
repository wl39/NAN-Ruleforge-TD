using UnityEngine;
using RuleforgeTD.GameLogic.Content;

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
        [SerializeField]
        private EnemyHealthBarVisualSettings visualSettings;
        private Transform shieldRoot;
        private Transform shieldFillTransform;
        private SpriteRenderer shieldFillRenderer;
        private SpriteRenderer shieldBackgroundRenderer;
        private bool usesEliteHealthColor;
        private Color eliteHealthColor;
        private Color shieldColor = new Color32(66, 210, 200, 255);

        public string DisplayedValue => string.Empty;
        public float FullWidth => ResolveFullWidth();
        public float BarLocalY =>
            fillTransform != null &&
            fillTransform.parent != null
                ? fillTransform.parent.localPosition.y
                : 0f;
        public EnemyHealthBarVisualSettings VisualSettings =>
            visualSettings;
        public bool ValueVisible =>
            valueText != null &&
                valueText.gameObject.activeSelf;
        public bool ShieldVisible =>
            shieldRoot != null &&
            shieldRoot.gameObject.activeSelf;

        private void Awake()
        {
            if (health == null)
            {
                health = GetComponent<EnemyHealth>();
            }

            ApplyVisualSettings();
            HideValueText();
        }

        private void OnEnable()
        {
            ApplyVisualSettings();
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
            EnemyHealthBarVisualSettings settings)
        {
            health = targetHealth;
            fillTransform = targetFill;
            fillRenderer = targetFillRenderer;
            valueText = targetValueText;
            visualSettings = settings;
            ApplyVisualSettings();
            HideValueText();
            Refresh(health.CurrentHealth, health.MaxHealth);
        }

        public void ConfigureElitePresentation(
            CompiledEliteTraitDefinition trait)
        {
            usesEliteHealthColor = trait != null;
            eliteHealthColor = trait == null
                ? Color.white
                : EliteEnemyVisualView.ParseColor(
                    trait.HealthBarColor,
                    new Color32(120, 146, 178, 255));
            shieldColor = trait == null
                ? new Color32(66, 210, 200, 255)
                : EliteEnemyVisualView.ParseColor(
                    trait.ShieldBarColor,
                    new Color32(66, 210, 200, 255));
            if (health != null)
            {
                Refresh(health.CurrentHealth, health.MaxHealth);
            }
        }

        public void ApplyShield(long shieldMilli, long maximumHealthMilli)
        {
            if (shieldMilli <= 0L || maximumHealthMilli <= 0L)
            {
                if (shieldRoot != null)
                {
                    shieldRoot.gameObject.SetActive(false);
                }
                return;
            }

            EnsureShieldBar();
            if (shieldRoot == null ||
                shieldFillTransform == null ||
                shieldFillRenderer == null)
            {
                return;
            }
            float ratio = Mathf.Clamp01(
                (float)shieldMilli / maximumHealthMilli);
            float fullWidth = ResolveFullWidth();
            float fillWidth = fullWidth * ratio;
            Vector3 scale = shieldFillTransform.localScale;
            scale.x = fillWidth;
            shieldFillTransform.localScale = scale;
            Vector3 position = shieldFillTransform.localPosition;
            position.x = -fullWidth * 0.5f + fillWidth * 0.5f;
            shieldFillTransform.localPosition = position;
            shieldFillRenderer.color = shieldColor;
            shieldRoot.gameObject.SetActive(true);
        }

        /// <summary>
        /// Returns the health bar's top edge in this enemy root's local space.
        /// Overlay views can consume this value without depending on prefab
        /// names or duplicating the health bar's authored dimensions.
        /// </summary>
        public bool TryGetVisualTopLocalY(out float topLocalY)
        {
            topLocalY = 0f;
            Transform barRoot =
                fillTransform != null
                    ? fillTransform.parent
                    : null;
            if (barRoot == null)
            {
                return false;
            }

            SpriteRenderer[] renderers =
                barRoot.GetComponentsInChildren<SpriteRenderer>(
                    true);
            bool foundRenderer = false;
            for (int index = 0;
                 index < renderers.Length;
                 index++)
            {
                SpriteRenderer renderer = renderers[index];
                if (renderer == null ||
                    renderer.sprite == null)
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;
                Vector3 worldTop = new Vector3(
                    bounds.center.x,
                    bounds.max.y,
                    bounds.center.z);
                float candidate =
                    transform.InverseTransformPoint(
                        worldTop).y;
                if (!foundRenderer ||
                    candidate > topLocalY)
                {
                    topLocalY = candidate;
                    foundRenderer = true;
                }
            }

            if (!foundRenderer)
            {
                topLocalY = barRoot.localPosition.y;
            }

            return true;
        }

        private void Refresh(int currentHealth, int maxHealth)
        {
            float ratio = maxHealth <= 0 ? 0f : Mathf.Clamp01((float)currentHealth / maxHealth);
            float fullWidth = ResolveFullWidth();
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
                fillRenderer.color = usesEliteHealthColor
                    ? eliteHealthColor
                    : Color.Lerp(
                        new Color(0.92f, 0.2f, 0.18f, 1f),
                        new Color(0.25f, 0.9f, 0.38f, 1f),
                        ratio);
            }

            HideValueText();
        }

        private void ApplyVisualSettings()
        {
            if (visualSettings == null ||
                fillTransform == null)
            {
                return;
            }

            Transform barRoot = fillTransform.parent;
            if (barRoot != null)
            {
                Vector3 rootPosition =
                    barRoot.localPosition;
                rootPosition.y = visualSettings.LocalY;
                barRoot.localPosition = rootPosition;

                Transform background =
                    ResolveBackgroundTransform(barRoot);
                if (background != null)
                {
                    Vector3 backgroundScale =
                        background.localScale;
                    backgroundScale.x =
                        visualSettings.BackgroundWidth;
                    backgroundScale.y =
                        visualSettings.BackgroundHeight;
                    background.localScale =
                        backgroundScale;
                }
            }

            Vector3 fillScale = fillTransform.localScale;
            fillScale.x = visualSettings.FillWidth;
            fillScale.y = visualSettings.FillHeight;
            fillTransform.localScale = fillScale;
        }

        private float ResolveFullWidth()
        {
            if (visualSettings != null)
            {
                return visualSettings.FillWidth;
            }

            return fillTransform == null
                ? 0f
                : Mathf.Abs(fillTransform.localScale.x);
        }

        private Transform ResolveBackgroundTransform(
            Transform barRoot)
        {
            if (barRoot == null)
            {
                return null;
            }

            SpriteRenderer[] renderers =
                barRoot.GetComponentsInChildren<
                    SpriteRenderer>(true);
            for (int index = 0;
                 index < renderers.Length;
                 index++)
            {
                SpriteRenderer renderer = renderers[index];
                if (renderer != null &&
                    renderer != fillRenderer)
                {
                    return renderer.transform;
                }
            }

            return null;
        }

        private void HideValueText()
        {
            if (valueText == null)
            {
                return;
            }

            valueText.text = string.Empty;
            valueText.gameObject.SetActive(false);
        }

        private void EnsureShieldBar()
        {
            if (shieldRoot != null ||
                fillTransform == null ||
                fillRenderer == null)
            {
                return;
            }

            Transform healthRoot = fillTransform.parent;
            if (healthRoot == null)
            {
                return;
            }

            var root = new GameObject("Shield Bar");
            root.transform.SetParent(healthRoot, false);
            root.transform.localPosition =
                new Vector3(0f, -0.13f, 0f);
            shieldRoot = root.transform;

            var background = new GameObject("Background");
            background.transform.SetParent(shieldRoot, false);
            shieldBackgroundRenderer =
                background.AddComponent<SpriteRenderer>();
            shieldBackgroundRenderer.sprite = fillRenderer.sprite;
            shieldBackgroundRenderer.color =
                new Color32(16, 50, 58, 240);
            shieldBackgroundRenderer.sortingLayerID =
                fillRenderer.sortingLayerID;
            shieldBackgroundRenderer.sortingOrder =
                fillRenderer.sortingOrder;
            background.transform.localScale = new Vector3(
                ResolveFullWidth(),
                Mathf.Max(0.02f, fillTransform.localScale.y * 0.7f),
                1f);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(shieldRoot, false);
            shieldFillTransform = fill.transform;
            shieldFillRenderer = fill.AddComponent<SpriteRenderer>();
            shieldFillRenderer.sprite = fillRenderer.sprite;
            shieldFillRenderer.color = shieldColor;
            shieldFillRenderer.sortingLayerID =
                fillRenderer.sortingLayerID;
            shieldFillRenderer.sortingOrder =
                fillRenderer.sortingOrder + 1;
            shieldFillTransform.localScale = new Vector3(
                ResolveFullWidth(),
                Mathf.Max(0.02f, fillTransform.localScale.y * 0.7f),
                1f);
        }
    }
}
