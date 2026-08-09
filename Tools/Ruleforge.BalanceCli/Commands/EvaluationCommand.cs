using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RuleforgeTD.BalanceCli.Balance;
using RuleforgeTD.BalanceCli.Content;
using RuleforgeTD.BalanceCli.Evaluation;
using RuleforgeTD.BalanceCli.Infrastructure;
using RuleforgeTD.BalanceCli.Simulation;

namespace RuleforgeTD.BalanceCli.Commands;

internal static class EvaluationCommand
{
    public static int Run(CliArguments arguments)
    {
        RepositoryPaths paths = CommandSupport.Paths(arguments);
        IReadOnlyList<SeedPair> seeds = CommandSupport.Seeds(
            paths,
            arguments,
            "validation",
            out string seedSet);
        if (arguments.HasFlag("all-difficulties") &&
            arguments.Optional("difficulty") != null)
        {
            throw new CliUsageException(
                "Use either --all-difficulties or --difficulty, not both.");
        }
        string[] difficulties = arguments.HasFlag("all-difficulties")
            ? new[] { "easy", "medium", "hard" }
            : new[] { arguments.Get("difficulty", "current") };
        using JsonDocument targets = JsonDocument.Parse(
            File.ReadAllText(paths.BalanceTargets));
        var artifact = new EvaluationArtifact
        {
            SeedSet = seedSet,
            TargetsHash = JsonSupport.Sha256File(paths.BalanceTargets),
            SeedSetsHash = JsonSupport.Sha256File(paths.SeedSets),
            PolicyLockHash = JsonSupport.Sha256File(paths.PolicyLock)
        };
        foreach (string difficulty in difficulties)
        {
            AddOptionalIndexEvidence(paths, arguments, difficulty, artifact);
            EvaluateDifficulty(
                paths,
                arguments,
                targets.RootElement,
                difficulty,
                seedSet,
                seeds,
                artifact);
        }

        string outputDirectory = arguments.Optional("output-dir") is { } output
            ? CommandSupport.ResolvePath(paths, output)
            : CommandSupport.DefaultArtifactDirectory(
                paths,
                "evaluation",
                string.Join("-", difficulties),
                seedSet);
        CommandArtifactWriter.WriteEvaluation(outputDirectory, artifact);
        foreach (DifficultyGateReport report in artifact.DifficultyReports)
        {
            Console.WriteLine(
                report.DifficultyId + ": " +
                (report.Passed ? "PASS" : "FAIL") + " (" +
                report.PassedGateCount + " passed, " +
                report.FailedGateCount + " failed)");
        }
        Console.WriteLine("artifacts: " + outputDirectory);
        return artifact.Passed ? ExitCodes.Success : ExitCodes.GateFailure;
    }

    private static void EvaluateDifficulty(
        RepositoryPaths paths,
        CliArguments arguments,
        JsonElement targets,
        string difficulty,
        string seedSet,
        IReadOnlyList<SeedPair> seeds,
        EvaluationArtifact artifact)
    {
        LoadedSimulationContent loaded = new HeadlessContentLoader(paths).Load(
            difficulty,
            CommandSupport.Scenario(arguments, false));
        ValidateExternalIndexes(
            paths,
            arguments,
            difficulty,
            seedSet,
            loaded);
        artifact.Profiles.Add(new EvaluationProfileRecord
        {
            DifficultyId = difficulty,
            ProfileHash = loaded.DifficultyProfileHash,
            Profile = loaded.Profile
        });
        string[] policyIds = difficulty switch
        {
            "easy" => new[] { "novice-ensemble", "no-spend", "card-fixture" },
            "medium" => new[]
            {
                "novice-ensemble", "good-standalone", "synergy-tactical"
            },
            "hard" => new[]
            {
                "novice-ensemble", "good-standalone", "synergy-tactical",
                "synergy-no-combat-build", "synergy-disabled", "oracle-search"
            },
            _ => new[] { arguments.Get("policy", "novice-random-spender") }
        };
        var reports = new Dictionary<string, BatchStatisticalReport>(
            StringComparer.Ordinal);
        bool runtimeValid = true;
        foreach (string requestedPolicy in policyIds)
        {
            List<SimulationResult> runs = CommandSupport.RunBatch(
                paths,
                difficulty,
                requestedPolicy,
                seeds,
                CommandSupport.Scenario(arguments, false),
                arguments);
            runtimeValid &= runs.All(run =>
                run.Result is not
                    (SimulationOutcome.Error or SimulationOutcome.Timeout) &&
                run.SafetyLimitReachedCount == 0 &&
                run.RejectedCommandCount == 0);
            var evaluator = new BatchEvaluator();
            if (requestedPolicy == "novice-ensemble")
            {
                foreach (BatchStatisticalReport member in
                         evaluator.AggregateGroups(runs))
                {
                    reports[member.PolicyId] = member;
                    artifact.PolicyReports.Add(member);
                }
                BatchStatisticalReport ensemble = evaluator.Aggregate(
                    runs,
                    requireHomogeneousBatch: false);
                ensemble.PolicyId = "novice-ensemble";
                ensemble.PolicyVersion = "ensemble-1";
                reports[ensemble.PolicyId] = ensemble;
                artifact.PolicyReports.Add(ensemble);
            }
            else
            {
                BatchStatisticalReport report = evaluator.Aggregate(runs);
                reports[requestedPolicy] = report;
                artifact.PolicyReports.Add(report);
            }
        }

        var definitions = new List<DifficultyGateDefinition>();
        var evidence = new List<DifficultyMetricEvidence>();
        AddGate(
            definitions,
            evidence,
            "runtime-valid",
            "runtime.valid",
            runtimeValid ? 1 : 0,
            1,
            1,
            "simulation results");
        if (targets.TryGetProperty(difficulty, out JsonElement target))
        {
            AddFrozenGates(
                difficulty,
                target,
                reports,
                arguments,
                definitions,
                evidence);
        }
        artifact.DifficultyReports.Add(new DifficultyGateEvaluator().Evaluate(
            difficulty,
            seedSet,
            definitions,
            evidence));
    }

    private static void AddOptionalIndexEvidence(
        RepositoryPaths paths,
        CliArguments arguments,
        string difficulty,
        EvaluationArtifact artifact)
    {
        string? strength = CommandSupport.ResolveIndexPath(
            paths,
            arguments,
            "card-strength",
            difficulty);
        if (!string.IsNullOrWhiteSpace(strength))
        {
            string hash = JsonSupport.Sha256File(strength);
            artifact.Indexes.Add(new EvaluationIndexEvidence
            {
                DifficultyId = difficulty,
                IndexType = "card-strength",
                Path = strength,
                Hash = hash
            });
            if (!string.IsNullOrWhiteSpace(arguments.Optional("card-strength")))
            {
                artifact.CardStrengthIndexPath = strength;
                artifact.CardStrengthIndexHash = hash;
            }
        }
        string? synergy = CommandSupport.ResolveIndexPath(
            paths,
            arguments,
            "card-synergy",
            difficulty);
        if (!string.IsNullOrWhiteSpace(synergy))
        {
            string hash = JsonSupport.Sha256File(synergy);
            artifact.Indexes.Add(new EvaluationIndexEvidence
            {
                DifficultyId = difficulty,
                IndexType = "card-synergy",
                Path = synergy,
                Hash = hash
            });
            if (!string.IsNullOrWhiteSpace(arguments.Optional("card-synergy")))
            {
                artifact.CardSynergyIndexPath = synergy;
                artifact.CardSynergyIndexHash = hash;
            }
        }
        if (difficulty == "easy")
        {
            string? coverage = CommandSupport.ResolveIndexPath(
                paths,
                arguments,
                "card-coverage",
                difficulty);
            if (!string.IsNullOrWhiteSpace(coverage))
            {
                artifact.Indexes.Add(new EvaluationIndexEvidence
                {
                    DifficultyId = difficulty,
                    IndexType = "card-coverage",
                    Path = coverage,
                    Hash = JsonSupport.Sha256File(coverage)
                });
            }
        }
    }

    private static void ValidateExternalIndexes(
        RepositoryPaths paths,
        CliArguments arguments,
        string difficulty,
        string seedSet,
        LoadedSimulationContent loaded)
    {
        bool strict = arguments.HasFlag("strict-indices") ||
            (!arguments.HasFlag("allow-bootstrap-indices") &&
             seedSet is "validation" or "holdout");
        if (!strict || difficulty == "current")
        {
            return;
        }

        int minimumSamples = arguments.GetInt("minimum-index-samples", 2);
        if (minimumSamples < 1)
        {
            throw new CliUsageException(
                "--minimum-index-samples must be positive.");
        }
        string strengthPath = CommandSupport.ResolveIndexPath(
            paths,
            arguments,
            "card-strength",
            difficulty) ??
            throw new InvalidDataException(
                "Strict " + difficulty + " evaluation requires --card-strength-" +
                difficulty + " (or --card-strength).");
        CardStrengthIndex strength = JsonSupport.Read<CardStrengthIndex>(
            strengthPath);
        strength.Validate();
        if (!string.Equals(
                strength.ContentHash,
                loaded.CompiledContentHash,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Card strength content hash does not match the " +
                difficulty + " compiled content.");
        }
        CardStrengthEntry[] strengthEntries = strength.Entries
            .Where(entry => entry.IsEvaluable && string.Equals(
                entry.DifficultyId,
                difficulty,
                StringComparison.Ordinal))
            .ToArray();
        if (strengthEntries.Length == 0 || strengthEntries.Any(entry =>
                entry.Lift.MatchedSeedCount < minimumSamples))
        {
            throw new InvalidDataException(
                "Card strength index has no sufficiently sampled evaluable " +
                difficulty +
                " entries.");
        }
        if (difficulty == "easy")
        {
            string[] missingCards = loaded.Content.Cards
                .Select(card => card.StableId)
                .Where(cardId => !strengthEntries.Any(entry => string.Equals(
                    entry.CardId,
                    cardId,
                    StringComparison.Ordinal)))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (missingCards.Length > 0)
            {
                throw new InvalidDataException(
                    "Easy card strength index lacks an evaluable path for: " +
                    string.Join(", ", missingCards) + ".");
            }

            string coveragePath = CommandSupport.ResolveIndexPath(
                paths,
                arguments,
                "card-coverage",
                difficulty) ?? throw new InvalidDataException(
                    "Strict easy evaluation requires --card-coverage-easy.");
            CardCoverageReport coverage =
                JsonSupport.Read<CardCoverageReport>(coveragePath);
            ValidateCoverageEvidence(
                coverage,
                loaded,
                minimumSamples);
        }

        if (difficulty != "hard")
        {
            return;
        }
        string synergyPath = CommandSupport.ResolveIndexPath(
            paths,
            arguments,
            "card-synergy",
            difficulty) ??
            throw new InvalidDataException(
                "Strict hard evaluation requires --card-synergy-hard " +
                "(or --card-synergy).");
        CardSynergyIndex synergy = JsonSupport.Read<CardSynergyIndex>(
            synergyPath);
        synergy.Validate();
        if (!string.Equals(
                synergy.ContentHash,
                loaded.CompiledContentHash,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Card synergy content hash does not match the hard compiled " +
                "content.");
        }
        CardSynergyEntry[] synergyEntries = synergy.Entries
            .Where(entry => string.Equals(
                entry.DifficultyId,
                difficulty,
                StringComparison.Ordinal))
            .ToArray();
        if (synergyEntries.Length == 0 || synergyEntries.Any(entry =>
                entry.SynergyLift.MatchedSeedCount < minimumSamples))
        {
            throw new InvalidDataException(
                "Card synergy index has no sufficiently sampled hard entries.");
        }
    }

    private static void ValidateCoverageEvidence(
        CardCoverageReport coverage,
        LoadedSimulationContent loaded,
        int minimumSamples)
    {
        if (!string.Equals(
                coverage.DifficultyId,
                "easy",
                StringComparison.Ordinal) ||
            !string.Equals(
                coverage.ContentHash,
                loaded.CompiledContentHash,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Easy card coverage does not match the compiled Easy content.");
        }
        if (coverage.SeedCount < minimumSamples)
        {
            throw new InvalidDataException(
                "Easy card coverage has fewer matched seeds than required.");
        }

        string[] activeCards = loaded.Content.Cards
            .Select(card => card.StableId)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var byCard = coverage.Cards
            .GroupBy(card => card.CardId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(),
                StringComparer.Ordinal);
        string[] invalid = activeCards.Where(cardId =>
                !byCard.TryGetValue(cardId, out CardCoverageEntry[]? entries) ||
                entries.Length != 1 ||
                entries[0].LegalExperimentCount == 0 ||
                entries[0].ValidExperimentCount == 0)
            .ToArray();
        if (invalid.Length > 0)
        {
            throw new InvalidDataException(
                "Easy card coverage lacks a valid legal path for: " +
                string.Join(", ", invalid) + ".");
        }
    }

    private static void AddFrozenGates(
        string difficulty,
        JsonElement target,
        IReadOnlyDictionary<string, BatchStatisticalReport> reports,
        CliArguments arguments,
        ICollection<DifficultyGateDefinition> definitions,
        ICollection<DifficultyMetricEvidence> evidence)
    {
        if (difficulty == "easy")
        {
            AddReportTargets(
                "novice-ensemble",
                target.GetProperty("noviceEnsemble"),
                reports["novice-ensemble"],
                definitions,
                evidence);
            foreach (string policy in
                     RuleforgeTD.BalanceCli.Policies.PolicyFactory.NoviceEnsembleIds)
            {
                AddWinRateGate(
                    "each-novice-" + policy,
                    target.GetProperty("eachNovicePolicy"),
                    reports[policy],
                    definitions,
                    evidence);
            }
            AddWinRateGate(
                "no-spend",
                target.GetProperty("noSpendPolicy"),
                reports["no-spend"],
                definitions,
                evidence);
            AddWinRateGate(
                "all-starting-cards",
                target.GetProperty("cardCoverage"),
                reports["card-fixture"],
                definitions,
                evidence,
                minimumName: "allStartingCardsWinRateMin");
            AddCardCoverageGate(
                target.GetProperty("cardCoverage"),
                arguments,
                definitions,
                evidence);
            return;
        }
        if (difficulty == "medium")
        {
            AddReportTargets(
                "good-standalone",
                target.GetProperty("goodStandalonePolicy"),
                reports["good-standalone"],
                definitions,
                evidence);
            AddWinRateGate(
                "novice-ensemble",
                target.GetProperty("noviceEnsemble"),
                reports["novice-ensemble"],
                definitions,
                evidence);
            AddWinRateGate(
                "synergy-tactical",
                target.GetProperty("synergyTacticalPolicy"),
                reports["synergy-tactical"],
                definitions,
                evidence);
            return;
        }
        if (difficulty == "hard")
        {
            AddReportTargets(
                "synergy-tactical",
                target.GetProperty("synergyTacticalPolicy"),
                reports["synergy-tactical"],
                definitions,
                evidence);
            AddWinRateGate(
                "good-standalone",
                target.GetProperty("goodStandalonePolicy"),
                reports["good-standalone"],
                definitions,
                evidence);
            AddWinRateGate(
                "novice-ensemble",
                target.GetProperty("noviceEnsemble"),
                reports["novice-ensemble"],
                definitions,
                evidence);
            AddWinRateGate(
                "synergy-no-combat-build",
                target.GetProperty("synergyNoCombatBuildPolicy"),
                reports["synergy-no-combat-build"],
                definitions,
                evidence);
            AddWinRateDropGate(
                "no-combat-build-drop",
                target.GetProperty("synergyNoCombatBuildPolicy"),
                reports["synergy-tactical"],
                reports["synergy-no-combat-build"],
                definitions,
                evidence);
            AddWinRateGate(
                "synergy-disabled",
                target.GetProperty("synergyDisabledPolicy"),
                reports["synergy-disabled"],
                definitions,
                evidence);
            AddWinRateDropGate(
                "synergy-disabled-drop",
                target.GetProperty("synergyDisabledPolicy"),
                reports["synergy-tactical"],
                reports["synergy-disabled"],
                definitions,
                evidence);
            AddWinRateGate(
                "oracle-search",
                target.GetProperty("oracleSearchPolicy"),
                reports["oracle-search"],
                definitions,
                evidence);
            AddGate(
                definitions,
                evidence,
                "successful-mid-wave-build-ratio",
                "successfulRunsWithMidWaveBuildRatio",
                reports["synergy-tactical"].SuccessfulRunMidWaveBuildRatio,
                target.GetProperty(
                    "successfulRunsWithMidWaveBuildRatioMin").GetDouble(),
                null,
                "synergy-tactical");
        }
    }

    private static void AddReportTargets(
        string id,
        JsonElement target,
        BatchStatisticalReport report,
        ICollection<DifficultyGateDefinition> definitions,
        ICollection<DifficultyMetricEvidence> evidence)
    {
        AddWinRateGate(id, target, report, definitions, evidence);
        if (target.TryGetProperty("medianRemainingHealthMin", out JsonElement min) ||
            target.TryGetProperty("medianRemainingHealthMax", out _))
        {
            double? minimum = min.ValueKind == JsonValueKind.Number
                ? min.GetDouble()
                : null;
            double? maximum = target.TryGetProperty(
                "medianRemainingHealthMax",
                out JsonElement max)
                    ? max.GetDouble()
                    : null;
            AddGate(
                definitions,
                evidence,
                id + "-median-health",
                id + ".medianRemainingHealth",
                report.VictoryRemainingHealth.Median,
                minimum,
                maximum,
                report.PolicyId);
        }
        if (target.TryGetProperty("p10RemainingHealthMin", out JsonElement p10))
        {
            AddGate(
                definitions,
                evidence,
                id + "-p10-health",
                id + ".p10RemainingHealth",
                report.VictoryRemainingHealth.P10,
                p10.GetDouble(),
                null,
                report.PolicyId);
        }
    }

    private static void AddWinRateGate(
        string id,
        JsonElement target,
        BatchStatisticalReport report,
        ICollection<DifficultyGateDefinition> definitions,
        ICollection<DifficultyMetricEvidence> evidence,
        string minimumName = "winRateMin")
    {
        double? minimum = target.TryGetProperty(
            minimumName,
            out JsonElement min)
                ? min.GetDouble()
                : null;
        double? maximum = target.TryGetProperty(
            "winRateMax",
            out JsonElement max)
                ? max.GetDouble()
                : null;
        AddGate(
            definitions,
            evidence,
            id + "-win-rate",
            id + ".winRate",
            report.WinRate,
            minimum,
            maximum,
            report.PolicyId,
            report.WinRateWilson95);
    }

    private static void AddWinRateDropGate(
        string id,
        JsonElement target,
        BatchStatisticalReport full,
        BatchStatisticalReport control,
        ICollection<DifficultyGateDefinition> definitions,
        ICollection<DifficultyMetricEvidence> evidence)
    {
        AddGate(
            definitions,
            evidence,
            id,
            id,
            full.WinRate - control.WinRate,
            target.GetProperty(
                "minimumWinRateDropFromFullPolicy").GetDouble(),
            null,
            full.PolicyId + " - " + control.PolicyId);
    }

    private static void AddCardCoverageGate(
        JsonElement target,
        CliArguments arguments,
        ICollection<DifficultyGateDefinition> definitions,
        ICollection<DifficultyMetricEvidence> evidence)
    {
        string metric = "cardCoverage.minimumViablePathWinRate";
        definitions.Add(new DifficultyGateDefinition
        {
            GateId = "active-card-viable-path",
            Metric = metric,
            Minimum = target.GetProperty("activeCardPathWinRateMin").GetDouble(),
            Required = target.GetProperty(
                "eachActiveCardHasViablePath").GetBoolean()
        });
        RepositoryPaths paths = CommandSupport.Paths(arguments);
        string? path = CommandSupport.ResolveIndexPath(
            paths,
            arguments,
            "card-coverage",
            "easy");
        if (path == null)
        {
            return;
        }
        CardCoverageReport coverage =
            JsonSupport.Read<CardCoverageReport>(path);
        LoadedSimulationContent loaded = new HeadlessContentLoader(paths)
            .Load("easy", SimulationScenario.Standard());
        if (!string.Equals(
                coverage.ContentHash,
                loaded.CompiledContentHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                coverage.DifficultyId,
                "easy",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Easy card coverage evidence targets different content.");
        }
        string[] activeCards = loaded.Content.Cards
            .Select(card => card.StableId)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, double> bestWinRate = coverage.Cards
            .GroupBy(entry => entry.CardId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Max(entry => entry.BestCandidateWinRate),
                StringComparer.Ordinal);
        double minimum = activeCards.Length == 0
            ? 0
            : activeCards.Select(cardId =>
                    bestWinRate.TryGetValue(cardId, out double value)
                        ? value
                        : 0d)
                .Min();
        evidence.Add(new DifficultyMetricEvidence
        {
            Metric = metric,
            Value = minimum,
            Source = path
        });
    }

    private static void AddGate(
        ICollection<DifficultyGateDefinition> definitions,
        ICollection<DifficultyMetricEvidence> evidence,
        string gateId,
        string metric,
        double value,
        double? minimum,
        double? maximum,
        string source,
        WilsonInterval? interval = null)
    {
        definitions.Add(new DifficultyGateDefinition
        {
            GateId = gateId,
            Metric = metric,
            Minimum = minimum,
            Maximum = maximum
        });
        evidence.Add(new DifficultyMetricEvidence
        {
            Metric = metric,
            Value = value,
            Wilson95 = interval,
            Source = source
        });
    }
}
