using HPD.Agent.Middleware;
using HPD.Agent.Sandbox;
using HPD.Environment.Contracts;
using HPDOS.ToolHarnesses.Middleware;
using System.Linq;

namespace HPD.Agent.ToolHarness.Coding.Tests;

internal static class LanguageServerTestFactory
{
    public static LanguageServerService Create(
        LanguageServerOptions? options = null,
        IEnumerable<ILanguageServerRegistryProvider>? providers = null,
        ILanguageServerToolResolver? resolver = null,
        string? root = null)
    {
        var service = new LanguageServerService(
            options ?? new LanguageServerOptions(),
            providers,
            resolver);
        RuntimeWorkspaceBinding workspace =
            TestRuntimeWorkspace.Create(
                root ??
                    options?.WorkspaceFolders.FirstOrDefault() ??
                    Directory.GetCurrentDirectory(),
                resolveLinks: false);
        service.ConfigureRuntimeAsync(
                new RuntimeProcessExecutionBinding
                {
                    EnvironmentId = "test-language-environment",
                    EnvironmentRevision = 1,
                    ProcessProvider = NeverProcessProvider.Instance,
                    ExecutionTarget =
                        new TargetHandle<ExecutionUnit>(
                            new TargetRoute
                            {
                                Kind = new TargetKind(
                                    "test.language.execution"),
                                Scope = new ResourceScope(
                                    "test.language"),
                            },
                            TargetHandleLifetime.LiveCapability,
                            TargetHandleAuthority.Control |
                                TargetHandleAuthority.Observe),
                },
                workspace)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return service;
    }

    private sealed class NeverProcessProvider : IProcessProvider
    {
        public static NeverProcessProvider Instance { get; } = new();

        public ProviderId ProviderId => new("test.language.process");

        public ValueTask<IProcessInvocationHandle> StartAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "Tests that launch processes must provide a tool resolver.");

        public ValueTask<ProcessInvocationResult> RunAsync(
            ProcessInvocationSpec spec,
            IProcessOutputSink? output = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "Tests that launch processes must provide a tool resolver.");

        public ValueTask SignalAsync(
            TargetHandle<ProcessInvocation> process,
            ProcessSignal signal,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask ResizeTerminalAsync(
            TargetHandle<ProcessInvocation> process,
            TerminalSpec size,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ProcessInvocationResult> WaitAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<ProcessOutputChunk> ReadOutputAsync(
            TargetHandle<ProcessInvocation> process,
            CancellationToken cancellationToken = default) =>
            EmptyOutput(cancellationToken);

        private static async IAsyncEnumerable<ProcessOutputChunk> EmptyOutput(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }
    }
}
