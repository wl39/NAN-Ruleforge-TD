using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;
using RuleforgeTD.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuleforgeTD.Battle
{
    /// <summary>
    /// 모든 활성 화상 불길을 하나의 동적 메시로 그리는 WebGL용 배치 렌더러다.
    /// 별도 파티클이나 Hazard별 GameObject를 만들지 않고, 시뮬레이션 틱을
    /// 애니메이션 시간으로 사용해 일시정지와 전투 배속을 그대로 따른다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class StageOneFireTrailRenderer : MonoBehaviour
    {
        public const int SortingOrder = 14;
        public const int MaximumFlameSamplesPerSegment = 8;
        public const int MaximumFireGroundSamplesPerSegment = 96;
        public const float FirePixelSize = 0.0625f;
        public const int FadeTickCount = 8;
        public const int IgniteTickCount = 3;
        public const int PoisonResolutionMultiplier = 4;
        public const float PoisonPixelSize = 0.015625f;
        public const float PoisonBorderWidth = 0.0625f;
        public const int MaximumPoisonCircleSamples = 12;
        public const int PoisonBubbleCountPerHazard = 6;
        public const int MaximumHazardQuads = 8192;

        private static readonly HazardSnapshot[] EmptyHazards =
            Array.Empty<HazardSnapshot>();

        private readonly List<Vector3> vertices =
            new List<Vector3>(4096);
        private readonly List<Color32> colors =
            new List<Color32>(4096);
        private readonly List<Vector2> uvs =
            new List<Vector2>(4096);
        private readonly List<int> triangles =
            new List<int>(6144);
        private readonly HashSet<long> poisonPixelKeys =
            new HashSet<long>();
        private readonly HashSet<long> poisonInteriorPixelKeys =
            new HashSet<long>();
        private readonly HashSet<long> firePixelKeys =
            new HashSet<long>();

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh runtimeMesh;
        private Material runtimeMaterial;
        private Texture2D runtimePixelTexture;
        private HazardSnapshot[] currentHazards = EmptyHazards;
        private long animationTick = long.MinValue;
        private int visibleHazardCount;
        private float lastMaximumOpacity;

        public int VisibleHazardCount => visibleHazardCount;
        public long AnimationTick => animationTick;
        public int VertexCount =>
            runtimeMesh == null ? 0 : runtimeMesh.vertexCount;
        public Mesh RuntimeMesh => runtimeMesh;
        public bool HasRuntimeMaterial => runtimeMaterial != null;
        public float LastMaximumOpacity => lastMaximumOpacity;

        private void Awake()
        {
            EnsureResources();
        }

        /// <summary>
        /// 최신 권위 스냅샷으로 불길 메시를 갱신한다. 같은 스냅샷과 틱이
        /// 반복 전달되면 메시를 다시 만들지 않아 정지 프레임의 CPU 사용을 줄인다.
        /// </summary>
        public void ApplySnapshot(
            HazardSnapshot[] hazards,
            long simulationTick)
        {
            EnsureResources();
            HazardSnapshot[] next =
                hazards ?? EmptyHazards;
            if (ReferenceEquals(currentHazards, next) &&
                animationTick == simulationTick)
            {
                return;
            }

            currentHazards = next;
            animationTick = simulationTick;
            RebuildMesh();
        }

        public void Clear()
        {
            currentHazards = EmptyHazards;
            animationTick = long.MinValue;
            visibleHazardCount = 0;
            lastMaximumOpacity = 0f;
            if (runtimeMesh != null)
            {
                runtimeMesh.Clear(false);
            }
        }

        private void EnsureResources()
        {
            if (meshFilter == null)
            {
                meshFilter = GetComponent<MeshFilter>();
            }

            if (meshRenderer == null)
            {
                meshRenderer = GetComponent<MeshRenderer>();
            }

            WorldSortingLayers.Apply(
                meshRenderer,
                WorldSortingLayers.Route);
            meshRenderer.sortingOrder = SortingOrder;

            if (runtimeMesh == null)
            {
                runtimeMesh = new Mesh
                {
                    name = "Stage One Batched Fire Trail",
                    hideFlags = HideFlags.DontSave,
                    indexFormat = IndexFormat.UInt32
                };
                runtimeMesh.MarkDynamic();
                meshFilter.sharedMesh = runtimeMesh;
            }

            if (runtimeMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    shader = Shader.Find("Unlit/Transparent");
                }

                if (shader != null)
                {
                    if (runtimePixelTexture == null)
                    {
                        runtimePixelTexture = new Texture2D(
                            1,
                            1,
                            TextureFormat.RGBA32,
                            false)
                        {
                            name = "Stage One Hazard Point Texture",
                            filterMode = FilterMode.Point,
                            wrapMode = TextureWrapMode.Clamp,
                            hideFlags = HideFlags.DontSave
                        };
                        runtimePixelTexture.SetPixel(
                            0,
                            0,
                            Color.white);
                        runtimePixelTexture.Apply(false, true);
                    }

                    runtimeMaterial = new Material(shader)
                    {
                        name = "Stage One Fire Trail Material",
                        hideFlags = HideFlags.DontSave,
                        mainTexture = runtimePixelTexture
                    };
                    meshRenderer.sharedMaterial =
                        runtimeMaterial;
                }
            }
        }

        private void RebuildMesh()
        {
            vertices.Clear();
            colors.Clear();
            uvs.Clear();
            triangles.Clear();
            visibleHazardCount = 0;
            lastMaximumOpacity = 0f;

            for (int i = 0; i < currentHazards.Length; i++)
            {
                HazardSnapshot hazard = currentHazards[i];
                if (hazard.RemainingTicks <= 0)
                {
                    continue;
                }

                if (hazard.StatusType == StatusType.Burn)
                {
                    AddFireSegment(in hazard);
                }
                else if (hazard.StatusType == StatusType.Poison)
                {
                    AddPoisonArea(in hazard);
                }
                else
                {
                    continue;
                }
                visibleHazardCount++;
            }

            runtimeMesh.Clear(false);
            if (vertices.Count == 0)
            {
                return;
            }

            runtimeMesh.SetVertices(vertices);
            runtimeMesh.SetColors(colors);
            runtimeMesh.SetUVs(0, uvs);
            runtimeMesh.SetTriangles(
                triangles,
                0,
                false);
            runtimeMesh.RecalculateBounds();
        }

        private void AddPoisonArea(
            in HazardSnapshot hazard)
        {
            poisonPixelKeys.Clear();
            poisonInteriorPixelKeys.Clear();
            Vector2 start = ToWorld(hazard.StartPosition);
            Vector2 end = ToWorld(hazard.EndPosition);
            Vector2 delta = end - start;
            float length = delta.magnitude;
            float radius = Mathf.Max(
                PoisonPixelSize,
                hazard.RadiusMilli / 1000f);
            int duration = Mathf.Max(1, hazard.DurationTicks);
            int age = Mathf.Max(
                0,
                duration - hazard.RemainingTicks);
            float ignite = Mathf.Clamp01(
                (age + 1f) / IgniteTickCount);
            float fade = Mathf.Clamp01(
                hazard.RemainingTicks /
                (float)FadeTickCount);
            float opacity = Mathf.Clamp01(ignite * fade);
            lastMaximumOpacity = Mathf.Max(
                lastMaximumOpacity,
                opacity);

            StageOneCardEffectPalette.TryGetStyle(
                "poison",
                out StageOneCardEffectStyle style);
            Color primary = style.Primary;
            float spacing = Mathf.Max(
                PoisonPixelSize,
                radius * 1.25f);
            int sampleCount = Mathf.Clamp(
                Mathf.CeilToInt(length / spacing) + 1,
                1,
                MaximumPoisonCircleSamples);
            int gridRadius = Mathf.Max(
                1,
                Mathf.CeilToInt(radius / PoisonPixelSize));
            float innerRadius = Mathf.Max(
                0f,
                radius - PoisonBorderWidth);
            int minimumGridX = int.MaxValue;
            int maximumGridX = int.MinValue;
            int minimumGridY = int.MaxValue;
            int maximumGridY = int.MinValue;
            for (int sample = 0; sample < sampleCount; sample++)
            {
                float t = sampleCount <= 1
                    ? 0.5f
                    : sample / (float)(sampleCount - 1);
                Vector2 center = Vector2.Lerp(start, end, t);
                int centerGridX = Mathf.RoundToInt(
                    center.x / PoisonPixelSize);
                int centerGridY = Mathf.RoundToInt(
                    center.y / PoisonPixelSize);
                minimumGridX = Mathf.Min(
                    minimumGridX,
                    centerGridX - gridRadius);
                maximumGridX = Mathf.Max(
                    maximumGridX,
                    centerGridX + gridRadius);
                minimumGridY = Mathf.Min(
                    minimumGridY,
                    centerGridY - gridRadius);
                maximumGridY = Mathf.Max(
                    maximumGridY,
                    centerGridY + gridRadius);

                for (int y = -gridRadius;
                     y <= gridRadius;
                     y++)
                {
                    for (int x = -gridRadius;
                         x <= gridRadius;
                         x++)
                    {
                        Vector2 offset = new Vector2(
                            x * PoisonPixelSize,
                            y * PoisonPixelSize);
                        float distance = offset.magnitude;
                        if (distance > radius)
                        {
                            continue;
                        }

                        int pixelGridX = centerGridX + x;
                        int pixelGridY = centerGridY + y;
                        long pixelKey =
                            ((long)pixelGridX << 32) ^
                            (uint)pixelGridY;
                        poisonPixelKeys.Add(pixelKey);
                        if (distance <= innerRadius)
                        {
                            poisonInteriorPixelKeys.Add(pixelKey);
                        }
                    }
                }
            }

            Color borderColor = primary;
            borderColor.a = 0.96f * opacity;
            Color fillColor = primary;
            fillColor.a = 0.32f * opacity;
            for (int gridY = minimumGridY;
                 gridY <= maximumGridY;
                 gridY++)
            {
                int runKind = 0;
                int runStartX = minimumGridX;
                for (int gridX = minimumGridX;
                     gridX <= maximumGridX + 1;
                     gridX++)
                {
                    int nextKind = 0;
                    if (gridX <= maximumGridX)
                    {
                        long pixelKey =
                            ((long)gridX << 32) ^
                            (uint)gridY;
                        if (poisonPixelKeys.Contains(pixelKey))
                        {
                            nextKind =
                                poisonInteriorPixelKeys.Contains(
                                    pixelKey)
                                    ? 2
                                    : 1;
                        }
                    }

                    if (nextKind == runKind)
                    {
                        continue;
                    }

                    if (runKind != 0)
                    {
                        if (vertices.Count / 4 >=
                            MaximumHazardQuads)
                        {
                            return;
                        }

                        AddPoisonPixelRun(
                            runStartX,
                            gridX - 1,
                            gridY,
                            runKind == 2
                                ? fillColor
                                : borderColor);
                    }

                    runKind = nextKind;
                    runStartX = gridX;
                }
            }

            AddPoisonBubbles(
                in hazard,
                start,
                end,
                radius,
                bubbleColor: style.Secondary,
                opacity: opacity);
        }

        /// <summary>
        /// Adds deterministic, Point-filtered bubbles above the poison board.
        /// Their centers remain inside the authoritative capsule radius; the
        /// upward screen motion is presentation-only and never changes the
        /// hazard snapshot or collision area.
        /// </summary>
        private void AddPoisonBubbles(
            in HazardSnapshot hazard,
            Vector2 start,
            Vector2 end,
            float radius,
            Color bubbleColor,
            float opacity)
        {
            float bubblePixel = PoisonPixelSize * 2f;
            for (int i = 0;
                 i < PoisonBubbleCountPerHazard;
                 i++)
            {
                float along =
                    (i + 0.5f) /
                    PoisonBubbleCountPerHazard;
                Vector2 basePoint = Vector2.Lerp(
                    start,
                    end,
                    along);
                float phase = Mathf.Repeat(
                    (animationTick + hazard.Id * 13L) / 18f +
                    i * 0.173f,
                    1f);
                float rise = Mathf.SmoothStep(0f, 1f, phase);
                float signed =
                    ((hazard.Id * 37 + i * 17) & 15) /
                    7.5f - 1f;
                Vector2 center = basePoint + new Vector2(
                    signed * radius * 0.46f,
                    Mathf.Lerp(
                        -radius * 0.34f,
                        radius * 0.48f,
                        rise));
                float bubbleRadius = Mathf.Lerp(
                    bubblePixel,
                    bubblePixel * 2.1f,
                    rise);
                Color color = bubbleColor;
                color.a = Mathf.Clamp01(
                    opacity *
                    Mathf.InverseLerp(1f, 0.76f, phase));
                AddPoisonBubbleRing(
                    center,
                    bubbleRadius,
                    bubblePixel,
                    color);
            }
        }

        private void AddPoisonBubbleRing(
            Vector2 center,
            float radius,
            float pixelSize,
            Color color)
        {
            Color32 pixelColor = color;
            const int pointCount = 8;
            float half = pixelSize * 0.5f;
            for (int i = 0; i < pointCount; i++)
            {
                float angle = Mathf.PI * 2f * i / pointCount;
                Vector2 point = center + new Vector2(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius);
                AddQuad(
                    point + new Vector2(-half, -half),
                    point + new Vector2(half, -half),
                    point + new Vector2(half, half),
                    point + new Vector2(-half, half),
                    pixelColor,
                    pixelColor);
            }
        }

        /// <summary>
        /// 같은 색으로 이어진 고해상도 픽셀을 가로 한 줄의 Quad로 묶는다.
        /// 4배 샘플링에서도 셀마다 Quad를 만드는 방식보다 적은 정점으로
        /// 자연스러운 원 외곽을 유지한다.
        /// </summary>
        private void AddPoisonPixelRun(
            int startGridX,
            int endGridX,
            int gridY,
            Color32 color)
        {
            float halfPixel = PoisonPixelSize * 0.5f;
            float left =
                startGridX * PoisonPixelSize - halfPixel;
            float right =
                endGridX * PoisonPixelSize + halfPixel;
            float centerY = gridY * PoisonPixelSize;
            AddQuad(
                new Vector2(left, centerY - halfPixel),
                new Vector2(right, centerY - halfPixel),
                new Vector2(right, centerY + halfPixel),
                new Vector2(left, centerY + halfPixel),
                color,
                color);
        }

        private void AddFireSegment(
            in HazardSnapshot hazard)
        {
            firePixelKeys.Clear();
            Vector2 start = ToWorld(hazard.StartPosition);
            Vector2 end = ToWorld(hazard.EndPosition);
            Vector2 delta = end - start;
            float length = delta.magnitude;
            Vector2 direction = length > 0.0001f
                ? delta / length
                : Vector2.right;
            Vector2 normal =
                new Vector2(-direction.y, direction.x);
            float radius = Mathf.Max(
                0.08f,
                hazard.RadiusMilli / 1000f);

            int duration = Mathf.Max(
                1,
                hazard.DurationTicks);
            int age = Mathf.Max(
                0,
                duration - hazard.RemainingTicks);
            float ignite = Mathf.Clamp01(
                (age + 1f) / IgniteTickCount);
            float fade = Mathf.Clamp01(
                hazard.RemainingTicks /
                (float)FadeTickCount);
            float pulse =
                0.88f +
                0.12f *
                Mathf.Sin(
                    (animationTick +
                     hazard.Id * 11L) *
                    0.73f);
            float opacity =
                Mathf.Clamp01(ignite * fade * pulse);
            lastMaximumOpacity = Mathf.Max(
                lastMaximumOpacity,
                opacity);

            // The texture alone cannot pixelate polygon silhouettes. Build
            // the entire heat bed from world-grid squares so its edge stays
            // stair-stepped even when the trail is diagonal or enlarged.
            float groundRadius = Mathf.Max(
                FirePixelSize,
                radius * 0.72f);
            int alongSamples = Mathf.Clamp(
                Mathf.CeilToInt(length / FirePixelSize) + 1,
                1,
                MaximumFireGroundSamplesPerSegment);
            int gridRadius = Mathf.Max(
                1,
                Mathf.CeilToInt(groundRadius / FirePixelSize));
            for (int along = 0; along < alongSamples; along++)
            {
                float t = alongSamples <= 1
                    ? 0.5f
                    : along / (float)(alongSamples - 1);
                Vector2 sampleCenter = Vector2.Lerp(start, end, t);
                int centerGridX = Mathf.RoundToInt(
                    sampleCenter.x / FirePixelSize);
                int centerGridY = Mathf.RoundToInt(
                    sampleCenter.y / FirePixelSize);
                for (int y = -gridRadius; y <= gridRadius; y++)
                {
                    for (int x = -gridRadius; x <= gridRadius; x++)
                    {
                        Vector2 offset = new Vector2(
                            x * FirePixelSize,
                            y * FirePixelSize);
                        if (offset.sqrMagnitude >
                            groundRadius * groundRadius)
                        {
                            continue;
                        }

                        int gridX = centerGridX + x;
                        int gridY = centerGridY + y;
                        long key = ((long)gridX << 32) ^ (uint)gridY;
                        if (!firePixelKeys.Add(key))
                        {
                            continue;
                        }

                        int variation =
                            (gridX * 17 ^ gridY * 31 ^ hazard.Id) & 3;
                        Color32 groundColor = variation == 0
                            ? new Color32(238, 61, 8, 156)
                            : variation == 1
                                ? new Color32(169, 28, 4, 140)
                                : new Color32(112, 17, 5, 118);
                        AddFirePixel(
                            new Vector2(
                                gridX * FirePixelSize,
                                gridY * FirePixelSize),
                            WithAlpha(groundColor, opacity));
                    }
                }
            }

            float spacing = Mathf.Max(
                0.22f,
                radius * 0.72f);
            int sampleCount = Mathf.Clamp(
                Mathf.CeilToInt(
                    length /
                    spacing) + 1,
                2,
                MaximumFlameSamplesPerSegment);
            for (int sampleIndex = 0;
                 sampleIndex < sampleCount;
                 sampleIndex++)
            {
                float t = sampleCount <= 1
                    ? 0.5f
                    : sampleIndex /
                      (float)(sampleCount - 1);
                Vector2 basePoint =
                    Vector2.Lerp(start, end, t);
                int phaseSeed =
                    hazard.Id * 17 +
                    sampleIndex * 7;
                float phase =
                    (animationTick + phaseSeed) *
                    0.91f;
                float lateral =
                    Mathf.Sin(phase * 0.63f) *
                    radius *
                    0.24f;
                basePoint += normal * lateral;

                int baseGridX = Mathf.RoundToInt(
                    basePoint.x / FirePixelSize);
                int baseGridY = Mathf.RoundToInt(
                    basePoint.y / FirePixelSize);
                int flameHeight = Mathf.Clamp(
                    Mathf.RoundToInt(
                        radius *
                        (1.05f + 0.34f * Mathf.Sin(phase)) /
                        FirePixelSize),
                    3,
                    8);
                int tipSway =
                    (int)((animationTick + phaseSeed) % 3L) - 1;
                for (int layer = 0; layer < flameHeight; layer++)
                {
                    float layerProgress =
                        layer / (float)Mathf.Max(1, flameHeight - 1);
                    int layerSway = Mathf.RoundToInt(
                        tipSway * layerProgress);
                    int halfWidth = layerProgress < 0.34f
                        ? 1
                        : 0;
                    for (int x = -halfWidth; x <= halfWidth; x++)
                    {
                        bool inner = x == 0 && layerProgress < 0.72f;
                        Color32 flameColor = inner
                            ? layerProgress < 0.38f
                                ? new Color32(255, 239, 122, 250)
                                : new Color32(255, 151, 12, 245)
                            : layerProgress > 0.76f
                                ? new Color32(255, 196, 39, 205)
                                : new Color32(219, 42, 3, 232);
                        AddFirePixel(
                            new Vector2(
                                (baseGridX + x + layerSway) *
                                FirePixelSize,
                                (baseGridY + layer) * FirePixelSize),
                            WithAlpha(flameColor, opacity));
                    }
                }
            }
        }

        private void AddFirePixel(
            Vector2 center,
            Color32 color)
        {
            float half = FirePixelSize * 0.5f;
            AddQuad(
                center + new Vector2(-half, -half),
                center + new Vector2(half, -half),
                center + new Vector2(half, half),
                center + new Vector2(-half, half),
                color,
                color);
        }

        private void AddQuad(
            Vector2 first,
            Vector2 second,
            Vector2 third,
            Vector2 fourth,
            Color32 edgeColor,
            Color32 centerColor)
        {
            if (vertices.Count / 4 >= MaximumHazardQuads)
            {
                return;
            }

            int index = vertices.Count;
            AddVertex(first, edgeColor);
            AddVertex(second, edgeColor);
            AddVertex(third, centerColor);
            AddVertex(fourth, centerColor);
            triangles.Add(index);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
            triangles.Add(index);
            triangles.Add(index + 2);
            triangles.Add(index + 3);
        }

        private void AddVertex(
            Vector2 position,
            Color32 color)
        {
            vertices.Add(
                new Vector3(
                    position.x,
                    position.y,
                    -0.02f));
            colors.Add(color);
            uvs.Add(Vector2.zero);
        }

        private static Color32 WithAlpha(
            Color32 color,
            float opacity)
        {
            color.a = (byte)Mathf.Clamp(
                Mathf.RoundToInt(
                    color.a * opacity),
                0,
                255);
            return color;
        }

        private static Vector2 ToWorld(
            SimPosition position)
        {
            return new Vector2(
                position.X.MilliUnits / 1000f,
                position.Y.MilliUnits / 1000f);
        }

        private void OnDestroy()
        {
            DestroyRuntimeObject(runtimeMesh);
            DestroyRuntimeObject(runtimeMaterial);
            DestroyRuntimeObject(runtimePixelTexture);
            runtimeMesh = null;
            runtimeMaterial = null;
            runtimePixelTexture = null;
        }

        private static void DestroyRuntimeObject(
            UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
