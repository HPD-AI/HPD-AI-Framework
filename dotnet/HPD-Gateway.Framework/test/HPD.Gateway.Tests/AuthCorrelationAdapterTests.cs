using FluentAssertions;
using HPD.Auth.Audit.Services;
using HPD.Auth.Core.Audit;
using HPD.Auth.Core.Events;
using HPD.Gateway.HPDAuth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class AuthCorrelationAdapterTests
{
    [Fact]
    public async Task Auth_audit_and_Gateway_receive_the_same_immutable_correlation()
    {
        const string value = "shared-request-01HZX8Y7";
        var correlation = new FixedCorrelationContext(value);
        var writer = new CapturingWriter();
        using var services = new ServiceCollection().BuildServiceProvider();
        var observer = new AuditingAuthObserver(
            writer,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AuditingAuthObserver>.Instance,
            correlation);

        await observer.HandleAsync(new PasswordChangedEvent { UserId = Guid.NewGuid() });
        var gatewayValue = correlation.RequireGatewayCorrelation();

        writer.Write!.CorrelationId.Should().Be(value);
        gatewayValue.Should().Be(value);
        writer.Write.CorrelationId.Should().NotBeSameAs(value);
        gatewayValue.Should().NotBeSameAs(value);
    }

    private sealed class CapturingWriter : IAuthAuditWriter
    {
        public AuthAuditWrite? Write { get; private set; }
        public ValueTask WriteAsync(AuthAuditWrite write, CancellationToken cancellationToken = default)
        {
            Write = write;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedCorrelationContext(string value) : IAuthCorrelationContext
    {
        public string? CorrelationId { get; } = value;
    }
}
