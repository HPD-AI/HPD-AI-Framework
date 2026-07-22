using System.Text.Json;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugProtocolGenerationTests
{
    [Fact]
    public void Pinned_schema_metadata_is_exposed()
    {
        DebugProtocolSource.Commit.Should().Be("e34479c39ed4973210115872c8e118c097a50d4a");
        DebugProtocolSource.SchemaSha256.Should().Be("ff8ae4c6cfd588a050e9346c35fd104748a27ef4518d1c3268529ca6f8ff5818");
        DebugProtocolSource.DefinitionCount.Should().Be(192);
        DebugProtocolSource.CodeLicense.Should().Be("MIT");
        DebugProtocolSource.SpecificationLicense.Should().Be("CC-BY");
    }

    [Fact]
    public void Feature_inventory_classifies_the_complete_message_surface()
    {
        DebugProtocolFeatureInventory.All.Should().HaveCount(62);
        DebugProtocolFeatureInventory.All.Count(entry => entry.Kind == DapFeatureKind.Request).Should().Be(43);
        DebugProtocolFeatureInventory.All.Count(entry => entry.Kind == DapFeatureKind.ReverseRequest).Should().Be(2);
        DebugProtocolFeatureInventory.All.Count(entry => entry.Kind == DapFeatureKind.Event).Should().Be(17);
        DebugProtocolFeatureInventory.All.Select(entry => entry.Name).Should().OnlyHaveUniqueItems();
        DebugProtocolFeatureInventory.Capabilities.Should().HaveCount(52);
        DebugProtocolFeatureInventory.Capabilities.Count(entry => entry.Direction == DapCapabilityDirection.ClientToAdapter).Should().Be(10);
        DebugProtocolFeatureInventory.Capabilities.Count(entry => entry.Direction == DapCapabilityDirection.AdapterToClient).Should().Be(42);
        DebugProtocolFeatureInventory.Capabilities
            .Select(entry => (entry.Name, entry.Direction))
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Descriptors_bind_commands_to_typed_arguments_and_bodies()
    {
        DebugProtocolDescriptors.InitializeRequest.Command.Should().Be("initialize");
        DebugProtocolDescriptors.InitializeRequest.Direction.Should().Be(DapRequestDirection.ClientToAdapter);
        DebugProtocolDescriptors.ThreadsRequest.Should().BeOfType<DapRequestDescriptor<DapNoArguments, ThreadsResponseBody>>();
        DebugProtocolDescriptors.RunInTerminalRequest.Direction.Should().Be(DapRequestDirection.AdapterToClient);
        DebugProtocolDescriptors.StartDebuggingRequest.Direction.Should().Be(DapRequestDirection.AdapterToClient);
        DebugProtocolDescriptors.StoppedEvent.Should().BeOfType<DapEventDescriptor<StoppedEventBody>>();
        DebugProtocolDescriptors.InitializeRequest.ArgumentsTypeInfo.Should().BeSameAs(DapJsonContext.Default.InitializeRequestArguments);
        DebugProtocolDescriptors.InitializeRequest.BodyTypeInfo.Should().BeSameAs(DapJsonContext.Default.Capabilities);
        DebugProtocolDescriptors.StoppedEvent.BodyTypeInfo.Should().BeSameAs(DapJsonContext.Default.StoppedEventBody);
    }

    [Fact]
    public void Open_string_values_round_trip_unknown_extensions()
    {
        var original = new CompletionItemType("future-completion-kind");

        var json = JsonSerializer.Serialize(original, DapJsonContext.Default.CompletionItemType);
        var restored = JsonSerializer.Deserialize(json, DapJsonContext.Default.CompletionItemType);

        json.Should().Be("\"future-completion-kind\"");
        restored.Should().Be(original);
    }

    [Fact]
    public void Unknown_object_fields_are_ignored_without_breaking_known_fields()
    {
        const string json = """
            {"name":"sample.cs","sourceReference":7,"futureField":{"enabled":true}}
            """;

        var source = JsonSerializer.Deserialize(json, DapJsonContext.Default.Source)!;
        source.Name.Should().Be("sample.cs");
        source.SourceReference.Should().Be(7);
    }

    [Fact]
    public void Nullable_environment_values_preserve_delete_semantics()
    {
        const string json = """
            {"cwd":"/workspace","args":["dotnet","run"],"env":{"KEEP":"yes","REMOVE":null}}
            """;

        var arguments = JsonSerializer.Deserialize(json, DapJsonContext.Default.RunInTerminalRequestArguments)!;

        arguments.Env.Should().ContainKey("KEEP").WhoseValue.Should().Be("yes");
        arguments.Env.Should().ContainKey("REMOVE").WhoseValue.Should().BeNull();
    }

    [Fact]
    public void Exact_singular_capability_wire_names_are_preserved()
    {
        const string json = """
            {"supportTerminateDebuggee":true,"supportSuspendDebuggee":true}
            """;

        var capabilities = JsonSerializer.Deserialize(json, DapJsonContext.Default.Capabilities)!;

        capabilities.SupportTerminateDebuggee.Should().BeTrue();
        capabilities.SupportSuspendDebuggee.Should().BeTrue();
    }

    [Fact]
    public void Representative_canonical_and_synthetic_types_have_generated_metadata()
    {
        DapJsonContext.Default.GetTypeInfo(typeof(ProtocolMessage)).Should().NotBeNull();
        DapJsonContext.Default.GetTypeInfo(typeof(InitializeRequestArguments)).Should().NotBeNull();
        DapJsonContext.Default.GetTypeInfo(typeof(ThreadsResponseBody)).Should().NotBeNull();
        DapJsonContext.Default.GetTypeInfo(typeof(StoppedEventBody)).Should().NotBeNull();
        DapJsonContext.Default.GetTypeInfo(typeof(RunInTerminalRequestArguments)).Should().NotBeNull();
    }
}
