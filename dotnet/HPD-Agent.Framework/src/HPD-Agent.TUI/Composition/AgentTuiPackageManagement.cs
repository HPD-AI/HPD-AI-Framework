using System.Text;
using HPD.Agent.Packages;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Composition;

public static class AgentTuiPackageManagement
{
    public const string PackagesPageId = "hpd.packages";

    public static HpdAgentTuiBuilder AddPackageManagement(
        this HpdAgentTuiBuilder builder,
        IHpdPackageRuntime packages)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(packages);

        return builder
            .TryAddPage(new HpdAgentTuiPageDescriptor(PackagesPageId, _ => RenderPackagesPage(packages))
            {
                Title = "Packages",
                Description = "Inspect loaded HPD packages.",
                Hidden = true
            })
            .TryAddSlashCommand(new HpdAgentTuiCommandDescriptor("packages", async context =>
            {
                await packages.ListAsync();
                context.Navigation.GoToPage(PackagesPageId);
            })
            {
                Title = "/packages",
                Description = "Inspect loaded HPD packages.",
                Order = 640
            })
            .TryAddSlashCommand(new HpdAgentTuiCommandDescriptor("package", context =>
                ExecutePackageCommand(context, packages))
            {
                Title = "/package",
                Description = "Enable, disable, reload, or inspect HPD packages.",
                Order = 641
            });
    }

    private static IComponent RenderPackagesPage(IHpdPackageRuntime packages)
    {
        var loaded = packages.Packages
            .OrderBy(static package => package.Scope, StringComparer.Ordinal)
            .ThenBy(static package => package.Id, StringComparer.Ordinal)
            .ToArray();

        if (loaded.Length == 0)
        {
            return new Markdown("**Packages**\n\nNo packages are loaded.");
        }

        var sb = new StringBuilder();
        sb.AppendLine("**Packages**");
        sb.AppendLine();

        foreach (var package in loaded)
        {
            sb.Append("- `");
            sb.Append(package.Id);
            sb.Append("` ");
            sb.Append(package.DisplayName);
            sb.Append(" ");
            sb.Append(package.Version);
            sb.Append(" · ");
            sb.Append(package.Scope);
            sb.Append(" · ");
            sb.Append(package.State);
            sb.Append(" · ");
            sb.Append(package.Manifest.LoadMode);
            sb.Append(" · ");
            sb.Append(package.Manifest.Trust);

            if (package.Impacts.Count > 0)
            {
                sb.Append(" · impacts: ");
                sb.Append(string.Join(", ", package.Impacts));
            }

            sb.AppendLine();

            AppendContributionList(sb, "agent", package.Contributions.AgentContributors);
            AppendContributionList(sb, "providers", package.Contributions.ProviderFactories);
            AppendContributionList(sb, "provider config", package.Contributions.ProviderConfigSerializers);
            AppendContributionList(sb, "secrets", package.Contributions.SecretAliases);
            AppendContributionList(sb, "model catalogs", package.Contributions.ModelCatalogs);

            foreach (var diagnostic in package.Diagnostics)
            {
                sb.Append("  - ");
                sb.Append(diagnostic.Severity);
                sb.Append(": ");
                sb.Append(diagnostic.Message);
                sb.AppendLine();
            }
        }

        return new Markdown(sb.ToString());
    }

    private static void AppendContributionList(
        StringBuilder sb,
        string label,
        IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
        {
            return;
        }

        sb.Append("  - ");
        sb.Append(label);
        sb.Append(": ");
        sb.Append(string.Join(", ", keys.Select(static key => $"`{key}`")));
        sb.AppendLine();
    }

    private static async ValueTask ExecutePackageCommand(
        AgentTuiCommandContext context,
        IHpdPackageRuntime packages)
    {
        var args = SplitArgs(context.Arguments);
        if (args.Count == 0)
        {
            await packages.ListAsync();
            context.Navigation.GoToPage(PackagesPageId);
            return;
        }

        var verb = args[0];
        switch (verb)
        {
            case "list":
                await packages.ListAsync();
                context.Navigation.GoToPage(PackagesPageId);
                break;
            case "enable":
                await EnablePackageAsync(context, packages, args);
                break;
            case "reload":
                await ReloadPackageAsync(context, packages, args);
                break;
            case "disable":
                await DisablePackageAsync(context, packages, args);
                break;
            case "info":
                ShowPackageInfo(context, packages, args);
                break;
            default:
                NotifyUsage(context);
                break;
        }
    }

    private static async ValueTask EnablePackageAsync(
        AgentTuiCommandContext context,
        IHpdPackageRuntime packages,
        IReadOnlyList<string> args)
    {
        if (!TryReadPackageId(context, args, out var packageId))
        {
            return;
        }

        try
        {
            var loaded = await packages.EnableRegisteredAsync(packageId, HpdPackageScopes.App);
            NotifyPackageResult(context, "Enabled", loaded);
            context.Navigation.GoToPage(PackagesPageId);
        }
        catch (KeyNotFoundException ex)
        {
            Notify(context, ex.Message, TranscriptSeverity.Warning);
        }
    }

    private static async ValueTask ReloadPackageAsync(
        AgentTuiCommandContext context,
        IHpdPackageRuntime packages,
        IReadOnlyList<string> args)
    {
        if (!TryReadPackageId(context, args, out var packageId))
        {
            return;
        }

        var scope = packages.Packages.FirstOrDefault(package =>
            string.Equals(package.Id, packageId, StringComparison.Ordinal))?.Scope ?? HpdPackageScopes.App;
        try
        {
            var loaded = await packages.ReloadRegisteredAsync(packageId, scope);
            NotifyPackageResult(context, "Reloaded", loaded);
            context.Navigation.GoToPage(PackagesPageId);
        }
        catch (KeyNotFoundException ex)
        {
            Notify(context, ex.Message, TranscriptSeverity.Warning);
        }
    }

    private static async ValueTask DisablePackageAsync(
        AgentTuiCommandContext context,
        IHpdPackageRuntime packages,
        IReadOnlyList<string> args)
    {
        if (!TryReadPackageId(context, args, out var packageId))
        {
            return;
        }

        if (await packages.DisableAsync(packageId))
        {
            Notify(context, $"Disabled package `{packageId}`.", TranscriptSeverity.Success);
        }
        else
        {
            Notify(context, $"Package `{packageId}` is not loaded.", TranscriptSeverity.Warning);
        }

        context.Navigation.GoToPage(PackagesPageId);
    }

    private static void ShowPackageInfo(
        AgentTuiCommandContext context,
        IHpdPackageRuntime packages,
        IReadOnlyList<string> args)
    {
        if (!TryReadPackageId(context, args, out var packageId))
        {
            return;
        }

        if (!packages.Packages.Any(package => string.Equals(package.Id, packageId, StringComparison.Ordinal)))
        {
            Notify(context, $"Package `{packageId}` is not loaded.", TranscriptSeverity.Warning);
            return;
        }

        context.Navigation.GoToPage(PackagesPageId);
    }

    private static bool TryReadPackageId(
        AgentTuiCommandContext context,
        IReadOnlyList<string> args,
        out string packageId)
    {
        if (args.Count >= 2)
        {
            packageId = args[1];
            return true;
        }

        packageId = "";
        NotifyUsage(context);
        return false;
    }

    private static void NotifyPackageResult(
        AgentTuiCommandContext context,
        string verb,
        HpdLoadedPackage package)
    {
        var severity = package.State == HpdPackageLoadState.Enabled
            ? TranscriptSeverity.Success
            : TranscriptSeverity.Error;
        Notify(context, $"{verb} package `{package.Id}`: {package.State}.", severity);
    }

    private static void NotifyUsage(AgentTuiCommandContext context)
        => Notify(
            context,
            "Usage: `/package enable <id>`, `/package disable <id>`, `/package reload <id>`, `/package info <id>`.",
            TranscriptSeverity.Warning);

    private static void Notify(
        AgentTuiCommandContext context,
        string message,
        TranscriptSeverity severity)
    {
        context.Shell.Transcript.AddFinal(new TranscriptEntry(
            Id: Guid.NewGuid().ToString("N"),
            EntryKey: null,
            Cell: new NoticeCell(message, Severity: severity),
            Metadata: new TranscriptEntryMetadata()));
    }

    private static IReadOnlyList<string> SplitArgs(string arguments)
        => arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
