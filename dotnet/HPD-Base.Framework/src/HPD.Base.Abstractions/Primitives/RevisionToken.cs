namespace HPD.Base;

/// <summary>
/// Carries an opaque optimistic concurrency token.
/// </summary>
public readonly record struct RevisionToken(string Value);
