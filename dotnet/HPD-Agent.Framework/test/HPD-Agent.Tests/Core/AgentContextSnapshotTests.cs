using System.Text.Json;
using System.Text.Json.Nodes;

namespace HPD.Agent.Tests.Core;

public sealed class AgentContextSnapshotTests
{
    [Fact]
    public void Capture_IsolatesNestedDataAcrossInvocationsAndSource()
    {
        var nested = new Dictionary<string, object>
        {
            ["items"] = new List<object> { new[] { "evidence" } },
            ["json"] = JsonNode.Parse("{\"allowed\":[\"read\"]}")!
        };
        var source = Config(nested);
        var first = AgentRunConfigSnapshot.Capture(source, null)!;
        var second = AgentRunConfigSnapshot.Capture(source, null)!;
        var firstData = Assert.IsType<Dictionary<string, object>>(first.Context!.Properties!["value"]);
        var firstList = Assert.IsType<List<object>>(firstData["items"]);
        Assert.IsType<string[]>(firstList[0])[0] = "changed";
        ((JsonNode)firstData["json"])["allowed"]![0] = "write";

        foreach (var other in new[] { source, second })
        {
            var data = (Dictionary<string, object>)other.Context!.Properties!["value"];
            Assert.Equal("evidence", ((string[])((List<object>)data["items"])[0])[0]);
            Assert.Equal("read", ((JsonNode)data["json"])["allowed"]![0]!.GetValue<string>());
        }
    }

    [Fact]
    public void Capture_ClonesJsonElementBeyondDocumentLifetime()
    {
        AgentRunConfig snapshot;
        using (var document = JsonDocument.Parse("{\"root\":\"evidence\"}"))
            snapshot = AgentRunConfigSnapshot.Capture(Config(document.RootElement), null)!;
        Assert.Equal("evidence", ((JsonElement)snapshot.Context!.Properties!["value"]).GetProperty("root").GetString());
    }

    [Fact]
    public void Capture_RejectsOpaqueValuesAndCyclesWithMemberPath()
    {
        var exception = Assert.Throws<AgentRunConfigurationException>(() => AgentRunConfigSnapshot.Capture(Config(new object()), null));
        Assert.Equal("UnsupportedContextOwnership", exception.Code);
        Assert.Equal("Context.Properties[value]", exception.Path);
        var cycle = new List<object>();
        cycle.Add(cycle);
        exception = Assert.Throws<AgentRunConfigurationException>(() => AgentRunConfigSnapshot.Capture(Config(cycle), null));
        Assert.Equal("ContextValueCycle", exception.Code);
    }

    [Fact]
    public void Capture_PreservesExplicitHostSharedIdentityAcrossRepeatedCapture()
    {
        var service = new object();
        var source = Config(service);
        source.Context!.SharedProperties = new HashSet<string> { "value" };
        var first = AgentRunConfigSnapshot.Capture(source, null)!;
        var second = AgentRunConfigSnapshot.Capture(first, null)!;
        Assert.Same(service, second.Context!.Properties!["value"]);
        Assert.NotSame(source.Context.SharedProperties, first.Context!.SharedProperties);
    }

    [Fact]
    public void Capture_CopiesCustomValuesAndToolMetadata()
    {
        var metadata = new Metadata { Value = "original" };
        var source = Config(metadata);
        source.Context!.ToolInstances = new Dictionary<string, IToolMetadata> { ["tool"] = metadata };
        var first = AgentRunConfigSnapshot.Capture(source, null)!;
        var second = AgentRunConfigSnapshot.Capture(source, null)!;
        Assert.IsType<Metadata>(first.Context!.Properties!["value"]).Value = "changed";
        Assert.IsType<Metadata>(first.Context.ToolInstances!["tool"]).Value = "changed tool";
        Assert.Equal("original", Assert.IsType<Metadata>(second.Context!.Properties!["value"]).Value);
        Assert.Equal("original", Assert.IsType<Metadata>(second.Context.ToolInstances!["tool"]).Value);
        Assert.Equal("original", metadata.Value);
    }

    [Fact]
    public void Capture_PreservesImmutableAndExplicitSharedToolIdentity()
    {
        var immutable = new ImmutableValue("read");
        var tool = new Metadata();
        var source = Config(immutable);
        source.Context!.ToolInstances = new Dictionary<string, IToolMetadata> { ["service"] = tool };
        source.Context.SharedToolInstances = new HashSet<string> { "service" };
        var snapshot = AgentRunConfigSnapshot.Capture(source, null)!;
        Assert.Same(immutable, snapshot.Context!.Properties!["value"]);
        Assert.Same(tool, snapshot.Context.ToolInstances!["service"]);
        Assert.NotSame(source.Context.SharedToolInstances, snapshot.Context.SharedToolInstances);
    }

    [Fact]
    public void Capture_PreservesDictionaryKeyComparison()
    {
        var source = Config(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["Read"] = true });
        var snapshot = AgentRunConfigSnapshot.Capture(source, null)!;
        var dictionary = Assert.IsType<Dictionary<string, object>>(snapshot.Context!.Properties!["value"]);
        Assert.True((bool)dictionary["READ"]);
    }

    [Fact]
    public void Capture_RejectsExcessiveDepth()
    {
        object data = "value";
        for (var depth = 0; depth < 65; depth++) data = new List<object> { data };
        var exception = Assert.Throws<AgentRunConfigurationException>(() => AgentRunConfigSnapshot.Capture(Config(data), null));
        Assert.Equal("ContextValueDepthExceeded", exception.Code);
    }

    [Fact]
    public void Capture_RejectsInvalidCustomSnapshotAndRedactsHostExceptions()
    {
        var invalid = Assert.Throws<AgentRunConfigurationException>(() => AgentRunConfigSnapshot.Capture(Config(new InvalidSnapshot()), null));
        Assert.Equal("UnsupportedContextOwnership", invalid.Code);
        var failed = Assert.Throws<AgentRunConfigurationException>(() => AgentRunConfigSnapshot.Capture(Config(new ThrowingSnapshot()), null));
        Assert.Equal("ContextSnapshotFailed", failed.Code);
        Assert.DoesNotContain("secret", failed.Message);
    }

    private static AgentRunConfig Config(object value) => new()
    {
        Context = new() { Properties = new Dictionary<string, object> { ["value"] = value } }
    };

    private sealed class InvalidSnapshot : IAgentContextSnapshotValue
    {
        public object CaptureContextSnapshot() => this;
    }

    private sealed record ImmutableValue(string Access) : IAgentImmutableContextValue;

    private sealed class ThrowingSnapshot : IAgentContextSnapshotValue
    {
        public object CaptureContextSnapshot() => throw new InvalidOperationException("secret");
    }

    private sealed class Metadata : IToolMetadata, IAgentContextSnapshotValue
    {
        public string Value { get; set; } = "";
        public object CaptureContextSnapshot() => new Metadata { Value = Value };
        public T? GetProperty<T>(string propertyName, T? defaultValue = default) => defaultValue;
        public bool HasProperty(string propertyName) => false;
        public IEnumerable<string> GetPropertyNames() => [];
    }
}
