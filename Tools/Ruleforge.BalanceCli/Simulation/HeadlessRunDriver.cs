using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RuleforgeTD.BalanceCli.Content;
using RuleforgeTD.BalanceCli.Infrastructure;
using RuleforgeTD.BalanceCli.Policies;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.BalanceCli.Simulation;

/// <summary>
/// Runs the authoritative GameSimulation to a terminal phase using only public
/// snapshots, public quotes, GameCommand submission, and Step calls.
/// </summary>
public sealed class HeadlessRunDriver
{
    private readonly HeadlessContentLoader contentLoader;
    private readonly LegalActionGenerator actionGenerator;

    public HeadlessRunDriver(
        HeadlessContentLoader contentLoader,
        LegalActionGenerator? actionGenerator = null)
    {
        this.contentLoader = contentLoader ??
            throw new ArgumentNullException(nameof(contentLoader));
        this.actionGenerator = actionGenerator ?? new LegalActionGenerator();
    }

    public SimulationRunOutput Execute(
        SimulationRunRequest request,
        IPlayerPolicy policy,
        CancellationToken cancellationToken = default,
        ISimulationProgressObserver? progressObserver = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(policy);
        SimulationScenario scenario = request.Scenario?.Clone() ??
            SimulationScenario.Standard();
        ValidateScenario(scenario);

        LoadedSimulationContent loaded = request.DifficultyProfileOverride == null
            ? contentLoader.Load(request.DifficultyId, scenario)
            : contentLoader.LoadProfile(
                request.DifficultyProfileOverride,
                scenario);
        if (!string.Equals(
                loaded.Profile.DifficultyId,
                request.DifficultyId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Profile override difficulty does not match the run request.");
        }
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        string runId = CreateRunId(loaded, scenario, policy, request);
        var simulation = new GameSimulation();
        simulation.Initialize(loaded.Content, request.GameSeed);
        var telemetry = new SimulationTelemetry();
        SimulationSnapshot initial = simulation.GetSnapshot();
        telemetry.ObserveInitial(initial);
        telemetry.ObserveCommandEvents(
            simulation.ReadPresentationEvents(),
            initial);
        ReplayRecorder? recorder = scenario.CaptureReplay
            ? new ReplayRecorder(
                loaded,
                scenario,
                runId,
                timestamp,
                policy.PolicyId,
                policy.PolicyVersion,
                request.GameSeed,
                request.PolicySeed)
            : null;
        var policyRandom = new PolicyRandom(request.PolicySeed);
        var policyMemory = new PolicyMemory();
        var publicKnowledge = PublicGameKnowledge.FromContent(loaded.Content);
        bool memoryInitialized = false;
        int decisions = 0;
        SimulationOutcome outcome = SimulationOutcome.Error;
        string? error = null;
        string lastAction = "initialize";

        ReportProgress(
            progressObserver,
            request,
            policy,
            loaded.Content,
            initial,
            telemetry,
            decisions,
            lastAction);

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SimulationSnapshot snapshot = simulation.GetSnapshot();
                if (snapshot.Phase == RunPhase.Victory)
                {
                    outcome = SimulationOutcome.Victory;
                    break;
                }
                if (snapshot.Phase == RunPhase.Defeat)
                {
                    outcome = SimulationOutcome.Defeat;
                    break;
                }
                if (snapshot.Tick >= scenario.MaximumLogicalTicks)
                {
                    outcome = SimulationOutcome.Timeout;
                    error = "MaximumLogicalTicks reached.";
                    break;
                }
                if (decisions >= scenario.MaximumDecisions)
                {
                    outcome = SimulationOutcome.Timeout;
                    error = "MaximumDecisions reached.";
                    break;
                }

                IReadOnlyList<LegalAction> legalActions =
                    actionGenerator.Generate(simulation, snapshot);
                if (legalActions.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No legal action is available in phase " +
                        snapshot.Phase + ".");
                }
                if (snapshot.Phase == RunPhase.CardPackLoadout &&
                    snapshot.PendingCardInstanceId >= 0 &&
                    !IsPendingCardProgressPossible(snapshot, legalActions))
                {
                    throw new InvalidOperationException(
                        "Card-pack loadout cannot equip or make room for the " +
                        "pending card.");
                }

                UpdatePolicyMemory(
                    policyMemory,
                    snapshot,
                    ref memoryInitialized);
                var context = new PolicyContext
                {
                    DifficultyId = request.DifficultyId,
                    Scenario = scenario,
                    LegalActions = legalActions,
                    PublicKnowledge = publicKnowledge,
                    Random = policyRandom,
                    Memory = policyMemory,
                    CardStrength = request.CardStrength,
                    CardSynergy = request.CardSynergy,
                    Settings = request.PolicySettings,
                    OracleActionScores = request.OracleActionScores
                };
                LegalAction? forcedAction = FindRequiredScenarioAction(
                    snapshot,
                    legalActions,
                    scenario);
                PolicyDecision decision = forcedAction == null
                    ? policy.Decide(snapshot, context)
                    : new PolicyDecision(
                        forcedAction.ActionId,
                        "SCENARIO_FIXTURE");
                LegalAction selected = legalActions.FirstOrDefault(action =>
                    string.Equals(
                        action.ActionId,
                        decision.ActionId,
                        StringComparison.Ordinal)) ??
                    throw new InvalidOperationException(
                        "Policy selected non-legal action '" +
                        decision.ActionId + "'.");
                decisions++;
                policyMemory.DecisionsInPhase++;
                policyMemory.PublicActionHistory.Add(selected.ActionId);
                lastAction = selected.ActionId;

                if (selected.HasCommand)
                {
                    SubmitCommand(
                        simulation,
                        selected,
                        telemetry,
                        recorder,
                        decisions - 1,
                        policyMemory);
                    ReportProgress(
                        progressObserver,
                        request,
                        policy,
                        loaded.Content,
                        simulation.GetSnapshot(),
                        telemetry,
                        decisions,
                        lastAction);
                }
                else if (selected.Kind != LegalActionKind.NoOp ||
                         snapshot.Phase != RunPhase.Combat)
                {
                    throw new InvalidOperationException(
                        "Only combat NoOp may omit a GameCommand.");
                }

                if (simulation.Phase == RunPhase.Combat)
                {
                    StepSimulation(
                        simulation,
                        selected.ActionId,
                        telemetry,
                        recorder,
                        loaded.Content);
                    ReportProgress(
                        progressObserver,
                        request,
                        policy,
                        loaded.Content,
                        simulation.GetSnapshot(),
                        telemetry,
                        decisions,
                        lastAction);
                }
            }
        }
        catch (OperationCanceledException exception)
        {
            outcome = SimulationOutcome.Error;
            error = exception.Message;
        }
        catch (Exception exception)
        {
            outcome = SimulationOutcome.Error;
            error = exception.GetType().Name + ": " + exception.Message;
        }

        SimulationSnapshot finalSnapshot = simulation.GetSnapshot();
        telemetry.ObserveCommandEvents(
            simulation.ReadPresentationEvents(),
            finalSnapshot);
        ReportProgress(
            progressObserver,
            request,
            policy,
            loaded.Content,
            finalSnapshot,
            telemetry,
            decisions,
            lastAction,
            outcome,
            error);
        var result = CreateResult(
            loaded,
            scenario,
            policy,
            request,
            runId,
            timestamp,
            outcome,
            error,
            decisions,
            simulation,
            finalSnapshot,
            telemetry);
        recorder?.Complete(result, finalSnapshot, loaded.Content);
        if (recorder != null)
        {
            result.Operations = new List<ReplayOperationRecord>(
                recorder.Replay.Operations);
        }
        if (recorder != null &&
            !string.IsNullOrWhiteSpace(request.ReplayOutputPath))
        {
            JsonSupport.Write(request.ReplayOutputPath, recorder.Replay);
            result.ReplayPath = request.ReplayOutputPath;
        }
        return new SimulationRunOutput
        {
            Result = result,
            Replay = recorder?.Replay
        };
    }

    public SimulationResult Run(
        string difficultyId,
        IPlayerPolicy policy,
        ulong gameSeed,
        ulong policySeed,
        SimulationScenario? scenario = null,
        string? replayPath = null,
        CancellationToken cancellationToken = default,
        ISimulationProgressObserver? progressObserver = null) =>
        Execute(
            new SimulationRunRequest
            {
                DifficultyId = difficultyId,
                GameSeed = gameSeed,
                PolicySeed = policySeed,
                Scenario = scenario ?? SimulationScenario.Standard(),
                ReplayOutputPath = replayPath
            },
            policy,
            cancellationToken,
            progressObserver).Result;

    private static void ReportProgress(
        ISimulationProgressObserver? observer,
        SimulationRunRequest request,
        IPlayerPolicy policy,
        CompiledContent content,
        SimulationSnapshot snapshot,
        SimulationTelemetry telemetry,
        int decisions,
        string lastAction,
        SimulationOutcome? outcome = null,
        string? error = null)
    {
        if (observer == null)
        {
            return;
        }

        int enemiesAlive = 0;
        int activeStatuses = 0;
        foreach (EnemySnapshot enemy in snapshot.Enemies)
        {
            if (!enemy.Alive)
            {
                continue;
            }
            enemiesAlive++;
            foreach (StatusSnapshot status in enemy.StatusDetails)
            {
                if (status.RemainingTicks > 0)
                {
                    activeStatuses++;
                }
            }
        }

        var update = new SimulationProgressUpdate
        {
            DifficultyId = request.DifficultyId,
            PolicyId = policy.PolicyId,
            GameSeed = request.GameSeed,
            PolicySeed = request.PolicySeed,
            Tick = snapshot.Tick,
            TickRate = content.Run.TickRate,
            Phase = snapshot.Phase,
            WaveNumber = snapshot.WaveIndex < 0
                ? 0
                : Math.Min(content.WaveCount, snapshot.WaveIndex + 1),
            TotalWaves = content.WaveCount,
            BaseHealth = snapshot.BaseHealth,
            StartingBaseHealth = telemetry.StartingBaseHealth,
            Gold = snapshot.Gold,
            GoldEarned = telemetry.GoldEarned,
            GoldSpent = telemetry.GoldSpent,
            EnemiesAlive = enemiesAlive,
            EnemiesKilled = telemetry.EnemyDeathCount,
            EnemiesLeaked = telemetry.LeakedEnemyCount,
            ActiveProjectiles = snapshot.Projectiles.Length,
            ActiveStatuses = activeStatuses,
            TowerCount = snapshot.Towers.Length,
            TotalDamageMilli = telemetry.TotalDamageMilli,
            Decisions = decisions,
            LastAction = lastAction,
            Outcome = outcome,
            Error = error
        };

        try
        {
            observer.Observe(update);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A read-only presentation observer must never change the
            // authoritative simulation outcome.
        }
    }

    private static void SubmitCommand(
        GameSimulation simulation,
        LegalAction action,
        SimulationTelemetry telemetry,
        ReplayRecorder? recorder,
        int commandSequence,
        PolicyMemory policyMemory)
    {
        GameCommand command = action.Command!.Value;
        SimulationSnapshot before = simulation.GetSnapshot();
        string stateBefore = ReplayRunner.StateHash(
            simulation.ComputeStateHash());
        CommandResult commandResult = simulation.Submit(in command);
        SimulationSnapshot after = simulation.GetSnapshot();
        string stateAfter = ReplayRunner.StateHash(
            simulation.ComputeStateHash());
        telemetry.ObserveCommand(
            action.ActionId,
            in command,
            in commandResult,
            before,
            after,
            simulation.Content,
            commandSequence);
        telemetry.ObserveCommandEvents(
            simulation.ReadPresentationEvents(),
            after);
        recorder?.RecordCommand(
            action.ActionId,
            in command,
            in commandResult,
            before,
            after,
            stateBefore,
            stateAfter);
        if (!commandResult.Accepted)
        {
            throw new InvalidOperationException(
                "Legal action '" + action.ActionId +
                "' was rejected by GameLogic: " + commandResult.Error +
                " (" + commandResult.Message + ").");
        }
        if (commandResult.Accepted && before.Phase == RunPhase.Planning)
        {
            policyMemory.PlanningGoldSpent = checked(
                policyMemory.PlanningGoldSpent +
                Math.Max(0, before.Gold - after.Gold));
        }
    }

    private static void StepSimulation(
        GameSimulation simulation,
        string actionId,
        SimulationTelemetry telemetry,
        ReplayRecorder? recorder,
        CompiledContent content)
    {
        SimulationSnapshot before = simulation.GetSnapshot();
        string stateBefore = ReplayRunner.StateHash(
            simulation.ComputeStateHash());
        simulation.Step();
        SimulationSnapshot after = simulation.GetSnapshot();
        string stateAfter = ReplayRunner.StateHash(
            simulation.ComputeStateHash());
        SimulationEventBuffer events = simulation.ReadPresentationEvents();
        telemetry.ObserveStep(before, after, events, content);
        recorder?.RecordStep(
            actionId,
            before,
            after,
            stateBefore,
            stateAfter);
    }

    private static SimulationResult CreateResult(
        LoadedSimulationContent loaded,
        SimulationScenario scenario,
        IPlayerPolicy policy,
        SimulationRunRequest request,
        string runId,
        DateTimeOffset timestamp,
        SimulationOutcome outcome,
        string? error,
        int decisions,
        GameSimulation simulation,
        SimulationSnapshot finalSnapshot,
        SimulationTelemetry telemetry)
    {
        int diagnosticCount = (int)Math.Min(
            (ulong)int.MaxValue,
            simulation.Diagnostics.TotalWritten);
        telemetry.SafetyLimitReachedCount = Math.Max(
            telemetry.SafetyLimitReachedCount,
            diagnosticCount);
        List<EquippedCardRecord> equipped = SnapshotRecords.EquippedCards(
            finalSnapshot,
            loaded.Content);
        List<FinalTowerRecord> towers = SnapshotRecords.FinalTowers(
            finalSnapshot);
        List<FinalCardRecord> cards = SnapshotRecords.FinalCards(
            finalSnapshot,
            loaded.Content);
        var result = new SimulationResult
        {
            RunId = runId,
            Timestamp = timestamp,
            GameVersion = loaded.Content.Version.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            BaseContentHash = loaded.BaseContentHash,
            ContentHash = loaded.CompiledContentHash,
            DifficultyId = request.DifficultyId,
            DifficultyProfileHash = loaded.DifficultyProfileHash,
            ScenarioId = scenario.ScenarioId,
            ScenarioHash = loaded.ScenarioHash,
            PolicyId = policy.PolicyId,
            PolicyVersion = policy.PolicyVersion,
            GameSeed = request.GameSeed,
            PolicySeed = request.PolicySeed,
            Result = outcome,
            FinalRunPhase = finalSnapshot.Phase,
            ClearedWaveCount = outcome == SimulationOutcome.Victory
                ? loaded.Content.WaveCount
                : telemetry.WaveCompletedCount,
            FailedWave = outcome == SimulationOutcome.Victory
                ? 0
                : Math.Max(0, finalSnapshot.WaveIndex + 1),
            RemainingBaseHealth = finalSnapshot.BaseHealth,
            TotalLeakDamage = telemetry.TotalLeakDamage,
            LeakedEnemyCount = telemetry.LeakedEnemyCount,
            GoldEarned = telemetry.GoldEarned,
            GoldSpent = telemetry.GoldSpent,
            GoldUnspent = finalSnapshot.Gold,
            TowerBuildCount = telemetry.TowerBuildCount,
            TowerUpgradeCount = telemetry.TowerUpgradeCount,
            MidWaveTowerBuildCount = telemetry.MidWaveTowerBuildCount,
            MidWaveBuildTicks = new List<long>(telemetry.MidWaveBuildTicks),
            SelectedStartingTower = telemetry.SelectedStartingTower,
            SelectedCards = new List<string>(telemetry.SelectedCards),
            EquippedCards = equipped,
            CardOrder = BuildCardOrder(equipped),
            SlotSubjectTypes = equipped.Select(card => card.SubjectType).ToList(),
            DraftChoices = new List<CardChoiceRecord>(telemetry.DraftChoices),
            CardPackChoices = new List<CardChoiceRecord>(telemetry.CardPackChoices),
            RejectedCommandCount = telemetry.RejectedCommandCount,
            RejectedCommandReasons = new Dictionary<string, int>(
                telemetry.RejectedCommandReasons,
                StringComparer.Ordinal),
            SafetyLimitReachedCount = telemetry.SafetyLimitReachedCount,
            SafetyLimitReasons = new Dictionary<string, int>(
                telemetry.SafetyLimitReasons,
                StringComparer.Ordinal),
            TotalLogicalTicks = finalSnapshot.Tick,
            TotalDecisions = decisions,
            FinalStateHash = ReplayRunner.StateHash(
                simulation.ComputeStateHash()),
            FinalSnapshotHash = StableSnapshotProjection.ComputeHash(
                finalSnapshot,
                loaded.Content),
            Error = error,
            CardExecutionCount = new Dictionary<string, long>(
                telemetry.CardExecutionCount,
                StringComparer.Ordinal),
            DamageByEnemyType = new Dictionary<string, long>(
                telemetry.DamageByEnemyType,
                StringComparer.Ordinal),
            LeaksByEnemyType = new Dictionary<string, int>(
                telemetry.LeaksByEnemyType,
                StringComparer.Ordinal),
            TowerBuildsByDefinition = new Dictionary<string, int>(
                telemetry.TowerBuildsByDefinition,
                StringComparer.Ordinal),
            StatusApplications = new Dictionary<string, long>(
                telemetry.StatusApplications,
                StringComparer.Ordinal),
            BossAbilityActivatedCount = telemetry.BossAbilityActivatedCount,
            BossAbilityTelegraphedCount = telemetry.BossAbilityTelegraphedCount,
            EnemyDeathCount = telemetry.EnemyDeathCount,
            TotalDamageMilli = telemetry.TotalDamageMilli,
            MidWaveBuilds = new List<MidWaveBuildRecord>(telemetry.MidWaveBuilds),
            Commands = new List<CommandLogEntry>(telemetry.Commands),
            PhaseTransitions = new List<PhaseTransitionRecord>(
                telemetry.PhaseTransitions),
            FinalTowers = towers,
            FinalCards = cards,
            Telemetry = telemetry
        };
        foreach (MidWaveBuildRecord build in result.MidWaveBuilds)
        {
            build.ActualOutcomeAfterBuild = outcome.ToString();
        }
        return result;
    }

    private static List<string> BuildCardOrder(
        IEnumerable<EquippedCardRecord> cards) => cards
        .OrderBy(card => card.TowerInstanceId)
        .ThenBy(card => card.SlotIndex)
        .Select(card => card.TowerInstanceId + ":" + card.SlotIndex + ":" +
            card.CardId)
        .ToList();

    private static void UpdatePolicyMemory(
        PolicyMemory memory,
        SimulationSnapshot snapshot,
        ref bool initialized)
    {
        if (!initialized || memory.Phase != snapshot.Phase)
        {
            memory.Phase = snapshot.Phase;
            memory.DecisionsInPhase = 0;
            if (snapshot.Phase == RunPhase.Planning)
            {
                memory.PlanningInitialGold = snapshot.Gold;
                memory.PlanningGoldSpent = 0;
            }
            initialized = true;
        }
    }

    private static LegalAction? FindRequiredScenarioAction(
        SimulationSnapshot snapshot,
        IReadOnlyList<LegalAction> actions,
        SimulationScenario scenario)
    {
        if (snapshot.Phase == RunPhase.AwaitingStartingTower &&
            !string.IsNullOrWhiteSpace(scenario.ForcedStartingTowerId))
        {
            return RequireScenarioAction(actions, action =>
                action.Kind == LegalActionKind.ChooseStartingTower &&
                string.Equals(
                    action.TowerDefinitionId,
                    scenario.ForcedStartingTowerId,
                    StringComparison.Ordinal),
                "forced starting tower");
        }
        if (snapshot.Phase == RunPhase.Planning &&
            snapshot.Towers.Length == 0 &&
            !string.IsNullOrWhiteSpace(scenario.ForcedPlacedTowerId))
        {
            return RequireScenarioAction(actions, action =>
                action.Kind == LegalActionKind.PlaceTower &&
                string.Equals(
                    action.TowerDefinitionId,
                    scenario.ForcedPlacedTowerId,
                    StringComparison.Ordinal),
                "forced placed tower");
        }
        if (snapshot.Phase == RunPhase.Planning &&
            scenario.ForcedTowerLevel.HasValue)
        {
            TowerSnapshot tower = snapshot.Towers
                .Where(item => string.IsNullOrWhiteSpace(
                        scenario.ForcedPlacedTowerId) ||
                    string.Equals(
                        item.DefinitionId,
                        scenario.ForcedPlacedTowerId,
                        StringComparison.Ordinal))
                .OrderBy(item => item.Id)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(tower.DefinitionId))
            {
                if (tower.Level > scenario.ForcedTowerLevel.Value &&
                    !scenario.ForcedTowerLevelIsMinimum)
                {
                    throw new InvalidOperationException(
                        "Fixture tower exceeded the forced level.");
                }
                if (tower.Level < scenario.ForcedTowerLevel.Value)
                {
                    return RequireScenarioAction(actions, action =>
                        action.Kind == LegalActionKind.UpgradeTower &&
                        action.TowerInstanceId == tower.Id,
                        "forced tower upgrade");
                }
            }
        }
        if (snapshot.Phase is RunPhase.Planning or RunPhase.CardPackLoadout)
        {
            LegalAction? fixtureProgram = FindFixtureProgramAction(
                snapshot,
                actions,
                scenario);
            if (fixtureProgram != null)
            {
                return fixtureProgram;
            }
        }
        if (snapshot.Phase is RunPhase.Planning or RunPhase.CardPackLoadout &&
            scenario.ForcedSubjectType.HasValue)
        {
            LegalAction? subjectAction = actions.FirstOrDefault(action =>
                action.SubjectType == scenario.ForcedSubjectType &&
                action.Kind is LegalActionKind.SetTowerSubjectType or
                    LegalActionKind.SetSlotSubjectType);
            return subjectAction;
        }
        return null;
    }

    private static LegalAction? FindFixtureProgramAction(
        SimulationSnapshot snapshot,
        IReadOnlyList<LegalAction> actions,
        SimulationScenario scenario)
    {
        if (scenario.FixtureCardProgram.Count == 0 ||
            string.IsNullOrWhiteSpace(scenario.ForcedPlacedTowerId))
        {
            return null;
        }

        TowerSnapshot tower = snapshot.Towers
            .Where(item => string.Equals(
                item.DefinitionId,
                scenario.ForcedPlacedTowerId,
                StringComparison.Ordinal))
            .OrderBy(item => item.Id)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(tower.DefinitionId))
        {
            return null;
        }

        foreach (SimulationCardFixtureSlot slot in scenario.FixtureCardProgram
                     .OrderBy(value => value.Order))
        {
            if (slot.SlotIndex < 0 ||
                slot.SlotIndex >= tower.CardInstanceIds.Length)
            {
                throw new InvalidOperationException(
                    "Fixture slot is outside the forced tower: " +
                    slot.SlotIndex + ".");
            }

            LegalAction? occupying = actions.FirstOrDefault(action =>
                action.Kind == LegalActionKind.UnequipCard &&
                action.TowerInstanceId == tower.Id &&
                action.SlotIndex == slot.SlotIndex);
            bool correctCard = occupying != null && string.Equals(
                occupying.CardId,
                slot.CardId,
                StringComparison.Ordinal);
            if (occupying != null && !correctCard)
            {
                return occupying;
            }

            if (tower.CardSubjectTypes[slot.SlotIndex] != slot.SubjectType)
            {
                LegalAction subject = actions.FirstOrDefault(action =>
                    action.Kind == LegalActionKind.SetSlotSubjectType &&
                    action.TowerInstanceId == tower.Id &&
                    action.SlotIndex == slot.SlotIndex &&
                    action.SubjectType == slot.SubjectType) ??
                    throw new InvalidOperationException(
                        "Fixture subject is not legal for the forced tower slot.");
                return subject;
            }

            if (correctCard)
            {
                continue;
            }

            LegalAction? placement = actions
                .Where(action =>
                    (action.Kind is LegalActionKind.EquipCard or
                        LegalActionKind.MoveCard) &&
                    action.TowerInstanceId == tower.Id &&
                    action.SlotIndex == slot.SlotIndex &&
                    string.Equals(
                        action.CardId,
                        slot.CardId,
                        StringComparison.Ordinal))
                .OrderByDescending(action => action.CardInstanceId)
                .FirstOrDefault();
            if (placement == null)
            {
                throw new InvalidOperationException(
                    "Fixture card '" + slot.CardId +
                    "' cannot be placed in forced slot " +
                    slot.SlotIndex + ".");
            }
            return placement;
        }
        return null;
    }

    private static LegalAction RequireScenarioAction(
        IReadOnlyList<LegalAction> actions,
        Func<LegalAction, bool> predicate,
        string description) => actions.FirstOrDefault(predicate) ??
        throw new InvalidOperationException(
            "Scenario's " + description + " is not a legal action.");

    private static bool IsPendingCardProgressPossible(
        SimulationSnapshot snapshot,
        IReadOnlyList<LegalAction> actions)
    {
        CardInstanceSnapshot pending = Array.Find(
            snapshot.Cards,
            card => card.Id == snapshot.PendingCardInstanceId);
        if (pending.Id == snapshot.PendingCardInstanceId && pending.Equipped)
        {
            return actions.Any(action =>
                action.Kind == LegalActionKind.ResumeCardPackCombat);
        }
        return actions.Any(action =>
            action.Kind == LegalActionKind.EquipCard &&
            action.CardInstanceId == snapshot.PendingCardInstanceId) ||
            actions.Any(action => action.Kind == LegalActionKind.UnequipCard);
    }

    private static string CreateRunId(
        LoadedSimulationContent loaded,
        SimulationScenario scenario,
        IPlayerPolicy policy,
        SimulationRunRequest request)
    {
        string source = string.Join(
            "|",
            loaded.CompiledContentHash,
            loaded.DifficultyProfileHash,
            loaded.ScenarioHash,
            policy.PolicyId,
            policy.PolicyVersion,
            request.GameSeed,
            request.PolicySeed,
            scenario.ScenarioId);
        return JsonSupport.Sha256Text(source)[..24];
    }

    private static void ValidateScenario(SimulationScenario scenario)
    {
        if (scenario.MaximumLogicalTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scenario.MaximumLogicalTicks));
        }
        if (scenario.MaximumDecisions <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scenario.MaximumDecisions));
        }
    }
}
