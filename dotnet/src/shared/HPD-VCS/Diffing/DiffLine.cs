namespace HPD.VCS.Diffing;

public enum DiffLineType { Unchanged, Added, Removed }

public readonly record struct DiffLine(DiffLineType Type, string Content);
