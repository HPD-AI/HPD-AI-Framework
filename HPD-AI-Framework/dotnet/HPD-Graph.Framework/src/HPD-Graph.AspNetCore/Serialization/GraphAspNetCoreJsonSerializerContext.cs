using System.Text.Json.Serialization;
using HPD.Graph.Hosting.Data;
using Microsoft.AspNetCore.Mvc;

namespace HPD.Graph.AspNetCore.Serialization;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(CreateWorkflowRequest))]
[JsonSerializable(typeof(UpdateWorkflowRequest))]
[JsonSerializable(typeof(ExecuteWorkflowRequest))]
[JsonSerializable(typeof(ResumeSuspensionRequest))]
[JsonSerializable(typeof(CreateScheduleRequest))]
[JsonSerializable(typeof(UpdateScheduleRequest))]
public partial class GraphAspNetCoreJsonSerializerContext : JsonSerializerContext
{
}
