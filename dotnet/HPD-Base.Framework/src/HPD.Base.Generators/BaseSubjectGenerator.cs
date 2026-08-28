#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace HPD.Base.Generators;

internal static class BaseSubjectGenerator
{
    private const string AttributeName = "HPD.Base.BaseExportedSubjectAttribute";
    private const string CollectionAttribute = "HPD.Base.BaseCollectionAttribute";
    private static readonly DiagnosticDescriptor Invalid = new(
        "HPDBASE0460", "Invalid exported subject contract",
        "Exported subject marker '{0}' has an invalid closed declaration: {1}",
        "HPD.Base.Generation", DiagnosticSeverity.Error, true);

    internal static void Generate(SourceProductionContext context, ImmutableArray<INamedTypeSymbol> subjects)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (INamedTypeSymbol subject in subjects.OrderBy(static value => value.ToDisplayString(), StringComparer.Ordinal))
        {
            AttributeData attribute = subject.GetAttributes().Single(value => value.AttributeClass?.ToDisplayString() == AttributeName);
            string? id = ConstructorString(attribute, 0);
            int version = NamedInt(attribute, "Version", 1);
            string? module = NamedString(attribute, "OwningModuleId");
            INamedTypeSymbol? privateRecord = NamedType(attribute, "PrivateRecordType");
            AttributeData? privateCollection = privateRecord?.GetAttributes().FirstOrDefault(value => value.AttributeClass?.ToDisplayString() == CollectionAttribute);
            string? collectionId = privateCollection is null ? null : ConstructorString(privateCollection, 0);
            string? acquisitionGrant = NamedString(attribute, "AcquisitionGrantId");
            string? validationGrant = NamedString(attribute, "ValidationGrantId");
            string? administrationGrant = NamedString(attribute, "AdministrationGrantId");
            string? planId = NamedString(attribute, "ValidationPlanId");
            int planVersion = NamedInt(attribute, "ValidationPlanVersion", 1);
            int idKind = NamedInt(attribute, "SubjectIdKind", 0);
            int maximumIdBytes = NamedInt(attribute, "MaximumSubjectIdUtf8Bytes", 256);
            int scope = NamedInt(attribute, "Scope", 0);
            string? activeField = NamedString(attribute, "ActiveFieldId");
            bool activeValue = NamedBool(attribute, "ActiveValue", true);
            string? tombstoneField = NamedString(attribute, "TombstoneFieldId");
            bool coordinatedRetirement = NamedBool(attribute, "SupportsCoordinatedRetirement", false);
            string? scopeField = NamedString(attribute, "ScopeFieldId");
            int[] audiences = NamedArray(attribute, "Audiences", new[] { 1 });
            string identity = id + "\0" + version;
            string? error = !IsPartial(subject) || subject.TypeKind != TypeKind.Class || subject.IsGenericType ? "the marker must be a partial non-generic class" :
                !ValidId(id) || !identities.Add(identity) || version < 1 || !ValidId(module) || !ValidId(collectionId) ||
                !ValidId(acquisitionGrant) || !ValidId(validationGrant) || !ValidId(administrationGrant) || !ValidId(planId) || planVersion < 1 ? "stable identities or versions are invalid or duplicated" :
                idKind is < 0 or > 2 || maximumIdBytes is < 1 or > 256 || scope is < 0 or > 2 ? "the ID grammar, maximum, or scope is invalid" :
                activeField is null || !ValidId(activeField) || !ValidId(tombstoneField) || scopeField is not null && !ValidId(scopeField) ? "a bound stable lifecycle or scope field ID is invalid" :
                scope == 0 && scopeField is not null || scope != 0 && scopeField is null ? "the scope and scope-field binding disagree" :
                audiences.Length == 0 || audiences.Any(static value => value is < 0 or > 2) || audiences.Distinct().Count() != audiences.Length ? "the audience set is invalid" : null;
            if (error is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(Invalid, subject.Locations.FirstOrDefault() ?? Location.None, subject.Name, error));
                continue;
            }
            context.AddSource(subject.Name + ".HPDBaseSubject.g.cs", SourceText.From(Render(subject, id!, version, module!, collectionId!,
                acquisitionGrant!, validationGrant!, administrationGrant!, planId!, planVersion, idKind, maximumIdBytes, scope, activeField, activeValue,
                tombstoneField!, coordinatedRetirement, scopeField, audiences), Encoding.UTF8));
        }
    }

    private static string Render(INamedTypeSymbol subject, string id, int version, string module, string collectionId,
        string acquisitionGrant, string validationGrant, string administrationGrant, string planId, int planVersion, int idKind, int maximumIdBytes,
        int scope, string? activeField, bool activeValue, string tombstoneField, bool coordinatedRetirement, string? scopeField, int[] audiences)
    {
        string type = subject.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var source = new StringBuilder("// <auto-generated />\n#nullable enable\n\n");
        if (!subject.ContainingNamespace.IsGlobalNamespace) source.Append("namespace ").Append(subject.ContainingNamespace.ToDisplayString()).AppendLine(";\n");
        source.Append("partial class ").Append(subject.Name).AppendLine("\n{");
        source.AppendLine("    private static readonly global::HPD.Base.BaseGeneratedSubjectRegistration __registration = global::HPD.Base.BaseGeneratedSubjects.Register<" + type + ">(new global::HPD.Base.BaseExportedSubjectDefinition");
        source.AppendLine("    {");
        source.Append("        Id = ").Append(Literal(id)).AppendLine(","); source.Append("        Version = ").Append(version).AppendLine(",");
        source.Append("        OwningModuleId = ").Append(Literal(module)).AppendLine(",");
        source.Append("        SubjectIdKind = (global::HPD.Base.BaseSubjectIdKind)").Append(idKind).AppendLine(",");
        source.Append("        MaximumSubjectIdUtf8Bytes = ").Append(maximumIdBytes).AppendLine(",");
        source.Append("        Scope = (global::HPD.Base.BaseSubjectScopeKind)").Append(scope).AppendLine(",");
        source.Append("        AcquisitionGrantId = ").Append(Literal(acquisitionGrant)).AppendLine(",");
        source.Append("        ValidationGrantId = ").Append(Literal(validationGrant)).AppendLine(",");
        source.Append("        AdministrationGrantId = ").Append(Literal(administrationGrant)).AppendLine(",");
        source.Append("        TombstoneFieldId = ").Append(Literal(tombstoneField)).AppendLine(",");
        source.Append("        SupportsCoordinatedRetirement = ").Append(coordinatedRetirement ? "true" : "false").AppendLine(",");
        source.Append("        Audiences = [").Append(string.Join(", ", audiences.OrderBy(static value => value).Select(static value => "(global::HPD.Base.HPDBaseEndpointAudience)" + value))).AppendLine("],");
        source.AppendLine("        ValidationPlan = new global::HPD.Base.BaseSubjectValidationPlanDefinition"); source.AppendLine("        {");
        source.Append("            Id = ").Append(Literal(planId)).AppendLine(","); source.Append("            Version = ").Append(planVersion).AppendLine(",");
        source.Append("            ContractId = ").Append(Literal(id)).AppendLine(","); source.Append("            ContractVersion = ").Append(version).AppendLine(",");
        source.AppendLine("            ContractChecksum = \"0000000000000000000000000000000000000000000000000000000000000000\",");
        source.Append("            PrivateCollectionId = ").Append(Literal(collectionId)).AppendLine(",");
        source.AppendLine("            SubjectId = global::HPD.Base.BaseSubjectIdBinding.RecordId,");
        source.Append("            Active = new global::HPD.Base.BaseSubjectActiveBinding { Kind = global::HPD.Base.BaseSubjectActiveBindingKind.")
            .Append(activeField is null ? "NotDeclared" : "RequiredBooleanField").Append(", FieldId = ").Append(activeField is null ? "null" : Literal(activeField))
            .Append(", ActiveValue = ").Append(activeValue ? "true" : "false").AppendLine(" },");
        string scopeKind = scope switch { 1 => "RequiredTenantField", 2 => "RequiredProjectField", _ => "Global" };
        source.Append("            Scope = new global::HPD.Base.BaseSubjectScopeBinding { Kind = global::HPD.Base.BaseSubjectScopeBindingKind.")
            .Append(scopeKind).Append(", FieldId = ").Append(scopeField is null ? "null" : Literal(scopeField)).AppendLine(" },");
        source.AppendLine("            Access = global::HPD.Base.BaseSubjectValidationAccessShape.ContractAndSubjectPrimaryKeys,");
        source.AppendLine("            Limits = global::HPD.Base.BaseSubjectValidationLimits.Default,");
        source.AppendLine("        },"); source.AppendLine("    });");
        source.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        source.AppendLine("    internal static void PublishHPDBaseSubjectAuthority()");
        source.AppendLine("    {");
        source.Append("        global::HPD.Base.BaseGeneratedSubjectAuthority.Publish<").Append(type).AppendLine(">(__registration);");
        source.Append("        global::HPD.Base.BaseSubjectReferenceJsonConverterFactory.Register<").Append(type)
            .Append(">((global::HPD.Base.BaseSubjectIdKind)").Append(idKind).Append(", ").Append(maximumIdBytes).AppendLine(");");
        source.AppendLine("    }");
        source.AppendLine("    /// <summary>Gets the generated immutable installation receipt for the exporting module.</summary>");
        source.AppendLine("    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        source.AppendLine("    public static global::HPD.Base.BaseGeneratedSubjectRegistration HPDBaseSubjectRegistration => __registration;");
        source.AppendLine("    /// <summary>Resolves this exported subject from an authorized installed BASE session.</summary>");
        source.Append("    public static global::HPD.Base.BaseExportedSubjectContract<").Append(type).Append("> Contract(global::HPD.Base.BaseSession session) => session.GetExportedSubjectContract<")
            .Append(type).AppendLine(">(__registration);");
        source.AppendLine("}");
        return source.ToString();
    }

    private static bool IsPartial(INamedTypeSymbol symbol) => symbol.DeclaringSyntaxReferences.Select(static value => value.GetSyntax())
        .OfType<TypeDeclarationSyntax>().Any(static value => value.Modifiers.Any(SyntaxKind.PartialKeyword));
    private static string? ConstructorString(AttributeData value, int index) => value.ConstructorArguments.Length > index ? value.ConstructorArguments[index].Value as string : null;
    private static string? NamedString(AttributeData value, string name) => value.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as string;
    private static INamedTypeSymbol? NamedType(AttributeData value, string name) => value.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as INamedTypeSymbol;
    private static int NamedInt(AttributeData value, string name, int fallback) { TypedConstant item = value.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value; return item.Value is null ? fallback : Convert.ToInt32(item.Value); }
    private static bool NamedBool(AttributeData value, string name, bool fallback) { TypedConstant item = value.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value; return item.Value is null ? fallback : (bool)item.Value; }
    private static int[] NamedArray(AttributeData value, string name, int[] fallback) { TypedConstant item = value.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value; return item.Kind == TypedConstantKind.Array ? item.Values.Select(static entry => Convert.ToInt32(entry.Value)).ToArray() : fallback; }
    private static bool ValidId(string? value) => value is { Length: >= 1 and <= 128 } && AsciiLetterOrDigit(value[0]) && value.All(static character => AsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    private static bool AsciiLetterOrDigit(char value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);
}
