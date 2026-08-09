using System;
using System.Collections.Generic;
using System.Linq;
using RuleforgeTD.BalanceCli.Simulation;

namespace RuleforgeTD.BalanceCli.Evaluation;

public sealed class BatchStatisticalReport
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string DifficultyId { get; set; } = string.Empty;
    public string PolicyId { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public string ScenarioId { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string DifficultyProfileHash { get; set; } = string.Empty;
    public int RunCount { get; set; }
    public int VictoryCount { get; set; }
    public int DefeatCount { get; set; }
    public int TimeoutCount { get; set; }
    public int ErrorCount { get; set; }
    public double WinRate { get; set; }
    public WilsonInterval WinRateWilson95 { get; set; } = new(0, 1);
    public DistributionStatistics RemainingHealth { get; set; } = new();
    public DistributionStatistics VictoryRemainingHealth { get; set; } = new();
    public DistributionStatistics FailedWave { get; set; } = new();
    public DistributionStatistics ClearedWaves { get; set; } = new();
    public DistributionStatistics LeakDamage { get; set; } = new();
    public DistributionStatistics GoldEarned { get; set; } = new();
    public DistributionStatistics GoldSpent { get; set; } = new();
    public DistributionStatistics GoldUnspent { get; set; } = new();
    public DistributionStatistics TowerBuilds { get; set; } = new();
    public DistributionStatistics TowerUpgrades { get; set; } = new();
    public DistributionStatistics LogicalTicks { get; set; } = new();
    public double MidWaveBuildRunRatio { get; set; }
    public double SuccessfulRunMidWaveBuildRatio { get; set; }
    public double RejectedCommandRate { get; set; }
    public int RejectedCommandRunCount { get; set; }
    public double SafetyLimitRunRatio { get; set; }
    public int SafetyLimitFailureCount { get; set; }
    public int RuntimeFailureCount { get; set; }
    public Dictionary<int, int> FailureWaveDistribution { get; set; } = new();
    public Dictionary<string, CategoryWinStatistics> StartingTowerStats { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, CategoryWinStatistics> CardStats { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, double> MeanTowerBuildsByDefinition { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, long> LeaksByEnemyType { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, long> CardExecutionCount { get; set; } =
        new(StringComparer.Ordinal);
}

public sealed class BatchEvaluator
{
    public BatchStatisticalReport Aggregate(
        IReadOnlyList<SimulationResult> runs,
        bool requireHomogeneousBatch = true)
    {
        ArgumentNullException.ThrowIfNull(runs);
        if (runs.Count == 0)
        {
            throw new ArgumentException("A batch must contain at least one run.");
        }
        ValidateUniqueRuns(runs);
        SimulationResult first = runs[0];
        if (requireHomogeneousBatch)
        {
            ValidateHomogeneous(runs, first);
        }

        int wins = runs.Count(RunOutcomeClassifier.IsSuccessful);
        int timeouts = runs.Count(run =>
            run.Result == SimulationOutcome.Timeout);
        int errors = runs.Count(run => run.Result == SimulationOutcome.Error);
        long decisions = runs.Sum(run => (long)Math.Max(0, run.TotalDecisions));
        var report = new BatchStatisticalReport
        {
            DifficultyId = first.DifficultyId,
            PolicyId = first.PolicyId,
            PolicyVersion = first.PolicyVersion,
            ScenarioId = first.ScenarioId,
            ContentHash = first.ContentHash,
            DifficultyProfileHash = first.DifficultyProfileHash,
            RunCount = runs.Count,
            VictoryCount = wins,
            // Safety-truncated or command-rejected terminal victories are
            // effective losses and therefore live in DefeatCount as well as
            // their explicit diagnostic counters below.
            DefeatCount = runs.Count - wins - timeouts - errors,
            TimeoutCount = timeouts,
            ErrorCount = errors,
            WinRate = wins / (double)runs.Count,
            WinRateWilson95 = StatisticalMath.Wilson95(wins, runs.Count),
            RemainingHealth = StatisticalMath.Describe(
                runs.Select(run => (double)run.RemainingBaseHealth)),
            VictoryRemainingHealth = StatisticalMath.Describe(runs
                .Where(RunOutcomeClassifier.IsSuccessful)
                .Select(run => (double)run.RemainingBaseHealth)),
            FailedWave = StatisticalMath.Describe(runs
                .Where(run => !RunOutcomeClassifier.IsSuccessful(run))
                .Select(run => (double)run.FailedWave)),
            ClearedWaves = StatisticalMath.Describe(
                runs.Select(run => (double)run.ClearedWaveCount)),
            LeakDamage = StatisticalMath.Describe(
                runs.Select(run => (double)run.TotalLeakDamage)),
            GoldEarned = StatisticalMath.Describe(
                runs.Select(run => (double)run.GoldEarned)),
            GoldSpent = StatisticalMath.Describe(
                runs.Select(run => (double)run.GoldSpent)),
            GoldUnspent = StatisticalMath.Describe(
                runs.Select(run => (double)run.GoldUnspent)),
            TowerBuilds = StatisticalMath.Describe(
                runs.Select(run => (double)run.TowerBuildCount)),
            TowerUpgrades = StatisticalMath.Describe(
                runs.Select(run => (double)run.TowerUpgradeCount)),
            LogicalTicks = StatisticalMath.Describe(
                runs.Select(run => (double)run.TotalLogicalTicks)),
            MidWaveBuildRunRatio = runs.Count(run =>
                run.MidWaveTowerBuildCount > 0) / (double)runs.Count,
            SuccessfulRunMidWaveBuildRatio = wins == 0
                ? 0
                : runs.Count(run =>
                    RunOutcomeClassifier.IsSuccessful(run) &&
                    run.MidWaveTowerBuildCount > 0) / (double)wins,
            RejectedCommandRate = decisions == 0
                ? 0
                : runs.Sum(run => (long)run.RejectedCommandCount) /
                    (double)decisions,
            RejectedCommandRunCount = runs.Count(run =>
                run.RejectedCommandCount > 0),
            SafetyLimitRunRatio = runs.Count(run =>
                run.SafetyLimitReachedCount > 0) / (double)runs.Count,
            SafetyLimitFailureCount = runs.Count(run =>
                run.SafetyLimitReachedCount > 0),
            RuntimeFailureCount = runs.Count(
                RunOutcomeClassifier.HasRuntimeFailure),
            FailureWaveDistribution = runs
                .Where(run => !RunOutcomeClassifier.IsSuccessful(run))
                .GroupBy(run => run.FailedWave)
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count()),
            StartingTowerStats = CategoryStats(
                runs,
                run => string.IsNullOrWhiteSpace(run.SelectedStartingTower)
                    ? "(none)"
                    : run.SelectedStartingTower),
            CardStats = CardStats(runs),
            MeanTowerBuildsByDefinition = SumDictionaries(
                    runs.Select(run =>
                        (IDictionary<string, int>)run.TowerBuildsByDefinition))
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value / (double)runs.Count,
                    StringComparer.Ordinal),
            LeaksByEnemyType = SumDictionaries(
                runs.Select(run =>
                    (IDictionary<string, int>)run.LeaksByEnemyType)),
            CardExecutionCount = SumDictionaries(
                runs.Select(run =>
                    (IDictionary<string, long>)run.CardExecutionCount))
        };
        return report;
    }

    public IReadOnlyList<BatchStatisticalReport> AggregateGroups(
        IEnumerable<SimulationResult> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        return runs.GroupBy(run => new
            {
                run.DifficultyId,
                run.PolicyId,
                run.PolicyVersion,
                run.ScenarioId,
                run.ContentHash,
                run.DifficultyProfileHash
            })
            .OrderBy(group => group.Key.DifficultyId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.PolicyId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.ScenarioId, StringComparer.Ordinal)
            .Select(group => Aggregate(group.ToList()))
            .ToArray();
    }

    private static Dictionary<string, CategoryWinStatistics> CategoryStats(
        IReadOnlyList<SimulationResult> runs,
        Func<SimulationResult, string> category)
    {
        return runs.GroupBy(category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    int wins = group.Count(RunOutcomeClassifier.IsSuccessful);
                    return new CategoryWinStatistics
                    {
                        Runs = group.Count(),
                        Wins = wins,
                        SelectionRate = group.Count() / (double)runs.Count,
                        WinRate = wins / (double)group.Count(),
                        WinRateWilson95 = StatisticalMath.Wilson95(
                            wins,
                            group.Count())
                    };
                },
                StringComparer.Ordinal);
    }

    private static Dictionary<string, CategoryWinStatistics> CardStats(
        IReadOnlyList<SimulationResult> runs)
    {
        var observations = runs.SelectMany(run => run.SelectedCards
            .Concat(run.DraftChoices.Select(choice => choice.CardId))
            .Concat(run.CardPackChoices.Select(choice => choice.CardId))
            .Where(card => !string.IsNullOrWhiteSpace(card))
            .Distinct(StringComparer.Ordinal)
            .Select(card => (Card: card, Run: run)));
        return observations.GroupBy(value => value.Card, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    int count = group.Count();
                    int wins = group.Count(value =>
                        RunOutcomeClassifier.IsSuccessful(value.Run));
                    return new CategoryWinStatistics
                    {
                        Runs = count,
                        Wins = wins,
                        SelectionRate = count / (double)runs.Count,
                        WinRate = wins / (double)count,
                        WinRateWilson95 = StatisticalMath.Wilson95(wins, count)
                    };
                },
                StringComparer.Ordinal);
    }

    private static Dictionary<string, long> SumDictionaries<T>(
        IEnumerable<IDictionary<string, T>> dictionaries)
        where T : struct, IConvertible
    {
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (IDictionary<string, T> dictionary in dictionaries)
        {
            foreach ((string key, T value) in dictionary)
            {
                totals.TryGetValue(key, out long current);
                totals[key] = checked(
                    current + Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return totals;
    }

    private static void ValidateUniqueRuns(IReadOnlyList<SimulationResult> runs)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (SimulationResult run in runs)
        {
            string key = run.DifficultyId + "|" + run.PolicyId + "|" +
                run.ScenarioId + "|" + run.GameSeed + "|" + run.PolicySeed;
            if (!keys.Add(key))
            {
                throw new InvalidOperationException(
                    "Batch contains duplicate run identity: " + key);
            }
        }
    }

    private static void ValidateHomogeneous(
        IEnumerable<SimulationResult> runs,
        SimulationResult first)
    {
        foreach (SimulationResult run in runs)
        {
            if (!string.Equals(run.DifficultyId, first.DifficultyId, StringComparison.Ordinal) ||
                !string.Equals(run.PolicyId, first.PolicyId, StringComparison.Ordinal) ||
                !string.Equals(run.PolicyVersion, first.PolicyVersion, StringComparison.Ordinal) ||
                !string.Equals(run.ScenarioId, first.ScenarioId, StringComparison.Ordinal) ||
                !string.Equals(run.ContentHash, first.ContentHash, StringComparison.Ordinal) ||
                !string.Equals(
                    run.DifficultyProfileHash,
                    first.DifficultyProfileHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Batch aggregation requires one difficulty, policy, scenario, " +
                    "content hash, and profile hash.");
            }
        }
    }
}
