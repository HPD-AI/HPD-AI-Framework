using HPDOS.Core.Auth;
using HPDOS.Shell.Cli;
using HPDOS.Shell.Cli.TUI;
using HPDOS.Shell.Shell;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace HPDOS.Shell.Cli.Commands;

/// <summary>
/// Implements the `hpdos providers` standalone command.
/// Runs the same provider table + connect/disconnect flow as the /providers slash command,
/// but as a standalone executable outside of chat.
/// </summary>
public static class ProvidersCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        IProviderOperations ops;

        if (ShellConfig.RemoteServerUrl is { } remoteUrl)
        {
            var http = new HttpClient { BaseAddress = new Uri(remoteUrl.TrimEnd('/')) };
            ops = new RemoteProviderOperations(http);
        }
        else
        {
            // Local mode — start Kestrel and run the OAuth flows in-process so browser
            // OAuth (OAuthCallbackServer) works correctly without going through HTTP.
            await GUIMode.StartServerAsync();

            if (ShellConfig.Port == 0)
            {
                AnsiConsole.MarkupLine("[red]Failed to start server.[/]");
                return 1;
            }

            var authManager = GUIMode.Services!.GetRequiredService<AuthManager>();
            ops = new LocalProviderOperations(authManager);
        }

        using var cts = CtrlCTokenSource.Create();
        try
        {
            await ProviderSetupFlow.RunAsync(ops, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal exit via Ctrl+C
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
        finally
        {
            if (ShellConfig.RemoteServerUrl is null)
                await GUIMode.StopServerAsync();
        }

        return 0;
    }
}
