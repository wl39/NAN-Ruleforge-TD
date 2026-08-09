using System;
using System.Collections.Generic;
using System.Globalization;

namespace RuleforgeTD.BalanceCli.Infrastructure;

public sealed class CliArguments
{
    private readonly Dictionary<string, string> values =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> flags =
        new(StringComparer.Ordinal);

    private CliArguments(string command)
    {
        Command = command;
    }

    public string Command { get; }

    public static CliArguments Parse(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            return new CliArguments("help");
        }

        var parsed = new CliArguments(args[0]);
        for (int index = 1; index < args.Length; index++)
        {
            string token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Unexpected argument: " + token);
            }

            string key = token[2..];
            if (index + 1 < args.Length &&
                !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                parsed.values[key] = args[++index];
            }
            else
            {
                parsed.flags.Add(key);
            }
        }

        return parsed;
    }

    public bool HasFlag(string key) => flags.Contains(key);

    public string Get(string key, string defaultValue) =>
        values.TryGetValue(key, out string? value) ? value : defaultValue;

    public string Require(string key) =>
        values.TryGetValue(key, out string? value)
            ? value
            : throw new ArgumentException("Missing required option --" + key + ".");

    public int GetInt(string key, int defaultValue) =>
        values.TryGetValue(key, out string? value)
            ? int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : defaultValue;

    public ulong GetUlong(string key, ulong defaultValue) =>
        values.TryGetValue(key, out string? value)
            ? ulong.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : defaultValue;

    public string? Optional(string key) =>
        values.TryGetValue(key, out string? value) ? value : null;
}
