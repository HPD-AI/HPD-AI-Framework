using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;

namespace HPD.Agent.TUI.Commands;

internal static class ModelSelectionCommand
{
    public static HpdAgentTuiCommandDescriptor Create(
        IAgentTuiModelCatalog catalog,
        AgentTuiModelSelectionState selection,
        string commandName,
        AgentTuiModelSelectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentNullException.ThrowIfNull(options);

        return new HpdAgentTuiCommandDescriptor(commandName, context =>
            ExecuteAsync(catalog, selection, options, context))
        {
            Title = $"/{commandName}",
            Description = "Choose the provider/model for future prompts.",
            Order = options.Order
        };
    }

    private static async ValueTask ExecuteAsync(
        IAgentTuiModelCatalog catalog,
        AgentTuiModelSelectionState selection,
        AgentTuiModelSelectionOptions options,
        AgentTuiCommandContext context)
    {
        var arguments = SplitArguments(context.Arguments);
        if (arguments.Count >= 2)
        {
            await AgentTuiModelSelectionFlow.CommitSelectionAsync(
                    selection,
                    context,
                    arguments[0],
                    arguments[1],
                    options)
                .ConfigureAwait(false);
            return;
        }

        var catalogContext = new AgentTuiModelCatalogContext(context.Scope, context.Shell);

        await context.Dialogs.RunFlowAsync<object?>(
                (flow, cancellationToken) => RunModelSelectionFlowAsync(
                    catalog,
                    catalogContext,
                    context,
                    selection,
                    options,
                    flow,
                    cancellationToken),
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async ValueTask<object?> RunModelSelectionFlowAsync(
        IAgentTuiModelCatalog catalog,
        AgentTuiModelCatalogContext catalogContext,
        AgentTuiCommandContext context,
        AgentTuiModelSelectionState selection,
        AgentTuiModelSelectionOptions options,
        AgentTuiDialogFlowContext flow,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var providerWasImplicit = await AgentTuiModelSelectionFlow.HasSingleConnectedProviderAsync(
                    catalog,
                    catalogContext,
                    cancellationToken)
                .ConfigureAwait(false);
            var providerStep = await AgentTuiModelSelectionFlow.SelectProviderAsync(
                    catalog,
                    catalogContext,
                    context,
                    flow,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!providerStep.IsSubmitted || providerStep.Value is null)
            {
                return null;
            }

            while (true)
            {
                var modelStep = await AgentTuiModelSelectionFlow.SelectModelAsync(
                        catalog,
                        catalogContext,
                        context,
                        flow,
                        selection,
                        options,
                        providerStep.Value,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (modelStep.IsBack)
                {
                    if (providerWasImplicit)
                    {
                        return null;
                    }

                    break;
                }

                if (!modelStep.IsSubmitted || modelStep.Value is null)
                {
                    return null;
                }

                if (options.ConfigureSelection is null)
                {
                    await AgentTuiModelSelectionFlow.CommitSelectionAsync(
                            selection,
                            context,
                            modelStep.Value,
                            options)
                        .ConfigureAwait(false);
                    return null;
                }

                var configured = await options.ConfigureSelection(
                        context,
                        modelStep.Value)
                    .ConfigureAwait(false);
                if (configured is not null)
                {
                    await AgentTuiModelSelectionFlow.CommitSelectionAsync(
                            selection,
                            context,
                            configured,
                            options)
                        .ConfigureAwait(false);
                    return null;
                }
            }
        }
    }

    private static IReadOnlyList<string> SplitArguments(string arguments)
        => arguments
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

}
