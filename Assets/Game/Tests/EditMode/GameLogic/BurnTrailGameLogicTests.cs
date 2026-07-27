using System;
using NUnit.Framework;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;
using UnityEditor;
using UnityEngine;

namespace RuleforgeTD.Tests.EditMode.GameLogic
{
    public sealed class BurnTrailGameLogicTests
    {
        private const string ContentAssetPath =
            "Assets/Game/Data/Logic/phase1-content.json";

        [Test]
        public void BurnTrail_CoversShortLaunchToImpactAndBurnsFollower()
        {
            ContentCatalogDto source = LoadContentDto();
            source.run.startingTowerChoices =
                new[] { "ballista" };
            source.run.initiallyUnlockedTowers =
                Array.Empty<string>();
            source.run.startingCards =
                new[] { "burn" };
            source.run.pathPointXMilli =
                new[] { 0, 20000 };
            source.run.pathPointYMilli =
                new[] { 0, 0 };
            source.run.buildSpotXMilli[0] = -2000;
            source.run.buildSpotYMilli[0] = 0;
            source.run.regularDraftWaveNumbers =
                Array.Empty<int>();
            source.run.bossCardPackWaveNumbers =
                Array.Empty<int>();
            source.waves = new[]
            {
                new WaveDefinitionDto
                {
                    id = "burn_trail_wave",
                    spawns = new[]
                    {
                        new WaveSpawnDto
                        {
                            enemyId = "raider",
                            count = 2,
                            firstSpawnTick = 0,
                            intervalTicks = 10
                        }
                    }
                }
            };

            TowerDefinitionDto ballista =
                FindTower(source, "ballista");
            ballista.attackWindupTicks = 0;
            ballista.cooldownTicks = 1000;
            ballista.rangeMilli = 5000;
            ballista.baseDamageMilli = 1;
            ballista.projectileSpeedMilliPerTick = 1000;
            ballista.projectileLifetimeTicks = 60;
            FindEnemy(source, "raider")
                .speedMilliPerTick = 100;

            EffectNodeDto burnNode =
                FindCard(source, "burn")
                    .projectileEffects[0];
            burnNode.amount2 = 5000;
            burnNode.amount3 = 60;
            burnNode.radiusMilli = 400;

            CompiledContent content =
                ContentCompiler.Compile(
                    source,
                    GameSimulation.IsEffectOperationSupported);
            var simulation = new GameSimulation();
            simulation.Initialize(content, 919UL);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower(
                        "ballista")));
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower(
                        "ballista",
                        0)));

            SimulationSnapshot planning =
                simulation.GetSnapshot();
            int towerId = planning.Towers[0].Id;
            int burnCardInstanceId =
                FindCardInstance(
                    planning,
                    content,
                    "burn");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.EquipCard(
                        burnCardInstanceId,
                        towerId,
                        0)));
            AssertAccepted(
                simulation.Submit(
                    GameCommand.StartWave()));

            HazardSnapshot trail = default(HazardSnapshot);
            bool sawTrail = false;
            bool followerBurned = false;
            int followerId = -1;
            int maximumHazardCount = 0;
            bool sawImpactPosition = false;
            SimPosition impactPosition =
                SimPosition.Origin;
            for (int step = 0; step < 30; step++)
            {
                simulation.Step();
                SimulationSnapshot snapshot =
                    simulation.GetSnapshot();
                maximumHazardCount = Math.Max(
                    maximumHazardCount,
                    snapshot.Hazards.Length);
                if (snapshot.Hazards.Length > 0)
                {
                    trail = snapshot.Hazards[0];
                    sawTrail = true;
                }

                if (!sawImpactPosition &&
                    snapshot.Enemies.Length > 0)
                {
                    EnemySnapshot firstEnemy =
                        snapshot.Enemies[0];
                    for (int enemyIndex = 1;
                         enemyIndex <
                         snapshot.Enemies.Length;
                         enemyIndex++)
                    {
                        if (snapshot.Enemies[enemyIndex].Id <
                            firstEnemy.Id)
                        {
                            firstEnemy =
                                snapshot.Enemies[enemyIndex];
                        }
                    }

                    if (ContainsStatus(
                            firstEnemy,
                            StatusType.Burn))
                    {
                        impactPosition =
                            firstEnemy.Position;
                        sawImpactPosition = true;
                    }
                }

                if (snapshot.Enemies.Length >= 2)
                {
                    followerId = Math.Max(
                        snapshot.Enemies[0].Id,
                        snapshot.Enemies[1].Id);
                    EnemySnapshot follower =
                        snapshot.Enemies[0].Id ==
                        followerId
                            ? snapshot.Enemies[0]
                            : snapshot.Enemies[1];
                    followerBurned =
                        ContainsStatus(
                            follower,
                            StatusType.Burn);
                    if (followerBurned)
                    {
                        break;
                    }
                }
            }

            Assert.That(sawTrail, Is.True);
            Assert.That(
                trail.StartPosition,
                Is.EqualTo(
                    planning.Towers[0].Position),
                "The first fire segment must begin at the launch point.");
            Assert.That(
                PathModel.DistanceMilli(
                    trail.StartPosition,
                    trail.EndPosition),
                Is.LessThan(burnNode.amount2),
                "A short shot must still flush its final fire segment.");
            Assert.That(
                trail.EndPosition.X.MilliUnits,
                Is.GreaterThanOrEqualTo(0),
                "The active segment must be extended through the impact end.");
            Assert.That(sawImpactPosition, Is.True);
            Assert.That(
                trail.EndPosition,
                Is.EqualTo(impactPosition),
                "The final fire endpoint must be the authoritative hit position.");
            Assert.That(
                trail.DurationTicks,
                Is.EqualTo(60));
            Assert.That(
                maximumHazardCount,
                Is.EqualTo(1),
                "Hit expiration must not add an overshoot segment past the target.");
            Assert.That(
                followerId,
                Is.GreaterThanOrEqualTo(0));
            Assert.That(
                followerBurned,
                Is.True,
                "An enemy entering the persisted trail must receive Burn.");
        }

        [Test]
        public void BurnTrail_StateRemainsDeterministic()
        {
            CompiledContent content =
                ContentCompiler.Compile(
                    LoadContentDto(),
                    GameSimulation.IsEffectOperationSupported);
            GameSimulation left =
                CreateBurnSimulation(content, 44UL);
            GameSimulation right =
                CreateBurnSimulation(content, 44UL);

            for (int step = 0; step < 80; step++)
            {
                left.Step();
                right.Step();
                Assert.That(
                    left.ComputeStateHash(),
                    Is.EqualTo(
                        right.ComputeStateHash()),
                    "Burn trail diverged at step " +
                    step + ".");
            }
        }

        private static GameSimulation CreateBurnSimulation(
            CompiledContent content,
            ulong seed)
        {
            var simulation = new GameSimulation();
            simulation.Initialize(content, seed);
            AssertAccepted(
                simulation.Submit(
                    GameCommand.ChooseStartingTower(
                        "ballista")));
            AssertAccepted(
                simulation.Submit(
                    GameCommand.PlaceTower(
                        "ballista",
                        0)));
            SimulationSnapshot snapshot =
                simulation.GetSnapshot();
            int cardId =
                FindCardInstance(
                    snapshot,
                    content,
                    "burn");
            AssertAccepted(
                simulation.Submit(
                    GameCommand.EquipCard(
                        cardId,
                        snapshot.Towers[0].Id,
                        0)));
            AssertAccepted(
                simulation.Submit(
                    GameCommand.StartWave()));
            return simulation;
        }

        private static ContentCatalogDto LoadContentDto()
        {
            TextAsset asset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    ContentAssetPath);
            Assert.That(asset, Is.Not.Null);
            ContentCatalogDto source =
                JsonUtility.FromJson<ContentCatalogDto>(
                    asset.text);
            Assert.That(source, Is.Not.Null);
            return source;
        }

        private static CardDefinitionDto FindCard(
            ContentCatalogDto source,
            string stableId)
        {
            for (int i = 0; i < source.cards.Length; i++)
            {
                if (source.cards[i].id == stableId)
                {
                    return source.cards[i];
                }
            }

            Assert.Fail("Missing card DTO '" + stableId + "'.");
            return null;
        }

        private static TowerDefinitionDto FindTower(
            ContentCatalogDto source,
            string stableId)
        {
            for (int i = 0; i < source.towers.Length; i++)
            {
                if (source.towers[i].id == stableId)
                {
                    return source.towers[i];
                }
            }

            Assert.Fail("Missing tower DTO '" + stableId + "'.");
            return null;
        }

        private static EnemyDefinitionDto FindEnemy(
            ContentCatalogDto source,
            string stableId)
        {
            for (int i = 0; i < source.enemies.Length; i++)
            {
                if (source.enemies[i].id == stableId)
                {
                    return source.enemies[i];
                }
            }

            Assert.Fail("Missing enemy DTO '" + stableId + "'.");
            return null;
        }

        private static int FindCardInstance(
            SimulationSnapshot snapshot,
            CompiledContent content,
            string stableId)
        {
            for (int i = 0; i < snapshot.Cards.Length; i++)
            {
                CardInstanceSnapshot card =
                    snapshot.Cards[i];
                if (content.GetCard(
                        card.DefinitionId)
                    .StableId == stableId)
                {
                    return card.Id;
                }
            }

            Assert.Fail(
                "Missing card instance '" +
                stableId + "'.");
            return -1;
        }

        private static bool ContainsStatus(
            in EnemySnapshot enemy,
            StatusType statusType)
        {
            for (int i = 0;
                 i < enemy.Statuses.Length;
                 i++)
            {
                if (enemy.Statuses[i] == statusType)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertAccepted(
            CommandResult result)
        {
            Assert.That(
                result.Accepted,
                Is.True,
                result.Error.ToString());
        }
    }
}
