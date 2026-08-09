#if UNITY_EDITOR
using NUnit.Framework;
using RuleforgeTD.UI;

namespace RuleforgeTD.Tests.EditMode
{
    public sealed class CampaignStageProgressTests
    {
        [SetUp]
        public void SetUp()
        {
            CampaignStageProgress.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            CampaignStageProgress.ResetForTests();
        }

        [Test]
        public void DemoCampaign_UnlocksStagesSequentially()
        {
            Assert.That(CampaignStageProgress.IsUnlocked(1), Is.True);
            Assert.That(CampaignStageProgress.IsUnlocked(2), Is.False);
            Assert.That(CampaignStageProgress.IsUnlocked(3), Is.False);

            CampaignStageProgress.MarkStageCompleted(1);
            Assert.That(CampaignStageProgress.IsCleared(1), Is.True);
            Assert.That(CampaignStageProgress.IsUnlocked(2), Is.True);
            Assert.That(CampaignStageProgress.IsUnlocked(3), Is.False);

            CampaignStageProgress.MarkStageCompleted(2);
            Assert.That(CampaignStageProgress.IsCleared(2), Is.True);
            Assert.That(CampaignStageProgress.IsUnlocked(3), Is.True);
        }

        [Test]
        public void DemoCampaign_NeverUnlocksUnbuiltStageFour()
        {
            CampaignStageProgress.MarkStageCompleted(1);
            CampaignStageProgress.MarkStageCompleted(2);
            CampaignStageProgress.MarkStageCompleted(3);

            Assert.That(
                CampaignStageProgress.HighestUnlockedStage,
                Is.EqualTo(3));
            Assert.That(CampaignStageProgress.IsUnlocked(4), Is.False);
            Assert.That(
                CampaignStageProgress.DisplayedStageCount,
                Is.EqualTo(15));
        }

        [TestCase("Stage01", 1)]
        [TestCase("Stage02", 2)]
        [TestCase("Stage03", 3)]
        public void SceneName_MapsToDemoStage(
            string sceneName,
            int expectedStage)
        {
            Assert.That(
                CampaignStageProgress.TryGetStageNumber(
                    sceneName,
                    out int actualStage),
                Is.True);
            Assert.That(actualStage, Is.EqualTo(expectedStage));
        }
    }
}
#endif
