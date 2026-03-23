namespace HPD.Agent.Sandbox;

/// <summary>
/// Sandbox isolation tiers - implementations in separate packages.
/// </summary>
public enum SandboxTier
{
    /// <summary>Local OS-level sandbox (HPD.Sandbox.Local)</summary>
    Local,

    /// <summary>Container sandbox - Docker, Podman, containerd (HPD.Sandbox.Container)</summary>
    Container,

    /// <summary>Cloud VM sandbox (HPD.Sandbox.Cloud)</summary>
    Cloud
}
