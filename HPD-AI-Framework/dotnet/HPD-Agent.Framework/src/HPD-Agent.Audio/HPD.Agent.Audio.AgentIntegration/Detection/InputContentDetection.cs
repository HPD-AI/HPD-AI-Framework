using HPD.Agent.Audio.Media;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.AgentIntegration.Detection;

public sealed record InputContentDetection(
    int ContentIndex,
    AIContent OriginalContent,
    InputContentRef InputContent);
