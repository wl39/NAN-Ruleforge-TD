using System.Globalization;
using System.Text;
using RuleforgeTD.BalanceCli.Balance;
using RuleforgeTD.BalanceCli.Evaluation;
using RuleforgeTD.BalanceCli.Infrastructure;
using RuleforgeTD.BalanceCli.Simulation;

namespace RuleforgeTD.BalanceCli.Commands;

public sealed class BatchArtifact
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string DifficultyId { get; set; } = string.Empty;
    public string RequestedPolicyId { get; set; } = string.Empty;
    public string SeedSet { get; set; } = string.Empty;
    public string SeedSetHash { get; set; } = string.Empty;
    public List<BatchStatisticalReport> Reports { get; set; } = new();
    public List<SimulationResult> Runs { get; set; } = new();
}

public sealed class EvaluationArtifact
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string SeedSet { get; set; } = string.Empty;
    public string TargetsHash { get; set; } = string.Empty;
    public string SeedSetsHash { get; set; } = string.Empty;
    public string PolicyLockHash { get; set; } = string.Empty;
    public string CardStrengthIndexPath { get; set; } = string.Empty;
    public string CardStrengthIndexHash { get; set; } = string.Empty;
    public string CardSynergyIndexPath { get; set; } = string.Empty;
    public string CardSynergyIndexHash { get; set; } = string.Empty;
    public List<EvaluationIndexEvidence> Indexes { get; set; } = new();
    public List<EvaluationProfileRecord> Profiles { get; set; } = new();
    public List<BatchStatisticalReport> PolicyReports { get; set; } = new();
    public List<DifficultyGateReport> DifficultyReports { get; set; } = new();
    public bool Passed => DifficultyReports.Count > 0 &&
        DifficultyReports.All(report => report.Passed);
}

public sealed class EvaluationIndexEvidence
{
    public string DifficultyId { get; set; } = string.Empty;
    public string IndexType { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
}

public sealed class EvaluationProfileRecord
{
    public string DifficultyId { get; set; } = string.Empty;
    public string ProfileHash { get; set; } = string.Empty;
    public DifficultyProfile Profile { get; set; } = new();
}

public sealed class VerificationCheck
{
    public string Id { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Detail { get; set; } = string.Empty;
}

public sealed class VerificationArtifact
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string BaseContentHash { get; set; } = string.Empty;
    public string PolicyLockHash { get; set; } = string.Empty;
    public string TargetsHash { get; set; } = string.Empty;
    public string SeedSetsHash { get; set; } = string.Empty;
    public List<VerificationCheck> Checks { get; set; } = new();
    public bool Passed => Checks.Count > 0 && Checks.All(check => check.Passed);
}

internal static class CommandArtifactWriter
{
    public static void WriteBatch(string directory, BatchArtifact artifact)
    {
        Directory.CreateDirectory(directory);
        JsonSupport.Write(Path.Combine(directory, "batch.json"), artifact);
        JsonSupport.Write(Path.Combine(directory, "report.json"), artifact.Reports);
        File.WriteAllText(
            Path.Combine(directory, "runs.csv"),
            RunsCsv(artifact.Runs),
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(directory, "report.md"),
            BatchMarkdown(artifact),
            new UTF8Encoding(false));
    }

    public static void WriteEvaluation(
        string directory,
        EvaluationArtifact artifact)
    {
        Directory.CreateDirectory(directory);
        JsonSupport.Write(Path.Combine(directory, "evaluation.json"), artifact);
        File.WriteAllText(
            Path.Combine(directory, "evaluation.csv"),
            GateCsv(artifact),
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(directory, "evaluation.md"),
            EvaluationMarkdown(artifact),
            new UTF8Encoding(false));
    }

    public static string SafeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ||
                character is '-' or '_' ? character : '-');
        }
        return builder.ToString();
    }

    public static string RunsCsv(IEnumerable<SimulationResult> runs)
    {
        var text = new StringBuilder();
        text.AppendLine(
            "runId,difficulty,policy,gameSeed,policySeed,result,remainingBaseHealth," +
            "clearedWaveCount,failedWave,leakDamage,goldEarned,goldSpent,goldUnspent," +
            "towerBuildCount,towerUpgradeCount,midWaveBuildCount,rejectedCommands," +
            "safetyLimitCount,totalLogicalTicks,finalStateHash");
        foreach (SimulationResult run in runs)
        {
            string[] values =
            {
                run.RunId,
                run.DifficultyId,
                run.PolicyId,
                run.GameSeed.ToString(CultureInfo.InvariantCulture),
                run.PolicySeed.ToString(CultureInfo.InvariantCulture),
                run.Result.ToString(),
                run.RemainingBaseHealth.ToString(CultureInfo.InvariantCulture),
                run.ClearedWaveCount.ToString(CultureInfo.InvariantCulture),
                run.FailedWave.ToString(CultureInfo.InvariantCulture),
                run.TotalLeakDamage.ToString(CultureInfo.InvariantCulture),
                run.GoldEarned.ToString(CultureInfo.InvariantCulture),
                run.GoldSpent.ToString(CultureInfo.InvariantCulture),
                run.GoldUnspent.ToString(CultureInfo.InvariantCulture),
                run.TowerBuildCount.ToString(CultureInfo.InvariantCulture),
                run.TowerUpgradeCount.ToString(CultureInfo.InvariantCulture),
                run.MidWaveTowerBuildCount.ToString(CultureInfo.InvariantCulture),
                run.RejectedCommandCount.ToString(CultureInfo.InvariantCulture),
                run.SafetyLimitReachedCount.ToString(CultureInfo.InvariantCulture),
                run.TotalLogicalTicks.ToString(CultureInfo.InvariantCulture),
                run.FinalStateHash
            };
            text.AppendLine(string.Join(',', values.Select(Csv)));
        }
        return text.ToString();
    }

    private static string GateCsv(EvaluationArtifact artifact)
    {
        var text = new StringBuilder();
        text.AppendLine(
            "difficulty,gate,metric,actual,minimum,maximum,wilsonLow,wilsonHigh,passed,source,message");
        foreach (DifficultyGateReport report in artifact.DifficultyReports)
        {
            foreach (DifficultyGateResult gate in report.Gates)
            {
                string[] values =
                {
                    report.DifficultyId,
                    gate.GateId,
                    gate.Metric,
                    Format(gate.Actual),
                    Format(gate.Minimum),
                    Format(gate.Maximum),
                    Format(gate.Wilson95?.Lower),
                    Format(gate.Wilson95?.Upper),
                    gate.Passed ? "true" : "false",
                    gate.EvidenceSource,
                    gate.Message
                };
                text.AppendLine(string.Join(',', values.Select(Csv)));
            }
        }
        return text.ToString();
    }

    private static string BatchMarkdown(BatchArtifact artifact)
    {
        var text = new StringBuilder();
        text.AppendLine("# Ruleforge TD batch report");
        text.AppendLine();
        text.AppendLine("- Difficulty: `" + artifact.DifficultyId + "`");
        text.AppendLine("- Requested policy: `" + artifact.RequestedPolicyId + "`");
        text.AppendLine("- Seed set: `" + artifact.SeedSet + "`");
        text.AppendLine("- Runs: " + artifact.Runs.Count);
        text.AppendLine();
        text.AppendLine("| Policy | Runs | Win rate | Wilson 95% | Median HP | P10 HP | Mid-wave build | Errors | Timeouts |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (BatchStatisticalReport report in artifact.Reports)
        {
            text.Append("| ").Append(report.PolicyId)
                .Append(" | ").Append(report.RunCount)
                .Append(" | ").Append(report.WinRate.ToString("P1", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(report.WinRateWilson95.Lower.ToString("P1", CultureInfo.InvariantCulture))
                .Append("–")
                .Append(report.WinRateWilson95.Upper.ToString("P1", CultureInfo.InvariantCulture))
                .Append(" | ").Append(report.RemainingHealth.Median.ToString("0.##", CultureInfo.InvariantCulture))
                .Append(" | ").Append(report.RemainingHealth.P10.ToString("0.##", CultureInfo.InvariantCulture))
                .Append(" | ").Append(report.MidWaveBuildRunRatio.ToString("P1", CultureInfo.InvariantCulture))
                .Append(" | ").Append(report.ErrorCount)
                .Append(" | ").Append(report.TimeoutCount)
                .AppendLine(" |");
        }
        return text.ToString();
    }

    private static string EvaluationMarkdown(EvaluationArtifact artifact)
    {
        var text = new StringBuilder();
        text.AppendLine("# Ruleforge TD difficulty evaluation");
        text.AppendLine();
        text.AppendLine("- Seed set: `" + artifact.SeedSet + "`");
        text.AppendLine("- Overall: **" + (artifact.Passed ? "PASS" : "FAIL") + "**");
        text.AppendLine();
        text.AppendLine("| Difficulty | Gate | Actual | Target | Result |");
        text.AppendLine("|---|---|---:|---:|---:|");
        foreach (DifficultyGateReport report in artifact.DifficultyReports)
        {
            foreach (DifficultyGateResult gate in report.Gates)
            {
                string target = (gate.Minimum.HasValue ? ">= " + Format(gate.Minimum) : "") +
                    (gate.Minimum.HasValue && gate.Maximum.HasValue ? ", " : "") +
                    (gate.Maximum.HasValue ? "<= " + Format(gate.Maximum) : "");
                text.Append("| ").Append(report.DifficultyId)
                    .Append(" | ").Append(gate.GateId)
                    .Append(" | ").Append(Format(gate.Actual))
                    .Append(" | ").Append(target)
                    .Append(" | ").Append(gate.Passed ? "PASS" : "FAIL")
                    .AppendLine(" |");
            }
        }
        return text.ToString();
    }

    private static string Format(double? value) => value.HasValue
        ? value.Value.ToString("0.####", CultureInfo.InvariantCulture)
        : string.Empty;

    private static string Csv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') &&
            !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
