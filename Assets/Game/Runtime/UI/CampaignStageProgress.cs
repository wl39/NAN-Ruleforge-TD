using System;
using UnityEngine;

namespace RuleforgeTD.UI
{
    /// <summary>
    /// Small WebGL-safe campaign progression boundary. PlayerPrefs is backed
    /// by browser storage in WebGL, while battle simulation stays independent
    /// from platform persistence.
    /// </summary>
    public static class CampaignStageProgress
    {
        public const int DisplayedStageCount = 15;
        public const int DemoStageCount = 3;

        private const string HighestUnlockedKey =
            "ruleforge.campaign.highest_unlocked";
        private const string ClearedKeyPrefix =
            "ruleforge.campaign.cleared.";

        public static int HighestUnlockedStage => Mathf.Clamp(
            PlayerPrefs.GetInt(HighestUnlockedKey, 1),
            1,
            DemoStageCount);

        public static bool IsUnlocked(int stageNumber)
        {
            return stageNumber >= 1 &&
                   stageNumber <= DemoStageCount &&
                   stageNumber <= HighestUnlockedStage;
        }

        public static bool IsCleared(int stageNumber)
        {
            return stageNumber >= 1 &&
                   stageNumber <= DemoStageCount &&
                   PlayerPrefs.GetInt(
                       ClearedKeyPrefix + stageNumber,
                       0) == 1;
        }

        public static void MarkSceneCompleted(string sceneName)
        {
            if (TryGetStageNumber(sceneName, out int stageNumber))
            {
                MarkStageCompleted(stageNumber);
            }
        }

        public static void MarkStageCompleted(int stageNumber)
        {
            if (stageNumber < 1 || stageNumber > DemoStageCount)
            {
                return;
            }

            PlayerPrefs.SetInt(ClearedKeyPrefix + stageNumber, 1);
            int nextUnlocked = Mathf.Min(
                DemoStageCount,
                stageNumber + 1);
            if (nextUnlocked > HighestUnlockedStage)
            {
                PlayerPrefs.SetInt(
                    HighestUnlockedKey,
                    nextUnlocked);
            }

            PlayerPrefs.Save();
        }

        public static bool TryGetStageNumber(
            string sceneName,
            out int stageNumber)
        {
            stageNumber = 0;
            if (string.IsNullOrWhiteSpace(sceneName) ||
                !sceneName.StartsWith(
                    "Stage",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string suffix = sceneName.Substring("Stage".Length);
            return int.TryParse(suffix, out stageNumber) &&
                   stageNumber >= 1 &&
                   stageNumber <= DemoStageCount;
        }

#if UNITY_EDITOR
        public static void ResetForTests()
        {
            PlayerPrefs.DeleteKey(HighestUnlockedKey);
            for (int stage = 1; stage <= DemoStageCount; stage++)
            {
                PlayerPrefs.DeleteKey(ClearedKeyPrefix + stage);
            }
        }
#endif
    }
}
