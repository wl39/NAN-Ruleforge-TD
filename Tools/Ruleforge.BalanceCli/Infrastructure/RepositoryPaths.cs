using System;
using System.IO;

namespace RuleforgeTD.BalanceCli.Infrastructure;

public sealed class RepositoryPaths
{
    private RepositoryPaths(string root)
    {
        Root = Path.GetFullPath(root);
        ContentJson = Path.Combine(
            Root,
            "Assets",
            "Game",
            "Data",
            "Logic",
            "phase1-content.json");
        CardModules = Path.Combine(
            Root,
            "Assets",
            "Game",
            "Data",
            "Cards");
        BalanceData = Path.Combine(
            Root,
            "Assets",
            "Game",
            "Data",
            "Balance");
        BalanceArtifacts = Path.Combine(
            Root,
            "Artifacts",
            "Balance");
        Prompts = Path.Combine(
            Root,
            "Tools",
            "Ruleforge.BalanceCli",
            "Prompts");
    }

    public string Root { get; }
    public string ContentJson { get; }
    public string CardModules { get; }
    public string BalanceData { get; }
    public string BalanceArtifacts { get; }
    public string Prompts { get; }

    public string Profile(string difficultyId) =>
        Path.Combine(BalanceData, difficultyId + ".profile.json");

    public string SeedSets => Path.Combine(BalanceData, "seed-sets.json");
    public string BalanceTargets =>
        Path.Combine(BalanceData, "balance-targets.json");
    public string PolicyLock => Path.Combine(BalanceData, "policy-lock.json");

    public static RepositoryPaths Discover(string? explicitRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return Validate(explicitRoot);
        }

        string? current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            string marker = Path.Combine(
                current,
                "Assets",
                "Game",
                "Data",
                "Logic",
                "phase1-content.json");
            if (File.Exists(marker))
            {
                return new RepositoryPaths(current);
            }

            current = Directory.GetParent(current)?.FullName;
        }

        string appBase = AppContext.BaseDirectory;
        current = appBase;
        while (!string.IsNullOrEmpty(current))
        {
            string marker = Path.Combine(
                current,
                "Assets",
                "Game",
                "Data",
                "Logic",
                "phase1-content.json");
            if (File.Exists(marker))
            {
                return new RepositoryPaths(current);
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the Ruleforge TD repository root. " +
            "Run from the repository or pass --repo <path>.");
    }

    private static RepositoryPaths Validate(string root)
    {
        var paths = new RepositoryPaths(root);
        if (!File.Exists(paths.ContentJson))
        {
            throw new FileNotFoundException(
                "The repository root does not contain phase1-content.json.",
                paths.ContentJson);
        }

        return paths;
    }
}
