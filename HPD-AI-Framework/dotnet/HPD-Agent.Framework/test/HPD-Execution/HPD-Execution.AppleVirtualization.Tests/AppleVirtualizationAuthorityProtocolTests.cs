namespace HPD.Execution.AppleVirtualization.Tests;

using System.Text.Json;
using FluentAssertions;
using HPD.Execution.AppleVirtualization.Authority;
using HPD.Execution.AppleVirtualization.GuestAgent;
using HPD.Execution.AppleVirtualization.Protocol;
using HPD.Execution.Contracts;
using Xunit;

public sealed class AppleVirtualizationAuthorityProtocolTests
{
    [Fact]
    public void Authority_helper_dtos_round_trip_through_source_generated_json()
    {
        AppleVirtualizationHelperEnvelope envelope = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.AuthorityBind,
            "authority-helper-1",
            1,
            AppleVirtualizationHelperProtocol.AuthorityBindingRequestSchema) with
        {
            AuthorityBindingRequest = EngineSocketAuthorityRequest("binding-engine"),
        };

        byte[] json = AppleVirtualizationHelperJsonCodec.Encode(envelope);
        AppleVirtualizationHelperEnvelope roundTrip = AppleVirtualizationHelperJsonCodec.Decode(json);

        roundTrip.Operation.Should().Be(AppleVirtualizationHelperOperation.AuthorityBind);
        roundTrip.AuthorityBindingRequest.Should().NotBeNull();
        roundTrip.AuthorityBindingRequest!.BindingId.Should().Be("binding-engine");
        roundTrip.AuthorityBindingRequest.Source.SensitiveEndpointKind.Should().Be(SensitiveEndpointKind.EngineSocket);
        roundTrip.AuthorityBindingRequest.EffectiveAuthorityClass.Should().Be(SensitiveAuthorityClass.RootlessEngineControl);
        roundTrip.AuthorityBindingRequest.Redaction.Should().Be(SensitiveRedactionLevel.RedactSecretValues);
    }

    [Fact]
    public void Guest_agent_authority_dtos_round_trip_through_source_generated_json()
    {
        AppleVirtualizationGuestAgentEnvelope envelope = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.AuthorityBind,
            "authority-guest-1",
            2,
            AppleVirtualizationGuestAgentProtocol.AuthoritySchema) with
        {
            AuthorityProjectionRequest = new AppleVirtualizationGuestAgentAuthorityProjectionRequest
            {
                BindingId = "binding-credential",
                Source = new AppleVirtualizationGuestAgentAuthoritySource
                {
                    Kind = AuthoritySourceKind.Credential,
                    Credential = new CredentialRef("credential-ref-only"),
                    SensitiveEndpointKind = SensitiveEndpointKind.CredentialProxy,
                    AuthorityClass = SensitiveAuthorityClass.CredentialUseViaHostFunction,
                    RedactedDisplayName = "credential:***",
                },
                Target = new AppleVirtualizationGuestAgentAuthorityTarget
                {
                    Kind = AuthorityTargetKind.ExecutionUnit,
                    UnitId = "unit-1",
                },
                Projection = new AppleVirtualizationGuestAgentAuthorityProjection
                {
                    Kind = AuthorityProjectionKind.EnvironmentReference,
                    EnvironmentVariableName = "HPD_CREDENTIAL_REF",
                },
                EffectiveAuthorityClass = SensitiveAuthorityClass.CredentialUseViaHostFunction,
                AuditCorrelationId = "audit-credential",
            },
        };

        AppleVirtualizationGuestAgentEnvelope roundTrip = RoundTrip(envelope);

        roundTrip.Operation.Should().Be(AppleVirtualizationGuestAgentOperation.AuthorityBind);
        roundTrip.AuthorityProjectionRequest!.BindingId.Should().Be("binding-credential");
        roundTrip.AuthorityProjectionRequest.Source.Credential.Should().Be(new CredentialRef("credential-ref-only"));
        roundTrip.AuthorityProjectionRequest.Source.SensitiveEndpointKind.Should().Be(SensitiveEndpointKind.CredentialProxy);
        roundTrip.AuthorityProjectionRequest.Projection.EnvironmentVariableName.Should().Be("HPD_CREDENTIAL_REF");
    }

    [Fact]
    public async Task Fake_helper_routes_authority_bind_status_and_revoke()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        AppleVirtualizationAuthorityBindingRequest request = EngineSocketAuthorityRequest("binding-route");

        AppleVirtualizationHelperEnvelope bind = await helper.SendAsync(AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.AuthorityBind,
            "authority-bind",
            3,
            AppleVirtualizationHelperProtocol.AuthorityBindingRequestSchema) with
        {
            AuthorityBindingRequest = request,
        });
        AppleVirtualizationHelperEnvelope status = await helper.SendAsync(AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.AuthorityStatus,
            "authority-status",
            4,
            AppleVirtualizationHelperProtocol.AuthorityBindingRequestSchema) with
        {
            AuthorityBindingRequest = request with { Action = AppleVirtualizationAuthorityBindingAction.Status },
        });
        AppleVirtualizationHelperEnvelope revoke = await helper.SendAsync(AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.AuthorityRevoke,
            "authority-revoke",
            5,
            AppleVirtualizationHelperProtocol.AuthorityBindingRequestSchema) with
        {
            AuthorityBindingRequest = request with { Action = AppleVirtualizationAuthorityBindingAction.Revoke },
        });

        bind.AuthorityBindingResponse!.BindingPhase.Should().Be(AuthorityBindingPhase.Projected);
        bind.AuthorityBindingResponse.BoundAuthority!.AuditCorrelationId.Should().Be("audit-binding-route");
        status.AuthorityBindingResponse!.BindingPhase.Should().Be(AuthorityBindingPhase.Projected);
        revoke.AuthorityBindingResponse!.BindingPhase.Should().Be(AuthorityBindingPhase.Revoked);
        revoke.AuthorityBindingResponse.RevocationStatus.Should().Be(RevocationVerificationStatus.Verified);
    }

    [Fact]
    public async Task Fake_guest_agent_reports_bound_authority_with_lease_and_audit_correlation()
    {
        var toolharness = new FakeAppleVirtualizationGuestAgentToolHarness();
        DateTimeOffset boundAt = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = boundAt.AddMinutes(5);
        var request = AppleVirtualizationGuestAgentEnvelope.Request(
            AppleVirtualizationGuestAgentOperation.AuthorityBind,
            "guest-authority-bind",
            6,
            AppleVirtualizationGuestAgentProtocol.AuthoritySchema) with
        {
            AuthorityProjectionRequest = new AppleVirtualizationGuestAgentAuthorityProjectionRequest
            {
                BindingId = "binding-ssh-agent",
                Source = new AppleVirtualizationGuestAgentAuthoritySource
                {
                    Kind = AuthoritySourceKind.HostService,
                    HostService = HostServiceKind.SshAgent,
                    SensitiveEndpointKind = SensitiveEndpointKind.SshAgent,
                    AuthorityClass = SensitiveAuthorityClass.CredentialDelegation,
                    RedactedDisplayName = "ssh-agent:***",
                },
                Target = new AppleVirtualizationGuestAgentAuthorityTarget
                {
                    Kind = AuthorityTargetKind.ExecutionUnit,
                    UnitId = "unit-ssh",
                },
                Projection = new AppleVirtualizationGuestAgentAuthorityProjection
                {
                    Kind = AuthorityProjectionKind.SocketPath,
                    TargetSocketPath = new UnixSocketPath("/run/hpd/authority/ssh-agent.sock"),
                },
                EffectiveAuthorityClass = SensitiveAuthorityClass.CredentialDelegation,
                AuditCorrelationId = "audit-ssh",
                Lease = new AppleVirtualizationGuestAgentAuthorityLease
                {
                    BoundAt = boundAt,
                    ExpiresAt = expiresAt,
                    Lifetime = BindingLifetime.ExecutionUnit,
                    RevokeOnTargetStop = true,
                },
            },
        };

        AppleVirtualizationGuestAgentEnvelope response = await toolharness.SendAsync(request);

        response.AuthorityStatus.Should().NotBeNull();
        response.AuthorityStatus!.BindingPhase.Should().Be(AuthorityBindingPhase.Projected);
        response.AuthorityStatus.BoundAuthority!.AuditCorrelationId.Should().Be("audit-ssh");
        response.AuthorityStatus.BoundAuthority.ExpiresAt.Should().Be(expiresAt);
        response.AuthorityStatus.AuditEvents.Should().ContainSingle(audit => audit.CorrelationId == "audit-ssh");
    }

    [Fact]
    public void Redaction_shape_prevents_secret_values_in_serialized_authority_diagnostics()
    {
        const string secret = "super-secret-token";
        AppleVirtualizationHelperEnvelope envelope = AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.AuthorityBind,
            "authority-redaction",
            7,
            AppleVirtualizationHelperProtocol.AuthorityBindingRequestSchema) with
        {
            AuthorityBindingRequest = new AppleVirtualizationAuthorityBindingRequest
            {
                BindingId = "binding-redacted",
                Source = new AppleVirtualizationAuthoritySourceDescriptor
                {
                    Kind = AuthoritySourceKind.Credential,
                    Credential = new CredentialRef("credential-ref"),
                    SensitiveEndpointKind = SensitiveEndpointKind.CredentialProxy,
                    AuthorityClass = SensitiveAuthorityClass.CredentialUseViaHostFunction,
                    RedactedDisplayName = "credential:***",
                },
                Target = new AppleVirtualizationAuthorityTargetDescriptor { Kind = AuthorityTargetKind.ExecutionUnit, UnitId = "unit-1" },
                Projection = new AppleVirtualizationAuthorityProjectionDescriptor { Kind = AuthorityProjectionKind.EnvironmentReference, EnvironmentVariableName = "HPD_CREDENTIAL_REF" },
                Redaction = SensitiveRedactionLevel.RedactSecretValues,
                AuditLabel = "credential access without value",
            },
        };

        string json = JsonSerializer.Serialize(envelope, AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);

        json.Should().NotContain(secret);
        json.Should().Contain("credential:***");
    }

    [Fact]
    public void Sensitive_authority_dto_shape_distinguishes_engine_socket_and_credential_proxy()
    {
        AppleVirtualizationAuthorityBindingRequest engine = EngineSocketAuthorityRequest("binding-engine-sensitive");
        var credential = new AppleVirtualizationAuthorityBindingRequest
        {
            BindingId = "binding-credential-sensitive",
            Source = new AppleVirtualizationAuthoritySourceDescriptor
            {
                Kind = AuthoritySourceKind.Credential,
                Credential = new CredentialRef("credential-ref"),
                SensitiveEndpointKind = SensitiveEndpointKind.CredentialProxy,
                AuthorityClass = SensitiveAuthorityClass.CredentialUseViaHostFunction,
            },
            Target = new AppleVirtualizationAuthorityTargetDescriptor { Kind = AuthorityTargetKind.ExecutionUnit, UnitId = "unit-1" },
            Projection = new AppleVirtualizationAuthorityProjectionDescriptor { Kind = AuthorityProjectionKind.EnvironmentReference, EnvironmentVariableName = "HPD_CREDENTIAL_REF" },
            EffectiveAuthorityClass = SensitiveAuthorityClass.CredentialUseViaHostFunction,
        };

        engine.Source.SensitiveEndpointKind.Should().Be(SensitiveEndpointKind.EngineSocket);
        engine.EffectiveAuthorityClass.Should().Be(SensitiveAuthorityClass.RootlessEngineControl);
        credential.Source.SensitiveEndpointKind.Should().Be(SensitiveEndpointKind.CredentialProxy);
        credential.EffectiveAuthorityClass.Should().Be(SensitiveAuthorityClass.CredentialUseViaHostFunction);
    }

    [Theory]
    [InlineData(HostServiceKind.DockerDaemon, "/var/run/docker.sock")]
    [InlineData(HostServiceKind.PodmanDaemon, "/run/user/501/podman/podman.sock")]
    [InlineData(HostServiceKind.ContainerdDaemon, "/run/containerd/containerd.sock")]
    public void Host_engine_services_are_rejected_as_host_passthrough(
        HostServiceKind service,
        string socketPath)
    {
        AppleVirtualizationAuthoritySourceClassification classification = AppleVirtualizationAuthoritySourceClassifier.Classify(
            new AppleVirtualizationAuthoritySourceDescriptor
            {
                Kind = AuthoritySourceKind.HostService,
                HostService = service,
                SocketPath = new UnixSocketPath(socketPath),
                Locus = BoundaryLocus.Host,
            });

        classification.IsSupported.Should().BeFalse();
        classification.SensitiveEndpointKind.Should().Be(SensitiveEndpointKind.EngineSocket);
        classification.DiagnosticCode.Should().Be("AppleVirtualization.AuthorityHostEngineSocketPassthroughRejected");
        classification.DiagnosticMessage.Should().Contain("Host engine socket passthrough is rejected");
    }

    [Theory]
    [InlineData("/run/user/501/docker.sock", SensitiveAuthorityClass.RootlessEngineControl)]
    [InlineData("/run/user/501/podman/podman.sock", SensitiveAuthorityClass.RootlessEngineControl)]
    [InlineData("/run/containerd/containerd.sock", SensitiveAuthorityClass.RootfulEngineControl)]
    public void Runtime_host_engine_unix_sockets_classify_as_engine_control_not_ordinary_endpoints(
        string socketPath,
        SensitiveAuthorityClass expectedAuthorityClass)
    {
        AppleVirtualizationAuthoritySourceClassification classification = AppleVirtualizationAuthoritySourceClassifier.Classify(
            new AppleVirtualizationAuthoritySourceDescriptor
            {
                Kind = AuthoritySourceKind.UnixSocket,
                SocketPath = new UnixSocketPath(socketPath),
                Locus = BoundaryLocus.RuntimeHost,
            });

        classification.IsSupported.Should().BeTrue();
        classification.SensitiveEndpointKind.Should().Be(SensitiveEndpointKind.EngineSocket);
        classification.AuthorityClass.Should().Be(expectedAuthorityClass);
        classification.Redaction.Should().Be(SensitiveRedactionLevel.RedactIdentifiers);
    }

    [Fact]
    public void Ssh_agent_classifies_as_credential_delegation()
    {
        AppleVirtualizationAuthoritySourceClassification classification = AppleVirtualizationAuthoritySourceClassifier.Classify(
            new AppleVirtualizationAuthoritySourceDescriptor
            {
                Kind = AuthoritySourceKind.HostService,
                HostService = HostServiceKind.SshAgent,
                SocketPath = new UnixSocketPath("/private/tmp/ssh-agent.sock"),
            });

        classification.IsSupported.Should().BeTrue();
        classification.SensitiveEndpointKind.Should().Be(SensitiveEndpointKind.SshAgent);
        classification.AuthorityClass.Should().Be(SensitiveAuthorityClass.CredentialDelegation);
        classification.RedactedDisplayName.Should().Be("ssh-agent:***");
    }

    [Fact]
    public void Credential_source_classifies_as_credential_use_without_secret_values()
    {
        const string secret = "secret-value-that-must-not-appear";
        AppleVirtualizationAuthoritySourceClassification classification = AppleVirtualizationAuthoritySourceClassifier.Classify(
            new AppleVirtualizationAuthoritySourceDescriptor
            {
                Kind = AuthoritySourceKind.Credential,
                Credential = new CredentialRef("credential-ref-only"),
                RedactedDisplayName = "credential:***",
            });

        string serialized = JsonSerializer.Serialize(classification);

        classification.IsSupported.Should().BeTrue();
        classification.SensitiveEndpointKind.Should().Be(SensitiveEndpointKind.CredentialProxy);
        classification.AuthorityClass.Should().Be(SensitiveAuthorityClass.CredentialUseViaHostFunction);
        serialized.Should().NotContain(secret);
        serialized.Should().NotContain("credential-ref-only");
        serialized.Should().Contain("credential:***");
    }

    [Fact]
    public void Trust_service_classifies_as_trust_mutation()
    {
        AppleVirtualizationAuthoritySourceClassification classification = AppleVirtualizationAuthoritySourceClassifier.Classify(
            new AppleVirtualizationAuthoritySourceDescriptor
            {
                Kind = AuthoritySourceKind.HostService,
                HostService = HostServiceKind.TlsTrustService,
            });

        classification.IsSupported.Should().BeTrue();
        classification.SensitiveEndpointKind.Should().Be(SensitiveEndpointKind.TrustService);
        classification.AuthorityClass.Should().Be(SensitiveAuthorityClass.TrustMutation);
    }

    [Fact]
    public void Host_daemon_control_socket_classifies_as_privileged_daemon_control()
    {
        AppleVirtualizationAuthoritySourceClassification classification = AppleVirtualizationAuthoritySourceClassifier.Classify(
            new AppleVirtualizationAuthoritySourceDescriptor
            {
                Kind = AuthoritySourceKind.UnixSocket,
                SocketPath = new UnixSocketPath("/var/run/host-daemon.sock"),
            });

        classification.IsSupported.Should().BeTrue();
        classification.SensitiveEndpointKind.Should().Be(SensitiveEndpointKind.HostDaemonControl);
        classification.AuthorityClass.Should().Be(SensitiveAuthorityClass.PrivilegedDaemonControl);
    }

    [Fact]
    public void Host_function_classifies_as_host_callback_authority()
    {
        AppleVirtualizationAuthoritySourceClassification classification = AppleVirtualizationAuthoritySourceClassifier.Classify(
            new AppleVirtualizationAuthoritySourceDescriptor
            {
                Kind = AuthoritySourceKind.HostFunction,
            });

        classification.IsSupported.Should().BeTrue();
        classification.SensitiveEndpointKind.Should().Be(SensitiveEndpointKind.FunctionDebug);
        classification.AuthorityClass.Should().Be(SensitiveAuthorityClass.HostFunctionCallback);
    }

    [Fact]
    public void Unknown_provider_defined_source_is_rejected_without_bounded_metadata()
    {
        AppleVirtualizationAuthoritySourceClassification classification = AppleVirtualizationAuthoritySourceClassifier.Classify(
            new AppleVirtualizationAuthoritySourceDescriptor
            {
                Kind = AuthoritySourceKind.ProviderDefined,
            });

        classification.IsSupported.Should().BeFalse();
        classification.DiagnosticCode.Should().Be("AppleVirtualization.AuthorityProviderDefinedMissingMetadata");
        classification.AuthorityClass.Should().Be(SensitiveAuthorityClass.ProviderDefined);
    }

    [Fact]
    public void Provider_defined_source_is_accepted_only_with_bounded_metadata()
    {
        ProviderExtensionData extension = new(
            new ProviderId("apple-virtualization"),
            new SchemaId("hpd.execution.apple-virtualization.authority.classification.provider-defined.v1"),
            new ContentType("application/json"),
            new byte[] { 1, 2, 3 });

        AppleVirtualizationAuthoritySourceClassification classification = AppleVirtualizationAuthoritySourceClassifier.Classify(
            new AppleVirtualizationAuthoritySourceDescriptor
            {
                Kind = AuthoritySourceKind.ProviderDefined,
            },
            [extension]);

        classification.IsSupported.Should().BeTrue();
        classification.SensitiveEndpointKind.Should().Be(SensitiveEndpointKind.ProviderDefined);
        classification.AuthorityClass.Should().Be(SensitiveAuthorityClass.ProviderDefined);
        classification.Redaction.Should().Be(SensitiveRedactionLevel.RedactIdentifiers);
    }

    [Fact]
    public void Authority_evidence_reader_ignores_unrelated_extensions_and_reads_authority_evidence()
    {
        var evidence = new AppleVirtualizationAuthorityEvidenceExtension
        {
            BindingId = "authority-1",
            BindingPhase = AuthorityBindingPhase.Degraded,
            RevocationStatus = RevocationVerificationStatus.Pending,
            Conditions =
            [
                new Condition(
                    "AppleVirtualization.GuestAgentAuthorityTargetMissing",
                    ConditionStatus.True,
                    "TargetMissing",
                    "Projected authority socket path is missing.",
                    DateTimeOffset.UtcNow,
                    new ResourceGeneration(1),
                    DiagnosticSeverity.Warning),
            ],
            RevocationEvidence =
            [
                new AppleVirtualizationAuthorityRevocationEvidence
                {
                    Kind = AppleVirtualizationAuthorityRevocationEvidenceKind.GuestSocketAbsent,
                    Observed = true,
                    GuestSocketPath = new UnixSocketPath("/run/hpd/engine/docker.sock"),
                    ObservedAt = DateTimeOffset.UtcNow,
                },
            ],
        };
        ProviderExtensionData unrelated = new(
            AppleVirtualizationProviderDescriptor.ProviderId,
            new SchemaId("hpd.execution.apple-virtualization.unrelated.v1"),
            new ContentType("application/json"),
            new byte[] { 1, 2, 3 });
        ProviderExtensionData extension = new(
            AppleVirtualizationProviderDescriptor.ProviderId,
            AppleVirtualizationAuthorityBindingProvider.AuthorityEvidenceExtensionSchema,
            new ContentType("application/json"),
            JsonSerializer.SerializeToUtf8Bytes(
                evidence,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationAuthorityEvidenceExtension));

        bool read = AppleVirtualizationAuthorityEvidenceReader.TryRead(
            [unrelated, extension],
            out AppleVirtualizationAuthorityEvidenceExtension? roundTrip);

        read.Should().BeTrue();
        roundTrip.BindingId.Should().Be("authority-1");
        roundTrip.Conditions.Should().ContainSingle(condition =>
            condition.Type == "AppleVirtualization.GuestAgentAuthorityTargetMissing");
        roundTrip.RevocationEvidence.Should().ContainSingle(item =>
            item.Kind == AppleVirtualizationAuthorityRevocationEvidenceKind.GuestSocketAbsent);
    }

    private static AppleVirtualizationAuthorityBindingRequest EngineSocketAuthorityRequest(string bindingId)
    {
        DateTimeOffset boundAt = DateTimeOffset.UtcNow;
        return new AppleVirtualizationAuthorityBindingRequest
        {
            BindingId = bindingId,
            Source = new AppleVirtualizationAuthoritySourceDescriptor
            {
                Kind = AuthoritySourceKind.HostService,
                HostService = HostServiceKind.DockerDaemon,
                SocketPath = new UnixSocketPath("/run/user/501/docker.sock"),
                SensitiveEndpointKind = SensitiveEndpointKind.EngineSocket,
                AuthorityClass = SensitiveAuthorityClass.RootlessEngineControl,
                RedactedDisplayName = "engine-socket:***",
            },
            Target = new AppleVirtualizationAuthorityTargetDescriptor
            {
                Kind = AuthorityTargetKind.ExecutionUnit,
                UnitId = "unit-engine",
            },
            Projection = new AppleVirtualizationAuthorityProjectionDescriptor
            {
                Kind = AuthorityProjectionKind.SocketPath,
                TargetSocketPath = new UnixSocketPath("/run/hpd/authority/docker.sock"),
                SocketPermissions = new UnixSocketPermissions(0x180),
            },
            EffectiveAuthorityClass = SensitiveAuthorityClass.RootlessEngineControl,
            Redaction = SensitiveRedactionLevel.RedactSecretValues,
            AuditCorrelationId = "audit-" + bindingId,
            Lease = new AppleVirtualizationAuthorityLeaseDescriptor
            {
                Lifetime = BindingLifetime.ExecutionUnit,
                BoundAt = boundAt,
                ExpiresAt = boundAt.AddMinutes(10),
                RevokeOnTargetStop = true,
            },
        };
    }

    private static AppleVirtualizationGuestAgentEnvelope RoundTrip(AppleVirtualizationGuestAgentEnvelope envelope)
    {
        byte[] json = AppleVirtualizationGuestAgentJsonCodec.Encode(envelope);
        return AppleVirtualizationGuestAgentJsonCodec.Decode(json);
    }
}
