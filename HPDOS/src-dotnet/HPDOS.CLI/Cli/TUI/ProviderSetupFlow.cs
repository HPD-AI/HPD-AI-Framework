using HPDOS.Core.Auth;
using HPDOS.Core.Shell;
using Spectre.Console;

namespace HPDOS.Shell.Cli.TUI;

/// <summary>
/// Shared Spectre.Console TUI flow for connecting and disconnecting providers.
/// Called by both the /providers slash command (inside hpdos chat) and the
/// standalone hpdos providers command.
/// </summary>
public static class ProviderSetupFlow
{
    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the full provider management loop: shows the table, then routes
    /// to connect/disconnect sub-flows until the user selects "Done".
    /// </summary>
    public static async Task RunAsync(
        IProviderOperations ops,
        CancellationToken ct,
        ProviderOptionsStore? optionsStore = null)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var summaries = await ops.GetSummaryAsync();
                DrawProviderTable(summaries);

                // Pick a provider to act on (or Done).
                var picked = await PickProviderAsync(summaries, ct);
                if (picked == null || ct.IsCancellationRequested)
                    return;

                var summary = summaries.First(s => s.ProviderId == picked);
                var isConnected = summary.IsAuthenticated && !summary.IsExpired;

                // Not connected → go straight to connect (skip redundant action picker).
                if (!isConnected)
                {
                    await ConnectProviderAsync(ops, preselectedId: picked, ct);
                    continue;
                }

                // Connected → show per-provider action menu.
                var methods = await ops.GetMethodsAsync(picked);
                var hasMultipleMethods = methods.Count > 1;
                var storedCount = summary.StoredEntries?.Count ?? 1;
                var hasConfigurableOptions = ProviderConfigFlow.HasOptions(picked);
                var action = await PickProviderActionAsync(
                    summary.DisplayName, hasMultipleMethods, storedCount, hasConfigurableOptions, ct);
                if (action == "back" || ct.IsCancellationRequested)
                    continue;

                switch (action)
                {
                    case "configure":
                        var store = optionsStore ?? await ProviderOptionsStore.LoadAsync();
                        await ProviderConfigFlow.RunAsync(picked, summary.DisplayName, store, ct);
                        break;
                    case "add":
                        await SwitchAuthMethodAsync(ops, picked, ct);
                        break;
                    case "set_active":
                        await SetActiveEntryAsync(ops, summary, ct);
                        break;
                    case "remove_entry":
                        await RemoveEntryAsync(ops, summary, ct);
                        break;
                    case "disconnect":
                        await DisconnectProviderAsync(ops, ct, preselectedId: picked);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C or external cancellation — return silently.
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
        }
    }

    // ── Connect flow ──────────────────────────────────────────────────────────

    /// <summary>
    /// Guides the user through connecting a provider.
    /// </summary>
    /// <param name="ops">Provider operations abstraction.</param>
    /// <param name="preselectedId">
    ///   If non-null, skip the provider picker and go straight to this provider.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task ConnectProviderAsync(
        IProviderOperations ops,
        string? preselectedId,
        CancellationToken ct,
        bool allowReconnect = false)
    {
        try
        {
            // 1. Load summaries; filter to disconnected unless we're switching methods.
            var summaries = await ops.GetSummaryAsync();
            var candidates = allowReconnect
                ? summaries
                : summaries.Where(s => !s.IsAuthenticated).ToList();

            if (candidates.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]All providers are already connected.[/]");
                return;
            }

            // 2. Resolve the provider ID — either from the caller or from a picker.
            string providerId;
            string displayName;

            if (preselectedId != null)
            {
                var match = candidates.FirstOrDefault(
                    s => s.ProviderId.Equals(preselectedId, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    AnsiConsole.MarkupLine(
                        $"[yellow]Provider [bold]{Markup.Escape(preselectedId)}[/] not found.[/]");
                    return;
                }

                providerId  = match.ProviderId;
                displayName = match.DisplayName;
            }
            else
            {
                var pickerMap = candidates.ToDictionary(s => s.DisplayName, s => s);

                var prompt = new SelectionPrompt<string>()
                    .Title("[bold]Which provider would you like to connect?[/]")
                    .PageSize(12)
                    .WrapAround(true)
                    .AddChoices(pickerMap.Keys);

                string picked;
                try { picked = await prompt.ShowAsync(AnsiConsole.Console, ct); }
                catch (OperationCanceledException) { return; }

                var chosen = pickerMap[picked];
                providerId  = chosen.ProviderId;
                displayName = chosen.DisplayName;
            }

            // 3. Load method metadata for this provider.
            var methods = await ops.GetMethodsAsync(providerId);

            // 4. If more than one method, show a picker; otherwise use index 0.
            int methodIndex;

            if (methods.Count > 1)
            {
                var methodLabels = methods
                    .Select((m, i) => (Label: FormatMethodLabel(m, i), Index: i))
                    .ToList();

                var methodPrompt = new SelectionPrompt<string>()
                    .Title($"[bold]How would you like to connect [cyan]{Markup.Escape(displayName)}[/]?[/]")
                    .PageSize(8)
                    .WrapAround(true)
                    .AddChoices(methodLabels.Select(m => m.Label));

                string pickedLabel;
                try { pickedLabel = await methodPrompt.ShowAsync(AnsiConsole.Console, ct); }
                catch (OperationCanceledException) { return; }

                methodIndex = methodLabels.First(m => m.Label == pickedLabel).Index;
            }
            else
            {
                methodIndex = 0;
            }

            // 5. Kick off the flow and dispatch on the result type.
            AnsiConsole.WriteLine();
            var result = await ops.StartLoginAsync(providerId, methodIndex, ct);
            await HandleFlowResultAsync(ops, providerId, methodIndex, displayName, result, ct);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[dim]Cancelled.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
        }
    }

    /// <summary>Switches the auth method for an already-connected provider.</summary>
    private static Task SwitchAuthMethodAsync(IProviderOperations ops, string providerId, CancellationToken ct)
        => ConnectProviderAsync(ops, preselectedId: providerId, ct, allowReconnect: true);

    // ── Disconnect flow ───────────────────────────────────────────────────────

    /// <summary>
    /// Guides the user through disconnecting an already-connected provider.
    /// </summary>
    public static async Task DisconnectProviderAsync(IProviderOperations ops, CancellationToken ct, string? preselectedId = null)
    {
        try
        {
            var summaries = await ops.GetSummaryAsync();
            var connected = summaries
                .Where(s => s.IsAuthenticated && !s.IsExpired)
                .ToList();

            if (connected.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No connected providers to disconnect.[/]");
                return;
            }

            AuthSummary chosen;

            if (preselectedId != null)
            {
                var match = connected.FirstOrDefault(
                    s => s.ProviderId.Equals(preselectedId, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    AnsiConsole.MarkupLine("[yellow]Provider is not connected.[/]");
                    return;
                }
                chosen = match;
            }
            else
            {
                var pickerMap = connected.ToDictionary(s => s.DisplayName, s => s);
                var prompt = new SelectionPrompt<string>()
                    .Title("[bold]Which provider would you like to disconnect?[/]")
                    .PageSize(12)
                    .WrapAround(true)
                    .AddChoices(pickerMap.Keys);

                string pickedName;
                try { pickedName = await prompt.ShowAsync(AnsiConsole.Console, ct); }
                catch (OperationCanceledException) { return; }
                chosen = pickerMap[pickedName];
            }

            bool confirmed;
            var confirmPrompt = new ConfirmationPrompt(
                $"Disconnect [bold]{Markup.Escape(chosen.DisplayName)}[/]?")
            { DefaultValue = false };
            try { confirmed = await confirmPrompt.ShowAsync(AnsiConsole.Console, ct); }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[dim]Cancelled.[/]");
                return;
            }

            if (!confirmed)
            {
                AnsiConsole.MarkupLine("[dim]Cancelled.[/]");
                return;
            }

            var ok = await ops.LogoutAsync(chosen.ProviderId);
            if (ok)
                AnsiConsole.MarkupLine($"[green]✓ {Markup.Escape(chosen.DisplayName)} disconnected.[/]");
            else
                AnsiConsole.MarkupLine($"[red]Failed to disconnect {Markup.Escape(chosen.DisplayName)}.[/]");
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[dim]Cancelled.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void DrawProviderTable(List<AuthSummary> summaries)
    {
        AnsiConsole.WriteLine();

        var table = new Table()
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[bold]Provider[/]"))
            .AddColumn(new TableColumn("[bold]Status[/]"))
            .AddColumn(new TableColumn("[bold]Active method[/]"))
            .AddColumn(new TableColumn("[bold]Stored[/]"))
            .AddColumn(new TableColumn("[bold]Expires[/]"));

        foreach (var s in summaries)
        {
            var status = (s.IsAuthenticated, s.IsExpired) switch
            {
                (true, false) => "[green]✓ connected[/]",
                (true, true)  => "[yellow]⚠ expired[/]",
                _             => "[dim]✗[/]"
            };

            var source  = s.Source is not null ? Markup.Escape(s.Source) : "[dim]—[/]";
            var expires = s.ExpiresAt.HasValue ? s.ExpiresAt.Value.ToString("yyyy-MM-dd") : "[dim]—[/]";
            var count   = s.StoredEntries is { Count: > 0 }
                ? s.StoredEntries.Count.ToString()
                : "[dim]—[/]";

            table.AddRow(Markup.Escape(s.DisplayName), status, source, count, expires);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Shows all providers as a flat list. Returns the selected ProviderId, or null for "Done".
    /// </summary>
    private static async Task<string?> PickProviderAsync(List<AuthSummary> summaries, CancellationToken ct)
    {
        const string DoneKey = "__done__";

        var prompt = new SelectionPrompt<string>()
            .Title("[bold]Select a provider to manage:[/]")
            .PageSize(14)
            .WrapAround(true)
            .UseConverter(id =>
            {
                if (id == DoneKey) return "Done (back to chat)";
                var s = summaries.FirstOrDefault(x => x.ProviderId == id);
                if (s is null) return id;
                var badge = (s.IsAuthenticated, s.IsExpired) switch
                {
                    (true, false) => "[green]✓[/] ",
                    (true, true)  => "[yellow]⚠[/] ",
                    _             => "[dim]✗[/] "
                };
                var hint = s.SupportsFreeModels ? " [dim](free models available)[/]" : string.Empty;
                return $"{badge}{Markup.Escape(s.DisplayName)}{hint}";
            })
            .AddChoices(summaries.Select(s => s.ProviderId).Append(DoneKey));

        var picked = await prompt.ShowAsync(AnsiConsole.Console, ct);
        return picked == DoneKey ? null : picked;
    }

    /// <summary>
    /// Shows actions available for a connected provider.
    /// Returns "add", "set_active", "remove_entry", "disconnect", or "back".
    /// </summary>
    private static async Task<string> PickProviderActionAsync(
        string displayName, bool hasMultipleMethods, int storedCount,
        bool hasConfigurableOptions, CancellationToken ct)
    {
        var choices = new List<string>();
        if (hasConfigurableOptions) choices.Add("configure");   // provider-specific options
        if (hasMultipleMethods)     choices.Add("add");         // add another connection method
        if (storedCount > 1)        choices.Add("set_active");  // switch which stored entry is active
        if (storedCount > 1)        choices.Add("remove_entry"); // remove a specific stored entry
        choices.Add("disconnect");                               // disconnect (remove active entry)
        choices.Add("back");

        var prompt = new SelectionPrompt<string>()
            .Title($"[bold]{Markup.Escape(displayName)}[/] — what would you like to do?")
            .PageSize(8)
            .WrapAround(true)
            .UseConverter(c => c switch
            {
                "configure"    => "Configure",
                "add"          => "Add connection method",
                "set_active"   => "Switch active connection",
                "remove_entry" => "Remove a stored connection",
                "disconnect"   => "Disconnect active",
                "back"         => "Back",
                _              => c
            })
            .AddChoices(choices);

        return await prompt.ShowAsync(AnsiConsole.Console, ct);
    }

    /// <summary>Shows a picker of all stored entries and sets the selected one as active.</summary>
    private static async Task SetActiveEntryAsync(IProviderOperations ops, AuthSummary summary, CancellationToken ct)
    {
        var entries = summary.StoredEntries;
        if (entries is null || entries.Count == 0) return;

        var prompt = new SelectionPrompt<string>()
            .Title($"[bold]Choose active connection for {Markup.Escape(summary.DisplayName)}:[/]")
            .PageSize(10)
            .WrapAround(true)
            .UseConverter(id =>
            {
                var e = entries.FirstOrDefault(x => x.Id == id);
                if (e is null) return id;
                var active = e.Id == summary.ActiveEntryId ? " [green](active)[/]" : string.Empty;
                return $"{Markup.Escape(e.MethodLabel)}{active}";
            })
            .AddChoices(entries.Select(e => e.Id));

        string picked;
        try { picked = await prompt.ShowAsync(AnsiConsole.Console, ct); }
        catch (OperationCanceledException) { return; }

        var ok = await ops.SetActiveEntryAsync(summary.ProviderId, picked);
        if (ok)
        {
            var label = entries.FirstOrDefault(e => e.Id == picked)?.MethodLabel ?? picked;
            AnsiConsole.MarkupLine($"[green]✓ Now using: {Markup.Escape(label)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Failed to switch active connection.[/]");
        }
    }

    /// <summary>Shows a picker of stored entries and removes the selected one.</summary>
    private static async Task RemoveEntryAsync(IProviderOperations ops, AuthSummary summary, CancellationToken ct)
    {
        var entries = summary.StoredEntries;
        if (entries is null || entries.Count == 0) return;

        var prompt = new SelectionPrompt<string>()
            .Title($"[bold]Which stored connection to remove from {Markup.Escape(summary.DisplayName)}?[/]")
            .PageSize(10)
            .WrapAround(true)
            .UseConverter(id =>
            {
                var e = entries.FirstOrDefault(x => x.Id == id);
                if (e is null) return id;
                var active = e.Id == summary.ActiveEntryId ? " [green](active)[/]" : string.Empty;
                return $"{Markup.Escape(e.MethodLabel)}{active}";
            })
            .AddChoices(entries.Select(e => e.Id));

        string picked;
        try { picked = await prompt.ShowAsync(AnsiConsole.Console, ct); }
        catch (OperationCanceledException) { return; }

        var label = entries.FirstOrDefault(e => e.Id == picked)?.MethodLabel ?? picked;

        var confirmPrompt = new ConfirmationPrompt(
            $"Remove [bold]{Markup.Escape(label)}[/]?")
        { DefaultValue = false };

        bool confirmed;
        try { confirmed = await confirmPrompt.ShowAsync(AnsiConsole.Console, ct); }
        catch (OperationCanceledException) { return; }

        if (!confirmed) return;

        var ok = await ops.LogoutEntryAsync(summary.ProviderId, picked);
        if (ok)
            AnsiConsole.MarkupLine($"[green]✓ Removed: {Markup.Escape(label)}[/]");
        else
            AnsiConsole.MarkupLine("[red]Failed to remove connection.[/]");
    }

    private static string FormatMethodLabel(AuthMethodInfo method, int index)
    {
        var recommended = method.IsRecommended ? " [dim](recommended)[/]" : string.Empty;
        var desc = !string.IsNullOrWhiteSpace(method.Description)
            ? $"  [dim]{Markup.Escape(method.Description)}[/]"
            : string.Empty;

        return $"{Markup.Escape(method.Label)}{recommended}{desc}";
    }

    /// <summary>
    /// Dispatches on the <see cref="AuthFlowResult"/> returned by
    /// <see cref="IProviderOperations.StartLoginAsync"/> and drives any
    /// secondary UI needed (secret TextPrompt, device-code LiveDisplay, etc.).
    /// </summary>
    private static async Task HandleFlowResultAsync(
        IProviderOperations ops,
        string providerId,
        int methodIndex,
        string displayName,
        AuthFlowResult result,
        CancellationToken ct)
    {
        switch (result)
        {
            // ── Already done (e.g. OAuthBrowser completed, WellKnown env var found) ──
            case AuthFlowResult.Success:
                AnsiConsole.MarkupLine($"[green]✓ {Markup.Escape(displayName)} connected.[/]");
                break;

            // ── Flow needs a secret value from the user (ApiKey, etc.) ──────────
            case AuthFlowResult.NeedsUserInput needsInput:
                await HandleNeedsUserInputAsync(ops, providerId, methodIndex, displayName, needsInput, ct);
                break;

            // ── Device code / manual-code flow ───────────────────────────────────
            case AuthFlowResult.PendingUserAction pending:
                await HandlePendingActionAsync(displayName, pending, ct);
                break;

            case AuthFlowResult.Failed failed:
                AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(failed.Error)}[/]");
                break;

            case AuthFlowResult.Cancelled:
                AnsiConsole.MarkupLine("[dim]Cancelled.[/]");
                break;
        }
    }

    private static async Task HandleNeedsUserInputAsync(
        IProviderOperations ops,
        string providerId,
        int methodIndex,
        string displayName,
        AuthFlowResult.NeedsUserInput needsInput,
        CancellationToken ct)
    {
        var promptLabel = !string.IsNullOrWhiteSpace(needsInput.InputLabel)
            ? needsInput.InputLabel
            : needsInput.Prompt;

        var keyPrompt = new TextPrompt<string>($"[bold]{Markup.Escape(promptLabel)}[/]")
        {
            IsSecret = true
        };

        string input;
        try { input = await keyPrompt.ShowAsync(AnsiConsole.Console, ct); }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[dim]Cancelled.[/]");
            return;
        }

        var completeResult = await ops.CompleteLoginAsync(providerId, methodIndex, input, ct);

        switch (completeResult)
        {
            case AuthFlowResult.Success:
                AnsiConsole.MarkupLine($"[green]✓ {Markup.Escape(displayName)} connected.[/]");
                break;
            case AuthFlowResult.Failed f:
                AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(f.Error)}[/]");
                break;
            case AuthFlowResult.Cancelled:
                AnsiConsole.MarkupLine("[dim]Cancelled.[/]");
                break;
            default:
                // Unexpected nested state — treat as a failure.
                AnsiConsole.MarkupLine("[red]✗ Unexpected response while completing login.[/]");
                break;
        }
    }

    private static async Task HandlePendingActionAsync(
        string displayName,
        AuthFlowResult.PendingUserAction pending,
        CancellationToken ct)
    {
        // Build the initial panel shown inside the LiveDisplay.
        Panel BuildPanel(string spinner)
        {
            var url      = pending.Url      is not null ? $"\n  [link]{Markup.Escape(pending.Url)}[/]" : string.Empty;
            var userCode = pending.UserCode is not null ? $"\n\n  Code: [bold yellow]{Markup.Escape(pending.UserCode)}[/]" : string.Empty;
            var msg      = !string.IsNullOrWhiteSpace(pending.Message)
                ? Markup.Escape(pending.Message)
                : $"Sign in to {Markup.Escape(displayName)}";

            var body = $"{msg}{url}{userCode}\n\n  {spinner} Waiting for authorisation…";

            return new Panel(body)
                .Header($"[bold] {Markup.Escape(displayName)} [/]")
                .BorderColor(Color.Cyan1)
                .Padding(1, 0);
        }

        // The WaitForCompletion delegate blocks until the flow is complete
        // or the CancellationToken fires. We run it inside a LiveDisplay so
        // the panel stays visible and refreshes periodically.
        AuthFlowResult finalResult = new AuthFlowResult.Cancelled();

        var spinnerFrames = Spinner.Known.Dots.Frames;
        int frameIndex = 0;

        await AnsiConsole.Live(BuildPanel(spinnerFrames[0]))
            .AutoClear(true)
            .StartAsync(async ctx =>
            {
                // Start a background refresh loop to animate the spinner.
                using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                var refreshTask = Task.Run(async () =>
                {
                    while (!refreshCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(100, refreshCts.Token).ConfigureAwait(false);
                        frameIndex = (frameIndex + 1) % spinnerFrames.Count;
                        ctx.UpdateTarget(BuildPanel(spinnerFrames[frameIndex]));
                    }
                }, refreshCts.Token);

                try
                {
                    finalResult = await pending.WaitForCompletion(ct).ConfigureAwait(false);
                }
                finally
                {
                    await refreshCts.CancelAsync();
                    try { await refreshTask.ConfigureAwait(false); } catch { /* suppress */ }
                }
            });

        // After the LiveDisplay closes, report the outcome.
        switch (finalResult)
        {
            case AuthFlowResult.Success:
                AnsiConsole.MarkupLine($"[green]✓ {Markup.Escape(displayName)} connected.[/]");
                break;
            case AuthFlowResult.Failed f:
                AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(f.Error)}[/]");
                break;
            case AuthFlowResult.Cancelled:
                AnsiConsole.MarkupLine("[dim]Cancelled.[/]");
                break;
            default:
                AnsiConsole.MarkupLine("[red]✗ Unexpected result from authorisation flow.[/]");
                break;
        }
    }
}
