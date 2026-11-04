# HPD Environment Host Containers

`HPD-Environment.HostContainers` runs reviewed HPDOS workloads through a
Docker-compatible engine on the current macOS or Linux host. It is a real HPD
Environment provider, not a direct Docker bypass.

## Security boundary

- Linux containers share the Linux host kernel. On macOS, Docker Desktop and
  similar engines may execute Linux workloads in an engine-managed VM. The
  provider does not claim direct macOS kernel sharing or ownership of that VM.
- Apps never receive the engine socket, `DOCKER_HOST`, host paths, or arbitrary
  host-process execution.
- Engine operations require a current, short-lived Environment authority
  binding. An engine-incarnation change fences old authority and endpoints.
- Provider-controlled engine processes use a fixed environment and a strict
  command allowlist. Reviewed native executables require an explicitly
  configured filesystem root and may receive only explicitly allowlisted
  environment variables.
- App endpoints are published only through an HPD-owned IPv4 loopback route.
  Broad host bindings and undeclared ports are rejected.
- The provider never starts or stops the user-owned container engine.

Use Host Containers only for trusted, reviewed Apps. Apps requiring
hardware-virtualized isolation must fail closed or use Apple Virtualization.

Host Containers and Apple report provider-neutral standard capabilities.
Host Containers reports Linux kernel sharing on Linux and degraded/unknown
topology on macOS. It does not claim hardware virtualization unless a provider
actually owns and proves that boundary.

## Retained Agent sandbox

The Agent-facing adapter creates one retained sandbox container per execution
unit. Workspace directories are validated and converted into explicit bind
mounts during container creation. Commands and language servers use
`docker exec` inside that retained namespace, while model-facing file tools use
the verified local bind source through `IWorkspaceFileStore`.

```csharp
.WithHostContainersSandbox(new HostContainersSandboxOptions
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

Content that already exists in the container image or an engine-owned volume
uses a provider-owned source instead of a host bind:

```csharp
Workspaces =
[
    new HostContainersWorkspaceOptions
    {
        SourceKind = HostContainersWorkspaceSourceKind.ProviderSource,
        ProviderSourceId = "workspace-volume",
        ContainerPath = "/workspace",
    },
],
```

The provider supports local Unix-socket engines, validated directory bind
mounts, provider-owned content already present in the container, retained
command execution, and read-only container roots. Provider-owned workspaces use
bounded container-side file operations and do not require a host path.

Agent copy-in/copy-out workspaces compose this provider-owned mode with explicit
session materialization and finalization; they do not use an implicit bind
mount or infer container paths from host paths.

Filtered egress, public endpoint publication, PTY execution, detached process
adoption, and non-Unix-socket engine transports remain explicit unsupported
capabilities.

## Configuration

```sh
export HPDOS_ENVIRONMENT_MODE=host-containers
export HPDOS_HOST_CONTAINERS_ENGINE_KIND=docker
export HPDOS_HOST_CONTAINERS_ENGINE_ENDPOINT="$HOME/.docker/run/docker.sock"
export HPDOS_HOST_CONTAINERS_DOCKER_CLI_PATH=/usr/local/bin/docker
```

`HPDOS_HOST_CONTAINERS_WORKLOAD_STATE_ROOT` may select the HPDOS-owned staging root.
Otherwise HPDOS places it under its backend state directory. The engine
endpoint must be a local Unix socket. TCP engines are not supported.

Configuration wins over discovery. Without an explicit endpoint, discovery
checks only a bounded well-known list and rejects ambiguity. Host Containers startup
reports an unavailable engine; it does not launch Docker Desktop or another
daemon.

Reviewed native executables require explicit policy:

```csharp
new HostContainersProviderOptions
{
    AllowedHostExecutableRoots = ["/opt/hpd/applications"],
    AllowedHostEnvironmentVariables =
    [
        "WAYLAND_DISPLAY",
        "XDG_RUNTIME_DIR",
        "DISPLAY",
        "XAUTHORITY",
    ],
}
```

Executable paths must be absolute and resolve beneath an allowed root. Shell
execution is not made available by listing `/bin`; HPD container and Compose
operations keep their dedicated envelopes. Native processes receive only the
fixed provider environment plus variables explicitly present in the allowlist.

## Lifecycle

1. HPDOS starts with the Environment stopped.
2. The user starts the Environment.
3. Host Containers creates a logical `RuntimeHost`, probes the selected engine, and
   records its incarnation.
4. App operations obtain bounded engine authority and create HPD-owned
   workloads.
5. The provider creates deterministic HPD-owned engine networks with exact
   labels. HPDOS attaches Compose through a generated external-network
   override; package YAML is unchanged.
6. HPDOS generates its own loopback-only Compose endpoint override; package
   `ports` remain forbidden.
7. Browser launch, authentication, and client-tool binding remain at the HPDOS
   gateway.
8. Environment stop revokes launch and engine authority before endpoints and
   workload teardown, then releases logical resources. It leaves the host and
   engine running.

An engine restart invalidates old workload observations, authority bindings,
and endpoint publications. Recovery must re-observe exact HPD ownership and
must not replay an ambiguous mutation.

The Host Containers provider persists and atomically advances its provider generation in
the provider-private workload-state root. A logical host stop/start advances
the host-start generation independently. Corrupt generation state fails
closed.

Physical network realization uses an operation-scoped
`NetworkRealizationContext`: one exact execution-unit owner and one current
engine-authority binding. Status re-inspects the physical network so deletion,
identity replacement, or label mutation is detected. Re-adoption after an
engine-generation change succeeds only when the deterministic name, engine ID,
labels, and immutable intent match.

## Persistent data

App-durable volumes and Compose staging allocations live in provider-owned
directories beneath the configured Local storage root. HPDOS retains the
logical volume/allocation identity and reacquires the effective provider path
for each operation; Apps never select an absolute host path. The Docker engine
receives only the provider-resolved bind projection for the current owned
workload. Retention, reservation, backup, restore, and erase follow the typed
Environment storage lifecycle. Provider migration is not an ordinary restart
and requires an explicit migration workflow.

## Physical acceptance

The shared Penpot physical test supports Local when
`HPDOS_REAL_PENPOT_ENVIRONMENT=host-containers`. Images must already exist in the selected
engine under the exact digest-pinned references in the signed package. The
test does not grant registry or engine credentials to Penpot.

Physical Host Containers and physical Apple results are separate evidence. Passing one
does not imply the other passed.
