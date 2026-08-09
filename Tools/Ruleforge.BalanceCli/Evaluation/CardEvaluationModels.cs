using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RuleforgeTD.BalanceCli.Policies;

namespace RuleforgeTD.BalanceCli.Evaluation;

public enum CardExperimentVariantKind
{
    Baseline = 0,
    Program = 1
}

/// <summary>
/// Describes a legal fixture that the caller must realize through the normal
/// simulation setup and command path. Evaluators never mutate GameSimulation.
/// </summary>
public sealed record CardExperimentVariant(
    string VariantId,
    string DifficultyId,
    string TowerDefinitionId,
    int TowerLevel,
    CardExperimentVariantKind Kind,
    IReadOnlyList<CardProgramStep> OrderedProgram);

/// <summary>
/// Authoritative outcome projected by the scenario runner. GoldEfficiency and
/// BossStability are intentionally supplied by the integration layer so this
/// package does not duplicate any gameplay formulas. IsValid means the fixture
/// is evaluable; an IsRuntimeFailure row remains valid so it stays in the win
/// rate denominator, while its FailureReason preserves the diagnostic.
/// </summary>
public sealed record EvaluationRunMetrics(
    SeedPair Seed,
    bool Victory,
    int RemainingBaseHealth,
    int ClearedWaveCount,
    int TotalLeakDamage,
    double GoldEfficiency,
    double BossStability,
    bool IsValid = true,
    string? FailureReason = null,
    string ScenarioHash = "",
    string FixtureContextHash = "",
    bool FixtureVerified = false,
    bool IsRuntimeFailure = false);

public delegate ValueTask<EvaluationRunMetrics> CardExperimentRunner(
    CardExperimentVariant variant,
    SeedPair seed,
    CancellationToken cancellationToken);

public sealed class CardLiftMetrics
{
    public double BaselineWinRate { get; set; }
    public double CandidateWinRate { get; set; }
    public double WinRateLift { get; set; }
    public double MeanRemainingHealthLift { get; set; }
    public double MeanClearedWaveLift { get; set; }
    public double MeanGoldEfficiencyLift { get; set; }
    public double MeanLeakReduction { get; set; }
    public double MeanBossStabilityLift { get; set; }
    public double CompositeScore { get; set; }
    public int MatchedSeedCount { get; set; }
    public int InvalidRunCount { get; set; }
    public int CleanMetricSeedCount { get; set; }
    public int RuntimeFailureSeedCount { get; set; }
    public int RuntimeFailureRunCount { get; set; }
    public List<string> RuntimeFailureDiagnostics { get; set; } = new();
}

public sealed class CardLiftScoringWeights
{
    public double WinRate { get; set; } = 100.0;
    public double RemainingHealth { get; set; } = 1.0;
    public double ClearedWave { get; set; } = 2.0;
    // The projector is a raw earned/spent ratio and can legitimately be in
    // the hundreds for a free level-one fixture. Keep it as diagnostic
    // evidence, but normalize its contribution so it cannot drown out wins,
    // cleared waves, health, leakage, or boss handling.
    public double GoldEfficiency { get; set; } = 0.01;
    public double LeakReduction { get; set; } = 0.1;
    public double BossStability { get; set; } = 10.0;

    public double Score(CardLiftMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        return
            (metrics.WinRateLift * WinRate) +
            (metrics.MeanRemainingHealthLift * RemainingHealth) +
            (metrics.MeanClearedWaveLift * ClearedWave) +
            (metrics.MeanGoldEfficiencyLift * GoldEfficiency) +
            (metrics.MeanLeakReduction * LeakReduction) +
            (metrics.MeanBossStabilityLift * BossStability);
    }
}

internal static class CardLiftCalculator
{
    public static CardLiftMetrics Difference(
        IReadOnlyList<EvaluationRunMetrics> baseline,
        IReadOnlyList<EvaluationRunMetrics> candidate,
        CardLiftScoringWeights weights)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(weights);
        if (baseline.Count != candidate.Count)
        {
            throw new InvalidOperationException(
                "Matched card evaluation sets have different lengths.");
        }

        int valid = 0;
        int invalid = 0;
        int clean = 0;
        int runtimeFailureSeeds = 0;
        int runtimeFailureRuns = 0;
        var runtimeDiagnostics = new SortedSet<string>(StringComparer.Ordinal);
        double victory = 0;
        int baselineWins = 0;
        int candidateWins = 0;
        double health = 0;
        double waves = 0;
        double gold = 0;
        double leakReduction = 0;
        double boss = 0;
        for (int index = 0; index < baseline.Count; index++)
        {
            EvaluationRunMetrics before = baseline[index];
            EvaluationRunMetrics after = candidate[index];
            if (before.Seed != after.Seed)
            {
                throw new InvalidOperationException(
                    "Card evaluation results are not in matched-seed order at " +
                    index + ".");
            }
            if (!before.IsValid || !after.IsValid)
            {
                invalid++;
                continue;
            }

            valid++;
            bool beforeWon = before.Victory && !before.IsRuntimeFailure;
            bool afterWon = after.Victory && !after.IsRuntimeFailure;
            baselineWins += beforeWon ? 1 : 0;
            candidateWins += afterWon ? 1 : 0;
            victory += (afterWon ? 1 : 0) - (beforeWon ? 1 : 0);
            int rowRuntimeFailures = CountRuntimeFailures(
                new[] { before, after },
                runtimeDiagnostics);
            runtimeFailureRuns += rowRuntimeFailures;
            if (rowRuntimeFailures > 0)
            {
                runtimeFailureSeeds++;
                continue;
            }

            clean++;
            health += after.RemainingBaseHealth - before.RemainingBaseHealth;
            waves += after.ClearedWaveCount - before.ClearedWaveCount;
            gold += after.GoldEfficiency - before.GoldEfficiency;
            leakReduction += before.TotalLeakDamage - after.TotalLeakDamage;
            boss += after.BossStability - before.BossStability;
        }

        if (valid == 0)
        {
            throw new InvalidOperationException(
                "Card evaluation has no valid matched-seed pairs.");
        }

        var metrics = new CardLiftMetrics
        {
            BaselineWinRate = baselineWins / (double)valid,
            CandidateWinRate = candidateWins / (double)valid,
            WinRateLift = victory / valid,
            MeanRemainingHealthLift = DivideClean(health, clean),
            MeanClearedWaveLift = DivideClean(waves, clean),
            MeanGoldEfficiencyLift = DivideClean(gold, clean),
            MeanLeakReduction = DivideClean(leakReduction, clean),
            MeanBossStabilityLift = DivideClean(boss, clean),
            MatchedSeedCount = valid,
            InvalidRunCount = invalid,
            CleanMetricSeedCount = clean,
            RuntimeFailureSeedCount = runtimeFailureSeeds,
            RuntimeFailureRunCount = runtimeFailureRuns,
            RuntimeFailureDiagnostics = runtimeDiagnostics.ToList()
        };
        metrics.CompositeScore = weights.Score(metrics);
        return metrics;
    }

    public static CardLiftMetrics Interaction(
        IReadOnlyList<EvaluationRunMetrics> baseline,
        IReadOnlyList<EvaluationRunMetrics> a,
        IReadOnlyList<EvaluationRunMetrics> b,
        IReadOnlyList<EvaluationRunMetrics> ab,
        CardLiftScoringWeights weights) =>
        LinearCombination(
            new[] { baseline, a, b, ab },
            new[] { 1.0, -1.0, -1.0, 1.0 },
            weights);

    public static CardLiftMetrics TripleInteraction(
        IReadOnlyList<EvaluationRunMetrics> baseline,
        IReadOnlyList<EvaluationRunMetrics> a,
        IReadOnlyList<EvaluationRunMetrics> b,
        IReadOnlyList<EvaluationRunMetrics> c,
        IReadOnlyList<EvaluationRunMetrics> ab,
        IReadOnlyList<EvaluationRunMetrics> ac,
        IReadOnlyList<EvaluationRunMetrics> bc,
        IReadOnlyList<EvaluationRunMetrics> abc,
        CardLiftScoringWeights weights) =>
        LinearCombination(
            new[] { baseline, a, b, c, ab, ac, bc, abc },
            new[] { -1.0, 1.0, 1.0, 1.0, -1.0, -1.0, -1.0, 1.0 },
            weights);

    private static CardLiftMetrics LinearCombination(
        IReadOnlyList<EvaluationRunMetrics>[] sets,
        double[] coefficients,
        CardLiftScoringWeights weights)
    {
        int length = sets[0].Count;
        if (sets.Length != coefficients.Length)
        {
            throw new ArgumentException("A coefficient is required per result set.");
        }
        foreach (IReadOnlyList<EvaluationRunMetrics> set in sets)
        {
            if (set.Count != length)
            {
                throw new InvalidOperationException(
                    "Interaction result sets have different lengths.");
            }
        }

        int valid = 0;
        int invalid = 0;
        int clean = 0;
        int runtimeFailureSeeds = 0;
        int runtimeFailureRuns = 0;
        var runtimeDiagnostics = new SortedSet<string>(StringComparer.Ordinal);
        double victory = 0;
        double health = 0;
        double waves = 0;
        double gold = 0;
        double leaks = 0;
        double boss = 0;
        for (int runIndex = 0; runIndex < length; runIndex++)
        {
            SeedPair seed = sets[0][runIndex].Seed;
            bool validRow = true;
            for (int setIndex = 0; setIndex < sets.Length; setIndex++)
            {
                EvaluationRunMetrics candidate = sets[setIndex][runIndex];
                if (candidate.Seed != seed)
                {
                    throw new InvalidOperationException(
                        "Interaction results are not in matched-seed order.");
                }
                validRow &= candidate.IsValid;
            }
            if (!validRow)
            {
                invalid++;
                continue;
            }

            valid++;
            var row = new EvaluationRunMetrics[sets.Length];
            for (int setIndex = 0; setIndex < sets.Length; setIndex++)
            {
                row[setIndex] = sets[setIndex][runIndex];
            }
            int rowRuntimeFailures = CountRuntimeFailures(
                row,
                runtimeDiagnostics);
            runtimeFailureRuns += rowRuntimeFailures;
            if (rowRuntimeFailures > 0)
            {
                runtimeFailureSeeds++;
            }
            for (int setIndex = 0; setIndex < sets.Length; setIndex++)
            {
                EvaluationRunMetrics observation = sets[setIndex][runIndex];
                double coefficient = coefficients[setIndex];
                victory += coefficient *
                    (observation.Victory && !observation.IsRuntimeFailure
                        ? 1.0
                        : 0.0);
            }
            if (rowRuntimeFailures > 0)
            {
                continue;
            }

            clean++;
            for (int setIndex = 0; setIndex < sets.Length; setIndex++)
            {
                EvaluationRunMetrics observation = sets[setIndex][runIndex];
                double coefficient = coefficients[setIndex];
                health += coefficient * observation.RemainingBaseHealth;
                waves += coefficient * observation.ClearedWaveCount;
                gold += coefficient * observation.GoldEfficiency;
                // Lower leakage is better, hence the sign inversion.
                leaks -= coefficient * observation.TotalLeakDamage;
                boss += coefficient * observation.BossStability;
            }
        }

        if (valid == 0)
        {
            throw new InvalidOperationException(
                "Interaction evaluation has no valid matched-seed rows.");
        }

        var metrics = new CardLiftMetrics
        {
            WinRateLift = victory / valid,
            MeanRemainingHealthLift = DivideClean(health, clean),
            MeanClearedWaveLift = DivideClean(waves, clean),
            MeanGoldEfficiencyLift = DivideClean(gold, clean),
            MeanLeakReduction = DivideClean(leaks, clean),
            MeanBossStabilityLift = DivideClean(boss, clean),
            MatchedSeedCount = valid,
            InvalidRunCount = invalid,
            CleanMetricSeedCount = clean,
            RuntimeFailureSeedCount = runtimeFailureSeeds,
            RuntimeFailureRunCount = runtimeFailureRuns,
            RuntimeFailureDiagnostics = runtimeDiagnostics.ToList()
        };
        metrics.CompositeScore = weights.Score(metrics);
        return metrics;
    }

    private static int CountRuntimeFailures(
        IEnumerable<EvaluationRunMetrics> observations,
        ISet<string> diagnostics)
    {
        int count = 0;
        foreach (EvaluationRunMetrics observation in observations)
        {
            if (!observation.IsRuntimeFailure)
            {
                continue;
            }
            count++;
            diagnostics.Add(observation.FailureReason ??
                "UNSPECIFIED_RUNTIME_FAILURE");
        }
        return count;
    }

    private static double DivideClean(double total, int cleanCount) =>
        cleanCount == 0 ? 0 : total / cleanCount;
}
