using System;
using System.Collections.Generic;
using System.Linq;
using RuleforgeTD.BalanceCli.Policies;
using RuleforgeTD.BalanceCli.Simulation;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.BalanceCli.Evaluation;

/// <summary>
/// Realizes a card experiment exclusively by selecting LegalActions. The
/// content fixture grants ownership, but this policy must still choose the
/// tower, place it, pay for each upgrade, set each slot subject, and equip each
/// distinct card instance through public GameCommands.
/// </summary>
public sealed class CardExperimentFixturePolicy : IPlayerPolicy
{
    public string PolicyId => "card-experiment-fixture";
    public string PolicyVersion => "2.0.0";

    public PolicyDecision Decide(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        return snapshot.Phase switch
        {
            RunPhase.AwaitingStartingTower => Decision(
                Require(context.LegalActions, action =>
                    action.Kind == LegalActionKind.ChooseStartingTower &&
                    string.Equals(
                        action.TowerDefinitionId,
                        context.Scenario.ForcedStartingTowerId,
                        StringComparison.Ordinal),
                    "forced starting tower"),
                "FIXTURE_STARTING_TOWER"),
            RunPhase.Planning => DecidePlanning(snapshot, context),
            RunPhase.Combat => Decision(
                Require(context.LegalActions, action =>
                    action.Kind == LegalActionKind.NoOp,
                    "combat no-op"),
                "FIXTURE_NO_OP"),
            RunPhase.Draft => Decision(
                FirstKind(context.LegalActions, LegalActionKind.SelectDraft),
                "FIXTURE_FIRST_DRAFT"),
            RunPhase.CardPackChoice => Decision(
                FirstKind(context.LegalActions, LegalActionKind.SelectCardPack),
                "FIXTURE_FIRST_CARD_PACK_CHOICE"),
            RunPhase.CardPackLoadout => DecideUnexpectedCardPackLoadout(
                snapshot,
                context),
            _ => throw new InvalidOperationException(
                "Fixture policy cannot act in phase " + snapshot.Phase + ".")
        };
    }

    private static PolicyDecision DecidePlanning(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        string targetTowerId = context.Scenario.ForcedPlacedTowerId ??
            throw new InvalidOperationException(
                "Card fixture requires ForcedPlacedTowerId.");
        if (snapshot.Towers.Length == 0)
        {
            return Decision(
                Require(context.LegalActions, action =>
                    action.Kind == LegalActionKind.PlaceTower &&
                    string.Equals(
                        action.TowerDefinitionId,
                        targetTowerId,
                        StringComparison.Ordinal),
                    "fixture tower placement"),
                "FIXTURE_PLACE_TOWER");
        }

        TowerSnapshot tower = snapshot.Towers
            .Where(value => string.Equals(
                value.DefinitionId,
                targetTowerId,
                StringComparison.Ordinal))
            .OrderBy(value => value.Id)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(tower.DefinitionId))
        {
            throw new InvalidOperationException(
                "The fixture target tower was not placed.");
        }
        int targetLevel = context.Scenario.ForcedTowerLevel ?? 1;
        if (tower.Level > targetLevel)
        {
            throw new InvalidOperationException(
                "Fixture tower exceeded requested level " + targetLevel + ".");
        }
        if (tower.Level < targetLevel)
        {
            return Decision(
                Require(context.LegalActions, action =>
                    action.Kind == LegalActionKind.UpgradeTower &&
                    action.TowerInstanceId == tower.Id,
                    "fixture tower upgrade"),
                "FIXTURE_UPGRADE_TOWER");
        }

        IReadOnlyList<FixtureAssignment> assignments = BuildAssignments(
            snapshot,
            context);
        var assignedIds = new HashSet<int>(assignments.Select(value =>
            value.CardInstance.Id));
        foreach (CardInstanceSnapshot card in snapshot.Cards
                     .Where(card => card.Equipped)
                     .OrderBy(card => card.Id))
        {
            FixtureAssignment? assignment = assignments.FirstOrDefault(value =>
                value.CardInstance.Id == card.Id);
            bool correct = assignment != null &&
                card.TowerId == tower.Id &&
                card.Slot == assignment.Slot.SlotIndex;
            if (!correct || !assignedIds.Contains(card.Id))
            {
                return Decision(
                    Require(context.LegalActions, action =>
                        action.Kind == LegalActionKind.UnequipCard &&
                        action.CardInstanceId == card.Id,
                        "remove non-fixture card"),
                    "FIXTURE_UNEQUIP_CONTAMINANT");
            }
        }

        foreach (FixtureAssignment assignment in assignments.OrderBy(value =>
                     value.Slot.Order))
        {
            int slot = assignment.Slot.SlotIndex;
            if (slot < 0 || slot >= tower.CardSubjectTypes.Length)
            {
                throw new InvalidOperationException(
                    "Fixture slot is outside the tower snapshot: " + slot + ".");
            }
            if (tower.CardSubjectTypes[slot] != assignment.Slot.SubjectType)
            {
                return Decision(
                    Require(context.LegalActions, action =>
                        action.Kind == LegalActionKind.SetSlotSubjectType &&
                        action.TowerInstanceId == tower.Id &&
                        action.SlotIndex == slot &&
                        action.SubjectType == assignment.Slot.SubjectType,
                        "fixture slot subject"),
                    "FIXTURE_SET_SLOT_SUBJECT");
            }
        }

        foreach (FixtureAssignment assignment in assignments.OrderBy(value =>
                     value.Slot.Order))
        {
            CardInstanceSnapshot card = assignment.CardInstance;
            if (card.Equipped)
            {
                continue;
            }
            return Decision(
                Require(context.LegalActions, action =>
                    action.Kind == LegalActionKind.EquipCard &&
                    action.CardInstanceId == card.Id &&
                    action.TowerInstanceId == tower.Id &&
                    action.SlotIndex == assignment.Slot.SlotIndex,
                    "fixture card equip"),
                "FIXTURE_EQUIP_CARD");
        }

        return Decision(
            FirstKind(context.LegalActions, LegalActionKind.StartWave),
            "FIXTURE_START_WAVE");
    }

    private static PolicyDecision DecideUnexpectedCardPackLoadout(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        LegalAction? resume = context.LegalActions.FirstOrDefault(action =>
            action.Kind == LegalActionKind.ResumeCardPackCombat);
        if (resume != null)
        {
            return Decision(resume, "FIXTURE_RESUME_CARD_PACK");
        }
        throw new InvalidOperationException(
            "Card experiment entered a card-pack loadout that requires " +
            "equipping a non-fixture card " + snapshot.PendingCardInstanceId +
            ". The fixture policy never opens world card packs.");
    }

    private static IReadOnlyList<FixtureAssignment> BuildAssignments(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        List<SimulationCardFixtureSlot> slots = context.Scenario
            .FixtureCardProgram
            .OrderBy(value => value.Order)
            .ToList();
        var assignments = new List<FixtureAssignment>(slots.Count);
        foreach (IGrouping<string, SimulationCardFixtureSlot> group in slots
                     .GroupBy(value => value.CardId, StringComparer.Ordinal))
        {
            List<CardInstanceSnapshot> owned = snapshot.Cards
                .Where(card => string.Equals(
                    StableCardId(card, context),
                    group.Key,
                    StringComparison.Ordinal))
                .OrderBy(card => card.Id)
                .ToList();
            if (string.Equals(
                    group.Key,
                    context.Scenario.FixtureControlCardId,
                    StringComparison.Ordinal))
            {
                if (owned.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Fixture control card instance is missing.");
                }
                // The loader appends the control card first, so the lowest
                // instance ID is reserved and can never enter an assignment.
                owned.RemoveAt(0);
            }
            List<SimulationCardFixtureSlot> groupedSlots = group
                .OrderBy(value => value.Order)
                .ToList();
            if (owned.Count < groupedSlots.Count)
            {
                throw new InvalidOperationException(
                    "Not enough owned instances of fixture card '" + group.Key +
                    "'. Required " + groupedSlots.Count + ", found " +
                    owned.Count + ".");
            }
            for (int index = 0; index < groupedSlots.Count; index++)
            {
                assignments.Add(new FixtureAssignment(
                    groupedSlots[index],
                    owned[index]));
            }
        }
        return assignments;
    }

    private static string StableCardId(
        CardInstanceSnapshot card,
        PolicyContext context) =>
        context.PublicKnowledge.CardStableId(card.DefinitionId);

    private static LegalAction FirstKind(
        IReadOnlyList<LegalAction> actions,
        LegalActionKind kind) => actions
        .Where(action => action.Kind == kind)
        .OrderBy(action => action.ActionId, StringComparer.Ordinal)
        .FirstOrDefault() ?? throw new InvalidOperationException(
            "Fixture policy requires legal action " + kind + ".");

    private static LegalAction Require(
        IReadOnlyList<LegalAction> actions,
        Func<LegalAction, bool> predicate,
        string description) => actions
        .Where(predicate)
        .OrderBy(action => action.ActionId, StringComparer.Ordinal)
        .FirstOrDefault() ?? throw new InvalidOperationException(
            "Fixture policy cannot find legal action for " + description + ".");

    private static PolicyDecision Decision(
        LegalAction action,
        string reasonCode) => new(action.ActionId, reasonCode);

    private sealed record FixtureAssignment(
        SimulationCardFixtureSlot Slot,
        CardInstanceSnapshot CardInstance);
}
