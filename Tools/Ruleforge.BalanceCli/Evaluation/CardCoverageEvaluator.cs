using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RuleforgeTD.BalanceCli.Policies;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.BalanceCli.Evaluation;

public enum CardCoverageClassification
{
    Clearable = 0,
    WeakInTestedContexts = 1,
    NoLegalFixturePath = 2,
    PolicyOrFixtureFailure = 3
}

public sealed class CardCoverageExperimentResult
{
    public string TowerDefinitionId { get; set; } = string.Empty;
    public int TowerLevel { get; set; }
    public SubjectType SubjectType { get; set; }
    public int SlotIndex { get; set; }
    public string CardId { get; set; } = string.Empty;
    public int MatchedSeedCount { get; set; }
    public int InvalidSeedCount { get; set; }
    public int RuntimeFailureSeedCount { get; set; }
    public int RuntimeFailureRunCount { get; set; }
    public int CandidateVictoryCount { get; set; }
    public double CandidateWinRate { get; set; }
    public double CompositeLift { get; set; }
    public bool HasClearablePath { get; set; }
    public List<string> FailureReasons { get; set; } = new();
}

public sealed class CardCoverageEntry
{
    public string CardId { get; set; } = string.Empty;
    public CardCoverageClassification Classification { get; set; }
    public int LegalExperimentCount { get; set; }
    public int ValidExperimentCount { get; set; }
    public int ClearableExperimentCount { get; set; }
    public string BestTowerDefinitionId { get; set; } = string.Empty;
    public int BestTowerLevel { get; set; }
    public SubjectType BestSubjectType { get; set; }
    public int BestSlotIndex { get; set; } = -1;
    public double BestCandidateWinRate { get; set; }
    public double BestCompositeLift { get; set; }
    public List<string> Reasons { get; set; } = new();
    public List<CardCoverageExperimentResult> Experiments { get; set; } = new();
}

public sealed class CardCoverageReport
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string DifficultyId { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public int SeedCount { get; set; }
    public int ActiveCardCount { get; set; }
    public int CardsWithLegalPath { get; set; }
    public int CardsWithClearablePath { get; set; }
    public double LegalPathCoverageRatio { get; set; }
    public double ClearablePathCoverageRatio { get; set; }
    public List<CardCoverageEntry> Cards { get; set; } = new();
    public List<UnsupportedCardExperimentContext> UnsupportedContexts { get; set; } =
        new();
}

/// <summary>
/// Runs the Easy card-coverage matrix on matched seeds and preserves invalid
/// fixtures as evidence. A card is clearable only when at least one legal
/// tower/subject/slot context wins a real authoritative simulation.
/// </summary>
public sealed class CardCoverageEvaluator
{
    private readonly CardLiftScoringWeights weights;

    public CardCoverageEvaluator(CardLiftScoringWeights? weights = null)
    {
        this.weights = weights ?? new CardLiftScoringWeights();
    }

    public async Task<CardCoverageReport> EvaluateAsync(
        CardExperimentEnumeration enumeration,
        IReadOnlyList<SeedPair> seeds,
        CardExperimentRunner runner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enumeration);
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(runner);
        if (seeds.Count == 0)
        {
            throw new ArgumentException(
                "Card coverage requires at least one matched seed pair.",
                nameof(seeds));
        }
        List<SeedPair> orderedSeeds = ValidateAndSortSeeds(seeds);
        var experimentResults = new List<CardCoverageExperimentResult>(
            enumeration.StrengthExperiments.Count);
        var baselineCache = new Dictionary<
            BaselineCacheKey,
            EvaluationRunMetrics>();
        foreach (CardStrengthExperiment experiment in
                 enumeration.StrengthExperiments
                     .OrderBy(value => value.Card.CardId, StringComparer.Ordinal)
                     .ThenBy(value => value.TowerDefinitionId,
                         StringComparer.Ordinal)
                     .ThenBy(value => value.Card.SubjectType)
                     .ThenBy(value => value.Card.SlotIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            experimentResults.Add(await EvaluateExperimentAsync(
                experiment,
                orderedSeeds,
                runner,
                baselineCache,
                cancellationToken).ConfigureAwait(false));
        }

        var report = new CardCoverageReport
        {
            DifficultyId = enumeration.DifficultyId,
            ContentHash = enumeration.ContentHash,
            SeedCount = orderedSeeds.Count,
            ActiveCardCount = enumeration.ActiveCardIds.Count,
            UnsupportedContexts = enumeration.UnsupportedContexts
                .Select(CloneUnsupported)
                .ToList()
        };
        foreach (string cardId in enumeration.ActiveCardIds.OrderBy(
                     value => value,
                     StringComparer.Ordinal))
        {
            List<CardCoverageExperimentResult> cardExperiments =
                experimentResults
                    .Where(value => string.Equals(
                        value.CardId,
                        cardId,
                        StringComparison.Ordinal))
                    .ToList();
            List<UnsupportedCardExperimentContext> cardUnsupported =
                enumeration.UnsupportedContexts.Where(context =>
                    string.IsNullOrEmpty(context.CardId) ||
                    string.Equals(context.CardId, cardId,
                        StringComparison.Ordinal)).ToList();
            report.Cards.Add(BuildEntry(
                cardId,
                cardExperiments,
                cardUnsupported));
        }

        report.CardsWithLegalPath = report.Cards.Count(card =>
            card.LegalExperimentCount > 0);
        report.CardsWithClearablePath = report.Cards.Count(card =>
            card.Classification == CardCoverageClassification.Clearable);
        if (report.ActiveCardCount > 0)
        {
            report.LegalPathCoverageRatio =
                report.CardsWithLegalPath / (double)report.ActiveCardCount;
            report.ClearablePathCoverageRatio =
                report.CardsWithClearablePath / (double)report.ActiveCardCount;
        }
        return report;
    }

    private async Task<CardCoverageExperimentResult> EvaluateExperimentAsync(
        CardStrengthExperiment experiment,
        IReadOnlyList<SeedPair> seeds,
        CardExperimentRunner runner,
        IDictionary<BaselineCacheKey, EvaluationRunMetrics> baselineCache,
        CancellationToken cancellationToken)
    {
        CardProgramStep step = experiment.Card;
        string context = experiment.DifficultyId + ":" +
            experiment.TowerDefinitionId + ":L" + experiment.TowerLevel + ":" +
            step.SubjectType + ":" + step.SlotIndex + ":" + step.CardId;
        var baseline = new CardExperimentVariant(
            experiment.DifficultyId + ":" +
                experiment.TowerDefinitionId + ":L" +
                experiment.TowerLevel + ":coverage-baseline",
            experiment.DifficultyId,
            experiment.TowerDefinitionId,
            experiment.TowerLevel,
            CardExperimentVariantKind.Baseline,
            Array.Empty<CardProgramStep>());
        var candidate = new CardExperimentVariant(
            context + ":candidate",
            experiment.DifficultyId,
            experiment.TowerDefinitionId,
            experiment.TowerLevel,
            CardExperimentVariantKind.Program,
            new[] { step });
        var before = new List<EvaluationRunMetrics>(seeds.Count);
        var after = new List<EvaluationRunMetrics>(seeds.Count);
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        int candidateWins = 0;
        int validPairs = 0;
        foreach (SeedPair seed in seeds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var baselineKey = new BaselineCacheKey(
                experiment.DifficultyId,
                experiment.TowerDefinitionId,
                experiment.TowerLevel,
                seed);
            if (!baselineCache.TryGetValue(
                    baselineKey,
                    out EvaluationRunMetrics? cachedBaseline))
            {
                cachedBaseline = await runner(
                    baseline,
                    seed,
                    cancellationToken).ConfigureAwait(false);
                baselineCache.Add(baselineKey, cachedBaseline);
            }
            EvaluationRunMetrics baselineResult = cachedBaseline ??
                throw new InvalidOperationException(
                    "Card coverage baseline cache returned null.");
            EvaluationRunMetrics candidateResult = await runner(
                candidate,
                seed,
                cancellationToken).ConfigureAwait(false);
            RequireSeed(seed, baselineResult, "baseline", context);
            RequireSeed(seed, candidateResult, "candidate", context);
            before.Add(baselineResult);
            after.Add(candidateResult);
            if (baselineResult.IsValid && candidateResult.IsValid)
            {
                validPairs++;
                candidateWins += candidateResult.Victory &&
                    !candidateResult.IsRuntimeFailure ? 1 : 0;
            }
            else
            {
                AddFailure(reasons, baselineResult.FailureReason, "baseline");
                AddFailure(reasons, candidateResult.FailureReason, "candidate");
            }
            if (baselineResult.IsRuntimeFailure)
            {
                AddFailure(reasons, baselineResult.FailureReason, "baseline");
            }
            if (candidateResult.IsRuntimeFailure)
            {
                AddFailure(reasons, candidateResult.FailureReason, "candidate");
            }
        }

        CardLiftMetrics? lift = validPairs > 0
            ? CardLiftCalculator.Difference(before, after, weights)
            : null;
        return new CardCoverageExperimentResult
        {
            TowerDefinitionId = experiment.TowerDefinitionId,
            TowerLevel = experiment.TowerLevel,
            SubjectType = step.SubjectType,
            SlotIndex = step.SlotIndex,
            CardId = step.CardId,
            MatchedSeedCount = validPairs,
            InvalidSeedCount = seeds.Count - validPairs,
            RuntimeFailureSeedCount = lift?.RuntimeFailureSeedCount ?? 0,
            RuntimeFailureRunCount = lift?.RuntimeFailureRunCount ?? 0,
            CandidateVictoryCount = candidateWins,
            CandidateWinRate = validPairs == 0
                ? 0
                : candidateWins / (double)validPairs,
            CompositeLift = lift?.CompositeScore ?? 0,
            HasClearablePath = candidateWins > 0,
            FailureReasons = reasons.ToList()
        };
    }

    private static CardCoverageEntry BuildEntry(
        string cardId,
        List<CardCoverageExperimentResult> experiments,
        IReadOnlyList<UnsupportedCardExperimentContext> unsupported)
    {
        List<CardCoverageExperimentResult> valid = experiments
            .Where(experiment => experiment.MatchedSeedCount > 0)
            .ToList();
        List<CardCoverageExperimentResult> clearable = valid
            .Where(experiment => experiment.HasClearablePath)
            .ToList();
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        foreach (CardCoverageExperimentResult experiment in experiments)
        {
            foreach (string reason in experiment.FailureReasons)
            {
                reasons.Add(reason);
            }
        }
        foreach (UnsupportedCardExperimentContext context in unsupported)
        {
            reasons.Add(context.ReasonCode + ": " + context.Detail);
        }

        CardCoverageClassification classification;
        if (experiments.Count == 0)
        {
            classification = CardCoverageClassification.NoLegalFixturePath;
            reasons.Add(
                CardExperimentFailureCodes.IllegalCardPlacement +
                ": no legal compiled level-one fixture path was found.");
        }
        else if (valid.Count == 0)
        {
            classification = CardCoverageClassification.PolicyOrFixtureFailure;
            reasons.Add(
                "POLICY_OR_FIXTURE_FAILURE: every matched context was invalid.");
        }
        else if (clearable.Count == 0)
        {
            classification = CardCoverageClassification.WeakInTestedContexts;
            reasons.Add(
                "CARD_WEAK_OR_DIFFICULTY_EXCESSIVE: legal contexts completed " +
                "without a victory on the requested seeds.");
        }
        else
        {
            classification = CardCoverageClassification.Clearable;
        }

        CardCoverageExperimentResult? best = valid
            .OrderByDescending(experiment => experiment.CandidateWinRate)
            .ThenByDescending(experiment => experiment.CompositeLift)
            .ThenBy(experiment => experiment.TowerDefinitionId,
                StringComparer.Ordinal)
            .ThenBy(experiment => experiment.SubjectType)
            .ThenBy(experiment => experiment.SlotIndex)
            .FirstOrDefault();
        return new CardCoverageEntry
        {
            CardId = cardId,
            Classification = classification,
            LegalExperimentCount = experiments.Count,
            ValidExperimentCount = valid.Count,
            ClearableExperimentCount = clearable.Count,
            BestTowerDefinitionId = best?.TowerDefinitionId ?? string.Empty,
            BestTowerLevel = best?.TowerLevel ?? 0,
            BestSubjectType = best?.SubjectType ?? SubjectType.Projectile,
            BestSlotIndex = best?.SlotIndex ?? -1,
            BestCandidateWinRate = best?.CandidateWinRate ?? 0,
            BestCompositeLift = best?.CompositeLift ?? 0,
            Reasons = reasons.ToList(),
            Experiments = experiments
        };
    }

    private static List<SeedPair> ValidateAndSortSeeds(
        IEnumerable<SeedPair> seeds)
    {
        var seen = new HashSet<SeedPair>();
        var result = new List<SeedPair>();
        foreach (SeedPair seed in seeds)
        {
            if (!seen.Add(seed))
            {
                throw new ArgumentException(
                    "Card coverage seed pairs must be unique: " + seed + ".",
                    nameof(seeds));
            }
            result.Add(seed);
        }
        result.Sort((left, right) =>
        {
            int game = left.GameSeed.CompareTo(right.GameSeed);
            return game != 0
                ? game
                : left.PolicySeed.CompareTo(right.PolicySeed);
        });
        return result;
    }

    private static void RequireSeed(
        SeedPair expected,
        EvaluationRunMetrics actual,
        string side,
        string context)
    {
        if (actual.Seed != expected)
        {
            throw new InvalidOperationException(
                "Card coverage " + side + " runner returned seed " +
                actual.Seed + " for expected seed " + expected + " in " +
                context + ".");
        }
    }

    private static void AddFailure(
        ISet<string> reasons,
        string? failure,
        string side)
    {
        if (!string.IsNullOrWhiteSpace(failure))
        {
            reasons.Add(side + ": " + failure);
        }
    }

    private static UnsupportedCardExperimentContext CloneUnsupported(
        UnsupportedCardExperimentContext source) => new()
        {
            ReasonCode = source.ReasonCode,
            Detail = source.Detail,
            CardId = source.CardId,
            TowerDefinitionId = source.TowerDefinitionId,
            TowerLevel = source.TowerLevel,
            SubjectTypes = new List<SubjectType>(source.SubjectTypes),
            SlotIndices = new List<int>(source.SlotIndices)
        };

    private readonly record struct BaselineCacheKey(
        string DifficultyId,
        string TowerDefinitionId,
        int TowerLevel,
        SeedPair Seed);
}
