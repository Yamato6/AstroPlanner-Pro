using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace AstroPlanner.SkyBridge.App;

/// <summary>A validated message from the web UI on the app channel.</summary>
public abstract record AppIncomingMessage(int Id);

/// <summary>A request: <c>{"v":2,"kind":"req","id":7,"type":"app.info","payload":{…}}</c>.</summary>
public sealed record AppRequestMessage(int Id, string Method, JsonElement Payload) : AppIncomingMessage(Id);

/// <summary>The web UI gave up on a request: <c>{"v":2,"kind":"cancel","id":7}</c>.</summary>
public sealed record AppCancelMessage(int Id) : AppIncomingMessage(Id);

/// <summary>
/// The app channel (bridge v2, docs/bridge.md), next to the sky protocol (v1) on the same
/// WebView2 pipe. The web UI sends requests and cancellations; the host answers each request once
/// (<c>kind: "res"</c>, with <c>payload</c> or <c>error</c>) and pushes events (<c>kind: "evt"</c>).
/// Incoming messages are untrusted: anything malformed, oversized or with extra members is rejected.
/// </summary>
public static partial class AppMessageCodec
{
    public const int ProtocolVersion = 2;

    /// <summary>
    /// Largest accepted request, in UTF-16 characters (up to ~3× as many UTF-8 bytes; harmless at
    /// this size). Big inputs go through files, not messages. The other limits count the same way.
    /// </summary>
    public const int MaximumRequestLength = 64 * 1024;

    /// <summary>Largest response the host sends; bigger answers must be paged.</summary>
    public const int MaximumResponseLength = 4 * 1024 * 1024;

    /// <summary>Largest event the host sends.</summary>
    public const int MaximumEventLength = 256 * 1024;

    /// <summary>
    /// True when <paramref name="json"/> is an object whose top-level <c>v</c> is 2, i.e. it belongs
    /// to this channel rather than to the sky protocol. Cheap: anything longer than
    /// <see cref="MaximumRequestLength"/> is not looked at (the sky codec then rejects it by its own,
    /// smaller limit), and the scan stops at the first top-level "v".
    /// </summary>
    public static bool IsAppMessage(string? json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > MaximumRequestLength)
        {
            return false;
        }

        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions { MaxDepth = 8 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isVersion = reader.ValueTextEquals("v");
                reader.Read();
                if (isVersion)
                {
                    return reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var v) && v == ProtocolVersion;
                }

                reader.Skip();
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
        }

        return false;
    }

    /// <summary>
    /// Decodes a request or a cancellation. Returns null (and the reason) for anything else; the
    /// request's payload is checked later against its method's contract.
    /// </summary>
    public static AppIncomingMessage? Decode(string? json, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(json) || json.Length > MaximumRequestLength)
        {
            error = "empty or oversized message";
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var version) || version != ProtocolVersion
                || !root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number
                || !idElement.TryGetInt32(out var id) || id < 1)
            {
                error = "bad envelope";
                return null;
            }

            var names = root.EnumerateObject().Select(p => p.Name).ToList();
            var members = names.ToHashSet(StringComparer.Ordinal);
            if (members.Count != names.Count)
            {
                // {"v":2,…,"v":1}: parsers disagree on which one wins, so neither does.
                error = "duplicate member";
                return null;
            }

            switch (kind.GetString())
            {
                case "cancel" when members.SetEquals(["v", "kind", "id"]):
                    return new AppCancelMessage(id);
                case "req" when members.SetEquals(["v", "kind", "id", "type", "payload"])
                                && root.GetProperty("type") is { ValueKind: JsonValueKind.String } type
                                && IsMethodName(type.GetString())
                                && root.GetProperty("payload") is { ValueKind: JsonValueKind.Object } payload:
                    return new AppRequestMessage(id, type.GetString()!, payload.Clone());
                default:
                    error = "bad envelope";
                    return null;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            // InvalidOperationException: e.g. a lone surrogate ("\ud800") in a string read with GetString.
            error = "malformed JSON";
            return null;
        }
    }

    /// <summary>A successful answer to request <paramref name="id"/>.</summary>
    public static string Response<T>(int id, string method, T payload, JsonTypeInfo<T> typeInfo) =>
        Encode(writer =>
        {
            Header(writer, "res", id, method);
            writer.WritePropertyName("payload");
            JsonSerializer.Serialize(writer, payload, typeInfo);
        });

    /// <summary>A failed answer to request <paramref name="id"/>.</summary>
    public static string Error(int id, string method, string code, string message) =>
        Encode(writer =>
        {
            Header(writer, "res", id, method);
            writer.WritePropertyName("error");
            JsonSerializer.Serialize(writer, new RpcErrorPayload(code, message), AppJsonContext.Default.RpcErrorPayload);
        });

    /// <summary>An event pushed to the web UI.</summary>
    public static string Event<T>(AppEvent<T> appEvent, T payload)
    {
        ArgumentNullException.ThrowIfNull(appEvent);
        var json = Encode(writer =>
        {
            Header(writer, "evt", id: null, appEvent.Name);
            writer.WritePropertyName("payload");
            JsonSerializer.Serialize(writer, payload, appEvent.Payload);
        });
        return json.Length <= MaximumEventLength
            ? json
            : throw new InvalidOperationException($"The '{appEvent.Name}' event exceeds {MaximumEventLength} characters.");
    }

    /// <summary>"area.verb" names: lower-case area, then one or more camelCase segments.</summary>
    public static bool IsMethodName(string? name) => name is { Length: <= 64 } && MethodName().IsMatch(name);

    private static void Header(Utf8JsonWriter writer, string kind, int? id, string type)
    {
        writer.WriteNumber("v", ProtocolVersion);
        writer.WriteString("kind", kind);
        if (id is { } value)
        {
            writer.WriteNumber("id", value);
        }

        writer.WriteString("type", type);
    }

    private static string Encode(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [GeneratedRegex("^[a-z]+(\\.[a-z][a-zA-Z]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex MethodName();
}
