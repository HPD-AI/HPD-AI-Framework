using System.Collections.Immutable;

namespace HPD.Agent.Permissions;

/// <summary>Declares the stable portable identity of a permission policy implementation.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PermissionPolicyDefinitionAttribute(string id) : Attribute
{
    /// <summary>Gets the stable policy descriptor ID.</summary>
    public string Id { get; } = ValidateId(id);

    private static string ValidateId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Any(char.IsControl))
            throw new ArgumentException("Permission definition IDs must be canonical and control-free.", nameof(id));
        return value;
    }
}

/// <summary>Identifies a generated package of permission descriptors in an assembly.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class PermissionDescriptorPackageAttribute(Type packageType) : Attribute
{
    /// <summary>Gets the generated package implementation type.</summary>
    public Type PackageType { get; } = packageType ??
        throw new ArgumentNullException(nameof(packageType));
}

/// <summary>Supplies one immutable generated permission descriptor package.</summary>
public interface IPermissionDescriptorPackage
{
    /// <summary>Gets descriptors indexed by stable policy or interaction ID.</summary>
    IReadOnlyDictionary<string, AIFunctionPermissionDescriptor> Descriptors { get; }
}

/// <summary>Provides immutable permission descriptor lookup without runtime type discovery.</summary>
public interface IPermissionDescriptorCatalog
{
    /// <summary>Attempts to resolve one stable descriptor ID.</summary>
    bool TryGet(string descriptorId, out AIFunctionPermissionDescriptor descriptor);
}

/// <summary>Contains the immutable merged permission descriptors for one agent composition.</summary>
public sealed class PermissionDescriptorCatalog : IPermissionDescriptorCatalog
{
    private readonly ImmutableDictionary<string, AIFunctionPermissionDescriptor> _descriptors;

    /// <summary>Creates an immutable descriptor catalog.</summary>
    public PermissionDescriptorCatalog(IEnumerable<AIFunctionPermissionDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        var builder = ImmutableDictionary.CreateBuilder<string, AIFunctionPermissionDescriptor>(
            StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            if (!builder.TryAdd(descriptor.DescriptorId, descriptor))
                throw new InvalidOperationException(
                    $"Permission descriptor ID '{descriptor.DescriptorId}' is registered more than once.");
        }
        _descriptors = builder.ToImmutable();
    }

    /// <inheritdoc />
    public bool TryGet(string descriptorId, out AIFunctionPermissionDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptorId);
        return _descriptors.TryGetValue(descriptorId, out descriptor!);
    }
}

/// <summary>Registers generated permission descriptor packages during application composition.</summary>
public static class PermissionDescriptorComposition
{
    private static readonly object s_gate = new();
    private static readonly Dictionary<Type, IPermissionDescriptorPackage> s_packages = [];

    /// <summary>Registers one generated package. Registering the same package type is idempotent.</summary>
    public static void Register(IPermissionDescriptorPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        lock (s_gate)
        {
            var packageType = package.GetType();
            if (!s_packages.TryAdd(packageType, package))
                return;
        }
    }

    internal static IPermissionDescriptorCatalog CreateCatalog()
    {
        lock (s_gate)
        {
            var descriptors = new List<AIFunctionPermissionDescriptor>();
            var owners = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var package in s_packages.Values)
            {
                foreach (var descriptor in package.Descriptors.Values)
                {
                    if (owners.TryGetValue(descriptor.DescriptorId, out var owner) &&
                        owner != package.GetType())
                        throw new InvalidOperationException(
                            $"Permission descriptor ID '{descriptor.DescriptorId}' is provided by both " +
                            $"'{owner.FullName}' and '{package.GetType().FullName}'.");
                    owners[descriptor.DescriptorId] = package.GetType();
                    descriptors.Add(descriptor);
                }
            }
            return new PermissionDescriptorCatalog(descriptors);
        }
    }

    internal static void ResetForTesting()
    {
        lock (s_gate) s_packages.Clear();
    }
}
