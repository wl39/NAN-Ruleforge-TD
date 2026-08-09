using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Simulation;
using UnityEngine;

namespace RuleforgeTD.Battle
{
    /// <summary>
    /// 권위 상태 스냅샷을 머리 위의 정적인 사각 아이콘 격자로 표현한다.
    /// 전투 상태를 변경하지 않으며 카드 VFX 색상은 공용 팔레트에서만 가져온다.
    /// 같은 표시 정체성(예: 냉기/빙결)은 아이콘 하나로 합치고
    /// 실제 중첩 수를 아이콘 안에 표시한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageOneEnemyStatusIconStackView :
        MonoBehaviour
    {
        public const int IconsPerRow = 3;
        public const float IconSize = 0.18f;
        public const float IconSpacing = 0.205f;
        public const float HealthBarClearance = 0.025f;
        public const float FallbackHeadClearance = 0.05f;
        public const int MaximumDisplayedStackCount = 9;
        public const float StackLabelCharacterSize = 0.03f;
        public const int StackLabelFontSize = 36;

        private const int IconSortingOffset = 8;
        private const string StackRootName =
            "Enemy Status Icon Stack";

        [SerializeField]
        private SpriteRenderer targetRenderer;

        private readonly List<IconSlot> slots =
            new List<IconSlot>(8);
        private readonly string[] activeEffectIds =
            new string[32];
        private readonly int[] activeStackCounts =
            new int[32];
        private readonly int[] activePriorities =
            new int[32];
        private Transform stackRoot;
        private int activeIconCount;
        private float preferredHealthBarTopLocalY =
            float.NaN;
        private int cachedSortingLayerId = int.MinValue;
        private int cachedSortingOrder = int.MinValue;

        public int ActiveIconCount => activeIconCount;
        public int ActiveRowCount =>
            activeIconCount <= 0
                ? 0
                : (activeIconCount + IconsPerRow - 1) /
                  IconsPerRow;

        private void Awake()
        {
            CacheRenderer();
            EnsureStackRoot();
        }

        private void OnEnable()
        {
            CacheRenderer();
            EnsureStackRoot();
            SynchronizeSorting();
        }

        private void LateUpdate()
        {
            SynchronizeSorting();
            LayoutActiveIcons();
        }

        private void OnDisable()
        {
            ResetVisuals();
        }

        public void Configure(
            SpriteRenderer enemyRenderer,
            float healthBarTopLocalY = float.NaN)
        {
            targetRenderer =
                enemyRenderer != null
                    ? enemyRenderer
                    : GetComponent<SpriteRenderer>();
            preferredHealthBarTopLocalY =
                healthBarTopLocalY;
            cachedSortingLayerId = int.MinValue;
            cachedSortingOrder = int.MinValue;
            EnsureStackRoot();
            SynchronizeSorting();
            LayoutActiveIcons();
        }

        public static bool HasVisibleDebuff(
            StatusSnapshot[] statuses)
        {
            if (statuses == null)
            {
                return false;
            }

            for (int index = 0;
                 index < statuses.Length;
                 index++)
            {
                StatusSnapshot status = statuses[index];
                if (status.Stacks > 0 &&
                    status.RemainingTicks > 0 &&
                    StageOneStatusEffectVisualCatalog.TryGet(
                        status.Type,
                        out var definition) &&
                    definition.ShowDebuffIcon)
                {
                    return true;
                }
            }

            return false;
        }

        public void ApplySnapshot(in EnemySnapshot snapshot)
        {
            if (!snapshot.Alive)
            {
                ResetVisuals();
                return;
            }

            ApplyStatuses(snapshot.StatusDetails);
        }

        public void ApplyStatuses(StatusSnapshot[] statuses)
        {
            Array.Clear(
                activeEffectIds,
                0,
                activeEffectIds.Length);
            Array.Clear(
                activeStackCounts,
                0,
                activeStackCounts.Length);
            Array.Clear(
                activePriorities,
                0,
                activePriorities.Length);
            int nextCount = 0;
            if (statuses != null)
            {
                for (int index = 0;
                     index < statuses.Length;
                     index++)
                {
                    StatusSnapshot status = statuses[index];
                    if (status.Stacks <= 0 ||
                        status.RemainingTicks <= 0 ||
                        !StageOneStatusEffectVisualCatalog.TryGet(
                            status.Type,
                            out var definition) ||
                        !definition.ShowDebuffIcon)
                    {
                        continue;
                    }

                    int existingIndex =
                        FindEffectId(
                            definition.EffectId,
                            nextCount);
                    if (existingIndex >= 0)
                    {
                        activeStackCounts[existingIndex] =
                            CombineStackCounts(
                                activeStackCounts[
                                    existingIndex],
                                status.Stacks);
                        activePriorities[existingIndex] =
                            Math.Max(
                                activePriorities[existingIndex],
                                GetDisplayPriority(status.Type));
                        continue;
                    }

                    if (nextCount >= activeEffectIds.Length)
                    {
                        break;
                    }

                    activeEffectIds[nextCount] =
                        definition.EffectId;
                    activeStackCounts[nextCount] =
                        CombineStackCounts(
                            0,
                            status.Stacks);
                    activePriorities[nextCount] =
                        GetDisplayPriority(status.Type);
                    nextCount++;
                }
            }

            // 대규모 전투에서는 제어·처형 보조처럼 즉시 판단이 필요한 상태를
            // 먼저 두고 핵심 3개만 표시한다. 전체 상태는 적 상세 정보가 권위다.
            SortByDisplayPriority(nextCount);
            nextCount = Math.Min(IconsPerRow, nextCount);

            EnsureSlotCount(nextCount);
            activeIconCount = nextCount;
            for (int index = 0;
                 index < slots.Count;
                 index++)
            {
                bool active = index < activeIconCount;
                slots[index].Root.SetActive(active);
                if (!active)
                {
                    continue;
                }

                string effectId = activeEffectIds[index];
                slots[index].EffectId = effectId;
                slots[index].DisplayedStackCount =
                    activeStackCounts[index];
                string stackLabel =
                    FormatStackLabel(
                        activeStackCounts[index]);
                slots[index].StackText.text =
                    stackLabel;
                slots[index].StackText.gameObject
                    .SetActive(
                        !string.IsNullOrEmpty(
                            stackLabel));
                if (StageOneCardEffectPalette.TryGetStyle(
                        effectId,
                        out StageOneCardEffectStyle style))
                {
                    ApplyStyle(slots[index], style);
                }
            }

            LayoutActiveIcons();
            SynchronizeSorting();
        }

        public void ResetVisuals()
        {
            activeIconCount = 0;
            Array.Clear(
                activeEffectIds,
                0,
                activeEffectIds.Length);
            Array.Clear(
                activeStackCounts,
                0,
                activeStackCounts.Length);
            for (int index = 0;
                 index < slots.Count;
                 index++)
            {
                slots[index].EffectId = string.Empty;
                slots[index].DisplayedStackCount = 0;
                slots[index].StackText.text =
                    string.Empty;
                slots[index].StackText.gameObject
                    .SetActive(false);
                slots[index].Root.SetActive(false);
            }
        }

        private void SortByDisplayPriority(int count)
        {
            for (int index = 1; index < count; index++)
            {
                string effectId = activeEffectIds[index];
                int stackCount = activeStackCounts[index];
                int priority = activePriorities[index];
                int insertion = index;
                while (insertion > 0 &&
                       activePriorities[insertion - 1] < priority)
                {
                    activeEffectIds[insertion] =
                        activeEffectIds[insertion - 1];
                    activeStackCounts[insertion] =
                        activeStackCounts[insertion - 1];
                    activePriorities[insertion] =
                        activePriorities[insertion - 1];
                    insertion--;
                }

                activeEffectIds[insertion] = effectId;
                activeStackCounts[insertion] = stackCount;
                activePriorities[insertion] = priority;
            }
        }

        private static int GetDisplayPriority(StatusType type)
        {
            switch (type)
            {
                case StatusType.Stun:
                    return 100;
                case StatusType.Frozen:
                case StatusType.Chill:
                    return 95;
                case StatusType.Bind:
                    return 90;
                case StatusType.Airborne:
                    return 88;
                case StatusType.Fear:
                    return 85;
                case StatusType.Mark:
                    return 80;
                case StatusType.Curse:
                    return 75;
                case StatusType.Shock:
                    return 70;
                case StatusType.Corrosion:
                    return 68;
                case StatusType.Seal:
                    return 66;
                case StatusType.Burn:
                    return 60;
                case StatusType.Poison:
                    return 59;
                case StatusType.Bleed:
                    return 58;
                case StatusType.Slow:
                    return 50;
                default:
                    return 40;
            }
        }

        public string GetIconEffectId(int index)
        {
            return IsActiveIndex(index)
                ? slots[index].EffectId
                : string.Empty;
        }

        public Vector3 GetIconLocalPosition(int index)
        {
            return IsActiveIndex(index)
                ? slots[index].Root.transform.localPosition
                : Vector3.zero;
        }

        public Color GetIconPrimaryColor(int index)
        {
            return IsActiveIndex(index)
                ? slots[index].Fill.color
                : Color.clear;
        }

        public Color GetIconSecondaryColor(int index)
        {
            return IsActiveIndex(index)
                ? slots[index].Border.color
                : Color.clear;
        }

        public int GetIconDisplayedStackCount(int index)
        {
            return IsActiveIndex(index)
                ? slots[index].DisplayedStackCount
                : 0;
        }

        public string GetIconStackLabel(int index)
        {
            return IsActiveIndex(index)
                ? slots[index].StackText.text
                : string.Empty;
        }

        public static string FormatStackLabel(
            int stackCount)
        {
            if (stackCount <= 1)
            {
                return string.Empty;
            }

            return stackCount >=
                   MaximumDisplayedStackCount
                ? "9+"
                : "x" + stackCount;
        }

        private void CacheRenderer()
        {
            if (targetRenderer == null)
            {
                targetRenderer =
                    GetComponent<SpriteRenderer>();
            }
        }

        private void EnsureStackRoot()
        {
            if (stackRoot != null)
            {
                return;
            }

            Transform existing =
                transform.Find(StackRootName);
            if (existing != null)
            {
                stackRoot = existing;
                return;
            }

            var root = new GameObject(StackRootName);
            root.transform.SetParent(transform, false);
            stackRoot = root.transform;
        }

        private void EnsureSlotCount(int requiredCount)
        {
            EnsureStackRoot();
            bool createdSlot = false;
            while (slots.Count < requiredCount)
            {
                slots.Add(CreateSlot(slots.Count));
                createdSlot = true;
            }

            if (createdSlot)
            {
                cachedSortingLayerId = int.MinValue;
                cachedSortingOrder = int.MinValue;
            }
        }

        private IconSlot CreateSlot(int index)
        {
            var root = new GameObject(
                "Status Icon " + index);
            root.transform.SetParent(stackRoot, false);

            SpriteRenderer border =
                CreateSquareRenderer(
                    "Border",
                    root.transform,
                    Vector3.one * IconSize);
            SpriteRenderer fill =
                CreateSquareRenderer(
                    "Fill",
                    root.transform,
                    Vector3.one * (IconSize * 0.72f));
            TextMesh stackText =
                CreateStackText(
                    root.transform,
                    out MeshRenderer stackTextRenderer);
            root.SetActive(false);
            return new IconSlot(
                root,
                border,
                fill,
                stackText,
                stackTextRenderer);
        }

        private static SpriteRenderer CreateSquareRenderer(
            string objectName,
            Transform parent,
            Vector3 scale)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation =
                Quaternion.identity;
            child.transform.localScale = scale;
            SpriteRenderer renderer =
                child.AddComponent<SpriteRenderer>();
            renderer.sprite = SharedResources.SquareSprite;
            return renderer;
        }

        private static TextMesh CreateStackText(
            Transform parent,
            out MeshRenderer textRenderer)
        {
            var child = new GameObject("Stack Count");
            child.transform.SetParent(parent, false);
            child.transform.localPosition =
                new Vector3(0f, 0.005f, -0.006f);
            child.transform.localRotation =
                Quaternion.identity;

            TextMesh text = child.AddComponent<TextMesh>();
            text.text = string.Empty;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize =
                StackLabelCharacterSize;
            text.fontSize = StackLabelFontSize;
            text.fontStyle = FontStyle.Normal;
            text.color = Color.white;

            Font font =
                Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            if (font != null)
            {
                text.font = font;
            }

            textRenderer =
                child.GetComponent<MeshRenderer>();
            if (font != null &&
                textRenderer != null)
            {
                textRenderer.sharedMaterial =
                    font.material;
            }

            child.SetActive(false);
            return text;
        }

        private static void ApplyStyle(
            IconSlot slot,
            StageOneCardEffectStyle style)
        {
            Color primary = style.Primary;
            primary.a = 0.96f;
            Color secondary = style.Secondary;
            secondary.a = 1f;
            slot.Fill.color = primary;
            slot.Border.color = secondary;
        }

        private void LayoutActiveIcons()
        {
            if (stackRoot == null ||
                activeIconCount <= 0)
            {
                return;
            }

            float baseHeight =
                ResolveBottomRowCenterY();
            for (int index = 0;
                 index < activeIconCount;
                 index++)
            {
                int row = index / IconsPerRow;
                int column = index % IconsPerRow;
                slots[index].Root.transform.localPosition =
                    new Vector3(
                        (column -
                         (IconsPerRow - 1) * 0.5f) *
                        IconSpacing,
                        baseHeight +
                        row * IconSpacing,
                        -0.03f);
            }
        }

        private float ResolveBottomRowCenterY()
        {
            if (!float.IsNaN(
                    preferredHealthBarTopLocalY) &&
                !float.IsInfinity(
                    preferredHealthBarTopLocalY))
            {
                return preferredHealthBarTopLocalY +
                       HealthBarClearance +
                       IconSize * 0.5f;
            }

            if (targetRenderer == null ||
                targetRenderer.sprite == null)
            {
                return 0.62f;
            }

            Transform rendererTransform =
                targetRenderer.transform;
            float spriteTop =
                targetRenderer.sprite.bounds.max.y *
                Mathf.Abs(
                    rendererTransform.localScale.y);
            return Mathf.Max(
                0.48f,
                rendererTransform.localPosition.y +
                spriteTop +
                FallbackHeadClearance +
                IconSize * 0.5f);
        }

        private void SynchronizeSorting()
        {
            if (targetRenderer == null)
            {
                return;
            }

            int layer = targetRenderer.sortingLayerID;
            int order =
                targetRenderer.sortingOrder +
                IconSortingOffset;
            if (layer == cachedSortingLayerId &&
                order == cachedSortingOrder)
            {
                return;
            }

            cachedSortingLayerId = layer;
            cachedSortingOrder = order;
            for (int index = 0;
                 index < slots.Count;
                 index++)
            {
                IconSlot slot = slots[index];
                slot.Border.sortingLayerID = layer;
                slot.Border.sortingOrder = order;
                slot.Fill.sortingLayerID = layer;
                slot.Fill.sortingOrder = order + 1;
                slot.StackTextRenderer.sortingLayerID =
                    layer;
                slot.StackTextRenderer.sortingOrder =
                    order + 2;
            }
        }

        private int FindEffectId(
            string effectId,
            int count)
        {
            for (int index = 0;
                 index < count;
                 index++)
            {
                if (string.Equals(
                        activeEffectIds[index],
                        effectId,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private static int CombineStackCounts(
            int current,
            int added)
        {
            if (current >=
                MaximumDisplayedStackCount ||
                added >=
                MaximumDisplayedStackCount)
            {
                return MaximumDisplayedStackCount;
            }

            return Mathf.Min(
                MaximumDisplayedStackCount,
                Mathf.Max(0, current) +
                Mathf.Max(0, added));
        }

        private bool IsActiveIndex(int index)
        {
            return index >= 0 &&
                   index < activeIconCount &&
                   index < slots.Count;
        }

        private sealed class IconSlot
        {
            public IconSlot(
                GameObject root,
                SpriteRenderer border,
                SpriteRenderer fill,
                TextMesh stackText,
                MeshRenderer stackTextRenderer)
            {
                Root = root;
                Border = border;
                Fill = fill;
                StackText = stackText;
                StackTextRenderer =
                    stackTextRenderer;
                EffectId = string.Empty;
            }

            public GameObject Root { get; }
            public SpriteRenderer Border { get; }
            public SpriteRenderer Fill { get; }
            public TextMesh StackText { get; }
            public MeshRenderer StackTextRenderer { get; }
            public string EffectId { get; set; }
            public int DisplayedStackCount { get; set; }
        }

        private static class SharedResources
        {
            private static Sprite squareSprite;

            public static Sprite SquareSprite =>
                squareSprite ?? (squareSprite =
                    CreateSquareSprite());

            private static Sprite CreateSquareSprite()
            {
                const int size = 4;
                var texture = new Texture2D(
                    size,
                    size,
                    TextureFormat.RGBA32,
                    false)
                {
                    name =
                        "Ruleforge Status Icon Square Texture",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags =
                        HideFlags.HideAndDontSave
                };
                var pixels = new Color32[size * size];
                for (int index = 0;
                     index < pixels.Length;
                     index++)
                {
                    pixels[index] =
                        new Color32(
                            255,
                            255,
                            255,
                            255);
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, size, size),
                    new Vector2(0.5f, 0.5f),
                    size);
                sprite.name =
                    "Ruleforge Status Icon Square";
                sprite.hideFlags =
                    HideFlags.HideAndDontSave;
                return sprite;
            }
        }
    }
}
