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
    public sealed class UncommonCardGameLogicTests
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
            simulation.Initialize(content, 0xA11CEUL);
            SetPrivateField(simulation, "nextEntityId", 10000);
        }

        [Test]
        public void RegistryAndProjectileVisuals_CoverAllUncommonOperations()
        {
            EffectOperation[] projectileOperations =
            {
                EffectOperation.BindCurse,
                EffectOperation.CreateBindTrap,
                EffectOperation.MakeAirborneProjectile,
                EffectOperation.BindShock,
                EffectOperation.BindFreeze,
                EffectOperation.CreateAfterimageProjectile,
                EffectOperation.EnableProjectilePulse,
                EffectOperation.EnableProjectileMagnet,
                EffectOperation.EnableProjectileReflect,
                EffectOperation.EnableProjectileContagion,
                EffectOperation.BindSeal,
                EffectOperation.BindCorrosion,
                EffectOperation.EnableProjectileOrbit,
                EffectOperation.BindLifesteal,
                EffectOperation.BindFear
            };
            EffectOperation[] enemyOperations =
            {
                EffectOperation.ApplyCurse,
                EffectOperation.ApplyBind,
                EffectOperation.ApplyAirborne,
                EffectOperation.ApplyShock,
                EffectOperation.ApplyFreeze,
                EffectOperation.ApplyAfterimage,
                EffectOperation.ApplyEnemyPulse,
                EffectOperation.ApplyEnemyMagnet,
                EffectOperation.ApplyEnemyReflect,
                EffectOperation.ApplyEnemyContagion,
                EffectOperation.ApplySeal,
                EffectOperation.ApplyCorrosion,
                EffectOperation.ApplyEnemyOrbit,
                EffectOperation.ApplyLifesteal,
                EffectOperation.ApplyFear
            };
            for (int i = 0; i < projectileOperations.Length; i++)
            {
                Assert.That(
                    GameSimulation.IsEffectOperationSupported(
                        projectileOperations[i]),
                    Is.True,
                    projectileOperations[i].ToString());
                Assert.That(
                    GameSimulation.IsEffectOperationSupported(
                        enemyOperations[i]),
                    Is.True,
                    enemyOperations[i].ToString());
            }

            ProjectileState projectile = AddProjectile(
                simulation,
                200,
                SimPosition.Origin);
            EffectExecutionContext context = Context(
                SubjectType.Projectile,
                projectile.Id,
                projectile.Id);
            for (int i = 0; i < projectileOperations.Length; i++)
            {
                simulation.ExecuteUncommonEffect(
                    context,
                    projectileOperations[i],
                    Node(
                        projectileOperations[i],
                        amount: 1000,
                        amount2: 1000,
                        amount3: 2,
                        durationTicks: 5,
                        intervalTicks: 1,
                        maxStacks: 2,
                        radiusMilli: 1000,
                        limit: 2));
            }

            ProjectileEffectVisualFlags expected =
                ProjectileEffectVisualFlags.Curse |
                ProjectileEffectVisualFlags.Bind |
                ProjectileEffectVisualFlags.Airborne |
                ProjectileEffectVisualFlags.Shock |
                ProjectileEffectVisualFlags.Freeze |
                ProjectileEffectVisualFlags.Afterimage |
                ProjectileEffectVisualFlags.Pulse |
                ProjectileEffectVisualFlags.Magnet |
                ProjectileEffectVisualFlags.Reflect |
                ProjectileEffectVisualFlags.Contagion |
                ProjectileEffectVisualFlags.Seal |
                ProjectileEffectVisualFlags.Corrosion |
                ProjectileEffectVisualFlags.Orbit |
                ProjectileEffectVisualFlags.Lifesteal |
                ProjectileEffectVisualFlags.Fear;
            Assert.That(
                simulation.GetProjectileUncommonEffectFlags(
                    projectile.Id),
                Is.EqualTo(expected));
        }

        [Test]
        public void Curse_ExtendsDebuffsAndAmplifiesOnlyStatusDamage()
        {
            EnemyState enemy = AddEnemy(
                simulation,
                300,
                SimPosition.Origin);
            EffectExecutionContext context = Context(
                SubjectType.Enemy,
                enemy.Id,
                enemy.Id);
            simulation.ExecuteUncommonEffect(
                context,
                EffectOperation.ApplyCurse,
                Node(
                    EffectOperation.ApplyCurse,
                    amount: 2000,
                    durationTicks: 30,
                    maxStacks: 3,
                    radiusMilli: 1500,
                    limit: 5000));
            simulation.ApplyStatus(
                context,
                StatusType.Burn,
                Node(
                    EffectOperation.ApplyBurn,
                    amount: 1000,
                    durationTicks: 10,
                    intervalTicks: 2,
                    maxStacks: 3));

            StatusInstance burn = FindStatus(
                enemy,
                StatusType.Burn);
            Assert.That(burn, Is.Not.Null);
            Assert.That(
                burn.RemainingTicks,
                Is.EqualTo(13),
                "10 ticks increased by 20%, plus the status-system compensation tick.");
            Assert.That(
                simulation.ModifyDamageForUncommonStatuses(
                    enemy,
                    1000,
                    EventTags.DamageOverTime),
                Is.EqualTo(1200));
            Assert.That(
                simulation.ModifyDamageForUncommonStatuses(
                    enemy,
                    1000,
                    EventTags.SingleTarget),
                Is.EqualTo(1000));

            simulation.ExecuteUncommonEffect(
                context,
                EffectOperation.ApplyFear,
                Node(
                    EffectOperation.ApplyFear,
                    amount: 10000,
                    durationTicks: 1,
                    intervalTicks: 4,
                    maxStacks: 1,
                    limit: 12500));
            StatusInstance fear = FindStatus(
                enemy,
                StatusType.Fear);
            fear.RemainingTicks = 0;
            simulation.HandleUncommonStatusExpired(enemy, fear);
            StatusInstance haste = FindStatus(
                enemy,
                StatusType.FearHaste);
            Assert.That(haste, Is.Not.Null);
            Assert.That(
                haste.RemainingTicks,
                Is.EqualTo(9),
                "Curse must not lengthen the beneficial post-fear haste.");
        }

        [Test]
        public void BindAndAirborne_BlockMovementAndLandWithCollisionDamage()
        {
            EnemyState bound = AddEnemy(
                simulation,
                400,
                SimPosition.FromMilliUnits(1000, 0));
            bound.BaseSpeedMilliPerTick = 100;
            EffectExecutionContext boundContext = Context(
                SubjectType.Enemy,
                bound.Id,
                bound.Id);
            simulation.ExecuteUncommonEffect(
                boundContext,
                EffectOperation.ApplyBind,
                Node(
                    EffectOperation.ApplyBind,
                    amount: 30,
                    durationTicks: 20,
                    maxStacks: 1));
            Assert.That(
                simulation.TryProcessUncommonEnemyMovement(bound),
                Is.True);
            Assert.That(bound.PathProgressMilli, Is.Zero);

            EnemyState airborne = AddEnemy(
                simulation,
                401,
                SimPosition.FromMilliUnits(2000, 0));
            EnemyState nearby = AddEnemy(
                simulation,
                402,
                SimPosition.FromMilliUnits(2500, 0));
            RebuildSpatialIndex(simulation);
            EffectExecutionContext airborneContext = Context(
                SubjectType.Enemy,
                airborne.Id,
                airborne.Id);
            simulation.ExecuteUncommonEffect(
                airborneContext,
                EffectOperation.ApplyAirborne,
                Node(
                    EffectOperation.ApplyAirborne,
                    amount: 30,
                    amount2: 3000,
                    durationTicks: 1,
                    radiusMilli: 1000,
                    limit: 4));
            StatusInstance status = FindStatus(
                airborne,
                StatusType.Airborne);
            Assert.That(status, Is.Not.Null);
            Assert.That(status.ArmorIgnoreBps, Is.EqualTo(3000));
            Assert.That(
                simulation.TryProcessUncommonEnemyMovement(airborne),
                Is.True);

            status.RemainingTicks = 0;
            simulation.HandleUncommonStatusExpired(
                airborne,
                status);
            airborne.Statuses.Remove(status);
            simulation.Step();
            Assert.That(
                FindStatus(airborne, StatusType.Airborne),
                Is.Null);
            Assert.That(nearby.HealthMilli, Is.LessThan(100000));
            AssertPresentation(
                simulation,
                "airborne_land");
        }

        [Test]
        public void ShockAndFreeze_UseStacksChainsAndPostFreezeImmunity()
        {
            EnemyState source = AddEnemy(
                simulation,
                500,
                SimPosition.Origin);
            EnemyState chained = AddEnemy(
                simulation,
                501,
                SimPosition.FromMilliUnits(500, 0));
            RebuildSpatialIndex(simulation);
            EffectExecutionContext context = Context(
                SubjectType.Enemy,
                source.Id,
                source.Id);
            CompiledEffectNode shock = Node(
                EffectOperation.ApplyShock,
                amount: 3000,
                durationTicks: 30,
                maxStacks: 3,
                radiusMilli: 1000,
                limit: 2);
            for (int i = 0; i < 3; i++)
            {
                simulation.ExecuteUncommonEffect(
                    context,
                    EffectOperation.ApplyShock,
                    shock);
            }
            Assert.That(
                FindStatus(source, StatusType.Shock).Stacks,
                Is.Zero);
            simulation.Step();
            Assert.That(chained.HealthMilli, Is.LessThan(100000));
            AssertPresentation(simulation, "shock_chain");

            CompiledEffectNode freeze = Node(
                EffectOperation.ApplyFreeze,
                amount: 1,
                amount3: 2,
                durationTicks: 30,
                intervalTicks: 4,
                maxStacks: 3,
                radiusMilli: 1000,
                limit: 4);
            for (int i = 0; i < 3; i++)
            {
                simulation.ExecuteUncommonEffect(
                    context,
                    EffectOperation.ApplyFreeze,
                    freeze);
            }
            Assert.That(
                FindStatus(source, StatusType.Chill),
                Is.Null);
            StatusInstance frozen = FindStatus(
                source,
                StatusType.Frozen);
            Assert.That(frozen, Is.Not.Null);
            Assert.That(frozen.TickInterval, Is.EqualTo(4));
            frozen.RemainingTicks = 0;
            simulation.HandleUncommonStatusExpired(
                source,
                frozen);
            StatusInstance immunity = FindStatus(
                source,
                StatusType.FreezeImmunity);
            Assert.That(immunity, Is.Not.Null);
            Assert.That(immunity.RemainingTicks, Is.EqualTo(5));
        }

        [Test]
        public void Afterimage_InheritsProjectileRuntimeAndTransfersPhantomDamage()
        {
            ProjectileState original = AddProjectile(
                simulation,
                600,
                SimPosition.Origin);
            EffectExecutionContext projectileContext = Context(
                SubjectType.Projectile,
                original.Id,
                original.Id);
            simulation.AccelerateProjectile(
                projectileContext,
                Node(
                    EffectOperation.AccelerateProjectile,
                    amount: 15000,
                    amount2: 1000,
                    limit: 5000));
            simulation.ExecuteUncommonEffect(
                projectileContext,
                EffectOperation.CreateAfterimageProjectile,
                Node(
                    EffectOperation.CreateAfterimageProjectile,
                    amount: 5000,
                    durationTicks: 2));

            List<ProjectileState> projectiles =
                GetPrivateField<List<ProjectileState>>(
                    simulation,
                    "projectiles");
            Assert.That(projectiles, Has.Count.EqualTo(2));
            ProjectileState ghost = projectiles[1];
            Assert.That(ghost.DamageMilli, Is.EqualTo(5000));
            Assert.That(
                GetPrivateField<
                    Dictionary<int, List<ProjectileAccelerationRuntime>>>(
                    simulation,
                    "commonProjectileAccelerations")
                    .ContainsKey(ghost.Id.Value),
                Is.True);
            Assert.That(
                simulation.ProcessUncommonProjectileTick(ghost),
                Is.True);

            EnemyState originalEnemy = AddEnemy(
                simulation,
                601,
                SimPosition.FromMilliUnits(5000, 0),
                addLineage: true);
            EffectExecutionContext enemyContext = Context(
                SubjectType.Enemy,
                originalEnemy.Id,
                originalEnemy.Id);
            simulation.ExecuteUncommonEffect(
                enemyContext,
                EffectOperation.ApplyAfterimage,
                Node(
                    EffectOperation.ApplyAfterimage,
                    amount: 5000,
                    durationTicks: 30,
                    radiusMilli: 750));
            List<EnemyState> enemies =
                GetPrivateField<List<EnemyState>>(
                    simulation,
                    "enemies");
            EnemyState phantom = enemies[enemies.Count - 1];
            Assert.That(phantom.Id, Is.Not.EqualTo(originalEnemy.Id));
            Assert.That(phantom.WaveProgressBudget, Is.Zero);

            simulation.HandleUncommonDamageApplied(
                phantom,
                DamageEvent(
                    simulation,
                    phantom.Id,
                    phantom.Id,
                    EventTags.SingleTarget),
                1000);
            simulation.Step();
            Assert.That(
                originalEnemy.HealthMilli,
                Is.EqualTo(99500));
        }

        [Test]
        public void ProjectilePulseMagnetAndContagion_ChangeCombatSubjects()
        {
            EnemyState pulseTarget = AddEnemy(
                simulation,
                700,
                SimPosition.FromMilliUnits(1000, 0));
            RebuildSpatialIndex(simulation);
            ProjectileState pulse = AddProjectile(
                simulation,
                701,
                SimPosition.Origin);
            EffectExecutionContext pulseContext = Context(
                SubjectType.Projectile,
                pulse.Id,
                pulse.Id);
            simulation.ExecuteUncommonEffect(
                pulseContext,
                EffectOperation.EnableProjectilePulse,
                Node(
                    EffectOperation.EnableProjectilePulse,
                    amount: 2500,
                    durationTicks: 30,
                    intervalTicks: 100,
                    radiusMilli: 2000,
                    limit: 4));
            UncommonProjectileEffectRuntime pulseRuntime =
                GetRuntime(
                    simulation,
                    pulse.Id,
                    EffectOperation.EnableProjectilePulse);
            pulseRuntime.NextTick = simulation.Tick;
            simulation.ProcessUncommonProjectileTick(pulse);
            simulation.Step();
            Assert.That(pulseTarget.HealthMilli, Is.LessThan(100000));
            AssertPresentation(simulation, "pulse");

            ProjectileState magnet = AddProjectile(
                simulation,
                702,
                SimPosition.FromMilliUnits(10000, 0));
            ProjectileState absorbed = AddProjectile(
                simulation,
                703,
                SimPosition.FromMilliUnits(10000, 0));
            long initialDamage = magnet.DamageMilli;
            simulation.ExecuteUncommonEffect(
                Context(
                    SubjectType.Projectile,
                    magnet.Id,
                    magnet.Id),
                EffectOperation.EnableProjectileMagnet,
                Node(
                    EffectOperation.EnableProjectileMagnet,
                    amount: 4000,
                    amount2: 5000,
                    durationTicks: 30,
                    intervalTicks: 1,
                    radiusMilli: 1000));
            UncommonProjectileEffectRuntime magnetRuntime =
                GetRuntime(
                    simulation,
                    magnet.Id,
                    EffectOperation.EnableProjectileMagnet);
            magnetRuntime.NextTick = simulation.Tick;
            simulation.ProcessUncommonProjectileTick(magnet);
            Assert.That(absorbed.Alive, Is.False);
            Assert.That(magnet.DamageMilli, Is.GreaterThan(initialDamage));

            ProjectileState carrier = AddProjectile(
                simulation,
                704,
                SimPosition.FromMilliUnits(20000, 0));
            ProjectileState receiver = AddProjectile(
                simulation,
                705,
                SimPosition.FromMilliUnits(20000, 0));
            carrier.Bindings.Add(new EffectBinding
            {
                Trigger = BindingTrigger.OnHit,
                Kind = BindingKind.Burn,
                CardId = new CardId(0),
                CardInstanceId = 77,
                Node = Node(
                    EffectOperation.ApplyBurn,
                    amount: 1000,
                    durationTicks: 20,
                    intervalTicks: 3,
                    maxStacks: 3)
            });
            simulation.ExecuteUncommonEffect(
                Context(
                    SubjectType.Projectile,
                    carrier.Id,
                    carrier.Id),
                EffectOperation.EnableProjectileContagion,
                Node(
                    EffectOperation.EnableProjectileContagion,
                    durationTicks: 30,
                    intervalTicks: 1,
                    radiusMilli: 1000));
            UncommonProjectileEffectRuntime contagionRuntime =
                GetRuntime(
                    simulation,
                    carrier.Id,
                    EffectOperation.EnableProjectileContagion);
            contagionRuntime.NextTick = simulation.Tick;
            simulation.ProcessUncommonProjectileTick(carrier);
            Assert.That(receiver.Bindings, Has.Count.EqualTo(1));
            Assert.That(receiver.Bindings[0].Kind, Is.EqualTo(BindingKind.Burn));
        }

        [Test]
        public void ReflectSealCorrosionOrbitLifestealAndFear_AreFunctional()
        {
            EnemyState reflectedFrom = AddEnemy(
                simulation,
                800,
                SimPosition.FromMilliUnits(1000, 0));
            EnemyState reflectedTo = AddEnemy(
                simulation,
                801,
                SimPosition.FromMilliUnits(2000, 0));
            RebuildSpatialIndex(simulation);
            simulation.ExecuteUncommonEffect(
                Context(
                    SubjectType.Enemy,
                    reflectedFrom.Id,
                    reflectedFrom.Id),
                EffectOperation.ApplyEnemyReflect,
                Node(
                    EffectOperation.ApplyEnemyReflect,
                    amount: 1,
                    durationTicks: 30,
                    maxStacks: 1));
            ProjectileState projectile = AddProjectile(
                simulation,
                802,
                reflectedFrom.Position);
            projectile.HitEnemies.Add(reflectedFrom.Id.Value);
            Assert.That(
                simulation.HandleUncommonProjectileHit(
                    projectile,
                    reflectedFrom,
                    ProjectileHitEvent(
                        simulation,
                        projectile.Id,
                        reflectedFrom.Id)),
                Is.True);
            Assert.That(projectile.TargetId, Is.EqualTo(reflectedTo.Id));

            simulation.ExecuteUncommonEffect(
                Context(
                    SubjectType.Enemy,
                    reflectedFrom.Id,
                    reflectedFrom.Id),
                EffectOperation.ApplySeal,
                Node(
                    EffectOperation.ApplySeal,
                    amount: 1,
                    durationTicks: 30,
                    maxStacks: 1));
            Assert.That(
                simulation.IsEnemySpecialAbilitySealed(
                    reflectedFrom),
                Is.True);

            reflectedFrom.Armor = 20;
            simulation.ExecuteUncommonEffect(
                Context(
                    SubjectType.Enemy,
                    reflectedFrom.Id,
                    reflectedFrom.Id),
                EffectOperation.ApplyCorrosion,
                Node(
                    EffectOperation.ApplyCorrosion,
                    amount: 2,
                    durationTicks: 30,
                    intervalTicks: 5,
                    maxStacks: 10,
                    limit: 3000,
                    chanceBps: 250));
            StatusInstance corrosion = FindStatus(
                reflectedFrom,
                StatusType.Corrosion);
            simulation.ProcessUncommonStatusTick(
                reflectedFrom,
                corrosion);
            Assert.That(reflectedFrom.Armor, Is.EqualTo(18));
            Assert.That(
                reflectedFrom.MaxHealthMilli,
                Is.LessThan(100000));

            EnemyState orbiting = AddEnemy(
                simulation,
                803,
                SimPosition.FromMilliUnits(5000, 0));
            orbiting.PathProgressMilli = 5000;
            orbiting.BaseSpeedMilliPerTick = 100;
            simulation.ExecuteUncommonEffect(
                Context(
                    SubjectType.Enemy,
                    orbiting.Id,
                    orbiting.Id),
                EffectOperation.ApplyEnemyOrbit,
                Node(
                    EffectOperation.ApplyEnemyOrbit,
                    amount: 2500,
                    durationTicks: 30,
                    intervalTicks: 5,
                    radiusMilli: 650,
                    limit: 2500));
            SimPosition orbitStart = orbiting.Position;
            Assert.That(
                simulation.TryProcessUncommonEnemyMovement(orbiting),
                Is.True);
            Assert.That(orbiting.Position, Is.Not.EqualTo(orbitStart));

            EnemyState feared = AddEnemy(
                simulation,
                804,
                SimPosition.FromMilliUnits(8000, 0));
            feared.PathProgressMilli = 8000;
            feared.BaseSpeedMilliPerTick = 100;
            simulation.ExecuteUncommonEffect(
                Context(
                    SubjectType.Enemy,
                    feared.Id,
                    feared.Id),
                EffectOperation.ApplyFear,
                Node(
                    EffectOperation.ApplyFear,
                    amount: 10000,
                    durationTicks: 30,
                    intervalTicks: 15,
                    maxStacks: 1,
                    limit: 12500));
            Assert.That(
                simulation.TryProcessUncommonEnemyMovement(feared),
                Is.True);
            Assert.That(feared.PathProgressMilli, Is.EqualTo(7900));

            int baseBefore = content.Run.BaseHealth - 5;
            SetPrivateField(simulation, "baseHealth", baseBefore);
            simulation.ExecuteUncommonEffect(
                Context(
                    SubjectType.Enemy,
                    reflectedTo.Id,
                    reflectedTo.Id),
                EffectOperation.ApplyLifesteal,
                Node(
                    EffectOperation.ApplyLifesteal,
                    amount: 1000,
                    durationTicks: 30,
                    maxStacks: 1));
            simulation.HandleUncommonDamageApplied(
                reflectedTo,
                DamageEvent(
                    simulation,
                    projectile.Id,
                    reflectedTo.Id,
                    EventTags.SingleTarget),
                10000);
            Assert.That(simulation.BaseHealth, Is.EqualTo(baseBefore + 1));
            AssertPresentation(simulation, "lifesteal_heal");
        }

        [Test]
        public void SameUncommonInputs_ProduceTheSameStateHash()
        {
            ulong first = BuildDeterministicState(0xD371UL);
            ulong second = BuildDeterministicState(0xD371UL);
            Assert.That(second, Is.EqualTo(first));
        }

        private ulong BuildDeterministicState(ulong seed)
        {
            var candidate = new GameSimulation();
            candidate.Initialize(content, seed);
            SetPrivateField(candidate, "nextEntityId", 20000);
            EnemyState first = AddEnemy(
                candidate,
                900,
                SimPosition.FromMilliUnits(1000, 0));
            AddEnemy(
                candidate,
                901,
                SimPosition.FromMilliUnits(1500, 0));
            RebuildSpatialIndex(candidate);
            EffectExecutionContext context = Context(
                SubjectType.Enemy,
                first.Id,
                first.Id);
            candidate.ExecuteUncommonEffect(
                context,
                EffectOperation.ApplyCurse,
                Node(
                    EffectOperation.ApplyCurse,
                    amount: 1500,
                    durationTicks: 60,
                    maxStacks: 3,
                    radiusMilli: 1500,
                    limit: 5000));
            candidate.ExecuteUncommonEffect(
                context,
                EffectOperation.ApplyFear,
                Node(
                    EffectOperation.ApplyFear,
                    amount: 10000,
                    durationTicks: 20,
                    intervalTicks: 10,
                    maxStacks: 1,
                    limit: 12500));
            ProjectileState projectile = AddProjectile(
                candidate,
                902,
                SimPosition.Origin);
            candidate.ExecuteUncommonEffect(
                Context(
                    SubjectType.Projectile,
                    projectile.Id,
                    projectile.Id),
                EffectOperation.EnableProjectilePulse,
                Node(
                    EffectOperation.EnableProjectilePulse,
                    amount: 2500,
                    durationTicks: 60,
                    intervalTicks: 10,
                    radiusMilli: 1500,
                    limit: 4));
            candidate.Step();
            return candidate.ComputeStateHash();
        }

        private static EnemyState AddEnemy(
            GameSimulation target,
            int id,
            SimPosition position,
            bool addLineage = false)
        {
            var enemy = new EnemyState
            {
                Id = new EntityId(id),
                DefinitionId = new EnemyDefinitionId(0),
                LineageId = new LineageId(id),
                Position = position,
                PathProgressMilli = 0,
                HealthMilli = 100000,
                MaxHealthMilli = 100000,
                Armor = 0,
                BaseSpeedMilliPerTick = 0,
                SpeedMultiplierBps = 10000,
                SizeMultiplierBps = 10000,
                AreaDamageTakenBps = 10000,
                SingleDamageTakenBps = 10000,
                Alive = true
            };
            GetPrivateField<List<EnemyState>>(
                target,
                "enemies").Add(enemy);
            if (addLineage)
            {
                GetPrivateField<Dictionary<int, LineageState>>(
                    target,
                    "lineages").Add(
                    enemy.LineageId.Value,
                    new LineageState
                    {
                        Id = enemy.LineageId,
                        SpawnedEntityCount = 1,
                        LiveMembers = 1
                    });
            }
            return enemy;
        }

        private static ProjectileState AddProjectile(
            GameSimulation target,
            int id,
            SimPosition position)
        {
            var projectile = new ProjectileState
            {
                Id = new EntityId(id),
                SourceTowerId = new TowerId(0),
                Position = position,
                DirectionXBps = 10000,
                DirectionYBps = 0,
                DamageMilli = 10000,
                SpeedMilliPerTick = 0,
                RadiusMilli = 100,
                LifetimeRemaining = 300,
                PierceDamageMultiplierBps = 9000,
                Alive = true,
                RootChainId = ChainId.Invalid,
                ActivationId = ActivationId.Invalid,
                LastTrailPosition = position
            };
            GetPrivateField<List<ProjectileState>>(
                target,
                "projectiles").Add(projectile);
            return projectile;
        }

        private static EffectExecutionContext Context(
            SubjectType type,
            EntityId subjectId,
            EntityId sourceId)
        {
            return new EffectExecutionContext(
                type,
                subjectId,
                new TowerId(0),
                new CardId(0),
                1,
                sourceId,
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

        private static StatusInstance FindStatus(
            EnemyState enemy,
            StatusType type)
        {
            for (int i = 0; i < enemy.Statuses.Count; i++)
            {
                StatusInstance status = enemy.Statuses[i];
                if (status.Type == type &&
                    status.RemainingTicks > 0)
                {
                    return status;
                }
            }
            return null;
        }

        private static UncommonProjectileEffectRuntime GetRuntime(
            GameSimulation target,
            EntityId projectileId,
            EffectOperation operation)
        {
            Dictionary<int, List<UncommonProjectileEffectRuntime>> runtimes =
                GetPrivateField<
                    Dictionary<int, List<UncommonProjectileEffectRuntime>>>(
                    target,
                    "uncommonProjectileEffects");
            List<UncommonProjectileEffectRuntime> effects =
                runtimes[projectileId.Value];
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].Operation == operation)
                {
                    return effects[i];
                }
            }
            Assert.Fail(
                "Missing uncommon runtime {0} on projectile {1}.",
                operation,
                projectileId.Value);
            return null;
        }

        private static void RebuildSpatialIndex(
            GameSimulation target)
        {
            GetPrivateField<SpatialHashGrid>(
                target,
                "spatialIndex").Rebuild(
                    GetPrivateField<List<EnemyState>>(
                        target,
                        "enemies"));
        }

        private static GameEvent ProjectileHitEvent(
            GameSimulation target,
            EntityId projectileId,
            EntityId enemyId)
        {
            return new GameEvent(
                target.Tick,
                EventPhase.Projectile,
                RuleforgeTD.GameLogic.Core.EventType.ProjectileHit,
                ChainId.Invalid,
                EventId.Invalid,
                ActivationId.Invalid,
                new TowerId(0),
                new CardId(0),
                projectileId,
                enemyId,
                SubjectType.Enemy,
                0,
                0,
                EventTags.Projectile |
                EventTags.SingleTarget,
                RewardOrigin.EnemyDrop);
        }

        private static GameEvent DamageEvent(
            GameSimulation target,
            EntityId sourceId,
            EntityId enemyId,
            EventTags tags)
        {
            return new GameEvent(
                target.Tick,
                EventPhase.Damage,
                RuleforgeTD.GameLogic.Core.EventType.DamageRequested,
                ChainId.Invalid,
                EventId.Invalid,
                ActivationId.Invalid,
                new TowerId(0),
                new CardId(0),
                sourceId,
                enemyId,
                SubjectType.Enemy,
                0,
                0,
                tags,
                RewardOrigin.EnemyDrop);
        }

        private static void AssertPresentation(
            GameSimulation target,
            string contentId)
        {
            SimulationEventBuffer events =
                target.ReadPresentationEvents();
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Type ==
                        PresentationEventType.EffectTriggered &&
                    events[i].ContentId == contentId)
                {
                    return;
                }
            }
            Assert.Fail(
                "Missing presentation effect '{0}'.",
                contentId);
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

        private static void SetPrivateField<T>(
            GameSimulation target,
            string fieldName,
            T value)
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
            field.SetValue(target, value);
        }
    }
}
