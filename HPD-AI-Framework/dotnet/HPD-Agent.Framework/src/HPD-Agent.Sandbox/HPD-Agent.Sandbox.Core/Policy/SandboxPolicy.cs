namespace HPD.Agent.Sandbox.Policy;

internal sealed record SandboxPolicy
{
    public required NetworkPolicy Network { get; init; }
    public required FilesystemPolicy Filesystem { get; init; }
    public required UnixSocketPolicy UnixSockets { get; init; }

    public bool EnableWeakerNestedSandbox { get; init; }
    public bool EnableViolationMonitoring { get; init; }
    public bool AllowPty { get; init; }
    public bool AllowLocalBinding { get; init; }
    public bool AllowMacOSTrustdLookup { get; init; }
    public int MandatoryDenySearchDepth { get; init; }
    public int? ExternalHttpProxyPort { get; init; }
    public int? ExternalSocksProxyPort { get; init; }
}
