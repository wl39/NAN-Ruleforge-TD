using System;
using UnityEngine;

namespace RuleforgeTD.Battle
{
    [Serializable]
    public sealed class StageOnePrefabBinding
    {
        [SerializeField]
        private string definitionId = string.Empty;

        [SerializeField]
        private GameObject prefab;

        [SerializeField, Min(0.1f)]
        private float scaleMultiplier = 1f;

        [SerializeField, Range(1, 7)]
        private int visualLevel = 1;

        public string DefinitionId => definitionId;
        public GameObject Prefab => prefab;
        public float ScaleMultiplier => scaleMultiplier;
        public int VisualLevel => visualLevel;

        public StageOnePrefabBinding(
            string stableId,
            GameObject sourcePrefab,
            float visualScale = 1f,
            int towerVisualLevel = 1)
        {
            definitionId = stableId ?? string.Empty;
            prefab = sourcePrefab;
            scaleMultiplier = Mathf.Max(0.1f, visualScale);
            visualLevel = Mathf.Clamp(towerVisualLevel, 1, 7);
        }
    }

    /// <summary>
    /// Stage01 presentation assets keyed by stable simulation definition IDs.
    /// Combat rules never read this catalog.
    /// </summary>
    [CreateAssetMenu(
        fileName = "StageOnePresentationCatalog",
        menuName = "Ruleforge TD/Stage One Presentation Catalog")]
    public sealed class StageOnePresentationCatalog : ScriptableObject
    {
        [SerializeField]
        private TextAsset contentJson;

        [SerializeField]
        private TextAsset localizationJson;

        [SerializeField]
        private Font uiFont;

        [SerializeField]
        private StageOnePrefabBinding[] towerPrefabs =
            Array.Empty<StageOnePrefabBinding>();

        [SerializeField]
        private StageOnePrefabBinding[] enemyPrefabs =
            Array.Empty<StageOnePrefabBinding>();

        [SerializeField]
        private Sprite[] projectileDirectionSprites =
            Array.Empty<Sprite>();

        [SerializeField]
        private string[] defaultCardProgram =
            Array.Empty<string>();

        public TextAsset ContentJson => contentJson;
        public TextAsset LocalizationJson => localizationJson;
        public Font UiFont => uiFont;
        public int TowerBindingCount =>
            towerPrefabs == null ? 0 : towerPrefabs.Length;
        public int EnemyBindingCount =>
            enemyPrefabs == null ? 0 : enemyPrefabs.Length;
        public int ProjectileDirectionCount =>
            projectileDirectionSprites == null
                ? 0
                : projectileDirectionSprites.Length;
        public int DefaultCardProgramCount =>
            defaultCardProgram == null
                ? 0
                : defaultCardProgram.Length;
        public string DefaultTowerId =>
            towerPrefabs != null &&
            towerPrefabs.Length > 0 &&
            towerPrefabs[0] != null
                ? towerPrefabs[0].DefinitionId
                : string.Empty;

        public bool TryGetTower(
            string definitionId,
            out GameObject prefab,
            out float scaleMultiplier)
        {
            return TryGetTower(
                definitionId,
                1,
                out prefab,
                out scaleMultiplier);
        }

        public bool TryGetTower(
            string definitionId,
            int level,
            out GameObject prefab,
            out float scaleMultiplier)
        {
            return TryGetBinding(
                towerPrefabs,
                definitionId,
                Mathf.Clamp(level, 1, 7),
                out prefab,
                out scaleMultiplier);
        }

        public bool TryGetEnemy(
            string definitionId,
            out GameObject prefab,
            out float scaleMultiplier)
        {
            return TryGetBinding(
                enemyPrefabs,
                definitionId,
                1,
                out prefab,
                out scaleMultiplier);
        }

        public Sprite GetProjectileDirectionSprite(int index)
        {
            if (projectileDirectionSprites == null ||
                index < 0 ||
                index >= projectileDirectionSprites.Length)
            {
                return null;
            }

            return projectileDirectionSprites[index];
        }

        public string GetDefaultCardId(int index)
        {
            if (defaultCardProgram == null ||
                index < 0 ||
                index >= defaultCardProgram.Length)
            {
                return string.Empty;
            }

            return defaultCardProgram[index] ?? string.Empty;
        }

        public void ConfigureAuthoring(
            TextAsset sourceContent,
            TextAsset sourceLocalization,
            Font sourceUiFont,
            StageOnePrefabBinding[] towers,
            StageOnePrefabBinding[] enemies,
            Sprite[] projectileSprites,
            string[] initialCardProgram)
        {
            contentJson = sourceContent;
            localizationJson = sourceLocalization;
            uiFont = sourceUiFont;
            towerPrefabs = CloneBindings(towers);
            enemyPrefabs = CloneBindings(enemies);
            projectileDirectionSprites = projectileSprites == null
                ? Array.Empty<Sprite>()
                : (Sprite[])projectileSprites.Clone();
            defaultCardProgram = initialCardProgram == null
                ? Array.Empty<string>()
                : (string[])initialCardProgram.Clone();
        }

        private static bool TryGetBinding(
            StageOnePrefabBinding[] bindings,
            string definitionId,
            int visualLevel,
            out GameObject prefab,
            out float scaleMultiplier)
        {
            if (bindings != null)
            {
                for (int i = 0; i < bindings.Length; i++)
                {
                    StageOnePrefabBinding binding = bindings[i];
                    if (binding != null &&
                        string.Equals(
                            binding.DefinitionId,
                            definitionId,
                            StringComparison.Ordinal) &&
                        binding.VisualLevel == visualLevel)
                    {
                        prefab = binding.Prefab;
                        scaleMultiplier =
                            binding.ScaleMultiplier;
                        return prefab != null;
                    }
                }
            }

            prefab = null;
            scaleMultiplier = 1f;
            return false;
        }

        private static StageOnePrefabBinding[] CloneBindings(
            StageOnePrefabBinding[] bindings)
        {
            return bindings == null
                ? Array.Empty<StageOnePrefabBinding>()
                : (StageOnePrefabBinding[])bindings.Clone();
        }
    }
}
