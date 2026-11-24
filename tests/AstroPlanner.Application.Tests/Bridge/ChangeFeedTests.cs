using AstroPlanner.Application.Bridge;

namespace AstroPlanner.Application.Tests.Bridge;

/// <summary>A burst of changes becomes one event with the latest revision, on the UI thread.</summary>
public sealed class ChangeFeedTests
{
    private readonly ManualTimeProvider _clock = new(DateTimeOffset.UnixEpoch);
    private readonly QueueDispatcher _ui = new();
    private readonly List<int> _published = [];

    private ChangeFeed Create() => new(_ui, _clock, _published.Add);

    [Fact]
    public void ABurst_IsPublishedOnce_WithTheLatestRevision_OnTheUiThread()
    {
        var feed = Create();
        feed.Revision.Should().Be(0);

        feed.Invalidate();
        feed.Invalidate();
        feed.Invalidate();
        _published.Should().BeEmpty("nothing is sent before the delay");

        _clock.LastTimer!.Fire();
        _published.Should().BeEmpty("the timer only posts to the UI thread");
        _ui.RunAll();

        _published.Should().Equal(3);
        feed.Revision.Should().Be(3);
    }

    [Fact]
    public void AChangeAfterAPublish_StartsANewBurst()
    {
        var feed = Create();
        feed.Invalidate();
        _clock.LastTimer!.Fire();
        _ui.RunAll();
        var first = _clock.LastTimer;

        feed.Invalidate();
        _clock.LastTimer.Should().NotBeSameAs(first);
        first.IsDisposed.Should().BeTrue();
        _clock.LastTimer!.Fire();
        _ui.RunAll();

        _published.Should().Equal(1, 2);
    }

    [Fact]
    public void Dispose_DropsThePendingEvent_AndIgnoresLaterChanges()
    {
        var feed = Create();
        feed.Invalidate();
        var timer = _clock.LastTimer!;

        feed.Dispose();
        timer.Fire();
        _ui.RunAll();
        feed.Invalidate();

        _published.Should().BeEmpty();
        timer.IsDisposed.Should().BeTrue();
        feed.Revision.Should().Be(1);
    }

    /// <summary>A UI thread the test pumps by hand.</summary>
    private sealed class QueueDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _actions = new();

        public bool CheckAccess() => true;

        public void Post(Action action) => _actions.Enqueue(action);

        public void RunAll()
        {
            while (_actions.TryDequeue(out var action))
            {
                action();
            }
        }
    }
}
