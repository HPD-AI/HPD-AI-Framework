using HPD.Agent.Packages;
using HPD.Agent.Providers;
using HPD.Agent.TUI.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.TUI.Console;

internal sealed class ConsolePackageContext
{
    private ConsolePackageContext(
        AgentBuilderContributorStore agentContributors,
        ProviderContributionStore providerContributions,
        AgentTuiPackageManager tuiPackages)
    {
        AgentContributors = agentContributors;
        ProviderContributions = providerContributions;
        TuiPackages = tuiPackages;
    }

    public AgentBuilderContributorStore AgentContributors { get; }

    public ProviderContributionStore ProviderContributions { get; }

    public AgentTuiPackageManager TuiPackages { get; }

    public static ConsolePackageContext Create(AgentTuiContributionStore tuiContributions)
    {
        ArgumentNullException.ThrowIfNull(tuiContributions);

        var services = new ServiceCollection();
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var packages = new HpdPackageManager(
            services,
            new HpdPackageContributionStores(agentContributors, providerContributions));
        var tuiPackages = new AgentTuiPackageManager(
            packages,
            tuiContributions,
            services.BuildServiceProvider());

        tuiPackages.EnableRegisteredPackages(HpdPackageScopes.App);
        return new ConsolePackageContext(
            agentContributors,
            providerContributions,
            tuiPackages);
    }
}
