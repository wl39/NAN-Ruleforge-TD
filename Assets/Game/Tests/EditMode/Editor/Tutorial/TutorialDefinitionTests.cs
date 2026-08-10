#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NUnit.Framework;
using RuleforgeTD.Tutorial;

namespace RuleforgeTD.Tests.EditMode.Tutorial
{
    public sealed class TutorialDefinitionTests
    {
        [Test]
        public void KoreanResource_DefinesValidatedCoreFlowAndTips()
        {
            TutorialDefinition definition =
                TutorialDefinitionLoader.LoadKorean();

            Assert.That(
                definition.TutorialId,
                Is.EqualTo(TutorialIds.CoreTutorialId));
            Assert.That(
                definition.SchemaVersion,
                Is.EqualTo(TutorialIds.SchemaVersion));
            Assert.That(
                definition.ContentVersion,
                Is.EqualTo(TutorialIds.CurrentContentVersion));
            Assert.That(
                definition.Locale,
                Is.EqualTo(TutorialIds.KoreanLocale));
            Assert.That(
                definition.Steps.Count,
                Is.EqualTo(TutorialIds.CoreStepIds.Count));
            Assert.That(
                definition.ContextualTips.Count,
                Is.EqualTo(TutorialIds.ContextualTipIds.Count));

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var orders = new HashSet<int>();
            int previousOrder = 0;
            int highestChapter = 0;
            for (int index = 0; index < definition.Steps.Count; index++)
            {
                TutorialStepDefinition step = definition.Steps[index];
                Assert.That(
                    step.Id,
                    Is.EqualTo(TutorialIds.CoreStepIds[index]));
                Assert.That(ids.Add(step.Id), Is.True);
                Assert.That(orders.Add(step.Order), Is.True);
                Assert.That(step.Order, Is.GreaterThan(previousOrder));
                Assert.That(step.Title, Is.Not.Empty);
                Assert.That(step.Body, Is.Not.Empty);
                Assert.That(ContainsHangul(step.Title + step.Body), Is.True);
                Assert.That(step.Anchors, Is.Not.Empty);
                Assert.That(
                    step.Allows(TutorialAction.SkipTutorial),
                    Is.True);
                previousOrder = step.Order;
                highestChapter = Math.Max(highestChapter, step.Chapter);
            }

            Assert.That(
                highestChapter,
                Is.EqualTo(TutorialIds.CoreChapterCount));

            previousOrder = 0;
            for (int index = 0;
                 index < definition.ContextualTips.Count;
                 index++)
            {
                TutorialContextualTipDefinition tip =
                    definition.ContextualTips[index];
                Assert.That(
                    tip.Id,
                    Is.EqualTo(TutorialIds.ContextualTipIds[index]));
                Assert.That(ids.Add(tip.Id), Is.True);
                Assert.That(orders.Add(tip.Order), Is.True);
                Assert.That(tip.Order, Is.GreaterThan(previousOrder));
                Assert.That(tip.Title, Is.Not.Empty);
                Assert.That(tip.Body, Is.Not.Empty);
                Assert.That(ContainsHangul(tip.Title + tip.Body), Is.True);
                Assert.That(tip.Anchors, Is.Not.Empty);
                Assert.That(
                    tip.Trigger,
                    Is.Not.EqualTo(TutorialContextTrigger.None));
                previousOrder = tip.Order;
            }
        }

        [Test]
        public void CardPractice_RestrictsInputToDeclaredActions()
        {
            TutorialDefinition definition =
                TutorialDefinitionLoader.LoadKorean();
            TutorialStepDefinition cardDrag =
                definition.FindStep(TutorialIds.Steps.CardDrag);

            Assert.That(cardDrag.RestrictInput, Is.True);
            Assert.That(
                cardDrag.Completion,
                Is.EqualTo(TutorialCompletion.CardDraggedToSlot));
            Assert.That(
                cardDrag.Allows(TutorialAction.DragCardToSlot),
                Is.True);
            Assert.That(
                cardDrag.Allows(TutorialAction.SkipTutorial),
                Is.True);
            Assert.That(
                cardDrag.Allows(TutorialAction.AutoEquipCard),
                Is.False);
            Assert.That(
                cardDrag.Allows(TutorialAction.StartWave),
                Is.False);
        }

        [Test]
        public void Loader_RejectsDuplicateStableIds()
        {
            string json = CreateTestDocument(
                CreateStep("test.first", 10, "제목", "본문") + "," +
                CreateStep("test.first", 20, "다른 제목", "다른 본문"));

            InvalidOperationException exception = Assert.Throws<
                InvalidOperationException>(
                () => TutorialDefinitionLoader.FromJson(
                    json,
                    "duplicate-id-test"));
            StringAssert.Contains("duplicate tutorial id", exception.Message);
        }

        [Test]
        public void Loader_RejectsDuplicateOrder()
        {
            string json = CreateTestDocument(
                CreateStep("test.first", 10, "제목", "본문") + "," +
                CreateStep("test.second", 10, "다른 제목", "다른 본문"));

            InvalidOperationException exception = Assert.Throws<
                InvalidOperationException>(
                () => TutorialDefinitionLoader.FromJson(
                    json,
                    "duplicate-order-test"));
            StringAssert.Contains("duplicate order", exception.Message);
        }

        [Test]
        public void Loader_RejectsEmptyLocalizedCopy()
        {
            string json = CreateTestDocument(
                CreateStep("test.first", 10, "", "본문"));

            InvalidOperationException exception = Assert.Throws<
                InvalidOperationException>(
                () => TutorialDefinitionLoader.FromJson(
                    json,
                    "empty-copy-test"));
            StringAssert.Contains("non-empty title and body", exception.Message);
        }

        [Test]
        public void CoreResource_ContainsControllerFacingIdentifiers()
        {
            TutorialDefinition definition =
                TutorialDefinitionLoader.LoadKorean();

            Assert.That(
                definition.FindStep(TutorialIds.Steps.Objective),
                Is.Not.Null);
            Assert.That(
                definition.FindStep(TutorialIds.Steps.WavePreview),
                Is.Not.Null);
            Assert.That(
                definition.FindStep(TutorialIds.Steps.TowerBuild),
                Is.Not.Null);
            Assert.That(
                definition.FindStep(TutorialIds.Steps.CardTarget),
                Is.Not.Null);
            Assert.That(
                definition.FindStep(TutorialIds.Steps.EnemyInspection),
                Is.Not.Null);
            Assert.That(
                definition.FindStep(TutorialIds.Steps.Complete),
                Is.Not.Null);
            Assert.That(
                definition.FindContextualTip(
                    TutorialIds.ContextualTips.BossEnemy),
                Is.Not.Null);
            Assert.That(
                definition.FindContextualTip(
                    TutorialIds.ContextualTips.StageThree),
                Is.Not.Null);
        }

        private static bool ContainsHangul(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] >= '\uac00' && value[index] <= '\ud7a3')
                {
                    return true;
                }
            }

            return false;
        }

        private static string CreateTestDocument(string steps)
        {
            return "{" +
                "\"schemaVersion\":1," +
                "\"tutorialId\":\"test.tutorial\"," +
                "\"contentVersion\":1," +
                "\"locale\":\"ko-KR\"," +
                "\"steps\":[" + steps + "]," +
                "\"contextualTips\":[]" +
                "}";
        }

        private static string CreateStep(
            string id,
            int order,
            string title,
            string body)
        {
            return "{" +
                "\"id\":\"" + id + "\"," +
                "\"chapter\":1," +
                "\"order\":" + order + "," +
                "\"title\":\"" + title + "\"," +
                "\"body\":\"" + body + "\"," +
                "\"anchors\":[\"BattleHud\"]," +
                "\"completion\":\"Acknowledged\"," +
                "\"completionTargetId\":\"\"," +
                "\"allowedActions\":[] ," +
                "\"pauseBattle\":false," +
                "\"restrictInput\":false" +
                "}";
        }
    }
}
#endif
