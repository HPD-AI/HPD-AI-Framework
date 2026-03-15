using HPD.VCS.Core;

namespace HPD.VCS.Graphing;

public enum GraphEdgeType { Direct, Indirect, Missing }

public readonly record struct GraphEdge(CommitId Target, GraphEdgeType Type);
