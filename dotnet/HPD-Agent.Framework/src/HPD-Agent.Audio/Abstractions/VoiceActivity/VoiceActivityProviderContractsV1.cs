using HPD.Agent.Providers;

namespace HPD.Agent.Audio.ProviderContracts.VoiceActivity;

/// <summary>Creates one typed voice-activity source product for the selected provider family.</summary>
public interface IVoiceActivitySourceProviderV1 : IProvider
{
    /// <summary>Creates a source from the captured provider configuration and lifecycle context.</summary>
    VoiceActivitySourceProductV1 CreateVoiceActivitySource(
        ProviderClientConfig configuration,
        ProviderComponentLifetimeContext context,
        IServiceProvider? services = null);
}

/// <summary>Closes the two executable voice-activity source ownership models.</summary>
public abstract record VoiceActivitySourceProductV1
{
    private VoiceActivitySourceProductV1() { }

    /// <summary>Contains a borrowed, synchronous source.</summary>
    public sealed record BorrowedSynchronous : VoiceActivitySourceProductV1
    {
        /// <summary>Creates a product around the exact provider-owned source.</summary>
        public BorrowedSynchronous(IBorrowedSynchronousVoiceActivitySourceV1 source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            if (source.Capabilities.InputOwnership != VoiceActivityInputOwnershipV1.BorrowedSynchronous)
                throw new ArgumentException("The source does not declare borrowed synchronous ownership.", nameof(source));
        }

        /// <summary>Gets the provider-owned source.</summary>
        public IBorrowedSynchronousVoiceActivitySourceV1 Source { get; }
    }

    /// <summary>Contains an isolated transferred source with durable settlement lookup.</summary>
    public sealed record Transferred : VoiceActivitySourceProductV1
    {
        /// <summary>Creates a product around the exact provider-owned source.</summary>
        public Transferred(ITransferredVoiceActivitySourceV1 source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            if (source.Capabilities.InputOwnership == VoiceActivityInputOwnershipV1.BorrowedSynchronous)
                throw new ArgumentException("A transferred product cannot advertise borrowed ownership.", nameof(source));
        }

        /// <summary>Gets the provider-owned source.</summary>
        public ITransferredVoiceActivitySourceV1 Source { get; }
    }
}

/// <summary>Creates the typed Audio product from one already-resolved provider-family handle.</summary>
public static class VoiceActivitySourceProviderBindingV1
{
    /// <summary>Binds a statically supplied provider using its declared voice-activity lifetime.</summary>
    public static VoiceActivitySourceProductV1 Create(
        IVoiceActivitySourceProviderV1 provider,
        ProviderClientConfig configuration,
        ProviderComponentLifetimeContext context,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(context);
        if (!provider.GetMetadata().Families.TryGetValue(ProviderClientFamily.VoiceActivityDetection, out var descriptor))
            throw new ArgumentException("The provider does not declare voice activity detection.", nameof(provider));

        return Create(new ResolvedProviderFamily<IVoiceActivitySourceProviderV1>(provider,
            ProviderClientFamily.VoiceActivityDetection, configuration, descriptor.Lifetime), context, services);
    }

    /// <summary>Creates the product without another registry lookup or mutable configuration handoff.</summary>
    public static VoiceActivitySourceProductV1 Create(
        ResolvedProviderFamily<IVoiceActivitySourceProviderV1> resolved,
        ProviderComponentLifetimeContext context,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(context);
        if (resolved.Family != ProviderClientFamily.VoiceActivityDetection)
            throw new ArgumentException("The resolved family is not voice activity detection.", nameof(resolved));
        if (context.Lifetime != resolved.Lifetime)
            throw new ArgumentException("The component lifecycle context contradicts the resolved family handle.", nameof(context));

        return resolved.Provider.CreateVoiceActivitySource(resolved.Configuration, context, services)
            ?? throw new InvalidOperationException("The voice activity provider returned no source product.");
    }
}
