using FluentAssertions;
using HPD.Base;
using HPD.Base.Tests.Application.Generation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HPD.Base.Tests.Application.Hosting;

public sealed class ApplicationHostBuilderTests
{
    [Fact]
    public void UnifiedBuilderInstallsCollectionProviderAndManifest()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBase(builder => builder

            .AddCollection(GeneratedProject.Collection));

        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        IBaseSessionFactory sessions = provider.GetRequiredService<IBaseSessionFactory>();
        _ = sessions.For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.System,
            SubjectId = "system",
        });

        HPDBaseInstalledFeatures manifest =
            provider.GetRequiredService<HPDBaseInstalledFeatures>();
        manifest.Provider.Should().Be("volatile");
        manifest.CollectionIds.Should().Equal("projects");
        provider.GetRequiredService<IRecordStoreRegistry>()
            .GetStoreForCollection("projects")
            .Should().NotBeNull();
    }

    [Fact]
    public void UnifiedBuilderDefaultsWhenMissingAndRejectsMultipleExplicitProviders()
    {
        var defaultServices = new ServiceCollection();
        defaultServices.AddHPDBase(
            builder => builder.AddCollection(GeneratedProject.Collection));
        Action duplicate = () => new ServiceCollection().AddHPDBase(
            builder => builder
                .Use(new TestProviderExtension("first"))
                .Use(new TestProviderExtension("second")));

        using var provider = defaultServices.BuildServiceProvider();
        provider.GetRequiredService<HPDBaseInstalledFeatures>().Provider
            .Should().Be("volatile");
        duplicate.Should().Throw<InvalidOperationException>()
            .WithMessage("*at most one explicit*");
    }

    [Fact]
    public void VolatileProviderIsPerHostSingletonAndExplicitProvidersSuppressIt()
    {
        static ServiceProvider VolatileHost()
        {
            var services = new ServiceCollection();
            services.AddHPDBase(builder => builder.AddCollection(GeneratedProject.Collection));
            return services.BuildServiceProvider();
        }

        using var firstHost = VolatileHost();
        using var secondHost = VolatileHost();
        var first = firstHost.GetRequiredService<VolatileRecordStore>();

        firstHost.GetRequiredService<VolatileRecordStore>().Should().BeSameAs(first);
        secondHost.GetRequiredService<VolatileRecordStore>().Should().NotBeSameAs(first);

        var explicitServices = new ServiceCollection();
        explicitServices.AddHPDBase(builder => builder
            .UseSqlite()
            .AddCollection(GeneratedProject.Collection));
        using var explicitHost = explicitServices.BuildServiceProvider();
        explicitHost.GetService<VolatileRecordStore>().Should().BeNull();
        explicitHost.GetRequiredService<HPDBaseInstalledFeatures>().Provider.Should().Be("sqlite");
    }

    [Fact]
    public void VolatileConfigurationCannotBeSilentlyIgnoredByExplicitProvider()
    {
        Action register = () => new ServiceCollection().AddHPDBase(builder => builder
            .ConfigureVolatileStore(options => options.MaxPageSize = 50)
            .UseSqlite()
            .AddCollection(GeneratedProject.Collection));

        register.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConfigureVolatileStore*explicit*");
    }

    [Fact]
    public void VolatileFileDefaultIsIndependentFromTheRecordProvider()
    {
        var services = new ServiceCollection();
        services.AddHPDBase(builder => builder
            .UseSqlite()
            .AddCollection(GeneratedProject.Collection)
            .AddFiles(options => options.Buckets.Add(new FileBucketDescriptor
            {
                BucketId = new FileBucketId("assets")
            })));

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IFileStorageProvider>()
            .Should().ContainSingle()
            .Which.ProviderRef.Should().Be(new FileProviderRef("volatile"));
        provider.GetRequiredService<IOptions<HPDBaseFilesOptions>>().Value.Buckets
            .Should().ContainSingle()
            .Which.ProviderRef.Should().Be(new FileProviderRef("volatile"));
    }

    [Fact]
    public void RequiredPhysicalIndexesFailClosedForUnsupportedProviders()
    {
        var required = HPD.Base.BaseCollection.Define(
            "required.projects",
            GeneratedApplicationJsonContext.Default.GeneratedProject,
            schema =>
            {
                schema.String("organizationId").Required();
                schema.Index("organization", "organizationId").Required();
            });

        Action register = () => new ServiceCollection().AddHPDBase(
            builder => builder.UseSqlite().AddCollection(required));

        register.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be installed*SQLite*");

        Action defaultRegister = () => new ServiceCollection().AddHPDBase(
            builder => builder.AddCollection(required));
        defaultRegister.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be installed*volatile*");
    }

    [Fact]
    public void OptionalModulesNeedNoEmptyConfigurationCallbacks()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBase(builder => builder

            .AddCollection(GeneratedProject.Collection)
            .AddFiles()
            .AddDependencies(options =>
                options.ProtectionKey = Enumerable.Repeat((byte)0x31, 32).ToArray())
            .AddRealtime());

        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true });
        HPDBaseInstalledFeatures manifest =
            provider.GetRequiredService<HPDBaseInstalledFeatures>();

        manifest.Files.Should().BeTrue();
        manifest.Dependencies.Should().BeTrue();
        manifest.Realtime.Should().BeTrue();
        typeof(HPDBaseBuilder)
            .GetMethod(nameof(HPDBaseBuilder.AddDependencies))!
            .GetParameters()[0]
            .IsOptional.Should().BeTrue();
    }

    [Fact]
    public void UnifiedBuilderRejectsDuplicateModuleInstallation()
    {
        Action duplicateRealtime = () => new ServiceCollection().AddHPDBase(
            builder => builder

                .AddRealtime()
                .AddRealtime());
        Action duplicateLiveQuery = () => new ServiceCollection().AddHPDBase(
            builder => builder

                .AddLiveQueries()
                .AddLiveQueries());

        duplicateRealtime.Should().Throw<InvalidOperationException>()
            .WithMessage("*Realtime is already registered*");
        duplicateLiveQuery.Should().Throw<InvalidOperationException>()
            .WithMessage("*Live queries are already registered*");
    }
}

file sealed class TestProviderExtension(string id) : IHPDBaseBuilderExtension
{
    public string Id { get; } = id;
    public bool IsRecordProvider => true;
    public bool SupportsRequiredIndexes => false;
    public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections) { }
}
