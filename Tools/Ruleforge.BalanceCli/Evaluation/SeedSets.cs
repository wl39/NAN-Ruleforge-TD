using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using RuleforgeTD.BalanceCli.Infrastructure;

namespace RuleforgeTD.BalanceCli.Evaluation;

public readonly record struct SeedPair(ulong GameSeed, ulong PolicySeed)
{
    public override string ToString() => GameSeed + ":" + PolicySeed;
}

public enum SeedSetKind
{
    Train = 0,
    Validation = 1,
    Holdout = 2
}

public sealed class SeedSetDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string Description { get; set; } = string.Empty;
    public List<SeedPair> Train { get; set; } = new();
    public List<SeedPair> Validation { get; set; } = new();
    public List<SeedPair> Holdout { get; set; } = new();

    public IReadOnlyList<SeedPair> Get(SeedSetKind kind) => kind switch
    {
        SeedSetKind.Train => Train,
        SeedSetKind.Validation => Validation,
        SeedSetKind.Holdout => Holdout,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public IReadOnlyList<SeedPair> Get(string name) =>
        Get(SeedSetLoader.ParseKind(name));
}

public sealed class SeedSetValidationException : Exception
{
    public SeedSetValidationException(IReadOnlyList<string> errors)
        : base("Invalid seed set document:\n" + string.Join("\n", errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

public static class SeedSetLoader
{
    public static SeedSetDocument Load(
        string path,
        IReadOnlyDictionary<SeedSetKind, int>? expectedCounts = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A seed set path is required.", nameof(path));
        }

        string json = File.ReadAllText(path);
        ValidateJsonShape(json);
        SeedSetDocument document = JsonSerializer.Deserialize<SeedSetDocument>(
            json,
            JsonSupport.Options) ?? throw new InvalidDataException(
            "JSON produced no seed set value: " + path);
        Validate(document, expectedCounts);
        return document;
    }

    public static void Validate(
        SeedSetDocument document,
        IReadOnlyDictionary<SeedSetKind, int>? expectedCounts = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<string>();
        if (document.SchemaVersion != 1)
        {
            errors.Add("schemaVersion must be 1.");
        }

        document.Train ??= new List<SeedPair>();
        document.Validation ??= new List<SeedPair>();
        document.Holdout ??= new List<SeedPair>();

        var seenPairs = new Dictionary<SeedPair, string>();
        var seenGameSeeds = new Dictionary<ulong, string>();
        var seenPolicySeeds = new Dictionary<ulong, string>();
        ValidateSet(
            SeedSetKind.Train,
            document.Train,
            expectedCounts,
            seenPairs,
            seenGameSeeds,
            seenPolicySeeds,
            errors);
        ValidateSet(
            SeedSetKind.Validation,
            document.Validation,
            expectedCounts,
            seenPairs,
            seenGameSeeds,
            seenPolicySeeds,
            errors);
        ValidateSet(
            SeedSetKind.Holdout,
            document.Holdout,
            expectedCounts,
            seenPairs,
            seenGameSeeds,
            seenPolicySeeds,
            errors);

        if (errors.Count > 0)
        {
            throw new SeedSetValidationException(
                new ReadOnlyCollection<string>(errors));
        }
    }

    public static SeedSetKind ParseKind(string name)
    {
        if (string.Equals(name, "train", StringComparison.OrdinalIgnoreCase))
        {
            return SeedSetKind.Train;
        }
        if (string.Equals(
                name,
                "validation",
                StringComparison.OrdinalIgnoreCase))
        {
            return SeedSetKind.Validation;
        }
        if (string.Equals(name, "holdout", StringComparison.OrdinalIgnoreCase))
        {
            return SeedSetKind.Holdout;
        }

        throw new ArgumentException(
            "Unknown seed set '" + name +
            "'. Expected train, validation, or holdout.",
            nameof(name));
    }

    private static void ValidateJsonShape(string json)
    {
        using JsonDocument parsed = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });
        JsonElement root = parsed.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Seed set root must be an object.");
        }
        var rootNames = new HashSet<string>(StringComparer.Ordinal);
        var allowedRootNames = new HashSet<string>(
            new[] { "schemaVersion", "description", "train", "validation", "holdout" },
            StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!rootNames.Add(property.Name) || !allowedRootNames.Contains(property.Name))
            {
                throw new InvalidDataException(
                    "Duplicate or unknown seed set property: " + property.Name);
            }
        }
        foreach (string required in new[]
                 {
                     "schemaVersion", "train", "validation", "holdout"
                 })
        {
            if (!rootNames.Contains(required))
            {
                throw new InvalidDataException(
                    "Missing required seed set property: " + required);
            }
        }
        foreach (string setName in new[] { "train", "validation", "holdout" })
        {
            JsonElement set = root.GetProperty(setName);
            if (set.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(setName + " must be an array.");
            }
            int index = 0;
            foreach (JsonElement pair in set.EnumerateArray())
            {
                if (pair.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException(
                        setName + "[" + index + "] must be an object.");
                }
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in pair.EnumerateObject())
                {
                    if (!names.Add(property.Name) ||
                        property.Name is not ("gameSeed" or "policySeed"))
                    {
                        throw new InvalidDataException(
                            "Duplicate or unknown property at " + setName + "[" +
                            index + "]: " + property.Name);
                    }
                    if (property.Value.ValueKind != JsonValueKind.Number ||
                        !property.Value.TryGetUInt64(out _))
                    {
                        throw new InvalidDataException(
                            setName + "[" + index + "]." + property.Name +
                            " must be an unsigned integer.");
                    }
                }
                if (!names.SetEquals(new[] { "gameSeed", "policySeed" }))
                {
                    throw new InvalidDataException(
                        setName + "[" + index +
                        "] must contain gameSeed and policySeed.");
                }
                index++;
            }
        }
    }

    private static void ValidateSet(
        SeedSetKind kind,
        IReadOnlyList<SeedPair> seeds,
        IReadOnlyDictionary<SeedSetKind, int>? expectedCounts,
        IDictionary<SeedPair, string> seenPairs,
        IDictionary<ulong, string> seenGameSeeds,
        IDictionary<ulong, string> seenPolicySeeds,
        ICollection<string> errors)
    {
        string setName = kind.ToString().ToLowerInvariant();
        if (seeds.Count == 0)
        {
            errors.Add(setName + " must contain at least one seed pair.");
        }
        if (expectedCounts != null &&
            expectedCounts.TryGetValue(kind, out int expected) &&
            seeds.Count != expected)
        {
            errors.Add(
                setName + " must contain exactly " + expected +
                " seed pairs, but contains " + seeds.Count + ".");
        }

        for (int index = 0; index < seeds.Count; index++)
        {
            SeedPair seed = seeds[index];
            string location = setName + "[" + index + "]";
            if (seed.GameSeed == 0)
            {
                errors.Add(location + ".gameSeed must be non-zero.");
            }
            if (seed.PolicySeed == 0)
            {
                errors.Add(location + ".policySeed must be non-zero.");
            }

            if (seenPairs.TryGetValue(seed, out string? priorPair))
            {
                errors.Add(
                    location + " duplicates seed pair from " + priorPair + ".");
            }
            else
            {
                seenPairs.Add(seed, location);
            }

            if (seenGameSeeds.TryGetValue(seed.GameSeed, out string? priorGame))
            {
                errors.Add(
                    location + ".gameSeed overlaps with " + priorGame + ".");
            }
            else
            {
                seenGameSeeds.Add(seed.GameSeed, location + ".gameSeed");
            }

            if (seenPolicySeeds.TryGetValue(
                    seed.PolicySeed,
                    out string? priorPolicy))
            {
                errors.Add(
                    location + ".policySeed overlaps with " + priorPolicy + ".");
            }
            else
            {
                seenPolicySeeds.Add(
                    seed.PolicySeed,
                    location + ".policySeed");
            }
        }
    }
}
