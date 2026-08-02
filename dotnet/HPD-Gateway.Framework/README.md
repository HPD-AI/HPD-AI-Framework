# HPD.Gateway

HPD.Gateway is a library-first .NET gateway framework. It provides stable,
typed declarations and lifecycle contracts over ASP.NET Core and YARP while
leaving HTTP execution to their native runtimes.

This initial implementation slice contains the public declaration model, a
strict bounded source-generated JSON boundary, portable structural validation,
ASP.NET-native candidate validation in `HPD.Gateway.Core`, immutable
domain-framed content identity, contract tests, and a Native AOT smoke
application. Candidate acceptance requires both validation layers. It does not
yet advertise publication, management, credential replacement, resilience, or
standalone-host support.
