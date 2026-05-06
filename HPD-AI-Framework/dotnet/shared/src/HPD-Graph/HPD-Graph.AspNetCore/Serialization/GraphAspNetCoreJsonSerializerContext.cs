using System.Text.Json.Serialization;
using HPDAgent.Graph.Hosting.Data;
using Microsoft.AspNetCore.Mvc;

namespace HPDAgent.Graph.AspNetCore.Serialization;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
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
