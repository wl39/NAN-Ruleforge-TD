using System.Collections;
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
        public IEnumerator ShowcaseScene_ConfiguresAllLevelsUnitsAndProjectiles()
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
            Assert.That(combatSystem.LivingEnemyCount, Is.EqualTo(4));
            Assert.That(
                Object.FindObjectsOfType<EnemyTestMovementSystem>(),
                Has.Length.EqualTo(1));

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
                Assert.That(showcase.TargetCount, Is.EqualTo(4));
                ArcherShowcaseCardProgram cardProgram =
                    showcase.CardProgram;
                Assert.That(cardProgram, Is.Not.Null);
                Assert.That(cardProgram.IsReady, Is.True);
                CollectionAssert.AreEqual(
                    new[] { "split", "burn", "poison" },
                    cardProgram.EquippedCardIds);
                Assert.That(
                    cardProgram.SplitProjectileCount,
                    Is.EqualTo(2));
                Assert.That(
                    cardProgram.SplitDamageBasisPoints,
                    Is.EqualTo(6500));
                AssertStatusDefinition(
                    cardProgram.BurnDefinition,
                    500,
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
                if (expectedOpen)
                {
                    SpriteRenderer bodyRenderer = tower.transform
                        .Find("Tower Body")
                        .GetComponent<SpriteRenderer>();
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
                    Is.EqualTo(closedTower.ArcherCount * 2),
                    "Split must create two tracked projectiles per archer release.");
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
            Assert.That(levelOne.PlayVolley(), Is.EqualTo(1));
            float hitTimeout = 2.5f;
            while (levelOneShowcase.SuccessfulHitCount == 0 &&
                   hitTimeout > 0f)
            {
                hitTimeout -= Time.deltaTime;
                yield return null;
            }

            Assert.That(levelOneShowcase.PooledProjectileCount, Is.GreaterThan(0));
            Assert.That(levelOneShowcase.SuccessfulHitCount, Is.GreaterThan(0));
            Assert.That(
                levelOneShowcase.TotalProjectileLaunchCount -
                launchesBeforeHit,
                Is.EqualTo(2),
                "The split card must turn one archer release into two arrows.");
            Assert.That(
                target.CurrentHealth < healthBeforeHit ||
                targetStatus.DirectPendingMilli != pendingDamageBeforeHit,
                Is.True,
                "A 650-milli split arrow must deal or accumulate deterministic damage.");
            Assert.That(targetStatus.HasBurn, Is.True);
            Assert.That(targetStatus.HasPoison, Is.True);

            targetStatus.enabled = false;
            int healthBeforeStatusTicks = target.CurrentHealth;
            targetStatus.SimulateTicksForTesting(30);
            Assert.That(
                target.CurrentHealth,
                Is.LessThan(healthBeforeStatusTicks),
                "Two authored burn intervals must convert milli damage into health loss.");
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
            Assert.That(status.BurnPendingDamageMilli, Is.EqualTo(500));
            Assert.That(enemy.CurrentHealth, Is.EqualTo(maximumHealth));

            status.SimulateTicksForTesting(15);
            Assert.That(status.BurnTickCount, Is.EqualTo(2));
            Assert.That(status.BurnPendingDamageMilli, Is.Zero);
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
        public IEnumerator SplitVolley_UsesTwoBranchesAndRespectsPoolBound()
        {
            SceneManager.LoadScene("ArcherTowerAnimationTest", LoadSceneMode.Single);
            yield return null;

            ArcherTowerShowcaseActor[] showcases =
                Object.FindObjectsOfType<ArcherTowerShowcaseActor>();
            foreach (ArcherTowerShowcaseActor showcase in showcases)
            {
                showcase.SetAutomaticPlayback(false);
            }

            ArcherTowerView levelSeven = Object
                .FindObjectsOfType<ArcherTowerView>()
                .Single(tower => tower.Level == 7);
            ArcherTowerShowcaseActor levelSevenShowcase =
                levelSeven.GetComponent<ArcherTowerShowcaseActor>();
            Assert.That(levelSevenShowcase.CardProgram.IsReady, Is.True);

            EnemyHealth target = Object.FindObjectsOfType<EnemyHealth>()
                .OrderByDescending(enemy => enemy.MaxHealth)
                .First();
            EnemyTestActor targetActor =
                target.GetComponent<EnemyTestActor>();
            Assert.That(targetActor, Is.Not.Null);
            targetActor.SetMovementEnabled(false);
            target.Configure(
                1000,
                target.GetComponent<RuleforgeTD.Rendering.DirectionalEnemyAnimator>());
            target.transform.position =
                levelSeven.transform.position + Vector3.up * 7.4f;
            ArcherEnemyCardStatusView targetStatus =
                target.GetComponent<ArcherEnemyCardStatusView>();
            Assert.That(targetStatus, Is.Not.Null);
            targetStatus.enabled = false;
            targetStatus.ClearAll();

            levelSevenShowcase.SetTargets(new[] { target });
            Assert.That(levelSevenShowcase.AimAtNearestTarget(), Is.SameAs(target));

            int initialLaunchCount =
                levelSevenShowcase.TotalProjectileLaunchCount;
            Assert.That(levelSeven.PlayVolley(), Is.EqualTo(3));
            float firstVolleyStartedAt = Time.time;
            while (Time.time - firstVolleyStartedAt < 0.72f)
            {
                yield return null;
            }

            Assert.That(
                levelSevenShowcase.TotalProjectileLaunchCount,
                Is.EqualTo(initialLaunchCount + 6));
            Assert.That(levelSevenShowcase.PooledProjectileCount, Is.EqualTo(6));
            Assert.That(levelSevenShowcase.ActiveProjectileCount, Is.EqualTo(6));

            Assert.That(levelSeven.PlayVolley(), Is.EqualTo(3));
            float secondVolleyStartedAt = Time.time;
            float secondReleaseTimeout = 1.2f;
            while (levelSevenShowcase.TotalProjectileLaunchCount <
                       initialLaunchCount + 12 &&
                   secondReleaseTimeout > 0f)
            {
                secondReleaseTimeout -= Time.deltaTime;
                yield return null;
            }

            Assert.That(
                levelSevenShowcase.TotalProjectileLaunchCount,
                Is.EqualTo(initialLaunchCount + 12));
            Assert.That(
                levelSevenShowcase.PooledProjectileCount,
                Is.EqualTo(
                    ArcherTowerShowcaseActor.MaximumPooledProjectiles));
            Assert.That(
                levelSevenShowcase.ActiveProjectileCount,
                Is.EqualTo(
                    ArcherTowerShowcaseActor.MaximumPooledProjectiles));

            while (Time.time - secondVolleyStartedAt < 0.72f)
            {
                yield return null;
            }

            Assert.That(levelSeven.PlayVolley(), Is.EqualTo(3));
            float thirdReleaseTimeout = 1.2f;
            while (levelSevenShowcase.TotalProjectileLaunchCount <
                       initialLaunchCount + 18 &&
                   thirdReleaseTimeout > 0f)
            {
                thirdReleaseTimeout -= Time.deltaTime;
                yield return null;
            }

            Assert.That(
                levelSevenShowcase.TotalProjectileLaunchCount,
                Is.EqualTo(initialLaunchCount + 18));
            Assert.That(
                levelSevenShowcase.PooledProjectileCount,
                Is.EqualTo(
                    ArcherTowerShowcaseActor.MaximumPooledProjectiles));
            Assert.That(
                levelSevenShowcase.ActiveProjectileCount,
                Is.LessThanOrEqualTo(
                    ArcherTowerShowcaseActor.MaximumPooledProjectiles));

            float expireTimeout = 2.8f;
            while (levelSevenShowcase.ActiveProjectileCount > 0 &&
                   expireTimeout > 0f)
            {
                expireTimeout -= Time.deltaTime;
                yield return null;
            }

            Assert.That(levelSevenShowcase.ActiveProjectileCount, Is.Zero);
            Assert.That(
                levelSevenShowcase.PooledProjectileCount,
                Is.EqualTo(
                    ArcherTowerShowcaseActor.MaximumPooledProjectiles));
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
