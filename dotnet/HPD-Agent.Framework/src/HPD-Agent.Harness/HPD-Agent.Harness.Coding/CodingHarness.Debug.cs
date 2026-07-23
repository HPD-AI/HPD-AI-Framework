using System.ComponentModel;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding.Debugging;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

public partial class CodingToolHarness
{
    /// <summary>
    /// Starts, controls, and inspects owned debugger session trees through one closed operation.
    /// </summary>
    [AIFunction]
    [RequiresPermission]
    [Description(
        "Starts, controls, and inspects debugger sessions. Select exactly one closed operation " +
        "using the request action discriminator. Launch and attach return a debugTreeId used by later operations.")]
    public Task<string> Debug(
        [Description("The debugger operation selected by its action discriminator.")]
        DebugOperation request,
        FunctionExecutionContext context = null!,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var dispatcher = context.Services?.GetService<DebugOperationDispatcher>();
        return dispatcher is null
            ? Task.FromResult(
                "<error tool=\"Debug\" action=\"unknown\" success=\"false\" kind=\"debug_not_configured\">" +
                "The model-facing debugger dispatcher is not configured.</error>")
            : dispatcher.ExecuteAsync(request, context, cancellationToken);
    }
}
