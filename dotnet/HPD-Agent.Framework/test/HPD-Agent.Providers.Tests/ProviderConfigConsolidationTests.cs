using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using Amazon.Runtime;
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using FluentAssertions;
using HPD.Agent.Providers.Anthropic;
using HPD.Agent.Providers.AzureAI;
using HPD.Agent.Providers.Bedrock;
using HPD.Agent.Providers.Cerebras;
using HPD.Agent.Providers.DeepSeek;
using HPD.Agent.Providers.GoogleAI;
using HPD.Agent.Providers.Hyperbolic;
using HPD.Agent.Providers.LMStudio;
using HPD.Agent.Providers.MiniMax;
#if NET10_0_OR_GREATER
using HPD.Agent.Providers.Mistral;
#endif
using HPD.Agent.Providers.Nebius;
using HPD.Agent.Providers.Nscale;
using HPD.Agent.Providers.NvidiaNim;
using HPD.Agent.Providers.Ollama;
using HPD.Agent.Providers.OnnxRuntime;
using HPD.Agent.Providers.OpenAI;
using HPD.Agent.Providers.OpenAICompatible;
using HPD.Agent.Providers.OVHcloud;
using HPD.Agent.Providers.Perplexity;
using HPD.Agent.Providers.SambaNova;
using HPD.Agent.Providers.Scaleway;
using HPD.Agent.Providers.SiliconFlow;
using HPD.Agent.Providers.Venice;
using HPD.Agent.Providers.Xai;
using HPD.Agent.Providers.Zai;
using Microsoft.Extensions.AI;

#if NET10_0_OR_GREATER
using HPD.Agent.Providers.Cohere;
using HPD.Agent.Providers.DashScope;
using HPD.Agent.Providers.DeepInfra;
using HPD.Agent.Providers.Fireworks;
using HPD.Agent.Providers.Groq;
using HPD.Agent.Providers.HuggingFace;
using HPD.Agent.Providers.Moonshot;
using HPD.Agent.Providers.Replicate;
using HPD.Agent.Providers.Together;
#endif

namespace HPD.Agent.Providers.Tests;

public class ProviderConfigConsolidationTests
{
    private static readonly string[] RuntimeChatOptionNames =
    [
        "Temperature",
        "TopP",
        "TopK",
        "MaxTokens",
        "MaxOutputTokens",
        "MaxOutputTokenCount",
        "MaxNewTokens",
        "NumPredict",
        "Stop",
        "StopSequences",
        "Seed",
        "RandomSeed",
        "FrequencyPenalty",
        "PresencePenalty",
        "ResponseFormat",
        "JsonSchema",
        "JsonSchemaName",
        "JsonSchemaDescription",
        "JsonSchemaIsStrict",
        "ToolChoice",
        "AllowParallelToolCalls",
        "ParallelToolCalls",
        "ReasoningEffort",
        "ReasoningEffortLevel",
        "ThinkingType",
        "SafePrompt"
    ];

    public static IEnumerable<object[]> ProviderConfigTypes()
    {
        yield return [typeof(OpenAICompatibleProviderConfig)];
        yield return [typeof(OpenAIProviderConfig)];
        yield return [typeof(AzureOpenAIProviderConfig)];
        yield return [typeof(AnthropicProviderConfig)];
        yield return [typeof(AzureAIProviderConfig)];
        yield return [typeof(BedrockProviderConfig)];
        yield return [typeof(GoogleAIProviderConfig)];
#if NET10_0_OR_GREATER
        yield return [typeof(MistralProviderConfig)];
#endif
        yield return [typeof(OllamaProviderConfig)];
        yield return [typeof(OnnxRuntimeProviderConfig)];
        yield return [typeof(XaiProviderConfig)];
        yield return [typeof(CerebrasProviderConfig)];
        yield return [typeof(DeepSeekProviderConfig)];
        yield return [typeof(SambaNovaProviderConfig)];
        yield return [typeof(HyperbolicProviderConfig)];
        yield return [typeof(OVHcloudProviderConfig)];
        yield return [typeof(NscaleProviderConfig)];
        yield return [typeof(VeniceProviderConfig)];
        yield return [typeof(PerplexityProviderConfig)];
        yield return [typeof(LMStudioProviderConfig)];
        yield return [typeof(NebiusProviderConfig)];
        yield return [typeof(NvidiaNimProviderConfig)];
        yield return [typeof(SiliconFlowProviderConfig)];
        yield return [typeof(ScalewayProviderConfig)];
        yield return [typeof(ZaiProviderConfig)];
        yield return [typeof(MiniMaxProviderConfig)];

#if NET10_0_OR_GREATER
        yield return [typeof(DashScopeProviderConfig)];
        yield return [typeof(DeepInfraProviderConfig)];
        yield return [typeof(FireworksProviderConfig)];
        yield return [typeof(GroqProviderConfig)];
        yield return [typeof(HuggingFaceProviderConfig)];
        yield return [typeof(MoonshotProviderConfig)];
        yield return [typeof(ReplicateProviderConfig)];
#endif
    }

    public static IEnumerable<object[]> EmptyChatProviderConfigTypes()
    {
        yield return [typeof(OpenAICompatibleProviderConfig)];
        yield return [typeof(AnthropicProviderConfig)];
#if NET10_0_OR_GREATER
        yield return [typeof(MistralProviderConfig)];
#endif
        yield return [typeof(XaiProviderConfig)];
        yield return [typeof(CerebrasProviderConfig)];
        yield return [typeof(DeepSeekProviderConfig)];
        yield return [typeof(SambaNovaProviderConfig)];
        yield return [typeof(HyperbolicProviderConfig)];
        yield return [typeof(OVHcloudProviderConfig)];
        yield return [typeof(NscaleProviderConfig)];
        yield return [typeof(VeniceProviderConfig)];
        yield return [typeof(PerplexityProviderConfig)];
        yield return [typeof(LMStudioProviderConfig)];
        yield return [typeof(NebiusProviderConfig)];
        yield return [typeof(NvidiaNimProviderConfig)];
        yield return [typeof(SiliconFlowProviderConfig)];
        yield return [typeof(ScalewayProviderConfig)];
        yield return [typeof(ZaiProviderConfig)];
        yield return [typeof(MiniMaxProviderConfig)];

#if NET10_0_OR_GREATER
        yield return [typeof(DeepInfraProviderConfig)];
        yield return [typeof(FireworksProviderConfig)];
        yield return [typeof(GroqProviderConfig)];
        yield return [typeof(HuggingFaceProviderConfig)];
        yield return [typeof(MoonshotProviderConfig)];
#endif
    }

    [Theory]
    [MemberData(nameof(ProviderConfigTypes))]
    public void ProviderConfig_ShouldNotExposeGenericRuntimeChatOptions(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type configType)
    {
        var propertyNames = configType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name);

        propertyNames.Should().NotIntersectWith(RuntimeChatOptionNames);
    }

    [Theory]
    [MemberData(nameof(EmptyChatProviderConfigTypes))]
    public void EmptyChatProviderConfig_ShouldHaveNoProviderSpecificProperties(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type configType)
    {
        configType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Should().BeEmpty();
    }

    [Fact]
    public void WithDeepSeek_ShouldRegisterChatProviderInBuilderRegistry()
    {
        var builder = new AgentBuilder()
            .WithDeepSeek(model: "deepseek-v4-flash", apiKey: "test");

        var provider = builder.ProviderRegistry.GetRequiredProvider<IChatClientProvider>("deepseek");

        provider.Should().NotBeNull();
        provider!.ProviderKey.Should().Be("deepseek");

        var chatConfig = builder.Config.EnsureChatClientConfig();
        chatConfig.ProviderKey.Should().Be("deepseek");
        chatConfig.ModelName.Should().Be("deepseek-v4-flash");
        chatConfig.ApiKey.Should().Be("test");
    }

    [Fact]
    public void WithOpenAI_ShouldRegisterChatProviderInBuilderRegistry()
    {
        var builder = new AgentBuilder()
            .WithOpenAI("gpt-4.1", apiKey: "test");

        builder.ProviderRegistry.GetRequiredProvider<IChatClientProvider>("openai")
            .Should().NotBeNull();
        builder.Config.EnsureChatClientConfig().ProviderKey.Should().Be("openai");
    }

    [Fact]
    public void WithAzureOpenAI_ShouldRegisterChatProviderInBuilderRegistry()
    {
        var builder = new AgentBuilder()
            .WithAzureOpenAI("https://hpd.openai.azure.com/", "gpt-4o", apiKey: "test");

        builder.ProviderRegistry.GetRequiredProvider<IChatClientProvider>("azure-openai")
            .Should().NotBeNull();
        builder.Config.EnsureChatClientConfig().ProviderKey.Should().Be("azure-openai");
    }

    [Fact]
    public void WithOllama_ShouldRegisterChatProviderInBuilderRegistry()
    {
        var builder = new AgentBuilder()
            .WithOllama("qwen3:latest");

        builder.ProviderRegistry.GetRequiredProvider<IChatClientProvider>("ollama")
            .Should().NotBeNull();
        builder.Config.EnsureChatClientConfig().ProviderKey.Should().Be("ollama");
    }

#if NET10_0_OR_GREATER
    [Fact]
    public void WithCohereEmbeddings_ShouldRegisterEmbeddingProviderInBuilderRegistry()
    {
        var builder = new AgentBuilder()
            .WithCohereEmbeddings("embed-english-v3.0", apiKey: "test");

        builder.ProviderRegistry.GetRequiredProvider<IEmbeddingGeneratorProvider>("cohere")
            .Should().NotBeNull();
        builder.Config.Clients.Embeddings.Should().NotBeNull();
    }
#endif

    [Fact]
    public void OllamaProviderConfig_ShouldOwnProviderConstructionOptions()
    {
        var builder = new AgentBuilder()
            .WithOllama("qwen3:latest", configure: ollama =>
            {
                ollama.TimeoutMs = 120000;
            });

        var chatConfig = builder.Config.EnsureChatClientConfig();
        var providerConfig = chatConfig.ProviderConfig as OllamaProviderConfig;

        providerConfig.Should().NotBeNull();
        providerConfig!.TimeoutMs.Should().Be(120000);
    }

    [Fact]
    public void OllamaProviderConfig_ShouldSerializeConstructionOptions()
    {
        var json = JsonSerializer.Serialize(
            new OllamaProviderConfig
            {
                TimeoutMs = 120000
            },
            OllamaJsonContext.Default.OllamaProviderConfig);

        json.Should().Contain("\"timeoutMs\":120000");

        var config = JsonSerializer.Deserialize(
            json,
            OllamaJsonContext.Default.OllamaProviderConfig);

        config.Should().NotBeNull();
        config!.TimeoutMs.Should().Be(120000);
    }

    [Fact]
    public void OllamaChatRequestOptions_ShouldWriteProviderSpecificRuntimeProperties()
    {
        var builder = new AgentBuilder()
            .WithOllama("qwen3:latest")
            .WithOllamaChatRequestOptions(new OllamaChatRequestOptions
            {
                KeepAlive = "10m",
                NumCtx = 8192,
                NumGpu = 99,
                UseMlock = true,
                MinP = 0.05f
            });

        var chatConfig = builder.Config.EnsureChatClientConfig();
        var defaults = chatConfig;

        defaults.Should().NotBeNull();
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("keep_alive", "10m");
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("num_ctx", 8192);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("num_gpu", 99);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("use_mlock", true);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("min_p", 0.05f);
    }

    [Fact]
    public void OllamaChatRequestOptions_ShouldSerializeRuntimeOptions()
    {
        var json = JsonSerializer.Serialize(
            new OllamaChatRequestOptions
            {
                KeepAlive = "10m",
                Template = "{{ .Prompt }}",
                NumCtx = 8192,
                UseMmap = false,
                MinP = 0.05f
            },
            OllamaJsonContext.Default.OllamaChatRequestOptions);

        json.Should().Contain("\"keepAlive\":\"10m\"");
        json.Should().Contain("\"template\":\"{{ .Prompt }}\"");
        json.Should().Contain("\"numCtx\":8192");
        json.Should().Contain("\"useMmap\":false");
        json.Should().Contain("\"minP\":0.05");

        var options = JsonSerializer.Deserialize(
            json,
            OllamaJsonContext.Default.OllamaChatRequestOptions);

        options.Should().NotBeNull();
        options!.KeepAlive.Should().Be("10m");
        options.Template.Should().Be("{{ .Prompt }}");
        options.NumCtx.Should().Be(8192);
        options.UseMmap.Should().BeFalse();
        options.MinP.Should().Be(0.05f);
    }

    [Fact]
    public void OllamaChatRequestOptions_ShouldNormalizeJsonAdditionalProperties()
    {
        using var document = JsonDocument.Parse("""
        {
          "keep_alive": "10m",
          "num_ctx": 8192,
          "use_mlock": true,
          "min_p": 0.05
        }
        """);

        var root = document.RootElement;

        OllamaChatRequestOptionKeys.Normalize("keep_alive", root.GetProperty("keep_alive"))
            .Should().Be("10m");
        OllamaChatRequestOptionKeys.Normalize("num_ctx", root.GetProperty("num_ctx"))
            .Should().Be(8192);
        OllamaChatRequestOptionKeys.Normalize("use_mlock", root.GetProperty("use_mlock"))
            .Should().Be(true);
        OllamaChatRequestOptionKeys.Normalize("min_p", root.GetProperty("min_p"))
            .Should().Be(0.05f);
    }

    [Fact]
    public void OnnxRuntimeProviderConfig_ShouldOwnExecutionProviderOptions()
    {
        var modelPath = Directory.GetCurrentDirectory();
        var builder = new AgentBuilder()
            .WithOnnxRuntime(modelPath, onnx =>
            {
                onnx.Providers = ["cuda", "cpu"];
                onnx.ExecutionProviderOptions = new Dictionary<string, Dictionary<string, string>>
                {
                    ["cuda"] = new()
                    {
                        ["device_id"] = "0"
                    }
                };
                onnx.HardwareDeviceType = "gpu";
                onnx.HardwareDeviceId = 0;
                onnx.EnableCaching = true;
                onnx.EnableStructuredToolCalling = true;
            });

        var chatConfig = builder.Config.EnsureChatClientConfig();
        var providerConfig = chatConfig.ProviderConfig as OnnxRuntimeProviderConfig;

        providerConfig.Should().NotBeNull();
        providerConfig!.ModelPath.Should().Be(modelPath);
        providerConfig.Providers.Should().Equal("cuda", "cpu");
        providerConfig.ExecutionProviderOptions!["cuda"]["device_id"].Should().Be("0");
        providerConfig.HardwareDeviceType.Should().Be("gpu");
        providerConfig.HardwareDeviceId.Should().Be(0);
        providerConfig.EnableCaching.Should().BeTrue();
        providerConfig.EnableStructuredToolCalling.Should().BeTrue();
    }

    [Fact]
    public void OnnxRuntimeChatRequestOptions_ShouldWriteProviderSpecificRuntimeProperties()
    {
        var builder = new AgentBuilder()
            .WithOnnxRuntime(Directory.GetCurrentDirectory())
            .WithOnnxRuntimeChatRequestOptions(new OnnxRuntimeChatRequestOptions
            {
                MinLength = 16,
                BatchSize = 1,
                DoSample = true,
                NumBeams = 4,
                NumReturnSequences = 2,
                EarlyStopping = true,
                LengthPenalty = 1.2f,
                ChunkSize = 256
            });

        var defaults = builder.Config.EnsureChatClientConfig();

        defaults.Should().NotBeNull();
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("min_length", 16);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("batch_size", 1);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("do_sample", true);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("num_beams", 4);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("num_return_sequences", 2);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("early_stopping", true);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("length_penalty", 1.2f);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("chunk_size", 256);
    }

    [Fact]
    public void OnnxRuntimeChatRequestOptions_ShouldSerializeRuntimeOptions()
    {
        var json = JsonSerializer.Serialize(
            new OnnxRuntimeChatRequestOptions
            {
                MinLength = 16,
                DoSample = true,
                RepetitionPenalty = 1.1f,
                NoRepeatNgramSize = 3,
                NumBeams = 4,
                PastPresentShareBuffer = true
            },
            OnnxRuntimeJsonContext.Default.OnnxRuntimeChatRequestOptions);

        json.Should().Contain("\"minLength\":16");
        json.Should().Contain("\"doSample\":true");
        json.Should().Contain("\"repetitionPenalty\":1.1");
        json.Should().Contain("\"noRepeatNgramSize\":3");
        json.Should().Contain("\"numBeams\":4");
        json.Should().Contain("\"pastPresentShareBuffer\":true");

        var options = JsonSerializer.Deserialize(
            json,
            OnnxRuntimeJsonContext.Default.OnnxRuntimeChatRequestOptions);

        options.Should().NotBeNull();
        options!.MinLength.Should().Be(16);
        options.DoSample.Should().BeTrue();
        options.RepetitionPenalty.Should().Be(1.1f);
        options.NoRepeatNgramSize.Should().Be(3);
        options.NumBeams.Should().Be(4);
        options.PastPresentShareBuffer.Should().BeTrue();
    }

    [Fact]
    public void ChatClientConfig_ShouldOwnSelectionAndPortableDefaults()
    {
        var config = new ChatClientConfig
        {
            ProviderKey = "xai",
            ModelName = "grok-4",
            Temperature = 0.2,
            TopP = 0.9,
            TopK = 40,
            MaxOutputTokens = 4096,
            Seed = 123,
            StopSequences = ["END"],
            Reasoning = new HPD.Agent.ReasoningOptions
            {
                Effort = HPD.Agent.ReasoningEffort.High,
                Output = HPD.Agent.ReasoningOutput.Summary
            }
        };

        config.Temperature.Should().Be(0.2);
        config.TopP.Should().Be(0.9);
        config.TopK.Should().Be(40);
        config.MaxOutputTokens.Should().Be(4096);
        config.Seed.Should().Be(123);
        config.StopSequences.Should().ContainSingle("END");
        config.Reasoning!.Effort.Should().Be(HPD.Agent.ReasoningEffort.High);
        config.Reasoning.Output.Should().Be(HPD.Agent.ReasoningOutput.Summary);
    }

    [Fact]
    public void AgentBuilder_WithReasoning_ShouldWriteChatConfig()
    {
        var builder = new AgentBuilder()
            .WithReasoning(
                HPD.Agent.ReasoningEffort.High,
                HPD.Agent.ReasoningOutput.Summary);

        var chatConfig = builder.Config.EnsureChatClientConfig();

        chatConfig.Should().NotBeNull();
        chatConfig!.Reasoning.Should().NotBeNull();
        chatConfig.Reasoning!.Effort.Should().Be(HPD.Agent.ReasoningEffort.High);
        chatConfig.Reasoning.Output.Should().Be(HPD.Agent.ReasoningOutput.Summary);
    }

    [Fact]
    public void ChatClientConfig_ShouldCompilePortableDefaultsToMeaiOptions()
    {
        var chatConfig = new ChatClientConfig { Temperature = 0.2, MaxOutputTokens = 1024 };
        var options = chatConfig.ToMicrosoftChatOptions();

        options.Should().NotBeNull();
        options!.Temperature.Should().Be(0.2f);
        options.MaxOutputTokens.Should().Be(1024);
    }

    [Fact]
    public void OpenAIProviderConfig_ShouldOwnProviderConstructionOptions()
    {
        var builder = new AgentBuilder()
            .WithOpenAI("gpt-4.1", configure: openAI =>
            {
                openAI.ChatApi = OpenAIChatApi.ChatCompletions;
                openAI.OrganizationId = "org_hpd";
                openAI.ProjectId = "proj_hpd";
                openAI.UserAgentApplicationId = "hpd-agent";
                openAI.NetworkTimeoutMs = 120000;
                openAI.EnableDistributedTracing = true;
            });

        var chatConfig = builder.Config.EnsureChatClientConfig();
        var providerConfig = chatConfig.ProviderConfig as OpenAIProviderConfig;

        providerConfig.Should().NotBeNull();
        providerConfig!.ChatApi.Should().Be(OpenAIChatApi.ChatCompletions);
        providerConfig.OrganizationId.Should().Be("org_hpd");
        providerConfig.ProjectId.Should().Be("proj_hpd");
        providerConfig.UserAgentApplicationId.Should().Be("hpd-agent");
        providerConfig.NetworkTimeoutMs.Should().Be(120000);
        providerConfig.EnableDistributedTracing.Should().Be(true);
    }

    [Fact]
    public void OpenAIProviderConfig_ShouldSerializeChatApiAsString()
    {
        var json = JsonSerializer.Serialize(
            new OpenAIProviderConfig
            {
                ChatApi = OpenAIChatApi.ChatCompletions,
                OrganizationId = "org_hpd",
                ProjectId = "proj_hpd"
            },
            OpenAIJsonContext.Default.OpenAIProviderConfig);

        json.Should().Contain("\"chatApi\":\"ChatCompletions\"");
        json.Should().Contain("\"organizationId\":\"org_hpd\"");

        var config = JsonSerializer.Deserialize(
            json,
            OpenAIJsonContext.Default.OpenAIProviderConfig);

        config.Should().NotBeNull();
        config!.ChatApi.Should().Be(OpenAIChatApi.ChatCompletions);
        config.OrganizationId.Should().Be("org_hpd");
        config.ProjectId.Should().Be("proj_hpd");
    }

    [Fact]
    public void AzureOpenAIProviderConfig_ShouldOwnAzureClientConstructionOptions()
    {
        var builder = new AgentBuilder()
            .WithAzureOpenAI(
                endpoint: "https://hpd.openai.azure.com/",
                model: "gpt-4o",
                configure: azure =>
                {
                    azure.ChatApi = OpenAIChatApi.ChatCompletions;
                    azure.ServiceVersion = AzureOpenAIServiceVersion.V2025_04_01_Preview;
                    azure.Audience = AzureOpenAIAudience.AzureGovernment.ToString();
                    azure.DefaultHeaders = new() { ["x-hpd"] = "agent" };
                    azure.DefaultQueryParameters = new() { ["api-version"] = "2025-04-01-preview" };
                    azure.UserAgentApplicationId = "hpd-agent";
                    azure.NetworkTimeoutMs = 120000;
                    azure.EnableDistributedTracing = true;
                });

        var chatConfig = builder.Config.EnsureChatClientConfig();
        var providerConfig = chatConfig.ProviderConfig as AzureOpenAIProviderConfig;

        providerConfig.Should().NotBeNull();
        providerConfig!.ChatApi.Should().Be(OpenAIChatApi.ChatCompletions);
        providerConfig.ServiceVersion.Should().Be(AzureOpenAIServiceVersion.V2025_04_01_Preview);
        providerConfig.Audience.Should().Be(AzureOpenAIAudience.AzureGovernment.ToString());
        providerConfig.DefaultHeaders.Should().Contain("x-hpd", "agent");
        providerConfig.DefaultQueryParameters.Should().Contain("api-version", "2025-04-01-preview");
        providerConfig.UserAgentApplicationId.Should().Be("hpd-agent");
        providerConfig.NetworkTimeoutMs.Should().Be(120000);
        providerConfig.EnableDistributedTracing.Should().Be(true);
    }

    [Fact]
    public void AzureOpenAIProviderConfig_ShouldSerializeAzureOptions()
    {
        var json = JsonSerializer.Serialize(
            new AzureOpenAIProviderConfig
            {
                ChatApi = OpenAIChatApi.ChatCompletions,
                ServiceVersion = AzureOpenAIServiceVersion.V2025_04_01_Preview,
                Audience = AzureOpenAIAudience.AzureGovernment.ToString(),
                DefaultQueryParameters = new() { ["api-version"] = "2025-04-01-preview" }
            },
            OpenAIJsonContext.Default.AzureOpenAIProviderConfig);

        json.Should().Contain("\"chatApi\":\"ChatCompletions\"");
        json.Should().Contain("\"serviceVersion\":\"V2025_04_01_Preview\"");
        json.Should().Contain("\"audience\":\"https://cognitiveservices.azure.us/.default\"");

        var config = JsonSerializer.Deserialize(
            json,
            OpenAIJsonContext.Default.AzureOpenAIProviderConfig);

        config.Should().NotBeNull();
        config!.ChatApi.Should().Be(OpenAIChatApi.ChatCompletions);
        config.ServiceVersion.Should().Be(AzureOpenAIServiceVersion.V2025_04_01_Preview);
        config.Audience.Should().Be(AzureOpenAIAudience.AzureGovernment.ToString());
        config.DefaultQueryParameters.Should().Contain("api-version", "2025-04-01-preview");
    }

    [Fact]
    public void GoogleAIProviderConfig_ShouldOwnProviderConstructionOptions()
    {
        var builder = new AgentBuilder()
            .WithGoogleAI("test-key", "gemini-2.0-flash", google =>
            {
                google.Platform = GoogleAIPlatform.VertexAI;
                google.ProjectId = "hpd-project";
                google.Region = "us-central1";
                google.ApiVersion = "v1beta1";
                google.CredentialsFile = "/tmp/google-credentials.json";
            });

        var chatConfig = builder.Config.EnsureChatClientConfig();
        var providerConfig = chatConfig.ProviderConfig as GoogleAIProviderConfig;

        providerConfig.Should().NotBeNull();
        providerConfig!.Platform.Should().Be(GoogleAIPlatform.VertexAI);
        providerConfig.ProjectId.Should().Be("hpd-project");
        providerConfig.Region.Should().Be("us-central1");
        providerConfig.ApiVersion.Should().Be("v1beta1");
        providerConfig.CredentialsFile.Should().Be("/tmp/google-credentials.json");
    }

    [Fact]
    public void GoogleAIProviderConfig_ShouldSerializePlatformAsString()
    {
        var json = JsonSerializer.Serialize(
            new GoogleAIProviderConfig
            {
                Platform = GoogleAIPlatform.VertexAI,
                ProjectId = "hpd-project",
                Region = "global"
            },
            GoogleAIJsonContext.Default.GoogleAIProviderConfig);

        json.Should().Contain("\"platform\":\"VertexAI\"");

        var config = JsonSerializer.Deserialize(
            json,
            GoogleAIJsonContext.Default.GoogleAIProviderConfig);

        config.Should().NotBeNull();
        config!.Platform.Should().Be(GoogleAIPlatform.VertexAI);
        config.ProjectId.Should().Be("hpd-project");
        config.Region.Should().Be("global");
    }

    [Fact]
    public void AzureAIProviderConfig_ShouldOwnProjectAndAzureOpenAIConstructionOptions()
    {
        var builder = new AgentBuilder()
            .WithAzureAI(
                endpoint: "https://hpd.services.ai.azure.com/api/projects/hpd",
                model: "gpt-4o",
                configure: azure =>
                {
                    azure.AuthMode = AzureAIAuthMode.DefaultAzureCredential;
                    azure.ProjectServiceVersion = AzureAIProjectServiceVersion.V1;
                    azure.OpenAIServiceVersion = AzureAIOpenAIServiceVersion.V2025_04_01_Preview;
                    azure.OpenAIConnectionId = "Azure.AI.OpenAI.AzureOpenAIClient";
                    azure.OpenAIAudience = AzureOpenAIAudience.AzureGovernment.ToString();
                    azure.OpenAIDefaultHeaders = new() { ["x-hpd"] = "agent" };
                    azure.OpenAIDefaultQueryParameters = new() { ["api-version"] = "2025-04-01-preview" };
                    azure.UserAgentApplicationId = "hpd-agent";
                    azure.NetworkTimeoutMs = 120000;
                    azure.EnableDistributedTracing = true;
                });

        var chatConfig = builder.Config.EnsureChatClientConfig();
        var providerConfig = chatConfig.ProviderConfig as AzureAIProviderConfig;

        providerConfig.Should().NotBeNull();
        providerConfig!.AuthMode.Should().Be(AzureAIAuthMode.DefaultAzureCredential);
        providerConfig.ProjectServiceVersion.Should().Be(AzureAIProjectServiceVersion.V1);
        providerConfig.OpenAIServiceVersion.Should().Be(AzureAIOpenAIServiceVersion.V2025_04_01_Preview);
        providerConfig.OpenAIConnectionId.Should().Be("Azure.AI.OpenAI.AzureOpenAIClient");
        providerConfig.OpenAIAudience.Should().Be(AzureOpenAIAudience.AzureGovernment.ToString());
        providerConfig.OpenAIDefaultHeaders.Should().Contain("x-hpd", "agent");
        providerConfig.OpenAIDefaultQueryParameters.Should().Contain("api-version", "2025-04-01-preview");
        providerConfig.UserAgentApplicationId.Should().Be("hpd-agent");
        providerConfig.NetworkTimeoutMs.Should().Be(120000);
        providerConfig.EnableDistributedTracing.Should().Be(true);
    }

    [Fact]
    public void AzureAIProviderConfig_ShouldSerializeAzureSdkOptions()
    {
        var json = JsonSerializer.Serialize(
            new AzureAIProviderConfig
            {
                AuthMode = AzureAIAuthMode.DefaultAzureCredential,
                ProjectServiceVersion = AzureAIProjectServiceVersion.V1,
                OpenAIServiceVersion = AzureAIOpenAIServiceVersion.V2025_04_01_Preview,
                OpenAIConnectionId = "Azure.AI.OpenAI.AzureOpenAIClient",
                OpenAIAudience = AzureOpenAIAudience.AzureGovernment.ToString()
            },
            AzureAIJsonContext.Default.AzureAIProviderConfig);

        json.Should().Contain("\"authMode\":\"DefaultAzureCredential\"");
        json.Should().Contain("\"projectServiceVersion\":\"V1\"");
        json.Should().Contain("\"openAIServiceVersion\":\"V2025_04_01_Preview\"");

        var config = JsonSerializer.Deserialize(
            json,
            AzureAIJsonContext.Default.AzureAIProviderConfig);

        config.Should().NotBeNull();
        config!.AuthMode.Should().Be(AzureAIAuthMode.DefaultAzureCredential);
        config.ProjectServiceVersion.Should().Be(AzureAIProjectServiceVersion.V1);
        config.OpenAIServiceVersion.Should().Be(AzureAIOpenAIServiceVersion.V2025_04_01_Preview);
        config.OpenAIConnectionId.Should().Be("Azure.AI.OpenAI.AzureOpenAIClient");
        config.OpenAIAudience.Should().Be(AzureOpenAIAudience.AzureGovernment.ToString());
    }

    [Fact]
    public void BedrockProviderConfig_ShouldOwnAwsClientConstructionOptions()
    {
        var builder = new AgentBuilder()
            .WithBedrock("anthropic.claude-3-5-sonnet-20240620-v1:0", "us-east-1", bedrock =>
            {
                bedrock.ProfileName = "hpd";
                bedrock.ServiceUrl = "https://bedrock-runtime.us-east-1.amazonaws.com";
                bedrock.AuthenticationRegion = "us-east-1";
                bedrock.UseFipsEndpoint = true;
                bedrock.UseDualstackEndpoint = true;
                bedrock.UseHttp = false;
                bedrock.RequestTimeoutMs = 120000;
                bedrock.ConnectTimeoutMs = 5000;
                bedrock.MaxRetryAttempts = 4;
                bedrock.RetryMode = RequestRetryMode.Adaptive;
                bedrock.DefaultConfigurationMode = DefaultConfigurationMode.CrossRegion;
                bedrock.MaxStaleConnectionRetries = 2;
                bedrock.AuthenticationServiceName = "bedrock";
                bedrock.AuthSchemePreference = ["sigv4"];
                bedrock.SigV4aSigningRegionSet = ["us-east-1", "us-west-2"];
                bedrock.IgnoreConfiguredEndpointUrls = true;
                bedrock.DisableHostPrefixInjection = true;
                bedrock.EndpointDiscoveryEnabled = false;
                bedrock.DisableRequestCompression = true;
                bedrock.RequestMinCompressionSizeBytes = 1024;
                bedrock.ClientAppId = "hpd-agent";
                bedrock.ThrottleRetries = true;
                bedrock.FastFailRequests = true;
                bedrock.CacheHttpClient = true;
                bedrock.HttpClientCacheSize = 4;
                bedrock.ProxyHost = "proxy.internal";
                bedrock.ProxyPort = 8080;
                bedrock.MaxConnectionsPerServer = 32;
                bedrock.LogResponse = false;
                bedrock.BufferSize = 8192;
                bedrock.ProgressUpdateIntervalMs = 1000;
                bedrock.ResignRetries = true;
                bedrock.AllowAutoRedirect = false;
                bedrock.LogMetrics = true;
                bedrock.DisableLogging = false;
            });

        var chatConfig = builder.Config.EnsureChatClientConfig();
        var providerConfig = chatConfig.ProviderConfig as BedrockProviderConfig;

        providerConfig.Should().NotBeNull();
        providerConfig!.Region.Should().Be("us-east-1");
        providerConfig.ProfileName.Should().Be("hpd");
        providerConfig.ServiceUrl.Should().Be("https://bedrock-runtime.us-east-1.amazonaws.com");
        providerConfig.AuthenticationRegion.Should().Be("us-east-1");
        providerConfig.UseFipsEndpoint.Should().Be(true);
        providerConfig.UseDualstackEndpoint.Should().Be(true);
        providerConfig.UseHttp.Should().Be(false);
        providerConfig.RequestTimeoutMs.Should().Be(120000);
        providerConfig.ConnectTimeoutMs.Should().Be(5000);
        providerConfig.MaxRetryAttempts.Should().Be(4);
        providerConfig.RetryMode.Should().Be(RequestRetryMode.Adaptive);
        providerConfig.DefaultConfigurationMode.Should().Be(DefaultConfigurationMode.CrossRegion);
        providerConfig.MaxStaleConnectionRetries.Should().Be(2);
        providerConfig.AuthenticationServiceName.Should().Be("bedrock");
        providerConfig.AuthSchemePreference.Should().Equal("sigv4");
        providerConfig.SigV4aSigningRegionSet.Should().Equal("us-east-1", "us-west-2");
        providerConfig.IgnoreConfiguredEndpointUrls.Should().Be(true);
        providerConfig.DisableHostPrefixInjection.Should().Be(true);
        providerConfig.EndpointDiscoveryEnabled.Should().Be(false);
        providerConfig.DisableRequestCompression.Should().Be(true);
        providerConfig.RequestMinCompressionSizeBytes.Should().Be(1024);
        providerConfig.ClientAppId.Should().Be("hpd-agent");
        providerConfig.ThrottleRetries.Should().Be(true);
        providerConfig.FastFailRequests.Should().Be(true);
        providerConfig.CacheHttpClient.Should().Be(true);
        providerConfig.HttpClientCacheSize.Should().Be(4);
        providerConfig.ProxyHost.Should().Be("proxy.internal");
        providerConfig.ProxyPort.Should().Be(8080);
        providerConfig.MaxConnectionsPerServer.Should().Be(32);
        providerConfig.LogResponse.Should().Be(false);
        providerConfig.BufferSize.Should().Be(8192);
        providerConfig.ProgressUpdateIntervalMs.Should().Be(1000);
        providerConfig.ResignRetries.Should().Be(true);
        providerConfig.AllowAutoRedirect.Should().Be(false);
        providerConfig.LogMetrics.Should().Be(true);
        providerConfig.DisableLogging.Should().Be(false);
    }

    [Fact]
    public void BedrockProviderConfig_ShouldSerializeAwsClientOptions()
    {
        var json = JsonSerializer.Serialize(
            new BedrockProviderConfig
            {
                Region = "us-west-2",
                ProfileName = "hpd",
                UseDualstackEndpoint = true,
                AuthenticationRegion = "us-west-2",
                AuthSchemePreference = ["sigv4", "sigv4a"],
                RetryMode = RequestRetryMode.Standard,
                ConnectTimeoutMs = 5000
            },
            BedrockJsonContext.Default.BedrockProviderConfig);

        json.Should().Contain("\"region\":\"us-west-2\"");
        json.Should().Contain("\"useDualstackEndpoint\":true");
        json.Should().Contain("\"authenticationRegion\":\"us-west-2\"");
        json.Should().Contain("\"authSchemePreference\":[\"sigv4\",\"sigv4a\"]");
        json.Should().Contain("\"retryMode\":\"Standard\"");

        var config = JsonSerializer.Deserialize(
            json,
            BedrockJsonContext.Default.BedrockProviderConfig);

        config.Should().NotBeNull();
        config!.Region.Should().Be("us-west-2");
        config.ProfileName.Should().Be("hpd");
        config.UseDualstackEndpoint.Should().Be(true);
        config.AuthenticationRegion.Should().Be("us-west-2");
        config.AuthSchemePreference.Should().Equal("sigv4", "sigv4a");
        config.RetryMode.Should().Be(RequestRetryMode.Standard);
        config.ConnectTimeoutMs.Should().Be(5000);
    }

#if NET10_0_OR_GREATER
    [Fact]
    public void CohereEmbeddingConfig_ShouldOwnPortableModelDefault()
    {
        var builder = new AgentBuilder()
            .WithCohereEmbeddings("embed-v4.0");

        var embeddingConfig = builder.Config.Clients!.Embeddings;
        embeddingConfig.Should().NotBeNull();
        embeddingConfig!.ModelName.Should().Be("embed-v4.0");
        embeddingConfig.ProviderConfig.Should().BeNull();
    }

#if NET10_0_OR_GREATER
    [Fact]
    public void HuggingFaceChatRequestOptions_ShouldWriteProviderSpecificRuntimeProperties()
    {
        var builder = new AgentBuilder()
            .WithHuggingFace("meta-llama/Meta-Llama-3-8B-Instruct")
            .WithHuggingFaceChatRequestOptions(new HuggingFaceChatRequestOptions
            {
                Logprobs = true,
                TopLogprobs = 5,
                N = 2,
                LogitBias = [1.0f, -2.0f],
                ToolPrompt = "Return tool calls as JSON."
            });

        var defaults = builder.Config.EnsureChatClientConfig();

        defaults.Should().NotBeNull();
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("logprobs", true);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("top_logprobs", 5);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("n", 2);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("tool_prompt", "Return tool calls as JSON.");
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties!["logit_bias"].Should().BeOfType<List<float>>();
    }

    [Fact]
    public void HuggingFaceChatRequestOptions_ShouldSerializeRuntimeOptions()
    {
        var json = JsonSerializer.Serialize(
            new HuggingFaceChatRequestOptions
            {
                Logprobs = true,
                TopLogprobs = 3,
                N = 2,
                LogitBias = [1.0f, -2.0f],
                ToolPrompt = "Use tools carefully."
            },
            HuggingFaceJsonContext.Default.HuggingFaceChatRequestOptions);

        json.Should().Contain("\"logprobs\":true");
        json.Should().Contain("\"topLogprobs\":3");
        json.Should().Contain("\"toolPrompt\":\"Use tools carefully.\"");

        var options = JsonSerializer.Deserialize(
            json,
            HuggingFaceJsonContext.Default.HuggingFaceChatRequestOptions);

        options.Should().NotBeNull();
        options!.Logprobs.Should().BeTrue();
        options.TopLogprobs.Should().Be(3);
        options.N.Should().Be(2);
        options.LogitBias.Should().Equal(1.0f, -2.0f);
        options.ToolPrompt.Should().Be("Use tools carefully.");
    }

    [Fact]
    public void HuggingFaceChatRequestOptions_ShouldMapGenericAndSpecificOptionsToChatRequest()
    {
        var options = new ChatOptions
        {
            ModelId = "mistralai/Mistral-7B-Instruct-v0.2",
            MaxOutputTokens = 256,
            Temperature = 0.2f,
            TopP = 0.9f,
            FrequencyPenalty = 0.4f,
            PresencePenalty = 0.5f,
            Seed = 42,
            StopSequences = ["</s>"]
        }.UseHuggingFaceChatRequestOptions(new HuggingFaceChatRequestOptions
        {
            Logprobs = true,
            TopLogprobs = 5,
            N = 2,
            LogitBias = [1.0f, -2.0f],
            ToolPrompt = "Return tool calls as JSON."
        });

        var request = HuggingFaceChatRequestOptionKeys.BuildRequest(
            [new ChatMessage { Role = ChatRole.User, Contents = { new TextContent("Hello") } }],
            options,
            "fallback-model",
            stream: false);

        request.Model.Should().Be("mistralai/Mistral-7B-Instruct-v0.2");
        request.MaxTokens.Should().Be(256);
        request.Temperature.Should().Be(0.2f);
        request.TopP.Should().Be(0.9f);
        request.FrequencyPenalty.Should().Be(0.4f);
        request.PresencePenalty.Should().Be(0.5f);
        request.Seed.Should().Be(42);
        request.Stop.Should().Equal("</s>");
        request.Logprobs.Should().BeTrue();
        request.TopLogprobs.Should().Be(5);
        request.N.Should().Be(2);
        request.LogitBias.Should().Equal(1.0f, -2.0f);
        request.ToolPrompt.Should().Be("Return tool calls as JSON.");
        request.Stream.Should().BeFalse();
    }
#endif

    [Fact]
    public void CohereChatRequestOptions_ShouldWriteProviderSpecificRuntimeProperties()
    {
        var builder = new AgentBuilder()
            .WithCohere("command-r-plus")
            .WithCohereChatRequestOptions(new CohereChatRequestOptions
            {
                StrictTools = true,
                CitationMode = CohereCitationMode.Accurate,
                SafetyMode = CohereSafetyMode.Strict,
                Logprobs = true,
                ThinkingTokenBudget = 2048,
                Priority = 1,
                Documents =
                [
                    new CohereChatDocument
                    {
                        Id = "doc-1",
                        Data = new Dictionary<string, object>
                        {
                            ["title"] = "HPD",
                            ["body"] = "Provider configuration belongs on provider config."
                        }
                    }
                ]
            });

        var defaults = builder.Config.EnsureChatClientConfig();

        defaults.Should().NotBeNull();
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("strict_tools", true);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("citation_mode", "accurate");
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("safety_mode", "strict");
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("logprobs", true);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("thinking_token_budget", 2048);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("priority", 1);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties!["documents"].Should().BeOfType<List<CohereChatDocument>>();
    }

    [Fact]
    public void CohereChatRequestOptions_ShouldSerializeRuntimeOptions()
    {
        var json = JsonSerializer.Serialize(
            new CohereChatRequestOptions
            {
                StrictTools = true,
                CitationMode = CohereCitationMode.Disabled,
                SafetyMode = CohereSafetyMode.Contextual,
                Logprobs = true,
                ThinkingEnabled = false,
                ThinkingTokenBudget = 512,
                Priority = 2,
                Documents =
                [
                    new CohereChatDocument
                    {
                        Text = "Short source document."
                    }
                ]
            },
            CohereJsonContext.Default.CohereChatRequestOptions);

        json.Should().Contain("\"strictTools\":true");
        json.Should().Contain("\"citationMode\":\"disabled\"");
        json.Should().Contain("\"safetyMode\":\"contextual\"");
        json.Should().Contain("\"thinkingEnabled\":false");
        json.Should().Contain("\"thinkingTokenBudget\":512");

        var options = JsonSerializer.Deserialize(
            json,
            CohereJsonContext.Default.CohereChatRequestOptions);

        options.Should().NotBeNull();
        options!.StrictTools.Should().BeTrue();
        options.CitationMode.Should().Be(CohereCitationMode.Disabled);
        options.SafetyMode.Should().Be(CohereSafetyMode.Contextual);
        options.ThinkingEnabled.Should().BeFalse();
        options.ThinkingTokenBudget.Should().Be(512);
        options.Documents![0].Text.Should().Be("Short source document.");
    }

    [Fact]
    public void CohereChatRequestOptions_ShouldMapReasoningAndSpecificOptionsToRawRequest()
    {
        var options = new ChatOptions
        {
            ModelId = "command-r-plus",
            Reasoning = new Microsoft.Extensions.AI.ReasoningOptions
            {
                Effort = Microsoft.Extensions.AI.ReasoningEffort.High
            }
        }.UseCohereChatRequestOptions(new CohereChatRequestOptions
        {
            StrictTools = true,
            CitationMode = CohereCitationMode.Fast,
            SafetyMode = CohereSafetyMode.Strict,
            Logprobs = true,
            ThinkingTokenBudget = 2048,
            Priority = 3
        });

        CohereChatRequestOptionKeys.ApplyRawRequestOptions(options);
        var request = options.RawRepresentationFactory!(null!) as global::Cohere.Chatv2Request;

        request.Should().NotBeNull();
        request!.StrictTools.Should().BeTrue();
        request.CitationOptions!.Mode.Should().Be(global::Cohere.CitationOptionsMode.Fast);
        request.SafetyMode.Should().Be(global::Cohere.Chatv2RequestSafetyMode.Strict);
        request.Logprobs.Should().BeTrue();
        request.Thinking!.Type.Should().Be(global::Cohere.ThinkingType.Enabled);
        request.Thinking.TokenBudget.Should().Be(2048);
        request.Priority.Should().Be(3);
    }

    [Fact]
    public void TogetherEmbeddingConfig_ShouldOwnPortableModelDefault()
    {
        var builder = new AgentBuilder()
            .WithTogetherEmbeddings("BAAI/bge-large-en-v1.5");

        var embeddingConfig = builder.Config.Clients!.Embeddings;
        embeddingConfig.Should().NotBeNull();
        embeddingConfig!.ModelName.Should().Be("BAAI/bge-large-en-v1.5");
        embeddingConfig.ProviderConfig.Should().BeNull();
    }

    [Fact]
    public void TogetherChatRequestOptions_ShouldWriteProviderSpecificRuntimeProperties()
    {
        var builder = new AgentBuilder()
            .WithTogether("deepseek-ai/DeepSeek-R1")
            .WithTogetherChatRequestOptions(new TogetherChatRequestOptions
            {
                ContextLengthExceededBehavior = TogetherContextLengthExceededBehavior.Truncate,
                RepetitionPenalty = 1.1,
                Logprobs = 5,
                Echo = true,
                N = 2,
                MinP = 0.05f,
                LogitBias = new Dictionary<string, float>
                {
                    ["105"] = 21.4f
                },
                Compliance = "strict",
                ChatTemplateKwargs = new Dictionary<string, object>
                {
                    ["enable_thinking"] = true
                },
                SafetyModel = "safety_model_name",
                ReasoningEnabled = true
            });

        var defaults = builder.Config.EnsureChatClientConfig();

        defaults.Should().NotBeNull();
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("context_length_exceeded_behavior", "truncate");
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("repetition_penalty", 1.1);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("logprobs", 5);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("echo", true);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("n", 2);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("min_p", 0.05f);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("compliance", "strict");
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("safety_model", "safety_model_name");
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("reasoning_enabled", true);
    }

    [Fact]
    public void TogetherChatRequestOptions_ShouldSerializeRuntimeOptions()
    {
        var json = JsonSerializer.Serialize(
            new TogetherChatRequestOptions
            {
                ContextLengthExceededBehavior = TogetherContextLengthExceededBehavior.Error,
                RepetitionPenalty = 1.2,
                Logprobs = 3,
                Echo = true,
                N = 2,
                MinP = 0.1f,
                LogitBias = new Dictionary<string, float>
                {
                    ["105"] = 21.4f
                },
                Compliance = "strict",
                ChatTemplateKwargs = new Dictionary<string, object>
                {
                    ["template"] = "chatml"
                },
                SafetyModel = "safety_model_name",
                ReasoningEnabled = false
            },
            TogetherJsonContext.Default.TogetherChatRequestOptions);

        json.Should().Contain("\"contextLengthExceededBehavior\":\"error\"");
        json.Should().Contain("\"repetitionPenalty\":1.2");
        json.Should().Contain("\"logprobs\":3");
        json.Should().Contain("\"reasoningEnabled\":false");

        var options = JsonSerializer.Deserialize(
            json,
            TogetherJsonContext.Default.TogetherChatRequestOptions);

        options.Should().NotBeNull();
        options!.ContextLengthExceededBehavior.Should().Be(TogetherContextLengthExceededBehavior.Error);
        options.RepetitionPenalty.Should().Be(1.2);
        options.Logprobs.Should().Be(3);
        options.ReasoningEnabled.Should().BeFalse();
        options.LogitBias!["105"].Should().Be(21.4f);
    }

    [Fact]
    public void TogetherChatRequestOptions_ShouldMapReasoningAndSpecificOptionsToRawRequest()
    {
        var options = new ChatOptions
        {
            ModelId = "deepseek-ai/DeepSeek-R1",
            FrequencyPenalty = 0.4f,
            PresencePenalty = 0.5f,
            Reasoning = new Microsoft.Extensions.AI.ReasoningOptions
            {
                Effort = Microsoft.Extensions.AI.ReasoningEffort.High
            }
        }.UseTogetherChatRequestOptions(new TogetherChatRequestOptions
        {
            ContextLengthExceededBehavior = TogetherContextLengthExceededBehavior.Truncate,
            RepetitionPenalty = 1.1,
            Logprobs = 5,
            Echo = true,
            N = 2,
            MinP = 0.05f,
            LogitBias = new Dictionary<string, float>
            {
                ["105"] = 21.4f
            },
            Compliance = "strict",
            ChatTemplateKwargs = new Dictionary<string, object>
            {
                ["enable_thinking"] = true
            },
            SafetyModel = "safety_model_name"
        });

        TogetherChatRequestOptionKeys.ApplyRawRequestOptions(options);
        var request = options.RawRepresentationFactory!(null!) as global::Together.ChatCompletionRequest;

        request.Should().NotBeNull();
        request!.FrequencyPenalty.Should().Be(0.4f);
        request.PresencePenalty.Should().Be(0.5f);
        request.ReasoningEffort.Should().Be(global::Together.ChatCompletionRequestReasoningEffort.High);
        request.Reasoning!.Enabled.Should().BeTrue();
        request.ContextLengthExceededBehavior.Should().Be(global::Together.ChatCompletionRequestContextLengthExceededBehavior.Truncate);
        request.RepetitionPenalty.Should().Be(1.1);
        request.Logprobs.Should().Be(5);
        request.Echo.Should().BeTrue();
        request.N.Should().Be(2);
        request.MinP.Should().Be(0.05f);
        request.LogitBias!["105"].Should().Be(21.4f);
        request.Compliance.Should().Be("strict");
        request.ChatTemplateKwargs.Should().NotBeNull();
        request.SafetyModel.Should().Be("safety_model_name");
    }

#if NET10_0_OR_GREATER
    [Fact]
    public void MistralChatRequestOptions_ShouldWriteProviderSpecificRuntimeProperties()
    {
        var builder = new AgentBuilder()
            .WithMistral("mistral-large-latest")
            .WithMistralChatRequestOptions(new MistralChatRequestOptions
            {
                SafePrompt = true,
                PredictionContent = "expected patch",
                PromptCacheKey = "workspace-thread-1",
                CompletionCount = 2
            });

        var defaults = builder.Config.EnsureChatClientConfig();

        defaults.Should().NotBeNull();
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().ContainKey(MistralChatRequestOptions.AdditionalPropertiesKey);
    }

    [Fact]
    public void MistralChatRequestOptions_ShouldSerializeRuntimeOptions()
    {
        var json = JsonSerializer.Serialize(
            new MistralChatRequestOptions
            {
                SafePrompt = true,
                PredictionContent = "expected patch",
                PromptCacheKey = "workspace-thread-1",
                CompletionCount = 2
            },
            MistralJsonContext.Default.MistralChatRequestOptions);

        json.Should().Contain("\"safePrompt\":true");
        json.Should().Contain("\"predictionContent\":\"expected patch\"");
        json.Should().Contain("\"promptCacheKey\":\"workspace-thread-1\"");
        json.Should().Contain("\"completionCount\":2");

        var options = JsonSerializer.Deserialize(
            json,
            MistralJsonContext.Default.MistralChatRequestOptions);

        options.Should().NotBeNull();
        options!.SafePrompt.Should().BeTrue();
        options.PredictionContent.Should().Be("expected patch");
        options.PromptCacheKey.Should().Be("workspace-thread-1");
        options.CompletionCount.Should().Be(2);
    }

    [Fact]
    public void MistralChatRequestOptions_ShouldMapReasoningAndSpecificOptionsToRawRequest()
    {
        var options = new ChatOptions
        {
            ModelId = "mistral-large-latest",
            Reasoning = new Microsoft.Extensions.AI.ReasoningOptions
            {
                Effort = Microsoft.Extensions.AI.ReasoningEffort.High
            }
        }.UseMistralChatRequestOptions(new MistralChatRequestOptions
        {
            SafePrompt = true,
            PredictionContent = "expected patch",
            PromptCacheKey = "workspace-thread-1",
            CompletionCount = 2
        });

        MistralChatRequestOptionKeys.ApplyRawRequestOptions(options);
        var request = options.RawRepresentationFactory!(null!) as global::Mistral.ChatCompletionRequest;

        request.Should().NotBeNull();
        request!.Model.Should().Be("mistral-large-latest");
        request.SafePrompt.Should().BeTrue();
        request.Prediction.Should().NotBeNull();
        request.Prediction!.Content.Should().Be("expected patch");
        request.PromptCacheKey.Should().Be("workspace-thread-1");
        request.N.Should().Be(2);
        request.ReasoningEffort.Should().Be(global::Mistral.ChatCompletionRequestReasoningEffort.High);
    }
#endif

    [Fact]
    public void MoonshotChatRequestOptions_ShouldWriteProviderSpecificRuntimeProperties()
    {
        var builder = new AgentBuilder()
            .WithMoonshot("kimi-k2.6")
            .WithMoonshotChatRequestOptions(new MoonshotChatRequestOptions
            {
                ThinkingKeep = MoonshotThinkingKeep.All
            });

        var defaults = builder.Config.EnsureChatClientConfig();

        defaults.Should().NotBeNull();
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("thinking_keep", "all");
    }

    [Fact]
    public void MoonshotChatRequestOptions_ShouldSerializeRuntimeOptions()
    {
        var json = JsonSerializer.Serialize(
            new MoonshotChatRequestOptions
            {
                ThinkingKeep = MoonshotThinkingKeep.All
            },
            MoonshotJsonContext.Default.MoonshotChatRequestOptions);

        json.Should().Contain("\"thinkingKeep\":\"all\"");

        var options = JsonSerializer.Deserialize(
            json,
            MoonshotJsonContext.Default.MoonshotChatRequestOptions);

        options.Should().NotBeNull();
        options!.ThinkingKeep.Should().Be(MoonshotThinkingKeep.All);
    }

    [Fact]
    public void DashScopeProviderConfig_ShouldOwnProviderConstructionOptions()
    {
        var builder = new AgentBuilder()
            .WithDashScope("qwen-plus", endpoint: "https://dashscope.aliyuncs.com/api/v1/", configure: dashScope =>
            {
                dashScope.WebsocketBaseAddress = "wss://dashscope.aliyuncs.com/api-ws/v1/inference/";
                dashScope.WorkspaceId = "ws_hpd";
                dashScope.SocketPoolSize = 16;
                dashScope.TimeoutSeconds = 180;
            })
            .WithDashScopeChatRequestOptions(new DashScopeChatRequestOptions { UseVl = true });

        var chatConfig = builder.Config.EnsureChatClientConfig();
        var providerConfig = chatConfig.ProviderConfig as DashScopeProviderConfig;

        providerConfig.Should().NotBeNull();
        chatConfig.Endpoint.Should().Be("https://dashscope.aliyuncs.com/api/v1/");
        providerConfig.WebsocketBaseAddress.Should().Be("wss://dashscope.aliyuncs.com/api-ws/v1/inference/");
        providerConfig.WorkspaceId.Should().Be("ws_hpd");
        providerConfig.SocketPoolSize.Should().Be(16);
        providerConfig.TimeoutSeconds.Should().Be(180);
        ((DashScopeChatRequestOptions)chatConfig.ProviderOptions!).UseVl.Should().BeTrue();
    }

    [Fact]
    public void DashScopeChatRequestOptions_ShouldWriteProviderSpecificRuntimeProperties()
    {
        var builder = new AgentBuilder()
            .WithDashScope("qwen-plus")
            .WithDashScopeChatRequestOptions(new DashScopeChatRequestOptions
            {
                EnableSearch = true,
                ThinkingBudget = 1024,
                EnableCodeInterpreter = true,
                SearchOptions = new DashScopeSearchRequestOptions
                {
                    EnableCitation = true,
                    SearchStrategy = DashScopeSearchStrategy.Turbo
                }
            });

        var chatConfig = builder.Config.EnsureChatClientConfig();
        var defaults = chatConfig;

        defaults.Should().NotBeNull();
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("enable_search", true);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("thinking_budget", 1024);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties.Should().Contain("enable_code_interpreter", true);
        defaults.ToMicrosoftChatOptions()!.AdditionalProperties!["search_options"].Should().BeOfType<DashScopeSearchRequestOptions>();
    }

    [Fact]
    public void DashScopeChatRequestOptions_ShouldSerializeRuntimeOptions()
    {
        var json = JsonSerializer.Serialize(
            new DashScopeChatRequestOptions
            {
                UseVl = true,
                EnableSearch = true,
                ThinkingBudget = 1024,
                SearchOptions = new DashScopeSearchRequestOptions
                {
                    EnableCitation = true,
                    SearchStrategy = DashScopeSearchStrategy.Turbo
                }
            },
            DashScopeJsonContext.Default.DashScopeChatRequestOptions);

        json.Should().Contain("\"useVl\":true");
        json.Should().Contain("\"enableSearch\":true");
        json.Should().Contain("\"thinkingBudget\":1024");
        json.Should().Contain("\"searchOptions\"");

        var options = JsonSerializer.Deserialize(
            json,
            DashScopeJsonContext.Default.DashScopeChatRequestOptions);

        options.Should().NotBeNull();
        options!.UseVl.Should().BeTrue();
        options.EnableSearch.Should().BeTrue();
        options.ThinkingBudget.Should().Be(1024);
        options.SearchOptions!.EnableCitation.Should().BeTrue();
        options.SearchOptions.SearchStrategy.Should().Be(DashScopeSearchStrategy.Turbo);
    }
#endif
}
