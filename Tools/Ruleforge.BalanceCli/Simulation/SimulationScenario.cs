using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Core;

namespace RuleforgeTD.BalanceCli.Simulation;

public sealed class SimulationScenario
{
    public string ScenarioId { get; set; } = "standard";
    public string? ForcedStartingTowerId { get; set; }
    public string? ForcedPlacedTowerId { get; set; }
    public int? ForcedTowerLevel { get; set; }
    public bool ForcedTowerLevelIsMinimum { get; set; }
    public SubjectType? ForcedSubjectType { get; set; }
    public int? StartingGoldOverride { get; set; }
    /// <summary>
    /// Test-only content fixture that replaces the progress-triggered world
    /// card-pack threshold with one deterministic positive value. Production
    /// simulations leave this null and use the authoritative run data.
    /// </summary>
    public int? WorldCardPackProgressThresholdOverride { get; set; }
    /// <summary>
    /// Required inert ownership card for a card experiment. GameSimulation's
    /// run contract requires at least one starting card, so the experiment
    /// policy keeps this instance owned but unequipped in both baseline and
    /// candidate runs.
    /// </summary>
    public string? FixtureControlCardId { get; set; }
    /// <summary>
    /// Ordered card fixture that must be realized through public loadout
    /// commands. This data participates in the scenario hash; the content
    /// loader uses only <see cref="AdditionalStartingCards"/> for ownership.
    /// </summary>
    public List<SimulationCardFixtureSlot> FixtureCardProgram { get; set; } =
        new();
    /// <summary>
    /// Prevents regular drafts, boss card packs, and progress-triggered world
    /// card packs from changing the owned-card set during a fixed experiment.
    /// </summary>
    public bool DisableCardRewardChoices { get; set; }
    /// <summary>
    /// Test-only content fixture. When enabled, the run starts with only the
    /// cards listed in <see cref="AdditionalStartingCards"/>. The fixture is
    /// applied before GameSimulation.Initialize and cards are still equipped
    /// exclusively through normal GameCommand submissions.
    /// </summary>
    public bool ReplaceStartingCards { get; set; }
    public List<string> AdditionalStartingCards { get; set; } = new();
    public int MaximumLogicalTicks { get; set; } = 60000;
    public int MaximumDecisions { get; set; } = 200000;
    public bool CaptureReplay { get; set; } = true;
    public bool CaptureTelemetry { get; set; } = true;

    public static SimulationScenario Standard() => new();

    public SimulationScenario Clone()
    {
        return new SimulationScenario
        {
            ScenarioId = ScenarioId,
            ForcedStartingTowerId = ForcedStartingTowerId,
            ForcedPlacedTowerId = ForcedPlacedTowerId,
            ForcedTowerLevel = ForcedTowerLevel,
            ForcedTowerLevelIsMinimum = ForcedTowerLevelIsMinimum,
            ForcedSubjectType = ForcedSubjectType,
            StartingGoldOverride = StartingGoldOverride,
            WorldCardPackProgressThresholdOverride =
                WorldCardPackProgressThresholdOverride,
            FixtureControlCardId = FixtureControlCardId,
            FixtureCardProgram = FixtureCardProgram
                .ConvertAll(slot => slot.Clone()),
            DisableCardRewardChoices = DisableCardRewardChoices,
            ReplaceStartingCards = ReplaceStartingCards,
            AdditionalStartingCards = new List<string>(AdditionalStartingCards),
            MaximumLogicalTicks = MaximumLogicalTicks,
            MaximumDecisions = MaximumDecisions,
            CaptureReplay = CaptureReplay,
            CaptureTelemetry = CaptureTelemetry
        };
    }
}

public sealed class SimulationCardFixtureSlot
{
    public int Order { get; set; }
    public string CardId { get; set; } = string.Empty;
    public int SlotIndex { get; set; }
    public SubjectType SubjectType { get; set; }

    public SimulationCardFixtureSlot Clone() => new()
    {
        Order = Order,
        CardId = CardId,
        SlotIndex = SlotIndex,
        SubjectType = SubjectType
    };
}
