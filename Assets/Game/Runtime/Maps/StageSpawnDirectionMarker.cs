using System;
using RuleforgeTD.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleforgeTD.Maps
{
    /// <summary>
    /// Draws a pulsing world-space arrow immediately before the first route
    /// waypoint. The arrow always points in the authoritative first movement
    /// direction and is installed for generated battle scenes at runtime.
    /// </summary>
    public sealed class StageSpawnDirectionMarker : MonoBehaviour
    {
        private const float BehindSpawnDistance = 0.9f;
        private const float PulseSpeed = 3.2f;
        private const float PulseAmount = 0.09f;

        private static readonly Color ArrowColor =
            new Color32(255, 213, 91, 255);
        private static readonly Color ShadowColor =
            new Color32(55, 42, 25, 210);

        private Mesh arrowMesh;
        private Material arrowMaterial;
        private Material shadowMaterial;
        private Vector3 authoredScale = new Vector3(1.35f, 1.35f, 1f);
        private bool initialized;

        public bool IsInitialized => initialized;
        public Vector2 Direction { get; private set; }
        public Vector2 MarkerPosition => transform.position;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneCallback()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(
            Scene scene,
            LoadSceneMode mode)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                FieldStageMap stage = roots[i]
                    .GetComponentInChildren<FieldStageMap>(true);
                if (stage == null || stage.Path == null)
                {
                    continue;
                }

                StageSpawnDirectionMarker existing = stage
                    .GetComponentInChildren<
                        StageSpawnDirectionMarker>(true);
                if (existing != null)
                {
                    return;
                }

                var markerObject = new GameObject(
                    "Enemy Spawn Direction");
                markerObject.transform.SetParent(
                    stage.transform,
                    false);
                StageSpawnDirectionMarker marker = markerObject
                    .AddComponent<StageSpawnDirectionMarker>();
                marker.Initialize(stage.Path);
                return;
            }
        }

        public void Initialize(StagePathAuthoring path)
        {
            if (path == null || path.WaypointCount < 2)
            {
                throw new ArgumentException(
                    "A spawn direction marker requires two path points.",
                    nameof(path));
            }

            Vector2 start = path.GetWorldWaypoint(0);
            Vector2 next = path.GetWorldWaypoint(1);
            Vector2 delta = next - start;
            if (delta.sqrMagnitude <= Mathf.Epsilon)
            {
                throw new InvalidOperationException(
                    "The first stage path segment cannot have zero length.");
            }

            Direction = delta.normalized;
            Vector2 markerPosition =
                start - Direction * BehindSpawnDistance;
            transform.position = new Vector3(
                markerPosition.x,
                markerPosition.y,
                -0.1f);
            transform.rotation = Quaternion.Euler(
                0f,
                0f,
                Mathf.Atan2(Direction.y, Direction.x) *
                Mathf.Rad2Deg);
            transform.localScale = authoredScale;
            CreateVisuals();
            initialized = true;
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            float pulse = 1f +
                Mathf.Sin(Time.unscaledTime * PulseSpeed) *
                PulseAmount;
            transform.localScale = authoredScale * pulse;
        }

        private void CreateVisuals()
        {
            if (arrowMesh != null)
            {
                return;
            }

            arrowMesh = CreateArrowMesh();
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                throw new InvalidOperationException(
                    "No unlit shader is available for the spawn arrow.");
            }

            shadowMaterial = new Material(shader)
            {
                color = ShadowColor,
                hideFlags = HideFlags.DontSave
            };
            arrowMaterial = new Material(shader)
            {
                color = ArrowColor,
                hideFlags = HideFlags.DontSave
            };
            CreateArrowLayer(
                "Arrow Shadow",
                arrowMesh,
                shadowMaterial,
                78,
                1.24f);
            CreateArrowLayer(
                "Arrow",
                arrowMesh,
                arrowMaterial,
                79,
                1f);
        }

        private void CreateArrowLayer(
            string objectName,
            Mesh mesh,
            Material material,
            int sortingOrder,
            float scale)
        {
            var layer = new GameObject(
                objectName,
                typeof(MeshFilter),
                typeof(MeshRenderer));
            layer.transform.SetParent(transform, false);
            layer.transform.localScale =
                new Vector3(scale, scale, 1f);
            layer.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer =
                layer.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            WorldSortingLayers.Apply(
                renderer,
                WorldSortingLayers.Route);
            renderer.sortingOrder = sortingOrder;
        }

        private static Mesh CreateArrowMesh()
        {
            var mesh = new Mesh
            {
                name = "Enemy Spawn Direction Arrow",
                hideFlags = HideFlags.DontSave
            };
            mesh.vertices = new[]
            {
                new Vector3(-0.75f, -0.22f, 0f),
                new Vector3(0.1f, -0.22f, 0f),
                new Vector3(0.1f, -0.48f, 0f),
                new Vector3(0.82f, 0f, 0f),
                new Vector3(0.1f, 0.48f, 0f),
                new Vector3(0.1f, 0.22f, 0f),
                new Vector3(-0.75f, 0.22f, 0f)
            };
            mesh.triangles = new[]
            {
                0, 1, 5,
                0, 5, 6,
                2, 3, 4
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDestroy()
        {
            DestroyRuntimeObject(arrowMesh);
            DestroyRuntimeObject(arrowMaterial);
            DestroyRuntimeObject(shadowMaterial);
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
