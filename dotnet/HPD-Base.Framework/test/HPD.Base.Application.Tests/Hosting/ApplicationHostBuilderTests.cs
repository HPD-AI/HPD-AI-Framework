using FluentAssertions;
using HPD.Base.Application.DependencyInjection;
using HPD.Base.Application.Hosting;
using HPD.Base.Application.Sessions;
using HPD.Base.Application.Tests.Generation;
using HPD.Base.Runtime;
using HPD.Base.Runtime.Stores;
using HPD.Base.Application.Schema;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Base.Application.Tests.Hosting;

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
            .WithMessage("*Only one*");
    }

    [Fact]
    public void RequiredPhysicalIndexesFailClosedForUnsupportedProviders()
    {
        var required = HPD.Base.Application.Schema.BaseCollection.Define(
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
}
