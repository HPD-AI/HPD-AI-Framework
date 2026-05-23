namespace Rhodium.Unsafe;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class HighPerformanceKernelAttribute : Attribute
{
    public string Reviewer { get; set; } = "";
    public string LastAuditDate { get; set; } = "";
}
