using System;
using System.IO;
using System.Linq;
using RuleforgeTD.BalanceCli.Evaluation;
using RuleforgeTD.BalanceCli.Infrastructure;
using RuleforgeTD.BalanceCli.Policies;
using RuleforgeTD.BalanceCli.Simulation;
using RuleforgeTD.BalanceCli.Terminal;

namespace RuleforgeTD.BalanceCli.Commands;

internal static class SimulationCommands
{
    public static int Simulate(CliArguments arguments)
    {
        RepositoryPaths paths = CommandSupport.Paths(arguments);
        string difficulty = arguments.Get("difficulty", "current");
        string policyId = arguments.Get(
            "policy",
            "novice-random-spender");
        IPlayerPolicy policy = PolicyFactory.Create(policyId);
        ulong gameSeed = arguments.GetUlong("game-seed", 1001);
        ulong policySeed = arguments.GetUlong("policy-seed", 2001);
        bool captureReplay = !arguments.HasFlag("no-replay");
        SimulationScenario scenario = CommandSupport.Scenario(
            arguments,
            captureReplay);
        string stem = CommandArtifactWriter.SafeName(
            difficulty + "-" + policyId + "-" + gameSeed + "-" + policySeed);
        string output = arguments.Optional("output") is { } outputOption
            ? CommandSupport.ResolvePath(paths, outputOption)
            : Path.Combine(paths.BalanceArtifacts, "runs", stem + ".result.json");
        string? replay = captureReplay
            ? arguments.Optional("replay") is { } replayOption
                ? CommandSupport.ResolvePath(paths, replayOption)
                : Path.Combine(
                    paths.BalanceArtifacts,
                    "runs",
                    "replays",
                    stem + ".json")
            : null;
        var request = new SimulationRunRequest
        {
            DifficultyId = difficulty,
            PolicyId = policy.PolicyId,
            GameSeed = gameSeed,
            PolicySeed = policySeed,
            Scenario = scenario,
            ReplayOutputPath = replay,
            CardStrength = CommandSupport.LoadStrength(arguments, difficulty),
            CardSynergy = CommandSupport.LoadSynergy(arguments, difficulty)
        };
        bool live = arguments.Command == "watch" || arguments.HasFlag("live");
        LiveTerminalDashboard? dashboard = live
            ? new LiveTerminalDashboard(
                arguments.GetInt("ticks-per-second", 120),
                arguments.GetInt("refresh-ms", 100))
            : null;
        SimulationResult result;
        try
        {
            result = CommandSupport.Driver(paths)
                .Execute(
                    request,
                    policy,
                    progressObserver: dashboard)
                .Result;
        }
        finally
        {
            dashboard?.Dispose();
        }
        JsonSupport.Write(output, result);
        Console.WriteLine(
            result.Result + " | wave=" + result.ClearedWaveCount +
            " | hp=" + result.RemainingBaseHealth +
            " | gold=" + result.GoldUnspent +
            " | tick=" + result.TotalLogicalTicks +
            " | hash=" + result.FinalStateHash);
        Console.WriteLine("result: " + output);
        if (!string.IsNullOrEmpty(result.ReplayPath))
        {
            Console.WriteLine("replay: " + result.ReplayPath);
        }
        if (!string.IsNullOrEmpty(result.Error))
        {
            Console.Error.WriteLine(result.Error);
        }
        return RunOutcomeClassifier.HasRuntimeFailure(result)
                ? ExitCodes.SimulationFailure
                : ExitCodes.Success;
    }

    public static int Batch(CliArguments arguments)
    {
        RepositoryPaths paths = CommandSupport.Paths(arguments);
        string difficulty = arguments.Get("difficulty", "current");
        string policy = arguments.Get("policy", "novice-ensemble");
        IReadOnlyList<RuleforgeTD.BalanceCli.Evaluation.SeedPair> seeds =
            CommandSupport.Seeds(
            paths,
            arguments,
            "validation",
            out string seedSet);
        string outputDirectory = arguments.Optional("output-dir") is { } output
            ? CommandSupport.ResolvePath(paths, output)
            : CommandSupport.DefaultArtifactDirectory(
                paths,
                "batch",
                difficulty,
                policy,
                seedSet);
        string? replayDirectory = arguments.HasFlag("replays")
            ? Path.Combine(outputDirectory, "replays")
            : null;
        SimulationScenario scenario = CommandSupport.Scenario(
            arguments,
            replayDirectory != null);
        List<SimulationResult> runs = CommandSupport.RunBatch(
            paths,
            difficulty,
            policy,
            seeds,
            scenario,
            arguments,
            replayDirectory);
        BatchArtifact artifact = CommandSupport.BuildBatchArtifact(
            difficulty,
            policy,
            seedSet,
            paths.SeedSets,
            runs);
        CommandArtifactWriter.WriteBatch(outputDirectory, artifact);
        foreach (RuleforgeTD.BalanceCli.Evaluation.BatchStatisticalReport report
                 in artifact.Reports)
        {
            Console.WriteLine(
                report.DifficultyId + " " + report.PolicyId +
                ": " + report.VictoryCount + "/" + report.RunCount +
                " wins (" + report.WinRate.ToString("P1") + ")");
        }
        Console.WriteLine("artifacts: " + outputDirectory);
        return runs.Any(RunOutcomeClassifier.HasRuntimeFailure)
                ? ExitCodes.SimulationFailure
                : ExitCodes.Success;
    }

    public static int Replay(CliArguments arguments)
    {
        RepositoryPaths paths = CommandSupport.Paths(arguments);
        string replayPath = CommandSupport.ResolvePath(
            paths,
            CommandSupport.RequireNonEmpty(arguments, "replay"));
        ReplayVerificationResult result = new ReplayRunner(
            new RuleforgeTD.BalanceCli.Content.HeadlessContentLoader(paths))
            .Run(replayPath);
        result.ReplayPath = replayPath;
        string output = arguments.Optional("output") is { } outputOption
            ? CommandSupport.ResolvePath(paths, outputOption)
            : Path.ChangeExtension(replayPath, ".verification.json");
        JsonSupport.Write(output, result);
        Console.WriteLine(
            (result.Matches ? "MATCH" : "MISMATCH") +
            " | result=" + result.Result +
            " | phase=" + result.FinalPhase +
            " | tick=" + result.FinalTick +
            " | hash=" + result.FinalStateHash);
        foreach (string mismatch in result.Mismatches.Take(20))
        {
            Console.Error.WriteLine("- " + mismatch);
        }
        Console.WriteLine("verification: " + output);
        return result.Matches ? ExitCodes.Success : ExitCodes.GateFailure;
    }
}
