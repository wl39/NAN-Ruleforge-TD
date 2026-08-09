using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RuleforgeTD.BalanceCli.Balance;

public sealed class BalanceObjectiveMeasurement
{
    public double Penalty { get; set; }
    public bool PassesAllGates { get; set; }
    public Dictionary<string, double> Components { get; set; } =
        new(StringComparer.Ordinal);
    public string EvidenceArtifact { get; set; } = string.Empty;
}

public delegate ValueTask<BalanceObjectiveMeasurement> BalanceCandidateEvaluator(
    DifficultyProfile profile,
    CancellationToken cancellationToken);

public enum BalanceCandidateDisposition
{
    RejectedValidation = 0,
    RejectedEvaluation = 1,
    RejectedNoImprovement = 2,
    Eligible = 3,
    Selected = 4
}

public sealed class BalanceCandidateTrial
{
    public required string ProposalId { get; init; }
    public BalanceCandidateDisposition Disposition { get; set; }
    public BalancePatchValidationResult? Validation { get; set; }
    public BalanceObjectiveMeasurement? Measurement { get; set; }
    public string CandidateProfileHash { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class BalanceOptimizationResult
{
    public required DifficultyProfile SourceProfile { get; init; }
    public required BalanceObjectiveMeasurement Baseline { get; init; }
    public DifficultyProfile? SelectedProfile { get; set; }
    public BalancePatch? SelectedPatch { get; set; }
    public BalanceObjectiveMeasurement? SelectedMeasurement { get; set; }
    public List<BalanceCandidateTrial> Trials { get; set; } = new();

    public bool Improved => SelectedProfile != null;
}

/// <summary>
/// Deterministic bounded candidate search. Candidate generation is deliberately
/// external; this class guarantees every candidate is schema/range validated,
/// applied to a clone, and measured before it can be selected.
/// </summary>
public sealed class BalanceOptimizer
{
    private readonly BalanceProposalValidator validator;

    public BalanceOptimizer(BalanceProposalValidator? validator = null)
    {
        this.validator = validator ?? new BalanceProposalValidator();
    }

    public async Task<BalanceOptimizationResult> OptimizeAsync(
        DifficultyProfile source,
        IEnumerable<BalancePatch> proposals,
        BalanceCandidateEvaluator evaluator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(proposals);
        ArgumentNullException.ThrowIfNull(evaluator);

        BalanceObjectiveMeasurement baseline = await evaluator(
            source,
            cancellationToken).ConfigureAwait(false);
        ValidateMeasurement(baseline, "baseline");
        var result = new BalanceOptimizationResult
        {
            SourceProfile = source,
            Baseline = baseline
        };

        var eligible = new List<(
            BalancePatch Patch,
            DifficultyProfile Profile,
            BalanceObjectiveMeasurement Measurement,
            BalanceCandidateTrial Trial)>();
        foreach (BalancePatch patch in proposals.OrderBy(
                     value => value.ProposalId,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trial = new BalanceCandidateTrial
            {
                ProposalId = patch.ProposalId
            };
            result.Trials.Add(trial);
            BalancePatchApplicationResult application;
            try
            {
                application = validator.Apply(source, patch);
                trial.Validation = application.Validation;
                trial.CandidateProfileHash = application.CandidateProfileHash;
            }
            catch (BalancePatchValidationException exception)
            {
                trial.Disposition = BalanceCandidateDisposition.RejectedValidation;
                trial.Validation = exception.Result;
                trial.Reason = exception.Message;
                continue;
            }
            catch (Exception exception)
            {
                trial.Disposition = BalanceCandidateDisposition.RejectedValidation;
                trial.Reason = exception.GetType().Name + ": " + exception.Message;
                continue;
            }

            try
            {
                BalanceObjectiveMeasurement measurement = await evaluator(
                    application.Candidate,
                    cancellationToken).ConfigureAwait(false);
                ValidateMeasurement(measurement, patch.ProposalId);
                trial.Measurement = measurement;
                if (measurement.Penalty >= baseline.Penalty)
                {
                    trial.Disposition =
                        BalanceCandidateDisposition.RejectedNoImprovement;
                    trial.Reason = "Candidate penalty did not improve the baseline.";
                    continue;
                }
                trial.Disposition = BalanceCandidateDisposition.Eligible;
                eligible.Add((patch, application.Candidate, measurement, trial));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                trial.Disposition = BalanceCandidateDisposition.RejectedEvaluation;
                trial.Reason = exception.GetType().Name + ": " + exception.Message;
            }
        }

        var selected = eligible
            .OrderBy(value => value.Measurement.Penalty)
            .ThenByDescending(value => value.Measurement.PassesAllGates)
            .ThenBy(value => value.Patch.Changes.Count)
            .ThenBy(value => value.Patch.ProposalId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (selected.Patch != null)
        {
            selected.Trial.Disposition = BalanceCandidateDisposition.Selected;
            result.SelectedPatch = selected.Patch;
            result.SelectedProfile = selected.Profile;
            result.SelectedMeasurement = selected.Measurement;
        }
        return result;
    }

    private static void ValidateMeasurement(
        BalanceObjectiveMeasurement measurement,
        string source)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        if (double.IsNaN(measurement.Penalty) ||
            double.IsInfinity(measurement.Penalty) ||
            measurement.Penalty < 0)
        {
            throw new InvalidOperationException(
                "Objective penalty for '" + source +
                "' must be a finite non-negative number.");
        }
    }
}
