#if UNITY_EDITOR
using NUnit.Framework;
using RuleforgeTD.Maps;
using UnityEngine;

namespace RuleforgeTD.Tests.EditMode
{
    public sealed class StageSpawnDirectionMarkerTests
    {
        [Test]
        public void Marker_PointsAlongFirstPathSegment()
        {
            var root = new GameObject("Spawn Marker Test");
            try
            {
                StagePathAuthoring path =
                    root.AddComponent<StagePathAuthoring>();
                path.ConfigureAuthoring(new[]
                {
                    new Vector2(3f, 4f),
                    new Vector2(3f, 12f),
                    new Vector2(10f, 12f)
                });
                var markerObject = new GameObject("Marker");
                markerObject.transform.SetParent(root.transform, false);
                StageSpawnDirectionMarker marker = markerObject
                    .AddComponent<StageSpawnDirectionMarker>();

                marker.Initialize(path);

                Assert.That(marker.IsInitialized, Is.True);
                Assert.That(marker.Direction.x, Is.EqualTo(0f).Within(0.001f));
                Assert.That(marker.Direction.y, Is.EqualTo(1f).Within(0.001f));
                Assert.That(marker.MarkerPosition.x, Is.EqualTo(3f).Within(0.001f));
                Assert.That(marker.MarkerPosition.y, Is.EqualTo(3.1f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
#endif
