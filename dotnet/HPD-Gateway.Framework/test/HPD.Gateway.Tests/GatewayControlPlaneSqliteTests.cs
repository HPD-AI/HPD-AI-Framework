using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using HPD.Gateway;
using HPD.Gateway.ControlPlane;
using HPD.Gateway.ControlPlane.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayControlPlaneSqliteTests
{
    [Fact]
    public void Public_surface_is_exact_and_provider_specific()
    {
        typeof(GatewaySqliteAuthorityOptions).Assembly.GetExportedTypes()
            .Should().BeEquivalentTo([
                typeof(GatewaySqliteAuthorityOptions),
                typeof(GatewaySqliteControlPlaneExtensions),
            ]);
        typeof(GatewaySqliteAuthorityOptions).Namespace
            .Should().Be("HPD.Gateway.ControlPlane.Sqlite");
    }

    [Fact]
    public void Connection_and_key_contract_fails_closed_without_service_contamination()
    {
        Action<GatewaySqliteAuthorityOptions>[] invalid =
        [
            static options => ConfigureKeys(options),
            static options => { ConfigureKeys(options); options.DataSource = "one.db"; options.ConnectionString = "Data Source=two.db"; },
            static options => { ConfigureValid(options); options.PlanProtectionKey = new byte[31]; },
            static options => { ConfigureValid(options); options.TokenProtectionKey = Key(1); },
            static options => { ConfigureValid(options); options.BusyTimeout = TimeSpan.Zero; },
            static options => { ConfigureValid(options); options.CommandTimeout = TimeSpan.FromMinutes(11); },
            static options => { ConfigureValid(options); options.DataSource = "not\u0000valid"; },
        ];

        foreach (Action<GatewaySqliteAuthorityOptions> configure in invalid)
        {
            var services = new ServiceCollection();
            services.AddSingleton(new ExistingMarker());
            ServiceDescriptor[] before = [.. services];

            Action registration = () => services.AddHpdGatewayControlPlane(controlPlane =>
                controlPlane.UseSqlite(configure));

            registration.Should().Throw<Exception>();
            services.Should().Equal(before);
            services.AddHpdGatewayControlPlane(controlPlane => controlPlane
                .UseSqlite(ConfigureValid));
            services.Count(static descriptor =>
                    descriptor.ServiceType == typeof(GatewayControlPlaneRegistration))
                .Should().Be(1);
        }
    }

    [Fact]
    public void Provider_projection_is_frozen_and_gateway_owned()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHpdGateway(static gateway => gateway.EnableCoreDeclarations());
        GatewaySqliteAuthorityOptions? retained = null;
        byte[] retainedPlanKey = Key(1);
        services.AddHpdGatewayControlPlane(controlPlane => controlPlane.UseSqlite(options =>
        {
            retained = options;
            ConfigureValid(options);
            options.PlanProtectionKey = retainedPlanKey;
            options.DataSource = "authority.db";
            options.EnableWal = false;
            options.BusyTimeout = TimeSpan.FromSeconds(7);
            options.CommandTimeout = TimeSpan.FromSeconds(41);
            options.InitializeSQLitePCLRaw = false;
        }));

        retainedPlanKey[0] = 0xff;
        retained!.DataSource = "changed.db";
        retained.EnableWal = true;
        retained.PlanProtectionKey = Key(9);

        using ServiceProvider provider = services.BuildServiceProvider();
        HPDBaseSqliteOptions projected = provider.GetRequiredService<IOptions<HPDBaseSqliteOptions>>().Value;
        GatewayManagementRuntimeOptions management = provider.GetRequiredService<GatewayManagementRuntimeOptions>();

        projected.StoreId.Should().Be("gateway-management");
        projected.ModuleId.Should().Be("hpd.gateway.control-plane.sqlite");
        projected.SchemaPrefix.Should().Be("hpd_gateway");
        projected.DataSource.Should().Be("authority.db");
        projected.ConnectionString.Should().BeNull();
        projected.EnableWal.Should().BeFalse();
        projected.BusyTimeout.Should().Be(TimeSpan.FromSeconds(7));
        projected.CommandTimeout.Should().Be(TimeSpan.FromSeconds(41));
        projected.InitializeSQLitePCLRaw.Should().BeFalse();
        projected.AdministrationEnabled.Should().BeTrue();
        projected.AllowClientRequestedIds.Should().BeTrue();
        projected.DefaultPageSize.Should().Be(64);
        projected.MaxPageSize.Should().Be(256);
        management.RequiredDurability.Should().Be(GatewayAuthorityDurability.RestartDurable);
        management.GetTokenKey().Should().Equal(Key(3));
        management.GetEpochReservationKey().Should().Equal(Key(4));
    }

    [Fact]
    public void Text_bounds_and_connection_syntax_are_exact()
    {
        string exactDataSource = new('a', 1_024);
        var exact = new ServiceCollection();
        exact.AddHpdGatewayControlPlane(controlPlane => controlPlane.UseSqlite(options =>
        {
            ConfigureKeys(options);
            options.DataSource = exactDataSource;
        }));

        AssertInvalid(options =>
        {
            ConfigureKeys(options);
            options.DataSource = new string('a', 1_025);
        });
        AssertInvalid(options =>
        {
            ConfigureKeys(options);
            options.ConnectionString = "not-a-connection-string";
        });
        AssertInvalid(options =>
        {
            ConfigureKeys(options);
            options.DataSource = ":memory:";
        });
        AssertInvalid(options =>
        {
            ConfigureKeys(options);
            options.DataSource = "file::memory:?cache=shared";
        });
        AssertInvalid(options =>
        {
            ConfigureKeys(options);
            options.ConnectionString = "Data Source=file::memory:?cache=shared";
        });
        AssertInvalid(options =>
        {
            ConfigureKeys(options);
            options.DataSource = "file:gateway?vfs=memdb";
        });
        AssertInvalid(options =>
        {
            ConfigureKeys(options);
            options.ConnectionString = "Data Source=file:gateway?vfs=memdb";
        });
        AssertInvalid(options =>
        {
            ConfigureKeys(options);
            options.ConnectionString = "Data Source=ephemeral;Mode=Memory;Cache=Shared";
        });
        var connectionString = new ServiceCollection();
        connectionString.AddHpdGatewayControlPlane(controlPlane => controlPlane.UseSqlite(options =>
        {
            ConfigureKeys(options);
            options.ConnectionString = "Data Source=gateway.db;Mode=ReadWriteCreate;Cache=Shared";
        }));
    }

    [Fact]
    public async Task Public_composition_is_restart_durable()
    {
        string database = Path.Combine(Path.GetTempPath(), $"gateway-slice4-{Guid.NewGuid():N}.db");
        var command = new GatewayProvisionTargetCommand(
            "slice4-ns", "slice4-target", "slice4-operation",
            new GatewayManagementActor("slice4-actor", "tests", "manage"),
            "slice4-correlation", "slice4-epoch");
        try
        {
            await using (ServiceProvider first = CreateProvider(database))
            {
                await InitializeSqlite(first);
                GatewayAuthorityCapabilitySnapshot capabilities = await first
                    .GetRequiredService<IGatewayAuthorityRuntime>().InitializeAsync();
                capabilities.Durability.Should().Be(GatewayAuthorityDurability.RestartDurable);
                (await first.GetRequiredService<IGatewayManagementCommandCoordinator>()
                    .ProvisionTargetAsync(command)).State.Should().Be(GatewayManagementCommandState.Accepted);
            }

            await using (ServiceProvider restarted = CreateProvider(database))
            {
                await InitializeSqlite(restarted);
                GatewayManagementCommandResult replay = await restarted
                    .GetRequiredService<IGatewayManagementCommandCoordinator>()
                    .ProvisionTargetAsync(command);
                replay.State.Should().Be(GatewayManagementCommandState.Duplicate, replay.Code);
            }
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(database + suffix)) File.Delete(database + suffix);
        }
    }

    private static ServiceProvider CreateProvider(string database)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHpdGateway(static gateway => gateway.EnableCoreDeclarations());
        services.AddHpdGatewayControlPlane(controlPlane => controlPlane
            .UseSqlite(options =>
            {
                ConfigureKeys(options);
                options.DataSource = database;
            }));
        return services.BuildServiceProvider();
    }

    private static async Task InitializeSqlite(ServiceProvider provider)
    {
        IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
        BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest
        {
            StoreId = "gateway-management",
        })).Value!;
        (await schemas.ApplyAsync(new BaseSchemaApplyRequest
        {
            ProtectedArtifact = plan.ProtectedArtifact,
        })).IsSuccess().Should().BeTrue();
    }

    private static void ConfigureValid(GatewaySqliteAuthorityOptions options)
    {
        ConfigureKeys(options);
        options.DataSource = "gateway.db";
    }

    private static void ConfigureKeys(GatewaySqliteAuthorityOptions options)
    {
        options.PlanProtectionKey = Key(1);
        options.TokenProtectionKey = Key(2);
        options.DesiredStateTokenKey = Key(3);
        options.EpochReservationKey = Key(4);
    }

    private static byte[] Key(byte value) => Enumerable.Repeat(value, 32).ToArray();
    private static void AssertInvalid(Action<GatewaySqliteAuthorityOptions> configure)
    {
        var services = new ServiceCollection();
        Action registration = () => services.AddHpdGatewayControlPlane(controlPlane =>
            controlPlane.UseSqlite(configure));
        registration.Should().Throw<ArgumentException>();
        services.Should().BeEmpty();
    }
    private sealed class ExistingMarker;
}
