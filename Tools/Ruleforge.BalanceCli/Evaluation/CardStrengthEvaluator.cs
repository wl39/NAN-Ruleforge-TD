using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RuleforgeTD.BalanceCli.Policies;

namespace RuleforgeTD.BalanceCli.Evaluation;

public sealed record CardStrengthKey(
    string DifficultyId,
    string TowerDefinitionId,
    RuleforgeTD.GameLogic.Core.SubjectType SubjectType,
    int SlotIndex,
    string CardId,
    int TowerLevel);

public sealed class CardStrengthEntry
{
    public string DifficultyId { get; set; } = string.Empty;
    public string TowerDefinitionId { get; set; } = string.Empty;
    public RuleforgeTD.GameLogic.Core.SubjectType SubjectType { get; set; }
    public int SlotIndex { get; set; }
    public string CardId { get; set; } = string.Empty;
    public int TowerLevel { get; set; }
    public CardLiftMetrics Lift { get; set; } = new();
    public bool IsEvaluable { get; set; } = true;
    public string FailureReason { get; set; } = string.Empty;
    public bool IsGoodStandalone { get; set; }

    public CardStrengthKey Key => new(
        DifficultyId,
        TowerDefinitionId,
        SubjectType,
        SlotIndex,
        CardId,
        TowerLevel);
}

public sealed class CardStrengthIndex : ICardStrengthLookup
{
    private IReadOnlyList<CardStrengthEntry>? lookupSource;
    private Dictionary<CardStrengthKey, CardStrengthEntry>? lookup;

    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ContentHash { get; set; } = string.Empty;
    public string SeedSetHash { get; set; } = string.Empty;
    public double GoodPercentile { get; set; } = 0.70;
    public CardLiftScoringWeights Weights { get; set; } = new();
    public List<CardStrengthEntry> Entries { get; set; } = new();

    public double GetScore(in CardStrengthQuery query)
    {
        var key = new CardStrengthKey(
            query.DifficultyId,
            query.TowerDefinitionId,
            query.SubjectType,
            query.SlotIndex,
            query.CardId,
            query.TowerLevel);
        return TryGet(key, out CardStrengthEntry? entry)
            ? entry!.Lift.CompositeScore
            : 0.0;
    }

    public bool TryGet(CardStrengthKey key, out CardStrengthEntry? entry)
    {
        EnsureLookup();
        return lookup!.TryGetValue(key, out entry);
    }

    public RuleforgeTD.BalanceCli.Policies.CardStrengthIndex ToPolicyIndex()
    {
        return new RuleforgeTD.BalanceCli.Policies.CardStrengthIndex
        {
            SchemaVersion = SchemaVersion,
            ContentHash = ContentHash,
            Entries = Entries.Where(entry => entry.IsEvaluable).Select(entry =>
                new RuleforgeTD.BalanceCli.Policies.CardStrengthEntry
                {
                    Difficulty = entry.DifficultyId,
                    TowerDefinition = entry.TowerDefinitionId,
                    SubjectType = entry.SubjectType,
                    SlotIndex = entry.SlotIndex,
                    CardId = entry.CardId,
                    TowerLevel = entry.TowerLevel,
                    SampleSize = entry.Lift.MatchedSeedCount,
                    BaselineWinRate = entry.Lift.BaselineWinRate,
                    CardWinRate = entry.Lift.CandidateWinRate,
                    WinRateLift = entry.Lift.WinRateLift,
                    RemainingHealthLift =
                        entry.Lift.MeanRemainingHealthLift,
                    ClearedWaveLift = entry.Lift.MeanClearedWaveLift,
                    LeakReduction = entry.Lift.MeanLeakReduction,
                    GoldEfficiencyLift =
                        entry.Lift.MeanGoldEfficiencyLift,
                    CompositeLift = entry.Lift.CompositeScore,
                    ViablePath = entry.IsGoodStandalone
                }).ToList()
        };
    }

    public void Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion != 1)
        {
            errors.Add("schemaVersion must be 1.");
        }
        if (GoodPercentile < 0 || GoodPercentile > 1)
        {
            errors.Add("goodPercentile must be between 0 and 1.");
        }

        var keys = new HashSet<CardStrengthKey>();
        for (int index = 0; index < Entries.Count; index++)
        {
            CardStrengthEntry entry = Entries[index];
            if (string.IsNullOrWhiteSpace(entry.DifficultyId) ||
                string.IsNullOrWhiteSpace(entry.TowerDefinitionId) ||
                string.IsNullOrWhiteSpace(entry.CardId))
            {
                errors.Add("entries[" + index + "] has an empty context id.");
            }
            if (entry.SlotIndex < 0 || entry.TowerLevel < 1)
            {
                errors.Add("entries[" + index + "] has an invalid slot or level.");
            }
            if (!entry.IsEvaluable && string.IsNullOrWhiteSpace(
                    entry.FailureReason))
            {
                errors.Add(
                    "entries[" + index +
                    "] must explain why the context is not evaluable.");
            }
            if (!keys.Add(entry.Key))
            {
                errors.Add("Duplicate card strength key at entries[" + index + "].");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Invalid card strength index:\n" + string.Join("\n", errors));
        }
        lookup = null;
        lookupSource = null;
    }

    private void EnsureLookup()
    {
        Entries ??= new List<CardStrengthEntry>();
        if (lookup != null &&
            ReferenceEquals(lookupSource, Entries) &&
            lookup.Count == Entries.Count)
        {
            return;
        }

        lookup = new Dictionary<CardStrengthKey, CardStrengthEntry>();
        foreach (CardStrengthEntry entry in Entries)
        {
            if (!lookup.TryAdd(entry.Key, entry))
            {
                throw new InvalidOperationException(
                    "Card strength index contains duplicate key: " + entry.Key);
            }
        }
        lookupSource = Entries;
    }
}

public sealed record CardStrengthExperiment(
    string DifficultyId,
    string TowerDefinitionId,
    int TowerLevel,
    CardProgramStep Card);

public sealed class CardStrengthEvaluator
{
    private readonly CardLiftScoringWeights weights;
    private readonly double goodPercentile;

    public CardStrengthEvaluator(
        CardLiftScoringWeights? weights = null,
        double goodPercentile = 0.70)
    {
        if (goodPercentile < 0 || goodPercentile > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(goodPercentile));
        }
        this.weights = weights ?? new CardLiftScoringWeights();
        this.goodPercentile = goodPercentile;
    }

    public async Task<CardStrengthIndex> EvaluateAsync(
        IReadOnlyList<CardStrengthExperiment> experiments,
        IReadOnlyList<SeedPair> seeds,
        CardExperimentRunner runner,
        string contentHash = "",
        string seedSetHash = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiments);
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(runner);
        if (experiments.Count == 0)
        {
            throw new ArgumentException(
                "At least one card strength experiment is required.",
                nameof(experiments));
        }
        if (seeds.Count == 0)
        {
            throw new ArgumentException(
                "At least one seed pair is required.",
                nameof(seeds));
        }
        var entries = new List<CardStrengthEntry>(experiments.Count);
        var cache = new Dictionary<string, IReadOnlyList<EvaluationRunMetrics>>(
            StringComparer.Ordinal);
        foreach (CardStrengthExperiment experiment in experiments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateExperiment(experiment);
            var baseline = new CardExperimentVariant(
                BuildVariantId(experiment, "baseline"),
                experiment.DifficultyId,
                experiment.TowerDefinitionId,
                experiment.TowerLevel,
                CardExperimentVariantKind.Baseline,
                Array.Empty<CardProgramStep>());
            var candidate = new CardExperimentVariant(
                BuildVariantId(experiment, "card"),
                experiment.DifficultyId,
                experiment.TowerDefinitionId,
                experiment.TowerLevel,
                CardExperimentVariantKind.Program,
                new[] { experiment.Card });

            IReadOnlyList<EvaluationRunMetrics> before = await RunAllAsync(
                baseline,
                seeds,
                runner,
                cache,
                cancellationToken).ConfigureAwait(false);
            IReadOnlyList<EvaluationRunMetrics> after = await RunAllAsync(
                candidate,
                seeds,
                runner,
                cache,
                cancellationToken).ConfigureAwait(false);
            MatchedEvaluationRows matched = SelectValidMatchedRows(
                before,
                after);
            entries.Add(new CardStrengthEntry
            {
                DifficultyId = experiment.DifficultyId,
                TowerDefinitionId = experiment.TowerDefinitionId,
                SubjectType = experiment.Card.SubjectType,
                SlotIndex = experiment.Card.SlotIndex,
                CardId = experiment.Card.CardId,
                TowerLevel = experiment.TowerLevel,
                Lift = matched.Before.Count == 0
                    ? new CardLiftMetrics
                    {
                        InvalidRunCount = seeds.Count
                    }
                    : WithInvalidCount(
                        CardLiftCalculator.Difference(
                            matched.Before,
                            matched.After,
                            weights),
                        matched.InvalidCount),
                IsEvaluable = matched.Before.Count > 0,
                FailureReason = matched.Before.Count == 0
                    ? SummarizeFailures(before, after)
                    : string.Empty
            });
        }

        MarkGoodStandalone(entries, goodPercentile);
        var index = new CardStrengthIndex
        {
            ContentHash = contentHash,
            SeedSetHash = seedSetHash,
            GoodPercentile = goodPercentile,
            Weights = weights,
            Entries = entries
        };
        index.Validate();
        return index;
    }

    private static async Task<IReadOnlyList<EvaluationRunMetrics>> RunAllAsync(
        CardExperimentVariant variant,
        IReadOnlyList<SeedPair> seeds,
        CardExperimentRunner runner,
        IDictionary<string, IReadOnlyList<EvaluationRunMetrics>> cache,
        CancellationToken cancellationToken)
    {
        string cacheKey = BuildCacheKey(variant);
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<EvaluationRunMetrics>? found))
        {
            return found;
        }

        var results = new List<EvaluationRunMetrics>(seeds.Count);
        foreach (SeedPair seed in seeds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EvaluationRunMetrics result = await runner(
                variant,
                seed,
                cancellationToken).ConfigureAwait(false);
            if (result.Seed != seed)
            {
                throw new InvalidOperationException(
                    "Card experiment runner returned a different seed for " +
                    variant.VariantId + ".");
            }
            results.Add(result);
        }
        cache.Add(cacheKey, results);
        return results;
    }

    internal static string BuildCacheKey(CardExperimentVariant variant)
    {
        string program = string.Join(
            ">",
            variant.OrderedProgram.Select(step =>
                step.SlotIndex + ":" + step.SubjectType + ":" + step.CardId));
        return variant.DifficultyId + "|" + variant.TowerDefinitionId + "|" +
            variant.TowerLevel + "|" + variant.Kind + "|" + program;
    }

    private static string BuildVariantId(
        CardStrengthExperiment experiment,
        string suffix) =>
        experiment.DifficultyId + ":" + experiment.TowerDefinitionId + ":L" +
        experiment.TowerLevel + ":" + experiment.Card.SlotIndex + ":" +
        experiment.Card.SubjectType + ":" + experiment.Card.CardId + ":" + suffix;

    private static void ValidateExperiment(CardStrengthExperiment experiment)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        if (string.IsNullOrWhiteSpace(experiment.DifficultyId) ||
            string.IsNullOrWhiteSpace(experiment.TowerDefinitionId) ||
            string.IsNullOrWhiteSpace(experiment.Card.CardId))
        {
            throw new ArgumentException("Card experiment context ids are required.");
        }
        if (experiment.TowerLevel < 1 || experiment.Card.SlotIndex < 0)
        {
            throw new ArgumentException(
                "Card experiment tower level and slot must be valid.");
        }
    }

    private static void MarkGoodStandalone(
        IReadOnlyList<CardStrengthEntry> entries,
        double percentile)
    {
        foreach (IGrouping<string, CardStrengthEntry> group in entries
                     .Where(entry =>
                         entry.IsEvaluable &&
                         entry.Lift.RuntimeFailureSeedCount == 0)
                     .GroupBy(
                     entry => entry.DifficultyId + "\u001f" +
                         entry.TowerDefinitionId + "\u001f" +
                         entry.SubjectType + "\u001f" + entry.SlotIndex +
                         "\u001f" + entry.TowerLevel,
                     StringComparer.Ordinal))
        {
            double[] scores = group
                .Select(entry => entry.Lift.CompositeScore)
                .OrderBy(value => value)
                .ToArray();
            double threshold = Percentile(scores, percentile);
            foreach (CardStrengthEntry entry in group)
            {
                entry.IsGoodStandalone =
                    entry.Lift.CompositeScore >= threshold;
            }
        }
    }

    private static MatchedEvaluationRows SelectValidMatchedRows(
        IReadOnlyList<EvaluationRunMetrics> before,
        IReadOnlyList<EvaluationRunMetrics> after)
    {
        if (before.Count != after.Count)
        {
            throw new InvalidOperationException(
                "Matched card evaluation sets have different lengths.");
        }
        var validBefore = new List<EvaluationRunMetrics>(before.Count);
        var validAfter = new List<EvaluationRunMetrics>(after.Count);
        int invalid = 0;
        for (int index = 0; index < before.Count; index++)
        {
            if (before[index].Seed != after[index].Seed)
            {
                throw new InvalidOperationException(
                    "Card evaluation results are not in matched-seed order at " +
                    index + ".");
            }
            if (!before[index].IsValid || !after[index].IsValid)
            {
                invalid++;
                continue;
            }
            validBefore.Add(before[index]);
            validAfter.Add(after[index]);
        }
        return new MatchedEvaluationRows(validBefore, validAfter, invalid);
    }

    private static CardLiftMetrics WithInvalidCount(
        CardLiftMetrics lift,
        int invalidCount)
    {
        lift.InvalidRunCount = invalidCount;
        return lift;
    }

    private static string SummarizeFailures(
        IReadOnlyList<EvaluationRunMetrics> before,
        IReadOnlyList<EvaluationRunMetrics> after)
    {
        string[] reasons = before.Concat(after)
            .Where(row => !row.IsValid)
            .Select(row => row.FailureReason ?? "UNSPECIFIED_INVALID_RUN")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return reasons.Length == 0
            ? "NO_VALID_MATCHED_SEED_PAIR"
            : string.Join(" | ", reasons);
    }

    private sealed record MatchedEvaluationRows(
        IReadOnlyList<EvaluationRunMetrics> Before,
        IReadOnlyList<EvaluationRunMetrics> After,
        int InvalidCount);

    private static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }
        if (sorted.Count == 1)
        {
            return sorted[0];
        }
        double position = (sorted.Count - 1) * p;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        double fraction = position - lower;
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * fraction);
    }
}
