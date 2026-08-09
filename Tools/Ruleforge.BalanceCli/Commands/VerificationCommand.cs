using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using RuleforgeTD.BalanceCli.Balance;
using RuleforgeTD.BalanceCli.Content;
using RuleforgeTD.BalanceCli.Evaluation;
using RuleforgeTD.BalanceCli.Infrastructure;
using RuleforgeTD.BalanceCli.Policies;
using RuleforgeTD.BalanceCli.Simulation;

namespace RuleforgeTD.BalanceCli.Commands;

internal static class VerificationCommand
{
    private static readonly string[] FrozenFiles =
    {
        "Assets/Game/Data/Balance/balance-targets.json",
        "Assets/Game/Data/Balance/current.profile.json",
        "Assets/Game/Data/Balance/easy.profile.json",
        "Assets/Game/Data/Balance/hard.profile.json",
        "Assets/Game/Data/Balance/medium.profile.json",
        "Assets/Game/Data/Balance/seed-sets.json",
        "Tools/Ruleforge.BalanceCli/Policies/DeterministicPolicies.cs",
        "Tools/Ruleforge.BalanceCli/Policies/PolicyContracts.cs",
        "Tools/Ruleforge.BalanceCli/Prompts/balance-director.md",
        "Tools/Ruleforge.BalanceCli/Prompts/player-easy.md",
        "Tools/Ruleforge.BalanceCli/Prompts/player-hard.md",
        "Tools/Ruleforge.BalanceCli/Prompts/player-medium.md"
    };

    public static int Run(CliArguments arguments)
    {
        RepositoryPaths paths = CommandSupport.Paths(arguments);
        var artifact = new VerificationArtifact
        {
            TargetsHash = ExistingHash(paths.BalanceTargets),
            SeedSetsHash = ExistingHash(paths.SeedSets),
            PolicyLockHash = ExistingHash(paths.PolicyLock)
        };
        var loader = new HeadlessContentLoader(paths);
        Check(artifact, "content-load", () =>
        {
            artifact.BaseContentHash = loader.ComputeBaseContentHash();
            foreach (string id in new[] { "current", "easy", "medium", "hard" })
            {
                loader.Load(id, SimulationScenario.Standard());
            }
            return "base=" + artifact.BaseContentHash;
        });
        Check(artifact, "seed-partitions", () =>
        {
            IReadOnlyDictionary<SeedSetKind, int> counts = TargetSeedCounts(
                paths.BalanceTargets);
            SeedSetLoader.Load(paths.SeedSets, counts);
            return "train=" + counts[SeedSetKind.Train] +
                ", validation=" + counts[SeedSetKind.Validation] +
                ", holdout=" + counts[SeedSetKind.Holdout];
        });
        Check(artifact, "policy-registry", () =>
        {
            foreach (string policyId in PolicyFactory.PolicyIds)
            {
                _ = PolicyFactory.Create(policyId);
            }
            return PolicyFactory.PolicyIds.Count + " policies";
        });
        Check(artifact, "frozen-policy-lock", () =>
        {
            return VerifyPolicyLock(paths, artifact.PolicyLockHash);
        });
        Check(artifact, "same-seed-determinism", () =>
        {
            ulong gameSeed = arguments.GetUlong("game-seed", 1001);
            ulong policySeed = arguments.GetUlong("policy-seed", 2001);
            SimulationResult first = RunOnce(paths, gameSeed, policySeed, null);
            SimulationResult second = RunOnce(paths, gameSeed, policySeed, null);
            string left = DeterministicProjection(first);
            string right = DeterministicProjection(second);
            if (!string.Equals(left, right, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Same seed produced a different command/final-state projection.");
            }
            return first.FinalStateHash;
        });
        Check(artifact, "replay-determinism", () =>
        {
            string replayPath = Path.Combine(
                paths.BalanceArtifacts,
                "verify",
                "determinism.replay.json");
            SimulationResult recorded = RunOnce(
                paths,
                arguments.GetUlong("game-seed", 1001),
                arguments.GetUlong("policy-seed", 2001),
                replayPath);
            ReplayVerificationResult replayed = new ReplayRunner(
                new HeadlessContentLoader(paths)).Run(replayPath);
            if (!replayed.Matches ||
                recorded.FinalStateHash != replayed.FinalStateHash)
            {
                throw new InvalidOperationException(
                    "Replay mismatch: " + string.Join("; ", replayed.Mismatches));
            }
            return replayed.FinalStateHash;
        });

        string output = arguments.Optional("output") is { } outputOption
            ? CommandSupport.ResolvePath(paths, outputOption)
            : Path.Combine(paths.BalanceArtifacts, "verify", "verification.json");
        JsonSupport.Write(output, artifact);
        foreach (VerificationCheck check in artifact.Checks)
        {
            Console.WriteLine(
                (check.Passed ? "PASS " : "FAIL ") + check.Id +
                ": " + check.Detail);
        }
        Console.WriteLine("verification: " + output);
        return artifact.Passed ? ExitCodes.Success : ExitCodes.GateFailure;
    }

    private static SimulationResult RunOnce(
        RepositoryPaths paths,
        ulong gameSeed,
        ulong policySeed,
        string? replayPath)
    {
        var scenario = SimulationScenario.Standard();
        scenario.CaptureReplay = replayPath != null;
        return CommandSupport.Driver(paths).Execute(
            new SimulationRunRequest
            {
                DifficultyId = "current",
                PolicyId = "novice-random-spender",
                GameSeed = gameSeed,
                PolicySeed = policySeed,
                Scenario = scenario,
                ReplayOutputPath = replayPath
            },
            new NoviceRandomSpenderPolicy()).Result;
    }

    private static string DeterministicProjection(SimulationResult result) =>
        JsonSupport.SerializeStable(new
        {
            result.Result,
            result.FinalRunPhase,
            result.RemainingBaseHealth,
            result.GoldUnspent,
            result.TotalLogicalTicks,
            result.FinalStateHash,
            result.FinalSnapshotHash,
            result.Commands,
            result.FinalTowers,
            result.FinalCards
        });

    private static IReadOnlyDictionary<SeedSetKind, int> TargetSeedCounts(
        string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement counts = document.RootElement.GetProperty("seedSets");
        return new Dictionary<SeedSetKind, int>
        {
            [SeedSetKind.Train] = counts.GetProperty("train").GetInt32(),
            [SeedSetKind.Validation] =
                counts.GetProperty("validation").GetInt32(),
            [SeedSetKind.Holdout] = counts.GetProperty("holdout").GetInt32()
        };
    }

    private static string ExistingHash(string path) =>
        File.Exists(path) ? JsonSupport.Sha256File(path) : string.Empty;

    private static string VerifyPolicyLock(
        RepositoryPaths paths,
        string lockHash)
    {
        if (!File.Exists(paths.PolicyLock))
        {
            throw new FileNotFoundException(
                "policy-lock.json is required for a frozen release gate.",
                paths.PolicyLock);
        }
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(paths.PolicyLock),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Policy lock root must be an object.");
        }
        string[] expectedProperties =
        {
            "schemaVersion", "frozenAtUtc", "hashAlgorithm",
            "policyVersions", "files"
        };
        var rootNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!rootNames.Add(property.Name) ||
                !expectedProperties.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "Duplicate or unknown policy-lock property: " +
                    property.Name + ".");
            }
        }
        if (!rootNames.SetEquals(expectedProperties))
        {
            throw new InvalidDataException(
                "Policy lock must contain exactly: " +
                string.Join(", ", expectedProperties) + ".");
        }
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
        {
            throw new InvalidDataException("Policy lock schemaVersion must be 1.");
        }
        JsonElement policyVersions = root.GetProperty("policyVersions");
        if (policyVersions.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Policy lock policyVersions must be an object.");
        }
        string[] lockedPolicyIds = policyVersions.EnumerateObject()
            .Select(entry => entry.Name)
            .ToArray();
        string[] registeredPolicyIds = PolicyFactory.PolicyIds
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (!lockedPolicyIds.SequenceEqual(
                registeredPolicyIds,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Policy lock policyVersions must be the canonical sorted " +
                "registry set.");
        }
        foreach (JsonProperty entry in policyVersions.EnumerateObject())
        {
            IPlayerPolicy policy = PolicyFactory.Create(entry.Name);
            string expectedVersion = entry.Value.ValueKind ==
                JsonValueKind.String
                    ? entry.Value.GetString() ?? string.Empty
                    : string.Empty;
            if (string.IsNullOrWhiteSpace(expectedVersion))
            {
                throw new InvalidDataException(
                    "Locked policy version is required for " +
                    entry.Name + ".");
            }
            if (!string.Equals(
                    policy.PolicyVersion,
                    expectedVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Policy version mismatch for " + entry.Name + ": lock=" +
                    expectedVersion + ", implementation=" +
                    policy.PolicyVersion + ".");
            }
        }
        string frozenAt = root.GetProperty("frozenAtUtc").GetString() ??
            string.Empty;
        if (!DateTimeOffset.TryParseExact(
                frozenAt,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
                out _))
        {
            throw new InvalidDataException(
                "Policy lock frozenAtUtc must be canonical UTC seconds.");
        }
        if (!string.Equals(
                root.GetProperty("hashAlgorithm").GetString(),
                "SHA-256",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Policy lock hashAlgorithm must be SHA-256.");
        }

        JsonElement files = root.GetProperty("files");
        if (files.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Policy lock files must be an object.");
        }
        string rootPrefix = Path.GetFullPath(paths.Root)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string[] entries = files.EnumerateObject()
            .Select(entry => entry.Name)
            .ToArray();
        if (!entries.SequenceEqual(FrozenFiles, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Policy lock files must be the canonical sorted required set.");
        }
        foreach (JsonProperty entry in files.EnumerateObject())
        {
            if (Path.IsPathRooted(entry.Name) ||
                entry.Name.Contains('\\') ||
                entry.Name.Split('/').Any(segment => segment is "" or "." or ".."))
            {
                throw new InvalidDataException(
                    "Policy lock contains an unsafe path: " + entry.Name + ".");
            }
            string file = Path.GetFullPath(Path.Combine(
                paths.Root,
                entry.Name.Replace('/', Path.DirectorySeparatorChar)));
            if (!file.StartsWith(rootPrefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Policy lock path escapes the repository: " +
                    entry.Name + ".");
            }
            string expected = entry.Value.ValueKind == JsonValueKind.String
                ? entry.Value.GetString() ?? string.Empty
                : string.Empty;
            if (expected.Length != 64 ||
                expected.Any(character =>
                    !(character is >= '0' and <= '9') &&
                    !(character is >= 'a' and <= 'f')))
            {
                throw new InvalidDataException(
                    "Policy lock hash must be lowercase SHA-256: " +
                    entry.Name + ".");
            }
            string actual = JsonSupport.Sha256File(file);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Frozen file hash mismatch: " + entry.Name + ".");
            }
        }
        return FrozenFiles.Length + " frozen files, policies=" +
            lockedPolicyIds.Length + ", lock=" + lockHash;
    }

    private static void Check(
        VerificationArtifact artifact,
        string id,
        Func<string> check)
    {
        try
        {
            artifact.Checks.Add(new VerificationCheck
            {
                Id = id,
                Passed = true,
                Detail = check()
            });
        }
        catch (Exception exception)
        {
            artifact.Checks.Add(new VerificationCheck
            {
                Id = id,
                Passed = false,
                Detail = exception.GetType().Name + ": " + exception.Message
            });
        }
    }
}
