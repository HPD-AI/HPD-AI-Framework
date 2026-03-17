using FluentAssertions;
using HPD.Agent.Adapters.Contracts;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Tests for the adapter exception hierarchy in <c>AdapterExceptions.cs</c>.
/// Verifies inheritance chain, constructor variants, and HTTP status mapping intent.
/// </summary>
public class AdapterExceptionsTests
{
    // ── Inheritance ───────────────────────────────────────────────────

    [Fact]
    public void AdapterAuthenticationException_IsAdapterException()
    {
        var ex = new AdapterAuthenticationException("test");

        ex.Should().BeAssignableTo<AdapterException>();
    }

    [Fact]
    public void AdapterRateLimitException_IsAdapterException()
    {
        var ex = new AdapterRateLimitException("test");

        ex.Should().BeAssignableTo<AdapterException>();
    }

    [Fact]
    public void AdapterPermissionException_IsAdapterException()
    {
        var ex = new AdapterPermissionException("test");

        ex.Should().BeAssignableTo<AdapterException>();
    }

    [Fact]
    public void AdapterNotFoundException_IsAdapterException()
    {
        var ex = new AdapterNotFoundException("test");

        ex.Should().BeAssignableTo<AdapterException>();
    }

    [Fact]
    public void AdapterException_IsSystemException()
    {
        // Every concrete subtype must ultimately derive from System.Exception
        new AdapterAuthenticationException("x").Should().BeAssignableTo<Exception>();
        new AdapterRateLimitException("x").Should().BeAssignableTo<Exception>();
        new AdapterPermissionException("x").Should().BeAssignableTo<Exception>();
        new AdapterNotFoundException("x").Should().BeAssignableTo<Exception>();
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
            new AdapterAuthenticationException(message),
            new AdapterRateLimitException(message),
            new AdapterPermissionException(message),
            new AdapterNotFoundException(message),
        ];

        exceptions.Should().AllSatisfy(ex => ex.Message.Should().Be(message));
    }

    // ── Inner exception constructor ───────────────────────────────────

    [Fact]
    public void AdapterAuthenticationException_InnerExceptionConstructor_SetsInner()
    {
        var inner = new InvalidOperationException("root cause");
        var ex    = new AdapterAuthenticationException("wrap", inner);

        ex.Message.Should().Be("wrap");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void AdapterRateLimitException_InnerExceptionConstructor_SetsInner()
    {
        var inner = new TimeoutException("slow");
        var ex    = new AdapterRateLimitException("rate limit", inner);

        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void AdapterPermissionException_InnerExceptionConstructor_SetsInner()
    {
        var inner = new UnauthorizedAccessException("denied");
        var ex    = new AdapterPermissionException("no permission", inner);

        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void AdapterNotFoundException_InnerExceptionConstructor_SetsInner()
    {
        var inner = new KeyNotFoundException("missing");
        var ex    = new AdapterNotFoundException("not found", inner);

        ex.InnerException.Should().BeSameAs(inner);
    }

    // ── Catchability ─────────────────────────────────────────────────

    [Fact]
    public void AllExceptions_CanBeCaughtAsAdapterException()
    {
        void ThrowAndCatch(Exception toThrow)
        {
            try { throw toThrow; }
            catch (AdapterException) { /* expected */ }
        }

        // Should not throw (i.e., catch block fires for all subtypes)
        var act = () =>
        {
            ThrowAndCatch(new AdapterAuthenticationException("x"));
            ThrowAndCatch(new AdapterRateLimitException("x"));
            ThrowAndCatch(new AdapterPermissionException("x"));
            ThrowAndCatch(new AdapterNotFoundException("x"));
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
            typeof(AdapterAuthenticationException),
            typeof(AdapterRateLimitException),
            typeof(AdapterPermissionException),
            typeof(AdapterNotFoundException),
        };

        types.Distinct().Should().HaveCount(4);
    }
}
