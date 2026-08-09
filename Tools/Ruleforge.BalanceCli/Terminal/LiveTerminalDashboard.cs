using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using RuleforgeTD.BalanceCli.Simulation;

namespace RuleforgeTD.BalanceCli.Terminal;

/// <summary>
/// Rewrites one terminal screen with the latest simulation metrics. It never
/// appends per-tick log lines and automatically becomes silent when stdout is
/// redirected to a file or pipe.
/// </summary>
public sealed class LiveTerminalDashboard : ISimulationProgressObserver, IDisposable
{
    private const string ClearScreen = "\u001b[2J\u001b[H";
    private const string MoveHome = "\u001b[H";
    private const string ClearBelow = "\u001b[J";
    private const string HideCursor = "\u001b[?25l";
    private const string ShowCursor = "\u001b[?25h";

    private readonly TextWriter writer;
    private readonly int pacingTicksPerSecond;
    private readonly int refreshMilliseconds;
    private readonly bool interactive;
    private readonly Stopwatch wallClock = new();
    private long firstTick;
    private long lastRenderMilliseconds = long.MinValue;
    private bool started;
    private bool disposed;

    public LiveTerminalDashboard(
        int pacingTicksPerSecond,
        int refreshMilliseconds = 100,
        TextWriter? writer = null,
        bool? interactive = null)
    {
        if (pacingTicksPerSecond < 0 || pacingTicksPerSecond > 10000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pacingTicksPerSecond),
                "Pacing must be between 0 and 10000 ticks per second.");
        }
        if (refreshMilliseconds < 50 || refreshMilliseconds > 5000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshMilliseconds),
                "Refresh interval must be between 50 and 5000 milliseconds.");
        }

        this.pacingTicksPerSecond = pacingTicksPerSecond;
        this.refreshMilliseconds = refreshMilliseconds;
        this.writer = writer ?? Console.Out;
        this.interactive = interactive ?? IsInteractiveTerminal();
    }

    public bool IsInteractive => interactive;

    public void Observe(SimulationProgressUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!interactive)
        {
            return;
        }

        if (!started)
        {
            started = true;
            firstTick = update.Tick;
            wallClock.Start();
            writer.Write(HideCursor);
            writer.Write(ClearScreen);
        }

        Pace(update.Tick);
        long elapsedMilliseconds = wallClock.ElapsedMilliseconds;
        bool terminal = update.Outcome.HasValue;
        if (!terminal &&
            lastRenderMilliseconds != long.MinValue &&
            elapsedMilliseconds - lastRenderMilliseconds < refreshMilliseconds)
        {
            return;
        }

        writer.Write(MoveHome);
        writer.Write(BuildFrame(update, pacingTicksPerSecond));
        writer.Write(ClearBelow);
        writer.Flush();
        lastRenderMilliseconds = elapsedMilliseconds;
    }

    public static string BuildFrame(
        SimulationProgressUpdate update,
        int pacingTicksPerSecond)
    {
        ArgumentNullException.ThrowIfNull(update);
        string status = update.Outcome?.ToString().ToUpperInvariant() ??
            "RUNNING";
        string pace = pacingTicksPerSecond == 0
            ? "unthrottled"
            : update.TickRate > 0
                ? ((double)pacingTicksPerSecond / update.TickRate)
                    .ToString("0.##", CultureInfo.InvariantCulture) + "x"
                : pacingTicksPerSecond.ToString(
                    CultureInfo.InvariantCulture) + " ticks/s";
        string action = Sanitize(update.LastAction, 72);
        var frame = new StringBuilder(640);
        frame.AppendLine("RULEFORGE TD  |  LIVE SIMULATION  |  " + status);
        frame.AppendLine(new string('─', 68));
        frame.AppendLine(
            "Difficulty  " + update.DifficultyId +
            "    Policy  " + update.PolicyId);
        frame.AppendLine(
            "Seed        game " + update.GameSeed +
            " / policy " + update.PolicySeed);
        frame.AppendLine(
            "Phase       " + update.Phase +
            "    Wave  " + update.WaveNumber + " / " + update.TotalWaves);
        frame.AppendLine(
            "Time        " + FormatDuration(update.SimulatedTime) +
            "    Tick  " + update.Tick +
            "    Pace  " + pace);
        frame.AppendLine();
        frame.AppendLine(
            "Base HP     " + update.BaseHealth + " / " +
            update.StartingBaseHealth);
        frame.AppendLine(
            "Gold        " + update.Gold +
            "    Earned  " + update.GoldEarned +
            "    Spent  " + update.GoldSpent);
        frame.AppendLine(
            "Enemies     " + update.EnemiesAlive + " alive" +
            "    Killed  " + update.EnemiesKilled +
            "    Leaked  " + update.EnemiesLeaked);
        frame.AppendLine(
            "Combat      " + update.ActiveProjectiles + " projectiles" +
            "    " + update.ActiveStatuses + " statuses" +
            "    " + update.TowerCount + " towers");
        frame.AppendLine(
            "Damage      " + (update.TotalDamageMilli / 1000d).ToString(
                "N1",
                CultureInfo.InvariantCulture) +
            "    Decisions  " + update.Decisions);
        frame.AppendLine();
        frame.AppendLine("Last action  " + action);
        if (!string.IsNullOrWhiteSpace(update.Error))
        {
            frame.AppendLine("Error        " + Sanitize(update.Error, 72));
        }
        frame.AppendLine(new string('─', 68));
        frame.Append("Ctrl+C to stop. The screen is refreshed in place; no tick log is appended.");
        return frame.ToString();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        if (interactive && started)
        {
            writer.Write(ShowCursor);
            writer.WriteLine();
            writer.Flush();
        }
    }

    private void Pace(long tick)
    {
        if (pacingTicksPerSecond <= 0 || tick <= firstTick)
        {
            return;
        }

        double targetMilliseconds =
            (double)(tick - firstTick) * 1000d / pacingTicksPerSecond;
        double remaining = targetMilliseconds - wallClock.Elapsed.TotalMilliseconds;
        if (remaining > 0.5d)
        {
            Thread.Sleep((int)Math.Ceiling(remaining));
        }
    }

    private static bool IsInteractiveTerminal()
    {
        if (Console.IsOutputRedirected)
        {
            return false;
        }
        return !string.Equals(
            Environment.GetEnvironmentVariable("TERM"),
            "dumb",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        int totalMinutes = (int)duration.TotalMinutes;
        return totalMinutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
            duration.Seconds.ToString("00", CultureInfo.InvariantCulture) + "." +
            (duration.Milliseconds / 100).ToString(
                CultureInfo.InvariantCulture);
    }

    private static string Sanitize(string? value, int maximumLength)
    {
        string sanitized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized[..(maximumLength - 1)] + "…";
    }
}
