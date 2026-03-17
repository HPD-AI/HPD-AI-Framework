using FluentAssertions;
using HPD.Agent.Adapters.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Tests for the <see cref="AdapterRegistration"/> record.
/// </summary>
public class AdapterRegistrationTests
{
    // ── Construction ──────────────────────────────────────────────────

    [Fact]
    public void AdapterRegistration_ConstructsWithAllFields()
    {
        Func<IEndpointRouteBuilder, string?, IEndpointConventionBuilder> mapFn =
            (_, _) => null!;

        var reg = new AdapterRegistration(
            Name:         "slack",
            AdapterType:  typeof(object),
            MapEndpoint:  mapFn,
            DefaultPath:  "/webhooks/slack");

        reg.Name.Should().Be("slack");
        reg.AdapterType.Should().Be(typeof(object));
        reg.MapEndpoint.Should().BeSameAs(mapFn);
        reg.DefaultPath.Should().Be("/webhooks/slack");
    }

    // ── Record equality ───────────────────────────────────────────────

    [Fact]
    public void AdapterRegistration_RecordEquality_SameValues_AreEqual()
    {
        Func<IEndpointRouteBuilder, string?, IEndpointConventionBuilder> fn = (_, _) => null!;

        var a = new AdapterRegistration("slack", typeof(object), fn, "/webhooks/slack");
        var b = new AdapterRegistration("slack", typeof(object), fn, "/webhooks/slack");

        // Delegate equality is reference equality; same fn → equal
        a.Should().Be(b);
    }

    [Fact]
    public void AdapterRegistration_RecordEquality_DifferentName_NotEqual()
    {
        Func<IEndpointRouteBuilder, string?, IEndpointConventionBuilder> fn = (_, _) => null!;

        var a = new AdapterRegistration("slack", typeof(object), fn, "/webhooks/slack");
        var b = new AdapterRegistration("teams", typeof(object), fn, "/webhooks/teams");

        a.Should().NotBe(b);
    }

    // ── Delegate invocability ─────────────────────────────────────────

    [Fact]
    public void AdapterRegistration_MapEndpoint_DelegateIsInvokable()
    {
        var invoked = false;
        Func<IEndpointRouteBuilder, string?, IEndpointConventionBuilder> fn =
            (_, _) => { invoked = true; return null!; };

        var reg = new AdapterRegistration("slack", typeof(object), fn, "/webhooks/slack");
        reg.MapEndpoint(null!, null);

        invoked.Should().BeTrue();
    }

    [Fact]
    public void AdapterRegistration_MapEndpoint_ReceivesPathArgument()
    {
        string? receivedPath = null;
        Func<IEndpointRouteBuilder, string?, IEndpointConventionBuilder> fn =
            (_, path) => { receivedPath = path; return null!; };

        var reg = new AdapterRegistration("slack", typeof(object), fn, "/webhooks/slack");
        reg.MapEndpoint(null!, "/custom/path");

        receivedPath.Should().Be("/custom/path");
    }
}
