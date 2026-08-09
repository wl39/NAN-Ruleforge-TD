using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using RuleforgeTD.BalanceCli.Evaluation;

namespace RuleforgeTD.BalanceCli.Balance;

/// <summary>
/// Describes whether a metric is better when it becomes smaller or larger.
/// The value is explicit so matched-seed reports never infer intent from a
/// metric name.
/// </summary>
public enum BalanceMetricGoal
{
    Minimize = 0,
    Maximize = 1
}

public sealed class MatchedSeedMetricDelta
{
    public ulong GameSeed { get; set; }
    public ulong PolicySeed { get; set; }
    public double Before { get; set; }
    public double After { get; set; }
    public double Delta { get; set; }

    /// <summary>
    /// Positive means the candidate moved in the requested direction,
    /// regardless of whether the raw metric is minimized or maximized.
    /// </summary>
    public double DirectionalImprovement { get; set; }
}

public sealed class MatchedSeedComparison
{
    public string Metric { get; set; } = string.Empty;
    public BalanceMetricGoal Goal { get; set; }
    public int MatchedSeedCount { get; set; }
    public int ImprovedSeedCount { get; set; }
    public int RegressedSeedCount { get; set; }
    public int UnchangedSeedCount { get; set; }
    public double MeanBefore { get; set; }
    public double MeanAfter { get; set; }
    public double MeanDelta { get; set; }
    public double MeanDirectionalImprovement { get; set; }
    public List<MatchedSeedMetricDelta> Seeds { get; set; } = new();
}

public sealed class MatchedSeedSetMismatchException : InvalidOperationException
{
    public MatchedSeedSetMismatchException(
        IReadOnlyList<SeedPair> missingCandidateSeeds,
        IReadOnlyList<SeedPair> unexpectedCandidateSeeds)
        : base(BuildMessage(missingCandidateSeeds, unexpectedCandidateSeeds))
    {
        MissingCandidateSeeds = missingCandidateSeeds;
        UnexpectedCandidateSeeds = unexpectedCandidateSeeds;
    }

    public IReadOnlyList<SeedPair> MissingCandidateSeeds { get; }
    public IReadOnlyList<SeedPair> UnexpectedCandidateSeeds { get; }

    private static string BuildMessage(
        IReadOnlyList<SeedPair> missing,
        IReadOnlyList<SeedPair> unexpected)
    {
        string missingText = missing.Count == 0
            ? "none"
            : string.Join(", ", missing.Select(seed => seed.ToString()));
        string unexpectedText = unexpected.Count == 0
            ? "none"
            : string.Join(", ", unexpected.Select(seed => seed.ToString()));
        return "Matched-seed comparison requires identical seed pairs. " +
               "Missing candidate seeds: " + missingText +
               "; unexpected candidate seeds: " + unexpectedText + ".";
    }
}

/// <summary>
/// Compares before/after observations only after proving that their game and
/// policy seed pairs are identical. Rows are emitted in unsigned numeric seed
/// order, making the JSON artifact stable even if a runner finishes out of
/// order.
/// </summary>
public static class MatchedSeedRunComparer
{
    public static MatchedSeedComparison Compare<TObservation>(
        IEnumerable<TObservation> before,
        IEnumerable<TObservation> after,
        Func<TObservation, SeedPair> seedSelector,
        Func<TObservation, double> metricSelector,
        string metric,
        BalanceMetricGoal goal = BalanceMetricGoal.Minimize,
        double equalityTolerance = 1e-9)
    {
        return Compare(
            before,
            after,
            seedSelector,
            seedSelector,
            metricSelector,
            metricSelector,
            metric,
            goal,
            equalityTolerance);
    }

    public static MatchedSeedComparison Compare<TBefore, TAfter>(
        IEnumerable<TBefore> before,
        IEnumerable<TAfter> after,
        Func<TBefore, SeedPair> beforeSeedSelector,
        Func<TAfter, SeedPair> afterSeedSelector,
        Func<TBefore, double> beforeMetricSelector,
        Func<TAfter, double> afterMetricSelector,
        string metric,
        BalanceMetricGoal goal = BalanceMetricGoal.Minimize,
        double equalityTolerance = 1e-9)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(beforeSeedSelector);
        ArgumentNullException.ThrowIfNull(afterSeedSelector);
        ArgumentNullException.ThrowIfNull(beforeMetricSelector);
        ArgumentNullException.ThrowIfNull(afterMetricSelector);
        if (string.IsNullOrWhiteSpace(metric))
        {
            throw new ArgumentException("A metric name is required.", nameof(metric));
        }
        if (!double.IsFinite(equalityTolerance) || equalityTolerance < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(equalityTolerance),
                "Equality tolerance must be finite and non-negative.");
        }

        Dictionary<SeedPair, TBefore> beforeBySeed = BuildUniqueIndex(
            before,
            beforeSeedSelector,
            "before");
        Dictionary<SeedPair, TAfter> afterBySeed = BuildUniqueIndex(
            after,
            afterSeedSelector,
            "after");
        if (beforeBySeed.Count == 0)
        {
            throw new InvalidOperationException(
                "Matched-seed comparison requires at least one seed pair.");
        }

        List<SeedPair> missing = SortSeeds(
            beforeBySeed.Keys.Where(seed => !afterBySeed.ContainsKey(seed)));
        List<SeedPair> unexpected = SortSeeds(
            afterBySeed.Keys.Where(seed => !beforeBySeed.ContainsKey(seed)));
        if (missing.Count > 0 || unexpected.Count > 0)
        {
            throw new MatchedSeedSetMismatchException(
                new ReadOnlyCollection<SeedPair>(missing),
                new ReadOnlyCollection<SeedPair>(unexpected));
        }

        var comparison = new MatchedSeedComparison
        {
            Metric = metric,
            Goal = goal
        };
        foreach (SeedPair seed in SortSeeds(beforeBySeed.Keys))
        {
            double beforeValue = beforeMetricSelector(beforeBySeed[seed]);
            double afterValue = afterMetricSelector(afterBySeed[seed]);
            EnsureFinite(beforeValue, metric, seed, "before");
            EnsureFinite(afterValue, metric, seed, "after");

            double delta = afterValue - beforeValue;
            double improvement = goal == BalanceMetricGoal.Minimize
                ? -delta
                : delta;
            comparison.Seeds.Add(new MatchedSeedMetricDelta
            {
                GameSeed = seed.GameSeed,
                PolicySeed = seed.PolicySeed,
                Before = beforeValue,
                After = afterValue,
                Delta = delta,
                DirectionalImprovement = improvement
            });
            if (improvement > equalityTolerance)
            {
                comparison.ImprovedSeedCount++;
            }
            else if (improvement < -equalityTolerance)
            {
                comparison.RegressedSeedCount++;
            }
            else
            {
                comparison.UnchangedSeedCount++;
            }
        }

        comparison.MatchedSeedCount = comparison.Seeds.Count;
        comparison.MeanBefore = comparison.Seeds.Average(row => row.Before);
        comparison.MeanAfter = comparison.Seeds.Average(row => row.After);
        comparison.MeanDelta = comparison.Seeds.Average(row => row.Delta);
        comparison.MeanDirectionalImprovement = comparison.Seeds.Average(
            row => row.DirectionalImprovement);
        return comparison;
    }

    private static Dictionary<SeedPair, TObservation> BuildUniqueIndex<TObservation>(
        IEnumerable<TObservation> observations,
        Func<TObservation, SeedPair> seedSelector,
        string side)
    {
        var result = new Dictionary<SeedPair, TObservation>();
        foreach (TObservation observation in observations)
        {
            SeedPair seed = seedSelector(observation);
            if (!result.TryAdd(seed, observation))
            {
                throw new InvalidOperationException(
                    "Duplicate " + side + " observation for seed pair " +
                    seed + ".");
            }
        }
        return result;
    }

    private static List<SeedPair> SortSeeds(IEnumerable<SeedPair> seeds) => seeds
        .OrderBy(seed => seed.GameSeed)
        .ThenBy(seed => seed.PolicySeed)
        .ToList();

    private static void EnsureFinite(
        double value,
        string metric,
        SeedPair seed,
        string side)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException(
                side + " metric '" + metric + "' for seed pair " + seed +
                " must be finite.");
        }
    }
}

public sealed class CoordinateDescentCandidateOptions
{
    /// <summary>
    /// Probe size in percentage points. Ten is the maximum because the most
    /// restrictive permitted modifier rule is +/-10 percent.
    /// </summary>
    public double StepPercent { get; set; } = 5;
    public bool IncludeDecrease { get; set; } = true;
    public bool IncludeIncrease { get; set; } = true;
    public string ObjectiveMetric { get; set; } = "objectivePenalty";
    public string ObjectiveTarget { get; set; } = "decrease";
    public BalanceMetricGoal ObjectiveGoal { get; set; } =
        BalanceMetricGoal.Minimize;
    public string ReasonCode { get; set; } =
        "DETERMINISTIC_COORDINATE_DESCENT_PROBE";
}

public sealed class CoordinateDescentCandidate
{
    public int CoordinateIndex { get; set; }
    public int Direction { get; set; }
    public string JsonPointer { get; set; } = string.Empty;
    public required BalancePatch Patch { get; init; }
    public required DifficultyProfile Profile { get; init; }
    public string ProfileHash { get; init; } = string.Empty;
}

/// <summary>
/// Generates one-field balance candidates in a fixed coordinate order. It
/// never modifies policy, target, seed, base-health, safety, or card data; its
/// output is restricted to DifficultyProfile.Modifiers and every proposal is
/// passed through BalanceProposalValidator before being returned.
/// </summary>
public sealed class DeterministicCoordinateDescentCandidateGenerator
{
    private static readonly CoordinateDefinition[] Coordinates =
    {
        new("/modifiers/startingGold", profile => profile.Modifiers.StartingGold,
            0, int.MaxValue),
        new("/modifiers/enemyHealthPermille",
            profile => profile.Modifiers.EnemyHealthPermille, 500, 1500),
        new("/modifiers/enemyArmorPermille",
            profile => profile.Modifiers.EnemyArmorPermille, 500, 1500),
        new("/modifiers/enemySpeedPermille",
            profile => profile.Modifiers.EnemySpeedPermille, 500, 1500),
        new("/modifiers/enemyResistancePermille",
            profile => profile.Modifiers.EnemyResistancePermille, 500, 1500),
        new("/modifiers/enemyCountPermille",
            profile => profile.Modifiers.EnemyCountPermille, 500, 1500),
        new("/modifiers/spawnIntervalPermille",
            profile => profile.Modifiers.SpawnIntervalPermille, 500, 1500),
        new("/modifiers/goldRewardPermille",
            profile => profile.Modifiers.GoldRewardPermille, 500, 1500),
        new("/modifiers/towerBuildCostPermille",
            profile => profile.Modifiers.TowerBuildCostPermille, 500, 1500),
        new("/modifiers/towerUpgradeCostPermille",
            profile => profile.Modifiers.TowerUpgradeCostPermille, 500, 1500),
        new("/modifiers/bossAbilityIntervalPermille",
            profile => profile.Modifiers.BossAbilityIntervalPermille, 500, 1500)
    };

    private readonly BalanceProposalValidator validator;

    public DeterministicCoordinateDescentCandidateGenerator(
        BalanceProposalValidator? validator = null)
    {
        this.validator = validator ?? new BalanceProposalValidator();
    }

    public static IReadOnlyList<string> PermittedModifierPointers { get; } =
        Array.AsReadOnly(Coordinates.Select(value => value.JsonPointer).ToArray());

    public IReadOnlyList<CoordinateDescentCandidate> Generate(
        DifficultyProfile source,
        string proposalIdPrefix,
        double baselineObjective,
        int pass = 0,
        CoordinateDescentCandidateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Modifiers == null)
        {
            throw new ArgumentException(
                "Source profile modifiers are required.", nameof(source));
        }
        if (string.IsNullOrWhiteSpace(proposalIdPrefix))
        {
            throw new ArgumentException(
                "A proposal ID prefix is required.", nameof(proposalIdPrefix));
        }
        if (!double.IsFinite(baselineObjective))
        {
            throw new ArgumentOutOfRangeException(
                nameof(baselineObjective),
                "The baseline objective must be finite.");
        }
        if (pass < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pass));
        }

        options ??= new CoordinateDescentCandidateOptions();
        ValidateOptions(options);
        string sourceHash = BalanceProfileHasher.Compute(source);
        var candidates = new List<CoordinateDescentCandidate>();
        int serial = 0;
        for (int coordinateIndex = 0;
             coordinateIndex < Coordinates.Length;
             coordinateIndex++)
        {
            CoordinateDefinition coordinate = Coordinates[coordinateIndex];
            int? nullableValue = coordinate.Read(source);
            if (!nullableValue.HasValue || nullableValue.Value == 0)
            {
                // A percent-bounded auto-patch cannot invent a value from a
                // null or zero baseline.
                continue;
            }

            long oldValue = nullableValue.Value;
            long step = Math.Max(
                1,
                checked((long)Math.Round(
                    Math.Abs(oldValue) * options.StepPercent / 100.0,
                    MidpointRounding.AwayFromZero)));
            if (options.IncludeDecrease)
            {
                TryAddCandidate(-1);
            }
            if (options.IncludeIncrease)
            {
                TryAddCandidate(1);
            }

            void TryAddCandidate(int direction)
            {
                long newValue;
                try
                {
                    newValue = checked(oldValue + direction * step);
                }
                catch (OverflowException)
                {
                    return;
                }
                if (newValue < coordinate.Minimum ||
                    newValue > coordinate.Maximum ||
                    newValue == oldValue)
                {
                    return;
                }

                string suffix = direction < 0 ? "down" : "up";
                var patch = new BalancePatch
                {
                    ProposalId = proposalIdPrefix + "-p" +
                        pass.ToString("D2", CultureInfo.InvariantCulture) + "-" +
                        serial.ToString("D2", CultureInfo.InvariantCulture) + "-" +
                        suffix,
                    Difficulty = source.DifficultyId,
                    SourceProfileHash = sourceHash,
                    Diagnosis = new List<BalanceDiagnosis>
                    {
                        new()
                        {
                            Metric = options.ObjectiveMetric,
                            Actual = baselineObjective,
                            Target = options.ObjectiveTarget,
                            Evidence = "Deterministic coordinate " +
                                coordinate.JsonPointer + " " + suffix + " probe."
                        }
                    },
                    Changes = new List<BalanceChange>
                    {
                        new()
                        {
                            JsonPointer = coordinate.JsonPointer,
                            OldValue = oldValue,
                            NewValue = newValue,
                            ChangePercent =
                                (newValue - oldValue) * 100.0 / Math.Abs(oldValue),
                            ReasonCode = options.ReasonCode
                        }
                    },
                    ExpectedEffects = new List<ExpectedBalanceEffect>
                    {
                        new()
                        {
                            Metric = options.ObjectiveMetric,
                            Direction = options.ObjectiveGoal ==
                                BalanceMetricGoal.Minimize
                                ? BalanceEffectDirection.Decrease
                                : BalanceEffectDirection.Increase
                        }
                    },
                    Risks = new List<string>
                    {
                        "Requires same-seed A/B evaluation and all-difficulty " +
                        "regression checks before approval."
                    },
                    NeedsStructuralReview = false
                };

                BalancePatchValidationResult validation = validator.Validate(
                    source,
                    patch);
                if (!validation.IsValid)
                {
                    return;
                }
                BalancePatchApplicationResult application = validator.Apply(
                    source,
                    patch);
                candidates.Add(new CoordinateDescentCandidate
                {
                    CoordinateIndex = coordinateIndex,
                    Direction = direction,
                    JsonPointer = coordinate.JsonPointer,
                    Patch = patch,
                    Profile = application.Candidate,
                    ProfileHash = application.CandidateProfileHash
                });
                serial++;
            }
        }
        return new ReadOnlyCollection<CoordinateDescentCandidate>(candidates);
    }

    private static void ValidateOptions(CoordinateDescentCandidateOptions options)
    {
        if (!double.IsFinite(options.StepPercent) ||
            options.StepPercent <= 0 ||
            options.StepPercent > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.StepPercent),
                "Coordinate stepPercent must be greater than zero and at most 10.");
        }
        if (!options.IncludeDecrease && !options.IncludeIncrease)
        {
            throw new ArgumentException(
                "At least one coordinate direction must be enabled.",
                nameof(options));
        }
        if (string.IsNullOrWhiteSpace(options.ObjectiveMetric) ||
            string.IsNullOrWhiteSpace(options.ObjectiveTarget) ||
            string.IsNullOrWhiteSpace(options.ReasonCode))
        {
            throw new ArgumentException(
                "Objective metric, target, and reasonCode are required.",
                nameof(options));
        }
    }

    private sealed record CoordinateDefinition(
        string JsonPointer,
        Func<DifficultyProfile, int?> Read,
        long Minimum,
        long Maximum);
}
