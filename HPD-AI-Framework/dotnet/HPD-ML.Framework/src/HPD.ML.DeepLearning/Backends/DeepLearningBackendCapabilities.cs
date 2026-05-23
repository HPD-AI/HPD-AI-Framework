namespace HPD.ML.DeepLearning.Backends;

public sealed record DeepLearningBackendCapabilities
{
    public static DeepLearningBackendCapabilities ManagedCpu { get; } = new()
    {
        Name = "managed-cpu",
        SupportsTraining = true,
        SupportsAutodiff = false,
        SupportsCpu = true,
        SupportsGpu = false,
        SupportsFloat32 = true,
        SupportedActivations = new HashSet<ActivationKind>
        {
            ActivationKind.Identity,
            ActivationKind.ReLU
        }
    };

    public required string Name { get; init; }
    public bool SupportsTraining { get; init; }
    public bool SupportsAutodiff { get; init; }
    public bool SupportsCpu { get; init; }
    public bool SupportsGpu { get; init; }
    public bool SupportsFloat32 { get; init; }
    public IReadOnlySet<ActivationKind> SupportedActivations { get; init; } =
        new HashSet<ActivationKind> { ActivationKind.Identity };

    public bool SupportsActivation(ActivationKind activation)
        => SupportedActivations.Contains(activation);
}
