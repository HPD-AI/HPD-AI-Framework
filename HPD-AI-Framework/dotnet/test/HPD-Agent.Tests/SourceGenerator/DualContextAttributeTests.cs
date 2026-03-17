using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HPD.Agent.Tests.SourceGenerator;

/// <summary>
/// Tests for dual-context attribute handling (FunctionResult +SystemPrompt).
/// Ensures the source generator correctly parses and emits both context types.
/// </summary>
public class DualContextAttributeTests
{
    [Fact]
    public void Generator_EmitsBothContexts_WhenBothSpecified()
    {
        // Arrange
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Test Toolkit"",
     
    FunctionResult = ""Toolkit activated with features A, B, C"",
    SystemPrompt = ""Always validate inputs and show your work""
)]
public class TestToolkit
{
    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert - No compilation errors
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Assert - FunctionResult appears in function result
        Assert.Contains("Toolkit activated with features A, B, C", generatedCode!);

        // Assert -SystemPrompt appears in AdditionalProperties
        Assert.Contains("[\"SystemPrompt\"]", generatedCode);
        Assert.Contains("Always validate inputs and show your work", generatedCode);
    }

    [Fact]
    public void Generator_HandlesSystemPromptOnly()
    {
        // Arrange
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Test Toolkit"",
     
    SystemPrompt = ""System-level rules only""
)]
public class TestToolkit
{
    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("[\"SystemPrompt\"]", generatedCode!);
        Assert.Contains("System-level rules only", generatedCode);
    }

    [Fact]
    public void Generator_HandlesFunctionResultOnly()
    {
        // Arrange
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Test Toolkit"",
     
    FunctionResult = ""One-time activation message""
)]
public class TestToolkit
{
    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("One-time activation message", generatedCode!);
        Assert.Contains("[\"FunctionResult\"]", generatedCode);
    }

    [Fact]
    public void Generator_HandlesMultilineSystemPrompt()
    {
        // Arrange
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Financial Toolkit"",
     
    SystemPrompt = @""# RULES
- Rule 1: Validate equations
- Rule 2: Show calculations
- Rule 3: Use decimal precision""
)]
public class FinancialToolkit
{
    [AIFunction]
    public decimal Calculate(decimal x) => x;
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("# RULES", generatedCode!);
        Assert.Contains("Rule 1: Validate equations", generatedCode);
        Assert.Contains("Rule 2: Show calculations", generatedCode);
        Assert.Contains("Rule 3: Use decimal precision", generatedCode);
    }

    [Fact]
    public void Generator_EscapesQuotesInVerbatimStrings()
    {
        // Arrange
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Test Toolkit"",
     
    SystemPrompt = @""Use """"quotes"""" properly""
)]
public class TestToolkit
{
    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        //SystemPrompt should be present
        Assert.Contains("[\"SystemPrompt\"]", generatedCode!);

        // Generated code should contain the quotes properly escaped
        // In the source code: @"Use ""quotes"" properly" becomes the string: Use "quotes" properly
        // When emitted as @"...", quotes are doubled: @"Use ""quotes"" properly"
        Assert.Contains("\"\"quotes\"\"", generatedCode);
    }

    [Fact]
    public void Generator_HandlesSpecialCharactersInContexts()
    {
        // Arrange
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Test Toolkit"",
     
    FunctionResult = ""Uses $, €, ¥ symbols"",
    SystemPrompt = ""Math: x > y, a + b = c""
)]
public class TestToolkit
{
    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("$", generatedCode!);
        Assert.Contains("€", generatedCode);
        Assert.Contains("x > y", generatedCode);
    }

    [Fact]
    public void Generator_HandlesEmptyContexts()
    {
        // Arrange
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Test Toolkit"",
     
    FunctionResult = """",
    SystemPrompt = """"
)]
public class TestToolkit
{
    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // Empty strings should not be added to AdditionalProperties
        // The generator should skip empty/null contexts
    }

    [Fact]
    public void Generator_HandlesPartialClass_PreservesDualContexts()
    {
        // Arrange - This tests the partial class merging fix
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Test Toolkit"",
     
    FunctionResult = ""Toolkit activated"",
    SystemPrompt = ""Toolkit rules""
)]
public partial class TestToolkit
{
    [AIFunction]
    public string Function1() => ""result1"";
}

public partial class TestToolkit
{
    [AIFunction]
    public string Function2() => ""result2"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Both contexts should be preserved after partial class merging
        Assert.Contains("Toolkit activated", generatedCode!);
        Assert.Contains("Toolkit rules", generatedCode);
        Assert.Contains("[\"SystemPrompt\"]", generatedCode);
        Assert.Contains("[\"FunctionResult\"]", generatedCode);
    }

    [Fact]
    public void Generator_HandlesBackwardCompatibility_LegacyPostExpansionInstructions()
    {
        // Arrange - Note: This tests the legacy FunctionResult property with the new [Collapse] attribute
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Legacy Toolkit"",
     
    FunctionResult = ""Legacy instructions""
)]
public class LegacyToolkit
{
    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Legacy postExpansionInstructions should map to FunctionResult
        Assert.Contains("Legacy instructions", generatedCode!);
    }

    [Fact]
    public void Generator_HandlesVeryLongSystemPrompt()
    {
        // Arrange
        var longRules = string.Join("\\n", Enumerable.Range(1, 50).Select(i => $"- Rule {i}"));
        var source = $@"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(
    ""Test Toolkit"",
     
    SystemPrompt = @""{longRules}""
)]
public class TestToolkit
{{
    [AIFunction]
    public string TestFunction() => ""result"";
}}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("Rule 1", generatedCode!);
        Assert.Contains("Rule 25", generatedCode);
        Assert.Contains("Rule 50", generatedCode);
    }

    [Fact]
    public void Generator_HandlesNullContexts()
    {
        // Arrange
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(""Test Toolkit"", Collapsed = true)]
public class TestToolkit
{
    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // When contexts are not specified, they should not appear in AdditionalProperties
        // The container function should still be generated
        Assert.Contains("TestToolkit", generatedCode!);
    }

    [Fact]
    public void Generator_HandlesMixedPositionalAndNamedArguments()
    {
        // Arrange - Test positional args with [Collapse] attribute
        var source = @"
using Microsoft.Extensions.AI;
using HPD.Agent;

[Collapse(""Test Toolkit"",   FunctionResult = ""Positional function context"", SystemPrompt = ""Positional system context"")]
public class TestToolkit
{
    [AIFunction]
    public string TestFunction() => ""result"";
}
";

        // Act
        var (generatedCode, diagnostics) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("Positional function context", generatedCode!);
        Assert.Contains("Positional system context", generatedCode);
    }

    #region Helper Methods

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
        CSharpGeneratorDriver.Create(generator)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedSyntaxTrees = outputCompilation.SyntaxTrees
            .Where(st => st.FilePath.Contains("g.cs"))
            .ToImmutableArray();

        var generatedSourceCode = string.Join("\n\n", generatedSyntaxTrees.Select(st => st.GetText().ToString()));

        return (generatedSourceCode, diagnostics);
    }

    #endregion
}
