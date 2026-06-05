namespace HPD.Agent.Sandbox.Network;

internal static class TlsTrustEnvironment
{
    public static readonly IReadOnlyList<string> TrustVariableNames =
    [
        "NODE_EXTRA_CA_CERTS",
        "SSL_CERT_FILE",
        "CURL_CA_BUNDLE",
        "REQUESTS_CA_BUNDLE",
        "PIP_CERT",
        "GIT_SSL_CAINFO",
        "AWS_CA_BUNDLE",
        "CARGO_HTTP_CAINFO",
        "DENO_CERT"
    ];

    public static IReadOnlyDictionary<string, string> Build(TlsTerminationConfig? config)
    {
        var environment = new Dictionary<string, string>();
        Apply(environment, config);
        return environment;
    }

    public static void Apply(IDictionary<string, string> environment, TlsTerminationConfig? config)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (config is null || !config.InjectTrustEnvironmentVariables)
            return;

        if (string.IsNullOrWhiteSpace(config.CaCertificatePath))
            return;

        ApplyCaCertificatePath(environment, config.CaCertificatePath);
    }

    public static void ApplyCaCertificatePath(IDictionary<string, string> environment, string caCertificatePath)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (string.IsNullOrWhiteSpace(caCertificatePath))
            throw new ArgumentException("CA certificate path cannot be empty.", nameof(caCertificatePath));

        foreach (var variableName in TrustVariableNames)
            environment[variableName] = caCertificatePath;
    }
}
