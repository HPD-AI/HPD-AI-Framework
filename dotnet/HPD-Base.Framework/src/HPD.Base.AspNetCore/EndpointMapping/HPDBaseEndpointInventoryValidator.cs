using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

internal sealed record HPDBaseApplicationPolicyMetadata(string Policy);

internal interface IHPDBaseEndpointSecurityMetadataValidator
{
    void Validate(Endpoint endpoint, HPDBaseEndpointDescriptor descriptor);
}

internal sealed class HPDBaseEndpointInventoryValidator(
    EndpointDataSource dataSource,
    IEnumerable<IHPDBaseEndpointSecurityMetadataValidator> securityValidators,
    HPDBaseEndpointFamilySelectionState? selections = null,
    BaseReadRegistry? reads = null)
{
    private sealed record Expected(string Method, string RouteSuffix, HPDBaseEndpointOperation Operation, string? Capability, HPDBaseEndpointAudience[] Audiences);

    private static readonly IReadOnlyDictionary<string, Expected> Static = new Dictionary<string, Expected>(StringComparer.Ordinal)
    {
        ["base.manifest"] = Public("GET", "/manifest", HPDBaseEndpointOperation.MetadataRead),
        ["base.capabilities"] = Public("GET", "/capabilities", HPDBaseEndpointOperation.MetadataRead),
        ["base.schema"] = Public("GET", "/schema", HPDBaseEndpointOperation.MetadataRead),
        ["base.collections.list"] = Public("GET", "/collections", HPDBaseEndpointOperation.MetadataRead),
        ["base.collections.get"] = Public("GET", "/collections/{collectionId}", HPDBaseEndpointOperation.MetadataRead),
        ["base.health"] = Public("GET", "/health", HPDBaseEndpointOperation.HealthRead),
        ["base.diagnostics"] = Public("GET", "/diagnostics", HPDBaseEndpointOperation.DiagnosticsRead),
        ["base.records.list"] = Protected("GET", "/collections/{collectionId}/records", HPDBaseEndpointOperation.RecordRead, HPDBaseCapabilities.RecordsRead),
        ["base.records.query"] = Protected("POST", "/collections/{collectionId}/records:query", HPDBaseEndpointOperation.RecordRead, HPDBaseCapabilities.RecordsRead),
        ["base.records.get"] = Protected("GET", "/collections/{collectionId}/records/{id}", HPDBaseEndpointOperation.RecordRead, HPDBaseCapabilities.RecordsRead),
        ["base.records.create"] = Protected("POST", "/collections/{collectionId}/records", HPDBaseEndpointOperation.RecordWrite, HPDBaseCapabilities.RecordsWrite),
        ["base.records.patch"] = Protected("PATCH", "/collections/{collectionId}/records/{id}", HPDBaseEndpointOperation.RecordWrite, HPDBaseCapabilities.RecordsWrite),
        ["base.records.replace"] = Protected("PUT", "/collections/{collectionId}/records/{id}", HPDBaseEndpointOperation.RecordWrite, HPDBaseCapabilities.RecordsWrite),
        ["base.records.upsert"] = Protected("PUT", "/collections/{collectionId}/records/{id}:upsert", HPDBaseEndpointOperation.RecordWrite, HPDBaseCapabilities.RecordsWrite),
        ["base.records.delete"] = Protected("DELETE", "/collections/{collectionId}/records/{id}", HPDBaseEndpointOperation.RecordDelete, HPDBaseCapabilities.RecordsDelete),
        ["base.records.batch"] = Protected("POST", "/records/batch", HPDBaseEndpointOperation.RecordBatchWrite, HPDBaseCapabilities.RecordsBatchWrite),
        ["base.admin.manifest"] = Control("GET", "/admin/manifest", HPDBaseEndpointOperation.MetadataRead, HPDBaseCapabilities.AdministrationMetadataRead),
        ["base.admin.capabilities"] = Control("GET", "/admin/capabilities", HPDBaseEndpointOperation.MetadataRead, HPDBaseCapabilities.AdministrationMetadataRead),
        ["base.admin.schema"] = Control("GET", "/admin/schema", HPDBaseEndpointOperation.MetadataRead, HPDBaseCapabilities.AdministrationMetadataRead),
        ["base.admin.collections.list"] = Control("GET", "/admin/collections", HPDBaseEndpointOperation.MetadataRead, HPDBaseCapabilities.AdministrationMetadataRead),
        ["base.admin.collections.get"] = Control("GET", "/admin/collections/{collectionId}", HPDBaseEndpointOperation.MetadataRead, HPDBaseCapabilities.AdministrationMetadataRead),
        ["base.admin.health"] = Control("GET", "/admin/health", HPDBaseEndpointOperation.HealthRead, HPDBaseCapabilities.AdministrationHealthRead),
        ["base.admin.diagnostics"] = Control("GET", "/admin/diagnostics", HPDBaseEndpointOperation.DiagnosticsRead, HPDBaseCapabilities.AdministrationDiagnosticsRead),
        ["base.admin.policy.explain"] = Control("POST", "/admin/policy/explain", HPDBaseEndpointOperation.PolicyExplain, HPDBaseCapabilities.PolicyExplain),
        ["base.files.objects.upload"] = Protected("POST", "/files/{bucketId}/objects", HPDBaseEndpointOperation.FileWrite, HPDBaseCapabilities.FilesWrite),
        ["base.files.objects.list"] = Protected("GET", "/files/{bucketId}/objects", HPDBaseEndpointOperation.FileRead, HPDBaseCapabilities.FilesRead),
        ["base.files.objects.download"] = Protected("GET", "/files/{bucketId}/objects/{objectId}", HPDBaseEndpointOperation.FileRead, HPDBaseCapabilities.FilesRead),
        ["base.files.objects.head"] = Protected("HEAD", "/files/{bucketId}/objects/{objectId}", HPDBaseEndpointOperation.FileRead, HPDBaseCapabilities.FilesRead),
        ["base.files.objects.metadata.get"] = Protected("GET", "/files/{bucketId}/objects/{objectId}/metadata", HPDBaseEndpointOperation.FileRead, HPDBaseCapabilities.FilesRead),
        ["base.files.objects.delete"] = Protected("DELETE", "/files/{bucketId}/objects/{objectId}", HPDBaseEndpointOperation.FileDelete, HPDBaseCapabilities.FilesDelete),
        ["base.realtime.websocket"] = Protected("GET", "/realtime/v1/socket", HPDBaseEndpointOperation.RealtimeSubscribe, HPDBaseCapabilities.RealtimeSubscribe)
    };

    internal void Validate()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        Endpoint[] endpoints = dataSource.Endpoints.ToArray();
        foreach (Endpoint endpoint in endpoints)
        {
            string? endpointName = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
            HPDBaseEndpointDescriptor[] descriptors = endpoint.Metadata.GetOrderedMetadata<HPDBaseEndpointDescriptor>().ToArray();
            if (descriptors.Length == 0)
            {
                if (endpointName is not null && (Static.ContainsKey(endpointName) || IsGeneratedId(endpointName)))
                    Fail("base.http.endpoint.descriptorMissing");
                continue;
            }
            if (descriptors.Length != 1) Fail("base.http.endpoint.descriptorDuplicate");
            HPDBaseEndpointDescriptor descriptor = descriptors[0];
            if (!ids.Add(descriptor.EndpointId)) Fail("base.http.endpoint.idDuplicate");
            Expected expected = ResolveExpected(descriptor);
            if (!expected.Audiences.Contains(descriptor.Audience) || expected.Operation != descriptor.Operation ||
                !string.Equals(expected.Capability, descriptor.Capability, StringComparison.Ordinal))
                Fail("base.http.endpoint.capabilityInvalid");
            if (!string.Equals(endpointName, descriptor.EndpointId, StringComparison.Ordinal)) Fail("base.http.endpoint.descriptorMissing");
            ValidateRoute(endpoint, expected);
            ValidateAudience(endpoint, descriptor);
            foreach (IHPDBaseEndpointSecurityMetadataValidator validator in securityValidators)
                validator.Validate(endpoint, descriptor);
        }
        ValidateRegisteredReadInventory(endpoints);
    }

    private static Expected ResolveExpected(HPDBaseEndpointDescriptor descriptor)
    {
        if (Static.TryGetValue(descriptor.EndpointId, out Expected? expected)) return expected;
        const string publicPrefix = "base.reads.public.";
        const string adminPrefix = "base.reads.admin.";
        string? id = descriptor.EndpointId.StartsWith(publicPrefix, StringComparison.Ordinal) ? descriptor.EndpointId[publicPrefix.Length..] :
            descriptor.EndpointId.StartsWith(adminPrefix, StringComparison.Ordinal) ? descriptor.EndpointId[adminPrefix.Length..] : null;
        if (id is null || !RegisteredReadEndpoints.IsValidHttpReadId(id)) Fail("base.http.endpoint.idInvalid");
        bool admin = descriptor.EndpointId.StartsWith(adminPrefix, StringComparison.Ordinal);
        return new Expected("POST", (admin ? "/admin/reads/" : "/reads/") + id,
            HPDBaseEndpointOperation.RegisteredRead,
            admin ? HPDBaseCapabilities.AdministrationRecordsRead : HPDBaseCapabilities.RecordsRead,
            admin ? [HPDBaseEndpointAudience.ControlPlane] : [HPDBaseEndpointAudience.Application, HPDBaseEndpointAudience.ControlPlane]);
    }

    private static void ValidateRoute(Endpoint endpoint, Expected expected)
    {
        if (endpoint is not RouteEndpoint route || route.RoutePattern.RawText is not { } raw ||
            !raw.EndsWith(expected.RouteSuffix, StringComparison.Ordinal)) Fail("base.http.endpoint.audienceConflict");
        string[] methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.ToArray() ?? [];
        if (methods.Length != 1 || !string.Equals(methods[0], expected.Method, StringComparison.OrdinalIgnoreCase))
            Fail("base.http.endpoint.audienceConflict");
    }

    private static void ValidateAudience(Endpoint endpoint, HPDBaseEndpointDescriptor descriptor)
    {
        if (descriptor.Audience != HPDBaseEndpointAudience.Public && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            Fail("base.http.endpoint.anonymous");
        HPDBaseApplicationPolicyMetadata[] policies = endpoint.Metadata.GetOrderedMetadata<HPDBaseApplicationPolicyMetadata>().ToArray();
        if (descriptor.Audience == HPDBaseEndpointAudience.Application)
        {
            if (policies.Length != 1 || endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count(data =>
                string.Equals(data.Policy, policies[0].Policy, StringComparison.Ordinal)) != 1)
                Fail("base.http.endpoint.audienceConflict");
        }
        else if (policies.Length != 0)
            Fail("base.http.endpoint.audienceConflict");
    }

    private static bool IsGeneratedId(string id) => id.StartsWith("base.reads.public.", StringComparison.Ordinal) || id.StartsWith("base.reads.admin.", StringComparison.Ordinal);

    private void ValidateRegisteredReadInventory(Endpoint[] endpoints)
    {
        if (selections is null || reads is null) return;
        foreach ((BaseReadExposure exposure, HPDBaseEndpointAudience audience) in selections.RegisteredReads())
        {
            foreach (IBaseReadRegistration read in reads.Registrations.Values.Where(read => read.Exposure == exposure))
            {
                string id = "base.reads." + (exposure == BaseReadExposure.Admin ? "admin." : "public.") + read.Id;
                int count = endpoints.Count(endpoint => endpoint.Metadata.GetMetadata<HPDBaseEndpointDescriptor>() is { } descriptor &&
                    descriptor.Audience == audience && string.Equals(descriptor.EndpointId, id, StringComparison.Ordinal));
                if (count != 1) Fail(count == 0 ? "base.http.endpoint.descriptorMissing" : "base.http.endpoint.familyDuplicate");
            }
        }
    }
    private static Expected Public(string method, string route, HPDBaseEndpointOperation operation) => new(method, route, operation, null, [HPDBaseEndpointAudience.Public]);
    private static Expected Protected(string method, string route, HPDBaseEndpointOperation operation, string capability) => new(method, route, operation, capability, [HPDBaseEndpointAudience.Application, HPDBaseEndpointAudience.ControlPlane]);
    private static Expected Control(string method, string route, HPDBaseEndpointOperation operation, string capability) => new(method, route, operation, capability, [HPDBaseEndpointAudience.ControlPlane]);
    private static void Fail(string code) => throw new InvalidOperationException(code);
}

internal sealed class HPDBaseEndpointInventoryStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.ApplicationServices.GetRequiredService<HPDBaseEndpointInventoryValidator>().Validate();
    };
}
