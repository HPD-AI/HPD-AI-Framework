using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace HPD.Agent.SourceGenerator.Capabilities;

/// <summary>Symbol-only discovery shared by source and referenced harness declarations.</summary>
internal static class HarnessSymbolDiscovery
{
    // These are CLR metadata names, including the historical global-namespace attributes.
    private static readonly (string Name, CapabilityType Kind)[] AttributeTypes =
    {
        ("AIFunctionAttribute", CapabilityType.Function),
        ("AIFunctionAttribute`1", CapabilityType.Function),
        ("SkillAttribute", CapabilityType.Skill),
        ("SkillAttribute`1", CapabilityType.Skill),
        ("SubAgentAttribute", CapabilityType.SubAgent),
        ("SubAgentAttribute`1", CapabilityType.SubAgent),
        ("HPD.Agent.MultiAgentAttribute", CapabilityType.MultiAgent),
        ("HPD.Agent.MultiAgentAttribute`1", CapabilityType.MultiAgent),
        ("HPD.Agent.McpServerAttribute", CapabilityType.MCPServer),
        ("HPD.Agent.McpServerAttribute`1", CapabilityType.MCPServer),
        ("HPD.Agent.OpenApiAttribute", CapabilityType.OpenApi)
    };

    internal static CapabilityType? Classify(AttributeData attribute, Compilation compilation)
    {
        var definition = attribute.AttributeClass?.OriginalDefinition;
        if (definition is null) return null;
        foreach (var entry in AttributeTypes)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    definition, compilation.GetTypeByMetadataName(entry.Name)))
                return entry.Kind;
        }
        return null;
    }

    /// <summary>Reads the nearest explicit capability declaration in a virtual override slot.</summary>
    internal static AttributeData? GetCapabilityAttribute(IMethodSymbol method, Compilation compilation)
    {
        for (var current = method; current is not null; current = current.OverriddenMethod)
        {
            var attribute = current.GetAttributes().FirstOrDefault(candidate =>
                Classify(candidate, compilation) is not null);
            if (attribute is not null) return attribute;
        }
        return null;
    }

    internal static ITypeSymbol? GetMetadataType(IMethodSymbol method, Compilation compilation)
    {
        var type = GetCapabilityAttribute(method, compilation)?.AttributeClass;
        return type is { IsGenericType: true, TypeArguments.Length: 1 }
            ? type.TypeArguments[0]
            : null;
    }

    /// <summary>Resolves readable instance metadata properties using class member hiding rules.</summary>
    internal static IEnumerable<IPropertySymbol> GetMetadataProperties(ITypeSymbol metadataType)
    {
        var hiddenNames = new HashSet<string>(System.StringComparer.Ordinal);
        for (var type = metadataType as INamedTypeSymbol; type is not null; type = type.BaseType)
        {
            foreach (var group in type.GetMembers().GroupBy(member => member.Name))
            {
                // Generated evaluators access metadata outside its declaring class.
                // Inaccessible members do not hide accessible base members in that context.
                if (!group.Any(member => member.DeclaredAccessibility == Accessibility.Public) ||
                    !hiddenNames.Add(group.Key)) continue;
                foreach (var property in group.OfType<IPropertySymbol>())
                    if (!property.IsStatic && !property.IsIndexer &&
                        property.DeclaredAccessibility == Accessibility.Public &&
                        property.GetMethod?.DeclaredAccessibility == Accessibility.Public)
                        yield return property;
            }
        }
    }

    /// <summary>
    /// Enumerates public capability slots, most derived first. Hidden members remain separate
    /// declarations so the resolver can diagnose collisions instead of silently dropping them.
    /// Constructed base symbols retain generic substitutions, including across assembly boundaries.
    /// </summary>
    internal static IEnumerable<IMethodSymbol> GetCapabilityMethods(
        INamedTypeSymbol owner, Compilation compilation)
    {
        var overridden = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var replacedNames = new HashSet<string>(StringComparer.Ordinal);
        for (var type = owner; type is not null && type.SpecialType != SpecialType.System_Object;
             type = type.BaseType)
        {
            var replacementNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary || overridden.Contains(method)) continue;
                for (var ancestor = method.OverriddenMethod; ancestor is not null;
                     ancestor = ancestor.OverriddenMethod)
                    overridden.Add(ancestor);
                if (method.DeclaredAccessibility != Accessibility.Public ||
                    GetCapabilityAttribute(method, compilation) is null ||
                    replacedNames.Contains(method.Name))
                    continue;
                if (IsExplicitReplacement(method))
                    replacementNames.Add(method.Name);
                yield return method;
            }
            replacedNames.UnionWith(replacementNames);
        }
    }

    /// <summary>
    /// An explicitly hidden same-named capability replaces the inherited capability contract.
    /// Ordinary overloads remain separate declarations and are diagnosed by validation.
    /// </summary>
    private static bool IsExplicitReplacement(IMethodSymbol method) =>
        method.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax declaration &&
            declaration.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NewKeyword));
}
