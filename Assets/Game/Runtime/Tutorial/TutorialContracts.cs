using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuleforgeTD.Tutorial
{
    /// <summary>
    /// UI input events which can advance, or be gated by, a tutorial step.
    /// Values are explicit because tutorial JSON and presentation code share
    /// this contract.
    /// </summary>
    public enum TutorialAction
    {
        None = 0,
        Continue = 1,
        OpenWavePreview = 2,
        SelectBuildSite = 3,
        BuildTower = 4,
        SelectTower = 5,
        OpenTowerLoadout = 6,
        DragCardToSlot = 7,
        AutoEquipCard = 8,
        UnequipCard = 9,
        SetCardTargetProjectile = 10,
        SetCardTargetEnemy = 11,
        ReorderCard = 12,
        CloseTowerLoadout = 13,
        StartWave = 14,
        TogglePause = 15,
        ChangeBattleSpeed = 16,
        SelectEnemy = 17,
        UpgradeTower = 18,
        ChooseDraftReward = 19,
        OpenGuide = 20,
        CloseGuide = 21,
        SkipTutorial = 22
    }

    /// <summary>
    /// Semantic locations that a presentation layer can register and
    /// highlight. The data layer never depends on a scene object.
    /// </summary>
    public enum TutorialAnchor
    {
        None = 0,
        SpawnPoint = 1,
        EnemyPath = 2,
        HomeBase = 3,
        BattleHud = 4,
        WavePreviewButton = 5,
        WavePreviewPanel = 6,
        BuildSite = 7,
        BuiltTower = 8,
        TowerRange = 9,
        TowerActionPanel = 10,
        TowerUpgradeButton = 11,
        TowerLoadoutButton = 12,
        CardInventory = 13,
        CardSlot = 14,
        CardTargetToggle = 15,
        CardExecutionOrder = 16,
        WaveStartButton = 17,
        BattleSpeedControls = 18,
        Enemy = 19,
        EnemyInspectionPanel = 20,
        DraftPanel = 21,
        DraftChoice = 22,
        GuideButton = 23
    }

    /// <summary>
    /// Observable result that completes a core tutorial record.
    /// </summary>
    public enum TutorialCompletion
    {
        Acknowledged = 0,
        WavePreviewOpened = 1,
        TowerBuilt = 2,
        TowerSelected = 3,
        TowerLoadoutOpened = 4,
        CardDraggedToSlot = 5,
        CardTargetCycleCompleted = 6,
        WaveStarted = 7,
        EnemyInspected = 8,
        TowerUpgraded = 9,
        DraftRewardChosen = 10,
        TutorialCompleted = 11,
        ContextTipAcknowledged = 12
    }

    /// <summary>
    /// First-time events which enqueue contextual help after the core lesson.
    /// </summary>
    public enum TutorialContextTrigger
    {
        None = 0,
        SecondTowerBuilt = 1,
        SecondSlotUnlocked = 2,
        ThirdSlotOrComputeUnlocked = 3,
        CombatEditAttempted = 4,
        NewEnemyTypePreviewed = 5,
        EliteEnemyPreviewed = 6,
        BossPreviewed = 7,
        CardPackOpened = 8,
        StatusEffectInspected = 9,
        VictoryReached = 10,
        DefeatReached = 11,
        StageTwoEntered = 12,
        StageThreeEntered = 13
    }

    /// <summary>
    /// Stable identifiers shared by JSON, controllers, tests and PlayerPrefs.
    /// Changing any identifier requires a content-version bump.
    /// </summary>
    public static class TutorialIds
    {
        public const int SchemaVersion = 1;
        public const int CurrentContentVersion = 1;
        public const int CoreChapterCount = 12;
        public const string CoreTutorialId = "ruleforge.core";
        public const string KoreanLocale = "ko-KR";
        public const string KoreanResourcePath = "RuleforgeTD/TutorialKo";

        public static class Steps
        {
            public const string Objective = "core.objective";
            public const string WavePreview = "core.wave_preview";
            public const string TowerBuild = "core.tower_build";
            public const string TowerSelect = "core.tower_select";
            public const string Loadout = "core.loadout";
            public const string CardDrag = "core.card_drag";
            public const string CardTarget = "core.card_target";
            public const string CardOrder = "core.card_order";
            public const string FirstWave = "core.first_wave";
            public const string EnemyInspection =
                "core.enemy_inspection";
            public const string TowerUpgrade = "core.tower_upgrade";
            public const string DraftReward = "core.draft_reward";
            public const string Complete = "core.complete";
        }

        public static class ContextualTips
        {
            public const string SecondTower = "tip.second_tower";
            public const string SecondSlot = "tip.second_slot";
            public const string ThirdSlotAndCompute =
                "tip.third_slot_compute";
            public const string CombatEditLocked =
                "tip.combat_edit_locked";
            public const string NewEnemyType = "tip.new_enemy_type";
            public const string EliteEnemy = "tip.elite_enemy";
            public const string BossEnemy = "tip.boss_enemy";
            public const string CardPack = "tip.card_pack";
            public const string StatusEffect = "tip.status_effect";
            public const string Victory = "tip.victory";
            public const string Defeat = "tip.defeat";
            public const string StageTwo = "tip.stage02";
            public const string StageThree = "tip.stage03";
        }

        private static readonly IReadOnlyList<string> coreStepIds =
            Array.AsReadOnly(new[]
            {
                Steps.Objective,
                Steps.WavePreview,
                Steps.TowerBuild,
                Steps.TowerSelect,
                Steps.Loadout,
                Steps.CardDrag,
                Steps.CardTarget,
                Steps.CardOrder,
                Steps.FirstWave,
                Steps.EnemyInspection,
                Steps.TowerUpgrade,
                Steps.DraftReward,
                Steps.Complete
            });

        private static readonly IReadOnlyList<string> contextualTipIds =
            Array.AsReadOnly(new[]
            {
                ContextualTips.SecondTower,
                ContextualTips.SecondSlot,
                ContextualTips.ThirdSlotAndCompute,
                ContextualTips.CombatEditLocked,
                ContextualTips.NewEnemyType,
                ContextualTips.EliteEnemy,
                ContextualTips.BossEnemy,
                ContextualTips.CardPack,
                ContextualTips.StatusEffect,
                ContextualTips.Victory,
                ContextualTips.Defeat,
                ContextualTips.StageTwo,
                ContextualTips.StageThree
            });

        public static IReadOnlyList<string> CoreStepIds => coreStepIds;

        public static IReadOnlyList<string> ContextualTipIds =>
            contextualTipIds;
    }

    [Serializable]
    public sealed class TutorialActionRule
    {
        [SerializeField] private TutorialAction action;
        [SerializeField] private string targetId = string.Empty;

        internal TutorialActionRule(
            TutorialAction action,
            string targetId)
        {
            this.action = action;
            this.targetId = targetId ?? string.Empty;
        }

        public TutorialAction Action => action;
        public string TargetId => targetId ?? string.Empty;

        public bool Matches(
            TutorialAction candidate,
            string candidateTargetId)
        {
            if (candidate != action)
            {
                return false;
            }

            return string.IsNullOrEmpty(TargetId) ||
                string.Equals(
                    TargetId,
                    candidateTargetId ?? string.Empty,
                    StringComparison.Ordinal);
        }
    }

    [Serializable]
    public sealed class TutorialStepDefinition
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private int chapter;
        [SerializeField] private int order;
        [SerializeField] private string title = string.Empty;
        [SerializeField, TextArea] private string body = string.Empty;
        [SerializeField] private TutorialAnchor[] anchors =
            Array.Empty<TutorialAnchor>();
        [SerializeField] private TutorialCompletion completion;
        [SerializeField] private string completionTargetId = string.Empty;
        [SerializeField] private TutorialActionRule[] allowedActions =
            Array.Empty<TutorialActionRule>();
        [SerializeField] private bool pauseBattle;
        [SerializeField] private bool restrictInput;

        internal TutorialStepDefinition(
            string id,
            int chapter,
            int order,
            string title,
            string body,
            TutorialAnchor[] anchors,
            TutorialCompletion completion,
            string completionTargetId,
            TutorialActionRule[] allowedActions,
            bool pauseBattle,
            bool restrictInput)
        {
            this.id = id ?? string.Empty;
            this.chapter = chapter;
            this.order = order;
            this.title = title ?? string.Empty;
            this.body = body ?? string.Empty;
            this.anchors = anchors == null
                ? Array.Empty<TutorialAnchor>()
                : (TutorialAnchor[])anchors.Clone();
            this.completion = completion;
            this.completionTargetId = completionTargetId ?? string.Empty;
            this.allowedActions = allowedActions == null
                ? Array.Empty<TutorialActionRule>()
                : (TutorialActionRule[])allowedActions.Clone();
            this.pauseBattle = pauseBattle;
            this.restrictInput = restrictInput;
        }

        public string Id => id ?? string.Empty;
        public int Chapter => chapter;
        public int Order => order;
        public string Title => title ?? string.Empty;
        public string Body => body ?? string.Empty;
        public IReadOnlyList<TutorialAnchor> Anchors => anchors;
        public TutorialCompletion Completion => completion;
        public string CompletionTargetId =>
            completionTargetId ?? string.Empty;
        public IReadOnlyList<TutorialActionRule> AllowedActions =>
            allowedActions;
        public bool PauseBattle => pauseBattle;
        public bool RestrictInput => restrictInput;

        /// <summary>
        /// Non-restricting explanation steps allow normal play. Restricting
        /// practice steps only allow rules declared in tutorial JSON.
        /// </summary>
        public bool Allows(
            TutorialAction action,
            string targetId = null)
        {
            if (!restrictInput)
            {
                return true;
            }

            for (int index = 0; index < allowedActions.Length; index++)
            {
                if (allowedActions[index].Matches(action, targetId))
                {
                    return true;
                }
            }

            return false;
        }
    }

    [Serializable]
    public sealed class TutorialContextualTipDefinition
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private int order;
        [SerializeField] private string title = string.Empty;
        [SerializeField, TextArea] private string body = string.Empty;
        [SerializeField] private TutorialContextTrigger trigger;
        [SerializeField] private string triggerTargetId = string.Empty;
        [SerializeField] private TutorialAnchor[] anchors =
            Array.Empty<TutorialAnchor>();
        [SerializeField] private bool pauseBattle;

        internal TutorialContextualTipDefinition(
            string id,
            int order,
            string title,
            string body,
            TutorialContextTrigger trigger,
            string triggerTargetId,
            TutorialAnchor[] anchors,
            bool pauseBattle)
        {
            this.id = id ?? string.Empty;
            this.order = order;
            this.title = title ?? string.Empty;
            this.body = body ?? string.Empty;
            this.trigger = trigger;
            this.triggerTargetId = triggerTargetId ?? string.Empty;
            this.anchors = anchors == null
                ? Array.Empty<TutorialAnchor>()
                : (TutorialAnchor[])anchors.Clone();
            this.pauseBattle = pauseBattle;
        }

        public string Id => id ?? string.Empty;
        public int Order => order;
        public string Title => title ?? string.Empty;
        public string Body => body ?? string.Empty;
        public TutorialContextTrigger Trigger => trigger;
        public string TriggerTargetId => triggerTargetId ?? string.Empty;
        public IReadOnlyList<TutorialAnchor> Anchors => anchors;
        public bool PauseBattle => pauseBattle;
    }

    [Serializable]
    public sealed class TutorialDefinition
    {
        [SerializeField] private int schemaVersion;
        [SerializeField] private string tutorialId = string.Empty;
        [SerializeField] private int contentVersion;
        [SerializeField] private string locale = string.Empty;
        [SerializeField] private TutorialStepDefinition[] steps =
            Array.Empty<TutorialStepDefinition>();
        [SerializeField]
        private TutorialContextualTipDefinition[] contextualTips =
            Array.Empty<TutorialContextualTipDefinition>();

        internal TutorialDefinition(
            int schemaVersion,
            string tutorialId,
            int contentVersion,
            string locale,
            TutorialStepDefinition[] steps,
            TutorialContextualTipDefinition[] contextualTips)
        {
            this.schemaVersion = schemaVersion;
            this.tutorialId = tutorialId ?? string.Empty;
            this.contentVersion = contentVersion;
            this.locale = locale ?? string.Empty;
            this.steps = steps == null
                ? Array.Empty<TutorialStepDefinition>()
                : (TutorialStepDefinition[])steps.Clone();
            this.contextualTips = contextualTips == null
                ? Array.Empty<TutorialContextualTipDefinition>()
                : (TutorialContextualTipDefinition[])contextualTips.Clone();
        }

        public int SchemaVersion => schemaVersion;
        public string TutorialId => tutorialId ?? string.Empty;
        public int ContentVersion => contentVersion;
        public string Locale => locale ?? string.Empty;
        public IReadOnlyList<TutorialStepDefinition> Steps => steps;
        public IReadOnlyList<TutorialContextualTipDefinition>
            ContextualTips => contextualTips;

        public TutorialStepDefinition FindStep(string stepId)
        {
            if (TryFindStep(stepId, out TutorialStepDefinition step))
            {
                return step;
            }

            throw new KeyNotFoundException(
                "Unknown tutorial step id '" + stepId + "'.");
        }

        public bool TryFindStep(
            string stepId,
            out TutorialStepDefinition step)
        {
            for (int index = 0; index < steps.Length; index++)
            {
                if (string.Equals(
                        steps[index].Id,
                        stepId,
                        StringComparison.Ordinal))
                {
                    step = steps[index];
                    return true;
                }
            }

            step = null;
            return false;
        }

        public TutorialContextualTipDefinition FindContextualTip(
            string tipId)
        {
            if (TryFindContextualTip(
                    tipId,
                    out TutorialContextualTipDefinition tip))
            {
                return tip;
            }

            throw new KeyNotFoundException(
                "Unknown tutorial contextual tip id '" + tipId + "'.");
        }

        public bool TryFindContextualTip(
            string tipId,
            out TutorialContextualTipDefinition tip)
        {
            for (int index = 0;
                 index < contextualTips.Length;
                 index++)
            {
                if (string.Equals(
                        contextualTips[index].Id,
                        tipId,
                        StringComparison.Ordinal))
                {
                    tip = contextualTips[index];
                    return true;
                }
            }

            tip = null;
            return false;
        }
    }

    internal static class TutorialIdentifier
    {
        public static bool IsValid(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool valid = character >= 'a' && character <= 'z' ||
                    character >= '0' && character <= '9' ||
                    character == '.' ||
                    character == '_' ||
                    character == '-';
                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }

        public static void ThrowIfInvalid(
            string value,
            string parameterName)
        {
            if (!IsValid(value))
            {
                throw new ArgumentException(
                    "Tutorial identifiers may contain only lowercase " +
                    "ASCII letters, digits, '.', '_' and '-'.",
                    parameterName);
            }
        }
    }
}
