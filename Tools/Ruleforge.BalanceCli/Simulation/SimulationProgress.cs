using System;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.BalanceCli.Simulation;

/// <summary>
/// Read-only, presentation-friendly progress from an authoritative simulation.
/// Observers receive scalar copies so terminal rendering cannot mutate game state.
/// </summary>
public sealed class SimulationProgressUpdate
{
    public string DifficultyId { get; init; } = string.Empty;
    public string PolicyId { get; init; } = string.Empty;
    public ulong GameSeed { get; init; }
    public ulong PolicySeed { get; init; }
    public long Tick { get; init; }
    public int TickRate { get; init; }
    public RunPhase Phase { get; init; }
    public int WaveNumber { get; init; }
    public int TotalWaves { get; init; }
    public int BaseHealth { get; init; }
    public int StartingBaseHealth { get; init; }
    public int Gold { get; init; }
    public int GoldEarned { get; init; }
    public int GoldSpent { get; init; }
    public int EnemiesAlive { get; init; }
    public int EnemiesKilled { get; init; }
    public int EnemiesLeaked { get; init; }
    public int ActiveProjectiles { get; init; }
    public int ActiveStatuses { get; init; }
    public int TowerCount { get; init; }
    public long TotalDamageMilli { get; init; }
    public int Decisions { get; init; }
    public string LastAction { get; init; } = string.Empty;
    public SimulationOutcome? Outcome { get; init; }
    public string? Error { get; init; }

    public TimeSpan SimulatedTime => TickRate > 0
        ? TimeSpan.FromSeconds((double)Tick / TickRate)
        : TimeSpan.Zero;
}

/// <summary>
/// Optional read-only simulation observer. Implementations may render progress
/// or pace wall-clock execution but must not feed data back into GameSimulation.
/// </summary>
public interface ISimulationProgressObserver
{
    void Observe(SimulationProgressUpdate update);
}
