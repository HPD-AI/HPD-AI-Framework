using System.Reflection;
using FluentAssertions;
using HPD.Auth.Core.Events;
using HPD.Events;
using Xunit;

namespace HPD.Auth.Core.Tests.Events;

[Trait("Category", "Events")]
public class SpecificEventTests
{
    [Fact]
    public void UserRegisteredEvent_InheritsFromAuthEvent()
    {
        var evt = new UserRegisteredEvent { UserId = Guid.NewGuid(), Email = "a@b.com" };
        evt.Should().BeAssignableTo<AuthEvent>();
    }

    [Fact]
    public void UserLoggedInEvent_AuthMethod_DefaultsToPassword()
    {
        var evt = new UserLoggedInEvent { UserId = Guid.NewGuid(), Email = "a@b.com" };
        evt.AuthMethod.Should().Be("password");
    }

    [Fact]
    public void LoginFailedEvent_HasNoUserId()
    {
        typeof(LoginFailedEvent)
            .GetProperty("UserId", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull(because: "LoginFailedEvent intentionally omits UserId");
    }

    [Fact]
    public void SessionRevokedEvent_RevokedBy_IsRequired_VerifiedViaReflection()
    {
        var prop = typeof(SessionRevokedEvent).GetProperty(nameof(SessionRevokedEvent.RevokedBy));
        prop.Should().NotBeNull();

        var hasRequiredAttribute = prop!.GetCustomAttributes()
            .Any(a => a.GetType().Name == "RequiredMemberAttribute");

        hasRequiredAttribute.Should().BeTrue(
            because: "SessionRevokedEvent.RevokedBy must be marked required");
    }

    [Fact]
    public void TwoFactorEnabledEvent_Method_IsRequired_VerifiedViaReflection()
    {
        var prop = typeof(TwoFactorEnabledEvent).GetProperty(nameof(TwoFactorEnabledEvent.Method));
        prop.Should().NotBeNull();

        var hasRequiredAttribute = prop!.GetCustomAttributes()
            .Any(a => a.GetType().Name == "RequiredMemberAttribute");

        hasRequiredAttribute.Should().BeTrue(
            because: "TwoFactorEnabledEvent.Method must be marked required");
    }

    [Fact]
    public void AllEventTypes_InheritFromAuthEvent()
    {
        var eventTypes = new[]
        {
            typeof(UserRegisteredEvent),
            typeof(UserLoggedInEvent),
            typeof(UserLoggedOutEvent),
            typeof(LoginFailedEvent),
            typeof(PasswordChangedEvent),
            typeof(PasswordResetRequestedEvent),
            typeof(EmailConfirmedEvent),
            typeof(TwoFactorEnabledEvent),
            typeof(SessionRevokedEvent),
        };

        foreach (var type in eventTypes)
        {
            type.IsAssignableTo(typeof(AuthEvent)).Should().BeTrue(
                because: $"{type.Name} must inherit from AuthEvent");
        }
    }

    [Fact]
    public void LoginFailedEvent_Priority_IsControl()
    {
        var evt = new LoginFailedEvent { Email = "a@b.com", Reason = "invalid_password" };
        evt.Priority.Should().Be(EventPriority.Control);
    }

    [Fact]
    public void SessionRevokedEvent_Priority_IsControl()
    {
        var evt = new SessionRevokedEvent { UserId = Guid.NewGuid(), SessionId = Guid.NewGuid(), RevokedBy = "user" };
        evt.Priority.Should().Be(EventPriority.Control);
    }

    [Fact]
    public void UserLoggedInEvent_Priority_IsNormal()
    {
        var evt = new UserLoggedInEvent { UserId = Guid.NewGuid(), Email = "a@b.com" };
        evt.Priority.Should().Be(EventPriority.Normal);
    }
}
