# HPD Environment Local

`HPD.Environment.Local` runs reviewed HPDOS workloads through a
Docker-compatible engine on the current macOS or Linux host. It is a real HPD
Environment provider, not a direct Docker bypass.

## Security boundary

- Local containers share the host kernel. This is weaker isolation than Apple
  Virtualization and is reported as such.
- Apps never receive the engine socket, `DOCKER_HOST`, host paths, or arbitrary
  host-process execution.
- Engine operations require a current, short-lived Environment authority
  binding. An engine-incarnation change fences old authority and endpoints.
- Provider-controlled host processes use a fixed environment and a strict
  command allowlist.
- App endpoints are published only through an HPD-owned IPv4 loopback route.
  Broad host bindings and undeclared ports are rejected.
- The provider never starts or stops the user-owned container engine.

Use Local only for trusted, reviewed Apps. Apps requiring hardware-virtualized
isolation must fail closed or use Apple Virtualization.

Local and Apple report the same provider-neutral standard capabilities:
process/container isolation, host-kernel sharing, hardware virtualization,
guest-agent boundary, mediated engine authority, and host-local endpoint
publication. Local truthfully reports host-kernel sharing as supported and
hardware virtualization as unsupported.

## Configuration

```sh
export HPDOS_ENVIRONMENT_MODE=local
export HPDOS_LOCAL_ENGINE_KIND=docker
export HPDOS_LOCAL_ENGINE_ENDPOINT="$HOME/.docker/run/docker.sock"
export HPDOS_LOCAL_DOCKER_CLI_PATH=/usr/local/bin/docker
```

`HPDOS_LOCAL_WORKLOAD_STATE_ROOT` may select the HPDOS-owned staging root.
Otherwise HPDOS places it under its backend state directory. The engine
endpoint must be a local Unix socket. TCP engines are not supported.

Configuration wins over discovery. Without an explicit endpoint, discovery
checks only a bounded well-known list and rejects ambiguity. Local startup
reports an unavailable engine; it does not launch Docker Desktop or another
daemon.

## Lifecycle

1. HPDOS starts with the Environment stopped.
2. The user starts the Environment.
3. Local creates a logical `RuntimeHost`, probes the selected engine, and
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

The Local provider persists and atomically advances its provider generation in
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

Compose named volumes remain engine-managed and HPDOS-owned. Retention follows
the signed App specification. Local staging files are provider-private and are
not an App-selected host bind mount. Provider migration is not an ordinary
restart and requires an explicit future migration workflow.

## Physical acceptance

The shared Penpot physical test supports Local when
`HPDOS_REAL_PENPOT_ENVIRONMENT=local`. Images must already exist in the selected
engine under the exact digest-pinned references in the signed package. The
test does not grant registry or engine credentials to Penpot.

Physical Local and physical Apple results are separate evidence. Passing one
does not imply the other passed.
