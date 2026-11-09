using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.ProviderContracts.EndOfTurn;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Audio.ElevenLabs;
using HPD.Agent.Providers.Audio.OpenAI;
using HPD.Agent.Providers.Audio.Silero;
using HPD.Agent.Providers.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using HPD.Agent.Secrets;

namespace HPD.Agent.Audio.Tests;

public sealed class AudioProviderV9ContractTests
{
    [Fact]
    public void OpenAiBuilders_AuthorAtomicApiKeySelectionsForAllAudioFamilies()
    {
        var builder = new AgentBuilder(new AgentConfig())
            .WithEventApplicationIdentity("HPD.Agent.Audio.Tests")
            .WithOpenAISpeechToText("whisper-test")
            .WithOpenAITextToSpeech("tts-test")
            .WithOpenAIRealtime("realtime-test");

        AssertSelection(builder.Config.Clients.SpeechToText!, "openai", "openai:ApiKey");
        AssertSelection(builder.Config.Clients.TextToSpeech!, "openai", "openai:ApiKey");
        AssertSelection(builder.Config.Clients.Realtime!, "openai", "openai:ApiKey");
    }

    [Fact]
    public void ElevenLabsBuilders_AuthorAtomicApiKeySelectionsForBothFamilies()
    {
        var builder = new AgentBuilder(new AgentConfig())
            .WithEventApplicationIdentity("HPD.Agent.Audio.Tests")
            .WithElevenLabsSpeechToText("scribe-test")
            .WithElevenLabsTextToSpeech("eleven-test");

        AssertSelection(builder.Config.Clients.SpeechToText!, "elevenlabs", "elevenlabs:ApiKey");
        AssertSelection(builder.Config.Clients.TextToSpeech!, "elevenlabs", "elevenlabs:ApiKey");
    }

    [Fact]
    public void ConsumerGeneratedComposition_ContainsBothElevenLabsAudioFamilies()
    {
        var composition = Assert.IsType<ProviderComposition>(ProviderCompositionHost.Current);

        Assert.True(composition.Runtime.TryGetFactory(
            ElevenLabsAudioProvider.Key,
            "platform",
            ProviderClientFamily.SpeechToText,
            out var speechToText));
        Assert.NotNull(speechToText);
        Assert.True(composition.Runtime.TryGetFactory(
            ElevenLabsAudioProvider.Key,
            "platform",
            ProviderClientFamily.TextToSpeech,
            out var textToSpeech));
        Assert.NotNull(textToSpeech);
    }

    [Fact]
    public async Task GeneratedComposition_RoutesOpenAiChatAndAllAudioFamiliesThroughOneBackend()
    {
        var composition = Assert.IsType<ProviderComposition>(ProviderCompositionHost.Current);
        Assert.True(composition.Descriptors.TryGet("openai", out var descriptor));
        Assert.NotNull(descriptor);

        ProviderClientFamily[] expectedFamilies =
        [
            ProviderClientFamily.Chat,
            ProviderClientFamily.SpeechToText,
            ProviderClientFamily.TextToSpeech,
            ProviderClientFamily.Realtime
        ];
        Assert.All(expectedFamilies, family => Assert.Contains(family, descriptor.Families.Keys));

        var backend = descriptor.Backends["platform"];
        Assert.Equal("platform", backend.BackendKey);
        Assert.Contains(
            backend.Authentication,
            authentication => expectedFamilies.All(family => authentication.SupportedFamilies.Contains(family)));
        Assert.All(expectedFamilies, family =>
            Assert.NotNull(composition.Runtime.GetFactory("openai", "platform", family)));

        var builder = new AgentBuilder(new AgentConfig(), composition)
            .WithEventApplicationIdentity("HPD.Agent.Audio.Tests")
            .WithOpenAI("chat-test")
            .WithOpenAISpeechToText("stt-test")
            .WithOpenAITextToSpeech("tts-test")
            .WithOpenAIRealtime("realtime-test");
        var provider = new ProviderRegistry(composition);
        var credentials = new ProviderAuthenticationCoordinator(
            new ExplicitSecretResolver(new Dictionary<string, string>
            {
                ["openai:ApiKey"] = "sk-test"
            }));
        using var services = new ServiceCollection()
            .AddSingleton<IProviderCredentialSource>(credentials)
            .BuildServiceProvider();
        var runtime = new ProviderFamilyClientRuntime(composition, provider, services);

        var chat = await runtime.CreateAsync<IChatClient>(builder.Config, ProviderClientFamily.Chat);
        await using var chatOwner = chat.Owner;
        var speechToText = await runtime.CreateAsync<ISpeechToTextClient>(
            builder.Config, ProviderClientFamily.SpeechToText);
        await using var speechToTextOwner = speechToText.Owner;
        var textToSpeech = await runtime.CreateAsync<ITextToSpeechClient>(
            builder.Config, ProviderClientFamily.TextToSpeech);
        await using var textToSpeechOwner = textToSpeech.Owner;
        var realtime = await runtime.CreateAsync<IRealtimeClient>(
            builder.Config, ProviderClientFamily.Realtime);
        await using var realtimeOwner = realtime.Owner;

        Assert.NotNull(chat.Client);
        Assert.NotNull(speechToText.Client);
        Assert.NotNull(textToSpeech.Client);
        Assert.NotNull(realtime.Client);
    }

    [Fact]
    public void AudioProviders_ExposeOnlyUniformAsyncFamilyFactories()
    {
        var openAi = new OpenAIAudioProvider();
        var elevenLabs = new ElevenLabsAudioProvider();
        var silero = new SileroAudioProvider();

        Assert.IsAssignableFrom<IProviderClientFactory<ISpeechToTextClient>>(openAi);
        Assert.IsAssignableFrom<IProviderClientFactory<ITextToSpeechClient>>(openAi);
        Assert.IsAssignableFrom<IProviderClientFactory<IRealtimeClient>>(openAi);
        Assert.IsAssignableFrom<IProviderClientFactory<ISpeechToTextClient>>(elevenLabs);
        Assert.IsAssignableFrom<IProviderClientFactory<ITextToSpeechClient>>(elevenLabs);
        Assert.IsAssignableFrom<IProviderClientFactory<VoiceActivitySourceProduct>>(silero);
        Assert.Equal(ProviderFamilyLifetime.StatefulPerAudioSession,
            silero.GetMetadata().Families[ProviderClientFamily.VoiceActivityDetection].Lifetime);
    }

    [Fact]
    public async Task FamilyNeutralRuntime_ConstructsLeafOwnedEndOfTurnFamilyWithExactLifetime()
    {
        var descriptor = new TestDescriptor();
        var provider = new TestEndOfTurnProvider();
        var composition = ProviderComposition.Create([
            CreateEndOfTurnFragment(descriptor, provider)
        ]);
        var registry = new ProviderRegistry(composition);
        registry.Register(provider);
        var credentialSource = new ProviderAuthenticationCoordinator(new EmptySecretResolver());
        var services = new ServiceCollection()
            .AddSingleton<IProviderCredentialSource>(credentialSource)
            .BuildServiceProvider();
        var config = new AgentConfig
        {
            ProviderDefaults =
            {
                new AgentProviderFamilyDefault
                {
                    Family = ProviderClientFamily.EndOfTurnDetection,
                    ProviderKey = "end-test",
                    BackendKey = "local"
                }
            },
            ProviderProfiles =
            {
                new AgentProviderBackendProfile
                {
                    ProviderKey = "end-test",
                    BackendKey = "local",
                    Clients = new AgentClientsConfig
                    {
                        EndOfTurn = new EndOfTurnClientConfig
                        {
                            Provider = new ProviderReference
                            {
                                Key = "end-test",
                                Backend = "local",
                                Authentication = new AnonymousProviderAuthentication()
                            }
                        }
                    }
                }
            }
        };

        var runtime = new ProviderFamilyClientRuntime(composition, registry, services);
        var construction = await runtime.CreateAsync<IEndOfTurnDetector>(
            config,
            ProviderClientFamily.EndOfTurnDetection);
        await using (construction.Owner)
        {
            Assert.Same(provider.Detector, construction.Client);
            Assert.Equal(ProviderFamilyLifetime.StatefulPerTurn, provider.ObservedLifetime);
        }
    }

    [Fact]
    public async Task FamilyNeutralRuntime_UsesManifestDefaultAuthenticationWithoutAuthorizer()
    {
        var (runtime, provider) = CreateEndOfTurnRuntime();
        var runClients = new AgentClientsConfig
        {
            EndOfTurn = new EndOfTurnClientConfig
            {
                Provider = new ProviderReference { Key = "end-test" }
            }
        };

        var construction = await runtime.CreateAsync<IEndOfTurnDetector>(
            new AgentConfig(),
            ProviderClientFamily.EndOfTurnDetection,
            runClients,
            ProviderSelectionSource.RemoteAgent);

        await using (construction.Owner)
            Assert.Same(provider.Detector, construction.Client);
    }

    [Fact]
    public async Task FamilyNeutralRuntime_RequiresAuthorizerForExplicitRunAuthentication()
    {
        var (runtime, _) = CreateEndOfTurnRuntime();
        var runClients = new AgentClientsConfig
        {
            EndOfTurn = new EndOfTurnClientConfig
            {
                Provider = new ProviderReference
                {
                    Key = "end-test",
                    Authentication = new AnonymousProviderAuthentication()
                }
            }
        };

        var exception = await Assert.ThrowsAsync<AgentRunConfigurationException>(() =>
            runtime.CreateAsync<IEndOfTurnDetector>(
                new AgentConfig(),
                ProviderClientFamily.EndOfTurnDetection,
                runClients,
                ProviderSelectionSource.RemoteAgent).AsTask());

        Assert.Equal("AuthenticationSelectionAuthorizerRequired", exception.Code);
        Assert.Equal("clients.EndOfTurnDetection.provider.authentication", exception.Path);
    }

    private static (ProviderFamilyClientRuntime Runtime, TestEndOfTurnProvider Provider) CreateEndOfTurnRuntime()
    {
        var descriptor = new TestDescriptor();
        var provider = new TestEndOfTurnProvider();
        var composition = ProviderComposition.Create([
            CreateEndOfTurnFragment(descriptor, provider)
        ]);
        var registry = new ProviderRegistry(composition);
        registry.Register(provider);
        var services = new ServiceCollection()
            .AddSingleton<IProviderCredentialSource>(
                new ProviderAuthenticationCoordinator(new EmptySecretResolver()))
            .BuildServiceProvider();
        return (new ProviderFamilyClientRuntime(composition, registry, services), provider);
    }

    private static ProviderManifestFragment CreateEndOfTurnFragment(
        IProviderDescriptor descriptor,
        TestEndOfTurnProvider provider) => new(
        [descriptor],
        [new ProviderRuntimeFactoryRegistration(
            "end-test",
            ["local"],
            [ProviderClientFamily.EndOfTurnDetection],
            () => provider)],
        [],
        []);

    private static void AssertSelection(ProviderClientConfig config, string providerKey, string secretKey)
    {
        Assert.Equal(providerKey, config.Provider!.Key);
        Assert.Equal(secretKey,
            Assert.IsType<ApiKeyProviderAuthentication>(config.Provider.Authentication).SecretKey);
        Assert.Null(config.GetType().GetProperty("ApiKey"));
        Assert.Null(config.GetType().GetProperty("AuthenticationKey"));
    }

    private sealed class TestEndOfTurnProvider : IEndOfTurnDetectorProvider
    {
        public TestDetector Detector { get; } = new();
        public ProviderFamilyLifetime? ObservedLifetime { get; private set; }
        public string ProviderKey => "end-test";
        public string DisplayName => "End test";
        public ProviderClientCredentialBinding ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) =>
            ProviderClientCredentialBinding.ConstructionTime;
        public ValueTask<ProviderClientConstruction<IEndOfTurnDetector>> CreateAsync(
            ProviderClientConstructionContext context,
            CancellationToken cancellationToken = default)
        {
            ObservedLifetime = context.Lifetime.Lifetime;
            return ValueTask.FromResult(new ProviderClientConstruction<IEndOfTurnDetector>
            {
                Client = Detector,
                Owner = ProviderClientConstructionUtilities.Own()
            });
        }
        public ProviderMetadata GetMetadata() => new()
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.EndOfTurnDetection] = new()
                {
                    Family = ProviderClientFamily.EndOfTurnDetection,
                    Lifetime = ProviderFamilyLifetime.StatefulPerTurn
                }
            }
        };
        public ProviderValidationResult ValidateConfiguration(EffectiveProviderClientConfig config) =>
            ProviderValidationResult.Success();
        public HPD.Agent.ErrorHandling.IProviderErrorHandler CreateErrorHandler() =>
            new HPD.Agent.ErrorHandling.GenericErrorHandler();
    }

    private sealed class TestDetector : IEndOfTurnDetector
    {
        public ValueTask<EndOfTurnDetectionResult> DetectAsync(
            EndOfTurnDetectionRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
            new EndOfTurnDetectionResult { IsEndOfTurn = true });
    }

    private sealed class TestDescriptor : IProviderDescriptor
    {
        private static readonly ProviderFamilyDescriptor Family = new()
        {
            Family = ProviderClientFamily.EndOfTurnDetection,
            Lifetime = ProviderFamilyLifetime.StatefulPerTurn
        };
        public string ProviderKey => "end-test";
        public string DisplayName => "End test";
        public Uri? DocumentationUri => null;
        public IReadOnlyDictionary<ProviderClientFamily, ProviderFamilyDescriptor> Families { get; } =
            new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.EndOfTurnDetection] = Family
            };
        public IReadOnlyDictionary<string, ProviderBackendDescriptor> Backends { get; } =
            new Dictionary<string, ProviderBackendDescriptor>
            {
                ["local"] = new()
                {
                    BackendKey = "local",
                    IsDefault = true,
                    Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
                    {
                        [ProviderClientFamily.EndOfTurnDetection] = Family
                    },
                    Authentication =
                    [
                        new ProviderAuthenticationDescriptor
                        {
                            Kind = ProviderAuthenticationKind.Anonymous,
                            IsDefault = true,
                            SupportedFamilies = new HashSet<ProviderClientFamily>
                                { ProviderClientFamily.EndOfTurnDetection }
                        }
                    ]
                }
            };
        public IReadOnlyList<string> Aliases => [];
    }

    private sealed class EmptySecretResolver : ISecretResolver
    {
        public ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ResolvedSecret?>(null);
    }
}
