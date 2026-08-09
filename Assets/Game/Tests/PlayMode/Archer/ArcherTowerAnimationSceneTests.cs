using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuleforgeTD.Enemies;
using RuleforgeTD.Enemies.Testing;
using RuleforgeTD.Towers.Archer;
using RuleforgeTD.Towers.Testing;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuleforgeTD.Tests.PlayMode
{
    public sealed class ArcherTowerAnimationSceneTests
    {
        [UnityTest]
        public IEnumerator MultipleArchers_TargetDifferentEnemiesAndReleaseTogether()
        {
            SceneManager.LoadScene(
                "ArcherTowerAnimationTest",
                LoadSceneMode.Single);
            yield return null;

            ArcherTowerShowcaseActor[] showcases =
                Object.FindObjectsOfType<ArcherTowerShowcaseActor>();
            for (int i = 0; i < showcases.Length; i++)
            {
                showcases[i].SetAutomaticPlayback(false);
            }

            ArcherTowerView tower = Object
                .FindObjectsOfType<ArcherTowerView>()
                .Single(candidate => candidate.Level == 6);
            ArcherTowerShowcaseActor showcase =
                tower.GetComponent<ArcherTowerShowcaseActor>();
            Assert.That(showcase, Is.Not.Null);
            tower.RestartIdle();
            Assert.That(showcase.AimAtNearestTarget(), Is.Not.Null);

            Assert.That(tower.ArcherCount, Is.EqualTo(3));
            Assert.That(
                showcase.AimedTargetCount,
                Is.EqualTo(tower.ArcherCount));
            var uniqueTargets = new HashSet<EnemyHealth>();
            for (int i = 0; i < showcase.AimedTargetCount; i++)
            {
                Assert.That(
                    uniqueTargets.Add(showcase.GetAimedTarget(i)),
                    Is.True,
                    "Each archer must receive a different target.");
            }

            var releaseTimes = new List<float>();
            var releasedTargetSlots = new HashSet<int>();
            tower.ArrowRequestedForTargetSlot +=
                (targetSlot, origin, direction, tier) =>
                {
                    releaseTimes.Add(Time.time);
                    releasedTargetSlots.Add(targetSlot);
                };

            Assert.That(
                tower.PlayVolley(),
                Is.EqualTo(showcase.AimedTargetCount));

            float timeout = Time.time + 2f;
            while (releaseTimes.Count < showcase.AimedTargetCount &&
                   Time.time < timeout)
            {
                yield return null;
            }

            Assert.That(
                releaseTimes,
                Has.Count.EqualTo(tower.ArcherCount));
            Assert.That(
                releasedTargetSlots,
                Is.EquivalentTo(new[] { 0, 1, 2 }));
            Assert.That(
                releaseTimes.Max() - releaseTimes.Min(),
                Is.LessThan(0.04f),
                "Archers should release together after receiving unique targets.");
        }

        [UnityTest]
        public IEnumerator ShowcaseScene_ConfiguresEnemySplitProgramAndSingleProjectiles()
        {
            SceneManager.LoadScene("ArcherTowerAnimationTest", LoadSceneMode.Single);
            yield return null;

            ArcherTowerView[] towers = Object
                .FindObjectsOfType<ArcherTowerView>()
                .OrderBy(tower => tower.Level)
                .ToArray();
            Assert.That(towers, Has.Length.EqualTo(7));

            EnemyHealth[] combatEnemies = Object
                .FindObjectsOfType<EnemyHealth>()
                .OrderBy(enemy => enemy.name)
                .ToArray();
            Assert.That(combatEnemies, Has.Length.EqualTo(4));
            ArcherEnemyCombatSystem combatSystem =
                Object.FindObjectOfType<ArcherEnemyCombatSystem>();
            Assert.That(combatSystem, Is.Not.Null);
            Assert.That(combatSystem.EnemyCount, Is.EqualTo(4));
            Assert.That(combatSystem.RootEnemyCount, Is.EqualTo(4));
            Assert.That(combatSystem.PooledEnemyCount, Is.EqualTo(8));
            Assert.That(combatSystem.TargetCount, Is.EqualTo(12));
            Assert.That(combatSystem.ActiveSplitEnemyCount, Is.Zero);
            Assert.That(combatSystem.LivingEnemyCount, Is.EqualTo(4));
            EnemyTestMovementSystem movementSystem =
                Object.FindObjectOfType<EnemyTestMovementSystem>();
            Assert.That(movementSystem, Is.Not.Null);
            Assert.That(movementSystem.ActorCount, Is.EqualTo(12));

            EnemyHealth[] authoredTargets =
                combatSystem.GetAllTargets();
            Assert.That(authoredTargets, Has.Length.EqualTo(12));
            foreach (EnemyHealth authoredTarget in authoredTargets)
            {
                Assert.That(authoredTarget, Is.Not.Null);
                Assert.That(authoredTarget.MaxHealth, Is.EqualTo(1000));
                Assert.That(authoredTarget.CurrentHealth, Is.EqualTo(1000));
                Assert.That(
                    authoredTarget.GetComponent<EnemyTestActor>(),
                    Is.Not.Null);
                Assert.That(
                    authoredTarget.GetComponent<ArcherEnemyCardStatusView>(),
                    Is.Not.Null);
            }

            for (int i = 0; i < towers.Length; i++)
            {
                int level = i + 1;
                ArcherTowerView tower = towers[i];
                bool expectedOpen = ArcherTowerView.LevelHasOpenRoof(level);
                int expectedArcherCount =
                    ArcherTowerView.GetDefaultArcherCount(level);
                Assert.That(tower.Level, Is.EqualTo(level));
                Assert.That(tower.HasOpenRoof, Is.EqualTo(expectedOpen));
                Assert.That(tower.ArcherCount, Is.EqualTo(expectedArcherCount));
                Assert.That(tower.AreArchersVisible, Is.EqualTo(expectedOpen));
                Assert.That(tower.UpgradeFrameCount, Is.EqualTo(4));
                Assert.That(
                    tower.IdleFrameDuration,
                    Is.EqualTo(0.12f).Within(0.001f));

                int expectedIdleFrames = level == 1
                    ? 1
                    : level <= 3
                        ? 4
                        : 6;
                Assert.That(tower.IdleFrameCount, Is.EqualTo(expectedIdleFrames));

                ArcherTowerShowcaseActor showcase =
                    tower.GetComponent<ArcherTowerShowcaseActor>();
                Assert.That(showcase, Is.Not.Null);
                showcase.SetAutomaticPlayback(false);
                showcase.RefreshTargets();
                Assert.That(showcase.TargetCount, Is.EqualTo(12));
                ArcherShowcaseCardProgram cardProgram =
                    showcase.CardProgram;
                Assert.That(cardProgram, Is.Not.Null);
                Assert.That(cardProgram.IsReady, Is.True);
                CollectionAssert.AreEqual(
                    new[] { "split", "burn", "poison" },
                    cardProgram.EquippedCardIds);
                Assert.That(
                    cardProgram.SplitEnemyCount,
                    Is.EqualTo(2));
                Assert.That(
                    cardProgram.SplitHealthBasisPoints,
                    Is.EqualTo(4500));
                AssertStatusDefinition(
                    cardProgram.BurnDefinition,
                    450,
                    90,
                    15,
                    10,
                    0);
                AssertStatusDefinition(
                    cardProgram.PoisonDefinition,
                    500,
                    180,
                    30,
                    20,
                    5000);
                int expectedDirections = tower.UnitTier == 1
                    ? 5
                    : tower.UnitTier == 2
                        ? 9
                        : 13;
                Assert.That(
                    showcase.ArrowDirectionCount,
                    Is.EqualTo(expectedDirections));
                Assert.That(
                    showcase.VolleyInterval,
                    Is.EqualTo(
                        ArcherTowerShowcaseActor.GetDefaultVolleyInterval(level))
                        .Within(0.001f));
                Assert.That(
                    showcase.ProjectileSpeed,
                    Is.EqualTo(
                        ArcherTowerShowcaseActor.DefaultProjectileSpeed)
                        .Within(0.001f));
                Assert.That(
                    showcase.VolleyInterval,
                    Is.GreaterThan(0.7f),
                    "A volley must not restart before the attack release frame.");
                if (i > 0)
                {
                    ArcherTowerShowcaseActor previousShowcase =
                        towers[i - 1]
                            .GetComponent<ArcherTowerShowcaseActor>();
                    Assert.That(
                        showcase.VolleyInterval,
                        Is.LessThan(previousShowcase.VolleyInterval),
                        "Every higher level must fire more frequently.");
                }

                DirectionalArcherAnimator[] towerArchers = tower
                    .GetComponentsInChildren<DirectionalArcherAnimator>(true);
                Assert.That(
                    towerArchers,
                    Has.Length.EqualTo(expectedArcherCount));
                foreach (DirectionalArcherAnimator archer in towerArchers)
                {
                    Assert.That(
                        archer.ArrowReleaseDelay,
                        Is.EqualTo(0.6f).Within(0.001f),
                        "The authored release delay must match the " +
                        "18-tick simulation windup at 30 Hz.");
                }
                SpriteRenderer bodyRenderer = tower.transform
                    .Find("Tower Body")
                    .GetComponent<SpriteRenderer>();
                Vector3 resolvedLaunchOrigin =
                    tower.GetNextProjectileLaunchOrigin();
                if (expectedOpen)
                {
                    Assert.That(
                        towerArchers.Any(archer =>
                            Vector3.Distance(
                                archer.ProjectileOrigin,
                                resolvedLaunchOrigin) <
                            0.001f),
                        Is.True,
                        "Open-roof towers must launch from an authored bow tip.");
                    float minimumX = towerArchers
                        .Min(archer => archer.transform.localPosition.x);
                    float maximumX = towerArchers
                        .Max(archer => archer.transform.localPosition.x);
                    Assert.That(minimumX, Is.GreaterThanOrEqualTo(-0.24f));
                    Assert.That(maximumX, Is.LessThanOrEqualTo(0.24f));
                    Assert.That(
                        minimumX + maximumX,
                        Is.EqualTo(0f).Within(0.001f),
                        "Archer seats must stay compact and centered.");
                    foreach (DirectionalArcherAnimator archer in towerArchers)
                    {
                        Assert.That(
                            archer.GetComponent<SpriteRenderer>().sortingOrder,
                            Is.GreaterThan(bodyRenderer.sortingOrder),
                            "Archers must render in front of the tower body.");
                    }
                }
                else
                {
                    Assert.That(
                        Vector3.Distance(
                            resolvedLaunchOrigin,
                            bodyRenderer.bounds.center),
                        Is.LessThan(0.001f),
                        "Closed-roof towers must launch from the tower center.");
                    foreach (DirectionalArcherAnimator archer in towerArchers)
                    {
                        Assert.That(
                            archer.gameObject.activeInHierarchy,
                            Is.True,
                            "Internal archers must stay active to reach their release frame.");
                        Assert.That(
                            archer.GetComponent<SpriteRenderer>().enabled,
                            Is.False,
                            "Closed-roof internal archers must never render.");
                    }
                }
            }

            int[] closedTowerIndices = { 3, 6 };
            for (int i = 0; i < closedTowerIndices.Length; i++)
            {
                ArcherTowerView closedTower = towers[closedTowerIndices[i]];
                ArcherTowerShowcaseActor closedShowcase =
                    closedTower.GetComponent<ArcherTowerShowcaseActor>();
                int requestedArrows = 0;
                int launchesBeforeVolley =
                    closedShowcase.TotalProjectileLaunchCount;
                closedTower.ArrowRequested +=
                    (origin, direction, tier) => requestedArrows++;

                Assert.That(closedShowcase.AimAtNearestTarget(), Is.Not.Null);
                Assert.That(
                    closedTower.PlayVolley(),
                    Is.EqualTo(closedTower.ArcherCount));

                float releaseTimeout = 1.4f;
                while ((requestedArrows < closedTower.ArcherCount ||
                        closedShowcase.SuccessfulHitCount == 0) &&
                       releaseTimeout > 0f)
                {
                    releaseTimeout -= Time.deltaTime;
                    yield return null;
                }

                Assert.That(
                    requestedArrows,
                    Is.EqualTo(closedTower.ArcherCount),
                    "Every hidden internal archer must release one arrow.");
                Assert.That(
                    closedShowcase.TotalProjectileLaunchCount -
                    launchesBeforeVolley,
                    Is.EqualTo(closedTower.ArcherCount),
                    "Enemy interpretation must keep one arrow per archer release.");
                Assert.That(
                    closedShowcase.SuccessfulHitCount,
                    Is.GreaterThan(0),
                    "A closed-roof tower must damage a live enemy.");
                Assert.That(
                    closedShowcase.PooledProjectileCount,
                    Is.LessThanOrEqualTo(
                        ArcherTowerShowcaseActor.MaximumPooledProjectiles));
                Assert.That(closedTower.AreArchersVisible, Is.False);
                foreach (DirectionalArcherAnimator archer in closedTower
                             .GetComponentsInChildren<DirectionalArcherAnimator>(true))
                {
                    Assert.That(
                        archer.GetComponent<SpriteRenderer>().enabled,
                        Is.False);
                }
            }

            ArcherTowerView levelTwo = towers[1];
            levelTwo.RestartIdle();
            Assert.That(levelTwo.CurrentFrameIndex, Is.Zero);
            yield return new WaitForSeconds(0.16f);
            Assert.That(
                levelTwo.CurrentFrameIndex,
                Is.Not.Zero,
                "Idle must advance immediately at a uniform rate without a long first-frame hold.");

            ArcherTowerView levelOne = towers[0];
            ArcherTowerShowcaseActor levelOneShowcase =
                levelOne.GetComponent<ArcherTowerShowcaseActor>();
            combatSystem.ResetAllLineages();
            levelOneShowcase.RefreshTargets();
            DirectionalArcherAnimator[] archers =
                levelOne.GetComponentsInChildren<DirectionalArcherAnimator>(true);
            Assert.That(archers, Has.Length.EqualTo(1));
            Vector3 archerSeatPosition = archers[0].transform.localPosition;

            Assert.That(levelOne.PlayUpgrade(), Is.True);
            Assert.That(levelOne.IsUpgrading, Is.True);
            Assert.That(levelOne.AreArchersVisible, Is.False);
            yield return new WaitForSeconds(0.72f);
            Assert.That(levelOne.IsUpgrading, Is.False);
            Assert.That(levelOne.AreArchersVisible, Is.True);
            Assert.That(levelOne.IsArcherLanding, Is.True);
            Assert.That(
                archers[0].transform.localPosition.y,
                Is.GreaterThan(archerSeatPosition.y + 0.5f));
            Assert.That(
                levelOne.PlayVolley(),
                Is.Zero,
                "Archers must not attack while dropping into the tower.");

            yield return new WaitForSeconds(0.55f);
            Assert.That(levelOne.IsArcherLanding, Is.False);
            Assert.That(
                Vector3.Distance(
                    archers[0].transform.localPosition,
                    archerSeatPosition),
                Is.LessThan(0.001f));

            archers[0].SetAim(Vector2.right);
            Assert.That(
                archers[0].GetComponent<SpriteRenderer>().flipX,
                Is.True,
                "The S source art faces left, so right aim must use flipX.");

            EnemyHealth target = levelOneShowcase.AimAtNearestTarget();
            Assert.That(target, Is.Not.Null);
            int maximumHealthBeforeHit = target.MaxHealth;
            Vector2 expectedAim =
                ArcherProjectileView.GetTargetAimPoint(target) -
                archers[0].transform.position;
            Assert.That(
                Vector2.Dot(
                    archers[0].AimDirection,
                    expectedAim.normalized),
                Is.GreaterThan(0.999f),
                "The archer must face its selected live enemy.");

            int healthBeforeHit = target.CurrentHealth;
            ArcherEnemyCardStatusView targetStatus =
                target.GetComponent<ArcherEnemyCardStatusView>();
            Assert.That(targetStatus, Is.Not.Null);
            int pendingDamageBeforeHit =
                targetStatus.DirectPendingMilli;
            int launchesBeforeHit =
                levelOneShowcase.TotalProjectileLaunchCount;
            int successfulHitsBefore =
                levelOneShowcase.SuccessfulHitCount;
            int releaseFrameIndex = -1;
            ArcherUnitAnimationBehaviour releaseBehaviour =
                ArcherUnitAnimationBehaviour.Idle;
            archers[0].ArrowReleased += releasedArcher =>
            {
                releaseFrameIndex = releasedArcher.CurrentFrameIndex;
                releaseBehaviour = releasedArcher.CurrentBehaviour;
            };
            Assert.That(levelOne.PlayVolley(), Is.EqualTo(1));
            float hitTimeout = 2.5f;
            while (levelOneShowcase.SuccessfulHitCount ==
                       successfulHitsBefore &&
                   hitTimeout > 0f)
            {
                hitTimeout -= Time.deltaTime;
                yield return null;
            }

            Assert.That(levelOneShowcase.PooledProjectileCount, Is.GreaterThan(0));
            Assert.That(levelOneShowcase.SuccessfulHitCount, Is.GreaterThan(0));
            Assert.That(
                releaseBehaviour,
                Is.EqualTo(ArcherUnitAnimationBehaviour.Attack));
            Assert.That(
                releaseFrameIndex,
                Is.EqualTo(5),
                "The projectile must begin on the final authored attack " +
                "frame, where the nocked arrow first leaves the bow.");
            Assert.That(
                levelOneShowcase.TotalProjectileLaunchCount -
                launchesBeforeHit,
                Is.EqualTo(1),
                "Enemy interpretation must not split the projectile.");
            Assert.That(
                target.CurrentHealth < healthBeforeHit ||
                targetStatus.DirectPendingMilli != pendingDamageBeforeHit,
                Is.True,
                "The full-damage arrow must deal deterministic damage before splitting.");

            EnemyHealth[] splitMembers =
                combatSystem.GetActiveLineageMembers(target);
            Assert.That(splitMembers, Has.Length.EqualTo(2));
            Assert.That(
                combatSystem.ActiveSplitEnemyCount,
                Is.GreaterThanOrEqualTo(1),
                "At least the targeted lineage must have an active split child.");
            Assert.That(
                Vector3.Distance(
                    splitMembers[0].transform.position,
                    splitMembers[1].transform.position),
                Is.GreaterThanOrEqualTo(0.6f),
                "Split enemies must spawn on visibly separated left/right branches.");
            int expectedSplitMaximum = Mathf.Max(
                1,
                maximumHealthBeforeHit *
                levelOneShowcase.CardProgram.SplitHealthBasisPoints /
                10000);
            foreach (EnemyHealth member in splitMembers)
            {
                Assert.That(member.MaxHealth, Is.EqualTo(expectedSplitMaximum));
                ArcherEnemyCardStatusView memberStatus =
                    member.GetComponent<ArcherEnemyCardStatusView>();
                Assert.That(memberStatus, Is.Not.Null);
                Assert.That(memberStatus.HasBurn, Is.True);
                Assert.That(memberStatus.HasPoison, Is.True);
            }

            targetStatus.enabled = false;
            int healthBeforeStatusTicks = target.CurrentHealth;
            targetStatus.SimulateTicksForTesting(45);
            Assert.That(
                target.CurrentHealth,
                Is.LessThan(healthBeforeStatusTicks),
                "Three authored burn intervals must convert milli damage into health loss.");
        }

        [UnityTest]
        public IEnumerator CardStatuses_AccumulateMilliTicksAndClearOnRespawn()
        {
            SceneManager.LoadScene("ArcherTowerAnimationTest", LoadSceneMode.Single);
            yield return null;

            ArcherTowerShowcaseActor[] showcases =
                Object.FindObjectsOfType<ArcherTowerShowcaseActor>();
            foreach (ArcherTowerShowcaseActor showcase in showcases)
            {
                showcase.SetAutomaticPlayback(false);
            }

            ArcherShowcaseCardProgram cardProgram = showcases
                .OrderBy(showcase =>
                    showcase.GetComponent<ArcherTowerView>().Level)
                .First()
                .CardProgram;
            Assert.That(cardProgram, Is.Not.Null);
            Assert.That(cardProgram.IsReady, Is.True);

            EnemyHealth enemy = Object.FindObjectsOfType<EnemyHealth>()
                .OrderByDescending(candidate => candidate.MaxHealth)
                .First();
            EnemyTestActor enemyActor =
                enemy.GetComponent<EnemyTestActor>();
            Assert.That(enemyActor, Is.Not.Null);
            enemyActor.SetMovementEnabled(false);

            ArcherEnemyCardStatusView status =
                enemy.GetComponent<ArcherEnemyCardStatusView>();
            Assert.That(status, Is.Not.Null);
            status.enabled = false;
            status.ClearAll();
            enemy.ResetHealth();
            int maximumHealth = enemy.MaxHealth;

            status.ApplyBurn(cardProgram.BurnDefinition);
            Assert.That(status.HasBurn, Is.True);
            Assert.That(status.BurnStacks, Is.EqualTo(1));
            Assert.That(status.BurnRemainingTicks, Is.EqualTo(90));
            Assert.That(status.BurnIntervalTicks, Is.EqualTo(15));
            Assert.That(status.BurnTickCount, Is.Zero);

            status.SimulateTicksForTesting(14);
            Assert.That(status.BurnTickCount, Is.Zero);
            Assert.That(status.BurnPendingDamageMilli, Is.Zero);
            Assert.That(enemy.CurrentHealth, Is.EqualTo(maximumHealth));

            status.SimulateTicksForTesting(1);
            Assert.That(status.BurnTickCount, Is.EqualTo(1));
            Assert.That(status.BurnPendingDamageMilli, Is.EqualTo(450));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(maximumHealth));

            status.SimulateTicksForTesting(15);
            Assert.That(status.BurnTickCount, Is.EqualTo(2));
            Assert.That(status.BurnPendingDamageMilli, Is.EqualTo(900));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(maximumHealth));

            status.SimulateTicksForTesting(15);
            Assert.That(status.BurnTickCount, Is.EqualTo(3));
            Assert.That(status.BurnPendingDamageMilli, Is.EqualTo(350));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(maximumHealth - 1));

            status.ClearAll();
            enemy.ResetHealth();
            for (int application = 0; application < 25; application++)
            {
                status.ApplyPoison(cardProgram.PoisonDefinition);
            }

            Assert.That(status.HasPoison, Is.True);
            Assert.That(status.PoisonStacks, Is.EqualTo(20));
            Assert.That(status.PoisonRemainingTicks, Is.EqualTo(180));
            Assert.That(status.PoisonIntervalTicks, Is.EqualTo(30));
            Assert.That(status.PoisonTickCount, Is.Zero);

            status.SimulateTicksForTesting(29);
            Assert.That(status.PoisonTickCount, Is.Zero);
            Assert.That(status.PoisonPendingDamageMilli, Is.Zero);
            Assert.That(enemy.CurrentHealth, Is.EqualTo(maximumHealth));

            status.SimulateTicksForTesting(1);
            Assert.That(status.PoisonTickCount, Is.EqualTo(1));
            Assert.That(status.PoisonPendingDamageMilli, Is.Zero);
            Assert.That(enemy.CurrentHealth, Is.EqualTo(maximumHealth - 10));

            int healthBeforeDirectDamage = enemy.CurrentHealth;
            Assert.That(status.ApplyDirectDamageMilli(650), Is.Zero);
            Assert.That(status.DirectPendingMilli, Is.EqualTo(650));
            Assert.That(
                enemy.CurrentHealth,
                Is.EqualTo(healthBeforeDirectDamage));
            Assert.That(status.ApplyDirectDamageMilli(650), Is.EqualTo(1));
            Assert.That(status.DirectPendingMilli, Is.EqualTo(300));
            Assert.That(
                enemy.CurrentHealth,
                Is.EqualTo(healthBeforeDirectDamage - 1));

            status.ApplyBurn(cardProgram.BurnDefinition);
            Assert.That(status.HasBurn, Is.True);
            enemy.Kill();
            Assert.That(enemy.IsDead, Is.True);

            float respawnTimeout = 2.4f;
            while (enemy.IsDead && respawnTimeout > 0f)
            {
                respawnTimeout -= Time.deltaTime;
                yield return null;
            }

            Assert.That(enemy.IsDead, Is.False);
            Assert.That(enemy.CurrentHealth, Is.EqualTo(maximumHealth));
            Assert.That(status.HasBurn, Is.False);
            Assert.That(status.HasPoison, Is.False);
            Assert.That(status.BurnStacks, Is.Zero);
            Assert.That(status.PoisonStacks, Is.Zero);
            Assert.That(status.BurnTickCount, Is.Zero);
            Assert.That(status.PoisonTickCount, Is.Zero);
            Assert.That(status.BurnPendingDamageMilli, Is.Zero);
            Assert.That(status.PoisonPendingDamageMilli, Is.Zero);
            Assert.That(status.DirectPendingMilli, Is.Zero);
        }

        [UnityTest]
        public IEnumerator EnemySplit_UsesHealthFloorDynamicPoolDeepCopyScaleAndReset()
        {
            SceneManager.LoadScene("ArcherTowerAnimationTest", LoadSceneMode.Single);
            yield return null;

            ArcherTowerShowcaseActor[] showcases =
                Object.FindObjectsOfType<ArcherTowerShowcaseActor>();
            foreach (ArcherTowerShowcaseActor showcase in showcases)
            {
                showcase.SetAutomaticPlayback(false);
            }

            ArcherShowcaseCardProgram cardProgram = showcases
                .OrderBy(showcase =>
                    showcase.GetComponent<ArcherTowerView>().Level)
                .First()
                .CardProgram;
            Assert.That(cardProgram, Is.Not.Null);
            Assert.That(cardProgram.IsReady, Is.True);
            ArcherEnemyCombatSystem combatSystem =
                Object.FindObjectOfType<ArcherEnemyCombatSystem>();
            Assert.That(combatSystem, Is.Not.Null);
            combatSystem.ResetAllLineages();
            EnemyTestMovementSystem movementSystem =
                Object.FindObjectOfType<EnemyTestMovementSystem>();
            Assert.That(movementSystem, Is.Not.Null);

            EnemyHealth target = Object.FindObjectsOfType<EnemyHealth>()
                .OrderBy(enemy => enemy.name)
                .First();
            EnemyTestActor targetActor =
                target.GetComponent<EnemyTestActor>();
            Assert.That(targetActor, Is.Not.Null);
            targetActor.SetMovementEnabled(false);
            RuleforgeTD.Rendering.DirectionalEnemyAnimator targetAnimator =
                target.GetComponent<
                    RuleforgeTD.Rendering.DirectionalEnemyAnimator>();
            target.Configure(
                1000,
                1000,
                targetAnimator);
            ArcherEnemyCardStatusView targetStatus =
                target.GetComponent<ArcherEnemyCardStatusView>();
            Assert.That(targetStatus, Is.Not.Null);
            targetStatus.enabled = false;
            targetStatus.ClearAll();

            Vector3 baseScale = target.transform.localScale;
            int initialPooledEnemyCount =
                combatSystem.PooledEnemyCount;
            int initialTargetCount =
                combatSystem.TargetCount;
            Assert.That(initialPooledEnemyCount, Is.EqualTo(8));
            Assert.That(initialTargetCount, Is.EqualTo(12));
            Assert.That(
                movementSystem.ActorCount,
                Is.EqualTo(initialTargetCount));
            Assert.That(target.MaxHealth, Is.EqualTo(1000));
            Assert.That(target.CurrentHealth, Is.EqualTo(1000));
            Assert.That(combatSystem.GetGeneration(target), Is.Zero);

            targetStatus.ApplyBurn(cardProgram.BurnDefinition);
            targetStatus.ApplyPoison(cardProgram.PoisonDefinition);
            targetStatus.ApplyPoison(cardProgram.PoisonDefinition);
            targetStatus.SimulateTicksForTesting(15);
            target.Configure(1000, 1000, targetAnimator);
            Assert.That(targetStatus.BurnTickCount, Is.EqualTo(1));
            Assert.That(
                targetStatus.BurnPendingDamageMilli,
                Is.EqualTo(450));
            Assert.That(
                targetStatus.PoisonTicksUntilDamage,
                Is.EqualTo(15));

            int[] expectedHealthByGeneration =
            {
                450,
                202,
                90,
                40,
                18,
                8,
                3,
                1
            };
            EnemyHealth branch = target;

            for (int generation = 1;
                 generation <= expectedHealthByGeneration.Length;
                 generation++)
            {
                EnemyHealth child =
                    combatSystem.ApplyEnemyProgram(
                        branch,
                        cardProgram);
                Assert.That(
                    child,
                    Is.Not.Null,
                    "Generation " + generation +
                    " must split while both scaled health values stay >= 1.");

                int expectedHealth =
                    expectedHealthByGeneration[generation - 1];
                Assert.That(branch.MaxHealth, Is.EqualTo(expectedHealth));
                Assert.That(branch.CurrentHealth, Is.EqualTo(expectedHealth));
                Assert.That(child.MaxHealth, Is.EqualTo(expectedHealth));
                Assert.That(child.CurrentHealth, Is.EqualTo(expectedHealth));
                Assert.That(
                    combatSystem.GetGeneration(branch),
                    Is.EqualTo(generation));
                Assert.That(
                    combatSystem.GetGeneration(child),
                    Is.EqualTo(generation));

                Vector3 expectedScale =
                    baseScale * Mathf.Pow(0.9f, generation);
                Assert.That(
                    Vector3.Distance(
                        branch.transform.localScale,
                        expectedScale),
                    Is.LessThan(0.0001f));
                Assert.That(
                    Vector3.Distance(
                        child.transform.localScale,
                        expectedScale),
                    Is.LessThan(0.0001f));

                ArcherEnemyCardStatusView branchStatus =
                    branch.GetComponent<ArcherEnemyCardStatusView>();
                ArcherEnemyCardStatusView childStatus =
                    child.GetComponent<ArcherEnemyCardStatusView>();
                Assert.That(branchStatus, Is.Not.Null);
                Assert.That(childStatus, Is.Not.Null);
                AssertStatusRuntimeEqual(branchStatus, childStatus);

                if (generation == 1)
                {
                    Assert.That(
                        childStatus.BurnTickCount,
                        Is.EqualTo(1),
                        "The child must inherit the source tick history.");
                    Assert.That(
                        childStatus.BurnPendingDamageMilli,
                        Is.EqualTo(450),
                        "The child must inherit fractional status damage.");
                    Assert.That(
                        childStatus.PoisonTicksUntilDamage,
                        Is.EqualTo(15),
                        "The child must inherit the next poison tick.");
                    Assert.That(
                        childStatus.BurnIntensityMilli,
                        Is.EqualTo(450));
                    Assert.That(
                        childStatus.BurnMaxStacks,
                        Is.EqualTo(10));
                    Assert.That(
                        childStatus.PoisonIntensityMilli,
                        Is.EqualTo(500));
                    Assert.That(
                        childStatus.PoisonMaxStacks,
                        Is.EqualTo(20));

                    int sourceBurnRemaining =
                        branchStatus.BurnRemainingTicks;
                    int sourcePoisonRemaining =
                        branchStatus.PoisonRemainingTicks;
                    childStatus.enabled = false;
                    childStatus.SimulateTicksForTesting(1);
                    Assert.That(
                        branchStatus.BurnRemainingTicks,
                        Is.EqualTo(sourceBurnRemaining));
                    Assert.That(
                        branchStatus.PoisonRemainingTicks,
                        Is.EqualTo(sourcePoisonRemaining));
                    Assert.That(
                        childStatus.BurnRemainingTicks,
                        Is.EqualTo(sourceBurnRemaining - 1));
                    Assert.That(
                        childStatus.PoisonRemainingTicks,
                        Is.EqualTo(sourcePoisonRemaining - 1));
                }

                Assert.That(
                    combatSystem.GetActiveLineageMembers(branch),
                    Has.Length.EqualTo(generation + 1));

                if (generation >= 3)
                {
                    Assert.That(
                        combatSystem.PooledEnemyCount,
                        Is.GreaterThan(initialPooledEnemyCount),
                        "The third split must grow beyond the two seeded children.");
                    Assert.That(
                        combatSystem.TargetCount,
                        Is.GreaterThan(initialTargetCount));
                    Assert.That(
                        movementSystem.ActorCount,
                        Is.EqualTo(combatSystem.TargetCount),
                        "Every high-water child must join deterministic movement.");
                }
            }

            Assert.That(branch.MaxHealth, Is.EqualTo(1));
            Assert.That(branch.CurrentHealth, Is.EqualTo(1));
            Assert.That(combatSystem.GetGeneration(branch), Is.EqualTo(8));
            Assert.That(
                combatSystem.GetActiveLineageMembers(branch),
                Has.Length.EqualTo(9));
            Assert.That(combatSystem.TotalSuccessfulSplits, Is.EqualTo(8));

            ArcherEnemyCardStatusView finalStatus =
                branch.GetComponent<ArcherEnemyCardStatusView>();
            int burnStacksBeforeRejectedSplit =
                finalStatus.BurnStacks;
            int poisonStacksBeforeRejectedSplit =
                finalStatus.PoisonStacks;
            int poolCountBeforeRejectedSplit =
                combatSystem.PooledEnemyCount;
            int targetCountBeforeRejectedSplit =
                combatSystem.TargetCount;
            Vector3 scaleBeforeRejectedSplit =
                branch.transform.localScale;

            Assert.That(
                combatSystem.ApplyEnemyProgram(branch, cardProgram),
                Is.Null,
                "Scaling one health by 45% yields zero, so split must stop.");
            Assert.That(branch.MaxHealth, Is.EqualTo(1));
            Assert.That(branch.CurrentHealth, Is.EqualTo(1));
            Assert.That(combatSystem.GetGeneration(branch), Is.EqualTo(8));
            Assert.That(
                Vector3.Distance(
                    branch.transform.localScale,
                    scaleBeforeRejectedSplit),
                Is.LessThan(0.0001f));
            Assert.That(
                combatSystem.PooledEnemyCount,
                Is.EqualTo(poolCountBeforeRejectedSplit));
            Assert.That(
                combatSystem.TargetCount,
                Is.EqualTo(targetCountBeforeRejectedSplit));
            Assert.That(
                finalStatus.BurnStacks,
                Is.EqualTo(
                    Mathf.Min(
                        finalStatus.BurnMaxStacks,
                        burnStacksBeforeRejectedSplit + 1)),
                "Remaining cards must execute after a rejected split.");
            Assert.That(
                finalStatus.PoisonStacks,
                Is.EqualTo(
                    Mathf.Min(
                        finalStatus.PoisonMaxStacks,
                        poisonStacksBeforeRejectedSplit + 1)));

            combatSystem.ResetAllLineages();

            Assert.That(combatSystem.ActiveSplitEnemyCount, Is.Zero);
            Assert.That(combatSystem.LivingEnemyCount, Is.EqualTo(4));
            Assert.That(combatSystem.TotalSuccessfulSplits, Is.Zero);
            Assert.That(
                combatSystem.PooledEnemyCount,
                Is.GreaterThan(initialPooledEnemyCount),
                "Reset must retain the grown high-water pool.");
            Assert.That(
                movementSystem.ActorCount,
                Is.EqualTo(combatSystem.TargetCount));
            Assert.That(
                combatSystem.GetActiveLineageMembers(target),
                Has.Length.EqualTo(1));
            foreach (EnemyHealth resetTarget in combatSystem.GetAllTargets())
            {
                Assert.That(resetTarget.MaxHealth, Is.EqualTo(1000));
                Assert.That(resetTarget.CurrentHealth, Is.EqualTo(1000));
                Assert.That(
                    combatSystem.GetGeneration(resetTarget),
                    Is.Zero);
                Assert.That(
                    Vector3.Distance(
                        resetTarget.transform.localScale,
                        baseScale),
                    Is.LessThan(0.0001f));

                ArcherEnemyCardStatusView resetStatus =
                    resetTarget.GetComponent<ArcherEnemyCardStatusView>();
                Assert.That(resetStatus, Is.Not.Null);
                Assert.That(resetStatus.HasBurn, Is.False);
                Assert.That(resetStatus.HasPoison, Is.False);
                Assert.That(resetStatus.BurnStacks, Is.Zero);
                Assert.That(resetStatus.PoisonStacks, Is.Zero);
                Assert.That(resetStatus.BurnRemainingTicks, Is.Zero);
                Assert.That(resetStatus.PoisonRemainingTicks, Is.Zero);
                Assert.That(resetStatus.BurnIntervalTicks, Is.Zero);
                Assert.That(resetStatus.PoisonIntervalTicks, Is.Zero);
                Assert.That(resetStatus.BurnTicksUntilDamage, Is.Zero);
                Assert.That(resetStatus.PoisonTicksUntilDamage, Is.Zero);
                Assert.That(resetStatus.BurnIntensityMilli, Is.Zero);
                Assert.That(resetStatus.PoisonIntensityMilli, Is.Zero);
                Assert.That(resetStatus.BurnMaxStacks, Is.Zero);
                Assert.That(resetStatus.PoisonMaxStacks, Is.Zero);
                Assert.That(resetStatus.BurnPendingDamageMilli, Is.Zero);
                Assert.That(resetStatus.PoisonPendingDamageMilli, Is.Zero);
                Assert.That(resetStatus.BurnTickCount, Is.Zero);
                Assert.That(resetStatus.PoisonTickCount, Is.Zero);
                Assert.That(resetStatus.DirectPendingMilli, Is.Zero);
                Assert.That(resetStatus.FixedTickAccumulator, Is.Zero);
            }

            combatSystem.Configure(
                new[] { target },
                1.8f);
            target.Configure(1000, 1000, targetAnimator);
            EnemyHealth fallbackChild =
                combatSystem.ApplyEnemyProgram(target, cardProgram);
            Assert.That(
                fallbackChild,
                Is.Not.Null,
                "The compatibility Configure overload must grow a pool " +
                "from the root when no seed children were supplied.");
            Assert.That(combatSystem.PooledEnemyCount, Is.EqualTo(1));
            Assert.That(combatSystem.TargetCount, Is.EqualTo(2));
        }

        private static void AssertStatusRuntimeEqual(
            ArcherEnemyCardStatusView expected,
            ArcherEnemyCardStatusView actual)
        {
            Assert.That(actual.BurnStacks, Is.EqualTo(expected.BurnStacks));
            Assert.That(
                actual.BurnRemainingTicks,
                Is.EqualTo(expected.BurnRemainingTicks));
            Assert.That(
                actual.BurnIntervalTicks,
                Is.EqualTo(expected.BurnIntervalTicks));
            Assert.That(
                actual.BurnTicksUntilDamage,
                Is.EqualTo(expected.BurnTicksUntilDamage));
            Assert.That(
                actual.BurnIntensityMilli,
                Is.EqualTo(expected.BurnIntensityMilli));
            Assert.That(
                actual.BurnMaxStacks,
                Is.EqualTo(expected.BurnMaxStacks));
            Assert.That(
                actual.BurnPendingDamageMilli,
                Is.EqualTo(expected.BurnPendingDamageMilli));
            Assert.That(
                actual.BurnTickCount,
                Is.EqualTo(expected.BurnTickCount));
            Assert.That(
                actual.PoisonStacks,
                Is.EqualTo(expected.PoisonStacks));
            Assert.That(
                actual.PoisonRemainingTicks,
                Is.EqualTo(expected.PoisonRemainingTicks));
            Assert.That(
                actual.PoisonIntervalTicks,
                Is.EqualTo(expected.PoisonIntervalTicks));
            Assert.That(
                actual.PoisonTicksUntilDamage,
                Is.EqualTo(expected.PoisonTicksUntilDamage));
            Assert.That(
                actual.PoisonIntensityMilli,
                Is.EqualTo(expected.PoisonIntensityMilli));
            Assert.That(
                actual.PoisonMaxStacks,
                Is.EqualTo(expected.PoisonMaxStacks));
            Assert.That(
                actual.PoisonPendingDamageMilli,
                Is.EqualTo(expected.PoisonPendingDamageMilli));
            Assert.That(
                actual.PoisonTickCount,
                Is.EqualTo(expected.PoisonTickCount));
            Assert.That(actual.TickRate, Is.EqualTo(expected.TickRate));
            Assert.That(
                actual.FixedTickAccumulator,
                Is.EqualTo(expected.FixedTickAccumulator).Within(0.0001f));
            Assert.That(
                actual.DirectPendingMilli,
                Is.EqualTo(expected.DirectPendingMilli));
        }

        private static void AssertStatusDefinition(
            ArcherShowcaseStatusDefinition definition,
            int intensityMilli,
            int durationTicks,
            int intervalTicks,
            int maxStacks,
            int armorIgnoreBps)
        {
            Assert.That(definition.IntensityMilli, Is.EqualTo(intensityMilli));
            Assert.That(definition.DurationTicks, Is.EqualTo(durationTicks));
            Assert.That(definition.IntervalTicks, Is.EqualTo(intervalTicks));
            Assert.That(definition.MaxStacks, Is.EqualTo(maxStacks));
            Assert.That(definition.ArmorIgnoreBps, Is.EqualTo(armorIgnoreBps));
            Assert.That(definition.TickRate, Is.EqualTo(30));
        }
    }
}
