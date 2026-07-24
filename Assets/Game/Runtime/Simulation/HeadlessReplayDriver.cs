using System;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.Simulation
{
    public static class HeadlessReplayDriver
    {
        public static GameSimulation Create(
            CompiledContent content,
            ulong seed)
        {
            var simulation = new GameSimulation();
            simulation.Initialize(content, seed);
            Require(
                simulation.Submit(
                    GameCommand.ChooseStartingTower("ballista")));

            int[] buildPoints = { 0, 2, 3, 4 };
            for (int i = 0; i < buildPoints.Length; i++)
            {
                Require(
                    simulation.Submit(
                        GameCommand.PlaceTower(
                            "ballista",
                            buildPoints[i])));
            }

            Require(
                simulation.Submit(
                    GameCommand.EquipCard(0, 0, 0)));
            Require(
                simulation.Submit(
                    GameCommand.EquipCard(1, 0, 1)));
            Require(
                simulation.Submit(
                    GameCommand.EquipCard(2, 0, 2)));
            Require(simulation.Submit(GameCommand.StartWave()));
            return simulation;
        }

        public static void AdvanceRunTransition(
            GameSimulation simulation)
        {
            if (simulation.Phase == RunPhase.Draft)
            {
                Require(
                    simulation.Submit(
                        GameCommand.SelectDraft(0)));
            }

            if (simulation.Phase == RunPhase.Planning)
            {
                Require(
                    simulation.Submit(
                        GameCommand.StartWave()));
            }
        }

        public static ulong ComputeVictoryHash(
            CompiledContent content,
            ulong seed,
            int maximumTicks,
            out int processedTicks)
        {
            GameSimulation simulation = Create(content, seed);
            processedTicks = 0;
            while (processedTicks < maximumTicks &&
                   simulation.Phase != RunPhase.Victory &&
                   simulation.Phase != RunPhase.Defeat)
            {
                simulation.Step();
                processedTicks++;
                AdvanceRunTransition(simulation);
            }

            if (simulation.Phase != RunPhase.Victory)
            {
                throw new InvalidOperationException(
                    "Reference replay did not reach Victory within " +
                    maximumTicks + " ticks; final phase was " +
                    simulation.Phase + ".");
            }

            return simulation.ComputeStateHash();
        }

        private static void Require(CommandResult result)
        {
            if (!result.Accepted)
            {
                throw new InvalidOperationException(
                    result.Error + ": " + result.Message);
            }
        }
    }
}
