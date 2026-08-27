using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.Providers;
using HPD.TUI.Components;

namespace HPD.Agent.TUI.Commands;

/// <summary>Provides the shared provider and model selection workflow used by agent TUI hosts.</summary>
public static class AgentTuiModelSelectionFlow
{
    private const int InitialModelChoiceLimit = 28;
    private const int BrowseModelChoiceLimit = 200;

    /// <summary>Selects a connected provider, returning <see langword="null"/> when the interaction is canceled.</summary>
    /// <param name="catalog">The provider and model catalog.</param>
    /// <param name="catalogContext">The current catalog context.</param>
    /// <param name="context">The active command context.</param>
    /// <param name="cancellationToken">A token that cancels the interaction.</param>
    /// <returns>The selected provider, or <see langword="null"/>.</returns>
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

    /// <summary>Determines whether exactly one non-expired connected provider is available.</summary>
    /// <param name="catalog">The provider and model catalog.</param>
    /// <param name="catalogContext">The current catalog context.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns><see langword="true"/> when exactly one connected provider is available.</returns>
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

    /// <summary>Selects a connected provider while preserving submitted, back, and canceled dialog outcomes.</summary>
    /// <param name="catalog">The provider and model catalog.</param>
    /// <param name="catalogContext">The current catalog context.</param>
    /// <param name="context">The active command context.</param>
    /// <param name="dialogs">The resumable dialog flow.</param>
    /// <param name="cancellationToken">A token that cancels the interaction.</param>
    /// <returns>The provider-selection dialog result.</returns>
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
                new AgentTuiSelectOptions { AllowFilter = true },
                cancellationToken)
            .ConfigureAwait(false);

        return selected.IsSubmitted
            ? AgentTuiDialogStepResult<AgentTuiProviderChoice>.Submitted(selected.Value!)
            : selected.IsBack
                ? AgentTuiDialogStepResult<AgentTuiProviderChoice>.Back()
                : AgentTuiDialogStepResult<AgentTuiProviderChoice>.Canceled();
    }

    /// <summary>Selects a model for an exact provider connection.</summary>
    /// <param name="catalog">The provider and model catalog.</param>
    /// <param name="catalogContext">The current catalog context.</param>
    /// <param name="context">The active command context.</param>
    /// <param name="selection">The persistent model-selection state.</param>
    /// <param name="options">The selection policy and callbacks.</param>
    /// <param name="provider">The exact provider connection.</param>
    /// <param name="title">An optional dialog title.</param>
    /// <param name="cancellationToken">A token that cancels the interaction.</param>
    /// <returns>The selected model, or <see langword="null"/>.</returns>
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

    /// <summary>Selects a model while preserving submitted, back, and canceled dialog outcomes.</summary>
    /// <param name="catalog">The provider and model catalog.</param>
    /// <param name="catalogContext">The current catalog context.</param>
    /// <param name="context">The active command context.</param>
    /// <param name="dialogs">The resumable dialog flow.</param>
    /// <param name="selection">The persistent model-selection state.</param>
    /// <param name="options">The selection policy and callbacks.</param>
    /// <param name="provider">The exact provider connection.</param>
    /// <param name="title">An optional dialog title.</param>
    /// <param name="cancellationToken">A token that cancels the interaction.</param>
    /// <returns>The model-selection dialog result.</returns>
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

        while (true)
        {
            var models = await catalog.GetModelsAsync(
                    catalogContext,
                    provider,
                    new AgentTuiModelQuery(),
                    cancellationToken)
                .ConfigureAwait(false);
            var selectableModels = ApplyModelPolicy(models, options).ToArray();

            var initialModels = GetInitialModels(provider, selectableModels);
            var choices = BuildModelChoices(provider, initialModels, selection, options, selectableModels.Length > initialModels.Count);
            if (choices.Count == 0)
            {
                return await ReadManualModelAsync(context, dialogs, provider, cancellationToken)
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

            var result = selected.Value.Kind switch
            {
                ModelChoiceKind.Model => selected.Value.Model is null
                    ? AgentTuiDialogStepResult<AgentTuiSelectedModel>.Canceled()
                    : AgentTuiDialogStepResult<AgentTuiSelectedModel>.Submitted(CreateSelectedModel(provider, selected.Value.Model)),
                ModelChoiceKind.Recent => await SelectRecentModelAsync(context, dialogs, selection, options, provider, cancellationToken)
                    .ConfigureAwait(false),
                ModelChoiceKind.Manual => await ReadManualModelAsync(context, dialogs, provider, cancellationToken)
                    .ConfigureAwait(false),
                ModelChoiceKind.SearchAll => await SearchModelsAsync(context, dialogs, catalog, catalogContext, provider, options, freeOnly: false, cancellationToken)
                    .ConfigureAwait(false),
                ModelChoiceKind.SearchFree => await SearchModelsAsync(context, dialogs, catalog, catalogContext, provider, options, freeOnly: true, cancellationToken)
                    .ConfigureAwait(false),
                _ => AgentTuiDialogStepResult<AgentTuiSelectedModel>.Canceled()
            };

            if (result.IsBack)
            {
                continue;
            }

            return result;
        }
    }

    /// <summary>Commits a selected model and invokes configured selection callbacks.</summary>
    /// <param name="selection">The persistent model-selection state.</param>
    /// <param name="context">The active command context.</param>
    /// <param name="model">The selected model.</param>
    /// <param name="options">The selection policy and callbacks.</param>
    /// <param name="configureSelection">Whether to invoke the configuration callback.</param>
    /// <returns>The committed model, or <see langword="null"/> when configuration declines it.</returns>
    public static async ValueTask<AgentTuiSelectedModel?> CommitSelectionAsync(
        AgentTuiModelSelectionState selection,
        AgentTuiCommandContext context,
        AgentTuiSelectedModel model,
        AgentTuiModelSelectionOptions options,
        bool configureSelection = true)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);

        var committed = !configureSelection || options.ConfigureSelection is null
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
            $"{committed.Provider.Key} / {committed.ModelId}",
            TranscriptSeverity.Info);
        if (options.SelectionCommitted is not null)
        {
            await options.SelectionCommitted(context, committed).ConfigureAwait(false);
        }

        return committed;
    }

    /// <summary>Creates and commits a selected model for an exact provider connection.</summary>
    /// <param name="selection">The persistent model-selection state.</param>
    /// <param name="context">The active command context.</param>
    /// <param name="provider">The exact provider connection.</param>
    /// <param name="modelId">The provider model identifier.</param>
    /// <param name="options">The selection policy and callbacks.</param>
    /// <returns>The committed model, or <see langword="null"/> when configuration declines it.</returns>
    public static async ValueTask<AgentTuiSelectedModel?> CommitSelectionAsync(
        AgentTuiModelSelectionState selection,
        AgentTuiCommandContext context,
        AgentTuiProviderChoice provider,
        string modelId,
        AgentTuiModelSelectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(context);

        return await CommitSelectionAsync(
                selection,
                context,
                new AgentTuiSelectedModel(
                    provider.SelectionId,
                    provider.TargetId,
                    provider.ConnectionId,
                    ProviderClientConfigSnapshot.CloneProviderReference(provider.Provider),
                    modelId,
                    Capabilities: AgentTuiModelCapabilities.None,
                    Chat: provider.Chat is null ? null : (ChatClientConfig)ProviderClientConfigSnapshot.Clone(provider.Chat)),
                options)
            .ConfigureAwait(false);
    }

    /// <summary>Appends a model-workflow notice to the command transcript.</summary>
    /// <param name="context">The active command context.</param>
    /// <param name="title">The notice title.</param>
    /// <param name="body">The notice body.</param>
    /// <param name="severity">The notice severity.</param>
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
            .Where(model => string.Equals(model.ConnectionId, provider.ConnectionId, StringComparison.Ordinal))
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
        var models = await catalog.GetModelsAsync(
                catalogContext,
                provider,
                new AgentTuiModelQuery(
                    Live: true,
                    FreeOnly: freeOnly),
                cancellationToken)
            .ConfigureAwait(false);

        var choices = ApplyModelPolicy(models, options)
            .OrderByDescending(static model => model.IsRecommended)
            .ThenBy(static model => model.IsFree ? 0 : 1)
            .ThenBy(static model => model.DisplayName ?? model.ModelId, StringComparer.OrdinalIgnoreCase)
            .Take(BrowseModelChoiceLimit)
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

            return await ReadManualModelAsync(context, dialogs, provider, cancellationToken)
                .ConfigureAwait(false);
        }

        var selected = await dialogs.SelectAsync(
                freeOnly ? "Search free models" : "Search models",
                choices,
                FormatModel,
                new AgentTuiSelectOptions { AllowFilter = true },
                cancellationToken)
            .ConfigureAwait(false);

        if (selected.IsSubmitted && selected.Value is not null)
        {
            return AgentTuiDialogStepResult<AgentTuiSelectedModel>.Submitted(CreateSelectedModel(provider, selected.Value!));
        }

        return selected.IsBack
            ? AgentTuiDialogStepResult<AgentTuiSelectedModel>.Back()
            : AgentTuiDialogStepResult<AgentTuiSelectedModel>.Canceled();
    }

    private static async ValueTask<AgentTuiDialogStepResult<AgentTuiSelectedModel>> ReadManualModelAsync(
        AgentTuiCommandContext context,
        AgentTuiDialogFlowContext dialogs,
        AgentTuiProviderChoice provider,
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
            provider.SelectionId,
            provider.TargetId,
            provider.ConnectionId,
            ProviderClientConfigSnapshot.CloneProviderReference(provider.Provider),
            modelId.Value.Trim(),
            Capabilities: AgentTuiModelCapabilities.None,
            Chat: provider.Chat is null ? null : (ChatClientConfig)ProviderClientConfigSnapshot.Clone(provider.Chat)));
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
                string.Equals(model.ConnectionId, provider.ConnectionId, StringComparison.Ordinal) &&
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

    private static AgentTuiSelectedModel CreateSelectedModel(
        AgentTuiProviderChoice provider,
        AgentTuiModelChoice model)
        => new(
            provider.SelectionId,
            provider.TargetId,
            provider.ConnectionId,
            ProviderClientConfigSnapshot.CloneProviderReference(provider.Provider),
            model.ModelId,
            model.DisplayName,
            model.Capabilities ?? AgentTuiModelCapabilities.None,
            provider.Chat is null ? null : (ChatClientConfig)ProviderClientConfigSnapshot.Clone(provider.Chat));

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
