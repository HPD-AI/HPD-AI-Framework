using HPD.Base.Runtime.DependencyInjection;
using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Results;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Tests.Descriptors;

public sealed class ManifestExpansionTests
{
    [Fact]
    public async Task UnknownExpansionTokenReturnsValidationFailed()
    {
        var services = new ServiceCollection();
        services.AddHPDBaseRuntime();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IBaseDescriptorProvider>().GetExpandedManifestAsync(new BaseManifestExpansionRequest
        {
            Principal = RuntimeTestData.AnonymousPrincipal,
            Operation = RuntimeTestData.Operation(BaseOperationKind.SchemaRead),
            Expand = ["schema", "mystery"]
        });

        Assert.Equal(HPD.Base.Results.OperationStatus.ValidationFailed, result.Status);
        Assert.False(result.IsSuccess());
    }
}
