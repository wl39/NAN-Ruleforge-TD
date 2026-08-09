using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RuleforgeTD.Battle;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuleforgeTD.Tests.PlayMode
{
    public sealed class StageOneImpactPresentationTests
    {
        [Test]
        public void
            EnemyAreaIndicator_UsesLogicalSpriteCenterOverEventPosition()
        {
            var controllerObject = new GameObject(
                "Area Center Controller",
                typeof(StageOneBattleController));
            controllerObject.SetActive(false);
            var vfxObject = new GameObject(
                "Area Center VFX",
                typeof(StageOneCardEffectVfxView));
            vfxObject.transform.SetParent(
                controllerObject.transform,
                false);
            var enemyObject = new GameObject(
                "Tall Area Center Enemy",
                typeof(SpriteRenderer),
                typeof(StageOneEnemyView));
            Texture2D texture = null;
            Sprite sprite = null;
            try
            {
                texture = new Texture2D(
                    2,
                    4,
                    TextureFormat.RGBA32,
                    false);
                texture.SetPixels(new Color[8]);
                texture.Apply();
                sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, 2f, 4f),
                    new Vector2(0.5f, 0f),
                    1f);

                SpriteRenderer renderer =
                    enemyObject.GetComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                StageOneEnemyView enemy =
                    enemyObject.GetComponent<StageOneEnemyView>();
                enemy.Configure(707, "tall_enemy", 1f);
                enemy.ApplySnapshot(
                    CreateEnemySnapshot(
                        707,
                        2000,
                        3000,
                        10000,
                        10000,
                        Array.Empty<StatusSnapshot>()));

                StageOneBattleController controller =
                    controllerObject.GetComponent<
                        StageOneBattleController>();
                StageOneCardEffectVfxView vfx =
                    vfxObject.GetComponent<
                        StageOneCardEffectVfxView>();
                vfx.InitializeNow(16);

                FieldInfo viewsField =
                    typeof(StageOneBattleController).GetField(
                        "enemyViews",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                Assert.That(viewsField, Is.Not.Null);
                var views =
                    (Dictionary<int, StageOneEnemyView>)
                    viewsField.GetValue(controller);
                views.Add(enemy.EntityId, enemy);

                FieldInfo vfxField =
                    typeof(StageOneBattleController).GetField(
                        "cardEffectVfx",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                Assert.That(vfxField, Is.Not.Null);
                vfxField.SetValue(controller, vfx);

                var areaEvent = new SimulationPresentationEvent(
                    1,
                    PresentationEventType.AreaEffectTriggered,
                    enemy.EntityId,
                    999,
                    1400,
                    "explode",
                    effectPosition:
                        SimPosition.FromMilliUnits(-9000, -9000),
                    hasEffectPosition: true);
                MethodInfo playMethod =
                    typeof(StageOneBattleController).GetMethod(
                        "PlayAreaEffectIndicator",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                Assert.That(playMethod, Is.Not.Null);
                playMethod.Invoke(
                    controller,
                    new object[] { areaEvent });

                Vector3 expected = enemy.LogicalImpactCenter;
                Assert.That(
                    vfx.LastStartPosition.x,
                    Is.EqualTo(expected.x).Within(0.001f));
                Assert.That(
                    vfx.LastStartPosition.y,
                    Is.EqualTo(expected.y).Within(0.001f));
                Assert.That(
                    vfx.LastPlayedAreaRadius,
                    Is.EqualTo(1.4f).Within(0.001f));
                Assert.That(
                    vfx.LastStartPosition.y,
                    Is.Not.EqualTo(-9f).Within(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(enemyObject);
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator
            DamageSnapshot_FlashesWhiteAndRecoilsWithoutMovingLogic()
        {
            var enemyObject = new GameObject(
                "Impact Feedback Enemy",
                typeof(SpriteRenderer),
                typeof(StageOneEnemyView));
            Texture2D enemyTexture = null;
            Sprite enemySprite = null;
            try
            {
                SpriteRenderer renderer =
                    enemyObject.GetComponent<SpriteRenderer>();
                enemyTexture =
                    new Texture2D(
                        2,
                        2,
                        TextureFormat.RGBA32,
                        false);
                enemyTexture.SetPixels(
                    new[]
                    {
                        Color.red,
                        Color.blue,
                        Color.green,
                        Color.yellow
                    });
                enemyTexture.Apply();
                enemySprite = Sprite.Create(
                    enemyTexture,
                    new Rect(0f, 0f, 2f, 2f),
                    new Vector2(0.5f, 0.5f),
                    2f);
                renderer.sprite = enemySprite;
                renderer.color =
                    new Color(0.55f, 0.7f, 0.8f, 1f);
                StageOneEnemyView enemy =
                    enemyObject.GetComponent<StageOneEnemyView>();
                enemy.Configure(101, "raider", 1f);

                StatusSnapshot[] burn =
                {
                    new StatusSnapshot(
                        1,
                        StatusType.Burn,
                        5,
                        7,
                        CardId.Invalid,
                        2,
                        1000,
                        30,
                        5,
                        5,
                        0)
                };
                enemy.ApplySnapshot(
                    CreateEnemySnapshot(
                        101,
                        100000,
                        100000,
                        10000,
                        10000,
                        burn));
                Vector3 firstLogical = enemy.LogicalPosition;
                Assert.That(
                    enemy.transform.position,
                    Is.EqualTo(firstLogical));
                Assert.That(enemy.IsHitFeedbackActive, Is.False);

                enemy.ApplySnapshot(
                    CreateEnemySnapshot(
                        101,
                        100100,
                        100000,
                        9000,
                        10000,
                        burn));

                Assert.That(enemy.IsHitFeedbackActive, Is.True);
                Assert.That(
                    enemy.StatusVisual.ImpactFlashStrength,
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(
                    enemy.StatusVisual.CurrentTint.r,
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(
                    enemy.StatusVisual.CurrentTint.g,
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(
                    enemy.StatusVisual.IsImpactFlashVisible,
                    Is.True,
                    "A solid-white silhouette overlay must make " +
                    "the hit visible even on a multicoloured sprite.");
                Assert.That(
                    enemy.StatusVisual.ImpactFlashOverlayAlpha,
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(
                    enemy.transform.position.x,
                    Is.LessThan(enemy.LogicalPosition.x),
                    "An enemy moving right should recoil left.");
                Assert.That(
                    enemy.LogicalPosition,
                    Is.EqualTo(
                        new Vector3(100.1f, 100f, -0.05f)));

                yield return new WaitForSecondsRealtime(0.04f);

                Assert.That(
                    enemy.StatusVisual.ImpactFlashStrength,
                    Is.EqualTo(1f).Within(0.001f),
                    "The white hit frame should be held long enough " +
                    "to remain visible at the visual impact point.");
                Assert.That(
                    enemy.transform.localScale.x,
                    Is.GreaterThan(1f));
                Assert.That(
                    enemy.transform.localScale.y,
                    Is.LessThan(1f));

                yield return new WaitForSecondsRealtime(
                    StageOneEnemyView.HitStaggerSeconds + 0.05f);

                Assert.That(enemy.IsHitFeedbackActive, Is.False);
                Assert.That(
                    enemy.transform.position,
                    Is.EqualTo(enemy.LogicalPosition));
                Assert.That(
                    enemy.StatusVisual.ImpactFlashStrength,
                    Is.Zero.Within(0.001f));
                Assert.That(
                    enemy.StatusVisual.IsImpactFlashVisible,
                    Is.False);
                Assert.That(
                    enemy.transform.localScale,
                    Is.EqualTo(Vector3.one));
                Assert.That(enemy.StatusVisual.IsBurning, Is.True);
                Assert.That(
                    enemy.StatusVisual.CurrentTint,
                    Is.Not.EqualTo(Color.white),
                    "The burn tint should be restored after the flash.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(enemyObject);
                if (enemySprite != null)
                {
                    UnityEngine.Object.DestroyImmediate(enemySprite);
                }

                if (enemyTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(enemyTexture);
                }
            }
        }

        [UnityTest]
        public IEnumerator
            RemovedArrow_ContinuesToEnemyCenterBeforePoolDeactivation()
        {
            var enemyObject = new GameObject(
                "Impact Target Enemy",
                typeof(SpriteRenderer),
                typeof(StageOneEnemyView));
            var projectileObject = new GameObject(
                "Impact Arrow",
                typeof(SpriteRenderer),
                typeof(StageOneProjectileView));
            try
            {
                StageOneEnemyView enemy =
                    enemyObject.GetComponent<StageOneEnemyView>();
                enemy.Configure(202, "raider", 1f);
                enemy.ApplySnapshot(
                    CreateEnemySnapshot(
                        202,
                        100600,
                        100000,
                        10000,
                        10000,
                        Array.Empty<StatusSnapshot>()));

                StageOneProjectileView projectile =
                    projectileObject.GetComponent<
                        StageOneProjectileView>();
                projectile.Configure(null);
                var projectileSnapshot =
                    new ProjectileSnapshot(
                        303,
                        9,
                        SimPosition.FromMilliUnits(
                            100000,
                            100000),
                        8000,
                        20,
                        150,
                        0,
                        0,
                        10000,
                        0,
                        false,
                        false,
                        0);
                projectile.ApplySnapshot(projectileSnapshot);
                Vector3 centerBeforeImpact =
                    enemy.LogicalImpactCenter;

                Assert.That(
                    projectile.PrepareImpact(enemy),
                    Is.True);
                projectile.ReturnToPool();

                Assert.That(projectile.ProjectileId, Is.EqualTo(-1));
                Assert.That(
                    projectile.IsImpactPresentationActive,
                    Is.True);
                Assert.That(projectileObject.activeSelf, Is.True);
                Assert.That(
                    projectile.ImpactTargetPosition,
                    Is.EqualTo(centerBeforeImpact));
                Assert.That(
                    enemy.WorldImpactCenter,
                    Is.Not.EqualTo(centerBeforeImpact),
                    "Recoil starts only after the pre-impact center " +
                    "has been captured.");
                Assert.That(enemy.IsHitFeedbackActive, Is.True);
                Assert.That(
                    enemy.StatusVisual.ImpactFlashStrength,
                    Is.EqualTo(1f).Within(0.001f));

                yield return new WaitForSecondsRealtime(
                    StageOneProjectileView.MaximumImpactTravelSeconds +
                    StageOneProjectileView.ImpactCenterHoldSeconds +
                    0.05f);

                Assert.That(
                    projectile.IsImpactPresentationActive,
                    Is.False);
                Assert.That(projectileObject.activeSelf, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(projectileObject);
                UnityEngine.Object.DestroyImmediate(enemyObject);
            }
        }

        [Test]
        public void
            AimLine_FliesStraightFromArcherOriginToEnemyCenter()
        {
            var enemyObject = new GameObject(
                "Straight Flight Target",
                typeof(SpriteRenderer),
                typeof(StageOneEnemyView));
            var projectileObject = new GameObject(
                "Straight Flight Arrow",
                typeof(SpriteRenderer),
                typeof(StageOneProjectileView));
            try
            {
                StageOneEnemyView enemy =
                    enemyObject.GetComponent<StageOneEnemyView>();
                enemy.Configure(404, "raider", 1f);
                enemy.ApplySnapshot(
                    CreateEnemySnapshot(
                        404,
                        104000,
                        102000,
                        10000,
                        10000,
                        Array.Empty<StatusSnapshot>()));

                StageOneProjectileView projectile =
                    projectileObject.GetComponent<
                        StageOneProjectileView>();
                projectile.Configure(null);
                var launchOrigin =
                    new Vector3(100f, 101f, -0.08f);

                projectile.ApplySnapshot(
                    CreateProjectileSnapshot(
                        505,
                        100000,
                        100000,
                        404),
                    launchOrigin,
                    enemy);
                Vector3 launchPoint =
                    projectile.transform.position;

                projectile.ApplySnapshot(
                    CreateProjectileSnapshot(
                        505,
                        101000,
                        100000,
                        404),
                    null,
                    enemy);
                Vector3 middlePoint =
                    projectile.transform.position;

                projectile.ApplySnapshot(
                    CreateProjectileSnapshot(
                        505,
                        102000,
                        100000,
                        404),
                    null,
                    enemy);
                Vector3 finalFlightPoint =
                    projectile.transform.position;

                Assert.That(projectile.IsUsingAimLine, Is.True);
                AssertCollinear(
                    launchPoint,
                    middlePoint,
                    finalFlightPoint);

                Vector3 centerBeforeImpact =
                    enemy.LogicalImpactCenter;
                Assert.That(
                    projectile.PrepareImpact(enemy),
                    Is.True);
                AssertCollinear(
                    middlePoint,
                    finalFlightPoint,
                    projectile.ImpactTargetPosition);
                Assert.That(
                    projectile.ImpactTargetPosition,
                    Is.EqualTo(centerBeforeImpact));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(projectileObject);
                UnityEngine.Object.DestroyImmediate(enemyObject);
            }
        }

        [Test]
        public void
            AimLine_MovingTargetCorrectionDoesNotCreateSuddenTurn()
        {
            var enemyObject = new GameObject(
                "Moving Flight Target",
                typeof(SpriteRenderer),
                typeof(StageOneEnemyView));
            var projectileObject = new GameObject(
                "Tracking Flight Arrow",
                typeof(SpriteRenderer),
                typeof(StageOneProjectileView));
            try
            {
                StageOneEnemyView enemy =
                    enemyObject.GetComponent<StageOneEnemyView>();
                enemy.Configure(606, "raider", 1f);
                enemy.ApplySnapshot(
                    CreateEnemySnapshot(
                        606,
                        104000,
                        102000,
                        10000,
                        10000,
                        Array.Empty<StatusSnapshot>()));

                StageOneProjectileView projectile =
                    projectileObject.GetComponent<
                        StageOneProjectileView>();
                projectile.Configure(null);
                projectile.ApplySnapshot(
                    CreateProjectileSnapshot(
                        707,
                        100000,
                        100000,
                        606),
                    new Vector3(100f, 101f, -0.08f),
                    enemy);
                Vector3 pointA = projectile.transform.position;
                projectile.ApplySnapshot(
                    CreateProjectileSnapshot(
                        707,
                        101000,
                        100000,
                        606),
                    null,
                    enemy);
                Vector3 pointB = projectile.transform.position;

                enemy.ApplySnapshot(
                    CreateEnemySnapshot(
                        606,
                        104100,
                        102050,
                        10000,
                        10000,
                        Array.Empty<StatusSnapshot>()));
                projectile.ApplySnapshot(
                    CreateProjectileSnapshot(
                        707,
                        102000,
                        100000,
                        606),
                    null,
                    enemy);
                Vector3 pointC = projectile.transform.position;

                Assert.That(
                    Vector2.Angle(
                        (Vector2)(pointB - pointA),
                        (Vector2)(pointC - pointB)),
                    Is.LessThan(3f));

                projectile.PrepareImpact(enemy);
                Assert.That(
                    Vector2.Angle(
                        (Vector2)(pointC - pointB),
                        (Vector2)(
                            projectile.ImpactTargetPosition -
                            pointC)),
                    Is.LessThan(3f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(projectileObject);
                UnityEngine.Object.DestroyImmediate(enemyObject);
            }
        }

        [Test]
        public void
            PiercingArrow_ResumesFromCrossedEnemyCenter()
        {
            var enemyObject = new GameObject(
                "Piercing Flight Target",
                typeof(SpriteRenderer),
                typeof(StageOneEnemyView));
            var projectileObject = new GameObject(
                "Piercing Flight Arrow",
                typeof(SpriteRenderer),
                typeof(StageOneProjectileView));
            try
            {
                StageOneEnemyView enemy =
                    enemyObject.GetComponent<StageOneEnemyView>();
                enemy.Configure(808, "raider", 1f);
                enemy.ApplySnapshot(
                    CreateEnemySnapshot(
                        808,
                        104000,
                        102000,
                        10000,
                        10000,
                        Array.Empty<StatusSnapshot>()));

                StageOneProjectileView projectile =
                    projectileObject.GetComponent<
                        StageOneProjectileView>();
                projectile.Configure(null);
                projectile.ApplySnapshot(
                    CreateProjectileSnapshot(
                        909,
                        100000,
                        100000,
                        808),
                    new Vector3(100f, 101f, -0.08f),
                    enemy);
                projectile.ApplySnapshot(
                    CreateProjectileSnapshot(
                        909,
                        103000,
                        100000,
                        808),
                    null,
                    enemy);
                Assert.That(
                    projectile.PrepareImpact(enemy),
                    Is.True);
                Vector3 crossedCenter =
                    projectile.ImpactTargetPosition;

                projectile.ApplySnapshot(
                    CreateProjectileSnapshot(
                        909,
                        104000,
                        100000,
                        808),
                    null,
                    enemy);

                Assert.That(
                    projectile.IsImpactPresentationActive,
                    Is.False);
                Assert.That(
                    projectile.transform.position,
                    Is.EqualTo(crossedCenter));
                Assert.That(projectile.IsUsingAimLine, Is.False);

                projectile.ApplySnapshot(
                    CreateProjectileSnapshot(
                        909,
                        105000,
                        100000,
                        808),
                    null,
                    enemy);
                Assert.That(
                    projectile.transform.position,
                    Is.EqualTo(
                        crossedCenter + Vector3.right));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(projectileObject);
                UnityEngine.Object.DestroyImmediate(enemyObject);
            }
        }

        private static EnemySnapshot CreateEnemySnapshot(
            int id,
            int xMilli,
            int yMilli,
            long healthMilli,
            long maxHealthMilli,
            StatusSnapshot[] statuses)
        {
            StatusSnapshot[] details =
                statuses ?? Array.Empty<StatusSnapshot>();
            StatusType[] types =
                details.Length > 0
                    ? new[] { details[0].Type }
                    : Array.Empty<StatusType>();
            return new EnemySnapshot(
                id,
                "raider",
                id,
                0,
                SimPosition.FromMilliUnits(xMilli, yMilli),
                healthMilli,
                maxHealthMilli,
                0,
                0,
                10000,
                10000,
                0,
                100,
                1,
                1,
                1,
                0,
                true,
                false,
                0,
                types,
                details,
                0);
        }

        private static ProjectileSnapshot CreateProjectileSnapshot(
            int id,
            int xMilli,
            int yMilli,
            int targetId)
        {
            return new ProjectileSnapshot(
                id,
                9,
                SimPosition.FromMilliUnits(xMilli, yMilli),
                8000,
                20,
                150,
                0,
                0,
                10000,
                0,
                false,
                false,
                0,
                targetId);
        }

        private static void AssertCollinear(
            Vector3 first,
            Vector3 second,
            Vector3 third)
        {
            Vector2 firstLeg = second - first;
            Vector2 secondLeg = third - second;
            float cross =
                firstLeg.x * secondLeg.y -
                firstLeg.y * secondLeg.x;
            Assert.That(
                Mathf.Abs(cross),
                Is.LessThan(0.001f),
                "The arrow changed direction before impact.");
        }
    }
}
