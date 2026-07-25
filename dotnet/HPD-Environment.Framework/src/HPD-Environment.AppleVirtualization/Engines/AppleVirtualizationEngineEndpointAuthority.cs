namespace HPD.Environment.AppleVirtualization.Engines;

using HPD.Environment.Contracts;

internal static class AppleVirtualizationEngineEndpointAuthority
{
    public static bool TryCreateBindingSpec(
        EngineControlPlaneStatus engine,
        EngineApiKind api,
        TargetHandle<ExecutionUnit> targetUnit,
        UnixSocketPath targetSocketPath,
        SensitiveProvenance? provenance,
        out AuthorityBindingSpec? spec,
        out Diagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(engine);

        if (engine.Phase != ResourcePhase.Ready || engine.EnginePhase != EngineControlPlanePhase.Ready)
        {
            spec = null;
            diagnostic = Diagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.EngineAuthorityEndpointNotReady",
                "Engine API authority binding requires a ready EngineControlPlane.",
                "engine.endpoint.authority");
            return false;
        }

        EngineApiEndpointStatus? endpoint = null;
        IReadOnlyList<EngineApiEndpointStatus> endpoints = engine.Endpoints;
        for (int i = 0; i < endpoints.Count; i++)
        {
            if (endpoints[i].Api == api)
            {
                endpoint = endpoints[i];
                break;
            }
        }

        if (endpoint is null)
        {
            spec = null;
            diagnostic = Diagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.EngineAuthorityEndpointMissing",
                "The requested engine API endpoint was not reported by the guest engine control plane.",
                "engine.endpoint.authority");
            return false;
        }

        SensitiveEndpointPolicy? sensitive = endpoint.SensitivePolicy;
        if (sensitive is null ||
            sensitive.Kind != SensitiveEndpointKind.EngineSocket ||
            endpoint.Endpoint.Sensitivity != EndpointSensitivity.Sensitive)
        {
            spec = null;
            diagnostic = Diagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.EngineAuthorityEndpointNotSensitive",
                "Engine API endpoints must be represented as sensitive engine socket endpoints before binding.",
                "engine.endpoint.sensitive");
            return false;
        }

        ProviderEndpoint providerEndpoint = endpoint.Endpoint.Endpoint;
        if (!string.Equals(providerEndpoint.Address, "guest", StringComparison.OrdinalIgnoreCase))
        {
            spec = null;
            diagnostic = Diagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.EngineAuthorityEndpointHostLocusRejected",
                "Engine API authority binding only accepts guest-visible runtime-host socket endpoints; provider or host-locus engine sockets fail closed.",
                "engine.endpoint.locus");
            return false;
        }

        if (!string.Equals(providerEndpoint.Scheme, "unix", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(providerEndpoint.Path))
        {
            spec = null;
            diagnostic = Diagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.EngineAuthorityEndpointSocketMissing",
                "Engine API authority binding requires a guest-visible Unix socket path.",
                "engine.endpoint.socketPath");
            return false;
        }

        if (providerEndpoint.Path[0] != '/')
        {
            spec = null;
            diagnostic = Diagnostic(
                DiagnosticSeverity.Error,
                "AppleVirtualization.EngineAuthorityEndpointSocketRelative",
                "Engine API authority binding requires an absolute guest-visible Unix socket path.",
                "engine.endpoint.socketPath");
            return false;
        }

        spec = new AuthorityBindingSpec
        {
            Kind = AuthorityBindingKind.HostService,
            Source = new AuthorityBindingSource
            {
                Kind = AuthoritySourceKind.UnixSocket,
                Locus = BoundaryLocus.RuntimeHost,
                SocketPath = new UnixSocketPath(providerEndpoint.Path),
            },
            Target = new AuthorityBindingTarget(
                AuthorityTargetKind.ExecutionUnit,
                Unit: targetUnit,
                Process: null,
                FunctionSandbox: null,
                ServiceName: null,
                Locus: BoundaryLocus.ExecutionUnit),
            Projection = new AuthorityBindingProjection
            {
                Kind = AuthorityProjectionKind.SocketPath,
                TargetSocketPath = targetSocketPath,
                ReadOnly = false,
            },
            Policy = new AuthorityBindingPolicy
            {
                Direction = AuthorityBindingDirection.ProviderToGuest,
                AuthorityClass = sensitive.AuthorityClass,
                EffectiveAuthorityClass = sensitive.AuthorityClass,
                Lease = sensitive.Lease ?? new SensitiveLeasePolicy
                {
                    Lifetime = BindingLifetime.ExecutionUnit,
                    RevokeOnTargetStop = true,
                },
                Redaction = sensitive.Redaction,
                RequireAudit = sensitive.RequireAudit,
                AllowProviderSideProxy = true,
                RequireExplicitUserApproval = sensitive.RequireExplicitUserApproval,
                Provenance = provenance,
            },
            AuditLabel = "engine-api:" + api,
        };
        diagnostic = null;
        return true;
    }

    private static Diagnostic Diagnostic(
        DiagnosticSeverity severity,
        string code,
        string message,
        string targetPath) =>
        new()
        {
            Severity = severity,
            Code = new DiagnosticCode(code),
            Message = message,
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            TargetPath = targetPath,
        };
}
