using HPD.Agent.Audio.Media;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.AgentIntegration.Detection;

internal sealed record InputContentDetection(
    int ContentIndex,
    AIContent OriginalContent,
    InputContentRef InputContent);
