using System;
using System.Collections.Generic;
using System.Linq;
using RuleforgeTD.BalanceCli.Evaluation;
using RuleforgeTD.BalanceCli.Simulation;

namespace RuleforgeTD.BalanceCli.Balance;

public sealed class MatchedSeedDelta
{
    public SeedPair Seed { get; set; }
    public SimulationOutcome BeforeOutcome { get; set; }
    public SimulationOutcome AfterOutcome { get; set; }
    public int WinDelta { get; set; }
    public int RemainingHealthDelta { get; set; }
    public int ClearedWaveDelta { get; set; }
    public int LeakDamageDelta { get; set; }
    public int GoldSpentDelta { get; set; }
    public int MidWaveBuildDelta { get; set; }
    public long LogicalTickDelta { get; set; }
}

public sealed class MatchedSeedComparisonReport
{
    public int MatchedSeedCount { get; set; }
    public double BeforeWinRate { get; set; }
    public double AfterWinRate { get; set; }
    public double WinRateDelta { get; set; }
    public WilsonInterval BeforeWilson95 { get; set; } = new(0, 1);
    public WilsonInterval AfterWilson95 { get; set; } = new(0, 1);
    public int LossToWinCount { get; set; }
    public int WinToLossCount { get; set; }
    public double MeanRemainingHealthDelta { get; set; }
    public double MeanClearedWaveDelta { get; set; }
    public double MeanLeakDamageDelta { get; set; }
    public double MeanGoldSpentDelta { get; set; }
    public double MeanMidWaveBuildDelta { get; set; }
    public double MeanLogicalTickDelta { get; set; }
    public List<MatchedSeedDelta> Rows { get; set; } = new();
}

public sealed class MatchedSeedComparer
{
    public MatchedSeedComparisonReport Compare(
        IReadOnlyList<SimulationResult> before,
        IReadOnlyList<SimulationResult> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        Dictionary<SeedPair, SimulationResult> beforeBySeed = Index(before, "before");
        Dictionary<SeedPair, SimulationResult> afterBySeed = Index(after, "after");
        SeedPair[] missingAfter = beforeBySeed.Keys.Except(afterBySeed.Keys).ToArray();
        SeedPair[] missingBefore = afterBySeed.Keys.Except(beforeBySeed.Keys).ToArray();
        if (missingAfter.Length > 0 || missingBefore.Length > 0)
        {
            throw new InvalidOperationException(
                "Matched-seed sets differ. Missing after: " +
                string.Join(",", missingAfter) + "; missing before: " +
                string.Join(",", missingBefore) + ".");
        }
        if (beforeBySeed.Count == 0)
        {
            throw new ArgumentException("Matched comparison needs at least one seed.");
        }

        var rows = new List<MatchedSeedDelta>(beforeBySeed.Count);
        foreach (SeedPair seed in beforeBySeed.Keys
                     .OrderBy(value => value.GameSeed)
                     .ThenBy(value => value.PolicySeed))
        {
            SimulationResult left = beforeBySeed[seed];
            SimulationResult right = afterBySeed[seed];
            bool leftWin = RunOutcomeClassifier.IsSuccessful(left);
            bool rightWin = RunOutcomeClassifier.IsSuccessful(right);
            rows.Add(new MatchedSeedDelta
            {
                Seed = seed,
                BeforeOutcome = left.Result,
                AfterOutcome = right.Result,
                WinDelta = (rightWin ? 1 : 0) - (leftWin ? 1 : 0),
                RemainingHealthDelta =
                    right.RemainingBaseHealth - left.RemainingBaseHealth,
                ClearedWaveDelta = right.ClearedWaveCount - left.ClearedWaveCount,
                LeakDamageDelta = right.TotalLeakDamage - left.TotalLeakDamage,
                GoldSpentDelta = right.GoldSpent - left.GoldSpent,
                MidWaveBuildDelta =
                    right.MidWaveTowerBuildCount - left.MidWaveTowerBuildCount,
                LogicalTickDelta = right.TotalLogicalTicks - left.TotalLogicalTicks
            });
        }

        int beforeWins = before.Count(RunOutcomeClassifier.IsSuccessful);
        int afterWins = after.Count(RunOutcomeClassifier.IsSuccessful);
        return new MatchedSeedComparisonReport
        {
            MatchedSeedCount = rows.Count,
            BeforeWinRate = beforeWins / (double)rows.Count,
            AfterWinRate = afterWins / (double)rows.Count,
            WinRateDelta = (afterWins - beforeWins) / (double)rows.Count,
            BeforeWilson95 = StatisticalMath.Wilson95(beforeWins, rows.Count),
            AfterWilson95 = StatisticalMath.Wilson95(afterWins, rows.Count),
            LossToWinCount = rows.Count(row => row.WinDelta > 0),
            WinToLossCount = rows.Count(row => row.WinDelta < 0),
            MeanRemainingHealthDelta = rows.Average(row => row.RemainingHealthDelta),
            MeanClearedWaveDelta = rows.Average(row => row.ClearedWaveDelta),
            MeanLeakDamageDelta = rows.Average(row => row.LeakDamageDelta),
            MeanGoldSpentDelta = rows.Average(row => row.GoldSpentDelta),
            MeanMidWaveBuildDelta = rows.Average(row => row.MidWaveBuildDelta),
            MeanLogicalTickDelta = rows.Average(row => row.LogicalTickDelta),
            Rows = rows
        };
    }

    private static Dictionary<SeedPair, SimulationResult> Index(
        IEnumerable<SimulationResult> runs,
        string label)
    {
        var result = new Dictionary<SeedPair, SimulationResult>();
        foreach (SimulationResult run in runs)
        {
            var seed = new SeedPair(run.GameSeed, run.PolicySeed);
            if (!result.TryAdd(seed, run))
            {
                throw new InvalidOperationException(
                    label + " contains duplicate seed pair " + seed + ".");
            }
        }
        return result;
    }
}
