using FluentAssertions;
using HPD.Auth.Audit.Observers;
using HPD.Auth.Audit.Services;
using HPD.Auth.Audit.Tests.Helpers;
using HPD.Auth.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HPD.Auth.Audit.Tests;

/// <summary>
/// AuditingAuthObserver fan-out — multiple IAuthEventObserver instances all receive the event.
/// (Replaces AuditInterceptor tests — the coordinator handles multi-publisher fan-out natively.)
/// </summary>
public class AuditInterceptorTests
{
    private static UserLoggedInEvent SampleEvent() => new()
    {
        UserId = Guid.NewGuid(),
        Email = "user@example.com"
    };

    [Fact]
    public async Task AllObservers_ReceiveEvent()
    {
        var obs1 = new Mock<IAuthEventObserver<UserLoggedInEvent>>();
        var obs2 = new Mock<IAuthEventObserver<UserLoggedInEvent>>();
        var obs3 = new Mock<IAuthEventObserver<UserLoggedInEvent>>();

        foreach (var o in new[] { obs1, obs2, obs3 })
        {
            o.Setup(x => x.ShouldProcess(It.IsAny<UserLoggedInEvent>())).Returns(true);
            o.Setup(x => x.OnEventAsync(It.IsAny<UserLoggedInEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        var (auditObserver, _) = AuditingObserverFactory.Create(services =>
        {
            services.AddSingleton<IAuthEventObserver<UserLoggedInEvent>>(obs1.Object);
            services.AddSingleton<IAuthEventObserver<UserLoggedInEvent>>(obs2.Object);
            services.AddSingleton<IAuthEventObserver<UserLoggedInEvent>>(obs3.Object);
        });

        var evt = SampleEvent();
        await auditObserver.OnEventAsync(evt);

        obs1.Verify(o => o.OnEventAsync(evt, It.IsAny<CancellationToken>()), Times.Once);
        obs2.Verify(o => o.OnEventAsync(evt, It.IsAny<CancellationToken>()), Times.Once);
        obs3.Verify(o => o.OnEventAsync(evt, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ThrowingObserver_DoesNotPreventSubsequentObservers()
    {
        var throwingObs = new Mock<IAuthEventObserver<UserLoggedInEvent>>();
        throwingObs.Setup(o => o.ShouldProcess(It.IsAny<UserLoggedInEvent>())).Returns(true);
        throwingObs.Setup(o => o.OnEventAsync(It.IsAny<UserLoggedInEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("obs 1 failed"));

        var successObs = new Mock<IAuthEventObserver<UserLoggedInEvent>>();
        successObs.Setup(o => o.ShouldProcess(It.IsAny<UserLoggedInEvent>())).Returns(true);
        successObs.Setup(o => o.OnEventAsync(It.IsAny<UserLoggedInEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var (auditObserver, _) = AuditingObserverFactory.Create(services =>
        {
            services.AddSingleton<IAuthEventObserver<UserLoggedInEvent>>(throwingObs.Object);
            services.AddSingleton<IAuthEventObserver<UserLoggedInEvent>>(successObs.Object);
        });

        await auditObserver.OnEventAsync(SampleEvent());

        successObs.Verify(
            o => o.OnEventAsync(It.IsAny<UserLoggedInEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ThrowingObserver_ExceptionDoesNotPropagateToCaller()
    {
        var throwingObs = new Mock<IAuthEventObserver<UserLoggedInEvent>>();
        throwingObs.Setup(o => o.ShouldProcess(It.IsAny<UserLoggedInEvent>())).Returns(true);
        throwingObs.Setup(o => o.OnEventAsync(It.IsAny<UserLoggedInEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var (auditObserver, _) = AuditingObserverFactory.Create(services =>
        {
            services.AddSingleton<IAuthEventObserver<UserLoggedInEvent>>(throwingObs.Object);
        });

        var act = async () => await auditObserver.OnEventAsync(SampleEvent());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NoObservers_CompletesSuccessfully()
    {
        var (auditObserver, _) = AuditingObserverFactory.Create();

        var act = async () => await auditObserver.OnEventAsync(SampleEvent());

        await act.Should().NotThrowAsync();
    }
}
