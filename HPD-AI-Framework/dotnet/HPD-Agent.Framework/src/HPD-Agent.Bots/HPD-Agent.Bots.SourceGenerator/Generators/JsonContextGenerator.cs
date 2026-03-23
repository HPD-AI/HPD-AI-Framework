using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace HPD.Agent.Bots.SourceGenerator.Generators;

/// <summary>
/// Placeholder — JSON serializer context generation is not emitted by the source generator.
///
/// System.Text.Json's source generator cannot see output from other Roslyn generators,
/// so a generated partial class cannot satisfy JsonSerializerContext's abstract members.
/// Each adapter project must declare its own hand-written JsonSerializerContext subclass
/// (e.g. SlackBotJsonContext) with all required [JsonSerializable] entries.
/// </summary>
internal static class JsonContextGenerator
{
    public static void Generate(
        SourceProductionContext context,
        IReadOnlyList<WebhookPayloadInfo> payloads)
    {
        // No-op: see summary above.
    }
}
