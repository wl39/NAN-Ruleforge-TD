using RuleforgeTD.GameLogic.Content;
using UnityEngine;

namespace RuleforgeTD.Enemies
{
    /// <summary>
    /// Applies an elite palette, outline, and overhead text badge to the existing
    /// enemy renderer. It never swaps or duplicates directional animation assets.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EliteEnemyVisualView : MonoBehaviour
    {
        private static readonly Vector2[] OutlineDirections =
        {
            Vector2.left,
            Vector2.right,
            Vector2.up,
            Vector2.down
        };

        private SpriteRenderer targetRenderer;
        private SpriteRenderer[] outlineRenderers;
        private TextMesh traitBadge;
        private MeshRenderer traitBadgeRenderer;
        private Color bodyTint = Color.white;
        private Color outlineColor = Color.black;
        private float outlineWidth = 0.035f;
        private bool activeTrait;

        public bool HasEliteTrait => activeTrait;
        public string BadgeText => traitBadge == null
            ? string.Empty
            : traitBadge.text;
        public Color BodyTint => bodyTint;

        private void LateUpdate()
        {
            SynchronizeRenderers();
        }

        public void Configure(
            SpriteRenderer enemyRenderer,
            EnemyHealthBarView healthBar,
            CompiledEliteTraitDefinition trait,
            Font font)
        {
            targetRenderer = enemyRenderer;
            activeTrait = trait != null && targetRenderer != null;
            EnsureVisuals(font);

            if (!activeTrait)
            {
                ResetVisuals();
                return;
            }

            bodyTint = ParseColor(trait.BodyTint, Color.white);
            outlineColor = ParseColor(
                trait.OutlineColor,
                new Color32(17, 25, 34, 255));
            outlineWidth = Mathf.Clamp(
                trait.OutlineWidthMilli / 1000f,
                0.01f,
                0.25f);
            targetRenderer.color = bodyTint;

            for (int i = 0; i < outlineRenderers.Length; i++)
            {
                SpriteRenderer outline = outlineRenderers[i];
                outline.color = outlineColor;
                outline.transform.localPosition =
                    (Vector3)(OutlineDirections[i] * outlineWidth);
                outline.enabled = true;
            }

            traitBadge.text = trait.IconText;
            traitBadge.color = Color.white;
            traitBadge.gameObject.SetActive(true);
            float badgeY = 1.2f;
            if (healthBar != null &&
                healthBar.TryGetVisualTopLocalY(out float topY))
            {
                badgeY = topY + 0.14f;
            }
            traitBadge.transform.localPosition =
                new Vector3(-0.48f, badgeY, -0.03f);
            SynchronizeRenderers();
        }

        public void ResetVisuals()
        {
            activeTrait = false;
            bodyTint = Color.white;
            if (targetRenderer != null)
            {
                targetRenderer.color = Color.white;
            }

            if (outlineRenderers != null)
            {
                for (int i = 0; i < outlineRenderers.Length; i++)
                {
                    if (outlineRenderers[i] != null)
                    {
                        outlineRenderers[i].enabled = false;
                    }
                }
            }

            if (traitBadge != null)
            {
                traitBadge.text = string.Empty;
                traitBadge.gameObject.SetActive(false);
            }
        }

        private void EnsureVisuals(Font font)
        {
            if (outlineRenderers == null ||
                outlineRenderers.Length != OutlineDirections.Length)
            {
                outlineRenderers =
                    new SpriteRenderer[OutlineDirections.Length];
                for (int i = 0; i < outlineRenderers.Length; i++)
                {
                    var host = new GameObject(
                        "Elite Outline " + i);
                    host.transform.SetParent(transform, false);
                    outlineRenderers[i] =
                        host.AddComponent<SpriteRenderer>();
                    outlineRenderers[i].enabled = false;
                }
            }

            if (traitBadge == null)
            {
                var host = new GameObject("Elite Trait Badge");
                host.transform.SetParent(transform, false);
                traitBadge = host.AddComponent<TextMesh>();
                traitBadge.anchor = TextAnchor.MiddleCenter;
                traitBadge.alignment = TextAlignment.Center;
                traitBadge.fontSize = 32;
                traitBadge.characterSize = 0.025f;
                traitBadge.fontStyle = FontStyle.Bold;
                traitBadgeRenderer =
                    host.GetComponent<MeshRenderer>();
            }

            if (font != null)
            {
                traitBadge.font = font;
                if (traitBadgeRenderer != null)
                {
                    traitBadgeRenderer.sharedMaterial = font.material;
                }
            }
        }

        private void SynchronizeRenderers()
        {
            if (!activeTrait || targetRenderer == null)
            {
                return;
            }

            for (int i = 0; i < outlineRenderers.Length; i++)
            {
                SpriteRenderer outline = outlineRenderers[i];
                outline.sprite = targetRenderer.sprite;
                outline.flipX = targetRenderer.flipX;
                outline.flipY = targetRenderer.flipY;
                outline.sortingLayerID =
                    targetRenderer.sortingLayerID;
                outline.sortingOrder =
                    targetRenderer.sortingOrder - 1;
                outline.sharedMaterial =
                    targetRenderer.sharedMaterial;
            }

            if (traitBadgeRenderer != null)
            {
                traitBadgeRenderer.sortingLayerID =
                    targetRenderer.sortingLayerID;
                traitBadgeRenderer.sortingOrder =
                    targetRenderer.sortingOrder + 20;
            }
        }

        public static Color ParseColor(string value, Color fallback)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   ColorUtility.TryParseHtmlString(
                       value,
                       out Color parsed)
                ? parsed
                : fallback;
        }
    }
}
