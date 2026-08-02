# HPD.Gateway

HPD.Gateway is a library-first .NET gateway framework. It provides stable,
typed declarations and lifecycle contracts over ASP.NET Core and YARP while
leaving HTTP execution to their native runtimes.

This initial implementation slice contains only the public declaration model,
strict bounded source-generated JSON boundary, total candidate validation,
canonical content hashing, initial contract tests, and a Native AOT smoke
application. It does not yet advertise publication, management, credential
replacement, resilience, or standalone-host support.
