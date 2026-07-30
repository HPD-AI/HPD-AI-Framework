using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using System.Net;
using System.Net.Sockets;

namespace HPD.Environment.Local.Tests;

public sealed class LocalEnvironmentVerticalSliceTests
{
    [Fact]
    public async Task Provider_reconstruction_advances_the_durable_generation()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-local-provider-generation",
            Guid.NewGuid().ToString("N"));
        try
        {
            ulong first = await HostProviderGenerationAsync(root);
            ulong second = await HostProviderGenerationAsync(root);

            Assert.Equal(first + 1, second);
            Assert.Equal(
                second.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                File.ReadAllText(
                    Path.Combine(root, "provider-generation")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("not-a-generation")]
    [InlineData("9223372036854775808")]
    public void Corrupt_provider_generation_fails_closed(
        string persisted)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-local-provider-generation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "provider-generation"),
            persisted);
        try
        {
            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() =>
                    new LocalEnvironmentProviderModule(
                        new LocalEnvironmentProviderOptions
                        {
                            WorkloadStateRoot = root,
                        }));

            Assert.Contains(
                "ProviderGenerationMalformed",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Logical_host_restart_advances_host_start_generation()
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions()));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        var spec = new RuntimeHostSpec
        {
            PreferredProvider =
                LocalEnvironmentProviderDescriptor.ProviderId,
            Platform =
                LocalEnvironmentProviderDescriptor.CurrentPlatform(),
        };
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> first =
            await runtime.EnsureHostAsync(spec);
        _ = await runtime.StopHostAsync(StopPolicy.Default);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> restarted =
            await runtime.EnsureHostAsync(spec);

        Assert.Equal(
            first.Status.Generations.HostStartGeneration!.Value.Value + 1,
            restarted.Status.Generations.HostStartGeneration!.Value.Value);
        Assert.Equal(
            restarted.Status.Generations.HostStartGeneration,
            restarted.Status.Readiness!.ObservedHostStartGeneration);
    }

    [Fact]
    public async Task Unsupported_host_platform_is_rejected()
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions()));
        var provider = registry.RuntimeHostProviders.Single();
        PlatformSpec current =
            LocalEnvironmentProviderDescriptor.CurrentPlatform();

        RuntimeHostStatus status = await provider.EnsureAsync(
            Metadata<RuntimeHost>(
                "unsupported-host",
                "RuntimeHost",
                new ResourceScope("test")),
            new RuntimeHostSpec
            {
                PreferredProvider =
                    LocalEnvironmentProviderDescriptor.ProviderId,
                Platform = new PlatformSpec(
                    current.OperatingSystem == "windows"
                        ? "plan9"
                        : "windows",
                    current.Architecture),
            },
            observed: null);

        Assert.Equal(RuntimeHostPhase.Failed, status.HostPhase);
        Assert.Contains(status.Diagnostics, diagnostic =>
            diagnostic.Code.Value ==
                "LocalEnvironment.PlatformMismatch");
    }

    [Fact]
    public async Task Rootful_engine_can_be_forbidden_by_policy()
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                EngineSocketPath = "/test/docker.sock",
                AllowRootfulEngine = false,
            },
            new FakeEngineProbe()));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider =
                    LocalEnvironmentProviderDescriptor.ProviderId,
                Platform =
                    LocalEnvironmentProviderDescriptor.CurrentPlatform(),
            });

        ResourceSnapshot<EngineControlPlane, EngineControlPlaneSpec, EngineControlPlaneStatus> engine =
            await runtime.EnsureEngineControlPlaneAsync(
                new EngineControlPlaneSpec
                {
                    Kind =
                        EngineControlPlaneKind.DockerCompatible,
                    Api = EngineApiKind.DockerCompatible,
                    AuthorityMode =
                        EngineAuthorityMode.ProviderDefined,
                    ImageStore =
                        EngineImageStoreMode.EngineLocal,
                    Host = Ref(host.Metadata),
                    EndpointPolicy = EnginePolicy(),
                });

        Assert.Equal(
            EngineControlPlanePhase.Failed,
            engine.Status.EnginePhase);
        Assert.Contains(engine.Status.Diagnostics, diagnostic =>
            diagnostic.Code.Value ==
                "LocalEnvironment.RootfulEngineForbidden");
    }

    [Fact]
    public async Task Host_process_rejects_working_directory_outside_provider_state()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-local-process-root",
            Guid.NewGuid().ToString("N"));
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                EngineSocketPath = "/test/docker.sock",
                DockerCliPath = "/bin/echo",
                WorkloadStateRoot = root,
            },
            new FakeEngineProbe
            {
                SocketPath = "/test/docker.sock",
            }));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider =
                    LocalEnvironmentProviderDescriptor.ProviderId,
                Platform =
                    LocalEnvironmentProviderDescriptor.CurrentPlatform(),
            });
        _ = await runtime.EnsureEngineControlPlaneAsync(
            new EngineControlPlaneSpec
            {
                Kind = EngineControlPlaneKind.DockerCompatible,
                Api = EngineApiKind.DockerCompatible,
                AuthorityMode = EngineAuthorityMode.Rootful,
                ImageStore = EngineImageStoreMode.EngineLocal,
                Host = Ref(host.Metadata),
                EndpointPolicy = EnginePolicy(),
            });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec
            {
                PreferredHost = Ref(host.Metadata),
            });
        try
        {
            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    runtime.RunProcessAsync(new ProcessInvocationSpec
                    {
                        Target = unit.Status.Handle!.Value,
                        Command = new ProcessCommandSpec
                        {
                            FileName = "/bin/sh",
                            WorkingDirectory = Path.GetTempPath(),
                            Arguments =
                            [
                                "-ceu",
                                "true",
                                "hpdos-compose-stage",
                            ],
                        },
                    }).AsTask());

            Assert.Contains(
                "HostWorkingDirectoryRejected",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            await runtime.DeleteExecutionUnitAsync(
                Ref(unit.Metadata));
            await runtime.DeleteHostAsync();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Host_shell_rejects_unrecognized_compose_marker()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-local-process-root",
            Guid.NewGuid().ToString("N"));
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                EngineSocketPath = "/test/docker.sock",
                DockerCliPath = "/bin/echo",
                WorkloadStateRoot = root,
            },
            new FakeEngineProbe()));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider =
                    LocalEnvironmentProviderDescriptor.ProviderId,
                Platform =
                    LocalEnvironmentProviderDescriptor.CurrentPlatform(),
            });
        _ = await runtime.EnsureEngineControlPlaneAsync(
            new EngineControlPlaneSpec
            {
                Kind =
                    EngineControlPlaneKind.DockerCompatible,
                Api = EngineApiKind.DockerCompatible,
                AuthorityMode =
                    EngineAuthorityMode.Rootful,
                ImageStore =
                    EngineImageStoreMode.EngineLocal,
                Host = Ref(host.Metadata),
                EndpointPolicy = EnginePolicy(),
            });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec
            {
                PreferredHost = Ref(host.Metadata),
            });
        try
        {
            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    runtime.RunProcessAsync(new ProcessInvocationSpec
                    {
                        Target = unit.Status.Handle!.Value,
                        Command = new ProcessCommandSpec
                        {
                            FileName = "/bin/sh",
                            Arguments =
                            [
                                "-ceu",
                                "true",
                                "hpdos-compose-arbitrary",
                            ],
                        },
                    }).AsTask());

            Assert.Contains(
                "HostShellRejected",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            await runtime.DeleteExecutionUnitAsync(
                Ref(unit.Metadata));
            await runtime.DeleteHostAsync();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Failed_post_start_setup_confirms_process_cleanup_before_releasing_ownership()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-local-process-root",
            Guid.NewGuid().ToString("N"));
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                DockerCliPath = "/bin/echo",
                WorkloadStateRoot = root,
            }));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider =
                    LocalEnvironmentProviderDescriptor.ProviderId,
                Platform =
                    LocalEnvironmentProviderDescriptor.CurrentPlatform(),
            });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec
            {
                PreferredHost = Ref(host.Metadata),
            });
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                runtime.RunProcessAsync(new ProcessInvocationSpec
                {
                    Target = unit.Status.Handle!.Value,
                    Command = new ProcessCommandSpec
                    {
                        FileName = "/bin/sh",
                        Arguments =
                        [
                            "-ceu",
                            "exec 0<&-; sleep 5",
                            "hpdos-compose-stage",
                        ],
                    },
                    Io = new ProcessIoSpec
                    {
                        StandardInput = new ProcessInputSpec
                        {
                            Kind = ProcessInputKind.InlineBytes,
                            InlineBytes = new byte[1024 * 1024],
                        },
                    },
                }).AsTask());

            await runtime.DeleteExecutionUnitAsync(Ref(unit.Metadata));
            RuntimeHostDeletionResult deleted =
                await runtime.DeleteHostAsync();
            Assert.True(deleted.Deleted);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Completed_retained_process_can_be_deleted_after_handle_disposal()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hpd-local-process-root",
            Guid.NewGuid().ToString("N"));
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                EngineSocketPath = "/test/docker.sock",
                DockerCliPath = "/bin/echo",
                WorkloadStateRoot = root,
            },
            new FakeEngineProbe
            {
                SocketPath = "/test/docker.sock",
            }));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider =
                    LocalEnvironmentProviderDescriptor.ProviderId,
                Platform =
                    LocalEnvironmentProviderDescriptor.CurrentPlatform(),
            });
        _ = await runtime.EnsureEngineControlPlaneAsync(
            new EngineControlPlaneSpec
            {
                Kind = EngineControlPlaneKind.DockerCompatible,
                Api = EngineApiKind.DockerCompatible,
                AuthorityMode = EngineAuthorityMode.Rootful,
                ImageStore = EngineImageStoreMode.EngineLocal,
                Host = Ref(host.Metadata),
                EndpointPolicy = EnginePolicy(),
            });
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec
            {
                PreferredHost = Ref(host.Metadata),
            });
        try
        {
            ResourceSnapshot<
                ProcessInvocation,
                ProcessInvocationSpec,
                ProcessInvocationStatus> process =
                await runtime.StartProcessAsync(new ProcessInvocationSpec
                {
                    Target = unit.Status.Handle!.Value,
                    Command = new ProcessCommandSpec
                    {
                        FileName = "/bin/sh",
                        Arguments =
                        [
                            "-ceu",
                            "true",
                            "hpdos-compose-stage",
                        ],
                    },
                });
            _ = await runtime.WaitProcessAsync(Ref(process.Metadata));

            await runtime.DeleteProcessAsync(Ref(process.Metadata));

            Assert.Empty(await runtime.ListProcessesAsync(Ref(unit.Metadata)));
        }
        finally
        {
            await runtime.DeleteExecutionUnitAsync(Ref(unit.Metadata));
            await runtime.DeleteHostAsync();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task NativeHostEngineUnitAndAuthorityFormOneOwnedSlice()
    {
        var probe = new FakeEngineProbe();
        var registry = new EnvironmentProviderRegistry();
        var module = new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                EngineSocketPath = "/test/docker.sock",
            },
            probe,
            new FakeNetworkClient());
        registry.RegisterModule(module);
        var runtime = new InMemoryEnvironmentRuntime(registry);
        PlatformSpec platform =
            LocalEnvironmentProviderDescriptor.CurrentPlatform();

        ResourceSnapshot<
            RuntimeHost,
            RuntimeHostSpec,
            RuntimeHostStatus> host = await runtime.EnsureHostAsync(
                new RuntimeHostSpec
                {
                    PreferredProvider =
                        LocalEnvironmentProviderDescriptor.ProviderId,
                    Platform = platform,
                });
        ResourceSnapshot<
            EngineControlPlane,
            EngineControlPlaneSpec,
            EngineControlPlaneStatus> engine =
            await runtime.EnsureEngineControlPlaneAsync(
                new EngineControlPlaneSpec
                {
                    Kind =
                        EngineControlPlaneKind.DockerCompatible,
                    Api = EngineApiKind.DockerCompatible,
                    AuthorityMode = EngineAuthorityMode.Rootful,
                    ImageStore = EngineImageStoreMode.EngineLocal,
                    WorkloadAdoption =
                        EngineWorkloadAdoptionMode.None,
                    Host = Ref(host.Metadata),
                    EndpointPolicy = EnginePolicy(),
                });
        ResourceSnapshot<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(
                new ExecutionUnitSpec
                {
                    PreferredHost = Ref(host.Metadata),
                    ReconciliationKey =
                        new ExecutionUnitIdentityKey("test-unit"),
                    WorkloadStorage = new WorkloadStorageRequest
                    {
                        LogicalId = "test-workload-storage",
                        PersistenceClass =
                            WorkloadStoragePersistenceClass.Workload,
                    },
                });
        EngineAuthorityBindingPlan plan =
            await runtime.PlanEngineAuthorityBindingAsync(
                new EngineAuthorityBindingRequest
                {
                    Engine = Ref(engine.Metadata),
                    Api = EngineApiKind.DockerCompatible,
                    TargetUnit = unit.Status.Handle!.Value,
                    TargetSocketPath = new UnixSocketPath(
                        "/run/hpd/engine/docker.sock"),
                    Provenance = new SensitiveProvenance(
                        Actor: "test",
                        Reason: "vertical-slice"),
                });
        ResourceSnapshot<
            AuthorityBinding,
            AuthorityBindingSpec,
            AuthorityBindingStatus> binding =
            await runtime.EnsureEngineAuthorityBindingAsync(plan);

        Assert.Equal(RuntimeHostPhase.Ready, host.Status.HostPhase);
        Assert.False(host.Status.GuestControl!.Expected);
        Assert.Equal(
            EngineControlPlanePhase.Ready,
            engine.Status.EnginePhase);
        Assert.Equal(
            new EngineIncarnationGeneration(1),
            engine.Status.EngineGeneration);
        Assert.True(engine.Status.ExternalMutationPossible);
        Assert.Equal(
            ExecutionUnitPhase.Ready,
            unit.Status.UnitPhase);
        Assert.Equal(
            "test-workload-storage",
            unit.Status.WorkloadStorage!.LogicalId);
        Assert.Equal(
            unit.Metadata.Generation,
            unit.Status.WorkloadStorage.Generation);
        Assert.Contains(
            $"{Path.DirectorySeparatorChar}allocations{Path.DirectorySeparatorChar}",
            unit.Status.WorkloadStorage.EffectiveRuntimePath,
            StringComparison.Ordinal);
        Assert.True(plan.Accepted);
        Assert.Equal(
            AuthorityBindingPhase.Projected,
            binding.Status.BindingPhase);
        Assert.Equal(
            SensitiveAuthorityClass.RootfulEngineControl,
            binding.Status.BoundAuthority!.EffectiveAuthorityClass);
        string allocationPath =
            unit.Status.WorkloadStorage.EffectiveRuntimePath;
        await runtime.RevokeAuthorityBindingAsync(Ref(binding.Metadata));
        AuthorityAuditEvent[] audit =
            module.GetAuthorityAuditEvents(
                binding.Metadata.Id.Value);
        Assert.Equal(
            [
                AuthorityAuditKind.Projected,
                AuthorityAuditKind.Revoked,
                AuthorityAuditKind.RevocationVerified,
            ],
            audit.Select(static item => item.Kind));
        Assert.All(
            audit,
            item => Assert.Equal("[REDACTED]", item.Actor));
        await runtime.DeleteExecutionUnitAsync(Ref(unit.Metadata));
        Assert.False(Directory.Exists(allocationPath));
    }

    [Fact]
    public async Task EngineReconnectAdvancesIncarnationAfterUnavailableProbe()
    {
        var probe = new FakeEngineProbe();
        var module = new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                EngineSocketPath = "/test/docker.sock",
            },
            probe);
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(module);
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<
            RuntimeHost,
            RuntimeHostSpec,
            RuntimeHostStatus> host = await runtime.EnsureHostAsync(
                new RuntimeHostSpec
                {
                    PreferredProvider =
                        LocalEnvironmentProviderDescriptor.ProviderId,
                    Platform =
                        LocalEnvironmentProviderDescriptor.CurrentPlatform(),
                });
        EngineControlPlaneSpec spec = new()
        {
            Kind = EngineControlPlaneKind.DockerCompatible,
            Api = EngineApiKind.DockerCompatible,
            AuthorityMode = EngineAuthorityMode.Rootful,
            ImageStore = EngineImageStoreMode.EngineLocal,
            Host = Ref(host.Metadata),
            EndpointPolicy = EnginePolicy(),
        };
        ResourceSnapshot<
            EngineControlPlane,
            EngineControlPlaneSpec,
            EngineControlPlaneStatus> first =
            await runtime.EnsureEngineControlPlaneAsync(spec);
        ResourceSnapshot<
            EngineControlPlane,
            EngineControlPlaneSpec,
            EngineControlPlaneStatus> repeated =
            await runtime.EnsureEngineControlPlaneAsync(spec);

        probe.Failure = new IOException("engine unavailable");
        ResourceSnapshot<
            EngineControlPlane,
            EngineControlPlaneSpec,
            EngineControlPlaneStatus> failed =
            await runtime.EnsureEngineControlPlaneAsync(spec);
        probe.Failure = null;
        ResourceSnapshot<
            EngineControlPlane,
            EngineControlPlaneSpec,
            EngineControlPlaneStatus> recovered =
            await runtime.EnsureEngineControlPlaneAsync(spec);

        Assert.Equal(
            EngineControlPlanePhase.Ready,
            first.Status.EnginePhase);
        Assert.Equal(first.Metadata, repeated.Metadata);
        Assert.Equal(
            ResourceReconciliationOutcome.Accepted,
            repeated.Status.ReconciliationOutcome);
        Assert.Equal(
            EngineControlPlanePhase.Failed,
            failed.Status.EnginePhase);
        Assert.Equal(
            new EngineIncarnationGeneration(2),
            recovered.Status.EngineGeneration);
    }

    [Fact]
    public async Task HardwareVirtualizationIsTruthfullyUnsupported()
    {
        var reporter = new LocalEnvironmentCapabilityReporter(
            new LocalEnvironmentProviderOptions());
        ProviderCapabilityReport report =
            await reporter.GetCapabilitiesAsync(
                LocalEnvironmentProviderDescriptor.ProviderId);

        CapabilityFact virtualization = Assert.Single(
            report.Capabilities,
            fact => fact.Id ==
                LocalEnvironmentProviderDescriptor
                    .HardwareVirtualizationCapability);
        Assert.Equal(
            CapabilityState.Unsupported,
            virtualization.State);
    }

    [Fact]
    public async Task RootlessEngineAuthorityRemainsRootlessAfterRedaction()
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                EngineSocketPath = "/run/user/501/docker.sock",
            },
            new FakeEngineProbe { IsRootless = true }));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider =
                    LocalEnvironmentProviderDescriptor.ProviderId,
                Platform =
                    LocalEnvironmentProviderDescriptor.CurrentPlatform(),
            });
        ResourceSnapshot<
            EngineControlPlane,
            EngineControlPlaneSpec,
            EngineControlPlaneStatus> engine =
            await runtime.EnsureEngineControlPlaneAsync(
                new EngineControlPlaneSpec
                {
                    Kind = EngineControlPlaneKind.DockerCompatible,
                    Api = EngineApiKind.DockerCompatible,
                    AuthorityMode = EngineAuthorityMode.Rootless,
                    ImageStore = EngineImageStoreMode.EngineLocal,
                    Host = Ref(host.Metadata),
                    EndpointPolicy = EnginePolicy() with
                    {
                        AuthorityClass =
                            SensitiveAuthorityClass.RootlessEngineControl,
                    },
                });
        ResourceSnapshot<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(
                new ExecutionUnitSpec
                {
                    PreferredHost = Ref(host.Metadata),
                    ReconciliationKey =
                        new ExecutionUnitIdentityKey("rootless-unit"),
                });
        EngineAuthorityBindingPlan plan =
            await runtime.PlanEngineAuthorityBindingAsync(
                new EngineAuthorityBindingRequest
                {
                    Engine = Ref(engine.Metadata),
                    Api = EngineApiKind.DockerCompatible,
                    TargetUnit = unit.Status.Handle!.Value,
                    TargetSocketPath =
                        new UnixSocketPath("/run/hpd/engine/docker.sock"),
                });
        ResourceSnapshot<
            AuthorityBinding,
            AuthorityBindingSpec,
            AuthorityBindingStatus> binding =
            await runtime.EnsureEngineAuthorityBindingAsync(plan);

        Assert.True(plan.Accepted);
        Assert.Equal(
            SensitiveAuthorityClass.RootlessEngineControl,
            binding.Status.BoundAuthority!.EffectiveAuthorityClass);
    }

    [Fact]
    public async Task EngineProcessUsesProviderPrivateSocket()
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                EngineSocketPath = "/private/test/docker.sock",
                DockerCliPath = "/bin/echo",
                DockerComposeCliPath = "/bin/echo",
                WorkloadStateRoot = Path.Combine(
                    Path.GetTempPath(),
                    "hpd-local-tests",
                    Guid.NewGuid().ToString("N")),
            },
            new FakeEngineProbe
            {
                SocketPath = "/private/test/docker.sock",
            }));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider =
                    LocalEnvironmentProviderDescriptor.ProviderId,
                Platform =
                    LocalEnvironmentProviderDescriptor.CurrentPlatform(),
            });
        ResourceSnapshot<
            EngineControlPlane,
            EngineControlPlaneSpec,
            EngineControlPlaneStatus> engine =
            await runtime.EnsureEngineControlPlaneAsync(
                new EngineControlPlaneSpec
                {
                    Kind = EngineControlPlaneKind.DockerCompatible,
                    Api = EngineApiKind.DockerCompatible,
                    AuthorityMode = EngineAuthorityMode.Rootful,
                    ImageStore = EngineImageStoreMode.EngineLocal,
                    Host = Ref(host.Metadata),
                    EndpointPolicy = EnginePolicy(),
                });
        ResourceSnapshot<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(
                new ExecutionUnitSpec
                {
                    PreferredHost = Ref(host.Metadata),
                    ReconciliationKey =
                        new ExecutionUnitIdentityKey("process-unit"),
                });
        EngineAuthorityBindingPlan plan =
            await runtime.PlanEngineAuthorityBindingAsync(
                new EngineAuthorityBindingRequest
                {
                    Engine = Ref(engine.Metadata),
                    Api = EngineApiKind.DockerCompatible,
                    TargetUnit = unit.Status.Handle!.Value,
                    TargetSocketPath =
                        new UnixSocketPath("/run/hpd/engine/docker.sock"),
                });
        ResourceSnapshot<
            AuthorityBinding,
            AuthorityBindingSpec,
            AuthorityBindingStatus> binding =
            await runtime.EnsureEngineAuthorityBindingAsync(plan);

        ProcessInvocationResult result = await runtime.RunProcessAsync(
            new ProcessInvocationSpec
            {
                Target = unit.Status.Handle.Value,
                Command = new ProcessCommandSpec
                {
                    FileName = "/usr/bin/docker",
                    Arguments =
                    [
                        "--host",
                        "unix:///run/hpd/engine/docker.sock",
                        "compose",
                        "--project-name",
                        "hpd-test",
                        "version",
                    ],
                },
                Isolation = ProcessIsolationPolicy.Default with
                {
                    AuthorityBindings = [Ref(binding.Metadata)],
                },
            });

        Assert.Equal(0, result.ExitCode);
        string output =
            System.Text.Encoding.UTF8.GetString(
                result.Output.Stdout.CapturedBytes.Span);
        Assert.Contains(
            "unix:///private/test/docker.sock",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "compose --project-name hpd-test version",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "/run/hpd/engine/docker.sock",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EndpointPublicationIsLoopbackOnlyAndRevocable()
    {
        using var target = new TcpListener(IPAddress.Loopback, 0);
        target.Start();
        int targetPort =
            ((IPEndPoint)target.LocalEndpoint).Port;
        Task targetTask = Task.Run(async () =>
        {
            using TcpClient client =
                await target.AcceptTcpClientAsync();
            using NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[4];
            await stream.ReadExactlyAsync(buffer);
            await stream.WriteAsync(buffer);
        });
        var probe = new FakeEngineProbe();
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                EngineSocketPath = "/test/docker.sock",
            },
            probe,
            new FakeNetworkClient()));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider =
                    LocalEnvironmentProviderDescriptor.ProviderId,
                Platform =
                    LocalEnvironmentProviderDescriptor.CurrentPlatform(),
            });
        var engineSpec = new EngineControlPlaneSpec
        {
            Kind = EngineControlPlaneKind.DockerCompatible,
            Api = EngineApiKind.DockerCompatible,
            AuthorityMode = EngineAuthorityMode.Rootful,
            ImageStore = EngineImageStoreMode.EngineLocal,
            Host = Ref(host.Metadata),
            EndpointPolicy = EnginePolicy(),
        };
        await runtime.EnsureEngineControlPlaneAsync(engineSpec);
        ResourceSnapshot<
            PublishedEndpoint,
            PublishedEndpointSpec,
            PublishedEndpointStatus> endpoint =
            await runtime.EnsurePublishedEndpointAsync(
                new PublishedEndpointSpec
                {
                    Listener = new EndpointListenerSpec(
                        EndpointListenerKind.HostAddress,
                        NetworkTransport.Tcp,
                        new IpAddressValue(
                            NetworkAddressFamily.IPv4,
                            0,
                            0x7f000001),
                        Ports: null,
                        Socket: null),
                    Target = new EndpointRouteTarget(
                        EndpointTargetKind.NetworkAddress,
                        Membership: null,
                        Unit: null,
                        Process: null,
                        ServiceName: null,
                        NetworkTransport.Tcp,
                        new NetworkPort(
                            checked((ushort)targetPort)),
                        SocketPath: null,
                        new IpAddressValue(
                            NetworkAddressFamily.IPv4,
                            0,
                            0x7f000001)),
                    ExposurePolicy = new EndpointExposurePolicy
                    {
                        Scope = EndpointExposureScope.HostLocal,
                        AllowEphemeralPort = true,
                    },
                    AuthorizationPolicy =
                        new EndpointAuthorizationPolicy
                        {
                            RequireLoopbackClient = true,
                        },
                    RoutingHost = Ref(host.Metadata),
                });
        int publishedPort =
            endpoint.Status.BoundListener!.Value.Ports!.Value
                .Start.Value;
        using var forwarded = new TcpClient();
        await forwarded.ConnectAsync(
            IPAddress.Loopback,
            publishedPort);
        using NetworkStream forwardedStream =
            forwarded.GetStream();
        await forwardedStream.WriteAsync("ping"u8.ToArray());
        byte[] reply = new byte[4];
        await forwardedStream.ReadExactlyAsync(reply);

        Assert.Equal("ping", System.Text.Encoding.ASCII.GetString(reply));
        await targetTask;
        probe.Fingerprint = "sha256:replacement-engine";
        await runtime.EnsureEngineControlPlaneAsync(engineSpec);
        PublishedEndpointStatus stale =
            await registry.EndpointPublicationProviders.Single()
                .GetStatusAsync(Ref(endpoint.Metadata));
        Assert.Equal(PublishedEndpointPhase.Failed, stale.EndpointPhase);
        await runtime.ReleasePublishedEndpointAsync(
            Ref(endpoint.Metadata));
        using var rejected = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(
            async () => await rejected.ConnectAsync(
                IPAddress.Loopback,
                publishedPort));
    }

    [Fact]
    public async Task EngineGenerationChangeFencesExistingAuthority()
    {
        var probe = new FakeEngineProbe();
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                EngineSocketPath = "/test/docker.sock",
                DockerCliPath = "/bin/echo",
            },
            probe));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider =
                    LocalEnvironmentProviderDescriptor.ProviderId,
                Platform =
                    LocalEnvironmentProviderDescriptor.CurrentPlatform(),
            });
        EngineControlPlaneSpec engineSpec = new()
        {
            Kind = EngineControlPlaneKind.DockerCompatible,
            Api = EngineApiKind.DockerCompatible,
            AuthorityMode = EngineAuthorityMode.Rootful,
            ImageStore = EngineImageStoreMode.EngineLocal,
            Host = Ref(host.Metadata),
            EndpointPolicy = EnginePolicy(),
        };
        ResourceSnapshot<
            EngineControlPlane,
            EngineControlPlaneSpec,
            EngineControlPlaneStatus> engine =
            await runtime.EnsureEngineControlPlaneAsync(engineSpec);
        ResourceSnapshot<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(
                new ExecutionUnitSpec
                {
                    PreferredHost = Ref(host.Metadata),
                    ReconciliationKey =
                        new ExecutionUnitIdentityKey("fenced-unit"),
                });
        EngineAuthorityBindingPlan plan =
            await runtime.PlanEngineAuthorityBindingAsync(
                new EngineAuthorityBindingRequest
                {
                    Engine = Ref(engine.Metadata),
                    Api = EngineApiKind.DockerCompatible,
                    TargetUnit = unit.Status.Handle!.Value,
                    TargetSocketPath =
                        new UnixSocketPath("/run/hpd/engine/docker.sock"),
                });
        ResourceSnapshot<
            AuthorityBinding,
            AuthorityBindingSpec,
            AuthorityBindingStatus> binding =
            await runtime.EnsureEngineAuthorityBindingAsync(plan);

        probe.Fingerprint = "sha256:replacement-engine";
        ResourceSnapshot<
            EngineControlPlane,
            EngineControlPlaneSpec,
            EngineControlPlaneStatus> replacement =
            await runtime.EnsureEngineControlPlaneAsync(engineSpec);

        Assert.Equal(
            new EngineIncarnationGeneration(2),
            replacement.Status.EngineGeneration);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await runtime.RunProcessAsync(
                new ProcessInvocationSpec
                {
                    Target = unit.Status.Handle.Value,
                    Command = new ProcessCommandSpec
                    {
                        FileName = "/usr/bin/docker",
                        Arguments = ["version"],
                    },
                    Isolation = ProcessIsolationPolicy.Default with
                    {
                        AuthorityBindings = [Ref(binding.Metadata)],
                    },
                }).AsTask());
    }

    [Fact]
    public async Task OwnedNetworkMembershipAndDiscoveryAreEngineFenced()
    {
        var probe = new FakeEngineProbe();
        var networkClient = new FakeNetworkClient();
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                EngineSocketPath = "/test/docker.sock",
            },
            probe,
            networkClient));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<
            RuntimeHost,
            RuntimeHostSpec,
            RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider =
                    LocalEnvironmentProviderDescriptor.ProviderId,
                Platform =
                    LocalEnvironmentProviderDescriptor.CurrentPlatform(),
            });
        var engineSpec = new EngineControlPlaneSpec
        {
            Kind = EngineControlPlaneKind.DockerCompatible,
            Api = EngineApiKind.DockerCompatible,
            AuthorityMode = EngineAuthorityMode.Rootful,
            ImageStore = EngineImageStoreMode.EngineLocal,
            Host = Ref(host.Metadata),
            EndpointPolicy = EnginePolicy(),
        };
        ResourceSnapshot<
            EngineControlPlane,
            EngineControlPlaneSpec,
            EngineControlPlaneStatus> engine =
            await runtime.EnsureEngineControlPlaneAsync(engineSpec);
        ResourceSnapshot<
            ExecutionUnit,
            ExecutionUnitSpec,
            ExecutionUnitStatus> unit =
            await runtime.EnsureExecutionUnitAsync(
                new ExecutionUnitSpec
                {
                    PreferredHost = Ref(host.Metadata),
                    ReconciliationKey =
                        new ExecutionUnitIdentityKey("network-unit"),
                });
        EngineAuthorityBindingPlan authorityPlan =
            await runtime.PlanEngineAuthorityBindingAsync(
                new EngineAuthorityBindingRequest
                {
                    Engine = Ref(engine.Metadata),
                    Api = EngineApiKind.DockerCompatible,
                    TargetUnit = unit.Status.Handle!.Value,
                    TargetSocketPath =
                        new UnixSocketPath("/run/hpd/engine/docker.sock"),
                    Provenance = new SensitiveProvenance(
                        "test",
                        "network-conformance"),
                });
        ResourceSnapshot<
            AuthorityBinding,
            AuthorityBindingSpec,
            AuthorityBindingStatus> authority =
            await runtime.EnsureEngineAuthorityBindingAsync(
                authorityPlan);
        ResourceMetadata<Network> networkMetadata =
            Metadata<Network>(
                "network-1",
                "Network",
                host.Metadata.Scope);
        INetworkProvider networkProvider =
            Assert.Single(registry.NetworkProviders);
        NetworkStatus network =
            await networkProvider.EnsureNetworkAsync(
                networkMetadata,
                new NetworkSpec
                {
                    Scope = NetworkScope.Runtime,
                    ConnectivityIntent =
                        NetworkConnectivityIntent.NatEgress,
                    AddressFamilies =
                        AddressFamilyRequirement.IPv4Required,
                    ExposurePolicy = new NetworkExposurePolicy
                    {
                        AllowPublishedEndpoints = true,
                        RequireExplicitPublication = true,
                    },
                },
                new NetworkRealizationContext(
                    Ref(unit.Metadata),
                    Ref(authority.Metadata)),
                observed: null);
        ResourceRef<Network> networkRef =
            Ref(networkMetadata);
        ResourceMetadata<NetworkMembership> membershipMetadata =
            Metadata<NetworkMembership>(
                "membership-1",
                "NetworkMembership",
                host.Metadata.Scope);
        NetworkMembershipStatus membership =
            await Assert.Single(registry.NetworkMembershipProviders)
                .EnsureMembershipAsync(
                    membershipMetadata,
                    new NetworkMembershipSpec
                    {
                        Network = networkRef,
                        Target = new NetworkMembershipTarget(
                            NetworkMembershipTargetKind.ExecutionUnit,
                            Host: null,
                            unit.Status.Handle,
                            Process: null),
                        Hostname = new ScopedName("editor"),
                        ServiceNames =
                            [new ServiceName("penpot")],
                    },
                    observed: null);
        ResourceMetadata<ServiceDiscovery> discoveryMetadata =
            Metadata<ServiceDiscovery>(
                "discovery-1",
                "ServiceDiscovery",
                host.Metadata.Scope);
        IServiceDiscoveryProvider discoveryProvider =
            Assert.Single(registry.ServiceDiscoveryProviders);
        ServiceDiscoveryStatus discovery =
            await discoveryProvider.EnsureServiceDiscoveryAsync(
                discoveryMetadata,
                new ServiceDiscoverySpec
                {
                    Scope = DiscoveryScope.Network,
                    Network = networkRef,
                },
                observed: null);
        IReadOnlyList<DiscoveryRecord> resolved =
            await discoveryProvider.ResolveAsync(
                new ServiceDiscoveryQuery(
                    Ref(discoveryMetadata),
                    new DnsName("PENPOT"),
                    DiscoveryRecordKind.Service));

        Assert.Equal(NetworkPhase.Ready, network.NetworkPhase);
        Assert.NotNull(network.Realization);
        Assert.StartsWith(
            "hpd-",
            network.Realization!.Name.Value,
            StringComparison.Ordinal);
        Assert.Equal(new string('a', 64), network.Realization.OpaqueId);
        Assert.Equal(1, networkClient.EnsureCount);
        Assert.Equal(
            NetworkMembershipPhase.Ready,
            membership.MembershipPhase);
        Assert.Single(resolved);
        Assert.Equal(
            ServiceDiscoveryPhase.Ready,
            discovery.DiscoveryPhase);
        NetworkStatus physicallyObserved =
            await networkProvider.GetStatusAsync(networkRef);
        Assert.Equal(NetworkPhase.Ready, physicallyObserved.NetworkPhase);
        Assert.Equal(1, networkClient.ObserveCount);
        networkClient.MutateLabels = true;
        NetworkStatus externallyMutated =
            await networkProvider.GetStatusAsync(networkRef);
        Assert.Equal(
            NetworkPhase.Failed,
            externallyMutated.NetworkPhase);
        Assert.Contains(
            externallyMutated.Diagnostics,
            diagnostic => diagnostic.Code.Value ==
                "LocalEnvironment.NetworkExternalMutationDetected");
        networkClient.MutateLabels = false;

        probe.Fingerprint = "sha256:replacement-engine";
        await runtime.EnsureEngineControlPlaneAsync(engineSpec);
        NetworkStatus stale =
            await networkProvider.GetStatusAsync(networkRef);
        Assert.Equal(NetworkPhase.Failed, stale.NetworkPhase);
        Assert.Empty(await discoveryProvider.ResolveAsync(
            new ServiceDiscoveryQuery(
                Ref(discoveryMetadata),
                new DnsName("penpot"))));

        EngineAuthorityBindingPlan reboundPlan =
            await runtime.PlanEngineAuthorityBindingAsync(
                new EngineAuthorityBindingRequest
                {
                    Engine = Ref(engine.Metadata),
                    Api = EngineApiKind.DockerCompatible,
                    TargetUnit = unit.Status.Handle!.Value,
                    TargetSocketPath =
                        new UnixSocketPath("/run/hpd/engine/docker.sock"),
                    Provenance = new SensitiveProvenance(
                        "test",
                        "network-recovery"),
                });
        ResourceSnapshot<
            AuthorityBinding,
            AuthorityBindingSpec,
            AuthorityBindingStatus> reboundAuthority =
            await runtime.EnsureEngineAuthorityBindingAsync(
                reboundPlan);
        NetworkStatus adopted =
            await networkProvider.EnsureNetworkAsync(
                networkMetadata,
                new NetworkSpec
                {
                    Scope = NetworkScope.Runtime,
                    ConnectivityIntent =
                        NetworkConnectivityIntent.NatEgress,
                    AddressFamilies =
                        AddressFamilyRequirement.IPv4Required,
                    ExposurePolicy = new NetworkExposurePolicy
                    {
                        AllowPublishedEndpoints = true,
                        RequireExplicitPublication = true,
                    },
                },
                new NetworkRealizationContext(
                    Ref(unit.Metadata),
                    Ref(reboundAuthority.Metadata)),
                observed: stale);
        Assert.Equal(NetworkPhase.Ready, adopted.NetworkPhase);
        Assert.Equal(new string('a', 64), adopted.Realization!.OpaqueId);
        await networkProvider.DeleteNetworkAsync(networkRef);
        Assert.Equal(1, networkClient.DeleteCount);
        Assert.Null(await networkClient.ObserveAsync(
            "/test/docker.sock",
            adopted.Realization.OpaqueId));
    }

    [Fact]
    public async Task NetworkRejectsHostVisibleOrImplicitPublication()
    {
        var probe = new FakeEngineProbe();
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                EngineSocketPath = "/test/docker.sock",
            },
            probe));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<
            RuntimeHost,
            RuntimeHostSpec,
            RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider =
                    LocalEnvironmentProviderDescriptor.ProviderId,
                Platform =
                    LocalEnvironmentProviderDescriptor.CurrentPlatform(),
            });
        await runtime.EnsureEngineControlPlaneAsync(
            new EngineControlPlaneSpec
            {
                Kind = EngineControlPlaneKind.DockerCompatible,
                Api = EngineApiKind.DockerCompatible,
                AuthorityMode = EngineAuthorityMode.Rootful,
                ImageStore = EngineImageStoreMode.EngineLocal,
                Host = Ref(host.Metadata),
                EndpointPolicy = EnginePolicy(),
            });

        NetworkStatus rejected =
            await Assert.Single(registry.NetworkProviders)
                .EnsureNetworkAsync(
                    Metadata<Network>(
                        "network-rejected",
                        "Network",
                        host.Metadata.Scope),
                    new NetworkSpec
                    {
                        Scope = NetworkScope.Runtime,
                        ConnectivityIntent =
                            NetworkConnectivityIntent.NatEgress,
                        AddressFamilies =
                            AddressFamilyRequirement.IPv4Required,
                        ExposurePolicy =
                            new NetworkExposurePolicy
                            {
                                AllowHostVisibleAddresses = true,
                                RequireExplicitPublication = false,
                            },
                    },
                    realizationContext: null,
                    observed: null);

        Assert.Equal(NetworkPhase.Failed, rejected.NetworkPhase);
        Assert.Contains(rejected.Diagnostics, diagnostic =>
            diagnostic.Code.Value ==
                "LocalEnvironment.NetworkHostExposureRejected");
    }

    private static SensitiveEndpointPolicy EnginePolicy() =>
        new()
        {
            Kind = SensitiveEndpointKind.EngineSocket,
            AuthorityClass =
                SensitiveAuthorityClass.RootfulEngineControl,
            Redaction = SensitiveRedactionLevel.RedactIdentifiers,
            RequireAudit = true,
        };

    private static ResourceRef<TResource> Ref<TResource>(
        ResourceMetadata<TResource> metadata)
        where TResource : IExecutionResourceMarker =>
        new(metadata.Id, metadata.Scope, metadata.Generation);

    private static async Task<ulong> HostProviderGenerationAsync(
        string stateRoot)
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalEnvironmentProviderModule(
            new LocalEnvironmentProviderOptions
            {
                WorkloadStateRoot = stateRoot,
            }));
        var runtime = new InMemoryEnvironmentRuntime(registry);
        ResourceSnapshot<
            RuntimeHost,
            RuntimeHostSpec,
            RuntimeHostStatus> host =
            await runtime.EnsureHostAsync(new RuntimeHostSpec
            {
                PreferredProvider =
                    LocalEnvironmentProviderDescriptor.ProviderId,
                Platform =
                    LocalEnvironmentProviderDescriptor.CurrentPlatform(),
            });
        return host.Status.Handle!.Value.ProviderGeneration;
    }

    private static ResourceMetadata<TResource> Metadata<TResource>(
        string id,
        string kind,
        ResourceScope scope)
        where TResource : IExecutionResourceMarker =>
        new()
        {
            Id = new ResourceId<TResource>(id),
            Kind = new ResourceKind(kind),
            Scope = scope,
            Generation = new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("1"),
        };

    private sealed class FakeEngineProbe : ILocalEngineProbe
    {
        public Exception? Failure { get; set; }
        public bool IsRootless { get; init; }
        public string SocketPath { get; init; } = "/test/docker.sock";
        public string Fingerprint { get; set; } =
            "sha256:test-engine";

        public ValueTask<LocalEngineObservation> ProbeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
                return ValueTask.FromException<
                    LocalEngineObservation>(Failure);
            return ValueTask.FromResult(new LocalEngineObservation(
                SocketPath,
                "28.0.0",
                "1.48",
                "linux",
                "arm64",
                Fingerprint,
                IsRootless));
        }
    }

    private sealed class FakeNetworkClient : ILocalEngineNetworkClient
    {
        private readonly Dictionary<string, LocalEngineNetworkObservation>
            _networks = new(StringComparer.Ordinal);
        public int EnsureCount { get; private set; }
        public int ObserveCount { get; private set; }
        public int DeleteCount { get; private set; }
        public bool HideNetworks { get; set; }
        public bool MutateLabels { get; set; }

        public ValueTask<LocalEngineNetworkObservation?> ObserveAsync(
            string socketPath,
            string identifier,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObserveCount++;
            if (HideNetworks)
                return ValueTask.FromResult<
                    LocalEngineNetworkObservation?>(null);
            LocalEngineNetworkObservation? network =
                _networks.Values.SingleOrDefault(item =>
                    string.Equals(
                        item.Id,
                        identifier,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        item.Name,
                        identifier,
                        StringComparison.Ordinal));
            if (network is not null && MutateLabels)
                network = network with
                {
                    Labels = new Dictionary<string, string>(
                        network.Labels,
                        StringComparer.Ordinal)
                    {
                        ["io.hpd.owner"] = "foreign",
                    },
                };
            return ValueTask.FromResult(network);
        }

        public ValueTask<LocalEngineNetworkObservation> EnsureAsync(
            string socketPath,
            string name,
            IReadOnlyDictionary<string, string> labels,
            bool internalOnly,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCount++;
            if (!_networks.TryGetValue(
                    name,
                    out LocalEngineNetworkObservation? network))
            {
                network = new LocalEngineNetworkObservation(
                    new string('a', 64),
                    name,
                    new Dictionary<string, string>(
                        labels,
                        StringComparer.Ordinal),
                    internalOnly);
                _networks.Add(name, network);
            }
            return ValueTask.FromResult(network);
        }

        public ValueTask DeleteAsync(
            string socketPath,
            LocalEngineNetworkObservation expected,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteCount++;
            _networks.Remove(expected.Name);
            return ValueTask.CompletedTask;
        }
    }
}
