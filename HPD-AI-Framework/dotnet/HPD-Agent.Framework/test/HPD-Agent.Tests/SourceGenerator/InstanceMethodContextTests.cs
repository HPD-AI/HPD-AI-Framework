using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HPD.Agent.Tests.SourceGenerator;

/// <summary>
/// Tests for instance method and property support in dual-context instruction injection.
/// Verifies that the source generator correctly detects static vs instance members
/// and generates appropriate code (instance.Method() vs StaticClass.Method()).
/// </summary>
public class InstanceMethodContextTests
{
    private static (string? generatedCode, ImmutableArray<Diagnostic> diagnostics) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.RuntimeHelpers).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.AI.AIFunction).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(CollapseAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new global::HPDToolSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedSyntaxTrees = outputCompilation.SyntaxTrees
            .Where(st => st.FilePath.Contains("g.cs"))
            .ToImmutableArray();

        var generatedSourceCode = string.Join("\n\n", generatedSyntaxTrees.Select(st => st.GetText().ToString()));

        return (generatedSourceCode, diagnostics);
    }

    [Fact]
    public void Generator_SupportsInstanceMethod_InFunctionResult()
    {
        // Arrange - Using an expression (method call) as attribute value
        // The source generator detects this as an expression rather than a literal string
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Dynamic Harness"",
     
    FunctionResult = GetActivationMessage()
)]
public class DynamicHarness
{
    private int _version = 1;

    public string GetActivationMessage()
    {
        return $""Harness v{_version} activated"";
    }

    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Should generate instance.GetActivationMessage() since it's an instance method
        Assert.Contains("instance.GetActivationMessage()", generatedCode!);
        Assert.DoesNotContain("DynamicHarness.GetActivationMessage()", generatedCode);
    }

    [Fact]
    public void Generator_SupportsStaticMethod_InFunctionResult()
    {
        // Arrange - Static method expression
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Static Harness"",
     
    FunctionResult = GetStaticMessage()
)]
public class StaticHarness
{
    public static string GetStaticMessage()
    {
        return ""Static activation message"";
    }

    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Should NOT prepend instance. for static methods
        Assert.Contains("GetStaticMessage()", generatedCode!);
        Assert.DoesNotContain("instance.GetStaticMessage()", generatedCode);
    }

    [Fact]
    public void Generator_SupportsInstanceProperty_InSystemPrompt()
    {
        // Arrange - Instance property expression
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Property Harness"",
     
    SystemPrompt = Rules
)]
public class PropertyHarness
{
    public string Rules => ""Instance-specific rules"";

    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Should generate instance.Rules since it's an instance property
        Assert.Contains("instance.Rules", generatedCode!);
    }

    [Fact]
    public void Generator_SupportsStaticProperty_InSystemPrompt()
    {
        // Arrange - Static property expression
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Static Property Harness"",
     
    SystemPrompt = StaticRules
)]
public class StaticPropertyHarness
{
    public static string StaticRules => ""Static rules for all instances"";

    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Should NOT prepend instance. for static properties
        Assert.Contains("StaticRules", generatedCode!);
        Assert.DoesNotContain("instance.StaticRules", generatedCode);
    }

    [Fact]
    public void Generator_SupportsMixedStaticAndInstance_InBothContexts()
    {
        // Arrange - Mixed static and instance expressions
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Mixed Harness"",
     
    FunctionResult = GetInstanceMessage(),
    SystemPrompt = StaticRules
)]
public class MixedHarness
{
    private string _name = ""MixedHarness"";

    public string GetInstanceMessage()
    {
        return $""{_name} activated"";
    }

    public static string StaticRules => ""Global rules"";

    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // FunctionResult should use instance method
        Assert.Contains("instance.GetInstanceMessage()", generatedCode!);

        //SystemPrompt should use static property
        Assert.Contains("[\"SystemPrompt\"] = StaticRules", generatedCode);
        Assert.DoesNotContain("instance.StaticRules", generatedCode);
    }

    [Fact]
    public void Generator_SupportsExternalStaticClass_InFunctionResult()
    {
        // Arrange - External static class method expression
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

public static class MessageBuilder
{
    public static string BuildMessage() => ""External static message"";
}

[Collapse(
    ""External Harness"",
     
    FunctionResult = MessageBuilder.BuildMessage()
)]
public class ExternalHarness
{
    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Should keep MessageBuilder.BuildMessage() as-is (external static class)
        Assert.Contains("MessageBuilder.BuildMessage()", generatedCode!);
        Assert.DoesNotContain("instance.MessageBuilder", generatedCode);
    }

    [Fact]
    public void Generator_SupportsComplexInstanceMethod_WithMultipleParameters()
    {
        // Arrange - Complex instance method expression
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Complex Harness"",
     
    FunctionResult = BuildActivationMessage()
)]
public class ComplexHarness
{
    private readonly string _environment;
    private readonly int _version;

    public ComplexHarness()
    {
        _environment = ""Production"";
        _version = 2;
    }

    public string BuildActivationMessage()
    {
        return $""Harness v{_version} in {_environment} activated"";
    }

    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Should generate instance.BuildActivationMessage()
        Assert.Contains("instance.BuildActivationMessage()", generatedCode!);
    }

    [Fact]
    public void Generator_SupportsInstanceMethodWithChaining()
    {
        // Arrange - Method chaining expression
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Chained Harness"",
     
    SystemPrompt = GetRules().Trim()
)]
public class ChainedHarness
{
    public string GetRules()
    {
        return ""  Rules with spaces  "";
    }

    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Should detect GetRules() as instance method and prepend instance.
        Assert.Contains("instance.GetRules().Trim()", generatedCode!);
    }

    [Fact]
    public void Generator_HandlesBackwardCompatibility_WithDeprecatedPostExpansionInstructions()
    {
        // Arrange - Instance method expression (legacy equivalent)
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Legacy Harness"",
     
    FunctionResult = GetLegacyInstructions()
)]
public class LegacyHarness
{
    public string GetLegacyInstructions()
    {
        return ""Legacy instructions"";
    }

    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // postExpansionInstructions maps to FunctionResult, so should use instance method
        Assert.Contains("instance.GetLegacyInstructions()", generatedCode!);
    }

    [Fact]
    public void Generator_DoesNotPrependInstance_ForLiteralStrings()
    {
        // Arrange
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Literal Harness"",
     
    FunctionResult = ""Literal activation message"",
    SystemPrompt = ""Literal rules""
)]
public class LiteralHarness
{
    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Should NOT have any instance. prefix for literal strings
        Assert.DoesNotContain("instance.\"", generatedCode!);

        // Should have verbatim string literals
        Assert.Contains("@\"Literal activation message\"", generatedCode);
        Assert.Contains("@\"Literal rules\"", generatedCode);
    }

    [Fact]
    public void Generator_SupportsInstanceMethod_ReturningDynamicContent()
    {
        // Arrange - Instance method returning dynamic content
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;
using System;

[Collapse(
    ""Time-based Harness"",
     
    FunctionResult = GetTimeBasedMessage()
)]
public class TimeBasedHarness
{
    private DateTime _createdAt = DateTime.UtcNow;

    public string GetTimeBasedMessage()
    {
        return $""Harness created at {_createdAt:yyyy-MM-dd HH:mm:ss} UTC"";
    }

    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Should use instance method
        Assert.Contains("instance.GetTimeBasedMessage()", generatedCode!);
    }

    [Fact]
    public void Generator_GeneratesCorrectMetadata_ForInstanceContexts()
    {
        // Arrange - Instance method expression in SystemPrompt
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Metadata Harness"",
     
    SystemPrompt = GetDynamicRules()
)]
public class MetadataHarness
{
    public string GetDynamicRules() => ""Dynamic rules"";

    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Should storeSystemPrompt in AdditionalProperties with instance. prefix
        Assert.Contains("[\"SystemPrompt\"] = instance.GetDynamicRules()", generatedCode!);
    }
}
