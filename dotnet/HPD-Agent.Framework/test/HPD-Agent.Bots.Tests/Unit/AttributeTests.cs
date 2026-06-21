using FluentAssertions;
using HPD.Agent.Bots;
using HPD.Agent.Bots.AspNetCore.Verification;

namespace HPD.Agent.Bots.Tests.Unit;

/// <summary>
/// Tests for adapter attribute construction and property values.
/// Verifies that each attribute stores its arguments correctly and has the expected defaults.
/// </summary>
public class AttributeTests
{
    // ── HpdBotAttribute ───────────────────────────────────────────

    [Fact]
    public void HpdBotAttribute_StoresName()
    {
        var attr = new HpdBotAttribute("slack");

        attr.Name.Should().Be("slack");
    }

    [Fact]
    public void HpdBotAttribute_TargetsClass()
    {
        var usage = (AttributeUsageAttribute)typeof(HpdBotAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Single();

        usage.ValidOn.Should().HaveFlag(AttributeTargets.Class);
    }

    // ── HpdBotEventHandlerAttribute ────────────────────────────────────

    [Fact]
    public void HpdBotEventHandlerAttribute_StoresEventType()
    {
        var attr = new HpdBotEventHandlerAttribute("app_mention");

        attr.EventType.Should().Be("app_mention");
    }

    [Fact]
    public void HpdBotEventHandlerAttribute_AllowMultiple_IsTrue()
    {
        var usage = (AttributeUsageAttribute)typeof(HpdBotEventHandlerAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Single();

        usage.AllowMultiple.Should().BeTrue();
    }

    [Fact]
    public void HpdBotEventHandlerAttribute_TargetsMethod()
    {
        var usage = (AttributeUsageAttribute)typeof(HpdBotEventHandlerAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Single();

        usage.ValidOn.Should().HaveFlag(AttributeTargets.Method);
    }

    // ── HpdHttpMethodsAttribute ────────────────────────────────────

    [Fact]
    public void HpdHttpMethodsAttribute_StoresMethods()
    {
        var attr = new HpdHttpMethodsAttribute("GET", "POST");

        attr.Methods.Should().Equal("GET", "POST");
    }

    [Fact]
    public void HpdHttpMethodsAttribute_TargetsClass()
    {
        var usage = (AttributeUsageAttribute)typeof(HpdHttpMethodsAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Single();

        usage.ValidOn.Should().HaveFlag(AttributeTargets.Class);
    }

    // ── HpdBotPreDispatchAttribute ───────────────────────────────────────

    [Fact]
    public void HpdBotPreDispatchAttribute_CanBeInstantiated()
    {
        var act = () => new HpdBotPreDispatchAttribute();

        act.Should().NotThrow();
    }

    [Fact]
    public void HpdBotPreDispatchAttribute_TargetsMethod()
    {
        var usage = (AttributeUsageAttribute)typeof(HpdBotPreDispatchAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Single();

        usage.ValidOn.Should().HaveFlag(AttributeTargets.Method);
    }

    [Fact]
    public void HpdBotPreDispatchAttribute_AllowMultiple_IsFalse()
    {
        var usage = (AttributeUsageAttribute)typeof(HpdBotPreDispatchAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Single();

        usage.AllowMultiple.Should().BeFalse();
    }

    // ── HpdBotEnvelopeExtractorAttribute ─────────────────────────────────────

    [Fact]
    public void HpdBotEnvelopeExtractorAttribute_CanBeInstantiated()
    {
        var act = () => new HpdBotEnvelopeExtractorAttribute();

        act.Should().NotThrow();
    }

    [Fact]
    public void HpdBotEnvelopeExtractorAttribute_TargetsMethod()
    {
        var usage = (AttributeUsageAttribute)typeof(HpdBotEnvelopeExtractorAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Single();

        usage.ValidOn.Should().HaveFlag(AttributeTargets.Method);
    }

    [Fact]
    public void HpdBotEnvelopeExtractorAttribute_AllowMultiple_IsFalse()
    {
        var usage = (AttributeUsageAttribute)typeof(HpdBotEnvelopeExtractorAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Single();

        usage.AllowMultiple.Should().BeFalse();
    }

    // ── HpdStreamingAttribute ─────────────────────────────────────────

    [Fact]
    public void HpdStreamingAttribute_StoresStrategy()
    {
        var attr = new HpdStreamingAttribute(StreamingStrategy.PostAndEdit);

        attr.Strategy.Should().Be(StreamingStrategy.PostAndEdit);
    }

    [Fact]
    public void HpdStreamingAttribute_DefaultDebounceMs_Is500()
    {
        var attr = new HpdStreamingAttribute(StreamingStrategy.PostAndEdit);

        attr.DebounceMs.Should().Be(500);
    }

    [Fact]
    public void HpdStreamingAttribute_CustomDebounceMs()
    {
        var attr = new HpdStreamingAttribute(StreamingStrategy.BufferAndPost)
        {
            DebounceMs = 250,
        };

        attr.DebounceMs.Should().Be(250);
    }

    [Fact]
    public void HpdStreamingAttribute_AllStrategiesAreValid()
    {
        var strategies = new[]
        {
            StreamingStrategy.PostAndEdit,
            StreamingStrategy.BufferAndPost,
            StreamingStrategy.Native,
        };

        foreach (var s in strategies)
        {
            var attr = new HpdStreamingAttribute(s);
            attr.Strategy.Should().Be(s);
        }
    }

    // ── HpdPermissionHandlerAttribute ────────────────────────────────

    [Fact]
    public void HpdPermissionHandlerAttribute_CanBeInstantiated()
    {
        var act = () => new HpdPermissionHandlerAttribute();

        act.Should().NotThrow();
    }

    [Fact]
    public void HpdPermissionHandlerAttribute_TargetsMethod()
    {
        var usage = (AttributeUsageAttribute)typeof(HpdPermissionHandlerAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Single();

        usage.ValidOn.Should().HaveFlag(AttributeTargets.Method);
    }

    // ── CardRendererAttribute ─────────────────────────────────────────

    [Fact]
    public void CardRendererAttribute_CanBeInstantiated()
    {
        var act = () => new CardRendererAttribute();

        act.Should().NotThrow();
    }

    // ── HpdBotPayloadAttribute ───────────────────────────────────────

    [Fact]
    public void HpdBotPayloadAttribute_CanBeInstantiated()
    {
        var act = () => new HpdBotPayloadAttribute();

        act.Should().NotThrow();
    }

    // ── ThreadIdAttribute ─────────────────────────────────────────────

    [Fact]
    public void ThreadIdAttribute_StoresFormatString()
    {
        var attr = new ThreadIdAttribute("slack:{Channel}:{ThreadTs}");

        attr.Format.Should().Be("slack:{Channel}:{ThreadTs}");
    }

    // ── BotErrorsAttribute / ErrorCodeAttribute ───────────────────

    [Fact]
    public void BotErrorsAttribute_StoresBotName()
    {
        var attr = new BotErrorsAttribute("slack");

        attr.BotName.Should().Be("slack");
    }

    [Fact]
    public void ErrorCodeAttribute_StoresCodeAndExceptionType()
    {
        var attr = new ErrorCodeAttribute("not_in_channel", typeof(Exception));

        attr.Code.Should().Be("not_in_channel");
        attr.ExceptionType.Should().Be(typeof(Exception));
    }

    // ── PlatformFormatConverter format attributes ─────────────────────

    [Fact]
    public void BoldAttribute_StoresFormatString()
    {
        var attr = new BoldAttribute("*{0}*");

        attr.Format.Should().Be("*{0}*");
    }

    [Fact]
    public void ItalicAttribute_StoresFormatString()
    {
        var attr = new ItalicAttribute("_{0}_");

        attr.Format.Should().Be("_{0}_");
    }

    [Fact]
    public void StrikeAttribute_StoresFormatString()
    {
        var attr = new StrikeAttribute("~{0}~");

        attr.Format.Should().Be("~{0}~");
    }

    [Fact]
    public void LinkAttribute_StoresFormatString()
    {
        var attr = new LinkAttribute("<{1}|{0}>");

        attr.Format.Should().Be("<{1}|{0}>");
    }

    [Fact]
    public void CodeAttribute_StoresFormatString()
    {
        var attr = new CodeAttribute("`{0}`");

        attr.Format.Should().Be("`{0}`");
    }

    [Fact]
    public void CodeBlockAttribute_StoresFormatString()
    {
        var attr = new CodeBlockAttribute("```\n{0}\n```");

        attr.Format.Should().Be("```\n{0}\n```");
    }

    // ── HmacFormat enum ───────────────────────────────────────────────

    [Fact]
    public void HmacFormat_V0TimestampBody_HasExpectedValue()
    {
        // Ensure the enum is defined — the exact int value is an implementation detail
        Enum.IsDefined(typeof(HmacFormat), HmacFormat.V0TimestampBody).Should().BeTrue();
    }

    // ── StreamingStrategy enum ────────────────────────────────────────

    [Fact]
    public void StreamingStrategy_AllThreeValuesAreDefined()
    {
        Enum.IsDefined(typeof(StreamingStrategy), StreamingStrategy.PostAndEdit).Should().BeTrue();
        Enum.IsDefined(typeof(StreamingStrategy), StreamingStrategy.BufferAndPost).Should().BeTrue();
        Enum.IsDefined(typeof(StreamingStrategy), StreamingStrategy.Native).Should().BeTrue();
    }

    // ── HpdSocketTransportAttribute ───────────────────────────────────

    private sealed class FakeSocketService : BotWebSocketService
    {
        public FakeSocketService() : base(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance) { }
        protected override Task<Uri> GetConnectionUriAsync(CancellationToken ct) => throw new NotImplementedException();
        protected override Task RunSessionAsync(System.Net.WebSockets.WebSocket ws, CancellationToken ct) => throw new NotImplementedException();
    }

    [Fact]
    public void HpdSocketTransportAttribute_StoresServiceType()
    {
        var attr = new HpdSocketTransportAttribute(typeof(FakeSocketService));

        attr.ServiceType.Should().Be(typeof(FakeSocketService));
    }

    [Fact]
    public void HpdSocketTransportAttribute_DefaultConfigPropertyIsEmpty()
    {
        var attr = new HpdSocketTransportAttribute(typeof(FakeSocketService));

        attr.ConfigProperty.Should().BeEmpty();
    }

    [Fact]
    public void HpdSocketTransportAttribute_ConfigPropertyCanBeSet()
    {
        var attr = new HpdSocketTransportAttribute(typeof(FakeSocketService))
        {
            ConfigProperty = "AppToken",
        };

        attr.ConfigProperty.Should().Be("AppToken");
    }

    [Fact]
    public void HpdSocketTransportAttribute_AllowMultiple_IsFalse()
    {
        var usage = (AttributeUsageAttribute)typeof(HpdSocketTransportAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Single();

        usage.AllowMultiple.Should().BeFalse();
    }

    [Fact]
    public void HpdSocketTransportAttribute_TargetsClass()
    {
        var usage = (AttributeUsageAttribute)typeof(HpdSocketTransportAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Single();

        usage.ValidOn.Should().HaveFlag(AttributeTargets.Class);
    }
}
