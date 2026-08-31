using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace HPD.Agent.SourceGenerator.Contracts;

/// <summary>
/// Compile-time semantic contract shared by schema, validation, and binding emitters.
/// This model deliberately contains Roslyn symbols and never crosses into generated runtime APIs.
/// </summary>
internal abstract record AIContractNode(
    ITypeSymbol Type,
    bool AllowsNull,
    string? Description);

/// <summary>
/// Represents the complete model-facing method contract before framework-owned fields are composed.
/// </summary>
internal sealed record AIFunctionMethodContract(
    ImmutableArray<AIFunctionContractParameter> Parameters);

/// <summary>
/// Represents one model-facing function parameter and its omission semantics.
/// </summary>
internal sealed record AIFunctionContractParameter(
    IParameterSymbol Symbol,
    string JsonName,
    AIContractNode Contract,
    bool IsRequired);

internal sealed record ScalarContractNode(
    ITypeSymbol Type,
    bool AllowsNull,
    string? Description,
    AIScalarKind Kind,
    string? Format = null,
    ImmutableArray<string> AllowedValues = default)
    : AIContractNode(Type, AllowsNull, Description);

internal sealed record ArrayContractNode(
    ITypeSymbol Type,
    bool AllowsNull,
    string? Description,
    AIContractNode Item)
    : AIContractNode(Type, AllowsNull, Description);

internal sealed record DictionaryContractNode(
    ITypeSymbol Type,
    bool AllowsNull,
    string? Description,
    AIContractNode Value)
    : AIContractNode(Type, AllowsNull, Description);

internal sealed record ObjectContractNode(
    ITypeSymbol Type,
    bool AllowsNull,
    string? Description,
    ImmutableArray<AIContractProperty> Properties,
    AIContractConstructionPlan Construction,
    ImmutableArray<string> AcceptedFrameworkProperties = default)
    : AIContractNode(Type, AllowsNull, Description);

internal sealed record UnionContractNode(
    ITypeSymbol Type,
    bool AllowsNull,
    string? Description,
    string DiscriminatorPropertyName,
    ImmutableArray<AIUnionCase> Cases)
    : AIContractNode(Type, AllowsNull, Description);

internal sealed record AIContractProperty(
    IPropertySymbol Symbol,
    string JsonName,
    AIContractNode Contract,
    bool IsRequired,
    string? Description);

internal sealed record AIUnionCase(
    string Discriminator,
    INamedTypeSymbol ConcreteType,
    ObjectContractNode Contract,
    string? InvocationModePolicy = null,
    string? InvocationModeHandling = null);

internal sealed record AIContractConstructionPlan(
    IMethodSymbol? Constructor,
    ImmutableArray<AIContractMemberBinding> Members);

internal sealed record AIContractMemberBinding(
    IPropertySymbol Property,
    IParameterSymbol? ConstructorParameter);

internal enum AIScalarKind
{
    String,
    Boolean,
    Integer,
    Number,
    Enum
}
