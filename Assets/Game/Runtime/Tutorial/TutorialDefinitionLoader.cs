using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuleforgeTD.Tutorial
{
    /// <summary>
    /// Loads the localized tutorial document from a Unity TextAsset and
    /// converts string enum names into a validated runtime definition.
    /// </summary>
    public static class TutorialDefinitionLoader
    {
        public static TutorialDefinition LoadKorean()
        {
            TextAsset asset = Resources.Load<TextAsset>(
                TutorialIds.KoreanResourcePath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    "Tutorial resource '" +
                    TutorialIds.KoreanResourcePath +
                    "' could not be loaded.");
            }

            return Load(asset);
        }

        public static TutorialDefinition Load(TextAsset jsonAsset)
        {
            if (jsonAsset == null)
            {
                throw new ArgumentNullException(nameof(jsonAsset));
            }

            return FromJson(jsonAsset.text, jsonAsset.name);
        }

        public static TutorialDefinition FromJson(
            string json,
            string sourceName = "inline tutorial JSON")
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw Invalid(sourceName, "JSON is empty.");
            }

            TutorialDocumentDto source;
            try
            {
                source = JsonUtility.FromJson<TutorialDocumentDto>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Tutorial data '" + sourceName +
                    "' could not be parsed.",
                    exception);
            }

            if (source == null)
            {
                throw Invalid(sourceName, "JSON produced no document.");
            }

            TutorialStepDefinition[] steps = ConvertSteps(
                source.steps,
                sourceName);
            TutorialContextualTipDefinition[] tips = ConvertTips(
                source.contextualTips,
                sourceName);
            var definition = new TutorialDefinition(
                source.schemaVersion,
                Normalize(source.tutorialId),
                source.contentVersion,
                Normalize(source.locale),
                steps,
                tips);
            TutorialDefinitionValidator.Validate(definition, sourceName);
            return definition;
        }

        private static TutorialStepDefinition[] ConvertSteps(
            TutorialStepDto[] source,
            string sourceName)
        {
            if (source == null)
            {
                return Array.Empty<TutorialStepDefinition>();
            }

            var result = new TutorialStepDefinition[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                TutorialStepDto step = source[index];
                if (step == null)
                {
                    throw Invalid(
                        sourceName,
                        "Step at index " + index + " is null.");
                }

                result[index] = new TutorialStepDefinition(
                    Normalize(step.id),
                    step.chapter,
                    step.order,
                    NormalizeCopy(step.title),
                    NormalizeCopy(step.body),
                    ConvertAnchors(
                        step.anchors,
                        sourceName,
                        "step '" + step.id + "'"),
                    ParseEnum<TutorialCompletion>(
                        step.completion,
                        sourceName,
                        "step '" + step.id + "' completion"),
                    Normalize(step.completionTargetId),
                    ConvertActionRules(
                        step.allowedActions,
                        sourceName,
                        step.id),
                    step.pauseBattle,
                    step.restrictInput);
            }

            return result;
        }

        private static TutorialContextualTipDefinition[] ConvertTips(
            TutorialContextualTipDto[] source,
            string sourceName)
        {
            if (source == null)
            {
                return Array.Empty<TutorialContextualTipDefinition>();
            }

            var result =
                new TutorialContextualTipDefinition[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                TutorialContextualTipDto tip = source[index];
                if (tip == null)
                {
                    throw Invalid(
                        sourceName,
                        "Contextual tip at index " + index +
                        " is null.");
                }

                result[index] = new TutorialContextualTipDefinition(
                    Normalize(tip.id),
                    tip.order,
                    NormalizeCopy(tip.title),
                    NormalizeCopy(tip.body),
                    ParseEnum<TutorialContextTrigger>(
                        tip.trigger,
                        sourceName,
                        "contextual tip '" + tip.id + "' trigger"),
                    Normalize(tip.triggerTargetId),
                    ConvertAnchors(
                        tip.anchors,
                        sourceName,
                        "contextual tip '" + tip.id + "'"),
                    tip.pauseBattle);
            }

            return result;
        }

        private static TutorialAnchor[] ConvertAnchors(
            string[] source,
            string sourceName,
            string owner)
        {
            if (source == null)
            {
                return Array.Empty<TutorialAnchor>();
            }

            var result = new TutorialAnchor[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                result[index] = ParseEnum<TutorialAnchor>(
                    source[index],
                    sourceName,
                    owner + " anchor at index " + index);
            }

            return result;
        }

        private static TutorialActionRule[] ConvertActionRules(
            TutorialActionRuleDto[] source,
            string sourceName,
            string stepId)
        {
            if (source == null)
            {
                return Array.Empty<TutorialActionRule>();
            }

            var result = new TutorialActionRule[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                TutorialActionRuleDto rule = source[index];
                if (rule == null)
                {
                    throw Invalid(
                        sourceName,
                        "Action rule at index " + index +
                        " for step '" + stepId + "' is null.");
                }

                result[index] = new TutorialActionRule(
                    ParseEnum<TutorialAction>(
                        rule.action,
                        sourceName,
                        "action rule for step '" + stepId + "'"),
                    Normalize(rule.targetId));
            }

            return result;
        }

        private static T ParseEnum<T>(
            string raw,
            string sourceName,
            string fieldName)
            where T : struct
        {
            if (string.IsNullOrWhiteSpace(raw) ||
                !Enum.TryParse(raw.Trim(), false, out T parsed) ||
                !Enum.IsDefined(typeof(T), parsed))
            {
                throw Invalid(
                    sourceName,
                    fieldName + " has unknown value '" + raw + "'.");
            }

            return parsed;
        }

        private static string Normalize(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        private static string NormalizeCopy(string value)
        {
            return value == null
                ? string.Empty
                : value.Trim().Replace("\\n", "\n");
        }

        private static InvalidOperationException Invalid(
            string sourceName,
            string reason)
        {
            return new InvalidOperationException(
                "Tutorial data '" + sourceName + "' is invalid: " +
                reason);
        }

        [Serializable]
        private sealed class TutorialDocumentDto
        {
            public int schemaVersion;
            public string tutorialId;
            public int contentVersion;
            public string locale;
            public TutorialStepDto[] steps;
            public TutorialContextualTipDto[] contextualTips;
        }

        [Serializable]
        private sealed class TutorialStepDto
        {
            public string id;
            public int chapter;
            public int order;
            public string title;
            public string body;
            public string[] anchors;
            public string completion;
            public string completionTargetId;
            public TutorialActionRuleDto[] allowedActions;
            public bool pauseBattle;
            public bool restrictInput;
        }

        [Serializable]
        private sealed class TutorialActionRuleDto
        {
            public string action;
            public string targetId;
        }

        [Serializable]
        private sealed class TutorialContextualTipDto
        {
            public string id;
            public int order;
            public string title;
            public string body;
            public string trigger;
            public string triggerTargetId;
            public string[] anchors;
            public bool pauseBattle;
        }
    }

    public static class TutorialDefinitionValidator
    {
        public static void Validate(
            TutorialDefinition definition,
            string sourceName = "tutorial definition")
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (definition.SchemaVersion <= 0)
            {
                throw Invalid(sourceName, "schemaVersion must be positive.");
            }
            if (definition.ContentVersion <= 0)
            {
                throw Invalid(
                    sourceName,
                    "contentVersion must be positive.");
            }
            if (!TutorialIdentifier.IsValid(definition.TutorialId))
            {
                throw Invalid(
                    sourceName,
                    "tutorialId is not a stable identifier.");
            }
            if (string.IsNullOrWhiteSpace(definition.Locale))
            {
                throw Invalid(sourceName, "locale is empty.");
            }
            if (definition.Steps.Count == 0)
            {
                throw Invalid(sourceName, "at least one step is required.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var orders = new HashSet<int>();
            int previousOrder = int.MinValue;
            int previousChapter = 0;
            for (int index = 0; index < definition.Steps.Count; index++)
            {
                TutorialStepDefinition step = definition.Steps[index];
                if (step == null)
                {
                    throw Invalid(
                        sourceName,
                        "step at index " + index + " is null.");
                }
                ValidateIdAndOrder(
                    step.Id,
                    step.Order,
                    "step",
                    sourceName,
                    ids,
                    orders);
                if (step.Order <= previousOrder)
                {
                    throw Invalid(
                        sourceName,
                        "steps must be sorted by ascending unique order.");
                }
                if (step.Chapter <= 0 || step.Chapter < previousChapter)
                {
                    throw Invalid(
                        sourceName,
                        "step '" + step.Id +
                        "' has an invalid chapter sequence.");
                }
                ValidateCopy(
                    step.Title,
                    step.Body,
                    "step '" + step.Id + "'",
                    sourceName);
                ValidateAnchors(
                    step.Anchors,
                    "step '" + step.Id + "'",
                    sourceName);
                ValidateActionRules(step, sourceName);
                if (!Enum.IsDefined(
                        typeof(TutorialCompletion),
                        step.Completion))
                {
                    throw Invalid(
                        sourceName,
                        "step '" + step.Id +
                        "' has an invalid completion.");
                }
                if (!string.IsNullOrEmpty(step.CompletionTargetId) &&
                    !TutorialIdentifier.IsValid(
                        step.CompletionTargetId))
                {
                    throw Invalid(
                        sourceName,
                        "step '" + step.Id +
                        "' has an invalid completionTargetId.");
                }

                previousOrder = step.Order;
                previousChapter = step.Chapter;
            }

            previousOrder = int.MinValue;
            for (int index = 0;
                 index < definition.ContextualTips.Count;
                 index++)
            {
                TutorialContextualTipDefinition tip =
                    definition.ContextualTips[index];
                if (tip == null)
                {
                    throw Invalid(
                        sourceName,
                        "contextual tip at index " + index +
                        " is null.");
                }
                ValidateIdAndOrder(
                    tip.Id,
                    tip.Order,
                    "contextual tip",
                    sourceName,
                    ids,
                    orders);
                if (tip.Order <= previousOrder)
                {
                    throw Invalid(
                        sourceName,
                        "contextual tips must be sorted by ascending " +
                        "unique order.");
                }
                ValidateCopy(
                    tip.Title,
                    tip.Body,
                    "contextual tip '" + tip.Id + "'",
                    sourceName);
                ValidateAnchors(
                    tip.Anchors,
                    "contextual tip '" + tip.Id + "'",
                    sourceName);
                if (tip.Trigger == TutorialContextTrigger.None ||
                    !Enum.IsDefined(
                        typeof(TutorialContextTrigger),
                        tip.Trigger))
                {
                    throw Invalid(
                        sourceName,
                        "contextual tip '" + tip.Id +
                        "' has an invalid trigger.");
                }
                if (!string.IsNullOrEmpty(tip.TriggerTargetId) &&
                    !TutorialIdentifier.IsValid(tip.TriggerTargetId))
                {
                    throw Invalid(
                        sourceName,
                        "contextual tip '" + tip.Id +
                        "' has an invalid triggerTargetId.");
                }

                previousOrder = tip.Order;
            }

            ValidateCoreContract(definition, sourceName);
        }

        private static void ValidateIdAndOrder(
            string id,
            int order,
            string kind,
            string sourceName,
            HashSet<string> ids,
            HashSet<int> orders)
        {
            if (!TutorialIdentifier.IsValid(id))
            {
                throw Invalid(
                    sourceName,
                    kind + " id '" + id +
                    "' is not a stable identifier.");
            }
            if (!ids.Add(id))
            {
                throw Invalid(
                    sourceName,
                    "duplicate tutorial id '" + id + "'.");
            }
            if (order <= 0 || !orders.Add(order))
            {
                throw Invalid(
                    sourceName,
                    kind + " '" + id +
                    "' has a missing or duplicate order.");
            }
        }

        private static void ValidateCopy(
            string title,
            string body,
            string owner,
            string sourceName)
        {
            if (string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(body))
            {
                throw Invalid(
                    sourceName,
                    owner + " must define non-empty title and body copy.");
            }
        }

        private static void ValidateAnchors(
            IReadOnlyList<TutorialAnchor> anchors,
            string owner,
            string sourceName)
        {
            if (anchors == null || anchors.Count == 0)
            {
                throw Invalid(
                    sourceName,
                    owner + " must define at least one anchor.");
            }

            var seen = new HashSet<TutorialAnchor>();
            for (int index = 0; index < anchors.Count; index++)
            {
                TutorialAnchor anchor = anchors[index];
                if (anchor == TutorialAnchor.None ||
                    !Enum.IsDefined(typeof(TutorialAnchor), anchor) ||
                    !seen.Add(anchor))
                {
                    throw Invalid(
                        sourceName,
                        owner + " has an invalid or duplicate anchor.");
                }
            }
        }

        private static void ValidateActionRules(
            TutorialStepDefinition step,
            string sourceName)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool hasSkip = false;
            bool hasPurposefulAction = false;
            for (int index = 0;
                 index < step.AllowedActions.Count;
                 index++)
            {
                TutorialActionRule rule = step.AllowedActions[index];
                if (rule == null ||
                    rule.Action == TutorialAction.None ||
                    !Enum.IsDefined(
                        typeof(TutorialAction),
                        rule.Action))
                {
                    throw Invalid(
                        sourceName,
                        "step '" + step.Id +
                        "' has an invalid allowed action.");
                }
                if (!string.IsNullOrEmpty(rule.TargetId) &&
                    !TutorialIdentifier.IsValid(rule.TargetId))
                {
                    throw Invalid(
                        sourceName,
                        "step '" + step.Id +
                        "' has an invalid action target id.");
                }

                string identity = rule.Action + ":" + rule.TargetId;
                if (!seen.Add(identity))
                {
                    throw Invalid(
                        sourceName,
                        "step '" + step.Id +
                        "' has a duplicate allowed action rule.");
                }

                hasSkip |= rule.Action == TutorialAction.SkipTutorial;
                hasPurposefulAction |=
                    rule.Action != TutorialAction.SkipTutorial;
            }

            if (step.RestrictInput &&
                (!hasSkip || !hasPurposefulAction))
            {
                throw Invalid(
                    sourceName,
                    "restricted step '" + step.Id +
                    "' must allow SkipTutorial and a goal action.");
            }
        }

        private static void ValidateCoreContract(
            TutorialDefinition definition,
            string sourceName)
        {
            if (!string.Equals(
                    definition.TutorialId,
                    TutorialIds.CoreTutorialId,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (definition.SchemaVersion != TutorialIds.SchemaVersion ||
                definition.ContentVersion !=
                    TutorialIds.CurrentContentVersion ||
                !string.Equals(
                    definition.Locale,
                    TutorialIds.KoreanLocale,
                    StringComparison.Ordinal))
            {
                throw Invalid(
                    sourceName,
                    "core tutorial schema, content version or locale does " +
                    "not match TutorialIds.");
            }
            if (definition.Steps.Count !=
                TutorialIds.CoreStepIds.Count)
            {
                throw Invalid(
                    sourceName,
                    "core tutorial must define exactly " +
                    TutorialIds.CoreStepIds.Count + " event steps.");
            }
            for (int index = 0;
                 index < TutorialIds.CoreStepIds.Count;
                 index++)
            {
                if (!string.Equals(
                        definition.Steps[index].Id,
                        TutorialIds.CoreStepIds[index],
                        StringComparison.Ordinal))
                {
                    throw Invalid(
                        sourceName,
                        "core step at index " + index +
                        " must be '" +
                        TutorialIds.CoreStepIds[index] + "'.");
                }
            }
            if (definition.Steps[definition.Steps.Count - 1].Chapter !=
                TutorialIds.CoreChapterCount)
            {
                throw Invalid(
                    sourceName,
                    "core tutorial must end at chapter " +
                    TutorialIds.CoreChapterCount + ".");
            }
            if (definition.ContextualTips.Count !=
                TutorialIds.ContextualTipIds.Count)
            {
                throw Invalid(
                    sourceName,
                    "core tutorial must define all contextual tip ids.");
            }
            for (int index = 0;
                 index < TutorialIds.ContextualTipIds.Count;
                 index++)
            {
                if (!string.Equals(
                        definition.ContextualTips[index].Id,
                        TutorialIds.ContextualTipIds[index],
                        StringComparison.Ordinal))
                {
                    throw Invalid(
                        sourceName,
                        "contextual tip at index " + index +
                        " must be '" +
                        TutorialIds.ContextualTipIds[index] + "'.");
                }
            }
        }

        private static InvalidOperationException Invalid(
            string sourceName,
            string reason)
        {
            return new InvalidOperationException(
                "Tutorial data '" + sourceName + "' is invalid: " +
                reason);
        }
    }
}
