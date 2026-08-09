using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RuleforgeTD.BalanceCli.Content;
using RuleforgeTD.BalanceCli.Evaluation;
using RuleforgeTD.BalanceCli.Infrastructure;
using RuleforgeTD.BalanceCli.Simulation;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using CardProgramStep = RuleforgeTD.BalanceCli.Policies.CardProgramStep;

namespace RuleforgeTD.BalanceCli.Commands;

internal static class DiscoveryCommands
{
    public static int DiscoverCards(CliArguments arguments)
    {
        RepositoryPaths paths = CommandSupport.Paths(arguments);
        string difficulty = arguments.Get("difficulty", "medium");
        IReadOnlyList<SeedPair> seeds = CommandSupport.Seeds(
            paths,
            arguments,
            "train",
            out string seedSet);
        var loader = new HeadlessContentLoader(paths);
        CardExperimentEnumeration enumeration =
            new CompiledCardExperimentEnumerator(loader).Enumerate(
                difficulty,
                new CardExperimentEnumerationOptions
                {
                    IncludePairExperiments = false
                });
        IReadOnlyList<CardStrengthExperiment> experiments =
            FilterStrengthExperiments(enumeration, arguments);
        string directory = arguments.Optional("output-dir") is { } output
            ? CommandSupport.ResolvePath(paths, output)
            : CommandSupport.DefaultArtifactDirectory(
                paths,
                "discovery",
                "cards",
                difficulty,
                seedSet);
        if (experiments.Count == 0)
        {
            Directory.CreateDirectory(directory);
            JsonSupport.Write(
                Path.Combine(directory, "card-experiment-enumeration.json"),
                enumeration);
            Console.Error.WriteLine(
                "No representable card experiment matches the requested " +
                "content context. Enumeration was saved to " + directory + ".");
            return HasDiscoveryFilter(arguments)
                ? ExitCodes.Usage
                : ExitCodes.DataError;
        }

        var simulationRunner = new CardExperimentSimulationRunner(
            loader,
            RunnerOptions(arguments));
        var coverageRunner = new CardExperimentSimulationRunner(
            loader,
            RunnerOptions(arguments, coverageNoviceMode: true));
        CardStrengthExperiment preflight = experiments[0];
        EvaluationRunMetrics preflightBaseline = simulationRunner.RunAsync(
                new CardExperimentVariant(
                    "preflight-baseline",
                    preflight.DifficultyId,
                    preflight.TowerDefinitionId,
                    preflight.TowerLevel,
                    CardExperimentVariantKind.Baseline,
                    Array.Empty<CardProgramStep>()),
                seeds[0],
                default)
            .GetAwaiter().GetResult();
        EvaluationRunMetrics preflightCard = simulationRunner.RunAsync(
                new CardExperimentVariant(
                    "preflight-card",
                    preflight.DifficultyId,
                    preflight.TowerDefinitionId,
                    preflight.TowerLevel,
                    CardExperimentVariantKind.Program,
                    new[] { preflight.Card }),
                seeds[0],
                default)
            .GetAwaiter().GetResult();
        if (!preflightBaseline.IsValid || !preflightCard.IsValid)
        {
            Console.Error.WriteLine(
                "Card fixture preflight failed. baseline=" +
                preflightBaseline.FailureReason + "; card=" +
                preflightCard.FailureReason + ".");
            Directory.CreateDirectory(directory);
            JsonSupport.Write(
                Path.Combine(directory, "card-experiment-enumeration.json"),
                enumeration);
            return ExitCodes.SimulationFailure;
        }
        if (arguments.HasFlag("coverage-only"))
        {
            CardCoverageReport coverage = new CardCoverageEvaluator()
                .EvaluateAsync(
                    CoverageEnumeration(enumeration, experiments),
                    seeds,
                    coverageRunner.AsDelegate())
                .GetAwaiter().GetResult();
            Directory.CreateDirectory(directory);
            JsonSupport.Write(
                Path.Combine(directory, "card-experiment-enumeration.json"),
                enumeration);
            WriteCoverage(directory, coverage);
            Console.WriteLine(
                "coverage experiments: " + experiments.Count +
                " | active cards: " + enumeration.ActiveCardIds.Count +
                " | seeds: " + seeds.Count);
            Console.WriteLine("artifacts: " + directory);
            return ExitCodes.Success;
        }
        CardStrengthIndex index;
        try
        {
            index = new CardStrengthEvaluator().EvaluateAsync(
                    experiments,
                    seeds,
                    simulationRunner.AsDelegate(),
                    enumeration.ContentHash,
                    JsonSupport.Sha256File(paths.SeedSets))
                .GetAwaiter().GetResult();
        }
        catch (InvalidOperationException exception)
        {
            Directory.CreateDirectory(directory);
            JsonSupport.Write(
                Path.Combine(directory, "card-experiment-enumeration.json"),
                enumeration);
            Console.Error.WriteLine(
                "Card discovery simulation/evaluation failed: " +
                exception.Message);
            return ExitCodes.SimulationFailure;
        }
        Directory.CreateDirectory(directory);
        JsonSupport.Write(
            Path.Combine(directory, "card-experiment-enumeration.json"),
            enumeration);
        JsonSupport.Write(
            Path.Combine(directory, "card-strength-index.json"),
            index);
        JsonSupport.Write(
            Path.Combine(directory, "card-strength-policy-index.json"),
            index.ToPolicyIndex());
        WriteStrengthCsvAndMarkdown(directory, index, enumeration, seeds.Count);
        if (arguments.HasFlag("coverage"))
        {
            CardCoverageReport coverage = new CardCoverageEvaluator()
                .EvaluateAsync(
                    CoverageEnumeration(enumeration, experiments),
                    seeds,
                    coverageRunner.AsDelegate())
                .GetAwaiter().GetResult();
            WriteCoverage(directory, coverage);
        }
        Console.WriteLine(
            "card experiments: " + experiments.Count +
            " | active cards: " + enumeration.ActiveCardIds.Count +
            " | seeds: " + seeds.Count);
        Console.WriteLine("artifacts: " + directory);
        return ExitCodes.Success;
    }

    public static int DiscoverSynergies(CliArguments arguments)
    {
        RepositoryPaths paths = CommandSupport.Paths(arguments);
        string difficulty = arguments.Get("difficulty", "hard");
        IReadOnlyList<SeedPair> seeds = CommandSupport.Seeds(
            paths,
            arguments,
            "train",
            out string seedSet);
        var loader = new HeadlessContentLoader(paths);
        int pairEnumerationLimit = arguments.GetInt(
            "pair-enumeration-limit",
            20000);
        if (pairEnumerationLimit < 1)
        {
            throw new CliUsageException(
                "--pair-enumeration-limit must be positive.");
        }
        CardExperimentEnumeration enumeration =
            new CompiledCardExperimentEnumerator(loader).Enumerate(
                difficulty,
                new CardExperimentEnumerationOptions
                {
                    IncludePairExperiments = true,
                    MaximumPairExperiments = pairEnumerationLimit,
                    // A triple cannot be expanded from a pair measured on a
                    // two-slot level. Skipping those levels also prevents the
                    // bounded deterministic enumeration from exhausting its
                    // budget before it ever reaches a legal triple context.
                    MinimumUnlockedSlotsForPairs =
                        arguments.HasFlag("triples") ? 3 : 2
                });
        IReadOnlyList<CardSynergyPairExperiment> experiments =
            FilterPairExperiments(enumeration, arguments);
        string directory = arguments.Optional("output-dir") is { } output
            ? CommandSupport.ResolvePath(paths, output)
            : CommandSupport.DefaultArtifactDirectory(
                paths,
                "discovery",
                "synergies",
                difficulty,
                seedSet);
        if (experiments.Count == 0)
        {
            Directory.CreateDirectory(directory);
            JsonSupport.Write(
                Path.Combine(directory, "card-experiment-enumeration.json"),
                enumeration);
            Console.Error.WriteLine(
                "No representable ordered pair experiment matches the " +
                "requested content context. Enumeration was saved to " +
                directory + ".");
            return HasDiscoveryFilter(arguments)
                ? ExitCodes.Usage
                : ExitCodes.DataError;
        }

        var simulationRunner = new CardExperimentSimulationRunner(
            loader,
            RunnerOptions(arguments));
        CardSynergyPairExperiment preflight = experiments[0];
        EvaluationRunMetrics preflightBaseline = simulationRunner.RunAsync(
                new CardExperimentVariant(
                    "pair-preflight-baseline",
                    preflight.DifficultyId,
                    preflight.TowerDefinitionId,
                    preflight.TowerLevel,
                    CardExperimentVariantKind.Baseline,
                    Array.Empty<CardProgramStep>()),
                seeds[0],
                default)
            .GetAwaiter().GetResult();
        EvaluationRunMetrics preflightPair = simulationRunner.RunAsync(
                new CardExperimentVariant(
                    "pair-preflight-program",
                    preflight.DifficultyId,
                    preflight.TowerDefinitionId,
                    preflight.TowerLevel,
                    CardExperimentVariantKind.Program,
                    new[] { preflight.First, preflight.Second }),
                seeds[0],
                default)
            .GetAwaiter().GetResult();
        if (!preflightBaseline.IsValid || !preflightPair.IsValid)
        {
            Console.Error.WriteLine(
                "Synergy fixture preflight failed. baseline=" +
                preflightBaseline.FailureReason + "; pair=" +
                preflightPair.FailureReason + ".");
            Directory.CreateDirectory(directory);
            JsonSupport.Write(
                Path.Combine(directory, "card-experiment-enumeration.json"),
                enumeration);
            return ExitCodes.SimulationFailure;
        }
        var evaluator = new SynergyEvaluator();
        CardSynergyIndex index;
        try
        {
            index = evaluator.EvaluatePairsAsync(
                    experiments,
                    seeds,
                    simulationRunner.AsDelegate(),
                    enumeration.ContentHash,
                    JsonSupport.Sha256File(paths.SeedSets))
                .GetAwaiter().GetResult();
        }
        catch (InvalidOperationException exception)
        {
            Directory.CreateDirectory(directory);
            JsonSupport.Write(
                Path.Combine(directory, "card-experiment-enumeration.json"),
                enumeration);
            Console.Error.WriteLine(
                "Synergy discovery simulation/evaluation failed: " +
                exception.Message);
            return ExitCodes.SimulationFailure;
        }
        int pairEntryCount = index.Entries.Count;
        if (arguments.HasFlag("triples"))
        {
            try
            {
                index = AddLegalTriples(
                    loader.Load(
                        difficulty,
                        SimulationScenario.Standard()).Content,
                    index,
                    evaluator,
                    seeds,
                    simulationRunner.AsDelegate(),
                    arguments);
            }
            catch (InvalidOperationException exception)
            {
                Directory.CreateDirectory(directory);
                JsonSupport.Write(
                    Path.Combine(
                        directory,
                        "card-experiment-enumeration.json"),
                    enumeration);
                JsonSupport.Write(
                    Path.Combine(directory, "card-synergy-index.json"),
                    index);
                JsonSupport.Write(
                    Path.Combine(
                        directory,
                        "card-synergy-policy-index.json"),
                    index.ToPolicyIndex());
                Console.Error.WriteLine(
                    "Triple discovery simulation/evaluation failed: " +
                    exception.Message + ". Pair artifacts were preserved in " +
                    directory + ".");
                return ExitCodes.SimulationFailure;
            }
        }
        Directory.CreateDirectory(directory);
        JsonSupport.Write(
            Path.Combine(directory, "card-experiment-enumeration.json"),
            enumeration);
        JsonSupport.Write(
            Path.Combine(directory, "card-synergy-index.json"),
            index);
        JsonSupport.Write(
            Path.Combine(directory, "card-synergy-policy-index.json"),
            index.ToPolicyIndex());
        WriteSynergyCsvAndMarkdown(directory, index, experiments.Count, seeds.Count);
        Console.WriteLine(
            "ordered pair experiments: " + experiments.Count +
            " | index entries: " + index.Entries.Count +
            " | seeds: " + seeds.Count);
        if (enumeration.PairEnumerationTruncated)
        {
            Console.Error.WriteLine(
                "warning: pair enumeration reached its configured limit; " +
                "the index is an explicitly truncated deterministic subset.");
        }
        int tripleCount = index.Entries.Count - pairEntryCount;
        if (arguments.HasFlag("triples") && tripleCount == 0)
        {
            Console.Error.WriteLine(
                "No legal three-slot/compute-capacity triple was measured.");
        }
        Console.WriteLine("artifacts: " + directory);
        return arguments.HasFlag("triples") && tripleCount == 0
            ? ExitCodes.DataError
            : ExitCodes.Success;
    }

    private static IReadOnlyList<CardStrengthExperiment>
        FilterStrengthExperiments(
            CardExperimentEnumeration enumeration,
            CliArguments arguments)
    {
        IEnumerable<CardStrengthExperiment> query =
            enumeration.StrengthExperiments;
        string? tower = arguments.Optional("tower");
        if (!string.IsNullOrWhiteSpace(tower))
        {
            query = query.Where(experiment => string.Equals(
                experiment.TowerDefinitionId,
                tower,
                StringComparison.Ordinal));
        }
        string? subject = arguments.Optional("subject");
        if (subject != null)
        {
            SubjectType parsed = CommandSupport.ParseSubject(subject);
            query = query.Where(experiment =>
                experiment.Card.SubjectType == parsed);
        }
        int maxCards = arguments.GetInt(
            "max-cards",
            enumeration.ActiveCardIds.Count);
        if (maxCards < 1)
        {
            throw new CliUsageException("--max-cards must be positive.");
        }
        HashSet<string> cards = query.Select(experiment =>
                experiment.Card.CardId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Take(maxCards)
            .ToHashSet(StringComparer.Ordinal);
        query = query.Where(experiment => cards.Contains(
            experiment.Card.CardId));
        if (!arguments.HasFlag("all-contexts"))
        {
            query = query
                .GroupBy(experiment => new
                {
                    experiment.Card.CardId,
                    experiment.TowerDefinitionId,
                    experiment.Card.SubjectType
                })
                .Select(group => group
                    .OrderBy(experiment => experiment.Card.SlotIndex)
                    .First());
        }
        return query
            .OrderBy(experiment => experiment.TowerDefinitionId,
                StringComparer.Ordinal)
            .ThenBy(experiment => experiment.Card.SubjectType)
            .ThenBy(experiment => experiment.Card.SlotIndex)
            .ThenBy(experiment => experiment.Card.CardId,
                StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<CardSynergyPairExperiment>
        FilterPairExperiments(
            CardExperimentEnumeration enumeration,
            CliArguments arguments)
    {
        IEnumerable<CardSynergyPairExperiment> query =
            enumeration.PairExperiments;
        string? tower = arguments.Optional("tower");
        if (!string.IsNullOrWhiteSpace(tower))
        {
            query = query.Where(experiment => string.Equals(
                experiment.TowerDefinitionId,
                tower,
                StringComparison.Ordinal));
        }
        string? subject = arguments.Optional("subject");
        if (subject != null)
        {
            SubjectType parsed = CommandSupport.ParseSubject(subject);
            query = query.Where(experiment =>
                experiment.First.SubjectType == parsed);
        }
        int maxCards = arguments.GetInt("max-cards", 8);
        if (maxCards < 2)
        {
            throw new CliUsageException(
                "--max-cards must be at least 2 for pair discovery.");
        }
        string[] availableCards = query
            .SelectMany(experiment => new[]
            {
                experiment.First.CardId,
                experiment.Second.CardId
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        HashSet<string> cards = SelectSynergyCandidateCards(
            availableCards,
            arguments,
            maxCards);
        int pairLimit = arguments.GetInt("pair-limit", 128);
        if (pairLimit < 1)
        {
            throw new CliUsageException("--pair-limit must be positive.");
        }
        return query.Where(experiment =>
                cards.Contains(experiment.First.CardId) &&
                cards.Contains(experiment.Second.CardId) &&
                experiment.First.SlotIndex == 0)
            .OrderBy(experiment => experiment.TowerDefinitionId,
                StringComparer.Ordinal)
            .ThenBy(experiment => experiment.First.SubjectType)
            .ThenBy(experiment => experiment.First.CardId,
                StringComparer.Ordinal)
            .ThenBy(experiment => experiment.Second.CardId,
                StringComparer.Ordinal)
            .Take(pairLimit)
            .ToArray();
    }

    private static HashSet<string> SelectSynergyCandidateCards(
        IReadOnlyCollection<string> availableCards,
        CliArguments arguments,
        int maxCards)
    {
        string? explicitCards = arguments.Optional("cards");
        if (!string.IsNullOrWhiteSpace(explicitCards))
        {
            string[] requested = explicitCards
                .Split(',', StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (requested.Length < 2)
            {
                throw new CliUsageException(
                    "--cards must contain at least two comma-separated ids.");
            }
            if (requested.Length > maxCards)
            {
                throw new CliUsageException(
                    "--cards contains more ids than --max-cards permits.");
            }
            var explicitAvailable = availableCards.ToHashSet(
                StringComparer.Ordinal);
            string[] missing = requested
                .Where(card => !explicitAvailable.Contains(card))
                .OrderBy(card => card, StringComparer.Ordinal)
                .ToArray();
            if (missing.Length > 0)
            {
                throw new CliUsageException(
                    "--cards contains unavailable ids: " +
                    string.Join(", ", missing) + ".");
            }
            return requested.ToHashSet(StringComparer.Ordinal);
        }

        string? strengthPath = arguments.Optional("card-strength");
        if (string.IsNullOrWhiteSpace(strengthPath))
        {
            return availableCards
                .OrderBy(value => value, StringComparer.Ordinal)
                .Take(maxCards)
                .ToHashSet(StringComparer.Ordinal);
        }

        RepositoryPaths paths = CommandSupport.Paths(arguments);
        string resolved = CommandSupport.ResolvePath(paths, strengthPath);
        CardStrengthIndex strength = JsonSupport.Read<CardStrengthIndex>(
            resolved);
        strength.Validate();
        var available = availableCards.ToHashSet(StringComparer.Ordinal);
        string difficulty = arguments.Get("difficulty", "hard");
        string? tower = arguments.Optional("tower");
        SubjectType? subject = arguments.Optional("subject") is { } rawSubject
            ? CommandSupport.ParseSubject(rawSubject)
            : null;
        string[] ranked = strength.Entries
            .Where(entry => entry.IsEvaluable &&
                available.Contains(entry.CardId) &&
                string.Equals(
                    entry.DifficultyId,
                    difficulty,
                    StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(tower) || string.Equals(
                    entry.TowerDefinitionId,
                    tower,
                    StringComparison.Ordinal)) &&
                (!subject.HasValue || entry.SubjectType == subject.Value))
            .GroupBy(entry => entry.CardId, StringComparer.Ordinal)
            .Select(group => new
            {
                CardId = group.Key,
                Score = group.Max(entry => entry.Lift.CompositeScore)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.CardId, StringComparer.Ordinal)
            .Select(item => item.CardId)
            .Take(maxCards)
            .ToArray();
        if (ranked.Length < Math.Min(maxCards, available.Count))
        {
            throw new CliUsageException(
                "--card-strength does not contain enough evaluable entries " +
                "for the requested difficulty/tower/subject context.");
        }
        return ranked.ToHashSet(StringComparer.Ordinal);
    }

    private static CardSynergyIndex AddLegalTriples(
        CompiledContent content,
        CardSynergyIndex pairIndex,
        SynergyEvaluator evaluator,
        IReadOnlyList<SeedPair> seeds,
        CardExperimentRunner runner,
        CliArguments arguments)
    {
        int thirdCardLimit = arguments.GetInt("third-card-limit", 8);
        int pairBeamWidth = arguments.GetInt("triple-pair-beam", 8);
        int maximumTripleCount = arguments.GetInt("triple-limit", 32);
        if (thirdCardLimit < 1 || pairBeamWidth < 1 || maximumTripleCount < 1)
        {
            throw new CliUsageException(
                "Triple discovery limits must all be positive.");
        }

        HashSet<string>? explicitCardIds =
            arguments.Optional("cards") is { } explicitCards
                ? explicitCards
                    .Split(',', StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.Ordinal)
                : null;

        var triples = new List<CardSynergyEntry>();
        int feasiblePairs = 0;
        foreach (CardSynergyEntry pair in pairIndex.Entries
                     .Where(entry => entry.OrderedProgram.Count == 2)
                     .OrderByDescending(entry =>
                         entry.SynergyLift.CompositeScore)
                     .ThenBy(entry => entry.OrderedSignature,
                         StringComparer.Ordinal))
        {
            if (feasiblePairs >= pairBeamWidth ||
                triples.Count >= maximumTripleCount)
            {
                break;
            }
            if (!content.TryGetTowerId(
                    pair.TowerDefinitionId,
                    out TowerDefinitionId towerId))
            {
                continue;
            }
            CompiledTowerDefinition tower = content.GetTower(towerId);
            if (!tower.TryGetLevel(
                    pair.TowerLevel,
                    out CompiledTowerLevelBalance level) ||
                level.UnlockedSlots < 3)
            {
                continue;
            }

            var occupied = new bool[level.UnlockedSlots];
            int programCompute = 0;
            bool pairIsLegal = true;
            var usedCards = new HashSet<string>(StringComparer.Ordinal);
            foreach (CardProgramStep step in pair.OrderedProgram)
            {
                if (!content.TryGetCardId(step.CardId, out CardId cardId))
                {
                    pairIsLegal = false;
                    break;
                }
                CompiledCardDefinition card = content.GetCard(cardId);
                programCompute = checked(programCompute + card.ComputeCost);
                if (!TryOccupy(occupied, step.SlotIndex, card.SlotCost))
                {
                    pairIsLegal = false;
                    break;
                }
                usedCards.Add(card.StableId);
            }
            if (!pairIsLegal || programCompute > level.ComputeCapacity)
            {
                continue;
            }

            SubjectType subject = pair.OrderedProgram[0].SubjectType;
            CardProgramStep[] thirdCards = content.Cards
                .Where(card =>
                    (explicitCardIds == null ||
                        explicitCardIds.Contains(card.StableId)) &&
                    !usedCards.Contains(card.StableId) &&
                    programCompute + card.ComputeCost <=
                        level.ComputeCapacity)
                .Select(card => new
                {
                    Card = card,
                    Slot = FirstFreeSlot(occupied, card.SlotCost)
                })
                .Where(candidate => candidate.Slot >= 0)
                .OrderBy(candidate => candidate.Card.StableId,
                    StringComparer.Ordinal)
                .Take(thirdCardLimit)
                .Select(candidate => new CardProgramStep(
                    candidate.Card.StableId,
                    subject,
                    candidate.Slot))
                .ToArray();
            if (thirdCards.Length == 0)
            {
                continue;
            }
            feasiblePairs++;
            var onePair = new CardSynergyIndex
            {
                SchemaVersion = pairIndex.SchemaVersion,
                ContentHash = pairIndex.ContentHash,
                SeedSetHash = pairIndex.SeedSetHash,
                Weights = pairIndex.Weights,
                Entries = new List<CardSynergyEntry> { pair }
            };
            CardSynergyIndex measured = evaluator.EvaluateTripleBeamAsync(
                    onePair,
                    thirdCards,
                    seeds,
                    runner,
                    pairBeamWidth: 1,
                    maximumTripleCount: Math.Min(
                        thirdCards.Length,
                        maximumTripleCount - triples.Count))
                .GetAwaiter().GetResult();
            triples.AddRange(measured.Entries.Where(entry =>
                entry.OrderedProgram.Count == 3));
        }

        var merged = new CardSynergyIndex
        {
            SchemaVersion = pairIndex.SchemaVersion,
            ContentHash = pairIndex.ContentHash,
            SeedSetHash = pairIndex.SeedSetHash,
            Weights = pairIndex.Weights,
            Entries = pairIndex.Entries
                .Concat(triples)
                .GroupBy(entry => entry.OrderedSignature,
                    StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderByDescending(entry =>
                    entry.SynergyLift.CompositeScore)
                .ThenBy(entry => entry.OrderedSignature,
                    StringComparer.Ordinal)
                .ToList()
        };
        merged.Validate();
        return merged;
    }

    private static bool TryOccupy(bool[] occupied, int slot, int slotCost)
    {
        if (slot < 0 || slotCost < 1 || slot + slotCost > occupied.Length)
        {
            return false;
        }
        for (int index = slot; index < slot + slotCost; index++)
        {
            if (occupied[index])
            {
                return false;
            }
        }
        for (int index = slot; index < slot + slotCost; index++)
        {
            occupied[index] = true;
        }
        return true;
    }

    private static int FirstFreeSlot(bool[] occupied, int slotCost)
    {
        if (slotCost < 1)
        {
            return -1;
        }
        for (int slot = 0; slot + slotCost <= occupied.Length; slot++)
        {
            bool free = true;
            for (int index = slot; index < slot + slotCost; index++)
            {
                free &= !occupied[index];
            }
            if (free)
            {
                return slot;
            }
        }
        return -1;
    }

    private static bool HasDiscoveryFilter(CliArguments arguments) =>
        arguments.Optional("tower") != null ||
        arguments.Optional("subject") != null;

    private static CardExperimentSimulationOptions RunnerOptions(
        CliArguments arguments,
        bool coverageNoviceMode = false) => new()
    {
        MaximumLogicalTicks = arguments.GetInt("max-ticks", 60000),
        MaximumDecisions = arguments.GetInt("max-decisions", 200000),
        RequireFixtureExecution = !arguments.HasFlag("allow-unexecuted"),
        RejectCommandRejections = true,
        CoverageNoviceMode = coverageNoviceMode
    };

    private static void WriteStrengthCsvAndMarkdown(
        string directory,
        CardStrengthIndex index,
        CardExperimentEnumeration enumeration,
        int seedCount)
    {
        var csv = new StringBuilder();
        csv.AppendLine(
            "difficulty,tower,subject,slot,card,towerLevel,matchedSeeds," +
            "invalidSeeds,cleanMetricSeeds,runtimeFailureSeeds," +
            "runtimeFailureRuns," +
            "baselineWinRate,cardWinRate,winRateLift,healthLift,leakReduction," +
            "compositeLift,evaluable,goodStandalone,runtimeDiagnostics," +
            "failureReason");
        foreach (CardStrengthEntry entry in index.Entries
                     .OrderByDescending(value => value.Lift.CompositeScore))
        {
            csv.AppendLine(string.Join(',', new[]
            {
                entry.DifficultyId,
                entry.TowerDefinitionId,
                entry.SubjectType.ToString(),
                entry.SlotIndex.ToString(CultureInfo.InvariantCulture),
                entry.CardId,
                entry.TowerLevel.ToString(CultureInfo.InvariantCulture),
                entry.Lift.MatchedSeedCount.ToString(CultureInfo.InvariantCulture),
                entry.Lift.InvalidRunCount.ToString(CultureInfo.InvariantCulture),
                entry.Lift.CleanMetricSeedCount.ToString(
                    CultureInfo.InvariantCulture),
                entry.Lift.RuntimeFailureSeedCount.ToString(
                    CultureInfo.InvariantCulture),
                entry.Lift.RuntimeFailureRunCount.ToString(
                    CultureInfo.InvariantCulture),
                Number(entry.Lift.BaselineWinRate),
                Number(entry.Lift.CandidateWinRate),
                Number(entry.Lift.WinRateLift),
                Number(entry.Lift.MeanRemainingHealthLift),
                Number(entry.Lift.MeanLeakReduction),
                Number(entry.Lift.CompositeScore),
                entry.IsEvaluable ? "true" : "false",
                entry.IsGoodStandalone ? "true" : "false",
                string.Join(" | ", entry.Lift.RuntimeFailureDiagnostics)
                    .Replace(',', ';')
                    .Replace('\r', ' ')
                    .Replace('\n', ' '),
                entry.FailureReason
                    .Replace(',', ';')
                    .Replace('\r', ' ')
                    .Replace('\n', ' ')
            }));
        }
        File.WriteAllText(
            Path.Combine(directory, "card-strength-index.csv"),
            csv.ToString(),
            new UTF8Encoding(false));
        var markdown = new StringBuilder();
        markdown.AppendLine("# Card strength discovery")
            .AppendLine()
            .AppendLine("- Active cards: " + enumeration.ActiveCardIds.Count)
            .AppendLine("- Matched seeds per experiment: " + seedCount)
            .AppendLine("- Unsupported contexts: " +
                enumeration.UnsupportedContexts.Count)
            .AppendLine()
            .AppendLine("| Card | Tower | Subject | Composite lift | Win lift | Runtime failures | Evaluable | Good | Diagnostics / failure |")
            .AppendLine("|---|---|---|---:|---:|---:|---:|---:|---|");
        foreach (CardStrengthEntry entry in index.Entries
                     .OrderByDescending(value => value.Lift.CompositeScore)
                     .Take(40))
        {
            markdown.Append("| ").Append(entry.CardId)
                .Append(" | ").Append(entry.TowerDefinitionId)
                .Append(" | ").Append(entry.SubjectType)
                .Append(" | ").Append(Number(entry.Lift.CompositeScore))
                .Append(" | ").Append(Number(entry.Lift.WinRateLift))
                .Append(" | ").Append(entry.Lift.RuntimeFailureSeedCount)
                .Append(" | ").Append(entry.IsEvaluable ? "yes" : "no")
                .Append(" | ").Append(entry.IsGoodStandalone ? "yes" : "no")
                .Append(" | ").Append(string.Join(" / ",
                    entry.Lift.RuntimeFailureDiagnostics)
                    .Replace('|', '/'))
                .Append(entry.Lift.RuntimeFailureDiagnostics.Count > 0 &&
                        !string.IsNullOrWhiteSpace(entry.FailureReason)
                    ? " / "
                    : string.Empty)
                .Append(entry.FailureReason.Replace('|', '/'))
                .AppendLine(" |");
        }
        File.WriteAllText(
            Path.Combine(directory, "card-strength-index.md"),
            markdown.ToString(),
            new UTF8Encoding(false));
    }

    private static void WriteCoverage(
        string directory,
        CardCoverageReport report)
    {
        JsonSupport.Write(
            Path.Combine(directory, "card-coverage.json"),
            report);
        var csv = new StringBuilder();
        csv.AppendLine(
            "cardId,classification,legalContexts,validContexts,clearableContexts," +
            "bestTower,bestLevel,bestSubject,bestSlot,bestWinRate,bestCompositeLift,reasons");
        foreach (CardCoverageEntry entry in report.Cards.OrderBy(
                     value => value.CardId,
                     StringComparer.Ordinal))
        {
            csv.AppendLine(string.Join(',', new[]
            {
                Csv(entry.CardId),
                Csv(entry.Classification.ToString()),
                entry.LegalExperimentCount.ToString(
                    CultureInfo.InvariantCulture),
                entry.ValidExperimentCount.ToString(
                    CultureInfo.InvariantCulture),
                entry.ClearableExperimentCount.ToString(
                    CultureInfo.InvariantCulture),
                Csv(entry.BestTowerDefinitionId),
                entry.BestTowerLevel.ToString(CultureInfo.InvariantCulture),
                Csv(entry.BestSubjectType.ToString()),
                entry.BestSlotIndex.ToString(CultureInfo.InvariantCulture),
                entry.BestCandidateWinRate.ToString(
                    "0.####",
                    CultureInfo.InvariantCulture),
                entry.BestCompositeLift.ToString(
                    "0.####",
                    CultureInfo.InvariantCulture),
                Csv(string.Join(" | ", entry.Reasons))
            }));
        }
        File.WriteAllText(
            Path.Combine(directory, "card-coverage.csv"),
            csv.ToString(),
            new UTF8Encoding(false));

        var markdown = new StringBuilder();
        markdown.AppendLine("# Easy card viable-path coverage")
            .AppendLine()
            .AppendLine("- Difficulty: `" + report.DifficultyId + "`")
            .AppendLine("- Active cards: " + report.ActiveCardCount)
            .AppendLine("- Cards with a legal path: " +
                report.CardsWithLegalPath + "/" + report.ActiveCardCount)
            .AppendLine("- Cards with a winning path: " +
                report.CardsWithClearablePath + "/" + report.ActiveCardCount)
            .AppendLine()
            .AppendLine("| Card | Classification | Best context | Win rate | Invalid/runtime detail |")
            .AppendLine("|---|---|---|---:|---|");
        foreach (CardCoverageEntry entry in report.Cards.OrderBy(
                     value => value.CardId,
                     StringComparer.Ordinal))
        {
            int runtimeFailures = entry.Experiments.Sum(experiment =>
                experiment.RuntimeFailureRunCount);
            markdown.Append("| ").Append(entry.CardId)
                .Append(" | ").Append(entry.Classification)
                .Append(" | ").Append(entry.BestTowerDefinitionId)
                .Append(" L").Append(entry.BestTowerLevel)
                .Append(' ').Append(entry.BestSubjectType)
                .Append(" slot ").Append(entry.BestSlotIndex)
                .Append(" | ").Append(entry.BestCandidateWinRate.ToString(
                    "P1",
                    CultureInfo.InvariantCulture))
                .Append(" | runtime failures: ").Append(runtimeFailures)
                .AppendLine(" |");
        }
        File.WriteAllText(
            Path.Combine(directory, "card-coverage.md"),
            markdown.ToString(),
            new UTF8Encoding(false));
    }

    private static CardExperimentEnumeration CoverageEnumeration(
        CardExperimentEnumeration source,
        IReadOnlyList<CardStrengthExperiment> experiments) => new()
    {
        SchemaVersion = source.SchemaVersion,
        DifficultyId = source.DifficultyId,
        ContentHash = source.ContentHash,
        ActiveCardIds = new List<string>(source.ActiveCardIds),
        ActiveTowerIds = new List<string>(source.ActiveTowerIds),
        StrengthExperiments = experiments.ToList(),
        UnsupportedContexts = source.UnsupportedContexts.ToList(),
        EnumeratesAllTowerLevels = source.EnumeratesAllTowerLevels,
        SupportsMixedSlotSubjects = source.SupportsMixedSlotSubjects,
        SupportsRepeatedCardInstances = source.SupportsRepeatedCardInstances,
        SupportsOrderedPrograms = source.SupportsOrderedPrograms
    };

    private static void WriteSynergyCsvAndMarkdown(
        string directory,
        CardSynergyIndex index,
        int experimentCount,
        int seedCount)
    {
        var csv = new StringBuilder();
        csv.AppendLine(
            "difficulty,tower,towerLevel,orderedProgram,matchedSeeds," +
            "cleanMetricSeeds,runtimeFailureSeeds,runtimeFailureRuns," +
            "programLift,synergyLift,runtimeDiagnostics,source");
        foreach (CardSynergyEntry entry in index.Entries
                     .OrderByDescending(value => value.SynergyLift.CompositeScore))
        {
            csv.AppendLine(string.Join(',', new[]
            {
                entry.DifficultyId,
                entry.TowerDefinitionId,
                entry.TowerLevel.ToString(CultureInfo.InvariantCulture),
                string.Join(">", entry.OrderedProgram.Select(step =>
                    step.SlotIndex + ":" + step.SubjectType + ":" +
                    step.CardId)),
                entry.SynergyLift.MatchedSeedCount.ToString(
                    CultureInfo.InvariantCulture),
                entry.SynergyLift.CleanMetricSeedCount.ToString(
                    CultureInfo.InvariantCulture),
                entry.SynergyLift.RuntimeFailureSeedCount.ToString(
                    CultureInfo.InvariantCulture),
                entry.SynergyLift.RuntimeFailureRunCount.ToString(
                    CultureInfo.InvariantCulture),
                Number(entry.ProgramLift.CompositeScore),
                Number(entry.SynergyLift.CompositeScore),
                string.Join(" | ", entry.SynergyLift.RuntimeFailureDiagnostics)
                    .Replace(',', ';')
                    .Replace('\r', ' ')
                    .Replace('\n', ' '),
                entry.DiscoverySource
            }));
        }
        File.WriteAllText(
            Path.Combine(directory, "card-synergy-index.csv"),
            csv.ToString(),
            new UTF8Encoding(false));
        var markdown = new StringBuilder();
        markdown.AppendLine("# Ordered card synergy discovery")
            .AppendLine()
            .AppendLine("- Ordered pair experiments: " + experimentCount)
            .AppendLine("- Matched seeds per experiment: " + seedCount)
            .AppendLine()
            .AppendLine("| Ordered program | Tower | Synergy lift | Program lift | Runtime failures | Diagnostics |")
            .AppendLine("|---|---|---:|---:|---:|---|");
        foreach (CardSynergyEntry entry in index.Entries
                     .OrderByDescending(value => value.SynergyLift.CompositeScore)
                     .Take(40))
        {
            markdown.Append("| ")
                .Append(string.Join(" → ", entry.OrderedProgram.Select(step =>
                    step.CardId + " (" + step.SubjectType + ")")))
                .Append(" | ").Append(entry.TowerDefinitionId)
                .Append(" | ").Append(Number(
                    entry.SynergyLift.CompositeScore))
                .Append(" | ").Append(Number(
                    entry.ProgramLift.CompositeScore))
                .Append(" | ").Append(
                    entry.SynergyLift.RuntimeFailureSeedCount)
                .Append(" | ").Append(string.Join(" / ",
                    entry.SynergyLift.RuntimeFailureDiagnostics)
                    .Replace('|', '/'))
                .AppendLine(" |");
        }
        File.WriteAllText(
            Path.Combine(directory, "card-synergy-index.md"),
            markdown.ToString(),
            new UTF8Encoding(false));
    }

    private static string Number(double value) => value.ToString(
        "0.####",
        CultureInfo.InvariantCulture);

    private static string Csv(string value) =>
        '"' + (value ?? string.Empty).Replace("\"", "\"\"") + '"';
}
