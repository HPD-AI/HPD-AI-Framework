using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal enum ToolHarnessConstructorParameterKind
{
    HarnessConfiguration,
    RequiredService,
    OptionalService,
    Literal
}

internal sealed record ToolHarnessConstructorParameterPlan(
    IParameterSymbol Parameter,
    ToolHarnessConstructorParameterKind Kind,
    string Expression);

internal sealed record ToolHarnessConstructionAnalysis(
    INamedTypeSymbol Owner,
    IMethodSymbol? Constructor,
    ImmutableArray<ToolHarnessConstructorParameterPlan> Parameters,
    ImmutableArray<Diagnostic> Diagnostics)
{
    internal bool IsValid =>
        Constructor is not null &&
        Parameters.Length == Constructor.Parameters.Length &&
        Diagnostics.All(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);

    internal string ActivationExpression(string context = "context")
    {
        if (!IsValid)
            throw new InvalidOperationException($"Invalid construction analysis for '{Owner.ToDisplayString()}'.");

        return $"{context} => new {Owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}({string.Join(", ", Parameters.Select(parameter => parameter.Expression))})";
    }
}

internal static class ToolHarnessConstructionAnalyzer
{
    private static readonly DiagnosticDescriptor NoPublicConstructor = new(
        "HPDHIN009",
        "ToolHarness has no public constructor",
        "Harness '{0}' has no public constructor for generated activation. Add one public constructor or register an explicitly constructed instance.",
        "HPD.Activation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor MultiplePublicConstructors = new(
        "HPDHIN010",
        "ToolHarness has multiple public constructors",
        "Harness '{0}' must declare exactly one public constructor. Found:{1} Consolidate construction into one public constructor, make helper constructors non-public, or register an explicitly constructed harness instance.",
        "HPD.Activation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor UnsupportedParameter = new(
        "HPDHIN011",
        "ToolHarness constructor parameter is not activation-compatible",
        "Harness '{0}' constructor parameter '{1}' cannot be generated: {2}",
        "HPD.Activation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor InvalidDependencyDefault = new(
        "HPDHIN012",
        "ToolHarness dependency default is invalid",
        "Harness '{0}' dependency default for '{1}' uses invalid implementation '{2}': {3}",
        "HPD.Activation",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor DuplicateDependencyDefault = new(
        "HPDHIN013",
        "ToolHarness dependency default duplicates a service contract",
        "Harness '{0}' has more than one dependency default for service '{1}'. The first declaration is on '{2}'.",
        "HPD.Activation",
        DiagnosticSeverity.Error,
        true);

    internal static ToolHarnessConstructionAnalysis Analyze(
        INamedTypeSymbol owner,
        Compilation compilation,
        DiagnosticDescriptor? parameterDescriptor = null)
    {
        parameterDescriptor ??= UnsupportedParameter;

        var constructors = owner.InstanceConstructors
            .Where(static constructor => constructor.DeclaredAccessibility == Accessibility.Public)
            .ToArray();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        if (constructors.Length == 0)
        {
            var location = owner.Locations.FirstOrDefault() ?? Location.None;
            diagnostics.Add(Diagnostic.Create(NoPublicConstructor, location, owner.Name));
            return new(owner, null, [], diagnostics.ToImmutable());
        }
        if (constructors.Length > 1)
        {
            var location = owner.Locations.FirstOrDefault() ?? Location.None;
            var constructorList = "\n  " + string.Join(
                "\n  ",
                constructors.Select(static constructor =>
                    constructor.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
            diagnostics.Add(Diagnostic.Create(
                MultiplePublicConstructors,
                location,
                constructors.Select(static constructor => constructor.Locations.FirstOrDefault() ?? Location.None)
                    .Where(static location => location != Location.None)
                    .ToImmutableArray(),
                properties: null,
                owner.Name,
                constructorList));
            return new(owner, null, [], diagnostics.ToImmutable());
        }

        var constructor = constructors[0];
        var parameters = ImmutableArray.CreateBuilder<ToolHarnessConstructorParameterPlan>();
        var assemblyContext = (ISymbol)owner.ContainingAssembly;
        foreach (var parameter in constructor.Parameters)
        {
            var invalidReason = ValidateParameter(parameter, compilation, assemblyContext);
            if (invalidReason is not null)
            {
                diagnostics.Add(Diagnostic.Create(
                    parameterDescriptor,
                    parameter.Locations.FirstOrDefault() ?? constructor.Locations.FirstOrDefault() ?? Location.None,
                    owner.Name,
                    parameter.Name,
                    invalidReason));
                continue;
            }

            parameters.Add(CreateParameterPlan(parameter));
        }

        return new(owner, constructor, parameters.ToImmutable(), diagnostics.ToImmutable());
    }

    internal static ImmutableArray<Diagnostic> AnalyzeDependencyDefaults(
        INamedTypeSymbol owner,
        Compilation compilation)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var firstDeclarations = new Dictionary<ISymbol, (ISymbol Owner, Location Location)>(
            SymbolEqualityComparer.Default);
        var assemblyContext = (ISymbol)owner.ContainingAssembly;

        var hierarchy = new Stack<INamedTypeSymbol>();
        for (var declaration = owner; declaration is not null; declaration = declaration.BaseType)
            hierarchy.Push(declaration);

        while (hierarchy.Count > 0)
        {
            var declaration = hierarchy.Pop();
            foreach (var attribute in declaration.GetAttributes().Where(static attribute =>
                         attribute.AttributeClass?.ToDisplayString() == "HPD.Agent.ToolHarnessDependencyAttribute")
                     .Where(attribute => IsDeclaredOn(attribute, declaration)))
            {
                var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                    ?? declaration.Locations.FirstOrDefault()
                    ?? Location.None;
                if (attribute.ConstructorArguments.Length != 2 ||
                    attribute.ConstructorArguments[0].Value is not INamedTypeSymbol service ||
                    attribute.ConstructorArguments[1].Value is not INamedTypeSymbol implementation)
                {
                    diagnostics.Add(Diagnostic.Create(
                        InvalidDependencyDefault,
                        location,
                        owner.Name,
                        "<unknown>",
                        "<unknown>",
                        "The dependency attribute must declare a service type and an implementation type."));
                    continue;
                }

                if (firstDeclarations.TryGetValue(service, out var first))
                {
                    diagnostics.Add(Diagnostic.Create(
                        DuplicateDependencyDefault,
                        location,
                        new[] { first.Location }.ToImmutableArray(),
                        properties: null,
                        owner.Name,
                        service.ToDisplayString(),
                        first.Owner.ToDisplayString()));
                    continue;
                }
                firstDeclarations.Add(service, (declaration, location));

                var invalidReason = ValidateDependencyImplementation(
                    service,
                    implementation,
                    compilation,
                    assemblyContext);
                if (invalidReason is not null)
                {
                    diagnostics.Add(Diagnostic.Create(
                        InvalidDependencyDefault,
                        location,
                        owner.Name,
                        service.ToDisplayString(),
                        implementation.ToDisplayString(),
                        invalidReason));
                }
            }
        }

        return diagnostics.ToImmutable();
    }

    private static bool IsDeclaredOn(AttributeData attribute, INamedTypeSymbol declaration)
    {
        if (attribute.ApplicationSyntaxReference is not { } syntax)
            return true;
        return declaration.DeclaringSyntaxReferences.Any(reference =>
            reference.SyntaxTree == syntax.SyntaxTree &&
            reference.Span.Contains(syntax.Span));
    }

    internal static string ConstructorExpression(
        INamedTypeSymbol owner,
        Compilation compilation,
        string context = "context")
    {
        var analysis = Analyze(owner, compilation);
        return analysis.IsValid
            ? analysis.ActivationExpression(context)
            : throw new InvalidOperationException(
                $"Dependency implementation '{owner.ToDisplayString()}' has an invalid constructor.");
    }

    private static string? ValidateDependencyImplementation(
        INamedTypeSymbol service,
        INamedTypeSymbol implementation,
        Compilation compilation,
        ISymbol assemblyContext)
    {
        if (!service.IsReferenceType)
            return "The service contract must be a reference type.";
        if (!compilation.IsSymbolAccessibleWithin(service, assemblyContext))
            return "The service contract is not accessible to generated code.";
        if (implementation.IsAbstract || implementation.IsStatic)
            return "The implementation must be concrete and non-static.";
        if (implementation.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return "The implementation must be a class or struct.";
        if (implementation.IsGenericType && !implementation.IsUnboundGenericType &&
            implementation.TypeArguments.Any(static argument => argument.TypeKind == TypeKind.TypeParameter))
            return "The implementation must be closed.";
        if (implementation.IsUnboundGenericType)
            return "The implementation must be closed.";
        if (!compilation.IsSymbolAccessibleWithin(implementation, assemblyContext))
            return "The implementation is not accessible to generated code.";
        if (!compilation.ClassifyConversion(implementation, service).IsImplicit)
            return "The implementation is not assignable to the service contract.";

        var construction = Analyze(implementation, compilation);
        if (!construction.IsValid)
        {
            var reason = construction.Diagnostics.FirstOrDefault()?.GetMessage();
            return reason ?? "The implementation must expose exactly one supported public constructor.";
        }

        return null;
    }

    private static string? ValidateParameter(
        IParameterSymbol parameter,
        Compilation compilation,
        ISymbol assemblyContext)
    {
        if (parameter.RefKind != RefKind.None)
            return "ref, in, and out parameters are not supported.";
        if (parameter.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
            return "Pointer and function-pointer parameters are not supported.";
        if (!compilation.IsSymbolAccessibleWithin(parameter.Type, assemblyContext))
            return $"Type '{parameter.Type.ToDisplayString()}' is not accessible to generated code.";

        if (parameter.Type is INamedTypeSymbol named &&
            named.OriginalDefinition.ToDisplayString() == "HPD.Agent.IHarnessConfig<T>")
        {
            var configuration = named.TypeArguments[0];
            if (!configuration.IsReferenceType ||
                configuration.TypeKind == TypeKind.TypeParameter ||
                !compilation.IsSymbolAccessibleWithin(configuration, assemblyContext))
            {
                return "The harness configuration contract must be a closed, accessible reference type.";
            }
            return null;
        }

        if (parameter.Type.Name.EndsWith("Config", StringComparison.Ordinal))
            return "Plain configuration constructor parameters are not supported. Use IHarnessConfig<T>.";

        if (parameter.IsOptional)
            return null;
        if (!parameter.Type.IsReferenceType)
            return $"Required type '{parameter.Type.ToDisplayString()}' is not a reference type.";
        return null;
    }

    private static ToolHarnessConstructorParameterPlan CreateParameterPlan(IParameterSymbol parameter)
    {
        var type = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (parameter.Type is INamedTypeSymbol named &&
            named.OriginalDefinition.ToDisplayString() == "HPD.Agent.IHarnessConfig<T>")
        {
            var configuration = named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return new(parameter, ToolHarnessConstructorParameterKind.HarnessConfiguration,
                $"context.GetHarnessConfig<{configuration}>()");
        }

        if (parameter.IsOptional)
        {
            var fallback = parameter.ExplicitDefaultValue is null
                ? "default"
                : SymbolDisplay.FormatPrimitive(parameter.ExplicitDefaultValue, true, false) ?? "default";
            if (parameter.Type.TypeKind == TypeKind.Enum)
                fallback = $"({type}){fallback}";
            if (parameter.Type.IsReferenceType)
            {
                return new(parameter, ToolHarnessConstructorParameterKind.OptionalService,
                    $"context.GetOptionalService<{type}>() ?? {fallback}");
            }
            return new(parameter, ToolHarnessConstructorParameterKind.Literal, fallback);
        }

        return new(parameter, ToolHarnessConstructorParameterKind.RequiredService,
            $"context.GetRequiredService<{type}>()");
    }
}
