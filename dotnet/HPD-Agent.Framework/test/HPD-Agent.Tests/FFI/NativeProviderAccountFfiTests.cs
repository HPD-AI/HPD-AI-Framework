using System.Runtime.InteropServices;
using System.Text.Json;
using HPD.Agent.FFI;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;

namespace HPD.Agent.Tests.FFI;

public sealed class NativeProviderAccountFfiTests
{
    [Fact]
    public void BeginAuthorization_AuthorizesFfiSelectionBeforeCredentialPreparation()
    {
        var authorizer = new DenyingAuthorizer();
        var coordinator = new ProviderAuthenticationCoordinator(new EmptySecretResolver());
        var handle = NativeExports.RegisterProviderAccountServiceForTesting(coordinator, authorizer);
        try
        {
            var request = new ProviderAccountFfiRequest
            {
                ProviderKey = "provider",
                BackendKey = "backend",
                Family = ProviderClientFamily.Chat,
                Authentication = new OAuthProviderAuthentication { AccountId = "account" },
                AuthorizationScope = new ProviderAuthorizationScope { TrustDomainId = "ffi-host" },
                Audience = new ProviderCredentialAudience { Audience = "api" }
            };
            var begin = new BeginProviderAuthorizationFfiRequest
            {
                Account = request,
                Flow = ProviderAuthorizationFlow.AuthorizationCodePkce
            };
            var json = JsonSerializer.Serialize(begin, HPDFFIJsonContext.Default.BeginProviderAuthorizationFfiRequest);

            var pointer = NativeExports.BeginProviderAuthorizationCore(handle, json);
            try
            {
                var response = Marshal.PtrToStringUTF8(pointer)!;
                var error = JsonSerializer.Deserialize(response, HPDFFIJsonContext.Default.ProviderAccountFfiError)!;
                Assert.Equal("FfiSelectionDenied", error.DiagnosticCode);
                Assert.Equal(ProviderSelectionSource.Ffi, authorizer.Source);
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }
        finally { NativeExports.DestroyHandleForTesting(handle); }
    }

    private sealed class DenyingAuthorizer : IProviderAuthenticationSelectionAuthorizer
    {
        public ProviderSelectionSource? Source { get; private set; }

        public ValueTask AuthorizeAsync(
            ProviderAuthenticationSelectionContext context,
            CancellationToken cancellationToken = default)
        {
            Source = context.Source;
            throw new AgentRunConfigurationException(
                "FfiSelectionDenied",
                "authentication",
                "The test selection is denied.");
        }
    }

    private sealed class EmptySecretResolver : ISecretResolver
    {
        public ValueTask<ResolvedSecret?> ResolveAsync(
            string key,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<ResolvedSecret?>(null);
    }
}
