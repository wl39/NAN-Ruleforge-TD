using System;
using System.Collections.Generic;
using RuleforgeTD.BalanceCli.Balance;
using RuleforgeTD.BalanceCli.Policies;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.BalanceCli.Simulation;

public enum SimulationOutcome
{
    Victory,
    Defeat,
    Timeout,
    Error
}

public sealed class SimulationRunRequest
{
    public string DifficultyId { get; set; } = "current";
    public DifficultyProfile? DifficultyProfileOverride { get; set; }
    public string PolicyId { get; set; } = "novice-random-spender";
    public ulong GameSeed { get; set; } = 1001;
    public ulong PolicySeed { get; set; } = 2001;
    public SimulationScenario Scenario { get; set; } =
        SimulationScenario.Standard();
    public string? ReplayOutputPath { get; set; }
    public string ArtifactDirectory { get; set; } = string.Empty;
    public bool WriteResult { get; set; } = true;
    public bool WriteReplay { get; set; } = true;
    public CardStrengthIndex CardStrength { get; set; } = new();
    public CardSynergyIndex CardSynergy { get; set; } = new();
    public IReadOnlyDictionary<string, string> PolicySettings { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, double> OracleActionScores { get; set; } =
        new Dictionary<string, double>(StringComparer.Ordinal);
}

public sealed class SimulationRunOutput
{
    public required SimulationResult Result { get; init; }
    public ReplayRecord? Replay { get; init; }
}

public sealed class SimulationResult
{
    public string RunId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string GameVersion { get; set; } = string.Empty;
    public string BaseContentHash { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string DifficultyId { get; set; } = string.Empty;
    public string DifficultyProfileHash { get; set; } = string.Empty;
    public string ScenarioId { get; set; } = string.Empty;
    public string ScenarioHash { get; set; } = string.Empty;
    public string PolicyId { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public ulong GameSeed { get; set; }
    public ulong PolicySeed { get; set; }
    public SimulationOutcome Result { get; set; }
    public RunPhase FinalRunPhase { get; set; }
    public int ClearedWaveCount { get; set; }
    public int FailedWave { get; set; }
    public int RemainingBaseHealth { get; set; }
    public int TotalLeakDamage { get; set; }
    public int LeakedEnemyCount { get; set; }
    public int GoldEarned { get; set; }
    public int GoldSpent { get; set; }
    public int GoldUnspent { get; set; }
    public int TowerBuildCount { get; set; }
    public int TowerUpgradeCount { get; set; }
    public int MidWaveTowerBuildCount { get; set; }
    public List<long> MidWaveBuildTicks { get; set; } = new();
    public string SelectedStartingTower { get; set; } = string.Empty;
    public List<string> SelectedCards { get; set; } = new();
    public List<EquippedCardRecord> EquippedCards { get; set; } = new();
    public List<string> CardOrder { get; set; } = new();
    public List<SubjectType> SlotSubjectTypes { get; set; } = new();
    public List<CardChoiceRecord> DraftChoices { get; set; } = new();
    public List<CardChoiceRecord> CardPackChoices { get; set; } = new();
    public int RejectedCommandCount { get; set; }
    public Dictionary<string, int> RejectedCommandReasons { get; set; } = new();
    public int SafetyLimitReachedCount { get; set; }
    public Dictionary<string, int> SafetyLimitReasons { get; set; } = new();
    public long TotalLogicalTicks { get; set; }
    public int TotalDecisions { get; set; }
    public string FinalStateHash { get; set; } = string.Empty;
    public string FinalSnapshotHash { get; set; } = string.Empty;
    public string ReplayPath { get; set; } = string.Empty;
    public string? Error { get; set; }
    public Dictionary<string, long> CardExecutionCount { get; set; } = new();
    public Dictionary<string, long> DamageByEnemyType { get; set; } = new();
    public Dictionary<string, int> LeaksByEnemyType { get; set; } = new();
    public Dictionary<string, int> TowerBuildsByDefinition { get; set; } = new();
    public Dictionary<string, long> StatusApplications { get; set; } = new();
    public int BossAbilityActivatedCount { get; set; }
    public int BossAbilityTelegraphedCount { get; set; }
    public int EnemyDeathCount { get; set; }
    public long TotalDamageMilli { get; set; }
    public List<MidWaveBuildRecord> MidWaveBuilds { get; set; } = new();
    public List<CommandLogEntry> Commands { get; set; } = new();
    public List<PhaseTransitionRecord> PhaseTransitions { get; set; } = new();
    public List<FinalTowerRecord> FinalTowers { get; set; } = new();
    public List<FinalCardRecord> FinalCards { get; set; } = new();
    public List<ReplayOperationRecord> Operations { get; set; } = new();
    public SimulationTelemetry Telemetry { get; set; } = new();
}

public sealed class EquippedCardRecord
{
    public int CardInstanceId { get; set; }
    public string CardId { get; set; } = string.Empty;
    public int TowerInstanceId { get; set; }
    public string TowerDefinitionId { get; set; } = string.Empty;
    public int SlotIndex { get; set; }
    public SubjectType SubjectType { get; set; }
}

public sealed class CardChoiceRecord
{
    public long Tick { get; set; }
    public int WaveNumber { get; set; }
    public int OfferIndex { get; set; }
    public string CardId { get; set; } = string.Empty;
}

public sealed class MidWaveBuildRecord
{
    public long MidWaveBuildTick { get; set; }
    public int GoldBeforeBuild { get; set; }
    public int GoldAfterBuild { get; set; }
    public string TowerDefinition { get; set; } = string.Empty;
    public int BuildSlot { get; set; }
    public int ThreatAtBuild { get; set; }
    public int EnemiesAliveAtBuild { get; set; }
    public int ExpectedLeakBeforeBuild { get; set; }
    public string ActualOutcomeAfterBuild { get; set; } = string.Empty;
}

public sealed class CommandLogEntry
{
    public int Sequence { get; set; }
    public long Tick { get; set; }
    public RunPhase Phase { get; set; }
    public string ActionId { get; set; } = string.Empty;
    public GameCommandType Type { get; set; }
    public string ContentId { get; set; } = string.Empty;
    public int PrimaryId { get; set; }
    public int SecondaryId { get; set; }
    public int TertiaryId { get; set; }
    public bool Accepted { get; set; }
    public CommandError Error { get; set; }
    public string Message { get; set; } = string.Empty;

    public GameCommand ToCommand()
    {
        return Type switch
        {
            GameCommandType.ChooseStartingTower =>
                GameCommand.ChooseStartingTower(ContentId),
            GameCommandType.PlaceTower =>
                GameCommand.PlaceTower(ContentId, PrimaryId),
            GameCommandType.EquipCard =>
                GameCommand.EquipCard(PrimaryId, SecondaryId, TertiaryId),
            GameCommandType.ReorderCard =>
                GameCommand.ReorderCard(PrimaryId, SecondaryId, TertiaryId),
            GameCommandType.MoveCard =>
                GameCommand.MoveCard(PrimaryId, SecondaryId, TertiaryId),
            GameCommandType.UnequipCard => GameCommand.UnequipCard(PrimaryId),
            GameCommandType.SelectDraft => GameCommand.SelectDraft(PrimaryId),
            GameCommandType.StartWave => GameCommand.StartWave(),
            GameCommandType.OpenCardPack => GameCommand.OpenCardPack(PrimaryId),
            GameCommandType.SelectCardPack =>
                GameCommand.SelectCardPack(PrimaryId),
            GameCommandType.ResumeCardPackCombat =>
                GameCommand.ResumeCardPackCombat(),
            GameCommandType.UpgradeTower => GameCommand.UpgradeTower(PrimaryId),
            GameCommandType.SetTowerSubjectType =>
                GameCommand.SetTowerSubjectType(
                    PrimaryId,
                    (SubjectType)SecondaryId),
            GameCommandType.SetTowerSlotSubjectType =>
                GameCommand.SetTowerSlotSubjectType(
                    PrimaryId,
                    SecondaryId,
                    (SubjectType)TertiaryId),
            GameCommandType.GrantDebugGold => throw new InvalidOperationException(
                "Balance replays reject GrantDebugGold."),
            _ => throw new InvalidOperationException(
                "Unsupported replay command type " + Type + ".")
        };
    }
}

public sealed class PhaseTransitionRecord
{
    public long Tick { get; set; }
    public RunPhase From { get; set; }
    public RunPhase To { get; set; }
}

public sealed class FinalTowerRecord
{
    public int TowerInstanceId { get; set; }
    public string DefinitionId { get; set; } = string.Empty;
    public int BuildPointIndex { get; set; }
    public int Level { get; set; }
    public List<int> CardInstanceIds { get; set; } = new();
    public List<SubjectType> SubjectTypes { get; set; } = new();
}

public sealed class FinalCardRecord
{
    public int CardInstanceId { get; set; }
    public string CardId { get; set; } = string.Empty;
    public int Level { get; set; }
    public bool Equipped { get; set; }
    public int TowerInstanceId { get; set; } = -1;
    public int SlotIndex { get; set; } = -1;
}

public enum ReplayOperationKind
{
    Command,
    Step
}

public sealed class ReplayOperationRecord
{
    public int Sequence { get; set; }
    public ReplayOperationKind Kind { get; set; }
    public string ActionId { get; set; } = string.Empty;
    public long TickBefore { get; set; }
    public RunPhase PhaseBefore { get; set; }
    public string StateHashBefore { get; set; } = string.Empty;
    public CommandLogEntry? Command { get; set; }
    public long TickAfter { get; set; }
    public RunPhase PhaseAfter { get; set; }
    public string StateHashAfter { get; set; } = string.Empty;
}

public sealed class ReplayRecord
{
    public int SchemaVersion { get; set; } = 2;
    public string RunId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string GameVersion { get; set; } = string.Empty;
    public string BaseContentHash { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string DifficultyId { get; set; } = string.Empty;
    public string DifficultyProfileHash { get; set; } = string.Empty;
    public string ScenarioHash { get; set; } = string.Empty;
    public string PolicyId { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public ulong GameSeed { get; set; }
    public ulong PolicySeed { get; set; }
    public SimulationScenario Scenario { get; set; } = new();
    public List<ReplayOperationRecord> Operations { get; set; } = new();
    public List<CommandLogEntry> Commands { get; set; } = new();
    public List<PhaseTransitionRecord> PhaseTransitions { get; set; } = new();
    /// <summary>
    /// Number of policy decisions completed before the driver stopped. Replay
    /// verification reconstructs this value from the command/Step operation
    /// stream before using it as maximum-decision timeout evidence.
    /// </summary>
    public int TotalDecisions { get; set; }
    public long FinalTick { get; set; }
    public SimulationOutcome Result { get; set; }
    public RunPhase FinalPhase { get; set; }
    public int RemainingBaseHealth { get; set; }
    public int FinalGold { get; set; }
    /// <summary>
    /// Original diagnostic only. Replay verification never trusts this string
    /// to derive an Error outcome because policy/host exceptions are not part of
    /// the authoritative GameCommand stream.
    /// </summary>
    public string? Error { get; set; }
    public string FinalStateHash { get; set; } = string.Empty;
    public string FinalSnapshotHash { get; set; } = string.Empty;
    public List<FinalTowerRecord> FinalTowers { get; set; } = new();
    public List<EquippedCardRecord> FinalCards { get; set; } = new();
    public List<FinalCardRecord> FinalCardStates { get; set; } = new();
}

public sealed class ReplayVerificationResult
{
    public bool Matches { get; set; }
    public bool Success
    {
        get => Matches;
        set => Matches = value;
    }
    public List<string> Mismatches { get; set; } = new();
    public List<string> Errors
    {
        get => Mismatches;
        set => Mismatches = value ?? new List<string>();
    }
    public string ReplayPath { get; set; } = string.Empty;
    public SimulationOutcome Result { get; set; }
    public RunPhase FinalPhase { get; set; }
    public long FinalTick { get; set; }
    public int RemainingBaseHealth { get; set; }
    public int FinalGold { get; set; }
    public string FinalStateHash { get; set; } = string.Empty;
    public string FinalSnapshotHash { get; set; } = string.Empty;
}
