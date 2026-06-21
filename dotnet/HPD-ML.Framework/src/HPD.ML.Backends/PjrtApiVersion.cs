namespace HPD.ML.Backends.Pjrt;

/// <summary>
/// PJRT C API version reported by a loaded plugin.
/// </summary>
public readonly record struct PjrtApiVersion(int Major, int Minor)
{
    public override string ToString() => $"{Major}.{Minor}";
}

