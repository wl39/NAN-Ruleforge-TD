using System;
using System.Collections.Generic;
using System.Linq;
using RuleforgeTD.BalanceCli.Infrastructure;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.BalanceCli.Simulation;

/// <summary>
/// A JSON-friendly, deterministically ordered projection of public state. The
/// authoritative replay fingerprint remains GameSimulation.ComputeStateHash;
/// this projection provides readable final-state diffs for CLI reports.
/// </summary>
public sealed class StableSnapshotProjection
{
    public long Tick { get; set; }
    public RunPhase Phase { get; set; }
    public int WaveIndex { get; set; }
    public int BaseHealth { get; set; }
    public int Gold { get; set; }
    public int CardPackProgress { get; set; }
    public int CardPackProgressBps { get; set; }
    public int NextCardPackThreshold { get; set; }
    public int PendingCardInstanceId { get; set; }
    public List<SnapshotTowerProjection> Towers { get; set; } = new();
    public List<SnapshotCardProjection> Cards { get; set; } = new();
    public List<SnapshotEnemyProjection> Enemies { get; set; } = new();
    public List<SnapshotProjectileProjection> Projectiles { get; set; } = new();
    public List<SnapshotHazardProjection> Hazards { get; set; } = new();
    public List<SnapshotLineageProjection> Lineages { get; set; } = new();
    public List<string> DraftOffers { get; set; } = new();
    public List<string> CardPackOffers { get; set; } = new();
    public List<string> UnlockedTowerIds { get; set; } = new();
    public List<int> RewardQueueCardPackIds { get; set; } = new();
    public List<SnapshotCardPackProjection> CardPacks { get; set; } = new();

    public static StableSnapshotProjection From(
        SimulationSnapshot snapshot,
        CompiledContent content)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(content);

        return new StableSnapshotProjection
        {
            Tick = snapshot.Tick,
            Phase = snapshot.Phase,
            WaveIndex = snapshot.WaveIndex,
            BaseHealth = snapshot.BaseHealth,
            Gold = snapshot.Gold,
            CardPackProgress = snapshot.CardPackProgress,
            CardPackProgressBps = snapshot.CardPackProgressBps,
            NextCardPackThreshold = snapshot.NextCardPackThreshold,
            PendingCardInstanceId = snapshot.PendingCardInstanceId,
            Towers = snapshot.Towers
                .OrderBy(tower => tower.Id)
                .Select(SnapshotTowerProjection.From)
                .ToList(),
            Cards = snapshot.Cards
                .OrderBy(card => card.Id)
                .Select(card => SnapshotCardProjection.From(card, content))
                .ToList(),
            Enemies = snapshot.Enemies
                .OrderBy(enemy => enemy.Id)
                .Select(SnapshotEnemyProjection.From)
                .ToList(),
            Projectiles = snapshot.Projectiles
                .OrderBy(projectile => projectile.Id)
                .Select(SnapshotProjectileProjection.From)
                .ToList(),
            Hazards = snapshot.Hazards
                .OrderBy(hazard => hazard.Id)
                .Select(SnapshotHazardProjection.From)
                .ToList(),
            Lineages = snapshot.Lineages
                .OrderBy(lineage => lineage.Id)
                .Select(SnapshotLineageProjection.From)
                .ToList(),
            DraftOffers = snapshot.DraftOffers
                .Select(id => content.GetCard(id).StableId)
                .ToList(),
            CardPackOffers = snapshot.CardPackOffers
                .Select(id => content.GetCard(id).StableId)
                .ToList(),
            UnlockedTowerIds = snapshot.UnlockedTowerIds
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList(),
            RewardQueueCardPackIds = snapshot.RewardQueueCardPackIds.ToList(),
            CardPacks = snapshot.CardPacks
                .OrderBy(pack => pack.Id)
                .Select(SnapshotCardPackProjection.From)
                .ToList()
        };
    }

    public static string ComputeHash(
        SimulationSnapshot snapshot,
        CompiledContent content) =>
        JsonSupport.Sha256Text(
            JsonSupport.SerializeStable(From(snapshot, content)));
}

public sealed class SnapshotTowerProjection
{
    public int Id { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public int BuildPointIndex { get; set; }
    public long XMilli { get; set; }
    public long YMilli { get; set; }
    public int Level { get; set; }
    public SubjectType SubjectType { get; set; }
    public List<int> CardInstanceIds { get; set; } = new();
    public List<SubjectType> CardSubjectTypes { get; set; } = new();

    internal static SnapshotTowerProjection From(TowerSnapshot tower) => new()
    {
        Id = tower.Id,
        DefinitionId = tower.DefinitionId,
        BuildPointIndex = tower.BuildPointIndex,
        XMilli = tower.Position.X.MilliUnits,
        YMilli = tower.Position.Y.MilliUnits,
        Level = tower.Level,
        SubjectType = tower.SubjectType,
        CardInstanceIds = tower.CardInstanceIds.ToList(),
        CardSubjectTypes = tower.CardSubjectTypes.ToList()
    };
}

public sealed class SnapshotCardProjection
{
    public int Id { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public int Level { get; set; }
    public bool Equipped { get; set; }
    public int TowerId { get; set; }
    public int Slot { get; set; }

    internal static SnapshotCardProjection From(
        CardInstanceSnapshot card,
        CompiledContent content) => new()
    {
        Id = card.Id,
        DefinitionId = content.GetCard(card.DefinitionId).StableId,
        Level = card.Level,
        Equipped = card.Equipped,
        TowerId = card.TowerId,
        Slot = card.Slot
    };
}

public sealed class SnapshotEnemyProjection
{
    public int Id { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public int LineageId { get; set; }
    public long PathProgressMilli { get; set; }
    public long XMilli { get; set; }
    public long YMilli { get; set; }
    public long HealthMilli { get; set; }
    public long MaxHealthMilli { get; set; }
    public int Armor { get; set; }
    public int SlowBps { get; set; }
    public int SpeedMultiplierBps { get; set; }
    public int SizeMultiplierBps { get; set; }
    public int ControlGauge { get; set; }
    public int ControlThreshold { get; set; }
    public int RewardBudget { get; set; }
    public int WaveProgressBudget { get; set; }
    public int CardPackProgressBudget { get; set; }
    public int Generation { get; set; }
    public bool Alive { get; set; }
    public bool IsShimmering { get; set; }
    public long ShieldMilli { get; set; }
    public int DeathBindingCount { get; set; }
    public List<SnapshotStatusProjection> Statuses { get; set; } = new();

    internal static SnapshotEnemyProjection From(EnemySnapshot enemy) => new()
    {
        Id = enemy.Id,
        DefinitionId = enemy.DefinitionId,
        LineageId = enemy.LineageId,
        PathProgressMilli = enemy.PathProgressMilli,
        XMilli = enemy.Position.X.MilliUnits,
        YMilli = enemy.Position.Y.MilliUnits,
        HealthMilli = enemy.HealthMilli,
        MaxHealthMilli = enemy.MaxHealthMilli,
        Armor = enemy.Armor,
        SlowBps = enemy.SlowBps,
        SpeedMultiplierBps = enemy.SpeedMultiplierBps,
        SizeMultiplierBps = enemy.SizeMultiplierBps,
        ControlGauge = enemy.ControlGauge,
        ControlThreshold = enemy.ControlThreshold,
        RewardBudget = enemy.RewardBudget,
        WaveProgressBudget = enemy.WaveProgressBudget,
        CardPackProgressBudget = enemy.CardPackProgressBudget,
        Generation = enemy.Generation,
        Alive = enemy.Alive,
        IsShimmering = enemy.IsShimmering,
        ShieldMilli = enemy.ShieldMilli,
        DeathBindingCount = enemy.DeathBindingCount,
        Statuses = enemy.StatusDetails
            .OrderBy(status => status.InstanceId)
            .Select(SnapshotStatusProjection.From)
            .ToList()
    };
}

public sealed class SnapshotStatusProjection
{
    public int InstanceId { get; set; }
    public StatusType Type { get; set; }
    public int SourceEntityId { get; set; }
    public int SourceTowerId { get; set; }
    public int SourceCardId { get; set; }
    public int Stacks { get; set; }
    public int Intensity { get; set; }
    public int RemainingTicks { get; set; }
    public int MaxStacks { get; set; }
    public int TickInterval { get; set; }
    public int ArmorIgnoreBps { get; set; }

    internal static SnapshotStatusProjection From(StatusSnapshot status) => new()
    {
        InstanceId = status.InstanceId,
        Type = status.Type,
        SourceEntityId = status.SourceEntityId,
        SourceTowerId = status.SourceTowerId,
        SourceCardId = status.SourceCardId.Value,
        Stacks = status.Stacks,
        Intensity = status.Intensity,
        RemainingTicks = status.RemainingTicks,
        MaxStacks = status.MaxStacks,
        TickInterval = status.TickInterval,
        ArmorIgnoreBps = status.ArmorIgnoreBps
    };
}

public sealed class SnapshotProjectileProjection
{
    public int Id { get; set; }
    public int TargetId { get; set; }
    public int SourceTowerId { get; set; }
    public long XMilli { get; set; }
    public long YMilli { get; set; }
    public long DamageMilli { get; set; }
    public int RemainingLifetimeTicks { get; set; }
    public int RadiusMilli { get; set; }
    public int PierceRemaining { get; set; }
    public int PiercesUsed { get; set; }
    public int DirectionXBps { get; set; }
    public int DirectionYBps { get; set; }
    public bool Homing { get; set; }
    public bool ApplyEnemyProgramOnHit { get; set; }
    public int BindingCount { get; set; }
    public ulong VisualFlags { get; set; }
    public int RicochetsUsed { get; set; }
    public int RicochetsRemaining { get; set; }
    public long DistanceTravelledMilli { get; set; }
    public int DelayRemainingTicks { get; set; }

    internal static SnapshotProjectileProjection From(
        ProjectileSnapshot projectile) => new()
    {
        Id = projectile.Id,
        TargetId = projectile.TargetId,
        SourceTowerId = projectile.SourceTowerId,
        XMilli = projectile.Position.X.MilliUnits,
        YMilli = projectile.Position.Y.MilliUnits,
        DamageMilli = projectile.DamageMilli,
        RemainingLifetimeTicks = projectile.RemainingLifetimeTicks,
        RadiusMilli = projectile.RadiusMilli,
        PierceRemaining = projectile.PierceRemaining,
        PiercesUsed = projectile.PiercesUsed,
        DirectionXBps = projectile.DirectionXBps,
        DirectionYBps = projectile.DirectionYBps,
        Homing = projectile.Homing,
        ApplyEnemyProgramOnHit = projectile.ApplyEnemyProgramOnHit,
        BindingCount = projectile.BindingCount,
        VisualFlags = (ulong)projectile.VisualFlags,
        RicochetsUsed = projectile.RicochetsUsed,
        RicochetsRemaining = projectile.RicochetsRemaining,
        DistanceTravelledMilli = projectile.DistanceTravelledMilli,
        DelayRemainingTicks = projectile.DelayRemainingTicks
    };
}

public sealed class SnapshotHazardProjection
{
    public int Id { get; set; }
    public StatusType StatusType { get; set; }
    public long StartXMilli { get; set; }
    public long StartYMilli { get; set; }
    public long EndXMilli { get; set; }
    public long EndYMilli { get; set; }
    public int RadiusMilli { get; set; }
    public int DurationTicks { get; set; }
    public int RemainingTicks { get; set; }
    public int SourceTowerId { get; set; }
    public int SourceCardId { get; set; }
    public int SourceCardInstanceId { get; set; }
    public int SourceEntityId { get; set; }

    internal static SnapshotHazardProjection From(HazardSnapshot hazard) => new()
    {
        Id = hazard.Id,
        StatusType = hazard.StatusType,
        StartXMilli = hazard.StartPosition.X.MilliUnits,
        StartYMilli = hazard.StartPosition.Y.MilliUnits,
        EndXMilli = hazard.EndPosition.X.MilliUnits,
        EndYMilli = hazard.EndPosition.Y.MilliUnits,
        RadiusMilli = hazard.RadiusMilli,
        DurationTicks = hazard.DurationTicks,
        RemainingTicks = hazard.RemainingTicks,
        SourceTowerId = hazard.SourceTowerId,
        SourceCardId = hazard.SourceCardId.Value,
        SourceCardInstanceId = hazard.SourceCardInstanceId,
        SourceEntityId = hazard.SourceEntityId
    };
}

public sealed class SnapshotLineageProjection
{
    public int Id { get; set; }
    public int HighestGeneration { get; set; }
    public int SplitCount { get; set; }
    public int SpawnedEntityCount { get; set; }
    public int LiveMembers { get; set; }
    public int BaseRewardBudget { get; set; }
    public int MaxRewardBudget { get; set; }
    public int PaidReward { get; set; }
    public int ForfeitedReward { get; set; }
    public int ProgressBudget { get; set; }
    public int ConsumedProgress { get; set; }
    public int BaseCardPackProgress { get; set; }
    public int AwardedCardPackProgress { get; set; }
    public int ForfeitedCardPackProgress { get; set; }
    public bool IsShimmering { get; set; }
    public int RewardAugmentCount { get; set; }

    internal static SnapshotLineageProjection From(LineageSnapshot lineage) =>
        new()
        {
            Id = lineage.Id,
            HighestGeneration = lineage.HighestGeneration,
            SplitCount = lineage.SplitCount,
            SpawnedEntityCount = lineage.SpawnedEntityCount,
            LiveMembers = lineage.LiveMembers,
            BaseRewardBudget = lineage.BaseRewardBudget,
            MaxRewardBudget = lineage.MaxRewardBudget,
            PaidReward = lineage.PaidReward,
            ForfeitedReward = lineage.ForfeitedReward,
            ProgressBudget = lineage.ProgressBudget,
            ConsumedProgress = lineage.ConsumedProgress,
            BaseCardPackProgress = lineage.BaseCardPackProgress,
            AwardedCardPackProgress = lineage.AwardedCardPackProgress,
            ForfeitedCardPackProgress = lineage.ForfeitedCardPackProgress,
            IsShimmering = lineage.IsShimmering,
            RewardAugmentCount = lineage.RewardAugmentCount
        };
}

public sealed class SnapshotCardPackProjection
{
    public int Id { get; set; }
    public CardPackSource Source { get; set; }
    public long XMilli { get; set; }
    public long YMilli { get; set; }
    public bool WorldDrop { get; set; }

    internal static SnapshotCardPackProjection From(CardPackSnapshot pack) =>
        new()
        {
            Id = pack.Id,
            Source = pack.Source,
            XMilli = pack.Position.X.MilliUnits,
            YMilli = pack.Position.Y.MilliUnits,
            WorldDrop = pack.WorldDrop
        };
}
