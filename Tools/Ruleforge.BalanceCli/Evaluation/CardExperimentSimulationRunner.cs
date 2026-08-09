using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RuleforgeTD.BalanceCli.Content;
using RuleforgeTD.BalanceCli.Infrastructure;
using RuleforgeTD.BalanceCli.Policies;
using RuleforgeTD.BalanceCli.Simulation;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.BalanceCli.Evaluation;

public static class CardExperimentFailureCodes
{
    public const string UnsupportedTowerLevel =
        "UNSUPPORTED_TOWER_LEVEL_FIXTURE";
    public const string UnsupportedMixedSubjects =
        "UNSUPPORTED_PER_SLOT_MIXED_SUBJECT_FIXTURE";
    public const string UnsupportedDuplicateCard =
        "UNSUPPORTED_DUPLICATE_CARD_FIXTURE";
    public const string TowerNotStartingChoice =
        "TOWER_NOT_AVAILABLE_AS_STARTING_FIXTURE";
    public const string TowerNotLegallyAccessible =
        "TOWER_NOT_LEGALLY_ACCESSIBLE_IN_FIXTURE";
    public const string IllegalCardPlacement = "ILLEGAL_CARD_PLACEMENT";
    public const string FixtureNotEquipped = "FIXTURE_CARD_NOT_EQUIPPED";
    public const string FixtureNotExecuted = "FIXTURE_CARD_NOT_EXECUTED";
    public const string FixtureContaminated = "FIXTURE_CONTAMINATED";
    public const string FixtureContextMismatch = "FIXTURE_CONTEXT_MISMATCH";
    public const string SimulationError = "SIMULATION_ERROR";
    public const string SimulationTimeout = "SIMULATION_TIMEOUT";
    public const string SafetyLimitReached = "SAFETY_LIMIT_REACHED";
    public const string RejectedCommand = "REJECTED_GAME_COMMAND";
}

public sealed class CardExperimentSimulationOptions
{
    public int MaximumLogicalTicks { get; set; } = 60000;
    public int MaximumDecisions { get; set; } = 200000;
    public bool RequireFixtureExecution { get; set; } = true;
    public bool RejectCommandRejections { get; set; } = true;
    public bool CoverageNoviceMode { get; set; }
    public Func<SimulationResult, double> GoldEfficiencyProjector { get; set; } =
        DefaultGoldEfficiency;
    public Func<SimulationResult, double> BossStabilityProjector { get; set; } =
        DefaultBossStability;

    private static double DefaultGoldEfficiency(SimulationResult result) =>
        result.GoldEarned / (double)Math.Max(1, result.GoldSpent);

    private static double DefaultBossStability(SimulationResult result)
    {
        int observed = Math.Max(
            result.BossAbilityTelegraphedCount,
            result.BossAbilityActivatedCount +
            result.Telemetry.BossAbilityBlockedCount);
        double handling = observed == 0
            ? 0
            : (result.Telemetry.BossAbilityBlockedCount -
               result.BossAbilityActivatedCount) / (double)observed;
        return (result.Result == SimulationOutcome.Victory ? 1.0 : 0.0) +
               handling;
    }
}

/// <summary>
/// Production adapter from the card evaluators to the authoritative headless
/// driver. Scenario composition may alter only the owned starting-card fixture;
/// every placement, subject selection, equip, move, and upgrade decision still
/// travels through LegalAction and GameCommand.
/// </summary>
public sealed class CardExperimentSimulationRunner
{
    private readonly HeadlessContentLoader contentLoader;
    private readonly HeadlessRunDriver driver;
    private readonly CardExperimentSimulationOptions options;

    public CardExperimentSimulationRunner(
        HeadlessContentLoader contentLoader,
        CardExperimentSimulationOptions? options = null,
        Func<IPlayerPolicy>? fallbackPolicyFactory = null,
        LegalActionGenerator? legalActionGenerator = null)
    {
        ArgumentNullException.ThrowIfNull(contentLoader);
        this.contentLoader = contentLoader;
        this.options = options ?? new CardExperimentSimulationOptions();
        ValidateOptions(this.options);
        // Retained for source compatibility with the first integration draft.
        // Experiment runs now always use the contamination-free dedicated
        // fixture policy; an arbitrary fallback cannot safely equip cards.
        _ = fallbackPolicyFactory;
        driver = new HeadlessRunDriver(contentLoader, legalActionGenerator);
    }

    public CardExperimentRunner AsDelegate() => RunAsync;

    public ValueTask<EvaluationRunMetrics> RunAsync(
        CardExperimentVariant variant,
        SeedPair seed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(variant);
        cancellationToken.ThrowIfCancellationRequested();
        string? unsupported = ValidateRepresentableVariant(variant);
        if (unsupported != null)
        {
            return ValueTask.FromResult(Invalid(seed, unsupported));
        }
        string fixtureContextHash = string.Empty;
        try
        {
            IReadOnlyList<CardProgramStep> program = variant.OrderedProgram ??
                Array.Empty<CardProgramStep>();
            LoadedSimulationContent fixtureContent = contentLoader.Load(
                variant.DifficultyId,
                SimulationScenario.Standard());
            string? contentError = ValidateAgainstContent(
                fixtureContent.Content,
                variant,
                out string startingTower,
                out string controlCard,
                out int upgradeBudget);
            if (contentError != null)
            {
                return ValueTask.FromResult(Invalid(seed, contentError));
            }
            int fixtureStartingGold = options.CoverageNoviceMode
                ? checked(
                    fixtureContent.Content.Run.StartingGold +
                    upgradeBudget)
                : upgradeBudget;
            fixtureContextHash = ComputeFixtureContextHash(
                variant,
                startingTower,
                controlCard,
                fixtureStartingGold,
                fixtureContent.CompiledContentHash);
            var scenario = new SimulationScenario
            {
                ScenarioId = "card-experiment-" +
                    JsonSupport.Sha256Text(variant.VariantId)[..16],
                ForcedStartingTowerId = startingTower,
                ForcedPlacedTowerId = variant.TowerDefinitionId,
                ForcedTowerLevel = variant.TowerLevel,
                ForcedTowerLevelIsMinimum = options.CoverageNoviceMode,
                ForcedSubjectType = null,
                StartingGoldOverride = fixtureStartingGold,
                FixtureControlCardId = controlCard,
                FixtureCardProgram = program.Select((step, order) =>
                    new SimulationCardFixtureSlot
                    {
                        Order = order,
                        CardId = step.CardId,
                        SlotIndex = step.SlotIndex,
                        SubjectType = step.SubjectType
                    }).ToList(),
                DisableCardRewardChoices = true,
                ReplaceStartingCards = true,
                AdditionalStartingCards = new[] { controlCard }
                    .Concat(program.Select(step => step.CardId))
                    .ToList(),
                MaximumLogicalTicks = options.MaximumLogicalTicks,
                MaximumDecisions = options.MaximumDecisions,
                CaptureReplay = false,
                CaptureTelemetry = true
            };
            IPlayerPolicy policy = options.CoverageNoviceMode
                ? new CardCoverageNovicePolicy()
                : new CardExperimentFixturePolicy();
            SimulationRunOutput output = driver.Execute(
                new SimulationRunRequest
                {
                    DifficultyId = variant.DifficultyId,
                    PolicyId = policy.PolicyId,
                    GameSeed = seed.GameSeed,
                    PolicySeed = seed.PolicySeed,
                    Scenario = scenario,
                    WriteResult = false,
                    WriteReplay = false
                },
                policy,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            SimulationResult result = output.Result;
            string? runtimeFailure = GetRuntimeFailure(
                result,
                includeSafetyLimits: false);
            bool fixtureVerified = false;
            if (runtimeFailure == null)
            {
                string? fixtureFailure = ValidateFixture(
                    variant,
                    scenario,
                    result,
                    options.CoverageNoviceMode);
                if (fixtureFailure != null)
                {
                    return ValueTask.FromResult(Invalid(
                        seed,
                        fixtureFailure,
                        fixtureContextHash));
                }
                fixtureVerified = true;
                runtimeFailure = GetRuntimeFailure(
                    result,
                    includeSafetyLimits: true);
            }
            double goldEfficiency = options.GoldEfficiencyProjector(result);
            double bossStability = options.BossStabilityProjector(result);
            if (!double.IsFinite(goldEfficiency) ||
                !double.IsFinite(bossStability))
            {
                runtimeFailure = CardExperimentFailureCodes.SimulationError +
                    ": metric projector returned a non-finite value";
                goldEfficiency = 0;
                bossStability = 0;
            }
            bool isRuntimeFailure = runtimeFailure != null;
            return ValueTask.FromResult(new EvaluationRunMetrics(
                seed,
                !isRuntimeFailure &&
                    result.Result == SimulationOutcome.Victory,
                result.RemainingBaseHealth,
                result.ClearedWaveCount,
                result.TotalLeakDamage,
                goldEfficiency,
                bossStability,
                true,
                runtimeFailure,
                result.ScenarioHash,
                fixtureContextHash,
                fixtureVerified,
                isRuntimeFailure));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(RuntimeLoss(
                seed,
                CardExperimentFailureCodes.SimulationError + ": " +
                exception.GetType().Name + ": " + exception.Message,
                fixtureContextHash));
        }
    }

    private static string SelectControlCard(CompiledContent content) =>
        content.Cards
            .Select(card => card.StableId)
            .OrderBy(cardId => cardId, StringComparer.Ordinal)
            .FirstOrDefault() ??
            throw new InvalidOperationException(
                "No active card is available as the fixture control.");

    private static int ComputeFixtureBudget(
        CompiledContent content,
        string towerDefinitionId,
        int targetLevel)
    {
        if (!content.TryGetTowerId(
                towerDefinitionId,
                out TowerDefinitionId towerId))
        {
            throw new InvalidOperationException(
                "Unknown fixture tower: " + towerDefinitionId + ".");
        }
        CompiledTowerDefinition tower = content.GetTower(towerId);
        if (targetLevel < 1 || targetLevel > tower.LevelCount)
        {
            throw new InvalidOperationException(
                "Fixture tower level is out of range: " + targetLevel + ".");
        }
        int budget = content.Run.FreeInitialTowerCount > 0
            ? 0
            : tower.ConstructionCost;
        for (int level = 2; level <= targetLevel; level++)
        {
            if (!tower.TryGetLevel(level, out CompiledTowerLevelBalance balance))
            {
                throw new InvalidOperationException(
                    "Missing fixture tower level " + level + ".");
            }
            budget = checked(budget + balance.UpgradeCost);
        }
        return budget;
    }

    private static string? ValidateAgainstContent(
        CompiledContent content,
        CardExperimentVariant variant,
        out string startingTower,
        out string controlCard,
        out int fixtureBudget)
    {
        startingTower = string.Empty;
        controlCard = string.Empty;
        fixtureBudget = 0;
        if (!content.TryGetTowerId(
                variant.TowerDefinitionId,
                out TowerDefinitionId targetTowerId))
        {
            return CardExperimentFailureCodes.TowerNotLegallyAccessible +
                ": unknown compiled tower '" + variant.TowerDefinitionId + "'.";
        }
        CompiledTowerDefinition tower = content.GetTower(targetTowerId);
        bool isStartingChoice = content.Run.StartingTowerChoices.Contains(
            targetTowerId);
        bool isInitiallyUnlocked = content.Run.InitiallyUnlockedTowers.Contains(
            targetTowerId);
        if (!isStartingChoice && !isInitiallyUnlocked)
        {
            return CardExperimentFailureCodes.TowerNotLegallyAccessible +
                ": tower is neither a starting choice nor initially unlocked.";
        }
        TowerDefinitionId[] startingChoices = content.Run.StartingTowerChoices;
        if (startingChoices.Length == 0)
        {
            return CardExperimentFailureCodes.TowerNotStartingChoice +
                ": compiled run has no starting choice.";
        }
        startingTower = isStartingChoice
            ? tower.StableId
            : content.GetTower(startingChoices[0]).StableId;
        if (!tower.TryGetLevel(
                variant.TowerLevel,
                out CompiledTowerLevelBalance level))
        {
            return CardExperimentFailureCodes.UnsupportedTowerLevel +
                ": tower '" + tower.StableId + "' has no level " +
                variant.TowerLevel + ".";
        }

        int totalCompute = 0;
        var occupiedSlots = new HashSet<int>();
        foreach (CardProgramStep step in variant.OrderedProgram)
        {
            if (!content.TryGetCardId(step.CardId, out CardId cardId))
            {
                return CardExperimentFailureCodes.IllegalCardPlacement +
                    ": unknown compiled card '" + step.CardId + "'.";
            }
            CompiledCardDefinition card = content.GetCard(cardId);
            if (step.SubjectType == SubjectType.Projectile &&
                tower.Trigger != TowerTrigger.Attack)
            {
                return CardExperimentFailureCodes.IllegalCardPlacement +
                    ": tower trigger " + tower.Trigger +
                    " cannot create a projectile subject.";
            }
            if (step.SlotIndex < 0 ||
                step.SlotIndex + card.SlotCost > level.UnlockedSlots)
            {
                return CardExperimentFailureCodes.IllegalCardPlacement +
                    ": card '" + step.CardId + "' does not fit unlocked slot " +
                    step.SlotIndex + " at level " + variant.TowerLevel + ".";
            }
            for (int slot = step.SlotIndex;
                 slot < step.SlotIndex + card.SlotCost;
                 slot++)
            {
                if (!occupiedSlots.Add(slot))
                {
                    return CardExperimentFailureCodes.IllegalCardPlacement +
                        ": fixture card slot ranges overlap at slot " + slot + ".";
                }
            }
            totalCompute = checked(totalCompute + card.ComputeCost);
        }
        if (totalCompute > level.ComputeCapacity)
        {
            return CardExperimentFailureCodes.IllegalCardPlacement +
                ": fixture compute cost " + totalCompute +
                " exceeds level capacity " + level.ComputeCapacity + ".";
        }

        controlCard = SelectControlCard(content);
        fixtureBudget = ComputeFixtureBudget(
            content,
            variant.TowerDefinitionId,
            variant.TowerLevel);
        return null;
    }

    private static string ComputeFixtureContextHash(
        CardExperimentVariant variant,
        string startingTower,
        string controlCard,
        int fixtureBudget,
        string contentHash)
    {
        var descriptor = new FixtureContextDescriptor
        {
            DifficultyId = variant.DifficultyId,
            ContentHash = contentHash,
            StartingTowerId = startingTower,
            PlacedTowerId = variant.TowerDefinitionId,
            TowerLevel = variant.TowerLevel,
            StartingGold = fixtureBudget,
            ControlCardId = controlCard
        };
        return JsonSupport.Sha256Text(JsonSupport.SerializeStable(descriptor));
    }

    private string? GetRuntimeFailure(
        SimulationResult result,
        bool includeSafetyLimits)
    {
        if (result.Result == SimulationOutcome.Timeout)
        {
            return CardExperimentFailureCodes.SimulationTimeout + ": " +
                result.Error;
        }
        if (result.Result == SimulationOutcome.Error ||
            !string.IsNullOrWhiteSpace(result.Error))
        {
            return CardExperimentFailureCodes.SimulationError + ": " +
                result.Error;
        }
        if (options.RejectCommandRejections && result.RejectedCommandCount > 0)
        {
            return CardExperimentFailureCodes.RejectedCommand + ": " +
                result.RejectedCommandCount;
        }
        if (includeSafetyLimits && result.SafetyLimitReachedCount > 0)
        {
            return CardExperimentFailureCodes.SafetyLimitReached + ": " +
                result.SafetyLimitReachedCount;
        }
        return null;
    }

    private string? ValidateFixture(
        CardExperimentVariant variant,
        SimulationScenario scenario,
        SimulationResult result,
        bool coverageNoviceMode)
    {
        FinalTowerRecord? fixtureTower = result.FinalTowers
            .Where(tower => string.Equals(
                tower.DefinitionId,
                variant.TowerDefinitionId,
                StringComparison.Ordinal))
            .OrderBy(tower => tower.TowerInstanceId)
            .FirstOrDefault();
        if (fixtureTower == null ||
            (!coverageNoviceMode && fixtureTower.Level != variant.TowerLevel) ||
            (coverageNoviceMode && fixtureTower.Level < variant.TowerLevel))
        {
            return CardExperimentFailureCodes.FixtureNotEquipped +
                ": expected tower " + variant.TowerDefinitionId + " at level " +
                variant.TowerLevel + ".";
        }
        if (!coverageNoviceMode && result.FinalTowers.Count != 1)
        {
            return CardExperimentFailureCodes.FixtureContaminated +
                ": expected exactly one tower, found " +
                result.FinalTowers.Count + ".";
        }
        if (!string.Equals(
                result.SelectedStartingTower,
                scenario.ForcedStartingTowerId,
                StringComparison.Ordinal))
        {
            return CardExperimentFailureCodes.FixtureContextMismatch +
                ": selected starting tower does not match the fixture anchor.";
        }
        if (scenario.StartingGoldOverride.HasValue &&
            result.Telemetry.StartingGold != scenario.StartingGoldOverride.Value)
        {
            return CardExperimentFailureCodes.FixtureContextMismatch +
                ": observed starting gold does not match the fixture budget.";
        }
        if (!coverageNoviceMode &&
            result.EquippedCards.Count != variant.OrderedProgram.Count)
        {
            return CardExperimentFailureCodes.FixtureContaminated +
                ": expected " + variant.OrderedProgram.Count +
                " equipped fixture cards, found " +
                result.EquippedCards.Count + ".";
        }
        foreach (CardProgramStep step in variant.OrderedProgram)
        {
            bool equipped = result.EquippedCards.Any(card =>
                card.TowerInstanceId == fixtureTower.TowerInstanceId &&
                card.SlotIndex == step.SlotIndex &&
                card.SubjectType == step.SubjectType &&
                string.Equals(card.CardId, step.CardId, StringComparison.Ordinal));
            if (!equipped)
            {
                return CardExperimentFailureCodes.FixtureNotEquipped + ": " +
                    step.CardId + " at slot " + step.SlotIndex + ".";
            }
            if (options.RequireFixtureExecution &&
                (!result.CardExecutionCount.TryGetValue(
                    step.CardId,
                    out long executions) ||
                 executions <= 0))
            {
                return CardExperimentFailureCodes.FixtureNotExecuted + ": " +
                    step.CardId + ".";
            }
        }
        if (!coverageNoviceMode &&
            !string.IsNullOrWhiteSpace(scenario.FixtureControlCardId))
        {
            int requiredControlInstances = 1 + variant.OrderedProgram.Count(step =>
                string.Equals(
                    step.CardId,
                    scenario.FixtureControlCardId,
                    StringComparison.Ordinal));
            List<FinalCardRecord> controls = result.FinalCards.Where(card =>
                string.Equals(
                    card.CardId,
                    scenario.FixtureControlCardId,
                    StringComparison.Ordinal)).ToList();
            if (controls.Count < requiredControlInstances ||
                !controls.Any(card => !card.Equipped))
            {
                return CardExperimentFailureCodes.FixtureContaminated +
                    ": reserved control card ownership was not preserved.";
            }
        }
        return null;
    }

    private static string? ValidateRepresentableVariant(
        CardExperimentVariant variant)
    {
        if (variant.TowerLevel < 1)
        {
            return CardExperimentFailureCodes.UnsupportedTowerLevel +
                ": requested level " + variant.TowerLevel + ".";
        }
        if (string.IsNullOrWhiteSpace(variant.DifficultyId) ||
            string.IsNullOrWhiteSpace(variant.TowerDefinitionId) ||
            string.IsNullOrWhiteSpace(variant.VariantId))
        {
            return CardExperimentFailureCodes.SimulationError +
                ": variant context IDs are required.";
        }
        IReadOnlyList<CardProgramStep> program = variant.OrderedProgram ??
            Array.Empty<CardProgramStep>();
        if (program.Any(step =>
                string.IsNullOrWhiteSpace(step.CardId) ||
                step.SlotIndex < 0))
        {
            return CardExperimentFailureCodes.IllegalCardPlacement +
                ": card IDs and non-negative slots are required.";
        }
        if (program.Select(step => step.SlotIndex).Distinct().Count() !=
            program.Count)
        {
            return CardExperimentFailureCodes.IllegalCardPlacement +
                ": two fixture cards request the same slot.";
        }
        int priorSlot = -1;
        foreach (CardProgramStep step in program)
        {
            if (step.SlotIndex <= priorSlot)
            {
                return CardExperimentFailureCodes.IllegalCardPlacement +
                    ": ordered program slots must be strictly increasing.";
            }
            priorSlot = step.SlotIndex;
        }
        return null;
    }

    private static EvaluationRunMetrics Invalid(
        SeedPair seed,
        string reason,
        string fixtureContextHash = "") =>
        new(
            seed,
            false,
            0,
            0,
            0,
            0,
            0,
            false,
            reason,
            string.Empty,
            fixtureContextHash,
            false);

    private static EvaluationRunMetrics RuntimeLoss(
        SeedPair seed,
        string reason,
        string fixtureContextHash = "") =>
        new(
            seed,
            false,
            0,
            0,
            0,
            0,
            0,
            true,
            reason,
            string.Empty,
            fixtureContextHash,
            false,
            true);

    private static void ValidateOptions(CardExperimentSimulationOptions options)
    {
        if (options.MaximumLogicalTicks <= 0 || options.MaximumDecisions <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Logical tick and decision limits must be positive.");
        }
        ArgumentNullException.ThrowIfNull(options.GoldEfficiencyProjector);
        ArgumentNullException.ThrowIfNull(options.BossStabilityProjector);
    }

    private sealed class FixtureContextDescriptor
    {
        public int SchemaVersion { get; set; } = 1;
        public string DifficultyId { get; set; } = string.Empty;
        public string ContentHash { get; set; } = string.Empty;
        public string StartingTowerId { get; set; } = string.Empty;
        public string PlacedTowerId { get; set; } = string.Empty;
        public int TowerLevel { get; set; }
        public int StartingGold { get; set; }
        public string ControlCardId { get; set; } = string.Empty;
        public bool CardRewardChoicesDisabled { get; set; } = true;
    }
}

public sealed class UnsupportedCardExperimentContext
{
    public string ReasonCode { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string CardId { get; set; } = string.Empty;
    public string TowerDefinitionId { get; set; } = string.Empty;
    public int TowerLevel { get; set; }
    public List<SubjectType> SubjectTypes { get; set; } = new();
    public List<int> SlotIndices { get; set; } = new();
}

public sealed class CardExperimentEnumeration
{
    public int SchemaVersion { get; set; } = 2;
    public string DifficultyId { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public List<string> ActiveCardIds { get; set; } = new();
    public List<string> ActiveTowerIds { get; set; } = new();
    public List<CardStrengthExperiment> StrengthExperiments { get; set; } = new();
    public List<CardSynergyPairExperiment> PairExperiments { get; set; } = new();
    public List<UnsupportedCardExperimentContext> UnsupportedContexts { get; set; } =
        new();
    public bool PairEnumerationTruncated { get; set; }
    public bool EnumeratesAllTowerLevels { get; set; } = true;
    public bool SupportsMixedSlotSubjects { get; set; } = true;
    public bool SupportsRepeatedCardInstances { get; set; } = true;
    public bool SupportsOrderedPrograms { get; set; } = true;
    public string TripleEnumerationStrategy { get; set; } =
        "Top-pair beam expansion in SynergyEvaluator; no full cartesian triple enumeration.";
}

public sealed class CardExperimentEnumerationOptions
{
    public bool IncludePairExperiments { get; set; } = true;
    public int MaximumPairExperiments { get; set; } = 100000;
    public int MinimumUnlockedSlotsForPairs { get; set; } = 2;
}

/// <summary>
/// Enumerates active compiled content rather than hard-coding card or tower
/// names. Only contexts representable by the production fixture are emitted as
/// runnable experiments; every structural exclusion is retained alongside it.
/// </summary>
public sealed class CompiledCardExperimentEnumerator
{
    private readonly HeadlessContentLoader contentLoader;

    public CompiledCardExperimentEnumerator(HeadlessContentLoader contentLoader)
    {
        this.contentLoader = contentLoader ??
            throw new ArgumentNullException(nameof(contentLoader));
    }

    public CardExperimentEnumeration Enumerate(
        string difficultyId,
        CardExperimentEnumerationOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(difficultyId))
        {
            throw new ArgumentException(
                "A difficulty ID is required.", nameof(difficultyId));
        }
        options ??= new CardExperimentEnumerationOptions();
        if (options.MaximumPairExperiments < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MaximumPairExperiments));
        }
        if (options.MinimumUnlockedSlotsForPairs < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MinimumUnlockedSlotsForPairs));
        }

        LoadedSimulationContent loaded = contentLoader.Load(
            difficultyId,
            SimulationScenario.Standard());
        CompiledContent content = loaded.Content;
        CompiledCardDefinition[] cards = content.Cards
            .OrderBy(card => card.StableId, StringComparer.Ordinal)
            .ToArray();
        CompiledTowerDefinition[] towers = content.Towers
            .OrderBy(tower => tower.StableId, StringComparer.Ordinal)
            .ToArray();
        var accessibleTowers = new HashSet<string>(
            content.Run.StartingTowerChoices.Select(id =>
                content.GetTower(id).StableId),
            StringComparer.Ordinal);
        accessibleTowers.UnionWith(content.Run.InitiallyUnlockedTowers.Select(id =>
            content.GetTower(id).StableId));
        var result = new CardExperimentEnumeration
        {
            DifficultyId = difficultyId,
            ContentHash = loaded.CompiledContentHash,
            ActiveCardIds = cards.Select(card => card.StableId).ToList(),
            ActiveTowerIds = towers.Select(tower => tower.StableId).ToList()
        };

        foreach (CompiledTowerDefinition tower in towers)
        {
            if (!accessibleTowers.Contains(tower.StableId))
            {
                result.UnsupportedContexts.Add(Unsupported(
                    CardExperimentFailureCodes.TowerNotLegallyAccessible,
                    "The tower is neither a starting choice nor initially " +
                    "unlocked, so no public-command-only fixture can place it.",
                    tower));
                continue;
            }
            SubjectType[] subjects = tower.Trigger == TowerTrigger.Attack
                ? new[] { SubjectType.Projectile, SubjectType.Enemy }
                : new[] { SubjectType.Enemy };
            for (int levelNumber = 1;
                 levelNumber <= tower.LevelCount;
                 levelNumber++)
            {
                if (!tower.TryGetLevel(
                        levelNumber,
                        out CompiledTowerLevelBalance level))
                {
                    result.UnsupportedContexts.Add(Unsupported(
                        CardExperimentFailureCodes.UnsupportedTowerLevel,
                        "The compiled tower level table has a gap.",
                        tower,
                        levelNumber));
                    continue;
                }
                foreach (SubjectType subject in subjects)
                {
                    for (int slot = 0; slot < level.UnlockedSlots; slot++)
                    {
                        foreach (CompiledCardDefinition card in cards)
                        {
                            if (CanPlace(card, slot, level))
                            {
                                result.StrengthExperiments.Add(
                                    new CardStrengthExperiment(
                                        difficultyId,
                                        tower.StableId,
                                        levelNumber,
                                        new CardProgramStep(
                                            card.StableId,
                                            subject,
                                            slot)));
                            }
                        }
                    }
                }
                if (options.IncludePairExperiments &&
                    !result.PairEnumerationTruncated &&
                    level.UnlockedSlots >=
                        options.MinimumUnlockedSlotsForPairs)
                {
                    AddOrderedSubjectPairs(
                        difficultyId,
                        tower,
                        levelNumber,
                        level,
                        subjects,
                        cards,
                        options.MaximumPairExperiments,
                        result);
                }
            }
        }

        var supportedCards = new HashSet<string>(
            result.StrengthExperiments.Select(experiment =>
                experiment.Card.CardId),
            StringComparer.Ordinal);
        foreach (CompiledCardDefinition card in cards.Where(card =>
                     !supportedCards.Contains(card.StableId)))
        {
            result.UnsupportedContexts.Add(new UnsupportedCardExperimentContext
            {
                ReasonCode = CardExperimentFailureCodes.IllegalCardPlacement,
                Detail = "No legal accessible-tower level/subject/slot context " +
                    "has enough unlocked slots and compute capacity.",
                CardId = card.StableId
            });
        }
        return result;
    }

    private static void AddOrderedSubjectPairs(
        string difficultyId,
        CompiledTowerDefinition tower,
        int levelNumber,
        CompiledTowerLevelBalance level,
        IReadOnlyList<SubjectType> subjects,
        IReadOnlyList<CompiledCardDefinition> cards,
        int maximum,
        CardExperimentEnumeration result)
    {
        foreach (SubjectType firstSubject in subjects)
        {
            foreach (SubjectType secondSubject in subjects)
            {
                for (int firstSlot = 0;
                     firstSlot < level.UnlockedSlots;
                     firstSlot++)
                {
                    foreach (CompiledCardDefinition first in cards)
                    {
                        if (!CanPlace(first, firstSlot, level))
                        {
                            continue;
                        }
                        for (int secondSlot = firstSlot + first.SlotCost;
                             secondSlot < level.UnlockedSlots;
                             secondSlot++)
                        {
                            foreach (CompiledCardDefinition second in cards)
                            {
                                if (!CanPlace(second, secondSlot, level) ||
                                    first.ComputeCost + second.ComputeCost >
                                        level.ComputeCapacity)
                                {
                                    continue;
                                }
                                if (result.PairExperiments.Count >= maximum)
                                {
                                    result.PairEnumerationTruncated = true;
                                    result.UnsupportedContexts.Add(
                                        new UnsupportedCardExperimentContext
                                        {
                                            ReasonCode = "PAIR_ENUMERATION_LIMIT",
                                            Detail = "Deterministic ordered pair " +
                                                "enumeration reached its configured " +
                                                "maximum of " + maximum + "."
                                        });
                                    return;
                                }
                                result.PairExperiments.Add(
                                    new CardSynergyPairExperiment(
                                        difficultyId,
                                        tower.StableId,
                                        levelNumber,
                                        new CardProgramStep(
                                            first.StableId,
                                            firstSubject,
                                            firstSlot),
                                        new CardProgramStep(
                                            second.StableId,
                                            secondSubject,
                                            secondSlot)));
                            }
                        }
                    }
                }
            }
        }
    }

    private static bool CanPlace(
        CompiledCardDefinition card,
        int slot,
        CompiledTowerLevelBalance level) =>
        card.SlotCost > 0 &&
        slot >= 0 &&
        slot + card.SlotCost <= level.UnlockedSlots &&
        card.ComputeCost <= level.ComputeCapacity;

    private static UnsupportedCardExperimentContext Unsupported(
        string reason,
        string detail,
        CompiledTowerDefinition tower,
        int level = 1) => new()
        {
            ReasonCode = reason,
            Detail = detail,
            TowerDefinitionId = tower.StableId,
            TowerLevel = level
        };
}
