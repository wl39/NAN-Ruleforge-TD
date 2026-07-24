using System;
using RuleforgeTD.GameLogic.Simulation;
using UnityEngine;

namespace RuleforgeTD.Simulation
{
    [DisallowMultipleComponent]
    public sealed class HeadlessSimulationHarness : MonoBehaviour
    {
        [SerializeField] private TextAsset contentJson;
        [SerializeField] private ulong seed = 12345UL;
        [SerializeField, Min(1)] private int ticksPerFrame = 30;
        [SerializeField, Min(1)] private int maximumTicks = 18000;
        [SerializeField] private ulong expectedFinalHash;

        private GameSimulation simulation;
        private int processedTicks;
        private bool completed;

        public GameSimulation Simulation => simulation;
        public bool Completed => completed;
        public ulong FinalHash { get; private set; }
        public string Failure { get; private set; }

        private void Start()
        {
            try
            {
                simulation = HeadlessReplayDriver.Create(
                    LogicContentJsonLoader.Load(contentJson),
                    seed);
            }
            catch (Exception exception)
            {
                Failure = exception.ToString();
                completed = true;
                Debug.LogError("RULEFORGE_HEADLESS_FAILED " + Failure);
            }
        }

        private void Update()
        {
            if (completed || simulation == null)
            {
                return;
            }

            try
            {
                int count = Math.Min(ticksPerFrame, maximumTicks - processedTicks);
                for (int i = 0; i < count; i++)
                {
                    simulation.Step();
                    processedTicks++;
                    HandleRunTransition();

                    if (simulation.Phase == RunPhase.Victory ||
                        simulation.Phase == RunPhase.Defeat)
                    {
                        Complete();
                        return;
                    }
                }

                if (processedTicks >= maximumTicks)
                {
                    throw new InvalidOperationException(
                        "Headless replay did not terminate within " + maximumTicks + " ticks.");
                }
            }
            catch (Exception exception)
            {
                Failure = exception.ToString();
                completed = true;
                Debug.LogError("RULEFORGE_HEADLESS_FAILED " + Failure);
            }
        }

        public void Configure(
            TextAsset sourceContent,
            ulong replaySeed,
            int simulationTicksPerFrame,
            int tickLimit,
            ulong expectedHash)
        {
            contentJson = sourceContent;
            seed = replaySeed;
            ticksPerFrame = Mathf.Max(1, simulationTicksPerFrame);
            maximumTicks = Mathf.Max(1, tickLimit);
            expectedFinalHash = expectedHash;
        }

        private void HandleRunTransition()
        {
            HeadlessReplayDriver.AdvanceRunTransition(simulation);
        }

        private void Complete()
        {
            FinalHash = simulation.ComputeStateHash();
            if (simulation.Phase != RunPhase.Victory)
            {
                throw new InvalidOperationException(
                    "Headless replay ended in " + simulation.Phase +
                    " instead of Victory.");
            }
            if (expectedFinalHash != 0UL &&
                FinalHash != expectedFinalHash)
            {
                throw new InvalidOperationException(
                    "Editor/WebGL state hash mismatch. Expected " +
                    expectedFinalHash.ToString("X16") + " but received " +
                    FinalHash.ToString("X16") + ".");
            }

            completed = true;
            Debug.Log(
                "RULEFORGE_HEADLESS_OK phase=" + simulation.Phase +
                " tick=" + simulation.Tick +
                " hash=" + FinalHash.ToString("X16") +
                " expected=" + expectedFinalHash.ToString("X16"));
        }
    }
}
