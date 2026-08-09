using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.BalanceCli.Simulation;

public enum LegalActionKind
{
    NoOp,
    ChooseStartingTower,
    PlaceTower,
    UpgradeTower,
    EquipCard,
    MoveCard,
    UnequipCard,
    ReorderCard,
    SetTowerSubjectType,
    SetSlotSubjectType,
    SelectDraft,
    OpenCardPack,
    SelectCardPack,
    ResumeCardPackCombat,
    StartWave
}

public sealed class LegalAction
{
    public required string ActionId { get; init; }
    public required LegalActionKind Kind { get; init; }
    public required string Summary { get; init; }
    public GameCommand? Command { get; init; }
    public bool HasCommand => Command.HasValue;
    public int Cost { get; init; }
    public string CardId { get; init; } = string.Empty;
    public int CardInstanceId { get; init; } = -1;
    public string TowerDefinitionId { get; init; } = string.Empty;
    public int TowerInstanceId { get; init; } = -1;
    public int BuildPointIndex { get; init; } = -1;
    public int SlotIndex { get; init; } = -1;
    public int OtherSlotIndex { get; init; } = -1;
    public SubjectType? SubjectType { get; init; }
    public bool SelfHarmRisk { get; init; }
    public int CardTier { get; init; }
    public IReadOnlyList<string> CardTags { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        EmptyMetadata;

    private static IReadOnlyDictionary<string, string> EmptyMetadata { get; } =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal));
}

public sealed class LegalActionGenerator
{
    public IReadOnlyList<LegalAction> Generate(
        GameSimulation simulation,
        SimulationSnapshot snapshot)
    {
        var actions = new List<LegalAction>();
        switch (snapshot.Phase)
        {
            case RunPhase.AwaitingStartingTower:
                AddStartingTowerActions(simulation.Content, actions);
                break;
            case RunPhase.Planning:
                AddLoadoutActions(simulation, snapshot, actions);
                AddEconomyActions(simulation, snapshot, actions, true);
                if (snapshot.Towers.Length > 0)
                {
                    AddCommand(
                        actions,
                        "start-wave",
                        LegalActionKind.StartWave,
                        "Start the next wave",
                        GameCommand.StartWave());
                }
                break;
            case RunPhase.Combat:
                foreach (CardPackSnapshot pack in snapshot.CardPacks)
                {
                    if (!pack.WorldDrop)
                    {
                        continue;
                    }
                    AddCommand(
                        actions,
                        "open-pack:" + pack.Id,
                        LegalActionKind.OpenCardPack,
                        "Open card pack " + pack.Id,
                        GameCommand.OpenCardPack(pack.Id));
                }
                AddEconomyActions(simulation, snapshot, actions, false);
                actions.Add(new LegalAction
                {
                    ActionId = "no-op",
                    Kind = LegalActionKind.NoOp,
                    Summary = "Advance one logical tick",
                    Metadata = CreateMetadata()
                });
                break;
            case RunPhase.Draft:
                AddOfferActions(
                    simulation.Content,
                    snapshot.DraftOffers,
                    LegalActionKind.SelectDraft,
                    GameCommand.SelectDraft,
                    "draft",
                    actions);
                break;
            case RunPhase.CardPackChoice:
                AddOfferActions(
                    simulation.Content,
                    snapshot.CardPackOffers,
                    LegalActionKind.SelectCardPack,
                    GameCommand.SelectCardPack,
                    "card-pack",
                    actions);
                break;
            case RunPhase.CardPackLoadout:
                AddLoadoutActions(simulation, snapshot, actions);
                AddUpgradeActions(simulation, snapshot, actions);
                bool pendingEquipped = snapshot.PendingCardInstanceId < 0 ||
                    snapshot.Cards.Any(card =>
                        card.Id == snapshot.PendingCardInstanceId &&
                        card.Equipped);
                if (pendingEquipped)
                {
                    AddCommand(
                        actions,
                        "resume-card-pack",
                        LegalActionKind.ResumeCardPackCombat,
                        "Resume after card pack loadout",
                        GameCommand.ResumeCardPackCombat());
                }
                break;
        }

        actions.Sort((left, right) =>
            StringComparer.Ordinal.Compare(left.ActionId, right.ActionId));
        return actions;
    }

    private static void AddStartingTowerActions(
        CompiledContent content,
        ICollection<LegalAction> actions)
    {
        foreach (TowerDefinitionId towerId in content.Run.StartingTowerChoices)
        {
            CompiledTowerDefinition tower = content.GetTower(towerId);
            AddCommand(
                actions,
                "choose-start:" + tower.StableId,
                LegalActionKind.ChooseStartingTower,
                "Choose starting tower " + tower.StableId,
                GameCommand.ChooseStartingTower(tower.StableId),
                towerDefinitionId: tower.StableId);
        }
    }

    private static void AddLoadoutActions(
        GameSimulation simulation,
        SimulationSnapshot snapshot,
        ICollection<LegalAction> actions)
    {
        foreach (CardInstanceSnapshot card in snapshot.Cards)
        {
            CompiledCardDefinition definition =
                simulation.Content.GetCard(card.DefinitionId);
            foreach (TowerSnapshot tower in snapshot.Towers)
            {
                int unlocked = simulation.GetTowerUnlockedSlotCount(tower.Id);
                for (int slot = 0; slot < unlocked; slot++)
                {
                    CardPlacementQuote quote = simulation.GetCardPlacementQuote(
                        card.Id,
                        tower.Id,
                        slot);
                    if (!quote.CanPlace ||
                        (card.Equipped &&
                         card.TowerId == tower.Id &&
                         card.Slot == slot))
                    {
                        continue;
                    }
                    LegalActionKind kind = card.Equipped
                        ? LegalActionKind.MoveCard
                        : LegalActionKind.EquipCard;
                    GameCommand command = card.Equipped
                        ? GameCommand.MoveCard(card.Id, tower.Id, slot)
                        : GameCommand.EquipCard(card.Id, tower.Id, slot);
                    AddCardCommand(
                        actions,
                        (card.Equipped ? "move:" : "equip:") +
                        card.Id + ":" + tower.Id + ":" + slot,
                        kind,
                        (card.Equipped ? "Move " : "Equip ") +
                        definition.StableId + " to tower " + tower.Id +
                        " slot " + slot,
                        command,
                        definition,
                        card.Id,
                        tower,
                        slot,
                        SubjectForSlot(tower, slot));
                }
            }

            if (card.Equipped)
            {
                TowerSnapshot equippedTower = Array.Find(
                    snapshot.Towers,
                    tower => tower.Id == card.TowerId);
                AddCardCommand(
                    actions,
                    "unequip:" + card.Id,
                    LegalActionKind.UnequipCard,
                    "Unequip " + definition.StableId,
                    GameCommand.UnequipCard(card.Id),
                    definition,
                    card.Id,
                    equippedTower,
                    card.Slot,
                    SubjectForSlot(equippedTower, card.Slot));
            }
        }

        foreach (TowerSnapshot tower in snapshot.Towers)
        {
            CompiledTowerDefinition towerDefinition = FindTower(
                simulation.Content,
                tower.DefinitionId);
            int unlocked = simulation.GetTowerUnlockedSlotCount(tower.Id);
            SubjectType currentTowerSubject = tower.SubjectType;
            foreach (SubjectType subject in Enum.GetValues<SubjectType>())
            {
                if (subject == currentTowerSubject ||
                    (subject == SubjectType.Projectile &&
                     towerDefinition.Trigger != TowerTrigger.Attack))
                {
                    continue;
                }
                AddCommand(
                    actions,
                    "tower-subject:" + tower.Id + ":" + subject,
                    LegalActionKind.SetTowerSubjectType,
                    "Set all slots on tower " + tower.Id + " to " + subject,
                    GameCommand.SetTowerSubjectType(tower.Id, subject),
                    towerDefinitionId: tower.DefinitionId,
                    towerInstanceId: tower.Id,
                    subjectType: subject);
            }
            for (int slot = 0; slot < unlocked; slot++)
            {
                foreach (SubjectType subject in Enum.GetValues<SubjectType>())
                {
                    if (subject == SubjectForSlot(tower, slot) ||
                        (subject == SubjectType.Projectile &&
                         towerDefinition.Trigger != TowerTrigger.Attack))
                    {
                        continue;
                    }
                    AddCommand(
                        actions,
                        "subject:" + tower.Id + ":" + slot + ":" +
                        subject,
                        LegalActionKind.SetSlotSubjectType,
                        "Set tower " + tower.Id + " slot " + slot +
                        " to " + subject,
                        GameCommand.SetTowerSlotSubjectType(
                            tower.Id,
                            slot,
                            subject),
                        towerDefinitionId: tower.DefinitionId,
                        towerInstanceId: tower.Id,
                        slotIndex: slot,
                        subjectType: subject);
                }
            }

            for (int from = 0; from < unlocked; from++)
            {
                if (tower.CardInstanceIds[from] < 0)
                {
                    continue;
                }
                for (int to = 0; to < unlocked; to++)
                {
                    if (from == to || tower.CardInstanceIds[to] == -2)
                    {
                        continue;
                    }
                    // All active cards currently have slotCost=1. For future
                    // multi-slot content, only emit reorder candidates whose
                    // authoritative card definitions fit both destinations.
                    if (!CanReorderCurrentContent(
                            simulation.Content,
                            snapshot,
                            tower,
                            from,
                            to,
                            unlocked))
                    {
                        continue;
                    }
                    AddCommand(
                        actions,
                        "reorder:" + tower.Id + ":" + from + ":" + to,
                        LegalActionKind.ReorderCard,
                        "Reorder tower " + tower.Id + " slot " + from +
                        " to " + to,
                        GameCommand.ReorderCard(tower.Id, from, to),
                        towerDefinitionId: tower.DefinitionId,
                        towerInstanceId: tower.Id,
                        slotIndex: from,
                        otherSlotIndex: to);
                }
            }
        }
    }

    private static void AddEconomyActions(
        GameSimulation simulation,
        SimulationSnapshot snapshot,
        ICollection<LegalAction> actions,
        bool includeUpgrades)
    {
        var occupied = new HashSet<int>(
            snapshot.Towers.Select(tower => tower.BuildPointIndex));
        foreach (string towerId in snapshot.UnlockedTowerIds)
        {
            TowerConstructionQuote quote =
                simulation.GetTowerConstructionQuote(towerId);
            if (!quote.CanConstruct)
            {
                continue;
            }
            foreach (BuildSpotSnapshot spot in snapshot.BuildSpots)
            {
                if (!spot.Unlocked || occupied.Contains(spot.Index))
                {
                    continue;
                }
                AddCommand(
                    actions,
                    "build:" + towerId + ":" + spot.Index,
                    LegalActionKind.PlaceTower,
                    "Build " + towerId + " at spot " + spot.Index,
                    GameCommand.PlaceTower(towerId, spot.Index),
                    quote.Cost,
                    towerId,
                    buildPointIndex: spot.Index);
            }
        }
        if (includeUpgrades)
        {
            AddUpgradeActions(simulation, snapshot, actions);
        }
    }

    private static void AddUpgradeActions(
        GameSimulation simulation,
        SimulationSnapshot snapshot,
        ICollection<LegalAction> actions)
    {
        foreach (TowerSnapshot tower in snapshot.Towers)
        {
            TowerUpgradeQuote quote = simulation.GetTowerUpgradeQuote(tower.Id);
            if (!quote.CanUpgrade)
            {
                continue;
            }
            AddCommand(
                actions,
                "upgrade:" + tower.Id,
                LegalActionKind.UpgradeTower,
                "Upgrade tower " + tower.Id + " to level " +
                quote.TargetLevel,
                GameCommand.UpgradeTower(tower.Id),
                quote.Cost,
                tower.DefinitionId,
                tower.Id);
        }
    }

    private static void AddOfferActions(
        CompiledContent content,
        IReadOnlyList<CardId> offers,
        LegalActionKind kind,
        Func<int, GameCommand> commandFactory,
        string prefix,
        ICollection<LegalAction> actions)
    {
        for (int index = 0; index < offers.Count; index++)
        {
            CompiledCardDefinition card = content.GetCard(offers[index]);
            AddCardCommand(
                actions,
                prefix + ":" + index + ":" + card.StableId,
                kind,
                "Select " + card.StableId + " from " + prefix,
                commandFactory(index),
                card,
                -1,
                default,
                -1,
                null);
        }
    }

    private static void AddCardCommand(
        ICollection<LegalAction> actions,
        string id,
        LegalActionKind kind,
        string summary,
        GameCommand command,
        CompiledCardDefinition card,
        int cardInstanceId,
        TowerSnapshot tower,
        int slot,
        SubjectType? subject)
    {
        actions.Add(new LegalAction
        {
            ActionId = id,
            Kind = kind,
            Summary = summary,
            Command = command,
            CardId = card.StableId,
            CardInstanceId = cardInstanceId,
            TowerDefinitionId = tower.DefinitionId ?? string.Empty,
            TowerInstanceId = tower.Id,
            SlotIndex = slot,
            SubjectType = subject,
            SelfHarmRisk = subject.HasValue &&
                IsSelfHarmRisk(card, subject.Value),
            CardTier = (int)card.Tier,
            CardTags = card.Tags,
            Metadata = CreateMetadata(
                card.StableId,
                cardInstanceId,
                tower.DefinitionId ?? string.Empty,
                tower.Id,
                -1,
                slot,
                -1,
                subject,
                subject.HasValue && IsSelfHarmRisk(card, subject.Value),
                (int)card.Tier,
                card.Tags)
        });
    }

    private static bool IsSelfHarmRisk(
        CompiledCardDefinition card,
        SubjectType subject)
    {
        if (subject != SubjectType.Enemy)
        {
            return false;
        }
        foreach (CompiledEffectNode node in card.EnemyEffects)
        {
            if (node.Operation is EffectOperation.Split or
                EffectOperation.EnlargeEnemy or
                EffectOperation.AccelerateEnemy or
                EffectOperation.DuplicateEnemy or
                EffectOperation.ApplyEnemyForbiddenDeal or
                EffectOperation.ApplyEnemyPhoenixCore or
                EffectOperation.ApplyEnemyMirrorWorld or
                EffectOperation.ApplyEnemyOuroboros)
            {
                return true;
            }
        }
        return false;
    }

    private static bool CanReorderCurrentContent(
        CompiledContent content,
        SimulationSnapshot snapshot,
        TowerSnapshot tower,
        int from,
        int to,
        int unlocked)
    {
        int fromId = tower.CardInstanceIds[from];
        int toId = tower.CardInstanceIds[to];
        CardInstanceSnapshot fromCard = Array.Find(
            snapshot.Cards,
            card => card.Id == fromId);
        int fromCost = content.GetCard(fromCard.DefinitionId).SlotCost;
        if (to + fromCost > unlocked)
        {
            return false;
        }
        if (toId < 0)
        {
            return fromCost == 1;
        }
        CardInstanceSnapshot toCard = Array.Find(
            snapshot.Cards,
            card => card.Id == toId);
        int toCost = content.GetCard(toCard.DefinitionId).SlotCost;
        return fromCost == 1 && toCost == 1;
    }

    private static SubjectType SubjectForSlot(TowerSnapshot tower, int slot) =>
        slot >= 0 && slot < tower.CardSubjectTypes.Length
            ? tower.CardSubjectTypes[slot]
            : tower.SubjectType;

    private static CompiledTowerDefinition FindTower(
        CompiledContent content,
        string stableId)
    {
        if (!content.TryGetTowerId(stableId, out TowerDefinitionId id))
        {
            throw new InvalidOperationException("Unknown tower " + stableId + ".");
        }
        return content.GetTower(id);
    }

    private static void AddCommand(
        ICollection<LegalAction> actions,
        string id,
        LegalActionKind kind,
        string summary,
        GameCommand command,
        int cost = 0,
        string towerDefinitionId = "",
        int towerInstanceId = -1,
        int buildPointIndex = -1,
        int slotIndex = -1,
        int otherSlotIndex = -1,
        SubjectType? subjectType = null)
    {
        actions.Add(new LegalAction
        {
            ActionId = id,
            Kind = kind,
            Summary = summary,
            Command = command,
            Cost = cost,
            TowerDefinitionId = towerDefinitionId,
            TowerInstanceId = towerInstanceId,
            BuildPointIndex = buildPointIndex,
            SlotIndex = slotIndex,
            OtherSlotIndex = otherSlotIndex,
            SubjectType = subjectType,
            Metadata = CreateMetadata(
                towerDefinitionId: towerDefinitionId,
                towerInstanceId: towerInstanceId,
                buildPointIndex: buildPointIndex,
                slotIndex: slotIndex,
                otherSlotIndex: otherSlotIndex,
                subjectType: subjectType)
        });
    }

    private static IReadOnlyDictionary<string, string> CreateMetadata(
        string cardId = "",
        int cardInstanceId = -1,
        string towerDefinitionId = "",
        int towerInstanceId = -1,
        int buildPointIndex = -1,
        int slotIndex = -1,
        int otherSlotIndex = -1,
        SubjectType? subjectType = null,
        bool selfHarmRisk = false,
        int cardTier = 0,
        IReadOnlyList<string>? cardTags = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cardId"] = cardId,
            ["cardInstanceId"] = cardInstanceId.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["towerDefinitionId"] = towerDefinitionId,
            ["towerInstanceId"] = towerInstanceId.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["buildPointIndex"] = buildPointIndex.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["slotIndex"] = slotIndex.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["otherSlotIndex"] = otherSlotIndex.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["subjectType"] = subjectType?.ToString() ?? string.Empty,
            ["selfHarmRisk"] = selfHarmRisk ? "true" : "false",
            ["cardTier"] = cardTier.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["cardTags"] = string.Join("|", cardTags ?? Array.Empty<string>())
        };
        return new ReadOnlyDictionary<string, string>(metadata);
    }
}
