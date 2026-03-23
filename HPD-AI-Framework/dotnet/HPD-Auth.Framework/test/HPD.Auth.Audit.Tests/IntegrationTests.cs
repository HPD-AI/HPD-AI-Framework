using FluentAssertions;
using HPD.Auth.Audit.Observers;
using HPD.Auth.Audit.Services;
using HPD.Auth.Audit.Tests.Helpers;
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;
using HPD.Events;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Text.Json;
using Xunit;

namespace HPD.Auth.Audit.Tests;

/// <summary>
/// Section 6: Integration Tests (in-memory EF Core)
/// </summary>
public class IntegrationTests
{
    private static async Task EmitAndDrainAsync(IEventCoordinator coordinator, AuditingAuthObserver observer, AuthEvent evt)
    {
        coordinator.Emit(evt);
        while (coordinator.TryRead(out var pending))
        {
            if (pending is AuthEvent authEvent && observer.ShouldProcess(authEvent))
                await observer.OnEventAsync(authEvent);
        }
    }

    [Fact]
    public async Task PublishUserLoggedInEvent_WritesCorrectAuditRow()
    {
        await using var sp = AuditTestFixture.Build(o => o.Features.EnableAuditLog = true);
        using var scope = sp.CreateScope();

        var coordinator = scope.ServiceProvider.GetRequiredService<IEventCoordinator>();
        var observer    = scope.ServiceProvider.GetRequiredService<AuditingAuthObserver>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

        var userId = Guid.NewGuid();
        var authEvent = new UserLoggedInEvent
        {
            UserId = userId,
            Email = "user@example.com",
            AuthMethod = "password"
        };

        await EmitAndDrainAsync(coordinator, observer, authEvent);

        var rows = await auditLogger.QueryAsync(new AuditLogQuery(PageSize: 100));

        rows.Should().HaveCount(1);
        var row = rows[0];
        row.Action.Should().Be("user.login");
        row.Category.Should().Be("authentication");
        row.UserId.Should().Be(userId);
        row.Success.Should().BeTrue();

        var metadata = JsonDocument.Parse(row.Metadata);
        metadata.RootElement.GetProperty("AuthMethod").GetString().Should().Be("password");
    }

    [Fact]
    public async Task PublishLoginFailedEvent_WritesAuditRowWithSuccessFalseAndErrorMessage()
    {
        await using var sp = AuditTestFixture.Build(o => o.Features.EnableAuditLog = true);
        using var scope = sp.CreateScope();

        var coordinator = scope.ServiceProvider.GetRequiredService<IEventCoordinator>();
        var observer    = scope.ServiceProvider.GetRequiredService<AuditingAuthObserver>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

        var authEvent = new LoginFailedEvent
        {
            Email = "x@example.com",
            Reason = "invalid_password",
            AuthContext = new AuthExecutionContext { IpAddress = "1.2.3.4" }
        };

        await EmitAndDrainAsync(coordinator, observer, authEvent);

        var rows = await auditLogger.QueryAsync(new AuditLogQuery(PageSize: 100));

        rows.Should().HaveCount(1);
        var row = rows[0];
        row.Action.Should().Be("user.login.failed");
        row.Success.Should().BeFalse();
        row.ErrorMessage.Should().Be("invalid_password");
        row.IpAddress.Should().Be("1.2.3.4");
    }

    [Fact]
    public async Task PublishSessionRevokedEvent_AuditRowContainsRevokedByInMetadata()
    {
        await using var sp = AuditTestFixture.Build(o => o.Features.EnableAuditLog = true);
        using var scope = sp.CreateScope();

        var coordinator = scope.ServiceProvider.GetRequiredService<IEventCoordinator>();
        var observer    = scope.ServiceProvider.GetRequiredService<AuditingAuthObserver>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

        var authEvent = new SessionRevokedEvent
        {
            UserId = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            RevokedBy = "admin"
        };

        await EmitAndDrainAsync(coordinator, observer, authEvent);

        var rows = await auditLogger.QueryAsync(new AuditLogQuery(PageSize: 100));

        rows.Should().HaveCount(1);
        // rows[0].Metadata is already a serialized JSON string — parse directly.
        var metadata = JsonDocument.Parse(rows[0].Metadata!);
        metadata.RootElement.GetProperty("RevokedBy").GetString().Should().Be("admin");
    }

    [Fact]
    public async Task PublishUnmappedEvent_NoAuditRow_ButObserverInvoked()
    {
        var observerMock = new Mock<IAuthEventObserver<TestUnknownEvent>>();
        observerMock.Setup(o => o.ShouldProcess(It.IsAny<TestUnknownEvent>())).Returns(true);
        observerMock
            .Setup(o => o.OnEventAsync(It.IsAny<TestUnknownEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var sp = AuditTestFixture.Build(
            o => o.Features.EnableAuditLog = true,
            builder => builder.Services.AddScoped<IAuthEventObserver<TestUnknownEvent>>(
                _ => observerMock.Object));

        using var scope = sp.CreateScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<IEventCoordinator>();
        var observer    = scope.ServiceProvider.GetRequiredService<AuditingAuthObserver>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

        var authEvent = new TestUnknownEvent();
        await EmitAndDrainAsync(coordinator, observer, authEvent);

        var rows = await auditLogger.QueryAsync(new AuditLogQuery(PageSize: 100));
        rows.Should().BeEmpty();

        observerMock.Verify(
            o => o.OnEventAsync(authEvent, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EnableAuditLogFalse_MultipleEmits_NoAuditRowsWritten()
    {
        await using var sp = AuditTestFixture.Build(o => o.Features.EnableAuditLog = false);
        using var scope = sp.CreateScope();

        var coordinator = scope.ServiceProvider.GetRequiredService<IEventCoordinator>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

        // When audit is disabled, AuditingAuthObserver is not registered —
        // events are emitted but never drained/observed.
        coordinator.Emit(new UserLoggedInEvent { UserId = Guid.NewGuid(), Email = "a@b.com" });
        coordinator.Emit(new LoginFailedEvent { Email = "a@b.com", Reason = "bad" });
        coordinator.Emit(new UserRegisteredEvent { UserId = Guid.NewGuid(), Email = "c@d.com" });

        var rows = await auditLogger.QueryAsync(new AuditLogQuery(PageSize: 100));
        rows.Should().BeEmpty();
    }
}
