using FluentAssertions;

namespace HPD.Agent.Tests.Storage;

public sealed class JsonAgentStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"hpd-json-agent-store-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadAsync_UnknownEnvelopeProperty_ReportsAgentAndProperty()
    {
        await WriteAgentAsync("""
        {
          "id": "test-agent",
          "config": {
            "name": "Test Agent"
          },
          "workspace": {
            "version": 1
          },
          "createdAt": "2026-06-14T00:00:00Z",
          "updatedAt": "2026-06-14T00:00:00Z"
        }
        """);

        var store = new JsonAgentStore(_root);
        var act = () => store.LoadAsync("test-agent");

        var exception = await act.Should().ThrowAsync<InvalidDataException>();
        exception.Which.Message.Should().Contain("test-agent");
        exception.Which.Message.Should().Contain(".json");
        exception.Which.Message.Should().Contain("workspace");
        exception.Which.InnerException.Should().BeOfType<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task LoadAsync_InvalidAgentConfig_ReportsValidationErrors()
    {
        await WriteAgentAsync("""
        {
          "id": "test-agent",
          "config": {
            "name": "",
            "maxAgenticIterations": 0
          },
          "createdAt": "2026-06-14T00:00:00Z",
          "updatedAt": "2026-06-14T00:00:00Z"
        }
        """);

        var store = new JsonAgentStore(_root);
        var act = () => store.LoadAsync("test-agent");

        var exception = await act.Should().ThrowAsync<InvalidDataException>();
        exception.Which.Message.Should().Contain("agent configuration is invalid");
        exception.Which.Message.Should().Contain("Agent name must not be empty");
        exception.Which.Message.Should().Contain("MaxFunctionCallTurns must be greater than zero");
    }

    [Fact]
    public async Task LoadAsync_UserOperationalBudgetAboveFormerFrameworkLimit_IsAccepted()
    {
        await WriteAgentAsync("""
        {
          "id": "test-agent",
          "config": {
            "name": "Large Budget Agent",
            "maxAgenticIterations": 500,
            "resilience": {
              "retry": {
                "modelRetries": { "maxRetries": 50 },
                "functionRetries": { "maxRetries": 50 }
              },
              "circuitBreaker": { "maxConsecutiveCalls": 500 },
              "consecutiveErrors": { "maxConsecutiveErrors": 500 },
              "totalErrors": { "maxTotalErrors": 5000 }
            }
          },
          "createdAt": "2026-06-14T00:00:00Z",
          "updatedAt": "2026-06-14T00:00:00Z"
        }
        """);

        var stored = await new JsonAgentStore(_root).LoadAsync("test-agent");

        stored.Should().NotBeNull();
        stored!.Config.MaxAgenticIterations.Should().Be(500);
        stored.Config.Resilience!.Retry!.ModelRetries.MaxRetries.Should().Be(50);
    }

    [Fact]
    public async Task LoadAsync_NullConfig_ReportsRequiredProperty()
    {
        await WriteAgentAsync("""
        {
          "id": "test-agent",
          "config": null,
          "createdAt": "2026-06-14T00:00:00Z",
          "updatedAt": "2026-06-14T00:00:00Z"
        }
        """);

        var store = new JsonAgentStore(_root);
        var act = () => store.LoadAsync("test-agent");

        var exception = await act.Should().ThrowAsync<InvalidDataException>();
        exception.Which.Message.Should().Contain("required 'config' property cannot be null");
    }

    [Fact]
    public async Task LoadAsync_ValidDefinition_ReturnsAgent()
    {
        var store = new JsonAgentStore(_root);
        await store.SaveAsync(new StoredAgent
        {
            Id = "test-agent",
            Config = new AgentConfig { Name = "Test Agent" }
        });

        var stored = await store.LoadAsync("test-agent");

        stored.Should().NotBeNull();
        stored!.Id.Should().Be("test-agent");
        stored.Config.Name.Should().Be("Test Agent");
    }

    [Fact]
    public async Task NamespacedIdsAndCaseVariantsRoundTripIndependently()
    {
        const string first = "my-app/reviewer";
        const string second = "my-app/Reviewer";
        var store = new JsonAgentStore(_root);
        var definition = SubAgent.FromConfig(
            first, "review", "Reviews code.", new AgentConfig { Name = "Reviewer" });
        await new AgentDefinitionMaterializer(store).MaterializeAsync(
            definition, new AgentConfig(), "TestHarness.Reviewer", CancellationToken.None);
        await store.SaveAsync(new StoredAgent
        {
            Id = second,
            Config = new AgentConfig { Name = "Second reviewer" }
        });

        var reopened = new JsonAgentStore(_root);
        (await reopened.ListIdsAsync()).Should().BeEquivalentTo(new[] { first, second });
        (await reopened.LoadAsync(first))!.Id.Should().Be(first);
        (await reopened.LoadAsync(second))!.Id.Should().Be(second);

        await reopened.DeleteAsync(first);
        (await reopened.LoadAsync(first)).Should().BeNull();
        (await reopened.ListIdsAsync()).Should().Equal(second);
    }

    [Fact]
    public async Task SaveIfUnchangedAsync_RejectsAStaleExpectedDefinition()
    {
        var store = new JsonAgentStore(_root);
        await store.SaveAsync(new StoredAgent
        {
            Id = "conditional-agent",
            Config = new AgentConfig { Name = "Original" },
        });
        StoredAgent expected = (await store.LoadAsync("conditional-agent"))!;
        await store.SaveAsync(new StoredAgent
        {
            Id = "conditional-agent",
            Config = new AgentConfig { Name = "Competitor" },
            CreatedAt = expected.CreatedAt,
            UpdatedAt = DateTime.UtcNow.AddSeconds(1),
        });

        bool saved = await store.SaveIfUnchangedAsync(new StoredAgent
        {
            Id = "conditional-agent",
            Config = new AgentConfig { Name = "Stale Studio Draft" },
            CreatedAt = expected.CreatedAt,
            UpdatedAt = DateTime.UtcNow.AddSeconds(2),
        }, expected);

        saved.Should().BeFalse();
        (await store.LoadAsync("conditional-agent"))!.Config.Name.Should().Be("Competitor");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task WriteAgentAsync(string json)
    {
        var store = new JsonAgentStore(_root);
        await store.SaveAsync(new StoredAgent
        {
            Id = "test-agent",
            Config = new AgentConfig { Name = "Test Agent" }
        });
        await File.WriteAllTextAsync(Directory.GetFiles(_root, "*.json").Single(), json);
    }
}
