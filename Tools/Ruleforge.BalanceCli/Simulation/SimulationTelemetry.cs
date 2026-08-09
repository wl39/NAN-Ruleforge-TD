using System;
using System.Collections.Generic;
using System.Linq;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.BalanceCli.Simulation;

/// <summary>
/// Read-only observations collected from snapshots, command results, and the
/// presentation-event sink. Consuming this telemetry never changes combat
/// state or consumes a simulation RNG stream.
/// </summary>
public sealed class SimulationTelemetry
{
    private readonly Dictionary<int, string> enemyDefinitionByEntity = new();
    private readonly Dictionary<int, int> projectileTowerByEntity = new();
    private readonly HashSet<int> knownTowerIds = new();

    public int StartingBaseHealth { get; set; }
    public int StartingGold { get; set; }
    public int GoldEarned { get; set; }
    public int GoldSpent { get; set; }
    public int TotalLeakDamage { get; set; }
    public int LeakedEnemyCount { get; set; }
    public int TowerBuildCount { get; set; }
    public int TowerUpgradeCount { get; set; }
    public int MidWaveTowerBuildCount { get; set; }
    public int RejectedCommandCount { get; set; }
    public int SafetyLimitReachedCount { get; set; }
    public int BossAbilityActivatedCount { get; set; }
    public int BossAbilityTelegraphedCount { get; set; }
    public int BossAbilityBlockedCount { get; set; }
    public int EnemyDeathCount { get; set; }
    public int WaveCompletedCount { get; set; }
    public int DeathChainCount { get; set; }
    public long TotalDamageMilli { get; set; }
    public string SelectedStartingTower { get; set; } = string.Empty;
    public List<string> SelectedCards { get; set; } = new();
    public List<long> MidWaveBuildTicks { get; set; } = new();
    public List<MidWaveBuildRecord> MidWaveBuilds { get; set; } = new();
    public List<CardChoiceRecord> DraftChoices { get; set; } = new();
    public List<CardChoiceRecord> CardPackChoices { get; set; } = new();
    public List<CommandLogEntry> Commands { get; set; } = new();
    public List<PhaseTransitionRecord> PhaseTransitions { get; set; } = new();
    public Dictionary<string, int> RejectedCommandReasons { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, int> SafetyLimitReasons { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, long> CardExecutionCount { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, long> DamageByEnemyType { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, long> DamageByTower { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, long> DamageBySourceEntity { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, long> DamageByCard { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, int> KillsByTower { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, int> KillsByCard { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, int> LeaksByEnemyType { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, int> TowerBuildsByDefinition { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, long> StatusApplications { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, long> StatusUptimeTicks { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, long> StatusUptimeTicksByCard { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, long> GoldByOrigin { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, long> GoldByTower { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, long> GoldByCard { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, int> BossAbilitiesActivated { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, int> BossAbilitiesBlocked { get; set; } =
        new(StringComparer.Ordinal);
    public List<string> AttributionLimitations { get; set; } = new()
    {
        "Presentation damage events expose their source entity, not the exact source card; DamageByCard and KillsByCard remain empty unless GameLogic adds that attribution.",
        "GameLogic currently exposes boss activations but no distinct boss-ability-blocked presentation event."
    };

    public void ObserveInitial(SimulationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        StartingBaseHealth = snapshot.BaseHealth;
        StartingGold = snapshot.Gold;
        RefreshPublicEntityMaps(snapshot);
    }

    public void ObserveCommand(
        string actionId,
        in GameCommand command,
        in CommandResult commandResult,
        SimulationSnapshot before,
        SimulationSnapshot after,
        CompiledContent content,
        int sequence)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(content);

        Commands.Add(new CommandLogEntry
        {
            Sequence = sequence,
            Tick = before.Tick,
            Phase = before.Phase,
            ActionId = actionId,
            Type = command.Type,
            ContentId = command.ContentId,
            PrimaryId = command.PrimaryId,
            SecondaryId = command.SecondaryId,
            TertiaryId = command.TertiaryId,
            Accepted = commandResult.Accepted,
            Error = commandResult.Error,
            Message = commandResult.Message
        });

        ObservePhaseTransition(before, after);
        if (!commandResult.Accepted)
        {
            RejectedCommandCount++;
            Add(RejectedCommandReasons, commandResult.Error.ToString(), 1);
            return;
        }

        int immediateSpend = Math.Max(0, before.Gold - after.Gold);
        GoldSpent = checked(GoldSpent + immediateSpend);
        switch (command.Type)
        {
            case GameCommandType.ChooseStartingTower:
                SelectedStartingTower = command.ContentId;
                break;
            case GameCommandType.PlaceTower:
                TowerBuildCount++;
                Add(TowerBuildsByDefinition, command.ContentId, 1);
                if (before.Phase == RunPhase.Combat)
                {
                    MidWaveTowerBuildCount++;
                    MidWaveBuildTicks.Add(before.Tick);
                    MidWaveBuilds.Add(new MidWaveBuildRecord
                    {
                        MidWaveBuildTick = before.Tick,
                        GoldBeforeBuild = before.Gold,
                        GoldAfterBuild = after.Gold,
                        TowerDefinition = command.ContentId,
                        BuildSlot = command.PrimaryId,
                        ThreatAtBuild = MaximumPathProgress(before),
                        EnemiesAliveAtBuild = before.Enemies.Count(enemy =>
                            enemy.Alive),
                        ExpectedLeakBeforeBuild = ExpectedLeakDamage(
                            before,
                            content)
                    });
                }
                break;
            case GameCommandType.UpgradeTower:
                TowerUpgradeCount++;
                break;
            case GameCommandType.SelectDraft:
                RecordCardChoice(
                    before,
                    content,
                    command.PrimaryId,
                    before.DraftOffers,
                    DraftChoices);
                break;
            case GameCommandType.SelectCardPack:
                RecordCardChoice(
                    before,
                    content,
                    command.PrimaryId,
                    before.CardPackOffers,
                    CardPackChoices);
                break;
        }

        RefreshPublicEntityMaps(after);
    }

    public void ObserveStep(
        SimulationSnapshot before,
        SimulationSnapshot after,
        SimulationEventBuffer events,
        CompiledContent content)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(content);

        ObservePhaseTransition(before, after);
        ObserveEvents(events, after);
        ObserveStatusUptime(after, content);

        int goldDelta = after.Gold - before.Gold;
        int observedSpend = GoldEarned - lastObservedEarned - goldDelta;
        if (observedSpend > 0)
        {
            GoldSpent = checked(GoldSpent + observedSpend);
        }
        lastObservedEarned = GoldEarned;
        RefreshPublicEntityMaps(after);
    }

    public void ObserveCommandEvents(
        SimulationEventBuffer events,
        SimulationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(snapshot);
        ObserveEvents(events, snapshot);
        lastObservedEarned = GoldEarned;
        RefreshPublicEntityMaps(snapshot);
    }

    private int lastObservedEarned;

    private void ObserveEvents(
        SimulationEventBuffer events,
        SimulationSnapshot snapshot)
    {
        RefreshPublicEntityMaps(snapshot);
        for (int index = 0; index < events.Count; index++)
        {
            SimulationPresentationEvent item = events[index];
            switch (item.Type)
            {
                case PresentationEventType.EnemySpawned:
                    enemyDefinitionByEntity[item.SubjectId] = item.ContentId;
                    break;
                case PresentationEventType.ProjectileSpawned:
                    projectileTowerByEntity[item.SubjectId] =
                        ResolveTower(item.SourceId);
                    break;
                case PresentationEventType.EnemyDamaged:
                    ObserveDamage(item);
                    break;
                case PresentationEventType.EnemyDied:
                    EnemyDeathCount++;
                    ObserveKill(item);
                    break;
                case PresentationEventType.EnemyLeaked:
                    LeakedEnemyCount++;
                    TotalLeakDamage = checked(
                        TotalLeakDamage + Math.Max(0, item.Value));
                    Add(LeaksByEnemyType, StableEnemyId(item), 1);
                    break;
                case PresentationEventType.CardExecuted:
                    Add(CardExecutionCount, item.ContentId, 1L);
                    break;
                case PresentationEventType.RewardGranted:
                    ObserveReward(item);
                    break;
                case PresentationEventType.StatusApplied:
                    Add(StatusApplications, item.ContentId, 1L);
                    break;
                case PresentationEventType.WaveCompleted:
                    WaveCompletedCount++;
                    break;
                case PresentationEventType.SafetyLimitReached:
                    SafetyLimitReachedCount++;
                    Add(SafetyLimitReasons, item.ContentId, 1);
                    break;
                case PresentationEventType.BossAbilityTelegraphed:
                    BossAbilityTelegraphedCount++;
                    break;
                case PresentationEventType.BossAbilityActivated:
                    BossAbilityActivatedCount++;
                    Add(BossAbilitiesActivated, item.ContentId, 1);
                    break;
                case PresentationEventType.EffectTriggered:
                    if (item.ContentId.Contains(
                            "chain",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        DeathChainCount++;
                    }
                    break;
            }
        }
    }

    private void ObserveDamage(in SimulationPresentationEvent item)
    {
        long amount = Math.Max(0, item.Value);
        TotalDamageMilli = checked(TotalDamageMilli + amount);
        Add(DamageByEnemyType, StableEnemyId(item), amount);
        Add(
            DamageBySourceEntity,
            item.SourceId.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            amount);
        int towerId = ResolveTower(item.SourceId);
        if (towerId >= 0)
        {
            Add(DamageByTower, TowerKey(towerId), amount);
        }
    }

    private void ObserveKill(in SimulationPresentationEvent item)
    {
        int towerId = ResolveTower(item.SourceId);
        if (towerId >= 0)
        {
            Add(KillsByTower, TowerKey(towerId), 1);
        }
    }

    private void ObserveReward(in SimulationPresentationEvent item)
    {
        int amount = Math.Max(0, item.Value);
        GoldEarned = checked(GoldEarned + amount);
        Add(GoldByOrigin, item.ContentId, (long)amount);
        if (item.SourceId >= 0)
        {
            Add(GoldByTower, TowerKey(item.SourceId), (long)amount);
        }
    }

    private void ObserveStatusUptime(
        SimulationSnapshot snapshot,
        CompiledContent content)
    {
        foreach (EnemySnapshot enemy in snapshot.Enemies)
        {
            foreach (StatusSnapshot status in enemy.StatusDetails)
            {
                if (status.RemainingTicks <= 0)
                {
                    continue;
                }
                Add(StatusUptimeTicks, status.Type.ToString(), 1L);
                if (status.SourceCardId.IsValid)
                {
                    Add(
                        StatusUptimeTicksByCard,
                        content.GetCard(status.SourceCardId).StableId,
                        1L);
                }
            }
        }
    }

    private void ObservePhaseTransition(
        SimulationSnapshot before,
        SimulationSnapshot after)
    {
        if (before.Phase == after.Phase)
        {
            return;
        }
        PhaseTransitions.Add(new PhaseTransitionRecord
        {
            Tick = after.Tick,
            From = before.Phase,
            To = after.Phase
        });
    }

    private void RefreshPublicEntityMaps(SimulationSnapshot snapshot)
    {
        foreach (TowerSnapshot tower in snapshot.Towers)
        {
            knownTowerIds.Add(tower.Id);
        }
        foreach (EnemySnapshot enemy in snapshot.Enemies)
        {
            enemyDefinitionByEntity[enemy.Id] = enemy.DefinitionId;
        }
        foreach (ProjectileSnapshot projectile in snapshot.Projectiles)
        {
            projectileTowerByEntity[projectile.Id] = projectile.SourceTowerId;
        }
    }

    private int ResolveTower(int sourceEntityId)
    {
        if (projectileTowerByEntity.TryGetValue(
                sourceEntityId,
                out int projectileTower))
        {
            return projectileTower;
        }
        return knownTowerIds.Contains(sourceEntityId)
            ? sourceEntityId
            : -1;
    }

    private string StableEnemyId(in SimulationPresentationEvent item)
    {
        if (!string.IsNullOrEmpty(item.ContentId) &&
            item.Type is PresentationEventType.EnemyLeaked or
                PresentationEventType.EnemyDied)
        {
            return item.ContentId;
        }
        return enemyDefinitionByEntity.TryGetValue(
            item.SubjectId,
            out string? definitionId)
                ? definitionId
                : "unknown";
    }

    private void RecordCardChoice(
        SimulationSnapshot snapshot,
        CompiledContent content,
        int offerIndex,
        IReadOnlyList<CardId> offers,
        ICollection<CardChoiceRecord> destination)
    {
        if (offerIndex < 0 || offerIndex >= offers.Count)
        {
            return;
        }
        string cardId = content.GetCard(offers[offerIndex]).StableId;
        SelectedCards.Add(cardId);
        destination.Add(new CardChoiceRecord
        {
            Tick = snapshot.Tick,
            WaveNumber = snapshot.WaveIndex + 1,
            OfferIndex = offerIndex,
            CardId = cardId
        });
    }

    private static int MaximumPathProgress(SimulationSnapshot snapshot)
    {
        long maximum = 0;
        foreach (EnemySnapshot enemy in snapshot.Enemies)
        {
            maximum = Math.Max(maximum, enemy.PathProgressMilli);
        }
        return (int)Math.Min(int.MaxValue, maximum);
    }

    private static int ExpectedLeakDamage(
        SimulationSnapshot snapshot,
        CompiledContent content)
    {
        double pathLength = 0;
        IReadOnlyList<SimPosition> points = content.Run.PathPoints;
        for (int index = 1; index < points.Count; index++)
        {
            long deltaX = points[index].X.MilliUnits -
                points[index - 1].X.MilliUnits;
            long deltaY = points[index].Y.MilliUnits -
                points[index - 1].Y.MilliUnits;
            pathLength += Math.Sqrt(
                (double)deltaX * deltaX + (double)deltaY * deltaY);
        }
        if (pathLength <= 0)
        {
            return 0;
        }

        int expected = 0;
        foreach (EnemySnapshot enemy in snapshot.Enemies)
        {
            if (!enemy.Alive || enemy.PathProgressMilli / pathLength < 0.70 ||
                !content.TryGetEnemyId(
                    enemy.DefinitionId,
                    out EnemyDefinitionId enemyId))
            {
                continue;
            }
            expected = checked(
                expected + content.GetEnemy(enemyId).LeakDamage);
        }
        return expected;
    }

    private static string TowerKey(int towerId) =>
        towerId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static void Add(
        IDictionary<string, int> values,
        string key,
        int amount)
    {
        key = string.IsNullOrEmpty(key) ? "unknown" : key;
        values[key] = values.TryGetValue(key, out int current)
            ? checked(current + amount)
            : amount;
    }

    private static void Add(
        IDictionary<string, long> values,
        string key,
        long amount)
    {
        key = string.IsNullOrEmpty(key) ? "unknown" : key;
        values[key] = values.TryGetValue(key, out long current)
            ? checked(current + amount)
            : amount;
    }
}
