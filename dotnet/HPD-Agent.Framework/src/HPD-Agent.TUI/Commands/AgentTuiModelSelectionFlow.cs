using HPD.Agent.TUI.Models;
using HPD.TUI.Components;

namespace HPD.Agent.TUI.Commands;

public static class AgentTuiModelSelectionFlow
{
    private const int InitialModelChoiceLimit = 28;
    private const int SearchModelChoiceLimit = 30;

    public static async ValueTask<AgentTuiProviderChoice?> SelectProviderAsync(
        IAgentTuiModelCatalog catalog,
        AgentTuiModelCatalogContext catalogContext,
        AgentTuiCommandContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(catalogContext);
        ArgumentNullException.ThrowIfNull(context);

        var providers = await catalog.GetProvidersAsync(catalogContext, cancellationToken)
            .ConfigureAwait(false);
        var connected = providers
            .Where(static provider => provider.IsRegistered && provider.IsAuthenticated && !provider.IsExpired)
            .OrderBy(static provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (connected.Length == 0)
        {
            AppendNotice(
                context,
                "No providers connected",
                "Register and authenticate a provider before choosing a model.",
                TranscriptSeverity.Warning);
            return null;
        }

        return connected.Length == 1
            ? connected[0]
            : await context.Dialogs.SelectAsync(
                    "Select provider",
                    connected,
                    static candidate => candidate.DisplayName,
                    cancellationToken)
                .ConfigureAwait(false);
    }

    public static async ValueTask<AgentTuiSelectedModel?> SelectModelAsync(
        IAgentTuiModelCatalog catalog,
        AgentTuiModelCatalogContext catalogContext,
        AgentTuiCommandContext context,
        AgentTuiModelSelectionState selection,
        AgentTuiModelSelectionOptions options,
        AgentTuiProviderChoice provider,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(catalogContext);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(provider);

        var models = await catalog.GetModelsAsync(
                catalogContext,
                provider.ProviderKey,
                new AgentTuiModelQuery(),
                cancellationToken)
            .ConfigureAwait(false);
        var selectableModels = ApplyModelPolicy(models, options).ToArray();

        var initialModels = GetInitialModels(provider, selectableModels);
        var choices = BuildModelChoices(provider, initialModels, selection, options, selectableModels.Length > initialModels.Count);
        if (choices.Count == 0)
        {
            return await ReadManualModelAsync(context, provider.ProviderKey, cancellationToken)
                .ConfigureAwait(false);
        }

        var selected = await context.Dialogs.SelectAsync(
                title ?? "Select model",
                choices,
                static choice => choice.Label,
                cancellationToken)
            .ConfigureAwait(false);

        if (selected is null)
        {
            return null;
        }

        return selected.Kind switch
        {
            ModelChoiceKind.Model => selected.Model is null ? null : CreateSelectedModel(selected.Model),
            ModelChoiceKind.Recent => await SelectRecentModelAsync(context, selection, options, provider, cancellationToken)
                .ConfigureAwait(false),
            ModelChoiceKind.Manual => await ReadManualModelAsync(context, provider.ProviderKey, cancellationToken)
                .ConfigureAwait(false),
            ModelChoiceKind.SearchAll => await SearchModelsAsync(
                    catalog,
                    catalogContext,
                    context,
                    provider,
                    options,
                    freeOnly: false,
                    cancellationToken)
                .ConfigureAwait(false),
            ModelChoiceKind.SearchFree => await SearchModelsAsync(
                    catalog,
                    catalogContext,
                    context,
                    provider,
                    options,
                    freeOnly: true,
                    cancellationToken)
                .ConfigureAwait(false),
            _ => null
        };
    }

    public static async ValueTask<AgentTuiSelectedModel?> CommitSelectionAsync(
        AgentTuiModelSelectionState selection,
        AgentTuiCommandContext context,
        AgentTuiSelectedModel model,
        AgentTuiModelSelectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);

        var committed = options.ConfigureSelection is null
            ? model
            : await options.ConfigureSelection(context, model).ConfigureAwait(false);
        if (committed is null)
        {
            return null;
        }

        selection.Set(committed);
        AppendNotice(
            context,
            "Model selected",
            $"{committed.ProviderKey} / {committed.ModelId}",
            TranscriptSeverity.Info);
        if (options.SelectionCommitted is not null)
        {
            await options.SelectionCommitted(context, committed).ConfigureAwait(false);
        }

        return committed;
    }

    public static async ValueTask<AgentTuiSelectedModel?> CommitSelectionAsync(
        AgentTuiModelSelectionState selection,
        AgentTuiCommandContext context,
        string providerKey,
        string modelId,
        AgentTuiModelSelectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(context);

        return await CommitSelectionAsync(
                selection,
                context,
                new AgentTuiSelectedModel(
                    providerKey,
                    modelId,
                    Capabilities: AgentTuiModelCapabilities.None),
                options)
            .ConfigureAwait(false);
    }

    public static void AppendNotice(
        AgentTuiCommandContext context,
        string title,
        string body,
        TranscriptSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Shell.Transcript.AddFinal(new TranscriptEntry(
            Id: $"model-command-{Guid.NewGuid():N}",
            EntryKey: null,
            Cell: new NoticeCell(title, new Text(body), severity),
            Metadata: new TranscriptEntryMetadata(
                AgentId: context.Scope.AgentId,
                AgentName: "tui",
                AgentChain: ["tui"])));
    }

    private static async ValueTask<AgentTuiSelectedModel?> SelectRecentModelAsync(
        AgentTuiCommandContext context,
        AgentTuiModelSelectionState selection,
        AgentTuiModelSelectionOptions options,
        AgentTuiProviderChoice provider,
        CancellationToken cancellationToken)
    {
        var recent = selection.Recent
            .Where(model => string.Equals(model.ProviderKey, provider.ProviderKey, StringComparison.OrdinalIgnoreCase))
            .Where(model => !options.RequireToolSupport || model.Capabilities?.SupportsTools == true)
            .ToArray();
        if (recent.Length == 0)
        {
            return null;
        }

        return await context.Dialogs.SelectAsync(
                "Recent models",
                recent,
                FormatModel,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<AgentTuiSelectedModel?> SearchModelsAsync(
        IAgentTuiModelCatalog catalog,
        AgentTuiModelCatalogContext catalogContext,
        AgentTuiCommandContext context,
        AgentTuiProviderChoice provider,
        AgentTuiModelSelectionOptions options,
        bool freeOnly,
        CancellationToken cancellationToken)
    {
        var search = await context.Dialogs.InputAsync(
                freeOnly ? "Search free models" : "Search models",
                allowEmpty: false,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var models = await catalog.GetModelsAsync(
                catalogContext,
                provider.ProviderKey,
                new AgentTuiModelQuery(
                    search.Trim(),
                    Live: true,
                    FreeOnly: freeOnly),
                cancellationToken)
            .ConfigureAwait(false);

        var choices = ApplyModelPolicy(models, options)
            .OrderByDescending(static model => model.IsRecommended)
            .ThenBy(static model => model.IsFree ? 0 : 1)
            .ThenBy(static model => model.DisplayName ?? model.ModelId, StringComparer.OrdinalIgnoreCase)
            .Take(SearchModelChoiceLimit)
            .ToArray();
        if (choices.Length == 0)
        {
            AppendNotice(
                context,
                options.RequireToolSupport ? "No tool-capable models found" : "No models found",
                options.RequireToolSupport
                    ? "Enter a model ID manually if you already know it supports tool calls."
                    : "Enter a model ID manually if you already know it.",
                TranscriptSeverity.Warning);
            return await ReadManualModelAsync(context, provider.ProviderKey, cancellationToken)
                .ConfigureAwait(false);
        }

        var selected = await context.Dialogs.SelectAsync(
                freeOnly ? "Select free model" : "Select model",
                choices,
                FormatModel,
                cancellationToken)
            .ConfigureAwait(false);
        return selected is null ? null : CreateSelectedModel(selected);
    }

    private static async ValueTask<AgentTuiSelectedModel?> ReadManualModelAsync(
        AgentTuiCommandContext context,
        string providerKey,
        CancellationToken cancellationToken)
    {
        var modelId = await context.Dialogs.InputAsync(
                "Enter model ID",
                allowEmpty: false,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(modelId)
            ? null
            : new AgentTuiSelectedModel(
                providerKey,
                modelId.Trim(),
                Capabilities: AgentTuiModelCapabilities.None);
    }

    private static List<ModelDialogChoice> BuildModelChoices(
        AgentTuiProviderChoice provider,
        IReadOnlyList<AgentTuiModelChoice> models,
        AgentTuiModelSelectionState selection,
        AgentTuiModelSelectionOptions options,
        bool hasMoreModels)
    {
        var choices = models
            .Select(static model => ModelDialogChoice.ForModel(model))
            .ToList();

        if (selection.Recent.Any(model =>
                string.Equals(model.ProviderKey, provider.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
                (!options.RequireToolSupport || model.Capabilities?.SupportsTools == true)))
        {
            choices.Add(new ModelDialogChoice(ModelChoiceKind.Recent, "Recent models", null));
        }

        if (provider.SupportsLiveModelSearch)
        {
            if (provider.SupportsFreeModels)
            {
                choices.Add(new ModelDialogChoice(ModelChoiceKind.SearchFree, "Search free models", null));
            }

            choices.Add(new ModelDialogChoice(
                ModelChoiceKind.SearchAll,
                hasMoreModels ? "Search more models" : "Search models",
                null));
        }

        choices.Add(new ModelDialogChoice(ModelChoiceKind.Manual, "Enter model ID manually", null));
        return choices;
    }

    private static IReadOnlyList<AgentTuiModelChoice> GetInitialModels(
        AgentTuiProviderChoice provider,
        IReadOnlyList<AgentTuiModelChoice> models)
    {
        if (!provider.SupportsLiveModelSearch)
        {
            return models.Take(InitialModelChoiceLimit).ToArray();
        }

        var recommended = models
            .Where(static model => model.IsRecommended)
            .Take(InitialModelChoiceLimit)
            .ToArray();

        return recommended.Length > 0
            ? recommended
            : models.Take(Math.Min(InitialModelChoiceLimit, 5)).ToArray();
    }

    private static IEnumerable<AgentTuiModelChoice> ApplyModelPolicy(
        IEnumerable<AgentTuiModelChoice> models,
        AgentTuiModelSelectionOptions options)
        => options.RequireToolSupport
            ? models.Where(static model => model.Capabilities?.SupportsTools == true)
            : models;

    private static string FormatModel(AgentTuiModelChoice model)
    {
        var label = string.IsNullOrWhiteSpace(model.DisplayName)
            ? model.ModelId
            : $"{model.DisplayName} ({model.ModelId})";
        if (model.IsRecommended)
        {
            label += " recommended";
        }

        if (model.IsFree)
        {
            label += " free";
        }

        return label;
    }

    private static AgentTuiSelectedModel CreateSelectedModel(AgentTuiModelChoice model)
        => new(
            model.ProviderKey,
            model.ModelId,
            model.DisplayName,
            model.Capabilities ?? AgentTuiModelCapabilities.None);

    private static string FormatModel(AgentTuiSelectedModel model)
    {
        var label = string.IsNullOrWhiteSpace(model.DisplayName)
            ? model.ModelId
            : $"{model.DisplayName} ({model.ModelId})";

        if (model.Chat?.Reasoning?.Effort is { } effort)
        {
            label += $" reasoning {effort.ToString().ToLowerInvariant()}";
        }

        return label;
    }

    private enum ModelChoiceKind
    {
        Model,
        Recent,
        SearchAll,
        SearchFree,
        Manual
    }

    private sealed record ModelDialogChoice(
        ModelChoiceKind Kind,
        string Label,
        AgentTuiModelChoice? Model)
    {
        public static ModelDialogChoice ForModel(AgentTuiModelChoice model)
            => new(ModelChoiceKind.Model, FormatModel(model), model);
    }
}
