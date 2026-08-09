using System;
using System.Collections.Generic;
using RuleforgeTD.GameLogic.Content;

namespace RuleforgeTD.BalanceCli.Balance;

public sealed class DifficultyProfile
{
    public int SchemaVersion { get; set; } = 1;
    public string DifficultyId { get; set; } = string.Empty;
    public string BaseContentHash { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DifficultyModifiers Modifiers { get; set; } = new();
    public List<EnemyBalanceOverride> EnemyOverrides { get; set; } = new();
    public List<WaveBalanceOverride> WaveOverrides { get; set; } = new();
    public List<BossBalanceOverride> BossOverrides { get; set; } = new();
}

public sealed class DifficultyModifiers
{
    public int? StartingGold { get; set; }
    public int EnemyHealthPermille { get; set; } = 1000;
    public int EnemyArmorPermille { get; set; } = 1000;
    public int EnemySpeedPermille { get; set; } = 1000;
    public int EnemyResistancePermille { get; set; } = 1000;
    public int EnemyCountPermille { get; set; } = 1000;
    public int SpawnIntervalPermille { get; set; } = 1000;
    public int GoldRewardPermille { get; set; } = 1000;
    public int TowerBuildCostPermille { get; set; } = 1000;
    public int TowerUpgradeCostPermille { get; set; } = 1000;
    public int BossAbilityIntervalPermille { get; set; } = 1000;
}

public sealed class EnemyBalanceOverride
{
    public string EnemyId { get; set; } = string.Empty;
    public int? MaxHealthMilli { get; set; }
    public int? Armor { get; set; }
    public int? SpeedMilliPerTick { get; set; }
    public int? RewardBudget { get; set; }
    public int? FireResistanceBps { get; set; }
    public int? PoisonResistanceBps { get; set; }
}

public sealed class WaveBalanceOverride
{
    public string WaveId { get; set; } = string.Empty;
    public List<WaveSpawnBalanceOverride> Spawns { get; set; } = new();
}

public sealed class WaveSpawnBalanceOverride
{
    public string EnemyId { get; set; } = string.Empty;
    public int Occurrence { get; set; }
    public int? Count { get; set; }
    public int? FirstSpawnTick { get; set; }
    public int? IntervalTicks { get; set; }
}

public sealed class BossBalanceOverride
{
    public string EnemyId { get; set; } = string.Empty;
    public int? AbilityIntervalTicks { get; set; }
    public int? EnragedAbilityIntervalTicks { get; set; }
    public int? ShieldBps { get; set; }
    public int? SummonCount { get; set; }
    public int? EnragedSummonCount { get; set; }
    public int? TeleportDistanceBps { get; set; }
    public int? EnragedTeleportDistanceBps { get; set; }
}

public static class DifficultyProfileValidator
{
    private const int MinimumPermille = 500;
    private const int MaximumPermille = 1500;

    public static void Validate(DifficultyProfile profile, string expectedId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = new List<string>();
        if (profile.SchemaVersion != 1)
        {
            errors.Add("schemaVersion must be 1.");
        }
        if (!string.Equals(
                profile.DifficultyId,
                expectedId,
                StringComparison.Ordinal))
        {
            errors.Add(
                "difficultyId must match requested profile '" +
                expectedId + "'.");
        }
        if (string.IsNullOrWhiteSpace(profile.BaseContentHash))
        {
            errors.Add("baseContentHash is required.");
        }

        if (profile.Modifiers == null)
        {
            errors.Add("modifiers is required.");
        }
        if (profile.EnemyOverrides == null ||
            profile.WaveOverrides == null ||
            profile.BossOverrides == null)
        {
            errors.Add("override arrays cannot be null.");
        }

        DifficultyModifiers modifiers = profile.Modifiers ?? new();
        ValidatePermille(
            modifiers.EnemyHealthPermille,
            nameof(modifiers.EnemyHealthPermille),
            errors);
        ValidatePermille(
            modifiers.EnemyArmorPermille,
            nameof(modifiers.EnemyArmorPermille),
            errors);
        ValidatePermille(
            modifiers.EnemySpeedPermille,
            nameof(modifiers.EnemySpeedPermille),
            errors);
        ValidatePermille(
            modifiers.EnemyResistancePermille,
            nameof(modifiers.EnemyResistancePermille),
            errors);
        ValidatePermille(
            modifiers.EnemyCountPermille,
            nameof(modifiers.EnemyCountPermille),
            errors);
        ValidatePermille(
            modifiers.SpawnIntervalPermille,
            nameof(modifiers.SpawnIntervalPermille),
            errors);
        ValidatePermille(
            modifiers.GoldRewardPermille,
            nameof(modifiers.GoldRewardPermille),
            errors);
        ValidatePermille(
            modifiers.TowerBuildCostPermille,
            nameof(modifiers.TowerBuildCostPermille),
            errors);
        ValidatePermille(
            modifiers.TowerUpgradeCostPermille,
            nameof(modifiers.TowerUpgradeCostPermille),
            errors);
        ValidatePermille(
            modifiers.BossAbilityIntervalPermille,
            nameof(modifiers.BossAbilityIntervalPermille),
            errors);
        if (modifiers.StartingGold is < 0)
        {
            errors.Add("startingGold cannot be negative.");
        }

        ValidateUniqueIds(
            profile.EnemyOverrides ?? new List<EnemyBalanceOverride>(),
            item => item.EnemyId,
            "enemyOverrides",
            errors);
        ValidateUniqueIds(
            profile.WaveOverrides ?? new List<WaveBalanceOverride>(),
            item => item.WaveId,
            "waveOverrides",
            errors);
        ValidateUniqueIds(
            profile.BossOverrides ?? new List<BossBalanceOverride>(),
            item => item.EnemyId,
            "bossOverrides",
            errors);
        ValidateEnemyOverrides(
            profile.EnemyOverrides ?? new List<EnemyBalanceOverride>(),
            errors);
        ValidateWaveOverrides(
            profile.WaveOverrides ?? new List<WaveBalanceOverride>(),
            errors);
        ValidateBossOverrides(
            profile.BossOverrides ?? new List<BossBalanceOverride>(),
            errors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Invalid difficulty profile '" + expectedId + "':\n" +
                string.Join("\n", errors));
        }
    }

    public static void Apply(
        ContentCatalogDto catalog,
        DifficultyProfile profile)
    {
        DifficultyModifiers modifiers = profile.Modifiers;
        if (modifiers.StartingGold.HasValue)
        {
            catalog.run.startingGold = modifiers.StartingGold.Value;
        }

        foreach (EnemyDefinitionDto enemy in catalog.enemies)
        {
            enemy.maxHealthMilli = ScalePositive(
                enemy.maxHealthMilli,
                modifiers.EnemyHealthPermille);
            enemy.armor = ScaleNonNegative(
                enemy.armor,
                modifiers.EnemyArmorPermille);
            enemy.speedMilliPerTick = ScalePositive(
                enemy.speedMilliPerTick,
                modifiers.EnemySpeedPermille);
            enemy.rewardBudget = ScaleNonNegative(
                enemy.rewardBudget,
                modifiers.GoldRewardPermille);
            enemy.fireResistanceBps = Math.Clamp(
                ScaleNonNegative(
                    enemy.fireResistanceBps,
                    modifiers.EnemyResistancePermille),
                0,
                10000);
            enemy.poisonResistanceBps = Math.Clamp(
                ScaleNonNegative(
                    enemy.poisonResistanceBps,
                    modifiers.EnemyResistancePermille),
                0,
                10000);
            if (string.Equals(enemy.rank, "Boss", StringComparison.Ordinal))
            {
                enemy.bossAbilityIntervalTicks = ScalePositive(
                    enemy.bossAbilityIntervalTicks,
                    modifiers.BossAbilityIntervalPermille);
                enemy.bossEnragedAbilityIntervalTicks = ScalePositive(
                    enemy.bossEnragedAbilityIntervalTicks,
                    modifiers.BossAbilityIntervalPermille);
            }
        }

        foreach (TowerDefinitionDto tower in catalog.towers)
        {
            tower.constructionCost = ScaleNonNegative(
                tower.constructionCost,
                modifiers.TowerBuildCostPermille);
            foreach (TowerLevelBalanceDto level in tower.levels)
            {
                level.upgradeCost = ScaleNonNegative(
                    level.upgradeCost,
                    modifiers.TowerUpgradeCostPermille);
            }
        }

        foreach (WaveDefinitionDto wave in catalog.waves)
        {
            foreach (WaveSpawnDto spawn in wave.spawns)
            {
                spawn.count = ScalePositive(
                    spawn.count,
                    modifiers.EnemyCountPermille);
                spawn.intervalTicks = ScalePositive(
                    spawn.intervalTicks,
                    modifiers.SpawnIntervalPermille);
            }
        }

        ApplyEnemyOverrides(catalog, profile.EnemyOverrides);
        ApplyWaveOverrides(catalog, profile.WaveOverrides);
        ApplyBossOverrides(catalog, profile.BossOverrides);
    }

    private static void ApplyEnemyOverrides(
        ContentCatalogDto catalog,
        IEnumerable<EnemyBalanceOverride> overrides)
    {
        foreach (EnemyBalanceOverride patch in overrides)
        {
            EnemyDefinitionDto enemy = Array.Find(
                catalog.enemies,
                item => string.Equals(
                    item.id,
                    patch.EnemyId,
                    StringComparison.Ordinal)) ?? throw new InvalidOperationException(
                "Unknown enemy override id '" + patch.EnemyId + "'.");
            enemy.maxHealthMilli = patch.MaxHealthMilli ?? enemy.maxHealthMilli;
            enemy.armor = patch.Armor ?? enemy.armor;
            enemy.speedMilliPerTick =
                patch.SpeedMilliPerTick ?? enemy.speedMilliPerTick;
            enemy.rewardBudget = patch.RewardBudget ?? enemy.rewardBudget;
            enemy.fireResistanceBps =
                patch.FireResistanceBps ?? enemy.fireResistanceBps;
            enemy.poisonResistanceBps =
                patch.PoisonResistanceBps ?? enemy.poisonResistanceBps;
        }
    }

    private static void ApplyWaveOverrides(
        ContentCatalogDto catalog,
        IEnumerable<WaveBalanceOverride> overrides)
    {
        foreach (WaveBalanceOverride patch in overrides)
        {
            WaveDefinitionDto wave = Array.Find(
                catalog.waves,
                item => string.Equals(
                    item.id,
                    patch.WaveId,
                    StringComparison.Ordinal)) ?? throw new InvalidOperationException(
                "Unknown wave override id '" + patch.WaveId + "'.");
            foreach (WaveSpawnBalanceOverride spawnPatch in patch.Spawns)
            {
                int occurrence = 0;
                WaveSpawnDto? match = null;
                foreach (WaveSpawnDto spawn in wave.spawns)
                {
                    if (!string.Equals(
                            spawn.enemyId,
                            spawnPatch.EnemyId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (occurrence++ == spawnPatch.Occurrence)
                    {
                        match = spawn;
                        break;
                    }
                }
                if (match == null)
                {
                    throw new InvalidOperationException(
                        "Wave override did not find spawn '" +
                        spawnPatch.EnemyId + "' occurrence " +
                        spawnPatch.Occurrence + ".");
                }
                match.count = spawnPatch.Count ?? match.count;
                match.firstSpawnTick =
                    spawnPatch.FirstSpawnTick ?? match.firstSpawnTick;
                match.intervalTicks =
                    spawnPatch.IntervalTicks ?? match.intervalTicks;
            }
        }
    }

    private static void ApplyBossOverrides(
        ContentCatalogDto catalog,
        IEnumerable<BossBalanceOverride> overrides)
    {
        foreach (BossBalanceOverride patch in overrides)
        {
            EnemyDefinitionDto enemy = Array.Find(
                catalog.enemies,
                item => string.Equals(
                    item.id,
                    patch.EnemyId,
                    StringComparison.Ordinal)) ?? throw new InvalidOperationException(
                "Unknown boss override id '" + patch.EnemyId + "'.");
            if (!string.Equals(enemy.rank, "Boss", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Boss override target is not a boss: " + patch.EnemyId);
            }
            enemy.bossAbilityIntervalTicks =
                patch.AbilityIntervalTicks ?? enemy.bossAbilityIntervalTicks;
            enemy.bossEnragedAbilityIntervalTicks =
                patch.EnragedAbilityIntervalTicks ??
                enemy.bossEnragedAbilityIntervalTicks;
            enemy.bossShieldBps = patch.ShieldBps ?? enemy.bossShieldBps;
            enemy.bossSummonCount = patch.SummonCount ?? enemy.bossSummonCount;
            enemy.bossEnragedSummonCount =
                patch.EnragedSummonCount ?? enemy.bossEnragedSummonCount;
            enemy.bossTeleportDistanceBps =
                patch.TeleportDistanceBps ?? enemy.bossTeleportDistanceBps;
            enemy.bossEnragedTeleportDistanceBps =
                patch.EnragedTeleportDistanceBps ??
                enemy.bossEnragedTeleportDistanceBps;
        }
    }

    private static int ScalePositive(int value, int permille)
    {
        if (value <= 0)
        {
            return value;
        }
        return Math.Max(1, ScaleNonNegative(value, permille));
    }

    private static int ScaleNonNegative(int value, int permille)
    {
        long scaled = checked((long)Math.Max(0, value) * permille);
        return checked((int)((scaled + 500L) / 1000L));
    }

    private static void ValidatePermille(
        int value,
        string name,
        ICollection<string> errors)
    {
        if (value < MinimumPermille || value > MaximumPermille)
        {
            errors.Add(
                name + " must be between " + MinimumPermille +
                " and " + MaximumPermille + ".");
        }
    }

    private static void ValidateUniqueIds<T>(
        IEnumerable<T> values,
        Func<T, string> idSelector,
        string field,
        ICollection<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (T value in values)
        {
            string id = idSelector(value);
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
            {
                errors.Add(field + " contains an empty or duplicate id.");
            }
        }
    }

    private static void ValidateEnemyOverrides(
        IEnumerable<EnemyBalanceOverride> overrides,
        ICollection<string> errors)
    {
        foreach (EnemyBalanceOverride value in overrides)
        {
            string prefix = "enemyOverrides['" + value.EnemyId + "']";
            ValidatePositive(value.MaxHealthMilli, prefix + ".maxHealthMilli", errors);
            ValidateNonNegative(value.Armor, prefix + ".armor", errors);
            ValidatePositive(
                value.SpeedMilliPerTick,
                prefix + ".speedMilliPerTick",
                errors);
            ValidateNonNegative(value.RewardBudget, prefix + ".rewardBudget", errors);
            ValidateBasisPoints(
                value.FireResistanceBps,
                prefix + ".fireResistanceBps",
                errors);
            ValidateBasisPoints(
                value.PoisonResistanceBps,
                prefix + ".poisonResistanceBps",
                errors);
        }
    }

    private static void ValidateWaveOverrides(
        IEnumerable<WaveBalanceOverride> overrides,
        ICollection<string> errors)
    {
        foreach (WaveBalanceOverride value in overrides)
        {
            string prefix = "waveOverrides['" + value.WaveId + "']";
            if (value.Spawns == null)
            {
                errors.Add(prefix + ".spawns cannot be null.");
                continue;
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (WaveSpawnBalanceOverride spawn in value.Spawns)
            {
                if (string.IsNullOrWhiteSpace(spawn.EnemyId))
                {
                    errors.Add(prefix + " contains a spawn with an empty enemyId.");
                }
                if (spawn.Occurrence < 0)
                {
                    errors.Add(prefix + " spawn occurrence cannot be negative.");
                }
                string key = spawn.EnemyId + "\u001f" + spawn.Occurrence;
                if (!keys.Add(key))
                {
                    errors.Add(
                        prefix + " contains duplicate spawn key '" +
                        spawn.EnemyId + "#" + spawn.Occurrence + "'.");
                }
                ValidatePositive(spawn.Count, prefix + ".spawns.count", errors);
                ValidateNonNegative(
                    spawn.FirstSpawnTick,
                    prefix + ".spawns.firstSpawnTick",
                    errors);
                ValidatePositive(
                    spawn.IntervalTicks,
                    prefix + ".spawns.intervalTicks",
                    errors);
            }
        }
    }

    private static void ValidateBossOverrides(
        IEnumerable<BossBalanceOverride> overrides,
        ICollection<string> errors)
    {
        foreach (BossBalanceOverride value in overrides)
        {
            string prefix = "bossOverrides['" + value.EnemyId + "']";
            ValidatePositive(
                value.AbilityIntervalTicks,
                prefix + ".abilityIntervalTicks",
                errors);
            ValidatePositive(
                value.EnragedAbilityIntervalTicks,
                prefix + ".enragedAbilityIntervalTicks",
                errors);
            ValidateBasisPoints(value.ShieldBps, prefix + ".shieldBps", errors);
            ValidateNonNegative(value.SummonCount, prefix + ".summonCount", errors);
            ValidateNonNegative(
                value.EnragedSummonCount,
                prefix + ".enragedSummonCount",
                errors);
            ValidateBasisPoints(
                value.TeleportDistanceBps,
                prefix + ".teleportDistanceBps",
                errors);
            ValidateBasisPoints(
                value.EnragedTeleportDistanceBps,
                prefix + ".enragedTeleportDistanceBps",
                errors);
        }
    }

    private static void ValidatePositive(
        int? value,
        string field,
        ICollection<string> errors)
    {
        if (value.HasValue && value.Value <= 0)
        {
            errors.Add(field + " must be positive when specified.");
        }
    }

    private static void ValidateNonNegative(
        int? value,
        string field,
        ICollection<string> errors)
    {
        if (value.HasValue && value.Value < 0)
        {
            errors.Add(field + " cannot be negative.");
        }
    }

    private static void ValidateBasisPoints(
        int? value,
        string field,
        ICollection<string> errors)
    {
        if (value.HasValue && value.Value is < 0 or > 10000)
        {
            errors.Add(field + " must be between 0 and 10000.");
        }
    }
}
