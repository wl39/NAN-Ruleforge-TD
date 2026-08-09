using System;
using System.Collections.Generic;
using RuleforgeTD.BalanceCli.Infrastructure;

namespace RuleforgeTD.BalanceCli.Balance;

public sealed class BalancePatch
{
    public int SchemaVersion { get; set; } = 1;
    public string ProposalId { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public string SourceProfileHash { get; set; } = string.Empty;
    public List<BalanceDiagnosis> Diagnosis { get; set; } = new();
    public List<BalanceChange> Changes { get; set; } = new();
    public List<ExpectedBalanceEffect> ExpectedEffects { get; set; } = new();
    public List<string> Risks { get; set; } = new();
    public bool NeedsStructuralReview { get; set; }
}

public sealed class BalanceDiagnosis
{
    public string Metric { get; set; } = string.Empty;
    public double Actual { get; set; }
    public string Target { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
}

public sealed class BalanceChange
{
    public string JsonPointer { get; set; } = string.Empty;
    public long OldValue { get; set; }
    public long NewValue { get; set; }
    public double? ChangePercent { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
}

public sealed class ExpectedBalanceEffect
{
    public string Metric { get; set; } = string.Empty;
    public BalanceEffectDirection Direction { get; set; }
}

public enum BalanceEffectDirection
{
    Increase = 0,
    Decrease = 1,
    Stabilize = 2
}

public sealed class BalancePatchValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string ComputedSourceProfileHash { get; set; } = string.Empty;
}

public sealed class BalancePatchValidationException : InvalidOperationException
{
    public BalancePatchValidationException(BalancePatchValidationResult result)
        : base("Invalid balance patch:\n" + string.Join("\n", result.Errors))
    {
        Result = result;
    }

    public BalancePatchValidationResult Result { get; }
}

public sealed class BalancePatchApplicationResult
{
    public required BalancePatch Patch { get; init; }
    public required DifficultyProfile Source { get; init; }
    public required DifficultyProfile Candidate { get; init; }
    public required BalancePatchValidationResult Validation { get; init; }
    public string CandidateProfileHash { get; init; } = string.Empty;
}

public static class BalanceProfileHasher
{
    public static string Compute(DifficultyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return JsonSupport.Sha256Text(JsonSupport.SerializeStable(profile));
    }
}
