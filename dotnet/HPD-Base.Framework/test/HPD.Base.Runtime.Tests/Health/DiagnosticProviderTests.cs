using HPD.Base.Health;
using HPD.Base.Runtime.DependencyInjection;
using HPD.Base.Runtime.Health;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Tests.Health;

public sealed class DiagnosticProviderTests
{
    [Fact]
    public async Task PublicDiagnosticsUsePublicMessageAndRemoveRemediation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBaseDiagnosticContributor, TestDiagnosticContributor>();
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IBaseDiagnosticProvider>().GetDiagnosticsAsync(
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.SchemaRead),
            VisibilityLevel.Public);

        var diagnostic = Assert.Single(result.Value!);
        Assert.Equal("Public message.", diagnostic.Message);
        Assert.Null(diagnostic.Remediation);
    }

    private sealed class TestDiagnosticContributor : IBaseDiagnosticContributor
    {
        public string Id => "test";

        public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new[]
            {
                new DiagnosticDescriptor
                {
                    Id = "diag",
                    Code = "diag",
                    Severity = DiagnosticSeverity.Warning,
                    Message = "Internal message.",
                    PublicMessage = "Public message.",
                    Remediation = "Restart host db01.",
                    Category = DiagnosticCategory.Configuration,
                    Visibility = VisibilityLevel.Public,
                    EmittedAt = DateTimeOffset.UnixEpoch
                }
            });
    }
}
