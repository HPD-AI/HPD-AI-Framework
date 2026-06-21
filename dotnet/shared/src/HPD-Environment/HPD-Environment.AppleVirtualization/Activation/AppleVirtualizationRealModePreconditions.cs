namespace HPD.Environment.AppleVirtualization.Activation;

using System.Runtime.InteropServices;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.Contracts;

internal static class AppleVirtualizationRealModePreconditions
{
    internal const string HelperExecutableFact = "real-mode-helper-executable";
    internal const string HelperModeFact = "real-mode-helper-mode";
    internal const string HostArchitectureFact = "real-mode-host-architecture";
    internal const string BootInputFact = "real-mode-boot-inputs";
    internal const string DiskImageFact = "real-mode-disk-image";
    internal const string SerialLogFact = "real-mode-serial-log";
    internal const string VirtiofsHostPathFact = "real-mode-virtiofs-host-path";
    internal const string EntitlementFact = "real-mode-entitlement";
    internal const string SigningFact = "real-mode-signing";

    private static readonly ProviderId ProviderId = AppleVirtualizationProviderDescriptor.ProviderId;

    public static AppleVirtualizationRealModePreconditionResult Evaluate(AppleVirtualizationProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var facts = new List<AppleVirtualizationPreflightFact>(capacity: 12);
        var diagnostics = new List<Diagnostic>(capacity: 8);

        ClassifyHelperPath(options, facts, diagnostics);
        ClassifyHelperMode(options, facts, diagnostics);
        ClassifyHostArchitecture(options.GuestImage.Architecture, facts, diagnostics);
        ClassifyBootInputs(options.GuestImage, facts, diagnostics);
        ClassifyDiskImage(options.GuestImage.DiskImagePath, facts, diagnostics);
        ClassifySerialLog(options.GuestImage.SerialLogPath, facts, diagnostics);
        ClassifySharedDirectories(options.GuestImage.SharedDirectories, facts, diagnostics);
        AddUnknownRuntimeVerificationFact(
            facts,
            EntitlementFact,
            "RequiresRuntimeVerification",
            "Helper entitlement can only be proven by the running signed helper or Apple runtime validation before VM creation.");
        AddUnknownRuntimeVerificationFact(
            facts,
            SigningFact,
            "RequiresRuntimeVerification",
            "Helper code-signing state is not treated as passed by provider-side path checks.");

        return new AppleVirtualizationRealModePreconditionResult(
            diagnostics.Count == 0,
            facts,
            diagnostics);
    }

    private static void ClassifyHelperPath(
        AppleVirtualizationProviderOptions options,
        List<AppleVirtualizationPreflightFact> facts,
        List<Diagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(options.HelperPath))
        {
            AddFailure(facts, diagnostics, HelperExecutableFact, "AppleVirtualization.HelperPathMissing", "HelperPath", "The Apple Virtualization helper path is empty.");
            return;
        }

        string? resolved = ResolveExecutablePath(options.HelperPath);
        if (resolved is null)
        {
            AddFailure(facts, diagnostics, HelperExecutableFact, "AppleVirtualization.HelperExecutableNotFound", "HelperPath", $"The Apple Virtualization helper executable '{options.HelperPath}' was not found.");
            return;
        }

        if (!IsExecutable(resolved))
        {
            AddFailure(facts, diagnostics, HelperExecutableFact, "AppleVirtualization.HelperNotExecutable", "HelperPath", $"The Apple Virtualization helper at '{resolved}' is not executable.");
            return;
        }

        AddSupportedFact(facts, HelperExecutableFact, "HelperExecutableFound", $"The Apple Virtualization helper exists and is executable at '{resolved}'.", resolved);
    }

    private static void ClassifyHelperMode(
        AppleVirtualizationProviderOptions options,
        List<AppleVirtualizationPreflightFact> facts,
        List<Diagnostic> diagnostics)
    {
        bool fakeTransport = options.HelperTransportMode == AppleVirtualizationHelperTransportMode.InMemoryFake;
        bool fakeArgument = options.HelperArguments.Any(argument => string.Equals(argument, "--fake", StringComparison.Ordinal));
        if (fakeTransport || fakeArgument || options.FeatureGates.EnableInMemoryFakeHelper)
        {
            AddFailure(facts, diagnostics, HelperModeFact, "AppleVirtualization.RealModeRequiresNonFakeHelper", "HelperArguments", "Fake helper mode cannot satisfy real VM boot preconditions.");
            return;
        }

        AddSupportedFact(facts, HelperModeFact, "RealHelperModeExplicit", "Helper options request a non-fake stdio helper for explicit real VM boot.");
    }

    private static void ClassifyHostArchitecture(
        AppleVirtualizationGuestArchitectureExpectation expectation,
        List<AppleVirtualizationPreflightFact> facts,
        List<Diagnostic> diagnostics)
    {
        Architecture actual = RuntimeInformation.ProcessArchitecture;
        bool matched = expectation switch
        {
            AppleVirtualizationGuestArchitectureExpectation.HostNative => actual is Architecture.Arm64 or Architecture.X64,
            AppleVirtualizationGuestArchitectureExpectation.Arm64 => actual == Architecture.Arm64,
            AppleVirtualizationGuestArchitectureExpectation.X64 => actual == Architecture.X64,
            _ => false,
        };

        if (!matched)
        {
            AddFailure(
                facts,
                diagnostics,
                HostArchitectureFact,
                "AppleVirtualization.RealModeArchitectureMismatch",
                "GuestImage.Architecture",
                $"Guest architecture expectation '{expectation}' does not match host process architecture '{actual}'.");
            return;
        }

        AddSupportedFact(facts, HostArchitectureFact, "ArchitectureExpectationMatched", $"Guest architecture expectation '{expectation}' matches host process architecture '{actual}'.", actual.ToString());
    }

    private static void ClassifyBootInputs(
        AppleVirtualizationGuestImageOptions guest,
        List<AppleVirtualizationPreflightFact> facts,
        List<Diagnostic> diagnostics)
    {
        if (guest.BootLoader == AppleVirtualizationGuestBootLoaderKind.Efi)
        {
            ClassifyReadableFile(guest.EfiVariableStorePath, "GuestImage.EfiVariableStorePath", "AppleVirtualization.RealModeEfiVariableStoreMissing", facts, diagnostics, BootInputFact);
            return;
        }

        bool kernelOk = ClassifyReadableFile(guest.KernelPath, "GuestImage.KernelPath", "AppleVirtualization.RealModeKernelMissing", facts, diagnostics, BootInputFact, addSuccess: false);
        bool initrdOk = ClassifyReadableFile(guest.InitrdPath, "GuestImage.InitrdPath", "AppleVirtualization.RealModeInitrdMissing", facts, diagnostics, BootInputFact, addSuccess: false);
        if (kernelOk && initrdOk)
        {
            AddSupportedFact(facts, BootInputFact, "BootInputsReadable", "Linux kernel and initrd exist and are readable.");
        }
    }

    private static void ClassifyDiskImage(
        string? path,
        List<AppleVirtualizationPreflightFact> facts,
        List<Diagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            AddFailure(facts, diagnostics, DiskImageFact, "AppleVirtualization.RealModeDiskImageMissing", "GuestImage.DiskImagePath", "Writable disk image path is required for real VM boot.");
            return;
        }

        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            AddSupportedFact(facts, DiskImageFact, "DiskImageReadableWritable", "Disk image exists and can be opened read/write.", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddFailure(facts, diagnostics, DiskImageFact, "AppleVirtualization.RealModeDiskImageNotWritable", "GuestImage.DiskImagePath", $"Disk image must be readable and writable for first-slice boot. {ex.Message}");
        }
    }

    private static void ClassifySerialLog(
        string? path,
        List<AppleVirtualizationPreflightFact> facts,
        List<Diagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            AddFailure(facts, diagnostics, SerialLogFact, "AppleVirtualization.RealModeSerialLogMissing", "GuestImage.SerialLogPath", "Serial log path is required for real VM boot diagnostics.");
            return;
        }

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            AddFailure(facts, diagnostics, SerialLogFact, "AppleVirtualization.RealModeSerialLogDirectoryMissing", "GuestImage.SerialLogPath", "Serial log path must include a parent directory.");
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            string probe = Path.Combine(directory, ".hpd-applevz-serial-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            AddSupportedFact(facts, SerialLogFact, "SerialLogDirectoryWritable", "Serial log parent directory exists or was created and is writable.", directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddFailure(facts, diagnostics, SerialLogFact, "AppleVirtualization.RealModeSerialLogDirectoryUnavailable", "GuestImage.SerialLogPath", $"Serial log parent directory is not usable. {ex.Message}");
        }
    }

    private static void ClassifySharedDirectories(
        IReadOnlyList<AppleVirtualizationGuestSharedDirectoryOptions> shares,
        List<AppleVirtualizationPreflightFact> facts,
        List<Diagnostic> diagnostics)
    {
        if (shares.Count == 0)
        {
            AddSupportedFact(facts, VirtiofsHostPathFact, "NoVirtiofsHostPathConfigured", "No optional virtiofs host path was configured for real-mode preflight.");
            return;
        }

        bool allOk = true;
        for (int i = 0; i < shares.Count; i++)
        {
            AppleVirtualizationGuestSharedDirectoryOptions share = shares[i];
            if (string.IsNullOrWhiteSpace(share.HostPath) || !Directory.Exists(share.HostPath))
            {
                allOk = false;
                AddFailure(facts, diagnostics, VirtiofsHostPathFact, "AppleVirtualization.RealModeVirtiofsHostPathMissing", $"GuestImage.SharedDirectories[{i}].HostPath", "Configured virtiofs host path must exist before real VM boot.");
            }
        }

        if (allOk)
        {
            AddSupportedFact(facts, VirtiofsHostPathFact, "VirtiofsHostPathsExist", "Configured virtiofs host paths exist. Guest mount verification remains downstream.");
        }
    }

    private static bool ClassifyReadableFile(
        string? path,
        string targetPath,
        string code,
        List<AppleVirtualizationPreflightFact> facts,
        List<Diagnostic> diagnostics,
        string factName,
        bool addSuccess = true)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            AddFailure(facts, diagnostics, factName, code, targetPath, $"{targetPath} is required and must point to an existing readable file.");
            return false;
        }

        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (addSuccess)
            {
                AddSupportedFact(facts, factName, "ReadableFileExists", $"{targetPath} exists and is readable.", path);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddFailure(facts, diagnostics, factName, code, targetPath, $"{targetPath} must be readable. {ex.Message}");
            return false;
        }
    }

    private static string? ResolveExecutablePath(string helperPath)
    {
        if (Path.IsPathFullyQualified(helperPath) ||
            helperPath.Contains(Path.DirectorySeparatorChar) ||
            helperPath.Contains(Path.AltDirectorySeparatorChar))
        {
            return File.Exists(helperPath) ? helperPath : null;
        }

        string? path = System.Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, helperPath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsExecutable(string path)
    {
        if (Directory.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (PlatformNotSupportedException)
        {
            return true;
        }
    }

    private static void AddSupportedFact(
        List<AppleVirtualizationPreflightFact> facts,
        string name,
        string reason,
        string message,
        string? observedValue = null) =>
        facts.Add(new AppleVirtualizationPreflightFact
        {
            Name = name,
            State = AppleVirtualizationPreflightFactState.Supported,
            Reason = reason,
            Message = message,
            ObservedValue = observedValue,
            Severity = DiagnosticSeverity.Info,
        });

    private static void AddUnknownRuntimeVerificationFact(
        List<AppleVirtualizationPreflightFact> facts,
        string name,
        string reason,
        string message) =>
        facts.Add(new AppleVirtualizationPreflightFact
        {
            Name = name,
            State = AppleVirtualizationPreflightFactState.Unknown,
            Reason = reason,
            Message = message,
            Severity = DiagnosticSeverity.Warning,
        });

    private static void AddFailure(
        List<AppleVirtualizationPreflightFact> facts,
        List<Diagnostic> diagnostics,
        string factName,
        string code,
        string targetPath,
        string message)
    {
        facts.Add(new AppleVirtualizationPreflightFact
        {
            Name = factName,
            State = AppleVirtualizationPreflightFactState.RequiresRemediation,
            Reason = code,
            Message = message,
            Severity = DiagnosticSeverity.Error,
        });
        diagnostics.Add(new Diagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Code = new DiagnosticCode(code),
            Message = message,
            ProviderId = ProviderId,
            TargetPath = targetPath,
        });
    }
}

internal sealed record AppleVirtualizationRealModePreconditionResult(
    bool Passed,
    IReadOnlyList<AppleVirtualizationPreflightFact> Facts,
    IReadOnlyList<Diagnostic> Diagnostics);
