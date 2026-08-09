using System;
using System.Collections.Generic;
using System.Linq;
using RuleforgeTD.BalanceCli.Simulation;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.BalanceCli.Policies;

public static class PolicyFactory
{
    public static IReadOnlyList<string> NoviceEnsembleIds { get; } = new[]
    {
        "novice-random-spender",
        "novice-build-first",
        "novice-upgrade-first"
    };

    public static IReadOnlyList<string> PolicyIds { get; } = new[]
    {
        "novice-random-spender",
        "novice-build-first",
        "novice-upgrade-first",
        "no-spend",
        "adversarial-random",
        "good-standalone",
        "synergy-tactical",
        "synergy-no-combat-build",
        "synergy-disabled",
        "oracle-search",
        "card-fixture"
    };

    public static IReadOnlyList<IPlayerPolicy> Expand(string policyId)
    {
        IEnumerable<string> ids = string.Equals(
            policyId,
            "novice-ensemble",
            StringComparison.Ordinal)
            ? NoviceEnsembleIds
            : new[] { policyId };
        return ids.Select(Create).ToArray();
    }

    public static IPlayerPolicy Create(string policyId)
    {
        return policyId switch
        {
            "novice-random-spender" => new NoviceRandomSpenderPolicy(),
            "novice-build-first" => new NoviceBuildFirstPolicy(),
            "novice-upgrade-first" => new NoviceUpgradeFirstPolicy(),
            "good-standalone" => new GoodStandalonePolicy(),
            "synergy-tactical" => new SynergyTacticalPolicy(),
            "synergy-no-combat-build" => new SynergyNoCombatBuildPolicy(),
            "synergy-disabled" => new SynergyDisabledPolicy(),
            "no-spend" => new NoSpendPolicy(),
            "adversarial-random" => new AdversarialRandomPolicy(),
            "oracle-search" => new OracleSearchPolicy(),
            "card-fixture" => new CardFixturePolicy(),
            _ => throw new ArgumentException("Unknown policy '" + policyId + "'.")
        };
    }
}

/// <summary>Discoverable registry name for command-line and batch callers.</summary>
public static class PolicyRegistry
{
    public static IReadOnlyList<string> PolicyIds => PolicyFactory.PolicyIds;
    public static IReadOnlyList<string> NoviceEnsembleIds =>
        PolicyFactory.NoviceEnsembleIds;
    public static IPlayerPolicy Create(string policyId) =>
        PolicyFactory.Create(policyId);
    public static IReadOnlyList<IPlayerPolicy> Expand(string policyId) =>
        PolicyFactory.Expand(policyId);
}

public abstract class DeterministicPolicyBase : IPlayerPolicy
{
    public abstract string PolicyId { get; }
    public virtual string PolicyVersion => "1.3.0";

    protected virtual bool UseStrengthIndex => false;
    protected virtual bool UseSynergyIndex => false;
    protected virtual bool AllowCombatBuild => false;
    protected virtual bool AvoidSelfHarm => true;
    protected virtual bool PreferBuild => false;
    protected virtual bool PreferUpgrade => false;
    protected virtual bool SpendGold => true;
    protected virtual bool EquipOnlyOneCard => false;
    protected virtual bool UseRandomCardChoice => false;
    protected virtual bool UseTacticalEconomy => false;

    public virtual PolicyDecision Decide(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        IReadOnlyList<LegalAction> actions = context.LegalActions;
        if (actions.Count == 0)
        {
            throw new InvalidOperationException(
                "Policy received no legal actions in phase " + snapshot.Phase + ".");
        }

        LegalAction? forced = ChooseForcedScenarioAction(snapshot, context);
        if (forced != null)
        {
            return Decision(forced, "SCENARIO_FIXTURE");
        }

        return snapshot.Phase switch
        {
            RunPhase.AwaitingStartingTower =>
                Decision(
                    ChooseStartingTower(snapshot, actions, context),
                    "CHOOSE_START"),
            RunPhase.Draft =>
                Decision(
                    ChooseCardOffer(snapshot, actions, context),
                    CardReason()),
            RunPhase.CardPackChoice =>
                Decision(
                    ChooseCardOffer(snapshot, actions, context),
                    CardReason()),
            RunPhase.CardPackLoadout =>
                DecideLoadout(snapshot, context, true),
            RunPhase.Planning => DecidePlanning(snapshot, context),
            RunPhase.Combat => DecideCombat(snapshot, context),
            _ => throw new InvalidOperationException(
                "Policy cannot act in terminal phase " + snapshot.Phase + ".")
        };
    }

    protected virtual LegalAction ChooseStartingTower(
        SimulationSnapshot snapshot,
        IReadOnlyList<LegalAction> actions,
        PolicyContext context)
    {
        List<LegalAction> attackTowers = actions
            .Where(action =>
                action.Kind == LegalActionKind.ChooseStartingTower &&
                context.PublicKnowledge.Tower(action.TowerDefinitionId).Trigger ==
                    TowerTrigger.Attack)
            .ToList();
        List<LegalAction> candidates = attackTowers.Count > 0
            ? attackTowers
            : actions
                .Where(action =>
                    action.Kind == LegalActionKind.ChooseStartingTower)
                .ToList();
        if (!UseStrengthIndex && !UseSynergyIndex)
        {
            return context.Random.Choose(candidates);
        }
        return candidates
            .OrderByDescending(action =>
                ScoreStartingTower(action, snapshot, context))
            .ThenBy(action => action.ActionId, StringComparer.Ordinal)
            .First();
    }

    protected virtual PolicyDecision DecidePlanning(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        IReadOnlyList<LegalAction> actions = context.LegalActions;
        // Choosing a starting definition does not place it. Every policy,
        // including the no-spend control, must realize the authoritative free
        // initial placement before StartWave can become legal.
        if (snapshot.Towers.Length == 0)
        {
            LegalAction? freeInitialTower = actions
                .Where(action =>
                    action.Kind == LegalActionKind.PlaceTower &&
                    action.Cost == 0)
                .OrderBy(action => action.ActionId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (freeInitialTower != null)
            {
                return Decision(freeInitialTower, "PLACE_INITIAL_TOWER");
            }
        }

        LegalAction? subject = ChooseSubjectImprovement(snapshot, context);
        if (subject != null)
        {
            return Decision(subject, "CHANGE_SUBJECT_CONTEXT");
        }

        LegalAction? equip = ChooseEquip(snapshot, context);
        if (equip != null)
        {
            return Decision(equip, "EQUIP_AVAILABLE_CARD");
        }

        LegalAction? replacement = ChooseWeakCardReplacement(snapshot, context);
        if (replacement != null)
        {
            return Decision(replacement, "REPLACE_WEAK_CARD");
        }

        LegalAction? reorder = ChooseReorder(snapshot, context);
        if (reorder != null)
        {
            return Decision(reorder, "REORDER_PROGRAM");
        }

        if (SpendGold && ShouldContinueSpending(snapshot, context))
        {
            LegalAction? economy = ChooseEconomyAction(
                snapshot,
                actions,
                context);
            if (economy != null)
            {
                return Decision(
                    economy,
                    economy.Kind == LegalActionKind.PlaceTower
                        ? "BUILD_EFFICIENT_TOWER"
                        : "UPGRADE_EFFICIENT_TOWER");
            }
        }

        return Decision(
            RequireKind(actions, LegalActionKind.StartWave),
            "START_WAVE");
    }

    protected virtual PolicyDecision DecideLoadout(
        SimulationSnapshot snapshot,
        PolicyContext context,
        bool cardPack)
    {
        bool pendingUnequipped = snapshot.PendingCardInstanceId >= 0 &&
            snapshot.Cards.Any(card =>
                card.Id == snapshot.PendingCardInstanceId &&
                !card.Equipped);
        LegalAction? pendingEquip = context.LegalActions
            .Where(action =>
                action.Kind == LegalActionKind.EquipCard &&
                action.CardInstanceId == snapshot.PendingCardInstanceId &&
                (!AvoidSelfHarm || !action.SelfHarmRisk) &&
                (!EquipOnlyOneCard ||
                 snapshot.Cards.Count(card => card.Equipped) == 0))
            .OrderByDescending(action => ScoreEquip(action, snapshot, context))
            .ThenBy(action => action.ActionId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (pendingEquip != null)
        {
            return Decision(pendingEquip, "EQUIP_AVAILABLE_CARD");
        }

        // A full loadout can make the required card-pack card impossible to
        // equip. Only in that state do we remove exactly one existing card.
        if (pendingUnequipped)
        {
            List<LegalAction> unequip = context.LegalActions
                .Where(action => action.Kind == LegalActionKind.UnequipCard)
                .ToList();
            List<LegalAction> nonFixture = unequip.Where(action =>
                !context.Scenario.AdditionalStartingCards.Contains(
                    action.CardId,
                    StringComparer.Ordinal)).ToList();
            if (nonFixture.Count > 0)
            {
                unequip = nonFixture;
            }
            LegalAction? weakest = unequip
                .OrderBy(action => ScoreCard(action.CardId, context))
                .ThenBy(action => action.ActionId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (weakest != null)
            {
                return Decision(weakest, "FREE_SLOT_FOR_CARD_PACK");
            }
        }

        LegalAction? subject = ChooseSubjectImprovement(snapshot, context);
        if (subject != null)
        {
            return Decision(subject, "CHANGE_SUBJECT_CONTEXT");
        }

        LegalAction? equip = ChooseEquip(snapshot, context);
        if (equip != null)
        {
            return Decision(equip, "EQUIP_AVAILABLE_CARD");
        }


        LegalAction? replacement = ChooseWeakCardReplacement(snapshot, context);
        if (replacement != null)
        {
            return Decision(replacement, "REPLACE_WEAK_CARD");
        }

        return Decision(
            RequireKind(
                context.LegalActions,
                LegalActionKind.ResumeCardPackCombat),
            "RESUME_CARD_PACK");
    }

    protected virtual PolicyDecision DecideCombat(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        LegalAction? pack = context.LegalActions.FirstOrDefault(action =>
            action.Kind == LegalActionKind.OpenCardPack);
        if (pack != null)
        {
            return Decision(pack, "OPEN_CARD_PACK");
        }

        if (AllowCombatBuild && IsMidWaveBuildMeaningful(snapshot, context))
        {
            LegalAction? build = context.LegalActions
                .Where(action => action.Kind == LegalActionKind.PlaceTower)
                .OrderByDescending(action =>
                    ScoreCombatBuild(action, snapshot, context))
                .ThenBy(action => action.Cost)
                .ThenBy(action => action.ActionId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (build != null)
            {
                return Decision(build, "MIDWAVE_BUILD");
            }
        }

        return Decision(
            RequireKind(context.LegalActions, LegalActionKind.NoOp),
            "NO_OP");
    }

    private static double ScoreCombatBuild(
        LegalAction action,
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        TowerKnowledge tower = context.PublicKnowledge.Tower(
            action.TowerDefinitionId);
        double score = tower.Trigger == TowerTrigger.Attack ? 6d : 2d;
        score += 100d / Math.Max(1, action.Cost);
        if (snapshot.Enemies.Length == 0)
        {
            return score;
        }

        EnemySnapshot threat = snapshot.Enemies
            .OrderByDescending(enemy => enemy.PathProgressMilli)
            .ThenByDescending(enemy =>
                context.PublicKnowledge.Enemy(enemy.DefinitionId).LeakDamage)
            .ThenBy(enemy => enemy.Id)
            .First();
        BuildSpotSnapshot spot = Array.Find(
            snapshot.BuildSpots,
            candidate => candidate.Index == action.BuildPointIndex);
        if (spot.Index == action.BuildPointIndex)
        {
            double distance = Math.Sqrt(
                spot.Position.DistanceSquaredRaw(threat.Position));
            score += 4d / (1d + distance / 1000d);
        }
        if (context.PublicKnowledge.Enemy(threat.DefinitionId).Rank ==
            EnemyRank.Boss)
        {
            score += tower.Trigger == TowerTrigger.Attack ? 2d : 1d;
        }
        return score;
    }

    protected virtual LegalAction ChooseCardOffer(
        SimulationSnapshot snapshot,
        IReadOnlyList<LegalAction> actions,
        PolicyContext context)
    {
        List<LegalAction> candidates = actions
            .Where(action => !AvoidSelfHarm || !action.SelfHarmRisk)
            .ToList();
        if (candidates.Count == 0)
        {
            candidates = actions.ToList();
        }
        // Standalone and novice policies do not intentionally stack duplicate
        // card definitions. Besides being a poor proxy for ordinary drafting,
        // repeated high-generation cards can turn an otherwise reasonable
        // policy into an accidental chain-budget stress test. A measured
        // synergy policy may still choose a duplicate when its ordered index
        // explicitly supports that program.
        if (!UseSynergyIndex)
        {
            var owned = new HashSet<string>(
                snapshot.Cards.Select(card =>
                    context.PublicKnowledge.CardStableId(card.DefinitionId)),
                StringComparer.Ordinal);
            List<LegalAction> novel = candidates.Where(action =>
                string.IsNullOrEmpty(action.CardId) ||
                !owned.Contains(action.CardId)).ToList();
            if (novel.Count > 0)
            {
                candidates = novel;
            }
        }
        if (UseRandomCardChoice)
        {
            return context.Random.Choose(candidates);
        }
        return candidates
            .OrderByDescending(action =>
                ScoreCardOffer(action, snapshot, context))
            .ThenBy(action => action.ActionId, StringComparer.Ordinal)
            .First();
    }

    protected virtual LegalAction? ChooseEquip(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        if (EquipOnlyOneCard && snapshot.Cards.Count(card => card.Equipped) > 0)
        {
            return null;
        }
        List<LegalAction> candidates = context.LegalActions
            .Where(action =>
                action.Kind == LegalActionKind.EquipCard &&
                (!AvoidSelfHarm || !action.SelfHarmRisk))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        string? fixtureCard = context.Scenario.AdditionalStartingCards
            .FirstOrDefault(cardId => candidates.Any(action =>
                string.Equals(action.CardId, cardId, StringComparison.Ordinal)));
        if (fixtureCard != null)
        {
            candidates = candidates.Where(action =>
                string.Equals(
                    action.CardId,
                    fixtureCard,
                    StringComparison.Ordinal)).ToList();
        }

        if (UseRandomCardChoice)
        {
            return context.Random.Choose(candidates);
        }
        return candidates
            .OrderByDescending(action => ScoreEquip(action, snapshot, context))
            .ThenBy(action => action.ActionId, StringComparer.Ordinal)
            .First();
    }

    protected virtual LegalAction? ChooseSubjectImprovement(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        if (context.Scenario.ForcedSubjectType.HasValue)
        {
            return context.LegalActions.FirstOrDefault(action =>
                action.Kind == LegalActionKind.SetSlotSubjectType &&
                action.SubjectType == context.Scenario.ForcedSubjectType);
        }
        if (!UseStrengthIndex && !UseSynergyIndex)
        {
            return null;
        }

        LegalAction? best = null;
        double bestDelta = 0.01;
        foreach (LegalAction action in context.LegalActions
                     .Where(action => action.Kind == LegalActionKind.SetSlotSubjectType)
                     .OrderBy(action => action.ActionId, StringComparer.Ordinal))
        {
            TowerSnapshot tower = Array.Find(
                snapshot.Towers,
                item => item.Id == action.TowerInstanceId);
            if (action.SlotIndex < 0 ||
                action.SlotIndex >= tower.CardInstanceIds.Length ||
                tower.CardInstanceIds[action.SlotIndex] < 0)
            {
                continue;
            }
            SubjectType current = tower.CardSubjectTypes[action.SlotIndex];
            SubjectType desiredSubject = action.SubjectType ?? current;
            double existing;
            double desired;
            if (UseSynergyIndex)
            {
                existing = ScoreTowerProgram(
                    tower,
                    tower.CardInstanceIds,
                    tower.CardSubjectTypes,
                    snapshot,
                    context);
                SubjectType[] subjects =
                    (SubjectType[])tower.CardSubjectTypes.Clone();
                subjects[action.SlotIndex] = desiredSubject;
                desired = ScoreTowerProgram(
                    tower,
                    tower.CardInstanceIds,
                    subjects,
                    snapshot,
                    context);
            }
            else
            {
                int cardInstance = tower.CardInstanceIds[action.SlotIndex];
                string? cardId = ResolveCardId(
                    cardInstance,
                    snapshot,
                    context);
                if (cardId == null)
                {
                    continue;
                }
                existing = ScoreCardContext(
                    cardId,
                    tower.DefinitionId,
                    current,
                    action.SlotIndex,
                    tower.Level,
                    context);
                desired = ScoreCardContext(
                    cardId,
                    tower.DefinitionId,
                    desiredSubject,
                    action.SlotIndex,
                    tower.Level,
                    context);
            }
            double delta = desired - existing;
            if (delta > bestDelta)
            {
                best = action;
                bestDelta = delta;
            }
        }
        return best;
    }

    protected virtual LegalAction? ChooseWeakCardReplacement(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        if (!UseStrengthIndex)
        {
            return null;
        }
        CardInstanceSnapshot[] available = snapshot.Cards
            .Where(card => !card.Equipped)
            .OrderBy(card => card.Id)
            .ToArray();
        if (available.Length == 0)
        {
            return null;
        }

        LegalAction? best = null;
        string? bestKey = null;
        double bestDelta = 0.05;
        foreach (LegalAction unequip in context.LegalActions
                     .Where(action => action.Kind == LegalActionKind.UnequipCard)
                     .OrderBy(action => action.ActionId, StringComparer.Ordinal))
        {
            if (unequip.CardInstanceId == snapshot.PendingCardInstanceId ||
                context.Scenario.AdditionalStartingCards.Contains(
                    unequip.CardId,
                    StringComparer.Ordinal))
            {
                continue;
            }
            TowerSnapshot tower = Array.Find(
                snapshot.Towers,
                item => item.Id == unequip.TowerInstanceId);
            if (string.IsNullOrEmpty(tower.DefinitionId) ||
                unequip.SlotIndex < 0 ||
                unequip.SlotIndex >= tower.CardInstanceIds.Length)
            {
                continue;
            }
            CardKnowledge current = context.PublicKnowledge.Card(unequip.CardId);
            SubjectType subject = tower.CardSubjectTypes[unequip.SlotIndex];
            double currentScore = ScoreTowerProgram(
                tower,
                tower.CardInstanceIds,
                tower.CardSubjectTypes,
                snapshot,
                context);
            foreach (CardInstanceSnapshot candidate in available)
            {
                string candidateId = context.PublicKnowledge.CardStableId(
                    candidate.DefinitionId);
                CardKnowledge candidateKnowledge =
                    context.PublicKnowledge.Card(candidateId);
                if (candidateKnowledge.SlotCost > current.SlotCost ||
                    candidateKnowledge.ComputeCost > current.ComputeCost ||
                    (AvoidSelfHarm && subject == SubjectType.Enemy &&
                     IsSelfHarmCard(candidateKnowledge, subject)))
                {
                    continue;
                }
                int[] replacement = (int[])tower.CardInstanceIds.Clone();
                replacement[unequip.SlotIndex] = candidate.Id;
                double candidateScore = ScoreTowerProgram(
                    tower,
                    replacement,
                    tower.CardSubjectTypes,
                    snapshot,
                    context);
                double delta = candidateScore - currentScore;
                string key = "replace:" + tower.Id + ":" +
                    unequip.CardInstanceId + "->" + candidate.Id;
                if (delta > bestDelta &&
                    !context.Memory.AppliedOptimizations.Contains(key))
                {
                    best = unequip;
                    bestKey = key;
                    bestDelta = delta;
                }
            }
        }
        if (best != null && bestKey != null)
        {
            context.Memory.AppliedOptimizations.Add(bestKey);
        }
        return best;
    }

    protected virtual LegalAction? ChooseReorder(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        if (!UseSynergyIndex)
        {
            return null;
        }
        LegalAction? best = null;
        string? bestKey = null;
        double bestDelta = 0.01;
        foreach (LegalAction action in context.LegalActions
                     .Where(action => action.Kind == LegalActionKind.ReorderCard)
                     .OrderBy(action => action.ActionId, StringComparer.Ordinal))
        {
            TowerSnapshot tower = Array.Find(
                snapshot.Towers,
                item => item.Id == action.TowerInstanceId);
            if (action.SlotIndex < 0 || action.OtherSlotIndex < 0 ||
                action.SlotIndex >= tower.CardInstanceIds.Length ||
                action.OtherSlotIndex >= tower.CardInstanceIds.Length)
            {
                continue;
            }
            int fromInstance = tower.CardInstanceIds[action.SlotIndex];
            int toInstance = tower.CardInstanceIds[action.OtherSlotIndex];
            if (fromInstance < 0 || toInstance == -2)
            {
                continue;
            }

            int[] reordered = (int[])tower.CardInstanceIds.Clone();
            reordered[action.OtherSlotIndex] = fromInstance;
            reordered[action.SlotIndex] = toInstance >= 0 ? toInstance : -1;
            string beforeSignature = ProgramSignature(
                tower.CardInstanceIds,
                tower.CardSubjectTypes);
            string afterSignature = ProgramSignature(
                reordered,
                tower.CardSubjectTypes);
            string optimizationKey = "program:" + tower.Id + ":" +
                beforeSignature + "->" + afterSignature;
            if (context.Memory.AppliedOptimizations.Contains(optimizationKey))
            {
                continue;
            }

            double current = ScoreTowerProgram(
                tower,
                tower.CardInstanceIds,
                tower.CardSubjectTypes,
                snapshot,
                context);
            double candidate = ScoreTowerProgram(
                tower,
                reordered,
                tower.CardSubjectTypes,
                snapshot,
                context);
            double delta = candidate - current;
            if (delta > bestDelta)
            {
                best = action;
                bestKey = optimizationKey;
                bestDelta = delta;
            }
        }
        if (best != null && bestKey != null)
        {
            context.Memory.AppliedOptimizations.Add(bestKey);
        }
        return best;
    }

    protected virtual LegalAction? ChooseEconomyAction(
        SimulationSnapshot snapshot,
        IReadOnlyList<LegalAction> actions,
        PolicyContext context)
    {
        List<LegalAction> builds = actions
            .Where(action => action.Kind == LegalActionKind.PlaceTower)
            .ToList();
        List<LegalAction> upgrades = actions
            .Where(action => action.Kind == LegalActionKind.UpgradeTower)
            .ToList();
        List<LegalAction> preferred;
        if (PreferBuild && builds.Count > 0)
        {
            preferred = builds;
        }
        else if (PreferUpgrade && upgrades.Count > 0)
        {
            preferred = upgrades;
        }
        else
        {
            preferred = builds.Concat(upgrades).ToList();
        }
        if (UseTacticalEconomy && preferred.Count > 0)
        {
            int reserve = TacticalGoldReserve(actions, context);
            preferred = preferred.Where(action =>
                snapshot.Gold - action.Cost >= reserve).ToList();
        }
        if (preferred.Count == 0)
        {
            return null;
        }
        return preferred
            .OrderByDescending(action =>
                ScoreEconomyAction(action, snapshot, context))
            .ThenBy(action => action.Cost)
            .ThenBy(action => action.ActionId, StringComparer.Ordinal)
            .First();
    }

    protected virtual bool ShouldContinueSpending(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        int targetUnspent = (context.Memory.PlanningInitialGold * 30 + 99) / 100;
        return snapshot.Gold > targetUnspent;
    }

    protected virtual double ScoreEquip(
        LegalAction action,
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        TowerSnapshot tower = Array.Find(
            snapshot.Towers,
            item => item.Id == action.TowerInstanceId);
        int towerLevel = string.IsNullOrEmpty(tower.DefinitionId)
            ? 1
            : tower.Level;
        double strength = ScoreCardContext(
            action.CardId,
            action.TowerDefinitionId,
            action.SubjectType ?? SubjectType.Projectile,
            action.SlotIndex,
            towerLevel,
            context);
        if (!UseSynergyIndex || string.IsNullOrEmpty(tower.DefinitionId) ||
            action.SlotIndex < 0 ||
            action.SlotIndex >= tower.CardInstanceIds.Length)
        {
            return strength;
        }

        int[] candidateSlots = (int[])tower.CardInstanceIds.Clone();
        candidateSlots[action.SlotIndex] = action.CardInstanceId;
        return ScoreTowerProgram(
            tower,
            candidateSlots,
            tower.CardSubjectTypes,
            snapshot,
            context);
    }

    protected virtual double ScoreCard(
        string cardId,
        PolicyContext context)
    {
        double strength = context.CardStrength.Score(
            context.DifficultyId,
            cardId);
        if (UseStrengthIndex && !double.IsNaN(strength))
        {
            return strength;
        }
        CardKnowledge card = context.PublicKnowledge.Card(cardId);
        return Math.Max(
            BootstrapCardScore(card, SubjectType.Projectile),
            BootstrapCardScore(card, SubjectType.Enemy));
    }

    protected virtual double ScoreCardOffer(
        LegalAction action,
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        double score = ScoreCard(action.CardId, context);
        if (!UseSynergyIndex)
        {
            return score;
        }

        double bestPair = 0d;
        foreach (TowerSnapshot tower in snapshot.Towers)
        {
            SubjectType[] candidateSubjects =
                context.PublicKnowledge.Tower(tower.DefinitionId).Trigger ==
                    TowerTrigger.Attack
                    ? new[] { SubjectType.Projectile, SubjectType.Enemy }
                    : new[] { SubjectType.Enemy };
            for (int slot = 0; slot < tower.CardInstanceIds.Length; slot++)
            {
                int instanceId = tower.CardInstanceIds[slot];
                string? existingCard = ResolveCardId(
                    instanceId,
                    snapshot,
                    context);
                if (existingCard == null)
                {
                    continue;
                }
                SubjectType existingSubject = tower.CardSubjectTypes[slot];
                foreach (SubjectType candidateSubject in candidateSubjects)
                {
                    double after = PairScore(
                        existingCard,
                        existingSubject,
                        action.CardId,
                        candidateSubject,
                        tower.DefinitionId,
                        tower.Level,
                        slot,
                        null,
                        context);
                    double before = PairScore(
                        action.CardId,
                        candidateSubject,
                        existingCard,
                        existingSubject,
                        tower.DefinitionId,
                        tower.Level,
                        null,
                        slot,
                        context);
                    bestPair = Math.Max(bestPair, Math.Max(after, before));
                }
            }
        }
        return score + bestPair;
    }

    private double ScoreStartingTower(
        LegalAction action,
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        TowerKnowledge tower = context.PublicKnowledge.Tower(
            action.TowerDefinitionId);
        var ownedCards = new HashSet<string>(
            snapshot.Cards.Select(card =>
                context.PublicKnowledge.CardStableId(card.DefinitionId)),
            StringComparer.Ordinal);
        IEnumerable<CardStrengthEntry> strengths = context.CardStrength.Entries
            .Where(entry =>
                string.Equals(
                    entry.Difficulty,
                    context.DifficultyId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    entry.TowerDefinition,
                    action.TowerDefinitionId,
                    StringComparison.Ordinal) &&
                entry.TowerLevel == 1 &&
                (ownedCards.Count == 0 || ownedCards.Contains(entry.CardId)));
        double[] bestStrengths = strengths
            .OrderByDescending(entry => entry.CompositeLift)
            .Take(3)
            .Select(entry => entry.CompositeLift)
            .ToArray();
        double score = bestStrengths.Length == 0
            ? 0d
            : bestStrengths.Average();
        if (UseSynergyIndex)
        {
            double synergy = context.CardSynergy.Entries
                .Where(entry =>
                    string.Equals(
                        entry.Difficulty,
                        context.DifficultyId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        entry.TowerDefinition,
                        action.TowerDefinitionId,
                        StringComparison.Ordinal) &&
                    entry.TowerLevel == 1 &&
                    (ownedCards.Count == 0 ||
                     (ownedCards.Contains(entry.FirstCardId) &&
                      ownedCards.Contains(entry.SecondCardId))))
                .Select(entry => entry.SynergyLift)
                .DefaultIfEmpty(0d)
                .Max();
            score += synergy;
        }
        score += tower.Trigger == TowerTrigger.Attack ? 1.0 : 0.25;
        score += tower.LevelCount * 0.02;
        score -= tower.ConstructionCost * 0.0001;
        return score;
    }

    private double ScoreCardContext(
        string cardId,
        string towerId,
        SubjectType subject,
        int slot,
        int level,
        PolicyContext context)
    {
        double strength = StrengthScore(
            cardId,
            towerId,
            subject,
            slot,
            level,
            context);
        return !double.IsNaN(strength)
            ? strength
            : BootstrapCardScore(context.PublicKnowledge.Card(cardId), subject);
    }

    private double ScoreTowerProgram(
        TowerSnapshot tower,
        IReadOnlyList<int> cardInstanceIds,
        IReadOnlyList<SubjectType> subjects,
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        var program = new List<CardProgramStep>();
        double score = 0d;
        for (int slot = 0; slot < cardInstanceIds.Count; slot++)
        {
            string? cardId = ResolveCardId(
                cardInstanceIds[slot],
                snapshot,
                context);
            if (cardId == null)
            {
                continue;
            }
            SubjectType subject = slot < subjects.Count
                ? subjects[slot]
                : tower.SubjectType;
            program.Add(new CardProgramStep(cardId, subject, slot));
            score += ScoreCardContext(
                cardId,
                tower.DefinitionId,
                subject,
                slot,
                tower.Level,
                context);
        }
        if (!UseSynergyIndex || program.Count < 2)
        {
            return score;
        }

        double indexed = context.CardSynergy.ScoreProgram(
            context.DifficultyId,
            tower.DefinitionId,
            tower.Level,
            program);
        if (!double.IsNaN(indexed))
        {
            return score + indexed;
        }
        for (int first = 0; first < program.Count; first++)
        {
            for (int second = first + 1; second < program.Count; second++)
            {
                CardProgramStep a = program[first];
                CardProgramStep b = program[second];
                score += PairScore(
                    a.CardId,
                    a.SubjectType,
                    b.CardId,
                    b.SubjectType,
                    tower.DefinitionId,
                    tower.Level,
                    a.SlotIndex,
                    b.SlotIndex,
                    context);
            }
        }
        return score;
    }

    private double PairScore(
        string firstCardId,
        SubjectType firstSubject,
        string secondCardId,
        SubjectType secondSubject,
        string towerId,
        int towerLevel,
        int? firstSlot,
        int? secondSlot,
        PolicyContext context)
    {
        double indexed = context.CardSynergy.Score(
            context.DifficultyId,
            firstCardId,
            firstSubject,
            secondCardId,
            secondSubject,
            towerId,
            towerLevel,
            firstSlot,
            secondSlot);
        if (!double.IsNaN(indexed))
        {
            return indexed;
        }

        CardKnowledge first = context.PublicKnowledge.Card(firstCardId);
        CardKnowledge second = context.PublicKnowledge.Card(secondCardId);
        int sharedTags = first.Tags.Intersect(
            second.Tags,
            StringComparer.Ordinal).Count();
        double score = sharedTags * 0.08;
        bool firstGenerates = first.CreatesAdditionalSubjects(firstSubject);
        bool secondGenerates = second.CreatesAdditionalSubjects(secondSubject);
        if (firstGenerates && !secondGenerates)
        {
            score += 0.30;
        }
        else if (!firstGenerates && secondGenerates)
        {
            score -= 0.20;
        }
        return score;
    }

    private double ScoreEconomyAction(
        LegalAction action,
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        double score = 100d / Math.Max(1, action.Cost);
        if (action.Kind == LegalActionKind.PlaceTower)
        {
            TowerKnowledge tower = context.PublicKnowledge.Tower(
                action.TowerDefinitionId);
            score += tower.Trigger == TowerTrigger.Attack ? 4d : 1d;
            if (!snapshot.Towers.Any(existing => string.Equals(
                    existing.DefinitionId,
                    action.TowerDefinitionId,
                    StringComparison.Ordinal)))
            {
                score += 1d;
            }
            if (UseTacticalEconomy && !snapshot.Towers.Any(existing =>
                    existing.CardInstanceIds.Any(card => card < 0)))
            {
                score += 3d;
            }
            return score;
        }

        TowerSnapshot upgraded = Array.Find(
            snapshot.Towers,
            tower => tower.Id == action.TowerInstanceId);
        if (string.IsNullOrEmpty(upgraded.DefinitionId))
        {
            return score;
        }
        int equipped = upgraded.CardInstanceIds.Count(card => card >= 0);
        score += 3d + equipped * 2d;
        if (UseTacticalEconomy && equipped > 0)
        {
            double program = ScoreTowerProgram(
                upgraded,
                upgraded.CardInstanceIds,
                upgraded.CardSubjectTypes,
                snapshot,
                context);
            score += Math.Clamp(program, -20d, 20d) * 0.1;
        }
        return score;
    }

    private static int TacticalGoldReserve(
        IReadOnlyList<LegalAction> actions,
        PolicyContext context)
    {
        int configured = context.IntSetting("tacticalReserveGold", -1);
        if (configured >= 0)
        {
            return configured;
        }
        return actions
            .Where(action =>
                action.Kind == LegalActionKind.PlaceTower &&
                action.Cost > 0)
            .Select(action => action.Cost)
            .DefaultIfEmpty(0)
            .Min();
    }

    private static string ProgramSignature(
        IReadOnlyList<int> cardInstanceIds,
        IReadOnlyList<SubjectType> subjects)
    {
        return string.Join(
            ",",
            Enumerable.Range(0, cardInstanceIds.Count).Select(index =>
                cardInstanceIds[index].ToString(
                    System.Globalization.CultureInfo.InvariantCulture) + "@" +
                (index < subjects.Count
                    ? ((int)subjects[index]).ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                    : "-1")));
    }

    private double StrengthScore(
        string cardId,
        string towerId,
        SubjectType? subject,
        int slot,
        int level,
        PolicyContext context)
    {
        if (!UseStrengthIndex || !subject.HasValue)
        {
            return double.NaN;
        }
        return context.CardStrength.Score(
            context.DifficultyId,
            cardId,
            towerId,
            subject,
            slot,
            level);
    }

    protected static double BootstrapCardScore(
        CardKnowledge card,
        SubjectType subject)
    {
        double score = card.Tier * 0.08 - card.ComputeCost * 0.01;
        foreach (EffectOperation operation in card.Operations(subject))
        {
            score += operation switch
            {
                EffectOperation.BindBurn or
                EffectOperation.ApplyBurn or
                EffectOperation.BindPoison or
                EffectOperation.ApplyPoison or
                EffectOperation.BindExplosion or
                EffectOperation.EnableProjectileExecute or
                EffectOperation.ApplyEnemyExecute => 0.24,
                EffectOperation.Split or
                EffectOperation.DuplicateProjectile or
                EffectOperation.CreateAfterimageProjectile => 0.20,
                EffectOperation.AddPierce or
                EffectOperation.ConfigureProjectileRicochet or
                EffectOperation.BindCorrosion or
                EffectOperation.ApplyCorrosion => 0.16,
                EffectOperation.BindStun or
                EffectOperation.ApplyStun or
                EffectOperation.ApplySlow or
                EffectOperation.ApplyBind or
                EffectOperation.ApplyFreeze => 0.12,
                EffectOperation.EnlargeEnemy or
                EffectOperation.AccelerateEnemy or
                EffectOperation.DuplicateEnemy or
                EffectOperation.ApplyEnemyForbiddenDeal => -0.35,
                _ => 0.05
            };
        }
        return score;
    }

    private static bool IsSelfHarmCard(
        CardKnowledge card,
        SubjectType subject)
    {
        return subject == SubjectType.Enemy &&
            card.Operations(subject).Any(operation => operation is
                EffectOperation.Split or
                EffectOperation.EnlargeEnemy or
                EffectOperation.AccelerateEnemy or
                EffectOperation.DuplicateEnemy or
                EffectOperation.ApplyEnemyForbiddenDeal or
                EffectOperation.ApplyEnemyPhoenixCore or
                EffectOperation.ApplyEnemyMirrorWorld or
                EffectOperation.ApplyEnemyOuroboros);
    }

    private LegalAction? ChooseForcedScenarioAction(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        if (snapshot.Phase == RunPhase.AwaitingStartingTower &&
            !string.IsNullOrEmpty(context.Scenario.ForcedStartingTowerId))
        {
            return context.LegalActions.FirstOrDefault(action =>
                action.Kind == LegalActionKind.ChooseStartingTower &&
                string.Equals(
                    action.TowerDefinitionId,
                    context.Scenario.ForcedStartingTowerId,
                    StringComparison.Ordinal));
        }
        if ((snapshot.Phase == RunPhase.Planning ||
             (snapshot.Phase == RunPhase.Combat && AllowCombatBuild)) &&
            snapshot.Towers.Length == 0 &&
            !string.IsNullOrEmpty(context.Scenario.ForcedPlacedTowerId))
        {
            return context.LegalActions.FirstOrDefault(action =>
                action.Kind == LegalActionKind.PlaceTower &&
                string.Equals(
                    action.TowerDefinitionId,
                    context.Scenario.ForcedPlacedTowerId,
                    StringComparison.Ordinal));
        }
        return null;
    }

    private static string? ResolveCardId(
        int cardInstanceId,
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        CardInstanceSnapshot card = Array.Find(
            snapshot.Cards,
            item => item.Id == cardInstanceId);
        return card.Id == cardInstanceId
            ? context.PublicKnowledge.CardStableId(card.DefinitionId)
            : null;
    }

    private static bool IsMidWaveBuildMeaningful(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        if (snapshot.Enemies.Length < 2)
        {
            return false;
        }
        long maxProgress = snapshot.Enemies.Max(enemy => enemy.PathProgressMilli);
        long pathLength = Math.Max(1L, context.PublicKnowledge.PathLengthMilli);
        long progressBps = maxProgress >= pathLength
            ? 10000L
            : maxProgress * 10000L / pathLength;
        int threshold = Math.Clamp(
            context.IntSetting("midwaveBuildThreatBps", 7000),
            0,
            10000);
        bool bossApproaching = snapshot.Enemies.Any(enemy =>
            context.PublicKnowledge.Enemy(enemy.DefinitionId).Rank ==
                EnemyRank.Boss &&
            enemy.PathProgressMilli * 10000L / pathLength >=
                Math.Max(3000, threshold - 2500));
        return progressBps >= threshold || bossApproaching;
    }

    protected static PolicyDecision Decision(
        LegalAction action,
        string reason) => new(action.ActionId, reason);

    protected static LegalAction RequireKind(
        IReadOnlyList<LegalAction> actions,
        LegalActionKind kind) => actions.FirstOrDefault(action => action.Kind == kind)
            ?? throw new InvalidOperationException(
                "Required legal action " + kind + " was not available.");

    private string CardReason() => UseSynergyIndex
        ? "SYNERGY_PROGRAM_CARD"
        : UseStrengthIndex
            ? "GOOD_STANDALONE_CARD"
            : "NOVICE_RANDOM";
}

public sealed class NoviceRandomSpenderPolicy : DeterministicPolicyBase
{
    public override string PolicyId => "novice-random-spender";
    protected override bool UseRandomCardChoice => true;

    protected override LegalAction? ChooseEconomyAction(
        SimulationSnapshot snapshot,
        IReadOnlyList<LegalAction> actions,
        PolicyContext context)
    {
        List<LegalAction> candidates = actions.Where(action =>
            action.Kind is LegalActionKind.PlaceTower or
                LegalActionKind.UpgradeTower).ToList();
        return candidates.Count == 0 ? null : context.Random.Choose(candidates);
    }
}

public sealed class NoviceBuildFirstPolicy : DeterministicPolicyBase
{
    public override string PolicyId => "novice-build-first";
    protected override bool PreferBuild => true;
    protected override bool UseRandomCardChoice => true;
}

public sealed class NoviceUpgradeFirstPolicy : DeterministicPolicyBase
{
    public override string PolicyId => "novice-upgrade-first";
    protected override bool PreferUpgrade => true;
    protected override bool UseRandomCardChoice => true;
}

public sealed class GoodStandalonePolicy : DeterministicPolicyBase
{
    public override string PolicyId => "good-standalone";
    protected override bool UseStrengthIndex => true;
}

public class SynergyTacticalPolicy : DeterministicPolicyBase
{
    public override string PolicyId => "synergy-tactical";
    protected override bool UseStrengthIndex => true;
    protected override bool UseSynergyIndex => true;
    protected override bool AllowCombatBuild => true;
    protected override bool UseTacticalEconomy => true;
}

public sealed class SynergyNoCombatBuildPolicy : SynergyTacticalPolicy
{
    public override string PolicyId => "synergy-no-combat-build";
    protected override bool AllowCombatBuild => false;
}

public sealed class SynergyDisabledPolicy : DeterministicPolicyBase
{
    public override string PolicyId => "synergy-disabled";
    protected override bool UseStrengthIndex => true;
    protected override bool AllowCombatBuild => true;
    protected override bool UseTacticalEconomy => true;
}

public sealed class NoSpendPolicy : DeterministicPolicyBase
{
    public override string PolicyId => "no-spend";
    protected override bool SpendGold => false;
    protected override bool EquipOnlyOneCard => true;
}

public sealed class CardFixturePolicy : DeterministicPolicyBase
{
    public override string PolicyId => "card-fixture";
    protected override bool PreferBuild => true;
}

/// <summary>
/// Reasonable novice used only by the Easy active-card coverage matrix. The
/// scenario forces one measured card into its legal context; the policy spends
/// normally on additional towers but cannot add a second card or perform
/// pair/order optimization.
/// </summary>
public sealed class CardCoverageNovicePolicy : DeterministicPolicyBase
{
    public override string PolicyId => "card-coverage-novice";
    protected override bool PreferBuild => true;
    protected override bool UseRandomCardChoice => true;
    protected override bool EquipOnlyOneCard => true;
}

public sealed class OracleSearchPolicy : SynergyTacticalPolicy
{
    public override string PolicyId => "oracle-search";
    public override string PolicyVersion => "1.3.0-public-search";
    protected override bool PreferBuild => true;
    protected override bool UseTacticalEconomy => false;

    protected override bool ShouldContinueSpending(
        SimulationSnapshot snapshot,
        PolicyContext context) => context.LegalActions.Any(action =>
            action.Kind is LegalActionKind.PlaceTower or
                LegalActionKind.UpgradeTower);

    protected override PolicyDecision DecideCombat(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        LegalAction? pack = context.LegalActions.FirstOrDefault(action =>
            action.Kind == LegalActionKind.OpenCardPack);
        if (pack != null)
        {
            return Decision(pack, "OPEN_CARD_PACK");
        }

        // The feasibility policy scores every currently legal construction
        // candidate from the public snapshot. It intentionally spends more
        // aggressively than the human-proxy policy, but never inspects future
        // spawns or the simulation's private RNG state.
        LegalAction? build = snapshot.Enemies.Length == 0
            ? null
            : context.LegalActions
                .Where(action => action.Kind == LegalActionKind.PlaceTower)
                .OrderByDescending(action =>
                    ScorePublicOracleBuild(action, snapshot, context))
                .ThenBy(action => action.ActionId, StringComparer.Ordinal)
                .FirstOrDefault();
        if (build != null)
        {
            return Decision(build, "ORACLE_PUBLIC_SEARCH_BUILD");
        }

        return Decision(
            RequireKind(context.LegalActions, LegalActionKind.NoOp),
            "NO_OP");
    }

    public override PolicyDecision Decide(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        LegalAction? searched = context.LegalActions
            .Where(action =>
                context.OracleActionScores.ContainsKey(action.ActionId))
            .OrderByDescending(action =>
                context.OracleActionScores[action.ActionId])
            .ThenBy(action => action.ActionId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (searched != null)
        {
            return Decision(searched, "ORACLE_REPLAY_SEARCH");
        }
        return base.Decide(snapshot, context);
    }

    private static double ScorePublicOracleBuild(
        LegalAction action,
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        TowerKnowledge tower = context.PublicKnowledge.Tower(
            action.TowerDefinitionId);
        double score = 100d / Math.Max(1, action.Cost);
        score += tower.Trigger == TowerTrigger.Attack ? 6d : 2d;
        EnemySnapshot threat = snapshot.Enemies
            .OrderByDescending(enemy => enemy.PathProgressMilli)
            .ThenByDescending(enemy =>
                context.PublicKnowledge.Enemy(enemy.DefinitionId).LeakDamage)
            .ThenBy(enemy => enemy.Id)
            .First();
        BuildSpotSnapshot spot = Array.Find(
            snapshot.BuildSpots,
            candidate => candidate.Index == action.BuildPointIndex);
        if (spot.Index == action.BuildPointIndex)
        {
            double distance = Math.Sqrt(
                spot.Position.DistanceSquaredRaw(threat.Position));
            score += 10d / (1d + distance / 1000d);
        }
        if (context.PublicKnowledge.Enemy(threat.DefinitionId).Rank ==
            EnemyRank.Boss)
        {
            score += tower.Trigger == TowerTrigger.Attack ? 3d : 1d;
        }
        return score;
    }
}

public sealed class AdversarialRandomPolicy : IPlayerPolicy
{
    public string PolicyId => "adversarial-random";
    public string PolicyVersion => "1.0.0";

    public PolicyDecision Decide(
        SimulationSnapshot snapshot,
        PolicyContext context)
    {
        IReadOnlyList<LegalAction> actions = context.LegalActions;
        LegalAction? forcedProgress = null;
        if (context.Memory.DecisionsInPhase >= 12)
        {
            forcedProgress = actions.FirstOrDefault(action => action.Kind is
                LegalActionKind.StartWave or
                LegalActionKind.ResumeCardPackCombat);
        }
        LegalAction chosen = forcedProgress ?? context.Random.Choose(actions);
        return new PolicyDecision(chosen.ActionId, "ADVERSARIAL_RANDOM");
    }
}
