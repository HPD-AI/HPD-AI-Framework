using HPD.Auth.Audit.Extensions;
using HPD.Auth.Audit.Observers;
using HPD.Auth.Audit.Services;
using HPD.Auth.Builder;
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Options;
using HPD.Auth.Extensions;
using HPD.Events;
using HPD.Events.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace HPD.Auth.Audit.Tests.Helpers;

/// <summary>
/// Shared fixture for building a real IServiceProvider with HPD.Auth + HPD.Auth.Audit.
/// </summary>
public static class AuditTestFixture
{
    public static ServiceProvider Build(
        Action<HPDAuthOptions>? configureOptions = null,
        Action<IHPDAuthBuilder>? configureBuilder = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = services.AddHPDAuth(o =>
        {
            o.AppName = "AuditTest-" + Guid.NewGuid();
            o.Features.EnableAuditLog = true;
            configureOptions?.Invoke(o);
        });

        builder.AddAudit();
        configureBuilder?.Invoke(builder);

        return services.BuildServiceProvider();
    }
}

/// <summary>
/// Builds a minimal AuditingAuthObserver with a mock IAuditLogger and a real EventCoordinator.
/// </summary>
public static class AuditingObserverFactory
{
    public static (AuditingAuthObserver Observer, Mock<IAuditLogger> AuditLoggerMock) Create(
        Action<IServiceCollection>? configureObservers = null)
    {
        var auditLoggerMock = new Mock<IAuditLogger>();
        auditLoggerMock
            .Setup(x => x.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddLogging();
        configureObservers?.Invoke(services);
        var sp = services.BuildServiceProvider();

        var logger = sp.GetRequiredService<ILogger<AuditingAuthObserver>>();
        var observer = new AuditingAuthObserver(auditLoggerMock.Object, sp, logger);

        return (observer, auditLoggerMock);
    }
}

/// <summary>Minimal custom event for unmapped-event tests.</summary>
public record TestUnknownEvent : AuthEvent { }
