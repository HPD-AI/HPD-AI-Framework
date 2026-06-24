using HPD.Agent.TUI.Models;
using HPD.TUI.Components;

namespace HPD.Agent.TUI.Commands;

internal static class ModelSelectionCommand
{
    private const int InitialModelChoiceLimit = 28;
    private const int SearchModelChoiceLimit = 30;

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
            ApplySelection(selection, context, arguments[0], arguments[1]);
            return;
        }

        if (selection.Current is { } current)
        {
            AppendNotice(
                context,
                "Current model",
                $"{current.ProviderKey} / {current.ModelId}",
                TranscriptSeverity.Info);
        }

        var catalogContext = new AgentTuiModelCatalogContext(context.Scope, context.Shell);
        var providers = await catalog.GetProvidersAsync(catalogContext, CancellationToken.None)
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
            return;
        }

        var provider = connected.Length == 1
            ? connected[0]
            : await context.Dialogs.SelectAsync(
                    "Select provider",
                    connected,
                    static candidate => candidate.DisplayName,
                    CancellationToken.None)
                .ConfigureAwait(false);

        if (provider is null)
        {
            return;
        }

        var model = await SelectModelAsync(catalog, catalogContext, context, selection, options, provider)
            .ConfigureAwait(false);
        if (model is null)
        {
            return;
        }

        ApplySelection(selection, context, model);
    }

    private static async ValueTask<AgentTuiModelChoice?> SelectModelAsync(
        IAgentTuiModelCatalog catalog,
        AgentTuiModelCatalogContext catalogContext,
        AgentTuiCommandContext context,
        AgentTuiModelSelectionState selection,
        AgentTuiModelSelectionOptions options,
        AgentTuiProviderChoice provider)
    {
        var models = await catalog.GetModelsAsync(
                catalogContext,
                provider.ProviderKey,
                new AgentTuiModelQuery(),
                CancellationToken.None)
            .ConfigureAwait(false);
        var selectableModels = ApplyModelPolicy(models, options).ToArray();

        var initialModels = GetInitialModels(provider, selectableModels);
        var choices = BuildModelChoices(provider, initialModels, selection, options, selectableModels.Length > initialModels.Count);
        if (choices.Count == 0)
        {
            return await ReadManualModelAsync(context, provider.ProviderKey)
                .ConfigureAwait(false);
        }

        var selected = await context.Dialogs.SelectAsync(
                "Select model",
                choices,
                static choice => choice.Label,
                CancellationToken.None)
            .ConfigureAwait(false);

        if (selected is null)
        {
            return null;
        }

        return selected.Kind switch
        {
            ModelChoiceKind.Model => selected.Model,
            ModelChoiceKind.Recent => await SelectRecentModelAsync(context, selection, options, provider)
                .ConfigureAwait(false),
            ModelChoiceKind.Manual => await ReadManualModelAsync(context, provider.ProviderKey)
                .ConfigureAwait(false),
            ModelChoiceKind.SearchAll => await SearchModelsAsync(
                    catalog,
                    catalogContext,
                    context,
                    provider,
                    options,
                    freeOnly: false)
                .ConfigureAwait(false),
            ModelChoiceKind.SearchFree => await SearchModelsAsync(
                    catalog,
                    catalogContext,
                    context,
                    provider,
                    options,
                    freeOnly: true)
                .ConfigureAwait(false),
            _ => null
        };
    }

    private static async ValueTask<AgentTuiModelChoice?> SelectRecentModelAsync(
        AgentTuiCommandContext context,
        AgentTuiModelSelectionState selection,
        AgentTuiModelSelectionOptions options,
        AgentTuiProviderChoice provider)
    {
        var recent = selection.Recent
            .Where(model => string.Equals(model.ProviderKey, provider.ProviderKey, StringComparison.OrdinalIgnoreCase))
            .Where(model => !options.RequireToolSupport || model.SupportsTools)
            .Select(static model => new AgentTuiModelChoice(
                model.ProviderKey,
                model.ModelId,
                model.DisplayName,
                SupportsTools: model.SupportsTools))
            .ToArray();
        if (recent.Length == 0)
        {
            return null;
        }

        return await context.Dialogs.SelectAsync(
                "Recent models",
                recent,
                FormatModel,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async ValueTask<AgentTuiModelChoice?> SearchModelsAsync(
        IAgentTuiModelCatalog catalog,
        AgentTuiModelCatalogContext catalogContext,
        AgentTuiCommandContext context,
        AgentTuiProviderChoice provider,
        AgentTuiModelSelectionOptions options,
        bool freeOnly)
    {
        var search = await context.Dialogs.InputAsync(
                freeOnly ? "Search free models" : "Search models",
                allowEmpty: false,
                cancellationToken: CancellationToken.None)
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
                CancellationToken.None)
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
            return await ReadManualModelAsync(context, provider.ProviderKey)
                .ConfigureAwait(false);
        }

        return await context.Dialogs.SelectAsync(
                freeOnly ? "Select free model" : "Select model",
                choices,
                FormatModel,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async ValueTask<AgentTuiModelChoice?> ReadManualModelAsync(
        AgentTuiCommandContext context,
        string providerKey)
    {
        var modelId = await context.Dialogs.InputAsync(
                "Enter model ID",
                allowEmpty: false,
                cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(modelId)
            ? null
            : new AgentTuiModelChoice(providerKey, modelId.Trim());
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
                (!options.RequireToolSupport || model.SupportsTools)))
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
            ? models.Where(static model => model.SupportsTools)
            : models;

    private static void ApplySelection(
        AgentTuiModelSelectionState selection,
        AgentTuiCommandContext context,
        AgentTuiModelChoice model)
    {
        selection.Set(model);
        AppendNotice(
            context,
            "Model selected",
            $"{model.ProviderKey} / {model.ModelId}",
            TranscriptSeverity.Info);
    }

    private static void ApplySelection(
        AgentTuiModelSelectionState selection,
        AgentTuiCommandContext context,
        string providerKey,
        string modelId)
    {
        selection.Set(providerKey, modelId);
        AppendNotice(
            context,
            "Model selected",
            $"{providerKey} / {modelId}",
            TranscriptSeverity.Info);
    }

    private static void AppendNotice(
        AgentTuiCommandContext context,
        string title,
        string body,
        TranscriptSeverity severity)
    {
        context.Shell.Transcript.AddFinal(new TranscriptEntry(
            Id: $"model-command-{Guid.NewGuid():N}",
            EntryKey: null,
            Cell: new NoticeCell(title, new Text(body), severity),
            Metadata: new TranscriptEntryMetadata(
                AgentId: context.Scope.AgentId,
                AgentName: "tui",
                AgentChain: ["tui"])));
    }

    private static IReadOnlyList<string> SplitArguments(string arguments)
        => arguments
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
