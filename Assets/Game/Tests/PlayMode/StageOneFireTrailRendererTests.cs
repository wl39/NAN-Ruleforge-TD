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
                    .sortingOrder,
                Is.EqualTo(
                    StageOneFireTrailRenderer.SortingOrder));
            Assert.That(
                renderer.RuntimeMesh.bounds.min.x,
                Is.LessThanOrEqualTo(0f));
            Assert.That(
                renderer.RuntimeMesh.bounds.max.x,
                Is.GreaterThanOrEqualTo(2f));

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
        public void LifetimeFadesAndNonBurnHazardsAreNotRendered()
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
            Assert.That(renderer.VisibleHazardCount, Is.Zero);
            Assert.That(renderer.VertexCount, Is.Zero);

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
    }
}
