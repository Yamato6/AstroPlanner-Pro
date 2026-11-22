using System.Text.Json;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.SkyBridge.Tests;

/// <summary>The host side of the app channel answers every request exactly once.</summary>
public sealed class RpcDispatcherTests
{
    private static readonly AppInfoResponse Info = new("2.0.0", AppThemes.Dark, 1, 2);

    private readonly List<string> _sent = [];
    private readonly List<(string Message, Exception? Exception)> _log = [];

    private RpcDispatcher Create(TimeSpan? timeout = null) =>
        new(_sent.Add, (message, exception) => _log.Add((message, exception)), timeout);

    private static string Request(int id, string method = "app.info", string payload = "{}") =>
        $$"""{"v":2,"kind":"req","id":{{id}},"type":"{{method}}","payload":{{payload}}}""";

    private JsonElement Single()
    {
        _sent.Should().ContainSingle();
        return JsonElement.Parse(_sent[0]);
    }

    private static string ErrorCode(JsonElement response) => response.GetProperty("error").GetProperty("code").GetString()!;

    [Fact]
    public async Task Request_IsAnsweredWithTheHandlersResponse()
    {
        var dispatcher = Create();
        dispatcher.Register(AppMethods.AppInfo, (_, _) => Task.FromResult(Info));

        await dispatcher.HandleAsync(Request(7));

        var response = Single();
        response.GetProperty("kind").GetString().Should().Be("res");
        response.GetProperty("id").GetInt32().Should().Be(7);
        response.GetProperty("type").GetString().Should().Be("app.info");
        response.GetProperty("payload").GetProperty("version").GetString().Should().Be("2.0.0");
        response.TryGetProperty("error", out _).Should().BeFalse();
        dispatcher.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task UnknownMethod_IsAnError()
    {
        var dispatcher = Create();

        await dispatcher.HandleAsync(Request(1, "targets.query"));

        ErrorCode(Single()).Should().Be(RpcErrorCodes.UnknownMethod);
    }

    [Theory]
    [InlineData("""{"extra":1}""")]
    [InlineData("""{"theme":"dark"}""")]
    public async Task ParametersOutsideTheContract_AreABadRequest(string payload)
    {
        var dispatcher = Create();
        var called = false;
        dispatcher.Register(AppMethods.AppInfo, (_, _) =>
        {
            called = true;
            return Task.FromResult(Info);
        });

        await dispatcher.HandleAsync(Request(1, payload: payload));

        ErrorCode(Single()).Should().Be(RpcErrorCodes.BadRequest);
        called.Should().BeFalse();
    }

    [Fact]
    public async Task AHandlerThatTakesTooLong_TimesOut_EvenIfItIgnoresItsToken()
    {
        var dispatcher = Create(TimeSpan.FromMilliseconds(50));
        var never = new TaskCompletionSource<AppInfoResponse>();
        dispatcher.Register(AppMethods.AppInfo, (_, _) => never.Task);

        await dispatcher.HandleAsync(Request(1));

        ErrorCode(Single()).Should().Be(RpcErrorCodes.Timeout);
        dispatcher.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task Cancel_StopsTheHandler_AndAnswersCancelled()
    {
        var dispatcher = Create();
        var started = new TaskCompletionSource();
        CancellationToken seen = default;
        dispatcher.Register(AppMethods.AppInfo, async (_, cancellationToken) =>
        {
            seen = cancellationToken;
            started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Info;
        });

        var running = dispatcher.HandleAsync(Request(4));
        await started.Task;
        dispatcher.PendingCount.Should().Be(1);
        await dispatcher.HandleAsync("""{"v":2,"kind":"cancel","id":4}""");
        await running;

        seen.IsCancellationRequested.Should().BeTrue();
        ErrorCode(Single()).Should().Be(RpcErrorCodes.Cancelled);
    }

    [Fact]
    public async Task CancelOfAnUnknownRequest_IsIgnored()
    {
        var dispatcher = Create();

        await dispatcher.HandleAsync("""{"v":2,"kind":"cancel","id":99}""");

        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task ARequestIdInUse_IsRejected()
    {
        var dispatcher = Create();
        var release = new TaskCompletionSource<AppInfoResponse>();
        dispatcher.Register(AppMethods.AppInfo, (_, _) => release.Task);

        var first = dispatcher.HandleAsync(Request(5));
        await dispatcher.HandleAsync(Request(5));
        ErrorCode(Single()).Should().Be(RpcErrorCodes.BadRequest);

        release.SetResult(Info);
        await first;
        _sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task AFailingHandler_AnswersInternal_AndLogsTheDetails()
    {
        var dispatcher = Create();
        dispatcher.Register<AppInfoRequest, AppInfoResponse>(AppMethods.AppInfo, (_, _) => throw new IOException("disk on fire"));

        await dispatcher.HandleAsync(Request(1));

        var response = Single();
        ErrorCode(response).Should().Be(RpcErrorCodes.Internal);
        response.GetProperty("error").GetProperty("message").GetString().Should().NotContain("disk", "details stay in the host log");
        _log.Should().ContainSingle(l => l.Exception is IOException);
    }

    [Fact]
    public async Task AnAnswerThatBreaksTheContract_IsNeverSent()
    {
        var dispatcher = Create();
        dispatcher.Register(AppMethods.AppInfo, (_, _) => Task.FromResult(Info with { Theme = "sepia" }));

        await dispatcher.HandleAsync(Request(1));

        ErrorCode(Single()).Should().Be(RpcErrorCodes.Internal);
    }

    [Fact]
    public async Task MalformedMessages_AreLoggedAndDropped()
    {
        var dispatcher = Create();

        await dispatcher.HandleAsync("""{"v":2,"kind":"req","id":0,"type":"app.info","payload":{}}""");
        await dispatcher.HandleAsync("{");

        _sent.Should().BeEmpty();
        _log.Should().HaveCount(2);
    }

    [Fact]
    public void Publish_SendsAnEvent()
    {
        var dispatcher = Create();

        dispatcher.Publish(AppEvents.ThemeChanged, new ThemeChangedEvent(AppThemes.Night));

        var message = Single();
        message.GetProperty("kind").GetString().Should().Be("evt");
        message.GetProperty("type").GetString().Should().Be("theme.changed");
        message.GetProperty("payload").GetProperty("theme").GetString().Should().Be("night");
        message.TryGetProperty("id", out _).Should().BeFalse();
    }
}
