using System;
using System.Collections.Generic;
using System.Linq;
using RuleforgeTD.BalanceCli.Content;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.BalanceCli.Simulation;

public sealed class ReplayRecorder
{
    private int nextOperationSequence;
    private int nextCommandSequence;

    public ReplayRecorder(
        LoadedSimulationContent loaded,
        SimulationScenario scenario,
        string runId,
        DateTimeOffset timestamp,
        string policyId,
        string policyVersion,
        ulong gameSeed,
        ulong policySeed)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(scenario);
        Replay = new ReplayRecord
        {
            RunId = runId,
            Timestamp = timestamp,
            GameVersion = loaded.Content.Version.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            BaseContentHash = loaded.BaseContentHash,
            ContentHash = loaded.CompiledContentHash,
            DifficultyId = loaded.Profile.DifficultyId,
            DifficultyProfileHash = loaded.DifficultyProfileHash,
            ScenarioHash = loaded.ScenarioHash,
            PolicyId = policyId,
            PolicyVersion = policyVersion,
            GameSeed = gameSeed,
            PolicySeed = policySeed,
            Scenario = scenario.Clone()
        };
    }

    public ReplayRecord Replay { get; }

    public CommandLogEntry RecordCommand(
        string actionId,
        in GameCommand command,
        in CommandResult result,
        SimulationSnapshot before,
        SimulationSnapshot after,
        string stateHashBefore,
        string stateHashAfter)
    {
        var commandRecord = new CommandLogEntry
        {
            Sequence = nextCommandSequence++,
            Tick = before.Tick,
            Phase = before.Phase,
            ActionId = actionId,
            Type = command.Type,
            ContentId = command.ContentId,
            PrimaryId = command.PrimaryId,
            SecondaryId = command.SecondaryId,
            TertiaryId = command.TertiaryId,
            Accepted = result.Accepted,
            Error = result.Error,
            Message = result.Message
        };
        Replay.Commands.Add(commandRecord);
        Replay.Operations.Add(new ReplayOperationRecord
        {
            Sequence = nextOperationSequence++,
            Kind = ReplayOperationKind.Command,
            ActionId = actionId,
            TickBefore = before.Tick,
            PhaseBefore = before.Phase,
            StateHashBefore = stateHashBefore,
            Command = commandRecord,
            TickAfter = after.Tick,
            PhaseAfter = after.Phase,
            StateHashAfter = stateHashAfter
        });
        RecordTransition(before, after);
        return commandRecord;
    }

    public void RecordStep(
        string actionId,
        SimulationSnapshot before,
        SimulationSnapshot after,
        string stateHashBefore,
        string stateHashAfter)
    {
        Replay.Operations.Add(new ReplayOperationRecord
        {
            Sequence = nextOperationSequence++,
            Kind = ReplayOperationKind.Step,
            ActionId = actionId,
            TickBefore = before.Tick,
            PhaseBefore = before.Phase,
            StateHashBefore = stateHashBefore,
            TickAfter = after.Tick,
            PhaseAfter = after.Phase,
            StateHashAfter = stateHashAfter
        });
        RecordTransition(before, after);
    }

    public void Complete(
        SimulationResult result,
        SimulationSnapshot snapshot,
        CompiledContent content)
    {
        Replay.TotalDecisions = result.TotalDecisions;
        Replay.FinalTick = snapshot.Tick;
        Replay.Result = result.Result;
        Replay.FinalPhase = snapshot.Phase;
        Replay.RemainingBaseHealth = snapshot.BaseHealth;
        Replay.FinalGold = snapshot.Gold;
        Replay.Error = result.Error;
        Replay.FinalStateHash = result.FinalStateHash;
        Replay.FinalSnapshotHash = result.FinalSnapshotHash;
        Replay.FinalTowers = SnapshotRecords.FinalTowers(snapshot);
        Replay.FinalCards = SnapshotRecords.EquippedCards(snapshot, content);
        Replay.FinalCardStates = SnapshotRecords.FinalCards(snapshot, content);
    }

    private void RecordTransition(
        SimulationSnapshot before,
        SimulationSnapshot after)
    {
        if (before.Phase == after.Phase)
        {
            return;
        }
        Replay.PhaseTransitions.Add(new PhaseTransitionRecord
        {
            Tick = after.Tick,
            From = before.Phase,
            To = after.Phase
        });
    }
}

internal static class SnapshotRecords
{
    public static List<FinalTowerRecord> FinalTowers(
        SimulationSnapshot snapshot) => snapshot.Towers
        .OrderBy(tower => tower.Id)
        .Select(tower => new FinalTowerRecord
        {
            TowerInstanceId = tower.Id,
            DefinitionId = tower.DefinitionId,
            BuildPointIndex = tower.BuildPointIndex,
            Level = tower.Level,
            CardInstanceIds = tower.CardInstanceIds.ToList(),
            SubjectTypes = tower.CardSubjectTypes.ToList()
        })
        .ToList();

    public static List<EquippedCardRecord> EquippedCards(
        SimulationSnapshot snapshot,
        CompiledContent content)
    {
        var towers = snapshot.Towers.ToDictionary(tower => tower.Id);
        return snapshot.Cards
            .Where(card => card.Equipped)
            .OrderBy(card => card.Id)
            .Select(card => new EquippedCardRecord
            {
                CardInstanceId = card.Id,
                CardId = content.GetCard(card.DefinitionId).StableId,
                TowerInstanceId = card.TowerId,
                TowerDefinitionId = TowerDefinition(
                    towers,
                    card.TowerId),
                SlotIndex = card.Slot,
                SubjectType = SlotSubject(
                    towers,
                    card.TowerId,
                    card.Slot)
            })
            .ToList();
    }

    public static List<FinalCardRecord> FinalCards(
        SimulationSnapshot snapshot,
        CompiledContent content) => snapshot.Cards
        .OrderBy(card => card.Id)
        .Select(card => new FinalCardRecord
        {
            CardInstanceId = card.Id,
            CardId = content.GetCard(card.DefinitionId).StableId,
            Level = card.Level,
            Equipped = card.Equipped,
            TowerInstanceId = card.TowerId,
            SlotIndex = card.Slot
        })
        .ToList();

    private static string TowerDefinition(
        IReadOnlyDictionary<int, TowerSnapshot> towers,
        int towerId) => towers.TryGetValue(
        towerId,
        out TowerSnapshot tower)
            ? tower.DefinitionId
            : string.Empty;

    private static RuleforgeTD.GameLogic.Core.SubjectType SlotSubject(
        IReadOnlyDictionary<int, TowerSnapshot> towers,
        int towerId,
        int slot) => towers.TryGetValue(
            towerId,
            out TowerSnapshot tower) &&
        slot >= 0 &&
        slot < tower.CardSubjectTypes.Length
            ? tower.CardSubjectTypes[slot]
            : default;
}
