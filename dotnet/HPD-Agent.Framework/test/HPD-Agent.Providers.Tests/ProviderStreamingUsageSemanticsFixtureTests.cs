using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Tests;

public sealed class ProviderStreamingUsageSemanticsFixtureTests
{
    public static TheoryData<string, string> PassThroughAdapters => new()
    {
        { "anthropic", "HPD.Agent.Providers.Anthropic.AnthropicConfiguredChatClient, HPD-Agent.Providers.Anthropic" },
        { "cohere", "HPD.Agent.Providers.Cohere.CohereProvider+CohereConfiguredChatClient, HPD-Agent.Providers.Cohere" },
        { "dashscope", "HPD.Agent.Providers.DashScope.DashScopeProvider+DashScopeConfiguredChatClient, HPD-Agent.Providers.DashScope" },
        { "deepinfra", "HPD.Agent.Providers.DeepInfra.DeepInfraProvider+DeepInfraConfiguredChatClient, HPD-Agent.Providers.DeepInfra" },
        { "fireworks", "HPD.Agent.Providers.Fireworks.FireworksProvider+FireworksConfiguredChatClient, HPD-Agent.Providers.Fireworks" },
        { "mistral", "HPD.Agent.Providers.Mistral.MistralProvider+MistralConfiguredChatClient, HPD-Agent.Providers.Mistral" },
        { "ollama", "HPD.Agent.Providers.Ollama.OllamaConfiguredChatClient, HPD-Agent.Providers.Ollama" },
        { "onnx-runtime", "HPD.Agent.Providers.OnnxRuntime.StructuredToolCallingOnnxRuntimeChatClient, HPD-Agent.Providers.OnnxRuntime" },
        { "together", "HPD.Agent.Providers.Together.TogetherProvider+TogetherConfiguredChatClient, HPD-Agent.Providers.Together" }
    };

    [Theory]
    [MemberData(nameof(PassThroughAdapters))]
    public async Task Shipped_adapter_preserves_one_terminal_usage_snapshot(
        string providerKey,
        string assemblyQualifiedType)
    {
        var adapterType = Type.GetType(assemblyQualifiedType, throwOnError: true)!;
        using var inner = new TerminalUsageChatClient();
        using var adapter = (IChatClient)CreateAdapter(adapterType, inner);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in adapter.GetStreamingResponseAsync([new(ChatRole.User, "fixture")]))
            updates.Add(update);

        var usage = updates.SelectMany(static update => update.Contents).OfType<UsageContent>()
            .Should().ContainSingle().Subject.Details;
        usage.InputTokenCount.Should().Be(11);
        usage.OutputTokenCount.Should().Be(7);
        ProviderStreamingUsageSemanticsCatalog.Resolve(providerKey, ProviderClientFamily.Chat)
            .Should().Be(UsageUpdateSemantics.FinalOnly);
    }

    [Fact]
    public void HuggingFace_projection_maps_a_usage_chunk_to_one_usage_content()
    {
        var wrapper = Type.GetType(
            "HPD.Agent.Providers.HuggingFace.HuggingFaceProvider+HuggingFaceConfiguredChatClient, HPD-Agent.Providers.HuggingFace",
            throwOnError: true)!;
        var method = wrapper.GetMethod("ToChatResponseUpdate", BindingFlags.Static | BindingFlags.NonPublic)!;
        var chunkType = method.GetParameters()[0].ParameterType;
        var chunk = Activator.CreateInstance(chunkType)!;
        var choicesProperty = chunkType.GetProperty("Choices")!;
        var choiceType = choicesProperty.PropertyType.GetGenericArguments()[0];
        choicesProperty.SetValue(chunk, Activator.CreateInstance(typeof(List<>).MakeGenericType(choiceType)));
        var usageProperty = chunkType.GetProperty("Usage")!;
        var usage = Activator.CreateInstance(usageProperty.PropertyType)!;
        usageProperty.PropertyType.GetProperty("PromptTokens")!.SetValue(usage, 11);
        usageProperty.PropertyType.GetProperty("CompletionTokens")!.SetValue(usage, 7);
        usageProperty.PropertyType.GetProperty("TotalTokens")!.SetValue(usage, 18);
        usageProperty.SetValue(chunk, usage);

        var update = (ChatResponseUpdate)method.Invoke(null, [chunk])!;

        var details = update.Contents.OfType<UsageContent>().Should().ContainSingle().Subject.Details;
        details.InputTokenCount.Should().Be(11);
        details.OutputTokenCount.Should().Be(7);
        ProviderStreamingUsageSemanticsCatalog.Resolve("huggingface", ProviderClientFamily.Chat)
            .Should().Be(UsageUpdateSemantics.FinalOnly);
    }

    private static object CreateAdapter(Type adapterType, IChatClient inner)
    {
        var constructor = adapterType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .First(candidate => candidate.GetParameters().Any(parameter => parameter.ParameterType == typeof(IChatClient)));
        var arguments = constructor.GetParameters().Select(parameter =>
        {
            if (parameter.ParameterType == typeof(IChatClient)) return (object?)inner;
            if (parameter.ParameterType == typeof(string)) return "fixture-model";
            if (parameter.ParameterType == typeof(Uri)) return new Uri("https://fixture.invalid/");
            if (parameter.HasDefaultValue) return parameter.DefaultValue;
            if (parameter.ParameterType.IsValueType) return Activator.CreateInstance(parameter.ParameterType);
            return null;
        }).ToArray();
        return constructor.Invoke(arguments);
    }

    private sealed class TerminalUsageChatClient : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("fixture", defaultModelId: "fixture-model");
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "fixture")));
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate { Contents = [new TextContent("fixture")] };
            await Task.Yield();
            yield return new ChatResponseUpdate
            {
                Contents = [new UsageContent(new UsageDetails { InputTokenCount = 11, OutputTokenCount = 7 })],
                FinishReason = ChatFinishReason.Stop
            };
        }
    }
}
