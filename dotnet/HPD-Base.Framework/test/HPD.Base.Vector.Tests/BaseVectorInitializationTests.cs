using FluentAssertions;
using HPD.Base.Vector.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Base.Vector.Tests;

public sealed class BaseVectorInitializationTests
{
    private static readonly DateTimeOffset Now = new(2035, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task Future_active_key_fails_application_initialization()
    {
        await using ServiceProvider provider = Build(new BaseOpaqueTokenKey
        {
            Id = 1,
            Key = new byte[32],
            IssueNotBefore = Now.AddMinutes(1),
        });

        OperationResult<BaseApplicationReadiness> result = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();

        result.Status.Should().Be(OperationStatus.StoreError);
        result.Error!.Code.Should().Be("base.application.initializationFailed");
        provider.GetRequiredService<IHPDBaseApplication>().CurrentReadiness.State.Should().Be(BaseApplicationReadinessState.Failed);
    }

    [Fact]
    public async Task Expired_active_and_retained_keys_fail_before_vector_provider_initialization()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddHPDBase(builder => builder
            .ConfigureTokenProtection(options =>
            {
                options.ActiveKey = new BaseOpaqueTokenKey
                {
                    Id = 1,
                    Key = new byte[32],
                    IssueNotBefore = Now.AddDays(-2),
                    IssueUntil = Now.AddDays(-1),
                    DecryptUntil = Now.AddDays(29),
                };
                options.DecryptionKeys =
                [
                    new BaseOpaqueTokenKey
                    {
                        Id = 2,
                        Key = Enumerable.Repeat((byte)2, 32).ToArray(),
                        IssueNotBefore = Now.AddDays(-32),
                        IssueUntil = Now.AddDays(-31),
                        DecryptUntil = Now.AddTicks(-1),
                    },
                ];
            })
            .AddVector()
            .UseTestVectorProvider());
        await using ServiceProvider provider = services.BuildServiceProvider();

        OperationResult<BaseApplicationReadiness> result = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();

        result.Status.Should().Be(OperationStatus.StoreError);
        result.Error!.Code.Should().Be("base.application.initializationFailed");
    }

    [Fact]
    public async Task Derived_provider_requires_an_explicit_default_consistency_mode()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddHPDBase(builder => builder
            .ConfigureTokenProtection(options => options.ActiveKey = ValidKey())
            .AddVector()
            .UseTestVectorProvider(options => options.Consistency = BaseVectorProviderConsistency.DerivedJournal));
        await using ServiceProvider provider = services.BuildServiceProvider();

        OperationResult<BaseApplicationReadiness> result = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();

        result.Status.Should().Be(OperationStatus.StoreError);
    }

    [Fact]
    public async Task Derived_provider_accepts_an_explicit_bounded_staleness_default()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddHPDBase(builder => builder
            .ConfigureTokenProtection(options => options.ActiveKey = ValidKey())
            .AddVector(options => options.DerivedProviderDefaultConsistency = new BaseVectorConsistencyRequirement.BoundedStaleness(TimeSpan.FromMinutes(1)))
            .UseTestVectorProvider(options => options.Consistency = BaseVectorProviderConsistency.DerivedJournal));
        await using ServiceProvider provider = services.BuildServiceProvider();

        OperationResult<BaseApplicationReadiness> result = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();

        result.Status.Should().Be(OperationStatus.Ok);
    }

    [Fact]
    public async Task Token_configuration_is_deeply_frozen_before_callback_owned_values_change()
    {
        HPDBaseTokenProtectionOptions? retained = null;
        byte[] key = Enumerable.Repeat((byte)7, 32).ToArray();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddHPDBase(builder => builder
            .ConfigureTokenProtection(options =>
            {
                options.ActiveKey = new BaseOpaqueTokenKey { Id = 7, Key = key, IssueNotBefore = Now.AddDays(-1) };
                retained = options;
            })
            .AddVector()
            .UseTestVectorProvider());
        key.AsSpan().Clear();
        retained!.ActiveKey = retained.ActiveKey with { IssueNotBefore = Now.AddDays(1) };
        await using ServiceProvider provider = services.BuildServiceProvider();

        OperationResult<BaseApplicationReadiness> result = await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();

        result.Status.Should().Be(OperationStatus.Ok);
    }

    private static ServiceProvider Build(BaseOpaqueTokenKey key)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddHPDBase(builder => builder
            .ConfigureTokenProtection(options => options.ActiveKey = key)
            .AddVector()
            .UseTestVectorProvider());
        return services.BuildServiceProvider();
    }

    private static BaseOpaqueTokenKey ValidKey() => new()
    {
        Id = 1,
        Key = new byte[32],
        IssueNotBefore = Now.AddDays(-1),
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
