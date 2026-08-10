using System.Collections;
using System.Reflection;
using NUnit.Framework;
using RuleforgeTD.Battle;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;
using RuleforgeTD.Maps;
using RuleforgeTD.Tutorial;
using RuleforgeTD.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RuleforgeTD.Tests.PlayMode.Tutorial
{
    public sealed class BattleTutorialControllerFlowTests
    {
        [TearDown]
        public void TearDown()
        {
            TutorialProgressStore.CreateCurrent().ResetForTests();
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator
            ManualReplay_CompletesGuidedBattleFlowAndPersistsResult()
        {
            TutorialProgressStore store =
                TutorialProgressStore.CreateCurrent();
            store.ResetForTests();
            store.MarkCompleted();
            store.RequestManualReplay();

            SceneManager.LoadScene("Stage01", LoadSceneMode.Single);
            yield return null;
            yield return null;
            yield return null;

            StageOneBattleController battle =
                Object.FindObjectOfType<StageOneBattleController>();
            Assert.That(battle, Is.Not.Null);
            BattleTutorialController tutorial =
                battle.TutorialController;
            Assert.That(tutorial, Is.Not.Null);
            Assert.That(tutorial.IsCoreActive, Is.True);
            Assert.That(
                tutorial.CurrentStepId,
                Is.EqualTo(TutorialIds.Steps.Objective));
            Assert.That(tutorial.RequestsWorldPause, Is.True);
            Assert.That(
                tutorial.Allows(TutorialAction.Continue),
                Is.True);
            Assert.That(
                tutorial.Allows(TutorialAction.StartWave),
                Is.False);
            Assert.That(store.IsManualReplayRequested, Is.True,
                "The replay marker must survive until completion or skip.");

            for (int page = 0; page < 3; page++)
            {
                tutorial.Overlay.NextButton.onClick.Invoke();
            }
            Assert.That(
                tutorial.CurrentStepId,
                Is.EqualTo(TutorialIds.Steps.WavePreview));

            battle.WavePreviewView.SummaryButton.onClick.Invoke();
            yield return null;
            Assert.That(battle.WavePreviewView.IsDetailVisible, Is.True);
            Assert.That(tutorial.Overlay.HasResolvedAnchor, Is.True);
            tutorial.Overlay.NextButton.onClick.Invoke();
            Assert.That(
                tutorial.CurrentStepId,
                Is.EqualTo(TutorialIds.Steps.TowerBuild));

            TowerBuildSiteView site = battle.StageMap.GetBuildSite(0);
            Assert.That(site.RequestBuild(), Is.True);
            yield return null;
            Assert.That(
                tutorial.CurrentStepId,
                Is.EqualTo(TutorialIds.Steps.TowerBuild));
            Button towerOption =
                battle.TowerBuildPickerView.GetOptionButton(0);
            tutorial.Overlay.RefreshNow();
            Assert.That(
                tutorial.Overlay.IsScreenPointInsideHole(
                    RectTransformUtility.WorldToScreenPoint(
                        null,
                        towerOption.transform.position)),
                Is.True,
                "The spotlight must move from the site to the tower option.");
            towerOption.onClick.Invoke();
            yield return null;
            Assert.That(battle.CurrentSnapshot.Towers.Length, Is.EqualTo(1));
            Assert.That(
                tutorial.CurrentStepId,
                Is.EqualTo(TutorialIds.Steps.Loadout),
                "The build command already selected the new tower, so the " +
                "satisfied selection step should complete automatically.");

            TowerSelectionView tower = FindLowestTower();
            battle.TowerActionView.CardsButton.onClick.Invoke();
            yield return null;
            yield return null;
            Assert.That(battle.IsTowerBlueprintOpen, Is.True);
            Assert.That(
                tutorial.CurrentStepId,
                Is.EqualTo(TutorialIds.Steps.CardDrag));

            Assert.That(battle.LoadoutView.VisibleCardCount, Is.GreaterThan(0));
            tutorial.Overlay.RefreshNow();
            Vector2 inventoryCardCenter =
                RectTransformUtility.WorldToScreenPoint(
                    null,
                    battle.LoadoutView.GetCardButton(0)
                        .transform.position);
            Vector2 slotDropCenter =
                RectTransformUtility.WorldToScreenPoint(
                    null,
                    battle.LoadoutView.GetSlotDropSurface(0)
                        .rectTransform.position);
            Assert.That(
                tutorial.Overlay.CurrentContent.BlockOutsideHole,
                Is.True,
                "The guided drag should keep unrelated input blocked.");
            Assert.That(
                tutorial.Overlay.IsScreenPointInsideHole(
                    inventoryCardCenter),
                Is.True,
                "The drag must be able to start on the inventory card.");
            Assert.That(
                tutorial.Overlay.IsScreenPointInsideHole(slotDropCenter),
                Is.True,
                "The same pass-through region must include the target slot.");
            Assert.That(battle.LoadoutView.RequestCardDrop(0, 0), Is.True);
            yield return null;
            Assert.That(
                tutorial.CurrentStepId,
                Is.EqualTo(TutorialIds.Steps.CardTarget));
            battle.LoadoutView.GetSlotEnemyButton(0).onClick.Invoke();
            Assert.That(
                battle.LoadoutView.GetSlotSubjectType(0),
                Is.EqualTo(SubjectType.Enemy));
            Assert.That(
                tutorial.CurrentStepId,
                Is.EqualTo(TutorialIds.Steps.CardTarget));
            battle.LoadoutView.GetSlotProjectileButton(0).onClick.Invoke();
            Assert.That(
                battle.LoadoutView.GetSlotSubjectType(0),
                Is.EqualTo(SubjectType.Projectile));
            Assert.That(
                tutorial.CurrentStepId,
                Is.EqualTo(TutorialIds.Steps.CardOrder));

            tutorial.Overlay.NextButton.onClick.Invoke();
            Assert.That(
                tutorial.CurrentStepId,
                Is.EqualTo(TutorialIds.Steps.FirstWave));
            battle.LoadoutView.CloseButton.onClick.Invoke();
            float closeDeadline = Time.realtimeSinceStartup + 2f;
            while (battle.IsTowerBlueprintOpen &&
                   Time.realtimeSinceStartup < closeDeadline)
            {
                yield return null;
            }
            Assert.That(battle.IsTowerBlueprintOpen, Is.False);

            GameSimulation simulation = GetSimulation(battle);
            Assert.That(
                simulation.Submit(GameCommand.GrantDebugGold(100000))
                    .Accepted,
                Is.True);
            Synchronize(battle);
            for (int buildPoint = 1;
                 buildPoint < battle.StageMap.BuildSiteCount;
                 buildPoint++)
            {
                Assert.That(
                    battle.TryBuildAt("ballista", buildPoint),
                    Is.True);
            }

            battle.Hud.PlayButton.onClick.Invoke();
            Assert.That(battle.CurrentPhase, Is.EqualTo(RunPhase.Combat));
            Assert.That(
                tutorial.CurrentStepId,
                Is.EqualTo(TutorialIds.Steps.EnemyInspection));

            battle.Hud.PlayButton.onClick.Invoke();
            Assert.That(battle.IsPaused, Is.True);
            tutorial.Overlay.NextButton.onClick.Invoke();
            Assert.That(tutorial.RequestsWorldPause, Is.False);
            Assert.That(tutorial.Overlay.BodyText.text, Does.Contain("재생"));
            Assert.That(
                tutorial.Allows(TutorialAction.SelectEnemy),
                Is.False);
            battle.Hud.PlayButton.onClick.Invoke();
            Assert.That(battle.IsPaused, Is.False);
            Assert.That(tutorial.Overlay.IsVisible, Is.False);

            StepUntilEnemyAppears(simulation);
            Synchronize(battle);
            yield return null;
            yield return null;
            Assert.That(tutorial.RequestsWorldPause, Is.True);
            Assert.That(tutorial.Overlay.HasResolvedAnchor, Is.True);
            StageOneEnemyView enemy =
                Object.FindObjectOfType<StageOneEnemyView>();
            Assert.That(enemy, Is.Not.Null);
            Assert.That(enemy.SelectionView.RequestSelection(), Is.True);
            yield return null;
            Assert.That(battle.EnemyInspectionView.IsVisible, Is.True);
            Assert.That(tutorial.Overlay.HasResolvedAnchor, Is.True);
            tutorial.Overlay.NextButton.onClick.Invoke();
            Assert.That(
                tutorial.CurrentStepId,
                Is.EqualTo(TutorialIds.Steps.TowerUpgrade));

            FastForwardCombat(simulation);
            Synchronize(battle);
            yield return null;
            Assert.That(battle.CurrentPhase, Is.EqualTo(RunPhase.Planning));
            tower = FindLowestTower();
            Assert.That(tower.RequestSelection(), Is.True);
            battle.TowerActionView.UpgradeButton.onClick.Invoke();
            yield return null;
            Assert.That(
                battle.CurrentSnapshot.Towers[0].Level,
                Is.EqualTo(2));

            battle.Hud.PlayButton.onClick.Invoke();
            Assert.That(
                tutorial.CurrentStepId,
                Is.EqualTo(TutorialIds.Steps.DraftReward));
            FastForwardCombat(simulation);
            Synchronize(battle);
            yield return null;
            Assert.That(battle.CurrentPhase, Is.EqualTo(RunPhase.Planning));
            battle.Hud.PlayButton.onClick.Invoke();
            FastForwardCombat(simulation);
            Synchronize(battle);
            yield return null;
            yield return null;
            Assert.That(
                battle.CurrentPhase,
                Is.EqualTo(RunPhase.CardPackChoice));
            tutorial.Overlay.RefreshNow();
            for (int choice = 0;
                 choice < StageOneHudView.RewardChoiceCapacity;
                 choice++)
            {
                Button choiceButton =
                    battle.Hud.GetRewardChoiceButton(choice);
                Assert.That(
                    tutorial.Overlay.IsScreenPointInsideHole(
                        RectTransformUtility.WorldToScreenPoint(
                            null,
                            choiceButton.transform.position)),
                    Is.True,
                    "Every reward choice must remain clickable.");
            }

            battle.Hud.GetRewardChoiceButton(1).onClick.Invoke();
            yield return null;
            Assert.That(
                battle.CurrentPhase,
                Is.Not.EqualTo(RunPhase.CardPackLoadout));
            Assert.That(
                tutorial.CurrentStepId,
                Is.EqualTo(TutorialIds.Steps.Complete));
            Assert.That(
                tutorial.Overlay.ProgressText.text,
                Is.EqualTo("12 / 12"));
            tutorial.Overlay.NextButton.onClick.Invoke();
            yield return null;

            Assert.That(tutorial.IsCoreActive, Is.False);
            Assert.That(tutorial.RequestsWorldPause, Is.False);
            Assert.That(store.IsCompleted, Is.True);
            Assert.That(store.IsManualReplayRequested, Is.False);
        }

        [UnityTest]
        public IEnumerator Skip_ReleasesPauseAndResolvesReplayImmediately()
        {
            TutorialProgressStore store =
                TutorialProgressStore.CreateCurrent();
            store.ResetForTests();
            store.RequestManualReplay();
            SceneManager.LoadScene("Stage01", LoadSceneMode.Single);
            yield return null;
            yield return null;
            yield return null;

            StageOneBattleController battle = Object
                .FindObjectOfType<StageOneBattleController>();
            BattleTutorialController tutorial = battle.TutorialController;
            Assert.That(tutorial.RequestsWorldPause, Is.True);

            for (int page = 0; page < 3; page++)
            {
                tutorial.Overlay.NextButton.onClick.Invoke();
            }
            battle.WavePreviewView.SummaryButton.onClick.Invoke();
            yield return null;
            tutorial.Overlay.NextButton.onClick.Invoke();
            Assert.That(
                battle.StageMap.GetBuildSite(0).RequestBuild(),
                Is.True);
            yield return null;
            battle.TowerBuildPickerView.GetOptionButton(0)
                .onClick.Invoke();
            yield return null;
            battle.TowerActionView.CardsButton.onClick.Invoke();
            yield return null;
            yield return null;
            Assert.That(
                battle.LoadoutView.RequestCardDrop(0, 0),
                Is.True);
            yield return null;
            battle.LoadoutView.GetSlotEnemyButton(0).onClick.Invoke();
            Assert.That(
                battle.LoadoutView.GetSlotSubjectType(0),
                Is.EqualTo(SubjectType.Enemy));

            Assert.That(
                tutorial.CurrentStepId,
                Is.EqualTo(TutorialIds.Steps.CardTarget));
            tutorial.Overlay.SkipButton.onClick.Invoke();
            yield return null;

            Assert.That(tutorial.IsCoreActive, Is.False);
            Assert.That(tutorial.RequestsWorldPause, Is.False);
            Assert.That(
                tutorial.Allows(TutorialAction.StartWave),
                Is.True);
            Assert.That(store.IsSkipped, Is.True);
            Assert.That(store.IsManualReplayRequested, Is.False);
            Assert.That(
                battle.LoadoutView.GetSlotSubjectType(0),
                Is.EqualTo(SubjectType.Projectile),
                "Skip must restore the temporary Enemy demonstration back " +
                "to the stable Projectile target.");
            Assert.That(battle.IsTowerBlueprintOpen, Is.True);
            battle.LoadoutView.CloseButton.onClick.Invoke();
            float closeDeadline = Time.realtimeSinceStartup + 2f;
            while (battle.IsTowerBlueprintOpen &&
                   Time.realtimeSinceStartup < closeDeadline)
            {
                yield return null;
            }
            Assert.That(battle.IsTowerBlueprintOpen, Is.False,
                "Skip must immediately restore normal loadout input.");
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator ContextualTip_UsesAndReleasesPresentationPause()
        {
            TutorialProgressStore store =
                TutorialProgressStore.CreateCurrent();
            store.ResetForTests();
            store.MarkCompleted();
            SceneManager.LoadScene("Stage01", LoadSceneMode.Single);
            yield return null;
            yield return null;
            yield return null;

            StageOneBattleController battle =
                Object.FindObjectOfType<StageOneBattleController>();
            BattleTutorialController tutorial = battle.TutorialController;
            Assert.That(tutorial.IsCoreActive, Is.False);

            SetPrivateField(tutorial, "contextualTipsEnabled", true);
            InvokePrivate(
                tutorial,
                "QueueContextualTip",
                TutorialIds.ContextualTips.SecondTower);
            InvokePrivate(
                tutorial,
                "TryShowNextContextualTip",
                battle.CurrentSnapshot);

            Assert.That(tutorial.IsShowingContextualTip, Is.True);
            Assert.That(tutorial.RequestsWorldPause, Is.True);
            Assert.That(
                tutorial.Overlay.SkipButton
                    .GetComponentInChildren<Text>(true).text,
                Is.EqualTo("건너뛰기"),
                "Contextual tutorial dismissal must use the full skip label.");
            SetPrivateField(tutorial, "contextualTipsEnabled", false);
            tutorial.Overlay.NextButton.onClick.Invoke();
            yield return null;

            Assert.That(tutorial.IsShowingContextualTip, Is.False);
            Assert.That(tutorial.RequestsWorldPause, Is.False);
            Assert.That(
                store.HasSeenContextualTip(
                    TutorialIds.ContextualTips.SecondTower),
                Is.True);
        }

        private static TowerSelectionView FindLowestTower()
        {
            TowerSelectionView[] towers =
                Object.FindObjectsOfType<TowerSelectionView>();
            TowerSelectionView result = null;
            for (int i = 0; i < towers.Length; i++)
            {
                if (towers[i].TowerId >= 0 &&
                    (result == null ||
                     towers[i].TowerId < result.TowerId))
                {
                    result = towers[i];
                }
            }
            Assert.That(result, Is.Not.Null);
            return result;
        }

        private static GameSimulation GetSimulation(
            StageOneBattleController battle)
        {
            PropertyInfo property = typeof(StageOneBattleController)
                .GetProperty(
                    "AuthoritativeSimulation",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null);
            return (GameSimulation)property.GetValue(battle);
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static void InvokePrivate(
            object target,
            string methodName,
            object argument)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(target, new[] { argument });
        }

        private static void Synchronize(StageOneBattleController battle)
        {
            MethodInfo method = typeof(StageOneBattleController)
                .GetMethod(
                    "SynchronizeAuthoritativeState",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(battle, null);
        }

        private static void StepUntilEnemyAppears(
            GameSimulation simulation)
        {
            int step = 0;
            while (simulation.Phase == RunPhase.Combat &&
                   simulation.GetSnapshot().Enemies.Length == 0 &&
                   step < 3000)
            {
                simulation.Step();
                step++;
            }
            Assert.That(
                simulation.GetSnapshot().Enemies.Length,
                Is.GreaterThan(0));
        }

        private static void FastForwardCombat(GameSimulation simulation)
        {
            int step = 0;
            while (simulation.Phase == RunPhase.Combat &&
                   step < 100000)
            {
                simulation.Step();
                step++;
            }
            Assert.That(step, Is.LessThan(100000));
            Assert.That(simulation.Phase, Is.Not.EqualTo(RunPhase.Defeat));
        }
    }
}
