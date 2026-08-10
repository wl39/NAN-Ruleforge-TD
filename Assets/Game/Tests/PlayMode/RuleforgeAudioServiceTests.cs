using System.Collections;
using NUnit.Framework;
using RuleforgeTD.Audio;
using RuleforgeTD.GameLogic.Simulation;
using RuleforgeTD.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RuleforgeTD.Tests.PlayMode
{
    public sealed class RuleforgeAudioServiceTests
    {
        private bool previousMuteState;
        private float previousVolume;

        [SetUp]
        public void SetUp()
        {
            previousMuteState = RuleforgeAudioService.IsMuted;
            previousVolume = RuleforgeAudioService.GameVolume;
        }

        [TearDown]
        public void TearDown()
        {
            RuleforgeAudioService.SetVolume(previousVolume);
            RuleforgeAudioService.SetMuted(previousMuteState);
            RuleforgeAudioService service =
                Object.FindObjectOfType<RuleforgeAudioService>();
            if (service != null)
            {
                Object.DestroyImmediate(service.gameObject);
            }
        }

        [Test]
        public void Resources_ContainAllConfiguredSoundEffects()
        {
            AssertClipExists(
                RuleforgeAudioService.UiPressResourcePath);
            AssertClipExists(
                RuleforgeAudioService.UiReleaseResourcePath);
            AssertClipExists(
                RuleforgeAudioService.WaveStartedResourcePath);
            AssertClipExists(
                RuleforgeAudioService.ProjectileHitResourcePath);
        }

        [Test]
        public void Resources_ContainAllConfiguredMusicTracks()
        {
            AssertClipExists(
                RuleforgeAudioService.MenuMusicResourcePath);
            AssertClipExists(
                RuleforgeAudioService.PlanningMusicResourcePath);
            AssertClipExists(
                RuleforgeAudioService.BattleIntroResourcePath);
            AssertClipExists(
                RuleforgeAudioService.BattleLoopResourcePath);
        }

        [Test]
        public void MusicState_UsesPlanningAndScheduledBattleLayers()
        {
            RuleforgeAudioService.PlayMusicForPhase(
                RunPhase.Planning,
                0f);
            RuleforgeAudioService service =
                Object.FindObjectOfType<RuleforgeAudioService>();
            Assert.That(service, Is.Not.Null);
            Assert.That(
                service.CurrentMusicCue,
                Is.EqualTo(
                    RuleforgeAudioService.MusicCue.Planning));
            Assert.That(service.ActiveMusicLayerCount, Is.EqualTo(1));

            RuleforgeAudioService.PlayMusicForPhase(
                RunPhase.Combat,
                0f);
            Assert.That(
                service.CurrentMusicCue,
                Is.EqualTo(
                    RuleforgeAudioService.MusicCue.Battle));
            Assert.That(
                service.ActiveMusicLayerCount,
                Is.EqualTo(2),
                "Battle music must schedule one intro and one loop layer.");

            RuleforgeAudioService.PlayMusicForPhase(
                RunPhase.Draft,
                0f);
            Assert.That(
                service.CurrentMusicCue,
                Is.EqualTo(
                    RuleforgeAudioService.MusicCue.Planning));
            Assert.That(service.ActiveMusicLayerCount, Is.EqualTo(1));
        }

        [Test]
        public void BattleSegments_MatchAnalyzedLoopDurations()
        {
            AudioClip intro = Resources.Load<AudioClip>(
                RuleforgeAudioService.BattleIntroResourcePath);
            AudioClip loop = Resources.Load<AudioClip>(
                RuleforgeAudioService.BattleLoopResourcePath);

            Assert.That(intro.channels, Is.EqualTo(2));
            Assert.That(loop.channels, Is.EqualTo(2));
            Assert.That(
                intro.length,
                Is.EqualTo(
                    RuleforgeAudioService.BattleIntroDuration)
                    .Within(0.02f));
            Assert.That(
                loop.length,
                Is.EqualTo(
                    RuleforgeAudioService.BattleLoopDuration)
                    .Within(0.02f));
        }

        [Test]
        public void PixelButton_PlaysMediumWoodDownAndLightWoodUp()
        {
            var eventSystemHost = new GameObject(
                "Audio Test Event System",
                typeof(EventSystem));
            var buttonHost = new GameObject(
                "Audio Test Button",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            try
            {
                Button button = buttonHost.GetComponent<Button>();
                button.targetGraphic = buttonHost.GetComponent<Image>();
                RuleforgePixelUi.Apply(
                    button,
                    RuleforgePixelButtonRole.Primary);

                RuleforgePixelButtonSkin skin =
                    buttonHost.GetComponent<
                        RuleforgePixelButtonSkin>();
                var pointer = new PointerEventData(
                    eventSystemHost.GetComponent<EventSystem>())
                {
                    button = PointerEventData.InputButton.Left,
                    pointerCurrentRaycast = new RaycastResult
                    {
                        gameObject = buttonHost
                    }
                };

                skin.OnPointerDown(pointer);
                RuleforgeAudioService service =
                    Object.FindObjectOfType<
                        RuleforgeAudioService>();
                Assert.That(service, Is.Not.Null);
                Assert.That(
                    service.LastPlayedCue,
                    Is.EqualTo(
                        RuleforgeAudioService.SoundCue.UiPress));

                skin.OnPointerUp(pointer);
                Assert.That(
                    service.LastPlayedCue,
                    Is.EqualTo(
                        RuleforgeAudioService.SoundCue.UiRelease));
                Assert.That(service.PlayedSoundCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(buttonHost);
                Object.DestroyImmediate(eventSystemHost);
            }
        }

        [Test]
        public void CombatEvents_UseLouderWaveBellAndPlankHit()
        {
            RuleforgeAudioService.PlayPresentationEvent(
                PresentationEventType.WaveStarted);
            RuleforgeAudioService service =
                Object.FindObjectOfType<RuleforgeAudioService>();
            Assert.That(service, Is.Not.Null);
            Assert.That(
                service.LastPlayedCue,
                Is.EqualTo(
                    RuleforgeAudioService.SoundCue.WaveStarted));
            float waveVolume = service.LastPlayedVolume;

            RuleforgeAudioService.PlayPresentationEvent(
                PresentationEventType.ProjectileHit);
            Assert.That(
                service.LastPlayedCue,
                Is.EqualTo(
                    RuleforgeAudioService.SoundCue.ProjectileHit));
            Assert.That(
                waveVolume,
                Is.GreaterThan(service.LastPlayedVolume));
        }

        [Test]
        public void ProjectileHitBurst_AttenuatesAndLimitsOverlappingSounds()
        {
            RuleforgeAudioService.PlayPresentationEvent(
                PresentationEventType.ProjectileHit);
            RuleforgeAudioService service =
                Object.FindObjectOfType<RuleforgeAudioService>();
            Assert.That(service, Is.Not.Null);

            float firstHitVolume = service.LastPlayedVolume;
            for (int i = 1; i < 12; i++)
            {
                RuleforgeAudioService.PlayPresentationEvent(
                    PresentationEventType.ProjectileHit);
            }

            Assert.That(
                service.PlayedSoundCount,
                Is.EqualTo(
                    RuleforgeAudioService
                        .MaximumProjectileHitSoundsPerFrame));
            Assert.That(
                service.LastPlayedVolume,
                Is.LessThan(firstHitVolume * 0.65f),
                "A dense hit burst must lower each overlapping hit sound.");
        }

        [UnityTest]
        public IEnumerator ProjectileHitPressure_PersistsAcrossFrames()
        {
            RuleforgeAudioService.PlayPresentationEvent(
                PresentationEventType.ProjectileHit);
            RuleforgeAudioService service =
                Object.FindObjectOfType<RuleforgeAudioService>();
            Assert.That(service, Is.Not.Null);
            float isolatedHitVolume = service.LastPlayedVolume;

            for (int i = 1; i < 12; i++)
            {
                RuleforgeAudioService.PlayPresentationEvent(
                    PresentationEventType.ProjectileHit);
            }

            yield return null;

            int playedBeforeNextFrameHit = service.PlayedSoundCount;
            RuleforgeAudioService.PlayPresentationEvent(
                PresentationEventType.ProjectileHit);

            Assert.That(
                service.PlayedSoundCount,
                Is.EqualTo(playedBeforeNextFrameHit + 1));
            Assert.That(
                service.LastPlayedVolume,
                Is.LessThan(isolatedHitVolume * 0.4f),
                "Hit density attenuation must not reset on the next frame.");
        }

        [Test]
        public void VolumeAndMute_ControlsTheGlobalGameVolume()
        {
            RuleforgeAudioService.SetVolume(0.35f);
            Assert.That(RuleforgeAudioService.IsMuted, Is.False);
            Assert.That(
                RuleforgeAudioService.GameVolume,
                Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(
                AudioListener.volume,
                Is.EqualTo(0.35f).Within(0.001f));

            Assert.That(RuleforgeAudioService.ToggleMuted(), Is.True);
            Assert.That(RuleforgeAudioService.IsMuted, Is.True);
            Assert.That(AudioListener.volume, Is.EqualTo(0f));

            Assert.That(RuleforgeAudioService.ToggleMuted(), Is.False);
            Assert.That(
                AudioListener.volume,
                Is.EqualTo(0.35f).Within(0.001f));

            RuleforgeAudioService.SetVolume(0f);
            Assert.That(RuleforgeAudioService.IsMuted, Is.True);
            Assert.That(AudioListener.volume, Is.EqualTo(0f));

            Assert.That(RuleforgeAudioService.ToggleMuted(), Is.False);
            Assert.That(
                RuleforgeAudioService.GameVolume,
                Is.EqualTo(0.35f).Within(0.001f));
        }

        [Test]
        public void BattleSettings_UsesGearVolumeAndExitConfirmation()
        {
            var host = new GameObject("Battle Settings Test");
            try
            {
                StageSelectionReturnButton settings =
                    host.AddComponent<StageSelectionReturnButton>();

                Assert.That(settings.SettingsButton, Is.Not.Null);
                Assert.That(settings.StageSelectionButton, Is.Not.Null);
                Assert.That(settings.SpeakerButton, Is.Not.Null);
                Assert.That(settings.VolumeSlider, Is.Not.Null);
                Assert.That(
                    settings.VolumeSlider.GetComponent<Image>()
                        .raycastTarget,
                    Is.True,
                    "The whole slider must receive pointer drags.");
                Assert.That(
                    settings.VolumeSlider.GetComponent<RectTransform>()
                        .sizeDelta.x,
                    Is.EqualTo(160f).Within(0.001f));
                Assert.That(
                    settings.VolumeSlider.handleRect.sizeDelta.y,
                    Is.EqualTo(10f).Within(0.001f));
                Assert.That(
                    settings.VolumeSlider.handleRect.sizeDelta.x,
                    Is.EqualTo(
                        settings.VolumeSlider.handleRect.sizeDelta.y)
                        .Within(0.001f),
                    "The volume handle must be circular, not a bar.");
                Assert.That(
                    settings.VolumeSlider.handleRect.GetComponent<
                        RuleforgeSliderKnobGraphic>(),
                    Is.Not.Null);
                Assert.That(settings.CancelButton, Is.Not.Null);
                Assert.That(settings.ConfirmButton, Is.Not.Null);
                Assert.That(
                    settings.SettingsIcon.Mode,
                    Is.EqualTo(RuleforgeSettingsIconMode.Gear));
                Assert.That(settings.IsMenuOpen, Is.False);
                Assert.That(settings.IsConfirmationOpen, Is.False);

                settings.SettingsButton.onClick.Invoke();
                Assert.That(settings.IsMenuOpen, Is.True);

                RuleforgeAudioService.SetVolume(1f);
                settings.SetMenuOpen(true);
                settings.VolumeSlider.value = 0.42f;
                Assert.That(
                    RuleforgeAudioService.GameVolume,
                    Is.EqualTo(0.42f).Within(0.001f));
                settings.SpeakerButton.onClick.Invoke();
                Assert.That(RuleforgeAudioService.IsMuted, Is.True);
                Assert.That(
                    settings.VolumeSlider.value,
                    Is.EqualTo(0f).Within(0.001f));
                Assert.That(
                    settings.SpeakerIcon.Mode,
                    Is.EqualTo(
                        RuleforgeSettingsIconMode.SpeakerMuted));

                settings.SpeakerButton.onClick.Invoke();
                Assert.That(RuleforgeAudioService.IsMuted, Is.False);
                Assert.That(
                    settings.VolumeSlider.value,
                    Is.EqualTo(0.42f).Within(0.001f));

                settings.VolumeSlider.value = 0.5f;
                var moveLeft = new AxisEventData(EventSystem.current)
                {
                    moveDir = MoveDirection.Left
                };
                settings.VolumeSlider.OnMove(moveLeft);
                Assert.That(
                    settings.VolumeSlider.value,
                    Is.LessThan(0.5f),
                    "Left input must reduce the volume.");
                float reducedVolume = settings.VolumeSlider.value;
                var moveRight = new AxisEventData(EventSystem.current)
                {
                    moveDir = MoveDirection.Right
                };
                settings.VolumeSlider.OnMove(moveRight);
                Assert.That(
                    settings.VolumeSlider.value,
                    Is.GreaterThan(reducedVolume),
                    "Right input must increase the volume.");

                settings.StageSelectionButton.onClick.Invoke();
                Assert.That(settings.IsMenuOpen, Is.False);
                Assert.That(settings.IsConfirmationOpen, Is.True);

                settings.CancelButton.onClick.Invoke();
                Assert.That(settings.IsConfirmationOpen, Is.False);
                Assert.That(settings.IsMenuOpen, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static void AssertClipExists(string resourcePath)
        {
            Assert.That(
                Resources.Load<AudioClip>(resourcePath),
                Is.Not.Null,
                "Missing audio resource: " + resourcePath);
        }
    }
}
