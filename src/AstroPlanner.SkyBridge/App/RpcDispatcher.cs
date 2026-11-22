using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace AstroPlanner.SkyBridge.App;

/// <summary>
/// Host side of the app channel: routes each request to its handler and answers it exactly once.
/// The request's payload must match its method's contract (strict JSON, then the DataAnnotations
/// of the record); a handler runs at most <see cref="DefaultTimeout"/> and is cancelled when the
/// web UI sends <c>cancel</c>. Handler exceptions become <c>internal</c> errors whose details stay
/// in the host log. Nothing here knows about WPF or WebView2: <c>send</c> posts a JSON message.
/// </summary>
public sealed class RpcDispatcher
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly Action<string> _send;
    private readonly Action<string, Exception?> _log;
    private readonly TimeSpan _timeout;
    private readonly Dictionary<string, Func<AppRequestMessage, CancellationToken, Task<string>>> _handlers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _pending = new();

    /// <param name="send">Posts a message to the web UI.</param>
    /// <param name="log">Writes a line (and the exception, if any) to the host log.</param>
    /// <param name="timeout">Longest a handler may run; <see cref="DefaultTimeout"/> by default.</param>
    public RpcDispatcher(Action<string> send, Action<string, Exception?> log, TimeSpan? timeout = null)
    {
        _send = send;
        _log = log;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>Requests still running (tests).</summary>
    public int PendingCount => _pending.Count;

    public void Register<TRequest, TResponse>(RpcMethod<TRequest, TResponse> method, Func<TRequest, CancellationToken, Task<TResponse>> handler)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.Add(method.Name, async (request, cancellationToken) =>
        {
            TRequest? parameters;
            try
            {
                parameters = request.Payload.Deserialize(method.Request);
            }
            catch (JsonException)
            {
                parameters = default;
            }

            if (parameters is null || !IsValid(parameters))
            {
                return AppMessageCodec.Error(request.Id, method.Name, RpcErrorCodes.BadRequest, "The parameters do not match the contract.");
            }

            var response = await handler(parameters, cancellationToken).ConfigureAwait(true);
            if (response is null || !IsValid(response))
            {
                throw new InvalidOperationException($"The '{method.Name}' handler returned an answer that breaks its contract.");
            }

            var json = AppMessageCodec.Response(request.Id, method.Name, response, method.Response);
            return json.Length <= AppMessageCodec.MaximumResponseLength
                ? json
                : AppMessageCodec.Error(request.Id, method.Name, RpcErrorCodes.TooLarge, "The answer is too large; request it in pages.");
        });
    }

    /// <summary>Pushes an event to the web UI.</summary>
    public void Publish<T>(AppEvent<T> appEvent, T payload) => _send(AppMessageCodec.Event(appEvent, payload));

    /// <summary>Handles one message of the app channel. Never throws.</summary>
    public async Task HandleAsync(string json)
    {
        switch (AppMessageCodec.Decode(json, out var error))
        {
            case null:
                _log($"Ignored an app message: {error}.", null);
                break;
            case AppCancelMessage cancel:
                if (_pending.TryGetValue(cancel.Id, out var running))
                {
                    await running.CancelAsync().ConfigureAwait(true);
                }

                break;
            case AppRequestMessage request:
                await RunAsync(request).ConfigureAwait(true);
                break;
        }
    }

    private async Task RunAsync(AppRequestMessage request)
    {
        if (!_handlers.TryGetValue(request.Method, out var handler))
        {
            _send(AppMessageCodec.Error(request.Id, request.Method, RpcErrorCodes.UnknownMethod, "No such method."));
            return;
        }

        using var cancelled = new CancellationTokenSource();
        if (!_pending.TryAdd(request.Id, cancelled))
        {
            _send(AppMessageCodec.Error(request.Id, request.Method, RpcErrorCodes.BadRequest, "This request id is already in use."));
            return;
        }

        using var timedOut = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancelled.Token, timedOut.Token);
        try
        {
            // WaitAsync: the answer goes out on time even if a handler ignores its token.
            _send(await handler(request, linked.Token).WaitAsync(linked.Token).ConfigureAwait(true));
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            _send(cancelled.IsCancellationRequested
                ? AppMessageCodec.Error(request.Id, request.Method, RpcErrorCodes.Cancelled, "The request was cancelled.")
                : AppMessageCodec.Error(request.Id, request.Method, RpcErrorCodes.Timeout, "The host did not answer in time."));
        }
#pragma warning disable CA1031 // A failing handler must still answer (and must not take the window down).
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log($"The app request '{request.Method}' failed.", ex);
            _send(AppMessageCodec.Error(request.Id, request.Method, RpcErrorCodes.Internal, "The request failed."));
        }
        finally
        {
            _pending.TryRemove(request.Id, out _);
        }
    }

    private static bool IsValid(object value) =>
        Validator.TryValidateObject(value, new ValidationContext(value), validationResults: null, validateAllProperties: true);
}
