using System.Reflection;
using System.Runtime.CompilerServices;
using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Extensions.Dynamic;

/// <summary>Loads one explicitly named, digest-bound extension artifact on JIT-capable runtimes only.</summary>
public static class DynamicExtensionArtifactLoader
{
    /// <summary>Verifies the exact artifact bytes and constructs the named extension without discovery or fallback.</summary>
    public static IDynamicPaymentExtension Load(string assemblyPath, string typeName, DynamicExtensionManifest manifest,
        CanonicalDigestProfileId digestProfile)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            throw new PlatformNotSupportedException("Dynamic extensions require a JIT-capable runtime.");
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath); ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(digestProfile);
        if (!manifest.SignatureVerified) throw new InvalidOperationException("Unsigned dynamic artifacts cannot load.");
        string fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Dynamic extension artifact is unavailable.", fullPath);
        CanonicalDigest actual = CanonicalDigest.Sha256(digestProfile, File.ReadAllBytes(fullPath));
        if (!actual.Equals(manifest.ArtifactDigest)) throw new InvalidDataException("Dynamic extension artifact digest does not match its manifest.");
        Assembly assembly = Assembly.LoadFrom(fullPath);
        Type type = assembly.GetType(typeName, throwOnError: true, ignoreCase: false)
            ?? throw new TypeLoadException("Dynamic extension type is unavailable.");
        if (!typeof(IDynamicPaymentExtension).IsAssignableFrom(type) || type.IsAbstract)
            throw new InvalidDataException("Dynamic extension type does not implement the required contract.");
        object? instance = Activator.CreateInstance(type, manifest);
        return instance as IDynamicPaymentExtension
            ?? throw new InvalidDataException("Dynamic extension construction returned an invalid instance.");
    }
}
