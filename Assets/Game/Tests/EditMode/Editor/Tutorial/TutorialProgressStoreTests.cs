#if UNITY_EDITOR
using NUnit.Framework;
using RuleforgeTD.Tutorial;

namespace RuleforgeTD.Tests.EditMode.Tutorial
{
    public sealed class TutorialProgressStoreTests
    {
        private TutorialProgressStore current;
        private TutorialProgressStore future;

        [SetUp]
        public void SetUp()
        {
            current = TutorialProgressStore.CreateCurrent();
            future = new TutorialProgressStore(
                TutorialIds.CoreTutorialId,
                TutorialIds.CurrentContentVersion + 1);
            current.ResetForTests();
            future.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            current.ResetForTests();
            future.ResetForTests();
        }

        [Test]
        public void FreshVersion_AutoStartsUntilCompletedOrSkipped()
        {
            Assert.That(current.ShouldAutoStart, Is.True);
            Assert.That(current.ShouldStartTutorial, Is.True);
            Assert.That(current.IsResolved, Is.False);

            current.MarkCompleted();

            Assert.That(current.IsCompleted, Is.True);
            Assert.That(current.IsSkipped, Is.False);
            Assert.That(current.IsResolved, Is.True);
            Assert.That(current.ShouldAutoStart, Is.False);
            Assert.That(current.ShouldStartTutorial, Is.False);

            current.MarkSkipped();

            Assert.That(current.IsCompleted, Is.False);
            Assert.That(current.IsSkipped, Is.True);
            Assert.That(current.ShouldAutoStart, Is.False);
        }

        [Test]
        public void ManualReplay_OverridesResolvedStateUntilConsumed()
        {
            current.MarkCompleted();
            current.RequestManualReplay();

            Assert.That(current.ShouldAutoStart, Is.False);
            Assert.That(current.IsManualReplayRequested, Is.True);
            Assert.That(current.ShouldStartTutorial, Is.True);
            Assert.That(current.ConsumeManualReplayRequest(), Is.True);
            Assert.That(current.ConsumeManualReplayRequest(), Is.False);
            Assert.That(current.IsCompleted, Is.True);
            Assert.That(current.ShouldStartTutorial, Is.False);
        }

        [Test]
        public void ContextualTipIds_ArePersistedOnlyOnce()
        {
            string first = TutorialIds.ContextualTips.SecondSlot;
            string second = TutorialIds.ContextualTips.BossEnemy;

            Assert.That(current.HasSeenContextualTip(first), Is.False);
            Assert.That(current.MarkContextualTipSeen(first), Is.True);
            Assert.That(current.MarkContextualTipSeen(first), Is.False);
            Assert.That(current.HasSeenContextualTip(first), Is.True);
            Assert.That(current.HasSeenContextualTip(second), Is.False);
            Assert.That(current.MarkContextualTipSeen(second), Is.True);
            Assert.That(current.HasSeenContextualTip(second), Is.True);
        }

        [Test]
        public void ContentVersions_HaveIndependentProgress()
        {
            current.MarkCompleted();
            current.MarkContextualTipSeen(
                TutorialIds.ContextualTips.StageTwo);

            Assert.That(current.IsCompleted, Is.True);
            Assert.That(future.IsCompleted, Is.False);
            Assert.That(future.ShouldAutoStart, Is.True);
            Assert.That(
                future.HasSeenContextualTip(
                    TutorialIds.ContextualTips.StageTwo),
                Is.False);
        }

        [Test]
        public void ResetForTests_ClearsOnlyRequestedVersion()
        {
            current.MarkSkipped();
            current.RequestManualReplay();
            current.MarkContextualTipSeen(
                TutorialIds.ContextualTips.StatusEffect);
            future.MarkCompleted();

            current.ResetForTests();

            Assert.That(current.IsResolved, Is.False);
            Assert.That(current.IsManualReplayRequested, Is.False);
            Assert.That(
                current.HasSeenContextualTip(
                    TutorialIds.ContextualTips.StatusEffect),
                Is.False);
            Assert.That(future.IsCompleted, Is.True);
        }
    }
}
#endif
