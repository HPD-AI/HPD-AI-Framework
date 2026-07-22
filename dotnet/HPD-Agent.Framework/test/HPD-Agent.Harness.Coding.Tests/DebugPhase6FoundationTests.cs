using HPD.Agent.ToolHarness.Coding.Debugging.Protocol.Generated;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPD.Agent.ToolHarness.Coding.Debugging.Protocol;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugPhase6FoundationTests
{
    [Fact]
    public void Every_canonical_client_request_has_exactly_one_runtime_classification()
    {
        var requests = DebugProtocolFeatureInventory.All.Where(x => x.Kind == DapFeatureKind.Request).ToArray();
        DebugCanonicalRequestCatalog.All.Should().HaveCount(requests.Length);
        DebugCanonicalRequestCatalog.All.Keys.Should().BeEquivalentTo(requests.Select(x => x.Name));
        DebugCanonicalRequestCatalog.ExplicitlyDeclaredCommands.Should().BeEquivalentTo(requests.Select(x => x.Name));
        DebugCanonicalRequestCatalog.All.Values.Should().OnlyContain(x =>
            !string.IsNullOrWhiteSpace(x.SemanticOwner) && !string.IsNullOrWhiteSpace(x.StatePrecondition) &&
            !string.IsNullOrWhiteSpace(x.ReferenceLifetime) && !string.IsNullOrWhiteSpace(x.ResultLimit));
        DebugCanonicalRequestCatalog.All.Values.Should().OnlyContain(x =>
            x.Status == DebugRequestImplementationStatus.Implemented &&
            x.TestStatus == DebugRequestTestStatus.ConformanceCovered);
    }

    [Fact]
    public void Unknown_canonical_request_has_a_typed_rejection()
    {
        var act = () => DebugCanonicalRequestCatalog.Get("futureRequest");
        act.Should().Throw<DebugSemanticException>().Which.Reason
            .Should().Be(DebugSemanticFailureReason.UnsupportedCanonicalRequest);
    }

    [Fact]
    public void Continuation_is_bound_to_owner_query_and_generation()
    {
        var registry = new DebugContinuationTokenRegistry();
        var context = Context();
        var token = registry.Create(context, new(42, "server-state"));

        registry.Resolve(token, context).Should().Be(new DebugContinuationState(42, "server-state"));
        Action wrongOwner = () => registry.Resolve(token, context with { DebugSessionId = "other" });
        Action wrongRuntime = () => registry.Resolve(token, context with { AgentRuntimeRegistrationId = "other-runtime" });
        Action wrongTree = () => registry.Resolve(token, context with { DebugTreeId = "other-tree" });
        Action wrongKind = () => registry.Resolve(token, context with { QueryKind = "loadedSources" });
        Action wrongQuery = () => registry.Resolve(token, context with { QueryIdentity = "different" });
        Action stale = () => registry.Resolve(token, context with { Generation = 2 });
        wrongOwner.Should().Throw<DebugSemanticException>().Which.Reason.Should().Be(DebugSemanticFailureReason.ReferenceOwnerMismatch);
        wrongRuntime.Should().Throw<DebugSemanticException>().Which.Reason.Should().Be(DebugSemanticFailureReason.ReferenceOwnerMismatch);
        wrongTree.Should().Throw<DebugSemanticException>().Which.Reason.Should().Be(DebugSemanticFailureReason.ReferenceOwnerMismatch);
        wrongKind.Should().Throw<DebugSemanticException>().Which.Reason.Should().Be(DebugSemanticFailureReason.InvalidArguments);
        wrongQuery.Should().Throw<DebugSemanticException>().Which.Reason.Should().Be(DebugSemanticFailureReason.InvalidArguments);
        stale.Should().Throw<DebugSemanticException>().Which.Reason.Should().Be(DebugSemanticFailureReason.ReferenceExpired);
    }

    [Fact]
    public void Continuations_expire_and_are_bounded_per_tree()
    {
        var now = DateTimeOffset.UtcNow;
        var registry = new DebugContinuationTokenRegistry(() => now, TimeSpan.FromMinutes(5));
        var first = registry.Create(Context(), new(0));
        for (var index = 1; index <= DebugContinuationTokenRegistry.MaximumTokens; index++)
            registry.Create(Context() with { QueryIdentity = index.ToString() }, new(index));
        registry.Count.Should().Be(DebugContinuationTokenRegistry.MaximumTokens);
        Action evicted = () => registry.Resolve(first, Context());
        evicted.Should().Throw<DebugSemanticException>().Which.Reason.Should().Be(DebugSemanticFailureReason.ReferenceExpired);

        var expiring = registry.Create(Context() with { QueryIdentity = "expires" }, new(1));
        now += TimeSpan.FromMinutes(5);
        Action expired = () => registry.Resolve(expiring, Context() with { QueryIdentity = "expires" });
        expired.Should().Throw<DebugSemanticException>().Which.Reason.Should().Be(DebugSemanticFailureReason.ReferenceExpired);
    }

    [Fact]
    public void Invalidation_revokes_only_matching_continuations()
    {
        var registry = new DebugContinuationTokenRegistry();
        var modules = registry.Create(Context(), new(10));
        var sourcesContext = Context() with { QueryKind = "loadedSources" };
        var sources = registry.Create(sourcesContext, new(20));

        registry.Revoke("protocol", "modules").Should().Be(1);
        Action revoked = () => registry.Resolve(modules, Context());
        revoked.Should().Throw<DebugSemanticException>();
        registry.Resolve(sources, sourcesContext).AdapterOffset.Should().Be(20);
    }

    [Fact]
    public void Semantic_service_exposes_no_generated_DAP_response_body()
    {
        var generatedNamespace = typeof(ThreadsResponseBody).Namespace;
        var leaked = typeof(DebugSemanticService).GetMethods(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Select(method => Unwrap(method.ReturnType))
            .Where(type => string.Equals(type.Namespace, generatedNamespace, StringComparison.Ordinal) &&
                type.Name.EndsWith("ResponseBody", StringComparison.Ordinal))
            .ToArray();
        leaked.Should().BeEmpty();
    }

    [Fact]
    public void Source_tokens_roundtrip_owned_adapter_data_without_exposing_it_in_the_token()
    {
        var projections = new DebugSessionProjections();
        using var document = JsonDocument.Parse("{\"private\":\"adapter-state\"}");
        var token = projections.CreateSourceToken(0, null, new Source
        {
            Name = "sample.cs", Path = "/workspace/sample.cs", SourceReference = 7,
            AdapterData = document.RootElement.Clone()
        });
        token.Should().NotContain("adapter-state").And.NotContain("sample.cs");
        var resolved = projections.ResolveSourceToken(token);
        resolved.SourceReference.Should().Be(7);
        resolved.Path.Should().Be("/workspace/sample.cs");
        resolved.AdapterData!.Value.GetProperty("private").GetString().Should().Be("adapter-state");
    }

    [Fact]
    public void Typed_extensions_reject_duplicates_and_canonical_shadowing()
    {
        Action duplicate = () => _ = new DebugAdapterExtensionRegistry(
            [new TestExtension("vendor.command"), new TestExtension("vendor.command")]);
        Action shadow = () => _ = new DebugAdapterExtensionRegistry([new TestExtension("threads")]);
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate*");
        shadow.Should().Throw<InvalidOperationException>().WithMessage("*shadows*");
    }

    [Fact]
    public void Typed_extension_host_and_extensions_are_registered_explicitly_through_DI()
    {
        var services = new ServiceCollection();
        services.AddHPDCodingDebugging().AddHPDDebugAdapterExtension<DiTestExtension>();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDebugAdapterExtensionHost>().Should().NotBeNull();
        provider.GetServices<IDebugAdapterExtensionRegistration>()
            .Should().ContainSingle(x => x is DiTestExtension);
    }

    private static Type Unwrap(Type type)
    {
        while (type.IsGenericType && type.GetGenericArguments().Length == 1 &&
               (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
            type = type.GetGenericArguments()[0];
        return type;
    }

    private sealed class TestExtension(string command) : DebugAdapterExtension<DapNoArguments, DapNoBody>
    {
        public override string AdapterId => "test";
        public override string Command => command;
        public override System.Text.Json.Serialization.Metadata.JsonTypeInfo<DapNoArguments> RequestTypeInfo
            => DapJsonContext.Default.DapNoArguments;
        public override System.Text.Json.Serialization.Metadata.JsonTypeInfo<DapNoBody> ResponseTypeInfo
            => DapJsonContext.Default.DapNoBody;
    }

    private sealed class DiTestExtension : DebugAdapterExtension<DapNoArguments, DapNoBody>
    {
        public override string AdapterId => "test";
        public override string Command => "vendor.di";
        public override System.Text.Json.Serialization.Metadata.JsonTypeInfo<DapNoArguments> RequestTypeInfo
            => DapJsonContext.Default.DapNoArguments;
        public override System.Text.Json.Serialization.Metadata.JsonTypeInfo<DapNoBody> ResponseTypeInfo
            => DapJsonContext.Default.DapNoBody;
    }

    private static DebugContinuationTokenContext Context() => new(
        "runtime", "tree", "protocol", "modules", "start=0;count=200", 1);
}
