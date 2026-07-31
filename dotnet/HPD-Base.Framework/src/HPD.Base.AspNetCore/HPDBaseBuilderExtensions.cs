using HPD.Base;
using HPD.Base.AspNetCore;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

public static class HPDBaseBuilderExtensions
{
    public static HPDBaseBuilder AddAspNetCore(this HPDBaseBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use(new Installer());
    }

    private sealed class Installer : IHPDBaseBuilderExtension
    {
        public string Id => "aspNetCore";
        public bool IsRecordProvider => false;
        public bool SupportsRequiredIndexes => false;
        public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections) =>
            services.AddHPDBaseAspNetCore();
    }
}
