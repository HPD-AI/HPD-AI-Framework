using FluentAssertions;
using HPD.Auth.Audit.Services;
using HPD.Auth.Core.Audit;
using HPD.Auth.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HPD.Auth.Audit.Tests;

public sealed class ClosedEventMappingTests
{
    public static TheoryData<AuthEvent, string> Mappings => new()
    {
        { new UserRegisteredEvent { UserId = Guid.NewGuid(), Email = "secret@example.test", RegistrationMethod = "email" }, "user.register" },
        { new UserLoggedInEvent { UserId = Guid.NewGuid(), Email = "secret@example.test", AuthMethod = "password" }, "user.login" },
        { new UserLoggedOutEvent { UserId = Guid.NewGuid(), SessionId = Guid.NewGuid() }, "user.logout" },
        { new LoginFailedEvent { Email = "secret@example.test", Reason = "hunter2" }, "user.login.failed" },
        { new PasswordChangedEvent { UserId = Guid.NewGuid() }, "password.change" },
        { new PasswordResetRequestedEvent { UserId = Guid.NewGuid(), Email = "secret@example.test" }, "password.reset.request" },
        { new EmailConfirmedEvent { UserId = Guid.NewGuid(), Email = "secret@example.test" }, "email.confirm" },
        { new TwoFactorEnabledEvent { UserId = Guid.NewGuid(), Method = "totp" }, "2fa.enable" },
        { new SessionRevokedEvent { UserId = Guid.NewGuid(), SessionId = Guid.NewGuid(), RevokedBy = "admin" }, "session.revoke" }
    };

    [Theory]
    [MemberData(nameof(Mappings))]
    public async Task ClosedEvent_ProducesExactSafeWrite(AuthEvent evt, string action)
    {
        AuthAuditWrite? captured = null;
        var writer = new Mock<IAuthAuditWriter>();
        writer.Setup(value => value.WriteAsync(It.IsAny<AuthAuditWrite>(), It.IsAny<CancellationToken>()))
            .Callback<AuthAuditWrite, CancellationToken>((write, _) => captured = write)
            .Returns(ValueTask.CompletedTask);
        using var services = new ServiceCollection().BuildServiceProvider();
        var observer = new AuditingAuthObserver(writer.Object, services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AuditingAuthObserver>.Instance);

        await observer.HandleAsync(evt);

        captured.Should().NotBeNull();
        captured!.Action.Should().Be(action);
        captured.ToString().Should().NotContain("secret@example.test").And.NotContain("hunter2");
    }
}
