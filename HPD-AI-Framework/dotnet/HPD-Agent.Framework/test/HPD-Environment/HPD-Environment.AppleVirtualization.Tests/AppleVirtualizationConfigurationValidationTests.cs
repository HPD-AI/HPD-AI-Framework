namespace HPD.Environment.AppleVirtualization.Tests;

using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.Contracts;
using Xunit;

public sealed class AppleVirtualizationConfigurationValidationTests
{
    [Fact]
    public void Validation_request_and_response_round_trip_through_source_generated_json()
    {
        AppleVirtualizationHelperEnvelope envelope = ValidationEnvelope("validation-round-trip", new AppleVirtualizationGuestImageOptions
        {
            BootLoader = AppleVirtualizationGuestBootLoaderKind.LinuxBootLoader,
            KernelPath = "/tmp/hpd-vz/vmlinuz",
            InitrdPath = "/tmp/hpd-vz/initrd.img",
            KernelCommandLine = "console=hvc0",
            DiskImagePath = "/tmp/hpd-vz/root.raw",
            SerialLogPath = "/tmp/hpd-vz/serial.log",
            ExpectedGuestAgentVersion = "0.1.0",
        }).ToResponse(sequenceNumber: 2) with
        {
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            VmConfigurationValidationResponse = new AppleVirtualizationVmConfigurationValidationResponse
            {
                Phase = AppleVirtualizationVmConfigurationValidationPhase.Completed,
                State = AppleVirtualizationVmConfigurationValidationState.Passed,
                Passed = true,
                HostRunning = false,
                HpdReady = false,
                PreflightFacts =
                [
                    new AppleVirtualizationPreflightFact
                    {
                        Name = "vm-configuration-validation",
                        State = AppleVirtualizationPreflightFactState.Supported,
                        Reason = "FakeVZConfigurationValidatePassed",
                        Message = "Validated without starting a VM.",
                        Severity = DiagnosticSeverity.Info,
                    },
                ],
            },
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
        AppleVirtualizationHelperEnvelope roundTrip = JsonSerializer.Deserialize(
            json,
            AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)!;

        roundTrip.Operation.Should().Be(AppleVirtualizationHelperOperation.VmConfigurationValidate);
        roundTrip.VmConfigurationValidationRequest.Should().NotBeNull();
        roundTrip.VmConfigurationValidationRequest!.GuestImage.KernelPath.Should().Be("/tmp/hpd-vz/vmlinuz");
        roundTrip.VmConfigurationValidationRequest.SharedDirectories.Should().ContainSingle(share => share.Tag == "hpd.share");
        roundTrip.VmConfigurationValidationResponse.Should().NotBeNull();
        roundTrip.VmConfigurationValidationResponse!.Passed.Should().BeTrue();
        roundTrip.VmConfigurationValidationResponse.HostRunning.Should().BeFalse();
        roundTrip.VmConfigurationValidationResponse.HpdReady.Should().BeFalse();
    }

    [Fact]
    public async Task Swift_validation_rejects_missing_boot_inputs_structurally()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();
        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(ValidationEnvelope(
            "validation-missing-boot",
            new AppleVirtualizationGuestImageOptions
            {
                DiskImagePath = "/tmp/hpd-vz/root.raw",
                SerialLogPath = "/tmp/hpd-vz/serial.log",
            }));

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        response.VmConfigurationValidationResponse.Should().NotBeNull();
        response.VmConfigurationValidationResponse!.State.Should().Be(AppleVirtualizationVmConfigurationValidationState.Failed);
        response.VmConfigurationValidationResponse.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.VmConfigurationBootInputMissing" &&
            diagnostic.TargetPath == "GuestImage.KernelPath");
        response.VmConfigurationValidationResponse.HostRunning.Should().BeFalse();
        response.VmConfigurationValidationResponse.HpdReady.Should().BeFalse();
    }

    [Fact]
    public async Task Swift_validation_rejects_missing_disk_image_structurally()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();
        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(ValidationEnvelope(
            "validation-missing-disk",
            new AppleVirtualizationGuestImageOptions
            {
                KernelPath = "/tmp/hpd-vz/vmlinuz",
                SerialLogPath = "/tmp/hpd-vz/serial.log",
            }));

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        response.VmConfigurationValidationResponse!.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.VmConfigurationDiskImageMissing" &&
            diagnostic.TargetPath == "GuestImage.DiskImagePath");
    }

    [Fact]
    public async Task Swift_validation_rejects_invalid_virtiofs_tag_and_path_structurally()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();
        AppleVirtualizationHelperEnvelope request = ValidationEnvelope(
            "validation-bad-virtiofs",
            CompleteGuestImage()) with
        {
            VmConfigurationValidationRequest = ValidationRequest(CompleteGuestImage()) with
            {
                SharedDirectories =
                [
                    new AppleVirtualizationVmConfigurationSharedDirectory
                    {
                        Tag = "bad tag!",
                        HostPath = "",
                        ReadOnly = true,
                    },
                ],
            },
        };

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(request);

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Error);
        response.VmConfigurationValidationResponse!.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.VmConfigurationVirtiofsTagInvalid");
        response.VmConfigurationValidationResponse.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code.Value == "AppleVirtualization.VmConfigurationVirtiofsPathInvalid");
    }

    [Fact]
    public async Task Valid_first_slice_request_reaches_validation_success_without_running_or_ready()
    {
        await using SwiftHelperProcess helper = await SwiftHelperProcess.StartAsync();

        AppleVirtualizationHelperEnvelope response = await helper.SendAsync(ValidationEnvelope(
            "validation-valid-looking",
            CompleteGuestImage()));

        response.ResponseStatus.Should().Be(AppleVirtualizationHelperResponseStatus.Ok);
        response.PayloadSchema.Should().Be(AppleVirtualizationHelperProtocol.VmConfigurationValidationResponseSchema);
        response.VmConfigurationValidationResponse.Should().NotBeNull();
        response.VmConfigurationValidationResponse!.Passed.Should().BeTrue();
        response.VmConfigurationValidationResponse.State.Should().Be(AppleVirtualizationVmConfigurationValidationState.Passed);
        response.VmConfigurationValidationResponse.HostRunning.Should().BeFalse();
        response.VmConfigurationValidationResponse.HpdReady.Should().BeFalse();
        response.VmConfigurationValidationResponse.PreflightFacts.Should().Contain(fact =>
            fact.Name == "vm-configuration-validation");
    }

    private static AppleVirtualizationHelperEnvelope ValidationEnvelope(
        string requestId,
        AppleVirtualizationGuestImageOptions guestImage) =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.VmConfigurationValidate,
            requestId,
            sequenceNumber: 32,
            AppleVirtualizationHelperProtocol.VmConfigurationValidationRequestSchema) with
        {
            VmConfigurationValidationRequest = ValidationRequest(guestImage),
        };

    private static AppleVirtualizationVmConfigurationValidationRequest ValidationRequest(
        AppleVirtualizationGuestImageOptions guestImage) =>
        new()
        {
            HostId = "host-validation",
            CpuCount = 2,
            MemorySizeBytes = 512L * 1024 * 1024,
            GuestImage = guestImage,
            IncludeSerialConsole = true,
            IncludeVirtioSocketPlaceholder = false,
            SharedDirectories =
            [
                new AppleVirtualizationVmConfigurationSharedDirectory
                {
                    Tag = "hpd.share",
                    HostPath = "/tmp",
                    ReadOnly = true,
                },
            ],
        };

    private static AppleVirtualizationGuestImageOptions CompleteGuestImage() => new()
    {
        BootLoader = AppleVirtualizationGuestBootLoaderKind.LinuxBootLoader,
        KernelPath = "/tmp/hpd-vz/vmlinuz",
        InitrdPath = "/tmp/hpd-vz/initrd.img",
        KernelCommandLine = "console=hvc0 hpd.validation=1",
        DiskImagePath = "/tmp/hpd-vz/root.raw",
        SerialLogPath = "/tmp/hpd-vz/serial.log",
        ExpectedGuestAgentVersion = "0.1.0",
        ExpectVirtiofsSupport = true,
    };

    private sealed class SwiftHelperProcess : IAsyncDisposable
    {
        private readonly Process _process;

        private SwiftHelperProcess(Process process)
        {
            _process = process;
        }

        public static async Task<SwiftHelperProcess> StartAsync()
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(ResolveHelperPath(), "--fake")
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };

            process.Start().Should().BeTrue();
            await Task.Yield();
            return new SwiftHelperProcess(process);
        }

        public async Task<AppleVirtualizationHelperEnvelope> SendAsync(AppleVirtualizationHelperEnvelope envelope)
        {
            string json = JsonSerializer.Serialize(
                envelope,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _process.StandardInput.WriteLineAsync(json).WaitAsync(cancellation.Token).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellation.Token).ConfigureAwait(false);
            string? line = await _process.StandardOutput.ReadLineAsync().WaitAsync(cancellation.Token).ConfigureAwait(false);
            if (line is null)
            {
                string stderr = await _process.StandardError.ReadToEndAsync(cancellation.Token).ConfigureAwait(false);
                throw new InvalidOperationException($"hpd-vz exited before writing a response. stderr: {stderr}");
            }

            return JsonSerializer.Deserialize(
                line,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)
                ?? throw new JsonException("Swift helper response was not a helper envelope.");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _process.StandardInput.Close();
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
            }
            catch
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            finally
            {
                _process.Dispose();
            }
        }

        private static string ResolveHelperPath()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                string helperRoot = Path.Combine(directory.FullName, "shared", "src", "HPD-Environment", "hpd-vz");
                if (Directory.Exists(helperRoot))
                {
                    return FindBuiltHelper(helperRoot);
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not locate hpd-vz source root from the test base directory.");
        }

        private static string FindBuiltHelper(string helperRoot)
        {
            string[] candidates =
            [
                Path.Combine(helperRoot, ".build", "debug", "hpd-vz"),
                Path.Combine(helperRoot, ".build", "arm64-apple-macosx", "debug", "hpd-vz"),
                Path.Combine(helperRoot, ".build", "x86_64-apple-macosx", "debug", "hpd-vz"),
            ];

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            string? discovered = Directory.Exists(Path.Combine(helperRoot, ".build"))
                ? Directory.EnumerateFiles(Path.Combine(helperRoot, ".build"), "hpd-vz", SearchOption.AllDirectories)
                    .FirstOrDefault(File.Exists)
                : null;
            return discovered ?? throw new FileNotFoundException("Built hpd-vz helper was not found. Run swift build first.", helperRoot);
        }
    }
}
