using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Infrastructure;

/// <summary>
/// Helper for creating Collapsed Toolkits and testing container expansion.
/// Provides utilities for building test Toolkits with container functions using HPDAIFunctionFactory.
/// </summary>
public static class CollapsedToolkitTestHelper
{
    /// <summary>
    /// Creates a container function for a Collapsed Toolkit.
    /// This simulates what the source generator creates for [Collapse] attributes.
    /// </summary>
    public static AIFunction CreateContainerFunction(
        string toolName,
        string description,
        IReadOnlyList<AIFunction> containedFunctions,
        string? postExpansionInstructions = null)
    {
        var functionNames = containedFunctions.Select(f => f.Name ?? "").ToArray();
        var functionCount = containedFunctions.Count;

        var options = new HPDAIFunctionFactoryOptions
        {
            Name = toolName,
            Description = description,
            AdditionalProperties = new Dictionary<string, object?>
            {
                ["IsContainer"] = true,
                ["ToolkitName"] = toolName,
                ["FunctionNames"] = functionNames,
                ["FunctionCount"] = functionCount,
                ["SourceType"] = "CSharp"
            }
        };

        return HPDAIFunctionFactory.Create(
            async (args, ct) =>
            {
                var funcs = string.Join(", ", functionNames);
                var instructions = postExpansionInstructions ?? "";
                return $"Toolkit '{toolName}' expanded. Available functions: {funcs}. {instructions}";
            },
            options);
    }

    /// <summary>
    /// Creates a function that belongs to a Collapsed Toolkit.
    /// Adds the necessary metadata to mark it as a Toolkit member.
    /// </summary>
    public static AIFunction CreateToolkitMemberFunction(
        string name,
        string description,
        Func<AIFunctionArguments, CancellationToken, Task<object?>> invocation,
        string toolName)
    {
        var options = new HPDAIFunctionFactoryOptions
        {
            Name = name,
            Description = description,
            AdditionalProperties = new Dictionary<string, object?>
            {
                ["ParentToolkit"] = toolName,
                ["ToolkitName"] = toolName
            }
        };

        return HPDAIFunctionFactory.Create(invocation, options);
    }

    /// <summary>
    /// Creates a complete Collapsed Toolkit with container and member functions.
    /// Returns (containerFunction, memberFunctions[]).
    /// </summary>
    public static (AIFunction Container, AIFunction[] Members) CreateCollapsedToolkit(
        string toolName,
        string description,
        params (string name, string desc, Func<AIFunctionArguments, CancellationToken, Task<object?>> func)[] functions)
    {
        // Create member functions
        var memberFunctions = functions.Select(f =>
            CreateToolkitMemberFunction(f.name, f.desc, f.func, toolName)
        ).ToArray();

        // Create container
        var container = CreateContainerFunction(toolName, description, memberFunctions);

        return (container, memberFunctions);
    }

    /// <summary>
    /// Helper to create simple member functions from synchronous delegates.
    /// </summary>
    public static (string name, string desc, Func<AIFunctionArguments, CancellationToken, Task<object?>> func)
        MemberFunc(string name, string description, Func<object?> syncFunc)
    {
        return (name, description, (args, ct) => Task.FromResult(syncFunc()));
    }

    /// <summary>
    /// Extracts tool names from a list of AIFunctions.
    /// Useful for assertions in container expansion tests.
    /// </summary>
    public static List<string> GetToolNames(IEnumerable<AIFunction> tools)
    {
        return tools.Select(t => t.Name ?? "").ToList();
    }

    /// <summary>
    /// Checks if a function is a container function.
    /// </summary>
    public static bool IsContainer(AIFunction function)
    {
        return function.AdditionalProperties?.TryGetValue("IsContainer", out var value) == true
            && value is bool isContainer
            && isContainer;
    }

    /// <summary>
    /// Gets the Toolkit name from a function's metadata.
    /// </summary>
    public static string? GetToolkitName(AIFunction function)
    {
        if (function.AdditionalProperties?.TryGetValue("ToolkitName", out var value) == true)
        {
            return value as string;
        }
        return null;
    }

    /// <summary>
    /// Creates a non-Collapsed function (regular function without Toolkit Collapsing).
    /// </summary>
    public static AIFunction CreateNonCollapsedFunction(
        string name,
        string description,
        Func<AIFunctionArguments, CancellationToken, Task<object?>> invocation)
    {
        var options = new HPDAIFunctionFactoryOptions
        {
            Name = name,
            Description = description
        };

        return HPDAIFunctionFactory.Create(invocation, options);
    }

    /// <summary>
    /// Helper to create simple functions from synchronous delegates.
    /// </summary>
    public static AIFunction CreateSimpleFunction(string name, string description, Func<object?> syncFunc)
    {
        return CreateNonCollapsedFunction(name, description, (args, ct) => Task.FromResult(syncFunc()));
    }

    /// <summary>
    /// Common test functions for quick test setup.
    /// </summary>
    public static class CommonFunctions
    {
        public static AIFunction Add() =>
            CreateSimpleFunction("Add", "Adds two numbers", () => "Result: sum");

        public static AIFunction Multiply() =>
            CreateSimpleFunction("Multiply", "Multiplies two numbers", () => "Result: product");

        public static AIFunction Subtract() =>
            CreateSimpleFunction("Subtract", "Subtracts two numbers", () => "Result: difference");

        public static AIFunction Echo() =>
            CreateSimpleFunction("Echo", "Echoes input text", () => "Echo result");

        public static AIFunction GetTime() =>
            CreateSimpleFunction("GetTime", "Gets current time", () => DateTime.UtcNow.ToString("O"));
    }
}

/// <summary>
/// Extension methods for working with Collapsed Toolkits in tests.
/// </summary>
public static class CollapsedToolkitExtensions
{
    /// <summary>
    /// Converts a list of AIFunctions to their names for easy assertion.
    /// </summary>
    public static List<string> ToNames(this IEnumerable<AIFunction> functions)
    {
        return functions.Select(f => f.Name ?? "").ToList();
    }

    /// <summary>
    /// Filters functions by whether they're containers.
    /// </summary>
    public static IEnumerable<AIFunction> OnlyContainers(this IEnumerable<AIFunction> functions)
    {
        return functions.Where(CollapsedToolkitTestHelper.IsContainer);
    }

    /// <summary>
    /// Filters functions by whether they're NOT containers.
    /// </summary>
    public static IEnumerable<AIFunction> ExcludeContainers(this IEnumerable<AIFunction> functions)
    {
        return functions.Where(f => !CollapsedToolkitTestHelper.IsContainer(f));
    }

    /// <summary>
    /// Filters functions by Toolkit name.
    /// </summary>
    public static IEnumerable<AIFunction> FromToolkit(this IEnumerable<AIFunction> functions, string toolName)
    {
        return functions.Where(f => CollapsedToolkitTestHelper.GetToolkitName(f) == toolName);
    }
}
