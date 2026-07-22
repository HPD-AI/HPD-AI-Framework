using System.Collections.Immutable;
using System.Text;
using HPD.Agent.ToolHarness.Coding.SourceGenerator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace HPD.Agent.ToolHarness.Coding.SourceGenerator;

[Generator]
public sealed class DebugAdapterSourceGenerator : IIncrementalGenerator
{
    private const string DeclarationAttribute = "HPD.Agent.ToolHarness.Coding.Debugging.Attributes.HpdDebugAdapterAttribute";
    private const string LanguagesAttribute = "HPD.Agent.ToolHarness.Coding.Debugging.Attributes.DebugAdapterLanguagesAttribute";
    private const string ExtensionsAttribute = "HPD.Agent.ToolHarness.Coding.Debugging.Attributes.DebugAdapterFileExtensionsAttribute";
    private const string RootMarkersAttribute = "HPD.Agent.ToolHarness.Coding.Debugging.Attributes.DebugAdapterRootMarkersAttribute";
    private const string TargetKindsAttribute = "HPD.Agent.ToolHarness.Coding.Debugging.Attributes.DebugAdapterTargetKindsAttribute";
    private const string FactoryAttribute = "HPD.Agent.ToolHarness.Coding.Debugging.Attributes.DebugAdapterFactoryAttribute";
    private const string CommandHintAttribute = "HPD.Agent.ToolHarness.Coding.Debugging.Attributes.DebugAdapterCommandHintAttribute";
    private const string ArgumentHintsAttribute = "HPD.Agent.ToolHarness.Coding.Debugging.Attributes.DebugAdapterArgumentHintsAttribute";
    private const string InstallGuidanceAttribute = "HPD.Agent.ToolHarness.Coding.Debugging.Attributes.DebugAdapterInstallGuidanceAttribute";
    private const string PriorityAttribute = "HPD.Agent.ToolHarness.Coding.Debugging.Attributes.DebugAdapterPriorityAttribute";
    private const string ExperimentalAttribute = "HPD.Agent.ToolHarness.Coding.Debugging.Attributes.DebugAdapterExperimentalAttribute";
    private const string DisabledAttribute = "HPD.Agent.ToolHarness.Coding.Debugging.Attributes.DebugAdapterDisabledByDefaultAttribute";
    private const string FactoryInterface = "HPD.Agent.ToolHarness.Coding.Debugging.IDebugAdapterFactory";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.SyntaxProvider.ForAttributeWithMetadataName(
                DeclarationAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ctx.TargetSymbol as INamedTypeSymbol)
            .Where(static symbol => symbol is not null)
            .Collect();
        context.RegisterSourceOutput(declarations.Combine(context.CompilationProvider),
            static (production, input) => Execute(production, input.Left!, input.Right));
    }

    private static void Execute(SourceProductionContext context, ImmutableArray<INamedTypeSymbol?> symbols, Compilation compilation)
    {
        var adapters = new List<AdapterInfo>();
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (symbol is null)
                continue;
            var adapter = Analyze(context, symbol);
            if (adapter is null)
                continue;
            if (ids.TryGetValue(adapter.Id, out var existing))
            {
                ReportAtAttributeArgument(context, DebugAdapterGeneratorDiagnostics.DuplicateId, symbol, DeclarationAttribute, 0, 0, adapter.Id, existing, symbol.Name);
                continue;
            }
            ids[adapter.Id] = symbol.Name;
            adapters.Add(adapter);
        }
        if (adapters.Count == 0)
            return;
        context.AddSource("DebugAdapterCatalog.g.cs", SourceText.From(Generate(adapters, compilation), Encoding.UTF8));
    }

    private static AdapterInfo? Analyze(SourceProductionContext context, INamedTypeSymbol symbol)
    {
        var invalid = false;
        if (symbol.DeclaredAccessibility != Accessibility.Public)
        {
            Report(context, DebugAdapterGeneratorDiagnostics.DeclarationNotPublic, symbol, symbol.Name);
            invalid = true;
        }
        var id = GetString(symbol, DeclarationAttribute);
        if (string.IsNullOrWhiteSpace(id))
        {
            ReportAtAttributeArgument(context, DebugAdapterGeneratorDiagnostics.InvalidId, symbol, DeclarationAttribute, 0, 0, symbol.Name);
            invalid = true;
        }
        var languages = GetStrings(symbol, LanguagesAttribute);
        if (languages.Count == 0)
        {
            Report(context, DebugAdapterGeneratorDiagnostics.MissingLanguages, symbol, symbol.Name);
            invalid = true;
        }
        for (var index = 0; index < languages.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(languages[index]))
            {
                ReportAtAttributeArgument(context, DebugAdapterGeneratorDiagnostics.InvalidLanguage, symbol, LanguagesAttribute, 0, index, symbol.Name);
                invalid = true;
            }
        }
        var extensions = GetStrings(symbol, ExtensionsAttribute);
        for (var index = 0; index < extensions.Count; index++)
        {
            var extension = extensions[index];
            if (string.IsNullOrWhiteSpace(extension) || !extension.StartsWith(".", StringComparison.Ordinal))
            {
                ReportAtAttributeArgument(context, DebugAdapterGeneratorDiagnostics.InvalidExtension, symbol, ExtensionsAttribute, 0, index, symbol.Name, extension);
                invalid = true;
            }
        }
        var roots = GetStrings(symbol, RootMarkersAttribute);
        for (var index = 0; index < roots.Count; index++)
        {
            var root = roots[index];
            if (IsInvalidRootMarker(root))
            {
                ReportAtAttributeArgument(context, DebugAdapterGeneratorDiagnostics.InvalidRootMarker, symbol, RootMarkersAttribute, 0, index, symbol.Name, root);
                invalid = true;
            }
        }
        invalid |= ReportDuplicates(context, symbol, LanguagesAttribute, "language", languages);
        invalid |= ReportDuplicates(context, symbol, ExtensionsAttribute, "extension", extensions);
        invalid |= ReportDuplicates(context, symbol, RootMarkersAttribute, "root marker", roots);
        var targetKinds = GetInt(symbol, TargetKindsAttribute);
        if (targetKinds == 0)
        {
            Report(context, DebugAdapterGeneratorDiagnostics.MissingTargetKinds, symbol, symbol.Name);
            invalid = true;
        }
        const int supportedTargetKinds = 1 | 2 | 4 | 8 | 16 | 32;
        if ((targetKinds & ~supportedTargetKinds) != 0)
        {
            ReportAtAttributeArgument(context, DebugAdapterGeneratorDiagnostics.UnsupportedTargetKinds, symbol, TargetKindsAttribute, 0, 0, symbol.Name, targetKinds);
            invalid = true;
        }
        var commands = GetRepeatedStrings(symbol, CommandHintAttribute);
        for (var index = 0; index < commands.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(commands[index]) || commands[index].Contains('\0') || commands[index].Contains('/') || commands[index].Contains('\\'))
            {
                ReportAtAttributeArgument(context, DebugAdapterGeneratorDiagnostics.InvalidCommandHint, symbol, CommandHintAttribute, index, 0, symbol.Name);
                invalid = true;
            }
        }
        var arguments = GetStrings(symbol, ArgumentHintsAttribute);
        for (var index = 0; index < arguments.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(arguments[index]) || arguments[index].Contains('\0'))
            {
                ReportAtAttributeArgument(context, DebugAdapterGeneratorDiagnostics.InvalidArgumentHint, symbol, ArgumentHintsAttribute, 0, index, symbol.Name);
                invalid = true;
            }
        }
        var installGuidance = GetString(symbol, InstallGuidanceAttribute);
        if (installGuidance is not null && (string.IsNullOrWhiteSpace(installGuidance) || installGuidance.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '-' or '_'))))
        {
            ReportAtAttributeArgument(context, DebugAdapterGeneratorDiagnostics.InvalidInstallGuidance, symbol, InstallGuidanceAttribute, 0, 0, symbol.Name, installGuidance);
            invalid = true;
        }
        var priority = GetInt(symbol, PriorityAttribute);
        if (priority is < -10000 or > 10000)
        {
            ReportAtAttributeArgument(context, DebugAdapterGeneratorDiagnostics.InvalidPriority, symbol, PriorityAttribute, 0, 0, symbol.Name, priority);
            invalid = true;
        }
        var factory = GetType(symbol, FactoryAttribute);
        if (factory is null && commands.Count == 0)
        {
            Report(context, DebugAdapterGeneratorDiagnostics.MissingCommandHint, symbol, symbol.Name);
            invalid = true;
        }
        if (factory?.ToDisplayString() == "HPD.Agent.ToolHarness.Coding.Debugging.StandardDebugAdapterFactory")
        {
            ReportAtAttributeArgument(context, DebugAdapterGeneratorDiagnostics.ExplicitStandardFactory, symbol, FactoryAttribute, 0, 0, symbol.Name);
            invalid = true;
        }
        if (factory is not null && (factory.TypeKind != TypeKind.Class || factory.IsAbstract ||
            factory.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal) ||
            !factory.AllInterfaces.Any(item => item.ToDisplayString() == FactoryInterface)))
        {
            ReportAtAttributeArgument(context, DebugAdapterGeneratorDiagnostics.InvalidFactory, symbol, FactoryAttribute, 0, 0, symbol.Name, factory.ToDisplayString());
            invalid = true;
        }
        if (invalid)
            return null;
        return new AdapterInfo(
            id!, languages, extensions, roots, targetKinds, commands,
            arguments, installGuidance,
            priority, Has(symbol, ExperimentalAttribute), Has(symbol, DisabledAttribute),
            factory?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::HPD.Agent.ToolHarness.Coding.Debugging.StandardDebugAdapterFactory");
    }

    private static string Generate(IReadOnlyList<AdapterInfo> adapters, Compilation compilation)
    {
        var assemblyName = compilation.AssemblyName ?? "unknown";
        var version = compilation.Assembly.Identity.Version?.ToString() ?? "0.0.0.0";
        var providerName = "GeneratedDebugAdapterCatalogProvider_" + Identifier(assemblyName);
        var writer = new StringBuilder("// <auto-generated/>\n#nullable enable\n\nnamespace HPD.Agent.ToolHarness.Coding.Debugging.Generated;\n\n");
        writer.AppendLine($"internal sealed class {providerName} : global::HPD.Agent.ToolHarness.Coding.Debugging.IDebugAdapterCatalogProvider");
        writer.AppendLine("{");
        writer.AppendLine("    public global::System.Collections.Generic.IEnumerable<global::HPD.Agent.ToolHarness.Coding.Debugging.DebugAdapterCatalogEntry> GetEntries() => All;");
        writer.AppendLine("    internal static readonly global::HPD.Agent.ToolHarness.Coding.Debugging.DebugAdapterCatalogEntry[] All =");
        writer.AppendLine("    [");
        foreach (var adapter in adapters.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            writer.AppendLine("        new()");
            writer.AppendLine("        {");
            writer.AppendLine("            Descriptor = new()");
            writer.AppendLine("            {");
            writer.AppendLine($"                Id = \"{Escape(adapter.Id)}\",");
            writer.AppendLine($"                Languages = [{List(adapter.Languages)}],");
            writer.AppendLine($"                FileExtensions = [{List(adapter.Extensions)}],");
            writer.AppendLine($"                RootMarkers = [{List(adapter.RootMarkers)}],");
            writer.AppendLine($"                TargetKinds = (global::HPD.Agent.ToolHarness.Coding.Debugging.DebugTargetKind){adapter.TargetKinds},");
            writer.AppendLine($"                CommandHints = [{List(adapter.CommandHints)}],");
            writer.AppendLine($"                ArgumentHints = [{List(adapter.ArgumentHints)}],");
            if (adapter.InstallGuidance is not null) writer.AppendLine($"                InstallGuidanceId = \"{Escape(adapter.InstallGuidance)}\",");
            writer.AppendLine($"                Priority = {adapter.Priority},");
            writer.AppendLine($"                EnabledByDefault = {(!adapter.Disabled).ToString().ToLowerInvariant()},");
            writer.AppendLine($"                Experimental = {adapter.Experimental.ToString().ToLowerInvariant()},");
            writer.AppendLine($"                Provenance = new() {{ PackageId = \"{Escape(assemblyName)}\", PackageVersion = \"{Escape(version)}\", AssemblyName = \"{Escape(assemblyName)}\" }}");
            writer.AppendLine("            },");
            writer.AppendLine($"            FactoryResolver = static services => global::HPD.Agent.ToolHarness.Coding.Debugging.DebugAdapterFactoryResolution.GetRequired<{adapter.FactoryType}>(services)");
            writer.AppendLine("        },");
        }
        writer.AppendLine("    ];");
        writer.AppendLine("}");
        return writer.ToString();
    }

    private static bool ReportDuplicates(SourceProductionContext context, INamedTypeSymbol symbol, string attributeName, string kind, IReadOnlyList<string> values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invalid = false;
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (!seen.Add(value))
            {
                ReportAtAttributeArgument(context, DebugAdapterGeneratorDiagnostics.DuplicateValue, symbol, attributeName, 0, index, symbol.Name, kind, value);
                invalid = true;
            }
        }
        return invalid;
    }
    private static void Report(SourceProductionContext context, DiagnosticDescriptor descriptor, INamedTypeSymbol symbol, params object[] args)
        => context.ReportDiagnostic(Diagnostic.Create(descriptor, symbol.Locations.FirstOrDefault(), args));
    private static void ReportAtAttributeArgument(
        SourceProductionContext context,
        DiagnosticDescriptor descriptor,
        INamedTypeSymbol symbol,
        string attributeName,
        int occurrence,
        int argumentIndex,
        params object[] args)
    {
        var attribute = symbol.GetAttributes()
            .Where(item => item.AttributeClass?.ToDisplayString() == attributeName)
            .Skip(occurrence)
            .FirstOrDefault();
        var syntax = attribute?.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
        var location = syntax?.ArgumentList?.Arguments.ElementAtOrDefault(argumentIndex)?.GetLocation()
            ?? syntax?.GetLocation()
            ?? symbol.Locations.FirstOrDefault();
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, args));
    }
    private static bool IsInvalidRootMarker(string marker)
    {
        if (string.IsNullOrWhiteSpace(marker) || marker.Contains('\0') ||
            marker.StartsWith("/", StringComparison.Ordinal) || marker.StartsWith("\\", StringComparison.Ordinal))
            return true;
        if (marker.Length >= 2 && char.IsLetter(marker[0]) && marker[1] == ':')
            return true;
        return marker.Split(new[] { '/', '\\' }).Any(segment => segment == "..");
    }
    private static bool Has(INamedTypeSymbol symbol, string name) => symbol.GetAttributes().Any(item => item.AttributeClass?.ToDisplayString() == name);
    private static AttributeData? Attribute(INamedTypeSymbol symbol, string name) => symbol.GetAttributes().FirstOrDefault(item => item.AttributeClass?.ToDisplayString() == name);
    private static string? GetString(INamedTypeSymbol symbol, string name) => Attribute(symbol, name)?.ConstructorArguments.FirstOrDefault().Value as string;
    private static int GetInt(INamedTypeSymbol symbol, string name) => Attribute(symbol, name)?.ConstructorArguments.FirstOrDefault().Value is int value ? value : 0;
    private static INamedTypeSymbol? GetType(INamedTypeSymbol symbol, string name) => Attribute(symbol, name)?.ConstructorArguments.FirstOrDefault().Value as INamedTypeSymbol;
    private static IReadOnlyList<string> GetStrings(INamedTypeSymbol symbol, string name)
    {
        var value = Attribute(symbol, name)?.ConstructorArguments.FirstOrDefault();
        return value is { Kind: TypedConstantKind.Array } ? value.Value.Values.Select(item => item.Value as string ?? "").ToArray() : [];
    }
    private static IReadOnlyList<string> GetRepeatedStrings(INamedTypeSymbol symbol, string name) => symbol.GetAttributes()
        .Where(item => item.AttributeClass?.ToDisplayString() == name).Select(item => item.ConstructorArguments.FirstOrDefault().Value as string ?? "").ToArray();
    private static string List(IEnumerable<string> values) => string.Join(", ", values.Select(value => $"\"{Escape(value)}\""));
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string Identifier(string value) => new(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());

    private sealed record AdapterInfo(string Id, IReadOnlyList<string> Languages, IReadOnlyList<string> Extensions, IReadOnlyList<string> RootMarkers, int TargetKinds, IReadOnlyList<string> CommandHints, IReadOnlyList<string> ArgumentHints, string? InstallGuidance, int Priority, bool Experimental, bool Disabled, string FactoryType);
}
