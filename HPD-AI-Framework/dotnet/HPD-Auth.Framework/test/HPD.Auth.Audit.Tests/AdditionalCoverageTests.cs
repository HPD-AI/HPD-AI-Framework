using FluentAssertions;
using HPD.Auth.Audit.Services;
using HPD.Auth.Audit.Tests.Helpers;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HPD.Auth.Audit.Tests;

/// <summary>
/// Additional coverage for gaps not addressed by the original test plan.
/// </summary>
public class AdditionalCoverageTests
{
    // ─── AuthContext IP/UserAgent enrichment ──────────────────────────────────

    [Fact]
    public async Task AuditingAuthObserver_AuthContextIpAddress_AppearsInWrittenEntry()
    {
        var (observer, auditLoggerMock) = AuditingObserverFactory.Create();
        AuditLogEntry? captured = null;
        auditLoggerMock
            .Setup(x => x.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        // IP comes from AuthContext, not a separate enricher
        var authEvent = new UserLoggedInEvent
        {
            UserId = Guid.NewGuid(),
            Email = "user@example.com",
            AuthContext = new AuthExecutionContext { IpAddress = "10.20.30.40", UserAgent = "TestAgent/1.0" }
        };

        await observer.OnEventAsync(authEvent);

        captured.Should().NotBeNull();
        captured!.IpAddress.Should().Be("10.20.30.40",
            "IpAddress should be taken from AuthContext when the audit entry has none");
        captured.UserAgent.Should().Be("TestAgent/1.0");
    }

    [Fact]
    public async Task AuditingAuthObserver_NullAuthContext_IpAddressIsNull()
    {
        var (observer, auditLoggerMock) = AuditingObserverFactory.Create();
        AuditLogEntry? captured = null;
        auditLoggerMock
            .Setup(x => x.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var authEvent = new UserLoggedInEvent
        {
            UserId = Guid.NewGuid(),
            Email = "user@example.com",
            AuthContext = null
        };

        await observer.OnEventAsync(authEvent);

        captured.Should().NotBeNull();
        captured!.IpAddress.Should().BeNull();
    }

    // ─── AuditingAuthObserver constructor null-guards ─────────────────────────

    [Fact]
    public void AuditingAuthObserver_NullAuditLogger_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILogger<AuditingAuthObserver>>();

        var act = () => new AuditingAuthObserver(null!, sp, logger);

        act.Should().Throw<ArgumentNullException>().WithParameterName("auditLogger");
    }

    [Fact]
    public void AuditingAuthObserver_NullServiceProvider_ThrowsArgumentNullException()
    {
        var auditLoggerMock = new Mock<IAuditLogger>();
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILogger<AuditingAuthObserver>>();

        var act = () => new AuditingAuthObserver(auditLoggerMock.Object, null!, logger);

        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void AuditingAuthObserver_NullLogger_ThrowsArgumentNullException()
    {
        var auditLoggerMock = new Mock<IAuditLogger>();
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var act = () => new AuditingAuthObserver(auditLoggerMock.Object, sp, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ─── AuthEventObserverBase constructor null-guard ─────────────────────────

    [Fact]
    public void AuthEventObserverBase_NullLogger_ThrowsArgumentNullException()
    {
        // TrackingObserver is the public concrete observer defined in AuthEventHandlerBaseTests.cs
        var act = () => new TrackingObserver(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ─── PasswordChangedEvent metadata ────────────────────────────────────────

    [Fact]
    public async Task PasswordChangedEvent_MetadataIsNull()
    {
        // PasswordChangedEvent has no extra fields, so Metadata should be null
        // on the AuditLogEntry (before serialization by the store).
        var (observer, auditLoggerMock) = AuditingObserverFactory.Create();
        AuditLogEntry? captured = null;
        auditLoggerMock
            .Setup(x => x.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        await observer.OnEventAsync(new PasswordChangedEvent { UserId = Guid.NewGuid() });

        captured.Should().NotBeNull();
        captured!.Metadata.Should().BeNull(
            "PasswordChangedEvent carries no extra fields, so Metadata should be null on AuditLogEntry");
    }
}
