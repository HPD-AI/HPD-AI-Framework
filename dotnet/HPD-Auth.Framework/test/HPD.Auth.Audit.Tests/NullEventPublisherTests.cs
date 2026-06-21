using FluentAssertions;
using HPD.Auth.Audit.Tests.Helpers;
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;
using HPD.Events;
using HPD.Events.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Audit.Tests;

/// <summary>
/// When EnableAuditLog=false, no AuditingAuthObserver is registered, so events emitted
/// to the coordinator are simply not observed — no audit rows are written.
/// </summary>
public class NullEventPublisherTests
{
    [Fact]
    public async Task DisabledAuditLog_CoordinatorEmit_WritesNoAuditRows()
    {
        await using var sp = AuditTestFixture.Build(o => o.Features.EnableAuditLog = false);

        using var scope = sp.CreateScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<IEventCoordinator>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

        // No AuditingAuthObserver registered when audit is disabled —
        // emitting just queues the event with no observer to drain it.
        coordinator.Emit(new UserLoggedInEvent { UserId = Guid.NewGuid(), Email = "user@example.com" });

        var rows = await auditLogger.QueryAsync(new AuditLogQuery());
        rows.Should().BeEmpty();
    }

    [Fact]
    public void DisabledAuditLog_NoAuditingAuthObserver_Registered()
    {
        using var sp = AuditTestFixture.Build(o => o.Features.EnableAuditLog = false);
        using var scope = sp.CreateScope();

        var observer = scope.ServiceProvider
            .GetService<HPD.Auth.Audit.Services.AuditingAuthObserver>();

        observer.Should().BeNull(because: "AuditingAuthObserver is not registered when audit is disabled");
    }
}
