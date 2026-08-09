#if UNITY_EDITOR
using NUnit.Framework;
using RuleforgeTD.Editor.AssetImport;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.Simulation;
using RuleforgeTD.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleforgeTD.Tests.EditMode
{
    public sealed class MainMenuSceneTests
    {
        [Test]
        public void MainMenu_LeadsBuildAndReferencesThreeDistinctStages()
        {
            MainMenuSceneBuilder.ValidateMainMenuFromCommandLine();
            EditorBuildSettingsScene[] settings =
                EditorBuildSettings.scenes;
            Assert.That(
                settings[0].path,
                Is.EqualTo(MainMenuSceneBuilder.MainMenuScenePath));
            Assert.That(
                settings[1].path,
                Is.EqualTo(
                    CraftPixFieldTilemapAssetBuilder.StageOneScenePath));
            Assert.That(
                settings[2].path,
                Is.EqualTo(StageTwoFieldMapBuilder.StageTwoScenePath));
            Assert.That(
                settings[3].path,
                Is.EqualTo(StageThreeFieldMapBuilder.StageThreeScenePath));

            Scene scene = EditorSceneManager.OpenScene(
                MainMenuSceneBuilder.MainMenuScenePath,
                OpenSceneMode.Additive);
            try
            {
                StageSelectionMenu menu = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    menu = root.GetComponentInChildren<
                        StageSelectionMenu>(true);
                    if (menu != null)
                    {
                        break;
                    }
                }

                Assert.That(menu, Is.Not.Null);
                Assert.That(menu.TextData, Is.Not.Null);
                Assert.That(menu.BattleTextData, Is.Not.Null);
                Assert.That(menu.StageOneContent, Is.Not.Null);
                Assert.That(menu.StageTwoContent, Is.Not.Null);
                Assert.That(menu.StageThreeContent, Is.Not.Null);
                Assert.That(menu.UiFont, Is.Not.Null);
                Assert.That(menu.WorldMapBackground, Is.Not.Null);
                Assert.That(menu.StageOneSceneName, Is.EqualTo("Stage01"));
                Assert.That(menu.StageTwoSceneName, Is.EqualTo("Stage02"));
                Assert.That(menu.StageThreeSceneName, Is.EqualTo("Stage03"));
                Assert.That(menu.DisplayedStageCount, Is.EqualTo(15));

                Assert.That(
                    ReadStarterIds(menu.StageOneContent),
                    Is.EqualTo(new[]
                    {
                        "split",
                        "burn",
                        "explode",
                        "poison"
                    }));
                Assert.That(
                    ReadStarterIds(menu.StageTwoContent),
                    Is.EqualTo(new[]
                    {
                        "pierce",
                        "mark",
                        "poison",
                        "corrosion"
                    }));
                Assert.That(
                    ReadStarterIds(menu.StageThreeContent),
                    Is.EqualTo(new[]
                    {
                        "ricochet",
                        "bleed",
                        "knockback",
                        "shock"
                    }));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static string[] ReadStarterIds(TextAsset contentAsset)
        {
            CompiledContent content =
                LogicContentJsonLoader.Load(contentAsset);
            var result = new string[content.Run.StartingCards.Length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = content.GetCard(
                    content.Run.StartingCards[i]).StableId;
            }

            return result;
        }
    }
}
#endif
