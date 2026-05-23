using Helium.Hastlayer;

namespace Helium.Hastlayer.Tests;

public class HastlayerSynthesisGateTests
{
    [Fact]
    public void SynthesisGateOptions_DefaultToTransformationOnly()
    {
        var options = new HastlayerSynthesisGateOptions();

        Assert.Equal("Nexys A7", options.DeviceName);
        Assert.False(options.ComposeImplementation);
    }

    [Fact]
    public void CompositionReadiness_ReportsMissingHardwareFramework()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            "helium-hastlayer-missing-framework",
            Guid.NewGuid().ToString("N"));

        var readiness = HastlayerSynthesisGate.CheckCompositionReadiness(
            new HastlayerSynthesisGateOptions
            {
                HardwareFrameworkPath = missingPath,
                ComposeImplementation = true,
            });

        Assert.False(readiness.IsReady);
        Assert.Equal(Path.GetFullPath(missingPath), readiness.HardwareFrameworkPath);
        Assert.Contains(readiness.Messages, message => message.Contains("Hardware framework path does not exist", StringComparison.Ordinal));
    }

    public static TheoryData<HastlayerKernelGate> KernelGates() =>
    [
        HastlayerKernelGate.Hello,
        HastlayerKernelGate.FixedPointMatVec,
        HastlayerKernelGate.RnsPolyMul,
        HastlayerKernelGate.RnsNttPolyMul,
        HastlayerKernelGate.GoldilocksPolyMul,
        HastlayerKernelGate.GoldilocksNttPolyMul,
    ];

    [Theory]
    [MemberData(nameof(KernelGates))]
    public async Task Kernel_TransformationGate_GeneratesHardwareDescription_WhenEnabled(HastlayerKernelGate kernel)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("HELIUM_HASTLAYER_SYNTHESIS_GATE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var generationPath = Path.Combine(
            Path.GetTempPath(),
            "helium-hastlayer-gate",
            Guid.NewGuid().ToString("N"));

        var result = await HastlayerSynthesisGate.GenerateKernelDescriptionAsync(
            kernel,
            new HastlayerSynthesisGateOptions
            {
                HardwareGenerationPath = generationPath,
                HardwareFrameworkPath = "HardwareFramework",
                ComposeImplementation = false,
            },
            CancellationToken.None);

        Assert.Equal("Nexys A7", result.DeviceName);
        Assert.EndsWith("Kernel", result.KernelName, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(result.Language));
        Assert.False(string.IsNullOrWhiteSpace(result.TransformationId));
        Assert.True(result.EntryPointCount >= 1);
    }

    [Fact]
    public async Task HelloKernel_CompositionGate_GeneratesHardwareImplementation_WhenEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("HELIUM_HASTLAYER_COMPOSITION_GATE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var generationPath = Path.Combine(
            Path.GetTempPath(),
            "helium-hastlayer-composition-gate",
            Guid.NewGuid().ToString("N"));

        var options = new HastlayerSynthesisGateOptions
        {
            HardwareGenerationPath = generationPath,
            HardwareFrameworkPath = Environment.GetEnvironmentVariable("HELIUM_HASTLAYER_HARDWARE_FRAMEWORK")
                ?? "HardwareFramework",
            ComposeImplementation = true,
        };
        var readiness = HastlayerSynthesisGate.CheckCompositionReadiness(options);
        if (!readiness.IsReady)
            return;

        var result = await HastlayerSynthesisGate.GenerateKernelDescriptionAsync(
            HastlayerKernelGate.Hello,
            options,
            CancellationToken.None);

        Assert.Equal("HelloKernel", result.KernelName);
        Assert.False(string.IsNullOrWhiteSpace(result.BinaryPath));
    }
}
