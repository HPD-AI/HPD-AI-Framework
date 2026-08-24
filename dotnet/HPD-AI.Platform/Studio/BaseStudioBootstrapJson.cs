using System.Buffers;
using System.Text.Json;

namespace HPD.AI.Platform.Studio;

/// <summary>Encodes validated Studio bootstrap authority to the fixed shell wire contract.</summary>
public static class BaseStudioBootstrapJson
{
    /// <summary>Encodes one already validated snapshot as bounded canonical UTF-8 JSON.</summary>
    public static byte[] Encode(BaseStudioBootstrapSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("applicationId", snapshot.ApplicationId);
            json.WriteString("mode", Name(snapshot.Mode));
            Authority(json, snapshot.Authority);
            json.WritePropertyName("modules"); json.WriteStartArray();
            foreach (BaseStudioVisibleModule value in snapshot.Modules)
            { json.WriteStartObject(); json.WriteString("moduleId", value.ModuleId); json.WriteNumber("version", value.Version);
              json.WriteString("displayNameMessageId", value.DisplayNameMessageId); json.WriteString("necessity", Name(value.Necessity));
              Hex(json, "registrationChecksum", value.RegistrationChecksum); Hex(json, "frontendAbiChecksum", value.FrontendAbiChecksum);
              Hex(json, "assetGraphChecksum", value.AssetGraphChecksum); json.WriteEndObject(); }
            json.WriteEndArray();
            json.WritePropertyName("pages"); json.WriteStartArray();
            foreach (BaseStudioVisiblePage value in snapshot.Pages)
            { json.WriteStartObject(); json.WriteString("moduleId", value.ModuleId); json.WriteString("pageId", value.PageId); json.WriteNumber("version", value.Version);
              json.WriteString("area", Name(value.Area)); json.WriteString("navigationRole", Name(value.NavigationRole)); Route(json, value.Route);
              json.WritePropertyName("acceptedResources"); json.WriteStartArray(); foreach (BaseStudioResourceKind resource in value.AcceptedResources) json.WriteStringValue(Name(resource)); json.WriteEndArray();
              json.WritePropertyName("observationMethodIds"); json.WriteStartArray(); foreach (string method in value.ObservationMethodIds) json.WriteStringValue(method); json.WriteEndArray();
              json.WritePropertyName("resolverMethodIds"); json.WriteStartArray(); foreach (string method in value.ResolverMethodIds) json.WriteStringValue(method); json.WriteEndArray();
              json.WritePropertyName("initialResource"); if (value.InitialResource is null) json.WriteNullValue(); else value.InitialResource.WriteJson(json);
              PagePresentation(json, value.Presentation);
              json.WritePropertyName("views"); json.WriteStartArray(); foreach (BaseStudioVisibleView view in value.Views)
              { json.WriteStartObject(); json.WriteString("viewId", view.ViewId); json.WriteNumber("version", view.Version);
                json.WriteString("observationMethodId", view.ObservationMethodId); json.WriteString("itemKind", Name(view.ItemKind));
                json.WriteString("itemNodeId", view.ItemNodeId); Hex(json, "itemNodeChecksum", view.ItemNodeChecksum);
                ViewPresentation(json, view.Presentation); Hex(json, "registrationChecksum", view.RegistrationChecksum); json.WriteEndObject(); }
              json.WriteEndArray();
              Hex(json, "registrationChecksum", value.RegistrationChecksum); json.WriteEndObject(); }
            json.WriteEndArray();
            json.WritePropertyName("commands"); json.WriteStartArray();
            foreach (BaseStudioVisibleCommand value in snapshot.Commands)
            { json.WriteStartObject(); json.WriteString("moduleId", value.ModuleId); json.WriteString("commandId", value.CommandId); json.WriteNumber("version", value.Version);
              json.WriteString("actionClass", Name(value.ActionClass));
              json.WritePropertyName("owningPageIds"); json.WriteStartArray(); foreach (string page in value.OwningPageIds) json.WriteStringValue(page); json.WriteEndArray();
              json.WritePropertyName("acceptedResources"); json.WriteStartArray(); foreach (BaseStudioResourceKind resource in value.AcceptedResources) json.WriteStringValue(Name(resource)); json.WriteEndArray();
              Hex(json, "registrationChecksum", value.RegistrationChecksum); json.WriteEndObject(); }
            json.WriteEndArray();
            json.WritePropertyName("resolvers"); json.WriteStartArray();
            foreach (BaseStudioVisibleResourceResolver value in snapshot.Resolvers)
            { json.WriteStartObject(); json.WriteString("moduleId", value.ModuleId); json.WriteString("kind", Name(value.Kind)); json.WriteString("resolverId", value.ResolverId);
              Hex(json, "registrationChecksum", value.RegistrationChecksum); json.WriteEndObject(); }
            json.WriteEndArray();
            json.WritePropertyName("linkResolvers"); json.WriteStartArray();
            foreach (BaseStudioVisibleLinkResolver value in snapshot.LinkResolvers)
            { json.WriteStartObject(); json.WriteString("moduleId", value.ModuleId); json.WriteString("sourceKind", Name(value.SourceKind));
              json.WriteString("relation", Name(value.Relation)); json.WriteString("targetKind", Name(value.TargetKind));
              json.WriteString("resolverId", value.ResolverId); json.WriteString("methodId", value.MethodId);
              Hex(json, "registrationChecksum", value.RegistrationChecksum); json.WriteEndObject(); }
            json.WriteEndArray();
            json.WritePropertyName("clients"); json.WriteStartArray();
            foreach (BaseStudioVisibleClient value in snapshot.Clients)
            { json.WriteStartObject(); json.WriteString("moduleId", value.ModuleId); json.WriteString("clientId", value.ClientId); json.WriteNumber("version", value.Version);
              json.WriteString("protocol", Name(value.Protocol)); Hex(json, "staticRuntimeAbiChecksum", value.StaticRuntimeAbiChecksum);
              Hex(json, "generatedContractChecksum", value.GeneratedContractChecksum); Hex(json, "operationInventoryChecksum", value.OperationInventoryChecksum);
              json.WriteString("endpointSurfaceId", value.EndpointSurfaceId); json.WriteString("transportClass", Name(value.TransportClass));
              json.WritePropertyName("owningPageIds"); json.WriteStartArray(); foreach (string page in value.OwningPageIds) json.WriteStringValue(page); json.WriteEndArray();
              json.WritePropertyName("limits"); json.WriteStartObject(); json.WriteNumber("maximumOperations", value.Limits.MaximumOperations);
              Long(json, "maximumRequestBytes", value.Limits.MaximumRequestBytes); Long(json, "maximumResponseBytes", value.Limits.MaximumResponseBytes);
              json.WriteNumber("maximumConcurrentRequests", value.Limits.MaximumConcurrentRequests);
              Long(json, "acquisitionDeadlineMilliseconds", checked((long)value.Limits.AcquisitionDeadline.TotalMilliseconds));
              Long(json, "operationDeadlineMilliseconds", checked((long)value.Limits.OperationDeadline.TotalMilliseconds));
              Long(json, "disposalDeadlineMilliseconds", checked((long)value.Limits.DisposalDeadline.TotalMilliseconds));
              Hex(json, "checksum", value.Limits.Checksum); json.WriteEndObject();
              json.WritePropertyName("operations"); json.WriteStartArray(); foreach (BaseStudioFrameworkSurfaceOperation operation in value.Operations)
              { json.WriteStartObject(); json.WriteString("operationId", operation.OperationId); json.WriteString("method", operation.Method.ToString().ToUpperInvariant());
                json.WriteString("relativePathTemplate", operation.RelativePathTemplate); json.WriteString("purpose", Name(operation.Purpose));
                json.WriteString("requiredCapability", operation.RequiredCapability); Long(json, "maximumRequestBytes", operation.MaximumRequestBytes);
                Long(json, "maximumResponseBytes", operation.MaximumResponseBytes); Long(json, "deadlineMilliseconds", checked((long)operation.Deadline.TotalMilliseconds));
                Strings(json, "requestMediaTypes", operation.RequestMediaTypes); Strings(json, "responseMediaTypes", operation.ResponseMediaTypes);
                Strings(json, "requestHeaderNames", operation.RequestHeaderNames); Strings(json, "responseHeaderNames", operation.ResponseHeaderNames);
                json.WriteEndObject(); } json.WriteEndArray(); json.WriteEndObject(); }
            json.WriteEndArray();
            ContractMap(json, snapshot.ContractMap);
            json.WritePropertyName("limits"); json.WriteStartObject();
            json.WriteNumber("maximumModules", snapshot.Limits.MaximumModules); json.WriteNumber("maximumPages", snapshot.Limits.MaximumPages);
            json.WriteNumber("maximumCommands", snapshot.Limits.MaximumCommands); json.WriteNumber("maximumResolvers", snapshot.Limits.MaximumResolvers);
            json.WriteNumber("maximumClients", snapshot.Limits.MaximumClients); json.WriteString("maximumBootstrapBytes", snapshot.Limits.MaximumBootstrapBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            json.WriteString("maximumRetainedBytes", snapshot.Limits.MaximumRetainedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            json.WriteString("bootstrapDeadlineMilliseconds", checked((long)snapshot.Limits.BootstrapDeadline.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Hex(json, "checksum", snapshot.Limits.Checksum); json.WriteEndObject();
            json.WriteString("capturedAtUtc", BaseStudioResponseAuthority.CanonicalUtc(snapshot.CapturedAtUtc));
            json.WriteString("expiresAtUtc", BaseStudioResponseAuthority.CanonicalUtc(snapshot.ExpiresAtUtc));
            Hex(json, "snapshotChecksum", snapshot.SnapshotChecksum); json.WriteEndObject();
        }
        if (buffer.WrittenCount > snapshot.Limits.MaximumBootstrapBytes)
            throw new InvalidOperationException("base.studio.bootstrapTooLarge");
        return buffer.WrittenSpan.ToArray();
    }

    private static void Authority(Utf8JsonWriter json, BaseStudioResponseAuthority value)
    {
        json.WritePropertyName("authority"); json.WriteStartObject();
        Long(json, "principalGeneration", value.PrincipalGeneration); Hex(json, "authenticatedSessionChecksum", value.AuthenticatedSessionChecksum);
        Hex(json, "protectedScopeChecksum", value.ProtectedScopeChecksum); Long(json, "applicationGraphGeneration", value.ApplicationGraphGeneration);
        Hex(json, "applicationGraphChecksum", value.ApplicationGraphChecksum); Long(json, "studioOwnerGeneration", value.StudioOwnerGeneration);
        Hex(json, "studioOwnerChecksum", value.StudioOwnerChecksum); Long(json, "policyOwnerGeneration", value.PolicyOwnerGeneration);
        Hex(json, "policyOwnerChecksum", value.PolicyOwnerChecksum); json.WritePropertyName("stores"); json.WriteStartArray();
        foreach (BaseStudioStoreAuthority store in value.Stores)
        { json.WriteStartObject(); json.WriteString("storeIdentity", store.StoreIdentity); Long(json, "providerGeneration", store.ProviderGeneration);
          Long(json, "restoreEpoch", store.RestoreEpoch); Long(json, "schemaGeneration", store.SchemaGeneration);
          Hex(json, "capabilityChecksum", store.CapabilityChecksum); Hex(json, "checksum", store.Checksum); json.WriteEndObject(); }
        json.WriteEndArray(); json.WriteString("authorizedThroughUtc", BaseStudioResponseAuthority.CanonicalUtc(value.AuthorizedThroughUtc));
        Hex(json, "checksum", value.Checksum); json.WriteEndObject();
    }

    private static void ContractMap(Utf8JsonWriter json, BaseStudioContractMap value)
    {
        json.WritePropertyName("contractMap"); json.WriteStartObject(); json.WriteString("protocolVersion", value.ProtocolVersion);
        json.WriteString("serializationProfile", value.SerializationProfile); json.WriteString("errorTaxonomy", value.ErrorTaxonomy);
        json.WriteString("realtimeProtocol", value.RealtimeProtocol); Hex(json, "runtimeAbiChecksum", value.RuntimeAbiChecksum);
        Hex(json, "interpreterVectorChecksum", value.InterpreterVectorChecksum); json.WritePropertyName("types"); json.WriteStartArray();
        foreach (BaseStudioNamedTypeContract type in value.Types)
        { json.WriteStartObject(); json.WriteString("typeId", type.TypeId); json.WriteString("canonicalDescriptor", Base64Url(type.GetCanonicalDescriptor()));
          Hex(json, "nodeChecksum", type.NodeChecksum); Hex(json, "checksum", type.Checksum); json.WriteEndObject(); }
        json.WriteEndArray(); json.WritePropertyName("endpoints"); json.WriteStartArray();
        foreach (BaseStudioEndpointContract endpoint in value.Endpoints)
        { json.WriteStartObject(); json.WriteString("endpointId", endpoint.EndpointId); json.WriteNumber("version", endpoint.Version);
          json.WriteString("method", endpoint.Method.ToString().ToUpperInvariant()); json.WriteString("relativeRoute", endpoint.RelativeRoute);
          json.WriteString("audience", "controlPlane"); json.WriteString("transport", endpoint.Transport == BaseStudioTransportKind.SameOriginHttp ? "sameOriginHttp" : "sameOriginRealtime");
          json.WriteString("requestNodeId", endpoint.RequestNodeId); Hex(json, "requestNodeChecksum", endpoint.RequestNodeChecksum);
          json.WriteString("resultNodeId", endpoint.ResultNodeId); Hex(json, "resultNodeChecksum", endpoint.ResultNodeChecksum);
          json.WriteString("errorNodeId", endpoint.ErrorNodeId); Hex(json, "errorNodeChecksum", endpoint.ErrorNodeChecksum);
          Long(json, "maximumRequestBytes", endpoint.MaximumRequestBytes); Long(json, "maximumResultBytes", endpoint.MaximumResultBytes);
          Long(json, "deadlineMilliseconds", checked((long)endpoint.Deadline.TotalMilliseconds)); Hex(json, "checksum", endpoint.Checksum); json.WriteEndObject(); }
        json.WriteEndArray(); json.WritePropertyName("methods"); json.WriteStartArray();
        foreach (BaseStudioMethodBinding method in value.Methods)
        { json.WriteStartObject(); json.WriteString("registeredMethodId", method.RegisteredMethodId); json.WriteString("kind", Name(method.Kind));
          json.WriteString("owningModuleId", method.OwningModuleId); json.WriteString("owningPageOrCommandId", method.OwningPageOrCommandId);
          json.WriteString("endpointId", method.EndpointId); json.WriteString("requestTypeId", method.RequestTypeId); json.WriteString("resultTypeId", method.ResultTypeId);
          Hex(json, "bindingChecksum", method.BindingChecksum); json.WriteEndObject(); }
        json.WriteEndArray(); Hex(json, "checksum", value.Checksum); json.WriteEndObject();
    }

    private static void Route(Utf8JsonWriter json, BaseStudioRouteTemplate route)
    {
        json.WritePropertyName("route"); json.WriteStartObject(); json.WriteString("id", route.TemplateId);
        json.WritePropertyName("segments"); json.WriteStartArray(); foreach (BaseStudioRouteSegment segment in route.Segments)
        { json.WriteStartObject(); json.WriteString("kind", segment.Kind == BaseStudioRouteSegmentKind.Literal ? "literal" : "parameter");
          if (segment.Kind == BaseStudioRouteSegmentKind.Literal) json.WriteString("value", segment.Value); else { json.WriteString("name", segment.Value); json.WriteString("codec", RouteCodec(segment.Codec!.Value)); }
          json.WriteEndObject(); } json.WriteEndArray(); json.WritePropertyName("query"); json.WriteStartArray();
        foreach (BaseStudioQueryParameter query in route.Query)
        { json.WriteStartObject(); json.WriteString("name", query.Name); json.WriteString("codec", RouteCodec(query.Codec)); json.WriteBoolean("required", query.Required);
          if (!query.RegisteredValues.IsEmpty) { json.WritePropertyName("allowed"); json.WriteStartArray(); foreach (string item in query.RegisteredValues) json.WriteStringValue(item); json.WriteEndArray(); }
          json.WriteEndObject(); }
        json.WriteEndArray(); json.WriteEndObject();
    }

    private static void PagePresentation(Utf8JsonWriter json, BaseStudioPagePresentationRegistration value)
    {
        json.WritePropertyName("presentation"); json.WriteStartObject(); json.WriteString("pageId", value.PageId);
        json.WriteNumber("pageVersion", value.PageVersion); json.WriteString("navigationRole", Name(value.NavigationRole));
        json.WriteString("workspace", Name(value.Workspace)); json.WritePropertyName("sections"); json.WriteStartArray();
        foreach (BaseStudioSectionRegistration section in value.Sections)
        { json.WriteStartObject(); json.WriteString("sectionId", section.SectionId); json.WriteString("labelMessageId", section.LabelMessageId);
          json.WriteNumber("order", section.Order); json.WriteString("kind", Name(section.Kind)); Strings(json, "viewIds", section.ViewIds);
          Strings(json, "commandIds", section.CommandIds); Hex(json, "checksum", section.Checksum); json.WriteEndObject(); }
        json.WriteEndArray();
        json.WritePropertyName("resourceRail"); if (value.ResourceRail is not { } rail) json.WriteNullValue(); else
        { json.WriteStartObject(); json.WriteString("railId", rail.RailId); json.WriteString("viewId", rail.ViewId); json.WriteString("itemKind", Name(rail.ItemKind));
          json.WriteString("search", Name(rail.Search)); json.WriteString("pinning", Name(rail.Pinning)); json.WriteNumber("initialWidthCssPixels", rail.InitialWidthCssPixels);
          json.WriteNumber("minimumWidthCssPixels", rail.MinimumWidthCssPixels); json.WriteNumber("maximumWidthCssPixels", rail.MaximumWidthCssPixels); Hex(json, "checksum", rail.Checksum); json.WriteEndObject(); }
        json.WritePropertyName("contextualDetail"); if (value.ContextualDetail is not { } detail) json.WriteNullValue(); else
        { json.WriteStartObject(); json.WritePropertyName("acceptedKinds"); json.WriteStartArray(); foreach (BaseStudioResourceKind kind in detail.AcceptedKinds) json.WriteStringValue(Name(kind)); json.WriteEndArray();
          Strings(json, "detailPageIds", detail.DetailPageIds); json.WriteNumber("fullScreenBelowCssPixels", detail.FullScreenBelowCssPixels);
          json.WriteString("closeBehavior", Name(detail.CloseBehavior)); json.WriteString("dirtyState", Name(detail.DirtyState)); Hex(json, "checksum", detail.Checksum); json.WriteEndObject(); }
        json.WriteString("draftRetention", Name(value.DraftRetention)); Hex(json, "checksum", value.Checksum); json.WriteEndObject();
    }

    private static void ViewPresentation(Utf8JsonWriter json, BaseStudioViewPresentationRegistration value)
    {
        json.WritePropertyName("presentation"); json.WriteStartObject(); json.WriteString("viewId", value.ViewId);
        json.WritePropertyName("grid"); if (value.Grid is not { } grid) json.WriteNullValue(); else
        { json.WriteStartObject(); json.WriteString("gridId", grid.GridId); json.WriteNumber("version", grid.Version); json.WriteString("rowKind", Name(grid.RowKind));
          json.WriteString("rowNodeId", grid.RowNodeId); Hex(json, "rowNodeChecksum", grid.RowNodeChecksum); json.WritePropertyName("columns"); json.WriteStartArray();
          foreach (BaseStudioGridColumnDefinition column in grid.Columns)
          { json.WriteStartObject(); json.WriteString("columnId", column.ColumnId); json.WriteString("stablePropertyOrEdgeId", column.StablePropertyOrEdgeId);
            json.WriteString("renderer", Name(column.Renderer)); json.WriteString("disclosure", Name(column.Disclosure)); json.WriteString("labelMessageId", column.LabelMessageId);
            json.WriteBoolean("initiallyVisible", column.InitiallyVisible); json.WriteNumber("initialOrder", column.InitialOrder); json.WriteNumber("initialWidthCssPixels", column.InitialWidthCssPixels);
            json.WriteNumber("minimumWidthCssPixels", column.MinimumWidthCssPixels); json.WriteNumber("maximumWidthCssPixels", column.MaximumWidthCssPixels);
            if (column.FilterId is null) json.WriteNull("filterId"); else json.WriteString("filterId", column.FilterId);
            if (column.SortId is null) json.WriteNull("sortId"); else json.WriteString("sortId", column.SortId); Hex(json, "checksum", column.Checksum); json.WriteEndObject(); }
          json.WriteEndArray(); json.WriteString("selection", Name(grid.Selection)); Strings(json, "rowCommandIds", grid.RowCommandIds);
          json.WriteNumber("virtualizationThreshold", grid.VirtualizationThreshold); json.WriteNumber("accessiblePageSize", grid.AccessiblePageSize);
          json.WriteNumber("maximumRows", grid.MaximumRows); Long(json, "maximumBytes", grid.MaximumBytes); Hex(json, "checksum", grid.Checksum); json.WriteEndObject(); }
        json.WritePropertyName("chart"); if (value.Chart is not { } chart) json.WriteNullValue(); else
        { json.WriteStartObject(); json.WriteString("chartId", chart.ChartId); json.WriteString("kind", Name(chart.Kind)); json.WriteString("bucketViewId", chart.BucketViewId);
          json.WriteString("equivalentTableViewId", chart.EquivalentTableViewId); json.WriteNumber("maximumBuckets", chart.MaximumBuckets);
          Hex(json, "disclosureChannelChecksum", chart.DisclosureChannelChecksum); Hex(json, "checksum", chart.Checksum); json.WriteEndObject(); }
        json.WriteString("emptyState", Name(value.EmptyState)); json.WritePropertyName("activity"); json.WriteStartObject(); json.WriteString("kind", Name(value.Activity.Kind));
        json.WriteNumber("maximumHintsPerRollingSecond", value.Activity.MaximumHintsPerRollingSecond); json.WriteNumber("maximumSupersededRefreshes", value.Activity.MaximumSupersededRefreshes);
        json.WriteNumber("maximumCoalescedKeys", value.Activity.MaximumCoalescedKeys); Hex(json, "checksum", value.Activity.Checksum); json.WriteEndObject();
        json.WritePropertyName("preferences"); json.WriteStartObject(); json.WriteString("schemaId", value.Preferences.SchemaId); json.WriteNumber("version", value.Preferences.Version);
        json.WritePropertyName("allowed"); json.WriteStartArray(); foreach (BaseStudioPreferenceKind kind in value.Preferences.Allowed) json.WriteStringValue(Name(kind)); json.WriteEndArray();
        Long(json, "maximumBytes", value.Preferences.MaximumBytes); Long(json, "maximumLifetimeMilliseconds", checked((long)value.Preferences.MaximumLifetime.TotalMilliseconds));
        Hex(json, "checksum", value.Preferences.Checksum); json.WriteEndObject(); Hex(json, "checksum", value.Checksum); json.WriteEndObject();
    }

    private static void Hex(Utf8JsonWriter json, string name, BaseStudioSha256 value) => json.WriteString(name, Convert.ToHexString(value.ToArray()).ToLowerInvariant());
    private static void Strings(Utf8JsonWriter json, string name, IEnumerable<string> values)
    { json.WritePropertyName(name); json.WriteStartArray(); foreach (string value in values) json.WriteStringValue(value); json.WriteEndArray(); }
    private static void Long(Utf8JsonWriter json, string name, long value) => json.WriteString(name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string RouteCodec(BaseStudioRouteCodec value) => value switch
    {
        BaseStudioRouteCodec.Identifier => "boundedId", BaseStudioRouteCodec.PositiveLong => "positiveLong",
        BaseStudioRouteCodec.NonnegativeLong => "nonnegativeLong", BaseStudioRouteCodec.Sha256 => "sha256",
        BaseStudioRouteCodec.StudioResourceIdentity => "resource", BaseStudioRouteCodec.Cursor => "cursor",
        BaseStudioRouteCodec.RegisteredEnum => "enum", BaseStudioRouteCodec.SelectedTab => "tab",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
    private static string Name<T>(T value) where T : struct, Enum => value.ToString() switch
    {
        "AreaLanding" => "areaLanding", "HiddenResolver" => "hiddenResolver", "OperationalTransition" => "operationalTransition",
        "DisasterOrRecoveryDomain" => "disasterOrRecoveryDomain", "SameOriginHttp" => "sameOriginHttp", "SameOriginRealtime" => "sameOriginRealtime",
        "ReceiptQuery" => "receiptQuery", "ReceiptResolve" => "receiptResolve", "InvalidationSubscribe" => "invalidationSubscribe",
        "StageCreate" => "stageCreate", "StageUpload" => "stageUpload", "StageFinalize" => "stageFinalize", "StageDispose" => "stageDispose",
        "StudioResourceIdentity" => "studioResourceIdentity", "NonnegativeLong" => "nonnegativeLong", "PositiveLong" => "positiveLong",
        "Sha256" => "sha256", "SelectedTab" => "selectedTab", "CanonicalCursor" => "canonicalCursor",
        var name => char.ToLowerInvariant(name[0]) + name[1..],
    };
}
