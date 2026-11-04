# HPD Agent Sandbox Host Containers

`HPD-Agent.Sandbox.HostContainers` connects Agent coding tools and command
execution to one retained HPD Environment Host Containers execution unit.

The adapter submits provider-neutral runtime-host, execution-unit, content
projection, process, and workspace requirements through
`EnvironmentSandboxBackendFactory`. It does not construct Docker commands.

```csharp
builder.WithHostContainersSandbox(
    new HostContainersSandboxOptions
    {
        Image = "hpd/coding-toolchain@sha256:...",
        EngineSocketPath = "/var/run/docker.sock",
        Workspaces =
        [
            new HostContainersWorkspaceOptions
            {
                Path = workspacePath,
                ContainerPath = "/workspace",
            },
        ],
    });
```

Provider-owned content already present in an image or engine-side volume uses:

```csharp
new HostContainersWorkspaceOptions
{
    SourceKind = HostContainersWorkspaceSourceKind.ProviderSource,
    ProviderSourceId = "workspace-volume",
    ContainerPath = "/workspace",
}
```

That profile uses bounded operations through the retained container process
lane and does not require or infer a host path.

`HostContainersWorkspaceSourceKind.CopyInOut` materializes a host directory into
provider-owned container storage during session creation and copies writable
content back before session disposal. No live bind mount is created. That
one-shot profile currently requires `AccessMode.ReadWrite`. Copy-out creates
and overwrites host files; it does not delete unrelated host-only files.

The session publishes one `RuntimeProcessExecutionBinding` and one
`RuntimeWorkspaceBinding`. Projection, file access, command execution, language
services, and debug adapters therefore target the same retained container
namespace and content instance.

The real-engine test requires an existing `HPD_CONTAINER_IMAGE` and a local
Docker-compatible Unix socket. Real acceptance is opt-in and is separate from
the fake-process composition tests.
