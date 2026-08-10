using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuleforgeTD.Tutorial
{
    /// <summary>
    /// WebGL-safe persistence boundary for tutorial presentation state.
    /// PlayerPrefs maps to browser storage in WebGL, while no value enters
    /// battle simulation or deterministic replay state.
    /// </summary>
    public sealed class TutorialProgressStore
    {
        private const string KeyRoot = "ruleforge.tutorial.";
        private const string CompletedSuffix = ".completed";
        private const string SkippedSuffix = ".skipped";
        private const string ReplaySuffix = ".manual_replay";
        private const string SeenTipsSuffix = ".seen_tips";

        private readonly string keyPrefix;
        private HashSet<string> seenTipIds;

        public TutorialProgressStore(
            string tutorialId,
            int contentVersion)
        {
            TutorialIdentifier.ThrowIfInvalid(
                tutorialId,
                nameof(tutorialId));
            if (contentVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(contentVersion),
                    contentVersion,
                    "Tutorial content version must be positive.");
            }

            TutorialId = tutorialId;
            ContentVersion = contentVersion;
            keyPrefix = KeyRoot + tutorialId + ".v" + contentVersion;
        }

        public string TutorialId { get; }
        public int ContentVersion { get; }
        public bool IsCompleted => ReadFlag(CompletedSuffix);
        public bool IsSkipped => ReadFlag(SkippedSuffix);
        public bool IsResolved => IsCompleted || IsSkipped;
        public bool IsManualReplayRequested => ReadFlag(ReplaySuffix);
        public bool ShouldAutoStart => !IsResolved;
        public bool ShouldStartTutorial =>
            IsManualReplayRequested || ShouldAutoStart;

        public static TutorialProgressStore CreateCurrent()
        {
            return new TutorialProgressStore(
                TutorialIds.CoreTutorialId,
                TutorialIds.CurrentContentVersion);
        }

        public static TutorialProgressStore FromDefinition(
            TutorialDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return new TutorialProgressStore(
                definition.TutorialId,
                definition.ContentVersion);
        }

        public void MarkCompleted()
        {
            PlayerPrefs.SetInt(Key(CompletedSuffix), 1);
            PlayerPrefs.DeleteKey(Key(SkippedSuffix));
            PlayerPrefs.DeleteKey(Key(ReplaySuffix));
            PlayerPrefs.Save();
        }

        public void MarkSkipped()
        {
            PlayerPrefs.SetInt(Key(SkippedSuffix), 1);
            PlayerPrefs.DeleteKey(Key(CompletedSuffix));
            PlayerPrefs.DeleteKey(Key(ReplaySuffix));
            PlayerPrefs.Save();
        }

        public void RequestManualReplay()
        {
            PlayerPrefs.SetInt(Key(ReplaySuffix), 1);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Consumes the request when a fresh Stage 01 run accepts it. The
        /// completed/skipped marker remains so abandoning that replay does not
        /// turn future visits into automatic tutorials.
        /// </summary>
        public bool ConsumeManualReplayRequest()
        {
            if (!IsManualReplayRequested)
            {
                return false;
            }

            PlayerPrefs.DeleteKey(Key(ReplaySuffix));
            PlayerPrefs.Save();
            return true;
        }

        public bool HasSeenContextualTip(string tipId)
        {
            TutorialIdentifier.ThrowIfInvalid(tipId, nameof(tipId));
            return GetSeenTipIds().Contains(tipId);
        }

        /// <summary>
        /// Returns true only for the first persisted acknowledgement of an ID.
        /// </summary>
        public bool MarkContextualTipSeen(string tipId)
        {
            TutorialIdentifier.ThrowIfInvalid(tipId, nameof(tipId));
            HashSet<string> ids = GetSeenTipIds();
            if (!ids.Add(tipId))
            {
                return false;
            }

            var sorted = new List<string>(ids);
            sorted.Sort(StringComparer.Ordinal);
            var state = new SeenTipState
            {
                ids = sorted.ToArray()
            };
            PlayerPrefs.SetString(
                Key(SeenTipsSuffix),
                JsonUtility.ToJson(state));
            PlayerPrefs.Save();
            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Deletes only this tutorial/version namespace for isolated tests.
        /// </summary>
        public void ResetForTests()
        {
            PlayerPrefs.DeleteKey(Key(CompletedSuffix));
            PlayerPrefs.DeleteKey(Key(SkippedSuffix));
            PlayerPrefs.DeleteKey(Key(ReplaySuffix));
            PlayerPrefs.DeleteKey(Key(SeenTipsSuffix));
            PlayerPrefs.Save();
            seenTipIds = null;
        }
#endif

        private bool ReadFlag(string suffix)
        {
            return PlayerPrefs.GetInt(Key(suffix), 0) == 1;
        }

        private string Key(string suffix)
        {
            return keyPrefix + suffix;
        }

        private HashSet<string> GetSeenTipIds()
        {
            if (seenTipIds != null)
            {
                return seenTipIds;
            }

            var result = new HashSet<string>(StringComparer.Ordinal);
            seenTipIds = result;
            string json = PlayerPrefs.GetString(
                Key(SeenTipsSuffix),
                string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            try
            {
                SeenTipState state =
                    JsonUtility.FromJson<SeenTipState>(json);
                if (state == null || state.ids == null)
                {
                    return result;
                }

                for (int index = 0; index < state.ids.Length; index++)
                {
                    string id = state.ids[index];
                    if (TutorialIdentifier.IsValid(id))
                    {
                        result.Add(id);
                    }
                }
            }
            catch (Exception)
            {
                // Corrupt presentation preferences must never block a run.
            }

            return seenTipIds;
        }

        [Serializable]
        private sealed class SeenTipState
        {
            public string[] ids = Array.Empty<string>();
        }
    }
}
