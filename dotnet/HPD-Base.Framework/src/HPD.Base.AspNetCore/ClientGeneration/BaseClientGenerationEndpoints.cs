using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Base;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

internal static class BaseClientGenerationEndpoints
{
    internal static void Map(
        IEndpointRouteBuilder endpoints,
        HPDBaseEndpointAudience audience,
        Action<IEndpointConventionBuilder, HPDBaseEndpointDescriptor>? convention = null)
    {
        string id = audience == HPDBaseEndpointAudience.Application
            ? "base.clientGeneration.application"
            : "base.clientGeneration.controlPlane";
        string capability = audience == HPDBaseEndpointAudience.Application
            ? HPDBaseCapabilities.ClientGenerate
            : HPDBaseCapabilities.AdministrationClientGenerate;
        endpoints.MapGet("/client-generation", (RequestDelegate)HandleAsync)
            .WithHPDBaseEndpoint(id, audience, HPDBaseEndpointOperation.ClientGenerationRead, capability, convention)
            .WithName(id);
    }

    private static async Task HandleAsync(HttpContext context)
    {
        BaseClientGenerationSnapshotBuilder builder = context.RequestServices.GetRequiredService<BaseClientGenerationSnapshotBuilder>();
        CancellationToken cancellationToken = context.RequestAborted;
        OperationResult<BaseClientGenerationSnapshotV2> result = await builder.BuildAsync(context, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess())
        {
            byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(result.Value, HPDBaseClientGenerationJsonContext.Default.BaseClientGenerationSnapshotV2);
            byte[] canonical = BaseClientGenerationSnapshotBuilder.Canonicalize(serialized);
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength = canonical.Length;
            await context.Response.Body.WriteAsync(canonical, cancellationToken).ConfigureAwait(false);
            return;
        }
        await Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "The client generation snapshot is unavailable.",
            extensions: new Dictionary<string, object?> { ["code"] = result.Error?.Code ?? "base.clientGeneration.inventoryUnavailable" }).ExecuteAsync(context).ConfigureAwait(false);
    }
}

internal sealed class BaseClientGenerationSnapshotBuilder(
    EndpointDataSource endpointDataSource,
    BaseCollectionRegistry collections,
    BaseReadRegistry reads,
    IRecordStoreRegistry stores,
    IBaseDescriptorRegistry descriptors,
    HPDBaseInstalledFeatures installedFeatures,
    HPDBaseAspNetCoreSnapshot aspNetCore,
    BaseLogicalSchema logicalSchema,
    IHPDBaseApplication application,
    TimeProvider timeProvider,
    IServiceProvider services,
    BaseSelectionProfileRegistry? selectionProfiles = null,
    BaseModuleMutationRegistry? moduleMutations = null,
    BaseSubjectLifecycleRegistry? lifecycleConsumers = null,
    IBaseSubjectLifecycleRuntime? lifecycleRuntime = null,
    BaseSubjectRetirementRegistry? retirementConsumers = null,
    IBaseSubjectRetirementRuntime? retirementRuntime = null,
    IBaseSessionFactory? sessions = null,
    IBaseHttpPrincipalContextFactory? principalFactory = null,
    IBasePolicyOrchestrator? policy = null)
{
    private const int MaximumSnapshotBytes = 4 * 1024 * 1024;

    internal async ValueTask<OperationResult<BaseClientGenerationSnapshotV2>> BuildAsync(HttpContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HPDBaseEndpointDescriptor? current = context.GetEndpoint()?.Metadata.GetMetadata<HPDBaseEndpointDescriptor>();
        if (current is null || current.Operation != HPDBaseEndpointOperation.ClientGenerationRead)
            return Failure("base.clientGeneration.inventoryUnavailable");

        PrincipalContext? generationPrincipal = principalFactory is null ? null : await principalFactory.CreateAsync(context, cancellationToken).ConfigureAwait(false);
        var authorizedTextIndexes = new HashSet<string>(StringComparer.Ordinal);
        if (generationPrincipal is not null && policy is not null)
        {
            foreach (CollectionDefinition collection in collections.Collections.Values)
            foreach (BaseTextIndexDefinition index in collection.TextIndexes ?? [])
            {
                if (index.Audience != current.Audience) continue;
                var operation = new OperationContext { ApplicationId = logicalSchema.ApplicationId, Audience = current.Audience, Operation = BaseOperationKind.TextQuery, CollectionId = collection.Id, TenantId = generationPrincipal.CurrentTenantId, ProjectId = context.Request.Query["projectId"].Count == 1 ? context.Request.Query["projectId"][0] : null, Now = timeProvider.GetUtcNow() };
                OperationResult<BasePolicyEvaluation> admitted = await policy.EvaluateReadAsync(new BasePolicyRequest { Principal = generationPrincipal, Operation = operation, Collection = collection, ResourceKind = PolicyResourceKind.TextIndex, TextIndexId = index.Id }, cancellationToken).ConfigureAwait(false);
                if (admitted.IsSuccess() && BaseSystemCollectionGate.HasExactTextGrant(admitted, BaseTextGrants.Query, generationPrincipal, operation, collection.Id, index.Id)) authorizedTextIndexes.Add(collection.Id + "\n" + index.Id);
            }
        }

        var authorizedLifecycle = new List<BaseInstalledSubjectLifecycleConsumer>();
        var authorizedLifecycleReconciliation = new HashSet<string>(StringComparer.Ordinal);
        var authorizedRetirement = new Dictionary<string, BaseInstalledSubjectRetirementConsumer>(StringComparer.Ordinal);
        string? lifecycleAudience = null;
        if (current.Audience == HPDBaseEndpointAudience.Application && lifecycleConsumers is not null && lifecycleRuntime is not null && sessions is not null && principalFactory is not null)
        {
            PrincipalContext principal = generationPrincipal!;
            lifecycleAudience = principal.AuthenticationState == PrincipalAuthenticationState.System ? "system"
                : principal.AuthenticationState == PrincipalAuthenticationState.Service ? "service" : null;
            BaseSession session = sessions.For(principal, options => options.ProjectId = context.Request.Query["projectId"].Count == 1 ? context.Request.Query["projectId"][0] : null);
            foreach (BaseInstalledSubjectLifecycleConsumer candidate in lifecycleConsumers.All.OrderBy(static value => value.Definition.Id, StringComparer.Ordinal).ThenBy(static value => value.Definition.Version))
                if (await lifecycleRuntime.AuthorizeGenerationAsync(session, candidate, cancellationToken).ConfigureAwait(false))
                {
                    authorizedLifecycle.Add(candidate);
                    if (await lifecycleRuntime.AuthorizeReconciliationGenerationAsync(session, candidate, cancellationToken).ConfigureAwait(false))
                        authorizedLifecycleReconciliation.Add(candidate.Definition.Id + "\n" + candidate.Definition.Version);
                    BaseInstalledSubjectRetirementConsumer? retirement=retirementConsumers?.FindConsumer(candidate.Definition.Id,candidate.Definition.Version);
                    if(retirement is not null&&retirementRuntime is not null&&await retirementRuntime.AuthorizeGenerationAsync(session,retirement,cancellationToken).ConfigureAwait(false))
                        authorizedRetirement.Add(candidate.Definition.Id+"\n"+candidate.Definition.Version,retirement);
                }
        }

        RouteEndpoint[] materialized = endpointDataSource.Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<HPDBaseEndpointDescriptor>() is { } descriptor
                && descriptor.Audience == current.Audience
                && descriptor.Operation != HPDBaseEndpointOperation.ModuleMutation
                && (descriptor.Operation switch
                {
                    HPDBaseEndpointOperation.SubjectLifecycleRead or HPDBaseEndpointOperation.SubjectLifecycleCheckpoint => authorizedLifecycle.Count != 0,
                    HPDBaseEndpointOperation.SubjectLifecycleReconciliationRead => authorizedLifecycleReconciliation.Count != 0,
                    _ => true,
                }))
            .OrderBy(endpoint => endpoint.Metadata.GetMetadata<HPDBaseEndpointDescriptor>()!.EndpointId, StringComparer.Ordinal)
            .ToArray();
        if (materialized.Length is 0 or > 256)
            return Failure("base.clientGeneration.inventoryUnavailable");

        IReadOnlyDictionary<string, RouteDescriptor> routeContracts = descriptors.Current.Manifest.Projections?
            .SelectMany(projection => projection.Routes ?? [])
            .GroupBy(route => route.OperationId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal)
            ?? new Dictionary<string, RouteDescriptor>(StringComparer.Ordinal);
        BaseClientEndpointDescriptor[] endpoints;
        try { endpoints = materialized.Select(endpoint => ToEndpoint(endpoint, routeContracts, reads, aspNetCore.Limits.MaxRequestBodyLength, aspNetCore.Administration.MaxArtifactBytes)).ToArray(); }
        catch (InvalidOperationException) { return Failure("base.clientGeneration.contractMissing"); }
        BaseClientCollectionDescriptor[] generatedCollections = collections.Collections.Values
            .Where(collection => collection.Enabled && collection.Exposed)
            .OrderBy(collection => collection.Id, StringComparer.Ordinal)
            .Select(collection => ToCollection(collection, stores, endpoints, installedFeatures))
            .ToArray();
        if (generatedCollections.Length > 256)
            return Failure("base.clientGeneration.snapshotTooLarge");

        IBaseReadRegistration[] installedReads = reads.Registrations.Values
            .Where(read => endpoints.Any(endpoint => endpoint.Id.EndsWith("." + read.Id, StringComparison.Ordinal) && endpoint.Operation == "RegisteredRead"))
            .OrderBy(read => read.Id, StringComparer.Ordinal).ToArray();
        BaseDependencyTemplate[] templates = (services.GetService<IBaseDependencyTemplateProvider>()?.Templates ?? [])
            .Where(template => template.Visibility == BaseDependencyVisibility.Public || current.Audience == HPDBaseEndpointAudience.ControlPlane && template.Visibility == BaseDependencyVisibility.Admin)
            .OrderBy(template => template.Id, StringComparer.Ordinal).ToArray();
        BaseSelectionOperationProfile[] graphSelections = (selectionProfiles?.All ?? []).Where(static profile => profile.HttpProjection?.GenerateL41Client == true).ToArray();
        IBaseModuleMutationRegistration[] graphModules = current.Audience == HPDBaseEndpointAudience.ControlPlane
            ? (moduleMutations?.Registrations ?? []).OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray()
            : [];
        BaseClientNamedTypeDescriptor[] types = BuildTypes(collections.Collections.Values, installedReads, templates.Length != 0, graphSelections, logicalSchema.ExportedSubjects)
            .Concat(graphModules.SelectMany(ModuleTypes)).Concat(authorizedLifecycle.Count == 0 ? [] : SubjectLifecycleEndpoints.LifecycleTypes())
            .OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        if (types.Length > 512)
            return Failure("base.clientGeneration.snapshotTooLarge");

        string schemaGeneration = (application.CurrentReadiness.SchemaGeneration ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string endpointDigest = Hash(Canonicalize(JsonSerializer.SerializeToUtf8Bytes(endpoints, HPDBaseClientGenerationJsonContext.Default.BaseClientEndpointDescriptorArray)));
        string basePath = BasePath(context.Request.Path.Value ?? "/base/client-generation");
        string audience = current.Audience == HPDBaseEndpointAudience.Application
            ? authorizedLifecycle.Count == 0 ? "application" : lifecycleAudience!
            : "controlPlane";
        BaseClientCapabilityDescriptor[] capabilities = endpoints
            .Where(endpoint => endpoint.Capability is not null)
            .Select(endpoint => endpoint.Capability!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => new BaseClientCapabilityDescriptor { Id = id, Available = true })
            .ToArray();
        BaseClientVectorIndexDescriptor[] vectors = BuildVectors(collections.Collections.Values);
        BaseClientTextIndexDescriptor[] textIndexes = BuildTextIndexes(collections.Collections.Values, authorizedTextIndexes);
        BaseClientSelectionMutationDescriptor[] selectionMutations = (selectionProfiles?.All ?? [])
            .Where(profile => profile.HttpProjection is { GenerateL41Client: true } projection
                && (projection.Audience == BaseSelectionEndpointAudience.Application ? "application" : "controlPlane") == audience)
            .OrderBy(static profile => profile.Id, StringComparer.Ordinal)
            .Select(profile => new BaseClientSelectionMutationDescriptor
            {
                Id = profile.Id, Version = profile.Version,
                Checksum = BaseSelectionProfileChecksum.Compute(profile), CollectionId = profile.CollectionId,
                GeneratedName = GeneratedName(profile.Id), MutationKind = profile.MutationKind == BaseSelectionMutationKind.MergePatch ? "mergePatch" : "delete",
                EndpointId = $"base.selection-mutations.{profile.Id}.execute",
                Route = $"{basePath}/selection-mutations/{profile.HttpProjection!.RouteName}/execute",
                MaximumSelectedRecords = profile.Limits.MaximumSelectedRecords,
                MaximumRequestBodyBytes = profile.HttpProjection.MaximumRequestBodyBytes,
                RequestTypeId = $"selection.{profile.Id}.request", ResultTypeId = "base.selection.result",
            }).ToArray();
        BaseClientModuleMutationDescriptor[] generatedModules = graphModules.Select(registration => new BaseClientModuleMutationDescriptor
        {
            Id = registration.Id, Version = registration.Version, GeneratedName = GeneratedName(registration.Id),
            Audience = registration.Audience == BaseModuleMutationAudience.System ? "system" : "service",
            RequestTypeId = registration.RequestTypeId, ResultTypeId = registration.ResultTypeId,
            Route = $"{basePath}/module-mutations/v1/{registration.Id}:execute",
            MaximumRequestBytes = moduleMutations!.Find(registration.Id, registration.Version)!.Limits.MaximumRequestBytes,
        }).ToArray();
        BaseClientSubjectLifecycleConsumerDescriptor[] generatedLifecycle = [.. authorizedLifecycle.Select(value =>
        {
            authorizedRetirement.TryGetValue(value.Definition.Id+"\n"+value.Definition.Version,out BaseInstalledSubjectRetirementConsumer? retirement);
            return new BaseClientSubjectLifecycleConsumerDescriptor
        {
            Id=value.Definition.Id,Version=value.Definition.Version,Checksum=value.Checksum,GeneratedName=GeneratedName(value.Definition.Id),
            Audience=value.Definition.Audience==BaseSubjectLifecycleConsumerAudience.System?"system":"service",ContractId=value.Definition.ContractId,ContractVersion=value.Definition.ContractVersion,
            ObservedStates=[..value.Definition.ObservedStates.Select(static state=>state.ToString().ToLowerInvariant())],ReadRoute=basePath+"/subject-lifecycle/feed/read",CheckpointRoute=basePath+"/subject-lifecycle/feed/checkpoints",
            ReconciliationRoute=authorizedLifecycleReconciliation.Contains(value.Definition.Id+"\n"+value.Definition.Version)?basePath+"/subject-lifecycle/reconciliation/read":null,
            RetirementParticipation=retirement is null?"observeOnly":retirement.Definition.Participation==BaseSubjectRetirementParticipation.AdvisoryAcknowledgement?"advisory":"required",
            AcknowledgementRoute=retirement is null?null:basePath+"/subject-retirement/acknowledgements",RetirementChecksum=retirement?.Checksum,
            MaximumFactsPerPage=value.Definition.Limits.MaximumFactsPerPage,MaximumResultBytes=value.Definition.Limits.MaximumResultBytes,
        };})];
        if (capabilities.Length > 256 || installedReads.Length > 256 || templates.Length > 512 || vectors.Length > 256 || textIndexes.Length > 256 || selectionMutations.Length > 256 || generatedModules.Length > 256)
            return Failure("base.clientGeneration.snapshotTooLarge");
        var generatedNames = new HashSet<string>(["reads", "files", "close", "collection", "connectivity", "$control", "$dynamic"], StringComparer.Ordinal);
        if (generatedCollections.Any(collection => !generatedNames.Add(collection.GeneratedName))
            || installedReads.Any(read => !generatedNames.Add("read:" + GeneratedName(read.Id)))
            || vectors.GroupBy(vector => vector.CollectionId, StringComparer.Ordinal).Any(group => group.Select(vector => vector.GeneratedName).Distinct(StringComparer.Ordinal).Count() != group.Count()))
            return Failure("base.clientGeneration.nameCollision");

        var snapshot = new BaseClientGenerationSnapshotV2
        {
            Protocol = new BaseClientProtocolDescriptor
            {
                ApplicationId = logicalSchema.ApplicationId,
                SchemaGeneration = schemaGeneration,
                EndpointInventoryDigest = endpointDigest,
                GeneratedAt = string.Empty
            },
            Application = new BaseClientApplicationDescriptor { ApplicationId = logicalSchema.ApplicationId, Audience = audience, BasePath = basePath },
            Schema = new BaseClientSchemaDescriptor { Generation = schemaGeneration, Collections = generatedCollections, Types = types },
            Endpoints = endpoints,
            Capabilities = capabilities,
            RegisteredReads = installedReads.Select(read => new BaseClientReadDescriptor
            {
                Id = read.Id,
                GeneratedName = GeneratedName(read.Id),
                EndpointId = endpoints.Single(endpoint => endpoint.Id.EndsWith("." + read.Id, StringComparison.Ordinal)).Id,
                ParameterTypeId = read.ClientContract.ParameterTypeId,
                RowTypeId = read.ClientContract.RowTypeId,
                MaxPageSize = 500,
                Watchable = installedFeatures.LiveQueries && installedFeatures.Dependencies && endpoints.Any(endpoint => endpoint.Operation == "RealtimeSubscribe")
            }).ToArray(),
            DependencyTemplates = templates.Select(template => new BaseClientDependencyTemplateDescriptor
            {
                Id = template.Id,
                Kind = LowerCamel(template.Kind.ToString()),
                Visibility = template.Visibility == BaseDependencyVisibility.Public ? "public" : "controlPlane",
                ParameterTypeIds = template.ParameterNames.Select(_ => "base.dependency.parameter").ToArray()
            }).ToArray(),
            VectorIndexes = vectors,
            TextIndexes = textIndexes,
            SelectionMutations = selectionMutations,
            ModuleMutations = generatedModules,
            SubjectLifecycleConsumers = generatedLifecycle,
            Errors = ErrorTaxonomy(endpoints),
            Digest = string.Empty
        };
        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(snapshot, HPDBaseClientGenerationJsonContext.Default.BaseClientGenerationSnapshotV2);
        byte[] canonical = Canonicalize(serialized, snapshotDigestInput: true);
        if (canonical.Length > MaximumSnapshotBytes)
            return Failure("base.clientGeneration.snapshotTooLarge");
        snapshot = snapshot with
        {
            Digest = Hash(canonical),
            Protocol = snapshot.Protocol with { GeneratedAt = timeProvider.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture) }
        };
        if (JsonSerializer.SerializeToUtf8Bytes(snapshot, HPDBaseClientGenerationJsonContext.Default.BaseClientGenerationSnapshotV2).Length > MaximumSnapshotBytes)
            return Failure("base.clientGeneration.snapshotTooLarge");
        return new OperationResult<BaseClientGenerationSnapshotV2> { Status = OperationStatus.Ok, Value = snapshot };
    }

    private static IEnumerable<BaseClientNamedTypeDescriptor> ModuleTypes(IBaseModuleMutationRegistration registration)
    {
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (BaseClientNamedTypeDescriptor type in Walk(registration.RequestTypeInfo, registration.RequestTypeId)) yield return type;
        foreach (BaseClientNamedTypeDescriptor type in Walk(registration.ResultTypeInfo, registration.ResultTypeId)) yield return type;

        IEnumerable<BaseClientNamedTypeDescriptor> Walk(System.Text.Json.Serialization.Metadata.JsonTypeInfo metadata, string id)
        {
            if (!emitted.Add(id)) yield break;
            Type type = metadata.Type;
            Type? nullable = Nullable.GetUnderlyingType(type);
            if (nullable is not null) type = nullable;
            BaseClientTypeNode? scalar = Scalar(type);
            if (scalar is not null) { yield return new() { Id = id, Node = scalar }; yield break; }
            if (type.IsArray || type.IsGenericType && type.GetGenericTypeDefinition() is var generic
                && (generic == typeof(List<>) || generic == typeof(IReadOnlyList<>) || generic == typeof(IList<>)
                    || generic == typeof(IEnumerable<>) || generic == typeof(System.Collections.Immutable.ImmutableArray<>)))
            {
                Type element = type.IsArray ? type.GetElementType()! : type.GetGenericArguments()[0];
                string elementId = id + ".item";
                System.Text.Json.Serialization.Metadata.JsonTypeInfo elementMetadata = metadata.Options.GetTypeInfo(element);
                foreach (BaseClientNamedTypeDescriptor child in Walk(elementMetadata, elementId)) yield return child;
                yield return new() { Id = id, Node = new() { Kind = "array", ElementTypeId = elementId, MinItems = 0, MaxItems = 256 } };
                yield break;
            }
            if (metadata.Kind != System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object)
                throw new InvalidOperationException("base.clientGeneration.typeMissing");
            var properties = new List<BaseClientPropertyDescriptor>();
            BaseSerializerPropertyDeclaration[] declarations = registration.SerializerDeclarations
                .Where(declaration => declaration.IsDeclaredOn(type) && !declaration.Ignored)
                .OrderBy(static declaration => declaration.ApplicationName, StringComparer.Ordinal).ToArray();
            foreach (BaseSerializerPropertyDeclaration declaration in declarations)
            {
                string expectedWireName = declaration.ExplicitWireName
                    ?? metadata.Options.PropertyNamingPolicy?.ConvertName(declaration.ApplicationName)
                    ?? declaration.ApplicationName;
                System.Text.Json.Serialization.Metadata.JsonPropertyInfo property = metadata.Properties.SingleOrDefault(candidate =>
                    string.Equals(candidate.Name, expectedWireName, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("base.clientGeneration.typeMissing");
                if (!declaration.HasPropertyType(property.PropertyType) || property.Get is null && property.Set is null)
                    throw new InvalidOperationException("base.clientGeneration.typeMissing");
                string childId = id + "." + declaration.ApplicationName;
                foreach (BaseClientNamedTypeDescriptor child in Walk(metadata.Options.GetTypeInfo(property.PropertyType), childId)) yield return child;
                properties.Add(new BaseClientPropertyDescriptor
                {
                    Name = declaration.ApplicationName, WireName = property.Name, TypeId = childId,
                    Required = declaration.Required, Nullable = declaration.Nullable, DisclosureShape = "none",
                });
            }
            if (metadata.Properties.Count(property => property.Get is not null || property.Set is not null) != declarations.Length)
                throw new InvalidOperationException("base.clientGeneration.typeMissing");
            yield return new() { Id = id, Node = new() { Kind = "object", AdditionalProperties = false, Properties = properties.ToArray() } };
        }

        static BaseClientTypeNode? Scalar(Type type)
        {
            if (type == typeof(string)) return new() { Kind = "string", Format = "plain", MinLength = 0, MaxLength = 1_048_576 };
            if (type == typeof(bool)) return new() { Kind = "boolean" };
            if (type == typeof(byte[])) return new() { Kind = "bytes", Wire = "base64", MaxBytes = 1_048_576 };
            if (type == typeof(decimal)) return new() { Kind = "decimal", Wire = "decimal-string" };
            if (type == typeof(DateTimeOffset) || type == typeof(DateTime)) return new() { Kind = "string", Format = "utc-instant", MinLength = 20, MaxLength = 35 };
            if (type.IsEnum) return new() { Kind = "enum", Values = Enum.GetNames(type) };
            if (type == typeof(byte)) return Integer(byte.MinValue, byte.MaxValue);
            if (type == typeof(sbyte)) return Integer(sbyte.MinValue, sbyte.MaxValue);
            if (type == typeof(short)) return Integer(short.MinValue, short.MaxValue);
            if (type == typeof(ushort)) return Integer(ushort.MinValue, ushort.MaxValue);
            if (type == typeof(int)) return Integer(int.MinValue, int.MaxValue);
            if (type == typeof(uint)) return Integer(uint.MinValue, uint.MaxValue);
            if (type == typeof(long)) return Integer(long.MinValue, long.MaxValue);
            if (type == typeof(BaseModuleGeneration)) return new() { Kind = "module-generation" };
            return null;

            static BaseClientTypeNode Integer<T>(T minimum, T maximum) where T : struct, System.IFormattable => new()
            {
                Kind = "integer", Minimum = minimum.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                Maximum = maximum.ToString(null, System.Globalization.CultureInfo.InvariantCulture), Wire = "decimal-string",
            };
        }
    }

    private static BaseClientEndpointDescriptor ToEndpoint(RouteEndpoint endpoint, IReadOnlyDictionary<string, RouteDescriptor> routes, BaseReadRegistry reads, long maximumRequestBodyBytes, long maximumArtifactBytes)
    {
        HPDBaseEndpointDescriptor descriptor = endpoint.Metadata.GetMetadata<HPDBaseEndpointDescriptor>()!;
        RouteDescriptor route;
        if (!routes.TryGetValue(descriptor.EndpointId, out route!))
        {
            route = descriptor.EndpointId switch
            {
                "hpd.base.vector.query" => new RouteDescriptor { OperationId = descriptor.EndpointId, Method = HttpMethodKind.Post, Path = endpoint.RoutePattern.RawText ?? "", RequestDtoId = "base.vector.query.request", ResponseDtoId = "base.vector.query.result" },
                "hpd.base.vector.metadata.list" => new RouteDescriptor { OperationId = descriptor.EndpointId, Method = HttpMethodKind.Get, Path = endpoint.RoutePattern.RawText ?? "", ResponseDtoId = "base.vector.indexStatus.array" },
                "hpd.base.vector.diagnostics.read" => new RouteDescriptor { OperationId = descriptor.EndpointId, Method = HttpMethodKind.Get, Path = endpoint.RoutePattern.RawText ?? "", ResponseDtoId = "base.vector.indexStatus" },
                "hpd.base.vector.rebuild" => new RouteDescriptor { OperationId = descriptor.EndpointId, Method = HttpMethodKind.Post, Path = endpoint.RoutePattern.RawText ?? "", RequestDtoId = "base.vector.rebuild.request", ResponseDtoId = "base.vector.rebuild.result" },
                "hpd.base.text.query" => new RouteDescriptor { OperationId = descriptor.EndpointId, Method = HttpMethodKind.Post, Path = endpoint.RoutePattern.RawText ?? "", RequestDtoId = "base.text.query.request", ResponseDtoId = "base.text.query.result" },
                "hpd.base.text.metadata.list" => new RouteDescriptor { OperationId = descriptor.EndpointId, Method = HttpMethodKind.Get, Path = endpoint.RoutePattern.RawText ?? "", ResponseDtoId = "base.text.indexStatus.array" },
                "hpd.base.text.diagnostics.read" => new RouteDescriptor { OperationId = descriptor.EndpointId, Method = HttpMethodKind.Get, Path = endpoint.RoutePattern.RawText ?? "", ResponseDtoId = "base.text.indexStatus" },
                "hpd.base.text.rebuild" => new RouteDescriptor { OperationId = descriptor.EndpointId, Method = HttpMethodKind.Post, Path = endpoint.RoutePattern.RawText ?? "", RequestDtoId = "base.text.rebuild.request", ResponseDtoId = "base.text.rebuild.result" },
                "base.subjectLifecycle.feed.read" => new RouteDescriptor { OperationId = descriptor.EndpointId, Method = HttpMethodKind.Post, Path = endpoint.RoutePattern.RawText ?? "", RequestDtoId = "base.subjectLifecycle.feed.read.request", ResponseDtoId = "base.subjectLifecycle.page" },
                "base.subjectLifecycle.feed.checkpoint" => new RouteDescriptor { OperationId = descriptor.EndpointId, Method = HttpMethodKind.Post, Path = endpoint.RoutePattern.RawText ?? "", RequestDtoId = "base.subjectLifecycle.feed.checkpoint.request", ResponseDtoId = "base.subjectLifecycle.checkpoint" },
                "base.subjectLifecycle.reconciliation.read" => new RouteDescriptor { OperationId = descriptor.EndpointId, Method = HttpMethodKind.Post, Path = endpoint.RoutePattern.RawText ?? "", RequestDtoId = "base.subjectLifecycle.reconciliation.read.request", ResponseDtoId = "base.subjectLifecycle.reconciliation.page" },
                _ => null!
            };
            if (route is not null) goto ContractResolved;
            string? readId = descriptor.EndpointId.StartsWith("base.reads.public.", StringComparison.Ordinal) ? descriptor.EndpointId["base.reads.public.".Length..]
                : descriptor.EndpointId.StartsWith("base.reads.admin.", StringComparison.Ordinal) ? descriptor.EndpointId["base.reads.admin.".Length..] : null;
            if (readId is null || !reads.Registrations.TryGetValue(readId, out IBaseReadRegistration? read)) throw new InvalidOperationException();
            route = new RouteDescriptor { OperationId = descriptor.EndpointId, Method = HttpMethodKind.Post, Path = endpoint.RoutePattern.RawText ?? "", RequestDtoId = read.ClientContract.ParameterTypeId, ResponseDtoId = read.ClientContract.RowTypeId };
        }
    ContractResolved:
        string method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.SingleOrDefault() ?? "GET";
        return new BaseClientEndpointDescriptor
        {
            Id = descriptor.EndpointId,
            Method = method.ToUpperInvariant(),
            Route = endpoint.RoutePattern.RawText ?? throw new InvalidOperationException("base.clientGeneration.inventoryUnavailable"),
            Audience = descriptor.Audience == HPDBaseEndpointAudience.Application ? "application" : "controlPlane",
            Operation = descriptor.Operation.ToString(),
            Capability = descriptor.Capability,
            RequestTypeId = route.RequestDtoId,
            ResponseTypeId = route.ResponseDtoId,
            SuccessStatuses = SuccessStatuses(method, descriptor.Operation),
            ErrorCodes = EndpointErrors(descriptor.Operation),
            MaximumRequestBodyBytes = MaximumRequestBody(descriptor.Operation, method, maximumRequestBodyBytes, maximumArtifactBytes),
            ResponseMode = descriptor.Operation switch
            {
                HPDBaseEndpointOperation.RealtimeSubscribe => "webSocket",
                HPDBaseEndpointOperation.BackupCreate => "stream",
                HPDBaseEndpointOperation.FileRead when method.Equals("HEAD", StringComparison.OrdinalIgnoreCase) => "empty",
                HPDBaseEndpointOperation.FileRead when route.ResponseDtoId == "application/octet-stream" => "bytes",
                HPDBaseEndpointOperation.FileDelete => "empty",
                _ => "json"
            },
            Replay = descriptor.Operation == HPDBaseEndpointOperation.RealtimeSubscribe ? "channelDependent" : "none",
            Resume = descriptor.Operation == HPDBaseEndpointOperation.RealtimeSubscribe ? "durableCursor" : "none",
            Cache = descriptor.Operation is HPDBaseEndpointOperation.ClientGenerationRead or HPDBaseEndpointOperation.MetadataRead ? "structuralDigest" : "none"
        };
    }

    private static BaseClientCollectionDescriptor ToCollection(
        CollectionDefinition collection,
        IRecordStoreRegistry stores,
        BaseClientEndpointDescriptor[] endpoints,
        HPDBaseInstalledFeatures installedFeatures)
    {
        StoreCapabilityDescriptor? store = stores.GetStoreForCollection(collection.Id)?.Capabilities;
        bool identifiedAtomic = store?.Batch?.Modes.Contains(BaseRecordBatchExecutionMode.Atomic) == true
            && store.Batch.MaxOperations >= 1
            && store.AtomicRequest is { Supported: true, Durability: not BaseAtomicRequestDurability.None, DuplicateResultReplay: true, FingerprintConflictDetection: true, IndeterminateResolution: true }
            && (!store.Batch.Durable || store.AtomicRequest.Durability == BaseAtomicRequestDurability.Durable)
            && endpoints.Any(endpoint => endpoint.Id == "base.records.batch" && endpoint.Capability == HPDBaseCapabilities.RecordsBatchWrite);
        var operations = new List<string>();
        if (collection.Operations.List && store?.Read.List == true) { operations.Add("list"); operations.Add("query"); }
        if (collection.Operations.Get && store?.Read.Get == true) operations.Add("get");
        if (identifiedAtomic)
        {
            if (collection.Operations.Create && store!.Mutation.Create) operations.Add("create");
            if (collection.Operations.Patch && store!.Mutation.Patch) operations.Add("patch");
            if (collection.Operations.Replace && store!.Mutation.Replace) operations.Add("replace");
            if (collection.Operations.Delete && store!.Mutation.Delete) operations.Add("delete");
            if (collection.Operations.Upsert && store!.Upsert is { Atomic: true, ExpectedRevision: true, ExistenceConditions: true } upsert
                && upsert.UpdateModes.Contains(RecordUpsertUpdateMode.Patch) && upsert.UpdateModes.Contains(RecordUpsertUpdateMode.Replace)) operations.Add("upsert");
            operations.Add("batch");
        }
        bool realtime = installedFeatures.Realtime && endpoints.Any(endpoint => endpoint.Operation == nameof(HPDBaseEndpointOperation.RealtimeSubscribe));
        if (realtime) operations.Add("realtime");
        if (realtime && installedFeatures.LiveQueries && installedFeatures.Dependencies && collection.Operations.List && store?.Read.List == true) operations.Add("watch");
        if ((collection.VectorIndexes?.Length ?? 0) != 0 && endpoints.Any(endpoint => endpoint.Operation == nameof(HPDBaseEndpointOperation.VectorQuery))) operations.Add("vector");
        if ((collection.TextIndexes?.Length ?? 0) != 0 && endpoints.Any(endpoint => endpoint.Operation == nameof(HPDBaseEndpointOperation.TextQuery))) operations.Add("text");
        return new BaseClientCollectionDescriptor
        {
            Id = collection.Id,
            GeneratedName = GeneratedName(collection.Name),
            RecordTypeId = $"collection.{collection.Id}.record",
            CreateTypeId = $"collection.{collection.Id}.create",
            ReplaceTypeId = $"collection.{collection.Id}.replace",
            PatchTypeId = $"collection.{collection.Id}.patch",
            Fields = (collection.Fields ?? []).Where(field => !field.Hidden).OrderBy(field => field.Id, StringComparer.Ordinal).Select(field => new BaseClientFieldDescriptor
            {
                Id = field.Id,
                WireName = field.WireName,
                GeneratedName = field.ApplicationName,
                ValueTypeId = $"field.{collection.Id}.{field.Id}",
                ServerGenerated = field.Generated is not null || field.ReadOnly,
                Mutable = !field.ReadOnly && field.Generated is null,
                DisclosureShape = DisclosureShape(field.Disclosure?.RecordRead ?? BaseFieldDisclosurePolicies.For(field.Confidentiality).RecordRead, field.Visibility is not null),
                Operators = Operators(field.Type)
            }).ToArray(),
            Operations = [.. operations],
            Pagination = collection.MutationMode is BaseCollectionMutationMode.AppendOnly or BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge ? "stableHistory" : "seek",
            MaxPageSize = Math.Min(store?.Read.MaxPageSize ?? 500, 500)
        };
    }

    private static BaseClientTextIndexDescriptor[] BuildTextIndexes(IEnumerable<CollectionDefinition> collections, IReadOnlySet<string> authorized) => collections
        .SelectMany(collection => (collection.TextIndexes ?? []).Where(index => authorized.Contains(collection.Id + "\n" + index.Id)).Select(index => new BaseClientTextIndexDescriptor
        {
            CollectionId = collection.Id, Id = index.Id, Version = index.Version, GeneratedName = GeneratedName(index.Id),
            AnalyzerId = index.AnalyzerContractId, ScoringId = index.ScoringContractId, Audience = index.Audience.ToString(), MaximumResults = index.Limits.MaximumResults,
            Fields = index.Fields.Select(static field => new BaseClientTextFieldDescriptor { Id = field.StableFieldId, GeneratedName = field.ApplicationName, WireName = field.WireName, Weight = field.Weight }).ToArray(),
            FilterFields = index.FilterFields.Select(static field => new BaseClientTextFilterFieldDescriptor { Id = field.StableFieldId, GeneratedName = field.ApplicationName, WireName = field.WireName, ValueKind = field.ValueKind.ToString() }).ToArray(),
        })).OrderBy(static value => value.CollectionId, StringComparer.Ordinal).ThenBy(static value => value.Id, StringComparer.Ordinal).ToArray();

    private static BaseClientNamedTypeDescriptor[] BuildTypes(IEnumerable<CollectionDefinition> definitions, IEnumerable<IBaseReadRegistration> reads, bool includeDependencyParameter, IEnumerable<BaseSelectionOperationProfile> selections, BaseLogicalExportedSubject[] subjects) => definitions
        .Where(collection => collection.Enabled && collection.Exposed)
        .SelectMany(collection => CollectionTypes(collection, subjects))
        .Concat(reads.SelectMany(read => ReadTypes(read, subjects)))
        .Concat([new BaseClientNamedTypeDescriptor
        {
            Id = "base.redacted",
            Node = new BaseClientTypeNode { Kind = "redacted" }
        }])
        .Concat(includeDependencyParameter ? [new BaseClientNamedTypeDescriptor { Id = "base.dependency.parameter", Node = new BaseClientTypeNode { Kind = "string", Format = "plain", MinLength = 0, MaxLength = 4096 } }] : [])
        .Concat(SelectionTypes(selections))
        .OrderBy(type => type.Id, StringComparer.Ordinal)
        .ToArray();

    private static IEnumerable<BaseClientNamedTypeDescriptor> SelectionTypes(IEnumerable<BaseSelectionOperationProfile> profiles)
    {
        BaseSelectionOperationProfile[] values = profiles.ToArray();
        if (values.Length == 0) yield break;
        yield return new() { Id = "base.selection.identity", Node = new() { Kind = "selection-identity" } };
        yield return new() { Id = "base.selection.outcome", Node = new() { Kind = "enum", Values = ["committed", "rolledBack", "partiallyCommitted"] } };
        yield return new() { Id = "base.selection.disposition", Node = new() { Kind = "enum", Values = ["committed", "duplicate"] } };
        yield return new() { Id = "base.selection.count", Node = new() { Kind = "integer", Minimum = "0", Maximum = "2147483647", Wire = "number" } };
        yield return new() { Id = "base.selection.wait", Node = new() { Kind = "integer", Minimum = "1", Maximum = "9007199254740991", Wire = "number" } };
        yield return new() { Id = "base.selection.result", Node = Object([
            Property("selectedCount", "base.selection.count", true), Property("mutatedCount", "base.selection.count", true),
            Property("outcome", "base.selection.outcome", true), Property("requestDisposition", "base.selection.disposition", true)]) };
        foreach (BaseSelectionOperationProfile profile in values)
        {
            string query = $"selection.{profile.Id}.query", previous = $"selection.{profile.Id}.previous";
            yield return new() { Id = query, Node = new() { Kind = "selection-query", MaximumNodes = profile.Limits.MaximumQueryNodes, MaximumDepth = profile.Limits.MaximumQueryDepth, MaximumLiterals = profile.Limits.MaximumLiteralValues, MaximumTake = profile.Limits.MaximumSelectedRecords } };
            yield return new() { Id = previous, Node = new() { Kind = "selection-previous-state", MaximumFields = profile.Limits.MaximumPreviousStateRequirements } };
            var properties = new List<BaseClientPropertyDescriptor> { Property("query", query, true), Property("previousState", previous, true), Property("requestIdentity", "base.selection.identity", false), Property("callerWaitTimeoutTicks", "base.selection.wait", false) };
            if (profile.MutationKind == BaseSelectionMutationKind.MergePatch)
            {
                string patch = $"selection.{profile.Id}.patch";
                yield return new() { Id = patch, Node = new() { Kind = "selection-patch", PatchTypeId = $"collection.{profile.CollectionId}.patch" } };
                properties.Insert(1, Property("patch", patch, true));
            }
            yield return new() { Id = $"selection.{profile.Id}.request", Node = Object(properties) };
        }
        static BaseClientTypeNode Object(IEnumerable<BaseClientPropertyDescriptor> properties) => new() { Kind = "object", AdditionalProperties = false, Properties = properties.ToArray() };
        static BaseClientPropertyDescriptor Property(string name, string type, bool required) => new() { Name = name, WireName = name, TypeId = type, Required = required, Nullable = false, DisclosureShape = "none" };
    }

    private static IEnumerable<BaseClientNamedTypeDescriptor> ReadTypes(IBaseReadRegistration read, BaseLogicalExportedSubject[] subjects)
    {
        BaseReadClientContract contract = read.ClientContract;
        foreach (BaseClientNamedTypeDescriptor type in MemberTypes(contract.Parameters, contract.ParameterTypeId, false)) yield return type;
        foreach (BaseClientNamedTypeDescriptor type in MemberTypes(contract.Row, contract.RowTypeId, true)) yield return type;
        yield return ReadObject(contract.ParameterTypeId, contract.Parameters, contract.ParameterTypeId);
        yield return ReadObject(contract.RowTypeId, contract.Row, contract.RowTypeId);

        IEnumerable<BaseClientNamedTypeDescriptor> MemberTypes(IReadOnlyList<BaseReadClientProperty> properties, string owner, bool output)
        {
            foreach (BaseReadClientProperty property in properties)
            {
                string valueId = owner + "." + property.Id;
                string scalarId = property.Array ? valueId + ".item" : valueId;
                BaseRelationalOperand? operand = output ? read.Plan.Projection.Single(projection => projection.FieldId == property.Id).Operand : null;
                BaseClientTypeNode node = operand?.Kind == BaseRelationalOperandKind.SubjectReference
                    ? SubjectNode(new BaseSubjectReferenceDefinition
                    {
                        ContractId = operand.SubjectContractId!, ContractVersion = operand.SubjectContractVersion!.Value,
                        ContractChecksum = string.Empty, Requirement = BaseSubjectReferenceRequirement.Exists,
                        Guarantee = BaseSubjectValidationGuarantee.TransactionSnapshot,
                    }, subjects)
                    : ReadScalar(property.Kind);
                yield return new BaseClientNamedTypeDescriptor { Id = scalarId, Node = node };
                if (property.Array) yield return new BaseClientNamedTypeDescriptor { Id = valueId, Node = new BaseClientTypeNode { Kind = "array", ElementTypeId = scalarId, MinItems = 0, MaxItems = 256 } };
            }
        }

        static BaseClientNamedTypeDescriptor ReadObject(string id, IReadOnlyList<BaseReadClientProperty> properties, string owner) => new()
        {
            Id = id,
            Node = new BaseClientTypeNode
            {
                Kind = "object", AdditionalProperties = false,
                Properties = properties.Select(property => new BaseClientPropertyDescriptor
                {
                    Name = property.GeneratedName, WireName = property.WireName, TypeId = owner + "." + property.Id,
                    Required = !property.Nullable, Nullable = property.Nullable, DisclosureShape = "none"
                }).ToArray()
            }
        };
    }

    private static BaseClientTypeNode ReadScalar(QueryValueKind kind) => kind switch
    {
        QueryValueKind.Boolean => new() { Kind = "boolean" },
        QueryValueKind.Integer => new() { Kind = "integer", Minimum = "-9223372036854775808", Maximum = "9223372036854775807", Wire = "decimal-string" },
        QueryValueKind.Decimal => new() { Kind = "decimal", Wire = "decimal-string" },
        QueryValueKind.Number => new() { Kind = "floating", Precision = "binary64", FiniteOnly = true },
        QueryValueKind.Id => new() { Kind = "string", Format = "record-id", MinLength = 1, MaxLength = 256 },
        QueryValueKind.DateTime => new() { Kind = "string", Format = "utc-instant", MinLength = 1, MaxLength = 64 },
        _ => new() { Kind = "string", Format = "plain", MinLength = 0, MaxLength = 4096 }
    };

    private static IEnumerable<BaseClientNamedTypeDescriptor> CollectionTypes(CollectionDefinition collection, BaseLogicalExportedSubject[] subjects)
    {
        FieldDefinition[] fields = (collection.Fields ?? []).Where(field => !field.Hidden).OrderBy(field => field.Id, StringComparer.Ordinal).ToArray();
        foreach (FieldDefinition field in fields)
        {
            if (field.Type == "vector")
            {
                int dimensions = (collection.VectorIndexes ?? []).Where(index => index.VectorFieldId == field.Id).Select(index => index.Dimensions).Distinct().Single();
                yield return new BaseClientNamedTypeDescriptor { Id = $"field.{collection.Id}.{field.Id}.item", Node = new BaseClientTypeNode { Kind = "floating", Precision = "binary32", FiniteOnly = true } };
                yield return new BaseClientNamedTypeDescriptor { Id = $"field.{collection.Id}.{field.Id}", Node = new BaseClientTypeNode { Kind = "array", ElementTypeId = $"field.{collection.Id}.{field.Id}.item", MinItems = dimensions, MaxItems = dimensions } };
            }
            else yield return new BaseClientNamedTypeDescriptor { Id = $"field.{collection.Id}.{field.Id}", Node = FieldNode(field, subjects) };
        }
        yield return ObjectType($"collection.{collection.Id}.record", fields, static field =>
            (field.Disclosure?.RecordRead ?? BaseFieldDisclosurePolicies.For(field.Confidentiality).RecordRead) != BaseRecordDisclosure.Omit,
            output: true, requiredMutable: false);
        yield return ObjectType($"collection.{collection.Id}.create", fields, static field => !field.ReadOnly && field.Generated is null, output: false, requiredMutable: true);
        yield return ObjectType($"collection.{collection.Id}.replace", fields, static field => !field.ReadOnly && field.Generated is null, output: false, requiredMutable: true);
        yield return ObjectType($"collection.{collection.Id}.patch", fields, static field => !field.ReadOnly && field.Generated is null, output: false, requiredMutable: false);

        BaseClientNamedTypeDescriptor ObjectType(string id, FieldDefinition[] source, Func<FieldDefinition, bool> include, bool output, bool requiredMutable) => new()
        {
            Id = id,
            Node = new BaseClientTypeNode
            {
                Kind = "object",
                AdditionalProperties = false,
                Properties = source.Where(include).Select(field => new BaseClientPropertyDescriptor
                {
                    Name = field.ApplicationName, WireName = field.WireName,
                    TypeId = $"field.{collection.Id}.{field.Id}",
                    Required = output || requiredMutable,
                    Nullable = field.Nullable,
                    DisclosureShape = output
                        ? DisclosureShape(field.Disclosure?.RecordRead ?? BaseFieldDisclosurePolicies.For(field.Confidentiality).RecordRead, field.Visibility is not null)
                        : "none"
                }).ToArray()
            }
        };
    }

    private static BaseClientTypeNode FieldNode(FieldDefinition field, BaseLogicalExportedSubject[] subjects) => field.Type switch
    {
        _ when field.SubjectReference is { } reference => SubjectNode(reference, subjects),
        "bool" or "boolean" => new() { Kind = "boolean" },
        "int" or "integer" => new() { Kind = "integer", Minimum = "-2147483648", Maximum = "2147483647", Wire = "number" },
        "long" => new() { Kind = "integer", Minimum = "-9223372036854775808", Maximum = "9223372036854775807", Wire = "decimal-string" },
        "decimal" => new() { Kind = "decimal", Wire = "decimal-string" },
        "id" => new() { Kind = "string", Format = "record-id", MinLength = 1, MaxLength = 256 },
        "datetime" or "instant" => new() { Kind = "string", Format = "utc-instant", MinLength = 1, MaxLength = 64 },
        _ when string.Equals(field.Format, "base64", StringComparison.Ordinal) => new() { Kind = "bytes", Wire = "base64", MaxBytes = field.MaximumBytes },
        _ => new() { Kind = "string", Format = "plain", MinLength = 0, MaxLength = 65536 }
    };

    private static BaseClientTypeNode SubjectNode(BaseSubjectReferenceDefinition reference, BaseLogicalExportedSubject[] subjects)
    {
        BaseLogicalExportedSubject contract = subjects.Single(value => value.Id == reference.ContractId && value.Version == reference.ContractVersion);
        return new BaseClientTypeNode
        {
            Kind = "subjectReference",
            ContractId = contract.Id,
            ContractVersion = contract.Version,
            SubjectIdKind = contract.SubjectIdKind switch { BaseSubjectIdKind.OrdinalString => "ordinalString", BaseSubjectIdKind.Guid => "guid", _ => "uint64" },
            MaximumSubjectIdUtf8Bytes = contract.MaximumSubjectIdUtf8Bytes,
            AuthorityEpochBytes = 16,
            IncarnationBytes = 24,
        };
    }

    private static string DisclosureShape(BaseRecordDisclosure disclosure, bool policyMayOmit) => disclosure switch
    {
        BaseRecordDisclosure.FixedMarker => "fixed-marker",
        BaseRecordDisclosure.Omit => "omission",
        BaseRecordDisclosure.Include when policyMayOmit => "omission",
        BaseRecordDisclosure.Include => "none",
        _ => throw new InvalidOperationException(BaseConfidentialityErrorCodes.ContractInvalid),
    };

    private static BaseClientVectorIndexDescriptor[] BuildVectors(IEnumerable<CollectionDefinition> definitions) => definitions
        .SelectMany(collection => (collection.VectorIndexes ?? []).Select(index => new BaseClientVectorIndexDescriptor
        {
            CollectionId = collection.Id,
            Id = index.Id,
            GeneratedName = GeneratedName(index.Id),
            Dimensions = index.Dimensions,
            Measure = index.Function switch
            {
                BaseVectorFunction.CosineSimilarity => "cosineSimilarity",
                BaseVectorFunction.DotProductSimilarity => "dotProductSimilarity",
                _ => "euclideanDistance"
            },
            FilterFieldIds = [.. index.FilterFieldIds]
        }))
        .OrderBy(index => index.CollectionId, StringComparer.Ordinal).ThenBy(index => index.Id, StringComparer.Ordinal)
        .ToArray();

    private static string[] Operators(string type) => type switch
    {
        "bool" or "boolean" => ["equal", "notEqual", "isNull", "isDefined"],
        "int" or "integer" or "long" or "decimal" or "datetime" or "instant" => ["equal", "notEqual", "lessThan", "lessThanOrEqual", "greaterThan", "greaterThanOrEqual", "in", "isNull", "isDefined", "between"],
        _ => ["equal", "notEqual", "in", "isNull", "isDefined", "contains", "notContains", "startsWith", "endsWith", "like", "notLike"]
    };

    private static int[] SuccessStatuses(string method, HPDBaseEndpointOperation operation) => operation switch
    {
        HPDBaseEndpointOperation.RealtimeSubscribe => [101],
        HPDBaseEndpointOperation.FileDelete => [204],
        HPDBaseEndpointOperation.FileWrite => [201],
        HPDBaseEndpointOperation.RecordDelete => [200],
        HPDBaseEndpointOperation.RecordWrite when method.Equals("POST", StringComparison.OrdinalIgnoreCase) => [201],
        _ => [200]
    };

    private static string[] EndpointErrors(HPDBaseEndpointOperation operation) => operation switch
    {
        HPDBaseEndpointOperation.RealtimeSubscribe => [BaseRealtimeErrorCodes.CursorExpired, "base.realtime.protocolInvalid", "base.realtime.payloadTooLarge"],
        HPDBaseEndpointOperation.BackupCreate or HPDBaseEndpointOperation.BackupValidate or HPDBaseEndpointOperation.BackupRestore => ["base.admin.backup.busy", "base.admin.backup.artifactInvalid", "base.admin.backup.multipartInvalid"],
        HPDBaseEndpointOperation.SubjectLifecycleRead or HPDBaseEndpointOperation.SubjectLifecycleCheckpoint or HPDBaseEndpointOperation.SubjectLifecycleReconciliationRead =>
        [
            BaseSubjectErrorCodes.LifecycleContractInvalid, BaseSubjectErrorCodes.LifecycleUnauthorized,
            BaseSubjectErrorCodes.CursorInvalid, BaseSubjectErrorCodes.CursorExpired,
            BaseSubjectErrorCodes.CursorScopeMismatch, BaseSubjectErrorCodes.ScopeAuthorityInvalid,
            BaseSubjectErrorCodes.CursorOvertaken, BaseSubjectErrorCodes.LifecycleReconciliationUnavailable,
            BaseSubjectErrorCodes.LifecycleProviderContractInvalid, BaseSubjectErrorCodes.LifecycleCapacityExceeded,
            BaseSubjectErrorCodes.Timeout, BaseSubjectErrorCodes.LifecycleCommitIndeterminate,
            BaseSubjectErrorCodes.MaintenanceRequired,
        ],
        _ => ["base.runtime.validation", "base.http.authenticationRequired", "base.http.authorizationDenied"]
    };

    private static long MaximumRequestBody(HPDBaseEndpointOperation operation, string method, long maximumRequestBodyBytes, long maximumArtifactBytes) => method.Equals("GET", StringComparison.OrdinalIgnoreCase) || method.Equals("HEAD", StringComparison.OrdinalIgnoreCase) ? 0
        : operation is HPDBaseEndpointOperation.BackupValidate or HPDBaseEndpointOperation.BackupRestore ? checked(maximumArtifactBytes + 96 * 1024)
        : operation is HPDBaseEndpointOperation.BackupCreate or HPDBaseEndpointOperation.AdministrativePurge ? 64 * 1024
        : operation == HPDBaseEndpointOperation.RealtimeSubscribe ? 0
        : maximumRequestBodyBytes;

    private static BaseClientErrorDescriptor[] ErrorTaxonomy(BaseClientEndpointDescriptor[] endpoints) => endpoints
        .SelectMany(static endpoint => endpoint.ErrorCodes)
        .Append(BaseMutationRequestErrorCodes.FingerprintConflict)
        .Append(BaseMutationErrorCodes.BatchIndeterminate)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .Select(static code => new BaseClientErrorDescriptor
        {
            Code = code,
            Category = code is BaseSubjectErrorCodes.LifecycleProviderContractInvalid or BaseSubjectErrorCodes.LifecycleReconciliationUnavailable or BaseSubjectErrorCodes.MaintenanceRequired ? "capability"
                : code == BaseSubjectErrorCodes.LifecycleCapacityExceeded || code == BaseSubjectErrorCodes.Timeout || code == BaseSubjectErrorCodes.LifecycleCommitIndeterminate ? "store"
                : code is BaseSubjectErrorCodes.LifecycleUnauthorized or BaseSubjectErrorCodes.CursorScopeMismatch or BaseSubjectErrorCodes.ScopeAuthorityInvalid ? "authorization"
                : code is BaseSubjectErrorCodes.CursorExpired or BaseSubjectErrorCodes.CursorOvertaken ? "conflict"
                : code.Contains("authentic", StringComparison.OrdinalIgnoreCase) ? "authentication"
                : code.Contains("authoriz", StringComparison.OrdinalIgnoreCase) ? "authorization"
                : code.Contains("conflict", StringComparison.OrdinalIgnoreCase) || code.Contains("expired", StringComparison.OrdinalIgnoreCase) ? "conflict"
                : code.Contains("indeterminate", StringComparison.OrdinalIgnoreCase) || code.Contains("busy", StringComparison.OrdinalIgnoreCase) ? "store"
                : "validation",
            Retryable = code == BaseSubjectErrorCodes.LifecycleCapacityExceeded || code == BaseSubjectErrorCodes.Timeout || code == BaseSubjectErrorCodes.MaintenanceRequired
                || code.Contains("busy", StringComparison.OrdinalIgnoreCase)
        })
        .ToArray();

    private static string BasePath(string requestPath)
    {
        const string suffix = "/client-generation";
        return requestPath.EndsWith(suffix, StringComparison.Ordinal) ? requestPath[..^suffix.Length] : "/base";
    }

    private static string GeneratedName(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool upper = false;
        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character)) { upper = builder.Length > 0; continue; }
            builder.Append(upper ? char.ToUpperInvariant(character) : builder.Length == 0 ? char.ToLowerInvariant(character) : character);
            upper = false;
        }
        return builder.Length == 0 ? throw new InvalidOperationException("base.clientGeneration.nameInvalid") : builder.ToString();
    }

    private static string LowerCamel(string value) => value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value[1..];

    private static string Hash(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static byte[] Canonicalize(byte[] json, bool snapshotDigestInput = false)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 });
        using var output = new MemoryStream(json.Length);
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false, SkipValidation = false }))
            WriteCanonical(writer, document.RootElement, snapshotDigestInput, depth: 0, propertyName: null);
        return output.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element, bool snapshotDigestInput, int depth, string? propertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject().Where(property => !(snapshotDigestInput && depth == 0 && property.NameEquals("digest"))).OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    if (snapshotDigestInput && depth == 1 && propertyName == "protocol" && property.NameEquals("generatedAt")) writer.WriteStringValue(string.Empty);
                    else WriteCanonical(writer, property.Value, snapshotDigestInput, depth + 1, property.Name);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (JsonElement item in element.EnumerateArray()) WriteCanonical(writer, item, snapshotDigestInput, depth + 1, propertyName); writer.WriteEndArray();
                break;
            case JsonValueKind.String: writer.WriteStringValue(element.GetString()); break;
            case JsonValueKind.Number: writer.WriteRawValue(element.GetRawText(), skipInputValidation: false); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new InvalidOperationException("base.clientGeneration.snapshotInvalid");
        }
    }
    private static OperationResult<BaseClientGenerationSnapshotV2> Failure(string code) => new() { Status = OperationStatus.CapabilityUnavailable, Error = new BaseError { Code = code, Message = "The client generation snapshot is unavailable." } };
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseClientGenerationSnapshotV2))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BaseClientEndpointDescriptor[]))]
internal partial class HPDBaseClientGenerationJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
