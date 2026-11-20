using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AstroPlanner.SkyBridge;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    NumberHandling = JsonNumberHandling.Strict,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(InitPayload))]
[JsonSerializable(typeof(ObserverPayload))]
[JsonSerializable(typeof(TimePayload))]
[JsonSerializable(typeof(TimeRatePayload))]
[JsonSerializable(typeof(FrameTransformPayload))]
[JsonSerializable(typeof(MovingBodiesPayload))]
[JsonSerializable(typeof(SkyLayers))]
[JsonSerializable(typeof(HighlightsPayload))]
[JsonSerializable(typeof(SelectionPayload))]
[JsonSerializable(typeof(CenterOnPayload))]
[JsonSerializable(typeof(ViewModePayload))]
[JsonSerializable(typeof(ThemePayload))]
[JsonSerializable(typeof(FovPayload))]
[JsonSerializable(typeof(SkyAppearancePayload))]
[JsonSerializable(typeof(FovChangedPayload))]
[JsonSerializable(typeof(ReadyPayload))]
[JsonSerializable(typeof(FirstFramePayload))]
[JsonSerializable(typeof(HoverPayload))]
[JsonSerializable(typeof(ViewChangedPayload))]
[JsonSerializable(typeof(StatsPayload))]
[JsonSerializable(typeof(ShortcutPayload))]
[JsonSerializable(typeof(ErrorPayload))]
internal sealed partial class SkyJsonContext : JsonSerializerContext;

/// <summary>A validated message received from the renderer.</summary>
public sealed record IncomingSkyMessage(string Type, object Payload);

/// <summary>
/// Encodes outgoing messages as <c>{"v":1,"type":…,"payload":…}</c> and decodes and validates
/// incoming ones. Anything malformed, unknown, oversized or out of range is rejected: the renderer
/// is treated as untrusted input.
/// </summary>
public static class SkyMessageCodec
{
    public const int ProtocolVersion = 1;

    /// <summary>Largest accepted incoming message (characters).</summary>
    public const int MaximumIncomingLength = 16 * 1024;

    /// <summary>Longest accepted id, kind or label.</summary>
    public const int MaximumTextLength = 128;

    public static string Init(InitPayload payload) => Encode(SkyMessageTypes.Init, payload, SkyJsonContext.Default.InitPayload);
    public static string SetObserver(ObserverPayload payload) => Encode(SkyMessageTypes.SetObserver, payload, SkyJsonContext.Default.ObserverPayload);
    public static string SetTime(TimePayload payload) => Encode(SkyMessageTypes.SetTime, payload, SkyJsonContext.Default.TimePayload);
    public static string SetTimeRate(TimeRatePayload payload) => Encode(SkyMessageTypes.SetTimeRate, payload, SkyJsonContext.Default.TimeRatePayload);
    public static string FrameTransform(FrameTransformPayload payload) => Encode(SkyMessageTypes.FrameTransform, payload, SkyJsonContext.Default.FrameTransformPayload);
    public static string MovingBodies(MovingBodiesPayload payload) => Encode(SkyMessageTypes.MovingBodies, payload, SkyJsonContext.Default.MovingBodiesPayload);
    public static string SetLayers(SkyLayers payload) => Encode(SkyMessageTypes.SetLayers, payload, SkyJsonContext.Default.SkyLayers);
    public static string SetHighlights(HighlightsPayload payload) => Encode(SkyMessageTypes.SetHighlights, payload, SkyJsonContext.Default.HighlightsPayload);
    public static string SelectObject(SelectionPayload payload) => Encode(SkyMessageTypes.SelectObject, payload, SkyJsonContext.Default.SelectionPayload);
    public static string CenterOn(CenterOnPayload payload) => Encode(SkyMessageTypes.CenterOn, payload, SkyJsonContext.Default.CenterOnPayload);
    public static string SetViewMode(ViewModePayload payload) => Encode(SkyMessageTypes.SetViewMode, payload, SkyJsonContext.Default.ViewModePayload);
    public static string SetTheme(ThemePayload payload) => Encode(SkyMessageTypes.SetTheme, payload, SkyJsonContext.Default.ThemePayload);
    public static string SetFov(FovPayload payload) => Encode(SkyMessageTypes.SetFov, payload, SkyJsonContext.Default.FovPayload);
    public static string SetSkyAppearance(SkyAppearancePayload payload) => Encode(SkyMessageTypes.SetSkyAppearance, payload, SkyJsonContext.Default.SkyAppearancePayload);

    /// <summary>Formats an instant the way the protocol expects it ("2026-03-20T02:00:00.000Z").</summary>
    public static string FormatUtc(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Encode<T>(string type, T payload, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", ProtocolVersion);
            writer.WriteString("type", type);
            writer.WritePropertyName("payload");
            JsonSerializer.Serialize(writer, payload, typeInfo);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Decodes and validates a message from the renderer. Returns null (and the reason) for
    /// anything that is not a well-formed, known, in-range message.
    /// </summary>
    public static IncomingSkyMessage? Decode(string? json, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(json) || json.Length > MaximumIncomingLength)
        {
            error = "empty or oversized message";
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var v) || v != ProtocolVersion
                || !root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("payload", out var payloadElement) || payloadElement.ValueKind != JsonValueKind.Object)
            {
                error = "bad envelope";
                return null;
            }

            var type = typeElement.GetString()!;
            object? payload = type switch
            {
                SkyMessageTypes.Ready => Valid(payloadElement.Deserialize(SkyJsonContext.Default.ReadyPayload),
                    p => Text(p.RendererVersion) && p.WebGlVersion is 1 or 2),
                SkyMessageTypes.FirstFrame => Valid(payloadElement.Deserialize(SkyJsonContext.Default.FirstFramePayload),
                    p => Finite(p.Milliseconds) && p.Milliseconds >= 0),
                SkyMessageTypes.SelectObject => Valid(payloadElement.Deserialize(SkyJsonContext.Default.SelectionPayload),
                    p => p.Id is null || (Kind(p.Kind) && Text(p.Id))),
                SkyMessageTypes.Hover => Valid(payloadElement.Deserialize(SkyJsonContext.Default.HoverPayload),
                    p => p.Id is null || (Kind(p.Kind) && Text(p.Id) && (p.Label is null || p.Label.Length <= MaximumTextLength))),
                SkyMessageTypes.ViewChanged => Valid(payloadElement.Deserialize(SkyJsonContext.Default.ViewChangedPayload),
                    p => p.Mode is SkyViewModes.Dome or SkyViewModes.AllSky or SkyViewModes.Finder
                         && Finite(p.AzimuthDegrees) && p.AzimuthDegrees is >= 0 and < 360
                         && Finite(p.AltitudeDegrees) && p.AltitudeDegrees is >= -90 and <= 90
                         && Finite(p.FovDegrees) && p.FovDegrees is > 0 and <= 360),
                SkyMessageTypes.Stats => Valid(payloadElement.Deserialize(SkyJsonContext.Default.StatsPayload),
                    p => Finite(p.Fps) && p.Fps >= 0 && Finite(p.P95FrameMs) && p.P95FrameMs >= 0
                         && p.Frames >= 0 && p.Width >= 0 && p.Height >= 0 && p.Stars >= 0),
                SkyMessageTypes.Shortcut => Valid(payloadElement.Deserialize(SkyJsonContext.Default.ShortcutPayload),
                    p => p.Action is SkyShortcuts.TogglePause or SkyShortcuts.ViewDome or SkyShortcuts.ViewAllSky
                         or SkyShortcuts.ToggleFov or SkyShortcuts.FocusSearch),
                SkyMessageTypes.FovChanged => Valid(payloadElement.Deserialize(SkyJsonContext.Default.FovChangedPayload),
                    p => Finite(p.X) && Finite(p.Y) && Finite(p.Z)
                         && Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z) - 1) <= 1e-3
                         && Finite(p.RotationDegrees) && p.RotationDegrees is >= 0 and < 360),
                SkyMessageTypes.Error => Valid(payloadElement.Deserialize(SkyJsonContext.Default.ErrorPayload),
                    p => p.Message is { Length: > 0 and <= 1024 }),
                _ => null
            };

            if (payload is null)
            {
                error = $"unknown or invalid '{(type.Length <= MaximumTextLength ? type : "?")}' message";
                return null;
            }

            return new IncomingSkyMessage(type, payload);
        }
        catch (JsonException)
        {
            error = "malformed JSON";
            return null;
        }
    }

    private static T? Valid<T>(T? payload, Func<T, bool> isValid) where T : class =>
        payload is not null && isValid(payload) ? payload : null;

    private static bool Finite(double value) => double.IsFinite(value);

    private static bool Text(string? value) => value is { Length: > 0 and <= MaximumTextLength };

    private static bool Kind(string? kind) => kind is SkyObjectKinds.Star or SkyObjectKinds.DeepSky or SkyObjectKinds.Body;
}
