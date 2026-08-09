using System;
using RuleforgeTD.BalanceCli.Infrastructure;

namespace RuleforgeTD.BalanceCli.Commands;

internal static class HelpCommand
{
    public static int Run(CliArguments arguments)
    {
        Print(arguments.Optional("command"));
        return ExitCodes.Success;
    }

    public static void Print(string? command = null)
    {
        if (!string.IsNullOrWhiteSpace(command) && command != "help")
        {
            PrintCommand(command);
            return;
        }

        Console.WriteLine(
            "Ruleforge TD deterministic balance CLI\n\n" +
            "Usage:\n" +
            "  Ruleforge.BalanceCli <command> [options]\n\n" +
            "Commands:\n" +
            "  simulate             Run one authoritative simulation\n" +
            "  watch                Run one simulation with a live dashboard\n" +
            "  batch                Run a policy over a named seed set\n" +
            "  evaluate             Evaluate frozen difficulty gates\n" +
            "  discover-cards       Measure matched-seed single-card lift\n" +
            "  discover-synergies   Measure ordered pair/triple lift\n" +
            "  optimize             Search bounded profile candidates\n" +
            "  replay               Replay recorded commands and Step calls\n" +
            "  verify               Validate data, policies and determinism\n" +
            "  help                 Show this help\n\n" +
            "Global option:\n" +
            "  --repo <path>         Explicit repository root\n\n" +
            "Exit codes:\n" +
            "  0 completed, 1 internal error, 2 usage error, 3 data error,\n" +
            "  4 simulation failure, 5 failed gate/replay, 130 cancelled\n\n" +
            "Use '<command> --help' for every supported option.");
    }

    private static void PrintCommand(string command)
    {
        string text = command switch
        {
            "simulate" =>
                "simulate [--difficulty current] [--policy novice-random-spender]\n" +
                "    [--game-seed 1001] [--policy-seed 2001]\n" +
                "    [--scenario-id id] [--starting-tower id] [--placed-tower id]\n" +
                "    [--subject Projectile|Enemy] [--max-ticks N] [--max-decisions N]\n" +
                "    [--card-strength file] [--card-synergy file]\n" +
                "    [--card-strength-easy file] [--card-strength-medium file]\n" +
                "    [--card-strength-hard file] [--card-synergy-hard file]\n" +
                "    [--output file] [--replay file] [--no-replay]\n" +
                "    [--live] [--ticks-per-second 120] [--refresh-ms 100]",
            "watch" =>
                "watch [--difficulty current] [--policy novice-random-spender]\n" +
                "    [--game-seed 1001] [--policy-seed 2001]\n" +
                "    [--ticks-per-second 120] [--refresh-ms 100]\n" +
                "    [--max-ticks N] [--max-decisions N]\n" +
                "    [--output file] [--replay file] [--no-replay]",
            "batch" =>
                "batch [--difficulty current] [--policy novice-ensemble]\n" +
                "    [--seed-set validation] [--limit N] [--replays]\n" +
                "    [--card-strength file] [--card-synergy file]\n" +
                "    [--scenario-id id] [--max-ticks N] [--max-decisions N]\n" +
                "    [--output-dir path]",
            "evaluate" =>
                "evaluate [--all-difficulties | --difficulty id]\n" +
                "    [--seed-set validation] [--limit N]\n" +
                "    [--card-strength file] [--card-synergy file]\n" +
                "    [--card-strength-easy file] [--card-strength-medium file]\n" +
                "    [--card-strength-hard file] [--card-synergy-hard file]\n" +
                "    [--card-coverage-easy file]\n" +
                "    [--strict-indices] [--minimum-index-samples n]\n" +
                "    [--allow-bootstrap-indices]\n" +
                "    [--max-ticks N] [--max-decisions N] [--output-dir path]",
            "discover-cards" =>
                "discover-cards [--difficulty medium] [--seed-set train]\n" +
                "    [--tower id] [--subject Projectile|Enemy] [--limit N]\n" +
                "    [--max-cards N] [--all-contexts]\n" +
                "    [--coverage | --coverage-only]\n" +
                "    [--max-ticks N] [--max-decisions N]\n" +
                "    [--allow-unexecuted]\n" +
                "    [--output-dir path]",
            "discover-synergies" =>
                "discover-synergies [--difficulty hard] [--seed-set train]\n" +
                "    [--tower id] [--subject Projectile|Enemy] [--limit N]\n" +
                "    [--max-cards 8] [--pair-limit 128]\n" +
                "    [--card-strength file] [--cards id1,id2,...]\n" +
                "    [--pair-enumeration-limit 20000]\n" +
                "    [--triples] [--third-card-limit 8]\n" +
                "    [--triple-pair-beam 8] [--triple-limit 32]\n" +
                "    [--max-ticks N] [--max-decisions N]\n" +
                "    [--allow-unexecuted]\n" +
                "    [--output-dir path]",
            "optimize" =>
                "optimize [--difficulty hard] [--policy id]\n" +
                "    [--seed-set train] [--validation-seed-set validation]\n" +
                "    [--limit N] [--candidate-limit 6] [--step-percent 5]\n" +
                "    [--card-strength file] [--card-synergy file]\n" +
                "    [--max-ticks N] [--max-decisions N]\n" +
                "    [--apply-approved] [--require-approval] [--output-dir path]",
            "replay" => "replay --replay <file> [--output file]",
            "verify" =>
                "verify [--game-seed 1001] [--policy-seed 2001]\n" +
                "    [--output file]",
            _ => throw new CliUsageException("Unknown command '" + command + "'.")
        };
        Console.WriteLine(
            "Usage:\n  Ruleforge.BalanceCli " + text +
            "\n\nAll relative paths are resolved from --repo (or the discovered root)." );
    }
}
