using System;
using System.Linq;
using System.Threading;
using RuleforgeTD.BalanceCli.Content;
using RuleforgeTD.BalanceCli.Infrastructure;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.BalanceCli.Simulation;

/// <summary>Replays recorded commands and Step calls without invoking a policy.</summary>
public sealed class ReplayRunner
{
    private readonly HeadlessContentLoader contentLoader;

    public ReplayRunner(HeadlessContentLoader contentLoader)
    {
        this.contentLoader = contentLoader ??
            throw new ArgumentNullException(nameof(contentLoader));
    }

    public ReplayRunner(RepositoryPaths paths)
        : this(new HeadlessContentLoader(paths))
    {
    }

    public ReplayVerificationResult Replay(
        string replayPath,
        CancellationToken cancellationToken = default)
    {
        ReplayVerificationResult result = Run(replayPath, cancellationToken);
        result.ReplayPath = replayPath;
        return result;
    }

    public ReplayVerificationResult Run(
        string replayPath,
        CancellationToken cancellationToken = default) =>
        Run(JsonSupport.Read<ReplayRecord>(replayPath), cancellationToken);

    public ReplayVerificationResult Run(
        ReplayRecord replay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replay);
        var verification = new ReplayVerificationResult();
        try
        {
            if (replay.SchemaVersion is not (1 or 2))
            {
                verification.Mismatches.Add(
                    "Unsupported replay schemaVersion " +
                    replay.SchemaVersion + ".");
            }
            LoadedSimulationContent loaded = contentLoader.Load(
                replay.DifficultyId,
                replay.Scenario);
            Compare(
                verification,
                "base content hash",
                replay.BaseContentHash,
                loaded.BaseContentHash);
            Compare(
                verification,
                "difficulty profile hash",
                replay.DifficultyProfileHash,
                loaded.DifficultyProfileHash);
            Compare(
                verification,
                "scenario hash",
                replay.ScenarioHash,
                loaded.ScenarioHash);
            Compare(
                verification,
                "compiled content hash",
                replay.ContentHash,
                loaded.CompiledContentHash);

            var simulation = new GameSimulation();
            simulation.Initialize(loaded.Content, replay.GameSeed);
            ReplayOperationRecord[] operations = replay.Operations
                .OrderBy(operation => operation.Sequence)
                .ToArray();
            bool sawRejectedCommand = false;
            for (int index = 0; index < operations.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReplayOperationRecord operation = operations[index];
                if (operation.Sequence != index)
                {
                    verification.Mismatches.Add(
                        "Replay operation sequence is not contiguous at " +
                        index + ".");
                }

                SimulationSnapshot before = simulation.GetSnapshot();
                CompareState(
                    verification,
                    operation,
                    before,
                    StateHash(simulation.ComputeStateHash()),
                    beforeOperation: true);
                if (operation.Kind == ReplayOperationKind.Command)
                {
                    if (operation.Command == null)
                    {
                        verification.Mismatches.Add(
                            "Command operation " + index +
                            " has no command payload.");
                        break;
                    }
                    GameCommand command = operation.Command.ToCommand();
                    CommandResult actual = simulation.Submit(in command);
                    sawRejectedCommand |= !actual.Accepted;
                    Compare(
                        verification,
                        "command " + index + " accepted",
                        operation.Command.Accepted,
                        actual.Accepted);
                    Compare(
                        verification,
                        "command " + index + " error",
                        operation.Command.Error,
                        actual.Error);
                    Compare(
                        verification,
                        "command " + index + " message",
                        operation.Command.Message,
                        actual.Message);
                }
                else if (operation.Kind == ReplayOperationKind.Step)
                {
                    simulation.Step();
                }
                else
                {
                    verification.Mismatches.Add(
                        "Unknown replay operation kind at " + index + ".");
                    break;
                }

                SimulationSnapshot after = simulation.GetSnapshot();
                CompareState(
                    verification,
                    operation,
                    after,
                    StateHash(simulation.ComputeStateHash()),
                    beforeOperation: false);
            }

            SimulationSnapshot final = simulation.GetSnapshot();
            int derivedDecisionCount = CountDecisions(
                operations,
                verification);
            if (replay.SchemaVersion >= 2)
            {
                Compare(
                    verification,
                    "total decisions reconstructed from replay operations",
                    replay.TotalDecisions,
                    derivedDecisionCount);
            }
            verification.FinalPhase = final.Phase;
            verification.FinalTick = final.Tick;
            verification.RemainingBaseHealth = final.BaseHealth;
            verification.FinalGold = final.Gold;
            verification.FinalStateHash =
                StateHash(simulation.ComputeStateHash());
            verification.FinalSnapshotHash =
                StableSnapshotProjection.ComputeHash(final, loaded.Content);
            verification.Result = DeriveOutcome(
                replay,
                final,
                derivedDecisionCount,
                sawRejectedCommand,
                verification);

            Compare(verification, "result", replay.Result, verification.Result);
            Compare(verification, "final phase", replay.FinalPhase, final.Phase);
            Compare(verification, "final tick", replay.FinalTick, final.Tick);
            Compare(
                verification,
                "remaining base health",
                replay.RemainingBaseHealth,
                final.BaseHealth);
            Compare(verification, "final gold", replay.FinalGold, final.Gold);
            Compare(
                verification,
                "final authoritative state hash",
                replay.FinalStateHash,
                verification.FinalStateHash);
            Compare(
                verification,
                "final snapshot hash",
                replay.FinalSnapshotHash,
                verification.FinalSnapshotHash);
            Compare(
                verification,
                "final tower state",
                JsonSupport.SerializeStable(replay.FinalTowers),
                JsonSupport.SerializeStable(
                    SnapshotRecords.FinalTowers(final)));
            Compare(
                verification,
                "final card state",
                JsonSupport.SerializeStable(replay.FinalCardStates),
                JsonSupport.SerializeStable(
                    SnapshotRecords.FinalCards(final, loaded.Content)));
        }
        catch (Exception exception)
        {
            verification.Mismatches.Add(
                exception.GetType().Name + ": " + exception.Message);
            verification.Result = SimulationOutcome.Error;
        }

        verification.Matches = verification.Mismatches.Count == 0;
        return verification;
    }

    private static int CountDecisions(
        IReadOnlyList<ReplayOperationRecord> operations,
        ReplayVerificationResult verification)
    {
        int decisions = 0;
        for (int index = 0; index < operations.Count; index++)
        {
            ReplayOperationRecord operation = operations[index];
            if (operation.Kind == ReplayOperationKind.Command)
            {
                decisions++;
                continue;
            }
            if (operation.Kind != ReplayOperationKind.Step)
            {
                continue;
            }

            // A command that leaves the simulation in Combat is followed by a
            // Step in the same HeadlessRunDriver decision. Every other Step is
            // the operation for a combat NoOp decision.
            bool pairedWithPriorCommand = index > 0 &&
                operations[index - 1].Kind == ReplayOperationKind.Command &&
                string.Equals(
                    operations[index - 1].ActionId,
                    operation.ActionId,
                    StringComparison.Ordinal) &&
                operations[index - 1].TickAfter == operation.TickBefore &&
                operations[index - 1].PhaseAfter == operation.PhaseBefore &&
                operation.PhaseBefore == RunPhase.Combat;
            if (!pairedWithPriorCommand)
            {
                decisions++;
            }
        }

        if (decisions < 0)
        {
            // Defensive guard for future operation formats; checked arithmetic
            // above cannot currently make this reachable.
            verification.Mismatches.Add(
                "Replay operation stream produced an invalid decision count.");
            return 0;
        }
        return decisions;
    }

    private static SimulationOutcome DeriveOutcome(
        ReplayRecord replay,
        SimulationSnapshot final,
        int derivedDecisionCount,
        bool sawRejectedCommand,
        ReplayVerificationResult verification)
    {
        if (final.Phase == RunPhase.Victory)
        {
            return SimulationOutcome.Victory;
        }
        if (final.Phase == RunPhase.Defeat)
        {
            return SimulationOutcome.Defeat;
        }

        // HeadlessRunDriver checks the logical-tick limit before the decision
        // limit at the top of every loop. Either independently observable
        // condition is sufficient to reproduce a Timeout.
        bool validTickLimit = replay.Scenario.MaximumLogicalTicks > 0;
        bool validDecisionLimit = replay.Scenario.MaximumDecisions > 0;
        if (!validTickLimit || !validDecisionLimit)
        {
            verification.Mismatches.Add(
                "Replay scenario contains a non-positive timeout limit.");
        }
        if ((validTickLimit &&
             final.Tick >= replay.Scenario.MaximumLogicalTicks) ||
            (validDecisionLimit &&
             derivedDecisionCount >= replay.Scenario.MaximumDecisions))
        {
            return SimulationOutcome.Timeout;
        }

        // A rejected authoritative GameCommand is replayable evidence for the
        // driver's Error path. Policy selection failures, cancellation, and
        // arbitrary host exceptions have no GameCommand representation and
        // therefore must not be certified by copying replay.Result.
        if (sawRejectedCommand)
        {
            return SimulationOutcome.Error;
        }

        verification.Mismatches.Add(
            "Replay ended in non-terminal phase " + final.Phase +
            " without reaching a recorded timeout limit or reproducing a " +
            "rejected GameCommand; recorded outcome " + replay.Result +
            " is not independently verifiable from the replay stream.");
        return SimulationOutcome.Error;
    }

    private static void CompareState(
        ReplayVerificationResult verification,
        ReplayOperationRecord operation,
        SimulationSnapshot snapshot,
        string stateHash,
        bool beforeOperation)
    {
        string position = beforeOperation ? "before" : "after";
        long expectedTick = beforeOperation
            ? operation.TickBefore
            : operation.TickAfter;
        RunPhase expectedPhase = beforeOperation
            ? operation.PhaseBefore
            : operation.PhaseAfter;
        string expectedHash = beforeOperation
            ? operation.StateHashBefore
            : operation.StateHashAfter;
        Compare(
            verification,
            "operation " + operation.Sequence + " tick " + position,
            expectedTick,
            snapshot.Tick);
        Compare(
            verification,
            "operation " + operation.Sequence + " phase " + position,
            expectedPhase,
            snapshot.Phase);
        Compare(
            verification,
            "operation " + operation.Sequence + " state hash " + position,
            expectedHash,
            stateHash);
    }

    private static void Compare<T>(
        ReplayVerificationResult verification,
        string label,
        T expected,
        T actual)
    {
        if (!Equals(expected, actual))
        {
            verification.Mismatches.Add(
                label + " mismatch: expected '" + expected +
                "', actual '" + actual + "'.");
        }
    }

    internal static string StateHash(ulong hash) => hash.ToString("X16");
}
