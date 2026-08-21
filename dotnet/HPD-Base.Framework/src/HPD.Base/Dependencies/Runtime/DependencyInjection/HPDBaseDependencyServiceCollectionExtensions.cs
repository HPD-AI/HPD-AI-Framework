using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Base;

/// <summary>Represents a hpdbase dependency service collection extensions.</summary>
public static class HPDBaseDependencyServiceCollectionExtensions
{
    /// <summary>Executes the add hpdbase dependencies operation.</summary>
    public static IServiceCollection AddHPDBaseDependencies(
        this IServiceCollection services,
        Action<BaseDependencyOptions> configure,
        params BaseDependencyTemplate[] templates)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(templates);

        var options = new BaseDependencyOptions();
        configure(options);
        Validate(options, templates);

        services.TryAddSingleton(options);
        services.AddSingleton(new BaseDependencyTemplate
        {
            Id = BaseDependencyIds.Collection,
            Kind = BaseDependencyKind.Collection,
            ParameterNames = ["tenant", "collection"],
            Visibility = BaseDependencyVisibility.Public,
            Description = "A tenant-scoped BASE collection."
        });
        services.AddSingleton(new BaseDependencyTemplate
        {
            Id = BaseDependencyIds.SubjectContract,
            Kind = BaseDependencyKind.Named,
            ParameterNames = ["contract", "version", "generation"],
            Visibility = BaseDependencyVisibility.Internal,
            Description = "An installed exported-subject authority generation."
        });
        services.AddSingleton(new BaseDependencyTemplate
        {
            Id = BaseDependencyIds.SubjectRetirement,
            Kind = BaseDependencyKind.Named,
            ParameterNames = ["contract", "version"],
            Visibility = BaseDependencyVisibility.Internal,
            Description = "A coordinated subject-retirement authority."
        });
        services.AddSingleton(new BaseDependencyTemplate
        {
            Id = BaseDependencyIds.Record,
            Kind = BaseDependencyKind.Record,
            ParameterNames = ["tenant", "collection", "record"],
            Visibility = BaseDependencyVisibility.Public,
            Description = "A tenant-scoped BASE record."
        });
        foreach (var template in templates)
            services.AddSingleton(template);

        services.TryAddSingleton<DefaultBaseDependencyServices>();
        services.TryAddSingleton<IBaseDependencyReferenceFactory>(
            static provider => provider.GetRequiredService<DefaultBaseDependencyServices>());
        services.TryAddSingleton<IBaseDependencyInvalidationMapper>(
            static provider => provider.GetRequiredService<DefaultBaseDependencyServices>());
        services.TryAddSingleton<IBaseDependencyTemplateProvider>(
            static provider => provider.GetRequiredService<DefaultBaseDependencyServices>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, BaseDependencyDescriptorContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseJsonTypeInfoContributor, BaseDependencyJsonTypeInfoContributor>());
        return services;
    }

    private static void Validate(BaseDependencyOptions options, BaseDependencyTemplate[] templates)
    {
        if (options.ProtectionKey is null || options.ProtectionKey.Length < 32)
            throw new ArgumentException("Dependency reference protection requires at least 32 bytes.", nameof(options));
        if (options.MaxReferencesPerInvalidation is < 2 or > 256)
            throw new ArgumentOutOfRangeException(nameof(options), "Dependency invalidation reference limit must be between 2 and 256.");

        var all = templates.Append(new BaseDependencyTemplate
        {
            Id = BaseDependencyIds.Collection,
            Kind = BaseDependencyKind.Collection
        }).Append(new BaseDependencyTemplate
        {
            Id = BaseDependencyIds.Record,
            Kind = BaseDependencyKind.Record
        }).Append(new BaseDependencyTemplate
        {
            Id = BaseDependencyIds.SubjectContract,
            Kind = BaseDependencyKind.Named
        }).ToArray();
        if (all.Any(template => template is null
            || !IsIdentifier(template.Id, 128)))
            throw new ArgumentException("Dependency template ids must be bounded ASCII identifiers.", nameof(templates));
        if (all.GroupBy(static template => template.Id, StringComparer.Ordinal).Any(static group => group.Count() != 1))
            throw new ArgumentException("Dependency template ids must be unique.", nameof(templates));
        if (templates.Any(static template => !Enum.IsDefined(template.Kind)
            || !Enum.IsDefined(template.Visibility)))
            throw new ArgumentException("Dependency template enum values are invalid.", nameof(templates));
        if (templates.Any(static template => template.ParameterNames is null
            || template.ParameterNames.Length > 16
            || template.ParameterNames.Any(name => !IsIdentifier(name, 64))
            || template.ParameterNames.Distinct(StringComparer.Ordinal).Count() != template.ParameterNames.Length))
            throw new ArgumentException("Dependency template parameters are invalid.", nameof(templates));
        if (templates.Any(static template => template.Description is { } description
            && (description.Length > 512 || description.Any(char.IsControl))))
            throw new ArgumentException("Dependency template descriptions must be bounded and cannot contain control characters.", nameof(templates));
    }

    private static bool IsIdentifier(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && value.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
}
