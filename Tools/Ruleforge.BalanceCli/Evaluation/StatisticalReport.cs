using System;
using System.Collections.Generic;
using System.Linq;

namespace RuleforgeTD.BalanceCli.Evaluation;

public sealed record WilsonInterval(
    double Lower,
    double Upper,
    double ConfidenceLevel = 0.95);

public sealed class DistributionStatistics
{
    public int Count { get; set; }
    public double Mean { get; set; }
    public double Median { get; set; }
    public double P10 { get; set; }
    public double P90 { get; set; }
    public double Minimum { get; set; }
    public double Maximum { get; set; }
}

public sealed class CategoryWinStatistics
{
    public int Runs { get; set; }
    public int Wins { get; set; }
    public double SelectionRate { get; set; }
    public double WinRate { get; set; }
    public WilsonInterval WinRateWilson95 { get; set; } = new(0, 1);
}

public static class StatisticalMath
{
    public static WilsonInterval Wilson95(int successes, int sampleSize)
    {
        if (sampleSize < 0 || successes < 0 || successes > sampleSize)
        {
            throw new ArgumentOutOfRangeException(nameof(successes));
        }
        if (sampleSize == 0)
        {
            return new WilsonInterval(0, 1);
        }

        const double z = 1.959963984540054;
        double n = sampleSize;
        double p = successes / n;
        double z2 = z * z;
        double denominator = 1 + (z2 / n);
        double center = (p + (z2 / (2 * n))) / denominator;
        double radius = z * Math.Sqrt(
            (p * (1 - p) / n) + (z2 / (4 * n * n))) / denominator;
        return new WilsonInterval(
            Math.Max(0, center - radius),
            Math.Min(1, center + radius));
    }

    public static DistributionStatistics Describe(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        double[] sorted = values.OrderBy(value => value).ToArray();
        if (sorted.Length == 0)
        {
            return new DistributionStatistics();
        }
        return new DistributionStatistics
        {
            Count = sorted.Length,
            Mean = sorted.Average(),
            Median = Percentile(sorted, 0.5),
            P10 = Percentile(sorted, 0.1),
            P90 = Percentile(sorted, 0.9),
            Minimum = sorted[0],
            Maximum = sorted[^1]
        };
    }

    /// <summary>R-7 linear interpolation, also used by common data tools.</summary>
    public static double Percentile(IReadOnlyList<double> sortedValues, double p)
    {
        ArgumentNullException.ThrowIfNull(sortedValues);
        if (p is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(p));
        }
        if (sortedValues.Count == 0)
        {
            return 0;
        }
        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }
        double position = (sortedValues.Count - 1) * p;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        double fraction = position - lower;
        return sortedValues[lower] +
            ((sortedValues[upper] - sortedValues[lower]) * fraction);
    }
}
