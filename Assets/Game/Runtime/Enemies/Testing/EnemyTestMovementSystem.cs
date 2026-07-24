using UnityEngine;

namespace RuleforgeTD.Enemies.Testing
{
    [DisallowMultipleComponent]
    public sealed class EnemyTestMovementSystem : MonoBehaviour
    {
        private const float SimulationStep = 1f / 30f;
        private const int MaxTicksPerFrame = 8;

        private EnemyTestActor[] actors;
        private float accumulator;

        private void Awake()
        {
            actors = FindObjectsOfType<EnemyTestActor>();
            for (int i = 0; i < actors.Length; i++)
            {
                actors[i].InitializeRoute();
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
                    actors[i].Simulate(SimulationStep);
                }

                accumulator -= SimulationStep;
                processedTicks++;
            }

            if (processedTicks == MaxTicksPerFrame)
            {
                accumulator = 0f;
            }
        }
    }
}
