using System;
using UnityEngine;

namespace RuleforgeTD.Enemies.Testing
{
    [DisallowMultipleComponent]
    public sealed class EnemyTestMovementSystem : MonoBehaviour
    {
        private const float SimulationStep = 1f / 30f;
        private const int MaxTicksPerFrame = 8;

        [SerializeField] private EnemyTestActor[] actors =
            Array.Empty<EnemyTestActor>();
        private float accumulator;

        public int ActorCount => actors == null ? 0 : actors.Length;

        private void Awake()
        {
            if (actors == null || actors.Length == 0)
            {
                actors = FindObjectsOfType<EnemyTestActor>();
                Array.Sort(actors, CompareActors);
            }

            InitializeActiveActors();
        }

        public void Configure(EnemyTestActor[] movementActors)
        {
            if (movementActors == null || movementActors.Length == 0)
            {
                actors = Array.Empty<EnemyTestActor>();
            }
            else
            {
                actors = new EnemyTestActor[movementActors.Length];
                Array.Copy(movementActors, actors, movementActors.Length);
            }

            accumulator = 0f;
            if (Application.isPlaying)
            {
                InitializeActiveActors();
            }
        }

        public void RegisterActor(EnemyTestActor actor)
        {
            if (actor == null)
            {
                return;
            }

            actors = actors ?? Array.Empty<EnemyTestActor>();
            for (int i = 0; i < actors.Length; i++)
            {
                if (actors[i] == actor)
                {
                    return;
                }
            }

            int previousLength = actors.Length;
            Array.Resize(ref actors, previousLength + 1);
            actors[previousLength] = actor;
        }

        private void InitializeActiveActors()
        {
            if (actors == null)
            {
                actors = Array.Empty<EnemyTestActor>();
                return;
            }

            for (int i = 0; i < actors.Length; i++)
            {
                EnemyTestActor actor = actors[i];
                if (actor != null &&
                    actor.gameObject.activeInHierarchy)
                {
                    actor.InitializeRoute();
                }
            }
        }

        private void Update()
        {
            accumulator += Mathf.Min(Time.unscaledDeltaTime, 0.25f);

            int processedTicks = 0;
            while (accumulator >= SimulationStep && processedTicks < MaxTicksPerFrame)
            {
                for (int i = 0; i < actors.Length; i++)
                {
                    EnemyTestActor actor = actors[i];
                    if (actor == null ||
                        !actor.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    actor.Simulate(SimulationStep);
                }

                accumulator -= SimulationStep;
                processedTicks++;
            }

            if (processedTicks == MaxTicksPerFrame)
            {
                accumulator = 0f;
            }
        }

        private static int CompareActors(
            EnemyTestActor left,
            EnemyTestActor right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            return string.CompareOrdinal(left.name, right.name);
        }
    }
}
