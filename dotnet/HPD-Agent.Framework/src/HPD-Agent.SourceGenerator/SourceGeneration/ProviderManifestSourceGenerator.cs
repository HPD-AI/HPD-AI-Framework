using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HPD.Agent.SourceGenerator.SourceGeneration;

/// <summary>Generates immutable provider manifest fragments from provider declarations.</summary>
[Generator]
public sealed class ProviderManifestSourceGenerator : IIncrementalGenerator
{
    private const string ProviderAttributeName = "HPD.Agent.Providers.HpdProviderAttribute";
    private const string FamilyAttributeName = "HPD.Agent.Providers.HpdProviderFamilyAttribute";
    private const string AliasAttributeName = "HPD.Agent.Providers.HpdProviderAliasAttribute";
    private const string PayloadAttributeName = "HPD.Agent.Providers.HpdProviderPayloadAttribute";
    private const string SecretAliasAttributeName = "HPD.Agent.Providers.HpdProviderSecretAliasAttribute";

    private static readonly DiagnosticDescriptor InvalidProviderKey = new(
        "HPDP002",
        "Invalid provider key",
        "Provider key '{0}' must be lowercase and URL-safe",
        "HPD.Provider",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingFamily = new(
        "HPDP003",
        "Provider family is required",
        "Provider '{0}' must declare at least one HpdProviderFamily attribute",
        "HPD.Provider",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingParameterlessConstructor = new(
        "HPDP009",
        "Provider factory cannot be generated",
        "Provider type '{0}' must have an accessible parameterless constructor",
        "HPD.Provider",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var providers = context.SyntaxProvider.ForAttributeWithMetadataName(
                ProviderAttributeName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => CreateProviderInfo(ctx))
            .Where(static info => info is not null)
            .Collect();

        context.RegisterSourceOutput(providers, static (productionContext, values) =>
        {
            foreach (var info in values)
            {
                if (info is not null)
                    EmitProvider(productionContext, info);
            }
        });
    }

    private static ProviderInfo? CreateProviderInfo(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type || context.Attributes.Length == 0)
            return null;

        var providerAttribute = context.Attributes[0];
        if (providerAttribute.ConstructorArguments.Length != 2)
            return null;

        var providerKey = providerAttribute.ConstructorArguments[0].Value as string ?? string.Empty;
        var displayName = providerAttribute.ConstructorArguments[1].Value as string ?? string.Empty;
        var documentationUrl = GetNamedString(providerAttribute, "DocumentationUrl");

        var families = ImmutableArray.CreateBuilder<FamilyInfo>();
        var aliases = ImmutableArray.CreateBuilder<string>();
        var payloads = ImmutableArray.CreateBuilder<PayloadInfo>();
        var secretAliases = ImmutableArray.CreateBuilder<SecretAliasInfo>();
        foreach (var attribute in type.GetAttributes())
        {
            var metadataName = attribute.AttributeClass?.ToDisplayString();
            if (metadataName == FamilyAttributeName && attribute.ConstructorArguments.Length == 1)
            {
                var familyValue = attribute.ConstructorArguments[0].Value;
                if (familyValue is null)
                    continue;

                var family = Convert.ToInt32(familyValue, System.Globalization.CultureInfo.InvariantCulture);
                var lifetime = GetNamedEnum(attribute, "Lifetime", defaultValue: 0);
                families.Add(new FamilyInfo(family, lifetime, GetNamedString(attribute, "DefaultModelName")));
            }
            else if (metadataName == AliasAttributeName && attribute.ConstructorArguments.Length == 1 &&
                     attribute.ConstructorArguments[0].Value is string alias)
            {
                aliases.Add(alias);
            }
            else if (metadataName == PayloadAttributeName && attribute.ConstructorArguments.Length == 4 &&
                     attribute.ConstructorArguments[2].Value is INamedTypeSymbol payloadType &&
                     attribute.ConstructorArguments[3].Value is INamedTypeSymbol contextType)
            {
                payloads.Add(new PayloadInfo(
                    Convert.ToInt32(attribute.ConstructorArguments[0].Value, System.Globalization.CultureInfo.InvariantCulture),
                    Convert.ToInt32(attribute.ConstructorArguments[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                    payloadType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }
            else if (metadataName == SecretAliasAttributeName && attribute.ConstructorArguments.Length == 2 &&
                     attribute.ConstructorArguments[0].Value is string secretKey)
            {
                secretAliases.Add(new SecretAliasInfo(
                    secretKey,
                    attribute.ConstructorArguments[1].Values.Select(static value => (string)value.Value!).ToImmutableArray()));
            }
        }

        var hasAccessibleParameterlessConstructor = type.InstanceConstructors.Any(static constructor =>
            constructor.Parameters.Length == 0 &&
            constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal);

        return new ProviderInfo(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Sanitize(type.ToDisplayString()),
            providerKey,
            displayName,
            documentationUrl,
            families.ToImmutable(),
            aliases.ToImmutable(),
            payloads.ToImmutable(),
            secretAliases.ToImmutable(),
            hasAccessibleParameterlessConstructor,
            type.Locations.FirstOrDefault());
    }

    private static void EmitProvider(SourceProductionContext context, ProviderInfo info)
    {
        var hasErrors = false;
        if (!IsValidProviderKey(info.ProviderKey))
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidProviderKey, info.Location, info.ProviderKey));
            hasErrors = true;
        }

        if (info.Families.IsDefaultOrEmpty)
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingFamily, info.Location, info.ProviderKey));
            hasErrors = true;
        }

        if (!info.HasAccessibleParameterlessConstructor)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MissingParameterlessConstructor,
                info.Location,
                info.ProviderTypeName));
            hasErrors = true;
        }

        if (hasErrors)
            return;

        var descriptorName = $"{info.SafeName}ProviderDescriptor";
        var manifestName = $"{info.SafeName}ProviderManifest";
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        var familyArguments = string.Join(", ", info.Families
            .OrderBy(static family => family.Family)
            .Select(static family => $"(global::HPD.Agent.Providers.ProviderClientFamily){family.Family}"));
        var aliasAssignment = info.Aliases.IsDefaultOrEmpty
            ? string.Empty
            : $", Aliases = new string[] {{ {string.Join(", ", info.Aliases.Select(Literal))} }}";
        source.AppendLine($"[assembly: global::HPD.Agent.Providers.HpdProviderManifestAttribute(typeof(global::HPD.Agent.Providers.Generated.{manifestName}), {Literal(info.ProviderKey)}, new global::HPD.Agent.Providers.ProviderClientFamily[] {{ {familyArguments} }}{aliasAssignment})]");
        source.AppendLine("namespace HPD.Agent.Providers.Generated;");
        source.AppendLine();
        source.AppendLine($"internal sealed class {descriptorName} : global::HPD.Agent.Providers.IProviderDescriptor");
        source.AppendLine("{");
        source.AppendLine($"    public string ProviderKey => {Literal(info.ProviderKey)};");
        source.AppendLine($"    public string DisplayName => {Literal(info.DisplayName)};");
        source.AppendLine(info.DocumentationUrl is null
            ? "    public global::System.Uri? DocumentationUri => null;"
            : $"    public global::System.Uri? DocumentationUri => new global::System.Uri({Literal(info.DocumentationUrl)});");
        source.AppendLine("    public global::System.Collections.Generic.IReadOnlyDictionary<global::HPD.Agent.Providers.ProviderClientFamily, global::HPD.Agent.Providers.ProviderFamilyDescriptor> Families { get; } =");
        source.AppendLine("        new global::System.Collections.ObjectModel.ReadOnlyDictionary<global::HPD.Agent.Providers.ProviderClientFamily, global::HPD.Agent.Providers.ProviderFamilyDescriptor>(");
        source.AppendLine("        new global::System.Collections.Generic.Dictionary<global::HPD.Agent.Providers.ProviderClientFamily, global::HPD.Agent.Providers.ProviderFamilyDescriptor>");
        source.AppendLine("        {");
        foreach (var family in info.Families.OrderBy(static family => family.Family))
        {
            source.AppendLine($"            [(global::HPD.Agent.Providers.ProviderClientFamily){family.Family}] = new global::HPD.Agent.Providers.ProviderFamilyDescriptor");
            source.AppendLine("            {");
            source.AppendLine($"                Family = (global::HPD.Agent.Providers.ProviderClientFamily){family.Family},");
            source.AppendLine($"                Lifetime = (global::HPD.Agent.Providers.ProviderFamilyLifetime){family.Lifetime},");
            if (family.DefaultModelName is not null)
                source.AppendLine($"                DefaultModelId = {Literal(family.DefaultModelName)},");
            source.AppendLine("            },");
        }
        source.AppendLine("        });");
        source.AppendLine("    public global::System.Collections.Generic.IReadOnlyList<string> Aliases { get; } =");
        source.AppendLine(info.Aliases.IsDefaultOrEmpty
            ? "        global::System.Array.Empty<string>();"
            : $"        global::System.Array.AsReadOnly(new string[] {{ {string.Join(", ", info.Aliases.Select(Literal))} }});");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("/// <summary>Provides the generated immutable provider manifest fragment.</summary>");
        source.AppendLine($"public static class {manifestName}");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>Gets the generated manifest fragment.</summary>");
        source.AppendLine("    public static global::HPD.Agent.Providers.ProviderManifestFragment Fragment { get; } = new(");
        source.AppendLine($"        new global::HPD.Agent.Providers.IProviderDescriptor[] {{ new {descriptorName}() }},");
        source.AppendLine("        new global::HPD.Agent.Providers.ProviderRuntimeFactoryRegistration[]");
        source.AppendLine("        {");
        source.AppendLine($"            new({Literal(info.ProviderKey)}, new global::HPD.Agent.Providers.ProviderClientFamily[] {{ {familyArguments} }}, static () => new {info.ProviderTypeName}()),");
        source.AppendLine("        },");
        source.AppendLine("        new global::HPD.Agent.Providers.ProviderPayloadJsonContract[]");
        source.AppendLine("        {");
        foreach (var payload in info.Payloads.OrderBy(static x => x.Family).ThenBy(static x => x.Kind))
        {
            source.AppendLine($"            new({Literal(info.ProviderKey)}, (global::HPD.Agent.Providers.ProviderClientFamily){payload.Family}, (global::HPD.Agent.Providers.ProviderPayloadKind){payload.Kind}, typeof({payload.PayloadType}), {payload.ContextType}.Default.GetTypeInfo(typeof({payload.PayloadType}))!, static value => global::System.Text.Json.JsonSerializer.Deserialize(global::System.Text.Json.JsonSerializer.SerializeToElement(value, {payload.ContextType}.Default.GetTypeInfo(typeof({payload.PayloadType}))!), {payload.ContextType}.Default.GetTypeInfo(typeof({payload.PayloadType}))!)!),");
        }
        source.AppendLine("        },");
        source.AppendLine("        new global::HPD.Agent.Providers.ProviderSecretAliasRegistration[]");
        source.AppendLine("        {");
        foreach (var secretAlias in info.SecretAliases.OrderBy(static x => x.SecretKey, StringComparer.Ordinal))
        {
            source.AppendLine($"            new({Literal(secretAlias.SecretKey)}, new string[] {{ {string.Join(", ", secretAlias.EnvironmentVariables.Select(Literal))} }}),");
        }
        source.AppendLine("        });");
        source.AppendLine("}");

        context.AddSource($"{info.SafeName}.ProviderManifest.g.cs", source.ToString());
    }

    private static string? GetNamedString(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as string;

    private static int GetNamedEnum(AttributeData attribute, string name, int defaultValue)
    {
        var value = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value;
        return value is null ? defaultValue : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsValidProviderKey(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (var character in value)
        {
            if ((character < 'a' || character > 'z') &&
                (character < '0' || character > '9') &&
                character is not '-' and not '.')
                return false;
        }

        return true;
    }

    private static string Literal(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        return builder.ToString();
    }

    private sealed class ProviderInfo
    {
        public ProviderInfo(
            string providerTypeName,
            string safeName,
            string providerKey,
            string displayName,
            string? documentationUrl,
            ImmutableArray<FamilyInfo> families,
            ImmutableArray<string> aliases,
            ImmutableArray<PayloadInfo> payloads,
            ImmutableArray<SecretAliasInfo> secretAliases,
            bool hasAccessibleParameterlessConstructor,
            Location? location)
        {
            ProviderTypeName = providerTypeName;
            SafeName = safeName;
            ProviderKey = providerKey;
            DisplayName = displayName;
            DocumentationUrl = documentationUrl;
            Families = families;
            Aliases = aliases;
            Payloads = payloads;
            SecretAliases = secretAliases;
            HasAccessibleParameterlessConstructor = hasAccessibleParameterlessConstructor;
            Location = location;
        }

        public string ProviderTypeName { get; }
        public string SafeName { get; }
        public string ProviderKey { get; }
        public string DisplayName { get; }
        public string? DocumentationUrl { get; }
        public ImmutableArray<FamilyInfo> Families { get; }
        public ImmutableArray<string> Aliases { get; }
        public ImmutableArray<PayloadInfo> Payloads { get; }
        public ImmutableArray<SecretAliasInfo> SecretAliases { get; }
        public bool HasAccessibleParameterlessConstructor { get; }
        public Location? Location { get; }
    }

    private sealed class PayloadInfo
    {
        public PayloadInfo(int family, int kind, string payloadType, string contextType)
        {
            Family = family;
            Kind = kind;
            PayloadType = payloadType;
            ContextType = contextType;
        }

        public int Family { get; }
        public int Kind { get; }
        public string PayloadType { get; }
        public string ContextType { get; }
    }

    private sealed record SecretAliasInfo(string SecretKey, ImmutableArray<string> EnvironmentVariables);

    private sealed class FamilyInfo
    {
        public FamilyInfo(int family, int lifetime, string? defaultModelName)
        {
            Family = family;
            Lifetime = lifetime;
            DefaultModelName = defaultModelName;
        }

        public int Family { get; }
        public int Lifetime { get; }
        public string? DefaultModelName { get; }
    }
}
