using System.Collections.Generic;
using NUnit.Framework;
using RuleforgeTD.Battle;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;
using UnityEngine;

namespace RuleforgeTD.Tests.PlayMode
{
    public sealed class StageOneFireTrailRendererTests
    {
        private GameObject host;
        private StageOneFireTrailRenderer renderer;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject(
                "Fire Trail Renderer Test",
                typeof(MeshFilter),
                typeof(MeshRenderer),
                typeof(StageOneFireTrailRenderer));
            renderer =
                host.GetComponent<
                    StageOneFireTrailRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null)
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void BurnSegment_BuildsOneBatchedAnimatedMesh()
        {
            HazardSnapshot[] hazards =
            {
                CreateHazard(
                    7,
                    StatusType.Burn,
                    0,
                    0,
                    2000,
                    0,
                    60)
            };

            renderer.ApplySnapshot(hazards, 10);
            Vector3[] firstFrame =
                renderer.RuntimeMesh.vertices;

            Assert.That(
                renderer.VisibleHazardCount,
                Is.EqualTo(1));
            Assert.That(renderer.VertexCount, Is.GreaterThan(4));
            Assert.That(renderer.HasRuntimeMaterial, Is.True);
            Assert.That(
                renderer.GetComponent<MeshRenderer>()
                    .sharedMaterial.mainTexture.filterMode,
                Is.EqualTo(FilterMode.Point),
                "Hazard art must keep crisp pixel sampling.");
            Assert.That(
                renderer.GetComponent<MeshRenderer>()
                    .sortingOrder,
                Is.EqualTo(
                    StageOneFireTrailRenderer.SortingOrder));
            Assert.That(
                renderer.RuntimeMesh.bounds.min.x,
                Is.LessThanOrEqualTo(0f));
            Assert.That(
                renderer.RuntimeMesh.bounds.max.x,
                Is.GreaterThanOrEqualTo(2f));
            Assert.That(
                renderer.VertexCount % 4,
                Is.Zero,
                "Burn art must be built only from pixel quads.");
            AssertPixelQuadGeometry(
                renderer.RuntimeMesh.vertices,
                StageOneFireTrailRenderer.FirePixelSize);

            renderer.ApplySnapshot(hazards, 11);
            Vector3[] secondFrame =
                renderer.RuntimeMesh.vertices;
            Assert.That(
                HasDifferentVertex(
                    firstFrame,
                    secondFrame),
                Is.True,
                "A later simulation tick must change the flame silhouette.");
        }

        [Test]
        public void LifetimeFadesAndPoisonShowsItsPixelArea()
        {
            renderer.ApplySnapshot(
                new[]
                {
                    CreateHazard(
                        2,
                        StatusType.Burn,
                        0,
                        0,
                        1000,
                        0,
                        60)
                },
                20);
            float fullOpacity =
                renderer.LastMaximumOpacity;

            renderer.ApplySnapshot(
                new[]
                {
                    CreateHazard(
                        2,
                        StatusType.Burn,
                        0,
                        0,
                        1000,
                        0,
                        1)
                },
                21);
            Assert.That(
                renderer.LastMaximumOpacity,
                Is.LessThan(fullOpacity));

            renderer.ApplySnapshot(
                new[]
                {
                    CreateHazard(
                        3,
                        StatusType.Poison,
                        0,
                        0,
                        1000,
                        0,
                        60)
                },
                22);
            Assert.That(renderer.VisibleHazardCount, Is.EqualTo(1));
            Assert.That(renderer.VertexCount, Is.GreaterThan(4));
            Assert.That(
                StageOneFireTrailRenderer
                    .PoisonResolutionMultiplier,
                Is.EqualTo(4));
            Assert.That(
                StageOneFireTrailRenderer.PoisonPixelSize,
                Is.EqualTo(1f / 64f).Within(0.000001f));
            Assert.That(
                renderer.RuntimeMesh.bounds.size.y,
                Is.GreaterThanOrEqualTo(0.75f),
                "Poison pixels must show the gameplay radius, not only " +
                "the path center.");
            Vector3[] poisonVertices =
                renderer.RuntimeMesh.vertices;
            var pixelCenters = new HashSet<Vector2>();
            for (int vertex = 0;
                 vertex + 3 < poisonVertices.Length;
                 vertex += 4)
            {
                Vector3 center =
                    (poisonVertices[vertex] +
                     poisonVertices[vertex + 1] +
                     poisonVertices[vertex + 2] +
                     poisonVertices[vertex + 3]) *
                    0.25f;
                Assert.That(
                    pixelCenters.Add(
                        new Vector2(center.x, center.y)),
                    Is.True,
                    "Overlapping poison samples must not darken into a " +
                    "rectangular block.");
            }

            renderer.ApplySnapshot(
                new[]
                {
                    CreateHazard(
                        3,
                        StatusType.Poison,
                        0,
                        0,
                        1000,
                        0,
                        60)
                },
                23);
            Assert.That(
                HasDifferentVertex(
                    poisonVertices,
                    renderer.RuntimeMesh.vertices),
                Is.True,
                "Poison bubbles must rise between simulation ticks.");

            renderer.Clear();
            Assert.That(renderer.VisibleHazardCount, Is.Zero);
            Assert.That(renderer.VertexCount, Is.Zero);
        }

        private static HazardSnapshot CreateHazard(
            int id,
            StatusType type,
            int startX,
            int startY,
            int endX,
            int endY,
            int remainingTicks)
        {
            return new HazardSnapshot(
                id,
                type,
                SimPosition.FromMilliUnits(
                    startX,
                    startY),
                SimPosition.FromMilliUnits(
                    endX,
                    endY),
                400,
                60,
                remainingTicks,
                1,
                CardId.Invalid,
                4,
                8);
        }

        private static bool HasDifferentVertex(
            Vector3[] first,
            Vector3[] second)
        {
            if (first.Length != second.Length)
            {
                return true;
            }

            for (int i = 0; i < first.Length; i++)
            {
                if ((first[i] - second[i])
                    .sqrMagnitude > 0.000001f)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertPixelQuadGeometry(
            Vector3[] vertices,
            float pixelSize)
        {
            Assert.That(vertices.Length % 4, Is.Zero);
            for (int vertex = 0;
                 vertex + 3 < vertices.Length;
                 vertex += 4)
            {
                var xValues = new HashSet<int>();
                var yValues = new HashSet<int>();
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 position = vertices[vertex + corner];
                    float gridX = position.x / (pixelSize * 0.5f);
                    float gridY = position.y / (pixelSize * 0.5f);
                    Assert.That(
                        gridX,
                        Is.EqualTo(Mathf.Round(gridX)).Within(0.0001f));
                    Assert.That(
                        gridY,
                        Is.EqualTo(Mathf.Round(gridY)).Within(0.0001f));
                    xValues.Add(Mathf.RoundToInt(gridX));
                    yValues.Add(Mathf.RoundToInt(gridY));
                }

                Assert.That(xValues.Count, Is.EqualTo(2));
                Assert.That(yValues.Count, Is.EqualTo(2));
            }
        }
    }
}
