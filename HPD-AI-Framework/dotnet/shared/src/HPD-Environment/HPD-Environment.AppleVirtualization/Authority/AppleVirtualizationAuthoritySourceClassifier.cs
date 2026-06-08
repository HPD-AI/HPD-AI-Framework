namespace HPD.Environment.AppleVirtualization.Authority;

using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.Contracts;

internal static class AppleVirtualizationAuthoritySourceClassifier
{
    private const int MaxProviderExtensionCount = 8;
    private const int MaxProviderExtensionPayloadBytes = 4096;

    public static AppleVirtualizationAuthoritySourceClassification Classify(AuthorityBindingSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        return Classify(
            new AppleVirtualizationAuthoritySourceDescriptor
            {
                Kind = spec.Source.Kind,
                Locus = spec.Source.Locus,
                HostService = spec.Source.HostService,
                SocketPath = spec.Source.SocketPath,
                Credential = spec.Source.Credential,
                ProviderCapabilityName = spec.Source.ProviderCapabilityName,
            },
            spec.ProviderExtensions,
            spec.Policy.Redaction);
    }

    public static AppleVirtualizationAuthoritySourceClassification Classify(
        AppleVirtualizationAuthoritySourceDescriptor source,
        IReadOnlyList<ProviderExtensionData>? providerExtensions = null,
        SensitiveRedactionLevel requestedRedaction = SensitiveRedactionLevel.RedactSecretValues)
    {
        ArgumentNullException.ThrowIfNull(source);

        SensitiveRedactionLevel redaction = DefaultRedaction(source.Kind, requestedRedaction);

        return source.Kind switch
        {
            AuthoritySourceKind.HostService => ClassifyHostService(source, redaction),
            AuthoritySourceKind.UnixSocket => ClassifyUnixSocket(source, redaction),
            AuthoritySourceKind.Credential => Supported(
                source,
                SensitiveEndpointKind.CredentialProxy,
                SensitiveAuthorityClass.CredentialUseViaHostFunction,
                redaction,
                "Credential references are authority-bearing handles; only the credential reference may be serialized."),
            AuthoritySourceKind.Secret => Supported(
                source,
                SensitiveEndpointKind.CredentialProxy,
                SensitiveAuthorityClass.CredentialDelegation,
                SensitiveRedactionLevel.RedactSecretValues,
                "Secret sources are credential delegation surfaces and must not serialize secret values."),
            AuthoritySourceKind.TrustAnchor => Supported(
                source,
                SensitiveEndpointKind.TrustService,
                SensitiveAuthorityClass.TrustMutation,
                redaction,
                "Trust anchors can mutate guest trust state and require explicit authority binding."),
            AuthoritySourceKind.PublishedEndpoint => Supported(
                source,
                SensitiveEndpointKind.ProviderDefined,
                SensitiveAuthorityClass.NetworkViaHostFunction,
                SensitiveRedactionLevel.RedactIdentifiers,
                "Published endpoint authority can expose a mediated network capability and must remain distinct from ordinary endpoint publication."),
            AuthoritySourceKind.HostFunction => Supported(
                source,
                SensitiveEndpointKind.FunctionDebug,
                SensitiveAuthorityClass.HostFunctionCallback,
                redaction,
                "Host functions are callbacks from the guest into host code and are authority-bearing."),
            AuthoritySourceKind.ProviderCapability => ClassifyProviderCapability(source, providerExtensions, redaction),
            AuthoritySourceKind.ProviderDefined => ClassifyProviderDefined(source, providerExtensions, redaction),
            _ => Unsupported(
                source,
                redaction,
                "AppleVirtualization.AuthoritySourceUnsupported",
                "Authority source kind is not supported by the Apple Virtualization provider."),
        };
    }

    private static AppleVirtualizationAuthoritySourceClassification ClassifyHostService(
        AppleVirtualizationAuthoritySourceDescriptor source,
        SensitiveRedactionLevel redaction)
    {
        return source.HostService switch
        {
            HostServiceKind.SshAgent => Supported(
                source,
                SensitiveEndpointKind.SshAgent,
                SensitiveAuthorityClass.CredentialDelegation,
                redaction,
                "SSH agent socket access delegates credential use and requires authority binding."),
            HostServiceKind.DockerDaemon => Engine(source, "Docker daemon socket access controls container workloads."),
            HostServiceKind.PodmanDaemon => Engine(source, "Podman daemon socket access controls container workloads."),
            HostServiceKind.ContainerdDaemon => Engine(source, "containerd socket access controls container workloads and image state."),
            HostServiceKind.BuildKitDaemon => Engine(source, "BuildKit socket access controls build execution and build cache state."),
            HostServiceKind.KubernetesApi => Supported(
                source,
                SensitiveEndpointKind.EngineSocket,
                SensitiveAuthorityClass.RootfulEngineControl,
                redaction,
                "Kubernetes API access controls workload state and must not be exposed as an ordinary endpoint."),
            HostServiceKind.GitCredentialHelper => Supported(
                source,
                SensitiveEndpointKind.CredentialProxy,
                SensitiveAuthorityClass.CredentialDelegation,
                redaction,
                "Git credential helper access delegates credential retrieval."),
            HostServiceKind.TlsTrustService or HostServiceKind.TrustAnchorStore => Supported(
                source,
                SensitiveEndpointKind.TrustService,
                SensitiveAuthorityClass.TrustMutation,
                redaction,
                "TLS trust services and trust stores can mutate guest trust state."),
            HostServiceKind.HttpProxy or HostServiceKind.SocksProxy or HostServiceKind.HostResolver => Supported(
                source,
                SensitiveEndpointKind.ProviderDefined,
                SensitiveAuthorityClass.NetworkViaHostFunction,
                SensitiveRedactionLevel.RedactIdentifiers,
                "Host network mediation can route guest traffic through host policy."),
            HostServiceKind.DisplayServer => Supported(
                source,
                SensitiveEndpointKind.ProviderDefined,
                SensitiveAuthorityClass.HostStateRead,
                SensitiveRedactionLevel.RedactIdentifiers,
                "Display server projection exposes host session state."),
            HostServiceKind.ProviderDefined => Unsupported(
                source,
                redaction,
                "AppleVirtualization.AuthorityHostServiceProviderDefined",
                "Provider-defined host services require explicit bounded provider extension metadata before classification."),
            null => Unsupported(
                source,
                redaction,
                "AppleVirtualization.AuthorityHostServiceMissing",
                "HostService authority sources must specify the host service kind."),
            _ => Unsupported(
                source,
                redaction,
                "AppleVirtualization.AuthorityHostServiceUnsupported",
                "Host service authority source is not supported by the Apple Virtualization provider."),
        };
    }

    private static AppleVirtualizationAuthoritySourceClassification ClassifyUnixSocket(
        AppleVirtualizationAuthoritySourceDescriptor source,
        SensitiveRedactionLevel redaction)
    {
        if (source.SocketPath is null)
        {
            return Unsupported(
                source,
                redaction,
                "AppleVirtualization.AuthorityUnixSocketMissingPath",
                "UnixSocket authority sources must specify a socket path.");
        }

        string path = source.SocketPath.Value.Value;
        if (ContainsAny(path, "docker.sock", ".docker/run/docker.sock"))
        {
            return Engine(source, "Docker socket access controls container workloads.");
        }

        if (ContainsAny(path, "podman.sock", "podman/podman.sock"))
        {
            return Engine(source, "Podman socket access controls container workloads.");
        }

        if (ContainsAny(path, "containerd.sock", "containerd/containerd.sock", "buildkit", "buildkitd.sock", "buildkit.sock"))
        {
            return Engine(source, "containerd or BuildKit socket access controls workload, image, or build state.");
        }

        if (ContainsAny(path, "ssh-agent", "ssh_auth_sock", "ssh-auth.sock", "/agent."))
        {
            return Supported(
                source,
                SensitiveEndpointKind.SshAgent,
                SensitiveAuthorityClass.CredentialDelegation,
                redaction,
                "SSH agent Unix sockets delegate credential use.");
        }

        if (ContainsAny(path, "credential", "git-credential", "keychain"))
        {
            return Supported(
                source,
                SensitiveEndpointKind.CredentialProxy,
                SensitiveAuthorityClass.CredentialDelegation,
                redaction,
                "Credential Unix sockets delegate credential retrieval or signing.");
        }

        if (ContainsAny(path, "trust", "cert", "ca-cert", "keychain"))
        {
            return Supported(
                source,
                SensitiveEndpointKind.TrustService,
                SensitiveAuthorityClass.TrustMutation,
                redaction,
                "Trust-related Unix sockets can mutate or disclose trust state.");
        }

        if (ContainsAny(path, "dockerd", "launchd", "daemon", "xpc", "/var/run/"))
        {
            return Supported(
                source,
                SensitiveEndpointKind.HostDaemonControl,
                SensitiveAuthorityClass.PrivilegedDaemonControl,
                redaction,
                "Host daemon sockets can control privileged host or provider services.");
        }

        return Unsupported(
            source,
            redaction,
            "AppleVirtualization.AuthorityUnixSocketUnclassified",
            "Unix socket authority source is unclassified; explicit bounded provider metadata is required before projection.");
    }

    private static AppleVirtualizationAuthoritySourceClassification ClassifyProviderCapability(
        AppleVirtualizationAuthoritySourceDescriptor source,
        IReadOnlyList<ProviderExtensionData>? providerExtensions,
        SensitiveRedactionLevel redaction)
    {
        if (string.IsNullOrWhiteSpace(source.ProviderCapabilityName))
        {
            return Unsupported(
                source,
                redaction,
                "AppleVirtualization.AuthorityProviderCapabilityMissingName",
                "Provider capability authority sources must specify a capability name.");
        }

        if (!HasBoundedProviderMetadata(providerExtensions, out string? reason))
        {
            return Unsupported(
                source,
                redaction,
                "AppleVirtualization.AuthorityProviderCapabilityMissingMetadata",
                reason);
        }

        return Supported(
            source,
            SensitiveEndpointKind.ProviderDefined,
            SensitiveAuthorityClass.HpdResourceMutation,
            SensitiveRedactionLevel.RedactIdentifiers,
            "Provider capability authority was accepted only because bounded provider extension metadata was supplied.");
    }

    private static AppleVirtualizationAuthoritySourceClassification ClassifyProviderDefined(
        AppleVirtualizationAuthoritySourceDescriptor source,
        IReadOnlyList<ProviderExtensionData>? providerExtensions,
        SensitiveRedactionLevel redaction)
    {
        if (!HasBoundedProviderMetadata(providerExtensions, out string? reason))
        {
            return Unsupported(
                source,
                redaction,
                "AppleVirtualization.AuthorityProviderDefinedMissingMetadata",
                reason);
        }

        return Supported(
            source,
            SensitiveEndpointKind.ProviderDefined,
            SensitiveAuthorityClass.ProviderDefined,
            SensitiveRedactionLevel.RedactIdentifiers,
            "Provider-defined authority was accepted only because bounded provider extension metadata was supplied.");
    }

    private static AppleVirtualizationAuthoritySourceClassification Engine(
        AppleVirtualizationAuthoritySourceDescriptor source,
        string message)
    {
        if (source.Locus == BoundaryLocus.Host)
        {
            return new AppleVirtualizationAuthoritySourceClassification(
                IsSupported: false,
                SourceKind: source.Kind,
                SensitiveEndpointKind: SensitiveEndpointKind.EngineSocket,
                AuthorityClass: IsRootlessSocket(source.SocketPath) ? SensitiveAuthorityClass.RootlessEngineControl : SensitiveAuthorityClass.RootfulEngineControl,
                Redaction: SensitiveRedactionLevel.RedactIdentifiers,
                RedactedDisplayName: RedactedDisplayName(source, SensitiveEndpointKind.EngineSocket),
                DiagnosticCode: "AppleVirtualization.AuthorityHostEngineSocketPassthroughRejected",
                DiagnosticMessage: message + " Host engine socket passthrough is rejected; engine sockets must originate inside the Apple Virtualization VM boundary.");
        }

        return Supported(
            source,
            SensitiveEndpointKind.EngineSocket,
            IsRootlessSocket(source.SocketPath) ? SensitiveAuthorityClass.RootlessEngineControl : SensitiveAuthorityClass.RootfulEngineControl,
            SensitiveRedactionLevel.RedactIdentifiers,
            message + " It remains blocked from ordinary endpoint publication.");
    }

    private static AppleVirtualizationAuthoritySourceClassification Supported(
        AppleVirtualizationAuthoritySourceDescriptor source,
        SensitiveEndpointKind endpointKind,
        SensitiveAuthorityClass authorityClass,
        SensitiveRedactionLevel redaction,
        string message) =>
        new(
            IsSupported: true,
            SourceKind: source.Kind,
            SensitiveEndpointKind: endpointKind,
            AuthorityClass: authorityClass,
            Redaction: redaction,
            RedactedDisplayName: RedactedDisplayName(source, endpointKind),
            DiagnosticCode: "AppleVirtualization.AuthoritySourceClassified",
            DiagnosticMessage: message);

    private static AppleVirtualizationAuthoritySourceClassification Unsupported(
        AppleVirtualizationAuthoritySourceDescriptor source,
        SensitiveRedactionLevel redaction,
        string code,
        string message) =>
        new(
            IsSupported: false,
            SourceKind: source.Kind,
            SensitiveEndpointKind: source.SensitiveEndpointKind ?? SensitiveEndpointKind.ProviderDefined,
            AuthorityClass: source.AuthorityClass == SensitiveAuthorityClass.None ? SensitiveAuthorityClass.None : SensitiveAuthorityClass.ProviderDefined,
            Redaction: redaction,
            RedactedDisplayName: RedactedDisplayName(source, source.SensitiveEndpointKind ?? SensitiveEndpointKind.ProviderDefined),
            DiagnosticCode: code,
            DiagnosticMessage: message);

    private static bool HasBoundedProviderMetadata(IReadOnlyList<ProviderExtensionData>? extensions, out string reason)
    {
        if (extensions is null || extensions.Count == 0)
        {
            reason = "Provider-defined sensitive authority requires explicit provider extension metadata.";
            return false;
        }

        if (extensions.Count > MaxProviderExtensionCount)
        {
            reason = "Provider-defined sensitive authority metadata exceeded the bounded extension count.";
            return false;
        }

        long payloadBytes = 0;
        for (int index = 0; index < extensions.Count; index++)
        {
            payloadBytes += extensions[index].Payload.Length;
            if (payloadBytes > MaxProviderExtensionPayloadBytes)
            {
                reason = "Provider-defined sensitive authority metadata exceeded the bounded payload size.";
                return false;
            }
        }

        reason = "Provider-defined sensitive authority metadata was bounded.";
        return true;
    }

    private static SensitiveRedactionLevel DefaultRedaction(AuthoritySourceKind kind, SensitiveRedactionLevel requested)
    {
        if (requested != SensitiveRedactionLevel.None)
        {
            return requested;
        }

        return kind is AuthoritySourceKind.Credential or AuthoritySourceKind.Secret
            ? SensitiveRedactionLevel.RedactSecretValues
            : SensitiveRedactionLevel.RedactIdentifiers;
    }

    private static string RedactedDisplayName(AppleVirtualizationAuthoritySourceDescriptor source, SensitiveEndpointKind endpointKind)
    {
        if (!string.IsNullOrWhiteSpace(source.RedactedDisplayName))
        {
            return source.RedactedDisplayName;
        }

        return endpointKind switch
        {
            SensitiveEndpointKind.EngineSocket => "engine-socket:***",
            SensitiveEndpointKind.CredentialProxy => "credential:***",
            SensitiveEndpointKind.SshAgent => "ssh-agent:***",
            SensitiveEndpointKind.TrustService => "trust-service:***",
            SensitiveEndpointKind.HostDaemonControl => "host-daemon:***",
            SensitiveEndpointKind.FunctionDebug => "host-function:***",
            _ => source.Kind + ":***",
        };
    }

    private static bool IsRootlessSocket(UnixSocketPath? path)
    {
        if (path is null)
        {
            return false;
        }

        string value = path.Value.Value;
        return ContainsAny(value, "/run/user/", "/.docker/run/", "/containers/podman/");
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        for (int index = 0; index < needles.Length; index++)
        {
            if (value.Contains(needles[index], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

internal readonly record struct AppleVirtualizationAuthoritySourceClassification(
    bool IsSupported,
    AuthoritySourceKind SourceKind,
    SensitiveEndpointKind SensitiveEndpointKind,
    SensitiveAuthorityClass AuthorityClass,
    SensitiveRedactionLevel Redaction,
    string RedactedDisplayName,
    string DiagnosticCode,
    string DiagnosticMessage);
