using Hast.Layer;
using Hast.Transformer.SimpleMemory;
using Hast.Transformer.Vhdl.Configuration;
using Hast.Xilinx.Drivers;

namespace Helium.Hastlayer;

public sealed record HastlayerSynthesisGateOptions
{
    public string DeviceName { get; init; } = NexysA7Driver.NexysA7;
    public string HardwareFrameworkPath { get; init; } = "HardwareFramework";
    public string? HardwareGenerationPath { get; init; }
    public bool ComposeImplementation { get; init; }
}

public sealed record HastlayerSynthesisGateResult(
    string DeviceName,
    string KernelName,
    string Language,
    string TransformationId,
    int EntryPointCount,
    string? BinaryPath,
    IReadOnlyList<string> WarningMessages);

public sealed record HastlayerCompositionReadiness(
    bool IsReady,
    string DeviceName,
    string HardwareFrameworkPath,
    IReadOnlyList<string> Messages);

public enum HastlayerKernelGate
{
    Hello,
    FixedPointMatVec,
    RnsPolyMul,
    RnsNttPolyMul,
    GoldilocksPolyMul,
    GoldilocksNttPolyMul,
}

public static class HastlayerSynthesisGate
{
    public static HastlayerCompositionReadiness CheckCompositionReadiness(HastlayerSynthesisGateOptions? options = null)
    {
        options ??= new HastlayerSynthesisGateOptions();
        var messages = new List<string>();
        var frameworkPath = Path.GetFullPath(options.HardwareFrameworkPath);

        try
        {
            using var hastlayer = global::Hast.Layer.Hastlayer.Create();
            var supportedDeviceNames = hastlayer.GetSupportedDevices()
                .Select(device => device.Name)
                .ToArray();
            if (!supportedDeviceNames.Contains(options.DeviceName, StringComparer.Ordinal))
            {
                messages.Add(
                    $"Device '{options.DeviceName}' is not registered. Supported devices: {string.Join(", ", supportedDeviceNames)}.");
            }
        }
        catch (Exception ex)
        {
            messages.Add($"Unable to enumerate Hastlayer devices: {ex.Message}");
        }

        if (!Directory.Exists(frameworkPath))
        {
            messages.Add($"Hardware framework path does not exist: {frameworkPath}");
        }
        else if (string.Equals(options.DeviceName, NexysA7Driver.NexysA7, StringComparison.Ordinal))
        {
            var constraintsPath = Path.Combine(frameworkPath, "Nexys4DDR_Master.xdc");
            if (!File.Exists(constraintsPath))
                messages.Add($"Nexys A7 constraints file is missing: {constraintsPath}");
        }
        else
        {
            var rtlSourcePath = Path.Combine(frameworkPath, "rtl", "src");
            var platformsPath = Path.Combine(frameworkPath, "platforms");
            if (!Directory.Exists(rtlSourcePath))
                messages.Add($"Vitis RTL source directory is missing: {rtlSourcePath}");
            if (!Directory.Exists(platformsPath))
                messages.Add($"Vitis platforms directory is missing: {platformsPath}");
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("XILINX_VITIS")))
                messages.Add("XILINX_VITIS is not set; vendor implementation composition may not be able to invoke Vitis.");
        }

        return new HastlayerCompositionReadiness(
            messages.Count == 0,
            options.DeviceName,
            frameworkPath,
            messages);
    }

    public static async Task<HastlayerSynthesisGateResult> GenerateHelloKernelDescriptionAsync(
        HastlayerSynthesisGateOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await GenerateKernelDescriptionAsync(HastlayerKernelGate.Hello, options, cancellationToken).ConfigureAwait(false);

    public static async Task<HastlayerSynthesisGateResult> GenerateKernelDescriptionAsync(
        HastlayerKernelGate kernel,
        HastlayerSynthesisGateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new HastlayerSynthesisGateOptions();
        if (!string.IsNullOrWhiteSpace(options.HardwareGenerationPath))
            Directory.CreateDirectory(options.HardwareGenerationPath);

        using var hastlayer = global::Hast.Layer.Hastlayer.Create();
        var configuration = new HardwareGenerationConfiguration(options.DeviceName, options.HardwareFrameworkPath)
        {
            Label = "Helium.Hastlayer.HelloKernel",
            EnableCaching = false,
            EnableHardwareTransformation = true,
            EnableHardwareImplementationComposition = options.ComposeImplementation,
            HardwareGenerationPath = options.HardwareGenerationPath,
        };

        AddEntryPoint(configuration, kernel);
        configuration.VhdlTransformerConfiguration().VhdlGenerationConfiguration = VhdlGenerationConfiguration.Release;

        var representationTask = hastlayer.GenerateHardwareAsync([KernelType(kernel).Assembly], configuration);
        var representation = await representationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        var description = representation.HardwareDescription;

        return new HastlayerSynthesisGateResult(
            representation.DeviceManifest.Name,
            KernelType(kernel).Name,
            description.Language,
            description.TransformationId,
            description.HardwareEntryPointNamesToMemberIdMappings.Count,
            representation.HardwareImplementation.BinaryPath,
            description.Warnings.Select(warning => $"{warning.Code}: {warning.Message}").ToArray());
    }

    private static void AddEntryPoint(IHardwareGenerationConfiguration configuration, HastlayerKernelGate kernel)
    {
        switch (kernel)
        {
            case HastlayerKernelGate.Hello:
                configuration.AddHardwareEntryPointMethod<HelloKernel>(k => k.Execute(default(SimpleMemory)!));
                break;
            case HastlayerKernelGate.FixedPointMatVec:
                configuration.AddHardwareEntryPointMethod<FixedPointMatVecKernel>(k => k.Execute(default(SimpleMemory)!));
                break;
            case HastlayerKernelGate.RnsPolyMul:
                configuration.AddHardwareEntryPointMethod<RnsPolyMulKernel>(k => k.Execute(default(SimpleMemory)!));
                break;
            case HastlayerKernelGate.RnsNttPolyMul:
                configuration.AddHardwareEntryPointMethod<RnsNttPolyMulKernel>(k => k.Execute(default(SimpleMemory)!));
                break;
            case HastlayerKernelGate.GoldilocksPolyMul:
                configuration.AddHardwareEntryPointMethod<GoldilocksPolyMulKernel>(k => k.Execute(default(SimpleMemory)!));
                break;
            case HastlayerKernelGate.GoldilocksNttPolyMul:
                configuration.AddHardwareEntryPointMethod<GoldilocksNttPolyMulKernel>(k => k.Execute(default(SimpleMemory)!));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kernel), kernel, "Unknown Hastlayer kernel gate.");
        }
    }

    private static Type KernelType(HastlayerKernelGate kernel) =>
        kernel switch
        {
            HastlayerKernelGate.Hello => typeof(HelloKernel),
            HastlayerKernelGate.FixedPointMatVec => typeof(FixedPointMatVecKernel),
            HastlayerKernelGate.RnsPolyMul => typeof(RnsPolyMulKernel),
            HastlayerKernelGate.RnsNttPolyMul => typeof(RnsNttPolyMulKernel),
            HastlayerKernelGate.GoldilocksPolyMul => typeof(GoldilocksPolyMulKernel),
            HastlayerKernelGate.GoldilocksNttPolyMul => typeof(GoldilocksNttPolyMulKernel),
            _ => throw new ArgumentOutOfRangeException(nameof(kernel), kernel, "Unknown Hastlayer kernel gate."),
        };
}
