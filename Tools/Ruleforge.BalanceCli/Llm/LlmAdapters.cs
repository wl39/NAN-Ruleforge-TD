using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RuleforgeTD.BalanceCli.Balance;
using RuleforgeTD.BalanceCli.Infrastructure;
using RuleforgeTD.BalanceCli.Policies;
using RuleforgeTD.BalanceCli.Simulation;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.BalanceCli.Llm;

/// <summary>
/// Controls which derived card-evaluation indexes are disclosed to an LLM
/// player. The simulation snapshot and public content knowledge are always
/// available; hidden simulation state is never part of the request.
/// </summary>
public enum LlmPlayerKnowledgeScope
{
    /// <summary>
    /// Selects snapshot-only for Easy, card strength for Medium, and both
    /// strength and synergy indexes for Hard.
    /// </summary>
    Automatic = 0,
    SnapshotOnly = 1,
    StandaloneCardStrength = 2,
    CardStrengthAndSynergy = 3
}

/// <summary>
/// Read-only boundary between the deterministic simulator and an externally
/// supplied text responder. This class owns no network client and can only
/// select an action that the CLI has already declared legal.
/// </summary>
public sealed class LlmPlayerAdapter : IPlayerPolicy
{
    private const int MaximumResponseCharacters = 16 * 1024;

    private readonly string promptText;
    private readonly Func<string, string> responseProvider;
    private readonly LlmPlayerKnowledgeScope knowledgeScope;

    public LlmPlayerAdapter(
        string policyId,
        string policyVersion,
        string promptFilePath,
        Func<string, string> responseProvider,
        LlmPlayerKnowledgeScope knowledgeScope =
            LlmPlayerKnowledgeScope.Automatic)
        : this(
            policyId,
            policyVersion,
            LoadPromptText(promptFilePath),
            responseProvider,
            knowledgeScope,
            promptAlreadyLoaded: true)
    {
    }

    private LlmPlayerAdapter(
        string policyId,
        string policyVersion,
        string promptText,
        Func<string, string> responseProvider,
        LlmPlayerKnowledgeScope knowledgeScope,
        bool promptAlreadyLoaded)
    {
        _ = promptAlreadyLoaded;
        PolicyId = RequireNonBlank(policyId, nameof(policyId));
        PolicyVersion = RequireNonBlank(policyVersion, nameof(policyVersion));
        this.promptText = RequireNonBlank(promptText, nameof(promptText));
        this.responseProvider = responseProvider ??
            throw new ArgumentNullException(nameof(responseProvider));
        if (!Enum.IsDefined(knowledgeScope))
        {
            throw new ArgumentOutOfRangeException(nameof(knowledgeScope));
        }
        this.knowledgeScope = knowledgeScope;
    }

    public string PolicyId { get; }
    public string PolicyVersion { get; }
    public string PromptText => promptText;

    /// <summary>
    /// Creates an adapter from an already-loaded prompt. This is useful for
    /// tests and for hosts that package prompt resources themselves.
    /// </summary>
    public static LlmPlayerAdapter FromPromptText(
        string policyId,
        string policyVersion,
        string promptText,
        Func<string, string> responseProvider,
        LlmPlayerKnowledgeScope knowledgeScope =
            LlmPlayerKnowledgeScope.Automatic)
    {
        return new LlmPlayerAdapter(
            policyId,
            policyVersion,
            promptText,
            responseProvider,
            knowledgeScope,
            promptAlreadyLoaded: true);
    }

    public PolicyDecision Decide(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        LlmPlayerPromptEnvelope envelope =
            BuildPromptEnvelope(snapshot, context);
        string requestText = ComposeRequest(
            promptText,
            JsonSerializer.Serialize(envelope, JsonSupport.Options));
        string responseJson = responseProvider(requestText);
        LlmPlayerSelection selection = ParseAndValidateResponse(
            responseJson,
            context.LegalActions);
        return new PolicyDecision(
            selection.SelectedActionId,
            selection.ReasonCode);
    }

    /// <summary>
    /// Builds the complete public input supplied to the external responder.
    /// Legal actions are copied into a command-free DTO so the model cannot
    /// manufacture or modify a GameCommand payload.
    /// </summary>
    public LlmPlayerPromptEnvelope BuildPromptEnvelope(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.PublicKnowledge);
        ArgumentNullException.ThrowIfNull(context.LegalActions);

        ValidateLegalActions(context.LegalActions);
        LlmPlayerKnowledgeScope resolvedScope = ResolveKnowledgeScope(
            knowledgeScope,
            context.DifficultyId);

        return new LlmPlayerPromptEnvelope
        {
            Difficulty = RequireNonBlank(
                context.DifficultyId,
                nameof(context.DifficultyId)),
            Policy = PolicyId,
            SuppliedChoiceToken = CreateChoiceToken(snapshot, context),
            Snapshot = snapshot,
            PublicKnowledge = context.PublicKnowledge,
            CardStrengthIndex = resolvedScope is
                LlmPlayerKnowledgeScope.StandaloneCardStrength or
                LlmPlayerKnowledgeScope.CardStrengthAndSynergy
                    ? context.CardStrength
                    : null,
            CardSynergyIndex = resolvedScope ==
                LlmPlayerKnowledgeScope.CardStrengthAndSynergy
                    ? context.CardSynergy
                    : null,
            LegalActions = context.LegalActions
                .Select(LlmLegalAction.FromLegalAction)
                .ToArray()
        };
    }

    public string SerializePromptEnvelope(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        return JsonSerializer.Serialize(
            BuildPromptEnvelope(snapshot, context),
            JsonSupport.Options);
    }

    public static LlmPlayerSelection ParseAndValidateResponse(
        string responseJson,
        IReadOnlyList<LegalAction> legalActions)
    {
        ArgumentNullException.ThrowIfNull(legalActions);
        ValidateLegalActions(legalActions);
        ValidateResponseSize(responseJson, MaximumResponseCharacters);

        LlmPlayerSelection selection;
        try
        {
            selection = JsonSerializer.Deserialize<LlmPlayerSelection>(
                    responseJson,
                    JsonSupport.StrictOptions) ??
                throw new InvalidDataException(
                    "LLM player response produced no JSON value.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "LLM player response must be one strict JSON object.",
                exception);
        }

        if (!legalActions.Any(action => string.Equals(
                action.ActionId,
                selection.SelectedActionId,
                StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "LLM player selected unknown or currently illegal actionId '" +
                selection.SelectedActionId + "'.");
        }

        ValidateReasonCode(selection.ReasonCode, "reasonCode");
        if (selection.EvidenceMetrics is { Count: > 8 })
        {
            throw new InvalidDataException(
                "evidenceMetrics may contain at most eight entries.");
        }
        if (selection.EvidenceMetrics != null)
        {
            foreach ((string key, double value) in selection.EvidenceMetrics)
            {
                if (string.IsNullOrWhiteSpace(key) || key.Length > 80)
                {
                    throw new InvalidDataException(
                        "evidenceMetrics keys must contain 1-80 characters.");
                }
                if (!double.IsFinite(value))
                {
                    throw new InvalidDataException(
                        "evidenceMetrics values must be finite numbers.");
                }
            }
        }

        return selection;
    }

    private static LlmPlayerKnowledgeScope ResolveKnowledgeScope(
        LlmPlayerKnowledgeScope configured,
        string difficultyId)
    {
        if (configured != LlmPlayerKnowledgeScope.Automatic)
        {
            return configured;
        }

        if (string.Equals(difficultyId, "medium", StringComparison.OrdinalIgnoreCase))
        {
            return LlmPlayerKnowledgeScope.StandaloneCardStrength;
        }
        if (string.Equals(difficultyId, "hard", StringComparison.OrdinalIgnoreCase))
        {
            return LlmPlayerKnowledgeScope.CardStrengthAndSynergy;
        }
        return LlmPlayerKnowledgeScope.SnapshotOnly;
    }

    private static string CreateChoiceToken(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        // Reading the state rather than advancing it keeps envelope creation a
        // pure operation while still varying the public tie-break token by turn.
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{context.Random.State:x16}-{snapshot.Tick:x16}-{context.Memory.DecisionsInPhase:x8}");
    }

    private static void ValidateLegalActions(
        IReadOnlyList<LegalAction> legalActions)
    {
        if (legalActions.Count == 0)
        {
            throw new InvalidOperationException(
                "An LLM player decision requires at least one legal action.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (LegalAction action in legalActions)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.ActionId))
            {
                throw new InvalidOperationException(
                    "Every legal action must have a non-empty actionId.");
            }
            if (!ids.Add(action.ActionId))
            {
                throw new InvalidOperationException(
                    "Duplicate legal actionId '" + action.ActionId + "'.");
            }
        }
    }

    private static string LoadPromptText(string path)
    {
        string fullPath = Path.GetFullPath(
            RequireNonBlank(path, nameof(path)));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "LLM player prompt file was not found.",
                fullPath);
        }

        return RequireNonBlank(
            File.ReadAllText(fullPath, Encoding.UTF8),
            nameof(path));
    }

    private static string ComposeRequest(string prompt, string inputJson) =>
        prompt.TrimEnd() + "\n\nINPUT_JSON:\n" + inputJson;

    internal static void ValidateReasonCode(string reasonCode, string name)
    {
        if (string.IsNullOrWhiteSpace(reasonCode) || reasonCode.Length > 96)
        {
            throw new InvalidDataException(
                name + " must contain 1-96 characters.");
        }

        foreach (char character in reasonCode)
        {
            if ((character is >= 'A' and <= 'Z') ||
                (character is >= '0' and <= '9') ||
                character == '_')
            {
                continue;
            }
            throw new InvalidDataException(
                name + " must use only A-Z, 0-9, and underscore.");
        }
    }

    internal static void ValidateResponseSize(string text, int maximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException("LLM response cannot be empty.");
        }
        if (text.Length > maximumCharacters)
        {
            throw new InvalidDataException(
                "LLM response exceeded the allowed character limit.");
        }
    }

    private static string RequireNonBlank(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", name);
        }
        return value;
    }
}

public sealed class LlmPlayerPromptEnvelope
{
    public required string Difficulty { get; init; }
    public required string Policy { get; init; }
    public required string SuppliedChoiceToken { get; init; }
    public required SimulationSnapshot Snapshot { get; init; }
    public required PublicGameKnowledge PublicKnowledge { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardStrengthIndex? CardStrengthIndex { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardSynergyIndex? CardSynergyIndex { get; init; }

    public required IReadOnlyList<LlmLegalAction> LegalActions { get; init; }
}

/// <summary>
/// A deliberately command-free view of a legal action. Only ActionId is
/// accepted back from the model.
/// </summary>
public sealed class LlmLegalAction
{
    public required string ActionId { get; init; }
    public required string Kind { get; init; }
    public required string Summary { get; init; }
    public int Cost { get; init; }
    public string CardId { get; init; } = string.Empty;
    public int CardInstanceId { get; init; } = -1;
    public string TowerDefinitionId { get; init; } = string.Empty;
    public int TowerInstanceId { get; init; } = -1;
    public int BuildPointIndex { get; init; } = -1;
    public int SlotIndex { get; init; } = -1;
    public int OtherSlotIndex { get; init; } = -1;
    public string? SubjectType { get; init; }
    public bool SelfHarmRisk { get; init; }
    public int CardTier { get; init; }
    public required IReadOnlyList<string> CardTags { get; init; }
    public required IReadOnlyDictionary<string, string> Metadata { get; init; }

    internal static LlmLegalAction FromLegalAction(LegalAction action)
    {
        return new LlmLegalAction
        {
            ActionId = action.ActionId,
            Kind = action.Kind.ToString(),
            Summary = action.Summary,
            Cost = action.Cost,
            CardId = action.CardId,
            CardInstanceId = action.CardInstanceId,
            TowerDefinitionId = action.TowerDefinitionId,
            TowerInstanceId = action.TowerInstanceId,
            BuildPointIndex = action.BuildPointIndex,
            SlotIndex = action.SlotIndex,
            OtherSlotIndex = action.OtherSlotIndex,
            SubjectType = action.SubjectType?.ToString(),
            SelfHarmRisk = action.SelfHarmRisk,
            CardTier = action.CardTier,
            CardTags = action.CardTags.ToArray(),
            Metadata = action.Metadata
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal)
        };
    }
}

public sealed class LlmPlayerSelection
{
    [JsonRequired]
    public string SelectedActionId { get; set; } = string.Empty;

    [JsonRequired]
    public string ReasonCode { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, double>? EvidenceMetrics { get; set; }
}

/// <summary>
/// Read-only proposal boundary for the AI balance director. It loads a prompt,
/// passes supplied reports to an injected responder, validates the returned
/// JSON, and returns a proposal. It never edits profiles, content, or code.
/// </summary>
public sealed class BalanceDirectorAdapter
{
    private const int MaximumResponseCharacters = 256 * 1024;
    private readonly string promptText;
    private readonly Func<string, string> responseProvider;

    public BalanceDirectorAdapter(
        string promptFilePath,
        Func<string, string> responseProvider)
        : this(
            LoadPromptText(promptFilePath),
            responseProvider,
            promptAlreadyLoaded: true)
    {
    }

    private BalanceDirectorAdapter(
        string promptText,
        Func<string, string> responseProvider,
        bool promptAlreadyLoaded)
    {
        _ = promptAlreadyLoaded;
        if (string.IsNullOrWhiteSpace(promptText))
        {
            throw new ArgumentException(
                "Balance director prompt cannot be empty.",
                nameof(promptText));
        }

        this.promptText = promptText;
        this.responseProvider = responseProvider ??
            throw new ArgumentNullException(nameof(responseProvider));
    }

    public string PromptText => promptText;

    public static BalanceDirectorAdapter FromPromptText(
        string promptText,
        Func<string, string> responseProvider)
    {
        return new BalanceDirectorAdapter(
            promptText,
            responseProvider,
            promptAlreadyLoaded: true);
    }

    public BalanceDirectorProposal Propose(
        object aggregateReport,
        object difficultyTargets,
        IReadOnlyCollection<string> allowedBalanceFields,
        object? beforeAfterReport = null)
    {
        BalanceDirectorPromptEnvelope envelope = BuildPromptEnvelope(
            aggregateReport,
            difficultyTargets,
            allowedBalanceFields,
            beforeAfterReport);
        string requestText = ComposeRequest(
            promptText,
            JsonSerializer.Serialize(envelope, JsonSupport.Options));
        string responseJson = responseProvider(requestText);
        return ParseAndValidateResponse(
            responseJson,
            envelope.AllowedBalanceFields);
    }

    /// <summary>
    /// Requests a proposal, binds trusted metadata, and passes it through the
    /// authoritative patch validator. This method does not apply or write it.
    /// </summary>
    public BalancePatch ProposeValidatedPatch(
        DifficultyProfile source,
        string proposalId,
        object aggregateReport,
        object difficultyTargets,
        object? beforeAfterReport = null,
        BalanceProposalValidator? validator = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        string[] fields = AllowedBalanceFieldSet.DifficultyProfiles
            .JsonPointerPatterns.ToArray();
        BalanceDirectorPromptEnvelope envelope = BuildPromptEnvelope(
            aggregateReport,
            difficultyTargets,
            fields,
            beforeAfterReport);
        string responseJson = responseProvider(ComposeRequest(
            promptText,
            JsonSerializer.Serialize(envelope, JsonSupport.Options)));
        return ParseAndValidatePatch(
            responseJson,
            source,
            proposalId,
            fields,
            validator);
    }

    public BalanceDirectorProposal ProposeFromJson(
        string aggregateReportJson,
        string difficultyTargetsJson,
        IReadOnlyCollection<string> allowedBalanceFields,
        string? beforeAfterReportJson = null)
    {
        BalanceDirectorPromptEnvelope envelope = BuildPromptEnvelopeFromJson(
            aggregateReportJson,
            difficultyTargetsJson,
            allowedBalanceFields,
            beforeAfterReportJson);
        string requestText = ComposeRequest(
            promptText,
            JsonSerializer.Serialize(envelope, JsonSupport.Options));
        string responseJson = responseProvider(requestText);
        return ParseAndValidateResponse(
            responseJson,
            envelope.AllowedBalanceFields);
    }

    public static BalanceDirectorPromptEnvelope BuildPromptEnvelope(
        object aggregateReport,
        object difficultyTargets,
        IReadOnlyCollection<string> allowedBalanceFields,
        object? beforeAfterReport = null)
    {
        ArgumentNullException.ThrowIfNull(aggregateReport);
        ArgumentNullException.ThrowIfNull(difficultyTargets);
        return new BalanceDirectorPromptEnvelope
        {
            AggregateReport = ToJsonElement(aggregateReport),
            BeforeAfterReport = beforeAfterReport == null
                ? null
                : ToJsonElement(beforeAfterReport),
            DifficultyTargets = ToJsonElement(difficultyTargets),
            AllowedBalanceFields = NormalizeAllowedFields(
                allowedBalanceFields)
        };
    }

    public static BalanceDirectorPromptEnvelope BuildPromptEnvelopeFromJson(
        string aggregateReportJson,
        string difficultyTargetsJson,
        IReadOnlyCollection<string> allowedBalanceFields,
        string? beforeAfterReportJson = null)
    {
        return new BalanceDirectorPromptEnvelope
        {
            AggregateReport = ParseJsonElement(
                aggregateReportJson,
                nameof(aggregateReportJson)),
            BeforeAfterReport = string.IsNullOrWhiteSpace(beforeAfterReportJson)
                ? null
                : ParseJsonElement(
                    beforeAfterReportJson,
                    nameof(beforeAfterReportJson)),
            DifficultyTargets = ParseJsonElement(
                difficultyTargetsJson,
                nameof(difficultyTargetsJson)),
            AllowedBalanceFields = NormalizeAllowedFields(
                allowedBalanceFields)
        };
    }

    public static BalanceDirectorProposal ParseAndValidateResponse(
        string responseJson,
        IReadOnlyCollection<string> allowedBalanceFields)
    {
        string[] allowedFields = NormalizeAllowedFields(allowedBalanceFields);
        LlmPlayerAdapter.ValidateResponseSize(
            responseJson,
            MaximumResponseCharacters);

        BalanceDirectorProposal proposal;
        try
        {
            proposal = JsonSerializer.Deserialize<BalanceDirectorProposal>(
                    responseJson,
                    JsonSupport.StrictOptions) ??
                throw new InvalidDataException(
                    "Balance director response produced no JSON value.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Balance director response must be one strict JSON object.",
                exception);
        }

        ValidateProposal(proposal, allowedFields);
        return proposal;
    }

    public static BalancePatch ParseAndValidatePatch(
        string responseJson,
        DifficultyProfile source,
        string proposalId,
        IReadOnlyCollection<string>? allowedBalanceFields = null,
        BalanceProposalValidator? validator = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(proposalId))
        {
            throw new ArgumentException("proposalId cannot be empty.", nameof(proposalId));
        }

        string[] fields = (allowedBalanceFields ??
                AllowedBalanceFieldSet.DifficultyProfiles.JsonPointerPatterns)
            .ToArray();
        BalanceDirectorProposal proposal = ParseAndValidateResponse(
            responseJson,
            fields);
        if (!string.Equals(
                proposal.Difficulty,
                source.DifficultyId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Proposal difficulty must match the trusted source profile.");
        }

        var patch = new BalancePatch
        {
            SchemaVersion = 1,
            ProposalId = proposalId,
            Difficulty = proposal.Difficulty,
            SourceProfileHash = BalanceProfileHasher.Compute(source),
            Diagnosis = proposal.Diagnosis,
            Changes = proposal.Changes,
            ExpectedEffects = proposal.ExpectedEffects,
            Risks = proposal.Risks,
            NeedsStructuralReview = proposal.NeedsStructuralReview
        };
        BalancePatchValidationResult validation =
            (validator ?? new BalanceProposalValidator()).Validate(source, patch);
        if (!validation.IsValid)
        {
            throw new BalancePatchValidationException(validation);
        }
        return patch;
    }

    private static void ValidateProposal(
        BalanceDirectorProposal proposal,
        IReadOnlyCollection<string> allowedFields)
    {
        if (!IsAllowedDifficulty(proposal.Difficulty))
        {
            throw new InvalidDataException(
                "difficulty must be easy, medium, hard, or global.");
        }
        if (proposal.Diagnosis == null || proposal.Changes == null ||
            proposal.ExpectedEffects == null || proposal.Risks == null)
        {
            throw new InvalidDataException(
                "diagnosis, changes, expectedEffects, and risks are required arrays.");
        }
        if (proposal.Changes.Count > 5)
        {
            throw new InvalidDataException(
                "A balance proposal may change at most five parameters.");
        }

        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (BalanceChange change in proposal.Changes)
        {
            if (change == null ||
                string.IsNullOrWhiteSpace(change.JsonPointer) ||
                !allowedFields.Any(pattern => JsonPointerPatternMatches(
                    pattern,
                    change.JsonPointer)))
            {
                throw new InvalidDataException(
                    "Change targets disallowed jsonPointer '" +
                    change?.JsonPointer + "'.");
            }
            if (!changed.Add(change.JsonPointer))
            {
                throw new InvalidDataException(
                    "A proposal cannot change jsonPointer '" +
                    change.JsonPointer + "' more than once.");
            }
            if (change.OldValue == change.NewValue)
            {
                throw new InvalidDataException(
                    "A balance change must alter its oldValue.");
            }
            LlmPlayerAdapter.ValidateReasonCode(
                change.ReasonCode,
                "changes.reasonCode");
        }

        foreach (BalanceDiagnosis diagnosis in proposal.Diagnosis)
        {
            if (diagnosis == null ||
                string.IsNullOrWhiteSpace(diagnosis.Metric) ||
                string.IsNullOrWhiteSpace(diagnosis.Target) ||
                string.IsNullOrWhiteSpace(diagnosis.Evidence) ||
                !double.IsFinite(diagnosis.Actual))
            {
                throw new InvalidDataException(
                    "Every diagnosis requires a metric, finite actual value, " +
                    "target, and evidence.");
            }
        }

        foreach (ExpectedBalanceEffect effect in proposal.ExpectedEffects)
        {
            if (effect == null ||
                string.IsNullOrWhiteSpace(effect.Metric) ||
                !Enum.IsDefined(effect.Direction))
            {
                throw new InvalidDataException(
                    "Every expected effect requires a metric and valid direction.");
            }
        }

        if (proposal.Risks.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException(
                "risks cannot contain empty strings.");
        }
    }

    private static bool IsAllowedDifficulty(string difficulty)
    {
        return string.Equals(difficulty, "easy", StringComparison.Ordinal) ||
            string.Equals(difficulty, "medium", StringComparison.Ordinal) ||
            string.Equals(difficulty, "hard", StringComparison.Ordinal) ||
            string.Equals(difficulty, "global", StringComparison.Ordinal);
    }

    private static bool JsonPointerPatternMatches(
        string pattern,
        string pointer)
    {
        string[] expected = SplitJsonPointer(pattern);
        string[] actual = SplitJsonPointer(pointer);
        if (expected.Length == 0 || expected.Length != actual.Length)
        {
            return false;
        }

        for (int index = 0; index < expected.Length; index++)
        {
            if (expected[index] != "*" &&
                !string.Equals(
                    expected[index],
                    actual[index],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private static string[] SplitJsonPointer(string pointer)
    {
        if (string.IsNullOrEmpty(pointer) || pointer[0] != '/')
        {
            return Array.Empty<string>();
        }
        return pointer.Split('/')
            .Skip(1)
            .Select(segment => segment
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal))
            .ToArray();
    }

    private static JsonElement ToJsonElement(object value)
    {
        return value switch
        {
            JsonElement element => element.Clone(),
            JsonDocument document => document.RootElement.Clone(),
            _ => JsonSerializer.SerializeToElement(
                value,
                value.GetType(),
                JsonSupport.Options)
        };
    }

    private static JsonElement ParseJsonElement(string json, string name)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("JSON cannot be empty.", name);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow
                });
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(name + " is not valid JSON.", exception);
        }
    }

    private static string[] NormalizeAllowedFields(
        IReadOnlyCollection<string> allowedBalanceFields)
    {
        ArgumentNullException.ThrowIfNull(allowedBalanceFields);
        string[] fields = allowedBalanceFields
            .Select(field => field?.Trim() ?? string.Empty)
            .OrderBy(field => field, StringComparer.Ordinal)
            .ToArray();
        if (fields.Length == 0 || fields.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "At least one non-empty allowed balance field is required.",
                nameof(allowedBalanceFields));
        }
        if (fields.Distinct(StringComparer.Ordinal).Count() != fields.Length)
        {
            throw new ArgumentException(
                "Allowed balance fields must be unique.",
                nameof(allowedBalanceFields));
        }
        return fields;
    }

    private static string LoadPromptText(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Prompt path cannot be empty.",
                nameof(path));
        }
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Balance director prompt file was not found.",
                fullPath);
        }

        string text = File.ReadAllText(fullPath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException(
                "Balance director prompt file is empty: " + fullPath);
        }
        return text;
    }

    private static string ComposeRequest(string prompt, string inputJson) =>
        prompt.TrimEnd() + "\n\nINPUT_JSON:\n" + inputJson;
}

public sealed class BalanceDirectorPromptEnvelope
{
    public required JsonElement AggregateReport { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? BeforeAfterReport { get; init; }

    public required JsonElement DifficultyTargets { get; init; }
    public required IReadOnlyList<string> AllowedBalanceFields { get; init; }
}

/// <summary>
/// Exact response shape requested by balance-director.md. Applying the
/// proposal remains the responsibility of the separately validated patch
/// pipeline.
/// </summary>
public sealed class BalanceDirectorProposal
{
    [JsonRequired]
    public string Difficulty { get; set; } = string.Empty;

    [JsonRequired]
    public List<BalanceDiagnosis> Diagnosis { get; set; } = new();

    [JsonRequired]
    public List<BalanceChange> Changes { get; set; } = new();

    [JsonRequired]
    public List<ExpectedBalanceEffect> ExpectedEffects { get; set; } = new();

    [JsonRequired]
    public List<string> Risks { get; set; } = new();

    [JsonRequired]
    public bool NeedsStructuralReview { get; set; }
}
