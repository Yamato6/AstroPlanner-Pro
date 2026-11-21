using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Tests;

/// <summary>M6b: predictions vs results (spec §8.7) and the user's calibration of the model.</summary>
public sealed class PredictionComparisonTests
{
    private static ObservationLogEntry Entry(FeasibilityVerdict verdict, int score, int rating, int day, bool photographed = true) =>
        new(Guid.NewGuid(), "M42", "Orion Nebula", new DateTime(2026, 1, day, 22, 0, 0), "", "", 3, 3, rating, "")
        {
            Kind = photographed ? LogEntryKind.Photographed : LogEntryKind.Observed,
            Prediction = new PredictionSummary(verdict, score, 20)
        };

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 50)]
    [InlineData(5, 100)]
    [InlineData(0, 0)]
    [InlineData(9, 100)]
    public void Ratings_MapOntoTheScoreScale(int rating, double score) =>
        PredictionComparison.RatingAsScore(rating).Should().Be(score);

    [Fact]
    public void Statistics_PerVerdict_AndOverall()
    {
        // Hand-computed: differences +10 (5★ vs 90), −10 (3★ vs 60), −15 (2★ vs 40), +0 (2★ vs 25 → 25).
        var stats = PredictionComparison.Compare(
        [
            Entry(FeasibilityVerdict.Excellent, 90, 5, 10),
            Entry(FeasibilityVerdict.Good, 60, 3, 11),
            Entry(FeasibilityVerdict.Marginal, 40, 2, 12),
            Entry(FeasibilityVerdict.Marginal, 25, 2, 13),
            Entry(FeasibilityVerdict.Good, 99, 1, 14, photographed: false)
        ]);

        stats.Points.Should().HaveCount(4);
        stats.Points.Should().BeInAscendingOrder(p => p.ObservedAtLocal);
        stats.MeanDifference.Should().BeApproximately((10 - 10 - 15 + 0) / 4.0, 1e-9);
        stats.ByVerdict.Select(v => v.Verdict).Should().Equal(FeasibilityVerdict.Excellent, FeasibilityVerdict.Good, FeasibilityVerdict.Marginal);
        var marginal = stats.ByVerdict[2];
        marginal.Count.Should().Be(2);
        marginal.MeanRating.Should().Be(2);
        marginal.MeanPredictedScore.Should().Be(32.5);
        marginal.MeanDifference.Should().Be(-7.5);

        // Pearson r of x = (90, 60, 40, 25) and y = (100, 50, 25, 25), computed separately: 0.9646.
        stats.Correlation.Should().BeApproximately(0.9646, 1e-4);
    }

    [Fact]
    public void Correlation_NeedsThreePointsWithSpread()
    {
        PredictionComparison.Compare([Entry(FeasibilityVerdict.Good, 60, 3, 1), Entry(FeasibilityVerdict.Good, 70, 4, 2)])
            .Correlation.Should().BeNull();
        PredictionComparison.Compare([Entry(FeasibilityVerdict.Good, 60, 3, 1), Entry(FeasibilityVerdict.Good, 70, 3, 2), Entry(FeasibilityVerdict.Good, 80, 3, 3)])
            .Correlation.Should().BeNull("the ratings do not vary");
        var empty = PredictionComparison.Compare([]);
        empty.Points.Should().BeEmpty();
        empty.MeanDifference.Should().BeNull();
    }

    [Fact]
    public void PredictionSummary_KeepsTheVerdictAndTheRecommendation()
    {
        var report = new FeasibilityReport
        {
            Verdict = FeasibilityVerdict.Good,
            Score = 64,
            Reasons = [],
            Framing = new FramingAssessment(FramingClass.Good, 100, 80, 0.5, 1),
            SignalToNoise = 18.5,
            Recommended = new RecommendedSettings(30, 800, null, 120, TimeSpan.FromHours(1), null, null)
        };

        PredictionSummary.From(report).Should().Be(new PredictionSummary(FeasibilityVerdict.Good, 64, 18.5) { SubSeconds = 30, SubCount = 120 });
        new CaptureDetails { SubSeconds = 30, SubCount = 120 }.TotalIntegration.Should().Be(TimeSpan.FromHours(1));
        new CaptureDetails { SubSeconds = 30 }.TotalIntegration.Should().BeNull();
    }

    [Fact]
    public void Tuning_OverridesOnlyWhatTheUserChanged()
    {
        var tuning = new FeasibilityTuning { SeeingArcsec = 1.8, SnrGood = 12 };

        var constants = tuning.Apply(FeasibilityConstants.Default with { ExtinctionCoefficient = 0.2 });

        constants.SeeingArcsec.Should().Be(1.8);
        constants.SnrGood.Should().Be(12);
        constants.SnrMarginal.Should().Be(FeasibilityConstants.Default.SnrMarginal);
        constants.ExtinctionCoefficient.Should().Be(0.2, "the site's extinction is not a tuning constant");
        tuning.IsDefault.Should().BeFalse();
        new FeasibilityTuning().IsDefault.Should().BeTrue();
        new FeasibilityTuning().Apply(FeasibilityConstants.Default).Should().Be(FeasibilityConstants.Default);
    }

    [Fact]
    public void Tuning_From_StoresDefaultsAsNull()
    {
        var tuning = FeasibilityTuning.From(FeasibilityConstants.Default with { OverheadSeconds = 5 });

        tuning.Should().Be(new FeasibilityTuning { OverheadSeconds = 5 });
        FeasibilityTuning.From(FeasibilityConstants.Default).IsDefault.Should().BeTrue();
    }
}
