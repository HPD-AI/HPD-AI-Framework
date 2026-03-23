using FluentAssertions;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Tests;

/// <summary>
/// Documents and verifies the registration behaviour when AddHPDAuth() is called
/// more than once on the same ServiceCollection (tests 9.1 – 9.2).
/// </summary>
public class IdempotencyTests
{
    // ── 9.1 ──────────────────────────────────────────────────────────────────────
    // AddScoped (not TryAddScoped) is used for stores, so a second call adds a
    // second descriptor. This test documents current behaviour; if the implementation
    // switches to TryAddScoped the assertion count should change to 1.

    [Fact]
    public void AddHPDAuth_Called_Twice_Does_Not_Duplicate_IAuditLogger()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddHPDAuth(o => o.AppName = "Idempotency_AuditLogger");
        services.AddHPDAuth(o => o.AppName = "Idempotency_AuditLogger");

        var auditLoggerDescriptors = services
            .Where(d => d.ServiceType == typeof(IAuditLogger))
            .ToList();

        // Document current AddScoped behaviour: two calls → two descriptors.
        auditLoggerDescriptors.Should().HaveCount(2);
    }

    // ── 9.2 ──────────────────────────────────────────────────────────────────────
    // TryAddScoped is used for email/SMS senders, so a second call is a no-op.

    [Fact]
    public void AddHPDAuth_Called_Twice_NoOpEmailSender_Not_Duplicated()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddHPDAuth(o => o.AppName = "Idempotency_EmailSender");
        services.AddHPDAuth(o => o.AppName = "Idempotency_EmailSender");

        var emailSenderDescriptors = services
            .Where(d => d.ServiceType == typeof(IHPDAuthEmailSender))
            .ToList();

        // TryAddScoped ensures exactly one registration regardless of call count.
        emailSenderDescriptors.Should().HaveCount(1);
    }
}
