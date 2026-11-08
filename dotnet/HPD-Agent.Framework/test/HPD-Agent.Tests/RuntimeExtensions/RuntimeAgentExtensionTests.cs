using System.Text.Json;
using HPD.Agent.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Tests.RuntimeExtensions;

public sealed class RuntimeAgentExtensionTests
{
    [Fact]
    public void Registry_ValidatesPayloadAndCreatesTypedEnvelopes()
    {
        var registry = new RuntimeAgentExtensionRegistry();
        Assert.StartsWith("sha256:", RuntimeAgentExtensionRegistry.EmptyCatalog.Digest, StringComparison.Ordinal);
        var descriptor = registry.Register(Schema(
            RuntimeAgentExtensionUsage.LiveOutput |
            RuntimeAgentExtensionUsage.DurableOutput |
            RuntimeAgentExtensionUsage.Input));
        var payload = Json("""{"percent":42,"stage":"extract"}""");

        Assert.Equal("tenant.acme.analysis:progress@1", descriptor.Key);
        Assert.True(registry.TryValidate(descriptor.Key, payload, out var error), error);
        Assert.False(registry.TryValidate(descriptor.Key, Json("""{"percent":101,"stage":"extract"}"""), out _));
        Assert.Equal("progress", registry.CreateLiveEvent(descriptor.Key, payload).SchemaName);
        Assert.Equal("progress", registry.CreateDurableEvent(descriptor.Key, payload).SchemaName);
        Assert.Equal("progress", registry.CreateInput(descriptor.Key, payload).SchemaName);
        Assert.StartsWith("sha256:", registry.GetCatalog().Digest, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_RejectsReservedNamespacesAndUnsafeSchemaFeatures()
    {
        var registry = new RuntimeAgentExtensionRegistry();
        Assert.Throws<ArgumentException>(() => registry.Register(new RuntimeAgentExtensionSchema
        {
            Namespace = "hpd.core.runtime",
            Name = "bad",
            Version = 1,
            Usage = RuntimeAgentExtensionUsage.LiveOutput,
            PayloadSchema = Json("""{"type":"object"}""")
        }));
        Assert.Throws<InvalidDataException>(() => registry.Register(new RuntimeAgentExtensionSchema
        {
            Namespace = "tenant.acme",
            Name = "bad",
            Version = 1,
            Usage = RuntimeAgentExtensionUsage.LiveOutput,
            PayloadSchema = Json("""{"type":"object","$ref":"https://example.test/schema"}""")
        }));
    }

    [Fact]
    public void RuntimeInput_DispatchesToTrustedHandler()
    {
        var registry = new RuntimeAgentExtensionRegistry();
        var descriptor = registry.Register(Schema(RuntimeAgentExtensionUsage.Input));
        var handler = new ProbeHandler();
        registry.RegisterInputHandler(descriptor.Key, handler);
        using var services = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();
        var input = registry.CreateInput(
            descriptor.Key,
            Json("""{"percent":75,"stage":"verify"}"""));
        var admitted = AgentInputDispatcher.GetBuiltInRegistration(typeof(RuntimeAgentInputEvent));
        Assert.Equal(AgentInputRoutingClass.Work, admitted.RoutingClass);
        var context = new AgentInputHandlingContext
        {
            AgentName = "probe",
            Config = new AgentConfig(),
            EventCoordinator = new HPD.Events.Core.EventCoordinator(),
            Services = services,
            RunMessagesAsync = static (_, _, _, _, _) =>
                Task.FromResult(AgentTurnResult.Empty),
            TryResolveClientToolOperation = static _ => false
        };

        var result = new RuntimeAgentInputHandler()
            .HandleAsync(input, context, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        Assert.IsType<AgentInputResult.Control>(result);
        Assert.Equal(75, handler.Percent);
    }

    [Fact]
    public void RuntimeEnvelopes_RoundTripThroughGeneratedCoreCodec()
    {
        var registry = new RuntimeAgentExtensionRegistry();
        var key = registry.Register(Schema(
            RuntimeAgentExtensionUsage.LiveOutput |
            RuntimeAgentExtensionUsage.DurableOutput |
            RuntimeAgentExtensionUsage.Input)).Key;
        var payload = Json("""{"percent":10,"stage":"start"}""");
        var codec = HPD.Agent.Tests.TestEventApplication.Codec;
        var inputCodec = new AgentInputCodec(
            HPD.Agent.Providers.ProviderComposition.Create([]),
            HPD.Agent.Tests.TestEventApplication.InputComposition);

        var liveJson = codec.Serialize(registry.CreateLiveEvent(key, payload));
        var durableJson = codec.Serialize(registry.CreateDurableEvent(key, payload));
        var inputJson = inputCodec.Serialize(registry.CreateInput(key, payload));

        Assert.IsType<RuntimeAgentLiveEvent>(codec.DeserializeEvent(liveJson));
        Assert.IsType<RuntimeAgentDurableEvent>(codec.DeserializeEvent(durableJson));
        Assert.IsType<RuntimeAgentInputEvent>(inputCodec.Deserialize(inputJson));
        Assert.True(HPD.Agent.Tests.TestEventApplication.InputComposition.IsPublic(typeof(RuntimeAgentInputEvent)));
    }

    [Fact]
    public async Task FileStore_PersistsAndHydratesSchemasWithPayloadDefinitions()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"hpd-runtime-schemas-{Guid.NewGuid():N}");
        try
        {
            var store = new FileRuntimeAgentExtensionSchemaStore(directory);
            var first = new RuntimeAgentExtensionRegistry();
            var registered = await first.RegisterPersistentAsync(
                Schema(RuntimeAgentExtensionUsage.LiveOutput),
                store);

            var reopened = new RuntimeAgentExtensionRegistry();
            Assert.Equal(1, await reopened.HydrateAsync(store));
            var catalog = reopened.GetCatalog();
            var descriptor = Assert.Single(catalog.Schemas);
            Assert.Equal(registered.Key, descriptor.Key);
            Assert.Equal("object", descriptor.PayloadSchema.GetProperty("type").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void JsonSchemaEvaluator_SupportsLocalDefinitionsAndComposition()
    {
        var registry = new RuntimeAgentExtensionRegistry();
        var key = registry.Register(new RuntimeAgentExtensionSchema
        {
            Namespace = "tenant.acme",
            Name = "composed",
            Version = 1,
            Usage = RuntimeAgentExtensionUsage.LiveOutput,
            PayloadSchema = Json("""
                {
                  "$defs": {
                    "state": { "type": "string", "enum": ["started", "stopped"] }
                  },
                  "type": "object",
                  "required": ["state", "detail"],
                  "properties": {
                    "state": { "$ref": "#/$defs/state" },
                    "detail": {
                      "oneOf": [
                        { "type": "object", "required": ["count"], "properties": { "count": { "type": "integer" } } },
                        { "type": "object", "required": ["message"], "properties": { "message": { "type": "string" } } }
                      ]
                    }
                  }
                }
                """)
        }).Key;

        Assert.True(registry.TryValidate(
            key,
            Json("""{"state":"started","detail":{"count":3}}"""),
            out var validError), validError);
        Assert.False(registry.TryValidate(
            key,
            Json("""{"state":"unknown","detail":{"count":3}}"""),
            out _));
    }

    private static RuntimeAgentExtensionSchema Schema(RuntimeAgentExtensionUsage usage) => new()
    {
        Namespace = "tenant.acme.analysis",
        Name = "progress",
        Version = 1,
        Usage = usage,
        Description = "Analysis progress.",
        PayloadSchema = Json("""
            {
              "type": "object",
              "required": ["percent", "stage"],
              "properties": {
                "percent": { "type": "integer", "minimum": 0, "maximum": 100 },
                "stage": { "type": "string", "minLength": 1, "maxLength": 64 }
              },
              "additionalProperties": false
            }
            """)
    };

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private sealed class ProbeHandler : IRuntimeAgentInputHandler
    {
        public int Percent { get; private set; }

        public ValueTask<RuntimeAgentInputHandlingResult> HandleAsync(
            RuntimeAgentInputEvent input,
            RuntimeAgentInputContext context,
            CancellationToken cancellationToken)
        {
            Percent = input.Payload.GetProperty("percent").GetInt32();
            Assert.Equal("probe", context.AgentName);
            return ValueTask.FromResult(RuntimeAgentInputHandlingResult.Success);
        }
    }
}
