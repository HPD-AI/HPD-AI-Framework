using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace HPD.Graph.Connectors.SourceGenerator;

internal sealed record ConnectorInfo(
    string Id,
    string DisplayName,
    string? Description,
    string? Version,
    string? IconUri,
    string ClassName,
    string Accessibility,
    string Namespace,
    string FullyQualifiedName,
    string? JsonContextType,
    IReadOnlyList<IMethodSymbol> PreDispatchMethods,
    IMethodSymbol? BodyExtractorMethod,
    IReadOnlyList<OpenApiSpecDeclaration> OpenApiSpecs,
    Location? Location,
    bool IsPartial);

internal sealed record OpenApiSpecDeclaration(
    string SpecPath,
    IReadOnlyList<string> IncludeOperations);

internal sealed record OpenApiOperationInfo(
    string ConnectorId,
    string OperationId,
    string Method,
    string Path,
    string? Description,
    IReadOnlyList<OpenApiFieldInfo> Parameters,
    IReadOnlyList<OpenApiFieldInfo> PayloadProperties);

internal sealed record OpenApiFieldInfo(
    string Name,
    string TypeName,
    bool Required,
    string Location,
    string? Description);

internal sealed record ConnectionInfo(
    string ConnectionType,
    string AppId,
    string DisplayName,
    string? Description,
    string AuthKind,
    string TypeName,
    string FullyQualifiedName,
    Location? Location,
    bool IsPartial);

internal sealed record ConfigInfo(
    string ActionType,
    string DisplayName,
    string? Description,
    string Traits,
    string TypeName,
    string FullyQualifiedName,
    IReadOnlyList<FieldInfo> Fields,
    Location? Location,
    bool IsPartial);

internal sealed record FieldInfo(
    string Name,
    string TypeName,
    string? ConnectionType,
    string? OptionProviderName,
    bool Required);

internal sealed record SourceInfo(
    string SourceType,
    string DisplayName,
    string? Description,
    string AppId,
    string TriggerKind,
    string TypeName,
    string FullyQualifiedName,
    string? ConfigType,
    string? ConfigTypeName,
    IReadOnlyList<FieldInfo> ConfigFields,
    bool HasFromWebhook,
    bool HasPoll,
    IMethodSymbol? FromWebhookMethod,
    IMethodSymbol? PollMethod,
    IMethodSymbol? RegisterMethod,
    IMethodSymbol? UpdateMethod,
    IMethodSymbol? UnregisterMethod,
    Location? Location,
    bool IsPartial);

internal sealed record ActionInfo(
    string ActionType,
    string HandlerName,
    string DisplayName,
    string Traits,
    string TypeName,
    string FullyQualifiedName,
    string? ConfigType,
    string? ConfigTypeName,
    IMethodSymbol? RunMethod,
    Location? Location,
    bool IsPartial);

internal sealed record OptionInfo(
    string Name,
    string TypeName,
    string FullyQualifiedName,
    IMethodSymbol Method,
    Location? Location);

internal sealed record AssetCatalogInfo(
    string Name,
    string TypeName,
    string FullyQualifiedName,
    string? ConfigType,
    string? ConfigTypeName,
    IReadOnlyList<FieldInfo> ConfigFields,
    IMethodSymbol? LoadMethod,
    Location? Location,
    bool IsPartial);

internal sealed record MaterializationInfo(
    string Type,
    string TypeName,
    string FullyQualifiedName,
    string? ConfigType,
    string? ConfigTypeName,
    IReadOnlyList<FieldInfo> ConfigFields,
    IMethodSymbol? RunMethod,
    Location? Location,
    bool IsPartial);

internal sealed record CheckInfo(
    string Name,
    string TypeName,
    string FullyQualifiedName,
    IMethodSymbol? RunMethod,
    Location? Location,
    bool IsPartial);

internal sealed record IoInfo(
    string Name,
    string TypeName,
    string FullyQualifiedName,
    Location? Location,
    bool IsPartial);

internal sealed record Model(
    IReadOnlyList<ConnectorInfo> Connectors,
    IReadOnlyList<ConnectionInfo> Connections,
    IReadOnlyList<ConfigInfo> Configs,
    IReadOnlyList<SourceInfo> Sources,
    IReadOnlyList<ActionInfo> Actions,
    IReadOnlyList<OptionInfo> Options,
    IReadOnlyList<AssetCatalogInfo> AssetCatalogs,
    IReadOnlyList<MaterializationInfo> Materializations,
    IReadOnlyList<CheckInfo> Checks,
    IReadOnlyList<IoInfo> IoManagers,
    IReadOnlyList<OpenApiOperationInfo> OpenApiOperations);

[Generator]
public sealed class ConnectorSourceGenerator : IIncrementalGenerator
{
    private const string AttrPrefix = "HPD.Graph.Connectors.Abstractions.Attributes.";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var connectors = Types(context, AttrPrefix + "HpdConnectorAttribute").Collect();
        var connections = Types(context, AttrPrefix + "HpdConnectionAttribute").Collect();
        var configs = Types(context, AttrPrefix + "HpdActionConfigAttribute").Collect();
        var sources = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax cds && cds.AttributeLists.Count > 0,
                static (ctx, _) => ctx.Node as TypeDeclarationSyntax)
            .Where(static node => node is not null)
            .Collect();
        var actions = Types(context, AttrPrefix + "HpdConnectorActionAttribute").Collect();
        var assetCatalogs = Types(context, AttrPrefix + "HpdConnectorAssetCatalogAttribute").Collect();
        var materializations = Types(context, AttrPrefix + "HpdConnectorMaterializationAttribute").Collect();
        var checks = Types(context, AttrPrefix + "HpdConnectorAssetCheckAttribute").Collect();
        var ios = Types(context, AttrPrefix + "HpdArtifactIOManagerAttribute").Collect();
        var options = Methods(context, AttrPrefix + "HpdConnectorOptionAttribute").Collect();
        var additionalFiles = context.AdditionalTextsProvider.Collect();

        var all = connectors
            .Combine(connections)
            .Combine(configs)
            .Combine(sources)
            .Combine(actions)
            .Combine(options)
            .Combine(assetCatalogs)
            .Combine(materializations)
            .Combine(checks)
            .Combine(ios)
            .Combine(additionalFiles)
            .Combine(context.CompilationProvider);

        context.RegisterSourceOutput(all, static (spc, tuple) =>
        {
            var (((((((((((connectorNodes, connectionNodes), configNodes), sourceNodes), actionNodes), optionMethods), assetNodes), materializationNodes), checkNodes), ioNodes), additionalTexts), compilation) = tuple;
            Execute(
                spc,
                compilation,
                connectorNodes,
                connectionNodes,
                configNodes,
                sourceNodes!,
                actionNodes,
                optionMethods,
                assetNodes,
                materializationNodes,
                checkNodes,
                ioNodes,
                additionalTexts);
        });
    }

    private static IncrementalValuesProvider<TypeDeclarationSyntax> Types(
        IncrementalGeneratorInitializationContext context,
        string metadataName)
    {
        return context.SyntaxProvider.ForAttributeWithMetadataName(
            metadataName,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (ctx, _) => (TypeDeclarationSyntax)ctx.TargetNode);
    }

    private static IncrementalValuesProvider<MethodDeclarationSyntax> Methods(
        IncrementalGeneratorInitializationContext context,
        string metadataName)
    {
        return context.SyntaxProvider.ForAttributeWithMetadataName(
            metadataName,
            predicate: static (node, _) => node is MethodDeclarationSyntax,
            transform: static (ctx, _) => (MethodDeclarationSyntax)ctx.TargetNode);
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<TypeDeclarationSyntax> connectorNodes,
        ImmutableArray<TypeDeclarationSyntax> connectionNodes,
        ImmutableArray<TypeDeclarationSyntax> configNodes,
        ImmutableArray<TypeDeclarationSyntax> sourceNodes,
        ImmutableArray<TypeDeclarationSyntax> actionNodes,
        ImmutableArray<MethodDeclarationSyntax> optionMethods,
        ImmutableArray<TypeDeclarationSyntax> assetNodes,
        ImmutableArray<TypeDeclarationSyntax> materializationNodes,
        ImmutableArray<TypeDeclarationSyntax> checkNodes,
        ImmutableArray<TypeDeclarationSyntax> ioNodes,
        ImmutableArray<AdditionalText> additionalTexts)
    {
        var connectors = connectorNodes.Select(n => ResolveConnector(compilation, n)).WhereNotNull().ToArray();
        var model = new Model(
            connectors,
            connectionNodes.Select(n => ResolveConnection(compilation, n)).WhereNotNull().ToArray(),
            configNodes.Select(n => ResolveConfig(compilation, n)).WhereNotNull().ToArray(),
            sourceNodes.Select(n => ResolveSource(compilation, n)).WhereNotNull().ToArray(),
            actionNodes.Select(n => ResolveAction(compilation, n)).WhereNotNull().ToArray(),
            optionMethods.Select(n => ResolveOption(compilation, n)).WhereNotNull().ToArray(),
            assetNodes.Select(n => ResolveAssetCatalog(compilation, n)).WhereNotNull().ToArray(),
            materializationNodes.Select(n => ResolveMaterialization(compilation, n)).WhereNotNull().ToArray(),
            checkNodes.Select(n => ResolveCheck(compilation, n)).WhereNotNull().ToArray(),
            ioNodes.Select(n => ResolveIo(compilation, n)).WhereNotNull().ToArray(),
            ResolveOpenApiOperations(connectors, additionalTexts));

        ReportDiagnostics(context, model);

        if (model.Connectors.Count == 0)
        {
            return;
        }

        context.AddSource("HPDGraphConnectorJsonContext.g.cs", SourceText.From(GenerateJsonContext(model), Encoding.UTF8));
        context.AddSource("HPDGraphConnectorRegistry.g.cs", SourceText.From(GenerateRegistry(model), Encoding.UTF8));
        context.AddSource("HPDGraphConnectorDescriptors.g.cs", SourceText.From(GenerateDescriptors(model), Encoding.UTF8));
        context.AddSource("HPDGraphConnectorRegistration.g.cs", SourceText.From(GenerateRegistration(model), Encoding.UTF8));
        context.AddSource("HPDGraphConnectorWrappers.g.cs", SourceText.From(GenerateWrappers(model), Encoding.UTF8));
        context.AddSource("HPDGraphConnectorBuilderExtensions.g.cs", SourceText.From(GenerateBuilderExtensions(model), Encoding.UTF8));
        foreach (var connector in model.Connectors)
        {
            context.AddSource($"{Safe(connector.Id)}{connector.ClassName}ConnectorPartial.g.cs", SourceText.From(GenerateConnectorPartial(connector, model.Sources), Encoding.UTF8));
        }
    }

    private static ConnectorInfo? ResolveConnector(Compilation compilation, TypeDeclarationSyntax node)
    {
        var symbol = GetSymbol(compilation, node);
        var attr = symbol?.GetAttribute("HpdConnectorAttribute");
        if (symbol is null || attr is null) return null;
        var id = GetCtorString(attr, 0) ?? symbol.Name;
        return new ConnectorInfo(
            id,
            GetNamedString(attr, "DisplayName") ?? ToDisplayName(id),
            GetNamedString(attr, "Description"),
            GetNamedString(attr, "Version"),
            GetNamedString(attr, "IconUri"),
            symbol.Name,
            ToAccessibility(symbol.DeclaredAccessibility),
            symbol.ContainingNamespace.IsGlobalNamespace ? "HPD.Graph.Connectors.Generated" : symbol.ContainingNamespace.ToDisplayString(),
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            GetNamedType(attr, "JsonContextType")?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.GetMembers().OfType<IMethodSymbol>().Where(m => m.GetAttribute("HpdConnectorPreDispatchAttribute") is not null).ToArray(),
            symbol.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => m.GetAttribute("HpdConnectorBodyExtractorAttribute") is not null),
            symbol.GetAttributes()
                .Where(a => a.AttributeClass?.Name == "HpdOpenApiSpecAttribute")
                .Select(a => new OpenApiSpecDeclaration(
                    GetCtorString(a, 0) ?? string.Empty,
                    GetNamedStringArray(a, "IncludeOperations")))
                .Where(s => !string.IsNullOrWhiteSpace(s.SpecPath))
                .ToArray(),
            node.GetLocation(),
            IsPartial(node));
    }

    private static ConnectionInfo? ResolveConnection(Compilation compilation, TypeDeclarationSyntax node)
    {
        var symbol = GetSymbol(compilation, node);
        var attr = symbol?.GetAttribute("HpdConnectionAttribute");
        if (symbol is null || attr is null) return null;
        var connectionType = GetCtorString(attr, 0) ?? symbol.Name;
        var appId = GetNamedString(attr, "AppId") ?? FirstSegment(connectionType);
        return new ConnectionInfo(
            connectionType,
            appId,
            GetNamedString(attr, "DisplayName") ?? ToDisplayName(connectionType),
            GetNamedString(attr, "Description"),
            GetNamedEnum(attr, "AuthKind") ?? "Custom",
            symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            node.GetLocation(),
            IsPartial(node));
    }

    private static ConfigInfo? ResolveConfig(Compilation compilation, TypeDeclarationSyntax node)
    {
        var symbol = GetSymbol(compilation, node);
        var attr = symbol?.GetAttribute("HpdActionConfigAttribute");
        if (symbol is null || attr is null) return null;
        var actionType = GetCtorString(attr, 0) ?? symbol.Name;
        return new ConfigInfo(
            actionType,
            GetNamedString(attr, "DisplayName") ?? ToDisplayName(actionType),
            GetNamedString(attr, "Description"),
            GetNamedEnum(attr, "Traits") ?? "None",
            symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ResolveFields(symbol),
            node.GetLocation(),
            IsPartial(node));
    }

    private static SourceInfo? ResolveSource(Compilation compilation, TypeDeclarationSyntax node)
    {
        var symbol = GetSymbol(compilation, node);
        if (symbol is null) return null;
        var attr = symbol.GetAttribute("HpdWebhookSourceAttribute");
        var trigger = "Webhook";
        attr ??= symbol.GetAttribute("HpdPollingSourceAttribute");
        if (attr is not null && attr.AttributeClass?.Name == "HpdPollingSourceAttribute") trigger = "Polling";
        attr ??= symbol.GetAttribute("HpdWebhookOrPollingSourceAttribute");
        if (attr is not null && attr.AttributeClass?.Name == "HpdWebhookOrPollingSourceAttribute") trigger = "Webhook";
        if (attr is null) return null;
        var sourceType = GetCtorString(attr, 0) ?? symbol.Name;
        var config = symbol.GetTypeMembers("Config").FirstOrDefault();
        var fromWebhook = symbol.GetMembers("FromWebhook").OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic);
        var poll = symbol.GetMembers("PollAsync").OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic);
        var register = symbol.GetMembers("RegisterAsync").OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic);
        var update = symbol.GetMembers("UpdateAsync").OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic);
        var unregister = symbol.GetMembers("UnregisterAsync").OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic);
        return new SourceInfo(
            sourceType,
            GetNamedString(attr, "DisplayName") ?? ToDisplayName(sourceType),
            GetNamedString(attr, "Description"),
            GetNamedString(attr, "AppId") ?? FirstSegment(sourceType),
            trigger,
            symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            config?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            config?.Name,
            config is null ? Array.Empty<FieldInfo>() : ResolveFields(config),
            fromWebhook is not null,
            poll is not null,
            fromWebhook,
            poll,
            register,
            update,
            unregister,
            node.GetLocation(),
            IsPartial(node));
    }

    private static ActionInfo? ResolveAction(Compilation compilation, TypeDeclarationSyntax node)
    {
        var symbol = GetSymbol(compilation, node);
        var attr = symbol?.GetAttribute("HpdConnectorActionAttribute");
        if (symbol is null || attr is null) return null;
        var actionType = GetCtorString(attr, 0) ?? symbol.Name;
        var configType = GetNamedType(attr, "ConfigType");
        var method = symbol.GetMembers("RunAsync").OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic);
        return new ActionInfo(
            actionType,
            actionType,
            GetNamedString(attr, "DisplayName") ?? ToDisplayName(actionType),
            GetNamedEnum(attr, "Traits") ?? "None",
            symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            configType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            configType?.Name,
            method,
            node.GetLocation(),
            IsPartial(node));
    }

    private static OptionInfo? ResolveOption(Compilation compilation, MethodDeclarationSyntax node)
    {
        var model = compilation.GetSemanticModel(node.SyntaxTree);
        var symbol = model.GetDeclaredSymbol(node) as IMethodSymbol;
        var attr = symbol?.GetAttribute("HpdConnectorOptionAttribute");
        if (symbol is null || attr is null || symbol.ContainingType is null) return null;
        var name = GetCtorString(attr, 0) ?? symbol.Name;
        return new OptionInfo(
            name,
            symbol.ContainingType.Name,
            symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol,
            node.GetLocation());
    }

    private static AssetCatalogInfo? ResolveAssetCatalog(Compilation compilation, TypeDeclarationSyntax node)
    {
        var symbol = GetSymbol(compilation, node);
        var attr = symbol?.GetAttribute("HpdConnectorAssetCatalogAttribute");
        if (symbol is null || attr is null) return null;
        var config = symbol.GetTypeMembers("Config").FirstOrDefault();
        return new AssetCatalogInfo(
            GetCtorString(attr, 0) ?? symbol.Name,
            symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            config?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            config?.Name,
            config is null ? Array.Empty<FieldInfo>() : ResolveFields(config),
            symbol.GetMembers("LoadAssetsAsync").OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic),
            node.GetLocation(),
            IsPartial(node));
    }

    private static MaterializationInfo? ResolveMaterialization(Compilation compilation, TypeDeclarationSyntax node)
    {
        var symbol = GetSymbol(compilation, node);
        var attr = symbol?.GetAttribute("HpdConnectorMaterializationAttribute");
        if (symbol is null || attr is null) return null;
        var configType = GetNamedType(attr, "ConfigType");
        return new MaterializationInfo(
            GetCtorString(attr, 0) ?? symbol.Name,
            symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            configType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            configType?.Name,
            configType is null ? Array.Empty<FieldInfo>() : ResolveFields(configType),
            symbol.GetMembers("RunAsync").OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic),
            node.GetLocation(),
            IsPartial(node));
    }

    private static CheckInfo? ResolveCheck(Compilation compilation, TypeDeclarationSyntax node)
    {
        var symbol = GetSymbol(compilation, node);
        var attr = symbol?.GetAttribute("HpdConnectorAssetCheckAttribute");
        if (symbol is null || attr is null) return null;
        return new CheckInfo(
            GetCtorString(attr, 0) ?? symbol.Name,
            symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.GetMembers("RunAsync").OfType<IMethodSymbol>().FirstOrDefault(m => m.IsStatic),
            node.GetLocation(),
            IsPartial(node));
    }

    private static IoInfo? ResolveIo(Compilation compilation, TypeDeclarationSyntax node)
    {
        var symbol = GetSymbol(compilation, node);
        var attr = symbol?.GetAttribute("HpdArtifactIOManagerAttribute");
        if (symbol is null || attr is null) return null;
        return new IoInfo(
            GetCtorString(attr, 0) ?? symbol.Name,
            symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            node.GetLocation(),
            IsPartial(node));
    }

    private static IReadOnlyList<OpenApiOperationInfo> ResolveOpenApiOperations(
        IReadOnlyList<ConnectorInfo> connectors,
        ImmutableArray<AdditionalText> additionalTexts)
    {
        var operations = new List<OpenApiOperationInfo>();
        foreach (var connector in connectors)
        {
            foreach (var spec in connector.OpenApiSpecs)
            {
                var text = FindAdditionalText(additionalTexts, spec.SpecPath);
                var source = text?.GetText()?.ToString();
                if (string.IsNullOrWhiteSpace(source))
                    continue;

                operations.AddRange(ParseOpenApiOperations(connector.Id, source!, spec.IncludeOperations));
            }
        }

        return operations;
    }

    private static AdditionalText? FindAdditionalText(
        ImmutableArray<AdditionalText> additionalTexts,
        string specPath)
    {
        return additionalTexts.FirstOrDefault(text =>
            string.Equals(text.Path, specPath, StringComparison.Ordinal) ||
            string.Equals(Path.GetFileName(text.Path), specPath, StringComparison.Ordinal) ||
            text.Path.EndsWith(specPath.Replace('/', Path.DirectorySeparatorChar), StringComparison.Ordinal) ||
            text.Path.EndsWith(specPath.Replace('\\', Path.DirectorySeparatorChar), StringComparison.Ordinal));
    }

    private static IReadOnlyList<OpenApiOperationInfo> ParseOpenApiOperations(
        string connectorId,
        string source,
        IReadOnlyList<string> includeOperations)
    {
        var include = includeOperations.Count == 0
            ? null
            : new HashSet<string>(includeOperations, StringComparer.Ordinal);
        using var document = JsonDocument.Parse(source);
        if (!document.RootElement.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object)
            return Array.Empty<OpenApiOperationInfo>();

        var result = new List<OpenApiOperationInfo>();
        foreach (var path in paths.EnumerateObject())
        {
            if (path.Value.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var method in path.Value.EnumerateObject())
            {
                if (!IsOpenApiHttpMethod(method.Name) || method.Value.ValueKind != JsonValueKind.Object)
                    continue;

                if (!method.Value.TryGetProperty("operationId", out var operationIdElement))
                    continue;

                var operationId = operationIdElement.GetString();
                if (string.IsNullOrWhiteSpace(operationId))
                    continue;
                if (include is not null && !include.Contains(operationId!))
                    continue;

                result.Add(new OpenApiOperationInfo(
                    connectorId,
                    operationId!,
                    method.Name.ToUpperInvariant(),
                    path.Name,
                    GetString(method.Value, "description") ?? GetString(method.Value, "summary"),
                    ParseOpenApiParameters(method.Value),
                    ParseOpenApiPayloadProperties(method.Value)));
            }
        }

        return result;
    }

    private static IReadOnlyList<OpenApiFieldInfo> ParseOpenApiParameters(JsonElement operation)
    {
        if (!operation.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Array)
            return Array.Empty<OpenApiFieldInfo>();

        var result = new List<OpenApiFieldInfo>();
        foreach (var parameter in parameters.EnumerateArray())
        {
            if (parameter.ValueKind != JsonValueKind.Object)
                continue;

            var location = ToOpenApiParameterLocation(GetString(parameter, "in"));
            if (location is null)
                continue;

            result.Add(new OpenApiFieldInfo(
                GetString(parameter, "name") ?? string.Empty,
                GetOpenApiSchemaType(parameter),
                GetBool(parameter, "required"),
                location,
                GetString(parameter, "description")));
        }

        return result.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToArray();
    }

    private static IReadOnlyList<OpenApiFieldInfo> ParseOpenApiPayloadProperties(JsonElement operation)
    {
        if (!operation.TryGetProperty("requestBody", out var requestBody) ||
            !requestBody.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("application/json", out var jsonContent) ||
            !jsonContent.TryGetProperty("schema", out var schema) ||
            !schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<OpenApiFieldInfo>();
        }

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var requiredElement) && requiredElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in requiredElement.EnumerateArray())
            {
                if (item.GetString() is { } requiredName)
                    required.Add(requiredName);
            }
        }

        return properties.EnumerateObject()
            .Select(property => new OpenApiFieldInfo(
                property.Name,
                GetOpenApiSchemaType(property.Value),
                required.Contains(property.Name),
                "Body",
                GetString(property.Value, "description")))
            .ToArray();
    }

    private static string GetOpenApiSchemaType(JsonElement element)
    {
        if (element.TryGetProperty("schema", out var schema))
            element = schema;

        return GetString(element, "type") ?? "string";
    }

    private static string? ToOpenApiParameterLocation(string? value)
        => value switch
        {
            "path" => "Path",
            "query" => "Query",
            "header" => "Header",
            "cookie" => "Cookie",
            _ => null
        };

    private static bool IsOpenApiHttpMethod(string value)
        => value is "get" or "post" or "put" or "delete" or "patch" or "head" or "options" or "trace";

    private static void ReportDiagnostics(SourceProductionContext context, Model model)
    {
        foreach (var item in model.Connectors.Where(c => !c.IsPartial))
            context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.MissingPartial, item.Location, item.ClassName, "HpdConnector"));
        foreach (var item in model.Sources.Where(s => !s.IsPartial))
            context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.MissingPartial, item.Location, item.TypeName, "HpdWebhookSource/HpdPollingSource"));
        foreach (var item in model.Actions.Where(a => !a.IsPartial))
            context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.MissingPartial, item.Location, item.TypeName, "HpdConnectorAction"));
        foreach (var item in model.AssetCatalogs.Where(a => !a.IsPartial))
            context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.MissingPartial, item.Location, item.TypeName, "HpdConnectorAssetCatalog"));
        foreach (var item in model.Materializations.Where(m => !m.IsPartial))
            context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.MissingPartial, item.Location, item.TypeName, "HpdConnectorMaterialization"));
        foreach (var item in model.Checks.Where(c => !c.IsPartial))
            context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.MissingPartial, item.Location, item.TypeName, "HpdConnectorAssetCheck"));
        foreach (var item in model.IoManagers.Where(i => !i.IsPartial))
            context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.MissingPartial, item.Location, item.TypeName, "HpdArtifactIOManager"));
        foreach (var item in model.Connectors.Where(c => c.JsonContextType is null))
            context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.MissingJsonContext, item.Location, item.Id));

        ReportDuplicates(context, "connector", model.Connectors.Select(c => (c.Id, c.ClassName, c.Location)));
        ReportDuplicates(context, "connection", model.Connections.Select(c => (c.ConnectionType, c.TypeName, c.Location)));
        ReportDuplicates(context, "config descriptor", model.Configs.Select(c => (c.ActionType, c.TypeName, c.Location)));
        ReportDuplicates(context, "source", model.Sources.Select(s => (s.SourceType, s.TypeName, s.Location)));
        ReportDuplicates(context, "action", model.Actions.Select(a => (a.ActionType, a.TypeName, a.Location)));
        ReportDuplicates(context, "option provider", model.Options.Select(o => (o.Name, o.TypeName, o.Location)));
        ReportDuplicates(context, "asset catalog", model.AssetCatalogs.Select(a => (a.Name, a.TypeName, a.Location)));
        ReportDuplicates(context, "materialization", model.Materializations.Select(m => (m.Type, m.TypeName, m.Location)));
        ReportDuplicates(context, "asset check", model.Checks.Select(c => (c.Name, c.TypeName, c.Location)));
        ReportDuplicates(context, "artifact IO manager", model.IoManagers.Select(i => (i.Name, i.TypeName, i.Location)));

        var connections = new HashSet<string>(model.Connections.Select(c => c.ConnectionType), StringComparer.Ordinal);
        var options = new HashSet<string>(model.Options.Select(o => o.Name), StringComparer.Ordinal);
        foreach (var action in model.Actions.Where(a => a.RunMethod is null))
            context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.InvalidSignature, action.Location, "Connector action", action.ActionType));
        foreach (var source in model.Sources)
        {
            if (source.TriggerKind == "Webhook" && !source.HasFromWebhook)
                context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.InvalidSignature, source.Location, "Webhook source", source.SourceType));
            if (source.TriggerKind == "Polling" && !source.HasPoll)
                context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.InvalidSignature, source.Location, "Polling source", source.SourceType));
        }
        foreach (var asset in model.AssetCatalogs.Where(a => a.LoadMethod is null))
            context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.InvalidSignature, asset.Location, "Asset catalog", asset.Name));
        foreach (var materialization in model.Materializations.Where(m => m.RunMethod is null))
            context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.InvalidSignature, materialization.Location, "Materialization", materialization.Type));
        foreach (var check in model.Checks.Where(c => c.RunMethod is null))
            context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.InvalidSignature, check.Location, "Asset check", check.Name));

        foreach (var config in model.Configs)
        {
            foreach (var field in config.Fields)
            {
                if (field.ConnectionType is not null && !connections.Contains(field.ConnectionType))
                    context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.UnknownReference, config.Location, config.TypeName, "connection type", field.ConnectionType));
                if (field.OptionProviderName is not null && !options.Contains(field.OptionProviderName))
                    context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.UnknownReference, config.Location, config.TypeName, "option provider", field.OptionProviderName));
            }
        }
    }

    private static string GenerateJsonContext(Model model)
    {
        var contextType = model.Connectors.FirstOrDefault(c => c.JsonContextType is not null)?.JsonContextType;

        var sb = Header();
        sb.AppendLine("namespace HPD.Graph.Connectors.Generated;");
        sb.AppendLine("internal static class HPDGraphConnectorJsonContext");
        sb.AppendLine("{");
        if (contextType is not null)
        {
            sb.AppendLine("    public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web);");
            sb.AppendLine("    public static System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>? GetTypeInfo<T>()");
            sb.AppendLine("    {");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine($"            return (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>){contextType}.Default.GetTypeInfo(typeof(T))!;");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (System.NotSupportedException)");
            sb.AppendLine("        {");
            sb.AppendLine("            return null;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
        }
        else
        {
            sb.AppendLine("    public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web);");
            sb.AppendLine("    public static System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>? GetTypeInfo<T>() => null;");
        }
        sb.AppendLine("    public static T? Deserialize<T>(string json)");
        sb.AppendLine("    {");
        sb.AppendLine("        var typeInfo = GetTypeInfo<T>();");
        sb.AppendLine("        return typeInfo is null ? System.Text.Json.JsonSerializer.Deserialize<T>(json, Options) : System.Text.Json.JsonSerializer.Deserialize(json, typeInfo);");
        sb.AppendLine("    }");
        sb.AppendLine("    public static System.Text.Json.JsonElement SerializeToElement<T>(T value)");
        sb.AppendLine("    {");
        sb.AppendLine("        var typeInfo = GetTypeInfo<T>();");
        sb.AppendLine("        return typeInfo is null ? System.Text.Json.JsonSerializer.SerializeToElement(value, Options) : System.Text.Json.JsonSerializer.SerializeToElement(value, typeInfo);");
        sb.AppendLine("    }");
        sb.AppendLine("    public static System.Text.Json.JsonElement SerializeObjectToElement(object value)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (value is System.Text.Json.JsonElement element) return element.Clone();");
        sb.AppendLine("        if (value is System.Text.Json.JsonDocument document) return document.RootElement.Clone();");
        if (contextType is not null)
        {
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine($"            var typeInfo = {contextType}.Default.GetTypeInfo(value.GetType());");
            sb.AppendLine("            if (typeInfo is not null) return System.Text.Json.JsonSerializer.SerializeToElement(value, typeInfo);");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (System.NotSupportedException)");
            sb.AppendLine("        {");
            sb.AppendLine("        }");
        }
        sb.AppendLine("        return System.Text.Json.JsonSerializer.SerializeToElement(value, value.GetType(), Options);");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateRegistry(Model model)
    {
        var sb = Header();
        sb.AppendLine("namespace HPD.Graph.Connectors.Generated;");
        sb.AppendLine("internal sealed record ConnectorRegistration(string ConnectorId, System.Type ConnectorType, string DefaultPath);");
        sb.AppendLine("internal static class ConnectorRegistry");
        sb.AppendLine("{");
        sb.AppendLine("    public static readonly ConnectorRegistration[] All =");
        sb.AppendLine("    [");
        foreach (var connector in model.Connectors)
            sb.AppendLine($"        new ConnectorRegistration(\"{Esc(connector.Id)}\", typeof({connector.FullyQualifiedName}), \"/workflows/sources/{Esc(connector.Id)}/webhook\"),");
        sb.AppendLine("    ];");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateDescriptors(Model model)
    {
        var connector = model.Connectors[0];
        var sb = Header();
        sb.AppendLine("namespace HPD.Graph.Connectors.Generated;");
        sb.AppendLine("internal static class GeneratedConnectorDescriptors");
        sb.AppendLine("{");
        sb.AppendLine("    public static HPD.Graph.Connectors.Abstractions.Descriptors.ConnectorPackageDescriptor Create()");
        sb.AppendLine("    {");
        sb.AppendLine("        return new HPD.Graph.Connectors.Abstractions.Descriptors.ConnectorPackageDescriptor");
        sb.AppendLine("        {");
        sb.AppendLine($"            ConnectorId = \"{Esc(connector.Id)}\",");
        sb.AppendLine($"            DisplayName = \"{Esc(connector.DisplayName)}\",");
        AppendOptionalString(sb, "Description", connector.Description, 12);
        AppendOptionalString(sb, "Version", connector.Version, 12);
        AppendOptionalString(sb, "IconUri", connector.IconUri, 12);
        sb.AppendLine("            Apps =");
        sb.AppendLine("            [");
        foreach (var app in model.Connections.Select(c => c.AppId).Concat(model.Sources.Select(s => s.AppId)).Distinct(StringComparer.Ordinal))
            sb.AppendLine($"                new HPD.Graph.Connectors.Abstractions.Descriptors.AppDescriptor {{ AppId = \"{Esc(app)}\", DisplayName = \"{Esc(ToDisplayName(app))}\" }},");
        sb.AppendLine("            ],");
        sb.AppendLine("            Connections =");
        sb.AppendLine("            [");
        foreach (var c in model.Connections)
        {
            sb.AppendLine("                new HPD.Graph.Connectors.Abstractions.Connections.ConnectionDescriptor");
            sb.AppendLine("                {");
            sb.AppendLine($"                    ConnectionType = \"{Esc(c.ConnectionType)}\", AppId = \"{Esc(c.AppId)}\", DisplayName = \"{Esc(c.DisplayName)}\",");
            sb.AppendLine($"                    AuthKind = HPD.Graph.Connectors.Abstractions.Connections.ConnectionAuthKind.{c.AuthKind}");
            sb.AppendLine("                },");
        }
        sb.AppendLine("            ],");
        sb.AppendLine("            Configs =");
        sb.AppendLine("            [");
        foreach (var config in model.Configs)
        {
            sb.AppendLine("                new HPD.Graph.Connectors.Abstractions.Configuration.ConnectorConfigDescriptor");
            sb.AppendLine("                {");
            sb.AppendLine($"                    ConfigType = \"{Esc(config.FullyQualifiedName)}\", DisplayName = \"{Esc(config.DisplayName)}\",");
            AppendFieldsForConfigDescriptor(sb, config, 20);
            sb.AppendLine("                },");
        }
        sb.AppendLine("            ],");
        sb.AppendLine("            Sources =");
        sb.AppendLine("            [");
        foreach (var s in model.Sources)
        {
            sb.AppendLine("                new HPD.Graph.Connectors.Abstractions.Sources.WorkflowSourceDescriptor");
            sb.AppendLine("                {");
            sb.AppendLine($"                    SourceType = \"{Esc(s.SourceType)}\", AppId = \"{Esc(s.AppId)}\", DisplayName = \"{Esc(s.DisplayName)}\",");
            sb.AppendLine($"                    TriggerKind = HPD.Graph.Connectors.Abstractions.Sources.SourceTriggerKind.{s.TriggerKind}");
            sb.AppendLine("                },");
        }
        sb.AppendLine("            ],");
        sb.AppendLine("            ConnectorActions =");
        sb.AppendLine("            [");
        foreach (var a in model.Actions)
        {
            sb.AppendLine("                new HPD.Graph.Connectors.Abstractions.Actions.ConnectorActionDescriptor");
            sb.AppendLine("                {");
            sb.AppendLine($"                    ActionType = \"{Esc(a.ActionType)}\", HandlerName = \"{Esc(a.HandlerName)}\", AppId = \"{Esc(FirstSegment(a.ActionType))}\", DisplayName = \"{Esc(a.DisplayName)}\",");
            if (a.ConfigType is not null) sb.AppendLine($"                    ConfigType = typeof({a.ConfigType}),");
            AppendFields(sb, model, a.ConfigType, 20);
            sb.AppendLine($"                    Traits = HPD.Graph.Connectors.Abstractions.Actions.ConnectorOperationTraits.{a.Traits}");
            sb.AppendLine("                },");
        }
        foreach (var operation in model.OpenApiOperations)
        {
            sb.AppendLine("                new HPD.Graph.Connectors.Abstractions.Actions.ConnectorActionDescriptor");
            sb.AppendLine("                {");
            sb.AppendLine($"                    ActionType = \"{Esc(operation.ConnectorId)}.{Esc(operation.OperationId)}\", HandlerName = \"{Esc(operation.ConnectorId)}.{Esc(operation.OperationId)}\", AppId = \"{Esc(operation.ConnectorId)}\", DisplayName = \"{Esc(ToDisplayName(operation.OperationId))}\",");
            AppendOpenApiFields(sb, operation, 20);
            sb.AppendLine($"                    Traits = HPD.Graph.Connectors.Abstractions.Actions.ConnectorOperationTraits.{InferOpenApiTraits(operation.Method)},");
            sb.AppendLine("                    Metadata = new System.Collections.Generic.Dictionary<string, string>");
            sb.AppendLine("                    {");
            sb.AppendLine($"                        [\"openapi.operationId\"] = \"{Esc(operation.OperationId)}\",");
            sb.AppendLine($"                        [\"openapi.method\"] = \"{Esc(operation.Method)}\",");
            sb.AppendLine($"                        [\"openapi.path\"] = \"{Esc(operation.Path)}\"");
            sb.AppendLine("                    }");
            sb.AppendLine("                },");
        }
        sb.AppendLine("            ],");
        sb.AppendLine("            Actions =");
        sb.AppendLine("            [");
        foreach (var a in model.Actions)
        {
            sb.AppendLine("                new HPD.Graph.Abstractions.Discovery.HandlerDescriptor");
            sb.AppendLine("                {");
            sb.AppendLine($"                    HandlerName = \"{Esc(a.HandlerName)}\", DisplayName = \"{Esc(a.DisplayName)}\", Domain = \"{Esc(FirstSegment(a.ActionType))}\",");
            sb.AppendLine($"                    HandlerType = typeof({a.FullyQualifiedName}).FullName!, ContextType = typeof(HPD.Graph.Core.Context.GraphContext).FullName!,");
            sb.AppendLine("                    Metadata = new System.Collections.Generic.Dictionary<string, string>");
            sb.AppendLine("                    {");
            sb.AppendLine($"                        [\"connector.actionType\"] = \"{Esc(a.ActionType)}\",");
            sb.AppendLine($"                        [\"connector.traits\"] = \"{Esc(a.Traits)}\"");
            sb.AppendLine("                    }");
            sb.AppendLine("                },");
        }
        foreach (var operation in model.OpenApiOperations)
        {
            sb.AppendLine("                new HPD.Graph.Abstractions.Discovery.HandlerDescriptor");
            sb.AppendLine("                {");
            sb.AppendLine($"                    HandlerName = \"{Esc(operation.ConnectorId)}.{Esc(operation.OperationId)}\", DisplayName = \"{Esc(ToDisplayName(operation.OperationId))}\", Domain = \"{Esc(operation.ConnectorId)}\",");
            sb.AppendLine("                    HandlerType = typeof(HPD.Graph.Connectors.OpenApi.Handlers.OpenApiCallOperationHandler).FullName!, ContextType = typeof(HPD.Graph.Core.Context.GraphContext).FullName!,");
            AppendOpenApiInputs(sb, operation, 20);
            sb.AppendLine("                    Outputs =");
            sb.AppendLine("                    [");
            sb.AppendLine("                        new HPD.Graph.Abstractions.Discovery.SocketDescriptor { Name = \"response\", TypeName = typeof(object).FullName!, Direction = HPD.Graph.Abstractions.Discovery.SocketDirection.Output, Required = false },");
            sb.AppendLine("                        new HPD.Graph.Abstractions.Discovery.SocketDescriptor { Name = \"error\", TypeName = typeof(object).FullName!, Direction = HPD.Graph.Abstractions.Discovery.SocketDirection.Output, Required = false },");
            sb.AppendLine("                    ],");
            sb.AppendLine("                    Metadata = new System.Collections.Generic.Dictionary<string, string>");
            sb.AppendLine("                    {");
            sb.AppendLine($"                        [\"connector.actionType\"] = \"{Esc(operation.ConnectorId)}.{Esc(operation.OperationId)}\",");
            sb.AppendLine($"                        [\"openapi.operationId\"] = \"{Esc(operation.OperationId)}\",");
            sb.AppendLine($"                        [\"openapi.method\"] = \"{Esc(operation.Method)}\",");
            sb.AppendLine($"                        [\"openapi.path\"] = \"{Esc(operation.Path)}\"");
            sb.AppendLine("                    }");
            sb.AppendLine("                },");
        }
        sb.AppendLine("            ],");
        sb.AppendLine("            OptionProviders =");
        sb.AppendLine("            [");
        foreach (var option in model.Options)
            sb.AppendLine($"                \"{Esc(option.Name)}\",");
        sb.AppendLine("            ],");
        sb.AppendLine("            ArtifactIOManagers =");
        sb.AppendLine("            [");
        foreach (var io in model.IoManagers)
            sb.AppendLine($"                \"{Esc(io.Name)}\",");
        sb.AppendLine("            ],");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateRegistration(Model model)
    {
        var connector = model.Connectors[0];
        var pascal = ToPascal(connector.Id);
        var sb = Header();
        sb.AppendLine($"namespace {connector.Namespace};");
        sb.AppendLine($"public static partial class {pascal}ConnectorServiceCollectionExtensions");
        sb.AppendLine("{");
        sb.AppendLine($"    public static Microsoft.Extensions.DependencyInjection.IServiceCollection Add{pascal}Connector(this Microsoft.Extensions.DependencyInjection.IServiceCollection services, params HPD.Graph.Base.BaseGraphActivationDefinition[] graphs)");
        sb.AppendLine("    {");
        sb.AppendLine("        HPD.Graph.Connectors.Core.DependencyInjection.ConnectorCoreServiceCollectionExtensions.AddHPDGraphConnectorsCore(services, graphs);");
        if (model.OpenApiOperations.Count > 0)
            sb.AppendLine("        HPD.Graph.Connectors.OpenApi.DependencyInjection.OpenApiConnectorServiceCollectionExtensions.AddHPDGraphConnectorsOpenApi(services);");
        sb.AppendLine($"        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<{connector.FullyQualifiedName}>(services);");
        sb.AppendLine("        services.AddSingleton(HPD.Graph.Connectors.Generated.GeneratedConnectorDescriptors.Create());");
        foreach (var a in model.Actions)
            sb.AppendLine($"        services.AddScoped<HPD.Graph.Abstractions.Handlers.IGraphNodeHandler<HPD.Graph.Core.Context.GraphContext>, HPD.Graph.Connectors.Generated.{Safe(a.ActionType)}ActionHandler>();");
        foreach (var operation in model.OpenApiOperations)
        {
            sb.AppendLine($"        services.AddSingleton(new HPD.Graph.Connectors.OpenApi.Catalog.OpenApiOperationRegistration(\"{Esc(operation.ConnectorId)}\", HPD.Graph.Connectors.Generated.{Safe(operation.ConnectorId + "." + operation.OperationId)}OpenApiOperationFactory.Create()));");
            sb.AppendLine($"        services.AddScoped<HPD.Graph.Abstractions.Handlers.IGraphNodeHandler<HPD.Graph.Core.Context.GraphContext>, HPD.Graph.Connectors.Generated.{Safe(operation.ConnectorId + "." + operation.OperationId)}OpenApiActionHandler>();");
        }
        foreach (var s in model.Sources)
        {
            var wrapper = $"HPD.Graph.Connectors.Generated.{Safe(s.SourceType)}SourceProvider";
            sb.AppendLine($"        services.AddSingleton<{wrapper}>();");
            sb.AppendLine($"        services.AddSingleton<HPD.Graph.Connectors.Abstractions.Sources.IWorkflowSourceProvider>(sp => sp.GetRequiredService<{wrapper}>());");
            if (s.HasFromWebhook)
                sb.AppendLine($"        services.AddSingleton<HPD.Graph.Connectors.Abstractions.Sources.IWebhookWorkflowSourceProvider>(sp => sp.GetRequiredService<{wrapper}>());");
            if (s.HasPoll)
                sb.AppendLine($"        services.AddSingleton<HPD.Graph.Connectors.Abstractions.Sources.IPollingWorkflowSourceProvider>(sp => sp.GetRequiredService<{wrapper}>());");
        }
        foreach (var option in model.Options)
            sb.AppendLine($"        services.AddSingleton<HPD.Graph.Connectors.Abstractions.Options.IConnectorOptionProvider, HPD.Graph.Connectors.Generated.{Safe(option.Name)}OptionProvider>();");
        foreach (var asset in model.AssetCatalogs)
            sb.AppendLine($"        services.AddSingleton<HPD.Graph.Connectors.Abstractions.Assets.IConnectorAssetCatalogProvider, HPD.Graph.Connectors.Generated.{Safe(asset.Name)}AssetCatalogProvider>();");
        foreach (var materialization in model.Materializations)
            sb.AppendLine($"        services.AddSingleton<HPD.Graph.Connectors.Abstractions.Materialization.IConnectorMaterializationProvider, HPD.Graph.Connectors.Generated.{Safe(materialization.Type)}MaterializationProvider>();");
        foreach (var check in model.Checks)
            sb.AppendLine($"        services.AddSingleton<HPD.Graph.Connectors.Abstractions.Materialization.IConnectorAssetCheckProvider, HPD.Graph.Connectors.Generated.{Safe(check.Name)}AssetCheckProvider>();");
        foreach (var io in model.IoManagers)
            sb.AppendLine($"        services.AddSingleton<HPD.Graph.Connectors.Abstractions.IO.IArtifactIOManager, {io.FullyQualifiedName}>();");
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine($"public static partial class {pascal}ConnectorEndpointRouteBuilderExtensions");
        sb.AppendLine("{");
        sb.AppendLine($"    public static Microsoft.AspNetCore.Builder.IEndpointConventionBuilder Map{pascal}ConnectorWebhooks(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, string path = \"/workflows/sources/{connector.Id}/webhook\")");
        sb.AppendLine("    {");
        sb.AppendLine($"        var connector = app.ServiceProvider.GetRequiredService<{connector.FullyQualifiedName}>();");
        sb.AppendLine("        return app.MapPost(path, (Microsoft.AspNetCore.Http.HttpContext ctx) => connector.HandleWebhookAsync(ctx));");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateWrappers(Model model)
    {
        var sb = Header();
        sb.AppendLine("namespace HPD.Graph.Connectors.Generated;");
        foreach (var action in model.Actions) GenerateActionWrapper(sb, model, action);
        foreach (var operation in model.OpenApiOperations) GenerateOpenApiOperationWrapper(sb, operation);
        foreach (var source in model.Sources) GenerateSourceWrapper(sb, model, source);
        foreach (var option in model.Options) GenerateOptionWrapper(sb, option);
        foreach (var asset in model.AssetCatalogs) GenerateAssetCatalogWrapper(sb, model, asset);
        foreach (var materialization in model.Materializations) GenerateMaterializationWrapper(sb, model, materialization);
        foreach (var check in model.Checks) GenerateCheckWrapper(sb, check);
        return sb.ToString();
    }

    private static void GenerateActionWrapper(StringBuilder sb, Model model, ActionInfo action)
    {
        var name = Safe(action.ActionType) + "ActionHandler";
        sb.AppendLine($"internal sealed class {name} : HPD.Graph.Abstractions.Handlers.IGraphNodeHandler<HPD.Graph.Core.Context.GraphContext>");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly System.IServiceProvider _services;");
        sb.AppendLine($"    public {name}(System.IServiceProvider services) => _services = services;");
        sb.AppendLine($"    public string HandlerName => \"{Esc(action.HandlerName)}\";");
        sb.AppendLine("    public async System.Threading.Tasks.Task<HPD.Graph.Abstractions.Execution.NodeExecutionResult> ExecuteAsync(HPD.Graph.Core.Context.GraphContext context, HPD.Graph.Abstractions.Handlers.HandlerInputs inputs, System.Threading.CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        if (action.RunMethod is null)
        {
            sb.AppendLine("        throw new System.InvalidOperationException(\"Connector action requires a static RunAsync method.\");");
        }
        else
        {
            if (action.ConfigType is not null)
            {
                sb.AppendLine($"        var config = DeserializeNodeConfig<{action.ConfigType}>(context);");
            }
            var connectionIdExpression = FindConnectionIdExpression(model, action.ConfigType, "config");
            var args = BuildMethodArguments(action.RunMethod, action.ConfigType, "config", "context", "cancellationToken", serviceProviderVariable: "_services", connectionIdExpression: connectionIdExpression, materializationContext: null);
            sb.AppendLine($"        var result = await {action.FullyQualifiedName}.{action.RunMethod.Name}({args}).ConfigureAwait(false);");
            sb.AppendLine("        var output = new System.Collections.Generic.Dictionary<string, object>();");
            sb.AppendLine("        if (result is not null) output[\"result\"] = result;");
            sb.AppendLine("        return HPD.Graph.Abstractions.Execution.NodeExecutionResult.Success.Single(output, System.TimeSpan.Zero, new HPD.Graph.Abstractions.Execution.NodeExecutionMetadata());");
        }
        sb.AppendLine("    }");
        sb.AppendLine("    private static T DeserializeNodeConfig<T>(HPD.Graph.Core.Context.GraphContext context) where T : new()");
        sb.AppendLine("    {");
        sb.AppendLine("        var node = context.CurrentNodeId is null ? null : context.Graph.GetNode(context.CurrentNodeId);");
        sb.AppendLine("        return node?.Config is null ? new T() : HPDGraphConnectorJsonContext.Deserialize<T>(node.Config.Value.GetRawText()) ?? new T();");
        sb.AppendLine("    }");
        AppendResolveConnectionHelper(sb);
        sb.AppendLine("}");
    }

    private static void GenerateOpenApiOperationWrapper(StringBuilder sb, OpenApiOperationInfo operation)
    {
        var safeName = Safe(operation.ConnectorId + "." + operation.OperationId);
        sb.AppendLine($"internal static class {safeName}OpenApiOperationFactory");
        sb.AppendLine("{");
        sb.AppendLine("    public static HPD.OpenApi.Core.Model.RestApiOperation Create()");
        sb.AppendLine("    {");
        sb.AppendLine("        return new HPD.OpenApi.Core.Model.RestApiOperation");
        sb.AppendLine("        {");
        sb.AppendLine($"            Id = \"{Esc(operation.OperationId)}\",");
        sb.AppendLine($"            Method = new System.Net.Http.HttpMethod(\"{Esc(operation.Method)}\"),");
        sb.AppendLine($"            Path = \"{Esc(operation.Path)}\",");
        AppendOptionalString(sb, "Description", operation.Description, 12);
        sb.AppendLine("            Parameters =");
        sb.AppendLine("            [");
        foreach (var parameter in operation.Parameters)
        {
            sb.AppendLine("                new HPD.OpenApi.Core.Model.RestApiParameter");
            sb.AppendLine("                {");
            sb.AppendLine($"                    Name = \"{Esc(parameter.Name)}\",");
            sb.AppendLine($"                    Type = \"{Esc(parameter.TypeName)}\",");
            sb.AppendLine($"                    IsRequired = {(parameter.Required ? "true" : "false")},");
            sb.AppendLine($"                    Location = HPD.OpenApi.Core.Model.RestApiParameterLocation.{Esc(parameter.Location)}");
            sb.AppendLine("                },");
        }
        sb.AppendLine("            ],");
        if (operation.PayloadProperties.Count > 0)
        {
            sb.AppendLine("            Payload = new HPD.OpenApi.Core.Model.RestApiPayload");
            sb.AppendLine("            {");
            sb.AppendLine("                MediaType = \"application/json\",");
            sb.AppendLine("                Properties =");
            sb.AppendLine("                [");
            foreach (var property in operation.PayloadProperties)
            {
                sb.AppendLine("                    new HPD.OpenApi.Core.Model.RestApiPayloadProperty");
                sb.AppendLine("                    {");
                sb.AppendLine($"                        Name = \"{Esc(property.Name)}\",");
                sb.AppendLine($"                        Type = \"{Esc(property.TypeName)}\",");
                sb.AppendLine($"                        IsRequired = {(property.Required ? "true" : "false")}");
                sb.AppendLine("                    },");
            }
            sb.AppendLine("                ]");
            sb.AppendLine("            },");
        }
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine($"internal sealed class {safeName}OpenApiActionHandler : HPD.Graph.Abstractions.Handlers.IGraphNodeHandler<HPD.Graph.Core.Context.GraphContext>");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly HPD.Graph.Connectors.OpenApi.Catalog.IOpenApiOperationCatalog _operations;");
        sb.AppendLine("    private readonly HPD.Graph.Connectors.Abstractions.Connections.IConnectionProvider _connections;");
        sb.AppendLine("    private readonly System.Collections.Generic.IEnumerable<HPD.Graph.Connectors.OpenApi.IOpenApiConnectionAdapter> _adapters;");
        sb.AppendLine($"    public {safeName}OpenApiActionHandler(HPD.Graph.Connectors.OpenApi.Catalog.IOpenApiOperationCatalog operations, HPD.Graph.Connectors.Abstractions.Connections.IConnectionProvider connections, System.Collections.Generic.IEnumerable<HPD.Graph.Connectors.OpenApi.IOpenApiConnectionAdapter> adapters)");
        sb.AppendLine("    {");
        sb.AppendLine("        _operations = operations;");
        sb.AppendLine("        _connections = connections;");
        sb.AppendLine("        _adapters = adapters;");
        sb.AppendLine("    }");
        sb.AppendLine($"    public string HandlerName => \"{Esc(operation.ConnectorId)}.{Esc(operation.OperationId)}\";");
        sb.AppendLine("    public System.Threading.Tasks.Task<HPD.Graph.Abstractions.Execution.NodeExecutionResult> ExecuteAsync(HPD.Graph.Core.Context.GraphContext context, HPD.Graph.Abstractions.Handlers.HandlerInputs inputs, System.Threading.CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        return new HPD.Graph.Connectors.OpenApi.Handlers.OpenApiCallOperationHandler(_operations, _connections, _adapters).ExecuteAsync(context, inputs, cancellationToken);");
        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    private static void GenerateSourceWrapper(StringBuilder sb, Model model, SourceInfo source)
    {
        var name = Safe(source.SourceType) + "SourceProvider";
        sb.AppendLine($"internal sealed class {name} : HPD.Graph.Connectors.Abstractions.Sources.IWebhookWorkflowSourceProvider, HPD.Graph.Connectors.Abstractions.Sources.IPollingWorkflowSourceProvider");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly HPD.Graph.Connectors.Abstractions.Sources.IWorkflowSourceDispatcher _dispatcher;");
        sb.AppendLine("    private readonly System.IServiceProvider _services;");
        sb.AppendLine($"    public {name}(HPD.Graph.Connectors.Abstractions.Sources.IWorkflowSourceDispatcher dispatcher, System.IServiceProvider services)");
        sb.AppendLine("    {");
        sb.AppendLine("        _dispatcher = dispatcher;");
        sb.AppendLine("        _services = services;");
        sb.AppendLine("    }");
        sb.AppendLine($"    public string SourceType => \"{Esc(source.SourceType)}\";");
        GenerateSourceLifecycleMethod(sb, model, source, source.RegisterMethod, "RegisterAsync", "source", "string.Empty");
        GenerateSourceLifecycleMethod(sb, model, source, source.UpdateMethod, "UpdateAsync", "source", "string.Empty");
        GenerateSourceLifecycleMethod(sb, model, source, source.UnregisterMethod, "UnregisterAsync", "null", "sourceId");
        sb.AppendLine("    public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<HPD.Graph.Connectors.Abstractions.Sources.WorkflowSourceStatus>> GetStatusAsync(System.Threading.CancellationToken ct = default) => System.Threading.Tasks.Task.FromResult<System.Collections.Generic.IReadOnlyList<HPD.Graph.Connectors.Abstractions.Sources.WorkflowSourceStatus>>(System.Array.Empty<HPD.Graph.Connectors.Abstractions.Sources.WorkflowSourceStatus>());");
        sb.AppendLine("    public async System.Threading.Tasks.Task ReceiveAsync(HPD.Graph.Connectors.Abstractions.Sources.WorkflowSource source, HPD.Graph.Connectors.Abstractions.Sources.WebhookEnvelope envelope, System.Threading.CancellationToken ct = default)");
        sb.AppendLine("    {");
        if (source.HasFromWebhook)
        {
            if (source.ConfigType is not null)
                sb.AppendLine($"        var config = source.Config is null ? new {source.ConfigType}() : HPDGraphConnectorJsonContext.Deserialize<{source.ConfigType}>(source.Config.Value.GetRawText()) ?? new {source.ConfigType}();");
            var configArg = source.ConfigType is null ? "" : ", config";
            if (source.FromWebhookMethod is not null)
            {
                var args = BuildMethodArguments(source.FromWebhookMethod, source.ConfigType, "config", null, "ct", envelopeVariable: "envelope", serviceProviderVariable: "_services", connectionIdExpression: FindConnectionIdExpression(model, source.ConfigType, "config"));
                sb.AppendLine($"        var candidate = {source.FullyQualifiedName}.{source.FromWebhookMethod.Name}({args});");
            }
            else
            {
                sb.AppendLine($"        var candidate = {source.FullyQualifiedName}.FromWebhook(envelope{configArg});");
            }
            sb.AppendLine("        if (candidate is null) return;");
            sb.AppendLine("        var payload = ToPayloadElement(candidate.Payload);");
            sb.AppendLine("        await _dispatcher.DispatchAsync(new HPD.Graph.Connectors.Abstractions.Events.WorkflowSourceEmittedEvent { SourceId = source.SourceId, GraphId = source.GraphId, SourceType = source.SourceType, Payload = payload, EventId = candidate.EventId, Summary = candidate.Summary, OccurredAt = candidate.OccurredAt ?? System.DateTimeOffset.UtcNow, DedupeStrategy = candidate.DedupeStrategy ?? HPD.Graph.Connectors.Abstractions.Sources.DedupeStrategy.Unique, Metadata = candidate.Metadata }, ct).ConfigureAwait(false);");
        }
        sb.AppendLine("    }");
        sb.AppendLine("    public async System.Threading.Tasks.Task PollAsync(HPD.Graph.Connectors.Abstractions.Sources.WorkflowSource source, HPD.Graph.Connectors.Abstractions.Sources.WorkflowSourceState? state, System.Threading.CancellationToken ct = default)");
        sb.AppendLine("    {");
        if (source.HasPoll)
        {
            if (source.ConfigType is not null)
                sb.AppendLine($"        var config = source.Config is null ? new {source.ConfigType}() : HPDGraphConnectorJsonContext.Deserialize<{source.ConfigType}>(source.Config.Value.GetRawText()) ?? new {source.ConfigType}();");
            var args = source.PollMethod is null
                ? (source.ConfigType is null ? "ct" : "config, ct")
                : BuildMethodArguments(source.PollMethod, source.ConfigType, "config", null, "ct", serviceProviderVariable: "_services", connectionIdExpression: FindConnectionIdExpression(model, source.ConfigType, "config"));
            sb.AppendLine($"        await foreach (var candidate in {source.FullyQualifiedName}.{source.PollMethod?.Name ?? "PollAsync"}({args}).WithCancellation(ct).ConfigureAwait(false))");
            sb.AppendLine("        {");
            sb.AppendLine("            var payload = ToPayloadElement(candidate.Payload);");
            sb.AppendLine("            await _dispatcher.DispatchAsync(new HPD.Graph.Connectors.Abstractions.Events.WorkflowSourceEmittedEvent { SourceId = source.SourceId, GraphId = source.GraphId, SourceType = source.SourceType, Payload = payload, EventId = candidate.EventId, Summary = candidate.Summary, OccurredAt = candidate.OccurredAt ?? System.DateTimeOffset.UtcNow, DedupeStrategy = candidate.DedupeStrategy ?? HPD.Graph.Connectors.Abstractions.Sources.DedupeStrategy.Unique, Metadata = candidate.Metadata }, ct).ConfigureAwait(false);");
            sb.AppendLine("        }");
        }
        sb.AppendLine("    }");
        sb.AppendLine("    private static System.Text.Json.JsonElement ToPayloadElement(object payload)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (payload is System.Text.Json.JsonElement element) return element.Clone();");
        sb.AppendLine("        if (payload is System.Text.Json.JsonDocument document) return document.RootElement.Clone();");
        sb.AppendLine("        return HPDGraphConnectorJsonContext.SerializeObjectToElement(payload);");
        sb.AppendLine("    }");
        AppendResolveConnectionHelper(sb);
        sb.AppendLine("}");
    }

    private static void GenerateSourceLifecycleMethod(
        StringBuilder sb,
        Model model,
        SourceInfo source,
        IMethodSymbol? method,
        string methodName,
        string sourceVariable,
        string sourceIdVariable)
    {
        if (methodName == "UnregisterAsync")
            sb.AppendLine("    public async System.Threading.Tasks.Task UnregisterAsync(string sourceId, System.Threading.CancellationToken ct = default)");
        else
            sb.AppendLine($"    public async System.Threading.Tasks.Task {methodName}(HPD.Graph.Connectors.Abstractions.Sources.WorkflowSource source, System.Threading.CancellationToken ct = default)");

        sb.AppendLine("    {");
        if (method is null)
        {
            sb.AppendLine("        await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);");
        }
        else
        {
            if (source.ConfigType is not null && method.Parameters.Any(p => SymbolEqualityByName(p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), source.ConfigType)))
            {
                if (methodName == "UnregisterAsync")
                {
                    sb.AppendLine($"        var config = new {source.ConfigType}();");
                }
                else
                {
                    sb.AppendLine($"        var config = source.Config is null ? new {source.ConfigType}() : HPDGraphConnectorJsonContext.Deserialize<{source.ConfigType}>(source.Config.Value.GetRawText()) ?? new {source.ConfigType}();");
                }
            }

            var returnType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var args = BuildMethodArguments(
                method,
                source.ConfigType,
                "config",
                null,
                "ct",
                serviceProviderVariable: "_services",
                connectionIdExpression: FindConnectionIdExpression(model, source.ConfigType, "config"),
                workflowSourceVariable: sourceVariable == "null" ? null : sourceVariable,
                sourceIdVariable: sourceIdVariable == "string.Empty" ? null : sourceIdVariable);
            var invocation = $"{source.FullyQualifiedName}.{method.Name}({args})";
            if (IsAwaitable(method.ReturnType))
                sb.AppendLine($"        await {invocation}.ConfigureAwait(false);");
            else if (returnType != "void")
            {
                sb.AppendLine($"        _ = {invocation};");
                sb.AppendLine("        await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);");
            }
            else
            {
                sb.AppendLine($"        {invocation};");
                sb.AppendLine("        await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);");
            }
        }
        sb.AppendLine("    }");
    }

    private static void GenerateOptionWrapper(StringBuilder sb, OptionInfo option)
    {
        var name = Safe(option.Name) + "OptionProvider";
        var returnType = option.Method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        sb.AppendLine($"internal sealed class {name} : HPD.Graph.Connectors.Abstractions.Options.IConnectorOptionProvider");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly System.IServiceProvider _services;");
        sb.AppendLine($"    public {name}(System.IServiceProvider services) => _services = services;");
        sb.AppendLine($"    public string OptionProviderName => \"{Esc(option.Name)}\";");
        sb.AppendLine("    public async System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<HPD.Graph.Connectors.Abstractions.Options.ConnectorOption>> GetOptionsAsync(HPD.Graph.Connectors.Abstractions.Options.ConnectorOptionRequest request, System.Threading.CancellationToken ct = default)");
        sb.AppendLine("    {");
        var typedRequestType = FindTypedOptionRequestType(option.Method);
        if (typedRequestType is not null)
            sb.AppendLine($"        var typedRequest = AdaptOptionRequest<{typedRequestType}>(request);");
        var args = BuildMethodArguments(
            option.Method,
            null,
            null,
            null,
            "ct",
            "request",
            serviceProviderVariable: "_services",
            connectionIdExpression: "request.ConnectionId ?? throw new System.InvalidOperationException(\"Connector option request requires ConnectionId.\")",
            typedOptionRequestType: typedRequestType,
            typedOptionRequestVariable: typedRequestType is null ? null : "typedRequest");
        sb.AppendLine($"        var result = await {option.FullyQualifiedName}.{option.Method.Name}({args}).ConfigureAwait(false);");
        if (returnType.Contains("ConnectorOptionPage"))
            sb.AppendLine("        return result.Options;");
        else
            sb.AppendLine("        return result;");
        sb.AppendLine("    }");
        sb.AppendLine("    private static T AdaptOptionRequest<T>(HPD.Graph.Connectors.Abstractions.Options.ConnectorOptionRequest request)");
        sb.AppendLine("    {");
        sb.AppendLine("        var json = new System.Text.Json.Nodes.JsonObject();");
        sb.AppendLine("        if (request.CurrentConfig is { } currentConfig && currentConfig.ValueKind == System.Text.Json.JsonValueKind.Object)");
        sb.AppendLine("        {");
        sb.AppendLine("            var current = System.Text.Json.Nodes.JsonNode.Parse(currentConfig.GetRawText())?.AsObject();");
        sb.AppendLine("            if (current is not null)");
        sb.AppendLine("            {");
        sb.AppendLine("                foreach (var property in current)");
        sb.AppendLine("                {");
        sb.AppendLine("                    json[property.Key] = property.Value?.DeepClone();");
        sb.AppendLine("                    var pascalKey = ToPascalPropertyName(property.Key);");
        sb.AppendLine("                    if (!json.ContainsKey(pascalKey)) json[pascalKey] = property.Value?.DeepClone();");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("        if (request.ConnectionId is not null) { json[\"connectionId\"] = request.ConnectionId; json[\"ConnectionId\"] = request.ConnectionId; }");
        sb.AppendLine("        if (request.Search is not null) { json[\"search\"] = request.Search; json[\"Search\"] = request.Search; }");
        sb.AppendLine("        if (request.Cursor is not null) { json[\"cursor\"] = request.Cursor; json[\"Cursor\"] = request.Cursor; }");
        sb.AppendLine("        if (request.Limit is not null) { json[\"limit\"] = request.Limit.Value; json[\"Limit\"] = request.Limit.Value; }");
        sb.AppendLine("        return HPDGraphConnectorJsonContext.Deserialize<T>(json.ToJsonString()) ?? throw new System.InvalidOperationException($\"Connector option request '{typeof(T).FullName}' could not be deserialized.\");");
        sb.AppendLine("    }");
        sb.AppendLine("    private static string ToPascalPropertyName(string name)");
        sb.AppendLine("    {");
        sb.AppendLine("        return string.IsNullOrEmpty(name) || char.IsUpper(name[0]) ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);");
        sb.AppendLine("    }");
        AppendResolveConnectionHelper(sb);
        sb.AppendLine("}");
    }

    private static void GenerateAssetCatalogWrapper(StringBuilder sb, Model model, AssetCatalogInfo asset)
    {
        var name = Safe(asset.Name) + "AssetCatalogProvider";
        sb.AppendLine($"internal sealed class {name} : HPD.Graph.Connectors.Abstractions.Assets.IConnectorAssetCatalogProvider");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly System.IServiceProvider _services;");
        sb.AppendLine($"    public {name}(System.IServiceProvider services) => _services = services;");
        sb.AppendLine($"    public string CatalogProviderName => \"{Esc(asset.Name)}\";");
        sb.AppendLine("    public async System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<HPD.Graph.Connectors.Abstractions.Assets.ConnectorAssetDescriptor>> LoadAssetsAsync(HPD.Graph.Connectors.Abstractions.Assets.ConnectorAssetCatalogRequest request, System.Threading.CancellationToken ct = default)");
        sb.AppendLine("    {");
        if (asset.LoadMethod is null)
            sb.AppendLine("        return System.Array.Empty<HPD.Graph.Connectors.Abstractions.Assets.ConnectorAssetDescriptor>();");
        else
        {
            if (asset.ConfigType is not null)
                sb.AppendLine($"        var config = request.Config is null ? new {asset.ConfigType}() : HPDGraphConnectorJsonContext.Deserialize<{asset.ConfigType}>(request.Config.Value.GetRawText()) ?? new {asset.ConfigType}();");
            var args = BuildMethodArguments(asset.LoadMethod, asset.ConfigType, "config", null, "ct", "request", serviceProviderVariable: "_services", connectionIdExpression: FindConnectionIdExpression(model, asset.ConfigType, "config"));
            sb.AppendLine($"        return await {asset.FullyQualifiedName}.{asset.LoadMethod.Name}({args}).ConfigureAwait(false);");
        }
        sb.AppendLine("    }");
        AppendResolveConnectionHelper(sb);
        sb.AppendLine("}");
    }

    private static void GenerateMaterializationWrapper(StringBuilder sb, Model model, MaterializationInfo materialization)
    {
        var name = Safe(materialization.Type) + "MaterializationProvider";
        sb.AppendLine($"internal sealed class {name} : HPD.Graph.Connectors.Abstractions.Materialization.IConnectorMaterializationProvider");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly System.IServiceProvider _services;");
        sb.AppendLine($"    public {name}(System.IServiceProvider services) => _services = services;");
        sb.AppendLine($"    public string MaterializationType => \"{Esc(materialization.Type)}\";");
        sb.AppendLine("    public async System.Collections.Generic.IAsyncEnumerable<HPD.Events.Event> MaterializeAsync(HPD.Graph.Connectors.Abstractions.Materialization.ConnectorMaterializationContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct = default)");
        sb.AppendLine("    {");
        if (materialization.RunMethod is not null)
        {
            if (materialization.ConfigType is not null)
                sb.AppendLine($"        var config = context.Config is null ? new {materialization.ConfigType}() : HPDGraphConnectorJsonContext.Deserialize<{materialization.ConfigType}>(context.Config.Value.GetRawText()) ?? new {materialization.ConfigType}();");
            var args = BuildMethodArguments(materialization.RunMethod, materialization.ConfigType, "config", null, "ct", serviceProviderVariable: "_services", connectionIdExpression: FindConnectionIdExpression(model, materialization.ConfigType, "config"), materializationContext: "context");
            sb.AppendLine($"        await foreach (var evt in {materialization.FullyQualifiedName}.{materialization.RunMethod.Name}({args}).WithCancellation(ct).ConfigureAwait(false)) yield return evt;");
        }
        sb.AppendLine("    }");
        AppendResolveConnectionHelper(sb);
        sb.AppendLine("}");
    }

    private static void GenerateCheckWrapper(StringBuilder sb, CheckInfo check)
    {
        var name = Safe(check.Name) + "AssetCheckProvider";
        sb.AppendLine($"internal sealed class {name} : HPD.Graph.Connectors.Abstractions.Materialization.IConnectorAssetCheckProvider");
        sb.AppendLine("{");
        sb.AppendLine($"    public string CheckName => \"{Esc(check.Name)}\";");
        sb.AppendLine("    public async System.Collections.Generic.IAsyncEnumerable<HPD.Events.Event> CheckAsync(HPD.Graph.Connectors.Abstractions.Materialization.ConnectorMaterializationContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct = default)");
        sb.AppendLine("    {");
        if (check.RunMethod is not null)
        {
            var args = BuildMethodArguments(check.RunMethod, null, null, null, "ct", materializationContext: "context");
            sb.AppendLine($"        var evt = await {check.FullyQualifiedName}.{check.RunMethod.Name}({args}).ConfigureAwait(false);");
            sb.AppendLine("        yield return evt;");
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    private static string GenerateConnectorPartial(ConnectorInfo connector, IReadOnlyList<SourceInfo> sources)
    {
        var sb = Header();
        sb.AppendLine($"namespace {connector.Namespace};");
        sb.AppendLine($"{connector.Accessibility} partial class {connector.ClassName}");
        sb.AppendLine("{");
        sb.AppendLine("    public async System.Threading.Tasks.Task<Microsoft.AspNetCore.Http.IResult> HandleWebhookAsync(Microsoft.AspNetCore.Http.HttpContext ctx)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var memory = new System.IO.MemoryStream();");
        sb.AppendLine("        await ctx.Request.Body.CopyToAsync(memory, ctx.RequestAborted).ConfigureAwait(false);");
        sb.AppendLine("        var bodyBytes = memory.ToArray();");
        sb.AppendLine("        System.Text.Json.JsonElement? body = null;");
        sb.AppendLine("        if (bodyBytes.Length > 0) { using var document = System.Text.Json.JsonDocument.Parse(bodyBytes); body = document.RootElement.Clone(); }");
        sb.AppendLine("        var headers = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);");
        sb.AppendLine("        foreach (var header in ctx.Request.Headers) headers[header.Key] = header.Value.ToString();");
        sb.AppendLine("        return await HandleWebhookAsync(new HPD.Graph.Connectors.Abstractions.Sources.WebhookEnvelope");
        sb.AppendLine("        {");
        sb.AppendLine("            Method = ctx.Request.Method,");
        sb.AppendLine("            Path = ctx.Request.Path.Value ?? string.Empty,");
        sb.AppendLine("            Headers = headers,");
        sb.AppendLine("            Body = body,");
        sb.AppendLine("            BodyBytes = bodyBytes,");
        sb.AppendLine("            QueryString = ctx.Request.QueryString.Value");
        sb.AppendLine("        }, ctx.RequestServices, ctx.RequestAborted).ConfigureAwait(false);");
        sb.AppendLine("    }");
        sb.AppendLine("    public async System.Threading.Tasks.Task<Microsoft.AspNetCore.Http.IResult> HandleWebhookAsync(HPD.Graph.Connectors.Abstractions.Sources.WebhookEnvelope envelope, System.IServiceProvider services, System.Threading.CancellationToken ct = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        var bodyBytes = envelope.BodyBytes ?? System.Array.Empty<byte>();");
        for (var hookIndex = 0; hookIndex < connector.PreDispatchMethods.Count; hookIndex++)
        {
            var hook = connector.PreDispatchMethods[hookIndex];
            var resultVariable = "preDispatchResult" + hookIndex;
            var args = BuildConnectorHookArguments(hook, "default!", "bodyBytes", "ct", "envelope", "services");
            var invocation = $"{(hook.IsStatic ? connector.FullyQualifiedName : "this")}.{hook.Name}({args})";
            if (IsAwaitableWithResult(hook.ReturnType))
            {
                sb.AppendLine($"        var {resultVariable} = await {invocation}.ConfigureAwait(false);");
                sb.AppendLine($"        if ({resultVariable} is not null) return {resultVariable};");
            }
            else if (IsAwaitable(hook.ReturnType))
            {
                sb.AppendLine($"        await {invocation}.ConfigureAwait(false);");
            }
            else if (IsIResult(hook.ReturnType))
            {
                sb.AppendLine($"        var {resultVariable} = {invocation};");
                sb.AppendLine($"        if ({resultVariable} is not null) return {resultVariable};");
            }
            else
            {
                sb.AppendLine($"        {invocation};");
            }
        }
        if (connector.BodyExtractorMethod is not null)
        {
            var args = BuildConnectorHookArguments(connector.BodyExtractorMethod, "default!", "bodyBytes", "ct", "envelope", "services");
            var invocation = $"{(connector.BodyExtractorMethod.IsStatic ? connector.FullyQualifiedName : "this")}.{connector.BodyExtractorMethod.Name}({args})";
            if (IsAwaitable(connector.BodyExtractorMethod.ReturnType))
                sb.AppendLine($"        var extracted = await {invocation}.ConfigureAwait(false);");
            else
                sb.AppendLine($"        var extracted = {invocation};");
            sb.AppendLine("        var extractedEventType = extracted.Item1;");
            sb.AppendLine("        bodyBytes = extracted.Item2 ?? bodyBytes;");
        }
        else
        {
            sb.AppendLine("        string? extractedEventType = null;");
        }
        sb.AppendLine("        var eventType = extractedEventType ?? envelope.EventType ?? (envelope.Headers.TryGetValue(\"x-hpd-event-type\", out var hpdEvent) ? hpdEvent : null);");
        sb.AppendLine("        if (eventType is null && envelope.Headers.TryGetValue(\"x-github-event\", out var githubEvent)) eventType = githubEvent;");
        sb.AppendLine("        var body = envelope.Body;");
        sb.AppendLine("        if (body is null && bodyBytes.Length > 0) { using var document = System.Text.Json.JsonDocument.Parse(bodyBytes); body = document.RootElement.Clone(); }");
        sb.AppendLine("        if (eventType is null && body is { ValueKind: System.Text.Json.JsonValueKind.Object } bodyObject && bodyObject.TryGetProperty(\"type\", out var typeProperty)) eventType = typeProperty.GetString();");
        sb.AppendLine("        var dispatchEnvelope = envelope with");
        sb.AppendLine("        {");
        sb.AppendLine("            Body = body,");
        sb.AppendLine("            BodyBytes = bodyBytes,");
        sb.AppendLine("            EventType = eventType");
        sb.AppendLine("        };");
        sb.AppendLine("        var sourceTypes = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal)");
        sb.AppendLine("        {");
        foreach (var source in sources.Where(s => string.Equals(FirstSegment(s.SourceType), connector.Id, StringComparison.Ordinal)))
            sb.AppendLine($"            \"{Esc(source.SourceType)}\",");
        sb.AppendLine("        };");
        sb.AppendLine("        var store = services.GetRequiredService<HPD.Graph.Connectors.Abstractions.Sources.IWorkflowSourceStore>();");
        sb.AppendLine("        var providers = services.GetServices<HPD.Graph.Connectors.Abstractions.Sources.IWebhookWorkflowSourceProvider>();");
        sb.AppendLine("        foreach (var source in await store.ListAsync(ct).ConfigureAwait(false))");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!source.Enabled || !sourceTypes.Contains(source.SourceType)) continue;");
        sb.AppendLine("            var provider = providers.FirstOrDefault(p => string.Equals(p.SourceType, source.SourceType, System.StringComparison.Ordinal));");
        sb.AppendLine("            if (provider is not null) await provider.ReceiveAsync(source, dispatchEnvelope, ct).ConfigureAwait(false);");
        sb.AppendLine("        }");
        sb.AppendLine("        return Microsoft.AspNetCore.Http.Results.Accepted();");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateBuilderExtensions(Model model)
    {
        var connector = model.Connectors[0];
        var sb = Header();
        sb.AppendLine($"namespace {connector.Namespace};");
        sb.AppendLine("public static partial class ConnectorGraphBuilderExtensions");
        sb.AppendLine("{");
        foreach (var action in model.Actions.Where(a => a.ConfigType is not null))
        {
            var method = "Add" + ToPascal(action.ActionType) + "Node";
            sb.AppendLine($"    public static HPD.Graph.Core.Builders.GraphBuilder {method}(this HPD.Graph.Core.Builders.GraphBuilder builder, string id, string name, {action.ConfigType} config)");
            sb.AppendLine("    {");
            sb.AppendLine($"        return builder.AddHandlerNode(id, name, \"{Esc(action.HandlerName)}\", node => node.WithConfig(HPD.Graph.Connectors.Generated.HPDGraphConnectorJsonContext.SerializeToElement(config)));");
            sb.AppendLine("    }");
        }
        foreach (var source in model.Sources.Where(s => s.ConfigType is not null))
        {
            var method = "Add" + ToPascal(source.SourceType) + "Source";
            sb.AppendLine($"    public static HPD.Graph.Core.Builders.GraphBuilder {method}(this HPD.Graph.Core.Builders.GraphBuilder builder, string id, string name, {source.ConfigType} config)");
            sb.AppendLine("    {");
            sb.AppendLine($"        return builder.AddHandlerNode(id, name, \"{Esc(source.SourceType)}\", node => node.WithConfig(HPD.Graph.Connectors.Generated.HPDGraphConnectorJsonContext.SerializeToElement(config)));");
            sb.AppendLine("    }");
        }
        foreach (var operation in model.OpenApiOperations)
        {
            var method = "Add" + ToPascal(operation.ConnectorId + "." + operation.OperationId) + "Node";
            sb.AppendLine($"    public static HPD.Graph.Core.Builders.GraphBuilder {method}(this HPD.Graph.Core.Builders.GraphBuilder builder, string id, string name, string connectionId, System.Text.Json.JsonElement? arguments = null, System.Uri? serverUrlOverride = null)");
            sb.AppendLine("    {");
            sb.AppendLine("        return builder.AddHandlerNode(id, name, \"" + Esc(operation.ConnectorId) + "." + Esc(operation.OperationId) + "\", node => node.WithConfig(System.Text.Json.JsonSerializer.SerializeToElement(");
            sb.AppendLine("            new HPD.Graph.Connectors.OpenApi.Handlers.OpenApiCallOperationConfig");
            sb.AppendLine("            {");
            sb.AppendLine($"                ConnectorId = \"{Esc(operation.ConnectorId)}\",");
            sb.AppendLine("                ConnectionId = connectionId,");
            sb.AppendLine($"                OperationId = \"{Esc(operation.OperationId)}\",");
            sb.AppendLine("                Arguments = arguments,");
            sb.AppendLine("                ServerUrlOverride = serverUrlOverride");
            sb.AppendLine("            },");
            sb.AppendLine("            HPD.Graph.Connectors.OpenApi.OpenApiConnectorJsonSerializerContext.Default.OpenApiCallOperationConfig)));");
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildMethodArguments(
        IMethodSymbol method,
        string? configType,
        string? configVariable,
        string? graphContextVariable,
        string cancellationTokenVariable,
        string? requestVariable = null,
        string? envelopeVariable = null,
        string? serviceProviderVariable = null,
        string? connectionIdExpression = null,
        string? materializationContext = null,
        string? workflowSourceVariable = null,
        string? sourceIdVariable = null,
        string? typedOptionRequestType = null,
        string? typedOptionRequestVariable = null)
    {
        var args = new List<string>();
        foreach (var p in method.Parameters)
        {
            var type = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (configType is not null && SymbolEqualityByName(type, configType))
                args.Add(configVariable!);
            else if (type == "global::System.Threading.CancellationToken")
                args.Add(cancellationTokenVariable);
            else if (type == "global::HPD.Graph.Connectors.Abstractions.Options.ConnectorOptionRequest" && requestVariable is not null)
                args.Add(requestVariable);
            else if (typedOptionRequestType is not null && typedOptionRequestVariable is not null && SymbolEqualityByName(type, typedOptionRequestType))
                args.Add(typedOptionRequestVariable);
            else if (type == "global::HPD.Graph.Connectors.Abstractions.Assets.ConnectorAssetCatalogRequest" && requestVariable is not null)
                args.Add(requestVariable);
            else if (type == "global::HPD.Graph.Connectors.Abstractions.Sources.WebhookEnvelope" && envelopeVariable is not null)
                args.Add(envelopeVariable);
            else if (type == "global::HPD.Graph.Connectors.Abstractions.Materialization.ConnectorMaterializationContext" && materializationContext is not null)
                args.Add(materializationContext);
            else if (type == "global::HPD.Graph.Connectors.Abstractions.Sources.WorkflowSource" && workflowSourceVariable is not null)
                args.Add(workflowSourceVariable);
            else if ((type == "string" || type == "global::System.String") && sourceIdVariable is not null && string.Equals(p.Name, "sourceId", StringComparison.OrdinalIgnoreCase))
                args.Add(sourceIdVariable);
            else if (type == "global::HPD.Graph.Connectors.Abstractions.Connections.IConnectionProvider" && serviceProviderVariable is not null)
                args.Add($"{serviceProviderVariable}.GetRequiredService<HPD.Graph.Connectors.Abstractions.Connections.IConnectionProvider>()");
            else if (type == "global::HPD.Graph.Connectors.Abstractions.Connections.IConnectionProvider" && materializationContext is not null)
                args.Add(materializationContext + ".Connections");
            else if (type == "global::HPD.Graph.Connectors.Abstractions.Connections.ResolvedConnection" && connectionIdExpression is not null && serviceProviderVariable is not null)
                args.Add($"await ResolveConnectionAsync({connectionIdExpression}, {cancellationTokenVariable}).ConfigureAwait(false)");
            else if (type == "global::HPD.Graph.Connectors.Abstractions.Connections.ResolvedConnection" && connectionIdExpression is not null && materializationContext is not null)
                args.Add($"await ResolveConnectionAsync({materializationContext}.Connections, {connectionIdExpression}, {cancellationTokenVariable}).ConfigureAwait(false)");
            else if (type == "global::HPD.Graph.Abstractions.Artifacts.ArtifactKey" && materializationContext is not null)
                args.Add(materializationContext + ".ArtifactKey");
            else if (graphContextVariable is not null && type == "global::HPD.Graph.Core.Context.GraphContext")
                args.Add(graphContextVariable);
            else if (serviceProviderVariable is not null && type == "global::System.IServiceProvider")
                args.Add(serviceProviderVariable);
            else if (serviceProviderVariable is not null && connectionIdExpression is not null)
                args.Add($"await {serviceProviderVariable}.GetRequiredService<HPD.Graph.Connectors.Abstractions.Connections.IConnectorClientFactory<{type}>>().CreateAsync(await ResolveConnectionAsync({connectionIdExpression}, {cancellationTokenVariable}).ConfigureAwait(false), {cancellationTokenVariable}).ConfigureAwait(false)");
            else if (serviceProviderVariable is not null)
                args.Add($"{serviceProviderVariable}.GetRequiredService<{type}>()");
            else
                args.Add("default!");
        }
        return string.Join(", ", args);
    }

    private static string? FindTypedOptionRequestType(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            var type = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (type == "global::System.Threading.CancellationToken" ||
                type == "global::HPD.Graph.Connectors.Abstractions.Options.ConnectorOptionRequest" ||
                type == "global::HPD.Graph.Connectors.Abstractions.Connections.IConnectionProvider" ||
                type == "global::System.IServiceProvider")
            {
                continue;
            }

            if (parameter.Name.EndsWith("request", StringComparison.OrdinalIgnoreCase) ||
                parameter.Type.Name.EndsWith("Request", StringComparison.Ordinal))
            {
                return type;
            }
        }

        return null;
    }

    private static string BuildConnectorHookArguments(
        IMethodSymbol method,
        string httpContextVariable,
        string bodyBytesVariable,
        string cancellationTokenVariable,
        string? envelopeVariable = null,
        string? serviceProviderVariable = null)
    {
        var args = new List<string>();
        foreach (var parameter in method.Parameters)
        {
            var type = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (type == "global::Microsoft.AspNetCore.Http.HttpContext")
                args.Add(httpContextVariable);
            else if (type == "global::HPD.Graph.Connectors.Abstractions.Sources.WebhookEnvelope" && envelopeVariable is not null)
                args.Add(envelopeVariable);
            else if (type == "global::System.IServiceProvider" && serviceProviderVariable is not null)
                args.Add(serviceProviderVariable);
            else if (type == "byte[]" || type == "global::System.Byte[]")
                args.Add(bodyBytesVariable);
            else if (type == "global::System.Threading.CancellationToken")
                args.Add(cancellationTokenVariable);
            else
                args.Add("default!");
        }

        return string.Join(", ", args);
    }

    private static bool IsAwaitable(ITypeSymbol type)
    {
        var display = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return display == "global::System.Threading.Tasks.Task" ||
            display.StartsWith("global::System.Threading.Tasks.Task<", StringComparison.Ordinal) ||
            display == "global::System.Threading.Tasks.ValueTask" ||
            display.StartsWith("global::System.Threading.Tasks.ValueTask<", StringComparison.Ordinal);
    }

    private static bool IsAwaitableWithResult(ITypeSymbol type)
    {
        var display = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return display.StartsWith("global::System.Threading.Tasks.Task<", StringComparison.Ordinal) ||
            display.StartsWith("global::System.Threading.Tasks.ValueTask<", StringComparison.Ordinal);
    }

    private static bool IsIResult(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Microsoft.AspNetCore.Http.IResult";

    private static string? FindConnectionIdExpression(Model model, string? configType, string configVariable)
    {
        if (configType is null) return null;
        var fields = model.Configs.FirstOrDefault(c => SymbolEqualityByName(c.FullyQualifiedName, configType))?.Fields
            ?? model.Sources.FirstOrDefault(s => s.ConfigType is not null && SymbolEqualityByName(s.ConfigType, configType))?.ConfigFields
            ?? model.AssetCatalogs.FirstOrDefault(a => a.ConfigType is not null && SymbolEqualityByName(a.ConfigType, configType))?.ConfigFields
            ?? model.Materializations.FirstOrDefault(m => m.ConfigType is not null && SymbolEqualityByName(m.ConfigType, configType))?.ConfigFields;
        var field = fields?.FirstOrDefault(f => f.ConnectionType is not null);
        return field is null ? null : $"{configVariable}.{field.Name}";
    }

    private static void AppendResolveConnectionHelper(StringBuilder sb)
    {
        sb.AppendLine("    private async System.Threading.Tasks.ValueTask<HPD.Graph.Connectors.Abstractions.Connections.ResolvedConnection> ResolveConnectionAsync(string connectionId, System.Threading.CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        var provider = _services.GetRequiredService<HPD.Graph.Connectors.Abstractions.Connections.IConnectionProvider>();");
        sb.AppendLine("        return await ResolveConnectionAsync(provider, connectionId, ct).ConfigureAwait(false);");
        sb.AppendLine("    }");
        sb.AppendLine("    private static async System.Threading.Tasks.ValueTask<HPD.Graph.Connectors.Abstractions.Connections.ResolvedConnection> ResolveConnectionAsync(HPD.Graph.Connectors.Abstractions.Connections.IConnectionProvider provider, string connectionId, System.Threading.CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        return await provider.ResolveAsync(connectionId, ct).ConfigureAwait(false) ?? throw new System.InvalidOperationException($\"Connector connection '{connectionId}' could not be resolved.\");");
        sb.AppendLine("    }");
    }

    private static void AppendFields(StringBuilder sb, Model model, string? configType, int spaces)
    {
        if (configType is null) return;
        var config = model.Configs.FirstOrDefault(c => SymbolEqualityByName(c.FullyQualifiedName, configType));
        if (config is null || config.Fields.Count == 0) return;

        var indent = new string(' ', spaces);
        sb.AppendLine($"{indent}Fields =");
        sb.AppendLine($"{indent}[");
        foreach (var field in config.Fields)
        {
            sb.AppendLine($"{indent}    new HPD.Graph.Connectors.Abstractions.Configuration.ConnectorFieldDescriptor");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        Name = \"{Esc(field.Name)}\",");
            sb.AppendLine($"{indent}        TypeName = \"{Esc(field.TypeName)}\",");
            sb.AppendLine($"{indent}        Required = {(field.Required ? "true" : "false")},");
            AppendOptionalString(sb, "ConnectionType", field.ConnectionType, spaces + 8);
            AppendOptionalString(sb, "OptionProviderName", field.OptionProviderName, spaces + 8);
            sb.AppendLine($"{indent}    }},");
        }
        sb.AppendLine($"{indent}],");
    }

    private static void AppendFieldsForConfigDescriptor(StringBuilder sb, ConfigInfo config, int spaces)
    {
        if (config.Fields.Count == 0) return;

        var indent = new string(' ', spaces);
        sb.AppendLine($"{indent}Fields =");
        sb.AppendLine($"{indent}[");
        foreach (var field in config.Fields)
        {
            sb.AppendLine($"{indent}    new HPD.Graph.Connectors.Abstractions.Configuration.ConnectorFieldDescriptor");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        Name = \"{Esc(field.Name)}\",");
            sb.AppendLine($"{indent}        TypeName = \"{Esc(field.TypeName)}\",");
            sb.AppendLine($"{indent}        Required = {(field.Required ? "true" : "false")},");
            AppendOptionalString(sb, "ConnectionType", field.ConnectionType, spaces + 8);
            AppendOptionalString(sb, "OptionProviderName", field.OptionProviderName, spaces + 8);
            sb.AppendLine($"{indent}    }},");
        }
        sb.AppendLine($"{indent}],");
    }

    private static void AppendOpenApiFields(StringBuilder sb, OpenApiOperationInfo operation, int spaces)
    {
        var fields = operation.Parameters.Concat(operation.PayloadProperties).ToArray();
        if (fields.Length == 0) return;

        var indent = new string(' ', spaces);
        sb.AppendLine($"{indent}Fields =");
        sb.AppendLine($"{indent}[");
        foreach (var field in fields)
        {
            sb.AppendLine($"{indent}    new HPD.Graph.Connectors.Abstractions.Configuration.ConnectorFieldDescriptor");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        Name = \"{Esc(field.Name)}\",");
            sb.AppendLine($"{indent}        TypeName = \"{Esc(OpenApiTypeName(field.TypeName))}\",");
            sb.AppendLine($"{indent}        Required = {(field.Required ? "true" : "false")},");
            sb.AppendLine($"{indent}    }},");
        }
        sb.AppendLine($"{indent}],");
    }

    private static void AppendOpenApiInputs(StringBuilder sb, OpenApiOperationInfo operation, int spaces)
    {
        var indent = new string(' ', spaces);
        sb.AppendLine($"{indent}Inputs =");
        sb.AppendLine($"{indent}[");
        sb.AppendLine($"{indent}    new HPD.Graph.Abstractions.Discovery.SocketDescriptor {{ Name = \"arguments\", TypeName = typeof(object).FullName!, Direction = HPD.Graph.Abstractions.Discovery.SocketDirection.Input, Required = false }},");
        foreach (var field in operation.Parameters.Concat(operation.PayloadProperties))
        {
            sb.AppendLine($"{indent}    new HPD.Graph.Abstractions.Discovery.SocketDescriptor");
            sb.AppendLine($"{indent}    {{");
            sb.AppendLine($"{indent}        Name = \"{Esc(field.Name)}\",");
            sb.AppendLine($"{indent}        TypeName = \"{Esc(OpenApiTypeName(field.TypeName))}\",");
            sb.AppendLine($"{indent}        Direction = HPD.Graph.Abstractions.Discovery.SocketDirection.Input,");
            sb.AppendLine($"{indent}        Required = {(field.Required ? "true" : "false")},");
            AppendOptionalString(sb, "Description", field.Description, spaces + 8);
            sb.AppendLine($"{indent}    }},");
        }
        sb.AppendLine($"{indent}],");
    }

    private static string InferOpenApiTraits(string method)
        => method switch
        {
            "GET" or "HEAD" => "ReadOnly | HPD.Graph.Connectors.Abstractions.Actions.ConnectorOperationTraits.Idempotent",
            "DELETE" => "Destructive",
            "PUT" => "Idempotent",
            _ => "OpenWorld"
        };

    private static string OpenApiTypeName(string? schemaType)
        => schemaType switch
        {
            "integer" => typeof(long).FullName!,
            "number" => typeof(double).FullName!,
            "boolean" => typeof(bool).FullName!,
            "array" => typeof(object[]).FullName!,
            "object" => typeof(object).FullName!,
            _ => typeof(string).FullName!
        };

    private static IReadOnlyList<FieldInfo> ResolveFields(INamedTypeSymbol symbol)
    {
        return symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic)
            .Select(p => new FieldInfo(
                p.Name,
                p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                p.GetAttribute("ConnectorConnectionAttribute") is { } conn ? GetCtorString(conn, 0) : null,
                p.GetAttribute("ConnectorOptionAttribute") is { } opt ? GetCtorString(opt, 0) : null,
                p.NullableAnnotation != NullableAnnotation.Annotated))
            .ToArray();
    }

    private static INamedTypeSymbol? GetSymbol(Compilation compilation, TypeDeclarationSyntax node)
    {
        return compilation.GetSemanticModel(node.SyntaxTree).GetDeclaredSymbol(node) as INamedTypeSymbol;
    }

    private static string? GetCtorString(AttributeData attr, int index)
    {
        return attr.ConstructorArguments.Length > index ? attr.ConstructorArguments[index].Value as string : null;
    }

    private static string? GetNamedString(AttributeData attr, string name)
    {
        return attr.NamedArguments.FirstOrDefault(kv => kv.Key == name).Value.Value as string;
    }

    private static IReadOnlyList<string> GetNamedStringArray(AttributeData attr, string name)
    {
        var value = attr.NamedArguments.FirstOrDefault(kv => kv.Key == name).Value;
        return value.Values.IsDefaultOrEmpty
            ? Array.Empty<string>()
            : value.Values.Select(v => v.Value as string).Where(s => s is not null).Select(s => s!).ToArray();
    }

    private static string? GetNamedEnum(AttributeData attr, string name)
    {
        var value = attr.NamedArguments.FirstOrDefault(kv => kv.Key == name).Value;
        if (value.Value is null) return null;
        if (value.Type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType) return null;
        return enumType
            .GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, value.Value))
            ?.Name;
    }

    private static INamedTypeSymbol? GetNamedType(AttributeData attr, string name)
    {
        return attr.NamedArguments.FirstOrDefault(kv => kv.Key == name).Value.Value as INamedTypeSymbol;
    }

    private static void ReportDuplicates(SourceProductionContext context, string kind, IEnumerable<(string Id, string TypeName, Location? Location)> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!seen.Add(item.Id))
                context.ReportDiagnostic(Diagnostic.Create(ConnectorDiagnostics.DuplicateId, item.Location, kind, item.Id, item.TypeName));
        }
    }

    private static bool IsPartial(TypeDeclarationSyntax node) =>
        node.Modifiers.Any(m => m.Text == "partial");

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool GetBool(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.True;

    private static string FirstSegment(string value)
    {
        var index = value.IndexOf('.');
        return index <= 0 ? value : value.Substring(0, index);
    }

    private static string ToDisplayName(string value) =>
        ToPascal(value).Replace("_", " ");

    private static string ToPascal(string value)
    {
        var parts = value.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }

    private static string Safe(string value) => ToPascal(value);

    private static string ToAccessibility(Accessibility accessibility) =>
        accessibility switch
        {
            Microsoft.CodeAnalysis.Accessibility.Public => "public",
            Microsoft.CodeAnalysis.Accessibility.Internal => "internal",
            _ => "internal"
        };

    private static string Esc(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void AppendOptionalString(StringBuilder sb, string property, string? value, int spaces)
    {
        if (value is not null) sb.AppendLine($"{new string(' ', spaces)}{property} = \"{Esc(value)}\",");
    }

    private static bool SymbolEqualityByName(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal) ||
        string.Equals(left, "global::" + right, StringComparison.Ordinal) ||
        string.Equals("global::" + left, right, StringComparison.Ordinal);

    private static StringBuilder Header()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.AspNetCore.Builder;");
        sb.AppendLine("using Microsoft.AspNetCore.Routing;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
        return sb;
    }
}

internal static class ConnectorGeneratorEnumerableExtensions
{
    public static AttributeData? GetAttribute(this ISymbol symbol, string name)
    {
        return symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == name);
    }

    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> values) where T : class =>
        values.Where(v => v is not null).Select(v => v!);
}
