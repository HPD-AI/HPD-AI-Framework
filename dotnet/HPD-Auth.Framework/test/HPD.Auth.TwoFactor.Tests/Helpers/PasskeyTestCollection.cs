using Xunit;

namespace HPD.Auth.TwoFactor.Tests.Helpers;

/// <summary>
/// Serializes all passkey integration test classes so they do not start
/// multiple TwoFactorWebFactory instances concurrently.
///
/// Background: each factory opens a SQLite in-memory database. When factories
/// start in parallel, the SQLite schema-initialization race can cause one
/// factory's EnsureCreated to complete before another factory's keep-alive
/// connection is established, resulting in "passkey not found" 404s for tests
/// that seed passkeys via a service scope and then exercise them through the
/// HTTP pipeline.
/// </summary>
[CollectionDefinition("PasskeyTests", DisableParallelization = true)]
public sealed class PasskeyTestCollection { }
