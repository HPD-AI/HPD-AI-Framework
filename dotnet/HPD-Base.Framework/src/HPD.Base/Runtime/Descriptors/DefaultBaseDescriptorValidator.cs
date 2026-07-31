using HPD.Base.Descriptors;
using HPD.Base.Schema;

namespace HPD.Base.Runtime.Descriptors;

internal sealed class DefaultBaseDescriptorValidator : IBaseDescriptorValidator
{
    public BaseRuntimeValidationResult Validate(BaseDescriptorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var issues = new List<BaseRuntimeValidationIssue>();
        AddDuplicateIssues(snapshot.Manifest.Collections?.Select(item => item.Id), "collection", issues);
        AddDuplicateIssues(snapshot.Manifest.Modules?.Select(item => item.Id), "module", issues);
        AddDuplicateIssues(snapshot.Manifest.Projections?.Select(item => item.Id), "projection", issues);
        AddDuplicateIssues(snapshot.Manifest.DtoContracts?.Select(item => item.Id), "dto", issues);
        AddDuplicateIssues(snapshot.Manifest.EventTypes?.Select(item => item.Type), "eventType", issues);
        AddDuplicateIssues(snapshot.Manifest.HealthRefs?.Select(item => item.Id), "healthRef", issues);
        AddDuplicateIssues(snapshot.Manifest.DiagnosticRefs?.Select(item => item.Id), "diagnosticRef", issues);
        AddDuplicateIssues(snapshot.Schema.Collections?.Select(item => item.Id), "schema.collection", issues);
        AddDuplicateIssues(snapshot.Capabilities.Families.Select(item => item.FamilyId), "capability.family", issues);
        AddDuplicateIssues(snapshot.Health.Select(item => item.Id), "health", issues);
        AddDuplicateIssues(snapshot.Diagnostics.Select(item => item.Id), "diagnostic", issues);
        AddIdHygieneIssues(snapshot, issues);
        AddCollectionScopedIssues(snapshot.Schema.Collections, issues);
        AddProjectionScopedIssues(snapshot.Manifest.Projections, issues);
        AddCapabilityScopedIssues(snapshot.Capabilities.Families, issues);
        AddReferenceIssues(snapshot, issues);
        AddDependencyConflictIssues(snapshot, issues);

        var fatal = issues.Any(issue => issue.Severity == BaseRuntimeValidationSeverity.Fatal);
        return new BaseRuntimeValidationResult
        {
            Succeeded = !fatal,
            Issues = issues.Count == 0 ? null : issues.ToArray()
        };
    }

    private static void AddDuplicateIssues(
        IEnumerable<string>? ids,
        string scope,
        List<BaseRuntimeValidationIssue> issues)
    {
        if (ids is null)
        {
            return;
        }

        foreach (var duplicate in ids.Where(id => !string.IsNullOrWhiteSpace(id))
                     .GroupBy(id => id, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key))
        {
            issues.Add(new BaseRuntimeValidationIssue
            {
                Severity = BaseRuntimeValidationSeverity.Fatal,
                Kind = BaseRuntimeValidationFailureKind.DuplicateId,
                Code = "base.runtime.descriptor.duplicateId",
                Message = $"Duplicate {scope} id '{duplicate}'.",
                TargetRef = duplicate
            });
        }
    }

    private static void AddCollectionScopedIssues(
        CollectionDefinition[]? collections,
        List<BaseRuntimeValidationIssue> issues)
    {
        if (collections is null)
        {
            return;
        }

        var collectionIds = collections.Select(collection => collection.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var collection in collections)
        {
            AddDuplicateFieldIssues(collection, issues);
            AddDuplicateIndexIssues(collection, issues);
            AddUnresolvedRelationIssues(collection, collectionIds, issues);
            AddUnresolvedIndexIssues(collection, issues);
        }
    }

    private static void AddProjectionScopedIssues(
        ProjectionDescriptor[]? projections,
        List<BaseRuntimeValidationIssue> issues)
    {
        if (projections is null)
        {
            return;
        }

        foreach (var projection in projections)
        {
            AddDuplicateIssues(projection.Routes?.Select(route => route.OperationId), $"projection.{projection.Id}.route", issues);
            AddDuplicateIssues(projection.DtoContracts?.Select(dto => dto.Id), $"projection.{projection.Id}.dto", issues);
            AddDuplicateIssues(projection.Entrypoints?.Select(entrypoint => entrypoint.Id), $"projection.{projection.Id}.entrypoint", issues);

            var routeIds = (projection.Routes ?? [])
                .Select(route => route.OperationId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var entrypoint in projection.Entrypoints ?? [])
            {
                foreach (var routeRef in entrypoint.RouteRefs ?? [])
                {
                    if (routeIds.Contains(routeRef))
                    {
                        continue;
                    }

                    issues.Add(Unresolved(
                        "base.runtime.descriptor.unresolvedProjectionRoute",
                        $"Projection '{projection.Id}' entrypoint '{entrypoint.Id}' references missing route '{routeRef}'.",
                        routeRef,
                        $"projections.{projection.Id}.entrypoints.{entrypoint.Id}.routeRefs"));
                }
            }
        }
    }

    private static void AddCapabilityScopedIssues(
        CapabilityFamilyDescriptor[]? families,
        List<BaseRuntimeValidationIssue> issues)
    {
        if (families is null)
        {
            return;
        }

        foreach (var family in families)
        {
            AddDuplicateIssues(family.Features?.Select(feature => feature.FeatureId), $"capability.{family.FamilyId}.feature", issues);
            AddDuplicateIssues(family.Limits?.Select(limit => limit.Name), $"capability.{family.FamilyId}.limit", issues);
        }
    }

    private static void AddReferenceIssues(
        BaseDescriptorSnapshot snapshot,
        List<BaseRuntimeValidationIssue> issues)
    {
        var dtoIds = snapshot.Manifest.DtoContracts?.Select(dto => dto.Id).ToHashSet(StringComparer.Ordinal) ?? [];
        var routeIds = (snapshot.Manifest.Projections ?? [])
            .SelectMany(projection => projection.Routes ?? [])
            .Select(route => route.OperationId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var eventTypes = snapshot.Manifest.EventTypes?.Select(eventType => eventType.Type).ToHashSet(StringComparer.Ordinal) ?? [];
        var healthRefs = snapshot.Manifest.HealthRefs?.Select(health => health.Id).ToHashSet(StringComparer.Ordinal) ?? [];
        var diagnosticRefs = snapshot.Manifest.DiagnosticRefs?.Select(diagnostic => diagnostic.Id).ToHashSet(StringComparer.Ordinal) ?? [];
        var moduleIds = snapshot.Manifest.Modules?.Select(module => module.Id).ToHashSet(StringComparer.Ordinal) ?? [];
        var featureIds = snapshot.Capabilities.Families
            .SelectMany(family => family.Features ?? [])
            .Select(feature => feature.FeatureId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var projection in snapshot.Manifest.Projections ?? [])
        {
            foreach (var route in projection.Routes ?? [])
            {
                AddDtoReference(route.RequestDtoId, dtoIds, issues, $"projections.{projection.Id}.routes.{route.OperationId}.requestDtoId");
                AddDtoReference(route.ResponseDtoId, dtoIds, issues, $"projections.{projection.Id}.routes.{route.OperationId}.responseDtoId");
                AddDtoReference(route.ErrorDtoId, dtoIds, issues, $"projections.{projection.Id}.routes.{route.OperationId}.errorDtoId");
                AddDtoReference(route.ResultDtoId, dtoIds, issues, $"projections.{projection.Id}.routes.{route.OperationId}.resultDtoId");
                AddFeatureReferences(route.RequiredFeatureIds, featureIds, issues, $"projections.{projection.Id}.routes.{route.OperationId}.requiredFeatureIds");
            }

            AddReferenceSet(projection.HealthRefs, healthRefs, "base.runtime.descriptor.unresolvedHealthRef", "health ref", issues, $"projections.{projection.Id}.healthRefs");
            AddReferenceSet(projection.DiagnosticRefs, diagnosticRefs, "base.runtime.descriptor.unresolvedDiagnosticRef", "diagnostic ref", issues, $"projections.{projection.Id}.diagnosticRefs");
            foreach (var entrypoint in projection.Entrypoints ?? [])
            {
                AddFeatureReferences(entrypoint.RequiredFeatureIds, featureIds, issues, $"projections.{projection.Id}.entrypoints.{entrypoint.Id}.requiredFeatureIds");
            }
        }

        foreach (var family in snapshot.Capabilities.Families)
        {
            if (!string.IsNullOrWhiteSpace(family.OwnerModuleId) && !moduleIds.Contains(family.OwnerModuleId))
            {
                issues.Add(Unresolved(
                    "base.runtime.descriptor.unresolvedCapabilityOwner",
                    $"Capability family '{family.FamilyId}' references missing owner module '{family.OwnerModuleId}'.",
                    family.OwnerModuleId,
                    $"capabilities.{family.FamilyId}.ownerModuleId"));
            }

            foreach (var feature in family.Features ?? [])
            {
                AddReferenceSet(feature.DtoContracts, dtoIds, "base.runtime.descriptor.unresolvedDto", "DTO", issues, $"capabilities.{family.FamilyId}.features.{feature.FeatureId}.dtoContracts");
                AddReferenceSet(feature.RouteRefs, routeIds, "base.runtime.descriptor.unresolvedRoute", "route", issues, $"capabilities.{family.FamilyId}.features.{feature.FeatureId}.routeRefs");
                AddReferenceSet(feature.EventTypeRefs, eventTypes, "base.runtime.descriptor.unresolvedEventType", "event type", issues, $"capabilities.{family.FamilyId}.features.{feature.FeatureId}.eventTypeRefs");
                AddReference(feature.HealthRef, healthRefs, "base.runtime.descriptor.unresolvedHealthRef", "health ref", issues, $"capabilities.{family.FamilyId}.features.{feature.FeatureId}.healthRef");
                AddReferenceSet(feature.DiagnosticRefs, diagnosticRefs, "base.runtime.descriptor.unresolvedDiagnosticRef", "diagnostic ref", issues, $"capabilities.{family.FamilyId}.features.{feature.FeatureId}.diagnosticRefs");
            }

            foreach (var dependency in family.Dependencies ?? [])
            {
                AddReference(dependency.FeatureId, featureIds, "base.runtime.descriptor.unresolvedFeature", "feature", issues, $"capabilities.{family.FamilyId}.dependencies.featureId");
                AddReference(dependency.ModuleId, moduleIds, "base.runtime.descriptor.unresolvedModule", "module", issues, $"capabilities.{family.FamilyId}.dependencies.moduleId");
            }
        }

        foreach (var module in snapshot.Manifest.Modules ?? [])
        {
            AddReferenceSet(module.ContributedCapabilities, featureIds, "base.runtime.descriptor.unresolvedFeature", "feature", issues, $"modules.{module.Id}.contributedCapabilities");
            AddReferenceSet(module.ContributedDtoIds, dtoIds, "base.runtime.descriptor.unresolvedDto", "DTO", issues, $"modules.{module.Id}.contributedDtoIds");
            AddReferenceSet(module.ContributedRouteIds, routeIds, "base.runtime.descriptor.unresolvedRoute", "route", issues, $"modules.{module.Id}.contributedRouteIds");
            AddReferenceSet(module.ContributedEventTypes, eventTypes, "base.runtime.descriptor.unresolvedEventType", "event type", issues, $"modules.{module.Id}.contributedEventTypes");
            AddReferenceSet(module.ContributedHealthRefIds, healthRefs, "base.runtime.descriptor.unresolvedHealthRef", "health ref", issues, $"modules.{module.Id}.contributedHealthRefIds");
            AddReferenceSet(module.ContributedDiagnosticIds, diagnosticRefs, "base.runtime.descriptor.unresolvedDiagnosticRef", "diagnostic ref", issues, $"modules.{module.Id}.contributedDiagnosticIds");
        }
    }

    private static void AddIdHygieneIssues(
        BaseDescriptorSnapshot snapshot,
        List<BaseRuntimeValidationIssue> issues)
    {
        AddInvalidIds(snapshot.Manifest.Collections?.Select(item => (item.Id, "manifest.collections.id")), issues);
        AddInvalidIds(snapshot.Manifest.Modules?.Select(item => (item.Id, "manifest.modules.id")), issues);
        AddInvalidIds(snapshot.Manifest.Projections?.Select(item => (item.Id, "manifest.projections.id")), issues);
        AddInvalidIds(snapshot.Manifest.Projections?.SelectMany(projection => projection.Routes ?? []).Select(route => (route.OperationId, "manifest.projections.routes.operationId")), issues);
        AddInvalidIds(snapshot.Manifest.DtoContracts?.Select(item => (item.Id, "manifest.dtoContracts.id")), issues);
        AddInvalidIds(snapshot.Manifest.EventTypes?.Select(item => (item.Type, "manifest.eventTypes.type")), issues);
        AddInvalidIds(snapshot.Manifest.HealthRefs?.Select(item => (item.Id, "manifest.healthRefs.id")), issues);
        AddInvalidIds(snapshot.Manifest.DiagnosticRefs?.Select(item => (item.Id, "manifest.diagnosticRefs.id")), issues);
        AddInvalidIds(snapshot.Schema.Collections?.Select(item => (item.Id, "schema.collections.id")), issues);
        AddInvalidIds(snapshot.Schema.Collections?.SelectMany(collection => collection.Fields ?? []).Select(field => (field.Id, "schema.collections.fields.id")), issues);
        AddInvalidIds(snapshot.Schema.Collections?.SelectMany(collection => collection.Fields ?? []).Select(field => (field.Name, "schema.collections.fields.name")), issues);
        AddInvalidIds(snapshot.Capabilities.Families.Select(item => (item.FamilyId, "capabilities.families.familyId")), issues);
        AddInvalidIds(snapshot.Capabilities.Families.SelectMany(family => family.Features ?? []).Select(feature => (feature.FeatureId, "capabilities.families.features.featureId")), issues);
        AddInvalidIds(snapshot.Health.Select(item => (item.Id, "health.id")), issues);
        AddInvalidIds(snapshot.Diagnostics.Select(item => (item.Id, "diagnostics.id")), issues);
    }

    private static void AddInvalidIds(
        IEnumerable<(string Id, string Path)>? ids,
        List<BaseRuntimeValidationIssue> issues)
    {
        foreach (var (id, path) in ids ?? [])
        {
            if (IsValidDescriptorId(id))
            {
                continue;
            }

            issues.Add(new BaseRuntimeValidationIssue
            {
                Severity = BaseRuntimeValidationSeverity.Fatal,
                Kind = BaseRuntimeValidationFailureKind.InvalidContribution,
                Code = "base.runtime.descriptor.invalidId",
                Message = "Descriptor id must be non-empty, trimmed, and free of control characters.",
                TargetRef = id,
                TargetPath = path
            });
        }
    }

    private static bool IsValidDescriptorId(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && string.Equals(id, id.Trim(), StringComparison.Ordinal)
        && !id.Any(char.IsControl);

    private static void AddDependencyConflictIssues(
        BaseDescriptorSnapshot snapshot,
        List<BaseRuntimeValidationIssue> issues)
    {
        var modules = snapshot.Manifest.Modules?.ToDictionary(module => module.Id, StringComparer.Ordinal) ?? [];
        var features = snapshot.Capabilities.Families
            .SelectMany(family => family.Features ?? [])
            .GroupBy(feature => feature.FeatureId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var family in snapshot.Capabilities.Families)
        {
            foreach (var dependency in family.Dependencies ?? [])
            {
                if (!dependency.Required)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(dependency.FeatureId)
                    && features.TryGetValue(dependency.FeatureId, out var feature)
                    && feature.Status != CapabilityStatus.Available)
                {
                    issues.Add(DependencyConflict(
                        $"Capability family '{family.FamilyId}' requires unavailable feature '{dependency.FeatureId}'.",
                        dependency.FeatureId,
                        $"capabilities.{family.FamilyId}.dependencies.featureId"));
                }

                if (!string.IsNullOrWhiteSpace(dependency.ModuleId)
                    && modules.TryGetValue(dependency.ModuleId, out var module)
                    && module.Status != ModuleStatus.Installed)
                {
                    issues.Add(DependencyConflict(
                        $"Capability family '{family.FamilyId}' requires unavailable module '{dependency.ModuleId}'.",
                        dependency.ModuleId,
                        $"capabilities.{family.FamilyId}.dependencies.moduleId"));
                }
            }
        }

        foreach (var module in snapshot.Manifest.Modules ?? [])
        {
            foreach (var dependency in module.Dependencies ?? [])
            {
                if (!dependency.Required)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(dependency.ModuleId)
                    && modules.TryGetValue(dependency.ModuleId, out var dependencyModule)
                    && dependencyModule.Status != ModuleStatus.Installed)
                {
                    issues.Add(DependencyConflict(
                        $"Module '{module.Id}' requires unavailable module '{dependency.ModuleId}'.",
                        dependency.ModuleId,
                        $"modules.{module.Id}.dependencies.moduleId"));
                }

                if (!string.IsNullOrWhiteSpace(dependency.FeatureId)
                    && features.TryGetValue(dependency.FeatureId, out var dependencyFeature)
                    && dependencyFeature.Status != CapabilityStatus.Available)
                {
                    issues.Add(DependencyConflict(
                        $"Module '{module.Id}' requires unavailable feature '{dependency.FeatureId}'.",
                        dependency.FeatureId,
                        $"modules.{module.Id}.dependencies.featureId"));
                }
            }
        }
    }

    private static void AddDtoReference(
        string? dtoId,
        HashSet<string> dtoIds,
        List<BaseRuntimeValidationIssue> issues,
        string targetPath) =>
        AddReference(dtoId, dtoIds, "base.runtime.descriptor.unresolvedDto", "DTO", issues, targetPath);

    private static void AddFeatureReferences(
        string[]? featureIds,
        HashSet<string> availableFeatureIds,
        List<BaseRuntimeValidationIssue> issues,
        string targetPath) =>
        AddReferenceSet(featureIds, availableFeatureIds, "base.runtime.descriptor.unresolvedFeature", "feature", issues, targetPath);

    private static void AddReferenceSet(
        string[]? refs,
        HashSet<string> knownRefs,
        string code,
        string label,
        List<BaseRuntimeValidationIssue> issues,
        string targetPath)
    {
        foreach (var reference in refs ?? [])
        {
            AddReference(reference, knownRefs, code, label, issues, targetPath);
        }
    }

    private static void AddReference(
        string? reference,
        HashSet<string> knownRefs,
        string code,
        string label,
        List<BaseRuntimeValidationIssue> issues,
        string targetPath)
    {
        if (string.IsNullOrWhiteSpace(reference) || knownRefs.Contains(reference))
        {
            return;
        }

        issues.Add(Unresolved(
            code,
            $"Descriptor references missing {label} '{reference}'.",
            reference,
            targetPath));
    }

    private static void AddDuplicateFieldIssues(
        CollectionDefinition collection,
        List<BaseRuntimeValidationIssue> issues)
    {
        AddDuplicateFieldIssue(collection, collection.Fields?.Select(field => field.Id), "field id", "id", issues);
        AddDuplicateFieldIssue(collection, collection.Fields?.Select(field => field.Name), "field name", "name", issues);
    }

    private static void AddDuplicateFieldIssue(
        CollectionDefinition collection,
        IEnumerable<string>? values,
        string label,
        string path,
        List<BaseRuntimeValidationIssue> issues)
    {
        if (values is null)
        {
            return;
        }

        foreach (var duplicate in values.Where(value => !string.IsNullOrWhiteSpace(value))
                     .GroupBy(value => value, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key))
        {
            issues.Add(new BaseRuntimeValidationIssue
            {
                Severity = BaseRuntimeValidationSeverity.Fatal,
                Kind = BaseRuntimeValidationFailureKind.DuplicateId,
                Code = "base.runtime.descriptor.duplicateField",
                Message = $"Collection '{collection.Id}' has duplicate {label} '{duplicate}'.",
                TargetRef = collection.Id,
                TargetPath = $"collections.{collection.Id}.fields.{path}.{duplicate}"
            });
        }
    }

    private static void AddUnresolvedRelationIssues(
        CollectionDefinition collection,
        HashSet<string> collectionIds,
        List<BaseRuntimeValidationIssue> issues)
    {
        foreach (var field in collection.Fields ?? [])
        {
            var targetCollectionId = field.Relation?.TargetCollectionId;
            if (!string.IsNullOrWhiteSpace(targetCollectionId) && !collectionIds.Contains(targetCollectionId))
            {
                issues.Add(new BaseRuntimeValidationIssue
                {
                    Severity = BaseRuntimeValidationSeverity.Fatal,
                    Kind = BaseRuntimeValidationFailureKind.UnresolvedReference,
                    Code = "base.runtime.descriptor.unresolvedRelation",
                    Message = $"Field '{field.Name}' on collection '{collection.Id}' references missing collection '{targetCollectionId}'.",
                    TargetRef = targetCollectionId,
                    TargetPath = $"collections.{collection.Id}.fields.{field.Name}.relation.targetCollectionId"
                });
            }
        }
    }

    private static void AddDuplicateIndexIssues(
        CollectionDefinition collection,
        List<BaseRuntimeValidationIssue> issues)
    {
        AddDuplicateIndexIssue(collection, collection.Indexes?.Select(index => index.Id), "index id", "id", issues);
        AddDuplicateIndexIssue(collection, collection.Indexes?.Select(index => index.Name), "index name", "name", issues);
    }

    private static void AddDuplicateIndexIssue(
        CollectionDefinition collection,
        IEnumerable<string>? values,
        string label,
        string path,
        List<BaseRuntimeValidationIssue> issues)
    {
        if (values is null)
        {
            return;
        }

        foreach (var duplicate in values.Where(value => !string.IsNullOrWhiteSpace(value))
                     .GroupBy(value => value, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key))
        {
            issues.Add(new BaseRuntimeValidationIssue
            {
                Severity = BaseRuntimeValidationSeverity.Fatal,
                Kind = BaseRuntimeValidationFailureKind.DuplicateId,
                Code = "base.runtime.descriptor.duplicateIndex",
                Message = $"Collection '{collection.Id}' has duplicate {label} '{duplicate}'.",
                TargetRef = collection.Id,
                TargetPath = $"collections.{collection.Id}.indexes.{path}.{duplicate}"
            });
        }
    }

    private static void AddUnresolvedIndexIssues(
        CollectionDefinition collection,
        List<BaseRuntimeValidationIssue> issues)
    {
        if (collection.Indexes is null)
        {
            return;
        }

        var fieldRefs = (collection.Fields ?? [])
            .SelectMany(field => new[] { field.Id, field.Name })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var index in collection.Indexes)
        {
            if (!string.Equals(index.CollectionId, collection.Id, StringComparison.Ordinal))
            {
                issues.Add(new BaseRuntimeValidationIssue
                {
                    Severity = BaseRuntimeValidationSeverity.Fatal,
                    Kind = BaseRuntimeValidationFailureKind.UnresolvedReference,
                    Code = "base.runtime.descriptor.unresolvedIndexCollection",
                    Message = $"Index '{index.Name}' on collection '{collection.Id}' references collection '{index.CollectionId}'.",
                    TargetRef = index.CollectionId,
                    TargetPath = $"collections.{collection.Id}.indexes.{index.Name}.collectionId"
                });
            }

            foreach (var part in index.Parts ?? [])
            {
                if (part.Kind != IndexPartKind.Field)
                {
                    continue;
                }

                var fieldRef = TopLevelFieldPath(part.FieldPath);
                if (!string.IsNullOrWhiteSpace(fieldRef) && fieldRefs.Contains(fieldRef))
                {
                    continue;
                }

                issues.Add(new BaseRuntimeValidationIssue
                {
                    Severity = BaseRuntimeValidationSeverity.Fatal,
                    Kind = BaseRuntimeValidationFailureKind.UnresolvedReference,
                    Code = "base.runtime.descriptor.unresolvedIndexField",
                    Message = $"Index '{index.Name}' on collection '{collection.Id}' references missing field '{part.FieldPath}'.",
                    TargetRef = part.FieldPath,
                    TargetPath = $"collections.{collection.Id}.indexes.{index.Name}.parts.fieldPath"
                });
            }
        }
    }

    private static string? TopLevelFieldPath(string? fieldPath)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return fieldPath;
        }

        var dotIndex = fieldPath.IndexOf('.');
        return dotIndex < 0 ? fieldPath : fieldPath[..dotIndex];
    }

    private static BaseRuntimeValidationIssue Unresolved(
        string code,
        string message,
        string? targetRef,
        string targetPath) => new()
        {
            Severity = BaseRuntimeValidationSeverity.Fatal,
            Kind = BaseRuntimeValidationFailureKind.UnresolvedReference,
            Code = code,
            Message = message,
            TargetRef = targetRef,
            TargetPath = targetPath
        };

    private static BaseRuntimeValidationIssue DependencyConflict(
        string message,
        string? targetRef,
        string targetPath) => new()
        {
            Severity = BaseRuntimeValidationSeverity.Fatal,
            Kind = BaseRuntimeValidationFailureKind.CapabilityDependencyConflict,
            Code = "base.runtime.descriptor.dependencyConflict",
            Message = message,
            TargetRef = targetRef,
            TargetPath = targetPath
        };
}
