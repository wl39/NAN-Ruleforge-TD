using RuleforgeTD.BalanceCli.Commands;
using RuleforgeTD.BalanceCli.Infrastructure;

try
{
    return CommandDispatcher.Run(CliArguments.Parse(args));
}
catch (CliUsageException exception)
{
    Console.Error.WriteLine("USAGE: " + exception.Message);
    return ExitCodes.Usage;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        "ERROR " + exception.GetType().Name + ": " + exception.Message);
    return ExitCodes.SoftwareError;
}
