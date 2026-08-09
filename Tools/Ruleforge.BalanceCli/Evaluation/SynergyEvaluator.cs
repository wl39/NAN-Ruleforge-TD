using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RuleforgeTD.BalanceCli.Policies;

namespace RuleforgeTD.BalanceCli.Evaluation;

public sealed class CardSynergyEntry
{
    public string DifficultyId { get; set; } = string.Empty;
    public string TowerDefinitionId { get; set; } = string.Empty;
    public int TowerLevel { get; set; }
    public List<CardProgramStep> OrderedProgram { get; set; } = new();
    public CardLiftMetrics ProgramLift { get; set; } = new();
    public CardLiftMetrics SynergyLift { get; set; } = new();
    public double FirstOnlyCompositeLift { get; set; }
    public double SecondOnlyCompositeLift { get; set; }
    public double? ThirdOnlyCompositeLift { get; set; }
    public string DiscoverySource { get; set; } = string.Empty;

    public string OrderedSignature => CardSynergyIndex.BuildSignature(
        DifficultyId,
        TowerDefinitionId,
        TowerLevel,
        OrderedProgram);
}

public sealed class CardSynergyIndex : ICardSynergyLookup
{
    private IReadOnlyList<CardSynergyEntry>? lookupSource;
    private Dictionary<string, CardSynergyEntry>? lookup;

    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ContentHash { get; set; } = string.Empty;
    public string SeedSetHash { get; set; } = string.Empty;
    public CardLiftScoringWeights Weights { get; set; } = new();
    public List<CardSynergyEntry> Entries { get; set; } = new();

    public double GetScore(CardSynergyQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        string signature = BuildSignature(
            query.DifficultyId,
            query.TowerDefinitionId,
            query.TowerLevel,
            query.OrderedProgram);
        EnsureLookup();
        return lookup!.TryGetValue(signature, out CardSynergyEntry? entry)
            ? entry.SynergyLift.CompositeScore
            : 0.0;
    }

    public bool TryGet(
        string difficultyId,
        string towerDefinitionId,
        int towerLevel,
        IReadOnlyList<CardProgramStep> orderedProgram,
        out CardSynergyEntry? entry)
    {
        string signature = BuildSignature(
            difficultyId,
            towerDefinitionId,
            towerLevel,
            orderedProgram);
        EnsureLookup();
        return lookup!.TryGetValue(signature, out entry);
    }

    public RuleforgeTD.BalanceCli.Policies.CardSynergyIndex ToPolicyIndex()
    {
        Validate();
        return new RuleforgeTD.BalanceCli.Policies.CardSynergyIndex
        {
            SchemaVersion = SchemaVersion,
            ContentHash = ContentHash,
            Entries = Entries.Select(entry =>
            {
                CardProgramStep first = entry.OrderedProgram[0];
                CardProgramStep second = entry.OrderedProgram[1];
                CardProgramStep? third = entry.OrderedProgram.Count > 2
                    ? entry.OrderedProgram[2]
                    : null;
                return new RuleforgeTD.BalanceCli.Policies.CardSynergyEntry
                {
                    Difficulty = entry.DifficultyId,
                    TowerDefinition = entry.TowerDefinitionId,
                    FirstCardId = first.CardId,
                    FirstSubjectType = first.SubjectType,
                    SecondCardId = second.CardId,
                    SecondSubjectType = second.SubjectType,
                    FirstSlotIndex = first.SlotIndex,
                    SecondSlotIndex = second.SlotIndex,
                    TowerLevel = entry.TowerLevel,
                    SampleSize = entry.SynergyLift.MatchedSeedCount,
                    FirstOnlyScore = entry.FirstOnlyCompositeLift,
                    SecondOnlyScore = entry.SecondOnlyCompositeLift,
                    CombinedScore = entry.ProgramLift.CompositeScore,
                    ExpectedAdditiveScore =
                        entry.ProgramLift.CompositeScore -
                        entry.SynergyLift.CompositeScore,
                    SynergyLift = entry.SynergyLift.CompositeScore,
                    IsTriple = third.HasValue,
                    ThirdCardId = third?.CardId,
                    ThirdSubjectType = third?.SubjectType
                };
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
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < Entries.Count; index++)
        {
            CardSynergyEntry entry = Entries[index];
            if (string.IsNullOrWhiteSpace(entry.DifficultyId) ||
                string.IsNullOrWhiteSpace(entry.TowerDefinitionId) ||
                entry.TowerLevel < 1)
            {
                errors.Add("entries[" + index + "] has an invalid context.");
            }
            if (entry.OrderedProgram.Count is < 2 or > 3)
            {
                errors.Add(
                    "entries[" + index + "] must contain a pair or triple.");
            }
            var occupiedSlots = new HashSet<int>();
            int priorSlot = -1;
            foreach (CardProgramStep step in entry.OrderedProgram)
            {
                if (string.IsNullOrWhiteSpace(step.CardId) ||
                    step.SlotIndex < 0 ||
                    step.SlotIndex <= priorSlot ||
                    !occupiedSlots.Add(step.SlotIndex))
                {
                    errors.Add(
                        "entries[" + index +
                        "] contains an invalid card or non-increasing slot order.");
                    break;
                }
                priorSlot = step.SlotIndex;
            }
            if (!signatures.Add(entry.OrderedSignature))
            {
                errors.Add("Duplicate ordered synergy at entries[" + index + "].");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Invalid card synergy index:\n" + string.Join("\n", errors));
        }
        lookup = null;
        lookupSource = null;
    }

    internal static string BuildSignature(
        string difficultyId,
        string towerDefinitionId,
        int towerLevel,
        IReadOnlyList<CardProgramStep> orderedProgram)
    {
        ArgumentNullException.ThrowIfNull(orderedProgram);
        string steps = string.Join(
            ">",
            orderedProgram.Select(step =>
                step.SlotIndex + ":" + step.SubjectType + ":" + step.CardId));
        return difficultyId + "|" + towerDefinitionId + "|L" + towerLevel +
            "|" + steps;
    }

    private void EnsureLookup()
    {
        Entries ??= new List<CardSynergyEntry>();
        if (lookup != null &&
            ReferenceEquals(lookupSource, Entries) &&
            lookup.Count == Entries.Count)
        {
            return;
        }

        lookup = new Dictionary<string, CardSynergyEntry>(StringComparer.Ordinal);
        foreach (CardSynergyEntry entry in Entries)
        {
            if (!lookup.TryAdd(entry.OrderedSignature, entry))
            {
                throw new InvalidOperationException(
                    "Card synergy index contains a duplicate ordered program: " +
                    entry.OrderedSignature);
            }
        }
        lookupSource = Entries;
    }
}

public sealed record CardSynergyPairExperiment(
    string DifficultyId,
    string TowerDefinitionId,
    int TowerLevel,
    CardProgramStep First,
    CardProgramStep Second);

public sealed class SynergyEvaluator
{
    private readonly CardLiftScoringWeights weights;

    public SynergyEvaluator(CardLiftScoringWeights? weights = null)
    {
        this.weights = weights ?? new CardLiftScoringWeights();
    }

    public async Task<CardSynergyIndex> EvaluatePairsAsync(
        IReadOnlyList<CardSynergyPairExperiment> experiments,
        IReadOnlyList<SeedPair> seeds,
        CardExperimentRunner runner,
        string contentHash = "",
        string seedSetHash = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiments);
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(runner);
        if (experiments.Count == 0 || seeds.Count == 0)
        {
            throw new ArgumentException(
                "Pair experiments and seed pairs must both be non-empty.");
        }
        var cache = new Dictionary<string, IReadOnlyList<EvaluationRunMetrics>>(
            StringComparer.Ordinal);
        var entries = new List<CardSynergyEntry>(experiments.Count);
        foreach (CardSynergyPairExperiment experiment in experiments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidatePair(experiment);
            IReadOnlyList<CardProgramStep> a = new[] { experiment.First };
            IReadOnlyList<CardProgramStep> b = new[] { experiment.Second };
            IReadOnlyList<CardProgramStep> ab = new[]
            {
                experiment.First,
                experiment.Second
            };
            CardExperimentVariant baseline = Variant(experiment, Array.Empty<CardProgramStep>());
            CardExperimentVariant variantA = Variant(experiment, a);
            CardExperimentVariant variantB = Variant(experiment, b);
            CardExperimentVariant variantAb = Variant(experiment, ab);

            IReadOnlyList<EvaluationRunMetrics> baselineRuns = await RunAllAsync(
                baseline, seeds, runner, cache, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<EvaluationRunMetrics> aRuns = await RunAllAsync(
                variantA, seeds, runner, cache, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<EvaluationRunMetrics> bRuns = await RunAllAsync(
                variantB, seeds, runner, cache, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<EvaluationRunMetrics> abRuns = await RunAllAsync(
                variantAb, seeds, runner, cache, cancellationToken).ConfigureAwait(false);
            entries.Add(new CardSynergyEntry
            {
                DifficultyId = experiment.DifficultyId,
                TowerDefinitionId = experiment.TowerDefinitionId,
                TowerLevel = experiment.TowerLevel,
                OrderedProgram = new List<CardProgramStep>(ab),
                ProgramLift = CardLiftCalculator.Difference(
                    baselineRuns,
                    abRuns,
                    weights),
                FirstOnlyCompositeLift = CardLiftCalculator.Difference(
                    baselineRuns,
                    aRuns,
                    weights).CompositeScore,
                SecondOnlyCompositeLift = CardLiftCalculator.Difference(
                    baselineRuns,
                    bRuns,
                    weights).CompositeScore,
                SynergyLift = CardLiftCalculator.Interaction(
                    baselineRuns,
                    aRuns,
                    bRuns,
                    abRuns,
                    weights),
                DiscoverySource = "matched-pair"
            });
        }

        var index = new CardSynergyIndex
        {
            ContentHash = contentHash,
            SeedSetHash = seedSetHash,
            Weights = weights,
            Entries = entries
                .OrderByDescending(entry => entry.SynergyLift.CompositeScore)
                .ThenBy(entry => entry.OrderedSignature, StringComparer.Ordinal)
                .ToList()
        };
        index.Validate();
        return index;
    }

    /// <summary>
    /// Expands only the strongest ordered pairs. For every generated triple it
    /// runs baseline, singleton, ordered-pair and triple fixtures on identical
    /// seeds and uses the third-order inclusion/exclusion interaction.
    /// </summary>
    public async Task<CardSynergyIndex> EvaluateTripleBeamAsync(
        CardSynergyIndex pairIndex,
        IReadOnlyList<CardProgramStep> thirdCardCandidates,
        IReadOnlyList<SeedPair> seeds,
        CardExperimentRunner runner,
        int pairBeamWidth = 32,
        int maximumTripleCount = 256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairIndex);
        ArgumentNullException.ThrowIfNull(thirdCardCandidates);
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(runner);
        if (pairBeamWidth < 1 || maximumTripleCount < 1 || seeds.Count == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pairBeamWidth),
                "Beam width, triple count, and seed count must be positive.");
        }
        pairIndex.Validate();
        var candidates = new Dictionary<string, TripleCandidate>(StringComparer.Ordinal);
        foreach (CardSynergyEntry pair in pairIndex.Entries
                     .Where(entry => entry.OrderedProgram.Count == 2)
                     .OrderByDescending(entry => entry.SynergyLift.CompositeScore)
                     .ThenBy(entry => entry.OrderedSignature, StringComparer.Ordinal)
                     .Take(pairBeamWidth))
        {
            foreach (CardProgramStep third in thirdCardCandidates)
            {
                if (pair.OrderedProgram.Any(step =>
                        step.SlotIndex == third.SlotIndex) ||
                    string.IsNullOrWhiteSpace(third.CardId) ||
                    third.SlotIndex < 0)
                {
                    continue;
                }

                List<CardProgramStep> program = pair.OrderedProgram
                    .Append(third)
                    .OrderBy(step => step.SlotIndex)
                    .ToList();
                string signature = CardSynergyIndex.BuildSignature(
                    pair.DifficultyId,
                    pair.TowerDefinitionId,
                    pair.TowerLevel,
                    program);
                candidates.TryAdd(
                    signature,
                    new TripleCandidate(pair, program));
                if (candidates.Count >= maximumTripleCount)
                {
                    break;
                }
            }
            if (candidates.Count >= maximumTripleCount)
            {
                break;
            }
        }

        var cache = new Dictionary<string, IReadOnlyList<EvaluationRunMetrics>>(
            StringComparer.Ordinal);
        var triples = new List<CardSynergyEntry>(candidates.Count);
        foreach (TripleCandidate candidate in candidates.Values.OrderBy(
                     value => CardSynergyIndex.BuildSignature(
                         value.Pair.DifficultyId,
                         value.Pair.TowerDefinitionId,
                         value.Pair.TowerLevel,
                         value.Program),
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CardSynergyEntry context = candidate.Pair;
            CardProgramStep a = candidate.Program[0];
            CardProgramStep b = candidate.Program[1];
            CardProgramStep c = candidate.Program[2];
            IReadOnlyList<CardProgramStep> ab = new[] { a, b };
            IReadOnlyList<CardProgramStep> ac = new[] { a, c };
            IReadOnlyList<CardProgramStep> bc = new[] { b, c };

            Task<IReadOnlyList<EvaluationRunMetrics>> Run(
                IReadOnlyList<CardProgramStep> program) =>
                RunAllAsync(
                    Variant(context, program),
                    seeds,
                    runner,
                    cache,
                    cancellationToken);

            IReadOnlyList<EvaluationRunMetrics> baseline = await Run(
                Array.Empty<CardProgramStep>()).ConfigureAwait(false);
            IReadOnlyList<EvaluationRunMetrics> runA = await Run(new[] { a })
                .ConfigureAwait(false);
            IReadOnlyList<EvaluationRunMetrics> runB = await Run(new[] { b })
                .ConfigureAwait(false);
            IReadOnlyList<EvaluationRunMetrics> runC = await Run(new[] { c })
                .ConfigureAwait(false);
            IReadOnlyList<EvaluationRunMetrics> runAb = await Run(ab)
                .ConfigureAwait(false);
            IReadOnlyList<EvaluationRunMetrics> runAc = await Run(ac)
                .ConfigureAwait(false);
            IReadOnlyList<EvaluationRunMetrics> runBc = await Run(bc)
                .ConfigureAwait(false);
            IReadOnlyList<EvaluationRunMetrics> runAbc = await Run(candidate.Program)
                .ConfigureAwait(false);

            triples.Add(new CardSynergyEntry
            {
                DifficultyId = context.DifficultyId,
                TowerDefinitionId = context.TowerDefinitionId,
                TowerLevel = context.TowerLevel,
                OrderedProgram = new List<CardProgramStep>(candidate.Program),
                ProgramLift = CardLiftCalculator.Difference(
                    baseline,
                    runAbc,
                    weights),
                FirstOnlyCompositeLift = CardLiftCalculator.Difference(
                    baseline,
                    runA,
                    weights).CompositeScore,
                SecondOnlyCompositeLift = CardLiftCalculator.Difference(
                    baseline,
                    runB,
                    weights).CompositeScore,
                ThirdOnlyCompositeLift = CardLiftCalculator.Difference(
                    baseline,
                    runC,
                    weights).CompositeScore,
                SynergyLift = CardLiftCalculator.TripleInteraction(
                    baseline,
                    runA,
                    runB,
                    runC,
                    runAb,
                    runAc,
                    runBc,
                    runAbc,
                    weights),
                DiscoverySource = "pair-beam-triple"
            });
        }

        var index = new CardSynergyIndex
        {
            ContentHash = pairIndex.ContentHash,
            SeedSetHash = pairIndex.SeedSetHash,
            Weights = weights,
            Entries = pairIndex.Entries
                .Concat(triples)
                .OrderByDescending(entry => entry.SynergyLift.CompositeScore)
                .ThenBy(entry => entry.OrderedSignature, StringComparer.Ordinal)
                .ToList()
        };
        index.Validate();
        return index;
    }

    private static CardExperimentVariant Variant(
        CardSynergyPairExperiment experiment,
        IReadOnlyList<CardProgramStep> program) =>
        new(
            CardSynergyIndex.BuildSignature(
                experiment.DifficultyId,
                experiment.TowerDefinitionId,
                experiment.TowerLevel,
                program),
            experiment.DifficultyId,
            experiment.TowerDefinitionId,
            experiment.TowerLevel,
            program.Count == 0
                ? CardExperimentVariantKind.Baseline
                : CardExperimentVariantKind.Program,
            program);

    private static CardExperimentVariant Variant(
        CardSynergyEntry context,
        IReadOnlyList<CardProgramStep> program) =>
        new(
            CardSynergyIndex.BuildSignature(
                context.DifficultyId,
                context.TowerDefinitionId,
                context.TowerLevel,
                program),
            context.DifficultyId,
            context.TowerDefinitionId,
            context.TowerLevel,
            program.Count == 0
                ? CardExperimentVariantKind.Baseline
                : CardExperimentVariantKind.Program,
            program);

    private static async Task<IReadOnlyList<EvaluationRunMetrics>> RunAllAsync(
        CardExperimentVariant variant,
        IReadOnlyList<SeedPair> seeds,
        CardExperimentRunner runner,
        IDictionary<string, IReadOnlyList<EvaluationRunMetrics>> cache,
        CancellationToken cancellationToken)
    {
        string key = CardStrengthEvaluator.BuildCacheKey(variant);
        if (cache.TryGetValue(key, out IReadOnlyList<EvaluationRunMetrics>? found))
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
                    "Synergy runner returned a mismatched seed for " +
                    variant.VariantId + ".");
            }
            results.Add(result);
        }
        cache.Add(key, results);
        return results;
    }

    private static void ValidatePair(CardSynergyPairExperiment experiment)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        if (string.IsNullOrWhiteSpace(experiment.DifficultyId) ||
            string.IsNullOrWhiteSpace(experiment.TowerDefinitionId) ||
            string.IsNullOrWhiteSpace(experiment.First.CardId) ||
            string.IsNullOrWhiteSpace(experiment.Second.CardId))
        {
            throw new ArgumentException("Pair experiment context ids are required.");
        }
        if (experiment.TowerLevel < 1 ||
            experiment.First.SlotIndex < 0 ||
            experiment.Second.SlotIndex < 0 ||
            experiment.First.SlotIndex >= experiment.Second.SlotIndex)
        {
            throw new ArgumentException(
                "Pair experiment requires strictly increasing execution slots.");
        }
    }

    private sealed record TripleCandidate(
        CardSynergyEntry Pair,
        List<CardProgramStep> Program);
}
