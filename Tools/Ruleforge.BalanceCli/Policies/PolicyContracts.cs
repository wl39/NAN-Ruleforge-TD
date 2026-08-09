using System;
using System.Collections.Generic;
using System.Linq;
using RuleforgeTD.BalanceCli.Simulation;
using RuleforgeTD.GameLogic.Content;
using RuleforgeTD.GameLogic.Core;
using RuleforgeTD.GameLogic.Simulation;

namespace RuleforgeTD.BalanceCli.Policies;

public interface IPlayerPolicy
{
    string PolicyId { get; }
    string PolicyVersion { get; }
    PolicyDecision Decide(SimulationSnapshot snapshot, PolicyContext context);
}

public readonly record struct PolicyDecision(
    string ActionId,
    string ReasonCode);

public readonly record struct CardProgramStep(
    string CardId,
    SubjectType SubjectType,
    int SlotIndex);

public readonly record struct CardStrengthQuery(
    string DifficultyId,
    string TowerDefinitionId,
    SubjectType SubjectType,
    int SlotIndex,
    string CardId,
    int TowerLevel);

public interface ICardStrengthLookup
{
    double GetScore(in CardStrengthQuery query);
}

public sealed record CardSynergyQuery(
    string DifficultyId,
    string TowerDefinitionId,
    int TowerLevel,
    IReadOnlyList<CardProgramStep> OrderedProgram);

public interface ICardSynergyLookup
{
    double GetScore(CardSynergyQuery query);
}

public sealed class PolicyContext
{
    public required string DifficultyId { get; init; }
    public required SimulationScenario Scenario { get; init; }
    public required IReadOnlyList<LegalAction> LegalActions { get; init; }
    public required PublicGameKnowledge PublicKnowledge { get; init; }
    public required PolicyRandom Random { get; init; }
    public required PolicyMemory Memory { get; init; }
    public CardStrengthIndex CardStrength { get; init; } = new();
    public CardSynergyIndex CardSynergy { get; init; } = new();
    public IReadOnlyDictionary<string, string> Settings { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Optional one-ply scores produced by an external coordinator by cloning
    /// and replaying the real GameSimulation. Policies never receive those
    /// simulations or their private/random state.
    /// </summary>
    public IReadOnlyDictionary<string, double> OracleActionScores { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);

    public int IntSetting(string key, int fallback)
    {
        return Settings.TryGetValue(key, out string? raw) &&
            int.TryParse(
                raw,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int value)
            ? value
            : fallback;
    }
}

public sealed class PolicyMemory
{
    public RunPhase Phase { get; set; }
    public int DecisionsInPhase { get; set; }
    public int PlanningInitialGold { get; set; }
    public int PlanningGoldSpent { get; set; }
    public HashSet<string> AppliedOptimizations { get; } =
        new(StringComparer.Ordinal);
    public List<string> PublicActionHistory { get; } = new();
}

public sealed class PolicyRandom
{
    private ulong state;

    public PolicyRandom(ulong seed)
    {
        state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
    }

    public ulong State => state;

    public ulong NextUlong()
    {
        ulong value = state;
        value ^= value >> 12;
        value ^= value << 25;
        value ^= value >> 27;
        state = value;
        return value * 2685821657736338717UL;
    }

    public int NextInt(int exclusiveMaximum)
    {
        if (exclusiveMaximum <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        }
        return (int)(NextUlong() % (uint)exclusiveMaximum);
    }

    public T Choose<T>(IReadOnlyList<T> values)
    {
        if (values.Count == 0)
        {
            throw new InvalidOperationException("Cannot choose from an empty list.");
        }
        return values[NextInt(values.Count)];
    }
}

public sealed class PublicGameKnowledge
{
    private readonly Dictionary<string, CardKnowledge> cards =
        new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> cardIdsByDefinition = new();
    private readonly Dictionary<string, TowerKnowledge> towers =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, EnemyKnowledge> enemies =
        new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, CardKnowledge> Cards => cards;
    public IReadOnlyDictionary<string, TowerKnowledge> Towers => towers;
    public IReadOnlyDictionary<string, EnemyKnowledge> Enemies => enemies;
    public long PathLengthMilli { get; private set; }

    public CardKnowledge Card(string cardId) => cards[cardId];
    public CardKnowledge Card(CardId cardId) =>
        cards[cardIdsByDefinition[cardId.Value]];
    public string CardStableId(CardId cardId) =>
        cardIdsByDefinition[cardId.Value];
    public TowerKnowledge Tower(string towerId) => towers[towerId];
    public EnemyKnowledge Enemy(string enemyId) => enemies[enemyId];

    public static PublicGameKnowledge FromContent(CompiledContent content)
    {
        var knowledge = new PublicGameKnowledge();
        foreach (CompiledCardDefinition card in content.Cards)
        {
            knowledge.cards.Add(card.StableId, new CardKnowledge
            {
                CardId = card.StableId,
                Tier = (int)card.Tier,
                ComputeCost = card.ComputeCost,
                SlotCost = card.SlotCost,
                Tags = card.Tags,
                ProjectileOperations = card.ProjectileEffects
                    .Select(node => node.Operation)
                    .ToArray(),
                EnemyOperations = card.EnemyEffects
                    .Select(node => node.Operation)
                    .ToArray()
            });
            knowledge.cardIdsByDefinition.Add(
                card.Id.Value,
                card.StableId);
        }
        foreach (CompiledTowerDefinition tower in content.Towers)
        {
            knowledge.towers.Add(tower.StableId, new TowerKnowledge
            {
                TowerId = tower.StableId,
                Trigger = tower.Trigger,
                SubjectMode = tower.SubjectTypeMode,
                ConstructionCost = tower.ConstructionCost,
                LevelCount = tower.LevelCount
            });
        }
        foreach (CompiledEnemyDefinition enemy in content.Enemies)
        {
            knowledge.enemies.Add(enemy.StableId, new EnemyKnowledge
            {
                EnemyId = enemy.StableId,
                Rank = enemy.Rank,
                LeakDamage = enemy.LeakDamage
            });
        }
        knowledge.PathLengthMilli = ComputePathLength(
            content.Run.PathPoints);
        return knowledge;
    }

    private static long ComputePathLength(IReadOnlyList<SimPosition> points)
    {
        ulong length = 0;
        for (int index = 1; index < points.Count; index++)
        {
            ulong segmentSquared = points[index - 1]
                .DistanceSquaredRaw(points[index]);
            ulong segment = IntegerSquareRoot(segmentSquared);
            length = ulong.MaxValue - length < segment
                ? ulong.MaxValue
                : length + segment;
        }
        return length > long.MaxValue ? long.MaxValue : (long)length;
    }

    private static ulong IntegerSquareRoot(ulong value)
    {
        ulong result = 0;
        ulong bit = 1UL << 62;
        while (bit > value)
        {
            bit >>= 2;
        }
        while (bit != 0)
        {
            if (value >= result + bit)
            {
                value -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }
            bit >>= 2;
        }
        return result;
    }
}

public sealed class CardKnowledge
{
    public string CardId { get; set; } = string.Empty;
    public int Tier { get; set; }
    public int ComputeCost { get; set; }
    public int SlotCost { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public EffectOperation[] ProjectileOperations { get; set; } =
        Array.Empty<EffectOperation>();
    public EffectOperation[] EnemyOperations { get; set; } =
        Array.Empty<EffectOperation>();

    public bool CreatesAdditionalSubjects(SubjectType subject) =>
        Operations(subject).Any(operation => operation is
            EffectOperation.Split or
            EffectOperation.DuplicateProjectile or
            EffectOperation.DuplicateEnemy or
            EffectOperation.CreateAfterimageProjectile or
            EffectOperation.CreateProjectileTimeRift or
            EffectOperation.CreateProjectileMirrorWorld);

    public IReadOnlyList<EffectOperation> Operations(SubjectType subject) =>
        subject == SubjectType.Projectile
            ? ProjectileOperations
            : EnemyOperations;
}

public sealed class TowerKnowledge
{
    public string TowerId { get; set; } = string.Empty;
    public TowerTrigger Trigger { get; set; }
    public SubjectTypeMode SubjectMode { get; set; }
    public int ConstructionCost { get; set; }
    public int LevelCount { get; set; }
}

public sealed class EnemyKnowledge
{
    public string EnemyId { get; set; } = string.Empty;
    public EnemyRank Rank { get; set; }
    public int LeakDamage { get; set; }
}

public sealed class CardStrengthIndex
{
    public int SchemaVersion { get; set; } = 1;
    public string ContentHash { get; set; } = string.Empty;
    public List<CardStrengthEntry> Entries { get; set; } = new();

    public double Score(
        string difficulty,
        string cardId,
        string? towerId = null,
        SubjectType? subject = null,
        int? slot = null,
        int? towerLevel = null)
    {
        IEnumerable<CardStrengthEntry> candidates = Entries.Where(entry =>
            string.Equals(entry.Difficulty, difficulty, StringComparison.Ordinal) &&
            string.Equals(entry.CardId, cardId, StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(towerId))
        {
            candidates = candidates.Where(entry =>
                string.Equals(
                    entry.TowerDefinition,
                    towerId,
                    StringComparison.Ordinal));
        }
        if (subject.HasValue)
        {
            candidates = candidates.Where(entry => entry.SubjectType == subject);
        }
        if (slot.HasValue)
        {
            candidates = candidates.Where(entry => entry.SlotIndex == slot.Value);
        }
        if (towerLevel.HasValue)
        {
            candidates = candidates.Where(entry =>
                entry.TowerLevel == towerLevel.Value);
        }
        CardStrengthEntry? best = candidates
            .OrderByDescending(entry => entry.CompositeLift)
            .FirstOrDefault();
        return best?.CompositeLift ?? double.NaN;
    }
}

public sealed class CardStrengthEntry
{
    public string Difficulty { get; set; } = string.Empty;
    public string TowerDefinition { get; set; } = string.Empty;
    public SubjectType SubjectType { get; set; }
    public int SlotIndex { get; set; }
    public string CardId { get; set; } = string.Empty;
    public int TowerLevel { get; set; }
    public int SampleSize { get; set; }
    public double BaselineWinRate { get; set; }
    public double CardWinRate { get; set; }
    public double WinRateLift { get; set; }
    public double RemainingHealthLift { get; set; }
    public double ClearedWaveLift { get; set; }
    public double LeakReduction { get; set; }
    public double GoldEfficiencyLift { get; set; }
    public double CompositeLift { get; set; }
    public bool ViablePath { get; set; }
}

public sealed class CardSynergyIndex
{
    public int SchemaVersion { get; set; } = 1;
    public string ContentHash { get; set; } = string.Empty;
    public List<CardSynergyEntry> Entries { get; set; } = new();

    public double Score(
        string difficulty,
        string firstCard,
        SubjectType firstSubject,
        string secondCard,
        SubjectType secondSubject,
        string? towerId = null,
        int? towerLevel = null,
        int? firstSlot = null,
        int? secondSlot = null)
    {
        CardSynergyEntry? best = Entries
            .Where(entry =>
                string.Equals(entry.Difficulty, difficulty, StringComparison.Ordinal) &&
                string.Equals(entry.FirstCardId, firstCard, StringComparison.Ordinal) &&
                entry.FirstSubjectType == firstSubject &&
                string.Equals(entry.SecondCardId, secondCard, StringComparison.Ordinal) &&
                entry.SecondSubjectType == secondSubject &&
                (string.IsNullOrEmpty(towerId) ||
                 string.Equals(entry.TowerDefinition, towerId, StringComparison.Ordinal)) &&
                (!towerLevel.HasValue || entry.TowerLevel == towerLevel.Value) &&
                (!firstSlot.HasValue ||
                 entry.FirstSlotIndex == firstSlot.Value) &&
                (!secondSlot.HasValue ||
                 entry.SecondSlotIndex == secondSlot.Value))
            .OrderByDescending(entry => entry.SynergyLift)
            .FirstOrDefault();
        return best?.SynergyLift ?? double.NaN;
    }

    public double ScoreProgram(
        string difficulty,
        string towerId,
        int towerLevel,
        IReadOnlyList<CardProgramStep> orderedProgram)
    {
        double score = 0d;
        bool found = false;
        for (int first = 0; first < orderedProgram.Count; first++)
        {
            for (int second = first + 1;
                 second < orderedProgram.Count;
                 second++)
            {
                CardProgramStep a = orderedProgram[first];
                CardProgramStep b = orderedProgram[second];
                foreach (CardSynergyEntry entry in Entries)
                {
                    if (!MatchesPrefix(
                            entry,
                            difficulty,
                            towerId,
                            towerLevel,
                            a,
                            b))
                    {
                        continue;
                    }

                    if (!entry.IsTriple)
                    {
                        score += entry.SynergyLift;
                        found = true;
                        continue;
                    }

                    for (int third = second + 1;
                         third < orderedProgram.Count;
                         third++)
                    {
                        CardProgramStep c = orderedProgram[third];
                        if (string.Equals(
                                entry.ThirdCardId,
                                c.CardId,
                                StringComparison.Ordinal) &&
                            entry.ThirdSubjectType == c.SubjectType)
                        {
                            score += entry.SynergyLift;
                            found = true;
                        }
                    }
                }
            }
        }
        return found ? score : double.NaN;
    }

    private static bool MatchesPrefix(
        CardSynergyEntry entry,
        string difficulty,
        string towerId,
        int towerLevel,
        CardProgramStep first,
        CardProgramStep second)
    {
        return string.Equals(
                   entry.Difficulty,
                   difficulty,
                   StringComparison.Ordinal) &&
            string.Equals(
                entry.TowerDefinition,
                towerId,
                StringComparison.Ordinal) &&
            entry.TowerLevel == towerLevel &&
            string.Equals(
                entry.FirstCardId,
                first.CardId,
                StringComparison.Ordinal) &&
            entry.FirstSubjectType == first.SubjectType &&
            entry.FirstSlotIndex == first.SlotIndex &&
            string.Equals(
                entry.SecondCardId,
                second.CardId,
                StringComparison.Ordinal) &&
            entry.SecondSubjectType == second.SubjectType &&
            entry.SecondSlotIndex == second.SlotIndex;
    }
}

public sealed class CardSynergyEntry
{
    public string Difficulty { get; set; } = string.Empty;
    public string TowerDefinition { get; set; } = string.Empty;
    public string FirstCardId { get; set; } = string.Empty;
    public SubjectType FirstSubjectType { get; set; }
    public string SecondCardId { get; set; } = string.Empty;
    public SubjectType SecondSubjectType { get; set; }
    public int FirstSlotIndex { get; set; }
    public int SecondSlotIndex { get; set; }
    public int TowerLevel { get; set; }
    public int SampleSize { get; set; }
    public double BaselineScore { get; set; }
    public double FirstOnlyScore { get; set; }
    public double SecondOnlyScore { get; set; }
    public double CombinedScore { get; set; }
    public double ExpectedAdditiveScore { get; set; }
    public double SynergyLift { get; set; }
    public bool IsTriple { get; set; }
    public string? ThirdCardId { get; set; }
    public SubjectType? ThirdSubjectType { get; set; }
}
