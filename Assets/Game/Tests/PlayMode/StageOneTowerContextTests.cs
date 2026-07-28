using System.Collections;
using NUnit.Framework;
using RuleforgeTD.Battle;
using RuleforgeTD.GameLogic.Simulation;
using RuleforgeTD.Maps;
using RuleforgeTD.Towers.Archer;
using RuleforgeTD.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuleforgeTD.Tests.PlayMode
{
    public sealed class StageOneTowerContextTests
    {
        [UnityTest]
        public IEnumerator
            SingleTap_ShowsRangeAndActions_WithoutOpeningBlueprint()
        {
            yield return LoadStageAndBuildTower();

            StageOneBattleController controller =
                Object.FindObjectOfType<
                    StageOneBattleController>();
            TowerSnapshot tower =
                controller.CurrentSnapshot.Towers[0];
            TowerSelectionView selection =
                Object.FindObjectOfType<TowerSelectionView>();

            Assert.That(selection, Is.Not.Null);
            Assert.That(selection.RequestPointerClick(), Is.True);
            yield return null;

            Assert.That(
                controller.SelectedTowerId,
                Is.EqualTo(tower.Id));
            Assert.That(
                controller.IsTowerBlueprintOpen,
                Is.False);
            Assert.That(controller.IsPaused, Is.False);
            Assert.That(
                controller.LoadoutView.IsVisible,
                Is.False);
            Assert.That(selection.IsSelected, Is.True);
            Assert.That(selection.IsContextVisible, Is.True);
            Assert.That(
                selection.AttackRangeWorld,
                Is.EqualTo(6f).Within(0.001f));
            Assert.That(selection.AttackRangeRoot, Is.Not.Null);
            Assert.That(
                selection.AttackRangeRoot.gameObject.activeSelf,
                Is.True);

            LineRenderer[] rangeDashes =
                selection.AttackRangeRoot
                    .GetComponentsInChildren<LineRenderer>();
            Assert.That(rangeDashes.Length, Is.EqualTo(48));
            LineRenderer firstDash = rangeDashes[0];
            Assert.That(firstDash.loop, Is.False);
            Assert.That(firstDash.positionCount, Is.EqualTo(4));
            Assert.That(firstDash.numCapVertices, Is.Zero);
            Assert.That(firstDash.numCornerVertices, Is.Zero);
            Assert.That(
                firstDash.startWidth,
                Is.EqualTo(0.02f).Within(0.001f));
            Assert.That(
                firstDash.endWidth,
                Is.EqualTo(firstDash.startWidth)
                    .Within(0.001f));
            Assert.That(
                Vector2.Distance(
                    selection.transform.position,
                    firstDash.GetPosition(0)),
                Is.EqualTo(6f).Within(0.01f));

            LineRenderer secondDash = rangeDashes[1];
            Vector2 firstDashEnd =
                firstDash.GetPosition(
                    firstDash.positionCount - 1) -
                selection.transform.position;
            Vector2 secondDashStart =
                secondDash.GetPosition(0) -
                selection.transform.position;
            Assert.That(
                Vector2.SignedAngle(
                    firstDashEnd,
                    secondDashStart),
                Is.InRange(1.5f, 3f),
                "The dash gaps should stay compact and visibly separated.");

            Vector3 initialDashStart =
                firstDash.GetPosition(0);
            float previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(0.12f);
            Vector3 rotatedDashStart =
                firstDash.GetPosition(0);
            Time.timeScale = previousTimeScale;

            Vector2 initialDirection =
                initialDashStart -
                selection.transform.position;
            Vector2 rotatedDirection =
                rotatedDashStart -
                selection.transform.position;
            Assert.That(
                Vector2.SignedAngle(
                    initialDirection,
                    rotatedDirection),
                Is.LessThan(-0.5f),
                "The dashed range must rotate clockwise with unscaled time.");

            StageOneTowerActionView actions =
                controller.TowerActionView;
            Assert.That(actions, Is.Not.Null);
            Assert.That(actions.IsVisible, Is.True);
            Assert.That(actions.Target, Is.SameAs(selection));
            Assert.That(actions.UpgradeButton.interactable, Is.True);
            Assert.That(actions.CardsButton.interactable, Is.True);
            Assert.That(
                actions.UpgradeButton
                    .GetComponentInChildren<UnityEngine.UI.Text>()
                    .text,
                Is.EqualTo("타워 업그레이드 · 100G"));
            Assert.That(
                actions.CardsButton
                    .GetComponentInChildren<UnityEngine.UI.Text>()
                    .text,
                Is.EqualTo("카드 장착하기"));

            actions.CardsButton.onClick.Invoke();
            Assert.That(
                controller.IsTowerBlueprintOpen,
                Is.True);
            Assert.That(controller.IsPaused, Is.True);
            Assert.That(actions.IsVisible, Is.False);
            Assert.That(selection.IsContextVisible, Is.False);
            Assert.That(
                selection.AttackRangeRoot.gameObject.activeSelf,
                Is.False);
        }

        [UnityTest]
        public IEnumerator
            HitArea_FollowsVisibleTowerAfterConstructionAnimation()
        {
            yield return LoadStageAndBuildTower();

            TowerSelectionView selection =
                Object.FindObjectOfType<TowerSelectionView>();
            ArcherTowerView tower =
                selection.GetComponent<ArcherTowerView>();
            BoxCollider2D hitArea =
                selection.GetComponent<BoxCollider2D>();

            Assert.That(tower, Is.Not.Null);
            Assert.That(tower.TowerRenderer, Is.Not.Null);
            Assert.That(hitArea, Is.Not.Null);

            yield return new WaitForSecondsRealtime(0.9f);
            yield return null;
            Physics2D.SyncTransforms();

            Bounds visibleBounds =
                tower.TowerRenderer.bounds;
            Bounds clickBounds = hitArea.bounds;
            Assert.That(
                hitArea.OverlapPoint(visibleBounds.center),
                Is.True,
                "The visible tower center must remain clickable " +
                "after its construction frames realign.");
            Assert.That(
                clickBounds.min.y,
                Is.LessThanOrEqualTo(
                    visibleBounds.min.y + 0.02f));
            Assert.That(
                clickBounds.max.y,
                Is.GreaterThanOrEqualTo(
                    visibleBounds.max.y - 0.02f));
        }

        [UnityTest]
        public IEnumerator
            OccupiedBuildSite_DoesNotInterceptVisibleTowerBase()
        {
            yield return LoadStageAndBuildTower();

            StageOneBattleController controller =
                Object.FindObjectOfType<
                    StageOneBattleController>();
            TowerBuildSiteView site =
                controller.StageMap.GetBuildSite(0);
            TowerSelectionView selection =
                Object.FindObjectOfType<TowerSelectionView>();
            Collider2D siteCollider =
                site.GetComponent<Collider2D>();
            BoxCollider2D towerCollider =
                selection.GetComponent<BoxCollider2D>();

            Assert.That(
                site.State,
                Is.EqualTo(
                    TowerBuildSiteVisualState.Occupied));
            Assert.That(siteCollider, Is.Not.Null);
            Assert.That(towerCollider, Is.Not.Null);
            Assert.That(
                siteCollider.enabled,
                Is.False,
                "An occupied build-site collider must not steal " +
                "clicks from the visible tower base.");

            yield return new WaitForSecondsRealtime(0.9f);
            yield return null;
            Physics2D.SyncTransforms();

            Vector2 towerBasePoint =
                site.transform.position;
            Assert.That(
                towerCollider.OverlapPoint(towerBasePoint),
                Is.True,
                "The click point formerly covered by the build site " +
                "must belong to the tower selection collider.");
            Collider2D[] hits =
                Physics2D.OverlapPointAll(towerBasePoint);
            CollectionAssert.Contains(hits, towerCollider);
            CollectionAssert.DoesNotContain(hits, siteCollider);

            site.ApplySimulationState(true, false);
            Assert.That(
                siteCollider.enabled,
                Is.True,
                "An available build site must restore its input collider.");
            site.ApplySimulationState(true, true);
            Assert.That(siteCollider.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator
            UpgradeKeepsFieldContext_AndDoubleTapOpensBlueprint()
        {
            yield return LoadStageAndBuildTower();

            StageOneBattleController controller =
                Object.FindObjectOfType<
                    StageOneBattleController>();
            int towerId =
                controller.CurrentSnapshot.Towers[0].Id;
            TowerSelectionView selection =
                Object.FindObjectOfType<TowerSelectionView>();

            Assert.That(selection.RequestSelection(), Is.True);
            yield return null;
            Assert.That(
                controller.TowerActionView.IsVisible,
                Is.True);

            controller.TowerActionView.UpgradeButton
                .onClick.Invoke();
            yield return null;

            Assert.That(
                controller.CurrentSnapshot.Towers[0].Level,
                Is.EqualTo(2));
            Assert.That(
                controller.IsTowerBlueprintOpen,
                Is.False);
            Assert.That(
                controller.SelectedTowerId,
                Is.EqualTo(towerId));
            selection =
                controller.SelectedTowerSelectionView;
            Assert.That(selection, Is.Not.Null);
            Assert.That(selection.IsContextVisible, Is.True);
            Assert.That(
                controller.TowerActionView.Target,
                Is.SameAs(selection));

            Camera stageCamera = Camera.main;
            Assert.That(stageCamera, Is.Not.Null);
            float distance =
                Mathf.Abs(
                    stageCamera.transform.position.z -
                    selection.transform.position.z);
            Vector3 nearRight =
                stageCamera.ViewportToWorldPoint(
                    new Vector3(0.99f, 0.5f, distance));
            nearRight.z = selection.transform.position.z;
            selection.transform.position = nearRight;
            Physics2D.SyncTransforms();
            yield return null;
            controller.TowerActionView.RefreshPosition();
            Assert.That(
                controller.TowerActionView.IsPlacedOnLeft,
                Is.True,
                "The compact actions must flip left at the right edge.");

            Assert.That(
                selection.RequestPointerClick(),
                Is.True);
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(
                selection.RequestPointerClick(),
                Is.True);
            Assert.That(
                controller.IsTowerBlueprintOpen,
                Is.True);
            Assert.That(controller.IsPaused, Is.True);
            Assert.That(
                controller.TowerActionView.IsVisible,
                Is.False);
            Assert.That(selection.IsContextVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator
            RewardClose_AllowsNextTowerTapToShowRangeAndActions()
        {
            yield return LoadStageAndBuildTower();

            StageOneBattleController controller =
                Object.FindObjectOfType<
                    StageOneBattleController>();
            int towerId =
                controller.CurrentSnapshot.Towers[0].Id;
            TowerSelectionView selection =
                controller.SelectedTowerSelectionView;

            Assert.That(selection, Is.Not.Null);
            Assert.That(selection.IsContextVisible, Is.True);
            Assert.That(
                controller.TowerActionView.IsVisible,
                Is.True);

            controller.Hud.ShowRewardChoices(
                new[]
                {
                    new StageOneCardDisplay(
                        "split",
                        "분열",
                        "탄환이 두 발로 나뉩니다.")
                });
            Assert.That(controller.Hud.IsRewardVisible, Is.True);
            Assert.That(controller.SelectedTowerId, Is.EqualTo(-1));
            Assert.That(selection.IsSelected, Is.False);
            Assert.That(selection.IsContextVisible, Is.False);
            Assert.That(
                controller.TowerActionView.IsVisible,
                Is.False);

            EventSystem.current.SetSelectedGameObject(
                controller.Hud
                    .GetRewardChoiceButton(0).gameObject);
            controller.Hud.HideRewardChoices();

            Assert.That(controller.Hud.IsRewardVisible, Is.False);
            Assert.That(
                controller.Hud.GetRewardChoiceButton(0)
                    .gameObject.activeInHierarchy,
                Is.False,
                "No hidden reward graphic may remain in the UI raycast hierarchy.");
            Assert.That(
                EventSystem.current.currentSelectedGameObject,
                Is.Null);

            TowerBuildSiteView occupiedSite =
                controller.StageMap.GetBuildSite(0);
            Collider2D occupiedSiteCollider =
                occupiedSite.GetComponent<Collider2D>();
            BoxCollider2D towerCollider =
                selection.GetComponent<BoxCollider2D>();
            Physics2D.SyncTransforms();
            Vector2 visibleTowerBase =
                occupiedSite.transform.position;
            Assert.That(
                occupiedSiteCollider.enabled,
                Is.False,
                "Reward close must not expose the stale occupied-site " +
                "collider beneath the tower.");
            Assert.That(
                towerCollider.OverlapPoint(visibleTowerBase),
                Is.True,
                "The visible tower base must resolve to the tower " +
                "selection target after reward close.");

            Assert.That(selection.RequestPointerClick(), Is.True);
            yield return null;

            Assert.That(
                controller.SelectedTowerId,
                Is.EqualTo(towerId));
            Assert.That(selection.IsContextVisible, Is.True);
            Assert.That(
                selection.AttackRangeRoot.gameObject.activeSelf,
                Is.True);
            Assert.That(
                controller.TowerActionView.IsVisible,
                Is.True);
        }

        [UnityTest]
        public IEnumerator
            BlueprintRoundTrip_FirstTowerTapRestoresUpgradeActions()
        {
            yield return LoadStageAndBuildTower();

            StageOneBattleController controller =
                Object.FindObjectOfType<
                    StageOneBattleController>();
            int towerId =
                controller.CurrentSnapshot.Towers[0].Id;
            TowerSelectionView selection =
                controller.SelectedTowerSelectionView;

            Assert.That(selection, Is.Not.Null);
            Assert.That(selection.RequestPointerClick(), Is.True);
            controller.TowerActionView.CardsButton.onClick.Invoke();
            Assert.That(controller.IsTowerBlueprintOpen, Is.True);

            // Exercise an interrupted/quick modal round-trip as well as the
            // normal close path. This used to leave the pre-blueprint tap in
            // TowerSelectionView and could classify the next tap as a double
            // click, skipping the compact upgrade actions.
            EventSystem.current.SetSelectedGameObject(
                controller.LoadoutView.CloseButton.gameObject);
            controller.LoadoutView.CloseButton.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.34f);

            Assert.That(controller.IsTowerBlueprintOpen, Is.False);
            Assert.That(controller.LoadoutView.IsVisible, Is.False);
            Assert.That(
                EventSystem.current.currentSelectedGameObject,
                Is.Null);

            Assert.That(selection.RequestPointerClick(), Is.True);
            yield return null;

            Assert.That(controller.IsTowerBlueprintOpen, Is.False);
            Assert.That(
                controller.SelectedTowerId,
                Is.EqualTo(towerId));
            Assert.That(selection.IsContextVisible, Is.True);
            Assert.That(
                controller.TowerActionView.IsVisible,
                Is.True);
            Assert.That(
                controller.TowerActionView.UpgradeButton.interactable,
                Is.True);
        }

        [UnityTest]
        public IEnumerator
            BackgroundClick_HidesTowerActionsAndAttackRange()
        {
            yield return LoadStageAndBuildTower();

            StageOneBattleController controller =
                Object.FindObjectOfType<
                    StageOneBattleController>();
            TowerSelectionView selection =
                controller.SelectedTowerSelectionView;

            Assert.That(selection, Is.Not.Null);
            Assert.That(selection.IsSelected, Is.True);
            Assert.That(selection.IsContextVisible, Is.True);
            Assert.That(
                selection.AttackRangeRoot.gameObject.activeSelf,
                Is.True);
            Assert.That(
                controller.TowerActionView.IsVisible,
                Is.True);

            Assert.That(
                RequestBackgroundClick(controller),
                Is.True,
                "The test scene must expose at least one UI-free " +
                "background point.");
            yield return null;

            Assert.That(controller.SelectedTowerId, Is.EqualTo(-1));
            Assert.That(selection.IsSelected, Is.False);
            Assert.That(selection.IsContextVisible, Is.False);
            Assert.That(
                selection.AttackRangeRoot.gameObject.activeSelf,
                Is.False);
            Assert.That(
                controller.TowerActionView.IsVisible,
                Is.False);
        }

        [UnityTest]
        public IEnumerator
            BackgroundClick_ClosesTowerBuildPicker()
        {
            yield return LoadStageAndBuildTower();

            StageOneBattleController controller =
                Object.FindObjectOfType<
                    StageOneBattleController>();
            TowerBuildSiteView availableSite = null;
            for (int siteIndex = 0;
                 siteIndex < controller.StageMap.BuildSiteCount;
                 siteIndex++)
            {
                TowerBuildSiteView candidate =
                    controller.StageMap.GetBuildSite(siteIndex);
                if (candidate != null && candidate.CanBuild)
                {
                    availableSite = candidate;
                    break;
                }
            }

            Assert.That(availableSite, Is.Not.Null);
            Assert.That(availableSite.RequestBuild(), Is.True);
            yield return null;

            Assert.That(
                controller.TowerBuildPickerView.IsVisible,
                Is.True);
            Assert.That(
                controller.PendingBuildPointIndex,
                Is.EqualTo(availableSite.BuildPointIndex));
            Assert.That(controller.SelectedTowerId, Is.EqualTo(-1));

            Assert.That(
                RequestBackgroundClick(controller),
                Is.True,
                "The test scene must expose at least one UI-free " +
                "background point.");
            yield return null;

            Assert.That(
                controller.TowerBuildPickerView.IsVisible,
                Is.False);
            Assert.That(
                controller.PendingBuildPointIndex,
                Is.EqualTo(-1));
            Assert.That(
                controller.CurrentSnapshot.Towers.Length,
                Is.EqualTo(1),
                "Clicking away must not accidentally build a tower.");
        }

        private static bool RequestBackgroundClick(
            StageOneBattleController controller)
        {
            StageOneCameraController cameraController =
                controller.CameraController;
            Assert.That(cameraController, Is.Not.Null);

            float[] viewportCoordinates =
            {
                0.08f,
                0.2f,
                0.35f,
                0.5f,
                0.65f,
                0.8f,
                0.92f
            };
            for (int yIndex = 0;
                 yIndex < viewportCoordinates.Length;
                 yIndex++)
            {
                for (int xIndex = 0;
                     xIndex < viewportCoordinates.Length;
                     xIndex++)
                {
                    Vector3 screenPoint =
                        Camera.main.ViewportToScreenPoint(
                            new Vector3(
                                viewportCoordinates[xIndex],
                                viewportCoordinates[yIndex],
                                0f));
                    if (cameraController.RequestBackgroundClick(
                            screenPoint))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerator LoadStageAndBuildTower()
        {
            SceneManager.LoadScene(
                "Stage01",
                LoadSceneMode.Single);
            yield return null;
            yield return null;

            StageOneBattleController controller =
                Object.FindObjectOfType<
                    StageOneBattleController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.IsInitialized, Is.True);
            KeyCode[] fundingSequence =
            {
                KeyCode.UpArrow,
                KeyCode.UpArrow,
                KeyCode.DownArrow,
                KeyCode.DownArrow,
                KeyCode.LeftArrow,
                KeyCode.RightArrow,
                KeyCode.LeftArrow,
                KeyCode.RightArrow,
                KeyCode.B,
                KeyCode.A
            };
            for (int index = 0;
                 index < fundingSequence.Length;
                 index++)
            {
                controller.ProcessKonamiKey(
                    fundingSequence[index]);
            }
            TowerBuildSiteView site =
                controller.StageMap.GetBuildSite(0);
            Assert.That(site.RequestBuild(), Is.True);
            yield return null;

            Assert.That(
                controller.CurrentSnapshot.Towers.Length,
                Is.Zero);
            Assert.That(
                controller.TowerBuildPickerView.IsVisible,
                Is.True);
            controller.TowerBuildPickerView
                .GetOptionButton(0).onClick.Invoke();
            yield return null;

            Assert.That(
                controller.IsTowerBlueprintOpen,
                Is.False);
            Assert.That(
                controller.SelectedTowerId,
                Is.EqualTo(
                    controller.CurrentSnapshot.Towers[0].Id));
            Assert.That(
                controller.TowerActionView.IsVisible,
                Is.True);
        }
    }
}
