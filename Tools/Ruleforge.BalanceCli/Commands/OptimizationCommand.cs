using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using RuleforgeTD.BalanceCli.Balance;
using RuleforgeTD.BalanceCli.Evaluation;
using RuleforgeTD.BalanceCli.Infrastructure;
using RuleforgeTD.BalanceCli.Policies;
using RuleforgeTD.BalanceCli.Simulation;

namespace RuleforgeTD.BalanceCli.Commands;

public sealed class OptimizationCommandArtifact
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string DifficultyId { get; set; } = string.Empty;
    public string PolicyId { get; set; } = string.Empty;
    public string TrainSeedSet { get; set; } = string.Empty;
    public string ValidationSeedSet { get; set; } = string.Empty;
    public string SourceProfileHash { get; set; } = string.Empty;
    public string BaseContentHash { get; set; } = string.Empty;
    public string SeedSetsHash { get; set; } = string.Empty;
    public string TargetsHash { get; set; } = string.Empty;
    public string PolicyLockHash { get; set; } = string.Empty;
    public BalanceOptimizationResult? TrainOptimization { get; set; }
    public BatchStatisticalReport? ValidationBefore { get; set; }
    public BatchStatisticalReport? ValidationAfter { get; set; }
    public MatchedSeedComparisonReport? ValidationMatchedSeeds { get; set; }
    public BalanceObjectiveMeasurement? ValidationBeforeObjective { get; set; }
    public BalanceObjectiveMeasurement? ValidationAfterObjective { get; set; }
    public bool ValidationApproved { get; set; }
    public bool AppliedToRepository { get; set; }
    public string DecisionReason { get; set; } = string.Empty;
}

internal static class OptimizationCommand
{
    public static int Run(CliArguments arguments)
    {
        RepositoryPaths paths = CommandSupport.Paths(arguments);
        string difficulty = arguments.Get("difficulty", "hard");
        string policyId = arguments.Get("policy", DefaultPolicy(difficulty));
        string trainSetName = arguments.Get("seed-set", "train");
        string validationSetName = arguments.Get(
            "validation-seed-set",
            "validation");
        if (string.Equals(trainSetName, "holdout", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                validationSetName,
                "holdout",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new CliUsageException(
                "optimize cannot inspect holdout seeds; use train and " +
                "validation only.");
        }
        if (string.Equals(
                trainSetName,
                validationSetName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new CliUsageException(
                "Training and validation seed sets must be distinct.");
        }
        SeedSetDocument seedDocument = SeedSetLoader.Load(paths.SeedSets);
        int seedLimit = arguments.GetInt("limit", int.MaxValue);
        SeedPair[] trainSeeds = TakeSeeds(
            seedDocument.Get(trainSetName),
            seedLimit);
        SeedPair[] validationSeeds = TakeSeeds(
            seedDocument.Get(validationSetName),
            seedLimit);
        DifficultyProfile source = JsonSupport.ReadStrict<DifficultyProfile>(
            paths.Profile(difficulty));
        DifficultyProfileValidator.Validate(source, difficulty);
        using JsonDocument targets = JsonDocument.Parse(
            File.ReadAllText(paths.BalanceTargets));
        JsonElement target = targets.RootElement.GetProperty(difficulty);
        SimulationScenario scenario = CommandSupport.Scenario(arguments, false);

        BatchStatisticalReport Measure(
            DifficultyProfile profile,
            IReadOnlyList<SeedPair> seeds)
        {
            List<SimulationResult> runs = CommandSupport.RunBatch(
                paths,
                difficulty,
                policyId,
                seeds,
                scenario,
                arguments,
                replayDirectory: null,
                profileOverride: profile);
            return AggregateRequested(policyId, runs);
        }

        BalanceObjectiveMeasurement Objective(
            DifficultyProfile profile,
            IReadOnlyList<SeedPair> seeds,
            string evidence)
        {
            BatchStatisticalReport report = Measure(profile, seeds);
            return ScoreObjective(difficulty, target, report, evidence);
        }

        BalanceObjectiveMeasurement trainBaseline = Objective(
            source,
            trainSeeds,
            "train baseline");
        var generator = new DeterministicCoordinateDescentCandidateGenerator();
        int candidateLimit = arguments.GetInt("candidate-limit", 6);
        if (candidateLimit < 1)
        {
            throw new CliUsageException("--candidate-limit must be positive.");
        }
        IReadOnlyList<CoordinateDescentCandidate> generated = generator.Generate(
            source,
            difficulty + "-coordinate",
            trainBaseline.Penalty,
            options: new CoordinateDescentCandidateOptions
            {
                StepPercent = CommandSupport.GetDouble(
                    arguments,
                    "step-percent",
                    5)
            });
        BalancePatch[] patches = generated
            .Take(candidateLimit)
            .Select(candidate => candidate.Patch)
            .ToArray();
        if (patches.Length == 0)
        {
            Console.Error.WriteLine(
                "No schema-valid bounded coordinate candidate was generated; " +
                "the source profile was not changed.");
            return ExitCodes.DataError;
        }
        var optimizer = new BalanceOptimizer();
        BalanceOptimizationResult optimization = optimizer.OptimizeAsync(
                source,
                patches,
                (profile, cancellationToken) =>
                    ValueTask.FromResult(Objective(
                        profile,
                        trainSeeds,
                        "train matched-seed candidate")))
            .GetAwaiter().GetResult();
        var artifact = new OptimizationCommandArtifact
        {
            DifficultyId = difficulty,
            PolicyId = policyId,
            TrainSeedSet = trainSetName,
            ValidationSeedSet = validationSetName,
            SourceProfileHash = BalanceProfileHasher.Compute(source),
            BaseContentHash = new RuleforgeTD.BalanceCli.Content.HeadlessContentLoader(
                paths)
                .ComputeBaseContentHash(),
            SeedSetsHash = JsonSupport.Sha256File(paths.SeedSets),
            TargetsHash = JsonSupport.Sha256File(paths.BalanceTargets),
            PolicyLockHash = File.Exists(paths.PolicyLock)
                ? JsonSupport.Sha256File(paths.PolicyLock)
                : string.Empty,
            TrainOptimization = optimization
        };

        if (optimization.SelectedProfile == null)
        {
            artifact.DecisionReason =
                "All bounded candidates failed validation, evaluation, or " +
                "matched-seed train improvement.";
        }
        else
        {
            List<SimulationResult> beforeRuns = CommandSupport.RunBatch(
                paths,
                difficulty,
                policyId,
                validationSeeds,
                scenario,
                arguments,
                profileOverride: source);
            List<SimulationResult> afterRuns = CommandSupport.RunBatch(
                paths,
                difficulty,
                policyId,
                validationSeeds,
                scenario,
                arguments,
                profileOverride: optimization.SelectedProfile);
            artifact.ValidationBefore = AggregateRequested(policyId, beforeRuns);
            artifact.ValidationAfter = AggregateRequested(policyId, afterRuns);
            if (PolicyFactory.Expand(policyId).Count == 1)
            {
                artifact.ValidationMatchedSeeds =
                    new MatchedSeedComparer().Compare(beforeRuns, afterRuns);
            }
            artifact.ValidationBeforeObjective = ScoreObjective(
                difficulty,
                target,
                artifact.ValidationBefore,
                "validation before");
            artifact.ValidationAfterObjective = ScoreObjective(
                difficulty,
                target,
                artifact.ValidationAfter,
                "validation after");
            artifact.ValidationApproved =
                artifact.ValidationAfterObjective.Penalty <
                    artifact.ValidationBeforeObjective.Penalty &&
                artifact.ValidationAfterObjective.PassesAllGates &&
                artifact.ValidationAfter.ErrorCount == 0 &&
                artifact.ValidationAfter.TimeoutCount == 0;
            artifact.DecisionReason = artifact.ValidationApproved
                ? "Candidate improved the frozen objective on both train and " +
                    "validation matched seeds. Difficulty profiles are isolated, " +
                    "so other named profiles and their compiled hashes are unchanged."
                : "Candidate did not both improve the validation objective and " +
                    "satisfy every measured target, or it produced a timeout/error; " +
                    "it was rejected.";
            if (PolicyFactory.Expand(policyId).Count > 1)
            {
                artifact.DecisionReason += " The ensemble was aggregated across " +
                    "its member policies; a single-policy MatchedSeedComparison " +
                    "row set is therefore intentionally omitted.";
            }
            if (artifact.ValidationApproved &&
                arguments.HasFlag("apply-approved"))
            {
                string recoveryDirectory = arguments.Optional("output-dir") is
                    { } recoveryOutput
                        ? CommandSupport.ResolvePath(paths, recoveryOutput)
                        : CommandSupport.DefaultArtifactDirectory(
                            paths,
                            "iterations",
                            difficulty,
                            trainSetName,
                            validationSetName);
                Directory.CreateDirectory(recoveryDirectory);
                JsonSupport.Write(
                    Path.Combine(recoveryDirectory, "source.profile.json"),
                    source);
                JsonSupport.Write(
                    Path.Combine(recoveryDirectory, "approved.profile.json"),
                    optimization.SelectedProfile);
                JsonSupport.Write(
                    paths.Profile(difficulty),
                    optimization.SelectedProfile);
                artifact.AppliedToRepository = true;
            }
        }

        string directory = arguments.Optional("output-dir") is { } output
            ? CommandSupport.ResolvePath(paths, output)
            : CommandSupport.DefaultArtifactDirectory(
                paths,
                "iterations",
                difficulty,
                trainSetName,
                validationSetName);
        Directory.CreateDirectory(directory);
        JsonSupport.Write(Path.Combine(directory, "optimization.json"), artifact);
        if (optimization.SelectedProfile != null)
        {
            JsonSupport.Write(
                Path.Combine(directory, "candidate.profile.json"),
                optimization.SelectedProfile);
        }
        WriteReport(directory, artifact);
        Console.WriteLine(
            "train candidates: " + patches.Length +
            " | selected: " + (optimization.SelectedPatch?.ProposalId ?? "none") +
            " | validation: " +
            (artifact.ValidationApproved ? "APPROVED" : "REJECTED"));
        Console.WriteLine("artifacts: " + directory);
        bool approvalRequired = arguments.HasFlag("require-approval") ||
            arguments.HasFlag("apply-approved");
        return approvalRequired && !artifact.ValidationApproved
            ? ExitCodes.GateFailure
            : ExitCodes.Success;
    }

    private static BatchStatisticalReport AggregateRequested(
        string policyId,
        IReadOnlyList<SimulationResult> runs)
    {
        var evaluator = new BatchEvaluator();
        if (PolicyFactory.Expand(policyId).Count == 1)
        {
            return evaluator.Aggregate(runs);
        }
        BatchStatisticalReport report = evaluator.Aggregate(
            runs,
            requireHomogeneousBatch: false);
        report.PolicyId = policyId;
        report.PolicyVersion = "ensemble-1";
        return report;
    }

    private static BalanceObjectiveMeasurement ScoreObjective(
        string difficulty,
        JsonElement target,
        BatchStatisticalReport report,
        string evidence)
    {
        var components = new Dictionary<string, double>(StringComparer.Ordinal);
        void AddRange(
            string id,
            double value,
            double? minimum,
            double? maximum,
            double weight)
        {
            double violation = minimum.HasValue && value < minimum.Value
                ? minimum.Value - value
                : maximum.HasValue && value > maximum.Value
                    ? value - maximum.Value
                    : 0;
            components[id] = violation * weight;
        }

        JsonElement policyTarget;
        if (difficulty == "easy")
        {
            policyTarget = target.GetProperty("noviceEnsemble");
        }
        else if (difficulty == "medium")
        {
            policyTarget = target.GetProperty("goodStandalonePolicy");
        }
        else
        {
            policyTarget = target.GetProperty("synergyTacticalPolicy");
        }
        AddRange(
            "winRate",
            report.WinRate,
            Optional(policyTarget, "winRateMin"),
            Optional(policyTarget, "winRateMax"),
            100);
        AddRange(
            "medianRemainingHealth",
            report.VictoryRemainingHealth.Median,
            Optional(policyTarget, "medianRemainingHealthMin"),
            Optional(policyTarget, "medianRemainingHealthMax"),
            2);
        AddRange(
            "p10RemainingHealth",
            report.VictoryRemainingHealth.P10,
            Optional(policyTarget, "p10RemainingHealthMin"),
            Optional(policyTarget, "p10RemainingHealthMax"),
            1);
        if (difficulty == "hard")
        {
            AddRange(
                "successfulMidWaveBuildRatio",
                report.SuccessfulRunMidWaveBuildRatio,
                target.GetProperty(
                    "successfulRunsWithMidWaveBuildRatioMin").GetDouble(),
                null,
                50);
        }
        components["runtimeFailure"] = report.RuntimeFailureCount * 1000.0;
        double penalty = components.Values.Sum();
        return new BalanceObjectiveMeasurement
        {
            Penalty = penalty,
            PassesAllGates = penalty <= 1e-12,
            Components = components,
            EvidenceArtifact = evidence
        };
    }

    private static double? Optional(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
            ? value.GetDouble()
            : null;

    private static SeedPair[] TakeSeeds(
        IReadOnlyList<SeedPair> seeds,
        int requested)
    {
        int count = requested == int.MaxValue ? seeds.Count : requested;
        if (count < 1 || count > seeds.Count)
        {
            throw new CliUsageException(
                "--limit must be between 1 and " + seeds.Count + ".");
        }
        return seeds.Take(count).ToArray();
    }

    private static string DefaultPolicy(string difficulty) =>
        difficulty switch
        {
            "easy" => "novice-ensemble",
            "medium" => "good-standalone",
            _ => "synergy-tactical"
        };

    private static void WriteReport(
        string directory,
        OptimizationCommandArtifact artifact)
    {
        var markdown = new StringBuilder();
        markdown.AppendLine("# Bounded balance optimization")
            .AppendLine()
            .AppendLine("- Difficulty: `" + artifact.DifficultyId + "`")
            .AppendLine("- Policy: `" + artifact.PolicyId + "`")
            .AppendLine("- Validation decision: **" +
                (artifact.ValidationApproved ? "APPROVED" : "REJECTED") +
                "**")
            .AppendLine("- Repository profile changed: " +
                (artifact.AppliedToRepository ? "yes" : "no"))
            .AppendLine()
            .AppendLine(artifact.DecisionReason)
            .AppendLine()
            .AppendLine("| Metric | Before | After |")
            .AppendLine("|---|---:|---:|");
        if (artifact.ValidationBefore != null &&
            artifact.ValidationAfter != null)
        {
            markdown.AppendLine("| Win rate | " +
                artifact.ValidationBefore.WinRate.ToString(
                    "P1", CultureInfo.InvariantCulture) + " | " +
                artifact.ValidationAfter.WinRate.ToString(
                    "P1", CultureInfo.InvariantCulture) + " |")
                .AppendLine("| Median victory HP | " +
                    artifact.ValidationBefore.VictoryRemainingHealth.Median
                        .ToString("0.##", CultureInfo.InvariantCulture) + " | " +
                    artifact.ValidationAfter.VictoryRemainingHealth.Median
                        .ToString("0.##", CultureInfo.InvariantCulture) + " |")
                .AppendLine("| Objective penalty | " +
                    artifact.ValidationBeforeObjective!.Penalty.ToString(
                        "0.####", CultureInfo.InvariantCulture) + " | " +
                    artifact.ValidationAfterObjective!.Penalty.ToString(
                        "0.####", CultureInfo.InvariantCulture) + " |");
        }
        File.WriteAllText(
            Path.Combine(directory, "optimization.md"),
            markdown.ToString(),
            new UTF8Encoding(false));

        var csv = new StringBuilder();
        csv.AppendLine("proposalId,disposition,penalty,passesAllGates,reason");
        foreach (BalanceCandidateTrial trial in
                 artifact.TrainOptimization?.Trials ??
                 new List<BalanceCandidateTrial>())
        {
            csv.AppendLine(string.Join(',', new[]
            {
                trial.ProposalId,
                trial.Disposition.ToString(),
                trial.Measurement?.Penalty.ToString(
                    "0.####", CultureInfo.InvariantCulture) ?? string.Empty,
                trial.Measurement?.PassesAllGates == true ? "true" : "false",
                '"' + trial.Reason.Replace("\"", "\"\"") + '"'
            }));
        }
        File.WriteAllText(
            Path.Combine(directory, "optimization.csv"),
            csv.ToString(),
            new UTF8Encoding(false));
    }
}
