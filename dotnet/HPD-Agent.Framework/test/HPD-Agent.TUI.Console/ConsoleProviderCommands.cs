using System.Text;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Console;

internal static class ConsoleProviderCommands
{
    private const string ProvidersPageId = "console.providers";
    private const string ProviderDetailPageId = "console.provider-detail";
    private static readonly ConsoleProviderPageState PageState = new();

    public static HpdAgentTuiBuilder AddConsoleProviderCommands(
        this HpdAgentTuiBuilder tui,
        ConsoleProviderContext providers)
        => tui.AddConsoleCommandSurface()
            .TryAddPage(new HpdAgentTuiPageDescriptor(ProvidersPageId, RenderProvidersPage)
            {
                Title = "Providers",
                Description = "Browse provider registration and authentication status.",
                Hidden = true,
                HandleInput = HandleProvidersPageInput
            })
            .TryAddPage(new HpdAgentTuiPageDescriptor(ProviderDetailPageId, RenderProviderDetailPage)
            {
                Title = "Provider",
                Description = "Inspect a provider.",
                Hidden = true,
                HandleInput = HandleProviderDetailPageInput
            })
            .TryAddSlashCommand(new HpdAgentTuiCommandDescriptor("providers", context =>
                ExecuteProvidersAsync(context, providers))
            {
                Title = "/providers",
                Description = "List provider registration and authentication status."
            })
            .TryAddSlashCommand(new HpdAgentTuiCommandDescriptor("provider", context =>
                ExecuteProviderAsync(context, providers))
            {
                Title = "/provider",
                Description = "Show provider setup or choose a provider model."
            });

    private static async ValueTask ExecuteProvidersAsync(
        AgentTuiCommandContext context,
        ConsoleProviderContext providers)
    {
        var statuses = new List<ConsoleProviderStatus>();

        foreach (var provider in providers.Providers.OrderBy(static candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var status = await providers.ProviderState
                .GetStatusAsync(provider.ProviderKey, CancellationToken.None)
                .ConfigureAwait(false);

            statuses.Add(new ConsoleProviderStatus(provider, status.IsRegistered, status.IsAuthenticated));
        }

        PageState.SetProviders(statuses);
        var selected = await context.Dialogs.SelectAsync(
                "Select provider",
                statuses,
                FormatProviderChoice,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!selected.IsSubmitted || selected.Value is not { } selectedProvider)
        {
            return;
        }

        PageState.SelectProvider(selectedProvider.Provider.Key);
        context.Navigation.GoToPage(ProviderDetailPageId);
    }

    private static async ValueTask ExecuteProviderAsync(
        AgentTuiCommandContext context,
        ConsoleProviderContext providers)
    {
        var args = SplitArgs(context.Arguments);
        if (args.Count == 0)
        {
            ConsoleCommandSurface.Show(context, "Provider commands", Usage(), TranscriptSeverity.Warning);
            return;
        }

        var provider = providers.Providers.FirstOrDefault(candidate =>
            string.Equals(candidate.ProviderKey, args[0], StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            AppendNotice(context, $"Provider `{args[0]}` is not known by this console.", TranscriptSeverity.Warning);
            return;
        }

        var verb = args.Count >= 2 ? args[1] : "status";
        switch (verb)
        {
            case "status":
                await ShowProviderStatusAsync(context, providers, provider).ConfigureAwait(false);
                break;
            case "use":
            case "model":
                UseProviderModel(context, providers, provider, args);
                break;
            case "setup":
            case "secrets":
                ShowProviderSetup(context, provider);
                break;
            default:
                ConsoleCommandSurface.Show(context, "Provider commands", Usage(), TranscriptSeverity.Warning);
                break;
        }
    }

    private static async Task ShowProviderStatusAsync(
        AgentTuiCommandContext context,
        ConsoleProviderContext providers,
        ConsoleProviderMetadata provider)
    {
        var status = await providers.ProviderState
            .GetStatusAsync(provider.ProviderKey, CancellationToken.None)
            .ConfigureAwait(false);

        var markdown = new StringBuilder();
        markdown.AppendLine($"Provider: `{EscapeMarkdown(provider.ProviderKey)}`");
        markdown.AppendLine();
        markdown.AppendLine($"- Registered: {(status.IsRegistered ? "yes" : "no")}");
        markdown.AppendLine($"- Authenticated: {(status.IsAuthenticated ? "yes" : "no")}");
        markdown.AppendLine("- Required secrets: " + (provider.RequiredSecretKeys.Count == 0
            ? "none"
            : string.Join(", ", provider.RequiredSecretKeys.Select(static key => $"`{EscapeMarkdown(key)}`"))));

        ConsoleCommandSurface.Show(context, provider.DisplayName, markdown.ToString());
    }

    private static void UseProviderModel(
        AgentTuiCommandContext context,
        ConsoleProviderContext providers,
        ConsoleProviderMetadata provider,
        IReadOnlyList<string> args)
    {
        if (args.Count < 3)
        {
            ConsoleCommandSurface.Show(
                context,
                "Provider commands",
                $"`/provider {provider.ProviderKey} use <modelId>`",
                TranscriptSeverity.Warning);
            return;
        }

        var modelId = args[2];
        providers.ModelSelection.Set(provider.ProviderKey, modelId);
        AppendNotice(context, "Model selected", $"{provider.ProviderKey} / {modelId}");
    }

    private static void ShowProviderSetup(
        AgentTuiCommandContext context,
        ConsoleProviderMetadata provider)
    {
        if (provider.RequiredSecretKeys.Count == 0)
        {
            ConsoleCommandSurface.Show(context, provider.DisplayName, "No secret is required by this console.");
            return;
        }

        var markdown = new StringBuilder();
        markdown.AppendLine("Set these keys through environment variables, user-secrets, or configuration:");
        markdown.AppendLine();
        foreach (var key in provider.RequiredSecretKeys)
        {
            markdown.Append("- `").Append(EscapeMarkdown(key)).AppendLine("`");
        }

        ConsoleCommandSurface.Show(context, $"{provider.DisplayName} setup", markdown.ToString());
    }

    private static void AppendNotice(
        AgentTuiCommandContext context,
        string message,
        TranscriptSeverity severity = TranscriptSeverity.Info,
        string? entryKey = null)
        => AppendOrUpdate(context, new TranscriptEntry(
                Id: $"provider-command-{Guid.NewGuid():N}",
                EntryKey: entryKey,
                Cell: new NoticeCell(message, Severity: severity),
                Metadata: Metadata(context)));

    private static void AppendNotice(
        AgentTuiCommandContext context,
        string title,
        string markdown,
        TranscriptSeverity severity = TranscriptSeverity.Info,
        string? entryKey = null)
        => AppendOrUpdate(context, new TranscriptEntry(
                Id: $"provider-command-{Guid.NewGuid():N}",
                EntryKey: entryKey,
                Cell: new NoticeCell(title, HPD.TUI.Content.TextBlock.Create(markdown), severity),
                Metadata: Metadata(context)));

    private static void AppendOrUpdate(AgentTuiCommandContext context, TranscriptEntry entry)
    {
        if (entry.EntryKey is null)
        {
            context.Shell.Transcript.AddFinal(entry);
            return;
        }

        context.Shell.Transcript.FinalizeLive(entry.EntryKey!, entry.AsFinal(), CommittedHistoryMutationPolicy.Reject);
    }

    private static TranscriptEntryMetadata Metadata(AgentTuiCommandContext context)
        => new(
            AgentId: context.Scope.AgentId,
            AgentName: "tui",
            AgentChain: ["tui"]);

    private static IReadOnlyList<string> SplitArgs(string arguments)
        => arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string EscapeMarkdown(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static string RequiredSecrets(ConsoleProviderMetadata provider)
        => provider.RequiredSecretKeys.Count == 0
            ? "none"
            : string.Join(", ", provider.RequiredSecretKeys.Select(static key => $"`{EscapeMarkdown(key)}`"));

    private static string FormatProviderChoice(ConsoleProviderStatus status)
    {
        var registered = status.Registered ? " registered" : "";
        var authenticated = status.Authenticated ? " authenticated" : "";
        return $"{status.Provider.DisplayName} ({status.Provider.Key}){registered}{authenticated}";
    }

    private static IComponent RenderProvidersPage(AgentTuiPageContext context)
    {
        var snapshot = PageState.Snapshot();
        var markdown = new StringBuilder();
        markdown.AppendLine("**Providers**");
        markdown.AppendLine();
        markdown.Append("Registered ")
            .Append(snapshot.Providers.Count(static status => status.Registered))
            .Append(" / ")
            .Append(snapshot.Providers.Count)
            .Append(", authenticated ")
            .Append(snapshot.Providers.Count(static status => status.Authenticated))
            .Append(" / ")
            .Append(snapshot.Providers.Count)
            .AppendLine(".");
        markdown.AppendLine();
        markdown.AppendLine("Use Up/Down to move, Enter to inspect, Esc to go back.");
        markdown.AppendLine();

        if (snapshot.Providers.Count == 0)
        {
            markdown.AppendLine("No providers found.");
            return HPD.TUI.Content.TextBlock.Create(markdown.ToString());
        }

        for (var i = 0; i < snapshot.Providers.Count; i++)
        {
            var status = snapshot.Providers[i];
            markdown.Append(i == snapshot.SelectedIndex ? "=> " : "   ")
                .Append(EscapeMarkdown(status.Provider.DisplayName))
                .Append(" `")
                .Append(EscapeMarkdown(status.Provider.Key))
                .Append('`');

            if (status.Registered)
            {
                markdown.Append(" registered");
            }

            if (status.Authenticated)
            {
                markdown.Append(" authenticated");
            }

            markdown.AppendLine();
            markdown.Append("    secrets: ")
                .AppendLine(RequiredSecrets(status.Provider));
        }

        return HPD.TUI.Content.TextBlock.Create(markdown.ToString());
    }

    private static bool HandleProvidersPageInput(AgentTuiPageContext context, KeyEvent key)
    {
        switch (key.Key)
        {
            case KeyCode.UpArrow:
                PageState.Move(-1);
                return true;
            case KeyCode.DownArrow:
                PageState.Move(1);
                return true;
            case KeyCode.Home:
                PageState.MoveToStart();
                return true;
            case KeyCode.End:
                PageState.MoveToEnd();
                return true;
            case KeyCode.Enter:
                if (PageState.SelectCurrent() is not null)
                {
                    context.Navigation.GoToPage(ProviderDetailPageId);
                }

                return true;
            default:
                return false;
        }
    }

    private static IComponent RenderProviderDetailPage(AgentTuiPageContext context)
    {
        var status = PageState.Snapshot().SelectedProvider;
        if (status is null)
        {
            return HPD.TUI.Content.TextBlock.Create("**Provider**\n\nNo provider selected.");
        }

        var provider = status.Provider;
        var markdown = new StringBuilder();
        markdown.AppendLine("**Provider**");
        markdown.AppendLine();
        markdown.Append("- name: ").AppendLine(EscapeMarkdown(provider.DisplayName));
        markdown.Append("- key: `").Append(EscapeMarkdown(provider.ProviderKey)).AppendLine("`");
        markdown.Append("- registered: ").AppendLine(status.Registered ? "yes" : "no");
        markdown.Append("- authenticated: ").AppendLine(status.Authenticated ? "yes" : "no");
        markdown.Append("- required secrets: ").AppendLine(RequiredSecrets(provider));
        markdown.AppendLine();
        markdown.AppendLine("Use `/provider <providerKey> setup` for setup details or `/model` to choose a model.");
        return HPD.TUI.Content.TextBlock.Create(markdown.ToString());
    }

    private static bool HandleProviderDetailPageInput(AgentTuiPageContext context, KeyEvent key)
        => false;

    private sealed record ConsoleProviderStatus(
        ConsoleProviderMetadata Provider,
        bool Registered,
        bool Authenticated);

    private sealed class ConsoleProviderPageState
    {
        private readonly object _gate = new();
        private IReadOnlyList<ConsoleProviderStatus> _providers = [];
        private ConsoleProviderStatus? _selectedProvider;
        private int _selectedIndex;

        public void SetProviders(IReadOnlyList<ConsoleProviderStatus> providers)
        {
            lock (_gate)
            {
                _providers = providers;
                _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, providers.Count - 1));
                _selectedProvider = providers.Count == 0 ? null : providers[_selectedIndex];
            }
        }

        public void Move(int delta)
        {
            lock (_gate)
            {
                if (_providers.Count == 0)
                {
                    return;
                }

                _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _providers.Count - 1);
                _selectedProvider = _providers[_selectedIndex];
            }
        }

        public void MoveToStart()
        {
            lock (_gate)
            {
                _selectedIndex = 0;
                _selectedProvider = _providers.Count == 0 ? null : _providers[0];
            }
        }

        public void MoveToEnd()
        {
            lock (_gate)
            {
                _selectedIndex = Math.Max(0, _providers.Count - 1);
                _selectedProvider = _providers.Count == 0 ? null : _providers[_selectedIndex];
            }
        }

        public ConsoleProviderStatus? SelectCurrent()
        {
            lock (_gate)
            {
                _selectedProvider = _providers.Count == 0 ? null : _providers[_selectedIndex];
                return _selectedProvider;
            }
        }

        public ConsoleProviderStatus? SelectProvider(string providerKey)
        {
            lock (_gate)
            {
                for (var i = 0; i < _providers.Count; i++)
                {
                    if (!string.Equals(_providers[i].Provider.Key, providerKey, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _selectedIndex = i;
                    _selectedProvider = _providers[i];
                    return _selectedProvider;
                }

                return null;
            }
        }

        public PageSnapshot Snapshot()
        {
            lock (_gate)
            {
                return new PageSnapshot(_providers.ToArray(), _selectedProvider, _selectedIndex);
            }
        }

        public sealed record PageSnapshot(
            IReadOnlyList<ConsoleProviderStatus> Providers,
            ConsoleProviderStatus? SelectedProvider,
            int SelectedIndex);
    }

    private static string Usage()
        => """
        Usage:

        - `/providers`
        - `/provider <providerKey>`
        - `/provider <providerKey> setup`
        - `/provider <providerKey> use <modelId>`
        """;
}
