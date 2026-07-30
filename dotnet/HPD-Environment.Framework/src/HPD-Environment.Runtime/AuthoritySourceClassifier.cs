#nullable enable

namespace HPD.Environment.Runtime;

using HPD.Environment.Contracts;

internal readonly record struct AuthoritySourceClassification(
    bool IsClassified,
    AuthoritySourceKind SourceKind,
    SensitiveEndpointKind EndpointKind,
    SensitiveAuthorityClass AuthorityClass,
    SensitiveRedactionLevel Redaction,
    string RedactedDisplayName,
    string DiagnosticCode,
    string DiagnosticMessage);

internal static class AuthoritySourceClassifier
{
    private const int MaxProviderExtensionCount = 8;
    private const int MaxProviderExtensionPayloadBytes = 4096;

    public static AuthoritySourceClassification Classify(
        AuthorityBindingSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        AuthorityBindingSource source = spec.Source;
        SensitiveRedactionLevel redaction = DefaultRedaction(
            source.Kind,
            spec.Policy.Redaction);

        return source.Kind switch
        {
            AuthoritySourceKind.HostService =>
                ClassifyHostService(source, redaction),
            AuthoritySourceKind.UnixSocket =>
                ClassifyUnixSocket(source, redaction),
            AuthoritySourceKind.Credential => Classified(
                source,
                SensitiveEndpointKind.CredentialProxy,
                SensitiveAuthorityClass.CredentialUseViaHostFunction,
                redaction,
                "Credential references delegate credential use; only the reference may cross the boundary."),
            AuthoritySourceKind.Secret => Classified(
                source,
                SensitiveEndpointKind.CredentialProxy,
                SensitiveAuthorityClass.CredentialDelegation,
                SensitiveRedactionLevel.RedactSecretValues,
                "Secret sources are credential-delegation surfaces and must not serialize secret values."),
            AuthoritySourceKind.TrustAnchor => Classified(
                source,
                SensitiveEndpointKind.TrustService,
                SensitiveAuthorityClass.TrustMutation,
                redaction,
                "Trust anchors can mutate trust state and require explicit authority."),
            AuthoritySourceKind.PublishedEndpoint => Classified(
                source,
                SensitiveEndpointKind.ProviderDefined,
                SensitiveAuthorityClass.NetworkViaHostFunction,
                SensitiveRedactionLevel.RedactIdentifiers,
                "Published endpoint authority exposes a mediated network capability."),
            AuthoritySourceKind.HostFunction => Classified(
                source,
                SensitiveEndpointKind.FunctionDebug,
                SensitiveAuthorityClass.HostFunctionCallback,
                redaction,
                "Host functions are authority-bearing callbacks into host code."),
            AuthoritySourceKind.ProviderCapability =>
                ClassifyProviderCapability(
                    source,
                    spec.ProviderExtensions,
                    redaction),
            AuthoritySourceKind.ProviderDefined =>
                ClassifyProviderDefined(
                    source,
                    spec.ProviderExtensions,
                    redaction),
            _ => Unclassified(
                source,
                redaction,
                "hpd.environment.authority.source-unsupported",
                "The authority source kind is not classified by the shared environment policy."),
        };
    }

    private static AuthoritySourceClassification ClassifyHostService(
        AuthorityBindingSource source,
        SensitiveRedactionLevel redaction) =>
        source.HostService switch
        {
            HostServiceKind.SshAgent => Classified(
                source,
                SensitiveEndpointKind.SshAgent,
                SensitiveAuthorityClass.CredentialDelegation,
                redaction,
                "SSH agent access delegates credential use."),
            HostServiceKind.DockerDaemon or
            HostServiceKind.PodmanDaemon or
            HostServiceKind.ContainerdDaemon or
            HostServiceKind.BuildKitDaemon or
            HostServiceKind.KubernetesApi =>
                Engine(source, redaction),
            HostServiceKind.GitCredentialHelper => Classified(
                source,
                SensitiveEndpointKind.CredentialProxy,
                SensitiveAuthorityClass.CredentialDelegation,
                redaction,
                "Git credential helper access delegates credential retrieval."),
            HostServiceKind.TlsTrustService or
            HostServiceKind.TrustAnchorStore => Classified(
                source,
                SensitiveEndpointKind.TrustService,
                SensitiveAuthorityClass.TrustMutation,
                redaction,
                "Trust services can mutate or disclose trust state."),
            HostServiceKind.HttpProxy or
            HostServiceKind.SocksProxy or
            HostServiceKind.HostResolver => Classified(
                source,
                SensitiveEndpointKind.ProviderDefined,
                SensitiveAuthorityClass.NetworkViaHostFunction,
                SensitiveRedactionLevel.RedactIdentifiers,
                "Host network mediation routes traffic through host policy."),
            HostServiceKind.DisplayServer => Classified(
                source,
                SensitiveEndpointKind.ProviderDefined,
                SensitiveAuthorityClass.HostStateRead,
                SensitiveRedactionLevel.RedactIdentifiers,
                "Display access exposes host session state."),
            null => Unclassified(
                source,
                redaction,
                "hpd.environment.authority.host-service-missing",
                "Host-service authority must name a service kind."),
            _ => Unclassified(
                source,
                redaction,
                "hpd.environment.authority.host-service-unclassified",
                "Provider-defined host services require bounded provider metadata and provider policy."),
        };

    private static AuthoritySourceClassification ClassifyUnixSocket(
        AuthorityBindingSource source,
        SensitiveRedactionLevel redaction)
    {
        if (source.SocketPath is null)
        {
            return Unclassified(
                source,
                redaction,
                "hpd.environment.authority.unix-socket-path-missing",
                "Unix-socket authority must include a path.");
        }

        string path = source.SocketPath.Value.Value;
        if (ContainsAny(
            path,
            "docker.sock",
            "podman.sock",
            "containerd.sock",
            "buildkit",
            "buildkitd.sock"))
        {
            return Engine(source, redaction);
        }
        if (ContainsAny(
            path,
            "ssh-agent",
            "ssh_auth_sock",
            "ssh-auth.sock",
            "/agent."))
        {
            return Classified(
                source,
                SensitiveEndpointKind.SshAgent,
                SensitiveAuthorityClass.CredentialDelegation,
                redaction,
                "SSH-agent socket access delegates credential use.");
        }
        if (ContainsAny(path, "credential", "git-credential", "keychain"))
        {
            return Classified(
                source,
                SensitiveEndpointKind.CredentialProxy,
                SensitiveAuthorityClass.CredentialDelegation,
                redaction,
                "Credential socket access delegates credential retrieval or signing.");
        }
        if (ContainsAny(path, "trust", "cert", "ca-cert"))
        {
            return Classified(
                source,
                SensitiveEndpointKind.TrustService,
                SensitiveAuthorityClass.TrustMutation,
                redaction,
                "Trust-related socket access can mutate or disclose trust state.");
        }
        if (ContainsAny(path, "dockerd", "launchd", "daemon", "xpc", "/var/run/"))
        {
            return Classified(
                source,
                SensitiveEndpointKind.HostDaemonControl,
                SensitiveAuthorityClass.PrivilegedDaemonControl,
                redaction,
                "Host-daemon sockets can control privileged host services.");
        }
        return Unclassified(
            source,
            redaction,
            "hpd.environment.authority.unix-socket-unclassified",
            "The Unix socket is unclassified; bounded provider metadata and provider policy are required.");
    }

    private static AuthoritySourceClassification ClassifyProviderCapability(
        AuthorityBindingSource source,
        IReadOnlyList<ProviderExtensionData> extensions,
        SensitiveRedactionLevel redaction)
    {
        if (string.IsNullOrWhiteSpace(source.ProviderCapabilityName))
        {
            return Unclassified(
                source,
                redaction,
                "hpd.environment.authority.provider-capability-name-missing",
                "Provider-capability authority must include a capability name.");
        }
        return HasBoundedProviderMetadata(extensions, out string reason)
            ? Classified(
                source,
                SensitiveEndpointKind.ProviderDefined,
                SensitiveAuthorityClass.HpdResourceMutation,
                SensitiveRedactionLevel.RedactIdentifiers,
                reason)
            : Unclassified(
                source,
                redaction,
                "hpd.environment.authority.provider-metadata-invalid",
                reason);
    }

    private static AuthoritySourceClassification ClassifyProviderDefined(
        AuthorityBindingSource source,
        IReadOnlyList<ProviderExtensionData> extensions,
        SensitiveRedactionLevel redaction) =>
        HasBoundedProviderMetadata(extensions, out string reason)
            ? Classified(
                source,
                SensitiveEndpointKind.ProviderDefined,
                SensitiveAuthorityClass.ProviderDefined,
                SensitiveRedactionLevel.RedactIdentifiers,
                reason)
            : Unclassified(
                source,
                redaction,
                "hpd.environment.authority.provider-metadata-invalid",
                reason);

    private static AuthoritySourceClassification Engine(
        AuthorityBindingSource source,
        SensitiveRedactionLevel redaction) =>
        Classified(
            source,
            SensitiveEndpointKind.EngineSocket,
            IsRootlessSocket(source.SocketPath)
                ? SensitiveAuthorityClass.RootlessEngineControl
                : SensitiveAuthorityClass.RootfulEngineControl,
            SensitiveRedactionLevel.RedactIdentifiers,
            "Container-engine authority controls workloads, images, networks, and storage and must remain mediated.");

    private static AuthoritySourceClassification Classified(
        AuthorityBindingSource source,
        SensitiveEndpointKind endpointKind,
        SensitiveAuthorityClass authorityClass,
        SensitiveRedactionLevel redaction,
        string message) =>
        new(
            true,
            source.Kind,
            endpointKind,
            authorityClass,
            redaction,
            RedactedDisplayName(source, endpointKind),
            "hpd.environment.authority.source-classified",
            message);

    private static AuthoritySourceClassification Unclassified(
        AuthorityBindingSource source,
        SensitiveRedactionLevel redaction,
        string code,
        string message) =>
        new(
            false,
            source.Kind,
            SensitiveEndpointKind.ProviderDefined,
            SensitiveAuthorityClass.None,
            redaction,
            RedactedDisplayName(
                source,
                SensitiveEndpointKind.ProviderDefined),
            code,
            message);

    private static bool HasBoundedProviderMetadata(
        IReadOnlyList<ProviderExtensionData> extensions,
        out string reason)
    {
        if (extensions.Count == 0)
        {
            reason =
                "Provider-defined authority requires explicit provider metadata.";
            return false;
        }
        if (extensions.Count > MaxProviderExtensionCount)
        {
            reason = "Provider metadata exceeded the bounded extension count.";
            return false;
        }

        long bytes = 0;
        foreach (ProviderExtensionData extension in extensions)
        {
            bytes += extension.Payload.Length;
            if (bytes > MaxProviderExtensionPayloadBytes)
            {
                reason = "Provider metadata exceeded the bounded payload size.";
                return false;
            }
        }
        reason = "Provider-defined authority metadata is present and bounded.";
        return true;
    }

    private static SensitiveRedactionLevel DefaultRedaction(
        AuthoritySourceKind kind,
        SensitiveRedactionLevel requested) =>
        requested != SensitiveRedactionLevel.None
            ? requested
            : kind is AuthoritySourceKind.Credential or AuthoritySourceKind.Secret
                ? SensitiveRedactionLevel.RedactSecretValues
                : SensitiveRedactionLevel.RedactIdentifiers;

    private static string RedactedDisplayName(
        AuthorityBindingSource source,
        SensitiveEndpointKind endpointKind) =>
        endpointKind switch
        {
            SensitiveEndpointKind.EngineSocket => "engine-socket:***",
            SensitiveEndpointKind.CredentialProxy => "credential:***",
            SensitiveEndpointKind.SshAgent => "ssh-agent:***",
            SensitiveEndpointKind.TrustService => "trust-service:***",
            SensitiveEndpointKind.HostDaemonControl => "host-daemon:***",
            SensitiveEndpointKind.FunctionDebug => "host-function:***",
            _ => source.Kind + ":***",
        };

    private static bool IsRootlessSocket(UnixSocketPath? path) =>
        path is not null &&
        ContainsAny(
            path.Value.Value,
            "/run/user/",
            "/.docker/run/",
            "/containers/podman/");

    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
