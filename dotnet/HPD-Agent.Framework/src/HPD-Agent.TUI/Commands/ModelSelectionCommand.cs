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

        if (selection.Current is { } current)
        {
            AgentTuiModelSelectionFlow.AppendNotice(
                context,
                "Current model",
                $"{current.ProviderKey} / {current.ModelId}",
                TranscriptSeverity.Info);
        }

        var catalogContext = new AgentTuiModelCatalogContext(context.Scope, context.Shell);
        var provider = await AgentTuiModelSelectionFlow.SelectProviderAsync(
                catalog,
                catalogContext,
                context,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (provider is null)
        {
            return;
        }

        var model = await AgentTuiModelSelectionFlow.SelectModelAsync(
                catalog,
                catalogContext,
                context,
                selection,
                options,
                provider,
                cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);
        if (model is null)
        {
            return;
        }

        await AgentTuiModelSelectionFlow.CommitSelectionAsync(
                selection,
                context,
                model,
                options)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<string> SplitArguments(string arguments)
        => arguments
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

}
