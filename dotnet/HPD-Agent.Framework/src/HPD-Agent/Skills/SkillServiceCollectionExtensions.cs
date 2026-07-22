using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Agent;

/// <summary>Registers replaceable managed-skill infrastructure.</summary>
public static class SkillServiceCollectionExtensions
{
    /// <summary>
    /// Adds an in-memory content-store default and a content-backed skill store.
    /// Existing application registrations are preserved.
    /// </summary>
    public static IServiceCollection AddHPDSkills(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IContentStore, InMemoryContentStore>();
        services.TryAddSingleton<ContentStoreSkillStore>(provider =>
            new ContentStoreSkillStore(provider.GetRequiredService<IContentStore>(), ContentStoreScopes.Skills));
        services.TryAddSingleton<IContentBackedSkillStore>(provider => provider.GetRequiredService<ContentStoreSkillStore>());
        services.TryAddSingleton<ISkillStore>(provider => provider.GetRequiredService<ContentStoreSkillStore>());
        return services;
    }

    /// <summary>Adds an explicit skill-store instance for host package management.</summary>
    public static IServiceCollection AddSkillStore(this IServiceCollection services, ISkillStore store)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(store);
        services.AddSingleton(store);
        if (store is IContentBackedSkillStore contentBacked)
            services.AddSingleton(contentBacked);
        return services;
    }
}
