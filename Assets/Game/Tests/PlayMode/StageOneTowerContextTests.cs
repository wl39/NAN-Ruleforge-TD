using System.Collections;
using NUnit.Framework;
using RuleforgeTD.Battle;
using RuleforgeTD.GameLogic.Simulation;
using RuleforgeTD.Maps;
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
                Is.EqualTo("타워 업그레이드"));
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

            Assert.That(controller.SelectTower(towerId), Is.True);
            controller.LoadoutView.CloseButton.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.34f);
            Assert.That(controller.SelectedTowerId, Is.EqualTo(-1));

            controller.Hud.ShowRewardChoices(
                new[]
                {
                    new StageOneCardDisplay(
                        "split",
                        "분열",
                        "탄환이 두 발로 나뉩니다.")
                });
            EventSystem.current.SetSelectedGameObject(
                controller.Hud
                    .GetRewardChoiceButton(0).gameObject);
            controller.Hud.HideRewardChoices();

            Assert.That(
                EventSystem.current.currentSelectedGameObject,
                Is.Null);
            Assert.That(selection.RequestSelection(), Is.True);
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
