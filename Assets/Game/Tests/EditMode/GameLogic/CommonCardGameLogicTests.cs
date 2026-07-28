using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Effects;
using RuleforgeTD.GameLogic.Simulation;
using UnityEditor;
using UnityEngine;

namespace RuleforgeTD.Tests.EditMode.GameLogic
{
    public sealed class CommonCardGameLogicTests
    {
        private const string ContentAssetPath =
            "Assets/Game/Data/Logic/phase1-content.json";

        private CompiledContent content;
        private GameSimulation simulation;

        [SetUp]
        public void SetUp()
        {
            TextAsset asset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    ContentAssetPath);
            Assert.That(asset, Is.Not.Null);
            ContentCatalogDto source =
                JsonUtility.FromJson<ContentCatalogDto>(
                    asset.text);
            content = ContentCompiler.Compile(
                source,
                GameSimulation.IsEffectOperationSupported);
            simulation = new GameSimulation();
            simulation.Initialize(content, 0xC0110FUL);
        }

        [Test]
        public void
            ImpactAndDeathVisualFlagsCaptureAppliedCardIdentity()
        {
            EnemyState enemy = CreateEnemy(
                7,
                1000,
                0,
                1000);
            GetPrivateField<List<EnemyState>>(
                simulation,
                "enemies").Add(enemy);
            simulation.MarkEnemyCardVisual(
                enemy.Id,
                "explode");
            enemy.Statuses.Add(new StatusInstance
            {
                InstanceId = 91,
                Type = StatusType.Burn,
                Stacks = 1,
                RemainingTicks = 30
            });

            ProjectileEffectVisualFlags deathFlags =
                GameSimulation.GetEnemyDeathVisualFlags(enemy);
            Assert.That(
                deathFlags.HasFlag(
                    ProjectileEffectVisualFlags.Explode),
                Is.True);
            Assert.That(
                deathFlags.HasFlag(
                    ProjectileEffectVisualFlags.Burn),
                Is.True);

            var projectile = new ProjectileState
            {
                Id = new EntityId(8),
                SourceTowerId = new TowerId(1),
                VisualFlags =
                    ProjectileEffectVisualFlags.Split |
                    ProjectileEffectVisualFlags.Poison,
                Alive = true
            };
            Assert.That(
                simulation.GetProjectileImpactVisualFlags(
                    projectile),
                Is.EqualTo(projectile.VisualFlags));
        }

        [Test]
        public void Ricochet_RedirectsToNearestUnhitEnemy_AndUsesGlobalCap()
        {
            List<EnemyState> enemies =
                GetPrivateField<List<EnemyState>>(
                    simulation,
                    "enemies");
            EnemyState hit = CreateEnemy(
                10,
                1000,
                0,
                1000);
            EnemyState nearest = CreateEnemy(
                11,
                1700,
                0,
                1700);
            EnemyState farther = CreateEnemy(
                12,
                2600,
                0,
                2600);
            enemies.Add(hit);
            enemies.Add(nearest);
            enemies.Add(farther);

            var projectile = new ProjectileState
            {
                Id = new EntityId(20),
                SourceTowerId = new TowerId(0),
                Position = SimPosition.Origin,
                TargetId = hit.Id,
                DirectionXBps = 10000,
                DamageMilli = 10000,
                SpeedMilliPerTick = 1000,
                LifetimeRemaining = 30,
                Alive = true
            };
            projectile.HitEnemies.Add(hit.Id.Value);
            GetPrivateField<List<ProjectileState>>(
                simulation,
                "projectiles").Add(projectile);

            EffectExecutionContext context = CreateContext(
                SubjectType.Projectile,
                projectile.Id,
                projectile.Id,
                cardInstanceId: 7);
            CompiledEffectNode node = Node(
                EffectOperation.ConfigureProjectileRicochet,
                amount: 99,
                amount2: 8000,
                radiusMilli: 5000);
            simulation.ConfigureProjectileRicochet(
                context,
                node);

            Assert.That(
                simulation.GetProjectileRicochetsRemaining(
                    projectile.Id),
                Is.EqualTo(
                    content.Safety
                        .MaxRicochetsPerProjectile));
            Assert.That(
                simulation.TryRicochetProjectile(
                    projectile,
                    hit),
                Is.True);
            Assert.That(
                projectile.TargetId,
                Is.EqualTo(nearest.Id));
            Assert.That(
                projectile.Position,
                Is.EqualTo(hit.Position));
            Assert.That(projectile.DamageMilli, Is.EqualTo(8000));
            Assert.That(
                simulation.GetProjectileRicochetsUsed(
                    projectile.Id),
                Is.EqualTo(1));

            SimulationEventBuffer events =
                simulation.ReadPresentationEvents();
            bool found = false;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Type ==
                    PresentationEventType.ProjectileRicochet)
                {
                    found = true;
                    Assert.That(
                        events[i].SubjectId,
                        Is.EqualTo(nearest.Id.Value));
                    Assert.That(
                        events[i].SourceId,
                        Is.EqualTo(projectile.Id.Value));
                    Assert.That(
                        events[i].ContentId,
                        Is.EqualTo("ricochet"));
                }
            }
            Assert.That(found, Is.True);
        }

        [Test]
        public void Bleed_MovementAccumulatesByMilliUnit_AndDealsPhysicalDamage()
        {
            EnemyState enemy = CreateEnemy(
                30,
                0,
                0,
                0);
            enemy.HealthMilli = 100000;
            enemy.MaxHealthMilli = 100000;
            GetPrivateField<List<EnemyState>>(
                simulation,
                "enemies").Add(enemy);

            EffectExecutionContext context = CreateContext(
                SubjectType.Enemy,
                enemy.Id,
                new EntityId(99),
                cardInstanceId: 8);
            simulation.ApplyBleed(
                context,
                Node(
                    EffectOperation.ApplyBleed,
                    amount: 1200,
                    durationTicks: 90,
                    maxStacks: 5));

            simulation.TriggerBleedFromMovement(
                enemy,
                600);
            Assert.That(enemy.HealthMilli, Is.EqualTo(100000));
            Assert.That(
                enemy.Statuses[0].NextTick,
                Is.EqualTo(600));

            simulation.TriggerBleedFromMovement(
                enemy,
                400);
            Assert.That(
                GetPrivateField<EventQueue>(
                    simulation,
                    "eventQueue").Count,
                Is.GreaterThan(0));
            simulation.Step();

            Assert.That(enemy.HealthMilli, Is.EqualTo(98800));
            Assert.That(
                enemy.Statuses[0].NextTick,
                Is.Zero);
        }

        [Test]
        public void AccelerateHomingAndDelay_UpdateProjectileDeterministically()
        {
            List<EnemyState> enemies =
                GetPrivateField<List<EnemyState>>(
                    simulation,
                    "enemies");
            EnemyState close = CreateEnemy(
                40,
                1000,
                0,
                1000);
            EnemyState priority = CreateEnemy(
                41,
                3000,
                0,
                3000);
            priority.Statuses.Add(new StatusInstance
            {
                InstanceId = 1,
                Type = StatusType.HomingPriority,
                RemainingTicks = 30,
                Stacks = 1,
                MaxStacks = 1
            });
            enemies.Add(close);
            enemies.Add(priority);

            var projectile = new ProjectileState
            {
                Id = new EntityId(50),
                SourceTowerId = new TowerId(0),
                Position = SimPosition.Origin,
                DamageMilli = 10000,
                SpeedMilliPerTick = 100,
                LifetimeRemaining = 30,
                Alive = true
            };
            GetPrivateField<List<ProjectileState>>(
                simulation,
                "projectiles").Add(projectile);
            EffectExecutionContext context = CreateContext(
                SubjectType.Projectile,
                projectile.Id,
                projectile.Id,
                cardInstanceId: 9);

            simulation.AccelerateProjectile(
                context,
                Node(
                    EffectOperation.AccelerateProjectile,
                    amount: 20000,
                    amount2: 1000,
                    limit: 5000));
            simulation.EnableProjectileHoming(context);
            simulation.DelayProjectile(
                context,
                Node(
                    EffectOperation.DelayProjectile,
                    amount: 15000,
                    durationTicks: 2));

            Assert.That(
                projectile.SpeedMilliPerTick,
                Is.EqualTo(200));
            Assert.That(
                projectile.TargetId,
                Is.EqualTo(priority.Id));
            Assert.That(projectile.Homing, Is.True);
            ProjectileEffectVisualFlags flags =
                simulation.GetCommonProjectileVisualFlags(
                    projectile);
            Assert.That(
                flags.HasFlag(
                    ProjectileEffectVisualFlags.Accelerate),
                Is.True);
            Assert.That(
                flags.HasFlag(
                    ProjectileEffectVisualFlags.Homing),
                Is.True);
            Assert.That(
                flags.HasFlag(
                    ProjectileEffectVisualFlags.Delay),
                Is.True);

            SimPosition previous = projectile.Position;
            projectile.Position =
                SimPosition.FromMilliUnits(1000, 0);
            simulation.RecordCommonProjectileMovement(
                projectile,
                previous);
            Assert.That(projectile.DamageMilli, Is.EqualTo(11000));
            Assert.That(
                simulation.GetProjectileTravelDistanceMilli(
                    projectile.Id),
                Is.EqualTo(1000));

            Assert.That(
                simulation.ShouldPauseProjectileForDelay(
                    projectile),
                Is.True);
            Assert.That(projectile.DamageMilli, Is.EqualTo(11000));
            Assert.That(
                simulation.GetProjectileDelayRemainingTicks(
                    projectile.Id),
                Is.EqualTo(1));
            Assert.That(
                simulation.ShouldPauseProjectileForDelay(
                    projectile),
                Is.True);
            Assert.That(projectile.DamageMilli, Is.EqualTo(16500));
            Assert.That(
                simulation.ShouldPauseProjectileForDelay(
                    projectile),
                Is.False);
        }

        [Test]
        public void AccelerateEnemy_IncreasesRewardOnce_AndDelayBlocksMovement()
        {
            EnemyState enemy = CreateEnemy(
                60,
                0,
                0,
                0);
            enemy.LineageId = new LineageId(5);
            enemy.RewardBudget = 100;
            GetPrivateField<List<EnemyState>>(
                simulation,
                "enemies").Add(enemy);
            GetPrivateField<Dictionary<int, LineageState>>(
                simulation,
                "lineages").Add(
                    enemy.LineageId.Value,
                    new LineageState
                    {
                        Id = enemy.LineageId,
                        BaseRewardBudget = 100,
                        MaxRewardBudget = 100,
                        LiveMembers = 1
                    });

            EffectExecutionContext context = CreateContext(
                SubjectType.Enemy,
                enemy.Id,
                enemy.Id,
                cardInstanceId: 10);
            CompiledEffectNode acceleration = Node(
                EffectOperation.AccelerateEnemy,
                amount: 15000,
                amount2: 2500,
                limit: 10000);
            simulation.AccelerateEnemy(
                context,
                acceleration);
            simulation.AccelerateEnemy(
                context,
                acceleration);

            LineageState lineage =
                GetPrivateField<Dictionary<int, LineageState>>(
                    simulation,
                    "lineages")[enemy.LineageId.Value];
            Assert.That(enemy.SpeedMultiplierBps, Is.EqualTo(22500));
            Assert.That(enemy.RewardBudget, Is.EqualTo(125));
            Assert.That(lineage.MaxRewardBudget, Is.EqualTo(125));
            Assert.That(
                lineage.AppliedRewardAugments.Count,
                Is.EqualTo(1));

            simulation.ApplyDelay(
                context,
                Node(
                    EffectOperation.ApplyDelay,
                    durationTicks: 3,
                    maxStacks: 1));
            Assert.That(simulation.IsEnemyDelayed(enemy), Is.True);
        }

        private static EnemyState CreateEnemy(
            int id,
            long x,
            long y,
            long pathProgress)
        {
            return new EnemyState
            {
                Id = new EntityId(id),
                DefinitionId = new EnemyDefinitionId(0),
                LineageId = new LineageId(id),
                Position = SimPosition.FromMilliUnits(x, y),
                PathProgressMilli = pathProgress,
                HealthMilli = 100000,
                MaxHealthMilli = 100000,
                BaseSpeedMilliPerTick = 100,
                SpeedMultiplierBps = 10000,
                Alive = true
            };
        }

        private static EffectExecutionContext CreateContext(
            SubjectType subjectType,
            EntityId subjectId,
            EntityId sourceEntityId,
            int cardInstanceId)
        {
            return new EffectExecutionContext(
                subjectType,
                subjectId,
                new TowerId(0),
                new CardId(0),
                cardInstanceId,
                sourceEntityId,
                ChainId.Invalid,
                ActivationId.Invalid,
                EventId.Invalid,
                0,
                0,
                0);
        }

        private static CompiledEffectNode Node(
            EffectOperation operation,
            int amount = 0,
            int amount2 = 0,
            int amount3 = 0,
            int durationTicks = 0,
            int intervalTicks = 0,
            int maxStacks = 0,
            int radiusMilli = 0,
            int limit = 0,
            int chanceBps = 0)
        {
            return new CompiledEffectNode(
                operation,
                amount,
                amount2,
                amount3,
                durationTicks,
                intervalTicks,
                maxStacks,
                radiusMilli,
                limit,
                chanceBps,
                null);
        }

        private static T GetPrivateField<T>(
            GameSimulation target,
            string fieldName)
        {
            FieldInfo field = typeof(GameSimulation).GetField(
                fieldName,
                BindingFlags.Instance |
                BindingFlags.NonPublic);
            Assert.That(
                field,
                Is.Not.Null,
                "Missing GameSimulation field '{0}'.",
                fieldName);
            return (T)field.GetValue(target);
        }
    }
}
