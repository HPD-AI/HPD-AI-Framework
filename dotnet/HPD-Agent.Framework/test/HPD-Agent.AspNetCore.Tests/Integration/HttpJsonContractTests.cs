using FluentAssertions;
using HPD.Agent;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.ClientTools;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore.Tests.Integration;

public sealed class HttpJsonContractTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly Type[] ResponseTypes =
    [
        typeof(SessionDto),
        typeof(List<SessionDto>),
        typeof(ThreadDto),
        typeof(List<ThreadDto>),
        typeof(ThreadGraphDto),
        typeof(SubAgentDto),
        typeof(List<SubAgentDto>),
        typeof(ThreadExecutionDto),
        typeof(List<ThreadExecutionDto>),
        typeof(ThreadExecutionCancellationDto),
        typeof(ThreadRuntimeStateDto),
        typeof(InputSubmissionDto),
        typeof(ThreadForkResultDto),
        typeof(ThreadContextUsage),
        typeof(ContentDto),
        typeof(List<ContentDto>),
        typeof(AgentSummaryDto),
        typeof(List<AgentSummaryDto>),
        typeof(StoredAgentDto),
        typeof(AgentRespondResult),
        typeof(ClientToolProviderSnapshot),
        typeof(IReadOnlyList<ClientToolProviderSnapshot>),
        typeof(AgentEventCatalog),
        typeof(AgentInputCatalog),
        typeof(RuntimeAgentExtensionCatalog),
        typeof(RuntimeAgentExtensionDescriptor),
        typeof(HPDAgentStorageInfo),
        typeof(ProblemDetails),
        typeof(HttpValidationProblemDetails)
    ];

    private readonly IServiceProvider _services;

    public HttpJsonContractTests(TestWebApplicationFactory factory)
    {
        _services = factory.CreateServer().Services;
    }

    [Fact]
    public void ProductionJsonOptions_ProvideMetadataForAllHttpResponseTypes()
    {
        var options = _services
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value;
        options.SerializerOptions.IsReadOnly.Should().BeTrue();

        foreach (var type in ResponseTypes)
        {
            options.SerializerOptions.GetTypeInfo(type)
                .Should().NotBeNull($"HTTP response type '{type.FullName}' must be source-generated");
        }
    }
}
