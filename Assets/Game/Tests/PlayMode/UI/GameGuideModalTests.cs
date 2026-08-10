using System;
using NUnit.Framework;
using RuleforgeTD.Tutorial;
using RuleforgeTD.UI;
using UnityEngine;

namespace RuleforgeTD.Tests.PlayMode.UI
{
    public sealed class GameGuideModalTests
    {
        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            GameGuideRuntime.ConsumeTutorialReplayRequest();
            TutorialProgressStore.CreateCurrent().ResetForTests();
        }

        [Test]
        public void Catalog_DefinesSevenTabsStarterCardsAndCoreEnemies()
        {
            GameGuideCatalog catalog = GameGuideCatalog.LoadDefault();

            Assert.That(catalog.Title, Is.EqualTo("게임 가이드"));
            Assert.That(catalog.TabCount, Is.EqualTo(7));
            Assert.That(catalog.GetTab(0).Id, Is.EqualTo("basics"));
            Assert.That(catalog.GetTab(1).Id, Is.EqualTo("towers"));
            Assert.That(catalog.GetTab(2).Id, Is.EqualTo("cards"));
            Assert.That(catalog.GetTab(3).Id, Is.EqualTo("combat"));
            Assert.That(catalog.GetTab(4).Id, Is.EqualTo("monsters"));
            Assert.That(catalog.GetTab(5).Id, Is.EqualTo("rewards"));
            Assert.That(catalog.GetTab(6).Id, Is.EqualTo("controls"));

            Assert.That(catalog.StarterCardCount, Is.EqualTo(11));
            Assert.That(catalog.GetStarterCard(0).Id, Is.EqualTo("split"));
            Assert.That(catalog.GetStarterCard(10).Id, Is.EqualTo("shock"));
            Assert.That(catalog.EnemyCount, Is.EqualTo(7));
            Assert.That(catalog.GetEnemy(0).Id, Is.EqualTo("raider"));
            Assert.That(
                catalog.GetEnemy(6).Id,
                Is.EqualTo("boss_time_walker"));

            string cardBody = catalog.BuildTabBody(
                catalog.FindTabIndex("cards"));
            Assert.That(cardBody, Does.Contain("스테이지 시작 카드 11종"));
            Assert.That(cardBody, Does.Contain("분열"));
            Assert.That(cardBody, Does.Contain("감전"));

            string enemyBody = catalog.BuildTabBody(
                catalog.FindTabIndex("monsters"));
            Assert.That(enemyBody, Does.Contain("Elite Golem"));
            Assert.That(enemyBody, Does.Contain("고블린 수호대장"));
            Assert.That(enemyBody, Does.Contain("시간 슬라임"));
        }

        [Test]
        public void Modal_OpenClose_UsesLeaseAndRaisesPublicSignals()
        {
            var host = new GameObject("Game Guide Test");
            var lease = new TrackingLease();
            int anyOpened = 0;
            int anyClosed = 0;
            int instanceOpened = 0;
            int instanceClosed = 0;
            Action opened = () => anyOpened++;
            Action closed = () => anyClosed++;
            GameGuideModal.AnyGuideOpened += opened;
            GameGuideModal.AnyGuideClosed += closed;

            try
            {
                GameGuideModal modal =
                    host.AddComponent<GameGuideModal>();
                modal.Initialize(null, lease.Acquire);
                modal.Opened += () => instanceOpened++;
                modal.Closed += () => instanceClosed++;

                Assert.That(modal.IsOpen, Is.False);
                Assert.That(GameGuideModal.IsAnyGuideOpen, Is.False);

                modal.Open();

                Assert.That(modal.IsOpen, Is.True);
                Assert.That(GameGuideModal.IsAnyGuideOpen, Is.True);
                Assert.That(lease.Acquired, Is.True);
                Assert.That(instanceOpened, Is.EqualTo(1));
                Assert.That(anyOpened, Is.EqualTo(1));

                modal.SelectTab(4);
                Assert.That(modal.SelectedTabIndex, Is.EqualTo(4));
                Assert.That(modal.ContentText.text, Does.Contain("보스"));

                modal.Close();

                Assert.That(modal.IsOpen, Is.False);
                Assert.That(GameGuideModal.IsAnyGuideOpen, Is.False);
                Assert.That(lease.Disposed, Is.True);
                Assert.That(instanceClosed, Is.EqualTo(1));
                Assert.That(anyClosed, Is.EqualTo(1));
            }
            finally
            {
                GameGuideModal.AnyGuideOpened -= opened;
                GameGuideModal.AnyGuideClosed -= closed;
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void DefaultPauseLease_RestoresPreviousTimeScale()
        {
            var host = new GameObject("Game Guide Time Scale Test");
            try
            {
                Time.timeScale = 2f;
                GameGuideModal modal =
                    host.AddComponent<GameGuideModal>();
                modal.Initialize(null);

                modal.Open();
                Assert.That(Time.timeScale, Is.EqualTo(0f));

                modal.Close();
                Assert.That(Time.timeScale, Is.EqualTo(2f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ReplayButton_SetsConsumableDecoupledRequest()
        {
            GameGuideRuntime.ConsumeTutorialReplayRequest();
            TutorialProgressStore.CreateCurrent().ResetForTests();
            int raised = 0;
            Action handler = () => raised++;
            GameGuideRuntime.TutorialReplayRequested += handler;
            var host = new GameObject("Game Guide Replay Test");

            try
            {
                GameGuideModal modal =
                    host.AddComponent<GameGuideModal>();
                modal.Initialize(null, () => new TrackingLease());
                modal.Open();

                modal.TutorialReplayButton.onClick.Invoke();

                Assert.That(modal.IsOpen, Is.False);
                Assert.That(
                    GameGuideRuntime.HasPendingTutorialReplayRequest,
                    Is.True);
                Assert.That(raised, Is.EqualTo(1));
                Assert.That(
                    TutorialProgressStore.CreateCurrent()
                        .IsManualReplayRequested,
                    Is.True);
                Assert.That(
                    GameGuideRuntime.ConsumeTutorialReplayRequest(),
                    Is.True);
                Assert.That(
                    GameGuideRuntime.ConsumeTutorialReplayRequest(),
                    Is.False);
            }
            finally
            {
                GameGuideRuntime.TutorialReplayRequested -= handler;
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void BattleSettings_ProvidesGuideAccessPoint()
        {
            var host = new GameObject("Battle Guide Access Test");
            try
            {
                StageSelectionReturnButton settings =
                    host.AddComponent<StageSelectionReturnButton>();

                Assert.That(settings.GuideButton, Is.Not.Null);
                Assert.That(settings.GameGuide, Is.Not.Null);
                Assert.That(settings.GameGuide.IsOpen, Is.False);

                settings.SetMenuOpen(true);
                settings.GuideButton.onClick.Invoke();

                Assert.That(settings.IsMenuOpen, Is.False);
                Assert.That(settings.GameGuide.IsOpen, Is.True);
                settings.GameGuide.Close();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private sealed class TrackingLease : IDisposable
        {
            public bool Acquired { get; private set; }
            public bool Disposed { get; private set; }

            public IDisposable Acquire()
            {
                Acquired = true;
                return this;
            }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
