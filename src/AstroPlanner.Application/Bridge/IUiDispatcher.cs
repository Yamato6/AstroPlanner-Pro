namespace AstroPlanner.Application.Bridge;

/// <summary>
/// The thread the view models belong to. View models and state services are not thread-safe:
/// like WPF objects, they are read and changed on one thread (the UI thread in the app). Work that
/// finishes elsewhere (a timer, a background task) comes back to it through <see cref="Post"/>.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>True on the view models' thread.</summary>
    bool CheckAccess();

    /// <summary>Runs <paramref name="action"/> on the view models' thread, later.</summary>
    void Post(Action action);
}

/// <summary>
/// An <see cref="IUiDispatcher"/> over a <see cref="SynchronizationContext"/> (WPF's dispatcher
/// context in the app, xUnit's in the tests). Without one, actions run inline.
/// </summary>
public sealed class SynchronizationContextUiDispatcher(SynchronizationContext? context) : IUiDispatcher
{
    /// <summary>The dispatcher of the calling thread (call it on the UI thread).</summary>
    public static SynchronizationContextUiDispatcher ForCurrentThread() => new(SynchronizationContext.Current);

    public bool CheckAccess() => context is null || SynchronizationContext.Current == context;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (context is null)
        {
            action();
        }
        else
        {
            context.Post(static state => ((Action)state!)(), action);
        }
    }
}
