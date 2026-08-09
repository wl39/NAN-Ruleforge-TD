using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using RuleforgeTD.BalanceCli.Balance;
using RuleforgeTD.BalanceCli.Infrastructure;
using RuleforgeTD.BalanceCli.Simulation;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Effects;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.BalanceCli.Content;

public sealed class LoadedSimulationContent
{
    public required CompiledContent Content { get; init; }
    public required DifficultyProfile Profile { get; init; }
    public required string BaseContentHash { get; init; }
    public required string DifficultyProfileHash { get; init; }
    public required string ScenarioHash { get; init; }
    public required string CompiledContentHash { get; init; }
}

public sealed class HeadlessContentLoader
{
    private readonly RepositoryPaths paths;

    public HeadlessContentLoader(RepositoryPaths paths)
    {
        this.paths = paths;
    }

    public LoadedSimulationContent Load(
        string difficultyId,
        SimulationScenario scenario)
    {
        string profilePath = paths.Profile(difficultyId);
        DifficultyProfile profile =
            JsonSupport.ReadStrict<DifficultyProfile>(profilePath);
        return LoadProfile(
            profile,
            scenario,
            JsonSupport.Sha256File(profilePath));
    }

    /// <summary>
    /// Compiles an already schema-validated candidate profile against the same
    /// composed content path as named profiles. This is used only for
    /// matched-seed optimizer evaluation; it never writes or mutates the
    /// repository profile.
    /// </summary>
    public LoadedSimulationContent LoadProfile(
        DifficultyProfile profile,
        SimulationScenario scenario,
        string? profileHash = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(scenario);
        ContentCatalogDto catalog = LoadComposedCatalog();
        string baseHash = Compile(catalog).ContentHash.ToString("X16");
        DifficultyProfileValidator.Validate(profile, profile.DifficultyId);
        if (!string.Equals(
                profile.BaseContentHash,
                "AUTO",
                StringComparison.Ordinal) &&
            !string.Equals(
                profile.BaseContentHash,
                baseHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Difficulty profile '" + profile.DifficultyId +
                "' targets base content " + profile.BaseContentHash +
                " but the repository content hash is " + baseHash + ".");
        }

        DifficultyProfileValidator.Apply(catalog, profile);
        ApplyScenario(catalog, scenario);
        CompiledContent compiled = Compile(catalog);

        return new LoadedSimulationContent
        {
            Content = compiled,
            Profile = profile,
            BaseContentHash = baseHash,
            DifficultyProfileHash = profileHash ??
                BalanceProfileHasher.Compute(profile),
            ScenarioHash = JsonSupport.Sha256Text(
                JsonSupport.SerializeStable(scenario)),
            CompiledContentHash = compiled.ContentHash.ToString("X16")
        };
    }

    public string ComputeBaseContentHash()
    {
        return Compile(LoadComposedCatalog()).ContentHash.ToString("X16");
    }

    private ContentCatalogDto LoadComposedCatalog()
    {
        ContentCatalogDto catalog = Deserialize<ContentCatalogDto>(
            File.ReadAllText(paths.ContentJson, Encoding.UTF8),
            paths.ContentJson);
        return CardContentCatalogComposer.Compose(catalog, LoadModules());
    }

    private static CompiledContent Compile(ContentCatalogDto catalog) =>
        EffectContentCompiler.Compile(
            catalog,
            GameSimulation.IsEffectOperationSupported);

    private List<CardContentModuleDto> LoadModules()
    {
        var modules = new List<CardContentModuleDto>();
        foreach (string modulePath in EnumerateModulePaths())
        {
            modules.Add(Deserialize<CardContentModuleDto>(
                File.ReadAllText(modulePath, Encoding.UTF8),
                modulePath));
        }
        return modules;
    }

    private IEnumerable<string> EnumerateModulePaths()
    {
        if (!Directory.Exists(paths.CardModules))
        {
            return Array.Empty<string>();
        }
        return Directory
            .EnumerateFiles(
                paths.CardModules,
                "*.json",
                SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static T Deserialize<T>(string json, string source)
    {
        T? value = JsonSerializer.Deserialize<T>(json, JsonSupport.Options);
        return value ?? throw new InvalidDataException(
            "Could not deserialize content JSON: " + source);
    }

    private static void ApplyScenario(
        ContentCatalogDto catalog,
        SimulationScenario scenario)
    {
        if (scenario.StartingGoldOverride.HasValue)
        {
            if (scenario.StartingGoldOverride.Value < 0)
            {
                throw new InvalidOperationException(
                    "Scenario starting gold cannot be negative.");
            }
            catalog.run.startingGold = scenario.StartingGoldOverride.Value;
        }
        if (scenario.WorldCardPackProgressThresholdOverride.HasValue)
        {
            int threshold =
                scenario.WorldCardPackProgressThresholdOverride.Value;
            if (threshold <= 0)
            {
                throw new InvalidOperationException(
                    "Scenario world card-pack progress threshold must be positive.");
            }
            catalog.run.cardPackProgressThresholds = new[] { threshold };
        }
        if (scenario.DisableCardRewardChoices)
        {
            catalog.run.regularDraftWaveNumbers = Array.Empty<int>();
            catalog.run.bossCardPackWaveNumbers = Array.Empty<int>();
            // The authoritative content compiler requires at least one
            // positive card-pack progress threshold. Keep a valid, unreachable
            // sentinel instead of deleting the array. One progress point per
            // lineage keeps even the compiler's maximum bounded wave schedule
            // far below this threshold without changing combat outcomes.
            catalog.run.normalKillProgress = 1;
            catalog.run.eliteKillProgress = 1;
            catalog.run.cardPackProgressThresholds = new[] { 1_000_000_000 };
        }
        if (!scenario.ReplaceStartingCards &&
            scenario.AdditionalStartingCards.Count == 0 &&
            scenario.FixtureCardProgram.Count == 0 &&
            string.IsNullOrWhiteSpace(scenario.FixtureControlCardId))
        {
            return;
        }

        var cardIds = new HashSet<string>(
            catalog.cards.Select(card => card.id),
            StringComparer.Ordinal);
        var startingCards = scenario.ReplaceStartingCards
            ? new List<string>()
            : new List<string>(catalog.run.startingCards);
        if (!string.IsNullOrWhiteSpace(scenario.FixtureControlCardId) &&
            !cardIds.Contains(scenario.FixtureControlCardId))
        {
            throw new InvalidOperationException(
                "Scenario requests unknown fixture control card '" +
                scenario.FixtureControlCardId + "'.");
        }
        ValidateFixtureProgram(scenario, cardIds);
        foreach (string cardId in scenario.AdditionalStartingCards)
        {
            if (!cardIds.Contains(cardId))
            {
                throw new InvalidOperationException(
                    "Scenario requests unknown card '" + cardId + "'.");
            }
            // Starting-card definitions are a multiset. Preserving duplicates
            // is required for pair/triple fixtures that use two instances of
            // the same card; GameSimulation assigns each its own instance ID.
            startingCards.Add(cardId);
        }
        if (startingCards.Count == 0)
        {
            throw new InvalidOperationException(
                "Scenario replacement must retain at least one starting card.");
        }
        catalog.run.startingCards = startingCards.ToArray();
    }

    private static void ValidateFixtureProgram(
        SimulationScenario scenario,
        IReadOnlySet<string> cardIds)
    {
        var orders = new HashSet<int>();
        var slots = new HashSet<int>();
        foreach (SimulationCardFixtureSlot fixture in
                 scenario.FixtureCardProgram)
        {
            if (fixture.Order < 0 || !orders.Add(fixture.Order))
            {
                throw new InvalidOperationException(
                    "Scenario fixture orders must be unique and non-negative.");
            }
            if (fixture.SlotIndex < 0 || !slots.Add(fixture.SlotIndex))
            {
                throw new InvalidOperationException(
                    "Scenario fixture slots must be unique and non-negative.");
            }
            if (!cardIds.Contains(fixture.CardId))
            {
                throw new InvalidOperationException(
                    "Scenario requests unknown fixture card '" +
                    fixture.CardId + "'.");
            }
        }
        if (string.IsNullOrWhiteSpace(scenario.FixtureControlCardId))
        {
            if (scenario.FixtureCardProgram.Count > 0)
            {
                throw new InvalidOperationException(
                    "A card fixture program requires an explicit control card.");
            }
            return;
        }
        if (!scenario.ReplaceStartingCards)
        {
            throw new InvalidOperationException(
                "An explicit card fixture must replace the normal starting cards.");
        }
        List<string> expected = new[] { scenario.FixtureControlCardId }
            .Concat(scenario.FixtureCardProgram
                .OrderBy(fixture => fixture.Order)
                .Select(fixture => fixture.CardId))
            .ToList();
        if (!expected.SequenceEqual(
                scenario.AdditionalStartingCards,
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Fixture starting cards must be exactly the reserved control " +
                "followed by the ordered fixture program.");
        }
    }

}
