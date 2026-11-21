namespace AstroPlanner.Testing;

/// <summary>
/// Timing budgets of the performance tests. The budgets are the spec's (§14), measured on a
/// developer machine; GitHub's shared runners (CI=true) are slower and noisier, so there each
/// budget gets <see cref="CiMargin"/> more. A real regression still fails on CI.
/// </summary>
internal static class PerformanceBudget
{
    public const double CiMargin = 1.5;

    public static bool IsCi => string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    public static TimeSpan Of(TimeSpan budget) => IsCi ? budget * CiMargin : budget;
}
