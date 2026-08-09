using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RuleforgeTD.BalanceCli.Balance;
using RuleforgeTD.BalanceCli.Content;
using RuleforgeTD.BalanceCli.Evaluation;
using RuleforgeTD.BalanceCli.Infrastructure;
using RuleforgeTD.BalanceCli.Policies;
using RuleforgeTD.BalanceCli.Simulation;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.BalanceCli.Commands;

internal static class CommandSupport
{
    public static RepositoryPaths Paths(CliArguments arguments) =>
        RepositoryPaths.Discover(arguments.Optional("repo"));

    public static HeadlessRunDriver Driver(RepositoryPaths paths) =>
        new(new HeadlessContentLoader(paths));

    public static SimulationScenario Scenario(
        CliArguments arguments,
        bool captureReplay)
    {
        var scenario = SimulationScenario.Standard();
        scenario.ScenarioId = arguments.Get("scenario-id", "standard");
        scenario.MaximumLogicalTicks = arguments.GetInt(
            "max-ticks",
            scenario.MaximumLogicalTicks);
        scenario.MaximumDecisions = arguments.GetInt(
            "max-decisions",
            scenario.MaximumDecisions);
        scenario.CaptureReplay = captureReplay;
        scenario.CaptureTelemetry = true;
        scenario.ForcedStartingTowerId = arguments.Optional("starting-tower");
        scenario.ForcedPlacedTowerId = arguments.Optional("placed-tower");
        string? subject = arguments.Optional("subject");
        if (subject != null)
        {
            scenario.ForcedSubjectType = ParseSubject(subject);
        }
        return scenario;
    }

    public static SubjectType ParseSubject(string value)
    {
        if (Enum.TryParse(value, true, out SubjectType subject))
        {
            return subject;
        }
        throw new CliUsageException(
            "Unknown subject '" + value + "'. Expected Projectile or Enemy.");
    }

    public static IReadOnlyList<SeedPair> Seeds(
        RepositoryPaths paths,
        CliArguments arguments,
        string defaultSet,
        out string seedSetName)
    {
        seedSetName = arguments.Get("seed-set", defaultSet);
        SeedSetDocument document = SeedSetLoader.Load(paths.SeedSets);
        IReadOnlyList<SeedPair> selected = document.Get(seedSetName);
        int limit = arguments.GetInt("limit", selected.Count);
        if (limit < 1 || limit > selected.Count)
        {
            throw new CliUsageException(
                "--limit must be between 1 and " + selected.Count + ".");
        }
        return selected.Take(limit).ToArray();
    }

    public static RuleforgeTD.BalanceCli.Policies.CardStrengthIndex
        LoadStrength(CliArguments arguments, string difficulty)
    {
        RepositoryPaths paths = Paths(arguments);
        string? path = ResolveIndexPath(
            paths,
            arguments,
            "card-strength",
            difficulty);
        if (string.IsNullOrWhiteSpace(path))
        {
            return new RuleforgeTD.BalanceCli.Policies.CardStrengthIndex();
        }
        RuleforgeTD.BalanceCli.Evaluation.CardStrengthIndex discovered =
            JsonSupport.Read<RuleforgeTD.BalanceCli.Evaluation.CardStrengthIndex>(
                path);
        discovered.Validate();
        return discovered.ToPolicyIndex();
    }

    public static RuleforgeTD.BalanceCli.Policies.CardSynergyIndex
        LoadSynergy(CliArguments arguments, string difficulty)
    {
        RepositoryPaths paths = Paths(arguments);
        string? path = ResolveIndexPath(
            paths,
            arguments,
            "card-synergy",
            difficulty);
        if (string.IsNullOrWhiteSpace(path))
        {
            return new RuleforgeTD.BalanceCli.Policies.CardSynergyIndex();
        }
        RuleforgeTD.BalanceCli.Evaluation.CardSynergyIndex discovered =
            JsonSupport.Read<RuleforgeTD.BalanceCli.Evaluation.CardSynergyIndex>(
                path);
        discovered.Validate();
        return discovered.ToPolicyIndex();
    }

    public static List<SimulationResult> RunBatch(
        RepositoryPaths paths,
        string difficulty,
        string requestedPolicy,
        IReadOnlyList<SeedPair> seeds,
        SimulationScenario scenario,
        CliArguments arguments,
        string? replayDirectory = null,
        DifficultyProfile? profileOverride = null)
    {
        IReadOnlyList<IPlayerPolicy> policies = PolicyFactory.Expand(
            requestedPolicy);
        HeadlessRunDriver driver = Driver(paths);
        var runs = new List<SimulationResult>(seeds.Count * policies.Count);
        RuleforgeTD.BalanceCli.Policies.CardStrengthIndex strength =
            LoadStrength(arguments, difficulty);
        RuleforgeTD.BalanceCli.Policies.CardSynergyIndex synergy =
            LoadSynergy(arguments, difficulty);
        int total = seeds.Count * policies.Count;
        int completed = 0;
        foreach (IPlayerPolicy policy in policies)
        {
            foreach (SeedPair seed in seeds)
            {
                string? replayPath = replayDirectory == null
                    ? null
                    : Path.Combine(
                        replayDirectory,
                        CommandArtifactWriter.SafeName(
                            difficulty + "-" + policy.PolicyId + "-" +
                            seed.GameSeed + "-" + seed.PolicySeed) + ".json");
                SimulationScenario runScenario = scenario.Clone();
                runScenario.CaptureReplay = replayPath != null;
                var request = new SimulationRunRequest
                {
                    DifficultyId = difficulty,
                    DifficultyProfileOverride = profileOverride,
                    PolicyId = policy.PolicyId,
                    GameSeed = seed.GameSeed,
                    PolicySeed = seed.PolicySeed,
                    Scenario = runScenario,
                    ReplayOutputPath = replayPath,
                    WriteReplay = replayPath != null,
                    WriteResult = false,
                    CardStrength = strength,
                    CardSynergy = synergy
                };
                SimulationResult result = driver.Execute(request, policy).Result;
                runs.Add(result);
                completed++;
                if (total <= 16 || completed == total || completed % 16 == 0)
                {
                    Console.Error.WriteLine(
                        "[" + completed + "/" + total + "] " +
                        difficulty + " " + policy.PolicyId + " " +
                        seed + " => " + result.Result);
                }
            }
        }
        return runs;
    }

    public static BatchArtifact BuildBatchArtifact(
        string difficulty,
        string requestedPolicy,
        string seedSet,
        string seedSetPath,
        IReadOnlyList<SimulationResult> runs)
    {
        var evaluator = new BatchEvaluator();
        return new BatchArtifact
        {
            DifficultyId = difficulty,
            RequestedPolicyId = requestedPolicy,
            SeedSet = seedSet,
            SeedSetHash = JsonSupport.Sha256File(seedSetPath),
            Reports = evaluator.AggregateGroups(runs).ToList(),
            Runs = runs.ToList()
        };
    }

    public static string ResolvePath(RepositoryPaths paths, string path) =>
        Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(paths.Root, path));

    /// <summary>
    /// Resolves a difficulty-specific index first, then the legacy generic
    /// option, and finally the frozen final-artifact convention. The final
    /// fallback keeps the documented no-argument validation command usable
    /// without pretending that indexes from different difficulty overlays
    /// share a compiled-content hash.
    /// </summary>
    public static string? ResolveIndexPath(
        RepositoryPaths paths,
        CliArguments arguments,
        string option,
        string difficulty)
    {
        string? configured = arguments.Optional(option + "-" + difficulty) ??
            arguments.Optional(option);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return ResolvePath(paths, configured);
        }

        string fileName = option switch
        {
            "card-strength" => "card-strength-index.json",
            "card-synergy" => "card-synergy-index.json",
            "card-coverage" => "card-coverage.json",
            _ => throw new ArgumentException(
                "Unknown index option '" + option + "'.",
                nameof(option))
        };
        string conventional = Path.Combine(
            paths.BalanceArtifacts,
            "final",
            "indices",
            difficulty,
            fileName);
        return File.Exists(conventional) ? conventional : null;
    }

    public static string DefaultArtifactDirectory(
        RepositoryPaths paths,
        string command,
        params string[] parts)
    {
        string name = string.Join(
            "-",
            parts.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(CommandArtifactWriter.SafeName));
        return Path.Combine(
            paths.BalanceArtifacts,
            command,
            string.IsNullOrEmpty(name) ? "result" : name);
    }

    public static string RequireNonEmpty(CliArguments arguments, string key)
    {
        string value = arguments.Require(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliUsageException("--" + key + " cannot be empty.");
        }
        return value;
    }

    public static double GetDouble(
        CliArguments arguments,
        string key,
        double fallback)
    {
        string? raw = arguments.Optional(key);
        return raw == null
            ? fallback
            : double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
