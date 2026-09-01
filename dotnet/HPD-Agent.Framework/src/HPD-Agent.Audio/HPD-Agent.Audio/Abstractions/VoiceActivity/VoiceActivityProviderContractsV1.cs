using HPD.Agent.Providers;

namespace HPD.Agent.Audio.ProviderContracts.VoiceActivity;

/// <summary>Creates one typed voice-activity source through the uniform asynchronous provider factory contract.</summary>
public interface IVoiceActivitySourceProviderV1 : IProvider, IProviderClientFactory<VoiceActivitySourceProductV1>;

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

/// <summary>Describes the exact provider cut visible while composing source middleware.</summary>
public sealed record VoiceActivitySourceMiddlewareContextV1
{
    /// <summary>Creates an immutable middleware context.</summary>
    public VoiceActivitySourceMiddlewareContextV1(
        string providerKey,
        ProviderFamilyLifetime lifetime)
    {
        if (string.IsNullOrWhiteSpace(providerKey)) throw new ArgumentException("A provider key is required.", nameof(providerKey));
        if (!Enum.IsDefined(lifetime)) throw new ArgumentOutOfRangeException(nameof(lifetime));
        ProviderKey = providerKey;
        Lifetime = lifetime;
    }

    /// <summary>Gets the canonical provider key.</summary>
    public string ProviderKey { get; }
    /// <summary>Gets the provider's declared family lifetime.</summary>
    public ProviderFamilyLifetime Lifetime { get; }
}

/// <summary>Wraps a typed source product without changing provider selection.</summary>
public interface IVoiceActivitySourceMiddlewareV1
{
    /// <summary>Wraps the current product and returns the product used by the next middleware.</summary>
    VoiceActivitySourceProductV1 Wrap(
        VoiceActivitySourceProductV1 current,
        VoiceActivitySourceMiddlewareContextV1 context);
}

/// <summary>Registers one explicitly supplied source middleware at a deterministic order.</summary>
public sealed record VoiceActivitySourceMiddlewareRegistrationV1
{
    /// <summary>Creates a registration.</summary>
    public VoiceActivitySourceMiddlewareRegistrationV1(
        string key,
        int order,
        IVoiceActivitySourceMiddlewareV1 middleware)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A middleware key is required.", nameof(key));
        ArgumentNullException.ThrowIfNull(middleware);
        Key = key;
        Order = order;
        Middleware = middleware;
    }

    /// <summary>Gets the stable middleware identity.</summary>
    public string Key { get; }
    /// <summary>Gets the primary ascending composition order.</summary>
    public int Order { get; }
    /// <summary>Gets the explicitly supplied middleware.</summary>
    public IVoiceActivitySourceMiddlewareV1 Middleware { get; }
}

/// <summary>Composes explicitly registered middleware by ascending order then ordinal key.</summary>
public static class VoiceActivitySourceMiddlewarePipelineV1
{
    /// <summary>Applies each middleware exactly once and rejects duplicate identities.</summary>
    public static VoiceActivitySourceProductV1 Apply(
        VoiceActivitySourceProductV1 product,
        VoiceActivitySourceMiddlewareContextV1 context,
        IReadOnlyList<VoiceActivitySourceMiddlewareRegistrationV1> registrations)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(registrations);
        var ordered = registrations.OrderBy(static item => item.Order)
            .ThenBy(static item => item.Key, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Select(static item => item.Key).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new ArgumentException("Voice activity middleware keys must be unique.", nameof(registrations));

        var current = product;
        foreach (var registration in ordered)
            current = registration.Middleware.Wrap(current, context)
                ?? throw new InvalidOperationException($"Voice activity middleware '{registration.Key}' returned no product.");
        return current;
    }
}
