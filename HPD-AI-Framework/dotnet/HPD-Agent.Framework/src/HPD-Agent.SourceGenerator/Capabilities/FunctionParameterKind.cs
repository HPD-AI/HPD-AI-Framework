using Microsoft.CodeAnalysis;

namespace HPD.Agent.SourceGenerator.Capabilities;

internal enum FunctionParameterKind
{
    ModelFacing,
    FunctionExecutionContext,
    CancellationToken,
    AIFunctionArguments,
    ServiceProvider,
    Unsupported
}

internal static class FunctionParameterClassifier
{
    public static readonly DiagnosticDescriptor UnsupportedRuntimeParameter = new(
        id: "HPD020",
        title: "Unsupported function runtime parameter",
        messageFormat: "Parameter '{0}' has unsupported runtime type '{1}'. Use FunctionExecutionContext and access sanctioned runtime capabilities through that context.",
        category: "HPD.Agent.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static FunctionParameterKind Classify(ITypeSymbol? type)
    {
        var metadataName = GetMetadataName(type);

        return metadataName switch
        {
            "HPD.Agent.Middleware.FunctionExecutionContext" => FunctionParameterKind.FunctionExecutionContext,
            "System.Threading.CancellationToken" => FunctionParameterKind.CancellationToken,
            "Microsoft.Extensions.AI.AIFunctionArguments" => FunctionParameterKind.AIFunctionArguments,
            "System.IServiceProvider" => FunctionParameterKind.ServiceProvider,

            "HPD.Agent.Middleware.HookContext" => FunctionParameterKind.Unsupported,
            "HPD.Agent.Middleware.AgentContext" => FunctionParameterKind.Unsupported,
            "HPD.Agent.AgentLoopState" => FunctionParameterKind.Unsupported,
            "HPD.Events.IEventCoordinator" => FunctionParameterKind.Unsupported,
            "HPD.Events.IEventFlowRegistry" => FunctionParameterKind.Unsupported,
            "HPD.Agent.Middleware.ToolResultMetadata" => FunctionParameterKind.Unsupported,

            _ => FunctionParameterKind.ModelFacing
        };
    }

    public static bool IsRuntimeParameter(FunctionParameterKind kind) =>
        kind is FunctionParameterKind.FunctionExecutionContext
            or FunctionParameterKind.CancellationToken
            or FunctionParameterKind.AIFunctionArguments
            or FunctionParameterKind.ServiceProvider;

    public static string GetMetadataName(ITypeSymbol? type)
    {
        if (type is null)
        {
            return "object";
        }

        return type
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);
    }
}
