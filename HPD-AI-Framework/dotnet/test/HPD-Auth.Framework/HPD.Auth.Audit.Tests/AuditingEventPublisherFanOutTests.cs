using FluentAssertions;
using HPD.Auth.Audit.Observers;
using HPD.Auth.Audit.Tests.Helpers;
using HPD.Auth.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HPD.Auth.Audit.Tests;

/// <summary>
/// AuditingAuthObserver — Fan-out to registered IAuthEventObserver&lt;TEvent&gt; instances.
/// </summary>
public class AuditingEventPublisherFanOutTests
{
    [Fact]
    public async Task SingleObserver_IsCalledOnce()
    {
        var observerMock = new Mock<IAuthEventObserver<UserLoggedInEvent>>();
        observerMock.Setup(o => o.ShouldProcess(It.IsAny<UserLoggedInEvent>())).Returns(true);
        observerMock.Setup(o => o.OnEventAsync(It.IsAny<UserLoggedInEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var (auditObserver, _) = AuditingObserverFactory.Create(services =>
        {
            services.AddSingleton<IAuthEventObserver<UserLoggedInEvent>>(observerMock.Object);
        });

        var authEvent = new UserLoggedInEvent { UserId = Guid.NewGuid(), Email = "user@example.com" };

        await auditObserver.OnEventAsync(authEvent);

        observerMock.Verify(o => o.OnEventAsync(authEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MultipleObservers_AllReceiveEvent()
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

        var authEvent = new UserLoggedInEvent { UserId = Guid.NewGuid(), Email = "user@example.com" };
        await auditObserver.OnEventAsync(authEvent);

        obs1.Verify(o => o.OnEventAsync(authEvent, It.IsAny<CancellationToken>()), Times.Once);
        obs2.Verify(o => o.OnEventAsync(authEvent, It.IsAny<CancellationToken>()), Times.Once);
        obs3.Verify(o => o.OnEventAsync(authEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ThrowingObserver_DoesNotPreventOtherObservers()
    {
        var throwingObs = new Mock<IAuthEventObserver<UserLoggedInEvent>>();
        throwingObs.Setup(o => o.ShouldProcess(It.IsAny<UserLoggedInEvent>())).Returns(true);
        throwingObs.Setup(o => o.OnEventAsync(It.IsAny<UserLoggedInEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("observer A failed"));

        var successObs = new Mock<IAuthEventObserver<UserLoggedInEvent>>();
        successObs.Setup(o => o.ShouldProcess(It.IsAny<UserLoggedInEvent>())).Returns(true);
        successObs.Setup(o => o.OnEventAsync(It.IsAny<UserLoggedInEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var (auditObserver, _) = AuditingObserverFactory.Create(services =>
        {
            services.AddSingleton<IAuthEventObserver<UserLoggedInEvent>>(throwingObs.Object);
            services.AddSingleton<IAuthEventObserver<UserLoggedInEvent>>(successObs.Object);
        });

        var authEvent = new UserLoggedInEvent { UserId = Guid.NewGuid(), Email = "user@example.com" };
        await auditObserver.OnEventAsync(authEvent);

        successObs.Verify(o => o.OnEventAsync(authEvent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ThrowingObserver_ExceptionDoesNotPropagateToOnEventAsync()
    {
        var throwingObs = new Mock<IAuthEventObserver<UserLoggedInEvent>>();
        throwingObs.Setup(o => o.ShouldProcess(It.IsAny<UserLoggedInEvent>())).Returns(true);
        throwingObs.Setup(o => o.OnEventAsync(It.IsAny<UserLoggedInEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var (auditObserver, _) = AuditingObserverFactory.Create(services =>
        {
            services.AddSingleton<IAuthEventObserver<UserLoggedInEvent>>(throwingObs.Object);
        });

        var authEvent = new UserLoggedInEvent { UserId = Guid.NewGuid(), Email = "user@example.com" };
        var act = async () => await auditObserver.OnEventAsync(authEvent);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NoObservers_CompletesSuccessfully()
    {
        var (auditObserver, _) = AuditingObserverFactory.Create();

        var act = async () => await auditObserver.OnEventAsync(
            new UserLoggedInEvent { UserId = Guid.NewGuid(), Email = "user@example.com" });

        await act.Should().NotThrowAsync();
    }
}
