using System.Collections;
using NUnit.Framework;
using RuleforgeTD.Battle;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Simulation;
using RuleforgeTD.Simulation;
using RuleforgeTD.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RuleforgeTD.Tests.PlayMode.UI
{
    public sealed class WavePreviewAnimationIntegrationTests
    {
        [UnityTest]
        public IEnumerator StagePreviewUsesLiveWalkFrames()
        {
            SceneManager.LoadScene("Stage01", LoadSceneMode.Single);
            yield return null;
            yield return null;

            StageOneBattleController battle =
                Object.FindObjectOfType<StageOneBattleController>();
            Assert.That(battle, Is.Not.Null);
            WavePreviewAnimatedImage[] previews =
                battle.WavePreviewView.GetComponentsInChildren<
                    WavePreviewAnimatedImage>(true);
            Assert.That(previews.Length, Is.GreaterThan(0));

            Text previewText = battle.WavePreviewView
                .GetComponentInChildren<Text>(true);
            Assert.That(previewText, Is.Not.Null);
            Assert.That(previewText.font, Is.Not.Null);
            const string monsterNames = "고블린늑대슬라임";
            for (int i = 0; i < monsterNames.Length; i++)
            {
                Assert.That(
                    previewText.font.HasCharacter(monsterNames[i]),
                    Is.True,
                    "Wave-preview font is missing '{0}'.",
                    monsterNames[i]);
            }

            battle.WavePreviewView.OpenGroup(0);
            yield return null;
            WavePreviewAnimatedImage active = null;
            for (int i = 0; i < previews.Length; i++)
            {
                if (previews[i].gameObject.activeInHierarchy &&
                    previews[i].IsAnimating)
                {
                    active = previews[i];
                    break;
                }
            }

            Assert.That(active, Is.Not.Null);
            Image image = active.GetComponent<Image>();
            Sprite first = image.sprite;
            bool changed = false;
            for (int i = 0; i < 8 && !changed; i++)
            {
                yield return new WaitForSecondsRealtime(0.06f);
                changed = image.sprite != null && image.sprite != first;
            }

            Assert.That(changed, Is.True);

            StageOnePresentationCatalog presentation =
                battle.PresentationCatalog;
            TextAsset[] modules = presentation.CardContentModules;
            CompiledContent content = LogicContentJsonLoader.Load(
                presentation.ContentJson,
                modules);
            StageOneUiTextCatalog localization =
                StageOneUiTextCatalog.Load(
                    presentation.LocalizationJson,
                    modules);
            var simulation = new GameSimulation();
            simulation.Initialize(content, 0xE11EUL);
            WavePreviewModel eliteForecast =
                WavePreviewModelFactory.Create(
                    simulation.GetWaveForecast(1),
                    content,
                    System.Array.Empty<CardInstanceSnapshot>(),
                    localization,
                    presentation,
                    false);

            WavePreviewGroupModel normalGoblin = default;
            WavePreviewGroupModel ironcladGoblin = default;
            bool foundNormal = false;
            bool foundIronclad = false;
            for (int i = 0; i < eliteForecast.Groups.Length; i++)
            {
                WavePreviewGroupModel group = eliteForecast.Groups[i];
                if (group.DisplayName == "고블린")
                {
                    normalGoblin = group;
                    foundNormal = true;
                }
                else if (group.DisplayName.Contains("철갑"))
                {
                    ironcladGoblin = group;
                    foundIronclad = true;
                }
            }

            Assert.That(foundNormal, Is.True);
            Assert.That(foundIronclad, Is.True);
            Assert.That(
                ironcladGoblin.PreviewAnimatorController,
                Is.Not.Null);
            Assert.That(
                ironcladGoblin.PreviewAnimatorController.name,
                Is.EqualTo("Goblin"));
            Assert.That(
                ironcladGoblin.PreviewTint,
                Is.EqualTo((Color)new Color32(113, 135, 166, 255)));
            Assert.That(ironcladGoblin.HasPreviewOutline, Is.True);
            Assert.That(
                ironcladGoblin.PreviewVisualScale,
                Is.GreaterThan(normalGoblin.PreviewVisualScale));
        }
    }
}
