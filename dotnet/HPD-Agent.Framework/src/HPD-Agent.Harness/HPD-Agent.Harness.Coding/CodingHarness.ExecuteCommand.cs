using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using HPD.Agent;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.Middleware;
using HPD.Agent.Security;
using HPD.Agent.ToolHarness.Coding.Security;
using HPD.Environment.Contracts;
using HPD.Events;
using HPD.Events.Core;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;

public partial class CodingToolHarness
{
    private static readonly ConcurrentDictionary<(string SessionId, string CommandId), ExecuteCommandProcessOperation>
        ProcessOperations = new();
    private static readonly Regex AnsiEscapeSequencePattern = new(
        "\\u001B(?:\\[[0-?]*[ -/]*[@-~]|\\][^\\u0007]*(?:\\u0007|\\u001B\\\\)|[@-_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const int DefaultExecuteCommandTimeoutMilliseconds = 120_000;
    private const int MaxExecuteCommandTimeoutMilliseconds = 30 * 60 * 1000;
    private const int MaxInlineCommandOutputChars = 30_000;
    private const int MaxInlineStreamChars = 15_000;
    private const int DefaultExecuteCommandProgressAfterMilliseconds = 2_000;
    private const int DefaultExecuteCommandAutoBackgroundAfterMilliseconds = 15_000;
    private const int DefaultExecuteCommandBackgroundStartSettleMilliseconds = 750;
    private const int MaxExecuteCommandReadOutputDelayMilliseconds = 10_000;
    private const int DefaultExecuteCommandMaxOutputChunkEventChars = 8_000;
    private const int DefaultExecuteCommandMaxOutputChunkEventsPerSecond = 8;
    private const int DefaultExecuteCommandMaxOutputChunkEventsPerCommand = 10_000;
    private const int MaxExecuteCommandTailLines = 2_000;

    /// <summary>
    /// Runs or manages a command using one closed, action-specific request contract.
    /// </summary>
    /// <param name="request">The command operation selected by its action discriminator.</param>
    /// <param name="context">The current function execution context.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The command result or a structured command error.</returns>
    [AIFunction]
    [Description("Runs, lists, inspects, or stops shell commands for the coding workspace. Select exactly one action shape. Use run for builds, tests, project scripts, package managers, git inspection, code generation, formatters, linters, and local servers. Use listBackground, readOutput, or stop only for background commands previously started by this function.")]
    public async Task<object> ExecuteCommand(
        [Description("The closed command operation to perform.")]
        ExecuteCommandOperation request,
        FunctionExecutionContext context = null!,
        CancellationToken cancellationToken = default)
        => request switch
        {
            RunCommandOperation run => await ExecuteCommandCore(
                ExecuteCommandAction.Run,
                run.Command,
                operationId: null,
                run.WorkingDirectory,
                run.TimeoutMilliseconds,
                run.ExecutionMode == CommandExecutionMode.Background,
                tailLines: 200,
                delayMilliseconds: 0,
                run.Environment,
                context,
                cancellationToken).ConfigureAwait(false),
            ListBackgroundCommandsOperation => await ExecuteCommandCore(
                ExecuteCommandAction.ListBackground,
                command: null,
                operationId: null,
                workingDirectory: null,
                DefaultExecuteCommandTimeoutMilliseconds,
                startsInBackground: false,
                tailLines: 200,
                delayMilliseconds: 0,
                environment: null,
                context,
                cancellationToken).ConfigureAwait(false),
            ReadCommandOutputOperation read => await ExecuteCommandCore(
                ExecuteCommandAction.ReadOutput,
                command: null,
                read.OperationId,
                workingDirectory: null,
                DefaultExecuteCommandTimeoutMilliseconds,
                startsInBackground: false,
                read.TailLines,
                read.DelayMilliseconds,
                environment: null,
                context,
                cancellationToken).ConfigureAwait(false),
            StopCommandOperation stop => await ExecuteCommandCore(
                ExecuteCommandAction.Stop,
                command: null,
                stop.OperationId,
                workingDirectory: null,
                DefaultExecuteCommandTimeoutMilliseconds,
                startsInBackground: false,
                tailLines: 200,
                delayMilliseconds: 0,
                environment: null,
                context,
                cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };

    internal async Task<object> ExecuteCommandCore(
        ExecuteCommandAction action = ExecuteCommandAction.Run,
        string? command = null,
        string? operationId = null,
        string? workingDirectory = null,
        int timeoutMilliseconds = DefaultExecuteCommandTimeoutMilliseconds,
        bool startsInBackground = false,
        int tailLines = 200,
        int delayMilliseconds = 0,
        IReadOnlyDictionary<string, string>? environment = null,
        FunctionExecutionContext context = null!,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var normalized = NormalizeExecuteCommandRequest(
            action,
            command,
            operationId,
            workingDirectory,
            timeoutMilliseconds,
            startsInBackground,
            tailLines,
            delayMilliseconds,
            environment,
            context,
            _executeCommandOptions);

        if (normalized.Error is { } error)
            return FormatExecuteCommandError(error);

        if (normalized.Request!.Action == ExecuteCommandAction.ListBackground)
            return ListBackgroundCommands(normalized.Request, context);

        if (normalized.Request.Action == ExecuteCommandAction.ReadOutput)
            return await ReadBackgroundCommandOutputAsync(normalized.Request, context, cancellationToken).ConfigureAwait(false);

        if (normalized.Request.Action == ExecuteCommandAction.Stop)
            return await StopBackgroundCommandAsync(normalized.Request, context, cancellationToken).ConfigureAwait(false);

        if (!context.RuntimeCapabilities.TryGet<RuntimeProcessExecutionBinding>(out _))
        {
            return FormatExecuteCommandError(new ExecuteCommandError(
                ExecuteCommandErrorKind.MissingRunner,
                normalized.Request.Command,
                normalized.Request.WorkingDirectory,
                "The runtime does not expose an authorized process execution binding."));
        }

        if (normalized.Request.StartsInBackground)
            return await RunExecuteCommandBackgroundAsync(normalized.Request, context, cancellationToken).ConfigureAwait(false);

        return await RunExecuteCommandForegroundAsync(
            normalized.Request,
            context,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> RunExecuteCommandBackgroundAsync(
        ExecuteCommandRequest request,
        FunctionExecutionContext context,
        CancellationToken cancellationToken,
        bool allowSandboxCapabilityRetry = true)
    {
        if (string.IsNullOrWhiteSpace(context.SessionId))
        {
            return FormatExecuteCommandError(new ExecuteCommandError(
                ExecuteCommandErrorKind.BackgroundUnavailable,
                request.Command,
                request.WorkingDirectory,
                "Background commands require a session id."));
        }

        if (!context.CanStartOperations)
        {
            return FormatExecuteCommandError(new ExecuteCommandError(
                ExecuteCommandErrorKind.BackgroundUnavailable,
                request.Command,
                request.WorkingDirectory,
                "Background commands require an active agent operation registry."));
        }

        var activeCount = CountRunningBackgroundCommands(context);
        if (activeCount >= _executeCommandOptions.MaxActiveBackgroundCommands)
        {
            return FormatExecuteCommandError(new ExecuteCommandError(
                ExecuteCommandErrorKind.BackgroundLimitExceeded,
                request.Command,
                request.WorkingDirectory,
                $"Active background command limit exceeded for this session ({_executeCommandOptions.MaxActiveBackgroundCommands})."));
        }

        var environmentContext = EnvironmentContext.CreateCurrent();
        var baseCommand = GetBaseCommand(request.Command);
        var category = DetectCommandCategory(baseCommand, request.Command);
        var processSpec = CreateProcessInvocationSpec(
            GetProcessExecutionTarget(context),
            environmentContext.ShellExecutable,
            [.. environmentContext.ShellCommandArgumentsPrefix, request.Command],
            request.WorkingDirectory,
            request.Environment,
            request.Timeout,
            request.Isolation);

        ExecuteCommandOutputStoreSession? outputStore = null;
        IProcessInvocationHandle? handle = null;

        try
        {
            var eventState = new ExecuteCommandEventState(_executeCommandOptions);
            outputStore = await ExecuteCommandOutputStoreSession.CreateAsync(
                request.CommandId,
                request,
                context,
                _executeCommandOptions,
                cancellationToken).ConfigureAwait(false);

            var outputSink = new ExecuteCommandOutputSink(
                context,
                request,
                baseCommand,
                category,
                outputStore,
                eventState);

            handle = await GetProcessExecution(context).ProcessProvider.StartAsync(
                processSpec,
                outputSink,
                cancellationToken).ConfigureAwait(false);

            var background = new ExecuteCommandProcessOperation(
                request.CommandId,
                context.SessionId,
                request,
                environmentContext.ShellExecutable,
                baseCommand,
                category,
                handle,
                outputStore,
                context);

            await context.TryPublishAsync(new ExecuteCommandProcessStartedEvent
            {
                ToolCallId = context.FunctionCallId,
                FunctionName = context.FunctionName,
                EventFlowId = request.CommandId,
                CommandId = request.CommandId,
                Command = request.Command,
                BaseCommand = baseCommand,
                Category = category,
                WorkingDirectory = request.WorkingDirectory,
                Shell = environmentContext.ShellExecutable,
                StartedAt = background.StartedAt,
                Background = true,
                AutoBackgroundEligible = false,
                ProcessId = null,
                TimeoutMilliseconds = (int)request.Timeout.TotalMilliseconds
            }, cancellationToken).ConfigureAwait(false);

            var completionTask = handle.WaitAsync(CancellationToken.None).AsTask();
            var completed = await Task.WhenAny(
                    completionTask,
                    Task.Delay(_executeCommandOptions.BackgroundStartSettleDelay, cancellationToken))
                .ConfigureAwait(false);

            if (completed == completionTask)
            {
                var result = await completionTask.ConfigureAwait(false);
                if (allowSandboxCapabilityRetry &&
                    TryClassifySandboxCapabilityDenial(request, result, out var capability, out var resource, out var failureSummary) &&
                    await RequestSandboxCapabilityAsync(
                        request,
                        context,
                        capability,
                        resource,
                        failureSummary,
                        cancellationToken).ConfigureAwait(false))
                {
                    await CleanupFailedBackgroundStartAsync(handle, outputStore, cancellationToken).ConfigureAwait(false);
                    handle = null;
                    outputStore = null;
                    var amended = request with
                    {
                        CommandId = $"cmd_{Guid.NewGuid():N}",
                        Isolation = ApplySandboxAmendment(request.Isolation, capability, resource)
                    };
                    return await RunExecuteCommandBackgroundAsync(
                        amended,
                        context,
                        cancellationToken,
                        allowSandboxCapabilityRetry: false).ConfigureAwait(false);
                }

                var duration = DateTimeOffset.UtcNow - background.StartedAt;
                var outputMetadata = await outputStore.CompleteAsync(
                    result,
                    environmentContext.ShellExecutable,
                    cancellationToken).ConfigureAwait(false);
                await EmitExecuteCommandProcessExitedEventAsync(
                    context,
                    request,
                    baseCommand,
                    category,
                    result,
                    outputMetadata,
                    duration,
                    cancellationToken).ConfigureAwait(false);

                await handle.DisposeAsync().ConfigureAwait(false);
                await outputStore.DisposeAsync().ConfigureAwait(false);
                handle = null;
                outputStore = null;

                return FormatExecuteCommandResult(
                    request,
                    environmentContext.ShellExecutable,
                    category,
                    baseCommand,
                    result,
                    outputMetadata,
                    duration);
            }

            await RegisterBackgroundProcessAsync(context, background, cancellationToken).ConfigureAwait(false);

            return FormatExecuteCommandBackgroundStarted(
                request,
                background.OperationId!,
                background.OutputStore.CombinedPath);
        }
        catch (Exception ex) when (IsMissingProcessProviderException(ex))
        {
            await CleanupFailedBackgroundStartAsync(handle, outputStore, cancellationToken).ConfigureAwait(false);
            return FormatExecuteCommandError(new ExecuteCommandError(
                ExecuteCommandErrorKind.MissingRunner,
                request.Command,
                request.WorkingDirectory,
                "No IProcessProvider runtime capability is available."));
        }
        catch (Exception ex)
        {
            await CleanupFailedBackgroundStartAsync(handle, outputStore, cancellationToken).ConfigureAwait(false);
            return FormatExecuteCommandError(new ExecuteCommandError(
                ExecuteCommandErrorKind.StartFailed,
                request.Command,
                request.WorkingDirectory,
                ex.Message));
        }
    }

    private async Task<string> RunExecuteCommandForegroundAsync(
        ExecuteCommandRequest request,
        FunctionExecutionContext context,
        CancellationToken cancellationToken,
        bool allowSandboxCapabilityRetry = true)
    {
        var environmentContext = EnvironmentContext.CreateCurrent();
        var baseCommand = GetBaseCommand(request.Command);
        var category = DetectCommandCategory(baseCommand, request.Command);
        var processSpec = CreateProcessInvocationSpec(
            GetProcessExecutionTarget(context),
            environmentContext.ShellExecutable,
            [.. environmentContext.ShellCommandArgumentsPrefix, request.Command],
            request.WorkingDirectory,
            request.Environment,
            request.Timeout,
            request.Isolation);

        var stopwatch = Stopwatch.StartNew();
        ExecuteCommandOutputStoreSession? outputStore = null;
        IProcessInvocationHandle? handle = null;
        var ownershipTransferred = false;

        try
        {
            var eventState = new ExecuteCommandEventState(_executeCommandOptions);
            outputStore = await ExecuteCommandOutputStoreSession.CreateAsync(
                request.CommandId,
                request,
                context,
                _executeCommandOptions,
                cancellationToken).ConfigureAwait(false);

            var outputSink = new ExecuteCommandOutputSink(
                context,
                request,
                baseCommand,
                category,
                outputStore,
                eventState);

            handle = await GetProcessExecution(context).ProcessProvider.StartAsync(
                processSpec,
                outputSink,
                cancellationToken).ConfigureAwait(false);

            await context.TryPublishAsync(new ExecuteCommandProcessStartedEvent
            {
                ToolCallId = context.FunctionCallId,
                FunctionName = context.FunctionName,
                EventFlowId = request.CommandId,
                CommandId = request.CommandId,
                Command = request.Command,
                BaseCommand = baseCommand,
                Category = category,
                WorkingDirectory = request.WorkingDirectory,
                Shell = environmentContext.ShellExecutable,
                StartedAt = DateTimeOffset.UtcNow,
                Background = false,
                AutoBackgroundEligible = false,
                ProcessId = null,
                TimeoutMilliseconds = (int)request.Timeout.TotalMilliseconds
            }, cancellationToken).ConfigureAwait(false);

            await eventState.TryEmitProgressAsync(
                context,
                request,
                baseCommand,
                category,
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);

            if (_executeCommandOptions.AutoBackgroundAfter is { } autoBackgroundAfter &&
                autoBackgroundAfter >= TimeSpan.Zero)
            {
                var completionTask = handle.WaitAsync(CancellationToken.None).AsTask();
                var completed = await Task.WhenAny(
                        completionTask,
                        Task.Delay(autoBackgroundAfter, cancellationToken))
                    .ConfigureAwait(false);
            if (completed != completionTask)
            {
                await eventState.TryEmitProgressAsync(
                    context,
                    request,
                    baseCommand,
                    category,
                    stopwatch.Elapsed,
                    cancellationToken).ConfigureAwait(false);

                var background = await TryRegisterExistingHandleAsBackgroundAsync(
                        request,
                        context,
                        environmentContext.ShellExecutable,
                        baseCommand,
                        category,
                        handle,
                        outputStore,
                        context.InvocationSnapshot,
                        cancellationToken).ConfigureAwait(false);

                    if (background is not null)
                    {
                        ownershipTransferred = true;
                        await context.TryPublishAsync(new ExecuteCommandAutoBackgroundedEvent
                        {
                            ToolCallId = context.FunctionCallId,
                            FunctionName = context.FunctionName,
                            EventFlowId = request.CommandId,
                            CommandId = request.CommandId,
                            Command = request.Command,
                            BaseCommand = baseCommand,
                            Category = category,
                            WorkingDirectory = request.WorkingDirectory,
                            OperationId = background.OperationId!,
                            BackgroundedAt = DateTimeOffset.UtcNow,
                            ElapsedMilliseconds = (long)stopwatch.Elapsed.TotalMilliseconds
                        }, cancellationToken).ConfigureAwait(false);

                        return FormatExecuteCommandBackgroundStarted(
                            request,
                            background.OperationId!,
                            background.OutputStore.CombinedPath,
                            autoBackgrounded: true);
                    }
                }
            }

            var result = await handle.WaitAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            if (allowSandboxCapabilityRetry &&
                TryClassifySandboxCapabilityDenial(request, result, out var capability, out var resource, out var failureSummary) &&
                await RequestSandboxCapabilityAsync(
                    request,
                    context,
                    capability,
                    resource,
                    failureSummary,
                    cancellationToken).ConfigureAwait(false))
            {
                var amended = request with
                {
                    CommandId = $"cmd_{Guid.NewGuid():N}",
                    Isolation = ApplySandboxAmendment(request.Isolation, capability, resource)
                };
                return await RunExecuteCommandForegroundAsync(
                    amended,
                    context,
                    cancellationToken,
                    allowSandboxCapabilityRetry: false).ConfigureAwait(false);
            }

            var outputMetadata = await outputStore.CompleteAsync(
                result,
                environmentContext.ShellExecutable,
                cancellationToken).ConfigureAwait(false);
            await EmitExecuteCommandProcessExitedEventAsync(
                context,
                request,
                baseCommand,
                category,
                result,
                outputMetadata,
                stopwatch.Elapsed,
                cancellationToken).ConfigureAwait(false);

            return FormatExecuteCommandResult(
                request,
                environmentContext.ShellExecutable,
                category,
                baseCommand,
                result,
                outputMetadata,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return FormatExecuteCommandError(new ExecuteCommandError(
                ExecuteCommandErrorKind.Cancelled,
                request.Command,
                request.WorkingDirectory,
                "Command execution was cancelled."));
        }
        catch (Exception ex) when (IsMissingProcessProviderException(ex))
        {
            stopwatch.Stop();
            return FormatExecuteCommandError(new ExecuteCommandError(
                ExecuteCommandErrorKind.MissingRunner,
                request.Command,
                request.WorkingDirectory,
                "No IProcessProvider runtime capability is available."));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return FormatExecuteCommandError(new ExecuteCommandError(
                ExecuteCommandErrorKind.StartFailed,
                request.Command,
                request.WorkingDirectory,
                ex.Message));
        }
        finally
        {
            if (!ownershipTransferred)
            {
                if (handle is not null)
                    await handle.DisposeAsync().ConfigureAwait(false);
                if (outputStore is not null)
                    await outputStore.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static bool TryClassifySandboxCapabilityDenial(
        ExecuteCommandRequest request,
        ProcessInvocationResult result,
        out AgentCapabilityKind capability,
        out AgentCapabilityResource? resource,
        out string failureSummary)
    {
        capability = default;
        resource = null;
        failureSummary = "";

        if (request.Isolation.Mode != ProcessIsolationMode.Isolated)
            return false;

        var violation = result.Violations.FirstOrDefault();
        if (violation is not null)
        {
            var normalizedType = violation.Type.Replace("_", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal)
                .ToLowerInvariant();
            AgentCapabilityKind? classified = normalizedType switch
            {
                var type when type.Contains("filesystemread", StringComparison.Ordinal) =>
                    AgentCapabilityKind.FilesystemRead,
                var type when type.Contains("filesystemwrite", StringComparison.Ordinal) =>
                    AgentCapabilityKind.FilesystemWrite,
                var type when type.Contains("network", StringComparison.Ordinal) =>
                    AgentCapabilityKind.NetworkEgress,
                var type when type.Contains("localbind", StringComparison.Ordinal) =>
                    AgentCapabilityKind.LocalBinding,
                _ => null
            };
            if (classified is { } recognized)
            {
                capability = recognized;
                if (!string.IsNullOrWhiteSpace(violation.Path))
                {
                    var canonical = Path.GetFullPath(violation.Path);
                    resource = new AgentCapabilityResource
                    {
                        Value = canonical,
                        DisplayName = Path.GetFileName(canonical)
                    };
                }
                failureSummary = string.IsNullOrWhiteSpace(violation.Message)
                    ? "The operation exceeded its sandbox capabilities."
                    : violation.Message;
                return true;
            }
        }

        if (request.Isolation.Interactive.AllowLocalBinding)
            return false;

        var combined = GetCapturedText(result);
        if (string.IsNullOrWhiteSpace(combined))
            return false;

        var text = combined.ToLowerInvariant();
        var hasPermissionDenial = text.Contains("operation not permitted", StringComparison.Ordinal) ||
                                  text.Contains("permission denied", StringComparison.Ordinal) ||
                                  text.Contains("eperm", StringComparison.Ordinal) ||
                                  text.Contains("eacces", StringComparison.Ordinal);
        var hasBindSignal = text.Contains("listen", StringComparison.Ordinal) ||
                            text.Contains("bind", StringComparison.Ordinal) ||
                            text.Contains("localhost", StringComparison.Ordinal) ||
                            text.Contains("127.0.0.1", StringComparison.Ordinal) ||
                            text.Contains("0.0.0.0", StringComparison.Ordinal);

        if (!hasPermissionDenial || !hasBindSignal)
            return false;

        capability = AgentCapabilityKind.LocalBinding;
        failureSummary = "The command failed while trying to bind a local port under the sandbox.";
        return true;
    }

    private static string GetCapturedText(ProcessInvocationResult result)
    {
        var stdout = DecodeCapturedText(result.Output.Stdout.CapturedBytes);
        var stderr = DecodeCapturedText(result.Output.Stderr.CapturedBytes);
        return string.Concat(stdout, "\n", stderr);
    }

    private static string DecodeCapturedText(ReadOnlyMemory<byte> bytes)
        => bytes.IsEmpty ? "" : Encoding.UTF8.GetString(bytes.Span);

    private static async Task<bool> RequestSandboxCapabilityAsync(
        ExecuteCommandRequest request,
        FunctionExecutionContext context,
        AgentCapabilityKind capability,
        AgentCapabilityResource? resource,
        string failureSummary,
        CancellationToken cancellationToken)
    {
        if (context.RunConfig.Security.Sandbox.Escape == AgentSandboxEscapePolicy.Deny)
            return false;

        var requestId = Guid.NewGuid().ToString("N");
        try
        {
            var response = await context.RequestAsync<AgentCapabilityRequestEvent, AgentCapabilityResponseEvent>(
                    new AgentCapabilityRequestEvent(
                        requestId,
                        nameof(ExecuteCommand),
                        context.FunctionCallId,
                        request.CommandId,
                        capability,
                        resource,
                        failureSummary),
                    cancellationToken,
                    timeout: null)
                .ConfigureAwait(false);
            return response.Approved;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static ProcessIsolationPolicy ApplySandboxAmendment(
        ProcessIsolationPolicy policy,
        AgentCapabilityKind capability,
        AgentCapabilityResource? resource)
        => capability switch
        {
            AgentCapabilityKind.LocalBinding => policy with
            {
                Interactive = policy.Interactive with { AllowLocalBinding = true }
            },
            AgentCapabilityKind.NetworkEgress => policy with
            {
                Network = policy.Network with { Mode = NetworkEgressMode.Unrestricted }
            },
            AgentCapabilityKind.InteractiveTerminal => policy with
            {
                Interactive = policy.Interactive with { AllowPty = true }
            },
            AgentCapabilityKind.UnsandboxedExecution => policy with
            {
                Mode = ProcessIsolationMode.Disabled
            },
            AgentCapabilityKind.FilesystemRead when resource is not null => policy with
            {
                Filesystem = policy.Filesystem with
                {
                    Rules =
                    [
                        .. policy.Filesystem.Rules,
                        new PathAccessRule
                        {
                            Kind = PathAccessRuleKind.AllowRead,
                            Path = new HostPath(resource.Value),
                            Reason = "Approved agent capability"
                        }
                    ]
                }
            },
            AgentCapabilityKind.FilesystemWrite when resource is not null => policy with
            {
                Filesystem = policy.Filesystem with
                {
                    Rules =
                    [
                        .. policy.Filesystem.Rules,
                        new PathAccessRule
                        {
                            Kind = PathAccessRuleKind.AllowWrite,
                            Path = new HostPath(resource.Value),
                            Reason = "Approved agent capability"
                        }
                    ]
                }
            },
            _ => policy
        };

    internal static ExecuteCommandNormalizationResult NormalizeExecuteCommandRequest(
        ExecuteCommandAction action,
        string? command,
        string? operationId,
        string? workingDirectory,
        int timeoutMilliseconds,
        bool startsInBackground,
        int tailLines,
        int delayMilliseconds,
        IReadOnlyDictionary<string, string>? environment,
        FunctionExecutionContext context,
        ExecuteCommandOptions options)
        => NormalizeExecuteCommandRequest(
            action,
            command,
            operationId,
            workingDirectory,
            timeoutMilliseconds,
            startsInBackground,
            tailLines,
            delayMilliseconds,
            environment,
            context.RunConfig,
            options);

    internal static ExecuteCommandNormalizationResult NormalizeExecuteCommandRequest(
        ExecuteCommandAction action,
        string? command,
        string? operationId,
        string? workingDirectory,
        int timeoutMilliseconds,
        bool startsInBackground,
        int tailLines,
        int delayMilliseconds,
        IReadOnlyDictionary<string, string>? environment,
        AgentRunConfig runConfig,
        ExecuteCommandOptions options)
    {
        if (timeoutMilliseconds <= 0)
        {
            return InvalidArguments(command, workingDirectory, "timeoutMilliseconds must be positive.");
        }

        if (timeoutMilliseconds > (int)options.MaxTimeout.TotalMilliseconds)
        {
            return InvalidArguments(
                command,
                workingDirectory,
                $"timeoutMilliseconds must be less than or equal to {(int)options.MaxTimeout.TotalMilliseconds}.");
        }

        if (tailLines <= 0 || tailLines > MaxExecuteCommandTailLines)
            return InvalidArguments(command, workingDirectory, $"tailLines must be between 1 and {MaxExecuteCommandTailLines}.");

        if (delayMilliseconds < 0 || delayMilliseconds > (int)options.MaxReadOutputDelay.TotalMilliseconds)
        {
            return InvalidArguments(
                command,
                workingDirectory,
                $"delayMilliseconds must be between 0 and {(int)options.MaxReadOutputDelay.TotalMilliseconds}.");
        }

        var argumentError = ValidateExecuteCommandActionArguments(
            action,
            command,
            operationId,
            workingDirectory,
            environment,
            startsInBackground,
            tailLines,
            delayMilliseconds);
        if (argumentError is not null)
            return InvalidArguments(command, workingDirectory, argumentError);

        var cwd = ResolveExecuteCommandWorkingDirectory(workingDirectory, runConfig);
        if (cwd.Error is { } cwdError)
        {
            return new ExecuteCommandNormalizationResult(null, cwdError with
            {
                Command = command,
                WorkingDirectory = cwd.WorkingDirectory
            });
        }

        AgentSandboxRuntime sandbox;
        try
        {
            sandbox = CodingSandboxRuntime.Capture(runConfig);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or JsonException)
        {
            return InvalidArguments(command, cwd.WorkingDirectory, ex.Message);
        }

        var effectiveEnvironment = BuildExecuteCommandEnvironment(environment, options);

        return new ExecuteCommandNormalizationResult(
            new ExecuteCommandRequest(
                $"cmd_{Guid.NewGuid():N}",
                action,
                command?.Trim() ?? string.Empty,
                operationId,
                cwd.WorkingDirectory!,
                TimeSpan.FromMilliseconds(timeoutMilliseconds),
                startsInBackground,
                tailLines,
                TimeSpan.FromMilliseconds(delayMilliseconds),
                effectiveEnvironment,
                sandbox.ToProcessIsolationPolicy(cwd.WorkingDirectory!)),
            null);
    }

    private static string? ValidateExecuteCommandActionArguments(
        ExecuteCommandAction action,
        string? command,
        string? operationId,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        bool startsInBackground,
        int tailLines,
        int delayMilliseconds)
    {
        return action switch
        {
            ExecuteCommandAction.Run when string.IsNullOrWhiteSpace(command)
                => "Run requires command.",
            ExecuteCommandAction.Run when !string.IsNullOrWhiteSpace(operationId)
                => "Run does not accept operationId.",
            ExecuteCommandAction.ListBackground when !string.IsNullOrWhiteSpace(command) ||
                                                     !string.IsNullOrWhiteSpace(operationId) ||
                                                     !string.IsNullOrWhiteSpace(workingDirectory) ||
                                                     environment is not null ||
                                                     startsInBackground ||
                                                     tailLines != 200 ||
                                                     delayMilliseconds != 0
                => "ListBackground accepts no command, operationId, workingDirectory, environment, executionMode, tailLines, or delayMilliseconds arguments.",
            ExecuteCommandAction.ReadOutput when string.IsNullOrWhiteSpace(operationId)
                => "ReadOutput requires operationId.",
            ExecuteCommandAction.ReadOutput when !string.IsNullOrWhiteSpace(command) ||
                                                !string.IsNullOrWhiteSpace(workingDirectory) ||
                                                environment is not null ||
                                                startsInBackground
                => "ReadOutput does not accept command, workingDirectory, environment, or executionMode.",
            ExecuteCommandAction.Stop when string.IsNullOrWhiteSpace(operationId)
                => "Stop requires operationId.",
            ExecuteCommandAction.Stop when !string.IsNullOrWhiteSpace(command) ||
                                          !string.IsNullOrWhiteSpace(workingDirectory) ||
                                          environment is not null ||
                                          startsInBackground ||
                                          tailLines != 200 ||
                                          delayMilliseconds != 0
                => "Stop does not accept command, workingDirectory, environment, executionMode, tailLines, or delayMilliseconds arguments.",
            _ => null
        };
    }

    private static ExecuteCommandWorkingDirectoryResult ResolveExecuteCommandWorkingDirectory(
        string? workingDirectory,
        FunctionExecutionContext context)
        => ResolveExecuteCommandWorkingDirectory(workingDirectory, context.RunConfig);

    private static ExecuteCommandWorkingDirectoryResult ResolveExecuteCommandWorkingDirectory(
        string? workingDirectory,
        AgentRunConfig runConfig)
    {
        string fullPath;
        try
        {
            var workspace = AgentWorkspace.From(runConfig);
            fullPath = string.IsNullOrWhiteSpace(workingDirectory)
                ? workspace.DefaultRootPath
                : workspace.CanonicalizeExplicitPath(workingDirectory);
        }
        catch (AgentWorkspaceException ex)
        {
            return new ExecuteCommandWorkingDirectoryResult(
                workingDirectory,
                new ExecuteCommandError(
                    ToExecuteCommandWorkspaceErrorKind(ex.Kind),
                    null,
                    workingDirectory,
                    ex.Message));
        }

        if (File.Exists(fullPath))
        {
            return new ExecuteCommandWorkingDirectoryResult(
                fullPath,
                new ExecuteCommandError(
                    ExecuteCommandErrorKind.WorkingDirectoryIsFile,
                    null,
                    fullPath,
                    "Working directory is a file."));
        }

        if (!Directory.Exists(fullPath))
        {
            return new ExecuteCommandWorkingDirectoryResult(
                fullPath,
                new ExecuteCommandError(
                    ExecuteCommandErrorKind.WorkingDirectoryNotFound,
                    null,
                    fullPath,
                    "Working directory does not exist."));
        }

        return new ExecuteCommandWorkingDirectoryResult(fullPath, null);
    }

    private static ExecuteCommandErrorKind ToExecuteCommandWorkspaceErrorKind(AgentWorkspaceErrorKind kind)
        => kind switch
        {
            AgentWorkspaceErrorKind.PathOutsideWorkspace or AgentWorkspaceErrorKind.UnknownRootId
                => ExecuteCommandErrorKind.WorkingDirectoryNotFound,
            _ => ExecuteCommandErrorKind.InvalidWorkspace
        };

    private static IReadOnlyDictionary<string, string?> BuildExecuteCommandEnvironment(
        IReadOnlyDictionary<string, string>? environment,
        ExecuteCommandOptions options)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (options.DisablePagers)
        {
            values["PAGER"] = "cat";
            values["GIT_PAGER"] = "cat";
        }

        if (options.DisableInteractivePrompts)
        {
            values["GIT_TERMINAL_PROMPT"] = "0";
            values["GIT_ASKPASS"] = "";
            values["SSH_ASKPASS"] = "";
            values["GH_PROMPT_DISABLED"] = "1";
            values["GCM_INTERACTIVE"] = "never";
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
                values[key] = value;
        }

        return values;
    }

    private static string FormatExecuteCommandResult(
        ExecuteCommandRequest request,
        string shell,
        ExecuteCommandCategory category,
        string baseCommand,
        ProcessInvocationResult result,
        ExecuteCommandOutputStoreMetadata outputMetadata,
        TimeSpan duration)
    {
        var stdout = BuildExecuteCommandStreamResult(result.Output.Stdout);
        var stderr = BuildExecuteCommandStreamResult(result.Output.Stderr);
        var interpretation = InterpretCommandResult(request.Command, result.ExitCode, result.CompletionKind);
        var builder = new StringBuilder();
        using var writer = CreateCodingToolHarnessXmlWriter(builder);

        writer.WriteStartElement("execute_command");
        writer.WriteAttributeString("command", request.Command);
        writer.WriteAttributeString("cwd", request.WorkingDirectory);
        writer.WriteAttributeString("shell", shell);
        writer.WriteAttributeString("category", FormatEnum(category));
        writer.WriteAttributeString("base_command", baseCommand);
        if (result.ExitCode is not null)
            writer.WriteAttributeString("exit_code", result.ExitCode.Value.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("completion_kind", FormatEnum(ToExecuteCommandCompletionKind(result.CompletionKind)));
        writer.WriteAttributeString("duration_ms", ((long)duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("timed_out", FormatBool(result.CompletionKind == ProcessCompletionKind.TimedOut));
        writer.WriteAttributeString("interrupted", FormatBool(result.CompletionKind is ProcessCompletionKind.Cancelled or ProcessCompletionKind.Stopped));
        writer.WriteAttributeString("output_drain_timed_out", FormatBool(result.Output.OutputDrainTimedOut));
        if (IsNoOutputExpected(baseCommand, result))
            writer.WriteAttributeString("no_output_expected", "true");

        WriteExecuteCommandStreamElement(writer, "stdout", stdout, result.Output.Stdout, outputMetadata.Stdout);
        WriteExecuteCommandStreamElement(writer, "stderr", stderr, result.Output.Stderr, outputMetadata.Stderr);

        writer.WriteStartElement("combined_output");
        WriteExecuteCommandOutputHandleAttributes(writer, outputMetadata.Combined);
        writer.WriteEndElement();

        WriteExecuteCommandOutputStoreElement(writer, outputMetadata);

        if (interpretation.Message is not null)
        {
            writer.WriteStartElement("interpretation");
            writer.WriteAttributeString("is_error", FormatBool(interpretation.IsError));
            writer.WriteString(interpretation.Message);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private static string FormatExecuteCommandBackgroundStarted(
        ExecuteCommandRequest request,
        string operationId,
        string outputPath,
        bool autoBackgrounded = false)
    {
        var builder = new StringBuilder();
        using var writer = CreateCodingToolHarnessXmlWriter(builder);

        writer.WriteStartElement("execute_command");
        writer.WriteAttributeString("command", request.Command);
        writer.WriteAttributeString("cwd", request.WorkingDirectory);
        writer.WriteAttributeString("background", "true");
        if (autoBackgrounded)
            writer.WriteAttributeString("auto_backgrounded", "true");
        writer.WriteAttributeString("operation_id", operationId);
        writer.WriteAttributeString("output_path", outputPath);
        writer.WriteAttributeString("startup_status", "launched_not_verified");
        writer.WriteStartElement("verification_hint");
        writer.WriteString("Background start only means the process launched. Use ExecuteCommand with a readOutput request containing operationId and delayMilliseconds to verify server readiness before telling the user it is running.");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private async ValueTask<ExecuteCommandProcessOperation?> TryRegisterExistingHandleAsBackgroundAsync(
        ExecuteCommandRequest request,
        FunctionExecutionContext context,
        string shell,
        string baseCommand,
        ExecuteCommandCategory category,
        IProcessInvocationHandle handle,
        ExecuteCommandOutputStoreSession outputStore,
        FunctionInvocationSnapshot invocation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.SessionId) ||
            !context.CanStartOperations)
        {
            return null;
        }

        var activeCount = CountRunningBackgroundCommands(context);
        if (activeCount >= _executeCommandOptions.MaxActiveBackgroundCommands)
            return null;

        var background = new ExecuteCommandProcessOperation(
            request.CommandId,
            context.SessionId,
            request,
            shell,
            baseCommand,
            category,
            handle,
            outputStore,
            context);

        try
        {
            await RegisterBackgroundProcessAsync(context, background, cancellationToken).ConfigureAwait(false);
            return background;
        }
        catch
        {
            return null;
        }
    }

    private static async ValueTask RegisterBackgroundProcessAsync(
        FunctionExecutionContext context,
        ExecuteCommandProcessOperation operation,
        CancellationToken cancellationToken)
    {
        if (!ProcessOperations.TryAdd((operation.SessionId, operation.CommandId), operation))
            throw new InvalidOperationException($"Command '{operation.CommandId}' is already registered.");
        try
        {
            var receipt = await context.StartOperationAsync(
                "ExecuteCommand",
                operation.Metadata,
                new AgentOperationNotificationPolicy { IncludeTerminal = true },
                (_, operationToken) => operation.ObserveCompletionAsync(operationToken),
                cancellationToken).ConfigureAwait(false);
            operation.OperationId = receipt.OperationId;
        }
        catch
        {
            ProcessOperations.TryRemove((operation.SessionId, operation.CommandId), out _);
            throw;
        }
    }

    private static string ListBackgroundCommands(
        ExecuteCommandRequest request,
        FunctionExecutionContext context)
    {
        var commands = ListRegisteredBackgroundCommands(context)
            .OrderByDescending(process => process.StartedAt)
            .ToArray();

        var builder = new StringBuilder();
        using var writer = CreateCodingToolHarnessXmlWriter(builder);

        writer.WriteStartElement("execute_command_background");
        writer.WriteAttributeString("count", commands.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var command in commands)
            WriteBackgroundCommandElement(writer, command);
        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private static async Task<string> ReadBackgroundCommandOutputAsync(
        ExecuteCommandRequest request,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (request.Delay > TimeSpan.Zero)
            await Task.Delay(request.Delay, cancellationToken).ConfigureAwait(false);

        if (!TryGetBackgroundCommand(request.OperationId, context, out var background))
        {
            return FormatExecuteCommandError(new ExecuteCommandError(
                ExecuteCommandErrorKind.OperationNotFound,
                null,
                request.WorkingDirectory,
                "Background command was not found for this session."));
        }

        await background.FlushOutputAsync(cancellationToken).ConfigureAwait(false);
        string tail;
        try
        {
            tail = await background.OutputStore.ReadCombinedTailAsync(request.TailLines, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return FormatExecuteCommandError(new ExecuteCommandError(
                ExecuteCommandErrorKind.OutputStoreFailed,
                background.Request.Command,
                background.Request.WorkingDirectory,
                $"Failed to read background command output: {ex.Message}"));
        }

        var metadata = background.OutputMetadata;

        var builder = new StringBuilder();
        using var writer = CreateCodingToolHarnessXmlWriter(builder);

        writer.WriteStartElement("execute_command_output");
        writer.WriteAttributeString("operation_id", background.CommandId);
        writer.WriteAttributeString("command", background.Request.Command);
        writer.WriteAttributeString("cwd", background.Request.WorkingDirectory);
        writer.WriteAttributeString("status", FormatEnum(background.Status));
        if (background.ExitCode is not null)
            writer.WriteAttributeString("exit_code", background.ExitCode.Value.ToString(CultureInfo.InvariantCulture));
        if (background.CompletedAt is not null)
            writer.WriteAttributeString("completed_at", background.CompletedAt.Value.ToString("O", CultureInfo.InvariantCulture));

        writer.WriteStartElement("combined_output");
        writer.WriteAttributeString("tail_lines", request.TailLines.ToString(CultureInfo.InvariantCulture));
        if (metadata is not null)
            WriteExecuteCommandOutputHandleAttributes(writer, metadata.Combined);
        else
            writer.WriteAttributeString("local_path", background.OutputStore.CombinedPath);
        writer.WriteString(SanitizeTerminalOutputForXml(tail));
        writer.WriteEndElement();

        if (metadata is not null)
            WriteExecuteCommandOutputStoreElement(writer, metadata);

        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private static async Task<string> StopBackgroundCommandAsync(
        ExecuteCommandRequest request,
        FunctionExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!TryGetBackgroundCommand(request.OperationId, context, out var background))
        {
            return FormatExecuteCommandError(new ExecuteCommandError(
                ExecuteCommandErrorKind.OperationNotFound,
                null,
                request.WorkingDirectory,
                "Background command was not found for this session."));
        }

        background.SuppressFinalStateNotification("handled-by-foreground-stop");

        if (background.Status == ExecuteCommandProcessOperationStatus.Running)
            await background.Process.StopAsync(
                new ProcessStopRequest(StopKind.GracefulThenKill, "requested"),
                cancellationToken).ConfigureAwait(false);

        var result = await background.WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);
        var outputMetadata = background.OutputMetadata;

        var builder = new StringBuilder();
        using var writer = CreateCodingToolHarnessXmlWriter(builder);

        writer.WriteStartElement("execute_command_stop");
        writer.WriteAttributeString("operation_id", background.CommandId);
        writer.WriteAttributeString("command", background.Request.Command);
        writer.WriteAttributeString("cwd", background.Request.WorkingDirectory);
        writer.WriteAttributeString("status", FormatEnum(background.Status));
        if (result.ExitCode is not null)
            writer.WriteAttributeString("exit_code", result.ExitCode.Value.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("completion_kind", FormatEnum(ToExecuteCommandCompletionKind(result.CompletionKind)));
        if (outputMetadata is not null)
        {
            writer.WriteStartElement("combined_output");
            WriteExecuteCommandOutputHandleAttributes(writer, outputMetadata.Combined);
            writer.WriteEndElement();
            WriteExecuteCommandOutputStoreElement(writer, outputMetadata);
        }
        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private static async ValueTask EmitExecuteCommandProcessExitedEventAsync(
        FunctionExecutionContext context,
        ExecuteCommandRequest request,
        string baseCommand,
        ExecuteCommandCategory category,
        ProcessInvocationResult result,
        ExecuteCommandOutputStoreMetadata outputMetadata,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await context.TryPublishAsync(new ExecuteCommandProcessExitedEvent
        {
            ToolCallId = context.FunctionCallId,
            FunctionName = context.FunctionName,
            EventFlowId = request.CommandId,
            CommandId = request.CommandId,
            Command = request.Command,
            BaseCommand = baseCommand,
            Category = category,
            WorkingDirectory = request.WorkingDirectory,
            ExitCode = result.ExitCode,
            CompletionKind = ToExecuteCommandCompletionKind(result.CompletionKind),
            DurationMilliseconds = (long)duration.TotalMilliseconds,
            StdoutBytes = result.Output.Stdout.BytesObserved,
            StderrBytes = result.Output.Stderr.BytesObserved,
            CombinedOutputBytes = result.Output.Stdout.BytesObserved + result.Output.Stderr.BytesObserved,
            StdoutBytesDiscarded = result.Output.Stdout.BytesDiscarded,
            StderrBytesDiscarded = result.Output.Stderr.BytesDiscarded,
            CombinedBytesDiscarded = result.Output.Stdout.BytesDiscarded + result.Output.Stderr.BytesDiscarded,
            OutputTruncated = result.Output.Stdout.Truncated || result.Output.Stderr.Truncated || outputMetadata.Stdout.Truncated || outputMetadata.Stderr.Truncated,
            OutputDrainTimedOut = result.Output.OutputDrainTimedOut,
            OutputEventsSuppressed = false,
            StdoutArtifactPath = outputMetadata.Stdout.ArtifactPath,
            StderrArtifactPath = outputMetadata.Stderr.ArtifactPath,
            CombinedOutputArtifactPath = outputMetadata.Combined.ArtifactPath,
            StdoutContentId = outputMetadata.Stdout.ContentId,
            StderrContentId = outputMetadata.Stderr.ContentId,
            CombinedOutputContentId = outputMetadata.Combined.ContentId,
            StdoutLocalPath = outputMetadata.Stdout.LocalPath,
            StderrLocalPath = outputMetadata.Stderr.LocalPath,
            CombinedOutputLocalPath = outputMetadata.Combined.LocalPath
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static async ValueTask EmitExecuteCommandOutputChunkEventAsync(
        FunctionExecutionContext context,
        ExecuteCommandRequest request,
        string baseCommand,
        ExecuteCommandCategory category,
        ProcessOutputChunk evt,
        ExecuteCommandEventState state,
        CancellationToken cancellationToken)
    {
        var observation = state.Observe(evt.Stream, evt.Bytes.Length);
        if (!state.TryReserveOutputEvent())
            return;

        var text = DecodeOutputEventText(
            evt.Bytes.Span,
            state.MaxOutputChunkEventChars,
            out var binary,
            out var truncated);

        await context.TryPublishAsync(new ExecuteCommandOutputChunkEvent
        {
            ToolCallId = context.FunctionCallId,
            FunctionName = context.FunctionName,
            EventFlowId = request.CommandId,
            CommandId = request.CommandId,
            Command = request.Command,
            BaseCommand = baseCommand,
            Category = category,
            WorkingDirectory = request.WorkingDirectory,
            Stream = evt.Stream == ProcessOutputStream.Stdout ? ExecuteCommandStreamKind.Stdout : ExecuteCommandStreamKind.Stderr,
            Text = text,
            ObservedAt = evt.ObservedAt,
            StreamBytesObserved = observation.StreamBytes,
            CombinedBytesObserved = observation.CombinedBytes,
            Truncated = truncated,
            Suppressed = state.OutputEventsSuppressed,
            Binary = binary
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void WriteExecuteCommandStreamElement(
        XmlWriter writer,
        string elementName,
        ExecuteCommandStreamResult streamResult,
        ProcessStreamOutput stream,
        ExecuteCommandOutputHandle handle)
    {
        writer.WriteStartElement(elementName);
        writer.WriteAttributeString("lines", streamResult.LineCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("chars", streamResult.CharCount.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("bytes_observed", stream.BytesObserved.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("bytes_captured", stream.BytesCaptured.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("bytes_discarded", stream.BytesDiscarded.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("truncated", FormatBool(stream.Truncated || streamResult.Truncated));
        writer.WriteAttributeString("binary", FormatBool(streamResult.Binary));
        WriteExecuteCommandOutputHandleAttributes(writer, handle);

        if (!string.IsNullOrEmpty(streamResult.Preview))
            writer.WriteString(SanitizeTerminalOutputForXml(streamResult.Preview));

        writer.WriteEndElement();
    }

    private static void WriteExecuteCommandOutputHandleAttributes(
        XmlWriter writer,
        ExecuteCommandOutputHandle handle)
    {
        if (!string.IsNullOrWhiteSpace(handle.ArtifactPath))
            writer.WriteAttributeString("artifact_path", handle.ArtifactPath);
        if (!string.IsNullOrWhiteSpace(handle.ContentId))
            writer.WriteAttributeString("content_id", handle.ContentId);
        if (!string.IsNullOrWhiteSpace(handle.LocalPath))
            writer.WriteAttributeString("local_path", handle.LocalPath);
    }

    private static void WriteBackgroundCommandElement(
        XmlWriter writer,
        ExecuteCommandProcessOperation command)
    {
        writer.WriteStartElement("command");
        writer.WriteAttributeString("operation_id", command.OperationId ?? command.CommandId);
        writer.WriteAttributeString("command", command.Request.Command);
        writer.WriteAttributeString("cwd", command.Request.WorkingDirectory);
        writer.WriteAttributeString("status", FormatEnum(command.Status));
        writer.WriteAttributeString("started_at", command.StartedAt.ToString("O", CultureInfo.InvariantCulture));
        if (command.CompletedAt is not null)
            writer.WriteAttributeString("completed_at", command.CompletedAt.Value.ToString("O", CultureInfo.InvariantCulture));
        if (command.ExitCode is not null)
            writer.WriteAttributeString("exit_code", command.ExitCode.Value.ToString(CultureInfo.InvariantCulture));
        if (command.CompletionKind is not null)
            writer.WriteAttributeString("completion_kind", FormatEnum(command.CompletionKind.Value));
        if (command.OutputMetadata?.Combined is { } combined)
            WriteExecuteCommandOutputHandleAttributes(writer, combined);
        else
            writer.WriteAttributeString("local_path", command.OutputStore.CombinedPath);
        if (!string.IsNullOrWhiteSpace(command.OutputMetadata?.Warning))
            writer.WriteAttributeString("output_store_warning", command.OutputMetadata.Warning);
        writer.WriteEndElement();
    }

    private static void WriteExecuteCommandOutputStoreElement(
        XmlWriter writer,
        ExecuteCommandOutputStoreMetadata outputMetadata)
    {
        if (outputMetadata.Warning is null && outputMetadata.ContentStoreAvailable)
            return;

        writer.WriteStartElement("output_store");
        writer.WriteAttributeString("content_store_available", FormatBool(outputMetadata.ContentStoreAvailable));
        if (!string.IsNullOrWhiteSpace(outputMetadata.Warning))
            writer.WriteAttributeString("warning", outputMetadata.Warning);
        if (!string.IsNullOrWhiteSpace(outputMetadata.Metadata.ArtifactPath))
            writer.WriteAttributeString("metadata_artifact_path", outputMetadata.Metadata.ArtifactPath);
        if (!string.IsNullOrWhiteSpace(outputMetadata.Metadata.ContentId))
            writer.WriteAttributeString("metadata_content_id", outputMetadata.Metadata.ContentId);
        if (!string.IsNullOrWhiteSpace(outputMetadata.Metadata.LocalPath))
            writer.WriteAttributeString("metadata_local_path", outputMetadata.Metadata.LocalPath);
        writer.WriteEndElement();
    }

    private static ExecuteCommandStreamResult BuildExecuteCommandStreamResult(ProcessStreamOutput stream)
    {
        if (stream.CapturedBytes.Length == 0)
            return new ExecuteCommandStreamResult("", 0, 0, false, false);

        var capturedBytes = stream.CapturedBytes.ToArray();
        var bomEncoding = DetectBomEncoding(capturedBytes);
        if (LooksBinary(capturedBytes, bomEncoding != null))
        {
            return new ExecuteCommandStreamResult(
                "Binary-looking output omitted from model result.",
                0,
                0,
                true,
                stream.Truncated);
        }

        var text = Encoding.UTF8.GetString(stream.CapturedBytes.Span);
        var preview = BuildHeadTailPreview(text, MaxInlineStreamChars, out var previewTruncated);
        return new ExecuteCommandStreamResult(
            preview,
            CountLines(preview),
            preview.Length,
            false,
            stream.Truncated || previewTruncated);
    }

    private static string SanitizeTerminalOutputForXml(string value)
    {
        var withoutAnsi = AnsiEscapeSequencePattern.Replace(value, "");
        if (withoutAnsi.All(static ch => XmlConvert.IsXmlChar(ch)))
            return withoutAnsi;

        var builder = new StringBuilder(withoutAnsi.Length);
        foreach (var rune in withoutAnsi.EnumerateRunes())
        {
            if (rune.Value > char.MaxValue || XmlConvert.IsXmlChar((char)rune.Value))
                builder.Append(rune);
        }

        return builder.ToString();
    }

    private static string BuildHeadTailPreview(string text, int maxChars, out bool truncated)
    {
        if (text.Length <= maxChars)
        {
            truncated = false;
            return text;
        }

        truncated = true;
        var marker = $"\n... [{text.Length - maxChars} chars omitted] ...\n";
        var budget = Math.Max(0, maxChars - marker.Length);
        var headLength = budget / 2;
        var tailLength = budget - headLength;
        return text[..headLength] + marker + text[^tailLength..];
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0)
            return 0;

        var count = 1;
        foreach (var ch in text)
        {
            if (ch == '\n')
                count++;
        }

        return count;
    }

    private static ExecuteCommandInterpretation InterpretCommandResult(
        string command,
        int? exitCode,
        ProcessCompletionKind completionKind)
    {
        if (completionKind == ProcessCompletionKind.TimedOut)
            return new ExecuteCommandInterpretation(true, "Command timed out.");

        if (completionKind is ProcessCompletionKind.Cancelled or ProcessCompletionKind.Stopped)
            return new ExecuteCommandInterpretation(true, "Command was interrupted.");

        if (exitCode is null)
            return new ExecuteCommandInterpretation(true, "Command did not produce an exit code.");

        if (exitCode == 0)
            return new ExecuteCommandInterpretation(false, null);

        var baseCommand = GetBaseCommand(command);
        var isNonError = exitCode == 1 && baseCommand is "grep" or "rg" or "diff" or "test" or "[";
        return isNonError
            ? new ExecuteCommandInterpretation(false, $"Command exited with code {exitCode}, which is not considered an error for {baseCommand}.")
            : new ExecuteCommandInterpretation(true, $"Command failed with exit code {exitCode}.");
    }

    private static bool IsNoOutputExpected(string baseCommand, ProcessInvocationResult result)
        => result.ExitCode == 0 &&
           result.Output.Stdout.BytesObserved == 0 &&
           result.Output.Stderr.BytesObserved == 0 &&
           baseCommand is "mkdir" or "rm" or "mv" or "cp" or "touch" or "chmod" or "cd";

    private static string GetBaseCommand(string command)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var index = 0;
        while (index < parts.Length && parts[index] is "timeout" or "env" or "nice" or "time" or "stdbuf")
            index++;

        return index < parts.Length
            ? Path.GetFileNameWithoutExtension(parts[index])
            : string.Empty;
    }

    private static ExecuteCommandCategory DetectCommandCategory(string baseCommand, string command)
    {
        var normalized = command.Trim();
        return baseCommand switch
        {
            "dotnet" when normalized.Contains(" build", StringComparison.Ordinal) => ExecuteCommandCategory.Build,
            "dotnet" when normalized.Contains(" test", StringComparison.Ordinal) => ExecuteCommandCategory.Test,
            "dotnet" when normalized.Contains(" format", StringComparison.Ordinal) => ExecuteCommandCategory.Format,
            "npm" when normalized.Equals("npm test", StringComparison.Ordinal) ||
                       normalized.StartsWith("npm test ", StringComparison.Ordinal) => ExecuteCommandCategory.Test,
            "npm" when normalized.Contains(" run lint", StringComparison.Ordinal) => ExecuteCommandCategory.Lint,
            "npm" when normalized.Contains(" run dev", StringComparison.Ordinal) ||
                       normalized.Contains(" start", StringComparison.Ordinal) => ExecuteCommandCategory.Server,
            "yarn" or "pnpm" or "bun" when normalized.Contains(" test", StringComparison.Ordinal) => ExecuteCommandCategory.Test,
            "yarn" or "pnpm" or "bun" when normalized.Contains(" lint", StringComparison.Ordinal) => ExecuteCommandCategory.Lint,
            "yarn" or "pnpm" or "bun" when normalized.Contains(" dev", StringComparison.Ordinal) => ExecuteCommandCategory.Server,
            "git" => ExecuteCommandCategory.Git,
            "rg" or "grep" => ExecuteCommandCategory.Search,
            "cat" or "head" or "tail" or "sed" or "awk" => ExecuteCommandCategory.Read,
            "mkdir" or "rm" or "mv" or "cp" or "touch" or "chmod" => ExecuteCommandCategory.FileMutation,
            "make" when normalized.Contains(" test", StringComparison.Ordinal) => ExecuteCommandCategory.Test,
            "make" => ExecuteCommandCategory.Build,
            _ => ExecuteCommandCategory.Unknown
        };
    }

    private static string DecodeOutputEventText(
        ReadOnlySpan<byte> bytes,
        int maxChars,
        out bool binary,
        out bool truncated)
    {
        var sample = bytes.Length > BinarySniffBytes
            ? bytes[..BinarySniffBytes].ToArray()
            : bytes.ToArray();
        var bomEncoding = DetectBomEncoding(sample);
        binary = LooksBinary(sample, bomEncoding != null);
        if (binary)
        {
            truncated = false;
            return "";
        }

        var text = Encoding.UTF8.GetString(bytes);
        if (text.Length <= maxChars)
        {
            truncated = false;
            return text;
        }

        truncated = true;
        return text[..maxChars];
    }

    internal static ExecuteCommandCompletionKind ToExecuteCommandCompletionKind(ProcessCompletionKind kind)
        => kind switch
        {
            ProcessCompletionKind.Completed => ExecuteCommandCompletionKind.Completed,
            ProcessCompletionKind.TimedOut => ExecuteCommandCompletionKind.TimedOut,
            ProcessCompletionKind.Cancelled => ExecuteCommandCompletionKind.Cancelled,
            ProcessCompletionKind.Stopped => ExecuteCommandCompletionKind.Stopped,
            ProcessCompletionKind.FailedToStart => ExecuteCommandCompletionKind.FailedToStart,
            ProcessCompletionKind.Exited => ExecuteCommandCompletionKind.Faulted,
            _ => ExecuteCommandCompletionKind.Faulted
        };

    private static string FormatExecuteCommandError(ExecuteCommandError error)
    {
        var builder = new StringBuilder();
        using var writer = CreateCodingToolHarnessXmlWriter(builder);

        writer.WriteStartElement("execute_command_error");
        writer.WriteAttributeString("kind", FormatEnum(error.Kind));
        if (!string.IsNullOrWhiteSpace(error.Command))
            writer.WriteAttributeString("command", error.Command);
        if (!string.IsNullOrWhiteSpace(error.WorkingDirectory))
            writer.WriteAttributeString("cwd", error.WorkingDirectory);
        writer.WriteString(error.Message);
        writer.WriteEndElement();
        writer.Flush();
        return builder.ToString();
    }

    private static ExecuteCommandNormalizationResult InvalidArguments(
        string? command,
        string? workingDirectory,
        string message)
        => new(null, new ExecuteCommandError(
            ExecuteCommandErrorKind.InvalidArguments,
            command,
            workingDirectory,
            message));

    private static bool IsMissingProcessProviderException(Exception ex)
        => ex is InvalidOperationException &&
           ex.Message.Contains(nameof(IProcessProvider), StringComparison.Ordinal);

    private static bool TryGetBackgroundCommand(
        string? operationId,
        FunctionExecutionContext context,
        out ExecuteCommandProcessOperation background)
    {
        if (!string.IsNullOrWhiteSpace(operationId) &&
            !string.IsNullOrWhiteSpace(context.SessionId) &&
            ProcessOperations.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.SessionId, context.SessionId, StringComparison.Ordinal) &&
                string.Equals(candidate.OperationId, operationId, StringComparison.Ordinal)) is { } process)
        {
            background = process;
            return true;
        }

        background = null!;
        return false;
    }

    private static IReadOnlyList<ExecuteCommandProcessOperation> ListRegisteredBackgroundCommands(
        FunctionExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.SessionId))
            return [];
        return ProcessOperations
            .Where(pair => string.Equals(pair.Key.SessionId, context.SessionId, StringComparison.Ordinal))
            .Select(static pair => pair.Value)
            .ToList();
    }

    private static int CountRunningBackgroundCommands(FunctionExecutionContext context)
        => ListRegisteredBackgroundCommands(context)
            .Count(process => process.Status == ExecuteCommandProcessOperationStatus.Running);

    private static async Task CleanupFailedBackgroundStartAsync(
        IProcessInvocationHandle? handle,
        ExecuteCommandOutputStoreSession? outputStore,
        CancellationToken cancellationToken)
    {
        if (handle is not null)
        {
            try
            {
                await handle.StopAsync(
                    new ProcessStopRequest(StopKind.GracefulThenKill, "background-start-failed"),
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup for a failed background start.
            }

            await handle.DisposeAsync().ConfigureAwait(false);
        }

        if (outputStore is not null)
            await outputStore.DisposeAsync().ConfigureAwait(false);
    }

    private static RuntimeProcessExecutionBinding GetProcessExecution(FunctionExecutionContext context) =>
        context.RuntimeCapabilities.TryGet<RuntimeProcessExecutionBinding>(out var binding)
            ? binding
            : throw new InvalidOperationException(
                "No runtime process execution binding is available. The runtime must publish both its process provider and authorized execution target.");

    private static TargetHandle<ExecutionUnit> GetProcessExecutionTarget(FunctionExecutionContext context) =>
        GetProcessExecution(context).ExecutionTarget;

    private static ProcessInvocationSpec CreateProcessInvocationSpec(
        TargetHandle<ExecutionUnit> target,
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        TimeSpan timeout,
        ProcessIsolationPolicy isolation) =>
        new()
        {
            Target = target,
            Command = new ProcessCommandSpec
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                Environment = BuildProcessEnvironment(environment),
            },
            Io = new ProcessIoSpec
            {
                StandardOutput = new ProcessOutputSpec
                {
                    Capture = true,
                    Stream = true,
                    MaxCapturedBytes = MaxInlineCommandOutputChars,
                },
                StandardError = new ProcessOutputSpec
                {
                    Capture = true,
                    Stream = true,
                    MaxCapturedBytes = MaxInlineCommandOutputChars,
                },
            },
            Policy = ProcessInvocationPolicy.Default with
            {
                Timeout = timeout,
                OutputDrainTimeout = TimeSpan.FromSeconds(2),
            },
            Isolation = isolation,
        };

    private static IReadOnlyDictionary<string, string?> BuildProcessEnvironment(IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is null || environment.Count == 0)
            return new Dictionary<string, string?>(0, StringComparer.Ordinal);

        return environment.ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.Ordinal);
    }

}

/// <summary>Closed model-facing command operation.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
[JsonDerivedType(typeof(RunCommandOperation), "run")]
[JsonDerivedType(typeof(ListBackgroundCommandsOperation), "listBackground")]
[JsonDerivedType(typeof(ReadCommandOutputOperation), "readOutput")]
[JsonDerivedType(typeof(StopCommandOperation), "stop")]
public abstract record ExecuteCommandOperation;

/// <summary>Runs a new shell command.</summary>
/// <param name="Command">The shell command to execute.</param>
/// <param name="WorkingDirectory">An optional workspace-relative working directory.</param>
/// <param name="TimeoutMilliseconds">The foreground timeout in milliseconds.</param>
/// <param name="ExecutionMode">Whether the spawned command remains attached or returns a background handle.</param>
/// <param name="Environment">Environment variables to add or override.</param>
public sealed record RunCommandOperation(
    [property: Description("The shell command to execute.")]
    string Command,
    [property: Description("Optional working directory. Relative paths are resolved from the selected workspace root.")]
    string? WorkingDirectory = null,
    [property: Description("Timeout in milliseconds. Defaults to 120000.")]
    int TimeoutMilliseconds = 120_000,
    [property: Description("Whether to wait for completion or start the command in the background and return a handle.")]
    CommandExecutionMode ExecutionMode = CommandExecutionMode.Synchronous,
    [property: Description("Optional environment variables to add or override.")]
    IReadOnlyDictionary<string, string>? Environment = null)
    : ExecuteCommandOperation;

/// <summary>Lists background commands owned by the current session.</summary>
public sealed record ListBackgroundCommandsOperation : ExecuteCommandOperation;

/// <summary>Reads recent output from a background command.</summary>
/// <param name="OperationId">The handle returned by a background run.</param>
/// <param name="TailLines">The maximum number of recent combined output lines.</param>
/// <param name="DelayMilliseconds">An optional delay before reading output.</param>
public sealed record ReadCommandOutputOperation(
    [property: Description("Background handle id returned by a previous background run.")]
    string OperationId,
    [property: Description("Maximum number of recent combined output lines to return.")]
    int TailLines = 200,
    [property: Description("Optional delay in milliseconds before reading output.")]
    int DelayMilliseconds = 0)
    : ExecuteCommandOperation;

/// <summary>Stops a background command.</summary>
/// <param name="OperationId">The handle returned by a background run.</param>
public sealed record StopCommandOperation(
    [property: Description("Background handle id returned by a previous background run.")]
    string OperationId)
    : ExecuteCommandOperation;

/// <summary>Controls whether a spawned command runs synchronously or in the background.</summary>
public enum CommandExecutionMode
{
    /// <summary>Wait for the command to finish or reach its foreground timeout.</summary>
    Synchronous,

    /// <summary>Start the command and return a background handle immediately.</summary>
    Background
}

public enum ExecuteCommandAction
{
    Run,
    ListBackground,
    ReadOutput,
    Stop
}

public sealed record ExecuteCommandOptions
{
    public TimeSpan DefaultTimeout { get; init; } =
        TimeSpan.FromMilliseconds(CodingToolHarnessDefaultExecuteCommandOptions.DefaultTimeoutMilliseconds);

    public TimeSpan MaxTimeout { get; init; } =
        TimeSpan.FromMilliseconds(CodingToolHarnessDefaultExecuteCommandOptions.MaxTimeoutMilliseconds);

    public TimeSpan ProgressAfter { get; init; } =
        TimeSpan.FromMilliseconds(CodingToolHarnessDefaultExecuteCommandOptions.ProgressAfterMilliseconds);

    public TimeSpan MaxReadOutputDelay { get; init; } =
        TimeSpan.FromMilliseconds(CodingToolHarnessDefaultExecuteCommandOptions.MaxReadOutputDelayMilliseconds);

    public TimeSpan? AutoBackgroundAfter { get; init; } =
        TimeSpan.FromMilliseconds(CodingToolHarnessDefaultExecuteCommandOptions.AutoBackgroundAfterMilliseconds);

    public TimeSpan BackgroundStartSettleDelay { get; init; } =
        TimeSpan.FromMilliseconds(CodingToolHarnessDefaultExecuteCommandOptions.BackgroundStartSettleMilliseconds);

    public TimeSpan? InactivityTimeout { get; init; } =
        TimeSpan.FromMilliseconds(CodingToolHarnessDefaultExecuteCommandOptions.InactivityTimeoutMilliseconds);

    public int MaxInlineCommandOutputChars { get; init; } =
        CodingToolHarnessDefaultExecuteCommandOptions.MaxInlineCommandOutputChars;

    public long MaxPersistedOutputBytes { get; init; } =
        CodingToolHarnessDefaultExecuteCommandOptions.MaxPersistedOutputBytes;

    public int MaxActiveBackgroundCommands { get; init; } = 8;

    public int MaxOutputChunkEventChars { get; init; } =
        CodingToolHarnessDefaultExecuteCommandOptions.MaxOutputChunkEventChars;

    public int MaxOutputChunkEventsPerSecond { get; init; } =
        CodingToolHarnessDefaultExecuteCommandOptions.MaxOutputChunkEventsPerSecond;

    public int MaxOutputChunkEventsPerCommand { get; init; } =
        CodingToolHarnessDefaultExecuteCommandOptions.MaxOutputChunkEventsPerCommand;

    public bool DisablePagers { get; init; } = true;

    public bool DisableInteractivePrompts { get; init; } = true;
}

internal static class CodingToolHarnessDefaultExecuteCommandOptions
{
    public const int DefaultTimeoutMilliseconds = 120_000;
    public const int MaxTimeoutMilliseconds = 30 * 60 * 1000;
    public const int ProgressAfterMilliseconds = 2_000;
    public const int AutoBackgroundAfterMilliseconds = 15_000;
    public const int BackgroundStartSettleMilliseconds = 750;
    public const int InactivityTimeoutMilliseconds = 10 * 60 * 1000;
    public const int MaxReadOutputDelayMilliseconds = 10_000;
    public const int MaxInlineCommandOutputChars = 30_000;
    public const int MaxPersistedOutputBytes = 64 * 1024 * 1024;
    public const int MaxOutputChunkEventChars = 8_000;
    public const int MaxOutputChunkEventsPerSecond = 8;
    public const int MaxOutputChunkEventsPerCommand = 10_000;
}

internal sealed record ExecuteCommandRequest(
    string CommandId,
    ExecuteCommandAction Action,
    string Command,
    string? OperationId,
    string WorkingDirectory,
    TimeSpan Timeout,
    bool StartsInBackground,
    int TailLines,
    TimeSpan Delay,
    IReadOnlyDictionary<string, string?> Environment,
    ProcessIsolationPolicy Isolation);

internal sealed record ExecuteCommandNormalizationResult(
    ExecuteCommandRequest? Request,
    ExecuteCommandError? Error);

internal sealed record ExecuteCommandWorkingDirectoryResult(
    string? WorkingDirectory,
    ExecuteCommandError? Error);

internal sealed record ExecuteCommandError(
    ExecuteCommandErrorKind Kind,
    string? Command,
    string? WorkingDirectory,
    string Message);

internal sealed record ExecuteCommandStreamResult(
    string Preview,
    int LineCount,
    int CharCount,
    bool Binary,
    bool Truncated);

internal sealed record ExecuteCommandInterpretation(
    bool IsError,
    string? Message);

internal sealed record ExecuteCommandOutputStoreMetadata(
    ExecuteCommandOutputHandle Stdout,
    ExecuteCommandOutputHandle Stderr,
    ExecuteCommandOutputHandle Combined,
    ExecuteCommandOutputHandle Metadata,
    bool ContentStoreAvailable,
    string? Warning);

internal sealed record ExecuteCommandOutputHandle(
    string? ArtifactPath,
    string? ContentId,
    string? LocalPath,
    string ContentType,
    long Bytes,
    bool Truncated,
    bool Binary);

internal enum ExecuteCommandErrorKind
{
    InvalidArguments,
    InvalidWorkspace,
    WorkingDirectoryNotFound,
    WorkingDirectoryIsFile,
    MissingRunner,
    StartFailed,
    Cancelled,
    BackgroundUnavailable,
    BackgroundLimitExceeded,
    OperationNotFound,
    OutputStoreFailed,
    NotImplemented
}

internal sealed class ExecuteCommandEventState
{
    private readonly ExecuteCommandOptions _options;
    private readonly object _lock = new();
    private long _stdoutBytes;
    private long _stderrBytes;
    private int _eventsEmitted;
    private int _eventsInCurrentSecond;
    private long _currentSecond;
    private bool _outputEventsSuppressed;
    private bool _progressEmitted;

    public ExecuteCommandEventState(ExecuteCommandOptions options)
    {
        _options = options;
        MaxOutputChunkEventChars = Math.Max(0, options.MaxOutputChunkEventChars);
    }

    public int MaxOutputChunkEventChars { get; }

    public bool OutputEventsSuppressed
    {
        get
        {
            lock (_lock)
                return _outputEventsSuppressed;
        }
    }

    public (long StreamBytes, long CombinedBytes) Observe(ProcessOutputStream stream, int bytes)
    {
        lock (_lock)
        {
            if (stream == ProcessOutputStream.Stdout)
                _stdoutBytes += bytes;
            else
                _stderrBytes += bytes;

            return (
                stream == ProcessOutputStream.Stdout ? _stdoutBytes : _stderrBytes,
                _stdoutBytes + _stderrBytes);
        }
    }

    public bool TryReserveOutputEvent()
    {
        lock (_lock)
        {
            if (_eventsEmitted >= _options.MaxOutputChunkEventsPerCommand)
            {
                _outputEventsSuppressed = true;
                return false;
            }

            var nowSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (nowSecond != _currentSecond)
            {
                _currentSecond = nowSecond;
                _eventsInCurrentSecond = 0;
            }

            if (_eventsInCurrentSecond >= _options.MaxOutputChunkEventsPerSecond)
            {
                _outputEventsSuppressed = true;
                return false;
            }

            _eventsEmitted++;
            _eventsInCurrentSecond++;
            return true;
        }
    }

    public async ValueTask TryEmitProgressAsync(
        FunctionExecutionContext context,
        ExecuteCommandRequest request,
        string baseCommand,
        ExecuteCommandCategory category,
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        long stdoutBytes;
        long stderrBytes;
        bool suppressed;

        lock (_lock)
        {
            if (_progressEmitted || elapsed < _options.ProgressAfter)
                return;

            _progressEmitted = true;
            stdoutBytes = _stdoutBytes;
            stderrBytes = _stderrBytes;
            suppressed = _outputEventsSuppressed;
        }

        await context.TryPublishAsync(new ExecuteCommandProgressEvent
        {
            ToolCallId = context.FunctionCallId,
            FunctionName = context.FunctionName,
            EventFlowId = request.CommandId,
            CommandId = request.CommandId,
            Command = request.Command,
            BaseCommand = baseCommand,
            Category = category,
            WorkingDirectory = request.WorkingDirectory,
            ElapsedMilliseconds = (long)elapsed.TotalMilliseconds,
            StdoutBytes = stdoutBytes,
            StderrBytes = stderrBytes,
            CombinedOutputBytes = stdoutBytes + stderrBytes,
            CombinedBytesDiscarded = 0,
            OutputObserved = stdoutBytes + stderrBytes > 0,
            OutputEventsSuppressed = suppressed
        }, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class ExecuteCommandOutputSink(
    FunctionExecutionContext context,
    ExecuteCommandRequest request,
    string baseCommand,
    ExecuteCommandCategory category,
    ExecuteCommandOutputStoreSession outputStore,
    ExecuteCommandEventState eventState) : IProcessOutputSink
{
    public async ValueTask OnOutputAsync(
        ProcessOutputChunk chunk,
        CancellationToken cancellationToken = default)
    {
        await outputStore.AppendAsync(
            chunk.Stream,
            chunk.Bytes,
            chunk.ObservedAt,
            cancellationToken).ConfigureAwait(false);

        await CodingToolHarness.EmitExecuteCommandOutputChunkEventAsync(
            context,
            request,
            baseCommand,
            category,
            chunk,
            eventState,
            cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class ExecuteCommandOutputStoreSession : IAsyncDisposable
{
    private readonly string _commandId;
    private readonly ExecuteCommandRequest _request;
    private readonly ExecuteCommandOptions _options;
    private readonly IContentStore? _contentStore;
    private readonly string? _sessionId;
    private readonly string _rootDirectory;
    private readonly CappedOutputFile _stdout;
    private readonly CappedOutputFile _stderr;
    private readonly CappedOutputFile _combined;
    private bool _completed;

    private ExecuteCommandOutputStoreSession(
        string commandId,
        ExecuteCommandRequest request,
        ExecuteCommandOptions options,
        IContentStore? contentStore,
        string? sessionId,
        string rootDirectory)
    {
        _commandId = commandId;
        _request = request;
        _options = options;
        _contentStore = contentStore;
        _sessionId = sessionId;
        _rootDirectory = rootDirectory;
        _stdout = new CappedOutputFile(Path.Combine(rootDirectory, "stdout.txt"), options.MaxPersistedOutputBytes);
        _stderr = new CappedOutputFile(Path.Combine(rootDirectory, "stderr.txt"), options.MaxPersistedOutputBytes);
        _combined = new CappedOutputFile(Path.Combine(rootDirectory, "combined.log"), options.MaxPersistedOutputBytes);
    }

    public static async ValueTask<ExecuteCommandOutputStoreSession> CreateAsync(
        string commandId,
        ExecuteCommandRequest request,
        FunctionExecutionContext context,
        ExecuteCommandOptions options,
        CancellationToken cancellationToken)
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "hpd-command-results", commandId);
        Directory.CreateDirectory(rootDirectory);

        var contentStore = context.SessionId is { Length: > 0 }
            ? context.ContentStore
            : null;

        var session = new ExecuteCommandOutputStoreSession(
            commandId,
            request,
            options,
            contentStore,
            context.SessionId,
            rootDirectory);
        await session.OpenAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    public async ValueTask AppendAsync(
        ProcessOutputStream stream,
        ReadOnlyMemory<byte> bytes,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (stream == ProcessOutputStream.Stdout)
            await _stdout.AppendAsync(bytes, cancellationToken).ConfigureAwait(false);
        else
            await _stderr.AppendAsync(bytes, cancellationToken).ConfigureAwait(false);

        await _combined.AppendAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    public string CombinedPath => _combined.Path;

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
        => await FlushCoreAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask<string> ReadCombinedTailAsync(
        int tailLines,
        CancellationToken cancellationToken)
    {
        await FlushCoreAsync(cancellationToken).ConfigureAwait(false);

        var path = ValidateLocalOutputPath(_rootDirectory, _combined.Path);
        if (!File.Exists(path))
            return "";

        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        return string.Join(
            System.Environment.NewLine,
            lines.Length <= tailLines ? lines : lines[^tailLines..]);
    }

    public async ValueTask<ExecuteCommandOutputStoreMetadata> CompleteAsync(
        ProcessInvocationResult result,
        string shell,
        CancellationToken cancellationToken)
    {
        if (_completed)
            throw new InvalidOperationException("Execute command output store session has already completed.");

        _completed = true;
        await EnsureCapturedOutputWrittenAsync(result, cancellationToken).ConfigureAwait(false);
        await FlushCoreAsync(cancellationToken).ConfigureAwait(false);

        var stdoutBinary = IsBinary(result.Output.Stdout);
        var stderrBinary = IsBinary(result.Output.Stderr);
        string? warning = null;

        var stdout = CreateLocalHandle(_stdout, "text/plain", stdoutBinary);
        var stderr = CreateLocalHandle(_stderr, "text/plain", stderrBinary);
        var combined = CreateLocalHandle(_combined, "text/plain", stdoutBinary && stderrBinary);

        var metadataPath = Path.Combine(_rootDirectory, "metadata.json");
        var metadataBytes = BuildMetadataBytes(result, shell, stdout, stderr, combined);
        await File.WriteAllBytesAsync(metadataPath, metadataBytes, cancellationToken).ConfigureAwait(false);
        var metadata = new ExecuteCommandOutputHandle(
            null,
            null,
            metadataPath,
            "application/json",
            metadataBytes.Length,
            false,
            false);

        if (_contentStore is not null && _sessionId is not null)
        {
            try
            {
                stdout = await CommitAsync(stdout, "stdout.txt", "stdout", cancellationToken).ConfigureAwait(false);
                stderr = await CommitAsync(stderr, "stderr.txt", "stderr", cancellationToken).ConfigureAwait(false);
                combined = await CommitAsync(combined, "combined.log", "combined", cancellationToken).ConfigureAwait(false);
                metadata = await CommitAsync(metadata, "metadata.json", "metadata", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                warning = $"Failed to commit command output artifacts: {ex.Message}";
            }
        }

        return new ExecuteCommandOutputStoreMetadata(
            stdout,
            stderr,
            combined,
            metadata,
            _contentStore is not null,
            warning);
    }

    public async ValueTask DisposeAsync()
    {
        await _stdout.DisposeAsync().ConfigureAwait(false);
        await _stderr.DisposeAsync().ConfigureAwait(false);
        await _combined.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask OpenAsync(CancellationToken cancellationToken)
    {
        await _stdout.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _stderr.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _combined.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask FlushCoreAsync(CancellationToken cancellationToken)
    {
        await _stdout.FlushAsync(cancellationToken).ConfigureAwait(false);
        await _stderr.FlushAsync(cancellationToken).ConfigureAwait(false);
        await _combined.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask EnsureCapturedOutputWrittenAsync(
        ProcessInvocationResult result,
        CancellationToken cancellationToken)
    {
        if (_stdout.Bytes == 0 && result.Output.Stdout.CapturedBytes.Length > 0)
            await _stdout.AppendAsync(result.Output.Stdout.CapturedBytes, cancellationToken).ConfigureAwait(false);

        if (_stderr.Bytes == 0 && result.Output.Stderr.CapturedBytes.Length > 0)
            await _stderr.AppendAsync(result.Output.Stderr.CapturedBytes, cancellationToken).ConfigureAwait(false);

        if (_combined.Bytes == 0)
        {
            if (result.Output.Stdout.CapturedBytes.Length > 0)
                await _combined.AppendAsync(result.Output.Stdout.CapturedBytes, cancellationToken).ConfigureAwait(false);
            if (result.Output.Stderr.CapturedBytes.Length > 0)
                await _combined.AppendAsync(result.Output.Stderr.CapturedBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<ExecuteCommandOutputHandle> CommitAsync(
        ExecuteCommandOutputHandle local,
        string fileName,
        string stream,
        CancellationToken cancellationToken)
    {
        if (local.LocalPath is null || _contentStore is null || _sessionId is null)
            return local;

        var artifactName = $"commands/{_commandId}/{fileName}";
        await using var data = new FileStream(
            local.LocalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 81920,
            useAsync: true);
        var contentInfo = await _contentStore.WriteAsync(
            ContentScope.Create(_sessionId),
            data,
            new ContentMetadata
            {
                ContentType = local.ContentType,
                Name = artifactName,
                Origin = ContentSource.Agent,
                Tags = new Dictionary<string, string>
                {
                    ["kind"] = "artifact",
                    ["artifact-kind"] = "execute_command_output",
                    ["command-id"] = _commandId,
                    ["stream"] = stream,
                    ["truncated"] = FormatBoolean(local.Truncated),
                    ["binary"] = FormatBoolean(local.Binary),
                    ["cwd"] = _request.WorkingDirectory
                }
            },
            new ContentWriteOptions { Mode = ContentWriteMode.Create },
            cancellationToken).ConfigureAwait(false);

        return local with
        {
            ArtifactPath = null,
            ContentId = contentInfo.Address.ContentId
        };
    }

    private byte[] BuildMetadataBytes(
        ProcessInvocationResult result,
        string shell,
        ExecuteCommandOutputHandle stdout,
        ExecuteCommandOutputHandle stderr,
        ExecuteCommandOutputHandle combined)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["command_id"] = _commandId,
            ["command"] = _request.Command,
            ["cwd"] = _request.WorkingDirectory,
            ["shell"] = shell,
            ["exit_code"] = result.ExitCode,
            ["completion_kind"] = result.CompletionKind.ToString(),
            ["stdout_bytes_observed"] = result.Output.Stdout.BytesObserved,
            ["stderr_bytes_observed"] = result.Output.Stderr.BytesObserved,
            ["output_drain_timed_out"] = result.Output.OutputDrainTimedOut,
            ["stdout"] = stdout,
            ["stderr"] = stderr,
            ["combined"] = combined
        };

        return JsonSerializer.SerializeToUtf8Bytes(metadata);
    }

    private static ExecuteCommandOutputHandle CreateLocalHandle(
        CappedOutputFile file,
        string contentType,
        bool binary)
        => new(
            null,
            null,
            file.Path,
            contentType,
            file.Bytes,
            file.Truncated,
            binary);

    private static bool IsBinary(ProcessStreamOutput stream)
    {
        if (stream.CapturedBytes.Length == 0)
            return false;

        var capturedBytes = stream.CapturedBytes.ToArray();
        var bomEncoding = CodingToolHarness.DetectBomEncoding(capturedBytes);
        return CodingToolHarness.LooksBinary(capturedBytes, bomEncoding != null);
    }

    internal static string ValidateLocalOutputPath(
        string rootDirectory,
        string path)
    {
        var fullRoot = Path.GetFullPath(rootDirectory);
        var fullPath = Path.GetFullPath(path);

        if (!IsPathUnderDirectory(fullRoot, fullPath))
            throw new InvalidOperationException("Output path is outside the command spool directory.");

        RejectReparsePoints(fullRoot, fullPath);
        return fullPath;
    }

    internal static bool IsPathUnderDirectory(
        string rootDirectory,
        string path)
    {
        var fullRoot = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);

        return fullPath.Equals(fullRoot, StringComparison.Ordinal) ||
            fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar &&
                fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static void RejectReparsePoints(
        string rootDirectory,
        string path)
    {
        var fullRoot = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        var current = fullPath;

        while (IsPathUnderDirectory(fullRoot, current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Output path contains a reparse point.");
            }

            if (current.Equals(fullRoot, StringComparison.Ordinal))
                break;

            var parent = Directory.GetParent(current);
            if (parent is null)
                break;

            current = parent.FullName;
        }
    }

    private static string FormatBoolean(bool value)
        => value.ToString().ToLowerInvariant();

    private sealed class CappedOutputFile : IAsyncDisposable
    {
        private readonly long _maxBytes;
        private FileStream? _stream;

        public CappedOutputFile(string path, long maxBytes)
        {
            Path = path;
            _maxBytes = maxBytes;
        }

        public string Path { get; }
        public long Bytes { get; private set; }
        public bool Truncated { get; private set; }

        public ValueTask OpenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _stream = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            return ValueTask.CompletedTask;
        }

        public async ValueTask AppendAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            if (_stream is null)
                throw new InvalidOperationException("Output file is not open.");

            var remaining = _maxBytes - Bytes;
            if (remaining <= 0)
            {
                Truncated = Truncated || bytes.Length > 0;
                return;
            }

            var writable = (int)Math.Min(bytes.Length, remaining);
            if (writable < bytes.Length)
                Truncated = true;

            await _stream.WriteAsync(bytes[..writable], cancellationToken).ConfigureAwait(false);
            Bytes += writable;
        }

        public async ValueTask FlushAsync(CancellationToken cancellationToken)
        {
            if (_stream is not null)
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (_stream is not null)
            {
                await _stream.DisposeAsync().ConfigureAwait(false);
                _stream = null;
            }
        }
    }
}
