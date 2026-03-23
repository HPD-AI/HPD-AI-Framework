using FluentAssertions;
using HPD.Agent.Bots.Contracts;

namespace HPD.Agent.Bots.Tests.Unit;

/// <summary>
/// Tests for the adapter exception hierarchy in <c>BotExceptions.cs</c>.
/// Verifies inheritance chain, constructor variants, and HTTP status mapping intent.
/// </summary>
public class BotExceptionsTests
{
    // ── Inheritance ───────────────────────────────────────────────────

    [Fact]
    public void BotAuthenticationException_IsBotException()
    {
        var ex = new BotAuthenticationException("test");

        ex.Should().BeAssignableTo<BotException>();
    }

    [Fact]
    public void BotRateLimitException_IsBotException()
    {
        var ex = new BotRateLimitException("test");

        ex.Should().BeAssignableTo<BotException>();
    }

    [Fact]
    public void BotPermissionException_IsBotException()
    {
        var ex = new BotPermissionException("test");

        ex.Should().BeAssignableTo<BotException>();
    }

    [Fact]
    public void BotNotFoundException_IsBotException()
    {
        var ex = new BotNotFoundException("test");

        ex.Should().BeAssignableTo<BotException>();
    }

    [Fact]
    public void BotException_IsSystemException()
    {
        // Every concrete subtype must ultimately derive from System.Exception
        new BotAuthenticationException("x").Should().BeAssignableTo<Exception>();
        new BotRateLimitException("x").Should().BeAssignableTo<Exception>();
        new BotPermissionException("x").Should().BeAssignableTo<Exception>();
        new BotNotFoundException("x").Should().BeAssignableTo<Exception>();
    }

    // ── Message constructor ───────────────────────────────────────────

    [Theory]
    [InlineData("auth error")]
    [InlineData("rate limit exceeded")]
    [InlineData("permission denied")]
    [InlineData("not found")]
    public void AllExceptions_MessageConstructor_SetsMessage(string message)
    {
        Exception[] exceptions =
        [
            new BotAuthenticationException(message),
            new BotRateLimitException(message),
            new BotPermissionException(message),
            new BotNotFoundException(message),
        ];

        exceptions.Should().AllSatisfy(ex => ex.Message.Should().Be(message));
    }

    // ── Inner exception constructor ───────────────────────────────────

    [Fact]
    public void BotAuthenticationException_InnerExceptionConstructor_SetsInner()
    {
        var inner = new InvalidOperationException("root cause");
        var ex    = new BotAuthenticationException("wrap", inner);

        ex.Message.Should().Be("wrap");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void BotRateLimitException_InnerExceptionConstructor_SetsInner()
    {
        var inner = new TimeoutException("slow");
        var ex    = new BotRateLimitException("rate limit", inner);

        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void BotPermissionException_InnerExceptionConstructor_SetsInner()
    {
        var inner = new UnauthorizedAccessException("denied");
        var ex    = new BotPermissionException("no permission", inner);

        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void BotNotFoundException_InnerExceptionConstructor_SetsInner()
    {
        var inner = new KeyNotFoundException("missing");
        var ex    = new BotNotFoundException("not found", inner);

        ex.InnerException.Should().BeSameAs(inner);
    }

    // ── Catchability ─────────────────────────────────────────────────

    [Fact]
    public void AllExceptions_CanBeCaughtAsBotException()
    {
        void ThrowAndCatch(Exception toThrow)
        {
            try { throw toThrow; }
            catch (BotException) { /* expected */ }
        }

        // Should not throw (i.e., catch block fires for all subtypes)
        var act = () =>
        {
            ThrowAndCatch(new BotAuthenticationException("x"));
            ThrowAndCatch(new BotRateLimitException("x"));
            ThrowAndCatch(new BotPermissionException("x"));
            ThrowAndCatch(new BotNotFoundException("x"));
        };

        act.Should().NotThrow();
    }

    // ── HTTP status intent (by type identity) ─────────────────────────

    [Fact]
    public void ExceptionTypeToHttpStatus_MappingIsDistinct()
    {
        // Each exception type maps to a different HTTP status.
        // The mapping lives in generated dispatch code; here we just verify
        // the four types are truly distinct types, not aliases.
        var types = new[]
        {
            typeof(BotAuthenticationException),
            typeof(BotRateLimitException),
            typeof(BotPermissionException),
            typeof(BotNotFoundException),
        };

        types.Distinct().Should().HaveCount(4);
    }
}
