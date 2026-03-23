using FluentAssertions;
using HPD.Auth.Audit.Observers;
using HPD.Auth.Core.Events;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HPD.Auth.Audit.Tests;

// ─── Concrete test observer implementations ────────────────────────────────────

public sealed class TrackingObserver : AuthEventObserverBase<UserLoggedInEvent>
{
    public bool WasCalled { get; private set; }

    public TrackingObserver(ILogger<TrackingObserver> logger) : base(logger) { }

    protected override Task ExecuteAsync(UserLoggedInEvent evt, CancellationToken ct)
    {
        WasCalled = true;
        return Task.CompletedTask;
    }
}

public sealed class ThrowingObserver : AuthEventObserverBase<UserLoggedInEvent>
{
    public ThrowingObserver(ILogger<ThrowingObserver> logger) : base(logger) { }

    protected override Task ExecuteAsync(UserLoggedInEvent evt, CancellationToken ct)
        => throw new InvalidOperationException("observer failed");
}

public sealed class CancellingObserver : AuthEventObserverBase<UserLoggedInEvent>
{
    public CancellingObserver(ILogger<CancellingObserver> logger) : base(logger) { }

    protected override Task ExecuteAsync(UserLoggedInEvent evt, CancellationToken ct)
        => throw new OperationCanceledException("cancelled");
}

/// <summary>
/// AuthEventObserverBase — exception safety and lifecycle
/// </summary>
public class AuthEventHandlerBaseTests
{
    private static UserLoggedInEvent SampleEvent() => new()
    {
        UserId = Guid.NewGuid(),
        Email = "user@example.com"
    };

    [Fact]
    public async Task OnEventAsync_InvokesExecuteAsync()
    {
        var logger = new Mock<ILogger<TrackingObserver>>();
        var observer = new TrackingObserver(logger.Object);

        await observer.OnEventAsync(SampleEvent());

        observer.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task OnEventAsync_ExceptionInExecuteAsync_IsSwallowed()
    {
        var logger = new Mock<ILogger<ThrowingObserver>>();
        var observer = new ThrowingObserver(logger.Object);

        var act = async () => await observer.OnEventAsync(SampleEvent());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnEventAsync_ExceptionInExecuteAsync_IsLoggedAtErrorLevel()
    {
        var logger = new Mock<ILogger<ThrowingObserver>>();
        var observer = new ThrowingObserver(logger.Object);

        await observer.OnEventAsync(SampleEvent());

        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task OnEventAsync_OperationCanceledException_PropagatesWhenCancelled()
    {
        var logger = new Mock<ILogger<CancellingObserver>>();
        var observer = new CancellingObserver(logger.Object);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await observer.OnEventAsync(SampleEvent(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
