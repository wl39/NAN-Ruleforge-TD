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

            Require(
                simulation.Submit(
                    GameCommand.PlaceTower(
                        "ballista",
                        0)));

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

            if (simulation.Phase == RunPhase.CardPackChoice)
            {
                Require(
                    simulation.Submit(
                        GameCommand.SelectCardPack(0)));
            }

            if (simulation.Phase == RunPhase.CardPackLoadout)
            {
                Require(
                    simulation.Submit(
                        GameCommand.ResumeCardPackCombat()));
            }

            if (simulation.Phase == RunPhase.Planning)
            {
                TryBuildAffordableTower(simulation);
                EquipAvailableCards(simulation);
                Require(
                    simulation.Submit(
                        GameCommand.StartWave()));
            }
        }

        private static void EquipAvailableCards(
            GameSimulation simulation)
        {
            while (true)
            {
                SimulationSnapshot snapshot =
                    simulation.GetSnapshot();
                bool equippedOne = false;
                for (int cardIndex = 0;
                     cardIndex < snapshot.Cards.Length &&
                     !equippedOne;
                     cardIndex++)
                {
                    CardInstanceSnapshot card =
                        snapshot.Cards[cardIndex];
                    if (card.Equipped)
                    {
                        continue;
                    }

                    for (int towerIndex = 0;
                         towerIndex < snapshot.Towers.Length &&
                         !equippedOne;
                         towerIndex++)
                    {
                        TowerSnapshot tower =
                            snapshot.Towers[towerIndex];
                        for (int slot = 0;
                             slot < tower.CardInstanceIds.Length;
                             slot++)
                        {
                            if (tower.CardInstanceIds[slot] >= 0)
                            {
                                continue;
                            }

                            CommandResult result =
                                simulation.Submit(
                                    GameCommand.EquipCard(
                                        card.Id,
                                        tower.Id,
                                        slot));
                            if (result.Accepted)
                            {
                                equippedOne = true;
                                break;
                            }
                        }
                    }
                }

                if (!equippedOne)
                {
                    return;
                }
            }
        }

        private static void TryBuildAffordableTower(
            GameSimulation simulation)
        {
            while (true)
            {
                SimulationSnapshot snapshot =
                    simulation.GetSnapshot();
                if (snapshot.Gold <
                        snapshot.TowerConstructionCost ||
                    snapshot.Towers.Length >=
                        snapshot.BuildSpots.Length)
                {
                    return;
                }

                bool placed = false;
                for (int spot = 0;
                     spot < snapshot.BuildSpots.Length;
                     spot++)
                {
                    bool occupied = false;
                    for (int tower = 0;
                         tower < snapshot.Towers.Length;
                         tower++)
                    {
                        if (snapshot.Towers[tower].
                            BuildPointIndex == spot)
                        {
                            occupied = true;
                            break;
                        }
                    }

                    if (!occupied)
                    {
                        Require(
                            simulation.Submit(
                                GameCommand.PlaceTower(
                                    "ballista",
                                    spot)));
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                {
                    return;
                }
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
