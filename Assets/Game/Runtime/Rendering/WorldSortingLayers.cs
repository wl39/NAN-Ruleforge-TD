using UnityEngine;

namespace RuleforgeTD.Rendering
{
    /// <summary>
    /// 월드 표현의 Sorting Layer 이름과 적용 경계를 한곳에서 관리한다.
    /// 개별 Renderer의 sortingOrder는 같은 역할 안의 세부 순서에만 쓴다.
    /// </summary>
    public static class WorldSortingLayers
    {
        public const string Route = "Route";
        public const string Tower = "Tower";
        public const string Enemy = "Enemy";
        public const string Object = "Object";
        public const string Effects = "Effects";

        public static void Apply(Renderer renderer, string layerName)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.sortingLayerName = layerName;
        }

        public static void ApplyToHierarchy(
            Transform root,
            string layerName)
        {
            if (root == null)
            {
                return;
            }

            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Apply(renderers[i], layerName);
            }
        }
    }
}
