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
observations, TLS material, telemetry instrumentation, and request inspection
currently fail closed before bundle creation. Management, credential
replacement, resilience, and standalone-host support are not implemented.
