using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HPD.Base;

/// <summary>Configures provider-backed durable activation workers.</summary>
public sealed class HPDBaseActivationWorkerOptions
{
    /// <summary>Gets or sets the stable System worker subject identity.</summary>
    public string WorkerSubjectId { get; set; } = "hpd.base.activation-worker";
    /// <summary>Gets or sets the optional exact tenant scope.</summary>
    public string? TenantId { get; set; }
    /// <summary>Gets or sets the optional exact project scope.</summary>
    public string? ProjectId { get; set; }
    /// <summary>Gets or sets the bounded delay after a complete empty pass.</summary>
    public TimeSpan EmptyPollInterval { get; set; } = TimeSpan.FromMilliseconds(250);
    /// <summary>Gets or sets the bounded shutdown wait.</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Adds hosted durable activation dispatch to an HPD.Base application.</summary>
public static class HPDBaseActivationWorkerServiceCollectionExtensions
{
    /// <summary>Adds one provider-backed System worker dispatcher.</summary>
    public static IServiceCollection AddHPDBaseActivationWorkers(
        this IServiceCollection services,
        Action<HPDBaseActivationWorkerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<HPDBaseActivationWorkerOptions>();
        if (configure is not null) services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, BaseActivationHostedDispatcher>());
        return services;
    }
}

internal sealed class BaseActivationHostedDispatcher(
    IBaseSessionFactory sessions,
    IBaseActivationWorkerRuntime runtime,
    BaseActivationRegistry registry,
    IBaseScheduleRuntime scheduleRuntime,
    BaseScheduleRegistry schedules,
    IOptions<HPDBaseActivationWorkerOptions> configured) : BackgroundService
{
    private BaseSession? _session;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        HPDBaseActivationWorkerOptions options = configured.Value;
        Validate(options);
        _session = CreateSession(options);
        foreach (BaseScheduleDefinition schedule in schedules.All
                     .OrderBy(static value => value.Id, StringComparer.Ordinal)
                     .ThenBy(static value => value.Version))
        {
            OperationResult<BaseScheduleMutationResult> installed = await scheduleRuntime.MutateAsync(
                _session, schedule, BaseScheduleMutationKind.Create, null,
                Identity("schedule-create", schedule.Id, schedule.Version, schedule.Checksum.AsSpan()),
                cancellationToken).ConfigureAwait(false);
            if (!installed.IsSuccess())
                throw new InvalidOperationException(installed.Error?.Code ?? "base.activation.scheduleConflict");
        }
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        HPDBaseActivationWorkerOptions options = configured.Value;
        BaseSession session = _session ?? CreateSession(options);

        while (!stoppingToken.IsCancellationRequested)
        {
            bool dispatched = await AdvanceSchedulesAsync(session, stoppingToken).ConfigureAwait(false);
            foreach (IBaseActivationRegistration registration in registry.Registrations)
            {
                OperationResult<BaseActivationDispatchResult> result;
                try
                {
                    result = await registration.RunOneAsync(runtime, session, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    continue;
                }
                dispatched |= result.IsSuccess() && result.Value is { Empty: false };
            }
            if (!dispatched)
                await Task.Delay(options.EmptyPollInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private BaseSession CreateSession(HPDBaseActivationWorkerOptions options) => sessions.For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectKind = AccessSubjectKind.System,
            SubjectId = options.WorkerSubjectId,
            CurrentTenantId = options.TenantId,
        }, value =>
        {
            value.Audience = HPDBaseEndpointAudience.Application;
            value.Mode = OperationMode.System;
            value.TenantId = options.TenantId;
            value.ProjectId = options.ProjectId;
        });

    private async ValueTask<bool> AdvanceSchedulesAsync(BaseSession session, CancellationToken cancellationToken)
    {
        bool changed = false;
        foreach (BaseScheduleDefinition schedule in schedules.All
                     .OrderBy(static value => value.Id, StringComparer.Ordinal)
                     .ThenBy(static value => value.Version))
        {
            try
            {
                OperationResult<BaseScheduleAuthority> authority = await scheduleRuntime.ReadAsync(
                    session, schedule, cancellationToken).ConfigureAwait(false);
                if (!authority.IsSuccess() || authority.Value is null || !authority.Value.Enabled) continue;
                byte[] boundary = System.Text.Encoding.UTF8.GetBytes(
                    $"{authority.Value.DefinitionGeneration}\n{authority.Value.ScheduleEpoch}\n{authority.Value.LastConsideredNominal}\n{authority.Value.NextNominal}");
                OperationResult<BaseScheduleMaintenancePage> advanced = await scheduleRuntime.AdvanceAsync(
                    session, schedule,
                    Identity("schedule-advance", schedule.Id, schedule.Version, boundary),
                    cancellationToken).ConfigureAwait(false);
                changed |= advanced.IsSuccess() && advanced.Value is { Occurrences: { Length: > 0 } };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { }
        }
        return changed;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        TimeSpan timeout = configured.Value.ShutdownTimeout;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(timeout);
        try { await base.StopAsync(bounded.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (bounded.IsCancellationRequested) { }
    }

    private static void Validate(HPDBaseActivationWorkerOptions options)
    {
        BaseApplicationId.Validate(options.WorkerSubjectId, nameof(options.WorkerSubjectId));
        if (options.EmptyPollInterval < TimeSpan.FromMilliseconds(10)
            || options.EmptyPollInterval > TimeSpan.FromMinutes(1)
            || options.ShutdownTimeout < TimeSpan.FromSeconds(1)
            || options.ShutdownTimeout > TimeSpan.FromMinutes(5)
            || options.ProjectId is not null && options.TenantId is null)
            throw new InvalidOperationException("base.activation.workerConfigurationInvalid");
    }

    private static BaseMutationRequestIdentity Identity(
        string operation, string id, int version, ReadOnlySpan<byte> authority)
    {
        byte[] fingerprint = System.Security.Cryptography.SHA256.HashData(authority);
        return BaseMutationRequestIdentity.Create(
            $"activation-worker:{id}:{version}", operation,
            Convert.ToHexStringLower(fingerprint),
            BaseMutationRequestFingerprint.Create(fingerprint));
    }
}
