using System.Text.Json;

namespace HPD.Agent.Client.Tests;

public sealed class ConformanceFixtureTests
{
    [Fact]
    public void CSharpRuntime_PassesLanguageNeutralConformanceFixtures()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Conformance",
                "fixtures",
                "conformance-v1.json")));
        var fixtures = document.RootElement.GetProperty("fixtures");
        var executed = 0;

        foreach (var fixture in fixtures.EnumerateArray())
        {
            var name = fixture.GetProperty("name").GetString()!;
            var kind = fixture.GetProperty("kind").GetString();
            switch (kind)
            {
                case "sse-state":
                    RunSseFixture(fixture, name);
                    executed++;
                    break;
                case "json-shape":
                    RunJsonShapeFixture(fixture, name);
                    executed++;
                    break;
                case "client-tool":
                    RunClientToolFixture(fixture, name);
                    executed++;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Fixture '{name}' has unsupported kind '{kind}'.");
            }
        }

        Assert.Equal(7, executed);
    }

    private static JsonElement? RunSseFixture(JsonElement fixture, string name)
    {
        var initial = fixture.GetProperty("initialCursor");
        var state = new HpdAgentSseState(new HpdAgentSseAppliedCursor(
            initial.GetProperty("generation").GetInt32(),
            initial.GetProperty("sequence").GetInt64()));
        var messages = fixture.GetProperty("messages").EnumerateArray().ToArray();
        var expectedError = fixture.TryGetProperty("expectedError", out var error)
            ? error.GetString()
            : null;

        if (expectedError is not null)
        {
            try
            {
                foreach (var message in messages)
                    state.Apply(Message(message), Sequence(message));
            }
            catch (HpdAgentStreamException exception)
            {
                var expected = expectedError == "journal-rebased"
                    ? exception is HpdAgentJournalRebasedException
                    : !(exception is HpdAgentJournalRebasedException) &&
                      exception.Message.Contains("did not match", StringComparison.Ordinal);
                Assert.True(expected, $"Fixture '{name}' raised {exception.GetType().Name}: {exception.Message}");
            }
            if (fixture.TryGetProperty("expectedCursor", out var expectedCursor))
            {
                Assert.Equal(
                    new HpdAgentSseAppliedCursor(
                        expectedCursor.GetProperty("generation").GetInt32(),
                        expectedCursor.GetProperty("sequence").GetInt64()),
                    state.AppliedCursor);
            }
            return null;
        }

        var decisions = messages
            .Select(message => state.Apply(Message(message), Sequence(message)).ToString().ToLowerInvariant())
            .ToArray();
        Assert.Equal(
            fixture.GetProperty("expectedDecisions")
                .EnumerateArray()
                .Select(static value => value.GetString())
                .ToArray(),
            decisions);
        var cursor = fixture.GetProperty("expectedCursor");
        Assert.Equal(
            new HpdAgentSseAppliedCursor(
                cursor.GetProperty("generation").GetInt32(),
                cursor.GetProperty("sequence").GetInt64()),
            state.AppliedCursor);
        return fixture.Clone();
    }

    private static void RunJsonShapeFixture(JsonElement fixture, string name)
    {
        var operation = fixture.GetProperty("operation").GetString();
        var actual = operation switch
        {
            "run-config-empty" => new HpdAgentRunConfig().ToJsonElement(),
            "chat-user-message" => HpdAgentChatContent.UserMessage(
                fixture.GetProperty("input").GetString()!),
            _ => throw new InvalidOperationException(
                $"Fixture '{name}' has unsupported operation '{operation}'.")
        };
        Assert.True(
            JsonElement.DeepEquals(fixture.GetProperty("expected"), actual),
            $"Fixture '{name}' JSON shape differed: {actual.GetRawText()}");
    }

    private static void RunClientToolFixture(JsonElement fixture, string name)
    {
        var registry = new HpdAgentClientToolRegistry();
        if (fixture.GetProperty("registration").ValueKind == JsonValueKind.String)
        {
            var handlerName = fixture.GetProperty("handler").GetString()!;
            registry.Register(
                fixture.GetProperty("registration").GetString()!,
                (_, _) => ValueTask.FromResult(
                    HpdAgentClientToolResult.Text(handlerName)));
        }
        var invocation = fixture.GetProperty("invocation");
        var result = registry.InvokeAsync(new HpdAgentClientToolInvocation(
            invocation.GetProperty("requestId").GetString()!,
            invocation.GetProperty("toolName").GetString()!,
            invocation.GetProperty("callId").GetString()!,
            invocation.GetProperty("arguments").Clone(),
            null,
            null,
            null)).AsTask().GetAwaiter().GetResult();

        Assert.Equal(
            fixture.GetProperty("expectedOutcome").GetString(),
            result.Outcome);
        if (fixture.TryGetProperty("expectedCapability", out var capability))
            Assert.Contains(capability.GetString()!, registry.Capabilities);
        if (fixture.TryGetProperty("expectedContent", out var content))
        {
            Assert.True(JsonElement.DeepEquals(content, Assert.Single(result.Content!)));
        }
        if (fixture.TryGetProperty("expectedErrorMessageContains", out var message))
        {
            Assert.Contains(
                message.GetString()!,
                result.ErrorMessage,
                StringComparison.Ordinal);
        }
    }

    private static HpdAgentSseMessage Message(JsonElement fixture) =>
        new(
            fixture.GetProperty("event").GetString(),
            fixture.GetProperty("id").ValueKind == JsonValueKind.Null
                ? null
                : fixture.GetProperty("id").GetString(),
            fixture.GetProperty("data").Clone());

    private static long? Sequence(JsonElement fixture) =>
        fixture.GetProperty("sequence").ValueKind == JsonValueKind.Null
            ? null
            : fixture.GetProperty("sequence").GetInt64();
}
