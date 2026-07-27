using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;
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
        public const int FadeTickCount = 8;
        public const int IgniteTickCount = 3;

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

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh runtimeMesh;
        private Material runtimeMaterial;
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
                meshRenderer.sortingOrder = SortingOrder;
            }

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
                    runtimeMaterial = new Material(shader)
                    {
                        name = "Stage One Fire Trail Material",
                        hideFlags = HideFlags.DontSave,
                        mainTexture = Texture2D.whiteTexture
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
                if (hazard.StatusType != StatusType.Burn ||
                    hazard.RemainingTicks <= 0)
                {
                    continue;
                }

                AddFireSegment(in hazard);
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

        private void AddFireSegment(
            in HazardSnapshot hazard)
        {
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

            // 바닥의 붉은 열기 띠가 선분 전체를 잇고, 그 위에 작은
            // 외곽/심지 불꽃을 결정적 위상으로 배치한다.
            float groundHalfWidth = radius * 0.72f;
            Vector2 side = normal * groundHalfWidth;
            AddQuad(
                start - side,
                end - side,
                end + side,
                start + side,
                WithAlpha(
                    new Color32(126, 18, 5, 116),
                    opacity),
                WithAlpha(
                    new Color32(238, 61, 8, 146),
                    opacity));

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

                float height =
                    radius *
                    (1.05f +
                     0.34f * Mathf.Sin(phase));
                float halfWidth =
                    radius *
                    (0.28f +
                     0.05f *
                     Mathf.Cos(phase * 1.37f));
                float wobble =
                    Mathf.Sin(phase * 1.71f) *
                    radius *
                    0.22f;
                Vector2 up = Vector2.up;
                Vector2 tip =
                    basePoint +
                    up * height +
                    Vector2.right * wobble;
                Vector2 outerLeft =
                    basePoint -
                    Vector2.right * halfWidth;
                Vector2 outerRight =
                    basePoint +
                    Vector2.right * halfWidth;
                AddTriangle(
                    outerLeft,
                    outerRight,
                    tip,
                    WithAlpha(
                        new Color32(219, 42, 3, 228),
                        opacity),
                    WithAlpha(
                        new Color32(255, 123, 8, 238),
                        opacity),
                    WithAlpha(
                        new Color32(255, 196, 39, 52),
                        opacity));

                Vector2 innerBase =
                    basePoint + up * height * 0.05f;
                Vector2 innerTip =
                    Vector2.Lerp(
                        innerBase,
                        tip,
                        0.68f);
                float innerHalfWidth =
                    halfWidth * 0.47f;
                AddTriangle(
                    innerBase -
                    Vector2.right * innerHalfWidth,
                    innerBase +
                    Vector2.right * innerHalfWidth,
                    innerTip,
                    WithAlpha(
                        new Color32(255, 151, 12, 245),
                        opacity),
                    WithAlpha(
                        new Color32(255, 218, 62, 250),
                        opacity),
                    WithAlpha(
                        new Color32(255, 246, 164, 88),
                        opacity));
            }
        }

        private void AddQuad(
            Vector2 first,
            Vector2 second,
            Vector2 third,
            Vector2 fourth,
            Color32 edgeColor,
            Color32 centerColor)
        {
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

        private void AddTriangle(
            Vector2 first,
            Vector2 second,
            Vector2 third,
            Color32 firstColor,
            Color32 secondColor,
            Color32 thirdColor)
        {
            int index = vertices.Count;
            AddVertex(first, firstColor);
            AddVertex(second, secondColor);
            AddVertex(third, thirdColor);
            triangles.Add(index);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
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
            runtimeMesh = null;
            runtimeMaterial = null;
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
