using System;
using RuleforgeTD.BalanceCli.Simulation;

namespace RuleforgeTD.BalanceCli.Evaluation;

/// <summary>
/// Applies the release-gate interpretation of an authoritative run. The raw
/// SimulationOutcome is preserved in artifacts, while safety truncation and
/// policy command rejection make that run an effective loss for statistics.
/// </summary>
public static class RunOutcomeClassifier
{
    public static bool IsSuccessful(SimulationResult run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.Result == SimulationOutcome.Victory &&
            !HasRuntimeFailure(run);
    }

    public static bool HasRuntimeFailure(SimulationResult run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.Result is SimulationOutcome.Error or
                SimulationOutcome.Timeout ||
            run.SafetyLimitReachedCount > 0 ||
            run.RejectedCommandCount > 0;
    }
}
