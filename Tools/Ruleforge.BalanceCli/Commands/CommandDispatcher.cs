using System;
using System.IO;
using System.Text.Json;
using RuleforgeTD.BalanceCli.Evaluation;
using RuleforgeTD.BalanceCli.Infrastructure;

namespace RuleforgeTD.BalanceCli.Commands;

internal static class ExitCodes
{
    public const int Success = 0;
    public const int SoftwareError = 1;
    public const int Usage = 2;
    public const int DataError = 3;
    public const int SimulationFailure = 4;
    public const int GateFailure = 5;
    public const int Cancelled = 130;
}

internal sealed class CliUsageException : Exception
{
    public CliUsageException(string message) : base(message)
    {
    }
}

internal static class CommandDispatcher
{
    public static int Run(CliArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        try
        {
            return RunCore(arguments);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("CANCELLED: operation was cancelled.");
            return ExitCodes.Cancelled;
        }
        catch (SeedSetValidationException exception)
        {
            Console.Error.WriteLine("DATA: " + exception.Message);
            return ExitCodes.DataError;
        }
        catch (JsonException exception)
        {
            Console.Error.WriteLine("DATA: " + exception.Message);
            return ExitCodes.DataError;
        }
        catch (InvalidDataException exception)
        {
            Console.Error.WriteLine("DATA: " + exception.Message);
            return ExitCodes.DataError;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine("DATA: " + exception.Message);
            return ExitCodes.DataError;
        }
        catch (FormatException exception)
        {
            Console.Error.WriteLine("USAGE: " + exception.Message);
            return ExitCodes.Usage;
        }
        catch (OverflowException exception)
        {
            Console.Error.WriteLine("USAGE: " + exception.Message);
            return ExitCodes.Usage;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine("USAGE: " + exception.Message);
            return ExitCodes.Usage;
        }
    }

    private static int RunCore(CliArguments arguments)
    {
        if (arguments.HasFlag("help") || arguments.HasFlag("h"))
        {
            HelpCommand.Print(arguments.Command);
            return ExitCodes.Success;
        }

        return arguments.Command switch
        {
            "help" => HelpCommand.Run(arguments),
            "simulate" => SimulationCommands.Simulate(arguments),
            "watch" => SimulationCommands.Simulate(arguments),
            "batch" => SimulationCommands.Batch(arguments),
            "evaluate" => EvaluationCommand.Run(arguments),
            "discover-cards" => DiscoveryCommands.DiscoverCards(arguments),
            "discover-synergies" =>
                DiscoveryCommands.DiscoverSynergies(arguments),
            "optimize" => OptimizationCommand.Run(arguments),
            "replay" => SimulationCommands.Replay(arguments),
            "verify" => VerificationCommand.Run(arguments),
            _ => throw new CliUsageException(
                "Unknown command '" + arguments.Command + "'.")
        };
    }
}
