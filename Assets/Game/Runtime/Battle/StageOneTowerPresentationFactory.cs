using System;
using RuleforgeTD.Rendering;
using UnityEngine;

namespace RuleforgeTD.Battle
{
    /// <summary>
    /// 타워 정의 ID를 Unity 표현으로 바꾸는 단일 경계다.
    /// 전용 프리팹이 없으면 카탈로그의 기본 prototype과 외형 메타데이터를 사용한다.
    /// </summary>
    internal static class StageOneTowerPresentationFactory
    {
        public static GameObject Create(
            StageOnePresentationCatalog catalog,
            string definitionId,
            int level,
            Transform parent)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (catalog.TryGetTower(
                    definitionId,
                    level,
                    out GameObject prefab,
                    out float scale))
            {
                return Instantiate(prefab, scale, parent);
            }

            if (catalog.TryGetTower(
                    catalog.DefaultTowerId,
                    level,
                    out prefab,
                    out scale))
            {
                GameObject prototype =
                    Instantiate(prefab, scale, parent);
                ApplyTint(
                    prototype,
                    catalog.GetTowerPrototypeTint(definitionId));
                return prototype;
            }

            var missing =
                new GameObject("Missing Tower " + definitionId);
            missing.transform.SetParent(parent, false);
            return missing;
        }

        private static GameObject Instantiate(
            GameObject prefab,
            float scale,
            Transform parent)
        {
            GameObject instance =
                UnityEngine.Object.Instantiate(prefab, parent);
            instance.transform.localScale *= scale;
            WorldSortingLayers.ApplyToHierarchy(
                instance.transform,
                WorldSortingLayers.Tower);
            return instance;
        }

        private static void ApplyTint(
            GameObject instance,
            Color tint)
        {
            SpriteRenderer[] renderers =
                instance.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Color source = renderers[i].color;
                renderers[i].color = new Color(
                    source.r * tint.r,
                    source.g * tint.g,
                    source.b * tint.b,
                    source.a);
            }
        }
    }
}
