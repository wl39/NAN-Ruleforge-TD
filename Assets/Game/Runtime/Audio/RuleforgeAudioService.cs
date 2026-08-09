using RuleforgeTD.GameLogic.Simulation;
using UnityEngine;

namespace RuleforgeTD.Audio
{
    /// <summary>
    /// 런타임에서 사용하는 짧은 2D 효과음을 한 곳에서 관리한다.
    /// Resources 경로와 상대 음량을 데이터처럼 고정해 씬별 AudioSource 설정
    /// 차이를 없애며, 전투 규칙에는 관여하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuleforgeAudioService : MonoBehaviour
    {
        public enum SoundCue
        {
            UiPress,
            UiRelease,
            WaveStarted,
            ProjectileHit
        }

        public const string UiPressResourcePath =
            "RuleforgeTD/Audio/Sfx/impactWood_medium_004";
        public const string UiReleaseResourcePath =
            "RuleforgeTD/Audio/Sfx/impactWood_light_004";
        public const string WaveStartedResourcePath =
            "RuleforgeTD/Audio/Sfx/impactBell_heavy_001";
        public const string ProjectileHitResourcePath =
            "RuleforgeTD/Audio/Sfx/impactPlank_medium_002";

        // 웨이브 시작은 다른 효과음보다 약 3 dB 크게 들리도록 둔다.
        public const float UiPressVolume = 0.58f;
        public const float UiReleaseVolume = 0.45f;
        public const float ProjectileHitVolume = 0.52f;
        public const float WaveStartedVolume = 0.82f;

        // 다중 화살/분열 빌드에서도 WebGL 오디오 보이스가 폭주하지 않게 한다.
        public const int MaximumProjectileHitSoundsPerFrame = 4;

        private const string MutedPreferenceKey =
            "ruleforge.audio.muted.v1";
        private const string VolumePreferenceKey =
            "ruleforge.audio.volume.v1";
        private const string LastAudibleVolumePreferenceKey =
            "ruleforge.audio.last_audible_volume.v1";

        private static RuleforgeAudioService instance;
        private static bool muteStateLoaded;
        private static bool muted;
        private static float gameVolume = 1f;
        private static float lastAudibleVolume = 1f;

        private AudioSource output;
        private AudioClip uiPressClip;
        private AudioClip uiReleaseClip;
        private AudioClip waveStartedClip;
        private AudioClip projectileHitClip;
        private int projectileHitFrame = -1;
        private int projectileHitCount;

        public SoundCue LastPlayedCue { get; private set; }
        public float LastPlayedVolume { get; private set; }
        public int PlayedSoundCount { get; private set; }

        public static bool IsMuted
        {
            get
            {
                EnsureMuteStateLoaded();
                return muted;
            }
        }

        /// <summary>
        /// 사용자가 선택한 음량이다. 음소거 중에도 마지막 슬라이더 값을
        /// 유지하므로 다시 소리를 켰을 때 같은 음량으로 복원된다.
        /// </summary>
        public static float GameVolume
        {
            get
            {
                EnsureMuteStateLoaded();
                return gameVolume;
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            instance = null;
            muteStateLoaded = false;
            muted = false;
            gameVolume = 1f;
            lastAudibleVolume = 1f;
        }

        public static void SetMuted(bool value)
        {
            EnsureMuteStateLoaded();
            if (value && gameVolume > 0.0001f)
            {
                lastAudibleVolume = gameVolume;
            }

            if (!value && gameVolume <= 0.0001f)
            {
                gameVolume = Mathf.Max(0.01f, lastAudibleVolume);
            }

            muted = value;
            ApplyMuteState();
            SavePreferences();
        }

        public static bool ToggleMuted()
        {
            EnsureMuteStateLoaded();
            SetMuted(!IsMuted);
            return muted;
        }

        public static void SetVolume(float value)
        {
            EnsureMuteStateLoaded();
            float clamped = Mathf.Clamp01(value);
            if (clamped > 0.0001f)
            {
                gameVolume = clamped;
                lastAudibleVolume = clamped;
                muted = false;
            }
            else
            {
                if (gameVolume > 0.0001f)
                {
                    lastAudibleVolume = gameVolume;
                }

                gameVolume = 0f;
                muted = true;
            }

            ApplyMuteState();
            SavePreferences();
        }

        public static void PlayUiPress()
        {
            EnsureInstance().Play(SoundCue.UiPress);
        }

        public static void PlayUiRelease()
        {
            EnsureInstance().Play(SoundCue.UiRelease);
        }

        public static void PlayPresentationEvent(
            PresentationEventType eventType)
        {
            switch (eventType)
            {
                case PresentationEventType.WaveStarted:
                    EnsureInstance().Play(SoundCue.WaveStarted);
                    break;
                case PresentationEventType.ProjectileHit:
                    EnsureInstance().PlayProjectileHit();
                    break;
            }
        }

        private static RuleforgeAudioService EnsureInstance()
        {
            if (instance != null)
            {
                return instance;
            }

            instance = FindObjectOfType<RuleforgeAudioService>();
            if (instance != null)
            {
                instance.EnsureOutput();
                return instance;
            }

            var host = new GameObject(
                "Ruleforge Audio",
                typeof(AudioSource),
                typeof(RuleforgeAudioService));
            DontDestroyOnLoad(host);
            instance = host.GetComponent<RuleforgeAudioService>();
            instance.EnsureOutput();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureOutput();
            EnsureMuteStateLoaded();
            ApplyMuteState();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void EnsureOutput()
        {
            if (output == null)
            {
                output = GetComponent<AudioSource>();
            }

            if (output == null)
            {
                output = gameObject.AddComponent<AudioSource>();
            }

            output.playOnAwake = false;
            output.loop = false;
            output.spatialBlend = 0f;
            output.ignoreListenerPause = true;
        }

        private static void EnsureMuteStateLoaded()
        {
            if (muteStateLoaded)
            {
                return;
            }

            muted = PlayerPrefs.GetInt(MutedPreferenceKey, 0) != 0;
            gameVolume = Mathf.Clamp01(
                PlayerPrefs.GetFloat(VolumePreferenceKey, 1f));
            lastAudibleVolume = Mathf.Clamp01(
                PlayerPrefs.GetFloat(
                    LastAudibleVolumePreferenceKey,
                    gameVolume > 0.0001f ? gameVolume : 1f));
            if (lastAudibleVolume <= 0.0001f)
            {
                lastAudibleVolume = 1f;
            }

            if (gameVolume <= 0.0001f)
            {
                muted = true;
            }
            muteStateLoaded = true;
            ApplyMuteState();
        }

        private static void ApplyMuteState()
        {
            AudioListener.volume = muted ? 0f : gameVolume;
        }

        private static void SavePreferences()
        {
            PlayerPrefs.SetInt(MutedPreferenceKey, muted ? 1 : 0);
            PlayerPrefs.SetFloat(VolumePreferenceKey, gameVolume);
            PlayerPrefs.SetFloat(
                LastAudibleVolumePreferenceKey,
                lastAudibleVolume);
            PlayerPrefs.Save();
        }

        private void PlayProjectileHit()
        {
            int frame = Time.frameCount;
            if (projectileHitFrame != frame)
            {
                projectileHitFrame = frame;
                projectileHitCount = 0;
            }

            if (projectileHitCount >=
                MaximumProjectileHitSoundsPerFrame)
            {
                return;
            }

            // 같은 프레임의 동시 적중은 첫 타격을 선명하게 남기고 뒤쪽
            // 타격을 조금씩 낮춰 클리핑과 피로도를 줄인다.
            float attenuation =
                1f / (1f + projectileHitCount * 0.18f);
            projectileHitCount++;
            Play(
                SoundCue.ProjectileHit,
                ProjectileHitVolume * attenuation);
        }

        private void Play(SoundCue cue)
        {
            Play(cue, GetDefaultVolume(cue));
        }

        private void Play(SoundCue cue, float volume)
        {
            EnsureOutput();
            AudioClip clip = GetClip(cue);
            if (clip == null)
            {
                Debug.LogWarning(
                    "Ruleforge audio clip is missing: " +
                    GetResourcePath(cue),
                    this);
                return;
            }

            output.PlayOneShot(clip, Mathf.Clamp01(volume));
            LastPlayedCue = cue;
            LastPlayedVolume = Mathf.Clamp01(volume);
            PlayedSoundCount++;
        }

        private AudioClip GetClip(SoundCue cue)
        {
            switch (cue)
            {
                case SoundCue.UiPress:
                    return uiPressClip != null
                        ? uiPressClip
                        : uiPressClip = LoadClip(cue);
                case SoundCue.UiRelease:
                    return uiReleaseClip != null
                        ? uiReleaseClip
                        : uiReleaseClip = LoadClip(cue);
                case SoundCue.WaveStarted:
                    return waveStartedClip != null
                        ? waveStartedClip
                        : waveStartedClip = LoadClip(cue);
                case SoundCue.ProjectileHit:
                    return projectileHitClip != null
                        ? projectileHitClip
                        : projectileHitClip = LoadClip(cue);
                default:
                    return null;
            }
        }

        private static AudioClip LoadClip(SoundCue cue)
        {
            return Resources.Load<AudioClip>(
                GetResourcePath(cue));
        }

        private static string GetResourcePath(SoundCue cue)
        {
            switch (cue)
            {
                case SoundCue.UiPress:
                    return UiPressResourcePath;
                case SoundCue.UiRelease:
                    return UiReleaseResourcePath;
                case SoundCue.WaveStarted:
                    return WaveStartedResourcePath;
                case SoundCue.ProjectileHit:
                    return ProjectileHitResourcePath;
                default:
                    return string.Empty;
            }
        }

        private static float GetDefaultVolume(SoundCue cue)
        {
            switch (cue)
            {
                case SoundCue.UiPress:
                    return UiPressVolume;
                case SoundCue.UiRelease:
                    return UiReleaseVolume;
                case SoundCue.WaveStarted:
                    return WaveStartedVolume;
                case SoundCue.ProjectileHit:
                    return ProjectileHitVolume;
                default:
                    return 0f;
            }
        }
    }
}
