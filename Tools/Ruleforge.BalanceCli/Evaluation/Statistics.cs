using System;
using System.Collections.Generic;
using System.Linq;

namespace RuleforgeTD.BalanceCli.Evaluation;

public readonly record struct WilsonInterval95(double Lower, double Upper);

public sealed class NumericSummary
{
    public int Count { get; set; }
    public double Sum { get; set; }
    public double Mean { get; set; }
    public double Median { get; set; }
    public double P10 { get; set; }
    public double P90 { get; set; }
    public double Minimum { get; set; }
    public double Maximum { get; set; }
}

/// <summary>
/// Additional allocation-friendly helpers retained for callers that need a
/// sum-bearing summary. Batch reporting uses the canonical Statistics type in
/// StatisticalReport.cs.
/// </summary>
public static class ExtendedStatistics
{
    private const double Wilson95Z = 1.959963984540054;

    public static NumericSummary Summarize(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        double[] sorted = values.OrderBy(value => value).ToArray();
        for (int index = 0; index < sorted.Length; index++)
        {
            if (double.IsNaN(sorted[index]) ||
                double.IsInfinity(sorted[index]))
            {
                throw new ArgumentException(
                    "Statistics cannot summarize NaN or infinity.",
                    nameof(values));
            }
        }

        if (sorted.Length == 0)
        {
            return new NumericSummary();
        }

        return new NumericSummary
        {
            Count = sorted.Length,
            Sum = sorted.Sum(),
            Mean = sorted.Average(),
            Median = PercentileSorted(sorted, 0.50),
            P10 = PercentileSorted(sorted, 0.10),
            P90 = PercentileSorted(sorted, 0.90),
            Minimum = sorted[0],
            Maximum = sorted[^1]
        };
    }

    public static double Percentile(
        IEnumerable<double> values,
        double probability)
    {
        ArgumentNullException.ThrowIfNull(values);
        ValidateProbability(probability);
        double[] sorted = values.OrderBy(value => value).ToArray();
        return sorted.Length == 0
            ? 0.0
            : PercentileSorted(sorted, probability);
    }

    public static WilsonInterval95 Wilson95(int successes, int trials)
    {
        if (trials < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(trials));
        }
        if (successes < 0 || successes > trials)
        {
            throw new ArgumentOutOfRangeException(nameof(successes));
        }
        if (trials == 0)
        {
            return new WilsonInterval95(0.0, 0.0);
        }

        double sampleSize = trials;
        double proportion = successes / sampleSize;
        double zSquared = Wilson95Z * Wilson95Z;
        double denominator = 1.0 + zSquared / sampleSize;
        double center =
            (proportion + zSquared / (2.0 * sampleSize)) /
            denominator;
        double margin =
            Wilson95Z * Math.Sqrt(
                proportion * (1.0 - proportion) / sampleSize +
                zSquared / (4.0 * sampleSize * sampleSize)) /
            denominator;
        return new WilsonInterval95(
            Math.Max(0.0, center - margin),
            Math.Min(1.0, center + margin));
    }

    private static double PercentileSorted(
        IReadOnlyList<double> sorted,
        double probability)
    {
        ValidateProbability(probability);
        if (sorted.Count == 0)
        {
            return 0.0;
        }
        if (sorted.Count == 1)
        {
            return sorted[0];
        }

        double position = (sorted.Count - 1) * probability;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }

        double fraction = position - lower;
        return sorted[lower] +
            (sorted[upper] - sorted[lower]) * fraction;
    }

    private static void ValidateProbability(double probability)
    {
        if (double.IsNaN(probability) ||
            probability < 0.0 ||
            probability > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(probability));
        }
    }
}
