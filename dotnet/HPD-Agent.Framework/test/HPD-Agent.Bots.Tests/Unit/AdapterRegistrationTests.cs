using FluentAssertions;
using HPD.Agent.Bots.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.Bots.Tests.Unit;

/// <summary>
/// Tests for the <see cref="BotRegistration"/> record.
/// </summary>
public class BotRegistrationTests
{
    // ── Construction ──────────────────────────────────────────────────

    [Fact]
    public void BotRegistration_ConstructsWithAllFields()
    {
        Func<IEndpointRouteBuilder, string?, IEndpointConventionBuilder> mapFn =
            (_, _) => null!;

        var reg = new BotRegistration(
            Name:         "slack",
            BotType:  typeof(object),
            MapEndpoint:  mapFn,
            DefaultPath:  "/webhooks/slack");

        reg.Name.Should().Be("slack");
        reg.BotType.Should().Be(typeof(object));
        reg.MapEndpoint.Should().BeSameAs(mapFn);
        reg.DefaultPath.Should().Be("/webhooks/slack");
    }

    // ── Record equality ───────────────────────────────────────────────

    [Fact]
    public void BotRegistration_RecordEquality_SameValues_AreEqual()
    {
        Func<IEndpointRouteBuilder, string?, IEndpointConventionBuilder> fn = (_, _) => null!;

        var a = new BotRegistration("slack", typeof(object), fn, "/webhooks/slack");
        var b = new BotRegistration("slack", typeof(object), fn, "/webhooks/slack");

        // Delegate equality is reference equality; same fn → equal
        a.Should().Be(b);
    }

    [Fact]
    public void BotRegistration_RecordEquality_DifferentName_NotEqual()
    {
        Func<IEndpointRouteBuilder, string?, IEndpointConventionBuilder> fn = (_, _) => null!;

        var a = new BotRegistration("slack", typeof(object), fn, "/webhooks/slack");
        var b = new BotRegistration("teams", typeof(object), fn, "/webhooks/teams");

        a.Should().NotBe(b);
    }

    // ── Delegate invocability ─────────────────────────────────────────

    [Fact]
    public void BotRegistration_MapEndpoint_DelegateIsInvokable()
    {
        var invoked = false;
        Func<IEndpointRouteBuilder, string?, IEndpointConventionBuilder> fn =
            (_, _) => { invoked = true; return null!; };

        var reg = new BotRegistration("slack", typeof(object), fn, "/webhooks/slack");
        reg.MapEndpoint(null!, null);

        invoked.Should().BeTrue();
    }

    [Fact]
    public void BotRegistration_MapEndpoint_ReceivesPathArgument()
    {
        string? receivedPath = null;
        Func<IEndpointRouteBuilder, string?, IEndpointConventionBuilder> fn =
            (_, path) => { receivedPath = path; return null!; };

        var reg = new BotRegistration("slack", typeof(object), fn, "/webhooks/slack");
        reg.MapEndpoint(null!, "/custom/path");

        receivedPath.Should().Be("/custom/path");
    }
}
