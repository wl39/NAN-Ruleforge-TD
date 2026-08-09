using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Effects;
using RuleforgeTD.GameLogic.Simulation;
using UnityEditor;
using UnityEngine;

namespace RuleforgeTD.Tests.EditMode.GameLogic
{
    public sealed class StageOpeningBalanceGameLogicTests
    {
        private const int MaximumFirstWaveSteps = 4000;

        [TestCase(
            "Assets/Game/Data/Logic/stage02-content.json",
            "Stage 02")]
        [TestCase(
            "Assets/Game/Data/Logic/stage03-content.json",
            "Stage 03")]
        [Timeout(30000)]
        public void FirstWave_KeepsSwarmAndFundsASecondBallista(
            string contentPath,
            string stageLabel)
        {
            CompiledContent content = LoadContent(contentPath);
            Assert.That(
                content.GetWave(0).TotalSpawnCount,
                Is.EqualTo(35),
                stageLabel +
                " wave 1 must preserve the 35-enemy swarm.");
            var viableCards = new HashSet<string>();
            var report = new StringBuilder();

            for (int cardIndex = 0;
                 cardIndex < content.Run.StartingCards.Length;
                 cardIndex++)
            {
                string cardStableId = content.GetCard(
                    content.Run.StartingCards[cardIndex]).StableId;
                int bestBaseHealth = -1;
                int bestSpotIndex = -1;
                int bestKillCount = -1;
                int bestLeakCount = int.MaxValue;
                int bestGold = 0;
                int bestSecondBallistaCost = 0;
                int zeroLeakSpotCount = 0;

                for (int spotIndex = 0;
                     spotIndex < content.Run.BuildSpots.Length;
                     spotIndex++)
                {
                    FirstWaveResult result = SimulateFirstWave(
                        content,
                        cardStableId,
                        spotIndex);
                    if (result.BaseHealth > bestBaseHealth ||
                        result.BaseHealth == bestBaseHealth &&
                        result.KillCount > bestKillCount)
                    {
                        bestBaseHealth = result.BaseHealth;
                        bestSpotIndex = spotIndex;
                        bestKillCount = result.KillCount;
                        bestLeakCount = result.LeakCount;
                        bestGold = result.Gold;
                        bestSecondBallistaCost =
                            result.SecondBallistaCost;
                    }
                    if (result.BaseHealth == content.Run.BaseHealth &&
                        result.Phase != RunPhase.Defeat &&
                        result.CanBuildSecondBallista)
                    {
                        zeroLeakSpotCount++;
                    }
                }

                if (zeroLeakSpotCount > 0)
                {
                    viableCards.Add(cardStableId);
                }
                report.Append(cardStableId)
                    .Append(": best base=")
                    .Append(bestBaseHealth)
                    .Append(" at spot ")
                    .Append(bestSpotIndex)
                    .Append(", kills=")
                    .Append(bestKillCount)
                    .Append(", leaks before end=")
                    .Append(bestLeakCount)
                    .Append(", gold=")
                    .Append(bestGold)
                    .Append("/")
                    .Append(bestSecondBallistaCost)
                    .Append(", zero-leak spots=")
                    .Append(zeroLeakSpotCount)
                    .AppendLine();
            }

            TestContext.WriteLine(stageLabel + " opening balance:\n" + report);
            Assert.That(
                viableCards.Count,
                Is.GreaterThanOrEqualTo(2),
                stageLabel +
                " must let at least two different starting cards clear " +
                "wave 1 with one free level-1 ballista, no bonus gold, " +
                "no leaks, and enough earned gold for a second ballista.\n" +
                report);
        }

        private static FirstWaveResult SimulateFirstWave(
            CompiledContent content,
            string cardStableId,
            int buildSpotIndex)
        {
            var simulation = new GameSimulation();
            simulation.Initialize(content, 0x0F1E57A6UL);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower("ballista")),
                "choose starting ballista");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower(
                        "ballista",
                        buildSpotIndex)),
                "place starting ballista");

            SimulationSnapshot snapshot = simulation.GetSnapshot();
            Assert.That(snapshot.Towers, Has.Length.EqualTo(1));
            int cardInstanceId = FindOwnedCardInstance(
                snapshot,
                content,
                cardStableId);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.EquipCard(
                        cardInstanceId,
                        snapshot.Towers[0].Id,
                        0)),
                "equip starter card '" + cardStableId + "'");
            AssertAccepted(
                simulation.Submit(GameCommand.StartWave()),
                "start first wave");

            int steps = 0;
            int killCount = 0;
            int leakCount = 0;
            while (simulation.Phase == RunPhase.Combat &&
                   steps < MaximumFirstWaveSteps)
            {
                simulation.Step();
                SimulationEventBuffer events =
                    simulation.ReadPresentationEvents();
                for (int eventIndex = 0;
                     eventIndex < events.Count;
                     eventIndex++)
                {
                    if (events[eventIndex].Type ==
                        PresentationEventType.EnemyDied)
                    {
                        killCount++;
                    }
                    else if (events[eventIndex].Type ==
                             PresentationEventType.EnemyLeaked)
                    {
                        leakCount++;
                    }
                }
                steps++;
            }

            Assert.That(
                simulation.Phase,
                Is.Not.EqualTo(RunPhase.Combat),
                "First wave exceeded the simulation step budget for " +
                cardStableId + " at spot " + buildSpotIndex + ".");
            snapshot = simulation.GetSnapshot();
            TowerConstructionQuote secondBallista =
                simulation.GetTowerConstructionQuote("ballista");
            return new FirstWaveResult(
                snapshot.BaseHealth,
                snapshot.Phase,
                killCount,
                leakCount,
                snapshot.Gold,
                secondBallista.Cost,
                secondBallista.CanConstruct);
        }

        private static int FindOwnedCardInstance(
            SimulationSnapshot snapshot,
            CompiledContent content,
            string cardStableId)
        {
            for (int cardIndex = 0;
                 cardIndex < snapshot.Cards.Length;
                 cardIndex++)
            {
                CardInstanceSnapshot card = snapshot.Cards[cardIndex];
                if (content.GetCard(card.DefinitionId).StableId ==
                    cardStableId)
                {
                    return card.Id;
                }
            }

            Assert.Fail(
                "No owned starting card instance exists for '{0}'.",
                cardStableId);
            return -1;
        }

        private static CompiledContent LoadContent(string contentPath)
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(
                contentPath);
            Assert.That(asset, Is.Not.Null, contentPath);
            ContentCatalogDto source =
                JsonUtility.FromJson<ContentCatalogDto>(asset.text);
            Assert.That(source, Is.Not.Null, contentPath);
            return EffectContentCompiler.Compile(
                source,
                GameSimulation.IsEffectOperationSupported);
        }

        private static void AssertAccepted(
            CommandResult result,
            string context)
        {
            Assert.That(
                result.Accepted,
                Is.True,
                context + ": " + result.Error + " / " + result.Message);
        }

        private readonly struct FirstWaveResult
        {
            public FirstWaveResult(
                int baseHealth,
                RunPhase phase,
                int killCount,
                int leakCount,
                int gold,
                int secondBallistaCost,
                bool canBuildSecondBallista)
            {
                BaseHealth = baseHealth;
                Phase = phase;
                KillCount = killCount;
                LeakCount = leakCount;
                Gold = gold;
                SecondBallistaCost = secondBallistaCost;
                CanBuildSecondBallista = canBuildSecondBallista;
            }

            public int BaseHealth { get; }
            public RunPhase Phase { get; }
            public int KillCount { get; }
            public int LeakCount { get; }
            public int Gold { get; }
            public int SecondBallistaCost { get; }
            public bool CanBuildSecondBallista { get; }
        }
    }
}
