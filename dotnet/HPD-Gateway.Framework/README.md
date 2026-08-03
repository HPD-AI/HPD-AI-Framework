# HPD.Gateway

HPD.Gateway is a library-first .NET gateway framework. It provides stable,
typed declarations and lifecycle contracts over ASP.NET Core and YARP while
leaving HTTP execution to their native runtimes.

The implemented foundation contains the public declaration model, a
strict bounded source-generated JSON boundary, portable structural validation,
ASP.NET-native candidate validation in `HPD.Gateway.Core`, immutable
domain-framed content identity, and one serialized HPD-owned YARP publication
stream in `HPD.Gateway.Yarp`. Candidate acceptance requires both validation
layers. Native publication provides exact-snapshot YARP acknowledgement,
historical LKG evidence, and explicit indeterminate recovery semantics. It does
include deterministic baseline Route/Cluster materialization, native named
policy selection, ordered non-body transforms, static destinations, balancing,
affinity, health, and supported transport/request projection. Discovery
observations, TLS material, and telemetry instrumentation currently fail closed
before bundle creation.

`HPD.Gateway.Inspection` adds opt-in bounded pre-forward request inspection.
Inspectors are explicitly registered by canonical name and selected through
immutable materialized Route metadata. Prefix mode requires a known accepted
length, retains only its bounded prefix, and then resumes transparent
forwarding. Complete mode uses ASP.NET Core's bounded request-owned buffering
with an explicit memory threshold and host-approved spill policy. Hosts using
inspection must call `AddHpdGatewayYarpInspection` and
`MapHpdGatewayReverseProxy`; ordinary Routes without inspection do not enter
the body path. Inspection does not provide replay, retries, mirroring, body
transforms, or response capture.

`HPD.Gateway.Resilience` adds optional, statically registered, exact-version
Upstream profiles for selected-response retry, circuit breaking, outbound
concurrency limiting, and per-attempt timeout. Retry is restricted to bodyless
safe HTTP/1.1/2 requests and selected status responses. The package emits only
closed profile/strategy/outcome telemetry tags and does not expose dynamic
Polly configuration or a general handler/plugin chain.

Routes may opt into closed protected-credential stripping. HPD removes
`Authorization`, `Proxy-Authorization`, `Cookie`, and a bounded set of
host-registered credential header names through deterministic YARP request
transforms immediately before forwarding. The candidate cannot provide header
names, and protected headers cannot be restored by candidate request
transforms. No disposition preserves ordinary YARP behavior.

`HPD.Gateway.OutputCaching` adds optional bounded startup-registered ASP.NET
Core Output Cache profiles over the framework's process-local memory store.
Profiles retain the native default safety policy, explicit bounded query and
header dimensions, host variation, locking, expiration, body/store limits,
and a profile-version key namespace. Cached Routes must be explicit GET/HEAD,
must strip protected credentials, and cannot also select request inspection.
ASP.NET Core continues to own lookup, capture, entries, storage, and serving;
HPD does not provide a cache engine or purge authority.

`HPD.Gateway.Hosting` begins the separate restart-bound standalone host
surface. Its first closed mode materializes exact and wildcard SNI entries
through Kestrel configuration without a `*` or default-certificate fallback,
so missing and unmatched SNI fail during the native TLS handshake. Certificate
material is supplied through a startup-only PFX source catalog and remains
outside host declarations, identity, YARP publication, and diagnostics.

Management, downstream credential replacement/delegation, shared cache-store
providers, dynamic listener reload, mTLS, HTTP/3 hosting, and L4 proxying are
not implemented.
