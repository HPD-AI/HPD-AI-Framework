namespace HPD.Environment.AppleVirtualization.Tests;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FluentAssertions;
using HPD.Environment.AppleVirtualization.Activation;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.Contracts;
using Xunit;

public sealed class AppleVirtualizationRealModePreconditionTests
{
    [Fact]
    public void Missing_helper_path_is_structured_precondition_failure()
    {
        AppleVirtualizationRealModePreconditionResult result = AppleVirtualizationRealModePreconditions.Evaluate(
            RealOptions(helperPath: ""));

        result.Passed.Should().BeFalse();
        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.HelperPathMissing" &&
            diagnostic.TargetPath == "HelperPath");
        result.Facts.Should().Contain(fact =>
            fact.Name == AppleVirtualizationRealModePreconditions.HelperExecutableFact &&
            fact.State == AppleVirtualizationPreflightFactState.RequiresRemediation);
    }

    [Fact]
    public void Fake_helper_cannot_satisfy_real_mode_preconditions()
    {
        using TempRealModeInputs inputs = TempRealModeInputs.Create();
        AppleVirtualizationRealModePreconditionResult result = AppleVirtualizationRealModePreconditions.Evaluate(
            RealOptions(inputs.HelperPath, ["--fake"], inputs));

        result.Passed.Should().BeFalse();
        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.RealModeRequiresNonFakeHelper" &&
            diagnostic.TargetPath == "HelperArguments");
    }

    [Fact]
    public void Missing_kernel_initrd_and_disk_inputs_are_structured_precondition_failures()
    {
        using TempRealModeInputs inputs = TempRealModeInputs.Create(createKernel: false, createInitrd: false, createDisk: false);
        AppleVirtualizationRealModePreconditionResult result = AppleVirtualizationRealModePreconditions.Evaluate(
            RealOptions(inputs.HelperPath, [], inputs));

        result.Passed.Should().BeFalse();
        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.RealModeKernelMissing" &&
            diagnostic.TargetPath == "GuestImage.KernelPath");
        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.RealModeInitrdMissing" &&
            diagnostic.TargetPath == "GuestImage.InitrdPath");
        foreach (AppleVirtualizationDiskRole role in Enum.GetValues<AppleVirtualizationDiskRole>())
        {
            result.Diagnostics.Should().Contain(diagnostic =>
                diagnostic.Code.Value == "AppleVirtualization.RealModeDiskImageMissing" &&
                diagnostic.TargetPath == $"GuestImage.DiskAttachments[{role}].DiskImagePath");
        }
    }

    [Fact]
    public void Entitlement_and_signing_unknown_are_not_treated_as_passed()
    {
        using TempRealModeInputs inputs = TempRealModeInputs.Create();
        AppleVirtualizationRealModePreconditionResult result = AppleVirtualizationRealModePreconditions.Evaluate(
            RealOptions(inputs.HelperPath, [], inputs));

        result.Passed.Should().BeTrue();
        result.Facts.Should().Contain(fact =>
            fact.Name == AppleVirtualizationRealModePreconditions.EntitlementFact &&
            fact.State == AppleVirtualizationPreflightFactState.Unknown &&
            fact.Reason == "RequiresRuntimeVerification");
        result.Facts.Should().Contain(fact =>
            fact.Name == AppleVirtualizationRealModePreconditions.SigningFact &&
            fact.State == AppleVirtualizationPreflightFactState.Unknown &&
            fact.Reason == "RequiresRuntimeVerification");
    }

    [Fact]
    public void Serial_log_parent_directory_is_created_deterministically()
    {
        using TempRealModeInputs inputs = TempRealModeInputs.Create(createSerialDirectory: false);
        Directory.Exists(Path.GetDirectoryName(inputs.SerialLogPath)!).Should().BeFalse();

        AppleVirtualizationRealModePreconditionResult result = AppleVirtualizationRealModePreconditions.Evaluate(
            RealOptions(inputs.HelperPath, [], inputs));

        result.Passed.Should().BeTrue();
        Directory.Exists(Path.GetDirectoryName(inputs.SerialLogPath)!).Should().BeTrue();
        result.Facts.Should().Contain(fact =>
            fact.Name == AppleVirtualizationRealModePreconditions.SerialLogFact &&
            fact.State == AppleVirtualizationPreflightFactState.Supported);
    }

    [Fact]
    public void Optional_virtiofs_host_path_must_exist_when_configured()
    {
        using TempRealModeInputs inputs = TempRealModeInputs.Create();
        AppleVirtualizationRealModePreconditionResult result = AppleVirtualizationRealModePreconditions.Evaluate(
            RealOptions(inputs.HelperPath, [], inputs) with
            {
                GuestImage = inputs.GuestImage with
                {
                    SharedDirectories =
                    [
                        new AppleVirtualizationGuestSharedDirectoryOptions
                        {
                            Tag = "workspace",
                            HostPath = Path.Combine(inputs.Root, "missing-workspace"),
                        },
                    ],
                },
            });

        result.Passed.Should().BeFalse();
        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.RealModeVirtiofsHostPathMissing" &&
            diagnostic.TargetPath == "GuestImage.SharedDirectories[0].HostPath");
    }

    [Fact]
    public void Preflight_fact_model_can_represent_host_unsupported_without_throwing()
    {
        var fact = new AppleVirtualizationPreflightFact
        {
            Name = "vzvirtualmachine-supported",
            State = AppleVirtualizationPreflightFactState.Unsupported,
            Reason = "VZVirtualMachineIsSupportedFalse",
            Message = "VZVirtualMachine.isSupported is false on this host.",
            Severity = DiagnosticSeverity.Error,
        };

        string json = JsonSerializer.Serialize(fact, AppleVirtualizationJsonContext.Default.AppleVirtualizationPreflightFact);
        AppleVirtualizationPreflightFact roundTrip = JsonSerializer.Deserialize(
            json,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationPreflightFact)!;

        roundTrip.State.Should().Be(AppleVirtualizationPreflightFactState.Unsupported);
        roundTrip.Reason.Should().Be("VZVirtualMachineIsSupportedFalse");
    }

    private static AppleVirtualizationProviderOptions RealOptions(
        string helperPath,
        IReadOnlyList<string>? arguments = null,
        TempRealModeInputs? inputs = null) =>
        new()
        {
            HelperPath = helperPath,
            HelperArguments = arguments ?? [],
            HelperTransportMode = AppleVirtualizationHelperTransportMode.StdIo,
            GuestImage = inputs?.GuestImage ?? new AppleVirtualizationGuestImageOptions(),
            FeatureGates = new AppleVirtualizationProviderFeatureGates
            {
                EnableRealHelperActivation = true,
                EnableRealVmBoot = true,
            },
        };

    private sealed class TempRealModeInputs : IDisposable
    {
        private TempRealModeInputs(
            string root,
            string helperPath,
            string kernelPath,
            string initrdPath,
            string diskPath,
            string serialLogPath)
        {
            Root = root;
            HelperPath = helperPath;
            SerialLogPath = serialLogPath;
            GuestImage = new AppleVirtualizationGuestImageOptions
            {
                KernelPath = kernelPath,
                InitrdPath = initrdPath,
                KernelCommandLine = "console=hvc0 root=/dev/vda1 rw",
                DiskAttachments = AppleVirtualizationTestDiskSet.Create(diskPath),
                SerialLogPath = serialLogPath,
                Architecture = HostNativeArchitectureExpectation(),
                ExpectedGuestAgentVersion = "0.1.0",
                ExpectVirtiofsSupport = true,
            };
        }

        public string Root { get; }
        public string HelperPath { get; }
        public string SerialLogPath { get; }
        public AppleVirtualizationGuestImageOptions GuestImage { get; }

        public static TempRealModeInputs Create(
            bool createKernel = true,
            bool createInitrd = true,
            bool createDisk = true,
            bool createSerialDirectory = true)
        {
            string root = Path.Combine(Path.GetTempPath(), "hpd-applevz-real-mode-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string helper = Path.Combine(root, "hpd-vz-test");
            File.WriteAllText(helper, "#!/bin/sh\nexit 0\n");
            MakeExecutable(helper);

            string kernel = Path.Combine(root, "vmlinuz");
            string initrd = Path.Combine(root, "initrd.img");
            string disk = Path.Combine(root, "root.raw");
            string serialDirectory = Path.Combine(root, "logs");
            string serial = Path.Combine(serialDirectory, "runtime-host.serial.log");
            if (createKernel)
            {
                File.WriteAllBytes(kernel, [0x01]);
            }

            if (createInitrd)
            {
                File.WriteAllBytes(initrd, [0x02]);
            }

            if (createDisk)
            {
                File.WriteAllBytes(disk, new byte[512]);
                File.WriteAllBytes(disk + ".runtime", new byte[512]);
                File.WriteAllBytes(disk + ".apps", new byte[512]);
            }

            if (createSerialDirectory)
            {
                Directory.CreateDirectory(serialDirectory);
            }

            return new TempRealModeInputs(root, helper, kernel, initrd, disk, serial);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static void MakeExecutable(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch (PlatformNotSupportedException)
            {
                using Process chmod = Process.Start("chmod", "+x " + path)!;
                chmod.WaitForExit();
            }
        }

        private static AppleVirtualizationGuestArchitectureExpectation HostNativeArchitectureExpectation() =>
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? AppleVirtualizationGuestArchitectureExpectation.Arm64
                : AppleVirtualizationGuestArchitectureExpectation.X64;
    }
}
