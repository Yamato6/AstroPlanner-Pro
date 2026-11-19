using AstroPlanner.Core.Algorithms;

namespace AstroPlanner.Core.Tests;

public sealed class TimeZoneConversionTests
{
    [Fact]
    public void ToUtc_MexicoCity_AppliesFixedOffsetWithoutDaylightSaving()
    {
        // Mexico dropped daylight saving time in 2022: Mexico City is UTC-6 all year.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time (Mexico)");

        var utc = TimeZoneConversion.ToUtc(new DateTime(2026, 10, 22, 20, 0, 0), zone);

        utc.Should().Be(new DateTime(2026, 10, 23, 2, 0, 0));
        utc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ToUtc_InvalidTimeDuringSpringForward_IsShiftedOneHour()
    {
        // In Los Angeles, on 2026-03-08 at 02:00 the clock jumps to 03:00: 02:30 does not
        // exist and is treated as 03:30 PDT (UTC-7).
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

        var utc = TimeZoneConversion.ToUtc(new DateTime(2026, 3, 8, 2, 30, 0), zone);

        utc.Should().Be(new DateTime(2026, 3, 8, 10, 30, 0));
    }

    [Fact]
    public void ToUtc_IgnoresKindOfInput()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time (Mexico)");
        var wallClock = new DateTime(2026, 1, 15, 22, 0, 0);

        var fromLocalKind = TimeZoneConversion.ToUtc(DateTime.SpecifyKind(wallClock, DateTimeKind.Local), zone);
        var fromUtcKind = TimeZoneConversion.ToUtc(DateTime.SpecifyKind(wallClock, DateTimeKind.Utc), zone);

        fromLocalKind.Should().Be(new DateTime(2026, 1, 16, 4, 0, 0));
        fromUtcKind.Should().Be(fromLocalKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Zone that does not exist")]
    public void FindOrLocal_UnknownId_ReturnsLocalZone(string? id)
    {
        TimeZoneConversion.FindOrLocal(id).Should().Be(TimeZoneInfo.Local);
    }

    [Fact]
    public void FindOrLocal_KnownId_ReturnsThatZone()
    {
        TimeZoneConversion.FindOrLocal("Pacific Standard Time").Id.Should().Be("Pacific Standard Time");
    }
}
