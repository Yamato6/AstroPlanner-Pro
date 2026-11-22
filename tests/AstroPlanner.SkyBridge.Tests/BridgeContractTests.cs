using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Services;
using AstroPlanner.Core.Sky;
using AstroPlanner.Core.Satellites;

namespace AstroPlanner.SkyBridge.Tests;

/// <summary>
/// Contract between C# and the renderer (spec §5.4, §11). The golden files in tests/fixtures/bridge
/// are read by both this suite and the Vitest suite of src/web/packages/sky-engine:
/// <list type="bullet">
/// <item><c>outgoing/*.json</c>: one sample of every C# → renderer message (generated here, validated by zod);</item>
/// <item><c>incoming/*.json</c>: renderer → C# samples that both sides must accept;</item>
/// <item><c>invalid/*.json</c>: renderer → C# messages that both sides must reject;</item>
/// <item><c>projection.json</c>: the projection cross-check (§5.3).</item>
/// </list>
/// Regenerate the generated files with <c>UPDATE_BRIDGE_FIXTURES=1 dotnet test</c>.
/// </summary>
public sealed class BridgeContractTests
{
    internal static string Fixtures { get; } = typeof(BridgeContractTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "BridgeFixtures").Value!;

    private static bool Update => Environment.GetEnvironmentVariable("UPDATE_BRIDGE_FIXTURES") == "1";

    private static readonly DateTime Utc = new(2026, 3, 20, 2, 0, 0, DateTimeKind.Utc);
    private static readonly ObserverLocation Aguascalientes = new(21.88, -102.29, 1888);

    internal static IReadOnlyDictionary<string, string> OutgoingSamples()
    {
        var mask = new HorizonMask([new HorizonPoint(0, 5), new HorizonPoint(90, 12.5), new HorizonPoint(200, 3)]);
        var bodies = SkyBridgeMapper.MovingBodies(Aguascalientes, Utc, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10),
            satellites: [SampleSatellite()]);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SkyMessageTypes.Init] = SkyMessageCodec.Init(new InitPayload(
                "https://data.astroplanner/", RefractionModel.Standard, new SkyLayers(), SkyViewModes.Dome, "dark", ShowStats: false)),
            [SkyMessageTypes.SetObserver] = SkyMessageCodec.SetObserver(SkyBridgeMapper.ToPayload("Aguascalientes", Aguascalientes, 20, mask)),
            [SkyMessageTypes.SetTime] = SkyMessageCodec.SetTime(new TimePayload(SkyMessageCodec.FormatUtc(Utc), 60)),
            [SkyMessageTypes.SetTimeRate] = SkyMessageCodec.SetTimeRate(new TimeRatePayload(0)),
            [SkyMessageTypes.FrameTransform] = SkyMessageCodec.FrameTransform(SkyBridgeMapper.ToPayload(SkyFrame.Compute(Aguascalientes, Utc))),
            [SkyMessageTypes.MovingBodies] = SkyMessageCodec.MovingBodies(bodies with { Bodies = [.. bodies.Bodies.Take(3), bodies.Bodies[^1]] }),
            [SkyMessageTypes.SetLayers] = SkyMessageCodec.SetLayers(new SkyLayers(EquatorialGrid: true, StarLimitMagnitude: 7)),
            [SkyMessageTypes.SetHighlights] = SkyMessageCodec.SetHighlights(new HighlightsPayload(["M42", "NGC 7000"])),
            [SkyMessageTypes.SelectObject] = SkyMessageCodec.SelectObject(new SelectionPayload(SkyObjectKinds.DeepSky, "M42")),
            [SkyMessageTypes.CenterOn] = SkyMessageCodec.CenterOn(SkyBridgeMapper.CenterOn(new EquatorialCoordinates(5.5881, -5.3911), 30)),
            [SkyMessageTypes.SetViewMode] = SkyMessageCodec.SetViewMode(new ViewModePayload(SkyViewModes.AllSky)),
            [SkyMessageTypes.SetTheme] = SkyMessageCodec.SetTheme(new ThemePayload("dark")),
            [SkyMessageTypes.SetFov] = SkyMessageCodec.SetFov(new FovPayload(true, 3.3652, 2.2483, 15,
                SkyBridgeMapper.ToVector(SkyFrame.FromEquatorial(new EquatorialCoordinates(5.5881, -5.3911))), "APS-C · 400 mm")),
            [SkyMessageTypes.SetSkyAppearance] = SkyMessageCodec.SetSkyAppearance(SkyBridgeMapper.Appearance(
                Aguascalientes, Utc, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), siteSqm: 21.3))
        };
    }

    [Fact]
    public void OutgoingSamples_MatchTheGoldenFiles()
    {
        var directory = Path.Combine(Fixtures, "outgoing");
        Directory.CreateDirectory(directory);
        foreach (var (type, json) in OutgoingSamples())
        {
            var path = Path.Combine(directory, type + ".json");
            var pretty = Pretty(json);
            if (Update)
            {
                File.WriteAllText(path, pretty, new UTF8Encoding(false));
            }

            File.Exists(path).Should().BeTrue($"{type}.json must exist (run with UPDATE_BRIDGE_FIXTURES=1)");
            File.ReadAllText(path).ReplaceLineEndings("\n").Should().Be(pretty, $"{type} changed: update the golden file and schema.ts together");
        }

        Directory.GetFiles(directory, "*.json").Select(Path.GetFileNameWithoutExtension)
            .Should().BeEquivalentTo(OutgoingSamples().Keys, "every outgoing message type has exactly one sample");
    }

    [Fact]
    public void IncomingSamples_AreAccepted()
    {
        var files = Directory.GetFiles(Path.Combine(Fixtures, "incoming"), "*.json");
        files.Should().NotBeEmpty();
        foreach (var file in files)
        {
            var message = SkyMessageCodec.Decode(File.ReadAllText(file), out var error);
            message.Should().NotBeNull($"{Path.GetFileName(file)}: {error}");
            message!.Type.Should().Be(Path.GetFileNameWithoutExtension(file).Split('.')[0]);
        }
    }

    [Fact]
    public void InvalidSamples_AreRejected()
    {
        var files = Directory.GetFiles(Path.Combine(Fixtures, "invalid"), "*.json");
        files.Should().HaveCountGreaterThan(5);
        foreach (var file in files)
        {
            SkyMessageCodec.Decode(File.ReadAllText(file), out var error).Should().BeNull(Path.GetFileName(file));
            error.Should().NotBeNullOrEmpty();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"v\":1,\"type\":\"ready\"}")]
    public void Decode_RejectsGarbage(string? json) =>
        SkyMessageCodec.Decode(json, out _).Should().BeNull();

    [Fact]
    public void Decode_RejectsOversizedMessages()
    {
        var label = new string('x', SkyMessageCodec.MaximumIncomingLength);
        var json = "{\"v\":1,\"type\":\"error\",\"payload\":{\"message\":\"" + label + "\"}}";
        SkyMessageCodec.Decode(json, out var error).Should().BeNull();
        error.Should().Contain("oversized");
    }

    [Fact]
    public void Decode_ReturnsTypedPayloads()
    {
        var message = SkyMessageCodec.Decode("""{"v":1,"type":"selectObject","payload":{"kind":"dso","id":"M42"}}""", out _);

        message!.Payload.Should().Be(new SelectionPayload("dso", "M42"));
        SkyMessageCodec.Decode("""{"v":1,"type":"selectObject","payload":{"kind":null,"id":null}}""", out _)!
            .Payload.Should().Be(new SelectionPayload(null, null));
    }

    [Fact]
    public void Projection_FixtureMatchesCore()
    {
        var path = Path.Combine(Fixtures, "projection.json");
        var generated = Pretty(ProjectionFixture.Generate());
        if (Update)
        {
            File.WriteAllText(path, generated, new UTF8Encoding(false));
        }

        File.ReadAllText(path).ReplaceLineEndings("\n").Should().Be(generated);
    }

    [Fact]
    public void Projection_MatrixReproducesGetHorizontal()
    {
        // The C# half of the cross-check: applying the transform exactly as the shader does
        // (matrix, then refraction with the protocol coefficients) gives GetHorizontal ≤ 0.01°.
        using var document = JsonDocument.Parse(ProjectionFixture.Generate());
        foreach (var testCase in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var frame = testCase.GetProperty("frame");
            var p = frame.GetProperty("precession").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            var epoch = DateTime.Parse(frame.GetProperty("epochUtc").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
            var utc = DateTime.Parse(testCase.GetProperty("utc").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
            var lst = frame.GetProperty("lstDegrees").GetDouble() + frame.GetProperty("lstRateDegreesPerSecond").GetDouble() * (utc - epoch).TotalSeconds;
            var matrix = SkyFrame.HorizonMatrix(frame.GetProperty("latitudeDegrees").GetDouble())
                         * Matrix3.RotationZ(-lst)
                         * new Matrix3(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7], p[8]);
            var j2000 = testCase.GetProperty("j2000").EnumerateArray().Select(e => e.GetDouble()).ToArray();

            var horizontal = SkyFrame.ToHorizontal(matrix.Transform(new Vector3d(j2000[0], j2000[1], j2000[2])));
            var refraction = testCase.GetProperty("refraction");
            var model = new RefractionModel(refraction.GetProperty("a").GetDouble(), refraction.GetProperty("b").GetDouble(),
                refraction.GetProperty("c").GetDouble(), refraction.GetProperty("d").GetDouble(), refraction.GetProperty("minimumAltitudeDegrees").GetDouble());
            var apparent = ProjectionFixture.Refract(horizontal.AltitudeDegrees, model);

            apparent.Should().BeApproximately(testCase.GetProperty("altitudeDegrees").GetDouble(), 0.01);
            AzimuthDifference(horizontal.AzimuthDegrees, testCase.GetProperty("azimuthDegrees").GetDouble()).Should().BeLessThan(0.01);
        }
    }

    [Fact]
    public void Refract_MatchesCoreRefraction()
    {
        foreach (var altitude in new[] { -3.0, -1.0, 0.0, 5.0, 30.0, 89.9 })
        {
            ProjectionFixture.Refract(altitude, RefractionModel.Standard)
                .Should().BeApproximately(Refraction.ApparentAltitude(altitude), 1e-12);
            ProjectionFixture.Refract(altitude, RefractionModel.ForElevation(1888))
                .Should().BeApproximately(Refraction.ApparentAltitude(altitude, Refraction.PressureScale(1888)), 1e-12);
        }
    }

    [Fact]
    public void MovingBodies_TracksCoverTheWindow()
    {
        var payload = SkyBridgeMapper.MovingBodies(Aguascalientes, Utc, TimeSpan.FromHours(12), TimeSpan.FromMinutes(10), id => id.ToUpperInvariant());

        payload.Bodies.Should().HaveCount(9);
        payload.Bodies[0].Kind.Should().Be("sun");
        payload.Bodies[1].Kind.Should().Be("moon");
        payload.Bodies[2].Name.Should().Be("MERCURY");
        payload.Bodies.Should().AllSatisfy(b => b.Track.Vectors.Should().HaveCount(145 * 3));
        payload.Bodies[0].Track.StartUtc.Should().Be("2026-03-19T14:00:00.000Z");
    }

    /// <summary>A three-sample satellite track (the real ones have 181), sunlit, then in the shadow, then sunlit.</summary>
    private static SatelliteTrack SampleSatellite()
    {
        var state = new MovingBodyState(SatelliteTracks.Id(25544), MovingBodyKind.Satellite, new HorizontalCoordinates(40, 120),
            new EquatorialCoordinates(6, 10), -3.1, 0, 35, 1, 0);
        TrackSample Sample(int seconds, double x, double y) =>
            new(Utc.AddSeconds(seconds), new Vector3d(x, y, Math.Sqrt(1 - (x * x) - (y * y))));
        return new SatelliteTrack("ISS (ZARYA)", new MovingBodyTrack(state, [Sample(-10, 0.6, 0.2), Sample(0, 0.58, 0.25), Sample(10, 0.55, 0.3)]),
            [true, false, true]);
    }

    [Fact]
    public void MovingBodies_SatellitesKeepTheirOwnShortTrack()
    {
        var payload = SkyBridgeMapper.MovingBodies(Aguascalientes, Utc, TimeSpan.FromHours(12), TimeSpan.FromMinutes(10), satellites: [SampleSatellite()]);

        var satellite = payload.Bodies[^1];
        satellite.Should().BeEquivalentTo(new { Id = "sat-25544", Kind = "satellite", Name = "ISS (ZARYA)", Magnitude = -3.1 },
            o => o.ExcludingMissingMembers());
        satellite.Track.StartUtc.Should().Be("2026-03-20T01:59:50.000Z");
        satellite.Track.StepSeconds.Should().Be(10);
        satellite.Track.Vectors.Should().HaveCount(9);
        satellite.Sunlit.Should().Equal(1, 0, 1);
        payload.Bodies.Take(payload.Bodies.Count - 1).Should().OnlyContain(b => b.Sunlit == null);
    }

    [Fact]
    public void Appearance_CoversTheWindow_FromDaylightToADarkNight()
    {
        var payload = SkyBridgeMapper.Appearance(Aguascalientes, Utc, TimeSpan.FromHours(12), TimeSpan.FromMinutes(10), siteSqm: null);

        payload.StartUtc.Should().Be("2026-03-19T14:00:00.000Z");
        payload.StepSeconds.Should().Be(600);
        payload.ZenithSqm.Should().HaveCount(145);
        payload.LimitingMagnitude.Should().HaveCount(145);
        payload.ZenithSqm.Max().Should().BeApproximately(22.0, 0.05, "new Moon: the night is as dark as a natural sky");
        payload.ZenithSqm.Min().Should().BeLessThan(6, "the window starts in daylight");
    }

    [Fact]
    public void Decode_FovChanged_ChecksTheVectorAndTheRotation()
    {
        SkyMessageCodec.Decode("""{"v":1,"type":"fovChanged","payload":{"x":0,"y":0.6,"z":0.8,"rotationDegrees":359.5}}""", out _)!
            .Payload.Should().Be(new FovChangedPayload(0, 0.6, 0.8, 359.5));
        SkyMessageCodec.Decode("""{"v":1,"type":"fovChanged","payload":{"x":0,"y":0,"z":0,"rotationDegrees":0}}""", out _).Should().BeNull();
        SkyMessageCodec.Decode("""{"v":1,"type":"fovChanged","payload":{"x":1,"y":0,"z":0,"rotationDegrees":-1}}""", out _).Should().BeNull();
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static double AzimuthDifference(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    internal static string Pretty(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, Indented)
            .ReplaceLineEndings("\n") + "\n";
    }
}

/// <summary>Builds tests/fixtures/bridge/projection.json from Core's <c>GetHorizontal</c>.</summary>
internal static class ProjectionFixture
{
    public static double Refract(double trueAltitude, RefractionModel model)
    {
        if (trueAltitude < model.MinimumAltitudeDegrees)
        {
            return trueAltitude;
        }

        var argument = (trueAltitude + model.B / (trueAltitude + model.C)) * Math.PI / 180;
        return trueAltitude + Math.Max(0, (model.A / Math.Tan(argument) + model.D) / 60);
    }

    public static string Generate()
    {
        var service = new AstronomyService();
        var sites = new (string Name, ObserverLocation Observer)[]
        {
            ("Aguascalientes", new ObserverLocation(21.88, -102.29, 1888)),
            ("Madrid", new ObserverLocation(40.42, -3.70, 667)),
            ("Sydney", new ObserverLocation(-33.87, 151.21)),
            ("Tromso", new ObserverLocation(69.65, 18.96))
        };
        var targets = new (string Id, EquatorialCoordinates J2000)[]
        {
            ("M42", new EquatorialCoordinates(5.5881, -5.3911)),
            ("Polaris", new EquatorialCoordinates(2.52975, 89.264109)),
            ("Sirius", new EquatorialCoordinates(6.752481, -16.716116)),
            ("M31", new EquatorialCoordinates(0.712306, 41.268750)),
            ("Acrux", new EquatorialCoordinates(12.443311, -63.099092))
        };
        var epochs = new[]
        {
            new DateTime(2026, 3, 20, 2, 0, 0, DateTimeKind.Utc),
            new DateTime(2031, 12, 1, 18, 30, 0, DateTimeKind.Utc)
        };

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("description", "Generated by AstroPlanner.SkyBridge.Tests from Core GetHorizontal (apparent altitude with refraction scaled by the site elevation, as sent in setObserver). Tolerance 0.01 degrees.");
            writer.WriteStartArray("cases");
            foreach (var (siteName, observer) in sites)
            {
                foreach (var epoch in epochs)
                {
                    var frame = SkyBridgeMapper.ToPayload(SkyFrame.Compute(observer, epoch));
                    // Two instants per frame: the epoch and 5 h later (LST extrapolation).
                    foreach (var utc in new[] { epoch, epoch.AddHours(5).AddMinutes(13) })
                    {
                        foreach (var (id, j2000) in targets)
                        {
                            var target = new CelestialObject(id, id, "Ori", CelestialObjectType.Unknown, 0, j2000);
                            var expected = service.GetHorizontal(target, observer, utc);
                            var vector = SkyFrame.FromEquatorial(j2000);

                            writer.WriteStartObject();
                            writer.WriteString("site", siteName);
                            var model = RefractionModel.ForElevation(observer.ElevationMeters);
                            writer.WriteStartObject("refraction");
                            writer.WriteNumber("a", model.A);
                            writer.WriteNumber("b", model.B);
                            writer.WriteNumber("c", model.C);
                            writer.WriteNumber("d", model.D);
                            writer.WriteNumber("minimumAltitudeDegrees", model.MinimumAltitudeDegrees);
                            writer.WriteEndObject();
                            writer.WriteString("target", id);
                            writer.WriteString("utc", SkyMessageCodec.FormatUtc(utc));
                            writer.WriteStartObject("frame");
                            writer.WriteString("epochUtc", frame.EpochUtc);
                            writer.WriteStartArray("precession");
                            foreach (var value in frame.Precession)
                            {
                                writer.WriteNumberValue(value);
                            }

                            writer.WriteEndArray();
                            writer.WriteNumber("latitudeDegrees", frame.LatitudeDegrees);
                            writer.WriteNumber("lstDegrees", frame.LstDegrees);
                            writer.WriteNumber("lstRateDegreesPerSecond", frame.LstRateDegreesPerSecond);
                            writer.WriteEndObject();
                            writer.WriteStartArray("j2000");
                            writer.WriteNumberValue(vector.X);
                            writer.WriteNumberValue(vector.Y);
                            writer.WriteNumberValue(vector.Z);
                            writer.WriteEndArray();
                            writer.WriteNumber("altitudeDegrees", Math.Round(expected.AltitudeDegrees, 6));
                            writer.WriteNumber("azimuthDegrees", Math.Round(expected.AzimuthDegrees, 6));
                            writer.WriteEndObject();
                        }
                    }
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
