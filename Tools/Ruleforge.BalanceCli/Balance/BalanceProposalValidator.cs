using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using RuleforgeTD.BalanceCli.Infrastructure;

namespace RuleforgeTD.BalanceCli.Balance;

public sealed record BalanceFieldConstraint(
    string JsonPointerPattern,
    double MaxPercentChange,
    long? MaxAbsoluteChange = null,
    long? MinimumValue = null,
    long? MaximumValue = null);

public sealed class AllowedBalanceFieldSet
{
    public AllowedBalanceFieldSet(IEnumerable<BalanceFieldConstraint> constraints)
    {
        Constraints = constraints?.ToArray() ??
            throw new ArgumentNullException(nameof(constraints));
    }

    public IReadOnlyList<BalanceFieldConstraint> Constraints { get; }
    public IReadOnlyList<string> JsonPointerPatterns => Constraints
        .Select(value => value.JsonPointerPattern)
        .ToArray();

    public bool TryMatch(
        string jsonPointer,
        out BalanceFieldConstraint? constraint)
    {
        constraint = Constraints.FirstOrDefault(candidate =>
            PointerPatternMatches(candidate.JsonPointerPattern, jsonPointer));
        return constraint != null;
    }

    public static AllowedBalanceFieldSet DifficultyProfiles { get; } = new(
        new[]
        {
            new BalanceFieldConstraint("/modifiers/startingGold", 10),
            new BalanceFieldConstraint("/modifiers/enemyHealthPermille", 10),
            new BalanceFieldConstraint("/modifiers/enemyArmorPermille", 10),
            new BalanceFieldConstraint("/modifiers/enemySpeedPermille", 10),
            new BalanceFieldConstraint("/modifiers/enemyResistancePermille", 10),
            new BalanceFieldConstraint("/modifiers/enemyCountPermille", 10),
            new BalanceFieldConstraint("/modifiers/spawnIntervalPermille", 15),
            new BalanceFieldConstraint("/modifiers/goldRewardPermille", 10),
            new BalanceFieldConstraint("/modifiers/towerBuildCostPermille", 10),
            new BalanceFieldConstraint("/modifiers/towerUpgradeCostPermille", 10),
            new BalanceFieldConstraint("/modifiers/bossAbilityIntervalPermille", 10),
            new BalanceFieldConstraint("/enemyOverrides/*/maxHealthMilli", 10),
            new BalanceFieldConstraint("/enemyOverrides/*/armor", 10),
            new BalanceFieldConstraint("/enemyOverrides/*/speedMilliPerTick", 10),
            new BalanceFieldConstraint("/enemyOverrides/*/rewardBudget", 10),
            new BalanceFieldConstraint("/enemyOverrides/*/fireResistanceBps", 10),
            new BalanceFieldConstraint("/enemyOverrides/*/poisonResistanceBps", 10),
            new BalanceFieldConstraint("/waveOverrides/*/spawns/*/count", 10, 2),
            new BalanceFieldConstraint("/waveOverrides/*/spawns/*/firstSpawnTick", 10),
            new BalanceFieldConstraint("/waveOverrides/*/spawns/*/intervalTicks", 15),
            new BalanceFieldConstraint("/bossOverrides/*/abilityIntervalTicks", 10),
            new BalanceFieldConstraint("/bossOverrides/*/enragedAbilityIntervalTicks", 10),
            new BalanceFieldConstraint("/bossOverrides/*/shieldBps", 10),
            new BalanceFieldConstraint("/bossOverrides/*/summonCount", 10, 2),
            new BalanceFieldConstraint("/bossOverrides/*/enragedSummonCount", 10, 2),
            new BalanceFieldConstraint("/bossOverrides/*/teleportDistanceBps", 10),
            new BalanceFieldConstraint("/bossOverrides/*/enragedTeleportDistanceBps", 10)
        });

    private static bool PointerPatternMatches(string pattern, string pointer)
    {
        string[] expected = SplitPointer(pattern);
        string[] actual = SplitPointer(pointer);
        if (expected.Length != actual.Length)
        {
            return false;
        }
        for (int index = 0; index < expected.Length; index++)
        {
            if (expected[index] != "*" &&
                !string.Equals(expected[index], actual[index], StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    internal static string[] SplitPointer(string pointer)
    {
        if (string.IsNullOrEmpty(pointer) || pointer[0] != '/')
        {
            return Array.Empty<string>();
        }
        return pointer.Split('/').Skip(1).Select(segment =>
            segment.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal)).ToArray();
    }
}

public sealed class BalanceProposalValidator
{
    private readonly AllowedBalanceFieldSet allowedFields;

    public BalanceProposalValidator(AllowedBalanceFieldSet? allowedFields = null)
    {
        this.allowedFields = allowedFields ??
            AllowedBalanceFieldSet.DifficultyProfiles;
    }

    public BalancePatchValidationResult Validate(
        DifficultyProfile source,
        BalancePatch patch)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(patch);
        var result = new BalancePatchValidationResult
        {
            ComputedSourceProfileHash = BalanceProfileHasher.Compute(source)
        };
        if (patch.SchemaVersion != 1)
        {
            result.Errors.Add("schemaVersion must be 1.");
        }
        if (string.IsNullOrWhiteSpace(patch.ProposalId))
        {
            result.Errors.Add("proposalId is required.");
        }
        if (!string.Equals(
                patch.Difficulty,
                source.DifficultyId,
                StringComparison.Ordinal))
        {
            result.Errors.Add("Patch difficulty must match the source profile.");
        }
        if (!string.Equals(
                patch.SourceProfileHash,
                result.ComputedSourceProfileHash,
                StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("sourceProfileHash does not match the source profile.");
        }
        if (patch.NeedsStructuralReview)
        {
            result.Errors.Add(
                "Structural proposals cannot be applied automatically.");
        }
        if (patch.Diagnosis.Count is < 1 or > 3)
        {
            result.Errors.Add("diagnosis must contain between one and three items.");
        }
        if (patch.Changes.Count is < 1 or > 5)
        {
            result.Errors.Add("changes must contain between one and five items.");
        }

        JsonNode root = JsonNode.Parse(JsonSupport.SerializeStable(source)) ??
            throw new InvalidOperationException("Source profile serialized to null.");
        var pointers = new HashSet<string>(StringComparer.Ordinal);
        foreach (BalanceChange change in patch.Changes)
        {
            if (!pointers.Add(change.JsonPointer))
            {
                result.Errors.Add("Duplicate jsonPointer: " + change.JsonPointer);
                continue;
            }
            if (!allowedFields.TryMatch(change.JsonPointer, out BalanceFieldConstraint? rule))
            {
                result.Errors.Add(
                    "Field is not approved for automatic balance changes: " +
                    change.JsonPointer);
                continue;
            }
            if (string.IsNullOrWhiteSpace(change.ReasonCode))
            {
                result.Errors.Add("reasonCode is required for " + change.JsonPointer + ".");
            }
            if (!TryResolve(root, change.JsonPointer, out JsonNode? current, out _))
            {
                result.Errors.Add(
                    "jsonPointer does not resolve in the source profile: " +
                    change.JsonPointer);
                continue;
            }
            if (!TryReadInteger(current, out long actualOld))
            {
                result.Errors.Add(
                    "Automatic balance fields must be integers: " +
                    change.JsonPointer);
                continue;
            }
            if (actualOld != change.OldValue)
            {
                result.Errors.Add(
                    "oldValue mismatch at " + change.JsonPointer +
                    "; expected " + actualOld + ", received " +
                    change.OldValue + ".");
                continue;
            }
            ValidateMagnitude(change, rule!, result);
        }
        return result;
    }

    public BalancePatchApplicationResult Apply(
        DifficultyProfile source,
        BalancePatch patch)
    {
        BalancePatchValidationResult validation = Validate(source, patch);
        if (!validation.IsValid)
        {
            throw new BalancePatchValidationException(validation);
        }

        JsonNode root = JsonNode.Parse(JsonSupport.SerializeStable(source))!;
        foreach (BalanceChange change in patch.Changes)
        {
            if (!TryResolve(root, change.JsonPointer, out _, out NodeLocation location))
            {
                throw new InvalidOperationException(
                    "Validated pointer became unresolved: " + change.JsonPointer);
            }
            location.Set(checked((int)change.NewValue));
        }
        DifficultyProfile candidate = root.Deserialize<DifficultyProfile>(
            JsonSupport.Options) ?? throw new InvalidOperationException(
            "Patched profile could not be deserialized.");
        DifficultyProfileValidator.Validate(candidate, source.DifficultyId);
        return new BalancePatchApplicationResult
        {
            Patch = patch,
            Source = source,
            Candidate = candidate,
            Validation = validation,
            CandidateProfileHash = BalanceProfileHasher.Compute(candidate)
        };
    }

    private static void ValidateMagnitude(
        BalanceChange change,
        BalanceFieldConstraint rule,
        BalancePatchValidationResult result)
    {
        if (change.NewValue is < int.MinValue or > int.MaxValue)
        {
            result.Errors.Add(
                change.JsonPointer + " is outside the supported integer range.");
            return;
        }
        if (change.NewValue < 0)
        {
            result.Errors.Add(
                change.JsonPointer + " cannot be negative.");
        }
        long absoluteDelta = Math.Abs(change.NewValue - change.OldValue);
        if (rule.MaxAbsoluteChange.HasValue &&
            absoluteDelta > rule.MaxAbsoluteChange.Value)
        {
            result.Errors.Add(
                change.JsonPointer + " changes by " + absoluteDelta +
                ", exceeding the absolute limit " +
                rule.MaxAbsoluteChange.Value + ".");
        }
        if (rule.MinimumValue.HasValue && change.NewValue < rule.MinimumValue ||
            rule.MaximumValue.HasValue && change.NewValue > rule.MaximumValue)
        {
            result.Errors.Add(change.JsonPointer + " is outside its allowed range.");
        }

        if (change.OldValue == 0)
        {
            if (change.NewValue != 0)
            {
                result.Errors.Add(
                    change.JsonPointer +
                    " cannot be changed automatically from a zero baseline.");
            }
            return;
        }
        double computed =
            (change.NewValue - change.OldValue) * 100.0 /
            Math.Abs((double)change.OldValue);
        if (Math.Abs(computed) > rule.MaxPercentChange + 1e-9)
        {
            result.Errors.Add(
                change.JsonPointer + " changes by " +
                computed.ToString("0.###", CultureInfo.InvariantCulture) +
                "%, exceeding the " + rule.MaxPercentChange + "% limit.");
        }
        if (change.ChangePercent.HasValue &&
            Math.Abs(change.ChangePercent.Value - computed) > 0.02)
        {
            result.Errors.Add(
                "changePercent does not match oldValue/newValue at " +
                change.JsonPointer + ".");
        }
    }

    private static bool TryReadInteger(JsonNode? node, out long value)
    {
        return long.TryParse(
            node?.ToJsonString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static bool TryResolve(
        JsonNode root,
        string pointer,
        out JsonNode? node,
        out NodeLocation location)
    {
        node = root;
        location = default;
        string[] segments = AllowedBalanceFieldSet.SplitPointer(pointer);
        if (segments.Length == 0)
        {
            return false;
        }
        for (int index = 0; index < segments.Length; index++)
        {
            string segment = segments[index];
            bool last = index == segments.Length - 1;
            if (node is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(segment, out JsonNode? child))
                {
                    return false;
                }
                if (last)
                {
                    node = child;
                    location = new NodeLocation(obj, segment, null, -1);
                    return true;
                }
                node = child;
            }
            else if (node is JsonArray array &&
                     int.TryParse(segment, out int arrayIndex) &&
                     arrayIndex >= 0 && arrayIndex < array.Count)
            {
                JsonNode? child = array[arrayIndex];
                if (last)
                {
                    node = child;
                    location = new NodeLocation(null, null, array, arrayIndex);
                    return true;
                }
                node = child;
            }
            else
            {
                return false;
            }
        }
        return false;
    }

    private readonly record struct NodeLocation(
        JsonObject? Object,
        string? Property,
        JsonArray? Array,
        int Index)
    {
        public void Set(int value)
        {
            if (Object != null && Property != null)
            {
                Object[Property] = value;
                return;
            }
            if (Array != null && Index >= 0)
            {
                Array[Index] = value;
                return;
            }
            throw new InvalidOperationException("JSON pointer has no writable target.");
        }
    }
}
