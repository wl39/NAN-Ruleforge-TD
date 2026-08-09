using System.Collections;
using NUnit.Framework;
using RuleforgeTD.Battle;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;
using RuleforgeTD.Maps;
using RuleforgeTD.Rendering;
using RuleforgeTD.Towers.Archer;
using RuleforgeTD.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RuleforgeTD.Tests.PlayMode
{
    public sealed class StageOneGameFlowTests
    {
        [UnityTest]
        public IEnumerator
            Stage01_BuildClickCardsPlayPauseAndSpeedAreWired()
        {
            SceneManager.LoadScene("Stage01", LoadSceneMode.Single);
            yield return null;
            yield return null;

            StageOneBattleController controller =
                Object.FindObjectOfType<StageOneBattleController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.IsInitialized, Is.True);
            Assert.That(
                controller.CurrentPhase,
                Is.EqualTo(
                    RunPhase.AwaitingStartingTower));
            Assert.That(controller.Hud, Is.Not.Null);
            Assert.That(controller.WavePreviewView, Is.Not.Null);
            Assert.That(controller.WavePreviewView.IsVisible, Is.True);
            Assert.That(
                controller.WavePreviewView.TotalEnemyText.text,
                Does.Contain("35"));
            Assert.That(
                controller.WavePreviewView.GroupButtonCount,
                Is.EqualTo(1));
            Assert.That(
                controller.PresentationCatalog.UiFont,
                Is.Not.Null);
            Assert.That(
                controller.PresentationCatalog.TryGetTower(
                    "ballista",
                    1,
                    out GameObject towerPrefab,
                    out float towerScale),
                Is.True);
            Assert.That(towerPrefab, Is.Not.Null);
            Assert.That(
                towerScale,
                Is.EqualTo(1.65f).Within(0.00001f),
                "1.65 exactly matches the 62 px build-site footprint.");
            Assert.That(
                62f / 48f * towerScale,
                Is.EqualTo(
                    62f / 32f *
                    TowerBuildSiteView.AuthoredVisualScale)
                    .Within(0.00001f),
                "Tower and build-site opaque footprints must occupy the same world width.");
            Assert.That(
                controller.PresentationCatalog.TryGetTower(
                    "ballista",
                    7,
                    out GameObject highestAuthoredTower,
                    out _),
                Is.True);
            Assert.That(
                controller.PresentationCatalog.TryGetTower(
                    "ballista",
                    99,
                    out GameObject fallbackTower,
                    out _),
                Is.True);
            Assert.That(
                fallbackTower,
                Is.SameAs(highestAuthoredTower),
                "Presentation assets must fall back without capping " +
                "the data-authored gameplay level.");
            Assert.That(
                controller.PresentationCatalog
                    .TowerAppearanceBindingCount,
                Is.EqualTo(2));
            Assert.That(
                controller.PresentationCatalog
                    .GetTowerPrototypeTint(
                        "mutation_obelisk").b,
                Is.GreaterThan(
                    controller.PresentationCatalog
                        .GetTowerPrototypeTint(
                            "mutation_obelisk").g));
            Assert.That(
                controller.PresentationCatalog.TryGetEnemy(
                    "raider",
                    out GameObject enemyPrefab,
                    out float enemyScale),
                Is.True);
            Assert.That(enemyPrefab, Is.Not.Null);
            Assert.That(
                enemyScale,
                Is.EqualTo(1.638f).Within(0.00001f));
            string[] scaledEnemyIds =
            {
                "runner",
                "armored_knight",
                "elite_golem",
                "boss_guardian",
                "boss_summoner",
                "boss_time_walker"
            };
            float[] expectedEnemyScales =
            {
                1.5561f,
                2.0475f,
                2.7027f,
                2.9484f,
                2.6208f,
                3.1941f
            };
            for (int i = 0; i < scaledEnemyIds.Length; i++)
            {
                Assert.That(
                    controller.PresentationCatalog.TryGetEnemy(
                        scaledEnemyIds[i],
                        out _,
                        out float scaledEnemy),
                    Is.True);
                Assert.That(
                    scaledEnemy,
                    Is.EqualTo(
                        expectedEnemyScales[i])
                        .Within(0.00001f),
                    scaledEnemyIds[i] +
                    " should use its new canonical visual scale.");
            }
            Assert.That(
                StageOneProjectileView.VisualScaleMultiplier,
                Is.EqualTo(1.65f));
            Assert.That(
                controller.Hud.HudText.font,
                Is.SameAs(
                    controller.PresentationCatalog.UiFont));
            AssertUiFontCoverage(
                controller.PresentationCatalog);

            TowerBuildSiteView firstSite =
                controller.StageMap.GetBuildSite(0);
            Assert.That(firstSite.CanBuild, Is.True);
            Assert.That(
                firstSite.transform.localScale,
                Is.EqualTo(
                    Vector3.one *
                    TowerBuildSiteView.AuthoredVisualScale));
            Assert.That(firstSite.RequestBuild(), Is.True);
            yield return null;

            Assert.That(
                controller.CurrentSnapshot.Towers.Length,
                Is.Zero,
                "Clicking an empty slot must only open the picker.");
            Assert.That(
                firstSite.State,
                Is.EqualTo(
                    TowerBuildSiteVisualState.Available));
            StageOneTowerBuildPickerView buildPicker =
                controller.TowerBuildPickerView;
            Assert.That(buildPicker, Is.Not.Null);
            Assert.That(buildPicker.IsVisible, Is.True);
            Assert.That(buildPicker.Target, Is.SameAs(firstSite));
            Assert.That(
                controller.PendingBuildPointIndex,
                Is.EqualTo(firstSite.BuildPointIndex));
            Assert.That(buildPicker.OptionCount, Is.EqualTo(1));
            Assert.That(
                buildPicker.GetOptionId(0),
                Is.EqualTo("ballista"));
            Assert.That(
                buildPicker.GetOptionCost(0),
                Is.Zero);
            Button firstBuildOption =
                buildPicker.GetOptionButton(0);
            Text optionName = firstBuildOption.transform
                .Find("Name").GetComponent<Text>();
            Text optionPrice = firstBuildOption.transform
                .Find("Price").GetComponent<Text>();
            Text optionDescription = firstBuildOption.transform
                .Find("Description").GetComponent<Text>();
            Assert.That(optionName.fontSize, Is.EqualTo(17));
            Assert.That(optionName.rectTransform.offsetMin.x,
                Is.EqualTo(20f).Within(0.001f));
            Assert.That(optionName.rectTransform.offsetMax.y,
                Is.EqualTo(-12f).Within(0.001f));
            Assert.That(optionPrice.fontSize, Is.EqualTo(14));
            Assert.That(optionDescription.fontSize, Is.EqualTo(11));
            Assert.That(optionDescription.rectTransform.offsetMin,
                Is.EqualTo(new Vector2(20f, 12f)));
            Assert.That(optionDescription.rectTransform.offsetMax,
                Is.EqualTo(new Vector2(-20f, -40f)));
            Assert.That(
                buildPicker.PanelRoot.parent.GetComponent<
                    StageOneSafeAreaFitter>(),
                Is.Not.Null);
            buildPicker.GetOptionButton(0).onClick.Invoke();
            yield return null;

            Assert.That(
                controller.CurrentSnapshot.Towers.Length,
                Is.EqualTo(1));
            Assert.That(
                controller.CurrentPhase,
                Is.EqualTo(RunPhase.Planning));
            Assert.That(controller.TowerViewCount, Is.EqualTo(1));
            TowerSelectionView placedTower =
                controller.SelectedTowerSelectionView;
            Assert.That(placedTower, Is.Not.Null);
            SpriteRenderer[] placedTowerRenderers = placedTower
                .GetComponentsInChildren<SpriteRenderer>(true);
            Assert.That(placedTowerRenderers, Is.Not.Empty);
            for (int rendererIndex = 0;
                 rendererIndex < placedTowerRenderers.Length;
                 rendererIndex++)
            {
                Assert.That(
                    placedTowerRenderers[rendererIndex]
                        .sortingLayerName,
                    Is.EqualTo(WorldSortingLayers.Tower));
            }
            Assert.That(
                firstSite.State,
                Is.EqualTo(
                    TowerBuildSiteVisualState.Occupied));

            TowerSnapshot tower =
                controller.CurrentSnapshot.Towers[0];
            Assert.That(tower.CardInstanceIds.Length, Is.EqualTo(3));
            Assert.That(tower.Level, Is.EqualTo(1));
            CollectionAssert.AreEqual(
                new[] { -1, -1, -1 },
                tower.CardInstanceIds);
            Assert.That(
                controller.SelectedTowerId,
                Is.EqualTo(tower.Id));
            Assert.That(buildPicker.IsVisible, Is.False);
            Assert.That(
                controller.TowerActionView.IsVisible,
                Is.True);
            Assert.That(controller.LoadoutView.IsVisible, Is.False);
            Assert.That(controller.IsTowerBlueprintOpen, Is.False);
            Assert.That(controller.IsPaused, Is.False);
            Assert.That(controller.Hud.IsVisible, Is.True);
            Assert.That(controller.Hud.HudCanvas.enabled, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(1f));

            controller.TowerActionView.CardsButton
                .onClick.Invoke();
            Assert.That(controller.LoadoutView.IsVisible, Is.True);
            Assert.That(controller.IsTowerBlueprintOpen, Is.True);
            Assert.That(controller.IsPaused, Is.True);
            Assert.That(controller.Hud.IsVisible, Is.False);
            Assert.That(controller.Hud.HudCanvas.enabled, Is.False);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(
                controller.LoadoutView.TitleText.text,
                Does.Contain("궁수 타워"));
            Assert.That(
                controller.LoadoutView.BlueprintGraphic,
                Is.Not.Null);
            Assert.That(
                controller.LoadoutView.BlueprintGraphic
                    .BackgroundColor.r,
                Is.GreaterThan(
                    controller.LoadoutView.BlueprintGraphic
                        .BackgroundColor.b));
            Assert.That(
                controller.LoadoutView.GetComponentsInChildren<
                    StageOneBlueprintGridGraphic>(true).Length,
                Is.EqualTo(1));
            Assert.That(
                controller.LoadoutView.TowerPreviewContent.parent
                    .GetComponent<Image>(),
                Is.SameAs(
                    controller.LoadoutView.TowerPreviewBackplate));
            Assert.That(
                controller.LoadoutView.GetSlotButton(0)
                    .interactable,
                Is.True);
            Assert.That(
                controller.LoadoutView.GetSlotButton(1)
                    .interactable,
                Is.False);
            Assert.That(
                controller.LoadoutView.GetSlotDescriptionText(0)
                    .text,
                Does.Contain("끌어"));
            Assert.That(
                controller.LoadoutView.GetSlotDescriptionText(1)
                    .text,
                Does.Contain("Lv.4"));
            Assert.That(
                controller.LoadoutView.GetSlotLabelText(0).text,
                Is.EqualTo("비어 있음"));
            Assert.That(
                controller.LoadoutView.GetSlotLabelText(1).text,
                Is.EqualTo("잠김"));
            Assert.That(
                controller.LoadoutView.ProjectileButton,
                Is.SameAs(controller.LoadoutView.EnemyButton));
            Rect subjectToggleRect =
                controller.LoadoutView
                    .GetSlotSubjectToggleButton(0)
                    .GetComponent<RectTransform>().rect;
            Assert.That(
                subjectToggleRect.width,
                Is.EqualTo(subjectToggleRect.height)
                    .Within(0.01f));
            Assert.That(
                controller.LoadoutView.GetSlotButton(0)
                    .GetComponent<RectTransform>()
                    .anchoredPosition.y,
                Is.GreaterThan(
                    controller.LoadoutView.GetSlotButton(1)
                        .GetComponent<RectTransform>()
                        .anchoredPosition.y));
            Assert.That(
                controller.LoadoutView.GetSlotButton(1)
                    .GetComponent<RectTransform>()
                    .anchoredPosition.y,
                Is.GreaterThan(
                    controller.LoadoutView.GetSlotButton(2)
                        .GetComponent<RectTransform>()
                        .anchoredPosition.y));

            ArcherTowerView archerTower =
                Object.FindObjectOfType<ArcherTowerView>();
            Assert.That(archerTower, Is.Not.Null);
            Assert.That(
                archerTower.IsVisibleBaseAlignmentEnabled,
                Is.True);
            Assert.That(
                archerTower.transform.position.y,
                Is.EqualTo(firstSite.transform.position.y)
                    .Within(0.001f),
                "The logical tower root must remain on the build point.");
            Assert.That(
                archerTower.transform.position.x,
                Is.EqualTo(firstSite.transform.position.x)
                    .Within(0.001f),
                "Horizontal sprite correction must not move the logical tower root.");
            Assert.That(
                archerTower.VisibleBaseOffsetX,
                Is.EqualTo(-1f / 48f)
                    .Within(0.0001f),
                "The 70 px tower frame needs one source-pixel left correction.");
            Assert.That(
                archerTower.VisibleBaseWorldY,
                Is.LessThan(
                    archerTower.transform.position.y - 0.4f),
                "The decorative ground pixels must extend below the build-point center.");
            Assert.That(
                archerTower.VisibleBaseOffsetY,
                Is.EqualTo(-30.5f / 48f)
                    .Within(0.001f),
                "The compensated ground anchor should preserve the approved base position.");
            Assert.That(
                controller.LoadoutView.TowerPreviewSource,
                Is.SameAs(archerTower.transform));
            Assert.That(
                controller.LoadoutView.TowerPreviewContent
                    .childCount,
                Is.GreaterThan(1));
            TowerSelectionView selection =
                controller.SelectedTowerSelectionView;
            Assert.That(selection, Is.Not.Null);
            Assert.That(selection.IsSelected, Is.True);
            Assert.That(
                selection.SelectionMarkerRoot,
                Is.Not.Null);
            Assert.That(
                selection.SelectionMarkerRoot.gameObject.activeSelf,
                Is.True);
            Assert.That(
                selection.SelectionMarkerRoot.childCount,
                Is.EqualTo(4));
            Assert.That(archerTower.UpgradeFrameCount, Is.GreaterThan(0));
            Assert.That(
                archerTower.Mode,
                Is.EqualTo(
                    ArcherTowerView.TowerAnimationMode.Upgrade),
                "The initial tower must visibly play its construction sequence.");
            Assert.That(archerTower.AreArchersVisible, Is.False);

            controller.LoadoutView.CloseButton.onClick.Invoke();
            Assert.That(controller.IsTowerBlueprintOpen, Is.True);
            Assert.That(controller.IsPaused, Is.True);
            Assert.That(controller.Hud.IsVisible, Is.False);
            Assert.That(Time.timeScale, Is.Zero);
            yield return new WaitForSecondsRealtime(0.34f);
            Assert.That(controller.IsTowerBlueprintOpen, Is.False);
            Assert.That(controller.IsPaused, Is.False);
            Assert.That(controller.Hud.IsVisible, Is.True);
            Assert.That(controller.Hud.HudCanvas.enabled, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(1f));

            float constructionTimeout =
                Time.realtimeSinceStartup + 4f;
            while ((archerTower.IsUpgrading ||
                    archerTower.IsArcherLanding) &&
                   Time.realtimeSinceStartup <
                       constructionTimeout)
            {
                yield return null;
            }

            Assert.That(
                archerTower.Mode,
                Is.EqualTo(
                    ArcherTowerView.TowerAnimationMode.Idle));
            Assert.That(archerTower.IsArcherLanding, Is.False);
            Assert.That(archerTower.AreArchersVisible, Is.True);
            DirectionalArcherAnimator[] archers =
                archerTower.GetComponentsInChildren<
                    DirectionalArcherAnimator>(true);
            Assert.That(archers, Is.Not.Empty);
            for (int i = 0; i < archers.Length; i++)
            {
                Assert.That(
                    archers[i].CurrentBehaviour,
                    Is.EqualTo(
                        ArcherUnitAnimationBehaviour.Idle));
            }

            KeyCode[] towerFundingSequence =
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
                 index < towerFundingSequence.Length;
                 index++)
            {
                controller.ProcessKonamiKey(
                    towerFundingSequence[index]);
            }

            Assert.That(controller.SelectTower(tower.Id), Is.True);
            Assert.That(controller.IsTowerBlueprintOpen, Is.True);
            Assert.That(controller.IsPaused, Is.True);
            Assert.That(controller.Hud.IsVisible, Is.False);
            Assert.That(controller.Hud.HudCanvas.enabled, Is.False);

            Assert.That(
                controller.LoadoutView.GetCardView(0)
                    .BodyImage.color,
                Is.EqualTo(
                    StageOneCardView.ProjectileBodyColor));
            Assert.That(
                controller.LoadoutView.GetCardView(0)
                    .TierBadgeText.text,
                Is.EqualTo("T1"));
            Assert.That(
                controller.LoadoutView.GetCardView(2)
                    .TierBadgeText.text,
                Is.EqualTo("T2"));
            Assert.That(
                controller.LoadoutView.RequestCardDrop(0, 0),
                Is.True);
            tower = controller.CurrentSnapshot.Towers[0];
            Assert.That(
                tower.CardInstanceIds[0],
                Is.GreaterThanOrEqualTo(0));
            Assert.That(
                controller.Hud.GetEquippedCardText(0).text,
                Does.Contain("분열"));
            Assert.That(
                controller.LoadoutView.GetSlotDescriptionText(0)
                    .text,
                Does.Contain("분열"));
            Assert.That(
                controller.LoadoutView.RequestSlotDoubleClick(1),
                Is.False,
                "A locked slot must ignore unequip requests.");
            Assert.That(
                controller.LoadoutView.RequestSlotDoubleClick(0),
                Is.True);
            Assert.That(
                controller.CurrentSnapshot.Towers[0]
                    .CardInstanceIds[0],
                Is.EqualTo(-1));
            Assert.That(
                controller.LoadoutView.RequestSlotDoubleClick(0),
                Is.False,
                "An empty slot must ignore unequip requests.");
            Assert.That(
                controller.LoadoutView.RequestCardDrop(0, 0),
                Is.True);
            Assert.That(
                controller.CurrentSnapshot.Towers[0]
                    .CardInstanceIds[0],
                Is.GreaterThanOrEqualTo(0));

            for (int level = 2; level <= 4; level++)
            {
                controller.LoadoutView.UpgradeButton
                    .onClick.Invoke();
            }
            tower = controller.CurrentSnapshot.Towers[0];
            Assert.That(tower.Level, Is.EqualTo(4));
            Assert.That(
                controller.LoadoutView.GetSlotButton(1)
                    .interactable,
                Is.True);
            selection =
                controller.SelectedTowerSelectionView;
            Assert.That(selection.IsSelected, Is.True);
            Assert.That(
                controller.LoadoutView.RequestCardDrop(1, 1),
                Is.True);
            Assert.That(
                controller.Hud.GetEquippedCardText(1).text,
                Does.Contain("화상"));

            controller.LoadoutView.UpgradeButton
                .onClick.Invoke();
            controller.LoadoutView.UpgradeButton
                .onClick.Invoke();
            controller.LoadoutView.UpgradeButton
                .onClick.Invoke();
            tower = controller.CurrentSnapshot.Towers[0];
            Assert.That(tower.Level, Is.EqualTo(7));
            Assert.That(
                controller.LoadoutView.GetSlotButton(2)
                    .interactable,
                Is.True);
            Assert.That(
                controller.LoadoutView.RequestCardDrop(3, 2),
                Is.True);
            Assert.That(
                controller.Hud.GetEquippedCardText(2).text,
                Does.Contain("중독"));

            Color projectileSettingsTint =
                controller.LoadoutView.SettingsTintImage.color;
            controller.LoadoutView.EnemyButton
                .onClick.Invoke();
            Assert.That(
                controller.CurrentSnapshot.Towers[0].SubjectType,
                Is.EqualTo(
                    RuleforgeTD.GameLogic.Core.SubjectType.Enemy));
            Assert.That(
                controller.LoadoutView.GetCardView(0)
                    .BodyImage.color,
                Is.EqualTo(
                    StageOneCardView.ProjectileBodyColor),
                "Changing slot 3 must not recolor slot 1.");
            Assert.That(
                controller.LoadoutView.GetCardView(3)
                    .BodyImage.color,
                Is.EqualTo(
                    StageOneCardView.EnemyBodyColor));
            Assert.That(
                controller.LoadoutView.GetCardView(3)
                    .DescriptionText.text,
                Does.Contain("적"));
            Assert.That(
                controller.LoadoutView.SettingsTintImage.color,
                Is.EqualTo(projectileSettingsTint));
            Assert.That(
                controller.LoadoutView.SettingsTintImage.color,
                Is.EqualTo(Color.clear));
            controller.LoadoutView.ProjectileButton
                .onClick.Invoke();
            Assert.That(
                controller.CurrentSnapshot.Towers[0].SubjectType,
                Is.EqualTo(
                    RuleforgeTD.GameLogic.Core.SubjectType.Projectile));
            Assert.That(
                controller.LoadoutView.GetCardView(3)
                    .BodyImage.color,
                Is.EqualTo(
                    StageOneCardView.ProjectileBodyColor));

            // All slots are full, so an inventory-card double click replaces
            // the bottom-most unlocked slot.
            controller.LoadoutView.GetCardButton(2)
                .onClick.Invoke();
            controller.LoadoutView.GetCardButton(2)
                .onClick.Invoke();
            tower = controller.CurrentSnapshot.Towers[0];
            Assert.That(
                controller.LoadoutView.GetSlotDescriptionText(0)
                    .text,
                Does.Contain("분열"));
            Assert.That(
                controller.LoadoutView.GetSlotDescriptionText(1)
                    .text,
                Does.Contain("화상"));
            Assert.That(
                controller.LoadoutView.GetSlotDescriptionText(2)
                    .text,
                Does.Contain("폭발"),
                "A full loadout must replace the last card.");

            // With slot 2 empty and slot 3 selected, double-clicking poison
            // must still fill the first empty slot instead of the selection.
            Assert.That(
                controller.LoadoutView.RequestSlotDoubleClick(1),
                Is.True);
            controller.LoadoutView.GetSlotButton(2)
                .onClick.Invoke();
            controller.LoadoutView.GetCardButton(3)
                .onClick.Invoke();
            controller.LoadoutView.GetCardButton(3)
                .onClick.Invoke();
            tower = controller.CurrentSnapshot.Towers[0];
            Assert.That(tower.CardInstanceIds[0], Is.GreaterThanOrEqualTo(0));
            Assert.That(tower.CardInstanceIds[1], Is.GreaterThanOrEqualTo(0));
            Assert.That(tower.CardInstanceIds[2], Is.GreaterThanOrEqualTo(0));
            Assert.That(
                controller.LoadoutView.GetSlotDescriptionText(1)
                    .text,
                Does.Contain("중독"),
                "The first gap must win over the currently selected slot.");

            // With slots 1 and 2 empty, the same gesture starts at slot 1.
            Assert.That(
                controller.LoadoutView.RequestSlotDoubleClick(0),
                Is.True);
            Assert.That(
                controller.LoadoutView.RequestSlotDoubleClick(1),
                Is.True);
            controller.LoadoutView.GetSlotButton(2)
                .onClick.Invoke();
            controller.LoadoutView.GetCardButton(1)
                .onClick.Invoke();
            controller.LoadoutView.GetCardButton(1)
                .onClick.Invoke();
            tower = controller.CurrentSnapshot.Towers[0];
            Assert.That(tower.CardInstanceIds[0], Is.GreaterThanOrEqualTo(0));
            Assert.That(tower.CardInstanceIds[1], Is.EqualTo(-1));
            Assert.That(
                controller.LoadoutView.GetSlotDescriptionText(0)
                    .text,
                Does.Contain("화상"),
                "Automatic equip must scan slots from 1 to 3.");

            // Dropping a different card on an occupied row replaces it.
            Assert.That(
                controller.LoadoutView.RequestCardDrop(3, 0),
                Is.True);
            Assert.That(
                controller.LoadoutView.GetSlotDescriptionText(0)
                    .text,
                Does.Contain("중독"));

            // Restore the original build for the later combat assertions.
            Assert.That(
                controller.LoadoutView.RequestCardDrop(0, 0),
                Is.True);
            Assert.That(
                controller.LoadoutView.RequestCardDrop(1, 1),
                Is.True);
            Assert.That(
                controller.LoadoutView.RequestCardDrop(3, 2),
                Is.True);
            Assert.That(
                controller.LoadoutView.GetSlotDescriptionText(0)
                    .text,
                Does.Contain("분열"));
            Assert.That(
                controller.LoadoutView.GetSlotDescriptionText(1)
                    .text,
                Does.Contain("화상"));
            Assert.That(
                controller.LoadoutView.GetSlotDescriptionText(2)
                    .text,
                Does.Contain("중독"));

            controller.LoadoutView.GetCardButton(0)
                .onClick.Invoke();
            controller.LoadoutView.GetCardButton(0)
                .onClick.Invoke();
            Assert.That(
                controller.LoadoutView.GetSlotDescriptionText(0)
                    .text,
                Does.Contain("분열"),
                "Double-clicking an already-equipped inventory card " +
                "must restore its original slot after the first click.");

            Assert.That(
                controller.CameraController,
                Is.Not.Null);
            Assert.That(
                controller.CameraController.IsInitialized,
                Is.True);
            Camera stageCamera = Camera.main;
            Assert.That(stageCamera, Is.Not.Null);
            float halfHeight = stageCamera.orthographicSize;
            float halfWidth = halfHeight * stageCamera.aspect;
            Bounds bounds = controller.CameraController.MapBounds;
            Assert.That(
                stageCamera.transform.position.x - halfWidth,
                Is.GreaterThanOrEqualTo(bounds.min.x - 0.05f));
            Assert.That(
                stageCamera.transform.position.x + halfWidth,
                Is.LessThanOrEqualTo(bounds.max.x + 0.05f));
            Assert.That(
                stageCamera.transform.position.y - halfHeight,
                Is.GreaterThanOrEqualTo(bounds.min.y - 0.05f));
            Assert.That(
                stageCamera.transform.position.y + halfHeight,
                Is.LessThanOrEqualTo(bounds.max.y + 0.05f));

            CanvasScaler hudScaler =
                controller.Hud.GetComponentInChildren<
                    CanvasScaler>();
            Assert.That(
                hudScaler.referenceResolution,
                Is.EqualTo(new Vector2(1600f, 900f)));
            Transform topHud =
                controller.Hud.transform.Find(
                    "Stage One HUD Canvas/Safe Area/Top HUD");
            Assert.That(topHud, Is.Not.Null);
            Assert.That(
                topHud.parent.GetComponent<
                    StageOneSafeAreaFitter>(),
                Is.Not.Null);
            Assert.That(
                topHud.GetComponent<Image>().color.a,
                Is.Zero.Within(0.001f));

            controller.LoadoutView.CloseButton.onClick.Invoke();
            Assert.That(controller.IsTowerBlueprintOpen, Is.True);
            Assert.That(controller.Hud.IsVisible, Is.False);
            Assert.That(Time.timeScale, Is.Zero);
            yield return new WaitForSecondsRealtime(0.34f);
            Assert.That(controller.IsTowerBlueprintOpen, Is.False);
            Assert.That(controller.IsPaused, Is.False);
            Assert.That(controller.Hud.IsVisible, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(1f));

            controller.Hud.PlayButton.onClick.Invoke();
            Assert.That(
                controller.CurrentPhase,
                Is.EqualTo(RunPhase.Combat));
            Assert.That(controller.IsPaused, Is.False);
            Assert.That(
                controller.WavePreviewView.IsVisible,
                Is.True,
                "Combat keeps the following wave summary visible.");
            Assert.That(
                controller.WavePreviewView.TotalEnemyText.text,
                Does.Contain("55"));
            controller.SetSpeed(2f);
            Assert.That(Time.timeScale, Is.EqualTo(2f));

            TowerBuildSiteView combatBuildSite =
                controller.StageMap.GetBuildSite(1);
            Assert.That(combatBuildSite.RequestBuild(), Is.True);
            yield return null;
            Assert.That(
                controller.TowerBuildPickerView.IsVisible,
                Is.True,
                "Tower selection must remain available during combat.");
            Assert.That(
                controller.CurrentSnapshot.Towers.Length,
                Is.EqualTo(1));
            controller.TowerBuildPickerView.CloseButton
                .onClick.Invoke();

            Assert.That(controller.SelectTower(tower.Id), Is.True);
            Assert.That(controller.IsTowerBlueprintOpen, Is.True);
            Assert.That(controller.IsPaused, Is.True);
            Assert.That(controller.Hud.IsVisible, Is.False);
            Assert.That(controller.Hud.HudCanvas.enabled, Is.False);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(
                controller.LoadoutView.UpgradeButton
                    .interactable,
                Is.False);
            Assert.That(
                controller.LoadoutView.GetCardButton(0)
                    .interactable,
                Is.False);
            Assert.That(
                controller.StageMap.GetBuildSite(1).CanBuild,
                Is.True);

            ArcherTowerView animatedPreviewTower =
                controller.LoadoutView.TowerPreviewSource
                    .GetComponent<ArcherTowerView>();
            Assert.That(animatedPreviewTower, Is.Not.Null);
            Assert.That(
                animatedPreviewTower
                    .IsBlueprintPreviewAnimationEnabled,
                Is.True);
            Sprite previewSpriteBefore =
                controller.LoadoutView.GetTowerPreviewSprite(
                    animatedPreviewTower.TowerRenderer);
            Assert.That(previewSpriteBefore, Is.Not.Null);
            DirectionalArcherAnimator[] previewArchers =
                animatedPreviewTower.GetComponentsInChildren<
                    DirectionalArcherAnimator>(true);
            Assert.That(previewArchers, Is.Not.Empty);
            for (int i = 0; i < previewArchers.Length; i++)
            {
                Assert.That(
                    previewArchers[i]
                        .IsBlueprintPreviewAnimationEnabled,
                    Is.True);
                Assert.That(
                    previewArchers[i].CurrentBehaviour,
                    Is.EqualTo(
                        ArcherUnitAnimationBehaviour.Idle));
            }

            long tickWhileBlueprintOpen =
                controller.CurrentSnapshot.Tick;
            yield return new WaitForSecondsRealtime(0.22f);
            Assert.That(
                controller.CurrentSnapshot.Tick,
                Is.EqualTo(tickWhileBlueprintOpen));
            Sprite previewSpriteAfter =
                controller.LoadoutView.GetTowerPreviewSprite(
                    animatedPreviewTower.TowerRenderer);
            Assert.That(previewSpriteAfter, Is.Not.Null);
            Assert.That(
                previewSpriteAfter,
                Is.Not.SameAs(previewSpriteBefore),
                "The left tower preview must animate on unscaled time " +
                "while the combat simulation remains paused.");
            Assert.That(
                previewSpriteAfter,
                Is.SameAs(
                    animatedPreviewTower.TowerRenderer.sprite));

            selection =
                controller.SelectedTowerSelectionView;
            Assert.That(selection, Is.Not.Null);
            controller.LoadoutView.CloseButton.onClick.Invoke();
            Assert.That(controller.IsTowerBlueprintOpen, Is.True);
            Assert.That(controller.IsPaused, Is.True);
            Assert.That(controller.Hud.IsVisible, Is.False);
            Assert.That(Time.timeScale, Is.Zero);
            yield return new WaitForSecondsRealtime(0.34f);
            Assert.That(controller.IsTowerBlueprintOpen, Is.False);
            Assert.That(controller.SelectedTowerId, Is.EqualTo(-1));
            Assert.That(controller.IsPaused, Is.False);
            Assert.That(controller.Hud.IsVisible, Is.True);
            Assert.That(controller.Hud.HudCanvas.enabled, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(2f));
            Assert.That(selection.IsSelected, Is.False);
            Assert.That(
                animatedPreviewTower
                    .IsBlueprintPreviewAnimationEnabled,
                Is.False);
            for (int i = 0; i < previewArchers.Length; i++)
            {
                Assert.That(
                    previewArchers[i]
                        .IsBlueprintPreviewAnimationEnabled,
                    Is.False);
            }

            yield return new WaitForSecondsRealtime(0.12f);
            long tickBeforePause =
                controller.CurrentSnapshot.Tick;
            Assert.That(
                tickBeforePause,
                Is.GreaterThan(tickWhileBlueprintOpen));

            controller.Hud.PlayButton.onClick.Invoke();
            Assert.That(controller.IsPaused, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            yield return new WaitForSecondsRealtime(0.12f);
            Assert.That(
                controller.CurrentSnapshot.Tick,
                Is.EqualTo(tickBeforePause));

            controller.Hud.GetSpeedButton(2f)
                .onClick.Invoke();
            Assert.That(controller.SpeedMultiplier, Is.EqualTo(2f));
            Assert.That(Time.timeScale, Is.Zero);
            controller.Hud.GetSpeedButton(0.5f)
                .onClick.Invoke();
            Assert.That(
                controller.SpeedMultiplier,
                Is.EqualTo(0.5f));
            Assert.That(Time.timeScale, Is.Zero);
            controller.Hud.GetSpeedButton(3f)
                .onClick.Invoke();
            Assert.That(controller.SpeedMultiplier, Is.EqualTo(3f));
            Assert.That(Time.timeScale, Is.Zero);
            controller.Hud.PlayButton.onClick.Invoke();
            Assert.That(controller.IsPaused, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(3f));

            float impactVfxTimeout =
                Time.realtimeSinceStartup + 3f;
            while (string.IsNullOrEmpty(
                       controller.CardEffectVfx
                           .LastPlayedEffectId) &&
                   Time.realtimeSinceStartup < impactVfxTimeout)
            {
                yield return null;
            }
            Assert.That(
                controller.CardEffectVfx.SemanticEventPlayCount,
                Is.Zero,
                "Card VFX must not play at the tower when a card executes; " +
                "projectile cards play on hit and enemy cards on death.");
            Assert.That(
                controller.CardEffectVfx.LastPlayedEffectId,
                Is.Not.Empty,
                "An impact or death must play the accumulated card VFX.");
            Assert.That(
                StageOneCardEffectPalette.TryGetStyle(
                    controller.CardEffectVfx.LastPlayedEffectId,
                    out _),
                Is.True);
            Assert.That(
                controller.CurrentSnapshot.Tick,
                Is.GreaterThan(tickBeforePause));

            Assert.That(controller.SelectTower(tower.Id), Is.True);
            selection =
                controller.SelectedTowerSelectionView;
            Assert.That(selection, Is.Not.Null);
            Assert.That(controller.Hud.IsVisible, Is.False);
            Assert.That(Time.timeScale, Is.Zero);
            controller.LoadoutView.CloseButton.onClick.Invoke();
            Assert.That(controller.IsTowerBlueprintOpen, Is.True);
            Assert.That(controller.IsPaused, Is.True);
            Assert.That(controller.Hud.IsVisible, Is.False);
            Assert.That(Time.timeScale, Is.Zero);
            yield return new WaitForSecondsRealtime(0.34f);
            Assert.That(controller.SelectedTowerId, Is.EqualTo(-1));
            Assert.That(controller.Hud.IsVisible, Is.True);
            Assert.That(controller.IsPaused, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(3f));
            Assert.That(selection.IsSelected, Is.False);
            Assert.That(
                selection.SelectionMarkerRoot.gameObject.activeSelf,
                Is.False);
        }

        private static void AssertUiFontCoverage(
            StageOnePresentationCatalog catalog)
        {
            Assert.That(catalog.LocalizationJson, Is.Not.Null);
            string missing =
                StageOneUiFontCoverage.FindMissingCharacters(
                    catalog.UiFont,
                    catalog.LocalizationJson.text);

            Assert.That(
                missing.Length,
                Is.Zero,
                "The Stage 01 UI font is missing required glyphs: " +
                missing);
        }

        [UnityTest]
        public IEnumerator
            KonamiCommand_GrantsOneThousandGold()
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
            int initialGold =
                controller.CurrentSnapshot.Gold;
            KeyCode[] sequence =
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

            for (int i = 0;
                 i < sequence.Length;
                 i++)
            {
                Assert.That(
                    controller.ProcessKonamiKey(
                        sequence[i]),
                    Is.EqualTo(
                        i == sequence.Length - 1));
            }

            Assert.That(
                controller.CurrentSnapshot.Gold,
                Is.EqualTo(initialGold + 1000));
            Assert.That(
                controller.KonamiSequenceProgress,
                Is.Zero);
            Assert.That(
                controller.Hud.StatusText.text,
                Does.Contain("1000"));
        }

        [UnityTest]
        public IEnumerator
            BuildPicker_OnlyOffersArcherTowerAcrossRun()
        {
            SceneManager.LoadScene(
                "Stage01",
                LoadSceneMode.Single);
            yield return null;
            yield return null;

            StageOneBattleController controller =
                Object.FindObjectOfType<
                    StageOneBattleController>();
            TowerBuildSiteView firstSite =
                controller.StageMap.GetBuildSite(0);
            Assert.That(firstSite.RequestBuild(), Is.True);
            yield return null;

            StageOneTowerBuildPickerView picker =
                controller.TowerBuildPickerView;
            Assert.That(picker.OptionCount, Is.EqualTo(1));
            Assert.That(
                picker.GetOptionId(0),
                Is.EqualTo("ballista"));
            Assert.That(
                picker.GetOptionCost(0),
                Is.Zero);
            picker.GetOptionButton(0)
                .onClick.Invoke();
            yield return null;

            Assert.That(
                controller.CurrentSnapshot.Towers.Length,
                Is.EqualTo(1));
            Assert.That(
                controller.CurrentSnapshot.Towers[0]
                    .DefinitionId,
                Is.EqualTo("ballista"));
            Assert.That(
                controller.IsTowerBlueprintOpen,
                Is.False);
            Assert.That(
                controller.TowerActionView.IsVisible,
                Is.True);

            ArcherTowerView archerView =
                Object.FindObjectOfType<ArcherTowerView>();
            Assert.That(
                archerView,
                Is.Not.Null);

            TowerBuildSiteView secondSite =
                controller.StageMap.GetBuildSite(1);
            Assert.That(secondSite.RequestBuild(), Is.True);
            yield return null;

            Assert.That(picker.OptionCount, Is.EqualTo(1));
            Assert.That(
                picker.GetOptionId(0),
                Is.EqualTo("ballista"));
            Assert.That(
                picker.GetOptionCost(0),
                Is.EqualTo(100));
            Assert.That(
                picker.GetOptionButton(0).interactable,
                Is.False);
            Assert.That(
                controller.CurrentSnapshot.Towers.Length,
                Is.EqualTo(1),
                "Opening a later picker must not construct a tower.");
        }

        [Test]
        public void
            ProjectileView_StartsAtLaunchOriginAndClearsItWhenPooled()
        {
            var host = new GameObject(
                "Projectile View Test",
                typeof(SpriteRenderer),
                typeof(StageOneProjectileView));
            try
            {
                StageOneProjectileView view =
                    host.GetComponent<StageOneProjectileView>();
                view.Configure(null);
                var snapshot = new ProjectileSnapshot(
                    17,
                    3,
                    SimPosition.FromMilliUnits(1000, 2000),
                    1000,
                    20,
                    150,
                    0,
                    0,
                    10000,
                    0,
                    false,
                    false,
                    0);
                var launchOrigin =
                    new Vector3(1.35f, 2.65f, 3f);

                view.ApplySnapshot(snapshot, launchOrigin);

                Assert.That(view.HasLaunchOrigin, Is.True);
                Assert.That(
                    view.LastLaunchOrigin,
                    Is.EqualTo(
                        new Vector3(1.35f, 2.65f, -0.08f)));
                Assert.That(
                    view.transform.position,
                    Is.EqualTo(view.LastLaunchOrigin));
                Assert.That(
                    view.transform.localScale,
                    Is.EqualTo(Vector3.one * 1.65f));

                var nextSnapshot = new ProjectileSnapshot(
                    17,
                    3,
                    SimPosition.FromMilliUnits(2000, 2000),
                    1000,
                    19,
                    150,
                    0,
                    0,
                    10000,
                    0,
                    false,
                    false,
                    0);
                view.ApplySnapshot(nextSnapshot);
                Assert.That(
                    view.transform.position,
                    Is.EqualTo(
                        new Vector3(2.35f, 2.65f, -0.08f)));

                var thirdSnapshot = new ProjectileSnapshot(
                    17,
                    3,
                    SimPosition.FromMilliUnits(3000, 2000),
                    1000,
                    18,
                    150,
                    0,
                    0,
                    10000,
                    0,
                    false,
                    false,
                    0);
                view.ApplySnapshot(thirdSnapshot);
                Assert.That(
                    view.transform.position,
                    Is.EqualTo(
                        new Vector3(3.35f, 2.65f, -0.08f)),
                    "The launch offset must stay constant so arrows " +
                    "do not curve back toward the simulation line.");

                view.ReturnToPool();

                Assert.That(view.ProjectileId, Is.EqualTo(-1));
                Assert.That(view.HasLaunchOrigin, Is.False);
                Assert.That(view.LastLaunchOrigin, Is.EqualTo(Vector3.zero));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
