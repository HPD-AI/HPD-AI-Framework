using FluentAssertions;
using HPD.Auth.Audit.Observers;
using HPD.Auth.Audit.Services;
using HPD.Auth.Audit.Tests.Helpers;
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;
using HPD.Events;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HPD.Auth.Audit.Tests;

/// <summary>
/// Section 7: Concurrency Tests
/// </summary>
public class ConcurrencyTests
{
    [Fact]
    public async Task TwoConcurrentPublishes_WriteTwoIndependentAuditRows()
    {
        // Use separate DI scopes + separate DB names to avoid EF concurrency issues
        // with the in-memory provider. Each scope gets its own DbContext.
        await using var sp = AuditTestFixture.Build(o => o.Features.EnableAuditLog = true);

        var userId = Guid.NewGuid();

        var event1 = new UserLoggedInEvent { UserId = userId, Email = "a@test.com" };
        var event2 = new UserLoggedInEvent { UserId = userId, Email = "b@test.com" };

        await Task.WhenAll(
            PublishInScope(sp, event1),
            PublishInScope(sp, event2));

        // Query from a fresh scope
        using var readScope = sp.CreateScope();
        var auditLogger = readScope.ServiceProvider.GetRequiredService<IAuditLogger>();
        var rows = await auditLogger.QueryAsync(new AuditLogQuery(PageSize: 100));

        rows.Should().HaveCount(2);
        rows.Select(r => r.Id).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task TwentyEventsConcurrently_WritesTwentyAuditRows()
    {
        await using var sp = AuditTestFixture.Build(o => o.Features.EnableAuditLog = true);

        var events = Enumerable.Range(0, 20)
            .Select(_ => new UserLoggedInEvent { UserId = Guid.NewGuid(), Email = "u@test.com" })
            .ToList();

        await Task.WhenAll(events.Select(e => PublishInScope(sp, e)));

        using var readScope = sp.CreateScope();
        var auditLogger = readScope.ServiceProvider.GetRequiredService<IAuditLogger>();
        var rows = await auditLogger.QueryAsync(new AuditLogQuery(PageSize: 100));

        rows.Should().HaveCount(20);
    }

    [Fact]
    public async Task ConcurrentPublishWithThrowingObserver_AllAuditRowsWritten()
    {
        var observerMock = new Mock<IAuthEventObserver<UserLoggedInEvent>>();
        observerMock.Setup(o => o.ShouldProcess(It.IsAny<UserLoggedInEvent>())).Returns(true);
        observerMock
            .Setup(o => o.OnEventAsync(It.IsAny<UserLoggedInEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("observer boom"));

        await using var sp = AuditTestFixture.Build(
            o => o.Features.EnableAuditLog = true,
            builder => builder.Services.AddScoped<IAuthEventObserver<UserLoggedInEvent>>(
                _ => observerMock.Object));

        var events = Enumerable.Range(0, 10)
            .Select(_ => new UserLoggedInEvent { UserId = Guid.NewGuid(), Email = "u@test.com" })
            .ToList();

        await Task.WhenAll(events.Select(e => PublishInScope(sp, e)));

        using var readScope = sp.CreateScope();
        var auditLogger = readScope.ServiceProvider.GetRequiredService<IAuditLogger>();
        var rows = await auditLogger.QueryAsync(new AuditLogQuery(PageSize: 100));

        rows.Should().HaveCount(10,
            "handler failure must not prevent audit log rows from being written");
    }

    private static async Task PublishInScope(IServiceProvider sp, UserLoggedInEvent authEvent)
    {
        using var scope = sp.CreateScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<IEventCoordinator>();
        var observer    = scope.ServiceProvider.GetRequiredService<AuditingAuthObserver>();
        coordinator.Emit(authEvent);
        while (coordinator.TryRead(out var pending))
        {
            if (pending is AuthEvent ae && observer.ShouldProcess(ae))
                await observer.OnEventAsync(ae);
        }
    }
}
