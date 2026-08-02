using System.Text;

namespace HPD.Base;

internal static class DescriptorViewFilter
{
    /// <summary>Executes the manifest operation.</summary>
    public static BaseManifest Manifest(BaseDescriptorSnapshot snapshot, VisibilityLevel view)
    {
        var manifest = snapshot.Manifest with
        {
            Visibility = view,
            Collections = FilterByVisibility(snapshot.Manifest.Collections, item => item.Visibility, view),
            Modules = FilterByVisibility(snapshot.Manifest.Modules, item => item.Visibility, view),
            Projections = FilterByVisibility(snapshot.Manifest.Projections, item => item.Visibility, view),
            DtoContracts = FilterByVisibility(snapshot.Manifest.DtoContracts, item => item.Visibility, view),
            EventTypes = FilterByVisibility(snapshot.Manifest.EventTypes, item => item.Visibility, view),
            HealthRefs = FilterByVisibility(snapshot.Manifest.HealthRefs, item => item.Visibility, view),
            DiagnosticRefs = FilterByVisibility(snapshot.Manifest.DiagnosticRefs, item => item.Visibility, view),
            Links = FilterByVisibility(snapshot.Manifest.Links, item => item.Visibility, view)
        };

        var visibleCapabilities = Capabilities(snapshot, view);
        var visibleFamilyIds = visibleCapabilities.Families.Select(family => family.FamilyId).ToHashSet(StringComparer.Ordinal);
        var visibleFeatureIds = visibleCapabilities.Families
            .SelectMany(family => family.Features ?? [])
            .Select(feature => feature.FeatureId)
            .ToHashSet(StringComparer.Ordinal);

        manifest = manifest with
        {
            Capabilities = manifest.Capabilities is null
                ? null
                : manifest.Capabilities with
                {
                    FamilyIds = NonEmpty(manifest.Capabilities.FamilyIds?.Where(visibleFamilyIds.Contains).ToArray()),
                    FeatureIds = NonEmpty(manifest.Capabilities.FeatureIds?.Where(visibleFeatureIds.Contains).ToArray())
                },
            Collections = manifest.Collections?
                .Select(collection => collection with { RequiredFeatureIds = VisibleRefs(collection.RequiredFeatureIds, visibleFeatureIds) })
                .ToArray()
        };

        return WithETag(PruneManifestReferences(manifest, visibleFeatureIds), view);
    }

    /// <summary>Executes the schema operation.</summary>
    public static SchemaMetadata Schema(BaseDescriptorSnapshot snapshot, VisibilityLevel view) =>
        snapshot.Schema with
        {
            Visibility = view,
            Collections = FilterByVisibility(snapshot.Schema.Collections, item => item.Visibility?.Visibility ?? VisibilityLevel.Public, view)?
                .Select(collection => Collection(collection, view))
                .Where(collection => view != VisibilityLevel.Public || collection.Visibility?.PublicSchema != false)
                .ToArray(),
            Sources = view == VisibilityLevel.Public ? null : FilterByVisibility(snapshot.Schema.Sources, item => item.Visibility, view),
            Diagnostics = Diagnostics(snapshot.Schema.Diagnostics, view)
        };

    /// <summary>Executes the collection operation.</summary>
    public static CollectionDefinition Collection(CollectionDefinition collection, VisibilityLevel view) =>
        collection with
        {
            Source = view == VisibilityLevel.Public ? null : collection.Source,
            Store = view == VisibilityLevel.Public ? null : collection.Store,
            PolicyRefs = view == VisibilityLevel.Public ? null : collection.PolicyRefs,
            Diagnostics = Diagnostics(collection.Diagnostics, view),
            Fields = collection.Fields?
                .Where(field => FieldVisible(field, view))
                .Select(field => Field(field, view))
                .ToArray(),
            Indexes = view == VisibilityLevel.Public ? PublicIndexes(collection.Indexes) : collection.Indexes
        };

    /// <summary>Executes the capabilities operation.</summary>
    public static CapabilityDescriptor Capabilities(BaseDescriptorSnapshot snapshot, VisibilityLevel view) =>
        snapshot.Capabilities with
        {
            Families = snapshot.Capabilities.Families
                .Where(family => IsVisible(family.Visibility, view))
                .Select(family => family with
                {
                    Features = FilterByVisibility(family.Features, feature => feature.Visibility, view)?
                        .Select(feature => feature with
                        {
                            DtoContracts = VisibleRefs(feature.DtoContracts, snapshot.Manifest.DtoContracts?.Where(dto => IsVisible(dto.Visibility, view)).Select(dto => dto.Id).ToHashSet(StringComparer.Ordinal)),
                            RouteRefs = VisibleRefs(feature.RouteRefs, VisibleRouteIds(snapshot.Manifest.Projections, view)),
                            EventTypeRefs = VisibleRefs(feature.EventTypeRefs, snapshot.Manifest.EventTypes?.Where(eventType => IsVisible(eventType.Visibility, view)).Select(eventType => eventType.Type).ToHashSet(StringComparer.Ordinal)),
                            HealthRef = VisibleRef(feature.HealthRef, snapshot.Manifest.HealthRefs?.Where(health => IsVisible(health.Visibility, view)).Select(health => health.Id).ToHashSet(StringComparer.Ordinal)),
                            DiagnosticRefs = VisibleRefs(feature.DiagnosticRefs, snapshot.Manifest.DiagnosticRefs?.Where(diagnostic => IsVisible(diagnostic.Visibility, view)).Select(diagnostic => diagnostic.Id).ToHashSet(StringComparer.Ordinal))
                        })
                        .ToArray(),
                    Dependencies = view == VisibilityLevel.Public ? null : family.Dependencies
                })
                .ToArray()
        };

    private static BaseManifest PruneManifestReferences(BaseManifest manifest, HashSet<string> visibleFeatureIds)
    {
        var visibleDtoIds = manifest.DtoContracts?.Select(dto => dto.Id).ToHashSet(StringComparer.Ordinal) ?? [];
        var visibleEventTypes = manifest.EventTypes?.Select(eventType => eventType.Type).ToHashSet(StringComparer.Ordinal) ?? [];
        var visibleHealthIds = manifest.HealthRefs?.Select(health => health.Id).ToHashSet(StringComparer.Ordinal) ?? [];
        var visibleDiagnosticIds = manifest.DiagnosticRefs?.Select(diagnostic => diagnostic.Id).ToHashSet(StringComparer.Ordinal) ?? [];

        var projections = manifest.Projections?
            .Select(projection => PruneProjection(projection, visibleDtoIds, visibleFeatureIds, visibleHealthIds, visibleDiagnosticIds))
            .Where(projection => projection.Routes is { Length: > 0 } || projection.Entrypoints is { Length: > 0 } || projection.DtoContracts is { Length: > 0 })
            .ToArray();
        var visibleRouteIds = projections?
            .SelectMany(projection => projection.Routes ?? [])
            .Select(route => route.OperationId)
            .ToHashSet(StringComparer.Ordinal) ?? [];

        return manifest with
        {
            Projections = NonEmpty(projections),
            Modules = manifest.Modules?
                .Select(module => module with
                {
                    ContributedCapabilities = VisibleRefs(module.ContributedCapabilities, visibleFeatureIds),
                    ContributedDtoIds = VisibleRefs(module.ContributedDtoIds, visibleDtoIds),
                    ContributedRouteIds = VisibleRefs(module.ContributedRouteIds, visibleRouteIds),
                    ContributedEventTypes = VisibleRefs(module.ContributedEventTypes, visibleEventTypes),
                    ContributedHealthRefIds = VisibleRefs(module.ContributedHealthRefIds, visibleHealthIds),
                    ContributedDiagnosticIds = VisibleRefs(module.ContributedDiagnosticIds, visibleDiagnosticIds)
                })
                .ToArray(),
            Links = manifest.Links?
                .Where(link => visibleDtoIds.Contains(link.ResponseDtoId) && AllVisible(link.RequiredFeatureIds, visibleFeatureIds))
                .Select(link => link with { RequiredFeatureIds = VisibleRefs(link.RequiredFeatureIds, visibleFeatureIds) })
                .ToArray()
        };
    }

    private static ProjectionDescriptor PruneProjection(
        ProjectionDescriptor projection,
        HashSet<string> visibleDtoIds,
        HashSet<string> visibleFeatureIds,
        HashSet<string> visibleHealthIds,
        HashSet<string> visibleDiagnosticIds)
    {
        var routes = projection.Routes?
            .Where(route => DtoVisible(route.RequestDtoId, visibleDtoIds)
                && DtoVisible(route.ResponseDtoId, visibleDtoIds)
                && DtoVisible(route.ErrorDtoId, visibleDtoIds)
                && DtoVisible(route.ResultDtoId, visibleDtoIds)
                && AllVisible(route.RequiredFeatureIds, visibleFeatureIds))
            .Select(route => route with { RequiredFeatureIds = VisibleRefs(route.RequiredFeatureIds, visibleFeatureIds) })
            .ToArray();
        var routeIds = routes?.Select(route => route.OperationId).ToHashSet(StringComparer.Ordinal) ?? [];

        return projection with
        {
            RequiredCapabilities = VisibleRefs(projection.RequiredCapabilities, visibleFeatureIds),
            ProvidedCapabilities = VisibleRefs(projection.ProvidedCapabilities, visibleFeatureIds),
            Routes = NonEmpty(routes),
            DtoContracts = projection.DtoContracts?
                .Where(dto => visibleDtoIds.Contains(dto.Id))
                .ToArray(),
            HealthRefs = VisibleRefs(projection.HealthRefs, visibleHealthIds),
            DiagnosticRefs = VisibleRefs(projection.DiagnosticRefs, visibleDiagnosticIds),
            Entrypoints = projection.Entrypoints?
                .Select(entrypoint => entrypoint with
                {
                    RequiredFeatureIds = VisibleRefs(entrypoint.RequiredFeatureIds, visibleFeatureIds),
                    RouteRefs = VisibleRefs(entrypoint.RouteRefs, routeIds)
                })
                .Where(entrypoint => entrypoint.RouteRefs is null or { Length: > 0 })
                .ToArray()
        };
    }

    /// <summary>Executes the health operation.</summary>
    public static HealthDescriptor[] Health(HealthDescriptor[]? health, VisibilityLevel view) =>
        (health ?? [])
        .Where(item => IsVisible(item.Visibility, view))
        .Select(item => view == VisibilityLevel.Public ? item with { Dependencies = null } : item)
        .ToArray();

    /// <summary>Executes the diagnostics operation.</summary>
    public static DiagnosticDescriptor[] Diagnostics(DiagnosticDescriptor[]? diagnostics, VisibilityLevel view) =>
        (diagnostics ?? [])
        .Where(item => IsVisible(item.Visibility, view))
        .Select(item => view == VisibilityLevel.Public
            ? item with { Message = item.PublicMessage ?? item.Message, Remediation = null }
            : item)
        .ToArray();

    private static FieldDefinition Field(FieldDefinition field, VisibilityLevel view) =>
        view == VisibilityLevel.Public
            ? field with
            {
                Store = null,
                Extensions = null,
                Generated = field.Generated?.PublicSafe == true ? field.Generated : null,
                Default = field.Default?.PublicSafe == true ? field.Default : null,
                Validation = PublicValidation(field.Validation)
            }
            : field;

    private static ValidationAnnotations? PublicValidation(ValidationAnnotations? validation)
    {
        if (validation is null)
        {
            return null;
        }

        var rules = validation.Rules?.Where(rule => rule.PublicSafe).ToArray();
        return validation with
        {
            Rules = rules is { Length: > 0 } ? rules : null,
            CustomValidators = null,
            Diagnostics = null
        };
    }

    private static IndexDefinition[]? PublicIndexes(IndexDefinition[]? indexes) =>
        indexes?
            .Select(index => index with
            {
                NativePredicate = null,
                NativeDefinition = null,
                AccessMethod = null,
                Extensions = null
            })
            .ToArray();

    private static bool FieldVisible(FieldDefinition field, VisibilityLevel view)
    {
        if (!IsVisible(field.Visibility?.Visibility ?? VisibilityLevel.Public, view))
        {
            return false;
        }

        if (view == VisibilityLevel.Public)
        {
            return !field.Hidden && !field.System && field.Visibility?.AdminOnly != true;
        }

        return true;
    }

    private static T[]? FilterByVisibility<T>(
        T[]? items,
        Func<T, VisibilityLevel> getVisibility,
        VisibilityLevel view)
    {
        var filtered = items?.Where(item => IsVisible(getVisibility(item), view)).ToArray();
        return filtered is { Length: > 0 } ? filtered : null;
    }

    private static bool IsVisible(VisibilityLevel itemVisibility, VisibilityLevel view) =>
        itemVisibility <= view;

    private static HashSet<string> VisibleRouteIds(ProjectionDescriptor[]? projections, VisibilityLevel view) =>
        (projections ?? [])
        .Where(projection => IsVisible(projection.Visibility, view))
        .SelectMany(projection => projection.Routes ?? [])
        .Where(route => IsVisible(route.Visibility, view))
        .Select(route => route.OperationId)
        .ToHashSet(StringComparer.Ordinal);

    private static string[]? VisibleRefs(string[]? refs, HashSet<string>? visibleRefs) =>
        visibleRefs is null ? refs : NonEmpty(refs?.Where(visibleRefs.Contains).ToArray());

    private static string? VisibleRef(string? reference, HashSet<string>? visibleRefs) =>
        reference is null || visibleRefs is null || visibleRefs.Contains(reference) ? reference : null;

    private static bool DtoVisible(string? dtoId, HashSet<string> visibleDtoIds) =>
        string.IsNullOrWhiteSpace(dtoId) || visibleDtoIds.Contains(dtoId);

    private static bool AllVisible(string[]? refs, HashSet<string> visibleRefs) =>
        refs is null || refs.All(visibleRefs.Contains);

    private static T[]? NonEmpty<T>(T[]? items) =>
        items is { Length: > 0 } ? items : null;

    private static BaseManifest WithETag(BaseManifest manifest, VisibilityLevel view)
    {
        var builder = new StringBuilder();
        Append(builder, "view", view.ToString());
        Append(builder, "manifest", manifest.ManifestVersion);
        Append(builder, "contract", manifest.ContractVersion);
        Append(builder, "runtime", manifest.Runtime.Id);
        Append(builder, "collections", manifest.Collections?.Select(item => item.Id));
        Append(builder, "modules", manifest.Modules?.Select(item => item.Id));
        Append(builder, "projections", manifest.Projections?.Select(item => item.Id));
        Append(builder, "routes", manifest.Projections?.SelectMany(projection => projection.Routes ?? []).Select(route => route.OperationId));
        Append(builder, "dtos", manifest.DtoContracts?.Select(item => item.Id));
        Append(builder, "events", manifest.EventTypes?.Select(item => item.Type));
        Append(builder, "health", manifest.HealthRefs?.Select(item => item.Id));
        Append(builder, "diagnostics", manifest.DiagnosticRefs?.Select(item => item.Id));
        Append(builder, "families", manifest.Capabilities?.FamilyIds);
        Append(builder, "features", manifest.Capabilities?.FeatureIds);

        return manifest with { ETag = $"W/\"base-{StableHash(builder.ToString()):x16}\"" };
    }

    private static ulong StableHash(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offset;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }

        return hash;
    }

    private static void Append(StringBuilder builder, string key, string? value) =>
        builder.Append(key).Append('=').Append(value ?? string.Empty).Append(';');

    private static void Append(StringBuilder builder, string key, IEnumerable<string>? values)
    {
        builder.Append(key).Append('=');
        foreach (var value in (values ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Order(StringComparer.Ordinal))
        {
            builder.Append(value).Append(',');
        }

        builder.Append(';');
    }
}
