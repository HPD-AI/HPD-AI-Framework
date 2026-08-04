using FluentAssertions;
using HPD.Auth.Admin;
using HPD.Auth.Core.Audit;
using Xunit;

namespace HPD.Auth.Admin.Tests;

public sealed class AdminAuditCorrelationTests
{
    [Fact]
    public async Task Custom_writer_receives_mapper_owned_correlation()
    {
        const string correlation = "admin-request-01HZX8Y7";
        var writer = new CapturingWriter();

        await AdminAuditMapper.WriteAsync(
            writer,
            AdminAuditOperation.UserView,
            new FixedCorrelationContext(correlation),
            Guid.NewGuid());

        writer.Write!.CorrelationId.Should().Be(correlation);
        writer.Write.CorrelationId.Should().NotBeSameAs(correlation);
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

    private sealed class FixedCorrelationContext(string correlationId) : IAuthCorrelationContext
    {
        public string? CorrelationId { get; } = correlationId;
    }
}
