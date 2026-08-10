using System.Collections;
using RuleforgeTD.GameLogic.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleforgeTD.Audio
{
    /// <summary>
    /// 런타임의 2D 효과음과 상태 기반 배경 음악을 한 곳에서 관리한다.
    /// 배경 음악은 두 개의 덱을 겹쳐 재생해 씬/전투 상태 변경 중에도
    /// 끊김 없는 크로스페이드를 제공하며, 전투 규칙에는 관여하지 않는다.
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

        public enum MusicCue
        {
            None,
            Menu,
            Planning,
            Battle
        }

        public const string UiPressResourcePath =
            "RuleforgeTD/Audio/Sfx/impactWood_medium_004";
        public const string UiReleaseResourcePath =
            "RuleforgeTD/Audio/Sfx/impactWood_light_004";
        public const string WaveStartedResourcePath =
            "RuleforgeTD/Audio/Sfx/impactBell_heavy_001";
        public const string ProjectileHitResourcePath =
            "RuleforgeTD/Audio/Sfx/impactPlank_medium_002";

        public const string MenuMusicResourcePath =
            "RuleforgeTD/Audio/Music/Menu_MinstrelDance";
        public const string PlanningMusicResourcePath =
            "RuleforgeTD/Audio/Music/Planning_TheBardsTale";
        public const string BattleIntroResourcePath =
            "RuleforgeTD/Audio/Music/Battle_Intro";
        public const string BattleLoopResourcePath =
            "RuleforgeTD/Audio/Music/Battle_Loop";

        // 웨이브 시작은 다른 효과음보다 약 3 dB 크게 들리도록 둔다.
        public const float UiPressVolume = 0.58f;
        public const float UiReleaseVolume = 0.45f;
        public const float ProjectileHitVolume = 0.52f;
        public const float WaveStartedVolume = 0.82f;

        // 분석한 활성 RMS를 기준으로 세 곡의 체감 음량을 약 -22 dBFS에
        // 맞춘 상대 게인이다. 전투곡 자체의 다이내믹은 그대로 유지한다.
        public const float MenuMusicVolume = 0.68f;
        public const float PlanningMusicVolume = 0.90f;
        public const float BattleMusicVolume = 0.52f;
        public const float MusicCrossfadeDuration = 1.35f;
        public const float InitialMusicFadeDuration = 0.80f;

        // 전투곡 분석 결과: 103.36 BPM, 0~11.915737초 도입부 이후
        // 27.303129초 루프. 두 PCM 자산의 경계는 샘플 단위로 이어진다.
        public const float BattleIntroDuration = 11.915737f;
        public const float BattleLoopDuration = 27.303129f;

        // 다중 화살/분열 빌드에서도 WebGL 오디오 보이스가 폭주하지 않게 한다.
        public const int MaximumProjectileHitSoundsPerFrame = 4;

        // 최근 적중 밀도를 프레임 경계 너머까지 기억한다. 피격 요청이 몰리면
        // 개별 음량을 낮추고, 조용해진 뒤에는 초당 이만큼 압력을 회복한다.
        public const float ProjectileHitPressureRecoveryPerSecond = 8f;
        public const float ProjectileHitPressureLimit = 16f;
        public const float ProjectileHitAttenuationPerPressure = 0.85f;
        public const float MinimumProjectileHitAttenuation = 0.18f;

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
        private readonly AudioSource[] musicPrimaryOutputs =
            new AudioSource[2];
        private readonly AudioSource[] musicLoopOutputs =
            new AudioSource[2];
        private readonly MusicCue[] musicDeckCues =
            new MusicCue[2];
        private readonly float[] musicDeckMix = new float[2];
        private AudioClip uiPressClip;
        private AudioClip uiReleaseClip;
        private AudioClip waveStartedClip;
        private AudioClip projectileHitClip;
        private AudioClip menuMusicClip;
        private AudioClip planningMusicClip;
        private AudioClip battleIntroClip;
        private AudioClip battleLoopClip;
        private Coroutine musicCrossfadeRoutine;
        private int activeMusicDeck = -1;
        private bool sceneHooked;
        private int projectileHitFrame = -1;
        private int projectileHitCount;
        private float projectileHitPressure;
        private float projectileHitPressureUpdatedAt = -1f;

        public SoundCue LastPlayedCue { get; private set; }
        public float LastPlayedVolume { get; private set; }
        public int PlayedSoundCount { get; private set; }
        public MusicCue CurrentMusicCue { get; private set; }

        public int ActiveMusicLayerCount
        {
            get
            {
                if (activeMusicDeck < 0)
                {
                    return 0;
                }

                int count =
                    musicPrimaryOutputs[activeMusicDeck] != null &&
                    musicPrimaryOutputs[activeMusicDeck].clip != null
                        ? 1
                        : 0;
                if (musicLoopOutputs[activeMusicDeck] != null &&
                    musicLoopOutputs[activeMusicDeck].clip != null)
                {
                    count++;
                }

                return count;
            }
        }

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

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartSceneMusic()
        {
            RuleforgeAudioService service = EnsureInstance();
            service.HandleSceneLoaded(
                SceneManager.GetActiveScene(),
                LoadSceneMode.Single);
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

        public static void PlayMenuMusic(
            float fadeDuration = MusicCrossfadeDuration)
        {
            EnsureInstance().PlayMusic(
                MusicCue.Menu,
                fadeDuration);
        }

        public static void PlayMusicForPhase(
            RunPhase phase,
            float fadeDuration = MusicCrossfadeDuration)
        {
            MusicCue cue = phase == RunPhase.Combat
                ? MusicCue.Battle
                : MusicCue.Planning;
            EnsureInstance().PlayMusic(cue, fadeDuration);
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
                instance.EnsureMusicOutputs();
                instance.EnsureSceneHook();
                return instance;
            }

            var host = new GameObject(
                "Ruleforge Audio",
                typeof(AudioSource),
                typeof(RuleforgeAudioService));
            DontDestroyOnLoad(host);
            instance = host.GetComponent<RuleforgeAudioService>();
            instance.EnsureOutput();
            instance.EnsureMusicOutputs();
            instance.EnsureSceneHook();
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
            EnsureMusicOutputs();
            EnsureSceneHook();
            EnsureMuteStateLoaded();
            ApplyMuteState();
        }

        private void OnDestroy()
        {
            if (sceneHooked)
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                sceneHooked = false;
            }

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

        private void EnsureMusicOutputs()
        {
            for (int deck = 0; deck < musicPrimaryOutputs.Length; deck++)
            {
                if (musicPrimaryOutputs[deck] != null &&
                    musicLoopOutputs[deck] != null)
                {
                    continue;
                }

                string deckName = deck == 0 ? "A" : "B";
                musicPrimaryOutputs[deck] = CreateMusicOutput(
                    "Music Deck " + deckName + " Primary");
                musicLoopOutputs[deck] = CreateMusicOutput(
                    "Music Deck " + deckName + " Loop");
            }
        }

        private AudioSource CreateMusicOutput(string objectName)
        {
            var host = new GameObject(
                objectName,
                typeof(AudioSource));
            host.transform.SetParent(transform, false);
            AudioSource source = host.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
            source.priority = 0;
            source.volume = 0f;
            return source;
        }

        private void EnsureSceneHook()
        {
            if (sceneHooked)
            {
                return;
            }

            SceneManager.sceneLoaded += HandleSceneLoaded;
            sceneHooked = true;
        }

        private void HandleSceneLoaded(
            Scene scene,
            LoadSceneMode mode)
        {
            if (string.Equals(
                    scene.name,
                    "MainMenu",
                    System.StringComparison.Ordinal))
            {
                PlayMusic(
                    MusicCue.Menu,
                    activeMusicDeck < 0
                        ? InitialMusicFadeDuration
                        : MusicCrossfadeDuration);
            }
        }

        private void PlayMusic(
            MusicCue cue,
            float fadeDuration)
        {
            EnsureMusicOutputs();
            if (cue == MusicCue.None ||
                (activeMusicDeck >= 0 &&
                 musicDeckCues[activeMusicDeck] == cue))
            {
                return;
            }

            int outgoingDeck = activeMusicDeck;
            int incomingDeck = outgoingDeck == 0 ? 1 : 0;
            if (!ConfigureMusicDeck(incomingDeck, cue))
            {
                return;
            }

            if (musicCrossfadeRoutine != null)
            {
                StopCoroutine(musicCrossfadeRoutine);
                musicCrossfadeRoutine = null;
            }

            activeMusicDeck = incomingDeck;
            CurrentMusicCue = cue;
            float duration = Mathf.Max(0f, fadeDuration);
            if (duration <= 0.0001f)
            {
                SetMusicDeckMix(incomingDeck, 1f);
                StopMusicDeck(outgoingDeck);
                return;
            }

            musicCrossfadeRoutine = StartCoroutine(
                CrossfadeMusicDecks(
                    outgoingDeck,
                    incomingDeck,
                    duration));
        }

        private bool ConfigureMusicDeck(
            int deck,
            MusicCue cue)
        {
            StopMusicDeck(deck);
            AudioSource primary = musicPrimaryOutputs[deck];
            AudioSource loop = musicLoopOutputs[deck];
            if (cue == MusicCue.Battle)
            {
                AudioClip introClip = GetMusicClip(
                    MusicCue.Battle,
                    false);
                AudioClip loopClip = GetMusicClip(
                    MusicCue.Battle,
                    true);
                if (introClip == null || loopClip == null)
                {
                    WarnMissingMusic(
                        introClip == null
                            ? BattleIntroResourcePath
                            : BattleLoopResourcePath);
                    return false;
                }

                primary.clip = introClip;
                primary.loop = false;
                loop.clip = loopClip;
                loop.loop = true;
                double startTime = AudioSettings.dspTime + 0.10d;
                primary.PlayScheduled(startTime);
                loop.PlayScheduled(
                    startTime +
                    introClip.samples /
                    (double)introClip.frequency);
            }
            else
            {
                AudioClip clip = GetMusicClip(cue, false);
                if (clip == null)
                {
                    WarnMissingMusic(GetMusicResourcePath(cue));
                    return false;
                }

                primary.clip = clip;
                primary.loop = true;
                primary.Play();
            }

            musicDeckCues[deck] = cue;
            SetMusicDeckMix(deck, 0f);
            return true;
        }

        private IEnumerator CrossfadeMusicDecks(
            int outgoingDeck,
            int incomingDeck,
            float duration)
        {
            float outgoingStart = outgoingDeck >= 0
                ? musicDeckMix[outgoingDeck]
                : 0f;
            float incomingStart = musicDeckMix[incomingDeck];
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float eased = normalized * normalized *
                              (3f - 2f * normalized);
                if (outgoingDeck >= 0)
                {
                    SetMusicDeckMix(
                        outgoingDeck,
                        Mathf.Lerp(outgoingStart, 0f, eased));
                }

                SetMusicDeckMix(
                    incomingDeck,
                    Mathf.Lerp(incomingStart, 1f, eased));
                yield return null;
            }

            SetMusicDeckMix(incomingDeck, 1f);
            StopMusicDeck(outgoingDeck);
            musicCrossfadeRoutine = null;
        }

        private void SetMusicDeckMix(int deck, float mix)
        {
            if (deck < 0 || deck >= musicDeckMix.Length)
            {
                return;
            }

            float clamped = Mathf.Clamp01(mix);
            musicDeckMix[deck] = clamped;
            float volume = GetMusicVolume(musicDeckCues[deck]) * clamped;
            if (musicPrimaryOutputs[deck] != null)
            {
                musicPrimaryOutputs[deck].volume = volume;
            }

            if (musicLoopOutputs[deck] != null)
            {
                musicLoopOutputs[deck].volume = volume;
            }
        }

        private void StopMusicDeck(int deck)
        {
            if (deck < 0 || deck >= musicDeckCues.Length)
            {
                return;
            }

            StopMusicOutput(musicPrimaryOutputs[deck]);
            StopMusicOutput(musicLoopOutputs[deck]);
            musicDeckCues[deck] = MusicCue.None;
            musicDeckMix[deck] = 0f;
        }

        private static void StopMusicOutput(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            source.Stop();
            source.clip = null;
            source.loop = false;
            source.volume = 0f;
        }

        private AudioClip GetMusicClip(
            MusicCue cue,
            bool loopSegment)
        {
            switch (cue)
            {
                case MusicCue.Menu:
                    return menuMusicClip != null
                        ? menuMusicClip
                        : menuMusicClip = Resources.Load<AudioClip>(
                            MenuMusicResourcePath);
                case MusicCue.Planning:
                    return planningMusicClip != null
                        ? planningMusicClip
                        : planningMusicClip = Resources.Load<AudioClip>(
                            PlanningMusicResourcePath);
                case MusicCue.Battle:
                    if (loopSegment)
                    {
                        return battleLoopClip != null
                            ? battleLoopClip
                            : battleLoopClip = Resources.Load<AudioClip>(
                                BattleLoopResourcePath);
                    }

                    return battleIntroClip != null
                        ? battleIntroClip
                        : battleIntroClip = Resources.Load<AudioClip>(
                            BattleIntroResourcePath);
                default:
                    return null;
            }
        }

        private static string GetMusicResourcePath(MusicCue cue)
        {
            switch (cue)
            {
                case MusicCue.Menu:
                    return MenuMusicResourcePath;
                case MusicCue.Planning:
                    return PlanningMusicResourcePath;
                case MusicCue.Battle:
                    return BattleIntroResourcePath;
                default:
                    return string.Empty;
            }
        }

        private static float GetMusicVolume(MusicCue cue)
        {
            switch (cue)
            {
                case MusicCue.Menu:
                    return MenuMusicVolume;
                case MusicCue.Planning:
                    return PlanningMusicVolume;
                case MusicCue.Battle:
                    return BattleMusicVolume;
                default:
                    return 0f;
            }
        }

        private void WarnMissingMusic(string resourcePath)
        {
            Debug.LogWarning(
                "Ruleforge music clip is missing: " + resourcePath,
                this);
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
            float now = Time.realtimeSinceStartup;
            if (projectileHitPressureUpdatedAt >= 0f)
            {
                float elapsed = Mathf.Max(
                    0f,
                    now - projectileHitPressureUpdatedAt);
                projectileHitPressure = Mathf.Max(
                    0f,
                    projectileHitPressure -
                    elapsed * ProjectileHitPressureRecoveryPerSecond);
            }

            projectileHitPressureUpdatedAt = now;
            float attenuation = Mathf.Max(
                MinimumProjectileHitAttenuation,
                1f / Mathf.Sqrt(
                    1f + projectileHitPressure *
                    ProjectileHitAttenuationPerPressure));
            projectileHitPressure = Mathf.Min(
                ProjectileHitPressureLimit,
                projectileHitPressure + 1f);

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
