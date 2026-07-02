using HPD.Agent.TUI.Composition;
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

        var provider = await SelectProviderAsync(
                catalog,
                catalogContext,
                context,
                new AgentTuiDialogFlowContext(context.Dialogs),
                cancellationToken)
            .ConfigureAwait(false);

        return provider.Status == AgentTuiDialogStepStatus.Submitted
            ? provider.Value
            : null;
    }

    public static async ValueTask<bool> HasSingleConnectedProviderAsync(
        IAgentTuiModelCatalog catalog,
        AgentTuiModelCatalogContext catalogContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(catalogContext);

        var providers = await catalog.GetProvidersAsync(catalogContext, cancellationToken)
            .ConfigureAwait(false);
        return providers.Count(static provider =>
            provider.IsRegistered &&
            provider.IsAuthenticated &&
            !provider.IsExpired) == 1;
    }

    public static async ValueTask<AgentTuiDialogStepResult<AgentTuiProviderChoice>> SelectProviderAsync(
        IAgentTuiModelCatalog catalog,
        AgentTuiModelCatalogContext catalogContext,
        AgentTuiCommandContext context,
        AgentTuiDialogFlowContext dialogs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(catalogContext);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dialogs);

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
            return AgentTuiDialogStepResult<AgentTuiProviderChoice>.Canceled();
        }

        if (connected.Length == 1)
        {
            return AgentTuiDialogStepResult<AgentTuiProviderChoice>.Submitted(connected[0]);
        }

        var selected = await dialogs.SelectAsync(
                "Select provider",
                connected,
                static candidate => candidate.DisplayName,
                cancellationToken)
            .ConfigureAwait(false);

        return selected.IsSubmitted
            ? AgentTuiDialogStepResult<AgentTuiProviderChoice>.Submitted(selected.Value!)
            : selected.IsBack
                ? AgentTuiDialogStepResult<AgentTuiProviderChoice>.Back()
                : AgentTuiDialogStepResult<AgentTuiProviderChoice>.Canceled();
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

        var selected = await SelectModelAsync(
                catalog,
                catalogContext,
                context,
                new AgentTuiDialogFlowContext(context.Dialogs),
                selection,
                options,
                provider,
                title,
                cancellationToken)
            .ConfigureAwait(false);

        return selected.Status == AgentTuiDialogStepStatus.Submitted
            ? selected.Value
            : null;
    }

    public static async ValueTask<AgentTuiDialogStepResult<AgentTuiSelectedModel>> SelectModelAsync(
        IAgentTuiModelCatalog catalog,
        AgentTuiModelCatalogContext catalogContext,
        AgentTuiCommandContext context,
        AgentTuiDialogFlowContext dialogs,
        AgentTuiModelSelectionState selection,
        AgentTuiModelSelectionOptions options,
        AgentTuiProviderChoice provider,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(catalogContext);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dialogs);
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
            return await ReadManualModelAsync(context, dialogs, provider.ProviderKey, cancellationToken)
                .ConfigureAwait(false);
        }

        var selected = await dialogs.SelectAsync(
                title ?? "Select model",
                choices,
                static choice => choice.Label,
                cancellationToken)
            .ConfigureAwait(false);

        if (selected.IsBack)
        {
            return AgentTuiDialogStepResult<AgentTuiSelectedModel>.Back();
        }

        if (selected.IsCanceled)
        {
            return AgentTuiDialogStepResult<AgentTuiSelectedModel>.Canceled();
        }

        if (!selected.IsSubmitted || selected.Value is null)
        {
            return AgentTuiDialogStepResult<AgentTuiSelectedModel>.Canceled();
        }

        return selected.Value.Kind switch
        {
            ModelChoiceKind.Model => selected.Value.Model is null
                ? AgentTuiDialogStepResult<AgentTuiSelectedModel>.Canceled()
                : AgentTuiDialogStepResult<AgentTuiSelectedModel>.Submitted(CreateSelectedModel(selected.Value.Model)),
            ModelChoiceKind.Recent => await SelectRecentModelAsync(context, dialogs, selection, options, provider, cancellationToken)
                .ConfigureAwait(false),
            ModelChoiceKind.Manual => await ReadManualModelAsync(context, dialogs, provider.ProviderKey, cancellationToken)
                .ConfigureAwait(false),
            ModelChoiceKind.SearchAll => await SearchModelsAsync(context, dialogs, catalog, catalogContext, provider, options, freeOnly: false, cancellationToken)
                .ConfigureAwait(false),
            ModelChoiceKind.SearchFree => await SearchModelsAsync(context, dialogs, catalog, catalogContext, provider, options, freeOnly: true, cancellationToken)
                .ConfigureAwait(false),
            _ => AgentTuiDialogStepResult<AgentTuiSelectedModel>.Canceled()
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

    private static async ValueTask<AgentTuiDialogStepResult<AgentTuiSelectedModel>> SelectRecentModelAsync(
        AgentTuiCommandContext context,
        AgentTuiDialogFlowContext dialogs,
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
            return AgentTuiDialogStepResult<AgentTuiSelectedModel>.Canceled();
        }

        var selected = await dialogs.SelectAsync(
                "Recent models",
                recent,
                FormatModel,
                cancellationToken)
            .ConfigureAwait(false);

        return selected.IsSubmitted
            ? AgentTuiDialogStepResult<AgentTuiSelectedModel>.Submitted(selected.Value!)
            : AgentTuiDialogStepResult<AgentTuiSelectedModel>.Back();
    }

    private static async ValueTask<AgentTuiDialogStepResult<AgentTuiSelectedModel>> SearchModelsAsync(
        AgentTuiCommandContext context,
        AgentTuiDialogFlowContext dialogs,
        IAgentTuiModelCatalog catalog,
        AgentTuiModelCatalogContext catalogContext,
        AgentTuiProviderChoice provider,
        AgentTuiModelSelectionOptions options,
        bool freeOnly,
        CancellationToken cancellationToken)
    {
        var search = await dialogs.InputAsync(
                freeOnly ? "Search free models" : "Search models",
                allowEmpty: false,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (search.IsBack)
        {
            return AgentTuiDialogStepResult<AgentTuiSelectedModel>.Back();
        }

        if (string.IsNullOrWhiteSpace(search.Value))
        {
            return AgentTuiDialogStepResult<AgentTuiSelectedModel>.Canceled();
        }

        var models = await catalog.GetModelsAsync(
                catalogContext,
                provider.ProviderKey,
                new AgentTuiModelQuery(
                    search.Value.Trim(),
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

            return await ReadManualModelAsync(context, dialogs, provider.ProviderKey, cancellationToken)
                .ConfigureAwait(false);
        }

        var selected = await dialogs.SelectAsync(
                freeOnly ? "Select free model" : "Select model",
                choices,
                FormatModel,
                cancellationToken)
            .ConfigureAwait(false);

        if (selected.IsSubmitted && selected.Value is not null)
        {
            return AgentTuiDialogStepResult<AgentTuiSelectedModel>.Submitted(CreateSelectedModel(selected.Value!));
        }

        return selected.IsBack
            ? AgentTuiDialogStepResult<AgentTuiSelectedModel>.Back()
            : AgentTuiDialogStepResult<AgentTuiSelectedModel>.Canceled();
    }

    private static async ValueTask<AgentTuiDialogStepResult<AgentTuiSelectedModel>> ReadManualModelAsync(
        AgentTuiCommandContext context,
        AgentTuiDialogFlowContext dialogs,
        string providerKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var modelId = await dialogs.InputAsync(
                "Enter model ID",
                allowEmpty: false,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (modelId.IsBack)
        {
            return AgentTuiDialogStepResult<AgentTuiSelectedModel>.Back();
        }

        if (string.IsNullOrWhiteSpace(modelId.Value))
        {
            return AgentTuiDialogStepResult<AgentTuiSelectedModel>.Canceled();
        }

        return AgentTuiDialogStepResult<AgentTuiSelectedModel>.Submitted(new AgentTuiSelectedModel(
            providerKey,
            modelId.Value.Trim(),
            Capabilities: AgentTuiModelCapabilities.None));
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
