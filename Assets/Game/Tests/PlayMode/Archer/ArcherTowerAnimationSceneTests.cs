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
                    closedShowcase.SuccessfulHitCount,
                    Is.GreaterThan(0),
                    "A closed-roof tower must damage a live enemy.");
                Assert.That(
                    closedShowcase.PooledProjectileCount,
                    Is.LessThanOrEqualTo(8));
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
                target.CurrentHealth,
                Is.LessThan(healthBeforeHit),
                "A tracked arrow must reach and damage the live enemy.");
        }
    }
}
