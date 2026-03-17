using FluentAssertions;
using HPD.Auth.Audit.Extensions;
using HPD.Auth.Audit.Observers;
using HPD.Auth.Audit.Services;
using HPD.Auth.Audit.Tests.Helpers;
using HPD.Auth.Core.Events;
using HPD.Events;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Audit.Tests;

internal class TestLoginObserverA : IAuthEventObserver<UserLoggedInEvent>
{
    public bool ShouldProcess(UserLoggedInEvent evt) => true;
    public Task OnEventAsync(UserLoggedInEvent evt, CancellationToken ct = default) => Task.CompletedTask;
}

internal class TestLoginObserverB : IAuthEventObserver<UserLoggedInEvent>
{
    public bool ShouldProcess(UserLoggedInEvent evt) => true;
    public Task OnEventAsync(UserLoggedInEvent evt, CancellationToken ct = default) => Task.CompletedTask;
}

internal class TestSessionObserver : IAuthEventObserver<SessionRevokedEvent>
{
    public bool ShouldProcess(SessionRevokedEvent evt) => true;
    public Task OnEventAsync(SessionRevokedEvent evt, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// AddAudit() DI Registration
/// </summary>
public class DIRegistrationTests
{
    [Fact]
    public async Task EnableAuditLog_True_RegistersAuditingAuthObserver()
    {
        await using var sp = AuditTestFixture.Build(o => o.Features.EnableAuditLog = true);

        using var scope = sp.CreateScope();
        var observer = scope.ServiceProvider.GetService<AuditingAuthObserver>();

        observer.Should().NotBeNull();
    }

    [Fact]
    public async Task EnableAuditLog_False_DoesNotRegisterAuditingAuthObserver()
    {
        await using var sp = AuditTestFixture.Build(o => o.Features.EnableAuditLog = false);

        using var scope = sp.CreateScope();
        var observer = scope.ServiceProvider.GetService<AuditingAuthObserver>();

        observer.Should().BeNull();
    }

    [Fact]
    public async Task IEventCoordinator_IsRegisteredAsScoped()
    {
        await using var sp = AuditTestFixture.Build();

        using var scope = sp.CreateScope();
        var act = () => scope.ServiceProvider.GetRequiredService<IEventCoordinator>();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task AddAuthObserver_RegistersTypedObserver()
    {
        await using var sp = AuditTestFixture.Build(
            configureBuilder: builder =>
                builder.AddAuthObserver<UserLoggedInEvent, TestLoginObserverA>());

        using var scope = sp.CreateScope();
        var observers = scope.ServiceProvider
            .GetServices<IAuthEventObserver<UserLoggedInEvent>>()
            .ToList();

        observers.Should().HaveCount(1);
        observers[0].Should().BeOfType<TestLoginObserverA>();
    }

    [Fact]
    public async Task MultipleAddAuthObserver_RegistersAllObservers()
    {
        await using var sp = AuditTestFixture.Build(
            configureBuilder: builder => builder
                .AddAuthObserver<UserLoggedInEvent, TestLoginObserverA>()
                .AddAuthObserver<UserLoggedInEvent, TestLoginObserverB>());

        using var scope = sp.CreateScope();
        var observers = scope.ServiceProvider
            .GetServices<IAuthEventObserver<UserLoggedInEvent>>()
            .ToList();

        observers.Should().HaveCount(2);
        observers.OfType<TestLoginObserverA>().Should().HaveCount(1);
        observers.OfType<TestLoginObserverB>().Should().HaveCount(1);
    }

    [Fact]
    public async Task AddAuthObserver_DifferentEventTypes_DoNotCrossContaminate()
    {
        await using var sp = AuditTestFixture.Build(
            configureBuilder: builder => builder
                .AddAuthObserver<UserLoggedInEvent, TestLoginObserverA>()
                .AddAuthObserver<SessionRevokedEvent, TestSessionObserver>());

        using var scope = sp.CreateScope();

        var loginObservers = scope.ServiceProvider
            .GetServices<IAuthEventObserver<UserLoggedInEvent>>().ToList();
        var sessionObservers = scope.ServiceProvider
            .GetServices<IAuthEventObserver<SessionRevokedEvent>>().ToList();

        loginObservers.Should().HaveCount(1);
        loginObservers[0].Should().BeOfType<TestLoginObserverA>();
        sessionObservers.Should().HaveCount(1);
        sessionObservers[0].Should().BeOfType<TestSessionObserver>();
    }
}
