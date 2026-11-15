using Microsoft.Extensions.AI;

namespace HPD.Agent;

public partial class AgentBuilder
{
    /// <summary>
    /// Registers a stable, host-owned function retry strategy.
    /// </summary>
    /// <remarks>
    /// Registration supplies executable code. Activate it by setting
    /// <see cref="RetryPolicySection.FunctionCustomRetryStrategy"/> to the same name.
    /// </remarks>
    public AgentBuilder WithFunctionRetryStrategy(
        string name,
        Func<Exception, int, CancellationToken, Task<TimeSpan?>> strategy)
    {
        _runtimeOptions.PolicyExtensions.AddFunctionRetryStrategy(name, strategy);
        return this;
    }

    /// <summary>
    /// Registers a stable, host-owned model retry strategy.
    /// </summary>
    /// <remarks>
    /// Registration supplies executable code. Activate it by setting
    /// <see cref="RetryPolicySection.ModelCustomRetryStrategy"/> to the same name.
    /// </remarks>
    public AgentBuilder WithModelRetryStrategy(
        string name,
        Func<Exception, int, CancellationToken, Task<TimeSpan?>> strategy)
    {
        _runtimeOptions.PolicyExtensions.AddModelRetryStrategy(name, strategy);
        return this;
    }

    /// <summary>
    /// Registers a stable, host-owned function error detector.
    /// </summary>
    /// <remarks>
    /// Registration supplies executable code. Activate it by setting
    /// <see cref="TotalErrorPolicy.CustomErrorDetector"/> to the same name.
    /// </remarks>
    public AgentBuilder WithErrorDetector(
        string name,
        Func<FunctionResultContent, bool> detector)
    {
        _runtimeOptions.PolicyExtensions.AddErrorDetector(name, detector);
        return this;
    }

    /// <summary>
    /// Registers a stable, host-owned external PII detector.
    /// </summary>
    /// <remarks>
    /// Registration supplies executable code. Activate it by setting
    /// <see cref="PIIProtectionPolicy.ExternalDetector"/> to the same name.
    /// </remarks>
    public AgentBuilder WithExternalPIIDetector(
        string name,
        Func<string, CancellationToken, Task<IEnumerable<PIIMatch>>> detector)
    {
        _runtimeOptions.PolicyExtensions.AddExternalPIIDetector(name, detector);
        return this;
    }

    private Func<Exception, int, CancellationToken, Task<TimeSpan?>> ResolveFunctionRetryStrategy(
        string name,
        string path)
    {
        if (_runtimeOptions.PolicyExtensions.TryGetFunctionRetryStrategy(name, out var strategy))
            return strategy!;
        throw MissingPolicyExtension(
            "function_retry_strategy_not_found",
            path,
            $"Function retry strategy '{name}' is not registered. Register it with WithFunctionRetryStrategy before building the agent.");
    }

    private Func<Exception, int, CancellationToken, Task<TimeSpan?>> ResolveModelRetryStrategy(
        string name,
        string path)
    {
        if (_runtimeOptions.PolicyExtensions.TryGetModelRetryStrategy(name, out var strategy))
            return strategy!;
        throw MissingPolicyExtension(
            "model_retry_strategy_not_found",
            path,
            $"Model retry strategy '{name}' is not registered. Register it with WithModelRetryStrategy before building the agent.");
    }

    private Func<FunctionResultContent, bool> ResolveErrorDetector(string name, string path)
    {
        if (_runtimeOptions.PolicyExtensions.TryGetErrorDetector(name, out var detector))
            return detector!;
        throw MissingPolicyExtension(
            "error_detector_not_found",
            path,
            $"Error detector '{name}' is not registered. Register it with WithErrorDetector before building the agent.");
    }

    private Func<string, CancellationToken, Task<IEnumerable<PIIMatch>>> ResolveExternalPIIDetector(
        string name,
        string path)
    {
        if (_runtimeOptions.PolicyExtensions.TryGetExternalPIIDetector(name, out var detector))
            return detector!;
        throw MissingPolicyExtension(
            "pii_detector_not_found",
            path,
            $"External PII detector '{name}' is not registered. Register it with WithExternalPIIDetector before building the agent.");
    }

    private static AgentRunConfigurationException MissingPolicyExtension(
        string code,
        string path,
        string message)
        => new(code, path, message);
}
