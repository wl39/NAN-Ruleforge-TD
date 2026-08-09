using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RuleforgeTD.BalanceCli.Infrastructure;

public static class JsonSupport
{
    public static readonly JsonSerializerOptions Options = CreateOptions();
    public static readonly JsonSerializerOptions StrictOptions =
        CreateOptions(disallowUnknownMembers: true);
    public static readonly JsonSerializerOptions CompactOptions =
        CreateOptions(writeIndented: false);

    public static T Read<T>(string path)
    {
        string json = File.ReadAllText(path, Encoding.UTF8);
        T? value = JsonSerializer.Deserialize<T>(json, Options);
        return value ?? throw new InvalidDataException(
            "JSON produced no value: " + path);
    }

    public static T ReadStrict<T>(string path)
    {
        string json = File.ReadAllText(path, Encoding.UTF8);
        T? value = JsonSerializer.Deserialize<T>(json, StrictOptions);
        return value ?? throw new InvalidDataException(
            "JSON produced no value: " + path);
    }

    public static void Write<T>(string path, T value)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, Options) + Environment.NewLine,
            new UTF8Encoding(false));
    }

    public static string SerializeStable<T>(T value) =>
        JsonSerializer.Serialize(value, CompactOptions);

    public static string Sha256Text(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static string Sha256File(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();

    private static JsonSerializerOptions CreateOptions(
        bool writeIndented = true,
        bool disallowUnknownMembers = false)
    {
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = false,
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = writeIndented,
            NumberHandling = JsonNumberHandling.Strict,
            UnmappedMemberHandling = disallowUnknownMembers
                ? JsonUnmappedMemberHandling.Disallow
                : JsonUnmappedMemberHandling.Skip
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
