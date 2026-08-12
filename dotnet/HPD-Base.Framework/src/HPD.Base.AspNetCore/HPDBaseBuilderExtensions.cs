using HPD.Base;
using HPD.Base.AspNetCore;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

/// <summary>Represents a hpdbase builder extensions.</summary>
public static class HPDBaseBuilderExtensions
{
    /// <summary>Executes the add asp net core operation.</summary>
    public static HPDBaseBuilder AddAspNetCore(this HPDBaseBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use(new Installer());
    }

    private sealed class Installer : IHPDBaseBuilderExtension
    {
        /// <summary>Gets the ID.</summary>
        public string Id => "aspNetCore";
        /// <summary>Executes the configure operation.</summary>
        public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections)
        {
            services.AddHPDBaseAspNetCore();
            services.AddHPDBaseRealtimeAspNetCore();
        }
    }
}
