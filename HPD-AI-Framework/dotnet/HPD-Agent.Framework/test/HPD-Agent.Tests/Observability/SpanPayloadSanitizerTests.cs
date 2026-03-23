// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.Observability;

/// <summary>
/// Unit tests for <see cref="SpanPayloadSanitizer"/>.
/// Covers sensitive-field redaction and length-cap behaviour.
/// </summary>
public class SpanPayloadSanitizerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SpanPayloadSanitizer Default() => new();

    private static SpanPayloadSanitizer WithOptions(SpanSanitizerOptions opts) => new(opts);

    // ── Null / empty input ────────────────────────────────────────────────────

    [Fact]
    public void Sanitize_NullInput_ReturnsEmptyString()
    {
        Default().Sanitize(null).Should().Be(string.Empty);
    }

    [Fact]
    public void Sanitize_EmptyString_ReturnsEmptyString()
    {
        Default().Sanitize("").Should().Be(string.Empty);
    }

    // ── Non-JSON pass-through ─────────────────────────────────────────────────

    [Fact]
    public void Sanitize_PlainText_ReturnsUnchanged()
    {
        const string text = "This is a plain text result with no JSON.";
        Default().Sanitize(text).Should().Be(text);
    }

    [Fact]
    public void Sanitize_MalformedJson_ReturnsUnchanged()
    {
        const string broken = "{ not valid json at all }}}";
        Default().Sanitize(broken).Should().Be(broken);
    }

    // ── Redaction — exact key matches ─────────────────────────────────────────

    [Theory]
    [InlineData("password")]
    [InlineData("passwd")]
    [InlineData("pwd")]
    [InlineData("token")]
    [InlineData("accesstoken")]
    [InlineData("secret")]
    [InlineData("clientsecret")]
    [InlineData("apikey")]
    [InlineData("api_key")]
    [InlineData("auth")]
    [InlineData("authorization")]
    [InlineData("bearer")]
    [InlineData("bearertoken")]
    [InlineData("jwt")]
    [InlineData("credential")]
    [InlineData("credentials")]
    [InlineData("privatekey")]
    [InlineData("private_key")]
    [InlineData("ssn")]
    [InlineData("key")]
    public void Sanitize_SensitiveKey_ValueRedacted(string key)
    {
        var json = $$"""{"{{key}}": "super-secret-value"}""";
        var result = Default().Sanitize(json);

        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("super-secret-value");
    }

    [Fact]
    public void Sanitize_PasswordKey_ExactJson()
    {
        var result = Default().Sanitize("""{"password": "abc123"}""");
        result.Should().Be("""{"password":"[REDACTED]"}""");
    }

    [Fact]
    public void Sanitize_ApiKeyWithUnderscore_Redacted()
    {
        var result = Default().Sanitize("""{"api_key": "sk-xxx"}""");
        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("sk-xxx");
    }

    // ── Redaction — case and separator variants ───────────────────────────────

    [Fact]
    public void Sanitize_CamelCaseApiKey_Redacted()
    {
        // "apiKey" normalises to "apikey" which is in the sensitive set
        var result = Default().Sanitize("""{"apiKey": "sk-xxx"}""");
        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("sk-xxx");
    }

    [Fact]
    public void Sanitize_KebabCaseApiKey_Redacted()
    {
        // "api-key" normalises to "apikey"
        var result = Default().Sanitize("""{"api-key": "sk-xxx"}""");
        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("sk-xxx");
    }

    [Fact]
    public void Sanitize_UpperCasePassword_Redacted()
    {
        var result = Default().Sanitize("""{"PASSWORD": "hunter2"}""");
        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("hunter2");
    }

    // ── Redaction — non-sensitive keys untouched ──────────────────────────────

    [Fact]
    public void Sanitize_NonSensitiveKeys_Preserved()
    {
        var json = """{"username": "alice", "email": "alice@example.com", "age": 30}""";
        var result = Default().Sanitize(json);

        result.Should().Contain("alice");
        result.Should().Contain("alice@example.com");
        result.Should().Contain("30");
    }

    [Fact]
    public void Sanitize_MixedObject_RedactsSensitivePreservesRest()
    {
        var json = """{"username": "alice", "password": "secret"}""";
        var result = Default().Sanitize(json);

        result.Should().Contain("alice");
        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("secret");
    }

    // ── Redaction — nested structures ─────────────────────────────────────────

    [Fact]
    public void Sanitize_NestedObject_DeepRedaction()
    {
        var json = """{"config": {"token": "xyz", "timeout": 30}}""";
        var result = Default().Sanitize(json);

        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("xyz");
        result.Should().Contain("30");
    }

    [Fact]
    public void Sanitize_ArrayOfObjects_RedactsInEachElement()
    {
        var json = """[{"key": "val1"}, {"key": "val2"}, {"name": "safe"}]""";
        var result = Default().Sanitize(json);

        result.Should().NotContain("val1");
        result.Should().NotContain("val2");
        result.Should().Contain("safe");
    }

    [Fact]
    public void Sanitize_DeeplyNested_RedactsAtAnyDepth()
    {
        var json = """{"a": {"b": {"c": {"password": "deep-secret"}}}}""";
        var result = Default().Sanitize(json);

        result.Should().NotContain("deep-secret");
        result.Should().Contain("[REDACTED]");
    }

    // ── Length cap ────────────────────────────────────────────────────────────

    [Fact]
    public void Sanitize_StringExceedsDefault4096Cap_Truncated()
    {
        var longText = new string('x', 5000);
        var result = Default().Sanitize(longText);

        result.Should().EndWith(" [truncated]");
        result.Length.Should().Be(4096 + " [truncated]".Length);
    }

    [Fact]
    public void Sanitize_StringUnderCap_Unchanged()
    {
        var text = new string('x', 100);
        Default().Sanitize(text).Should().Be(text);
    }

    [Fact]
    public void Sanitize_StringExactlyAtCap_NotTruncated()
    {
        var text = new string('x', 4096);
        Default().Sanitize(text).Should().Be(text);
        Default().Sanitize(text).Should().NotContain("[truncated]");
    }

    [Fact]
    public void Sanitize_MaxStringLengthZero_NoCap()
    {
        var opts = new SpanSanitizerOptions { MaxStringLength = 0 };
        var longText = new string('x', 10_000);
        WithOptions(opts).Sanitize(longText).Length.Should().Be(10_000);
    }

    [Fact]
    public void Sanitize_CustomCap_Respected()
    {
        var opts = new SpanSanitizerOptions { MaxStringLength = 100 };
        var text = new string('x', 200);
        var result = WithOptions(opts).Sanitize(text);

        result.Should().EndWith(" [truncated]");
        result.Length.Should().Be(100 + " [truncated]".Length);
    }

    // ── Both redaction + truncation applied ───────────────────────────────────

    [Fact]
    public void Sanitize_LargeSensitiveJson_RedactsAndTruncates()
    {
        // Build a JSON object with a sensitive key + lots of padding so the
        // serialized form exceeds the cap after redaction.
        var padding = new string('a', 5000);
        var json = $$"""{"password": "secret", "data": "{{padding}}"}""";

        var result = Default().Sanitize(json);

        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("secret");
        result.Should().EndWith(" [truncated]");
    }

    // ── Options flags ─────────────────────────────────────────────────────────

    [Fact]
    public void Sanitize_EnableRedactionFalse_SensitiveKeyPassesThrough()
    {
        var opts = new SpanSanitizerOptions { EnableRedaction = false };
        var json = """{"password": "visible"}""";
        var result = WithOptions(opts).Sanitize(json);

        result.Should().Contain("visible");
        result.Should().NotContain("[REDACTED]");
    }

    [Fact]
    public void Sanitize_EnableRedactionFalse_LengthCapStillApplied()
    {
        var opts = new SpanSanitizerOptions { EnableRedaction = false, MaxStringLength = 10 };
        var text = new string('x', 50);
        var result = WithOptions(opts).Sanitize(text);

        result.Should().EndWith(" [truncated]");
        result.Length.Should().Be(10 + " [truncated]".Length);
    }

    // ── JSON array root ───────────────────────────────────────────────────────

    [Fact]
    public void Sanitize_JsonArrayRoot_RedactionApplied()
    {
        var json = """[{"token": "abc"}, {"name": "safe"}]""";
        var result = Default().Sanitize(json);

        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("abc");
        result.Should().Contain("safe");
    }
}
