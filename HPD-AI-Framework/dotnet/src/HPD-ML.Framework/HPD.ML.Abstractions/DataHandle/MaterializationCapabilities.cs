namespace HPD.ML.Abstractions;

[Flags]
public enum MaterializationCapabilities
{
    CursorOnly = 0,
    ColumnarAccess = 1,
    BatchAccess = 2,
    DeviceResident = 4,
    KnownDensity = 8
}
