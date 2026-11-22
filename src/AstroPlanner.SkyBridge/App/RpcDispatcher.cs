using System.Collections.Concurrent;
using System.Text.Json;

namespace AstroPlanner.SkyBridge.App;

/// <summary>
/// Host side of the app channel: routes each request to its handler and answers it exactly once.
/// The request's payload must match its method's contract (strict JSON, then the DataAnnotations
/// of the record and of the records it contains); a handler gets at most <see cref="DefaultTimeout"/>
/// and is cancelled when the web UI sends <c>cancel</c>. Handler exceptions become <c>internal</c>
/// errors whose details stay in the host log. Nothing here knows about WPF or WebView2: <c>send</c>
/// posts a JSON message, and a failing <c>send</c> (the page is gone) is logged, never thrown.
/// <para>
/// Handlers must be asynchronous: the timeout can only answer once a handler yields, so heavy work
/// belongs on the thread pool (<see cref="Task.Run(Action)"/>). A handler that outlives its timeout
/// keeps its id reserved until it really ends, and its failure is logged.
/// </para>
/// </summary>
public sealed class RpcDispatcher
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Requests that may run at once; more are answered <see cref="RpcErrorCodes.Busy"/>.</summary>
    public const int MaximumConcurrentRequests = 32;

    private readonly Action<string> _send;
    private readonly Action<string, Exception?> _log;
    private readonly TimeSpan _timeout;
    private readonly Dictionary<string, Func<AppRequestMessage, CancellationToken, Task<string>>> _handlers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, Running> _pending = new();

    /// <param name="send">Posts a message to the web UI.</param>
    /// <param name="log">Writes a line (and the exception, if any) to the host log.</param>
    /// <param name="timeout">Longest a handler may run; <see cref="DefaultTimeout"/> by default.</param>
    public RpcDispatcher(Action<string> send, Action<string, Exception?> log, TimeSpan? timeout = null)
    {
        _send = send;
        _log = log;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>Requests still running, including handlers that outlived their timeout (tests).</summary>
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

            if (parameters is null || !ContractValidator.IsValid(parameters))
            {
                return AppMessageCodec.Error(request.Id, method.Name, RpcErrorCodes.BadRequest, "The parameters do not match the contract.");
            }

            var response = await handler(parameters, cancellationToken).ConfigureAwait(true);
            if (response is null || !ContractValidator.IsValid(response))
            {
                throw new InvalidOperationException($"The '{method.Name}' handler returned an answer that breaks its contract.");
            }

            var json = AppMessageCodec.Response(request.Id, method.Name, response, method.Response);
            return json.Length <= AppMessageCodec.MaximumResponseLength
                ? json
                : AppMessageCodec.Error(request.Id, method.Name, RpcErrorCodes.TooLarge, "The answer is too large; request it in pages.");
        });
    }

    /// <summary>
    /// Pushes an event to the web UI. An event that breaks its contract or its size limit is a bug:
    /// it is logged and dropped, never thrown at the code that raised it.
    /// </summary>
    public void Publish<T>(AppEvent<T> appEvent, T payload)
    {
        ArgumentNullException.ThrowIfNull(appEvent);
        if (payload is null || !ContractValidator.IsValid(payload))
        {
            _log($"Dropped a '{appEvent.Name}' event that breaks its contract.", null);
            return;
        }

        string json;
        try
        {
            json = AppMessageCodec.Event(appEvent, payload);
        }
        catch (InvalidOperationException ex)
        {
            _log($"Dropped a '{appEvent.Name}' event.", ex);
            return;
        }

        TrySend(json);
    }

    /// <summary>Handles one message of the app channel. Never throws.</summary>
    public async Task HandleAsync(string json)
    {
        try
        {
            switch (AppMessageCodec.Decode(json, out var error))
            {
                case null:
                    _log($"Ignored an app message: {error}.", null);
                    break;
                case AppCancelMessage cancel:
                    if (_pending.TryGetValue(cancel.Id, out var running))
                    {
                        running.Cancel();
                    }

                    break;
                case AppRequestMessage request:
                    await RunAsync(request).ConfigureAwait(true);
                    break;
            }
        }
#pragma warning disable CA1031 // The page must not be able to take the host down.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log("An app message could not be handled.", ex);
        }
    }

    /// <summary>Cancels every running request (the page is going away).</summary>
    public void CancelAll()
    {
        foreach (var running in _pending.Values)
        {
            running.Cancel();
        }
    }

    private async Task RunAsync(AppRequestMessage request)
    {
        if (!_handlers.TryGetValue(request.Method, out var handler))
        {
            TrySend(AppMessageCodec.Error(request.Id, request.Method, RpcErrorCodes.UnknownMethod, "No such method."));
            return;
        }

        if (_pending.ContainsKey(request.Id))
        {
            // Answering would settle the request already running under this id: drop the duplicate.
            _log($"Ignored an app request that reuses id {request.Id}.", null);
            return;
        }

        if (_pending.Count >= MaximumConcurrentRequests)
        {
            TrySend(AppMessageCodec.Error(request.Id, request.Method, RpcErrorCodes.Busy, "Too many requests are running; retry later."));
            return;
        }

        var running = new Running(_timeout);
        if (!_pending.TryAdd(request.Id, running))
        {
            running.Dispose();
            return;
        }

        Task<string>? work = null;
        try
        {
            work = handler(request, running.Token);
            // WaitAsync: the answer goes out on time even if a handler ignores its token.
            TrySend(await work.WaitAsync(running.Token).ConfigureAwait(true));
        }
        catch (OperationCanceledException) when (running.Token.IsCancellationRequested)
        {
            TrySend(running.CancelledByPage
                ? AppMessageCodec.Error(request.Id, request.Method, RpcErrorCodes.Cancelled, "The request was cancelled.")
                : AppMessageCodec.Error(request.Id, request.Method, RpcErrorCodes.Timeout, "The host did not answer in time."));
        }
#pragma warning disable CA1031 // A failing handler must still answer (and must not take the window down).
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log($"The app request '{request.Method}' failed.", ex);
            TrySend(AppMessageCodec.Error(request.Id, request.Method, RpcErrorCodes.Internal, "The request failed."));
        }
        finally
        {
            if (work is null || work.IsCompleted)
            {
                Release(request.Id, running);
            }
            else
            {
                // The handler outlived its answer: keep its id reserved and log a late failure.
                _ = work.ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        _log($"The app request '{request.Method}' failed after it was answered.", task.Exception);
                    }

                    Release(request.Id, running);
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }
    }

    private void Release(int id, Running running)
    {
        _pending.TryRemove(new KeyValuePair<int, Running>(id, running));
        running.Dispose();
    }

    private void TrySend(string json)
    {
        try
        {
            _send(json);
        }
#pragma warning disable CA1031 // The page may already be gone (window closing): log, never throw.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log("Could not send an app message to the page.", ex);
        }
    }

    /// <summary>The cancellation state of one request: cancelled by the page, or timed out.</summary>
    private sealed class Running : IDisposable
    {
        private readonly CancellationTokenSource _cancelled = new();
        private readonly CancellationTokenSource _timedOut;
        private readonly CancellationTokenSource _linked;
        private int _disposed;

        public Running(TimeSpan timeout)
        {
            _timedOut = new CancellationTokenSource(timeout);
            _linked = CancellationTokenSource.CreateLinkedTokenSource(_cancelled.Token, _timedOut.Token);
            Token = _linked.Token;
        }

        public CancellationToken Token { get; }

        public bool CancelledByPage => _cancelled.IsCancellationRequested;

        public void Cancel()
        {
            try
            {
                _cancelled.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The request ended in the meantime.
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _linked.Dispose();
                _timedOut.Dispose();
                _cancelled.Dispose();
            }
        }
    }
}
