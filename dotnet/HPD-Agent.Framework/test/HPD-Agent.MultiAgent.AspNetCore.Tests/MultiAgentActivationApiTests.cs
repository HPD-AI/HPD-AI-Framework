using System.Net;
using System.Net.Http.Json;
using HPD.Agent.MultiAgent.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.MultiAgent.AspNetCore.Tests;

public sealed class MultiAgentActivationApiTests
{
    [Fact]
    public void AspNetCore_contract_has_no_HPD_Base_dependency()
    {
        string[] references = typeof(IHPDMultiAgentWorkflowBackend).Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name!)
            .ToArray();

        references.Should().NotContain("HPD.Base");
        references.Should().NotContain("HPD.Base.AspNetCore");
        references.Should().NotContain("HPD.Graph.Base");
    }

    [Fact]
    public async Task Neutral_routes_list_and_submit_through_the_registered_backend()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHPDMultiAgentAspNetCore();
        builder.Services.AddSingleton<IHPDMultiAgentWorkflowBackend>(new FakeWorkflowBackend());

        await using WebApplication app = builder.Build();
        app.MapHPDMultiAgentApi();
        await app.StartAsync();

        using HttpClient client = app.GetTestClient();
        var workflows = await client.GetFromJsonAsync<WorkflowListJson>("/multi-agent/workflows");
        workflows!.Workflows.Should().ContainSingle()
            .Which.WorkflowId.Should().Be("review");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/multi-agent/workflows/review/runs")
        {
            Content = JsonContent.Create(new { input = new { value = 42 } }),
        };
        request.Headers.Add(HPDMultiAgentHttpHeaders.IdempotencyKey, "run-1");
        using HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        RunAcceptedJson accepted = (await response.Content.ReadFromJsonAsync<RunAcceptedJson>())!;
        accepted.ActivationId.Should().Be("activation-1");
        accepted.ExecutionId.Should().Be("execution-1");
        accepted.State.Should().Be("Pending");
        accepted.Disposition.Should().Be("Committed");
    }

    private sealed class FakeWorkflowBackend : IHPDMultiAgentWorkflowBackend
    {
        public IReadOnlyList<HPDMultiAgentWorkflow> Workflows { get; } =
        [
            new()
            {
                WorkflowId = "review",
                WorkflowVersion = "1.0.0",
                DefinitionVersion = 1,
                Checksum = "abc123",
            },
        ];

        public ValueTask<HPDMultiAgentRunSubmission> EnqueueAsync(
            HPDMultiAgentRunCommand command,
            HttpContext context,
            CancellationToken cancellationToken)
        {
            command.WorkflowId.Should().Be("review");
            command.IdempotencyKey.Should().Be("run-1");
            return ValueTask.FromResult(HPDMultiAgentRunSubmission.Accepted(new HPDMultiAgentRunAdmission
            {
                ExecutionId = "execution-1",
                ActivationId = "activation-1",
                State = "Pending",
                Disposition = "Committed",
            }));
        }
    }

    private sealed record WorkflowListJson(WorkflowJson[] Workflows);
    private sealed record WorkflowJson(string WorkflowId);
    private sealed record RunAcceptedJson(
        string ExecutionId,
        string ActivationId,
        string State,
        string Disposition);
}
