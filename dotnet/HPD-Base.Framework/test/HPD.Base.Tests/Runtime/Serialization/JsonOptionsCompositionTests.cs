using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FluentAssertions;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Serialization;

public sealed class JsonOptionsCompositionTests
{
    [Fact]
    public void RuntimeJsonOptionsAreFrozenAndIncludeRuntimeMetadata()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseRuntime();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IBaseJsonOptionsProvider>().Options;

        options.IsReadOnly.Should().BeTrue();
        options.GetTypeInfo(typeof(ExpandedBaseManifest)).Should().BeAssignableTo<JsonTypeInfo<ExpandedBaseManifest>>();
    }

    [Fact]
    public void RuntimeJsonOptionsComposeExplicitContributorMetadata()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBaseJsonTypeInfoContributor, TestJsonTypeInfoContributor>();
        services.AddHPDBaseRuntime();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IBaseJsonOptionsProvider>().Options;
        var typeInfo = (JsonTypeInfo<TestJsonPayload>)options.GetTypeInfo(typeof(TestJsonPayload));

        var json = JsonSerializer.Serialize(new TestJsonPayload { Name = "contributed" }, typeInfo);

        json.Should().Contain("contributed");
    }

    [Fact]
    public void RuntimeJsonOptionsDoNotFallBackToReflectionForUnknownTypes()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseRuntime();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IBaseJsonOptionsProvider>().Options;

        var act = () => options.GetTypeInfo(typeof(UnregisteredJsonPayload));

        act.Should().Throw<NotSupportedException>();
    }
}

internal sealed record TestJsonPayload
{
    public required string Name { get; init; }
}

internal sealed record UnregisteredJsonPayload
{
    public required string Name { get; init; }
}

[JsonSerializable(typeof(TestJsonPayload))]
internal sealed partial class TestJsonSerializerContext : JsonSerializerContext;

internal sealed class TestJsonTypeInfoContributor : IBaseJsonTypeInfoContributor
{
    public string Id => "test.json";

    public string Version => "1.0";

    public void AddTo(IBaseJsonTypeInfoRegistry registry)
    {
        registry.AddResolver(Id, TestJsonSerializerContext.Default);
    }
}
