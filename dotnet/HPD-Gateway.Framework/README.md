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
not yet include declaration-to-YARP materialization, management, credential
replacement, resilience, or standalone-host support.
