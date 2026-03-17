using HPD.Agent;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore.Lifecycle;

/// <summary>
/// ASP.NET Core-specific implementation of SessionManager.
/// Handles session/branch lifecycle and stream/session locks.
/// Agent building is handled separately by <see cref="AspNetCoreAgentManager"/>.
/// </summary>
internal class AspNetCoreSessionManager : SessionManager
{
    private readonly IOptionsMonitor<HPDAgentConfig> _optionsMonitor;
    private readonly string _name;

    internal AspNetCoreSessionManager(
        ISessionStore store,
        IOptionsMonitor<HPDAgentConfig> optionsMonitor,
        string name)
        : base(store)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public override bool AllowRecursiveBranchDelete =>
        _optionsMonitor.Get(_name).AllowRecursiveBranchDelete;
}
