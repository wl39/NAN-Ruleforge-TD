using System;
using RuleforgeTD.UI;
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

        [SerializeField, Min(1)]
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
            visualLevel = Mathf.Max(1, towerVisualLevel);
        }
    }

    /// <summary>
    /// 전용 프리팹이 아직 없는 타워가 공용 prototype을 사용할 때의
    /// 비권위 표현 설정이다. 안정 ID별 외형 분기를 전투 컨트롤러에 두지 않는다.
    /// </summary>
    [Serializable]
    public sealed class StageOneTowerAppearanceBinding
    {
        [SerializeField]
        private string definitionId = string.Empty;

        [SerializeField]
        private Color prototypeTint = Color.white;

        public string DefinitionId => definitionId;
        public Color PrototypeTint => prototypeTint;

        public StageOneTowerAppearanceBinding(
            string stableId,
            Color tint)
        {
            definitionId = stableId ?? string.Empty;
            prototypeTint = tint;
        }
    }

    /// <summary>
    /// 모든 전투 스테이지가 사용하는 표현 에셋 계약이다. 타입 이름은 기존
    /// 에셋 직렬화 호환을 위해 유지하며, 전투 규칙은 이 카탈로그를 읽지 않는다.
    /// </summary>
    [CreateAssetMenu(
        fileName = "BattlePresentationCatalog",
        menuName = "Ruleforge TD/Battle Presentation Catalog")]
    public sealed class StageOnePresentationCatalog : ScriptableObject,
        IEnemyPreviewSpriteProvider
    {
        [SerializeField]
        private TextAsset contentJson;

        [SerializeField]
        private TextAsset localizationJson;

        [SerializeField]
        private TextAsset[] cardContentModules =
            Array.Empty<TextAsset>();

        [SerializeField]
        private Font uiFont;

        [SerializeField]
        private StageOnePrefabBinding[] towerPrefabs =
            Array.Empty<StageOnePrefabBinding>();

        [SerializeField]
        private StageOneTowerAppearanceBinding[] towerAppearances =
            Array.Empty<StageOneTowerAppearanceBinding>();

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
        public TextAsset[] CardContentModules =>
            cardContentModules == null
                ? Array.Empty<TextAsset>()
                : (TextAsset[])cardContentModules.Clone();
        public int CardContentModuleCount =>
            cardContentModules == null
                ? 0
                : cardContentModules.Length;
        public Font UiFont => uiFont;
        public int TowerBindingCount =>
            towerPrefabs == null ? 0 : towerPrefabs.Length;
        public int TowerAppearanceBindingCount =>
            towerAppearances == null ? 0 : towerAppearances.Length;
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
            return TryGetBestTowerBinding(
                towerPrefabs,
                definitionId,
                Mathf.Max(1, level),
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

        /// <summary>
        /// 웨이브 예고에서 사용할 적 대표 이미지를 프리팹의 렌더러에서 찾는다.
        /// 별도 아이콘 테이블을 중복 관리하지 않아 실제 전투 외형과 예고가 함께 바뀐다.
        /// </summary>
        public bool TryGetEnemyPreviewSprite(
            string definitionId,
            out Sprite sprite)
        {
            sprite = null;
            if (!TryGetEnemy(
                    definitionId,
                    out GameObject prefab,
                    out _))
            {
                return false;
            }

            SpriteRenderer[] renderers =
                prefab.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null &&
                    renderers[i].sprite != null)
                {
                    sprite = renderers[i].sprite;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 전투 프리팹의 걷기 컨트롤러를 그대로 공유해 예고 UI와 실제 적의
        /// 움직임이 서로 다른 에셋으로 어긋나지 않게 한다.
        /// </summary>
        public bool TryGetEnemyPreviewAnimatorController(
            string definitionId,
            out RuntimeAnimatorController controller)
        {
            controller = null;
            if (!TryGetEnemy(
                    definitionId,
                    out GameObject prefab,
                    out _))
            {
                return false;
            }

            Animator animator =
                prefab.GetComponentInChildren<Animator>(true);
            if (animator == null ||
                animator.runtimeAnimatorController == null)
            {
                return false;
            }

            controller = animator.runtimeAnimatorController;
            return true;
        }

        /// <summary>
        /// 예고 UI도 전투 프리팹의 authored 크기 차이를 사용한다. 같은 고블린
        /// 애니메이션을 공유하는 중갑형/보스도 실제 전투와 같은 체급으로 보인다.
        /// </summary>
        public bool TryGetEnemyPreviewScaleMultiplier(
            string definitionId,
            out float scaleMultiplier)
        {
            return TryGetEnemy(
                definitionId,
                out _,
                out scaleMultiplier);
        }

        public Color GetTowerPrototypeTint(string definitionId)
        {
            if (towerAppearances != null)
            {
                for (int i = 0; i < towerAppearances.Length; i++)
                {
                    StageOneTowerAppearanceBinding binding =
                        towerAppearances[i];
                    if (binding != null &&
                        string.Equals(
                            binding.DefinitionId,
                            definitionId,
                            StringComparison.Ordinal))
                    {
                        return binding.PrototypeTint;
                    }
                }
            }

            return Color.white;
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
            string[] initialCardProgram,
            StageOneTowerAppearanceBinding[] appearances = null,
            TextAsset[] cardModules = null)
        {
            contentJson = sourceContent;
            localizationJson = sourceLocalization;
            cardContentModules = CloneTextAssets(cardModules);
            uiFont = sourceUiFont;
            towerPrefabs = CloneBindings(towers);
            towerAppearances = appearances == null
                ? Array.Empty<StageOneTowerAppearanceBinding>()
                : (StageOneTowerAppearanceBinding[])appearances.Clone();
            enemyPrefabs = CloneBindings(enemies);
            projectileDirectionSprites = projectileSprites == null
                ? Array.Empty<Sprite>()
                : (Sprite[])projectileSprites.Clone();
            defaultCardProgram = initialCardProgram == null
                ? Array.Empty<string>()
                : (string[])initialCardProgram.Clone();
        }

        /// <summary>
        /// Editor discovery가 카드 모듈 목록만 갱신할 때 사용한다. 배열은
        /// 방어 복사하며, 실제 직렬화 변경 여부를 반환해 불필요한 asset
        /// dirty/save와 import 재진입을 피한다.
        /// </summary>
        public bool ConfigureCardContentModules(
            TextAsset[] cardModules)
        {
            TextAsset[] next = CloneTextAssets(cardModules);
            if (AreSameAssets(cardContentModules, next))
            {
                return false;
            }

            cardContentModules = next;
            return true;
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

        /// <summary>
        /// 요청 레벨의 전용 프리팹이 없으면 같은 타워에서 가장 가까운 이전
        /// authored 레벨을 사용한다. 게임플레이 MaxLevel은 콘텐츠가 정하고,
        /// 프레젠테이션 에셋 수가 진행 상한이 되지 않게 한다.
        /// </summary>
        private static bool TryGetBestTowerBinding(
            StageOnePrefabBinding[] bindings,
            string definitionId,
            int visualLevel,
            out GameObject prefab,
            out float scaleMultiplier)
        {
            StageOnePrefabBinding best = null;
            if (bindings != null)
            {
                for (int i = 0; i < bindings.Length; i++)
                {
                    StageOnePrefabBinding candidate =
                        bindings[i];
                    if (candidate == null ||
                        candidate.Prefab == null ||
                        !string.Equals(
                            candidate.DefinitionId,
                            definitionId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (candidate.VisualLevel == visualLevel)
                    {
                        best = candidate;
                        break;
                    }

                    if (candidate.VisualLevel < visualLevel &&
                        (best == null ||
                         best.VisualLevel > visualLevel ||
                         candidate.VisualLevel >
                            best.VisualLevel))
                    {
                        best = candidate;
                        continue;
                    }

                    if (best == null ||
                        (best.VisualLevel > visualLevel &&
                         candidate.VisualLevel <
                            best.VisualLevel))
                    {
                        best = candidate;
                    }
                }
            }

            prefab = best == null ? null : best.Prefab;
            scaleMultiplier = best == null
                ? 1f
                : best.ScaleMultiplier;
            return prefab != null;
        }

        private static StageOnePrefabBinding[] CloneBindings(
            StageOnePrefabBinding[] bindings)
        {
            return bindings == null
                ? Array.Empty<StageOnePrefabBinding>()
                : (StageOnePrefabBinding[])bindings.Clone();
        }

        private static TextAsset[] CloneTextAssets(
            TextAsset[] assets)
        {
            return assets == null
                ? Array.Empty<TextAsset>()
                : (TextAsset[])assets.Clone();
        }

        private static bool AreSameAssets(
            TextAsset[] left,
            TextAsset[] right)
        {
            int leftLength = left == null ? 0 : left.Length;
            int rightLength = right == null ? 0 : right.Length;
            if (leftLength != rightLength)
            {
                return false;
            }

            for (int i = 0; i < leftLength; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
