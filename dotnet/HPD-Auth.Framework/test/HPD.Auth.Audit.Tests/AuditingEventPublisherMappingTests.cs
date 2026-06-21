using FluentAssertions;
using HPD.Auth.Audit.Tests.Helpers;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Events;
using HPD.Auth.Core.Interfaces;
using Moq;
using System.Text.Json;
using Xunit;

namespace HPD.Auth.Audit.Tests;

/// <summary>
/// AuditingAuthObserver — Event-to-AuditAction Mapping
/// </summary>
public class AuditingEventPublisherMappingTests
{
    [Fact]
    public async Task UserLoggedInEvent_MapsToCorrectAuditEntry()
    {
        var (observer, auditLoggerMock) = AuditingObserverFactory.Create();
        AuditLogEntry? captured = null;
        auditLoggerMock
            .Setup(x => x.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var userId = Guid.NewGuid();
        var authEvent = new UserLoggedInEvent
        {
            UserId = userId,
            Email = "user@example.com",
            AuthMethod = "password",
            AuthContext = new AuthExecutionContext { IpAddress = "1.2.3.4", UserAgent = "TestAgent/1.0" },
        };

        await observer.OnEventAsync(authEvent);

        captured.Should().NotBeNull();
        captured!.Action.Should().Be(AuditActions.UserLogin);
        captured.Category.Should().Be(AuditCategories.Authentication);
        captured.UserId.Should().Be(userId);
        captured.IpAddress.Should().Be("1.2.3.4");
        captured.UserAgent.Should().Be("TestAgent/1.0");
        captured.Success.Should().BeTrue();

        var metadata = JsonSerializer.Serialize(captured.Metadata);
        metadata.Should().Contain("AuthMethod");
        metadata.Should().Contain("password");
    }

    [Fact]
    public async Task UserLoggedOutEvent_MapsToCorrectAuditEntry()
    {
        var (observer, auditLoggerMock) = AuditingObserverFactory.Create();
        AuditLogEntry? captured = null;
        auditLoggerMock
            .Setup(x => x.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var authEvent = new UserLoggedOutEvent { UserId = userId, SessionId = sessionId };

        await observer.OnEventAsync(authEvent);

        captured.Should().NotBeNull();
        captured!.Action.Should().Be(AuditActions.UserLogout);
        captured.Category.Should().Be(AuditCategories.Authentication);
        captured.UserId.Should().Be(userId);

        var metadata = JsonSerializer.Serialize(captured.Metadata);
        metadata.Should().Contain("SessionId");
    }

    [Fact]
    public async Task UserRegisteredEvent_MapsToCorrectAuditEntry()
    {
        var (observer, auditLoggerMock) = AuditingObserverFactory.Create();
        AuditLogEntry? captured = null;
        auditLoggerMock
            .Setup(x => x.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var userId = Guid.NewGuid();
        var authEvent = new UserRegisteredEvent { UserId = userId, Email = "new@example.com", RegistrationMethod = "email" };

        await observer.OnEventAsync(authEvent);

        captured.Should().NotBeNull();
        captured!.Action.Should().Be(AuditActions.UserRegister);
        captured.Category.Should().Be(AuditCategories.Authentication);
        captured.UserId.Should().Be(userId);

        var metadata = JsonSerializer.Serialize(captured.Metadata);
        metadata.Should().Contain("RegistrationMethod");
    }

    [Fact]
    public async Task LoginFailedEvent_MapsToCorrectAuditEntry()
    {
        var (observer, auditLoggerMock) = AuditingObserverFactory.Create();
        AuditLogEntry? captured = null;
        auditLoggerMock
            .Setup(x => x.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var authEvent = new LoginFailedEvent
        {
            Email = "attacker@example.com",
            Reason = "invalid_password",
            AuthContext = new AuthExecutionContext { IpAddress = "5.6.7.8" },
        };

        await observer.OnEventAsync(authEvent);

        captured.Should().NotBeNull();
        captured!.Action.Should().Be(AuditActions.UserLoginFailed);
        captured.Category.Should().Be(AuditCategories.Authentication);
        captured.UserId.Should().BeNull();
        captured.Success.Should().BeFalse();
        captured.IpAddress.Should().Be("5.6.7.8");
        captured.ErrorMessage.Should().Be("invalid_password");

        var metadata = JsonSerializer.Serialize(captured.Metadata);
        metadata.Should().Contain("Email");
        metadata.Should().Contain("attacker@example.com");
    }

    [Fact]
    public async Task PasswordChangedEvent_MapsToCorrectAuditEntry()
    {
        var (observer, auditLoggerMock) = AuditingObserverFactory.Create();
        AuditLogEntry? captured = null;
        auditLoggerMock
            .Setup(x => x.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var userId = Guid.NewGuid();
        await observer.OnEventAsync(new PasswordChangedEvent { UserId = userId });

        captured.Should().NotBeNull();
        captured!.Action.Should().Be(AuditActions.PasswordChange);
        captured.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task PasswordResetRequestedEvent_MapsToCorrectAuditEntry()
    {
        var (observer, auditLoggerMock) = AuditingObserverFactory.Create();
        AuditLogEntry? captured = null;
        auditLoggerMock
            .Setup(x => x.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var userId = Guid.NewGuid();
        await observer.OnEventAsync(new PasswordResetRequestedEvent { UserId = userId, Email = "reset@example.com" });

        captured.Should().NotBeNull();
        captured!.Action.Should().Be(AuditActions.PasswordResetRequest);
        captured.UserId.Should().Be(userId);
        JsonSerializer.Serialize(captured.Metadata).Should().Contain("Email");
    }

    [Fact]
    public async Task EmailConfirmedEvent_MapsToCorrectAuditEntry()
    {
        var (observer, auditLoggerMock) = AuditingObserverFactory.Create();
        AuditLogEntry? captured = null;
        auditLoggerMock
            .Setup(x => x.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var userId = Guid.NewGuid();
        await observer.OnEventAsync(new EmailConfirmedEvent { UserId = userId, Email = "confirmed@example.com" });

        captured.Should().NotBeNull();
        captured!.Action.Should().Be(AuditActions.EmailConfirm);
        captured.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task TwoFactorEnabledEvent_MapsToCorrectAuditEntry()
    {
        var (observer, auditLoggerMock) = AuditingObserverFactory.Create();
        AuditLogEntry? captured = null;
        auditLoggerMock
            .Setup(x => x.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var userId = Guid.NewGuid();
        await observer.OnEventAsync(new TwoFactorEnabledEvent { UserId = userId, Method = "totp" });

        captured.Should().NotBeNull();
        captured!.Action.Should().Be(AuditActions.TwoFactorEnable);
        captured.UserId.Should().Be(userId);
        JsonSerializer.Serialize(captured.Metadata).Should().Contain("totp");
    }

    [Fact]
    public async Task SessionRevokedEvent_MapsToCorrectAuditEntry()
    {
        var (observer, auditLoggerMock) = AuditingObserverFactory.Create();
        AuditLogEntry? captured = null;
        auditLoggerMock
            .Setup(x => x.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await observer.OnEventAsync(new SessionRevokedEvent { UserId = userId, SessionId = sessionId, RevokedBy = "admin" });

        captured.Should().NotBeNull();
        captured!.Action.Should().Be(AuditActions.SessionRevoke);
        captured.UserId.Should().Be(userId);
        var metadata = JsonSerializer.Serialize(captured.Metadata);
        metadata.Should().Contain("SessionId");
        metadata.Should().Contain("admin");
    }

    [Fact]
    public async Task UnknownEvent_DoesNotWriteAuditLog()
    {
        var (observer, auditLoggerMock) = AuditingObserverFactory.Create();

        await observer.OnEventAsync(new TestUnknownEvent());

        auditLoggerMock.Verify(
            x => x.LogAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
