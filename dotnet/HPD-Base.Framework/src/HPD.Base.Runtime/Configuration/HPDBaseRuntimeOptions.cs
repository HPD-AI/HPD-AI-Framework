using HPD.Base.Descriptors;
using HPD.Base.Runtime.Observability;

namespace HPD.Base.Runtime.Configuration;

public sealed class HPDBaseRuntimeOptions
{
    public required RuntimeDescriptor Runtime { get; set; }
    public required CompatibilityDescriptor Compatibility { get; set; }
    public string ManifestVersion { get; set; } = "1.0";
    public VisibilityLevel DefaultManifestVisibility { get; set; } = VisibilityLevel.Public;
    public bool FailFastOnDescriptorValidation { get; set; } = true;
    public HPDBaseRuntimeLimitOptions Limits { get; set; } = new();
    public HPDBaseRuntimeEventOptions Events { get; set; } = new();
    /// <summary>Gets or sets bounded mutation, batch, and provider execution limits.</summary>
    public HPDBaseRuntimeMutationOptions Mutations { get; set; } = new();
    public HPDBaseRuntimeRedactionOptions Redaction { get; set; } = new();
    public HPDBaseRuntimeObservabilityOptions Observability { get; set; } = new();

    internal bool AllowPolicyAbstainAsAllowForDevelopment { get; set; }

    public static HPDBaseRuntimeOptions CreateDefault() => new()
    {
        Runtime = new RuntimeDescriptor
        {
            Id = "hpd.base.runtime",
            Name = "HPD.BASE Runtime",
            Mode = RuntimeMode.Production
        },
        Compatibility = new CompatibilityDescriptor
        {
            BaseContractVersion = "1.0",
            MinClientContractVersion = "1.0",
            MaxClientContractVersion = "1.0"
        }
    };
}

/// <summary>Configures validated Runtime mutation and transaction limits.</summary>
public sealed class HPDBaseRuntimeMutationOptions
{
    /// <summary>Gets or sets the maximum number of operations in one batch.</summary>
    public int MaxOperations { get; set; } = 100;

    /// <summary>Gets or sets the maximum canonical source-generated JSON payload size.</summary>
    public long MaxCanonicalPayloadBytes { get; set; } = 1_048_576;

    /// <summary>Gets or sets the maximum control-free batch item identifier length.</summary>
    public int MaxItemIdLength { get; set; } = 128;

    /// <summary>Gets or sets the maximum provider transaction processing duration.</summary>
    public TimeSpan MaxTransactionDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the maximum provider boundary acquisition duration.</summary>
    public TimeSpan StoreAcquisitionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the internal maximum commit-classification duration.</summary>
    public TimeSpan CommitCompletionTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
