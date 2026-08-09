using System;
using System.Collections.Generic;
using System.Linq;

namespace RuleforgeTD.BalanceCli.Evaluation;

public sealed class DifficultyGateDefinition
{
    public string GateId { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public bool Required { get; set; } = true;
    public string Description { get; set; } = string.Empty;
}

public sealed class DifficultyMetricEvidence
{
    public string Metric { get; set; } = string.Empty;
    public double Value { get; set; }
    public WilsonInterval? Wilson95 { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class DifficultyGateResult
{
    public string GateId { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public double? Actual { get; set; }
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public WilsonInterval? Wilson95 { get; set; }
    public bool Passed { get; set; }
    public bool MissingEvidence { get; set; }
    public double Violation { get; set; }
    public string EvidenceSource { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class DifficultyGateReport
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string DifficultyId { get; set; } = string.Empty;
    public string SeedSet { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public int PassedGateCount { get; set; }
    public int FailedGateCount { get; set; }
    public double TotalViolation { get; set; }
    public List<DifficultyGateResult> Gates { get; set; } = new();
}

/// <summary>
/// Evaluates frozen target definitions against named evidence. Derived metrics
/// such as hard policy win-rate drops are supplied explicitly, preventing the
/// evaluator from guessing which reports should be combined.
/// </summary>
public sealed class DifficultyGateEvaluator
{
    public DifficultyGateReport Evaluate(
        string difficultyId,
        string seedSet,
        IReadOnlyList<DifficultyGateDefinition> definitions,
        IReadOnlyList<DifficultyMetricEvidence> evidence)
    {
        if (string.IsNullOrWhiteSpace(difficultyId))
        {
            throw new ArgumentException("difficultyId is required.");
        }
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(evidence);
        var byMetric = evidence.ToDictionary(
            value => value.Metric,
            StringComparer.Ordinal);
        var gateIds = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<DifficultyGateResult>(definitions.Count);
        foreach (DifficultyGateDefinition definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.GateId) ||
                !gateIds.Add(definition.GateId))
            {
                throw new InvalidOperationException(
                    "Difficulty gate ids must be non-empty and unique.");
            }
            if (!definition.Minimum.HasValue && !definition.Maximum.HasValue)
            {
                throw new InvalidOperationException(
                    "Gate '" + definition.GateId + "' has no bound.");
            }

            if (!byMetric.TryGetValue(
                    definition.Metric,
                    out DifficultyMetricEvidence? observed))
            {
                results.Add(new DifficultyGateResult
                {
                    GateId = definition.GateId,
                    Metric = definition.Metric,
                    Minimum = definition.Minimum,
                    Maximum = definition.Maximum,
                    MissingEvidence = true,
                    Passed = !definition.Required,
                    Violation = definition.Required ? 1 : 0,
                    Message = definition.Required
                        ? "Required metric evidence is missing."
                        : "Optional metric evidence is missing."
                });
                continue;
            }

            double below = definition.Minimum.HasValue
                ? Math.Max(0, definition.Minimum.Value - observed.Value)
                : 0;
            double above = definition.Maximum.HasValue
                ? Math.Max(0, observed.Value - definition.Maximum.Value)
                : 0;
            double violation = below + above;
            results.Add(new DifficultyGateResult
            {
                GateId = definition.GateId,
                Metric = definition.Metric,
                Actual = observed.Value,
                Minimum = definition.Minimum,
                Maximum = definition.Maximum,
                Wilson95 = observed.Wilson95,
                EvidenceSource = observed.Source,
                Passed = violation <= 1e-12,
                Violation = violation,
                Message = violation <= 1e-12
                    ? "Target satisfied."
                    : "Metric is outside the frozen target range."
            });
        }

        return new DifficultyGateReport
        {
            DifficultyId = difficultyId,
            SeedSet = seedSet,
            Passed = results.All(result => result.Passed),
            PassedGateCount = results.Count(result => result.Passed),
            FailedGateCount = results.Count(result => !result.Passed),
            TotalViolation = results.Sum(result => result.Violation),
            Gates = results
        };
    }

    public static DifficultyMetricEvidence WinRateEvidence(
        string metric,
        BatchStatisticalReport report,
        string source = "") => new()
    {
        Metric = metric,
        Value = report.WinRate,
        Wilson95 = report.WinRateWilson95,
        Source = string.IsNullOrWhiteSpace(source)
            ? report.PolicyId + "@" + report.ScenarioId
            : source
    };
}
