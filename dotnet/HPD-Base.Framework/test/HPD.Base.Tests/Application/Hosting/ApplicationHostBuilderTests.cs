using FluentAssertions;
using HPD.Base;
using HPD.Base.Tests.Application.Generation;
using Microsoft.Extensions.DependencyInjection;
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
            .UseInMemory()
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
        manifest.Provider.Should().Be("inMemory");
        manifest.CollectionIds.Should().Equal("projects");
        provider.GetRequiredService<IRecordStoreRegistry>()
            .GetStoreForCollection("projects")
            .Should().NotBeNull();
    }

    [Fact]
    public void UnifiedBuilderRejectsMissingOrMultipleProviders()
    {
        Action missing = () => new ServiceCollection().AddHPDBase(
            builder => builder.AddCollection(GeneratedProject.Collection));
        Action duplicate = () => new ServiceCollection().AddHPDBase(
            builder => builder.UseInMemory().UseSqlite());

        missing.Should().Throw<InvalidOperationException>()
            .WithMessage("*exactly one*");
        duplicate.Should().Throw<InvalidOperationException>()
            .WithMessage("*exactly one*");
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
    }

    [Fact]
    public void OptionalModulesNeedNoEmptyConfigurationCallbacks()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBase(builder => builder
            .UseInMemory()
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
                .UseInMemory()
                .AddRealtime()
                .AddRealtime());
        Action duplicateLiveQuery = () => new ServiceCollection().AddHPDBase(
            builder => builder
                .UseInMemory()
                .AddLiveQueries()
                .AddLiveQueries());

        duplicateRealtime.Should().Throw<InvalidOperationException>()
            .WithMessage("*Realtime is already registered*");
        duplicateLiveQuery.Should().Throw<InvalidOperationException>()
            .WithMessage("*Live queries are already registered*");
    }
}
