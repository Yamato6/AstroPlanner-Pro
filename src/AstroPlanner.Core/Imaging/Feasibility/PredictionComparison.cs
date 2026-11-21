using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Imaging.Feasibility;

/// <summary>A photographed logbook entry: what the model predicted and how the user rated the result.</summary>
public sealed record PredictionPoint(Guid EntryId, string ObjectId, DateTime ObservedAtLocal, FeasibilityVerdict Verdict, int PredictedScore, int Rating)
{
    /// <summary>The rating on the score's 0–100 scale (1★ = 0 … 5★ = 100).</summary>
    public double RatingScore => PredictionComparison.RatingAsScore(Rating);

    /// <summary>Result minus prediction, in score points: positive = better than predicted.</summary>
    public double Difference => RatingScore - PredictedScore;
}

/// <summary>Predictions against results for one verdict.</summary>
/// <param name="MeanDifference">Mean of result minus prediction, in score points.</param>
public sealed record VerdictComparison(FeasibilityVerdict Verdict, int Count, double MeanRating, double MeanPredictedScore, double MeanDifference);

/// <summary>Everything the "Predictions vs results" view shows.</summary>
/// <param name="Correlation">Pearson correlation of predicted score and rating; null with fewer than 3 points or no spread.</param>
/// <param name="MeanDifference">Mean of result minus prediction over every point; null without points.</param>
public sealed record PredictionStatistics(
    IReadOnlyList<PredictionPoint> Points,
    IReadOnlyList<VerdictComparison> ByVerdict,
    double? Correlation,
    double? MeanDifference);

/// <summary>
/// Compares the feasibility model's predictions with the user's results (spec §8.7): the score
/// kept with each photographed entry against its 1–5 rating, mapped onto the same 0–100 scale.
/// It is how the user decides whether to recalibrate the model in Settings › Advanced.
/// </summary>
public static class PredictionComparison
{
    /// <summary>Points needed for a correlation.</summary>
    public const int MinimumPointsForCorrelation = 3;

    /// <summary>1★ → 0, 3★ → 50, 5★ → 100.</summary>
    public static double RatingAsScore(int rating) => (Math.Clamp(rating, 1, 5) - 1) * 25.0;

    public static PredictionStatistics Compare(IEnumerable<ObservationLogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var points = entries
            .Where(e => e.IsPhotographed && e.Prediction is not null)
            .OrderBy(e => e.ObservedAtLocal)
            .Select(e => new PredictionPoint(e.Id, e.ObjectId, e.ObservedAtLocal, e.Prediction!.Verdict, e.Prediction.Score, e.Rating))
            .ToList();

        var byVerdict = points
            .GroupBy(p => p.Verdict)
            .OrderByDescending(g => g.Key)
            .Select(g => new VerdictComparison(g.Key, g.Count(), g.Average(p => p.Rating), g.Average(p => p.PredictedScore),
                g.Average(p => p.Difference)))
            .ToList();

        return new PredictionStatistics(points, byVerdict, Correlation(points),
            points.Count > 0 ? points.Average(p => p.Difference) : null);
    }

    private static double? Correlation(List<PredictionPoint> points)
    {
        if (points.Count < MinimumPointsForCorrelation)
        {
            return null;
        }

        var meanX = points.Average(p => (double)p.PredictedScore);
        var meanY = points.Average(p => p.RatingScore);
        double covariance = 0, varianceX = 0, varianceY = 0;
        foreach (var point in points)
        {
            var dx = point.PredictedScore - meanX;
            var dy = point.RatingScore - meanY;
            covariance += dx * dy;
            varianceX += dx * dx;
            varianceY += dy * dy;
        }

        return varianceX > 0 && varianceY > 0 ? covariance / Math.Sqrt(varianceX * varianceY) : null;
    }
}
