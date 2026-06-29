using HPD.Base.Descriptors;

namespace HPD.Base.Runtime.Configuration;

public sealed class HPDBaseRuntimeOptions
{
    public required RuntimeDescriptor Runtime { get; set; }
    public required CompatibilityDescriptor Compatibility { get; set; }
    public string ManifestVersion { get; set; } = "1.0";
    public VisibilityLevel DefaultManifestVisibility { get; set; } = VisibilityLevel.Public;
    public bool FailFastOnDescriptorValidation { get; set; } = true;
    public bool AllowPolicyAbstainAsAllow { get; set; }
    public HPDBaseRuntimeLimitOptions Limits { get; set; } = new();
    public HPDBaseRuntimeEventOptions Events { get; set; } = new();
    public HPDBaseRuntimeRedactionOptions Redaction { get; set; } = new();

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
