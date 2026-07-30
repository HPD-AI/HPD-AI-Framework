using HPD.Base.Application.DependencyInjection;
using HPD.Base.Application.Hosting;
using HPD.Base.Application.Sessions;
using HPD.Base.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Testing;

/// <summary>Owns a deterministic in-process HPD.BASE application host.</summary>
public sealed class BaseTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private BaseTestHost(ServiceProvider provider, BaseTestTimeProvider time)
    {
        _provider = provider;
        Time = time;
    }

    public BaseTestTimeProvider Time { get; }
    public HPDBaseInstalledFeatures Features =>
        _provider.GetRequiredService<HPDBaseInstalledFeatures>();

    public static ValueTask<BaseTestHost> CreateAsync(
        Action<HPDBaseApplicationBuilder> configure,
        DateTimeOffset? initialTime = null)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var services = new ServiceCollection();
        services.AddLogging();
        var time = new BaseTestTimeProvider(
            initialTime ?? new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        services.AddSingleton<TimeProvider>(time);
        services.AddHPDBase(configure);
        ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        return ValueTask.FromResult(new BaseTestHost(provider, time));
    }

    public BaseSession Session(
        PrincipalContext principal,
        Action<BaseSessionOptions>? configure = null) =>
        _provider.GetRequiredService<IBaseSessionFactory>().For(principal, configure);

    public T GetRequiredService<T>() where T : notnull =>
        _provider.GetRequiredService<T>();

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
